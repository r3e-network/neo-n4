using System.Net;
using System.Text;
using Neo.Extensions.VM;
using Neo.L2.Settlement.Rpc;

namespace Neo.L2.Settlement.Rpc.UnitTests;

[TestClass]
public class UT_RpcSettlementWindowFinalizer
{
    private static readonly Uri FakeEndpoint = new("http://localhost/jsonrpc");
    private static readonly UInt160 Challenge = UInt160.Parse("0x" + new string('c', 40));

    [TestMethod]
    public async Task IsWindowExpiredAsync_NoWindow_ReturnsFalse()
    {
        using var handler = new QueueHandler(IntegerResponse(0));
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(FakeEndpoint, http);
        var finalizer = CreateFinalizer(rpc, (_, _) => ValueTask.FromResult(HaltReceipt()));

        Assert.IsFalse(await finalizer.IsWindowExpiredAsync(1001, 7));
    }

    [TestMethod]
    public async Task IsWindowExpiredAsync_ExpiredDeadline_ReturnsTrue()
    {
        // A deadline far in the past: the window is open and expired.
        using var handler = new QueueHandler(IntegerResponse(1));
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(FakeEndpoint, http);
        var finalizer = CreateFinalizer(rpc, (_, _) => ValueTask.FromResult(HaltReceipt()));

        Assert.IsTrue(await finalizer.IsWindowExpiredAsync(1001, 7));
    }

    [TestMethod]
    public async Task IsWindowExpiredAsync_OpenWindow_ReturnsFalse()
    {
        // A deadline far in the future: the window is open but not expired.
        using var handler = new QueueHandler(IntegerResponse(int.MaxValue));
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(FakeEndpoint, http);
        var finalizer = CreateFinalizer(rpc, (_, _) => ValueTask.FromResult(HaltReceipt()));

        Assert.IsFalse(await finalizer.IsWindowExpiredAsync(1001, 7));
    }

    [TestMethod]
    public async Task FinalizeIfPastWindowAsync_Expired_SendsFinalizeInvocation()
    {
        using var handler = new QueueHandler(
            IntegerResponse(1), // expiry read: past deadline
            IntegerResponse(0)); // post-failure re-read (race) — not used on HALT
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(FakeEndpoint, http);
        byte[]? submittedScript = null;
        var finalizer = CreateFinalizer(rpc, (script, _) =>
        {
            submittedScript = script.ToArray();
            return ValueTask.FromResult(HaltReceipt());
        });

        await finalizer.FinalizeIfPastWindowAsync(1001, 7);

        Assert.IsNotNull(submittedScript);
        using var expected = new Neo.VM.ScriptBuilder();
        expected.EmitDynamicCall(
            Challenge,
            "finalizeIfPastWindow",
            Neo.SmartContract.CallFlags.All,
            1001u,
            7UL);
        CollectionAssert.AreEqual(expected.ToArray(), submittedScript);
    }

    [TestMethod]
    public async Task FinalizeIfPastWindowAsync_NoWindow_IsNoOp()
    {
        using var handler = new QueueHandler(IntegerResponse(0));
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(FakeEndpoint, http);
        var sent = false;
        var finalizer = CreateFinalizer(rpc, (_, _) =>
        {
            sent = true;
            return ValueTask.FromResult(HaltReceipt());
        });

        await finalizer.FinalizeIfPastWindowAsync(1001, 7);

        Assert.IsFalse(sent, "no open window means nothing to finalize");
    }

    [TestMethod]
    public async Task FinalizeIfPastWindowAsync_StillOpen_Throws()
    {
        using var handler = new QueueHandler(IntegerResponse(int.MaxValue));
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(FakeEndpoint, http);
        var finalizer = CreateFinalizer(rpc, (_, _) => ValueTask.FromResult(HaltReceipt()));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => finalizer.FinalizeIfPastWindowAsync(1001, 7).AsTask());
    }

    [TestMethod]
    public async Task FinalizeIfPastWindowAsync_RaceLostAfterSend_SucceedsSilently()
    {
        // The send faults, but the re-read shows the window vanished: a concurrent
        // finalizer won the race and the outcome is success.
        using var handler = new QueueHandler(
            IntegerResponse(1), // expiry read: past deadline
            IntegerResponse(0)); // re-read: window gone
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(FakeEndpoint, http);
        var finalizer = CreateFinalizer(rpc, (_, _) =>
            ValueTask.FromException<RpcTransactionReceipt>(new InvalidOperationException("send failed")));

        await finalizer.FinalizeIfPastWindowAsync(1001, 7);
    }

    [TestMethod]
    public async Task FinalizeIfPastWindowAsync_NonHaltReceipt_Throws()
    {
        using var handler = new QueueHandler(IntegerResponse(1), IntegerResponse(1));
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(FakeEndpoint, http);
        var finalizer = CreateFinalizer(rpc, (_, _) =>
            ValueTask.FromResult(new RpcTransactionReceipt(UInt256.Zero, "FAULT", null, 0, 0)));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => finalizer.FinalizeIfPastWindowAsync(1001, 7).AsTask());
    }

    [TestMethod]
    public void Constructor_RejectsZeroChallengeHash()
    {
        using var http = new HttpClient(new QueueHandler());
        using var rpc = new JsonRpcClient(FakeEndpoint, http);

        Assert.ThrowsExactly<ArgumentException>(() =>
            CreateFinalizer(rpc, UInt160.Zero, (_, _) => ValueTask.FromResult(HaltReceipt())));
    }

    [TestMethod]
    public async Task IsWindowExpiredAsync_RejectsReservedChainId()
    {
        using var handler = new QueueHandler();
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(FakeEndpoint, http);
        var finalizer = CreateFinalizer(rpc, (_, _) => ValueTask.FromResult(HaltReceipt()));

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => finalizer.IsWindowExpiredAsync(0, 7).AsTask());
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => finalizer.IsWindowExpiredAsync(1001, 0).AsTask());
    }

    private static RpcSettlementWindowFinalizer CreateFinalizer(
        JsonRpcClient rpc,
        RpcSettlementWindowFinalizer.SendInvocationAsync sendInvocation)
        => CreateFinalizer(rpc, Challenge, sendInvocation);

    private static RpcSettlementWindowFinalizer CreateFinalizer(
        JsonRpcClient rpc,
        UInt160 challengeHash,
        RpcSettlementWindowFinalizer.SendInvocationAsync sendInvocation)
        => new(rpc, challengeHash, sendInvocation);

    private static RpcTransactionReceipt HaltReceipt()
        => new(UInt256.Parse("0x" + new string('f', 64)), "HALT", null, 0, 0);

    private static string IntegerResponse(long value)
        => "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"state\":\"HALT\",\"stack\":[{\"type\":\"Integer\",\"value\":\"" +
            value + "\"}]}}";

    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses;

        public QueueHandler(params string[] responses)
            => _responses = new Queue<string>(responses);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = _responses.Count > 0 ? _responses.Dequeue() : "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
