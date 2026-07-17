using System.Net.Http;
using System.Numerics;
using Neo.Json;
using Neo.L2.Persistence;
using Neo.L2.Settlement.Rpc;

namespace Neo.L2.Bridge.UnitTests;

[TestClass]
public class UT_RpcSharedBridgeDepositSource
{
    private const uint ChainId = 1099;
    private const string Endpoint = "http://l1.example:30332";
    private static readonly UInt160 SharedBridge = UInt160.Parse("0x" + new string('a', 40));
    private static readonly UInt160 L2Bridge = UInt160.Parse("0x" + new string('b', 40));
    private static readonly UInt160 Asset = UInt160.Parse("0x" + new string('c', 40));
    private static readonly UInt160 Recipient = UInt160.Parse("0x" + new string('d', 40));
    private static readonly UInt160 Sender = UInt160.Parse("0x" + new string('e', 40));
    private static readonly UInt256 Block9Hash = UInt256.Parse("0x" + new string('9', 64));
    private static readonly UInt256 Block10Hash = UInt256.Parse("0x" + new string('1', 64));
    private static readonly UInt256 TransactionHash = UInt256.Parse("0x" + new string('2', 64));

    [TestMethod]
    public void OpenFromChainDirectory_CreatesDurableStoreUnderLayout()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-n4-deposit-open-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var (rpc, _) = BuildRpc();
            using var source = RpcSharedBridgeDepositSource.OpenFromChainDirectory(
                dir,
                rpc,
                SharedBridge,
                ChainId,
                L2Bridge,
                startHeight: 10,
                ownsRpc: true);
            Assert.AreEqual(ChainId, source.ChainId);
            Assert.IsTrue(Directory.Exists(Path.Combine(
                dir, NeoHubDeployReport.RelativeSharedBridgeDepositEventStoreDir)));
            Assert.AreEqual(0, source.Peek(10).Count);
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
        var dir = Path.Combine(Path.GetTempPath(), "neo-n4-deposit-zero-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var (rpc, _) = BuildRpc();
            using var _ = rpc;
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                RpcSharedBridgeDepositSource.OpenFromChainDirectory(
                    dir, rpc, SharedBridge, ChainId, L2Bridge, startHeight: 0));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public async Task Scan_MaterializesDepositMessage_AndConfirmForgetsNonce()
    {
        using var store = new InMemoryKeyValueStore();
        var (rpc, stub) = BuildRpc();
        using var _ = rpc;
        var record = new SharedBridgeDepositRecord
        {
            Asset = Asset,
            Recipient = Recipient,
            Sender = Sender,
            Nonce = 7,
            Amount = 1_000,
        };
        RegisterDeposit(stub, eventChainId: ChainId, record);

        using var source = new RpcSharedBridgeDepositSource(
            rpc,
            SharedBridge,
            ChainId,
            L2Bridge,
            store,
            startHeight: 10,
            finalityDepth: 1);

        Assert.AreEqual(1, await source.ScanAsync());
        Assert.AreEqual(1, source.Peek(10).Count);
        Assert.AreEqual(1, source.ReadyCount);
        Assert.AreEqual(0, source.ReservedCount);
        var messages = source.Drain(10);
        Assert.AreEqual(1, messages.Count);
        Assert.AreEqual(7UL, messages[0].Nonce);
        Assert.AreEqual(MessageType.Deposit, messages[0].MessageType);
        Assert.AreEqual(L2Bridge, messages[0].Receiver);
        Assert.AreEqual(Sender, messages[0].Sender);
        CollectionAssert.AreEqual(record.ToDepositPayload().Encode(), messages[0].Payload.ToArray());
        Assert.AreEqual(0, source.Peek(10).Count, "drain must reserve");
        Assert.AreEqual(0, source.ReadyCount);
        Assert.AreEqual(1, source.ReservedCount);
        Assert.AreEqual(0, source.Drain(10).Count, "must not re-drain reserved");

        source.ConfirmConsumed(7);
        Assert.AreEqual(0, source.Peek(10).Count);
        Assert.AreEqual(0, source.ReservedCount);
        Assert.AreEqual(1, source.SoftConsumedCount);

        using var restarted = new RpcSharedBridgeDepositScanner(
            rpc, SharedBridge, ChainId, store, startHeight: 10, finalityDepth: 1);
        Assert.AreEqual(0, restarted.LoadTrackedNonces().Count);
    }

    [TestMethod]
    public async Task Scan_IgnoresOtherChainDeposits()
    {
        using var store = new InMemoryKeyValueStore();
        var (rpc, stub) = BuildRpc();
        using var _ = rpc;
        var record = new SharedBridgeDepositRecord
        {
            Asset = Asset,
            Recipient = Recipient,
            Sender = Sender,
            Nonce = 3,
            Amount = 10,
        };
        RegisterDeposit(stub, eventChainId: ChainId + 1, record);

        using var source = new RpcSharedBridgeDepositSource(
            rpc, SharedBridge, ChainId, L2Bridge, store, startHeight: 10, finalityDepth: 1);

        Assert.AreEqual(1, await source.ScanAsync());
        Assert.AreEqual(0, source.Peek(10).Count);

        using var scanner = new RpcSharedBridgeDepositScanner(
            rpc, SharedBridge, ChainId, store, startHeight: 10, finalityDepth: 1);
        Assert.AreEqual(0, scanner.LoadTrackedNonces().Count);
    }

    private static (JsonRpcClient Rpc, StubRpcHandler Stub) BuildRpc()
    {
        var stub = new StubRpcHandler();
        var http = new HttpClient(stub);
        return (new JsonRpcClient(new Uri(Endpoint), http), stub);
    }

    private static void RegisterDeposit(
        StubRpcHandler stub,
        uint eventChainId,
        SharedBridgeDepositRecord record)
    {
        var depositBytes = record.Encode();
        stub.RegisterRpc((method, parameters) => method switch
        {
            "getblockcount" => new JNumber(12),
            "getblockhash" => new JString(Block10Hash.ToString()),
            "getblock" => new JObject
            {
                ["index"] = 10,
                ["hash"] = Block10Hash.ToString(),
                ["previousblockhash"] = Block9Hash.ToString(),
                ["tx"] = new JArray { new JObject { ["hash"] = TransactionHash.ToString() } },
            },
            "getapplicationlog" => ApplicationLog(eventChainId, record.Nonce),
            "invokefunction" => InvokeGetDeposit(depositBytes),
            _ => null,
        });
    }

    private static JObject ApplicationLog(uint chainId, ulong nonce) => new()
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
                        ["contract"] = SharedBridge.ToString(),
                        ["eventname"] = "DepositEnqueued",
                        ["state"] = new JObject
                        {
                            ["type"] = "Array",
                            ["value"] = new JArray
                            {
                                Integer(chainId),
                                Integer(nonce),
                                new JObject
                                {
                                    ["type"] = "ByteString",
                                    ["value"] = Convert.ToBase64String(Sender.GetSpan().ToArray()),
                                },
                                new JObject
                                {
                                    ["type"] = "ByteString",
                                    ["value"] = Convert.ToBase64String(Recipient.GetSpan().ToArray()),
                                },
                                Integer(1_000),
                            },
                        },
                    },
                },
            },
        },
    };

    private static JObject InvokeGetDeposit(byte[] depositBytes) => new()
    {
        ["state"] = "HALT",
        ["stack"] = new JArray
        {
            new JObject
            {
                ["type"] = "ByteArray",
                ["value"] = Convert.ToBase64String(depositBytes),
            },
        },
    };

    private static JObject Integer<T>(T value) where T : IFormattable => new()
    {
        ["type"] = "Integer",
        ["value"] = value.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>Minimal HTTP stub matching ForcedInclusion/Messaging unit tests.</summary>
    private sealed class StubRpcHandler : HttpMessageHandler
    {
        private readonly List<(string Method, Func<string, JArray?, JToken?> Handler)> _handlers = new();

        public void RegisterRpc(Func<string, JArray?, JToken?> handler)
            => _handlers.Add(("*", handler));

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var envelope = (JObject)JToken.Parse(body)!;
            var method = envelope["method"]!.AsString();
            var parameters = envelope["params"] as JArray;
            JToken? result = null;
            foreach (var (_, handler) in _handlers)
            {
                result = handler(method, parameters);
                if (result is not null) break;
            }
            if (result is null)
                throw new InvalidOperationException($"unhandled RPC method {method}");

            var response = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = envelope["id"],
                ["result"] = result,
            };
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(response.ToString()),
            };
        }
    }
}
