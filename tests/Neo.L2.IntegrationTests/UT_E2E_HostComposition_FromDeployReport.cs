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
            Assert.IsTrue(settlementHost.HasMetricsReadinessCheck);
            Assert.IsTrue(settlementHost.IsProductionWired);
            Assert.IsTrue(settlementHost.IsOperatorReady);
            Assert.IsTrue(settlementHost.HasSealedBatchSink);
            Assert.AreEqual(1UL, settlementHost.NextExpectedBlock);
            Assert.AreEqual(0UL, settlementHost.LastAcknowledgedBatchNumber);
            Assert.AreEqual(1UL, settlementHost.NextBatchNumber);
            Assert.IsFalse(settlementHost.HasPendingSealedBatch);
            Assert.IsNull(settlementHost.PendingSealedBatchNumber);
            Assert.IsTrue(settlementHost.IsBatcherEnabled);
            Assert.IsTrue(settlementHost.HasBatchDepositSource);
            Assert.IsTrue(settlementHost.HasBatchMessageRouter);
            Assert.IsTrue(settlementHost.HasBatchForcedInclusionSource);
            Assert.IsTrue(settlementHost.HasBatchProver);
            Assert.AreEqual(0, settlementHost.OpenBatchForcedInclusionCount);
            Assert.AreEqual(0, settlementHost.OpenBatchL2ToL2MessageCount);
            Assert.AreEqual(0, settlementHost.OpenBatchWithdrawalCount);
            Assert.IsTrue(settlementHost.SupportsLocalDaReader);
            Assert.IsTrue(settlementHost.HasL1RpcEndpoint);
            Assert.IsNotNull(settlementHost.ExpectedNetwork);
            Assert.IsTrue(settlementHost.HasSettlementManagerHash);
            Assert.IsTrue(settlementHost.HasForcedInclusionHash);
            Assert.IsTrue(settlementHost.HasSharedBridgeHash);
            Assert.IsTrue(settlementHost.HasMessageRouterHash);
            Assert.IsFalse(settlementHost.HasL2BridgeHash);
            Assert.IsTrue(settlementHost.HasMessageOutbox);
            Assert.IsNull(
                settlementHost.GetLatestDurableCheckpointAsync().AsTask().GetAwaiter().GetResult());
            Assert.IsTrue(settlementHost.MaxForcedTransactionsPerBatch > 0);
            Assert.IsTrue(settlementHost.MetricsMaxConcurrentConnections > 0);
            Assert.IsTrue(settlementHost.MaxBlocksPerBatch > 0);
            Assert.IsTrue(settlementHost.MaxTransactionsPerBatch > 0);
            Assert.IsTrue(settlementHost.MaxBatchAgeMillis > 0);
            Assert.IsFalse(settlementHost.HasOpenBatch);
            Assert.AreEqual(UInt256.Zero, settlementHost.MessageOutboxL2ToL1Root);
            Assert.AreEqual(0, settlementHost.OpenBatchBlockCount);
            Assert.IsFalse(settlementHost.TryRetryPendingSealedBatch());
            Assert.IsTrue(settlementHost.RegisterInboundMessageNonce(11));
            Assert.AreEqual(1, settlementHost.KnownInboundNonceCount);
            settlementHost.InvalidateInboundMessageCache();
            Assert.AreEqual(0, settlementHost.L1InboxPendingCount);
            Assert.AreEqual(20260716u, settlementHost.ChainId);
            Assert.AreEqual(20260716u, settlementHost.BatcherConfiguredChainId);
            Assert.AreEqual(20260716u, settlementHost.SettlementConfiguredChainId);
            Assert.AreEqual(ProofType.Multisig, settlementHost.ProofType);
            Assert.AreEqual(ProofType.Multisig, settlementHost.SettlementConfiguredProofType);
            Assert.IsTrue(settlementHost.IsChainIdConfigConsistent);
            Assert.IsTrue(settlementHost.IsProofTypeConfigConsistent);
            Assert.AreEqual(settlementHost.ChainId, settlementHost.RpcChainId);
            Assert.AreEqual(DAMode.Local, settlementHost.DaMode);
            Assert.AreEqual(DAMode.Local, settlementHost.RpcDaMode);
            Assert.IsTrue(settlementHost.IsDaModeConfigConsistent);
            Assert.IsTrue(settlementHost.IsNeoHubHashWiringComplete);
            Assert.IsTrue(settlementHost.IsBatcherInboxWiringComplete);
            Assert.IsTrue(settlementHost.IsSecurityLevelProofTypeConsistent);
            Assert.IsTrue(settlementHost.IsSecurityLevelDaModeConsistent);
            Assert.IsTrue(settlementHost.IsMetricsWiringComplete);
            Assert.IsTrue(settlementHost.IsDepositPipelineWiringComplete);
            Assert.IsTrue(settlementHost.IsMessagePipelineWiringComplete);
            Assert.IsTrue(settlementHost.IsForcedInclusionPipelineWiringComplete);
            Assert.IsTrue(settlementHost.IsSettlementClientWiringComplete);
            Assert.IsTrue(settlementHost.HasExpectedNetwork);
            Assert.IsTrue(settlementHost.HasScannerDeployHeights);
            Assert.IsTrue(settlementHost.IsOfflinePassportComplete);
            Assert.AreEqual(0, settlementHost.OfflinePassportFailures.Count);
            Assert.AreEqual(0, settlementHost.PeekSharedBridgeDeposits(8).Count);
            var opStatus = settlementHost.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(opStatus.IsOperatorReady);
            Assert.IsTrue(opStatus.IsChainIdConfigConsistent);
            Assert.IsTrue(opStatus.IsProofTypeConfigConsistent);
            Assert.IsTrue(opStatus.IsDaModeConfigConsistent);
            Assert.AreEqual(DAMode.Local, opStatus.RpcDaMode);
            Assert.IsTrue(opStatus.IsNeoHubHashWiringComplete);
            Assert.IsTrue(opStatus.IsBatcherInboxWiringComplete);
            Assert.IsTrue(opStatus.IsSecurityLevelProofTypeConsistent);
            Assert.IsTrue(opStatus.IsSecurityLevelDaModeConsistent);
            Assert.IsTrue(opStatus.IsMetricsWiringComplete);
            Assert.IsTrue(opStatus.IsDepositPipelineWiringComplete);
            Assert.IsTrue(opStatus.IsMessagePipelineWiringComplete);
            Assert.IsTrue(opStatus.IsForcedInclusionPipelineWiringComplete);
            Assert.IsTrue(opStatus.IsSettlementClientWiringComplete);
            Assert.IsTrue(opStatus.HasExpectedNetwork);
            Assert.IsTrue(opStatus.HasScannerDeployHeights);
            Assert.IsTrue(opStatus.IsOfflinePassportComplete);
            Assert.AreEqual(0, opStatus.OfflinePassportFailures.Count);
            Assert.IsFalse(opStatus.IsSettlementPoisoned);
            Assert.IsTrue(opStatus.IsSettlementIdle);
            Assert.IsTrue(opStatus.IsPipelineHealthy);
            Assert.AreEqual(0, opStatus.PipelineHealthFailures.Count);
            Assert.AreEqual(0, opStatus.PendingSettlementCount);
            Assert.AreEqual(0, opStatus.ReadyDepositCount);
            Assert.IsTrue(opStatus.HasBatchProver);
            Assert.AreEqual(0, opStatus.OpenBatchForcedInclusionCount);
            Assert.AreEqual(0, opStatus.OpenBatchL2ToL2MessageCount);
            Assert.AreEqual(0, opStatus.OpenBatchWithdrawalCount);
            Assert.IsTrue(opStatus.SupportsLocalDaReader);
            Assert.IsTrue(opStatus.HasL1RpcEndpoint);
            Assert.AreEqual(settlementHost.ExpectedNetwork, opStatus.ExpectedNetwork);
            Assert.IsTrue(opStatus.HasSettlementManagerHash);
            Assert.IsTrue(opStatus.HasSharedBridgeHash);
            Assert.IsTrue(opStatus.HasMessageRouterHash);
            Assert.IsFalse(opStatus.HasL2BridgeHash);
            Assert.IsTrue(opStatus.HasMessageOutbox);
            Assert.IsNull(opStatus.LatestCheckpointBatchNumber);
            Assert.AreEqual(
                settlementHost.GetInitialStateRootAsync().AsTask().GetAwaiter().GetResult(),
                opStatus.InitialStateRoot);
            Assert.IsNull(opStatus.Recovery.FirstFailureAtUnixMilliseconds);
            Assert.IsTrue(opStatus.HasDepositSource);
            Assert.AreEqual(0, opStatus.DepositSourceReadyCount);
            Assert.AreEqual(0, opStatus.DepositSourceReservedCount);
            Assert.AreEqual(0, opStatus.DepositSourceSoftConsumedCount);
            Assert.IsTrue(opStatus.IsMetricsEnabled);
            Assert.IsTrue(settlementHost.IsMetricsEnabled);
            Assert.IsTrue(opStatus.IsSettlementEnabled);
            Assert.IsTrue(settlementHost.IsSettlementEnabled);
            Assert.IsTrue(opStatus.HasMessageRouter);
            Assert.IsTrue(opStatus.HasForcedInclusionFinalizer);
            Assert.IsTrue(opStatus.HasSettlementClient);
            Assert.IsTrue(opStatus.HasTransactionSender);
            Assert.AreEqual(1, opStatus.KnownInboundNonceCount);
            Assert.AreEqual(0, opStatus.L1InboxPendingCount);
            Assert.AreEqual(settlementHost.GetLatestRpcStateRoot(), opStatus.LatestRpcStateRoot);
            Assert.IsNotNull(settlementHost.ForcedInclusionFinalizer);
            Assert.IsNotNull(settlementHost.TransactionSender);
            Assert.IsTrue(settlementHost.RegisterForcedInclusionNonce(11));
            Assert.AreEqual(1, settlementHost.KnownForcedInclusionNonceCount);
            Assert.IsTrue(settlementHost.HasBatchForcedInclusionSource);
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
            var statusJson = File.ReadAllText(statusPath);
            StringAssert.Contains(statusJson, "\"isOperatorReady\": true");
            StringAssert.Contains(statusJson, "\"hasBatchProver\": true");
            StringAssert.Contains(statusJson, "\"openBatchForcedInclusionCount\": 0");
            StringAssert.Contains(statusJson, "\"openBatchL2ToL2MessageCount\": 0");
            StringAssert.Contains(statusJson, "\"openBatchWithdrawalCount\": 0");
            StringAssert.Contains(statusJson, "\"supportsLocalDaReader\": true");
            StringAssert.Contains(statusJson, "\"hasL1RpcEndpoint\": true");
            StringAssert.Contains(statusJson, "\"hasSettlementManagerHash\": true");
            StringAssert.Contains(statusJson, "\"hasSharedBridgeHash\": true");
            StringAssert.Contains(statusJson, "\"hasL2BridgeHash\": false");
            StringAssert.Contains(statusJson, "\"hasMessageOutbox\": true");
            StringAssert.Contains(statusJson, "\"isChainIdConfigConsistent\": true");
            StringAssert.Contains(statusJson, "\"isProofTypeConfigConsistent\": true");
            StringAssert.Contains(statusJson, "\"isDaModeConfigConsistent\": true");
            StringAssert.Contains(statusJson, "\"rpcDaMode\":");
            StringAssert.Contains(statusJson, "\"rpcChainId\":");
            StringAssert.Contains(statusJson, "\"isNeoHubHashWiringComplete\": true");
            StringAssert.Contains(statusJson, "\"isBatcherInboxWiringComplete\": true");
            StringAssert.Contains(statusJson, "\"isSecurityLevelProofTypeConsistent\": true");
            StringAssert.Contains(statusJson, "\"isSecurityLevelDaModeConsistent\": true");
            StringAssert.Contains(statusJson, "\"isMetricsWiringComplete\": true");
            StringAssert.Contains(statusJson, "\"hasMetricsReadinessCheck\": true");
            StringAssert.Contains(statusJson, "\"isDepositPipelineWiringComplete\": true");
            StringAssert.Contains(statusJson, "\"isMessagePipelineWiringComplete\": true");
            StringAssert.Contains(statusJson, "\"isForcedInclusionPipelineWiringComplete\": true");
            StringAssert.Contains(statusJson, "\"isSettlementClientWiringComplete\": true");
            StringAssert.Contains(statusJson, "\"hasExpectedNetwork\": true");
            StringAssert.Contains(statusJson, "\"hasScannerDeployHeights\": true");
            StringAssert.Contains(statusJson, "\"isOfflinePassportComplete\": true");
            StringAssert.Contains(statusJson, "\"offlinePassportFailures\":");
            StringAssert.Contains(statusJson, "\"isSettlementIdle\": true");
            StringAssert.Contains(statusJson, "\"isSettlementPoisoned\": false");
            StringAssert.Contains(statusJson, "\"isPipelineHealthy\": true");
            StringAssert.Contains(statusJson, "\"pipelineHealthFailures\":");
            StringAssert.Contains(statusJson, "\"hasOverdueForcedInclusion\": false");
            StringAssert.Contains(statusJson, "\"isSettlementRetrying\": false");
            StringAssert.Contains(statusJson, "\"isBatcherCheckpointAligned\": true");
            StringAssert.Contains(statusJson, "\"isMetricsHttpHealthy\":");
            StringAssert.Contains(statusJson, "\"metricsHttpHealthFailures\":");
            StringAssert.Contains(statusJson, "\"isLocalHostHealthy\":");
            StringAssert.Contains(statusJson, "\"localHostHealthFailures\":");
            StringAssert.Contains(statusJson, "\"initialStateRoot\":");
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
            Assert.IsTrue(gatewayHost.HasL1RpcEndpoint);
            Assert.IsNotNull(gatewayHost.ExpectedNetwork);
            Assert.AreEqual(1, gatewayHost.ProofSystem);
            Assert.AreEqual(UInt256.Parse("0x" + new string('d', 64)), gatewayHost.ReplayDomain);
            Assert.AreEqual(UInt256.Parse("0x" + new string('e', 64)), gatewayHost.VerificationKeyId);
            var gwOpStatus = gatewayHost.GetOperatorStatus();
            Assert.IsTrue(gwOpStatus.HasL1RpcEndpoint);
            Assert.AreEqual(gatewayHost.ReplayDomain, gwOpStatus.ReplayDomain);
            Assert.AreEqual(gatewayHost.VerificationKeyId, gwOpStatus.VerificationKeyId);
            Assert.AreEqual(gatewayHost.ProofSystem, gwOpStatus.ProofSystem);
            Assert.AreNotEqual(UInt160.Zero, gatewayHost.SettlementManagerHash);
            Assert.AreNotEqual(UInt160.Zero, gatewayHost.MessageRouterHash);
            Assert.AreEqual(gatewayHost.SettlementManagerHash, gwOpStatus.SettlementManagerHash);
            Assert.AreEqual(gatewayHost.MessageRouterHash, gwOpStatus.MessageRouterHash);
            Assert.AreSame(gatewayProof, gatewayHost.ProofProver);
            Assert.IsFalse(gatewayHost.HasPendingPublication);
            Assert.IsNull(gatewayHost.PendingPublicationEpoch);
            Assert.AreEqual(0, gatewayHost.AggregatorPendingCount);
            Assert.IsTrue(gatewayHost.HasDurableOutbox);
            Assert.IsTrue(gatewayHost.IsPublicationConfigured);
            Assert.IsTrue(gatewayHost.IsPublicationProfileReady);
            Assert.IsTrue(gatewayHost.HasExpectedNetwork);
            Assert.IsTrue(gatewayHost.IsOfflinePassportComplete);
            Assert.AreEqual(0, gatewayHost.OfflinePassportFailures.Count);
            Assert.IsTrue(gatewayHost.IsOutboxIdle);
            Assert.IsFalse(gatewayHost.IsOutboxPoisoned);
            Assert.IsTrue(gatewayHost.IsPublicationHealthy);
            Assert.AreEqual(0, gatewayHost.PublicationHealthFailures.Count);
            Assert.IsTrue(gatewayHost.IsGatewayHostHealthy);
            Assert.AreEqual(0, gatewayHost.GatewayHostHealthFailures.Count);
            Assert.IsNotNull(gatewayHost.OutboxStatus);
            Assert.AreSame(gatewayHost.Gateway.Aggregator, gatewayHost.Aggregator);
            var gwStatus = gatewayHost.GetOperatorStatus();
            Assert.IsFalse(gwStatus.HasPendingPublication);
            Assert.AreEqual(0, gwStatus.AggregatorPendingCount);
            Assert.IsTrue(gwStatus.HasDurableOutbox);
            Assert.IsTrue(gwStatus.IsPublicationConfigured);
            Assert.AreEqual(0, gwStatus.OutboxQueueDepth);
            Assert.AreEqual(MerklePathRoundProver.ConstBackendId, gwStatus.AggregationBackendId);
            var gwStatusPath = Path.Combine(chainDir, "gateway-status.json");
            gatewayHost.WriteOperatorStatusAsync(gwStatusPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(gwStatusPath));
            StringAssert.Contains(File.ReadAllText(gwStatusPath), "\"hasPendingPublication\": false");
            Assert.AreSame(settlementHost.Metrics.Metrics, gatewayHost.Metrics);
            Assert.AreEqual(0, settlementHost.ProcessReadyDeposits().Count);
            Assert.IsNotNull(settlementHost.DepositSource);
            Assert.IsNotNull(settlementHost.ForcedInclusion);
            Assert.IsTrue(gwStatus.HasMetrics);
            var gwPromPath = Path.Combine(chainDir, "gateway-metrics.prom");
            gatewayHost.WritePrometheusMetricsAsync(gwPromPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(gwPromPath));
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
            Assert.IsTrue(opStatus.HasMessageOutbox);
            Assert.IsTrue(host.HasMessageOutbox);
            Assert.AreEqual(host.ChainId, host.BatcherConfiguredChainId);
            Assert.AreEqual(host.ChainId, host.SettlementConfiguredChainId);
            Assert.AreEqual(host.ChainId, opStatus.BatcherConfiguredChainId);
            Assert.AreEqual(host.ProofType, host.SettlementConfiguredProofType);
            Assert.AreEqual(opStatus.ProofType, opStatus.SettlementConfiguredProofType);
            Assert.IsTrue(host.IsChainIdConfigConsistent);
            Assert.IsTrue(host.IsProofTypeConfigConsistent);
            Assert.IsTrue(host.IsDaModeConfigConsistent);
            Assert.IsTrue(host.IsNeoHubHashWiringComplete);
            Assert.IsTrue(host.IsBatcherInboxWiringComplete);
            Assert.IsTrue(host.IsSecurityLevelProofTypeConsistent);
            Assert.IsTrue(host.HasExpectedNetwork);
            Assert.IsTrue(host.HasScannerDeployHeights);
            Assert.IsTrue(host.IsOfflinePassportComplete);
            Assert.AreEqual(0, host.OfflinePassportFailures.Count);
            Assert.IsTrue(host.IsPipelineEnabled);
            Assert.IsTrue(opStatus.IsChainIdConfigConsistent);
            Assert.IsTrue(opStatus.IsProofTypeConfigConsistent);
            Assert.IsTrue(opStatus.IsDaModeConfigConsistent);
            Assert.IsTrue(opStatus.IsNeoHubHashWiringComplete);
            Assert.IsTrue(opStatus.IsBatcherInboxWiringComplete);
            Assert.IsTrue(opStatus.IsOfflinePassportComplete);
            Assert.AreEqual(0, opStatus.OfflinePassportFailures.Count);
            Assert.IsTrue(opStatus.IsPipelineEnabled);
            host.StartMetricsHttp(portOverride: 0);
            Assert.IsTrue(host.IsMetricsHttpListening);
            Assert.IsTrue(host.MetricsBoundPort > 0);
            Assert.IsNotNull(host.CreateRpcPlugin());
            Assert.AreEqual(0, host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult());
            var statusPath = Path.Combine(chainDir, "operator-status.json");
            host.WriteOperatorStatusAsync(statusPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(statusPath));
            var optStatusJson = File.ReadAllText(statusPath);
            StringAssert.Contains(optStatusJson, "\"proofType\": \"Optimistic\"");
            StringAssert.Contains(optStatusJson, "\"hasMessageOutbox\": true");
            StringAssert.Contains(optStatusJson, "\"batcherConfiguredChainId\":");

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
            Assert.AreEqual(MerklePathRoundProver.ConstBackendId, gatewayHost.AggregationBackendId);
            Assert.AreNotEqual(UInt160.Zero, gatewayHost.SettlementManagerHash);
            Assert.AreNotEqual(UInt160.Zero, gatewayHost.MessageRouterHash);
            var gwStatusPath = Path.Combine(chainDir, "gateway-status.json");
            gatewayHost.WriteOperatorStatusAsync(gwStatusPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(gwStatusPath));
            var gwJson = File.ReadAllText(gwStatusPath);
            StringAssert.Contains(gwJson, "\"settlementManagerHash\":");
            StringAssert.Contains(gwJson, "\"aggregationBackendId\":");
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
            Assert.AreEqual(ProofType.Zk, host.SettlementConfiguredProofType);
            Assert.IsTrue(host.IsChainIdConfigConsistent);
            Assert.IsTrue(host.IsProofTypeConfigConsistent);
            Assert.AreEqual(DAMode.L1, host.DaMode);
            Assert.AreEqual(host.ChainId, host.BatcherConfiguredChainId);
            Assert.AreEqual(host.ChainId, host.SettlementConfiguredChainId);
            Assert.AreEqual(0, host.PeekSharedBridgeDeposits(8).Count);
            var opStatus = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(opStatus.IsOperatorReady);
            Assert.AreEqual(ProofType.Zk, opStatus.ProofType);
            Assert.AreEqual(ProofType.Zk, opStatus.SettlementConfiguredProofType);
            Assert.IsTrue(opStatus.IsChainIdConfigConsistent);
            Assert.IsTrue(opStatus.IsProofTypeConfigConsistent);
            Assert.AreEqual(0, opStatus.ReadyDepositCount);
            Assert.IsTrue(opStatus.HasBatchProver);
            Assert.IsFalse(opStatus.SupportsLocalDaReader);
            Assert.IsFalse(host.SupportsLocalDaReader);
            Assert.AreEqual(0, opStatus.OpenBatchL2ToL2MessageCount);
            Assert.AreEqual(host.ChainId, opStatus.SettlementConfiguredChainId);
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
