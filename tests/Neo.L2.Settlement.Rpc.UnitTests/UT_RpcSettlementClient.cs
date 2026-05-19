using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Neo.Json;

namespace Neo.L2.Settlement.Rpc.UnitTests;

[TestClass]
public class UT_RpcSettlementClient
{
    /// <summary>HttpMessageHandler that captures the request body and replies with a canned response.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        public string LastRequestBody { get; private set; } = "";
        public string ResponseBody { get; set; } = "";
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            return new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static readonly Uri FakeEndpoint = new("http://localhost/jsonrpc");

    [TestMethod]
    public async Task JsonRpcClient_SendsCanonicalEnvelope()
    {
        var stub = new StubHandler { ResponseBody = "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":42}" };
        using var http = new HttpClient(stub);
        using var client = new JsonRpcClient(FakeEndpoint, http);

        var result = await client.CallAsync("ping", new JArray { 1, "hi" });
        Assert.AreEqual(42, (int)result!.AsNumber());

        StringAssert.Contains(stub.LastRequestBody, "\"jsonrpc\":\"2.0\"");
        StringAssert.Contains(stub.LastRequestBody, "\"method\":\"ping\"");
        StringAssert.Contains(stub.LastRequestBody, "\"id\":1");
    }

    [TestMethod]
    public async Task JsonRpcClient_NetworkError_WrappedAsJsonRpcException()
    {
        // SendAsync throws HttpRequestException (connection refused / DNS fail / TLS).
        // Wrap as JsonRpcException so callers see the uniform contract.
        var stub = new ThrowingHandler { ToThrow = new HttpRequestException("connection refused") };
        using var http = new HttpClient(stub);
        using var client = new JsonRpcClient(FakeEndpoint, http);

        var ex = await Assert.ThrowsExactlyAsync<JsonRpcException>(async () =>
            await client.CallAsync("ping", new JArray()));
        Assert.AreEqual(-32603, ex.Code);
        StringAssert.Contains(ex.Message, "connection refused");
    }

    [TestMethod]
    public async Task JsonRpcClient_CallerCancellation_PropagatesOperationCanceled()
    {
        // Caller-driven cancellation must NOT be wrapped — the caller expects
        // OperationCanceledException so they can distinguish their own cancel from a
        // server-side failure.
        var stub = new ThrowingHandler { ToThrow = new TaskCanceledException("cancelled") };
        using var http = new HttpClient(stub);
        using var client = new JsonRpcClient(FakeEndpoint, http);

        var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () =>
            await client.CallAsync("ping", new JArray(), cts.Token));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        public Exception ToThrow { get; set; } = new InvalidOperationException();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw ToThrow;
        }
    }

    [TestMethod]
    public async Task JsonRpcClient_HttpError_ThrowsJsonRpcException()
    {
        // Symmetric with the malformed-JSON test: server returns HTTP 502. Used to leak
        // as HttpRequestException from EnsureSuccessStatusCode; now wraps as JsonRpcException
        // so callers see one exception type for all RPC failure modes.
        var stub = new StubHandler
        {
            ResponseBody = "Bad Gateway\n",
            StatusCode = HttpStatusCode.BadGateway,
        };
        using var http = new HttpClient(stub);
        using var client = new JsonRpcClient(FakeEndpoint, http);

        var ex = await Assert.ThrowsExactlyAsync<JsonRpcException>(async () =>
            await client.CallAsync("ping", new JArray()));
        Assert.AreEqual(-32603, ex.Code);
        StringAssert.Contains(ex.Message, "502");
    }

    [TestMethod]
    public async Task JsonRpcClient_MalformedJson_ThrowsJsonRpcException()
    {
        // Server returns malformed JSON (e.g. proxy error page in HTML, or truncated body).
        // The client must wrap this as a JsonRpcException so callers don't have to handle
        // disparate parser exceptions.
        var stub = new StubHandler { ResponseBody = "<html><body>502 Bad Gateway</body></html>" };
        using var http = new HttpClient(stub);
        using var client = new JsonRpcClient(FakeEndpoint, http);

        var ex = await Assert.ThrowsExactlyAsync<JsonRpcException>(async () =>
            await client.CallAsync("ping", new JArray()));
        Assert.AreEqual(-32700, ex.Code, "JSON-RPC 2.0 spec code for parse error");
    }

    [TestMethod]
    public async Task JsonRpcClient_SurfacesRpcError()
    {
        var stub = new StubHandler
        {
            ResponseBody = "{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"code\":-32601,\"message\":\"method not found\"}}",
        };
        using var http = new HttpClient(stub);
        using var client = new JsonRpcClient(FakeEndpoint, http);

        var ex = await Assert.ThrowsExactlyAsync<JsonRpcException>(async () =>
            await client.CallAsync("missing", new JArray()));
        Assert.AreEqual(-32601, ex.Code);
    }

    [TestMethod]
    public async Task SettlementClient_GetCanonicalStateRoot_ParsesByteString()
    {
        var rootBytes = new byte[32];
        for (var i = 0; i < 32; i++) rootBytes[i] = (byte)(i + 1);
        var b64 = Convert.ToBase64String(rootBytes);
        var stub = new StubHandler
        {
            ResponseBody = "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"state\":\"HALT\",\"gasconsumed\":\"100\",\"stack\":[{\"type\":\"ByteString\",\"value\":\"" + b64 + "\"}]}}",
        };
        using var http = new HttpClient(stub);
        var rpc = new JsonRpcClient(FakeEndpoint, http);
        using var settlement = new RpcSettlementClient(
            rpc,
            UInt160.Parse("0x" + new string('1', 40)),
            (sm, bytes, ct) => new ValueTask<UInt256>(UInt256.Zero));

        var root = await settlement.GetCanonicalStateRootAsync(1001);
        Assert.AreEqual(new UInt256(rootBytes), root);
    }

    [TestMethod]
    public async Task SettlementClient_GetBatchStatus_ParsesIntegerStack()
    {
        var stub = new StubHandler
        {
            ResponseBody = "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"state\":\"HALT\",\"gasconsumed\":\"100\",\"stack\":[{\"type\":\"Integer\",\"value\":\"3\"}]}}",
        };
        using var http = new HttpClient(stub);
        var rpc = new JsonRpcClient(FakeEndpoint, http);
        using var settlement = new RpcSettlementClient(
            rpc,
            UInt160.Parse("0x" + new string('1', 40)),
            (sm, bytes, ct) => new ValueTask<UInt256>(UInt256.Zero));

        var status = await settlement.GetBatchStatusAsync(1001, 7);
        Assert.AreEqual(BatchStatus.Finalized, status);
    }

    [TestMethod]
    public async Task SettlementClient_FaultedStateThrows()
    {
        var stub = new StubHandler
        {
            ResponseBody = "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"state\":\"FAULT\",\"gasconsumed\":\"100\",\"stack\":[]}}",
        };
        using var http = new HttpClient(stub);
        var rpc = new JsonRpcClient(FakeEndpoint, http);
        using var settlement = new RpcSettlementClient(
            rpc,
            UInt160.Parse("0x" + new string('1', 40)),
            (sm, bytes, ct) => new ValueTask<UInt256>(UInt256.Zero));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await settlement.GetBatchStatusAsync(1001, 7));
    }

    [TestMethod]
    public async Task SettlementClient_SubmitBatch_DelegatesToSigner()
    {
        var stub = new StubHandler { ResponseBody = "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":\"unused\"}" };
        using var http = new HttpClient(stub);
        var rpc = new JsonRpcClient(FakeEndpoint, http);
        var sentBytes = (byte[]?)null;
        var sentTo = (UInt160?)null;
        var fakeTxHash = UInt256.Parse("0x" + new string('f', 64));

        using var settlement = new RpcSettlementClient(
            rpc,
            UInt160.Parse("0x" + new string('1', 40)),
            (sm, bytes, ct) => { sentTo = sm; sentBytes = bytes; return new ValueTask<UInt256>(fakeTxHash); });

        var commitment = new L2BatchCommitment
        {
            ChainId = 1001,
            BatchNumber = 5,
            FirstBlock = 100,
            LastBlock = 200,
            PreStateRoot = UInt256.Zero,
            PostStateRoot = UInt256.Parse("0x" + new string('a', 64)),
            TxRoot = UInt256.Zero,
            ReceiptRoot = UInt256.Zero,
            WithdrawalRoot = UInt256.Zero,
            L2ToL1MessageRoot = UInt256.Zero,
            L2ToL2MessageRoot = UInt256.Zero,
            DACommitment = UInt256.Zero,
            PublicInputHash = UInt256.Zero,
            ProofType = ProofType.Multisig,
            Proof = new byte[] { 0xAB },
        };
        var publicInputs = new PublicInputs
        {
            ChainId = 1001,
            BatchNumber = 5,
            PreStateRoot = UInt256.Zero,
            PostStateRoot = commitment.PostStateRoot,
            TxRoot = UInt256.Zero,
            ReceiptRoot = UInt256.Zero,
            WithdrawalRoot = UInt256.Zero,
            L2ToL1MessageRoot = UInt256.Zero,
            L2ToL2MessageRoot = UInt256.Zero,
            L1MessageHash = UInt256.Zero,
            DACommitment = UInt256.Zero,
            BlockContextHash = UInt256.Zero,
        };

        var txHash = await settlement.SubmitBatchAsync(commitment, publicInputs);
        Assert.AreEqual(fakeTxHash, txHash);
        Assert.AreEqual(UInt160.Parse("0x" + new string('1', 40)), sentTo);
        Assert.IsNotNull(sentBytes);
        Assert.IsTrue(sentBytes!.Length > 0);
    }

    [TestMethod]
    public void JsonRpcClient_Constructor_RejectsNullEndpoint()
        => Assert.ThrowsExactly<ArgumentNullException>(
            () => new JsonRpcClient((Uri)null!, httpClient: null));

    [TestMethod]
    public async Task JsonRpcClient_CallAsync_RejectsNullMethod()
    {
        // Pin JsonRpcClient.cs:63.
        using var client = new JsonRpcClient(new Uri("http://localhost"), httpClient: null);
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await client.CallAsync(null!, new JArray()));
    }

    [TestMethod]
    public async Task JsonRpcClient_CallAsync_RejectsNullParams()
    {
        // Pin JsonRpcClient.cs:64.
        using var client = new JsonRpcClient(new Uri("http://localhost"), httpClient: null);
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await client.CallAsync("foo", null!));
    }

    [TestMethod]
    public async Task SubmitBatchAsync_RejectsNullCommitment()
    {
        // Pin RpcSettlementClient.cs:46. Without it BatchSerializer.Encode(null) throws
        // with a generic ArgumentNullException naming "commitment" but BatchSerializer's
        // own line — naming the bad input directly at the API boundary is clearer.
        using var rpc = new JsonRpcClient(new Uri("http://localhost"), httpClient: null);
        var client = new RpcSettlementClient(rpc, UInt160.Zero, (sm, b, ct) => new ValueTask<UInt256>(UInt256.Zero));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await client.SubmitBatchAsync(null!, new PublicInputs
            {
                ChainId = 1,
                BatchNumber = 1,
                PreStateRoot = UInt256.Zero,
                PostStateRoot = UInt256.Zero,
                TxRoot = UInt256.Zero,
                ReceiptRoot = UInt256.Zero,
                WithdrawalRoot = UInt256.Zero,
                L2ToL1MessageRoot = UInt256.Zero,
                L2ToL2MessageRoot = UInt256.Zero,
                L1MessageHash = UInt256.Zero,
                DACommitment = UInt256.Zero,
                BlockContextHash = UInt256.Zero,
            }));
    }

    [TestMethod]
    public void RpcSettlementClient_Constructor_RejectsNullRpc()
    {
        // Pin RpcSettlementClient.cs:36.
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new RpcSettlementClient(null!, UInt160.Zero, (sm, b, ct) => new ValueTask<UInt256>(UInt256.Zero)));
    }

    [TestMethod]
    public void RpcSettlementClient_Constructor_RejectsNullSignAndSend()
    {
        // Pin RpcSettlementClient.cs:37.
        using var rpc = new JsonRpcClient(new Uri("http://localhost"), httpClient: null);
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new RpcSettlementClient(rpc, UInt160.Zero, null!));
    }

    [TestMethod]
    public void JsonRpcClient_RejectsRelativeUri()
    {
        // Regression for iter 207: a relative URI's .Scheme access throws
        // InvalidOperationException("operation not supported on relative URI"). The
        // iter-206 scheme check would have surfaced that confusing message; now
        // we reject the relative URI at the ctor with a clear ArgumentException.
        var ex = Assert.ThrowsExactly<ArgumentException>(
            () => new JsonRpcClient(new Uri("/x", UriKind.Relative), httpClient: null));
        StringAssert.Contains(ex.Message, "absolute URI");
    }

    [TestMethod]
    public void JsonRpcClient_RejectsNonHttpScheme()
    {
        // Regression for iter 206: a misconfigured endpoint with file://, mailto:, ftp://
        // etc. would previously slip past the ctor and surface as an obscure HttpClient
        // SendAsync error at first request. Now caught at construction.
        Assert.ThrowsExactly<ArgumentException>(
            () => new JsonRpcClient(new Uri("file:///tmp/x"), httpClient: null));
        Assert.ThrowsExactly<ArgumentException>(
            () => new JsonRpcClient(new Uri("ftp://example.com"), httpClient: null));
        Assert.ThrowsExactly<ArgumentException>(
            () => new JsonRpcClient(new Uri("mailto:foo@bar"), httpClient: null));

        // Boundary: http and https must succeed.
        using var http1 = new JsonRpcClient(new Uri("http://localhost"), httpClient: null);
        using var http2 = new JsonRpcClient(new Uri("https://example.com"), httpClient: null);
    }

    [TestMethod]
    public async Task JsonRpcClient_OutOfRangeErrorCode_ClampedToInternalError()
    {
        // Regression for iter 204: a server-supplied error code outside int range
        // (e.g. 1e100) used to wrap silently to int.MinValue or undefined behavior.
        // Now: clamps out-of-range to the -32603 "internal error" sentinel.
        var stub = new StubHandler
        {
            ResponseBody = "{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"code\":1e100,\"message\":\"insane code\"}}",
        };
        using var http = new HttpClient(stub);
        using var client = new JsonRpcClient(FakeEndpoint, http);

        var ex = await Assert.ThrowsExactlyAsync<JsonRpcException>(async () =>
            await client.CallAsync("ping", new JArray()));
        Assert.AreEqual(-32603, ex.Code);
        StringAssert.Contains(ex.Message, "insane code");
    }

    [TestMethod]
    public async Task SubmitBatchAsync_BuggySignAndSendReturnsNull_SurfacesContractViolation()
    {
        // Regression for iter 189: a buggy SignAndSendAsync delegate returning null
        // UInt256 would propagate as a NRE further downstream (e.g. an L1-tracker that
        // dereferences the tx hash). Now surfaced at the boundary as a clear
        // InvalidOperationException naming the delegate.
        using var rpc = new JsonRpcClient(FakeEndpoint, new HttpClient(new StubHandler { ResponseBody = "{}" }));
        using var settlement = new RpcSettlementClient(
            rpc,
            UInt160.Parse("0x" + new string('1', 40)),
            (sm, bytes, ct) => new ValueTask<UInt256>((UInt256)null!));

        var commitment = new L2BatchCommitment
        {
            ChainId = 1001,
            BatchNumber = 5,
            FirstBlock = 100,
            LastBlock = 200,
            PreStateRoot = UInt256.Zero,
            PostStateRoot = UInt256.Zero,
            TxRoot = UInt256.Zero,
            ReceiptRoot = UInt256.Zero,
            WithdrawalRoot = UInt256.Zero,
            L2ToL1MessageRoot = UInt256.Zero,
            L2ToL2MessageRoot = UInt256.Zero,
            DACommitment = UInt256.Zero,
            PublicInputHash = UInt256.Zero,
            ProofType = ProofType.Multisig,
            Proof = new byte[] { 0xAB },
        };
        var publicInputs = new PublicInputs
        {
            ChainId = 1001,
            BatchNumber = 5,
            PreStateRoot = UInt256.Zero,
            PostStateRoot = UInt256.Zero,
            TxRoot = UInt256.Zero,
            ReceiptRoot = UInt256.Zero,
            WithdrawalRoot = UInt256.Zero,
            L2ToL1MessageRoot = UInt256.Zero,
            L2ToL2MessageRoot = UInt256.Zero,
            L1MessageHash = UInt256.Zero,
            DACommitment = UInt256.Zero,
            BlockContextHash = UInt256.Zero,
        };
        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await settlement.SubmitBatchAsync(commitment, publicInputs));
        StringAssert.Contains(ex.Message, "SignAndSendAsync");
    }

    [TestMethod]
    public async Task JsonRpcClient_RejectsMismatchedResponseId()
    {
        // Regression for iter 182: JSON-RPC 2.0 §5 mandates response id == request id.
        // A buggy server / misconfigured proxy returning a response with the wrong id
        // would previously be accepted silently. Now caught as a contract violation.
        // The first call sends id=1; we craft a response with id=999 to force a mismatch.
        var stub = new StubHandler
        {
            ResponseBody = "{\"jsonrpc\":\"2.0\",\"id\":999,\"result\":42}",
        };
        using var http = new HttpClient(stub);
        using var client = new JsonRpcClient(FakeEndpoint, http);

        var ex = await Assert.ThrowsExactlyAsync<JsonRpcException>(async () =>
            await client.CallAsync("ping", new JArray()));
        Assert.AreEqual(-32603, ex.Code);
        StringAssert.Contains(ex.Message, "999");
        StringAssert.Contains(ex.Message, "request id");
    }
}
