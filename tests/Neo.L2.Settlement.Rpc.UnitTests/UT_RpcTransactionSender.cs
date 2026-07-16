using System.Net;
using System.Text;
using Neo.Extensions;
using Neo.Json;
using Neo.Network.P2P.Payloads;
using Neo.Wallets;

namespace Neo.L2.Settlement.Rpc.UnitTests;

[TestClass]
public class UT_RpcTransactionSender
{
    private const uint Network = 894_710_606;
    private static readonly Uri Endpoint = new("http://localhost:10332");

    [TestMethod]
    public void NeoGas_ParsesCurrentAndLegacyRpcFormats()
    {
        Assert.AreEqual(42L, NeoGas.ParseRpcValue("42"));
        Assert.AreEqual(10_000_000L, NeoGas.ParseRpcValue("0.1"));
        Assert.ThrowsExactly<FormatException>(() => NeoGas.ParseRpcValue("0.000000001"));
        Assert.ThrowsExactly<FormatException>(() => NeoGas.ParseRpcValue("-1"));
    }

    [TestMethod]
    public async Task SendInvocation_BuildsSignsBroadcastsAndConfirms()
    {
        var handler = new ScriptedRpcHandler(Network);
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(Endpoint, http);
        using var signer = CreateSigner();
        var sender = CreateSender(rpc, signer);

        var receipt = await sender.SendInvocationAsync(new byte[] { 0x40 });

        Assert.AreEqual("HALT", receipt.VmState);
        Assert.AreEqual(12_000_000L, receipt.SystemFee);
        Assert.AreEqual(1_000_000L, receipt.NetworkFee);
        CollectionAssert.AreEqual(
            new[]
            {
                "getversion",
                "invokescript",
                "getblockcount",
                "calculatenetworkfee",
                "getversion",
                "sendrawtransaction",
                "getapplicationlog",
            },
            handler.Methods);
        Assert.AreEqual(signer.Account.ToString(),
            ((JObject)((JArray)handler.Requests[1]["params"]!)[1]![0]!)["account"]!.AsString());
    }

    [TestMethod]
    public async Task BuildSignedInvocation_RejectsWrongNetworkBeforePreflight()
    {
        var handler = new ScriptedRpcHandler(Network + 1);
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(Endpoint, http);
        using var signer = CreateSigner();
        var sender = CreateSender(rpc, signer);

        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await sender.BuildSignedInvocationAsync(new byte[] { 0x40 }));

        StringAssert.Contains(error.Message, "network mismatch");
        CollectionAssert.AreEqual(new[] { "getversion" }, handler.Methods);
    }

    [TestMethod]
    public async Task BuildSignedInvocation_RejectsFaultedPreflight()
    {
        var handler = new ScriptedRpcHandler(Network) { PreflightState = "FAULT" };
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(Endpoint, http);
        using var signer = CreateSigner();
        var sender = CreateSender(rpc, signer);

        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await sender.BuildSignedInvocationAsync(new byte[] { 0x40 }));

        StringAssert.Contains(error.Message, "preflight faulted");
        CollectionAssert.AreEqual(new[] { "getversion", "invokescript" }, handler.Methods);
    }

    [TestMethod]
    public async Task BuildSignedInvocation_FailsClosedWhenNetworkFeeCannotBeCalculated()
    {
        var handler = new ScriptedRpcHandler(Network) { FailNetworkFee = true };
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(Endpoint, http);
        using var signer = CreateSigner();
        var sender = CreateSender(rpc, signer);

        var error = await Assert.ThrowsExactlyAsync<JsonRpcException>(async () =>
            await sender.BuildSignedInvocationAsync(new byte[] { 0x40 }));

        Assert.AreEqual(-32603, error.Code);
        Assert.IsFalse(handler.Methods.Contains("sendrawtransaction"));
    }

    [TestMethod]
    public async Task BroadcastAndWait_RetriesPendingApplicationLog()
    {
        var handler = new ScriptedRpcHandler(Network) { PendingApplicationLogs = 1 };
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(Endpoint, http);
        using var signer = CreateSigner();
        var sender = CreateSender(rpc, signer);

        var receipt = await sender.SendInvocationAsync(new byte[] { 0x40 });

        Assert.AreEqual("HALT", receipt.VmState);
        Assert.AreEqual(2, handler.Methods.Count(method => method == "getapplicationlog"));
    }

    [TestMethod]
    public async Task BroadcastAndWait_AlreadyKnownTransactionContinuesToConfirmation()
    {
        var handler = new ScriptedRpcHandler(Network)
        {
            BroadcastErrorMessage = "Transaction already exists in the memory pool",
        };
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(Endpoint, http);
        using var signer = CreateSigner();
        var sender = CreateSender(rpc, signer);

        var receipt = await sender.SendInvocationAsync(new byte[] { 0x40 });

        Assert.AreEqual("HALT", receipt.VmState);
        Assert.IsTrue(handler.Methods.Contains("getapplicationlog"));
    }

    [TestMethod]
    public async Task BroadcastAndWait_RejectsFaultedConfirmedTransaction()
    {
        var handler = new ScriptedRpcHandler(Network) { ExecutionState = "FAULT" };
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(Endpoint, http);
        using var signer = CreateSigner();
        var sender = CreateSender(rpc, signer);

        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await sender.SendInvocationAsync(new byte[] { 0x40 }));

        StringAssert.Contains(error.Message, "faulted");
        StringAssert.Contains(error.Message, "test fault");
    }

    [TestMethod]
    public async Task BroadcastAndWait_RejectsUnexpectedBroadcastResponse()
    {
        var handler = new ScriptedRpcHandler(Network) { BroadcastResult = "unexpected" };
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(Endpoint, http);
        using var signer = CreateSigner();
        var sender = CreateSender(rpc, signer);

        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await sender.SendInvocationAsync(new byte[] { 0x40 }));

        StringAssert.Contains(error.Message, "unexpected result");
        Assert.IsFalse(handler.Methods.Contains("getapplicationlog"));
    }

    [TestMethod]
    public async Task BroadcastAndWait_AcceptsNeoRpcServerHashObject()
    {
        // Production Neo RpcServer returns {"hash":"<UInt256>"}, not a bare boolean.
        var handler = new ScriptedRpcHandler(Network) { UseNeoHashObjectBroadcastResult = true };
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(Endpoint, http);
        using var signer = CreateSigner();
        var sender = CreateSender(rpc, signer);

        var receipt = await sender.SendInvocationAsync(new byte[] { 0x40 });

        Assert.AreEqual("HALT", receipt.VmState);
        Assert.IsTrue(handler.Methods.Contains("sendrawtransaction"));
        Assert.IsTrue(handler.Methods.Contains("getapplicationlog"));
    }

    [TestMethod]
    public void Constructor_RejectsUnsafeOptions()
    {
        using var http = new HttpClient(new ScriptedRpcHandler(Network));
        using var rpc = new JsonRpcClient(Endpoint, http);
        using var signer = CreateSigner();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RpcTransactionSender(
            rpc,
            signer,
            new RpcTransactionSenderOptions
            {
                ExpectedNetwork = Network,
                ValidUntilBlockDelta = 0,
            }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RpcTransactionSender(
            rpc,
            signer,
            new RpcTransactionSenderOptions
            {
                ExpectedNetwork = Network,
                SystemFeeMarginBasisPoints = 100_001,
            }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RpcTransactionSender(
            rpc,
            signer,
            new RpcTransactionSenderOptions
            {
                ExpectedNetwork = Network,
                MinimumSystemFeeMargin = -1,
            }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RpcTransactionSender(
            rpc,
            signer,
            new RpcTransactionSenderOptions
            {
                ExpectedNetwork = Network,
                ConfirmationTimeout = TimeSpan.Zero,
            }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RpcTransactionSender(
            rpc,
            signer,
            new RpcTransactionSenderOptions
            {
                ExpectedNetwork = Network,
                ConfirmationPollInterval = TimeSpan.Zero,
            }));
    }

    private static LocalKeyTransactionSigner CreateSigner()
    {
        var privateKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        return new LocalKeyTransactionSigner(new KeyPair(privateKey));
    }

    private static RpcTransactionSender CreateSender(JsonRpcClient rpc, INeoTransactionSigner signer)
    {
        return new RpcTransactionSender(
            rpc,
            signer,
            new RpcTransactionSenderOptions
            {
                ExpectedNetwork = Network,
                ConfirmationTimeout = TimeSpan.FromSeconds(1),
                ConfirmationPollInterval = TimeSpan.FromMilliseconds(1),
            });
    }

    private sealed class ScriptedRpcHandler(uint network) : HttpMessageHandler
    {
        public List<string> Methods { get; } = [];
        public List<JObject> Requests { get; } = [];
        public string PreflightState { get; init; } = "HALT";
        public string ExecutionState { get; init; } = "HALT";
        public bool FailNetworkFee { get; init; }
        public int PendingApplicationLogs { get; set; }
        public JToken BroadcastResult { get; init; } = true;
        public bool UseNeoHashObjectBroadcastResult { get; init; }
        public string? BroadcastErrorMessage { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var envelope = (JObject)JToken.Parse(body)!;
            var method = envelope["method"]!.AsString();
            Methods.Add(method);
            Requests.Add(envelope);

            var response = new JObject();
            response["jsonrpc"] = "2.0";
            response["id"] = envelope["id"];
            if (method == "calculatenetworkfee" && FailNetworkFee)
            {
                var error = new JObject();
                error["code"] = -32603;
                error["message"] = "fee service unavailable";
                response["error"] = error;
            }
            else if (method == "sendrawtransaction" && BroadcastErrorMessage is not null)
            {
                var error = new JObject();
                error["code"] = -501;
                error["message"] = BroadcastErrorMessage;
                response["error"] = error;
            }
            else if (method == "getapplicationlog" && PendingApplicationLogs-- > 0)
            {
                var error = new JObject();
                error["code"] = -100;
                error["message"] = "Unknown transaction";
                response["error"] = error;
            }
            else
            {
                response["result"] = Result(method, envelope);
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response.ToString(), Encoding.UTF8, "application/json"),
            };
        }

        private JToken Result(string method, JObject envelope)
        {
            return method switch
            {
                "getversion" => Version(),
                "invokescript" => InvokeResult(),
                "getblockcount" => 100,
                "calculatenetworkfee" => NetworkFee(),
                "sendrawtransaction" => SendRawTransactionResult(envelope),
                "getapplicationlog" => ApplicationLog(),
                _ => throw new InvalidOperationException($"Unexpected RPC method {method}"),
            };
        }

        private JToken SendRawTransactionResult(JObject envelope)
        {
            if (!UseNeoHashObjectBroadcastResult)
                return BroadcastResult;
            var base64 = ((JArray)envelope["params"]!)[0]!.AsString();
            var tx = Convert.FromBase64String(base64).AsSerializable<Transaction>();
            var obj = new JObject();
            obj["hash"] = tx.Hash.ToString();
            return obj;
        }

        private JObject Version()
        {
            var protocol = new JObject();
            protocol["network"] = network;
            var result = new JObject();
            result["protocol"] = protocol;
            return result;
        }

        private JObject InvokeResult()
        {
            var result = new JObject();
            result["state"] = PreflightState;
            result["gasconsumed"] = "0.10000000";
            result["exception"] = PreflightState == "HALT" ? null : "test preflight fault";
            return result;
        }

        private static JObject NetworkFee()
        {
            var result = new JObject();
            result["networkfee"] = "0.01000000";
            return result;
        }

        private JObject ApplicationLog()
        {
            var execution = new JObject();
            execution["vmstate"] = ExecutionState;
            execution["exception"] = ExecutionState == "HALT" ? null : "test fault";
            var result = new JObject();
            result["executions"] = new JArray { execution };
            return result;
        }
    }
}
