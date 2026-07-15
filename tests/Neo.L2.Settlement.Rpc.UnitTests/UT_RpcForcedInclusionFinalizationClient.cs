using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Neo.Cryptography;
using Neo.Extensions.VM;
using Neo.SmartContract;
using Neo.VM;

namespace Neo.L2.Settlement.Rpc.UnitTests;

[TestClass]
public class UT_RpcForcedInclusionFinalizationClient
{
    private static readonly Uri FakeEndpoint = new("http://localhost/jsonrpc");
    private static readonly UInt160 SettlementManager = UInt160.Parse("0x" + new string('1', 40));
    private static readonly UInt160 ForcedInclusion = UInt160.Parse("0x" + new string('2', 40));

    [TestMethod]
    public async Task ConsumeAndConfirm_ValidProofBroadcastsCanonicalInvocationAndReadsBack()
    {
        var proof = CreateProof(1, Hash(0x11));
        using var handler = new QueueHandler(
            RootResponse(proof.TxHash),
            BooleanResponse(false),
            BooleanResponse(true));
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(FakeEndpoint, http);
        byte[]? submittedScript = null;
        var client = CreateClient(rpc, (script, _) =>
        {
            submittedScript = script.ToArray();
            return ValueTask.FromResult(HaltReceipt());
        });

        await client.ConsumeAndConfirmAsync(1001, 7, [proof]);

        Assert.IsNotNull(submittedScript);
        using var expected = new ScriptBuilder();
        expected.EmitDynamicCall(
            ForcedInclusion,
            "consume",
            CallFlags.All,
            1001u,
            7UL,
            1UL,
            new ContractParameter(ContractParameterType.Array)
            {
                Value = new List<ContractParameter>(),
            },
            0UL);
        CollectionAssert.AreEqual(expected.ToArray(), submittedScript);
        Assert.AreEqual(3, handler.RequestBodies.Count);
        StringAssert.Contains(handler.RequestBodies[0], "getFinalizedTxRoot");
        StringAssert.Contains(handler.RequestBodies[1], "isConsumed");
        StringAssert.Contains(handler.RequestBodies[2], "isConsumed");
    }

    [TestMethod]
    public async Task ConsumeAndConfirm_AlreadyConsumedIsIdempotent()
    {
        var proof = CreateProof(1, Hash(0x22));
        using var handler = new QueueHandler(RootResponse(proof.TxHash), BooleanResponse(true));
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(FakeEndpoint, http);
        var broadcasts = 0;
        var client = CreateClient(rpc, (_, _) =>
        {
            broadcasts++;
            return ValueTask.FromResult(HaltReceipt());
        });

        await client.ConsumeAndConfirmAsync(1001, 7, [proof]);

        Assert.AreEqual(0, broadcasts);
        Assert.AreEqual(2, handler.RequestBodies.Count);
    }

    [TestMethod]
    public async Task ConsumeAndConfirm_PartialPriorSuccessOnlySubmitsRemainingNonce()
    {
        var left = Hash(0x31);
        var right = Hash(0x32);
        var root = HashPair(left, right);
        var first = CreateProof(1, left, 0, right);
        var second = CreateProof(2, right, 1, left);
        using var handler = new QueueHandler(
            RootResponse(root),
            BooleanResponse(true),
            BooleanResponse(false),
            BooleanResponse(true));
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(FakeEndpoint, http);
        var broadcasts = 0;
        var client = CreateClient(rpc, (_, _) =>
        {
            broadcasts++;
            return ValueTask.FromResult(HaltReceipt());
        });

        await client.ConsumeAndConfirmAsync(1001, 7, [first, second]);

        Assert.AreEqual(1, broadcasts);
        Assert.AreEqual(4, handler.RequestBodies.Count);
    }

    [TestMethod]
    public async Task ConsumeAndConfirm_AmbiguousBroadcastReconcilesConcurrentConsumption()
    {
        var proof = CreateProof(1, Hash(0x41));
        using var handler = new QueueHandler(
            RootResponse(proof.TxHash),
            BooleanResponse(false),
            BooleanResponse(true));
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(FakeEndpoint, http);
        var client = CreateClient(
            rpc,
            (_, _) => throw new InvalidOperationException("broadcast response lost"));

        await client.ConsumeAndConfirmAsync(1001, 7, [proof]);

        Assert.AreEqual(3, handler.RequestBodies.Count);
    }

    [TestMethod]
    public async Task ConsumeAndConfirm_WrongFinalizedRootRejectsBeforeBroadcast()
    {
        var proof = CreateProof(1, Hash(0x51));
        using var handler = new QueueHandler(RootResponse(Hash(0x52)));
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(FakeEndpoint, http);
        var broadcasts = 0;
        var client = CreateClient(rpc, (_, _) =>
        {
            broadcasts++;
            return ValueTask.FromResult(HaltReceipt());
        });

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await client.ConsumeAndConfirmAsync(1001, 7, [proof]));

        Assert.AreEqual(0, broadcasts);
        Assert.AreEqual(1, handler.RequestBodies.Count);
    }

    [TestMethod]
    public async Task ConsumeAndConfirm_ZeroFinalizedRootRejectsBeforeBroadcast()
    {
        var proof = CreateProof(1, Hash(0x61));
        using var handler = new QueueHandler(RootResponse(UInt256.Zero));
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(FakeEndpoint, http);
        var client = CreateClient(rpc, (_, _) => ValueTask.FromResult(HaltReceipt()));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await client.ConsumeAndConfirmAsync(1001, 7, [proof]));
    }

    [TestMethod]
    public async Task ConsumeAndConfirm_FalseReadbackRejectsConfirmedTransaction()
    {
        var proof = CreateProof(1, Hash(0x71));
        using var handler = new QueueHandler(
            RootResponse(proof.TxHash),
            BooleanResponse(false),
            BooleanResponse(false));
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(FakeEndpoint, http);
        var client = CreateClient(rpc, (_, _) => ValueTask.FromResult(HaltReceipt()));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await client.ConsumeAndConfirmAsync(1001, 7, [proof]));
    }

    [TestMethod]
    public async Task ConsumeAndConfirm_DuplicateNonceRejectsBeforeRpcOrBroadcast()
    {
        var proof = CreateProof(1, Hash(0x81));
        using var handler = new QueueHandler();
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(FakeEndpoint, http);
        var broadcasts = 0;
        var client = CreateClient(rpc, (_, _) =>
        {
            broadcasts++;
            return ValueTask.FromResult(HaltReceipt());
        });

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await client.ConsumeAndConfirmAsync(1001, 7, [proof, proof]));

        Assert.AreEqual(0, broadcasts);
        Assert.AreEqual(0, handler.RequestBodies.Count);
    }

    [TestMethod]
    public async Task ConsumeAndConfirm_NonCanonicalLeafIndexRejectsBeforeRpc()
    {
        var proof = CreateProof(1, Hash(0x91), leafIndex: 1);
        using var handler = new QueueHandler();
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(FakeEndpoint, http);
        var client = CreateClient(rpc, (_, _) => ValueTask.FromResult(HaltReceipt()));

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await client.ConsumeAndConfirmAsync(1001, 7, [proof]));

        Assert.AreEqual(0, handler.RequestBodies.Count);
    }

    [TestMethod]
    public async Task ConsumeAndConfirm_InvalidChainOrBatchRejects()
    {
        var proof = CreateProof(1, Hash(0xA1));
        using var handler = new QueueHandler();
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(FakeEndpoint, http);
        var client = CreateClient(rpc, (_, _) => ValueTask.FromResult(HaltReceipt()));

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () =>
            await client.ConsumeAndConfirmAsync(0, 7, [proof]));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () =>
            await client.ConsumeAndConfirmAsync(1001, 0, [proof]));
        Assert.AreEqual(0, handler.RequestBodies.Count);
    }

    private static RpcForcedInclusionFinalizationClient CreateClient(
        JsonRpcClient rpc,
        RpcForcedInclusionFinalizationClient.SendInvocationAsync sendInvocation)
        => new(rpc, SettlementManager, ForcedInclusion, sendInvocation);

    private static ForcedInclusionConsumptionProof CreateProof(
        ulong nonce,
        UInt256 transactionHash,
        uint leafIndex = 0,
        params UInt256[] siblings)
        => new()
        {
            Nonce = nonce,
            LeafIndex = leafIndex,
            TxHash = transactionHash,
            Siblings = siblings,
        };

    private static UInt256 Hash(byte fill)
    {
        var bytes = new byte[32];
        Array.Fill(bytes, fill);
        return new UInt256(bytes);
    }

    private static UInt256 HashPair(UInt256 left, UInt256 right)
    {
        var pair = new byte[64];
        left.GetSpan().CopyTo(pair);
        right.GetSpan().CopyTo(pair.AsSpan(32));
        return new UInt256(Crypto.Hash256(pair));
    }

    private static RpcTransactionReceipt HaltReceipt()
        => new(Hash(0xF1), "HALT", null, 1, 1);

    private static string RootResponse(UInt256 root)
        => StackResponse("ByteString", Convert.ToBase64String(root.GetSpan()));

    private static string BooleanResponse(bool value)
        => StackResponse("Boolean", value ? "true" : "false");

    private static string StackResponse(string type, string value)
        => "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"state\":\"HALT\",\"stack\":[{\"type\":\"" +
            type + "\",\"value\":\"" + value + "\"}]}}";

    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses;

        public QueueHandler(params string[] responses)
        {
            _responses = new Queue<string>(responses);
        }

        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            long requestId = 0;
            if (request.Content is not null)
            {
                var requestBody = await request.Content.ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);
                RequestBodies.Add(requestBody);
                using var requestJson = JsonDocument.Parse(requestBody);
                requestId = requestJson.RootElement.GetProperty("id").GetInt64();
            }

            if (_responses.Count == 0)
                throw new InvalidOperationException("No queued RPC response.");
            var responseBody = _responses.Dequeue().Replace(
                "\"id\":1",
                $"\"id\":{requestId}",
                StringComparison.Ordinal);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    responseBody,
                    Encoding.UTF8,
                    "application/json"),
            };
        }
    }
}
