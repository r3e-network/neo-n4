using System.Net;
using System.Text;
using System.Text.Json;
using Neo.Cryptography;
using Neo.Extensions.IO;
using Neo.Extensions.VM;
using Neo.Json;
using Neo.Network.P2P.Payloads;
using Neo.Stack.Cli.Commands;
using Neo.SmartContract;
using Neo.VM;
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

    [TestMethod]
    public async Task Broadcast_WitnessScopeGlobal_AcceptedForNestedNep17Paths()
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
                    "--witness-scope", "Global",
                },
                new byte[] { 0x40 },
                "test operation",
                http);

            Assert.AreEqual(0, result);
            Assert.AreEqual("Global", handler.LastInvokeSignerScope);
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentVariable, null);
        }
    }

    [TestMethod]
    public async Task Broadcast_WitnessScopeInvalid_FailsClosed()
    {
        var environmentVariable = $"NEO_N4_TEST_WIF_{Guid.NewGuid():N}";
        var key = new KeyPair(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());
        var wif = key.Export();
        key.PrivateKey.AsSpan().Clear();
        Environment.SetEnvironmentVariable(environmentVariable, wif);
        try
        {
            var result = await OperatorTransactionBroadcaster.BroadcastAsync(
                new[]
                {
                    "--rpc", "http://localhost:10332",
                    "--expected-network", Network.ToString(),
                    "--wif-env", environmentVariable,
                    "--witness-scope", "CustomContracts",
                },
                new byte[] { 0x40 },
                "test operation");

            Assert.AreEqual(12, result);
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentVariable, null);
        }
    }

    [TestMethod]
    public async Task Broadcast_CallerCancellationReturns130()
    {
        var environmentVariable = $"NEO_N4_TEST_WIF_{Guid.NewGuid():N}";
        var key = new KeyPair(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());
        var wif = key.Export();
        key.PrivateKey.AsSpan().Clear();
        Environment.SetEnvironmentVariable(environmentVariable, wif);
        try
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            using var http = new HttpClient(new RpcHandler(Network));

            var result = await OperatorTransactionBroadcaster.BroadcastAsync(
                new[]
                {
                    "--rpc", "http://localhost:10332",
                    "--expected-network", Network.ToString(),
                    "--wif-env", environmentVariable,
                },
                new byte[] { 0x40 },
                "test operation",
                http,
                cancellationToken: cancellation.Token);

            Assert.AreEqual(130, result);
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentVariable, null);
        }
    }

    [TestMethod]
    public async Task Broadcast_CommandSignerSignsAndConfirmsWithoutWif()
    {
        var key = new KeyPair(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());
        try
        {
            var verificationScript = Contract.CreateSignatureRedeemScript(key.PublicKey);
            var account = verificationScript.ToScriptHash();
            var placeholderInvocationScript = CreateSignatureInvocationScript(new byte[64]);
            var runner = new SigningCommandRunner(key);
            var handler = new RpcHandler(Network);
            using var http = new HttpClient(handler);

            var result = await OperatorTransactionBroadcaster.BroadcastAsync(
                new[]
                {
                    "--rpc", "http://localhost:10332",
                    "--expected-network", Network.ToString(),
                    "--signer-command", Environment.ProcessPath ?? throw new InvalidOperationException("Missing test host path."),
                    "--signer-account", account.ToString(),
                    "--signer-verification-script", Convert.ToHexString(verificationScript),
                    "--signer-placeholder-invocation-script", Convert.ToHexString(placeholderInvocationScript),
                },
                new byte[] { 0x40 },
                "test operation",
                http,
                externalSignerCommandRunner: runner);

            Assert.AreEqual(0, result);
            var request = runner.Request ?? throw new AssertFailedException("Signer command did not receive a request.");
            Assert.AreEqual(Network, request.Network);
            Assert.AreEqual(account, request.Account);
            Assert.AreEqual(WitnessScope.CalledByEntry, request.Scope);
            Assert.IsTrue(request.SignData.Length > 0);
            Assert.IsTrue(request.Transaction.Length > 0);
            Assert.IsTrue(runner.ProducedValidSignature);
            CollectionAssert.Contains(handler.Methods, "sendrawtransaction");
        }
        finally
        {
            key.PrivateKey.AsSpan().Clear();
        }
    }

    [TestMethod]
    public async Task Broadcast_CommandSignerRejectsEmptyResponseBeforeBroadcast()
    {
        var key = new KeyPair(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());
        try
        {
            var verificationScript = Contract.CreateSignatureRedeemScript(key.PublicKey);
            var account = verificationScript.ToScriptHash();
            var placeholderInvocationScript = CreateSignatureInvocationScript(new byte[64]);
            var handler = new RpcHandler(Network);
            using var http = new HttpClient(handler);

            var result = await OperatorTransactionBroadcaster.BroadcastAsync(
                new[]
                {
                    "--rpc", "http://localhost:10332",
                    "--expected-network", Network.ToString(),
                    "--signer-command", Environment.ProcessPath ?? throw new InvalidOperationException("Missing test host path."),
                    "--signer-account", account.ToString(),
                    "--signer-verification-script", Convert.ToHexString(verificationScript),
                    "--signer-placeholder-invocation-script", Convert.ToHexString(placeholderInvocationScript),
                },
                new byte[] { 0x40 },
                "test operation",
                http,
                externalSignerCommandRunner: EmptyCommandRunner.Instance);

            Assert.AreEqual(13, result);
            CollectionAssert.DoesNotContain(handler.Methods, "sendrawtransaction");
        }
        finally
        {
            key.PrivateKey.AsSpan().Clear();
        }
    }

    [TestMethod]
    public async Task Broadcast_CommandSignerRejectsResponseWithDifferentFeeEstimationShape()
    {
        var key = new KeyPair(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());
        try
        {
            var verificationScript = Contract.CreateSignatureRedeemScript(key.PublicKey);
            var account = verificationScript.ToScriptHash();
            var placeholderInvocationScript = CreateSignatureInvocationScript(new byte[64]);
            var handler = new RpcHandler(Network);
            using var http = new HttpClient(handler);

            var result = await OperatorTransactionBroadcaster.BroadcastAsync(
                new[]
                {
                    "--rpc", "http://localhost:10332",
                    "--expected-network", Network.ToString(),
                    "--signer-command", Environment.ProcessPath ?? throw new InvalidOperationException("Missing test host path."),
                    "--signer-account", account.ToString(),
                    "--signer-verification-script", Convert.ToHexString(verificationScript),
                    "--signer-placeholder-invocation-script", Convert.ToHexString(placeholderInvocationScript),
                },
                new byte[] { 0x40 },
                "test operation",
                http,
                externalSignerCommandRunner: ShortCommandRunner.Instance);

            Assert.AreEqual(13, result);
            CollectionAssert.DoesNotContain(handler.Methods, "sendrawtransaction");
        }
        finally
        {
            key.PrivateKey.AsSpan().Clear();
        }
    }

    [TestMethod]
    public async Task Broadcast_CommandSignerRejectsExplicitWifSelection()
    {
        var handler = new RpcHandler(Network);
        using var http = new HttpClient(handler);

        var result = await OperatorTransactionBroadcaster.BroadcastAsync(
            new[]
            {
                "--rpc", "http://localhost:10332",
                "--expected-network", Network.ToString(),
                "--signer-command", Environment.ProcessPath ?? throw new InvalidOperationException("Missing test host path."),
                "--wif-env", "TEST_OPERATOR_WIF",
            },
            new byte[] { 0x40 },
            "test operation",
            http);

        Assert.AreEqual(12, result);
        CollectionAssert.DoesNotContain(handler.Methods, "sendrawtransaction");
    }

    [TestMethod]
    public void ExternalSignerCommandRequest_UsesDocumentedCamelCaseJson()
    {
        var request = new ExternalSignerCommandRequest(
            Network,
            UInt160.Parse("0x0123456789abcdef0123456789abcdef01234567"),
            WitnessScope.CalledByEntry,
            new byte[] { 0x01, 0x02 },
            new byte[] { 0x03, 0x04 });

        using var document = JsonDocument.Parse(SystemExternalSignerCommandRunner.SerializeRequest(request));

        Assert.IsTrue(document.RootElement.TryGetProperty("version", out var version));
        Assert.AreEqual(1, version.GetInt32());
        Assert.IsTrue(document.RootElement.TryGetProperty("network", out var network));
        Assert.AreEqual(Network, network.GetUInt32());
        Assert.IsTrue(document.RootElement.TryGetProperty("signData", out var signData));
        Assert.AreEqual(Convert.ToBase64String(request.SignData.Span), signData.GetString());
        Assert.IsFalse(document.RootElement.TryGetProperty("Version", out _));
    }

    private static byte[] CreateSignatureInvocationScript(byte[] signature)
    {
        using var builder = new ScriptBuilder();
        builder.EmitPush(signature);
        return builder.ToArray();
    }

    private sealed class SigningCommandRunner(KeyPair key) : IExternalSignerCommandRunner
    {
        public ExternalSignerCommandRequest? Request { get; private set; }

        public bool ProducedValidSignature { get; private set; }

        public Task<ReadOnlyMemory<byte>> SignAsync(
            ExternalSignerCommand command,
            ExternalSignerCommandRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Request = request;
            var signData = request.SignData.ToArray();
            var signature = Crypto.Sign(signData, key);
            ProducedValidSignature = Crypto.VerifySignature(signData, signature, key.PublicKey);
            return Task.FromResult<ReadOnlyMemory<byte>>(
                CreateSignatureInvocationScript(signature));
        }
    }

    private sealed class EmptyCommandRunner : IExternalSignerCommandRunner
    {
        public static EmptyCommandRunner Instance { get; } = new();

        public Task<ReadOnlyMemory<byte>> SignAsync(
            ExternalSignerCommand command,
            ExternalSignerCommandRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ReadOnlyMemory<byte>.Empty);
        }
    }

    private sealed class ShortCommandRunner : IExternalSignerCommandRunner
    {
        public static ShortCommandRunner Instance { get; } = new();

        public Task<ReadOnlyMemory<byte>> SignAsync(
            ExternalSignerCommand command,
            ExternalSignerCommandRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<ReadOnlyMemory<byte>>(new byte[] { (byte)OpCode.NOP });
        }
    }

    private sealed class RpcHandler(uint network) : HttpMessageHandler
    {
        public List<string> Methods { get; } = [];

        public string? LastInvokeSignerScope { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var envelope = (JObject)JToken.Parse(body)!;
            var method = envelope["method"]!.AsString();
            Methods.Add(method);
            if (method == "invokescript"
                && envelope["params"] is JArray invokeParams
                && invokeParams.Count > 1
                && invokeParams[1] is JArray signers
                && signers.Count > 0
                && signers[0] is JObject signer)
            {
                LastInvokeSignerScope = signer["scopes"]?.AsString();
            }
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
