using System.Net;
using System.Security.Cryptography;
using System.Text;
using Neo.Cryptography.ECC;
using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.Executor;
using Neo.L2.Executor.ProofWitness;
using Neo.L2.Gateway.Rpc;
using Neo.L2.Persistence;
using Neo.L2.Proving;
using Neo.L2.Proving.Attestation;
using Neo.L2.Proving.RiscVZk;
using Neo.L2.Settlement.Rpc;
using Neo.Network.P2P.Payloads;
using Neo.Plugins.L2;
using Neo.Plugins.L2Gateway;
using Neo.Plugins.L2Rpc;
using Neo.Wallets;

namespace Neo.L2.IntegrationTests;

/// <summary>
/// Local host-composition smoke: materialize L2 layouts from the public testnet deploy
/// report and open chain-directory factories / one-shot host roots without funded L1 traffic.
/// </summary>
/// <remarks>
/// Pins the operator path documented in IMPLEMENTATION_STATUS (create-chain / init-l2
/// artifacts + Multisig/Optimistic/Zk WireProduction / Gateway host roots). Real RPC
/// publication, prove-batch, production DA credentials, and gateway host daemons remain
/// funded gates.
/// </remarks>
[TestClass]
public sealed class UT_E2E_HostComposition_FromDeployReport
{
    [TestMethod]
    public void MultisigHostComposition_OpensAllChainDirectoryFactories()
    {
        var reportPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "docs", "audit", "testnet-deployment-20260716-live.json"));
        if (!File.Exists(reportPath))
            Assert.Inconclusive($"repo evidence file not found at {reportPath}");

        var chainDir = Path.Combine(
            Path.GetTempPath(),
            "neo-n4-host-comp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(chainDir);
        try
        {
            var validatorA = new KeyPair(Enumerable.Range(5, 32).Select(i => (byte)i).ToArray()).PublicKey;
            var validatorB = new KeyPair(Enumerable.Range(6, 32).Select(i => (byte)i).ToArray()).PublicKey;
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

            var report = NeoHubDeployReport.Load(reportPath);
            report.WriteOperatorArtifacts(chainDir);

            // Deploy-report templates default Zk; rewrite Multisig for this host path.
            RewriteProofType(chainDir, "Neo.Plugins.L2Settlement", (byte)ProofType.Multisig);
            RewriteProofType(chainDir, "Neo.Plugins.L2Prover", (byte)ProofType.Multisig);
            File.WriteAllText(Path.Combine(chainDir, "genesis-manifest.json"), """
                { "chainId": 20260716, "initialStateRoot": "0x1111111111111111111111111111111111111111111111111111111111111111" }
                """);

            using var batch = L2BatchPlugin.CreateFromChainDirectory(chainDir);
            using var settlement = L2SettlementPlugin.CreateFromChainDirectory(chainDir);
            using var bridge = L2BridgePlugin.CreateFromChainDirectory(chainDir);
            using var metrics = L2MetricsPlugin.CreateFromChainDirectory(chainDir);
            using var da = L2DAPlugin.CreateLocalFromChainDirectory(chainDir);
            using var rpcStore = InMemoryL2RpcStore.OpenFromChainDirectory(chainDir);
            using var gateway = L2GatewayPlugin.CreateMerkleDurableFromChainDirectory(chainDir);

            var signerKeys = new[]
            {
                GenKey(0x10),
                GenKey(0x20),
            };
            var signers = new InMemorySignerSet(signerKeys);
            using var prover = L2ProverPlugin.CreateMultisigWiredFromChainDirectory(chainDir, signers);

            var vk = UInt256.Parse("0x" + new string('a', 64));
            var batchSp1 = Sp1BatchProofProver.OpenFromChainDirectory(chainDir, vk);
            var gatewaySp1 = Sp1GatewayProofProver.OpenFromChainDirectory(chainDir, vk);

            using var publisher = ProofBoundRpcGlobalRootPublisher.OpenFromChainDirectory(
                chainDir,
                (_, _, _, _, _, _, _, _, _, _, _, _) =>
                    ValueTask.FromResult(UInt256.Parse("0x" + new string('f', 64))));

            // Layout owns settlement RocksDB dirs; dispose before L1Inbox opens the same paths.
            using (var layout = L2SettlementStoreLayout.Open(chainDir))
            {
                Assert.IsNotNull(layout.ProofWitness);
                Assert.IsNotNull(layout.ForcedInclusionEvents);
                Assert.IsNotNull(layout.SharedBridgeDeposits);
                Assert.IsNotNull(layout.MessageRouterEvents);
            }

            // openFinalizedProofStore=false: InMemoryL2RpcStore already holds data/rpc/proofs.
            // Production hosts pick one owner (RPC store vs MessageRouter) for that path.
            using var inbox = L1InboxFromChainDirectory.Open(chainDir, openFinalizedProofStore: false);
            inbox.WireBatch(batch);

            Assert.AreEqual(20260716u, bridge.ChainId);
            Assert.AreEqual(ProofType.Multisig, prover.Kind);
            Assert.IsInstanceOfType(prover.Prover, typeof(AttestationProver));
            Assert.IsInstanceOfType(gateway.Aggregator, typeof(BinaryTreeAggregator));
            var binary = (BinaryTreeAggregator)gateway.Aggregator;
            Assert.IsInstanceOfType(binary.RoundProver, typeof(MerklePathRoundProver));
            Assert.AreEqual(
                Path.GetFullPath(Path.Combine(chainDir, NeoHubDeployReport.RelativeProverInboxDir)),
                batchSp1.QueueDirectory);
            Assert.AreEqual(
                Path.GetFullPath(Path.Combine(chainDir, NeoHubDeployReport.RelativeGatewayProverQueueDir)),
                gatewaySp1.QueueDirectory);
            Assert.IsNotNull(inbox.ForcedInclusion);
            Assert.IsNotNull(inbox.Deposits);
            Assert.IsNotNull(inbox.MessageRouter);
            Assert.IsNotNull(batch);
            Assert.IsNotNull(settlement);
            Assert.IsNotNull(da);
            Assert.IsNotNull(metrics);
            Assert.IsNotNull(rpcStore);
            Assert.IsNotNull(publisher);
            var settlementSettings = L2SettlementSettings.FromChainDirectory(chainDir);
            Assert.AreEqual(report.SettlementManager.ToString(), settlementSettings.SettlementManagerHash);
            Assert.AreEqual(20260716u, settlementSettings.ChainId);
            Assert.AreEqual((byte)ProofType.Multisig, settlementSettings.ProofType);
        }
        finally
        {
            if (Directory.Exists(chainDir))
                Directory.Delete(chainDir, recursive: true);
        }
    }

    [TestMethod]
    public void MultisigLocalHost_And_GatewayHost_OpenTogether_FromDeployReport()
    {
        var reportPath = ResolveDeployReportPath();
        if (!File.Exists(reportPath))
            Assert.Inconclusive($"repo evidence file not found at {reportPath}");

        var chainDir = Path.Combine(
            Path.GetTempPath(),
            "neo-n4-host-msig-gw-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(chainDir);
        try
        {
            MaterializeChain(chainDir, reportPath, ProofType.Multisig, DAMode.Local, "Optimistic");
            using var http = MockL1HttpClient(Root(0x11));
            var signers = new InMemorySignerSet([GenKey(0x10), GenKey(0x20)]);
            using var settlementHost = MultisigLocalHostComposition.Open(
                chainDir,
                new StubExecutor(),
                signers,
                new StubSigner(Account(0x44)),
                rpcHttpClient: http);

            // Deferred metrics start (operator path without Open startMetricsHttp flag).
            settlementHost.StartMetricsHttp(portOverride: 0);
            Assert.IsTrue(settlementHost.IsMetricsHttpListening);
            Assert.IsTrue(settlementHost.MetricsBoundPort > 0);
            Assert.IsTrue(settlementHost.IsProductionWired);
            Assert.IsTrue(settlementHost.IsOperatorReady);
            Assert.IsTrue(settlementHost.HasSealedBatchSink);
            Assert.AreEqual(20260716u, settlementHost.ChainId);
            Assert.AreEqual(ProofType.Multisig, settlementHost.ProofType);
            Assert.AreEqual(DAMode.Local, settlementHost.DaMode);
            Assert.AreEqual(0, settlementHost.PeekSharedBridgeDeposits(8).Count);
            var opStatus = settlementHost.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(opStatus.IsOperatorReady);
            Assert.AreEqual(0, opStatus.PendingSettlementCount);
            Assert.AreEqual(0, opStatus.ReadyDepositCount);
            Assert.IsTrue(opStatus.HasDepositSource);
            Assert.IsTrue(opStatus.HasMessageRouter);
            Assert.AreEqual(settlementHost.GetLatestRpcStateRoot(), opStatus.LatestRpcStateRoot);
            Assert.IsNotNull(settlementHost.ForcedInclusionFinalizer);
            Assert.IsNotNull(settlementHost.TransactionSender);
            Assert.IsTrue(settlementHost.RegisterForcedInclusionNonce(11));
            Assert.IsNotNull(settlementHost.MessageOutbox);
            var daReceipt = settlementHost.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 1,
                Payload = new byte[] { 0xDE, 0xAD },
            }).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(settlementHost.IsDaAvailableAsync(daReceipt).AsTask().GetAwaiter().GetResult());
            Assert.IsFalse(string.IsNullOrWhiteSpace(settlementHost.ExportPrometheusMetrics()));
            Assert.AreEqual(0, settlementHost.BridgeAssetCount);
            Assert.AreEqual(0, settlementHost.StagedWithdrawalCount);
            Assert.IsNotNull(settlementHost.BatchProver);
            var statusPath = Path.Combine(chainDir, "operator-status.json");
            settlementHost.WriteOperatorStatusAsync(statusPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(statusPath));
            StringAssert.Contains(File.ReadAllText(statusPath), "\"isOperatorReady\": true");
            var promPath = Path.Combine(chainDir, "metrics.prom");
            settlementHost.WritePrometheusMetricsAsync(promPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(promPath));
            // Local durable recovery surface (no funded L1 publish).
            var recovery = settlementHost.GetRecoveryStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(0, recovery.PendingCount);
            Assert.AreEqual(
                0,
                settlementHost.GetTrackedForcedInclusionNoncesAsync(20260716u).AsTask()
                    .GetAwaiter().GetResult().Count);
            var rpcPlugin = settlementHost.CreateRpcPlugin();
            Assert.IsNotNull(rpcPlugin);
            using (var httpClient = new HttpClient())
            {
                var ready = httpClient.GetAsync(
                    $"http://127.0.0.1:{settlementHost.MetricsBoundPort}/readyz")
                    .GetAwaiter().GetResult();
                Assert.AreEqual(System.Net.HttpStatusCode.OK, ready.StatusCode);
            }

            var gatewayProof = new DelegatingGatewayProofProver(
                proofSystem: 1,
                aggregationBackendId: MerklePathRoundProver.ConstBackendId,
                proofFactory: static (_, _, _) =>
                    ValueTask.FromResult<ReadOnlyMemory<byte>>(new byte[] { 0xC1 }));
            using var gatewayHost = GatewayHostComposition.OpenMerkle(
                chainDir,
                gatewayProof,
                new StubSigner(Account(0x55)),
                UInt256.Parse("0x" + new string('d', 64)),
                UInt256.Parse("0x" + new string('e', 64)),
                metrics: settlementHost.Metrics.Metrics);

            Assert.AreEqual(Path.GetFullPath(chainDir), settlementHost.ChainDirectory);
            Assert.AreEqual(ProofType.Multisig, settlementHost.Prover.Kind);
            Assert.IsInstanceOfType(settlementHost.Prover.Prover, typeof(AttestationProver));
            Assert.AreEqual(DAMode.Local, settlementHost.DaWriter.Mode);
            Assert.IsNotNull(settlementHost.ForcedInclusion);
            Assert.AreEqual(20260716u, settlementHost.ForcedInclusion.ChainId);
            Assert.IsNotNull(settlementHost.DepositSource);
            Assert.AreEqual(20260716u, settlementHost.Bridge.ChainId);
            Assert.IsNotNull(settlementHost.Metrics.Metrics);
            Assert.AreEqual(20260716u, settlementHost.RpcStore.ChainId);
            Assert.AreEqual(DAMode.Local, settlementHost.RpcStore.DAMode);
            Assert.IsNotNull(settlementHost.MessageRouter);
            Assert.IsTrue(settlementHost.IsProductionWired);
            Assert.IsTrue(settlementHost.Batch.HasSealedBatchSink);
            Assert.AreSame(settlementHost.DepositSource, settlementHost.Bridge.DepositSource);
            Assert.AreSame(
                settlementHost.ForcedInclusion,
                settlementHost.Batch.ForcedInclusionSource);

            Assert.AreEqual(Path.GetFullPath(chainDir), gatewayHost.ChainDirectory);
            Assert.IsInstanceOfType(gatewayHost.Gateway.Aggregator, typeof(BinaryTreeAggregator));
            Assert.AreEqual(
                MerklePathRoundProver.ConstBackendId,
                ((BinaryTreeAggregator)gatewayHost.Gateway.Aggregator).RoundProver.BackendId);
            Assert.IsNotNull(gatewayHost.Publisher);
            Assert.AreSame(gatewayProof, gatewayHost.ProofProver);
            Assert.IsFalse(gatewayHost.HasPendingPublication);
            Assert.IsNull(gatewayHost.PendingPublicationEpoch);
            Assert.IsNotNull(gatewayHost.OutboxStatus);
            Assert.AreSame(gatewayHost.Gateway.Aggregator, gatewayHost.Aggregator);
            var gwStatus = gatewayHost.GetOperatorStatus();
            Assert.IsFalse(gwStatus.HasPendingPublication);
            Assert.AreEqual(0, gwStatus.OutboxQueueDepth);
            Assert.AreEqual(MerklePathRoundProver.ConstBackendId, gwStatus.AggregationBackendId);
            var gwStatusPath = Path.Combine(chainDir, "gateway-status.json");
            gatewayHost.WriteOperatorStatusAsync(gwStatusPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(gwStatusPath));
            StringAssert.Contains(File.ReadAllText(gwStatusPath), "\"hasPendingPublication\": false");
        }
        finally
        {
            if (Directory.Exists(chainDir))
                Directory.Delete(chainDir, recursive: true);
        }
    }

    [TestMethod]
    public void MultisigLocalHost_And_GatewayOpenMultisig_And_OpenSp1_FromDeployReport()
    {
        var reportPath = ResolveDeployReportPath();
        if (!File.Exists(reportPath))
            Assert.Inconclusive($"repo evidence file not found at {reportPath}");

        var chainDir = Path.Combine(
            Path.GetTempPath(),
            "neo-n4-host-msig-gw-backends-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(chainDir);
        try
        {
            MaterializeChain(chainDir, reportPath, ProofType.Multisig, DAMode.Local, "Optimistic");
            using var http = MockL1HttpClient(Root(0x11));
            var signers = new InMemorySignerSet([GenKey(0x10), GenKey(0x20)]);
            using var settlementHost = MultisigLocalHostComposition.Open(
                chainDir,
                new StubExecutor(),
                signers,
                new StubSigner(Account(0x44)),
                rpcHttpClient: http);

            Assert.IsNotNull(settlementHost.Settlement.ProductionForcedInclusionSource);
            Assert.IsNotNull(settlementHost.Settlement.ProductionSettlementClient);
            Assert.AreEqual(20260716u, settlementHost.RpcStore.ChainId);

            var multisigProof = new DelegatingGatewayProofProver(
                proofSystem: 1,
                aggregationBackendId: MultisigRoundProver.ConstBackendId,
                proofFactory: static (_, _, _) =>
                    ValueTask.FromResult<ReadOnlyMemory<byte>>(new byte[] { 0xC0 }));
            using (var gatewayMultisig = GatewayHostComposition.OpenMultisig(
                chainDir,
                signers,
                threshold: 2,
                multisigProof,
                new StubSigner(Account(0x55)),
                UInt256.Parse("0x" + new string('d', 64)),
                UInt256.Parse("0x" + new string('e', 64)),
                metrics: settlementHost.Metrics.Metrics))
            {
                Assert.AreEqual(
                    MultisigRoundProver.ConstBackendId,
                    ((BinaryTreeAggregator)gatewayMultisig.Gateway.Aggregator).RoundProver.BackendId);
                Assert.AreEqual(
                    2,
                    ((MultisigRoundProver)((BinaryTreeAggregator)gatewayMultisig.Gateway.Aggregator)
                        .RoundProver).Threshold);
                Assert.IsNotNull(gatewayMultisig.Publisher);
            }

            // Dispose Multisig gateway before Sp1 reuses durable outbox paths.
            var vk = UInt256.Parse("0x" + new string('a', 64));
            using var gatewaySp1 = GatewayHostComposition.OpenSp1(
                chainDir,
                vk,
                new StubSigner(Account(0x66)),
                UInt256.Parse("0x" + new string('d', 64)),
                vk,
                resultTimeout: TimeSpan.FromSeconds(5),
                pollInterval: TimeSpan.FromMilliseconds(10),
                metrics: settlementHost.Metrics.Metrics);
            Assert.IsTrue(gatewaySp1.OwnsProofProver);
            Assert.IsInstanceOfType(gatewaySp1.ProofProver, typeof(Sp1GatewayProofProver));
            Assert.IsNotNull(gatewaySp1.Publisher);
            Assert.IsTrue(Directory.Exists(Path.Combine(
                chainDir, NeoHubDeployReport.RelativeGatewayProverQueueDir)));
        }
        finally
        {
            if (Directory.Exists(chainDir))
                Directory.Delete(chainDir, recursive: true);
        }
    }

    [TestMethod]
    public void OptimisticLocalHost_And_GatewayHost_OpenTogether_FromDeployReport()
    {
        var reportPath = ResolveDeployReportPath();
        if (!File.Exists(reportPath))
            Assert.Inconclusive($"repo evidence file not found at {reportPath}");

        var chainDir = Path.Combine(
            Path.GetTempPath(),
            "neo-n4-host-opt-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(chainDir);
        try
        {
            MaterializeChain(chainDir, reportPath, ProofType.Optimistic, DAMode.Local, "Optimistic");
            // L1 mock root must match genesis-manifest (LegacyFromChainDirectory checkpoint).
            using var http = MockL1HttpClient(Root(0x11));
            var sequencer = new KeyPair(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
            using var host = OptimisticLocalHostComposition.Open(
                chainDir,
                new StubExecutor(),
                sequencer,
                UInt160.Parse("0x" + new string('b', 40)),
                UInt256.Parse("0x" + new string('c', 64)),
                new StubSigner(Account(0x46)),
                rpcHttpClient: http,
                submittedAtUnixMs: static () => 1_700_000_000_000UL);

            Assert.AreEqual(ProofType.Optimistic, host.Prover.Kind);
            Assert.AreEqual(ProofType.Optimistic, host.Prover.Prover!.Kind);
            Assert.AreEqual(DAMode.Local, host.DaWriter.Mode);
            Assert.IsNotNull(host.ForcedInclusion);
            Assert.AreEqual(20260716u, host.ForcedInclusion.ChainId);
            Assert.AreEqual(20260716u, host.Bridge.ChainId);
            Assert.IsNotNull(host.Metrics.Metrics);
            Assert.AreEqual(20260716u, host.RpcStore.ChainId);
            Assert.IsTrue(host.Settlement.IsProductionWired);
            Assert.IsTrue(host.IsProductionWired);
            Assert.IsTrue(host.IsOperatorReady);
            Assert.IsTrue(host.HasSealedBatchSink);
            Assert.AreEqual(ProofType.Optimistic, host.ProofType);
            Assert.AreEqual(0, host.PeekSharedBridgeDeposits(8).Count);
            var opStatus = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(opStatus.IsOperatorReady);
            Assert.AreEqual(0, opStatus.PendingSettlementCount);
            host.StartMetricsHttp(portOverride: 0);
            Assert.IsTrue(host.IsMetricsHttpListening);
            Assert.IsTrue(host.MetricsBoundPort > 0);
            Assert.IsNotNull(host.CreateRpcPlugin());
            Assert.AreEqual(0, host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult());
            var statusPath = Path.Combine(chainDir, "operator-status.json");
            host.WriteOperatorStatusAsync(statusPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(statusPath));
            StringAssert.Contains(File.ReadAllText(statusPath), "\"proofType\": \"Optimistic\"");

            var gatewayProof = new DelegatingGatewayProofProver(
                proofSystem: 1,
                aggregationBackendId: MerklePathRoundProver.ConstBackendId,
                proofFactory: static (_, _, _) =>
                    ValueTask.FromResult<ReadOnlyMemory<byte>>(new byte[] { 0xC1 }));
            using var gatewayHost = GatewayHostComposition.OpenMerkle(
                chainDir,
                gatewayProof,
                new StubSigner(Account(0x47)),
                UInt256.Parse("0x" + new string('d', 64)),
                UInt256.Parse("0x" + new string('e', 64)),
                metrics: host.Metrics.Metrics);
            Assert.IsNotNull(gatewayHost.Publisher);
            Assert.AreEqual(
                MerklePathRoundProver.ConstBackendId,
                ((BinaryTreeAggregator)gatewayHost.Gateway.Aggregator).RoundProver.BackendId);
            var gwStatusPath = Path.Combine(chainDir, "gateway-status.json");
            gatewayHost.WriteOperatorStatusAsync(gwStatusPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(gwStatusPath));
        }
        finally
        {
            if (Directory.Exists(chainDir))
                Directory.Delete(chainDir, recursive: true);
        }
    }

    [TestMethod]
    public void ZkLocalHost_OpensFromDeployReport()
    {
        var reportPath = ResolveDeployReportPath();
        if (!File.Exists(reportPath))
            Assert.Inconclusive($"repo evidence file not found at {reportPath}");

        var chainDir = Path.Combine(
            Path.GetTempPath(),
            "neo-n4-host-zk-e2e-" + Guid.NewGuid().ToString("N"));
        var exeRoot = Path.Combine(
            Path.GetTempPath(),
            "neo-n4-host-zk-exe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(chainDir);
        Directory.CreateDirectory(exeRoot);
        try
        {
            var genesisRoot = MaterializeZkChain(chainDir, reportPath);
            var exePath = Path.Combine(exeRoot, "neo-zkvm-executor");
            File.WriteAllBytes(exePath, [0x01, 0x02]);
            var sha = SHA256.HashData(File.ReadAllBytes(exePath));
            var vk = Hash(0x77);

            using var http = MockL1HttpClient(genesisRoot);
            using var host = ZkLocalHostComposition.Open(
                chainDir,
                exePath,
                sha,
                vk,
                new StubProductionDaWriter(),
                new StubSigner(Account(0x48)),
                rpcHttpClient: http);

            Assert.AreEqual(ProofType.Zk, host.Prover.Kind);
            Assert.IsInstanceOfType(host.Prover.Prover, typeof(Sp1BatchProofProver));
            Assert.AreSame(host.Stack.Prover, host.Prover.Prover);
            Assert.AreEqual(vk, host.Stack.Profile.VerificationKeyId);
            Assert.AreEqual(genesisRoot, host.Stack.Profile.GenesisStateRoot);
            Assert.IsNotNull(host.ForcedInclusion);
            Assert.IsNotNull(host.Bridge.DepositSource);
            Assert.AreEqual(20260716u, host.Bridge.ChainId);
            Assert.IsNotNull(host.Metrics.Metrics);
            Assert.AreEqual(20260716u, host.RpcStore.ChainId);
            Assert.AreEqual(DAMode.L1, host.RpcStore.DAMode);
            Assert.IsTrue(host.IsProductionWired);
            Assert.IsTrue(host.IsOperatorReady);
            Assert.AreEqual(ProofType.Zk, host.ProofType);
            Assert.AreEqual(DAMode.L1, host.DaMode);
            Assert.AreEqual(0, host.PeekSharedBridgeDeposits(8).Count);
            var opStatus = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(opStatus.IsOperatorReady);
            Assert.AreEqual(ProofType.Zk, opStatus.ProofType);
            Assert.AreEqual(0, opStatus.ReadyDepositCount);
            host.StartMetricsHttp(portOverride: 0);
            Assert.IsTrue(host.IsMetricsHttpListening);
            Assert.IsTrue(host.MetricsBoundPort > 0);
            Assert.IsNotNull(host.CreateRpcPlugin());
            Assert.AreEqual(0, host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult());

            // Validity + Gateway SP1 one-shot share the chain directory (no funded daemon traffic).
            var gatewayVk = Hash(0x88);
            using var gatewayHost = GatewayHostComposition.OpenSp1(
                chainDir,
                gatewayVk,
                new StubSigner(Account(0x49)),
                UInt256.Parse("0x" + new string('d', 64)),
                gatewayVk,
                resultTimeout: TimeSpan.FromSeconds(5),
                pollInterval: TimeSpan.FromMilliseconds(10),
                metrics: host.Metrics.Metrics);
            Assert.IsTrue(gatewayHost.OwnsProofProver);
            Assert.IsInstanceOfType(gatewayHost.ProofProver, typeof(Sp1GatewayProofProver));
            Assert.IsNotNull(gatewayHost.Publisher);
            Assert.IsTrue(Directory.Exists(Path.Combine(
                chainDir, NeoHubDeployReport.RelativeGatewayProverQueueDir)));
        }
        finally
        {
            if (Directory.Exists(chainDir))
                Directory.Delete(chainDir, recursive: true);
            if (Directory.Exists(exeRoot))
                Directory.Delete(exeRoot, recursive: true);
        }
    }

    private static string ResolveDeployReportPath() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "docs", "audit", "testnet-deployment-20260716-live.json"));

    private static void MaterializeChain(
        string chainDir,
        string reportPath,
        ProofType proofType,
        DAMode daMode,
        string securityLevel)
    {
        var validatorA = new KeyPair(Enumerable.Range(5, 32).Select(i => (byte)i).ToArray()).PublicKey;
        var validatorB = new KeyPair(Enumerable.Range(6, 32).Select(i => (byte)i).ToArray()).PublicKey;
        var hexA = Convert.ToHexString(validatorA.EncodePoint(true)).ToLowerInvariant();
        var hexB = Convert.ToHexString(validatorB.EncodePoint(true)).ToLowerInvariant();
        File.WriteAllText(Path.Combine(chainDir, "chain.config.json"), $$"""
            {
              "chainId": 20260716,
              "proofType": "{{proofType}}",
              "securityLevel": "{{securityLevel}}",
              "daMode": "{{daMode}}",
              "sequencerModel": "DbftCommittee",
              "exitModel": "Permissionless",
              "gatewayEnabled": true,
              "permissionlessExit": true,
              "validators": [ "{{hexA}}", "{{hexB}}" ]
            }
            """);
        NeoHubDeployReport.Load(reportPath).WriteOperatorArtifacts(chainDir);
        RewriteProofType(chainDir, "Neo.Plugins.L2Settlement", (byte)proofType);
        RewriteProofType(chainDir, "Neo.Plugins.L2Prover", (byte)proofType);
        File.WriteAllText(Path.Combine(chainDir, "genesis-manifest.json"), """
            { "chainId": 20260716, "initialStateRoot": "0x1111111111111111111111111111111111111111111111111111111111111111" }
            """);
    }

    private static UInt256 MaterializeZkChain(string chainDir, string reportPath)
    {
        MaterializeChain(chainDir, reportPath, ProofType.Zk, DAMode.L1, "Validity");
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
        if (!File.Exists(path))
            Assert.Fail($"expected materialised plugin config at {path}");
        var text = File.ReadAllText(path);
        // Materialized configs use "ProofType": <byte> under PluginConfiguration.
        var rewritten = System.Text.RegularExpressions.Regex.Replace(
            text,
            "\"ProofType\"\\s*:\\s*\\d+",
            $"\"ProofType\": {proofType}");
        File.WriteAllText(path, rewritten);
    }

    private static (Neo.Cryptography.ECC.ECPoint Pub, byte[] Priv) GenKey(byte seed)
    {
        var priv = new byte[32];
        for (var i = 0; i < 32; i++) priv[i] = (byte)(seed + i);
        return (Neo.Cryptography.ECC.ECCurve.Secp256r1.G * priv, priv);
    }

    private static UInt160 Account(byte fill) =>
        UInt160.Parse("0x" + Convert.ToHexString(Enumerable.Repeat(fill, 20).ToArray()).ToLowerInvariant());

    private static UInt256 Root(byte fill) =>
        new(Enumerable.Repeat(fill, 32).ToArray());

    private static UInt256 Hash(byte fill)
    {
        var bytes = new byte[UInt256.Length];
        bytes[0] = fill;
        return new UInt256(bytes);
    }

    private static HttpClient MockL1HttpClient(UInt256 root)
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
                var b64 = Convert.ToBase64String(root.GetSpan().ToArray());
                return Json(
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"state\":\"HALT\",\"gasconsumed\":\"0\","
                    + "\"stack\":[{\"type\":\"ByteString\",\"value\":\"" + b64 + "\"}]}}");
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
