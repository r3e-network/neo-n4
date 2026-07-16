using System.Net.Http;
using Neo.Json;
using Neo.L2.Persistence;
using Neo.L2.Settlement.Rpc;

namespace Neo.L2.Messaging.UnitTests;

[TestClass]
public class UT_RpcMessageRouterEventScanner
{
    private const uint ChainId = 4242;
    private const string Endpoint = "http://l1.example:30332";
    private static readonly UInt160 ContractHash = UInt160.Parse("0x" + new string('a', 40));
    private static readonly UInt160 Sender = UInt160.Parse("0x" + new string('3', 40));
    private static readonly UInt160 Receiver = UInt160.Parse("0x" + new string('4', 40));
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

        using (var scanner = new RpcMessageRouterEventScanner(
            rpc, ContractHash, ChainId, store, startHeight: 10, finalityDepth: 1))
        {
            var observed = new List<ulong>();
            Assert.AreEqual(1, await scanner.ScanAsync(observed.Add));
            CollectionAssert.AreEqual(new ulong[] { 7 }, observed);
            CollectionAssert.AreEqual(new ulong[] { 7 }, scanner.LoadTrackedNonces().ToArray());
        }

        using var restarted = new RpcMessageRouterEventScanner(
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
        using var scanner = new RpcMessageRouterEventScanner(
            rpc, ContractHash, ChainId, store, startHeight: 10, finalityDepth: 1);

        var observed = new List<ulong>();
        Assert.AreEqual(1, await scanner.ScanAsync(observed.Add));
        Assert.AreEqual(0, observed.Count);
        Assert.AreEqual(0, scanner.LoadTrackedNonces().Count);
    }

    [TestMethod]
    public async Task Router_DequeueDiscoversEventViaScanner()
    {
        using var store = new InMemoryKeyValueStore();
        var (rpc, stub) = BuildRpc();
        using var _ = rpc;
        const ulong nonce = 7;
        RegisterChain(stub, eventChainId: ChainId, nonce: nonce);
        var encoded = EncodeMessage(nonce);
        stub.Register((method, _, _) => method switch
        {
            "getL1ToL2" => StubRpcHandler.ByteArrayBase64(encoded),
            "isConsumed" => StubRpcHandler.Boolean(false),
            _ => null,
        });

        var scanner = new RpcMessageRouterEventScanner(
            rpc, ContractHash, ChainId, store, startHeight: 10, finalityDepth: 1);
        using var router = new RpcMessageRouter(
            rpc, ContractHash, ChainId, Array.Empty<ulong>(),
            cacheTtl: TimeSpan.Zero, eventScanner: scanner);

        var inbound = await router.DequeueL1MessagesAsync(ChainId, maxMessages: 10);
        Assert.AreEqual(1, inbound.Count);
        Assert.AreEqual(nonce, inbound[0].Nonce);
        Assert.AreEqual(MessageType.Call, inbound[0].MessageType);
        Assert.AreNotEqual(UInt256.Zero, inbound[0].MessageHash);
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
        UInt160? eventContract = null)
    {
        stub.RegisterRpc((method, parameters) => method switch
        {
            "getblockcount" => new JNumber(12),
            "getblockhash" => new JString(Block10Hash.ToString()),
            "getblock" => Block(),
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
                        ["eventname"] = "L1ToL2Enqueued",
                        ["state"] = new JObject
                        {
                            ["type"] = "Array",
                            ["value"] = new JArray
                            {
                                Integer(chainId),
                                Integer(nonce),
                                ByteString(Sender),
                                ByteString(Receiver),
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

    private static JObject ByteString(UInt160 hash) => new()
    {
        ["type"] = "ByteString",
        ["value"] = Convert.ToBase64String(hash.GetSpan().ToArray()),
    };

    private static byte[] EncodeMessage(ulong nonce)
    {
        var payload = new byte[] { 0xAB, 0xCD };
        var encoded = new byte[4 + 4 + 8 + 20 + 20 + 1 + 4 + payload.Length];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(encoded.AsSpan(0, 4), 0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(encoded.AsSpan(4, 4), ChainId);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(encoded.AsSpan(8, 8), nonce);
        Sender.GetSpan().CopyTo(encoded.AsSpan(16, 20));
        Receiver.GetSpan().CopyTo(encoded.AsSpan(36, 20));
        encoded[56] = (byte)MessageType.Call;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(encoded.AsSpan(57, 4), payload.Length);
        payload.CopyTo(encoded.AsSpan(61));
        return encoded;
    }
}
