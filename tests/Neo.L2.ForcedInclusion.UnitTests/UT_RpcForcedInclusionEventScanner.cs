using System.Net.Http;
using Neo.Json;
using Neo.L2.Persistence;
using Neo.L2.Settlement.Rpc;

namespace Neo.L2.ForcedInclusion.UnitTests;

[TestClass]
public class UT_RpcForcedInclusionEventScanner
{
    private const uint ChainId = 4242;
    private const string Endpoint = "http://l1.example:30332";
    private static readonly UInt160 ContractHash = UInt160.Parse("0x" + new string('a', 40));
    private static readonly UInt256 Block9Hash = UInt256.Parse("0x" + new string('9', 64));
    private static readonly UInt256 Block10Hash = UInt256.Parse("0x" + new string('1', 64));
    private static readonly UInt256 TransactionHash = UInt256.Parse("0x" + new string('2', 64));

    [TestMethod]
    public async Task Scan_DiscoversFinalizedEvent_AndRestoresDurableNonce()
    {
        using var store = new InMemoryKeyValueStore();
        var (rpc, stub) = BuildRpc();
        using var _ = rpc;
        RegisterChain(stub, eventChainId: ChainId, nonce: 7);

        using (var scanner = new RpcForcedInclusionEventScanner(
            rpc, ContractHash, ChainId, store, startHeight: 10, finalityDepth: 1))
        {
            var observed = new List<ulong>();
            Assert.AreEqual(1, await scanner.ScanAsync(observed.Add));
            CollectionAssert.AreEqual(new ulong[] { 7 }, observed);
            CollectionAssert.AreEqual(new ulong[] { 7 }, scanner.LoadTrackedNonces().ToArray());
        }

        using var restarted = new RpcForcedInclusionEventScanner(
            rpc, ContractHash, ChainId, store, startHeight: 10, finalityDepth: 1);
        CollectionAssert.AreEqual(new ulong[] { 7 }, restarted.LoadTrackedNonces().ToArray());
        Assert.AreEqual(0, await restarted.ScanAsync(_ => Assert.Fail("cursor replayed a committed event")));
        Assert.IsTrue(stub.RpcCaptured.Any(call => call.Method == "getblockhash"));
    }

    [TestMethod]
    public async Task Scan_IgnoresDifferentChain()
    {
        using var store = new InMemoryKeyValueStore();
        var (rpc, stub) = BuildRpc();
        using var _ = rpc;
        RegisterChain(stub, eventChainId: ChainId + 1, nonce: 9);
        using var scanner = new RpcForcedInclusionEventScanner(
            rpc, ContractHash, ChainId, store, startHeight: 10, finalityDepth: 1);

        var observed = new List<ulong>();
        Assert.AreEqual(1, await scanner.ScanAsync(observed.Add));
        Assert.AreEqual(0, observed.Count);
        Assert.AreEqual(0, scanner.LoadTrackedNonces().Count);
    }

    [TestMethod]
    public async Task Scan_IgnoresSameNamedEventFromDifferentContract()
    {
        using var store = new InMemoryKeyValueStore();
        var (rpc, stub) = BuildRpc();
        using var _ = rpc;
        RegisterChain(
            stub,
            eventChainId: ChainId,
            nonce: 9,
            eventContract: UInt160.Parse("0x" + new string('b', 40)));
        using var scanner = new RpcForcedInclusionEventScanner(
            rpc, ContractHash, ChainId, store, startHeight: 10, finalityDepth: 1);

        var observed = new List<ulong>();
        Assert.AreEqual(1, await scanner.ScanAsync(observed.Add));
        Assert.AreEqual(0, observed.Count);
        Assert.AreEqual(0, scanner.LoadTrackedNonces().Count);
    }

    [TestMethod]
    public async Task Scan_ApplicationLogFailure_DoesNotAdvanceCursor()
    {
        using var store = new InMemoryKeyValueStore();
        var (rpc, stub) = BuildRpc();
        using var _ = rpc;
        var failLog = true;
        RegisterChain(stub, eventChainId: ChainId, nonce: 7, failApplicationLog: () => failLog);
        using var scanner = new RpcForcedInclusionEventScanner(
            rpc, ContractHash, ChainId, store, startHeight: 10, finalityDepth: 1);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await scanner.ScanAsync(_ => { }));
        failLog = false;
        Assert.AreEqual(1, await scanner.ScanAsync(_ => { }));
        Assert.AreEqual(
            2,
            stub.RpcCaptured.Count(call => call.Method == "getblock"),
            "the failed block must be replayed because its cursor was not committed");
    }

    [TestMethod]
    public async Task Scan_ChangedCommittedHash_FailsClosed()
    {
        using var store = new InMemoryKeyValueStore();
        var (rpc, stub) = BuildRpc();
        using var _ = rpc;
        var changed = false;
        RegisterChain(stub, eventChainId: ChainId, nonce: 7, changedResumeHash: () => changed);
        using var scanner = new RpcForcedInclusionEventScanner(
            rpc, ContractHash, ChainId, store, startHeight: 10, finalityDepth: 1);

        Assert.AreEqual(1, await scanner.ScanAsync(_ => { }));
        changed = true;
        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            async () => await scanner.ScanAsync(_ => { }));
    }

    [TestMethod]
    public async Task Source_DrainDiscoversEvent_AndConsumedConfirmationForgetsIt()
    {
        using var store = new InMemoryKeyValueStore();
        var (rpc, stub) = BuildRpc();
        using var _ = rpc;
        RegisterChain(stub, eventChainId: ChainId, nonce: 7);
        var transaction = new byte[] { 0xCA, 0xFE };
        var entry = EncodeEntry(transaction, deadline: 100);
        var consumed = false;
        stub.Register((method, _, _) => method switch
        {
            "getEntry" => StubRpcHandler.ByteArrayBase64(entry),
            "isConsumed" => StubRpcHandler.Boolean(consumed),
            _ => null,
        });
        var scanner = new RpcForcedInclusionEventScanner(
            rpc, ContractHash, ChainId, store, startHeight: 10, finalityDepth: 1);
        using var source = new RpcForcedInclusionSource(
            rpc, ContractHash, ChainId, Array.Empty<ulong>(),
            cacheTtl: TimeSpan.Zero, eventScanner: scanner);

        var pending = await source.DrainAsync(10);
        Assert.AreEqual(1, pending.Count);
        Assert.AreEqual(7UL, pending[0].Nonce);
        consumed = true;
        await source.ConfirmConsumedAsync(7);

        using var restored = new RpcForcedInclusionEventScanner(
            rpc, ContractHash, ChainId, store, startHeight: 10, finalityDepth: 1);
        Assert.AreEqual(0, restored.LoadTrackedNonces().Count);
    }

    private static (JsonRpcClient Rpc, StubRpcHandler Stub) BuildRpc()
    {
        var stub = new StubRpcHandler();
        var http = new HttpClient(stub);
        return (new JsonRpcClient(new Uri(Endpoint), http), stub);
    }

    private static void RegisterChain(
        StubRpcHandler stub,
        uint eventChainId,
        ulong nonce,
        Func<bool>? failApplicationLog = null,
        Func<bool>? changedResumeHash = null,
        UInt160? eventContract = null)
    {
        stub.RegisterRpc((method, parameters) => method switch
        {
            "getblockcount" => new JNumber(12),
            "getblockhash" => new JString(
                changedResumeHash?.Invoke() == true
                    ? UInt256.Parse("0x" + new string('f', 64)).ToString()
                    : Block10Hash.ToString()),
            "getblock" => Block(),
            "getapplicationlog" when failApplicationLog?.Invoke() == true
                => throw new InvalidOperationException("simulated application-log outage"),
            "getapplicationlog" => ApplicationLog(
                eventChainId, nonce, eventContract ?? ContractHash),
            _ => null,
        });
    }

    private static JObject Block() => new()
    {
        ["index"] = 10,
        ["hash"] = Block10Hash.ToString(),
        ["previousblockhash"] = Block9Hash.ToString(),
        ["tx"] = new JArray
        {
            new JObject { ["hash"] = TransactionHash.ToString() },
        },
    };

    private static JObject ApplicationLog(uint chainId, ulong nonce, UInt160 eventContract) => new()
    {
        ["executions"] = new JArray
        {
            new JObject
            {
                ["vmstate"] = "HALT",
                ["notifications"] = new JArray
                {
                    new JObject
                    {
                        ["contract"] = eventContract.ToString(),
                        ["eventname"] = "ForcedTxEnqueued",
                        ["state"] = new JObject
                        {
                            ["type"] = "Array",
                            ["value"] = new JArray
                            {
                                Integer(chainId),
                                Integer(nonce),
                                new JObject { ["type"] = "ByteString", ["value"] = "" },
                                new JObject { ["type"] = "ByteString", ["value"] = "" },
                            },
                        },
                    },
                },
            },
        },
    };

    private static JObject Integer<T>(T value) where T : IFormattable => new()
    {
        ["type"] = "Integer",
        ["value"] = value.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
    };

    private static byte[] EncodeEntry(byte[] transaction, uint deadline)
    {
        var sender = UInt160.Parse("0x" + new string('1', 40));
        var hash = new UInt256(Neo.Cryptography.Crypto.Hash256(transaction));
        var encoded = new byte[20 + 32 + 4 + transaction.Length + 4];
        sender.GetSpan().CopyTo(encoded.AsSpan(0, 20));
        hash.GetSpan().CopyTo(encoded.AsSpan(20, 32));
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
            encoded.AsSpan(52, 4), transaction.Length);
        transaction.CopyTo(encoded.AsSpan(56));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
            encoded.AsSpan(56 + transaction.Length, 4), deadline);
        return encoded;
    }
}
