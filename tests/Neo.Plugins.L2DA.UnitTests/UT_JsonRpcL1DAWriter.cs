using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Neo.L2.Settlement.Rpc;

namespace Neo.Plugins.L2DA.UnitTests;

[TestClass]
public class UT_JsonRpcL1DAWriter
{
    /// <summary>HttpMessageHandler that captures the request body and replies with a canned
    /// JSON-RPC response. Same pattern as UT_RpcSettlementClient — keeps the test in-process
    /// without needing a live L1 endpoint.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        public string LastRequestBody { get; private set; } = "";
        public string ResponseBody { get; set; } = "";
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static readonly Uri FakeEndpoint = new("http://localhost/jsonrpc");
    private static readonly UInt160 FakeContract = UInt160.Parse("0x" + new string('a', 40));
    private static readonly UInt256 FakeTxHash = UInt256.Parse("0x" + new string('5', 64));

    private static DAPublishRequest SampleRequest(byte[]? payload = null) => new()
    {
        ChainId = 1001,
        BatchNumber = 7,
        Payload = payload ?? new byte[] { 0x01, 0x02, 0x03 },
    };

    [TestMethod]
    public void Constructor_RejectsNullRpc()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new JsonRpcL1DAWriter(
            null!,
            FakeContract,
            (_, _, _) => new ValueTask<UInt256>(FakeTxHash)));
    }

    [TestMethod]
    public void Constructor_RejectsNullSignAndSend()
    {
        using var http = new HttpClient(new StubHandler());
        using var rpc = new JsonRpcClient(FakeEndpoint, http);
        Assert.ThrowsExactly<ArgumentNullException>(() => new JsonRpcL1DAWriter(rpc, FakeContract, null!));
    }

    [TestMethod]
    public void Constructor_RejectsEmptyIsAvailableMethod()
    {
        using var http = new HttpClient(new StubHandler());
        using var rpc = new JsonRpcClient(FakeEndpoint, http);
        // Empty isAvailableRpcMethod would silently send an invokefunction with method=""
        // → L1 returns FAULT, IsAvailable returns false. Better to surface at ctor.
        Assert.ThrowsExactly<ArgumentException>(() => new JsonRpcL1DAWriter(
            rpc, FakeContract,
            (_, _, _) => new ValueTask<UInt256>(FakeTxHash),
            isAvailableRpcMethod: ""));
    }

    [TestMethod]
    public void Mode_IsL1()
    {
        using var http = new HttpClient(new StubHandler());
        using var rpc = new JsonRpcClient(FakeEndpoint, http);
        using var writer = new JsonRpcL1DAWriter(rpc, FakeContract,
            (_, _, _) => new ValueTask<UInt256>(FakeTxHash));
        Assert.AreEqual(DAMode.L1, writer.Mode);
    }

    [TestMethod]
    public async Task PublishAsync_RejectsNullRequest()
    {
        using var http = new HttpClient(new StubHandler());
        using var rpc = new JsonRpcClient(FakeEndpoint, http);
        using var writer = new JsonRpcL1DAWriter(rpc, FakeContract,
            (_, _, _) => new ValueTask<UInt256>(FakeTxHash));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await writer.PublishAsync(null!));
    }

    [TestMethod]
    public async Task PublishAsync_DelegatesAndReturnsTxHashPointer()
    {
        // The delegate must be invoked with the configured contract hash + the request.
        // The receipt's pointer must be the 32-byte tx hash for off-chain re-fetch.
        UInt160? capturedContract = null;
        DAPublishRequest? capturedRequest = null;
        using var http = new HttpClient(new StubHandler());
        using var rpc = new JsonRpcClient(FakeEndpoint, http);
        using var writer = new JsonRpcL1DAWriter(rpc, FakeContract, (contract, request, _) =>
        {
            capturedContract = contract;
            capturedRequest = request;
            return new ValueTask<UInt256>(FakeTxHash);
        });

        var req = SampleRequest();
        var receipt = await writer.PublishAsync(req);

        Assert.AreEqual(FakeContract, capturedContract);
        Assert.AreSame(req, capturedRequest);
        Assert.AreEqual(DAMode.L1, receipt.Layer);
        // Pointer = tx hash bytes (32B). UInt256.GetSpan().ToArray() round-trips.
        Assert.AreEqual(32, receipt.Pointer.Length);
        CollectionAssert.AreEqual(FakeTxHash.GetSpan().ToArray(), receipt.Pointer.ToArray());
    }

    [TestMethod]
    public async Task PublishAsync_CommitmentIsHash256OfPayload()
    {
        // Cross-tier convention: every IDAWriter sets Commitment = Hash256(payload) so
        // DAAvailabilityCheck can compare across DA layers without knowing the layer.
        using var http = new HttpClient(new StubHandler());
        using var rpc = new JsonRpcClient(FakeEndpoint, http);
        using var writer = new JsonRpcL1DAWriter(rpc, FakeContract,
            (_, _, _) => new ValueTask<UInt256>(FakeTxHash));

        var payload = new byte[] { 0x10, 0x20, 0x30, 0x40 };
        var receipt = await writer.PublishAsync(SampleRequest(payload));
        var expected = new UInt256(Neo.Cryptography.Crypto.Hash256(payload));
        Assert.AreEqual(expected, receipt.Commitment);
    }

    [TestMethod]
    public async Task PublishAsync_SignAndSendReturnsNullThrows()
    {
        // Defense-in-depth: a buggy delegate that returns null would otherwise NRE deep
        // inside the receipt construction with no link back to the bad delegate.
        using var http = new HttpClient(new StubHandler());
        using var rpc = new JsonRpcClient(FakeEndpoint, http);
        using var writer = new JsonRpcL1DAWriter(rpc, FakeContract,
            (_, _, _) => new ValueTask<UInt256>((UInt256)null!));

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await writer.PublishAsync(SampleRequest()));
        StringAssert.Contains(ex.Message, "null tx hash");
    }

    [TestMethod]
    public async Task IsAvailableAsync_ZeroCommitment_ShortCircuitsTrue()
    {
        // Matches the off-chain DAAvailabilityCheck "no DA" sentinel: a zero commitment
        // means the batch never published, so we don't round-trip to L1 just to confirm
        // emptiness.
        using var http = new HttpClient(new StubHandler());
        using var rpc = new JsonRpcClient(FakeEndpoint, http);
        using var writer = new JsonRpcL1DAWriter(rpc, FakeContract,
            (_, _, _) => new ValueTask<UInt256>(FakeTxHash));
        var receipt = new DAReceipt
        {
            Commitment = UInt256.Zero,
            Pointer = ReadOnlyMemory<byte>.Empty,
            Layer = DAMode.L1,
        };
        Assert.IsTrue(await writer.IsAvailableAsync(receipt));
    }

    [TestMethod]
    public async Task IsAvailableAsync_HaltedTrue_ReturnsTrue()
    {
        var stub = new StubHandler
        {
            ResponseBody = "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"state\":\"HALT\",\"stack\":[{\"type\":\"Boolean\",\"value\":\"true\"}]}}",
        };
        using var http = new HttpClient(stub);
        using var rpc = new JsonRpcClient(FakeEndpoint, http);
        using var writer = new JsonRpcL1DAWriter(rpc, FakeContract,
            (_, _, _) => new ValueTask<UInt256>(FakeTxHash));
        var receipt = new DAReceipt
        {
            Commitment = UInt256.Parse("0x" + new string('1', 64)),
            Pointer = ReadOnlyMemory<byte>.Empty,
            Layer = DAMode.L1,
        };
        Assert.IsTrue(await writer.IsAvailableAsync(receipt));
    }

    [TestMethod]
    public async Task IsAvailableAsync_HaltedFalse_ReturnsFalse()
    {
        var stub = new StubHandler
        {
            ResponseBody = "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"state\":\"HALT\",\"stack\":[{\"type\":\"Boolean\",\"value\":\"false\"}]}}",
        };
        using var http = new HttpClient(stub);
        using var rpc = new JsonRpcClient(FakeEndpoint, http);
        using var writer = new JsonRpcL1DAWriter(rpc, FakeContract,
            (_, _, _) => new ValueTask<UInt256>(FakeTxHash));
        var receipt = new DAReceipt
        {
            Commitment = UInt256.Parse("0x" + new string('1', 64)),
            Pointer = ReadOnlyMemory<byte>.Empty,
            Layer = DAMode.L1,
        };
        Assert.IsFalse(await writer.IsAvailableAsync(receipt));
    }

    [TestMethod]
    public async Task IsAvailableAsync_FaultedState_ReturnsFalse()
    {
        // L1 contract faulted (e.g. method not found) → not-available. We don't throw
        // since the L2 audit framework treats false as "DA is gone, flag the batch".
        var stub = new StubHandler
        {
            ResponseBody = "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"state\":\"FAULT\",\"stack\":[]}}",
        };
        using var http = new HttpClient(stub);
        using var rpc = new JsonRpcClient(FakeEndpoint, http);
        using var writer = new JsonRpcL1DAWriter(rpc, FakeContract,
            (_, _, _) => new ValueTask<UInt256>(FakeTxHash));
        var receipt = new DAReceipt
        {
            Commitment = UInt256.Parse("0x" + new string('1', 64)),
            Pointer = ReadOnlyMemory<byte>.Empty,
            Layer = DAMode.L1,
        };
        Assert.IsFalse(await writer.IsAvailableAsync(receipt));
    }

    [TestMethod]
    public async Task PublishAsync_AfterDispose_Throws()
    {
        using var http = new HttpClient(new StubHandler());
        var rpc = new JsonRpcClient(FakeEndpoint, http);
        var writer = new JsonRpcL1DAWriter(rpc, FakeContract,
            (_, _, _) => new ValueTask<UInt256>(FakeTxHash));
        writer.Dispose();
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(
            async () => await writer.PublishAsync(SampleRequest()));
    }
}
