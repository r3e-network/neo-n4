using System.Net;
using System.Security.Cryptography;
using System.Text;
using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.Executor;
using Neo.L2.Executor.ProofWitness;
using Neo.L2.Persistence;
using Neo.L2.Proving;
using Neo.L2.Proving.RiscVZk;
using Neo.L2.Settlement.Rpc;
using Neo.Network.P2P.Payloads;
using Neo.Wallets;

namespace Neo.Plugins.L2Settlement.UnitTests;

/// <summary>Host composition tests for <see cref="ZkLocalHostComposition"/>.</summary>
[TestClass]
public sealed class UT_ZkLocalHostComposition
{
    [TestMethod]
    public void Open_FromDeployReport_WiresZkStack()
    {
        var reportPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "docs", "audit", "testnet-deployment-20260716-live.json"));
        if (!File.Exists(reportPath))
            Assert.Inconclusive($"repo evidence file not found at {reportPath}");

        var chainDir = Path.Combine(
            Path.GetTempPath(),
            "neo-n4-zk-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(chainDir);
        var exeRoot = Path.Combine(
            Path.GetTempPath(),
            "neo-n4-zk-exec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(exeRoot);
        try
        {
            var genesisRoot = MaterializeZkChain(chainDir, reportPath);
            var exePath = Path.Combine(exeRoot, "neo-zkvm-executor");
            File.WriteAllBytes(exePath, [0x01, 0x02, 0x03]);
            var sha = SHA256.HashData(File.ReadAllBytes(exePath));
            var vk = Hash(0x77);

            using var http = CanonicalRootHttpClient(genesisRoot);
            var da = new StubProductionDaWriter();
            using var host = ZkLocalHostComposition.Open(
                chainDir,
                exePath,
                sha,
                vk,
                da,
                new StubSigner(Account(0x44)),
                rpcHttpClient: http);

            Assert.AreEqual(Path.GetFullPath(chainDir), host.ChainDirectory);
            Assert.IsNotNull(host.Batch);
            Assert.IsNotNull(host.Settlement);
            Assert.IsNotNull(host.Layout.ProofWitness);
            Assert.AreEqual(ProofType.Zk, host.Prover.Kind);
            Assert.IsInstanceOfType(host.Prover.Prover, typeof(Sp1BatchProofProver));
            Assert.AreSame(host.Stack.Prover, host.Prover.Prover);
            Assert.IsInstanceOfType(host.Stack.Executor, typeof(Sp1StatefulBatchExecutor));
            Assert.AreEqual(ProofType.Zk, host.Stack.Profile.ProofType);
            Assert.AreEqual(vk, host.Stack.Profile.VerificationKeyId);
            Assert.AreEqual(genesisRoot, host.Stack.Profile.GenesisStateRoot);
            Assert.AreEqual(20260716u, host.ForcedInclusion.ChainId);
            Assert.IsNotNull(host.Settlement.ProductionDepositSource);
            Assert.IsNotNull(host.Settlement.ProductionMessageRouter);
            Assert.IsNotNull(host.Settlement.ProductionForcedInclusionSource);
            Assert.IsNotNull(host.Settlement.ProductionForcedInclusionFinalizer);
            Assert.IsNotNull(host.Settlement.ProductionSettlementClient);
            Assert.AreSame(host.ForcedInclusion, host.Settlement.ProductionForcedInclusionSource);
            Assert.AreSame(host.Settlement.ProductionDepositSource, host.DepositSource);
            Assert.AreSame(host.Settlement.ProductionMessageRouter, host.MessageRouter);
            Assert.AreSame(
                host.Settlement.ProductionForcedInclusionFinalizer,
                host.ForcedInclusionFinalizer);
            Assert.AreSame(host.Settlement.ProductionSettlementClient, host.SettlementClient);
            Assert.AreSame(host.Settlement.ProductionTransactionSender, host.TransactionSender);
            Assert.IsFalse(host.IsMetricsHttpListening);
            Assert.AreEqual(20260716u, host.Bridge.ChainId);
            Assert.AreSame(
                host.Settlement.ProductionDepositSource,
                host.Bridge.DepositSource);
            Assert.AreSame(host.Settlement.ProductionDepositSource, host.Batch.DepositSource);
            Assert.AreSame(host.Settlement.ProductionMessageRouter, host.Batch.MessageRouter);
            Assert.AreSame(host.ForcedInclusion, host.Batch.ForcedInclusionSource);
            Assert.IsTrue(host.Batch.HasSealedBatchSink);
            Assert.IsTrue(host.HasSealedBatchSink);
            Assert.AreEqual(1UL, host.NextExpectedBlock);
            Assert.IsFalse(host.HasPendingSealedBatch);
            Assert.IsFalse(host.HasOpenBatch);
            Assert.IsFalse(host.TryRetryPendingSealedBatch());
            Assert.IsTrue(host.Settlement.IsProductionWired);
            Assert.IsNotNull(host.Settlement.ProductionTransactionSender);
            Assert.IsNotNull(host.Metrics.Metrics);
            Assert.AreSame(da, host.DaWriter);
            Assert.AreEqual(0, host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult());
            Assert.AreEqual(20260716u, host.RpcStore.ChainId);
            Assert.AreEqual(DAMode.L1, host.RpcStore.DAMode);
            Assert.IsTrue(Directory.Exists(Path.Combine(
                chainDir, Sp1SettlementExecutionStack.RelativeProverQueueDir)));
            Assert.IsTrue(host.IsProductionWired);
            Assert.IsTrue(host.IsOperatorReady);
            Assert.AreEqual(20260716u, host.ChainId);
            Assert.AreEqual(ProofType.Zk, host.ProofType);
            Assert.AreEqual(DAMode.L1, host.DaMode);
            Assert.AreEqual(0, host.PeekSharedBridgeDeposits(8).Count);
            var status = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(status.IsOperatorReady);
            Assert.AreEqual(ProofType.Zk, status.ProofType);
            Assert.AreEqual(DAMode.L1, status.DaMode);
            Assert.AreEqual(0, status.PendingSettlementCount);
            Assert.AreEqual(0, status.ReadyDepositCount);
            Assert.IsTrue(status.HasDepositSource);
            Assert.IsTrue(status.HasMessageRouter);
            Assert.AreEqual(host.GetLatestRpcStateRoot(), status.LatestRpcStateRoot);
            Assert.AreEqual(host.RpcStore.GatewayEnabled, status.GatewayEnabled);
            Assert.IsNotNull(host.MessageOutbox);
            Assert.IsNull(
                host.GetMessageRouterProofAsync(new UInt256(new byte[32])).AsTask().GetAwaiter().GetResult());
            Assert.IsTrue(host.RegisterForcedInclusionNonce(9));
            host.InvalidateForcedInclusionCache();
            Assert.AreEqual(0, host.BridgeAssetCount);
            Assert.IsFalse(string.IsNullOrWhiteSpace(host.ExportPrometheusMetrics()));
            Assert.AreEqual(0, host.StagedWithdrawalCount);
            Assert.IsNotNull(host.BatchProver);
            var statusPath = Path.Combine(chainDir, "operator-status.json");
            host.WriteOperatorStatusAsync(statusPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(statusPath));
            StringAssert.Contains(File.ReadAllText(statusPath), "\"proofType\": \"Zk\"");
            var promPath = Path.Combine(chainDir, "metrics.prom");
            host.WritePrometheusMetricsAsync(promPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(promPath));
            // Zk ProveAsync / production DA remain funded operator paths (executor + credentials).
            var recovery = host.GetRecoveryStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(0, recovery.PendingCount);
            Assert.AreEqual(
                0,
                host.GetTrackedForcedInclusionNoncesAsync(20260716u).AsTask().GetAwaiter().GetResult()
                    .Count);

            host.StartMetricsHttp(portOverride: 0);
            Assert.IsTrue(host.IsMetricsHttpListening);
            Assert.IsTrue(host.MetricsBoundPort > 0);
            var rpcPlugin = host.CreateRpcPlugin();
            Assert.IsNotNull(rpcPlugin);
            Assert.IsFalse(rpcPlugin.IsRegistered(894710606));
        }
        finally
        {
            if (Directory.Exists(chainDir))
                Directory.Delete(chainDir, recursive: true);
            if (Directory.Exists(exeRoot))
                Directory.Delete(exeRoot, recursive: true);
        }
    }

    [TestMethod]
    public void Open_MultisigSettlementConfig_FailsClosed()
    {
        var reportPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "docs", "audit", "testnet-deployment-20260716-live.json"));
        if (!File.Exists(reportPath))
            Assert.Inconclusive($"repo evidence file not found at {reportPath}");

        var chainDir = Path.Combine(
            Path.GetTempPath(),
            "neo-n4-zk-host-ms-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(chainDir);
        var exeRoot = Path.Combine(
            Path.GetTempPath(),
            "neo-n4-zk-exec-ms-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(exeRoot);
        try
        {
            File.WriteAllText(Path.Combine(chainDir, "chain.config.json"), """
                {
                  "chainId": 20260716,
                  "proofType": "Multisig",
                  "securityLevel": "Optimistic",
                  "daMode": "Local",
                  "sequencerModel": "DbftCommittee",
                  "exitModel": "Permissionless",
                  "gatewayEnabled": true,
                  "permissionlessExit": true,
                  "validators": []
                }
                """);
            NeoHubDeployReport.Load(reportPath).WriteOperatorArtifacts(chainDir);
            RewriteProofType(chainDir, "Neo.Plugins.L2Settlement", (byte)ProofType.Multisig);
            RewriteProofType(chainDir, "Neo.Plugins.L2Prover", (byte)ProofType.Multisig);
            File.WriteAllText(Path.Combine(chainDir, "genesis-manifest.json"), """
                { "chainId": 20260716, "initialStateRoot": "0x1111111111111111111111111111111111111111111111111111111111111111" }
                """);
            var statePath = Path.Combine(chainDir, Sp1SettlementExecutionStack.RelativeStateDir);
            Directory.CreateDirectory(statePath);
            using (var seed = new RocksDbKeyValueStore(statePath))
            {
                seed.Put("k"u8, "v"u8);
            }

            var exePath = Path.Combine(exeRoot, "neo-zkvm-executor");
            File.WriteAllBytes(exePath, [0x01]);
            var sha = SHA256.HashData(File.ReadAllBytes(exePath));

            var da = new StubProductionDaWriter();
            var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
                ZkLocalHostComposition.Open(
                    chainDir,
                    exePath,
                    sha,
                    Hash(0x55),
                    da,
                    new StubSigner(Account(0x55))));
            StringAssert.Contains(ex.Message, "Zk");
            StringAssert.Contains(ex.Message, "Multisig");
        }
        finally
        {
            if (Directory.Exists(chainDir))
                Directory.Delete(chainDir, recursive: true);
            if (Directory.Exists(exeRoot))
                Directory.Delete(exeRoot, recursive: true);
        }
    }

    [TestMethod]
    public void Open_MissingBootstrapState_FailsClosed()
    {
        var reportPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "docs", "audit", "testnet-deployment-20260716-live.json"));
        if (!File.Exists(reportPath))
            Assert.Inconclusive($"repo evidence file not found at {reportPath}");

        var chainDir = Path.Combine(
            Path.GetTempPath(),
            "neo-n4-zk-host-nostate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(chainDir);
        var exeRoot = Path.Combine(
            Path.GetTempPath(),
            "neo-n4-zk-exec-nostate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(exeRoot);
        try
        {
            // Zk configs + genesis, but no data/state RocksDB.
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
            RewriteProofType(chainDir, "Neo.Plugins.L2Settlement", (byte)ProofType.Zk);
            RewriteProofType(chainDir, "Neo.Plugins.L2Prover", (byte)ProofType.Zk);
            File.WriteAllText(Path.Combine(chainDir, "genesis-manifest.json"), """
                { "chainId": 20260716, "initialStateRoot": "0x1111111111111111111111111111111111111111111111111111111111111111" }
                """);

            var exePath = Path.Combine(exeRoot, "neo-zkvm-executor");
            File.WriteAllBytes(exePath, [0x01]);
            var sha = SHA256.HashData(File.ReadAllBytes(exePath));
            var da = new StubProductionDaWriter();
            var ex = Assert.ThrowsExactly<DirectoryNotFoundException>(() =>
                ZkLocalHostComposition.Open(
                    chainDir,
                    exePath,
                    sha,
                    Hash(0x66),
                    da,
                    new StubSigner(Account(0x66))));
            StringAssert.Contains(ex.Message, "bootstrap-genesis");
        }
        finally
        {
            if (Directory.Exists(chainDir))
                Directory.Delete(chainDir, recursive: true);
            if (Directory.Exists(exeRoot))
                Directory.Delete(exeRoot, recursive: true);
        }
    }

    private static UInt256 MaterializeZkChain(string chainDir, string reportPath)
    {
        var validatorA = new KeyPair(Enumerable.Range(3, 32).Select(i => (byte)i).ToArray()).PublicKey;
        var validatorB = new KeyPair(Enumerable.Range(4, 32).Select(i => (byte)i).ToArray()).PublicKey;
        var hexA = Convert.ToHexString(validatorA.EncodePoint(true)).ToLowerInvariant();
        var hexB = Convert.ToHexString(validatorB.EncodePoint(true)).ToLowerInvariant();
        File.WriteAllText(Path.Combine(chainDir, "chain.config.json"), $$"""
            {
              "chainId": 20260716,
              "proofType": "Zk",
              "securityLevel": "Validity",
              "daMode": "L1",
              "sequencerModel": "DbftCommittee",
              "exitModel": "Permissionless",
              "gatewayEnabled": true,
              "permissionlessExit": true,
              "validators": [ "{{hexA}}", "{{hexB}}" ]
            }
            """);
        NeoHubDeployReport.Load(reportPath).WriteOperatorArtifacts(chainDir);
        RewriteProofType(chainDir, "Neo.Plugins.L2Settlement", (byte)ProofType.Zk);
        RewriteProofType(chainDir, "Neo.Plugins.L2Prover", (byte)ProofType.Zk);

        var statePath = Path.Combine(chainDir, Sp1SettlementExecutionStack.RelativeStateDir);
        Directory.CreateDirectory(statePath);
        UInt256 genesisRoot;
        using (var state = new RocksDbKeyValueStore(statePath))
        {
            NeoVMGenesisBootstrap.Run(state);
            genesisRoot = Sp1StateWitnessSource.InitializeGenesisContractBindings(state);
        }

        File.WriteAllText(Path.Combine(chainDir, "genesis-manifest.json"), $$"""
            {
              "schemaVersion": 1,
              "chainId": 20260716,
              "initialStateRoot": "{{genesisRoot}}"
            }
            """);
        return genesisRoot;
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

    private static UInt160 Account(byte fill) =>
        UInt160.Parse("0x" + Convert.ToHexString(Enumerable.Repeat(fill, 20).ToArray()).ToLowerInvariant());

    private static UInt256 Hash(byte fill)
    {
        var bytes = new byte[UInt256.Length];
        bytes[0] = fill;
        return new UInt256(bytes);
    }

    private static UInt256 Root(byte fill) =>
        new(Enumerable.Repeat(fill, 32).ToArray());

    private static HttpClient CanonicalRootHttpClient(UInt256 genesisRoot)
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
            if (body.Contains("invokefunction", StringComparison.Ordinal)
                || body.Contains("invokescript", StringComparison.Ordinal))
            {
                var root = Convert.ToBase64String(genesisRoot.GetSpan().ToArray());
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

    /// <summary>Marker stub satisfying Zk profile RequireProductionDA without funded backends.</summary>
    private sealed class StubProductionDaWriter : IProductionDAWriter
    {
        public DAMode Mode => DAMode.L1;
        public DAReceiptKind ReceiptKind => DAReceiptKind.L1Transaction;

        public ValueTask<DAReceipt> PublishAsync(
            DAPublishRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("stub production DA does not publish");

        public ValueTask<bool> IsAvailableAsync(
            DAReceipt receipt,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("stub production DA does not poll availability");
    }
}
