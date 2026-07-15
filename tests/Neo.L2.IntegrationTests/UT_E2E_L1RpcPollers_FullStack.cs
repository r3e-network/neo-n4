using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.Json;
using Neo.L2;
using Neo.L2.Bridge.Cli.Commands;
using Neo.L2.ForcedInclusion;
using Neo.L2.Messaging;
using Neo.L2.Proving.Attestation;
using Neo.L2.Sequencer;
using Neo.L2.Settlement.Rpc;
using Neo.L2.State;

namespace Neo.L2.IntegrationTests;

/// <summary>
/// Cross-component end-to-end test that wires every L1-RPC-backed production
/// adapter (<see cref="RpcSequencerCommitteeProvider"/>,
/// <see cref="RpcForcedInclusionSource"/>, <see cref="RpcMessageRouter"/>) plus
/// the bridge-CLI invocation builder against a single in-process L1 RPC stub.
/// Drives a deposit + cross-chain message through the full pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Each unit test passes against canned responses; this integration test goes
/// further by sharing one L1-RPC stub across all four production adapters,
/// pinning that they:
/// </para>
/// <list type="bullet">
///   <item>compose against a single endpoint without conflicting state</item>
///   <item>each route their <c>invokefunction</c> calls to the right contract hash</item>
///   <item>each parse their canonical contract responses correctly when those responses are produced by encoders that mirror the real on-chain methods</item>
/// </list>
/// <para>
/// The L1 stub keeps real per-contract state (sequencer registrations, forced-inclusion
/// queue, L1→L2 message queue), so the test exercises the round-trip
/// "operator enqueues on L1 → poller fetches via RPC → L2 consumes" flow.
/// </para>
/// </remarks>
[TestClass]
public class UT_E2E_L1RpcPollers_FullStack
{
    private const uint TestChainId = 7777;
    private const string Endpoint = "http://l1.example:30332";
    private static readonly UInt160 SequencerRegistryHash = UInt160.Parse("0x" + new string('a', 40));
    private static readonly UInt160 ForcedInclusionHash = UInt160.Parse("0x" + new string('b', 40));
    private static readonly UInt160 MessageRouterHash = UInt160.Parse("0x" + new string('c', 40));
    private static readonly UInt160 SharedBridgeHash = UInt160.Parse("0x" + new string('d', 40));

    [TestMethod]
    public async Task FullStack_PollersComposeAgainstSharedL1Stub()
    {
        var stub = new SharedL1Stub();
        using var http = new HttpClient(stub);
        using var rpc = new JsonRpcClient(new Uri(Endpoint), http);

        // ── Stage 1: operator-side L1 actions (would normally be wallet-signed
        //    transactions; here we mutate the stub state directly) ──────────

        // Register 3 sequencers on L1.
        var keys = Enumerable.Range(1, 3).Select(i => GenKey((byte)i)).ToList();
        var addr = UInt160.Parse("0x" + new string('1', 40));
        foreach (var (pub, _) in keys) stub.RegisterSequencer(TestChainId, pub, addr);

        // Enqueue 2 forced-inclusion entries on L1.
        var sender = UInt160.Parse("0x" + new string('2', 40));
        var tx1 = new byte[] { 0xAA };
        var tx2 = new byte[] { 0xBB };
        var tx1Hash = new UInt256(Crypto.Hash256(tx1));
        var tx2Hash = new UInt256(Crypto.Hash256(tx2));
        var nonce1 = stub.EnqueueForcedTx(TestChainId, sender, tx1Hash, tx1, deadline: 100);
        var nonce2 = stub.EnqueueForcedTx(TestChainId, sender, tx2Hash, tx2, deadline: 200);

        // Enqueue 2 L1→L2 messages on L1.
        var l1Sender = UInt160.Parse("0x" + new string('3', 40));
        var l2Receiver = UInt160.Parse("0x" + new string('4', 40));
        var msgNonce1 = stub.EnqueueL1ToL2(TestChainId, l1Sender, l2Receiver, MessageType.Deposit, new byte[] { 0x01 });
        var msgNonce2 = stub.EnqueueL1ToL2(TestChainId, l1Sender, l2Receiver, MessageType.Call, new byte[] { 0x02, 0x03 });

        // ── Stage 2: spin up the L2 node's production adapters ───────────────

        using var seqProvider = new RpcSequencerCommitteeProvider(
            rpc, SequencerRegistryHash, TestChainId,
            keys.Select(k => k.pub),
            cacheTtl: TimeSpan.Zero);
        using var forcedSource = new RpcForcedInclusionSource(
            rpc, ForcedInclusionHash, TestChainId,
            new[] { nonce1, nonce2 },
            cacheTtl: TimeSpan.Zero);
        using var msgRouter = new RpcMessageRouter(
            rpc, MessageRouterHash, TestChainId,
            new[] { msgNonce1, msgNonce2 },
            cacheTtl: TimeSpan.Zero);

        // ── Stage 3: assert the L2 sees what L1 has ──────────────────────────

        var committee = await seqProvider.GetActiveCommitteeAsync();
        Assert.AreEqual(3, committee.Count, "all 3 L1-registered sequencers visible to L2");
        Assert.IsTrue(committee.All(m => m.Status == 1));
        Assert.IsTrue(committee.All(m => m.L1Address == addr));

        var pendingForced = await forcedSource.DrainAsync(max: 10);
        Assert.AreEqual(2, pendingForced.Count);
        // deadline-ordered: 100 first, 200 second
        Assert.AreEqual(nonce1, pendingForced[0].Nonce);
        Assert.AreEqual(100u, pendingForced[0].DeadlineUnixSeconds);
        Assert.AreEqual(tx1Hash, pendingForced[0].TxHash);

        var inbound = await msgRouter.DequeueL1MessagesAsync(TestChainId, maxMessages: 10);
        Assert.AreEqual(2, inbound.Count);
        Assert.AreEqual(MessageType.Deposit, inbound[0].MessageType);
        Assert.AreEqual(MessageType.Call, inbound[1].MessageType);
        // Recomputed canonical hash (RpcMessageRouter never trusts an off-wire hash).
        Assert.AreNotEqual(UInt256.Zero, inbound[0].MessageHash);

        // ── Stage 4: the L2 batcher emits an outbound L2→L1 withdrawal message ──

        var l2EmittingContract = UInt160.Parse("0x" + new string('5', 40));
        var l2Asset = UInt160.Parse("0x" + new string('8', 40));
        const ulong withdrawalNonce = 1;
        const long withdrawalAmount = 1000;
        var withdrawalLeaf = MessageHasher.HashWithdrawal(new WithdrawalRequest
        {
            // Must match the chainId passed to BuildFinalizeWithdrawalWithProof — leaf
            // hash includes chainId as a 4B LE domain-separator.
            ChainId = TestChainId,
            EmittingContract = l2EmittingContract,
            L2Sender = l2Receiver,
            L1Recipient = l1Sender,
            L2Asset = l2Asset,
            Amount = withdrawalAmount,
            Nonce = withdrawalNonce,
        });
        var withdrawal = new CrossChainMessage
        {
            SourceChainId = TestChainId,
            TargetChainId = 0,  // L1
            Nonce = 1,
            Sender = l2Receiver,
            Receiver = l1Sender,
            MessageType = MessageType.Withdraw,
            Payload = new byte[] { 0xCC, 0xDD },
            MessageHash = withdrawalLeaf,
        };
        await msgRouter.EnqueueOutboundAsync(new[] { withdrawal });
        Assert.AreEqual(1, msgRouter.Outbox.L2ToL1Count + msgRouter.Outbox.L2ToL2Count);

        // ── Stage 5: bridge-CLI emits the canonical FinalizeWithdrawalWithProof script ──
        //   (this is the wallet-paste-able invocation hex an operator submits to L1)

        var siblings = new[] {
            UInt256.Parse("0x" + new string('6', 64)),
            UInt256.Parse("0x" + new string('7', 64)),
        };
        var script = InvocationBuilder.BuildFinalizeWithdrawalWithProof(
            SharedBridgeHash, TestChainId, batchNumber: 1,
            withdrawalLeaf, siblings, leafIndex: 0,
            emittingContract: l2EmittingContract,
            l2Sender: l2Receiver,
            l2Asset: l2Asset,
            withdrawalNonce: withdrawalNonce,
            asset: UInt160.Parse("0x" + new string('8', 40)),
            recipient: l1Sender,
            amount: withdrawalAmount);
        Assert.IsTrue(script.Length > 0);

        // ── Stage 6: closing flow — L1 confirms one forced-inclusion entry consumed,
        //   re-pull from the L2 must drop it ─────────────────────────────────

        stub.ConfirmForcedConsumed(TestChainId, nonce1);
        forcedSource.InvalidateCache();
        var afterConsume = await forcedSource.DrainAsync(max: 10);
        Assert.AreEqual(1, afterConsume.Count, "L1-consumed entry must drop on next poll");
        Assert.AreEqual(nonce2, afterConsume[0].Nonce);
    }

    [TestMethod]
    public async Task FullStack_SequencerExitDetected_ByPollerOnly()
    {
        // L1 unregisters a sequencer (status → 0). The provider's known-keys set is
        // unchanged, but the next GetActiveCommittee snapshot must drop the unregistered
        // member silently — operator's known-keys set is allowed to drift; L1 is the
        // source of truth.
        var stub = new SharedL1Stub();
        using var http = new HttpClient(stub);
        using var rpc = new JsonRpcClient(new Uri(Endpoint), http);
        var addr = UInt160.Parse("0x" + new string('1', 40));
        var k1 = GenKey(1).pub;
        var k2 = GenKey(2).pub;
        stub.RegisterSequencer(TestChainId, k1, addr);
        stub.RegisterSequencer(TestChainId, k2, addr);

        using var provider = new RpcSequencerCommitteeProvider(
            rpc, SequencerRegistryHash, TestChainId, new[] { k1, k2 },
            cacheTtl: TimeSpan.Zero);

        var before = await provider.GetActiveCommitteeAsync();
        Assert.AreEqual(2, before.Count);

        stub.UnregisterSequencer(TestChainId, k2);
        var after = await provider.GetActiveCommitteeAsync();
        Assert.AreEqual(1, after.Count);
        Assert.AreEqual(k1, after[0].PublicKey);
    }

    private static (ECPoint pub, byte[] priv) GenKey(byte seed)
    {
        var priv = new byte[32];
        for (var i = 0; i < 32; i++) priv[i] = (byte)(seed + i);
        return (ECCurve.Secp256r1.G * priv, priv);
    }

    /// <summary>
    /// In-process L1 RPC stub that holds real per-contract state (sequencer registry,
    /// forced-inclusion queue, message-router queue) and routes <c>invokefunction</c>
    /// calls by contract hash + method name. Mirrors the canonical encoders the on-chain
    /// contracts use, so the test exercises the production adapters' decoders against
    /// realistic responses (not hand-written canned blobs).
    /// </summary>
    private sealed class SharedL1Stub : HttpMessageHandler
    {
        // SequencerRegistry state.
        private readonly ConcurrentDictionary<(uint chain, ECPoint key), (byte status, UInt160 addr)> _sequencers = new();
        // ForcedInclusion state.
        private readonly ConcurrentDictionary<(uint chain, ulong nonce), (byte[] entry, bool consumed)> _forced = new();
        private long _nextForcedNonce = 0;
        // MessageRouter state.
        private readonly ConcurrentDictionary<(uint chain, ulong nonce), byte[]> _l1ToL2 = new();
        private readonly ConcurrentDictionary<UInt256, bool> _msgConsumed = new();
        private long _nextMsgNonce = 0;

        public void RegisterSequencer(uint chainId, ECPoint key, UInt160 addr)
            => _sequencers[(chainId, key)] = (1, addr);

        public void UnregisterSequencer(uint chainId, ECPoint key)
            => _sequencers.TryRemove((chainId, key), out _);

        public ulong EnqueueForcedTx(uint chainId, UInt160 sender, UInt256 txHash, byte[] tx, uint deadline)
        {
            var nonce = (ulong)Interlocked.Increment(ref _nextForcedNonce);
            var size = 20 + 32 + 4 + tx.Length + 4;
            var buf = new byte[size];
            var span = buf.AsSpan();
            sender.GetSpan().CopyTo(span.Slice(0, 20));
            txHash.GetSpan().CopyTo(span.Slice(20, 32));
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(52, 4), tx.Length);
            tx.CopyTo(span.Slice(56, tx.Length));
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(56 + tx.Length, 4), deadline);
            _forced[(chainId, nonce)] = (buf, false);
            return nonce;
        }

        public void ConfirmForcedConsumed(uint chainId, ulong nonce)
        {
            if (_forced.TryGetValue((chainId, nonce), out var v))
                _forced[(chainId, nonce)] = (v.entry, true);
        }

        public ulong EnqueueL1ToL2(uint targetChain, UInt160 sender, UInt160 receiver, MessageType type, byte[] payload)
        {
            var nonce = (ulong)Interlocked.Increment(ref _nextMsgNonce);
            var size = 4 + 4 + 8 + 20 + 20 + 1 + 4 + payload.Length;
            var buf = new byte[size];
            var span = buf.AsSpan();
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(0, 4), 0u);            // sourceChainId = L1
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(4, 4), targetChain);
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(8, 8), nonce);
            sender.GetSpan().CopyTo(span.Slice(16, 20));
            receiver.GetSpan().CopyTo(span.Slice(36, 20));
            span[56] = (byte)type;
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(57, 4), payload.Length);
            payload.CopyTo(span.Slice(61, payload.Length));
            _l1ToL2[(targetChain, nonce)] = buf;
            return nonce;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            var parsed = (JObject)JToken.Parse(body)!;
            var id = (long)((JNumber)parsed["id"]!).AsNumber();
            var rpcParams = (JArray)parsed["params"]!;
            var contractHash = rpcParams[0]!.AsString();
            var contractMethod = rpcParams[1]!.AsString();
            var contractArgs = (JArray)rpcParams[2]!;

            JToken? stackTop;
            if (contractHash == SequencerRegistryHash.ToString())
                stackTop = HandleSequencer(contractMethod, contractArgs);
            else if (contractHash == ForcedInclusionHash.ToString())
                stackTop = HandleForcedInclusion(contractMethod, contractArgs);
            else if (contractHash == MessageRouterHash.ToString())
                stackTop = HandleMessageRouter(contractMethod, contractArgs);
            else
                throw new InvalidOperationException($"unknown contract hash '{contractHash}'");

            var resp = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["result"] = new JObject
                {
                    ["state"] = "HALT",
                    ["stack"] = new JArray(stackTop),
                },
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(resp.ToString(), System.Text.Encoding.UTF8, "application/json"),
            };
        }

        private JToken HandleSequencer(string method, JArray args)
        {
            switch (method)
            {
                case "getStatus":
                    {
                        var chain = uint.Parse(args[0]!["value"]!.AsString());
                        var pubBytes = Convert.FromBase64String(args[1]!["value"]!.AsString());
                        var key = ECPoint.DecodePoint(pubBytes, ECCurve.Secp256r1);
                        return _sequencers.TryGetValue((chain, key), out var v)
                            ? Integer(v.status)
                            : Integer(0);
                    }
                case "getSequencerAddress":
                    {
                        var chain = uint.Parse(args[0]!["value"]!.AsString());
                        var pubBytes = Convert.FromBase64String(args[1]!["value"]!.AsString());
                        var key = ECPoint.DecodePoint(pubBytes, ECCurve.Secp256r1);
                        if (_sequencers.TryGetValue((chain, key), out var v))
                            return Bytes(v.addr.GetSpan().ToArray());
                        return Bytes(Array.Empty<byte>());
                    }
                case "isRegistered":
                    {
                        var chain = uint.Parse(args[0]!["value"]!.AsString());
                        var pubBytes = Convert.FromBase64String(args[1]!["value"]!.AsString());
                        var key = ECPoint.DecodePoint(pubBytes, ECCurve.Secp256r1);
                        return Boolean(_sequencers.ContainsKey((chain, key)));
                    }
                case "getMaxCommitteeSize":
                    return Integer(21);
                default:
                    throw new InvalidOperationException($"sequencer registry: unknown method {method}");
            }
        }

        private JToken HandleForcedInclusion(string method, JArray args)
        {
            switch (method)
            {
                case "getEntry":
                    {
                        var chain = uint.Parse(args[0]!["value"]!.AsString());
                        var nonce = ulong.Parse(args[1]!["value"]!.AsString());
                        return _forced.TryGetValue((chain, nonce), out var v)
                            ? Bytes(v.entry)
                            : Bytes(Array.Empty<byte>());
                    }
                case "isConsumed":
                    {
                        var chain = uint.Parse(args[0]!["value"]!.AsString());
                        var nonce = ulong.Parse(args[1]!["value"]!.AsString());
                        return _forced.TryGetValue((chain, nonce), out var v)
                            ? Boolean(v.consumed)
                            : Boolean(false);
                    }
                default:
                    throw new InvalidOperationException($"forced inclusion: unknown method {method}");
            }
        }

        private JToken HandleMessageRouter(string method, JArray args)
        {
            switch (method)
            {
                case "getL1ToL2":
                    {
                        var chain = uint.Parse(args[0]!["value"]!.AsString());
                        var nonce = ulong.Parse(args[1]!["value"]!.AsString());
                        return _l1ToL2.TryGetValue((chain, nonce), out var bytes)
                            ? Bytes(bytes)
                            : Bytes(Array.Empty<byte>());
                    }
                case "isConsumed":
                    {
                        var hashStr = args[0]!["value"]!.AsString();
                        var hash = UInt256.Parse(hashStr);
                        return Boolean(_msgConsumed.TryGetValue(hash, out var b) && b);
                    }
                default:
                    throw new InvalidOperationException($"message router: unknown method {method}");
            }
        }

        private static JObject Integer(int v) => new() { ["type"] = "Integer", ["value"] = v.ToString() };
        private static JObject Boolean(bool v) => new() { ["type"] = "Boolean", ["value"] = v ? "true" : "false" };
        private static JObject Bytes(byte[] b) => new() { ["type"] = "ByteString", ["value"] = Convert.ToBase64String(b) };
    }
}
