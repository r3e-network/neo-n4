using System.Net;
using System.Text;
using Neo.Json;
using Neo.Stack.Cli.Commands;
using Neo.Wallets;

namespace Neo.Stack.Cli.UnitTests;

[TestClass]
[DoNotParallelize]
public class UT_OperatorTransactionBroadcaster
{
    private const uint Network = 894_710_606;

    [TestMethod]
    public async Task Broadcast_ImportsEnvironmentSignerAndConfirmsTransaction()
    {
        var environmentVariable = $"NEO_N4_TEST_WIF_{Guid.NewGuid():N}";
        var key = new KeyPair(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());
        var wif = key.Export();
        key.PrivateKey.AsSpan().Clear();
        Environment.SetEnvironmentVariable(environmentVariable, wif);
        try
        {
            var handler = new RpcHandler(Network);
            using var http = new HttpClient(handler);

            var result = await OperatorTransactionBroadcaster.BroadcastAsync(
                new[]
                {
                    "--rpc", "http://localhost:10332",
                    "--expected-network", Network.ToString(),
                    "--wif-env", environmentVariable,
                },
                new byte[] { 0x40 },
                "test operation",
                http);

            Assert.AreEqual(0, result);
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
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentVariable, null);
        }
    }

    [TestMethod]
    public async Task Broadcast_PrefixedModeRequiresPrefixedRpcOption()
    {
        var result = await OperatorTransactionBroadcaster.BroadcastAsync(
            new[] { "--rpc", "http://localhost:10332" },
            new byte[] { 0x40 },
            "test operation",
            optionPrefix: "l1");

        Assert.AreEqual(10, result);
    }

    private sealed class RpcHandler(uint network) : HttpMessageHandler
    {
        public List<string> Methods { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var envelope = (JObject)JToken.Parse(body)!;
            var method = envelope["method"]!.AsString();
            Methods.Add(method);
            var response = new JObject();
            response["jsonrpc"] = "2.0";
            response["id"] = envelope["id"];
            response["result"] = Result(method);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response.ToString(), Encoding.UTF8, "application/json"),
            };
        }

        private JToken Result(string method)
        {
            return method switch
            {
                "getversion" => Version(),
                "invokescript" => InvokeResult(),
                "getblockcount" => 100,
                "calculatenetworkfee" => NetworkFee(),
                "sendrawtransaction" => true,
                "getapplicationlog" => ApplicationLog(),
                _ => throw new InvalidOperationException($"Unexpected RPC method {method}"),
            };
        }

        private JObject Version()
        {
            var protocol = new JObject();
            protocol["network"] = network;
            var result = new JObject();
            result["protocol"] = protocol;
            return result;
        }

        private static JObject InvokeResult()
        {
            var result = new JObject();
            result["state"] = "HALT";
            result["gasconsumed"] = "1000000";
            return result;
        }

        private static JObject NetworkFee()
        {
            var result = new JObject();
            result["networkfee"] = "1000";
            return result;
        }

        private static JObject ApplicationLog()
        {
            var execution = new JObject();
            execution["vmstate"] = "HALT";
            var result = new JObject();
            result["executions"] = new JArray { execution };
            return result;
        }
    }
}
