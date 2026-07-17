using System.Net;
using System.Text;
using Neo.Cryptography.ECC;
using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.Executor.ProofWitness;
using Neo.L2.Proving;
using Neo.L2.Proving.Attestation;
using Neo.L2.Settlement.Rpc;
using Neo.Network.P2P.Payloads;
using Neo.Wallets;

namespace Neo.Plugins.L2Settlement.UnitTests;

/// <summary>Host composition tests for <see cref="MultisigLocalHostComposition"/>.</summary>
[TestClass]
public sealed class UT_MultisigLocalHostComposition
{
    [TestMethod]
    public void Open_FromDeployReport_WiresMultisigLocalStack()
    {
        var reportPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "docs", "audit", "testnet-deployment-20260716-live.json"));
        if (!File.Exists(reportPath))
            Assert.Inconclusive($"repo evidence file not found at {reportPath}");

        var chainDir = Path.Combine(
            Path.GetTempPath(),
            "neo-n4-msig-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(chainDir);
        try
        {
            MaterializeMultisigChain(chainDir, reportPath);
            using var http = CanonicalRootHttpClient();
            using var host = MultisigLocalHostComposition.Open(
                chainDir,
                new StubExecutor(),
                SampleSigners(),
                new StubSigner(Account(0x44)),
                rpcHttpClient: http);

            Assert.AreEqual(Path.GetFullPath(chainDir), host.ChainDirectory);
            Assert.IsNotNull(host.Batch);
            Assert.IsNotNull(host.Settlement);
            Assert.IsNotNull(host.Layout.ProofWitness);
            Assert.AreEqual(ProofType.Multisig, host.Prover.Kind);
            Assert.IsInstanceOfType(host.Prover.Prover, typeof(AttestationProver));
            Assert.AreEqual(DAMode.Local, host.DaWriter.Mode);
            Assert.AreEqual(20260716u, host.ForcedInclusion.ChainId);
            Assert.IsNotNull(host.Settlement.ProductionDepositSource);
            Assert.IsNotNull(host.Settlement.ProductionMessageRouter);
            Assert.IsNotNull(host.Settlement.ProductionForcedInclusionSource);
            Assert.IsNotNull(host.Settlement.ProductionForcedInclusionFinalizer);
            Assert.IsNotNull(host.Settlement.ProductionSettlementClient);
            Assert.AreSame(host.ForcedInclusion, host.Settlement.ProductionForcedInclusionSource);
            // Host-level production surfaces (no dig into Settlement internals).
            Assert.AreSame(host.Settlement.ProductionDepositSource, host.DepositSource);
            Assert.AreSame(host.Settlement.ProductionMessageRouter, host.MessageRouter);
            Assert.AreSame(
                host.Settlement.ProductionForcedInclusionFinalizer,
                host.ForcedInclusionFinalizer);
            Assert.AreSame(host.Settlement.ProductionSettlementClient, host.SettlementClient);
            Assert.AreSame(host.Settlement.ProductionTransactionSender, host.TransactionSender);
            Assert.IsFalse(host.IsMetricsHttpListening);
            Assert.AreEqual(0, host.MetricsBoundPort);
            Assert.AreEqual(20260716u, host.Bridge.ChainId);
            Assert.AreSame(
                host.Settlement.ProductionDepositSource,
                host.Bridge.DepositSource);
            // WireProduction installs the L1 inbox + sealed-batch sink on the batcher.
            Assert.AreSame(host.Settlement.ProductionDepositSource, host.Batch.DepositSource);
            Assert.AreSame(host.Settlement.ProductionMessageRouter, host.Batch.MessageRouter);
            Assert.AreSame(host.ForcedInclusion, host.Batch.ForcedInclusionSource);
            Assert.IsTrue(host.Batch.HasSealedBatchSink);
            Assert.IsTrue(host.Settlement.IsProductionWired);
            Assert.IsTrue(host.IsProductionWired);
            Assert.IsNotNull(host.Metrics.Metrics);
            Assert.AreEqual(0, host.Metrics.BoundPort); // HTTP not started by default
            Assert.AreEqual(20260716u, host.RpcStore.ChainId);
            Assert.AreEqual(DAMode.Local, host.RpcStore.DAMode);
        }
        finally
        {
            if (Directory.Exists(chainDir))
                Directory.Delete(chainDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task Open_StartMetricsHttp_ReadyzOk_AndSettleHelpersWork()
    {
        var reportPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "docs", "audit", "testnet-deployment-20260716-live.json"));
        if (!File.Exists(reportPath))
            Assert.Inconclusive($"repo evidence file not found at {reportPath}");

        var chainDir = Path.Combine(
            Path.GetTempPath(),
            "neo-n4-msig-host-metrics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(chainDir);
        try
        {
            MaterializeMultisigChain(chainDir, reportPath);
            using var http = CanonicalRootHttpClient();
            using var host = MultisigLocalHostComposition.Open(
                chainDir,
                new StubExecutor(),
                SampleSigners(),
                new StubSigner(Account(0x44)),
                rpcHttpClient: http,
                startMetricsHttp: true,
                metricsPortOverride: 0);
            Assert.IsTrue(host.IsMetricsHttpListening);
            Assert.IsTrue(host.MetricsBoundPort > 0);
            Assert.AreEqual(host.Metrics.BoundPort, host.MetricsBoundPort);
            Assert.IsTrue(host.Batch.HasSealedBatchSink);
            Assert.IsTrue(host.Settlement.IsProductionWired);
            Assert.IsNotNull(host.TransactionSender);
            Assert.AreSame(host.Settlement.ProductionTransactionSender, host.TransactionSender);

            using var client = new HttpClient();
            var ready = await client.GetAsync($"http://127.0.0.1:{host.MetricsBoundPort}/readyz");
            Assert.AreEqual(System.Net.HttpStatusCode.OK, ready.StatusCode);
            var health = await client.GetAsync($"http://127.0.0.1:{host.MetricsBoundPort}/healthz");
            Assert.AreEqual(System.Net.HttpStatusCode.OK, health.StatusCode);

            Assert.AreEqual(0, await host.GetPendingCountAsync());
            await host.ReconcileAsync();
            await host.SubmitNextAsync();
            Assert.IsTrue(host.IsProductionWired);

            var metricsBody = await client.GetStringAsync(
                $"http://127.0.0.1:{host.MetricsBoundPort}/metrics");
            Assert.IsFalse(string.IsNullOrWhiteSpace(metricsBody));
            // Prometheus exposition always includes TYPE/HELP or at least scrapeable text.
            StringAssert.Contains(metricsBody, "#");
        }
        finally
        {
            if (Directory.Exists(chainDir))
                Directory.Delete(chainDir, recursive: true);
        }
    }

    [TestMethod]
    public void Open_DeferredStartMetricsHttp_And_CreateRpcPlugin()
    {
        var reportPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "docs", "audit", "testnet-deployment-20260716-live.json"));
        if (!File.Exists(reportPath))
            Assert.Inconclusive($"repo evidence file not found at {reportPath}");

        var chainDir = Path.Combine(
            Path.GetTempPath(),
            "neo-n4-msig-host-deferred-metrics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(chainDir);
        try
        {
            MaterializeMultisigChain(chainDir, reportPath);
            using var http = CanonicalRootHttpClient();
            using var host = MultisigLocalHostComposition.Open(
                chainDir,
                new StubExecutor(),
                SampleSigners(),
                new StubSigner(Account(0x44)),
                rpcHttpClient: http);
            Assert.AreEqual(0, host.MetricsBoundPort);
            Assert.IsFalse(host.IsMetricsHttpListening);

            host.StartMetricsHttp(portOverride: 0);
            Assert.IsTrue(host.IsMetricsHttpListening);
            Assert.IsTrue(host.MetricsBoundPort > 0);
            Assert.IsTrue(host.IsProductionWired);

            var rpcPlugin = host.CreateRpcPlugin();
            Assert.IsNotNull(rpcPlugin);
            Assert.IsFalse(rpcPlugin.IsRegistered(894710606)); // no NeoSystem registration yet
            // Metrics already configured on the plugin; second WithMetrics would be ok only if
            // no networks registered — CreateRpcPlugin owns that one-shot wiring.
        }
        finally
        {
            if (Directory.Exists(chainDir))
                Directory.Delete(chainDir, recursive: true);
        }
    }

    [TestMethod]
    public void Open_ZkSettlementConfig_FailsClosed()
    {
        var reportPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "docs", "audit", "testnet-deployment-20260716-live.json"));
        if (!File.Exists(reportPath))
            Assert.Inconclusive($"repo evidence file not found at {reportPath}");

        var chainDir = Path.Combine(
            Path.GetTempPath(),
            "neo-n4-msig-host-zk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(chainDir);
        try
        {
            File.WriteAllText(Path.Combine(chainDir, "chain.config.json"), """
                {
                  "chainId": 20260716,
                  "proofType": "Zk",
                  "securityLevel": "Validity",
                  "daMode": "L1",
                  "sequencerModel": "DbftCommittee",
                  "exitModel": "Permissionless",
                  "gatewayEnabled": true,
                  "permissionlessExit": true,
                  "validators": []
                }
                """);
            NeoHubDeployReport.Load(reportPath).WriteOperatorArtifacts(chainDir);
            File.WriteAllText(Path.Combine(chainDir, "genesis-manifest.json"), """
                { "chainId": 20260716, "initialStateRoot": "0x1111111111111111111111111111111111111111111111111111111111111111" }
                """);

            var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
                MultisigLocalHostComposition.Open(
                    chainDir,
                    new StubExecutor(),
                    SampleSigners(),
                    new StubSigner(Account(0x55))));
            StringAssert.Contains(ex.Message, "Multisig");
            StringAssert.Contains(ex.Message, "Zk");
        }
        finally
        {
            if (Directory.Exists(chainDir))
                Directory.Delete(chainDir, recursive: true);
        }
    }

    private static void MaterializeMultisigChain(string chainDir, string reportPath)
    {
        var validatorA = new KeyPair(Enumerable.Range(3, 32).Select(i => (byte)i).ToArray()).PublicKey;
        var validatorB = new KeyPair(Enumerable.Range(4, 32).Select(i => (byte)i).ToArray()).PublicKey;
        var hexA = Convert.ToHexString(validatorA.EncodePoint(true)).ToLowerInvariant();
        var hexB = Convert.ToHexString(validatorB.EncodePoint(true)).ToLowerInvariant();
        File.WriteAllText(Path.Combine(chainDir, "chain.config.json"), $$"""
            {
              "chainId": 20260716,
              "proofType": "Multisig",
              "securityLevel": "Optimistic",
              "daMode": "Local",
              "sequencerModel": "DbftCommittee",
              "exitModel": "Permissionless",
              "gatewayEnabled": true,
              "permissionlessExit": true,
              "validators": [ "{{hexA}}", "{{hexB}}" ]
            }
            """);
        NeoHubDeployReport.Load(reportPath).WriteOperatorArtifacts(chainDir);
        RewriteProofType(chainDir, "Neo.Plugins.L2Settlement", (byte)ProofType.Multisig);
        RewriteProofType(chainDir, "Neo.Plugins.L2Prover", (byte)ProofType.Multisig);
        File.WriteAllText(Path.Combine(chainDir, "genesis-manifest.json"), """
            { "chainId": 20260716, "initialStateRoot": "0x1111111111111111111111111111111111111111111111111111111111111111" }
            """);
    }

    private static void RewriteProofType(string chainDir, string pluginFolder, byte proofType)
    {
        var path = Path.Combine(chainDir, "Plugins", pluginFolder, "config.json");
        var text = File.ReadAllText(path);
        var rewritten = System.Text.RegularExpressions.Regex.Replace(
            text,
            "\"ProofType\"\\s*:\\s*\\d+",
            $"\"ProofType\": {proofType}");
        File.WriteAllText(path, rewritten);
    }

    private static InMemorySignerSet SampleSigners()
    {
        var keys = Enumerable.Range(1, 2).Select(i =>
        {
            var priv = new byte[32];
            for (var j = 0; j < 32; j++) priv[j] = (byte)(i + j);
            return (ECCurve.Secp256r1.G * priv, priv);
        }).ToList();
        return new InMemorySignerSet(keys);
    }

    private static UInt160 Account(byte fill) =>
        UInt160.Parse("0x" + Convert.ToHexString(Enumerable.Repeat(fill, 20).ToArray()).ToLowerInvariant());

    private static UInt256 Root(byte fill) =>
        new(Enumerable.Repeat(fill, 32).ToArray());

    private static HttpClient CanonicalRootHttpClient()
    {
        return new HttpClient(new MockHandler(async (request, _) =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            if (body.Contains("getversion", StringComparison.Ordinal))
            {
                return Json(
                    """{"jsonrpc":"2.0","id":1,"result":{"protocol":{"network":894710606,"addressversion":53}}}""");
            }
            if (body.Contains("getblockcount", StringComparison.Ordinal))
            {
                return Json("""{"jsonrpc":"2.0","id":1,"result":100}""");
            }
            // Canonical state root for committee / profile paths.
            if (body.Contains("invokefunction", StringComparison.Ordinal)
                || body.Contains("invokescript", StringComparison.Ordinal))
            {
                var root = Convert.ToBase64String(Root(0x11).GetSpan().ToArray());
                return Json(
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"state\":\"HALT\",\"gasconsumed\":\"0\","
                    + "\"stack\":[{\"type\":\"ByteString\",\"value\":\"" + root + "\"}]}}");
            }
            return Json("""{"jsonrpc":"2.0","id":1,"result":null}""");
        }));
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private sealed class MockHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => handler(request, cancellationToken);
    }

    private sealed class StubExecutor : IProofWitnessBatchExecutor
    {
        public ValueTask<BatchExecutionResult> ApplyBatchAsync(
            BatchExecutionRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("stub executor does not execute batches");

        public ValueTask<ProofWitnessExecutionResult> ApplyBatchWithWitnessAsync(
            SealedBatch batch,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("stub executor does not execute batches");
    }

    private sealed class StubSigner(UInt160 account) : INeoTransactionSigner
    {
        public UInt160 Account { get; } = account;
        public WitnessScope Scope => WitnessScope.CalledByEntry;

        public Witness CreatePlaceholderWitness()
            => new()
            {
                InvocationScript = Array.Empty<byte>(),
                VerificationScript = Array.Empty<byte>(),
            };

        public ValueTask<Witness> SignAsync(
            Transaction tx, uint network, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("stub signer does not sign");
    }
}
