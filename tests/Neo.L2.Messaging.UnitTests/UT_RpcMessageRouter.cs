using System;
using System.Buffers.Binary;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Neo;
using Neo.L2;
using Neo.L2.Messaging;
using Neo.L2.Settlement.Rpc;

namespace Neo.L2.Messaging.UnitTests;

[TestClass]
public class UT_RpcMessageRouter
{
    private const uint TestChainId = 4242;
    private static readonly UInt160 RouterHash = UInt160.Parse("0x" + new string('a', 40));
    private const string Endpoint = "http://l1.example:30332";

    private static byte[] EncodeMessage(uint sourceChainId, uint targetChainId, ulong nonce,
        UInt160 sender, UInt160 receiver, byte messageType, byte[] payload)
    {
        var size = 4 + 4 + 8 + 20 + 20 + 1 + 4 + payload.Length;
        var buf = new byte[size];
        var span = buf.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(0, 4), sourceChainId);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(4, 4), targetChainId);
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(8, 8), nonce);
        sender.GetSpan().CopyTo(span.Slice(16, 20));
        receiver.GetSpan().CopyTo(span.Slice(36, 20));
        span[56] = messageType;
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(57, 4), payload.Length);
        payload.CopyTo(span.Slice(61, payload.Length));
        return buf;
    }

    private static (RpcMessageRouter router, StubRpcHandler stub, JsonRpcClient rpc) Build(params ulong[] genesisNonces)
    {
        var stub = new StubRpcHandler();
        var http = new HttpClient(stub);
        var rpc = new JsonRpcClient(new Uri(Endpoint), http);
        var router = new RpcMessageRouter(
            rpc, RouterHash, TestChainId, genesisNonces,
            cacheTtl: TimeSpan.Zero);
        return (router, stub, rpc);
    }

    [TestMethod]
    public async Task DequeueL1Messages_RoundTripsTwoNonces_OrderedByNonce()
    {
        var sender = UInt160.Parse("0x" + new string('1', 40));
        var receiver = UInt160.Parse("0x" + new string('2', 40));
        var (router, stub, rpc) = Build(genesisNonces: new ulong[] { 1, 2 });
        using var _ = rpc;
        Assert.AreEqual(2, router.KnownInboundNonceCount);

        stub.Register((m, h, p) =>
        {
            if (m == "isConsumed") return StubRpcHandler.Boolean(false);
            if (m == "getL1ToL2")
            {
                var nonce = ulong.Parse(p[1]!["value"]!.AsString());
                var encoded = EncodeMessage(0, TestChainId, nonce, sender, receiver,
                    (byte)MessageType.Call, new byte[] { (byte)nonce });
                return StubRpcHandler.ByteArrayBase64(encoded);
            }
            return null;
        });

        var dequeued = await router.DequeueL1MessagesAsync(TestChainId, maxMessages: 10);
        Assert.AreEqual(2, dequeued.Count);
        Assert.AreEqual(1UL, dequeued[0].Nonce);
        Assert.AreEqual(2UL, dequeued[1].Nonce);
        Assert.AreEqual(0u, dequeued[0].SourceChainId);
        Assert.AreEqual(TestChainId, dequeued[0].TargetChainId);
        Assert.IsTrue(router.RegisterInboundNonce(9));
        Assert.AreEqual(3, router.KnownInboundNonceCount);
    }

    [TestMethod]
    public async Task DequeueL1Messages_ReturnsAscendingNonce_GivenOutOfOrderInput()
    {
        // Pins the documented invariant at RpcMessageRouter.cs:167 — the dequeue
        // returns nonces in strict ascending order. If the L2 batcher consumed
        // nonce 7 before 3, the L1 replay-protection state (which assumes
        // per-source-chain monotonic processing) could permanently brick nonce 3.
        // Input genesisNonces deliberately scrambled so the test fails for the
        // right reason if the OrderBy(m => m.Nonce) ever regresses to "natural
        // dictionary order" or "first-emitted".
        var sender = UInt160.Parse("0x" + new string('1', 40));
        var receiver = UInt160.Parse("0x" + new string('2', 40));
        var (router, stub, rpc) = Build(genesisNonces: new ulong[] { 5, 3, 8, 1, 7 });
        using var _ = rpc;

        stub.Register((m, h, p) =>
        {
            if (m == "isConsumed") return StubRpcHandler.Boolean(false);
            if (m == "getL1ToL2")
            {
                var nonce = ulong.Parse(p[1]!["value"]!.AsString());
                return StubRpcHandler.ByteArrayBase64(EncodeMessage(
                    0, TestChainId, nonce, sender, receiver,
                    (byte)MessageType.Call, new byte[] { (byte)nonce }));
            }
            return null;
        });

        var dequeued = await router.DequeueL1MessagesAsync(TestChainId, maxMessages: 10);
        Assert.AreEqual(5, dequeued.Count, "all 5 nonces should come through (none are L1-consumed)");
        CollectionAssert.AreEqual(
            new ulong[] { 1, 3, 5, 7, 8 },
            dequeued.Select(m => m.Nonce).ToArray(),
            "DequeueL1MessagesAsync must return nonces in strict ascending order");
    }

    [TestMethod]
    public async Task DequeueL1Messages_DropsL1ConsumedEntries()
    {
        var sender = UInt160.Parse("0x" + new string('1', 40));
        var receiver = UInt160.Parse("0x" + new string('2', 40));
        var (router, stub, rpc) = Build(genesisNonces: new ulong[] { 7 });
        using var _ = rpc;

        stub.Register((m, h, p) =>
        {
            if (m == "isConsumed") return StubRpcHandler.Boolean(true);
            if (m == "getL1ToL2")
                return StubRpcHandler.ByteArrayBase64(EncodeMessage(0, TestChainId, 7, sender, receiver,
                    (byte)MessageType.Call, new byte[] { 0x01 }));
            return null;
        });

        var dequeued = await router.DequeueL1MessagesAsync(TestChainId, maxMessages: 10);
        Assert.AreEqual(0, dequeued.Count);
    }

    [TestMethod]
    public async Task DequeueL1Messages_LocalConsumeIsHonoredAcrossCalls()
    {
        var sender = UInt160.Parse("0x" + new string('1', 40));
        var receiver = UInt160.Parse("0x" + new string('2', 40));
        var (router, stub, rpc) = Build(genesisNonces: new ulong[] { 1, 2, 3 });
        using var _ = rpc;
        stub.Register((m, h, p) =>
        {
            if (m == "isConsumed") return StubRpcHandler.Boolean(false);
            if (m == "getL1ToL2")
            {
                var nonce = ulong.Parse(p[1]!["value"]!.AsString());
                return StubRpcHandler.ByteArrayBase64(EncodeMessage(0, TestChainId, nonce, sender, receiver,
                    (byte)MessageType.Call, new byte[] { (byte)nonce }));
            }
            return null;
        });

        var first = await router.DequeueL1MessagesAsync(TestChainId, maxMessages: 10);
        Assert.AreEqual(3, first.Count);
        // After Dequeue the inbox marks them locally consumed (matches L1MessageInbox.Dequeue contract).
        var second = await router.DequeueL1MessagesAsync(TestChainId, maxMessages: 10);
        Assert.AreEqual(0, second.Count, "subsequent Dequeue must not return already-dequeued nonces");
    }

    [TestMethod]
    public async Task DequeueL1Messages_RejectsMismatchedChainId()
    {
        var (router, _, rpc) = Build();
        using var _ = rpc;
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => router.DequeueL1MessagesAsync(chainId: 9999, maxMessages: 1).AsTask());
    }

    [TestMethod]
    public async Task RegisterInboundNonce_AddsToFutureDequeue()
    {
        var sender = UInt160.Parse("0x" + new string('1', 40));
        var receiver = UInt160.Parse("0x" + new string('2', 40));
        var stub = new StubRpcHandler();
        var http = new HttpClient(stub);
        var rpc = new JsonRpcClient(new Uri(Endpoint), http);
        var router = new RpcMessageRouter(
            rpc, RouterHash, TestChainId, new ulong[] { 1 },
            cacheTtl: TimeSpan.FromMinutes(10));
        using var _ = rpc;

        stub.Register((m, h, p) =>
        {
            if (m == "isConsumed") return StubRpcHandler.Boolean(false);
            if (m == "getL1ToL2")
            {
                var nonce = ulong.Parse(p[1]!["value"]!.AsString());
                return StubRpcHandler.ByteArrayBase64(EncodeMessage(0, TestChainId, nonce, sender, receiver,
                    (byte)MessageType.Call, new byte[] { (byte)nonce }));
            }
            return null;
        });

        var first = await router.DequeueL1MessagesAsync(TestChainId, maxMessages: 10);
        Assert.AreEqual(1, first.Count);

        Assert.IsTrue(router.RegisterInboundNonce(2));
        Assert.IsFalse(router.RegisterInboundNonce(2));

        var second = await router.DequeueL1MessagesAsync(TestChainId, maxMessages: 10);
        Assert.AreEqual(1, second.Count, "newly-registered nonce 2 should be returned");
        Assert.AreEqual(2UL, second[0].Nonce);
    }

    [TestMethod]
    public async Task EnqueueOutbound_AppendsToOutbox()
    {
        var (router, _, rpc) = Build();
        using var _ = rpc;
        var msg = new CrossChainMessage
        {
            SourceChainId = TestChainId,
            TargetChainId = 1,
            Nonce = 5,
            Sender = UInt160.Zero,
            Receiver = UInt160.Zero,
            MessageType = MessageType.Withdraw,
            Payload = new byte[] { 0xCA },
            MessageHash = UInt256.Zero,
        };
        await router.EnqueueOutboundAsync(new[] { msg });
        // L2->L1 message (target=1, the L1 sentinel) lands in the L2->L1 bucket.
        Assert.AreEqual(1, router.Outbox.L2ToL1Count + router.Outbox.L2ToL2Count);
    }

    [TestMethod]
    public async Task GetMessageProof_ReturnsRecordedBytes()
    {
        var (router, _, rpc) = Build();
        using var _ = rpc;
        var hash = UInt256.Parse("0x" + new string('e', 64));
        var proof = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        router.RecordFinalizedProof(hash, proof);
        var got = await router.GetMessageProofAsync(hash);
        Assert.IsNotNull(got);
        CollectionAssert.AreEqual(proof, got!.Value.ToArray());
    }

    [TestMethod]
    public async Task GetMessageProof_ReturnsNullForUnknownHash()
    {
        var (router, _, rpc) = Build();
        using var _ = rpc;
        var got = await router.GetMessageProofAsync(UInt256.Parse("0x" + new string('f', 64)));
        Assert.IsNull(got);
    }

    [TestMethod]
    public void DecodeMessage_RejectsTruncated()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => RpcMessageRouter.DecodeMessage(new byte[40]));
    }

    [TestMethod]
    public void DecodeMessage_RejectsPayloadLenSizeMismatch()
    {
        var sender = UInt160.Parse("0x" + new string('1', 40));
        var receiver = UInt160.Parse("0x" + new string('2', 40));
        var bytes = EncodeMessage(0, 1, 1, sender, receiver, 0, new byte[] { 0xAA });
        // Tamper payloadLen to claim 99 bytes when only 1 follows.
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(57, 4), 99);
        Assert.ThrowsExactly<InvalidDataException>(() => RpcMessageRouter.DecodeMessage(bytes));
    }

    [TestMethod]
    public void DecodeMessage_RoundTripsCanonicalEncoding_AndRecomputesHash()
    {
        var sender = UInt160.Parse("0x" + new string('1', 40));
        var receiver = UInt160.Parse("0x" + new string('2', 40));
        var bytes = EncodeMessage(7, 99, 42, sender, receiver, (byte)MessageType.Deposit, new byte[] { 0xCA, 0xFE });

        var decoded = RpcMessageRouter.DecodeMessage(bytes);

        Assert.AreEqual(7u, decoded.SourceChainId);
        Assert.AreEqual(99u, decoded.TargetChainId);
        Assert.AreEqual(42UL, decoded.Nonce);
        Assert.AreEqual(sender, decoded.Sender);
        Assert.AreEqual(receiver, decoded.Receiver);
        Assert.AreEqual(MessageType.Deposit, decoded.MessageType);
        CollectionAssert.AreEqual(new byte[] { 0xCA, 0xFE }, decoded.Payload.ToArray());
        // MessageHash must be the recomputed canonical hash, not zero.
        Assert.AreNotEqual(UInt256.Zero, decoded.MessageHash);
    }

    [TestMethod]
    public void OpenFromChainDirectory_CreatesEventAndProofStores()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-n4-router-open-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var stub = new StubRpcHandler();
            var http = new HttpClient(stub);
            var rpc = new JsonRpcClient(new Uri(Endpoint), http);
            using var router = RpcMessageRouter.OpenFromChainDirectory(
                dir, rpc, RouterHash, TestChainId, startHeight: 10, ownsRpc: true);
            Assert.IsTrue(Directory.Exists(Path.Combine(
                dir, NeoHubDeployReport.RelativeMessageRouterEventStoreDir)));
            Assert.IsTrue(Directory.Exists(Path.Combine(
                dir, NeoHubDeployReport.RelativeRpcProofStoreDir)));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void OpenFromChainDirectory_ZeroStartHeight_FailsClosed()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-n4-router-zero-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var stub = new StubRpcHandler();
            using var http = new HttpClient(stub);
            using var rpc = new JsonRpcClient(new Uri(Endpoint), http);
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                RpcMessageRouter.OpenFromChainDirectory(
                    dir, rpc, RouterHash, TestChainId, startHeight: 0));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
