using System.Net;
using System.Text;
using Neo.Json;
using Neo.L2.Bridge.External;
using Neo.L2.Persistence;
using Neo.L2.Settlement.Rpc;

using static Neo.L2.ExternalBridge.UnitTests.PayoutTestData;

namespace Neo.L2.ExternalBridge.UnitTests;

[TestClass]
public sealed class UT_RpcL1PayoutQueueScanner
{
    private static readonly Uri Endpoint = new("http://l1.example/");
    private static readonly UInt256 Block9Hash = H256(0x09);
    private static readonly UInt256 Block10Hash = H256(0x10);
    private static readonly UInt256 TransactionHash = H256(0x20);

    [TestMethod]
    public async Task Scan_CanonicalFinalizedEvent_IsDurableAndRestartSafe()
    {
        using var handler = new ScannerRpcHandler(Instruction());
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(Endpoint, http);
        using var store = new InMemoryKeyValueStore();
        using var outbox = new PersistentL2PayoutOutbox(store);

        using (var scanner = new RpcL1PayoutQueueScanner(
            rpc, Adapter, NeoChainId, store, outbox, startHeight: 10, finalityDepth: 1))
        {
            Assert.AreEqual(1, await scanner.ScanAsync());
        }

        Assert.AreEqual(Instruction(), outbox.LoadPending().Single().Instruction);
        using var restarted = new RpcL1PayoutQueueScanner(
            rpc, Adapter, NeoChainId, store, outbox, startHeight: 10, finalityDepth: 1);
        Assert.AreEqual(0, await restarted.ScanAsync());
        CollectionAssert.Contains(handler.Methods, "getblockhash");
    }

    [TestMethod]
    public async Task Scan_ChangedFinalizedHistory_FailsClosed()
    {
        using var handler = new ScannerRpcHandler(Instruction());
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(Endpoint, http);
        using var store = new InMemoryKeyValueStore();
        using var outbox = new PersistentL2PayoutOutbox(store);
        using var scanner = new RpcL1PayoutQueueScanner(
            rpc, Adapter, NeoChainId, store, outbox, startHeight: 10, finalityDepth: 1);

        Assert.AreEqual(1, await scanner.ScanAsync());
        handler.ResumeHash = H256(0xFE);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () =>
            await scanner.ScanAsync());
    }

    [TestMethod]
    public async Task Scan_ForeignOrFaultedNotifications_AreIgnored()
    {
        using var handler = new ScannerRpcHandler(Instruction())
        {
            IncludeCanonicalNotification = false,
        };
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(Endpoint, http);
        using var store = new InMemoryKeyValueStore();
        using var outbox = new PersistentL2PayoutOutbox(store);
        using var scanner = new RpcL1PayoutQueueScanner(
            rpc, Adapter, NeoChainId, store, outbox, startHeight: 10, finalityDepth: 1);

        Assert.AreEqual(1, await scanner.ScanAsync());
        Assert.AreEqual(0, outbox.LoadPending().Count);
    }

    [TestMethod]
    public async Task Scan_MalformedCanonicalNotification_DoesNotAdvanceCursor()
    {
        using var handler = new ScannerRpcHandler(Instruction())
        {
            MalformedCanonicalNotification = true,
        };
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(Endpoint, http);
        using var store = new InMemoryKeyValueStore();
        using var outbox = new PersistentL2PayoutOutbox(store);
        using var scanner = new RpcL1PayoutQueueScanner(
            rpc, Adapter, NeoChainId, store, outbox, startHeight: 10, finalityDepth: 1);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () =>
            await scanner.ScanAsync());
        handler.MalformedCanonicalNotification = false;
        Assert.AreEqual(1, await scanner.ScanAsync());
    }

    [TestMethod]
    public async Task Scanner_RejectsUnsafeConstructionAndUseAfterDispose()
    {
        using var handler = new ScannerRpcHandler(Instruction());
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(Endpoint, http);
        using var store = new InMemoryKeyValueStore();
        using var outbox = new PersistentL2PayoutOutbox(store);

        Assert.ThrowsExactly<ArgumentException>(() => new RpcL1PayoutQueueScanner(
            rpc, UInt160.Zero, NeoChainId, store, outbox, 10));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RpcL1PayoutQueueScanner(
            rpc, Adapter, 0, store, outbox, 10));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RpcL1PayoutQueueScanner(
            rpc, Adapter, NeoChainId, store, outbox, 10, maximumBlocksPerScan: 0));

        var scanner = new RpcL1PayoutQueueScanner(
            rpc, Adapter, NeoChainId, store, outbox, startHeight: 10);
        scanner.Dispose();
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(async () =>
            await scanner.ScanAsync());
    }

    private sealed class ScannerRpcHandler(L2PayoutInstruction instruction) : HttpMessageHandler
    {
        public List<string> Methods { get; } = [];
        public UInt256 ResumeHash { get; set; } = Block10Hash;
        public bool IncludeCanonicalNotification { get; init; } = true;
        public bool MalformedCanonicalNotification { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var envelope = (JObject)JToken.Parse(body)!;
            var method = envelope["method"]!.AsString();
            Methods.Add(method);
            var response = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = envelope["id"],
                ["result"] = Result(method),
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response.ToString(), Encoding.UTF8, "application/json"),
            };
        }

        private JToken Result(string method) => method switch
        {
            "getblockcount" => new JNumber(12),
            "getblockhash" => new JString(ResumeHash.ToString()),
            "getblock" => Block(),
            "getapplicationlog" => ApplicationLog(),
            _ => throw new InvalidOperationException($"Unexpected RPC method {method}"),
        };

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

        private JObject ApplicationLog()
        {
            var notifications = new JArray
            {
                Notification(H160(0xEE), "PayoutEnqueued", canonical: true),
                Notification(Adapter, "Unrelated", canonical: true),
            };
            if (IncludeCanonicalNotification)
                notifications.Add(Notification(Adapter, "PayoutEnqueued", canonical: true));
            return new JObject
            {
                ["executions"] = new JArray
                {
                    new JObject
                    {
                        ["vmstate"] = "FAULT",
                        ["notifications"] = new JArray
                        {
                            Notification(Adapter, "PayoutEnqueued", canonical: true),
                        },
                    },
                    new JObject
                    {
                        ["vmstate"] = "HALT",
                        ["notifications"] = notifications,
                    },
                },
            };
        }

        private JObject Notification(UInt160 contract, string eventName, bool canonical)
        {
            var values = CanonicalValues();
            if (MalformedCanonicalNotification && contract == Adapter && eventName == "PayoutEnqueued")
                values.RemoveAt(values.Count - 1);
            return new JObject
            {
                ["contract"] = contract.ToString(),
                ["eventname"] = eventName,
                ["state"] = new JObject
                {
                    ["type"] = canonical ? "Array" : "Any",
                    ["value"] = values,
                },
            };
        }

        private JArray CanonicalValues()
        {
            var foreignAsset = new byte[ExternalAssetId.Length];
            instruction.ForeignAsset.CopyTo(foreignAsset);
            return new JArray
            {
                Integer(instruction.Sequence),
                Bytes(instruction.Message.MessageHash.GetSpan()),
                Integer(instruction.Message.ExternalChainId),
                Integer(instruction.Message.NeoChainId),
                Integer(instruction.Message.Nonce),
                Bytes(foreignAsset),
                Bytes(instruction.NeoAsset.GetSpan()),
                Bytes(instruction.Message.Recipient.GetSpan()),
                Integer(instruction.Amount),
                Integer(instruction.Message.DeadlineUnixSeconds),
                Bytes(instruction.Message.SourceTxRef.GetSpan()),
                Bytes(instruction.CanonicalMessageBytes.Span),
            };
        }

        private static JObject Integer<T>(T value) where T : IFormattable => new()
        {
            ["type"] = "Integer",
            ["value"] = value.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        };

        private static JObject Bytes(ReadOnlySpan<byte> value) => new()
        {
            ["type"] = "ByteString",
            ["value"] = Convert.ToBase64String(value),
        };
    }
}
