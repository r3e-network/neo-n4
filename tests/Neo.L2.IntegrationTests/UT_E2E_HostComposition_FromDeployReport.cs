using System.Net;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Neo.Cryptography.ECC;
using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.Bridge;
using Neo.L2.Executor;
using Neo.L2.Executor.ProofWitness;
using Neo.L2.Gateway.Rpc;
using Neo.L2.Persistence;
using Neo.L2.Proving;
using Neo.L2.Proving.Attestation;
using Neo.L2.Proving.RiscVZk;
using Neo.L2.Settlement.Rpc;
using Neo.L2.State;
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
/// artifacts + Multisig/Optimistic/Zk WireProduction / Gateway host roots), including the
/// offline deposit mint → withdrawal seal → L2→L1 outbox path and Multisig/Optimistic
/// soft seal → local checkpoint → Gateway ReceiveBatch (no L1 settle/publish). Real RPC
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
            AssertSoftOpenBatchNoSeal(
                settlementHost.MaxBlocksPerBatch,
                (idx, ts, net, txs) => settlementHost.ProcessCommittedBlock(idx, ts, net, txs),
                () => settlementHost.HasOpenBatch,
                () => settlementHost.OpenBatchBlockCount,
                () => settlementHost.OpenBatchL1MessageCount,
                () => settlementHost.HasPendingSealedBatch,
                () => settlementHost.NextExpectedBlock,
                () => settlementHost.NextBatchNumber);
            AssertSoftOpenBatchOperatorSurface(
                () => settlementHost.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => settlementHost.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => settlementHost.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => settlementHost.FormatHealthProbeJson(),
                path => settlementHost.WriteOperatorStatusAsync(path).AsTask(),
                path => settlementHost.WriteHealthProbeAsync(path).AsTask(),
                chainDir,
                expectedOpenBatchBlockCount: settlementHost.MaxBlocksPerBatch > 2 ? 2 : 1);
            AssertSoftForcedInclusionOperatorSurface(
                settlementHost.RegisterForcedInclusionNonce,
                () => settlementHost.KnownForcedInclusionNonceCount,
                () => settlementHost.HasBatchForcedInclusionSource,
                () => settlementHost.HasOverdueForcedInclusionCached(),
                settlementHost.InvalidateForcedInclusionCache,
                () => settlementHost.OpenBatchForcedInclusionCount,
                () => settlementHost.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => settlementHost.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => settlementHost.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => settlementHost.FormatHealthProbeJson(),
                path => settlementHost.WriteOperatorStatusAsync(path).AsTask(),
                path => settlementHost.WriteHealthProbeAsync(path).AsTask(),
                chainDir,
                softNonce: 11);
            AssertSoftInboundMessageOperatorSurface(
                settlementHost.RegisterInboundMessageNonce,
                () => settlementHost.KnownInboundNonceCount,
                () => settlementHost.HasBatchMessageRouter,
                () => settlementHost.HasMessageRouter,
                () => settlementHost.IsMessagePipelineWiringComplete,
                settlementHost.InvalidateInboundMessageCache,
                () => settlementHost.OpenBatchL1MessageCount,
                () => settlementHost.L1InboxPendingCount,
                () => settlementHost.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => settlementHost.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => settlementHost.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => settlementHost.FormatHealthProbeJson(),
                path => settlementHost.WriteOperatorStatusAsync(path).AsTask(),
                path => settlementHost.WriteHealthProbeAsync(path).AsTask(),
                chainDir,
                softNonce: 11);
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
            Assert.IsTrue(settlementHost.IsPipelineHealthyAsync().AsTask().GetAwaiter().GetResult());
            Assert.IsTrue(settlementHost.IsSettlementRuntimeIdleAsync().AsTask().GetAwaiter().GetResult());
            Assert.IsFalse(settlementHost.IsSettlementPoisonedAsync().AsTask().GetAwaiter().GetResult());
            Assert.IsFalse(settlementHost.IsSettlementRetryingAsync().AsTask().GetAwaiter().GetResult());
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
            // Soft FI nonce 11 already registered in AssertSoftForcedInclusionOperatorSurface.
            Assert.IsFalse(settlementHost.RegisterForcedInclusionNonce(11));
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
            Assert.IsTrue(settlementHost.IsBatcherCheckpointAlignedAsync().AsTask().GetAwaiter().GetResult());
            var probe = settlementHost.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(probe.IsOfflinePassportComplete);
            Assert.IsTrue(probe.IsOperatorReady);
            Assert.IsTrue(probe.HasBatchProver);
            Assert.IsTrue(probe.IsDepositPipelineWiringComplete);
            Assert.IsTrue(probe.IsPipelineEnabled);
            Assert.IsFalse(probe.HasPendingSealedBatch);
            Assert.AreEqual(0, probe.SettlementRetryCount);
            Assert.IsTrue(probe.IsBatcherCheckpointAligned);
            Assert.IsFalse(probe.HasOverdueForcedInclusion);
            Assert.IsTrue(probe.IsPipelineHealthy);
            Assert.IsTrue(probe.IsSettlementRuntimeIdle);
            Assert.IsTrue(probe.IsSettlementIdle);
            Assert.IsFalse(probe.IsSettlementPoisoned);
            Assert.IsFalse(probe.IsSettlementRetrying);
            Assert.AreEqual(0, probe.PendingSettlementCount);
            Assert.IsNotNull(probe.Recovery);
            Assert.AreEqual(0, probe.Recovery.PendingCount);
            Assert.IsNull(probe.Recovery.State);
            Assert.AreEqual(0, probe.ReadyDepositCount);
            Assert.AreEqual(settlementHost.DepositSourceReadyCount, probe.DepositSourceReadyCount);
            Assert.AreEqual(settlementHost.L1InboxPendingCount, probe.L1InboxPendingCount);
            Assert.AreEqual(settlementHost.ConsumedDepositCount, probe.ConsumedDepositCount);
            Assert.IsTrue(probe.HasMessageOutbox);
            Assert.AreEqual(settlementHost.KnownForcedInclusionNonceCount, probe.KnownForcedInclusionNonceCount);
            Assert.IsFalse(string.IsNullOrWhiteSpace(probe.InitialStateRoot));
            Assert.AreEqual(settlementHost.ChainId, probe.ChainId);
            var probePath = Path.Combine(chainDir, "health-probe.json");
            settlementHost.WriteHealthProbeAsync(probePath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(probePath));
            var probeJson = File.ReadAllText(probePath);
            StringAssert.Contains(probeJson, "\"isPipelineHealthy\": true");
            StringAssert.Contains(probeJson, "\"isOperatorReady\": true");
            StringAssert.Contains(probeJson, "\"pendingSettlementCount\": 0");
            StringAssert.Contains(probeJson, "\"settlementRetryCount\": 0");
            StringAssert.Contains(probeJson, "\"isSettlementIdle\": true");
            StringAssert.Contains(probeJson, "\"readyDepositCount\": 0");
            StringAssert.Contains(probeJson, "\"recovery\":");
            StringAssert.Contains(probeJson, "\"isBatcherCheckpointAligned\": true");
            StringAssert.Contains(probeJson, "\"hasMessageOutbox\": true");
            StringAssert.Contains(probeJson, $"\"consumedDepositCount\": {settlementHost.ConsumedDepositCount}");
            StringAssert.Contains(probeJson, "\"chainId\": 20260716");
            StringAssert.Contains(probeJson, "\"initialStateRoot\":");
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
            StringAssert.Contains(statusJson, "\"isOpenBatchPastMaxAge\": false");
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

            // Offline deposit → withdrawal → L2→L1 outbox (no funded L1 scan/claim).
            var offlineBridge = AssertOfflineBridgeMintWithdrawalOutbox(
                settlementHost.RegisterBridgeAsset,
                settlementHost.ProcessDeposit,
                settlementHost.HasConsumedDeposit,
                () => settlementHost.ConsumedDepositCount,
                settlementHost.ProcessReadyDeposits,
                settlementHost.StageWithdrawal,
                () => settlementHost.StagedWithdrawalCount,
                settlementHost.SealWithdrawalBatch,
                msgs => settlementHost.EnqueueOutboundMessagesAsync(msgs).AsTask(),
                () => settlementHost.MessageOutbox!.L2ToL1Count,
                () => settlementHost.MessageOutboxL2ToL1Root);
            AssertOfflineBridgeRpcStoreSurface(
                offlineBridge,
                settlementHost.RecordRpcDeposit,
                settlementHost.GetRpcL1DepositStatus,
                settlementHost.RegisterRpcAsset,
                settlementHost.GetRpcBridgedAsset,
                settlementHost.GetRpcCanonicalAsset,
                settlementHost.RecordRpcWithdrawalProof,
                settlementHost.GetRpcWithdrawalProof,
                settlementHost.RecordRpcMessageProof,
                settlementHost.GetRpcMessageProof,
                settlementHost.RecordMessageRouterFinalizedProof,
                msgHash => settlementHost.GetMessageRouterProofAsync(msgHash).AsTask(),
                chainDir);
            AssertOfflineBridgeOperatorSurface(
                () => settlementHost.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => settlementHost.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => settlementHost.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => settlementHost.FormatHealthProbeJson(),
                path => settlementHost.WriteOperatorStatusAsync(path).AsTask(),
                path => settlementHost.WriteHealthProbeAsync(path).AsTask(),
                () => settlementHost.ScanSharedBridgeDepositsAsync().AsTask(),
                () => settlementHost.ScanAndProcessReadyDepositsAsync().AsTask(),
                chainDir);

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
                metricsPlugin: settlementHost.Metrics);

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
            Assert.IsTrue(gatewayHost.HasMetricsPlugin);
            Assert.AreSame(settlementHost.Metrics, gatewayHost.MetricsPlugin);
            Assert.IsTrue(gatewayHost.IsMetricsHttpListening);
            Assert.IsTrue(gatewayHost.IsMetricsHttpHealthy);
            Assert.AreEqual(gatewayHost.ChainDirectory, gatewayHost.GetHealthProbe().ChainDirectory);
            Assert.AreEqual(settlementHost.Metrics.ConfiguredPort, gatewayHost.MetricsConfiguredPort);
            Assert.AreEqual(settlementHost.Metrics.BindAddress, gatewayHost.MetricsBindAddress);
            Assert.IsNotNull(gatewayHost.OutboxStatus);
            Assert.AreSame(gatewayHost.Gateway.Aggregator, gatewayHost.Aggregator);
            var gwStatus = gatewayHost.GetOperatorStatus();
            Assert.IsFalse(gwStatus.HasPendingPublication);
            Assert.AreEqual(0, gwStatus.AggregatorPendingCount);
            Assert.IsTrue(gwStatus.HasDurableOutbox);
            Assert.IsTrue(gwStatus.IsPublicationConfigured);
            Assert.AreEqual(0, gwStatus.OutboxQueueDepth);
            Assert.AreEqual(MerklePathRoundProver.ConstBackendId, gwStatus.AggregationBackendId);
            Assert.IsTrue(gwStatus.HasMetricsPlugin);
            Assert.IsTrue(gwStatus.IsMetricsHttpHealthy);
            Assert.AreEqual(gatewayHost.MetricsConfiguredPort, gwStatus.MetricsConfiguredPort);
            Assert.AreEqual(gatewayHost.MetricsBindAddress, gwStatus.MetricsBindAddress);
            Assert.AreEqual(gatewayHost.MetricsMaxConcurrentConnections, gwStatus.MetricsMaxConcurrentConnections);
            var gwStatusPath = Path.Combine(chainDir, "gateway-status.json");
            gatewayHost.WriteOperatorStatusAsync(gwStatusPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(gwStatusPath));
            var gwStatusJson = File.ReadAllText(gwStatusPath);
            StringAssert.Contains(gwStatusJson, "\"hasPendingPublication\": false");
            StringAssert.Contains(gwStatusJson, "\"metricsBindAddress\":");
            StringAssert.Contains(gwStatusJson, "\"metricsMaxConcurrentConnections\":");
            StringAssert.Contains(gwStatusJson, "\"metricsConfiguredPort\":");
            var gwProbe = gatewayHost.GetHealthProbe();
            Assert.IsTrue(gwProbe.IsOfflinePassportComplete);
            Assert.IsTrue(gwProbe.IsPublicationConfigured);
            Assert.IsTrue(gwProbe.HasDurableOutbox);
            Assert.IsTrue(gwProbe.IsPublicationProfileReady);
            Assert.IsTrue(gwProbe.HasMetrics);
            Assert.IsTrue(gwProbe.HasMetricsPlugin);
            Assert.IsTrue(gwProbe.IsMetricsHttpListening);
            Assert.IsTrue(gwProbe.IsMetricsHttpHealthy);
            Assert.AreEqual(gatewayHost.MetricsConfiguredPort, gwProbe.MetricsConfiguredPort);
            Assert.AreEqual(gatewayHost.MetricsBindAddress, gwProbe.MetricsBindAddress);
            Assert.AreEqual(gatewayHost.MetricsMaxConcurrentConnections, gwProbe.MetricsMaxConcurrentConnections);
            Assert.AreEqual(gatewayHost.ProofSystem, gwProbe.ProofSystem);
            Assert.AreEqual(gatewayHost.AggregationBackendId, gwProbe.AggregationBackendId);
            Assert.AreEqual(gatewayHost.ReplayDomain.ToString(), gwProbe.ReplayDomain);
            Assert.AreEqual(gatewayHost.VerificationKeyId.ToString(), gwProbe.VerificationKeyId);
            Assert.AreEqual(gatewayHost.SettlementManagerHash.ToString(), gwProbe.SettlementManagerHash);
            Assert.AreEqual(gatewayHost.MessageRouterHash.ToString(), gwProbe.MessageRouterHash);
            Assert.IsFalse(gwProbe.HasPendingPublication);
            Assert.IsNull(gwProbe.PendingPublicationEpoch);
            Assert.AreEqual(0, gwProbe.AggregatorPendingCount);
            Assert.AreEqual(0, gwProbe.OutboxQueueDepth);
            Assert.AreEqual(0, gwProbe.OutboxRetryCount);
            Assert.IsTrue(string.IsNullOrEmpty(gwProbe.OutboxLastError));
            Assert.IsTrue(gwProbe.IsPublicationHealthy);
            Assert.IsTrue(gwProbe.IsOutboxIdle);
            Assert.IsFalse(gwProbe.IsOutboxPoisoned);
            var gwProbeJson = gatewayHost.FormatHealthProbeJson();
            StringAssert.Contains(gwProbeJson, "\"proofSystem\":");
            StringAssert.Contains(gwProbeJson, "\"settlementManagerHash\":");
            StringAssert.Contains(gwProbeJson, "\"aggregationBackendId\":");
            StringAssert.Contains(gwProbeJson, "\"hasMetricsPlugin\": true");
            var gwProbePath = Path.Combine(chainDir, "gateway-health-probe.json");
            gatewayHost.WriteHealthProbeAsync(gwProbePath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(gwProbePath));
            Assert.AreEqual(gwProbeJson, File.ReadAllText(gwProbePath));
            StringAssert.Contains(gwProbeJson, "\"isPublicationHealthy\": true");
            StringAssert.Contains(gwProbeJson, "\"outboxQueueDepth\": 0");
            StringAssert.Contains(gwProbeJson, "\"outboxRetryCount\": 0");
            StringAssert.Contains(gwProbeJson, "\"isPublicationConfigured\": true");
            StringAssert.Contains(gwProbeJson, "\"hasDurableOutbox\": true");
            StringAssert.Contains(gwProbeJson, "\"hasMetrics\": true");
            Assert.AreSame(settlementHost.Metrics.Metrics, gatewayHost.Metrics);
            Assert.AreEqual(0, settlementHost.ProcessReadyDeposits().Count);
            Assert.IsNotNull(settlementHost.DepositSource);
            Assert.IsNotNull(settlementHost.ForcedInclusion);
            Assert.IsTrue(gwStatus.HasMetrics);

            AssertSoftRpcStoreAndGatewayReceiveBatch(
                settlementHost.AddRpcBatch,
                settlementHost.FinalizeRpcBatch,
                settlementHost.GetRpcBatchStatus,
                settlementHost.GetLatestRpcStateRoot,
                settlementHost.RecordRpcDeposit,
                settlementHost.GetRpcL1DepositStatus,
                gatewayHost,
                chainDir,
                ProofType.Multisig,
                softRoot: Root(0xAB));
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
            settlementHost.StartMetricsHttp(portOverride: 0);
            using (var gatewayMultisig = GatewayHostComposition.OpenMultisig(
                chainDir,
                signers,
                threshold: 2,
                multisigProof,
                new StubSigner(Account(0x55)),
                UInt256.Parse("0x" + new string('d', 64)),
                UInt256.Parse("0x" + new string('e', 64)),
                metricsPlugin: settlementHost.Metrics))
            {
                Assert.AreEqual(
                    MultisigRoundProver.ConstBackendId,
                    ((BinaryTreeAggregator)gatewayMultisig.Gateway.Aggregator).RoundProver.BackendId);
                Assert.AreEqual(
                    2,
                    ((MultisigRoundProver)((BinaryTreeAggregator)gatewayMultisig.Gateway.Aggregator)
                        .RoundProver).Threshold);
                Assert.IsNotNull(gatewayMultisig.Publisher);
                Assert.IsTrue(gatewayMultisig.HasMetricsPlugin);
                Assert.IsTrue(gatewayMultisig.IsMetricsHttpHealthy);
                var msigProbe = gatewayMultisig.GetHealthProbe();
                Assert.AreEqual(gatewayMultisig.AggregationBackendId, msigProbe.AggregationBackendId);
                Assert.AreEqual(gatewayMultisig.SettlementManagerHash.ToString(), msigProbe.SettlementManagerHash);
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
                metricsPlugin: settlementHost.Metrics);
            Assert.IsTrue(gatewaySp1.OwnsProofProver);
            Assert.IsInstanceOfType(gatewaySp1.ProofProver, typeof(Sp1GatewayProofProver));
            Assert.IsNotNull(gatewaySp1.Publisher);
            Assert.IsTrue(gatewaySp1.HasMetricsPlugin);
            Assert.IsTrue(gatewaySp1.IsMetricsHttpHealthy);
            var sp1Probe = gatewaySp1.GetHealthProbe();
            Assert.IsTrue(sp1Probe.OwnsProofProver);
            Assert.AreEqual(gatewaySp1.VerificationKeyId.ToString(), sp1Probe.VerificationKeyId);
            Assert.IsTrue(Directory.Exists(Path.Combine(
                chainDir, NeoHubDeployReport.RelativeGatewayProverQueueDir)));
        }
        finally
        {
            if (Directory.Exists(chainDir))
                Directory.Delete(chainDir, recursive: true);
        }
    }

    /// <summary>
    /// Soft seal→local PersistAsync on Multisig host from deploy report (no L1 settle claim).
    /// </summary>
    [TestMethod]
    public void MultisigLocalHost_SoftSeal_EmptyBlock_PersistsLocalCheckpoint()
    {
        var reportPath = ResolveDeployReportPath();
        if (!File.Exists(reportPath))
            Assert.Inconclusive($"repo evidence file not found at {reportPath}");

        var chainDir = Path.Combine(
            Path.GetTempPath(),
            "neo-n4-host-msig-soft-seal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(chainDir);
        try
        {
            MaterializeChain(chainDir, reportPath, ProofType.Multisig, DAMode.Local, "Optimistic");
            RewriteMaxBlocksPerBatch(chainDir, 1);
            using var http = MockL1HttpClient(Root(0x11));
            var signers = new InMemorySignerSet([GenKey(0x10), GenKey(0x20)]);
            using var host = MultisigLocalHostComposition.Open(
                chainDir,
                new SoftPassThroughExecutor(),
                signers,
                new StubSigner(Account(0x44)),
                rpcHttpClient: http);

            Assert.AreEqual(1, host.MaxBlocksPerBatch);
            Assert.IsTrue(host.IsOperatorReady);
            Assert.IsNull(host.GetLatestDurableCheckpointAsync().AsTask().GetAwaiter().GetResult());

            var ts = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            host.ProcessCommittedBlock(1, ts, 894710606, Array.Empty<byte[]>());

            Assert.IsFalse(host.HasOpenBatch);
            Assert.IsFalse(host.HasPendingSealedBatch);
            Assert.AreEqual(2UL, host.NextExpectedBlock);
            Assert.AreEqual(1UL, host.LastAcknowledgedBatchNumber);
            Assert.AreEqual(1UL, host.LastAcknowledgedBlock);
            Assert.AreEqual(2UL, host.NextBatchNumber);
            // Durable artifact awaits L1 settle (funded); local queue not idle.
            Assert.AreEqual(1, host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult());

            var checkpoint = host.GetLatestDurableCheckpointAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsNotNull(checkpoint);
            Assert.AreEqual(1UL, checkpoint!.BatchNumber);
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, checkpoint.PostStateRoot);

            AssertSoftSealLocalDaSurface(
                req => host.PublishDaAsync(req).AsTask(),
                receipt => host.IsDaAvailableAsync(receipt).AsTask(),
                () => host.SupportsLocalDaReader,
                () => host.CreateLocalDaReader(),
                checkpoint.BatchNumber,
                chainDir);

            // Seal batch 2 while batch 1 still pending L1 (before inbound nonce registration
            // so L1 message drain does not attempt mock getMessage for soft-registered nonces).
            AssertSoftSealSecondBatchWhilePending(
                (idx, ts, net, txs) => host.ProcessCommittedBlock(idx, ts, net, txs),
                () => host.NextExpectedBlock,
                () => host.LastAcknowledgedBatchNumber,
                () => host.LastAcknowledgedBlock,
                () => host.NextBatchNumber,
                () => host.HasOpenBatch,
                () => host.HasPendingSealedBatch,
                () => host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetLatestDurableCheckpointAsync().AsTask().GetAwaiter().GetResult(),
                req => host.PublishDaAsync(req).AsTask(),
                receipt => host.IsDaAvailableAsync(receipt).AsTask(),
                () => host.SupportsLocalDaReader,
                () => host.CreateLocalDaReader(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                host.HasConsumedDeposit,
                chainDir,
                expectSoftOfflineBookkeeping: false);

            var status = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(2UL, status.LatestCheckpointBatchNumber);
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, status.LatestCheckpointPostStateRoot);
            Assert.IsTrue(status.PendingSettlementCount >= 2);
            Assert.IsFalse(status.IsSettlementIdle);
            Assert.IsTrue(status.IsBatcherCheckpointAligned);
            // Mock L1 settle fails → durable recovery Retrying (preferred over generic idle miss).
            Assert.IsTrue(status.IsOfflinePassportComplete);
            Assert.IsFalse(status.IsPipelineHealthy);
            Assert.IsTrue(status.IsSettlementRetrying);
            Assert.IsFalse(status.IsSettlementPoisoned);
            CollectionAssert.Contains(
                status.PipelineHealthFailures.ToArray(),
                nameof(status.IsSettlementRetrying));

            // Offline bridge while settlement still Retrying (no L1 settle required).
            AssertSoftSealOfflineBridgeWhileRetrying(
                host.RegisterBridgeAsset,
                host.ProcessDeposit,
                host.HasConsumedDeposit,
                () => host.ConsumedDepositCount,
                host.ProcessReadyDeposits,
                host.StageWithdrawal,
                () => host.StagedWithdrawalCount,
                host.SealWithdrawalBatch,
                msgs => host.EnqueueOutboundMessagesAsync(msgs).AsTask(),
                () => host.MessageOutbox!.L2ToL1Count,
                () => host.MessageOutboxL2ToL1Root,
                host.RecordRpcDeposit,
                host.GetRpcL1DepositStatus,
                host.RegisterRpcAsset,
                host.GetRpcBridgedAsset,
                host.GetRpcCanonicalAsset,
                host.RecordRpcWithdrawalProof,
                host.GetRpcWithdrawalProof,
                host.RecordRpcMessageProof,
                host.GetRpcMessageProof,
                host.RecordMessageRouterFinalizedProof,
                msgHash => host.GetMessageRouterProofAsync(msgHash).AsTask(),
                () => host.ScanSharedBridgeDepositsAsync().AsTask(),
                () => host.ScanAndProcessReadyDepositsAsync().AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                checkpoint.BatchNumber,
                chainDir);

            AssertSoftSealFiInboundWhileRetrying(
                host.RegisterForcedInclusionNonce,
                () => host.KnownForcedInclusionNonceCount,
                () => host.HasBatchForcedInclusionSource,
                () => host.HasOverdueForcedInclusionCached(),
                host.InvalidateForcedInclusionCache,
                () => host.OpenBatchForcedInclusionCount,
                host.RegisterInboundMessageNonce,
                () => host.KnownInboundNonceCount,
                () => host.HasBatchMessageRouter,
                () => host.HasMessageRouter,
                () => host.IsMessagePipelineWiringComplete,
                host.InvalidateInboundMessageCache,
                () => host.OpenBatchL1MessageCount,
                () => host.L1InboxPendingCount,
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir,
                softNonce: 11);

            // Durable host ops files on soft-seal retry (parity with unit SoftSeal writers).
            var softSealStatusPath = Path.Combine(chainDir, "soft-seal-operator-status.json");
            host.WriteOperatorStatusAsync(softSealStatusPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(softSealStatusPath));
            var softSealStatusFile = File.ReadAllText(softSealStatusPath);
            StringAssert.Contains(softSealStatusFile, "\"isSettlementRetrying\": true");
            StringAssert.Contains(softSealStatusFile, "\"latestCheckpointBatchNumber\": 2");
            StringAssert.Contains(softSealStatusFile, "IsSettlementRetrying");
            StringAssert.Contains(softSealStatusFile, "\"consumedDepositCount\": 1");
            StringAssert.Contains(softSealStatusFile, "\"knownForcedInclusionNonceCount\": 1");
            StringAssert.Contains(softSealStatusFile, "\"knownInboundNonceCount\": 1");
            var softSealProbePath = Path.Combine(chainDir, "soft-seal-health-probe.json");
            host.WriteHealthProbeAsync(softSealProbePath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(softSealProbePath));
            var softSealProbeFile = File.ReadAllText(softSealProbePath);
            StringAssert.Contains(softSealProbeFile, "\"isSettlementRetrying\": true");
            StringAssert.Contains(softSealProbeFile, "\"latestCheckpointBatchNumber\": 2");
            StringAssert.Contains(softSealProbeFile, "IsSettlementRetrying");
            StringAssert.Contains(softSealProbeFile, "\"consumedDepositCount\": 1");
            StringAssert.Contains(softSealProbeFile, "\"knownForcedInclusionNonceCount\": 1");
            StringAssert.Contains(softSealProbeFile, "\"knownInboundNonceCount\": 1");

            // Multi-batch soft queue still pending L1 (no funded settle). Avoid SubmitNext here:
            // extra SubmitNext with ≥2 pending can escalate to Poisoned before the explicit
            // Reconcile→poison path below (unit SoftSeal also defers SubmitNext until poison).
            Assert.IsTrue(host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult() >= 2);
            Assert.IsFalse(host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult().IsSettlementIdle);
            Assert.IsTrue(host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult().IsSettlementRetrying);

            // Soft seal → gateway: RPC finalize batch 1+2, dual-chain ReceiveBatch both
            // (no L1 PublishAggregate). Host settle still pending ≥2.
            var checkpoint2 = host.GetLatestDurableCheckpointAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsNotNull(checkpoint2);
            Assert.AreEqual(2UL, checkpoint2!.BatchNumber);
            AssertSoftSealFeedsGatewayReceiveBatch(
                host.AddRpcBatch,
                host.FinalizeRpcBatch,
                host.GetRpcBatch,
                host.GetLatestRpcStateRoot,
                checkpoint,
                chainDir,
                host.Metrics,
                ProofType.Multisig,
                Account(0x55),
                secondCheckpoint: checkpoint2,
                getRpcStateRootAtBatch: host.GetRpcStateRootAtBatch);
            // After Finalize inside gateway helper, tip + host scrape remain operator-readable.
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, host.GetLatestRpcStateRoot());
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, host.GetRpcStateRootAtBatch(1));
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, host.GetRpcStateRootAtBatch(2));
            Assert.IsNotNull(host.GetRpcBatch(2));
            var statusAfterGw = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, statusAfterGw.LatestRpcStateRoot);
            Assert.IsTrue(statusAfterGw.IsSettlementRetrying);
            Assert.IsFalse(statusAfterGw.IsSettlementIdle);
            Assert.IsTrue(statusAfterGw.PendingSettlementCount >= 2);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-multi-batch-rpc-gateway.json")));
            var hostProm = host.ExportPrometheusMetrics();
            Assert.IsFalse(string.IsNullOrWhiteSpace(hostProm));
            var hostPromPath = Path.Combine(chainDir, "soft-seal-host.prom");
            host.WritePrometheusMetricsAsync(hostPromPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(hostPromPath));
            Assert.AreEqual(hostProm, File.ReadAllText(hostPromPath));

            // Mock reconcile fails closed → SubmitNext escalates to Poisoned; local recover resets Retrying.
            Assert.ThrowsExactly<OverflowException>(
                () => host.ReconcileAsync().GetAwaiter().GetResult());
            host.SubmitNextAsync().GetAwaiter().GetResult();
            var afterPoison = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(afterPoison.IsSettlementPoisoned);
            Assert.IsFalse(afterPoison.IsSettlementRetrying);
            CollectionAssert.Contains(
                afterPoison.PipelineHealthFailures.ToArray(),
                nameof(afterPoison.IsSettlementPoisoned));
            Assert.IsNotNull(afterPoison.Recovery.BlockedBatchNumber);
            Assert.IsNotNull(afterPoison.Recovery.ArtifactContentHash);
            var blockedBatch = afterPoison.Recovery.BlockedBatchNumber!.Value;
            var contentHash = afterPoison.Recovery.ArtifactContentHash!;
            Assert.ThrowsExactly<InvalidOperationException>(
                () => host.RecoverPoisonedBatchAsync(blockedBatch, UInt256.Zero)
                    .GetAwaiter().GetResult());
            Assert.IsTrue(host.IsSettlementPoisonedAsync().AsTask().GetAwaiter().GetResult());
            host.RecoverPoisonedBatchAsync(blockedBatch, contentHash).GetAwaiter().GetResult();
            var afterRecover = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsFalse(afterRecover.IsSettlementPoisoned);
            Assert.IsTrue(afterRecover.IsSettlementRetrying);
            Assert.AreEqual(SettlementRecoveryState.Retrying, afterRecover.Recovery.State);
            Assert.AreEqual(0, afterRecover.Recovery.RetryCount);
            Assert.IsTrue(host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult() >= 1);
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, afterRecover.LatestRpcStateRoot);
            Assert.IsFalse(afterRecover.IsLocalHostHealthy);
            CollectionAssert.Contains(
                afterRecover.LocalHostHealthFailures.ToArray(),
                nameof(afterRecover.IsSettlementRetrying));
            Assert.IsFalse(host.IsSettlementRuntimeIdleAsync().AsTask().GetAwaiter().GetResult());
            Assert.IsTrue(afterRecover.IsOfflinePassportComplete);
            Assert.IsTrue(afterRecover.IsBatcherCheckpointAligned);
            Assert.IsTrue(afterRecover.IsOperatorReady);
            AssertSoftSealAfterRecoverSoftStateRetention(
                afterRecover,
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                host.HasConsumedDeposit,
                host.GetRpcL1DepositStatus,
                chainDir,
                getRpcBatch: host.GetRpcBatch,
                getRpcStateRootAtBatch: host.GetRpcStateRootAtBatch,
                getRpcBatchStatus: host.GetRpcBatchStatus,
                getLatestRpcStateRoot: host.GetLatestRpcStateRoot);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-recover-multi-batch-rpc.json")));
            var afterRecoverStatusPath = Path.Combine(chainDir, "soft-seal-after-recover-status.json");
            host.WriteOperatorStatusAsync(afterRecoverStatusPath).AsTask().GetAwaiter().GetResult();
            StringAssert.Contains(File.ReadAllText(afterRecoverStatusPath), "\"isSettlementRetrying\": true");
            StringAssert.Contains(File.ReadAllText(afterRecoverStatusPath), "\"isOfflinePassportComplete\": true");
            StringAssert.Contains(File.ReadAllText(afterRecoverStatusPath), "\"consumedDepositCount\": 1");
            StringAssert.Contains(File.ReadAllText(afterRecoverStatusPath), "\"knownForcedInclusionNonceCount\": 1");
            StringAssert.Contains(File.ReadAllText(afterRecoverStatusPath), "\"knownInboundNonceCount\": 1");
            var afterRecoverProbePath = Path.Combine(chainDir, "soft-seal-after-recover-probe.json");
            host.WriteHealthProbeAsync(afterRecoverProbePath).AsTask().GetAwaiter().GetResult();
            StringAssert.Contains(File.ReadAllText(afterRecoverProbePath), "\"isSettlementRetrying\": true");
            StringAssert.Contains(File.ReadAllText(afterRecoverProbePath), "\"consumedDepositCount\": 1");
            StringAssert.Contains(File.ReadAllText(afterRecoverProbePath), "\"knownForcedInclusionNonceCount\": 1");
            StringAssert.Contains(File.ReadAllText(afterRecoverProbePath), "\"knownInboundNonceCount\": 1");
            StringAssert.Contains(File.ReadAllText(afterRecoverStatusPath), "\"latestCheckpointBatchNumber\": 2");
            StringAssert.Contains(File.ReadAllText(afterRecoverProbePath), "\"latestCheckpointBatchNumber\": 2");
            StringAssert.Contains(
                File.ReadAllText(afterRecoverStatusPath),
                "\"latestRpcStateRoot\": \"" + SoftPassThroughExecutor.PostStateRoot + "\"");

            AssertSoftSealAfterRecoverDaAndSecondDeposit(
                req => host.PublishDaAsync(req).AsTask(),
                receipt => host.IsDaAvailableAsync(receipt).AsTask(),
                () => host.SupportsLocalDaReader,
                () => host.CreateLocalDaReader(),
                host.ProcessDeposit,
                host.HasConsumedDeposit,
                () => host.ConsumedDepositCount,
                host.RecordRpcDeposit,
                host.GetRpcL1DepositStatus,
                () => host.ScanSharedBridgeDepositsAsync().AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.ExportPrometheusMetrics(),
                path => host.WritePrometheusMetricsAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-recover-da-offline.json")));
            var secondOutbound = AssertSoftSealAfterRecoverSecondOutboundAndFi(
                host.StageWithdrawal,
                () => host.StagedWithdrawalCount,
                host.SealWithdrawalBatch,
                msgs => host.EnqueueOutboundMessagesAsync(msgs).AsTask(),
                () => host.MessageOutbox!.L2ToL1Count,
                () => host.MessageOutboxL2ToL1Root,
                host.RegisterForcedInclusionNonce,
                () => host.KnownForcedInclusionNonceCount,
                () => host.HasOverdueForcedInclusionCached(),
                host.InvalidateForcedInclusionCache,
                () => host.OpenBatchForcedInclusionCount,
                host.RegisterInboundMessageNonce,
                () => host.KnownInboundNonceCount,
                host.InvalidateInboundMessageCache,
                () => host.OpenBatchL1MessageCount,
                () => host.L1InboxPendingCount,
                host.RecordRpcWithdrawalProof,
                host.GetRpcWithdrawalProof,
                host.RecordRpcMessageProof,
                host.GetRpcMessageProof,
                host.RecordMessageRouterFinalizedProof,
                msgHash => host.GetMessageRouterProofAsync(msgHash).AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-recover-second-outbound.json")));
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-recover-second-outbound-rpc.json")));
            AssertSoftSealSecondPoisonRecoverRetention(
                () => host.ReconcileAsync(),
                () => host.SubmitNextAsync(),
                (batch, hash) => host.RecoverPoisonedBatchAsync(batch, hash),
                () => host.IsSettlementPoisonedAsync().AsTask(),
                () => host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                host.GetRpcBatch,
                host.GetRpcBatchStatus,
                host.GetRpcStateRootAtBatch,
                host.GetLatestRpcStateRoot,
                host.HasConsumedDeposit,
                host.GetRpcL1DepositStatus,
                host.GetRpcWithdrawalProof,
                host.GetRpcMessageProof,
                secondOutbound.WithdrawalLeaf,
                secondOutbound.OutboundMessageHash,
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-second-poison-recover.json")));
            AssertSoftSealAfterSecondRecoverDaAndThirdDeposit(
                req => host.PublishDaAsync(req).AsTask(),
                receipt => host.IsDaAvailableAsync(receipt).AsTask(),
                () => host.SupportsLocalDaReader,
                () => host.CreateLocalDaReader(),
                host.ProcessDeposit,
                host.HasConsumedDeposit,
                () => host.ConsumedDepositCount,
                host.RecordRpcDeposit,
                host.GetRpcL1DepositStatus,
                () => host.ScanSharedBridgeDepositsAsync().AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.ExportPrometheusMetrics(),
                path => host.WritePrometheusMetricsAsync(path).AsTask(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-second-recover-da-deposit.json")));
            var thirdOutbound = AssertSoftSealAfterSecondRecoverThirdOutboundAndFi(
                host.StageWithdrawal,
                () => host.StagedWithdrawalCount,
                host.SealWithdrawalBatch,
                msgs => host.EnqueueOutboundMessagesAsync(msgs).AsTask(),
                () => host.MessageOutbox!.L2ToL1Count,
                () => host.MessageOutboxL2ToL1Root,
                host.RegisterForcedInclusionNonce,
                () => host.KnownForcedInclusionNonceCount,
                () => host.HasOverdueForcedInclusionCached(),
                host.InvalidateForcedInclusionCache,
                () => host.OpenBatchForcedInclusionCount,
                host.RegisterInboundMessageNonce,
                () => host.KnownInboundNonceCount,
                host.InvalidateInboundMessageCache,
                () => host.OpenBatchL1MessageCount,
                () => host.L1InboxPendingCount,
                host.RecordRpcWithdrawalProof,
                host.GetRpcWithdrawalProof,
                host.RecordRpcMessageProof,
                host.GetRpcMessageProof,
                host.RecordMessageRouterFinalizedProof,
                msgHash => host.GetMessageRouterProofAsync(msgHash).AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-second-recover-third-outbound.json")));
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-second-recover-third-outbound-rpc.json")));
            AssertSoftSealThirdPoisonRecoverRetention(
                () => host.ReconcileAsync(),
                () => host.SubmitNextAsync(),
                (batch, hash) => host.RecoverPoisonedBatchAsync(batch, hash),
                () => host.IsSettlementPoisonedAsync().AsTask(),
                () => host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                host.GetRpcBatch,
                host.GetRpcBatchStatus,
                host.GetRpcStateRootAtBatch,
                host.GetLatestRpcStateRoot,
                host.HasConsumedDeposit,
                host.GetRpcL1DepositStatus,
                host.GetRpcWithdrawalProof,
                host.GetRpcMessageProof,
                thirdOutbound.WithdrawalLeaf,
                thirdOutbound.OutboundMessageHash,
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-third-poison-recover.json")));
            AssertSoftSealAfterThirdRecoverDaAndFourthDeposit(
                req => host.PublishDaAsync(req).AsTask(),
                receipt => host.IsDaAvailableAsync(receipt).AsTask(),
                () => host.SupportsLocalDaReader,
                () => host.CreateLocalDaReader(),
                host.ProcessDeposit,
                host.HasConsumedDeposit,
                () => host.ConsumedDepositCount,
                host.RecordRpcDeposit,
                host.GetRpcL1DepositStatus,
                () => host.ScanSharedBridgeDepositsAsync().AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.ExportPrometheusMetrics(),
                path => host.WritePrometheusMetricsAsync(path).AsTask(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-third-recover-da-deposit.json")));
            var fourthOutbound = AssertSoftSealAfterThirdRecoverFourthOutboundAndFi(
                host.StageWithdrawal,
                () => host.StagedWithdrawalCount,
                host.SealWithdrawalBatch,
                msgs => host.EnqueueOutboundMessagesAsync(msgs).AsTask(),
                () => host.MessageOutbox!.L2ToL1Count,
                () => host.MessageOutboxL2ToL1Root,
                host.RegisterForcedInclusionNonce,
                () => host.KnownForcedInclusionNonceCount,
                () => host.HasOverdueForcedInclusionCached(),
                host.InvalidateForcedInclusionCache,
                () => host.OpenBatchForcedInclusionCount,
                host.RegisterInboundMessageNonce,
                () => host.KnownInboundNonceCount,
                host.InvalidateInboundMessageCache,
                () => host.OpenBatchL1MessageCount,
                () => host.L1InboxPendingCount,
                host.RecordRpcWithdrawalProof,
                host.GetRpcWithdrawalProof,
                host.RecordRpcMessageProof,
                host.GetRpcMessageProof,
                host.RecordMessageRouterFinalizedProof,
                msgHash => host.GetMessageRouterProofAsync(msgHash).AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-third-recover-fourth-outbound.json")));
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-third-recover-fourth-outbound-rpc.json")));
            Assert.AreNotEqual(UInt256.Zero, fourthOutbound.WithdrawalLeaf);
            Assert.AreNotEqual(UInt256.Zero, fourthOutbound.OutboundMessageHash);
            AssertSoftSealFourthPoisonRecoverRetention(
                () => host.ReconcileAsync(),
                () => host.SubmitNextAsync(),
                (batch, hash) => host.RecoverPoisonedBatchAsync(batch, hash),
                () => host.IsSettlementPoisonedAsync().AsTask(),
                () => host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                host.GetRpcBatch,
                host.GetRpcBatchStatus,
                host.GetRpcStateRootAtBatch,
                host.GetLatestRpcStateRoot,
                host.HasConsumedDeposit,
                host.GetRpcL1DepositStatus,
                host.GetRpcWithdrawalProof,
                host.GetRpcMessageProof,
                fourthOutbound.WithdrawalLeaf,
                fourthOutbound.OutboundMessageHash,
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-fourth-poison-recover.json")));
            AssertSoftSealAfterFourthRecoverDaAndFifthDeposit(
                req => host.PublishDaAsync(req).AsTask(),
                receipt => host.IsDaAvailableAsync(receipt).AsTask(),
                () => host.SupportsLocalDaReader,
                () => host.CreateLocalDaReader(),
                host.ProcessDeposit,
                host.HasConsumedDeposit,
                () => host.ConsumedDepositCount,
                host.RecordRpcDeposit,
                host.GetRpcL1DepositStatus,
                () => host.ScanSharedBridgeDepositsAsync().AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.ExportPrometheusMetrics(),
                path => host.WritePrometheusMetricsAsync(path).AsTask(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-fourth-recover-da-deposit.json")));
            var fifthOutbound = AssertSoftSealAfterFourthRecoverFifthOutboundAndFi(
                host.StageWithdrawal,
                () => host.StagedWithdrawalCount,
                host.SealWithdrawalBatch,
                msgs => host.EnqueueOutboundMessagesAsync(msgs).AsTask(),
                () => host.MessageOutbox!.L2ToL1Count,
                () => host.MessageOutboxL2ToL1Root,
                host.RegisterForcedInclusionNonce,
                () => host.KnownForcedInclusionNonceCount,
                () => host.HasOverdueForcedInclusionCached(),
                host.InvalidateForcedInclusionCache,
                () => host.OpenBatchForcedInclusionCount,
                host.RegisterInboundMessageNonce,
                () => host.KnownInboundNonceCount,
                host.InvalidateInboundMessageCache,
                () => host.OpenBatchL1MessageCount,
                () => host.L1InboxPendingCount,
                host.RecordRpcWithdrawalProof,
                host.GetRpcWithdrawalProof,
                host.RecordRpcMessageProof,
                host.GetRpcMessageProof,
                host.RecordMessageRouterFinalizedProof,
                msgHash => host.GetMessageRouterProofAsync(msgHash).AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-fourth-recover-fifth-outbound.json")));
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-fourth-recover-fifth-outbound-rpc.json")));
            Assert.AreNotEqual(UInt256.Zero, fifthOutbound.WithdrawalLeaf);
            Assert.AreNotEqual(UInt256.Zero, fifthOutbound.OutboundMessageHash);
            AssertSoftSealFifthPoisonRecoverRetention(
                () => host.ReconcileAsync(),
                () => host.SubmitNextAsync(),
                (batch, hash) => host.RecoverPoisonedBatchAsync(batch, hash),
                () => host.IsSettlementPoisonedAsync().AsTask(),
                () => host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                host.GetRpcBatch,
                host.GetRpcBatchStatus,
                host.GetRpcStateRootAtBatch,
                host.GetLatestRpcStateRoot,
                host.HasConsumedDeposit,
                host.GetRpcL1DepositStatus,
                host.GetRpcWithdrawalProof,
                host.GetRpcMessageProof,
                fifthOutbound.WithdrawalLeaf,
                fifthOutbound.OutboundMessageHash,
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-fifth-poison-recover.json")));
            AssertSoftSealAfterFifthRecoverDaAndSixthDeposit(
                req => host.PublishDaAsync(req).AsTask(),
                receipt => host.IsDaAvailableAsync(receipt).AsTask(),
                () => host.SupportsLocalDaReader,
                () => host.CreateLocalDaReader(),
                host.ProcessDeposit,
                host.HasConsumedDeposit,
                () => host.ConsumedDepositCount,
                host.RecordRpcDeposit,
                host.GetRpcL1DepositStatus,
                () => host.ScanSharedBridgeDepositsAsync().AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.ExportPrometheusMetrics(),
                path => host.WritePrometheusMetricsAsync(path).AsTask(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-fifth-recover-da-deposit.json")));
            var sixthOutbound = AssertSoftSealAfterFifthRecoverSixthOutboundAndFi(
                host.StageWithdrawal,
                () => host.StagedWithdrawalCount,
                host.SealWithdrawalBatch,
                msgs => host.EnqueueOutboundMessagesAsync(msgs).AsTask(),
                () => host.MessageOutbox!.L2ToL1Count,
                () => host.MessageOutboxL2ToL1Root,
                host.RegisterForcedInclusionNonce,
                () => host.KnownForcedInclusionNonceCount,
                () => host.HasOverdueForcedInclusionCached(),
                host.InvalidateForcedInclusionCache,
                () => host.OpenBatchForcedInclusionCount,
                host.RegisterInboundMessageNonce,
                () => host.KnownInboundNonceCount,
                host.InvalidateInboundMessageCache,
                () => host.OpenBatchL1MessageCount,
                () => host.L1InboxPendingCount,
                host.RecordRpcWithdrawalProof,
                host.GetRpcWithdrawalProof,
                host.RecordRpcMessageProof,
                host.GetRpcMessageProof,
                host.RecordMessageRouterFinalizedProof,
                msgHash => host.GetMessageRouterProofAsync(msgHash).AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-fifth-recover-sixth-outbound.json")));
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-fifth-recover-sixth-outbound-rpc.json")));
            Assert.AreNotEqual(UInt256.Zero, sixthOutbound.WithdrawalLeaf);
            Assert.AreNotEqual(UInt256.Zero, sixthOutbound.OutboundMessageHash);
            AssertSoftSealSixthPoisonRecoverRetention(
                () => host.ReconcileAsync(),
                () => host.SubmitNextAsync(),
                (batch, hash) => host.RecoverPoisonedBatchAsync(batch, hash),
                () => host.IsSettlementPoisonedAsync().AsTask(),
                () => host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                host.GetRpcBatch,
                host.GetRpcBatchStatus,
                host.GetRpcStateRootAtBatch,
                host.GetLatestRpcStateRoot,
                host.HasConsumedDeposit,
                host.GetRpcL1DepositStatus,
                host.GetRpcWithdrawalProof,
                host.GetRpcMessageProof,
                sixthOutbound.WithdrawalLeaf,
                sixthOutbound.OutboundMessageHash,
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-sixth-poison-recover.json")));
            AssertSoftSealAfterSixthRecoverDaAndSeventhDeposit(
                req => host.PublishDaAsync(req).AsTask(),
                receipt => host.IsDaAvailableAsync(receipt).AsTask(),
                () => host.SupportsLocalDaReader,
                () => host.CreateLocalDaReader(),
                host.ProcessDeposit,
                host.HasConsumedDeposit,
                () => host.ConsumedDepositCount,
                host.RecordRpcDeposit,
                host.GetRpcL1DepositStatus,
                () => host.ScanSharedBridgeDepositsAsync().AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.ExportPrometheusMetrics(),
                path => host.WritePrometheusMetricsAsync(path).AsTask(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-sixth-recover-da-deposit.json")));
            var seventhOutbound = AssertSoftSealAfterSixthRecoverSeventhOutboundAndFi(
                host.StageWithdrawal,
                () => host.StagedWithdrawalCount,
                host.SealWithdrawalBatch,
                msgs => host.EnqueueOutboundMessagesAsync(msgs).AsTask(),
                () => host.MessageOutbox!.L2ToL1Count,
                () => host.MessageOutboxL2ToL1Root,
                host.RegisterForcedInclusionNonce,
                () => host.KnownForcedInclusionNonceCount,
                () => host.HasOverdueForcedInclusionCached(),
                host.InvalidateForcedInclusionCache,
                () => host.OpenBatchForcedInclusionCount,
                host.RegisterInboundMessageNonce,
                () => host.KnownInboundNonceCount,
                host.InvalidateInboundMessageCache,
                () => host.OpenBatchL1MessageCount,
                () => host.L1InboxPendingCount,
                host.RecordRpcWithdrawalProof,
                host.GetRpcWithdrawalProof,
                host.RecordRpcMessageProof,
                host.GetRpcMessageProof,
                host.RecordMessageRouterFinalizedProof,
                msgHash => host.GetMessageRouterProofAsync(msgHash).AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-sixth-recover-seventh-outbound.json")));
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-sixth-recover-seventh-outbound-rpc.json")));
            Assert.AreNotEqual(UInt256.Zero, seventhOutbound.WithdrawalLeaf);
            Assert.AreNotEqual(UInt256.Zero, seventhOutbound.OutboundMessageHash);
            AssertSoftSealSeventhPoisonRecoverRetention(
                () => host.ReconcileAsync(),
                () => host.SubmitNextAsync(),
                (batch, hash) => host.RecoverPoisonedBatchAsync(batch, hash),
                () => host.IsSettlementPoisonedAsync().AsTask(),
                () => host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                host.GetRpcBatch,
                host.GetRpcBatchStatus,
                host.GetRpcStateRootAtBatch,
                host.GetLatestRpcStateRoot,
                host.HasConsumedDeposit,
                host.GetRpcL1DepositStatus,
                host.GetRpcWithdrawalProof,
                host.GetRpcMessageProof,
                seventhOutbound.WithdrawalLeaf,
                seventhOutbound.OutboundMessageHash,
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-seventh-poison-recover.json")));
            AssertSoftSealAfterSeventhRecoverDaAndEighthDeposit(
                req => host.PublishDaAsync(req).AsTask(),
                receipt => host.IsDaAvailableAsync(receipt).AsTask(),
                () => host.SupportsLocalDaReader,
                () => host.CreateLocalDaReader(),
                host.ProcessDeposit,
                host.HasConsumedDeposit,
                () => host.ConsumedDepositCount,
                host.RecordRpcDeposit,
                host.GetRpcL1DepositStatus,
                () => host.ScanSharedBridgeDepositsAsync().AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.ExportPrometheusMetrics(),
                path => host.WritePrometheusMetricsAsync(path).AsTask(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-seventh-recover-da-deposit.json")));
            var eighthOutbound = AssertSoftSealAfterSeventhRecoverEighthOutboundAndFi(
                host.StageWithdrawal,
                () => host.StagedWithdrawalCount,
                host.SealWithdrawalBatch,
                msgs => host.EnqueueOutboundMessagesAsync(msgs).AsTask(),
                () => host.MessageOutbox!.L2ToL1Count,
                () => host.MessageOutboxL2ToL1Root,
                host.RegisterForcedInclusionNonce,
                () => host.KnownForcedInclusionNonceCount,
                () => host.HasOverdueForcedInclusionCached(),
                host.InvalidateForcedInclusionCache,
                () => host.OpenBatchForcedInclusionCount,
                host.RegisterInboundMessageNonce,
                () => host.KnownInboundNonceCount,
                host.InvalidateInboundMessageCache,
                () => host.OpenBatchL1MessageCount,
                () => host.L1InboxPendingCount,
                host.RecordRpcWithdrawalProof,
                host.GetRpcWithdrawalProof,
                host.RecordRpcMessageProof,
                host.GetRpcMessageProof,
                host.RecordMessageRouterFinalizedProof,
                msgHash => host.GetMessageRouterProofAsync(msgHash).AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-seventh-recover-eighth-outbound.json")));
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-seventh-recover-eighth-outbound-rpc.json")));
            Assert.AreNotEqual(UInt256.Zero, eighthOutbound.WithdrawalLeaf);
            Assert.AreNotEqual(UInt256.Zero, eighthOutbound.OutboundMessageHash);
            AssertSoftSealEighthPoisonRecoverRetention(
                () => host.ReconcileAsync(),
                () => host.SubmitNextAsync(),
                (batch, hash) => host.RecoverPoisonedBatchAsync(batch, hash),
                () => host.IsSettlementPoisonedAsync().AsTask(),
                () => host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                host.GetRpcBatch,
                host.GetRpcBatchStatus,
                host.GetRpcStateRootAtBatch,
                host.GetLatestRpcStateRoot,
                host.HasConsumedDeposit,
                host.GetRpcL1DepositStatus,
                host.GetRpcWithdrawalProof,
                host.GetRpcMessageProof,
                eighthOutbound.WithdrawalLeaf,
                eighthOutbound.OutboundMessageHash,
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-eighth-poison-recover.json")));
            AssertSoftSealAfterEighthRecoverDaAndNinthDeposit(
                req => host.PublishDaAsync(req).AsTask(),
                receipt => host.IsDaAvailableAsync(receipt).AsTask(),
                () => host.SupportsLocalDaReader,
                () => host.CreateLocalDaReader(),
                host.ProcessDeposit,
                host.HasConsumedDeposit,
                () => host.ConsumedDepositCount,
                host.RecordRpcDeposit,
                host.GetRpcL1DepositStatus,
                () => host.ScanSharedBridgeDepositsAsync().AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.ExportPrometheusMetrics(),
                path => host.WritePrometheusMetricsAsync(path).AsTask(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-eighth-recover-da-deposit.json")));
            var ninthOutbound = AssertSoftSealAfterEighthRecoverNinthOutboundAndFi(
                host.StageWithdrawal,
                () => host.StagedWithdrawalCount,
                host.SealWithdrawalBatch,
                msgs => host.EnqueueOutboundMessagesAsync(msgs).AsTask(),
                () => host.MessageOutbox!.L2ToL1Count,
                () => host.MessageOutboxL2ToL1Root,
                host.RegisterForcedInclusionNonce,
                () => host.KnownForcedInclusionNonceCount,
                () => host.HasOverdueForcedInclusionCached(),
                host.InvalidateForcedInclusionCache,
                () => host.OpenBatchForcedInclusionCount,
                host.RegisterInboundMessageNonce,
                () => host.KnownInboundNonceCount,
                host.InvalidateInboundMessageCache,
                () => host.OpenBatchL1MessageCount,
                () => host.L1InboxPendingCount,
                host.RecordRpcWithdrawalProof,
                host.GetRpcWithdrawalProof,
                host.RecordRpcMessageProof,
                host.GetRpcMessageProof,
                host.RecordMessageRouterFinalizedProof,
                msgHash => host.GetMessageRouterProofAsync(msgHash).AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-eighth-recover-ninth-outbound.json")));
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-eighth-recover-ninth-outbound-rpc.json")));
            Assert.AreNotEqual(UInt256.Zero, ninthOutbound.WithdrawalLeaf);
            Assert.AreNotEqual(UInt256.Zero, ninthOutbound.OutboundMessageHash);
            AssertSoftSealNinthPoisonRecoverRetention(
                () => host.ReconcileAsync(),
                () => host.SubmitNextAsync(),
                (batch, hash) => host.RecoverPoisonedBatchAsync(batch, hash),
                () => host.IsSettlementPoisonedAsync().AsTask(),
                () => host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                host.GetRpcBatch,
                host.GetRpcBatchStatus,
                host.GetRpcStateRootAtBatch,
                host.GetLatestRpcStateRoot,
                host.HasConsumedDeposit,
                host.GetRpcL1DepositStatus,
                host.GetRpcWithdrawalProof,
                host.GetRpcMessageProof,
                ninthOutbound.WithdrawalLeaf,
                ninthOutbound.OutboundMessageHash,
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-ninth-poison-recover.json")));
            AssertSoftSealAfterNinthRecoverDaAndTenthDeposit(
                req => host.PublishDaAsync(req).AsTask(),
                receipt => host.IsDaAvailableAsync(receipt).AsTask(),
                () => host.SupportsLocalDaReader,
                () => host.CreateLocalDaReader(),
                host.ProcessDeposit,
                host.HasConsumedDeposit,
                () => host.ConsumedDepositCount,
                host.RecordRpcDeposit,
                host.GetRpcL1DepositStatus,
                () => host.ScanSharedBridgeDepositsAsync().AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.ExportPrometheusMetrics(),
                path => host.WritePrometheusMetricsAsync(path).AsTask(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-ninth-recover-da-deposit.json")));
            var tenthOutbound = AssertSoftSealAfterNinthRecoverTenthOutboundAndFi(
                host.StageWithdrawal,
                () => host.StagedWithdrawalCount,
                host.SealWithdrawalBatch,
                msgs => host.EnqueueOutboundMessagesAsync(msgs).AsTask(),
                () => host.MessageOutbox!.L2ToL1Count,
                () => host.MessageOutboxL2ToL1Root,
                host.RegisterForcedInclusionNonce,
                () => host.KnownForcedInclusionNonceCount,
                () => host.HasOverdueForcedInclusionCached(),
                host.InvalidateForcedInclusionCache,
                () => host.OpenBatchForcedInclusionCount,
                host.RegisterInboundMessageNonce,
                () => host.KnownInboundNonceCount,
                host.InvalidateInboundMessageCache,
                () => host.OpenBatchL1MessageCount,
                () => host.L1InboxPendingCount,
                host.RecordRpcWithdrawalProof,
                host.GetRpcWithdrawalProof,
                host.RecordRpcMessageProof,
                host.GetRpcMessageProof,
                host.RecordMessageRouterFinalizedProof,
                msgHash => host.GetMessageRouterProofAsync(msgHash).AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-ninth-recover-tenth-outbound.json")));
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-ninth-recover-tenth-outbound-rpc.json")));
            Assert.AreNotEqual(UInt256.Zero, tenthOutbound.WithdrawalLeaf);
            Assert.AreNotEqual(UInt256.Zero, tenthOutbound.OutboundMessageHash);
            AssertSoftSealTenthPoisonRecoverRetention(
                () => host.ReconcileAsync(),
                () => host.SubmitNextAsync(),
                (batch, hash) => host.RecoverPoisonedBatchAsync(batch, hash),
                () => host.IsSettlementPoisonedAsync().AsTask(),
                () => host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                host.GetRpcBatch,
                host.GetRpcBatchStatus,
                host.GetRpcStateRootAtBatch,
                host.GetLatestRpcStateRoot,
                host.HasConsumedDeposit,
                host.GetRpcL1DepositStatus,
                host.GetRpcWithdrawalProof,
                host.GetRpcMessageProof,
                tenthOutbound.WithdrawalLeaf,
                tenthOutbound.OutboundMessageHash,
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-tenth-poison-recover.json")));
            AssertSoftSealAfterTenthRecoverDaAndEleventhDeposit(
                req => host.PublishDaAsync(req).AsTask(),
                receipt => host.IsDaAvailableAsync(receipt).AsTask(),
                () => host.SupportsLocalDaReader,
                () => host.CreateLocalDaReader(),
                host.ProcessDeposit,
                host.HasConsumedDeposit,
                () => host.ConsumedDepositCount,
                host.RecordRpcDeposit,
                host.GetRpcL1DepositStatus,
                () => host.ScanSharedBridgeDepositsAsync().AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.ExportPrometheusMetrics(),
                path => host.WritePrometheusMetricsAsync(path).AsTask(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-tenth-recover-da-deposit.json")));
            var eleventhOutbound = AssertSoftSealAfterTenthRecoverEleventhOutboundAndFi(
                host.StageWithdrawal,
                () => host.StagedWithdrawalCount,
                host.SealWithdrawalBatch,
                msgs => host.EnqueueOutboundMessagesAsync(msgs).AsTask(),
                () => host.MessageOutbox!.L2ToL1Count,
                () => host.MessageOutboxL2ToL1Root,
                host.RegisterForcedInclusionNonce,
                () => host.KnownForcedInclusionNonceCount,
                () => host.HasOverdueForcedInclusionCached(),
                host.InvalidateForcedInclusionCache,
                () => host.OpenBatchForcedInclusionCount,
                host.RegisterInboundMessageNonce,
                () => host.KnownInboundNonceCount,
                host.InvalidateInboundMessageCache,
                () => host.OpenBatchL1MessageCount,
                () => host.L1InboxPendingCount,
                host.RecordRpcWithdrawalProof,
                host.GetRpcWithdrawalProof,
                host.RecordRpcMessageProof,
                host.GetRpcMessageProof,
                host.RecordMessageRouterFinalizedProof,
                msgHash => host.GetMessageRouterProofAsync(msgHash).AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-tenth-recover-eleventh-outbound.json")));
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-tenth-recover-eleventh-outbound-rpc.json")));
            Assert.AreNotEqual(UInt256.Zero, eleventhOutbound.WithdrawalLeaf);
            Assert.AreNotEqual(UInt256.Zero, eleventhOutbound.OutboundMessageHash);
            AssertSoftSealEleventhPoisonRecoverRetention(
                () => host.ReconcileAsync(),
                () => host.SubmitNextAsync(),
                (batch, hash) => host.RecoverPoisonedBatchAsync(batch, hash),
                () => host.IsSettlementPoisonedAsync().AsTask(),
                () => host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                host.GetRpcBatch,
                host.GetRpcBatchStatus,
                host.GetRpcStateRootAtBatch,
                host.GetLatestRpcStateRoot,
                host.HasConsumedDeposit,
                host.GetRpcL1DepositStatus,
                host.GetRpcWithdrawalProof,
                host.GetRpcMessageProof,
                eleventhOutbound.WithdrawalLeaf,
                eleventhOutbound.OutboundMessageHash,
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-eleventh-poison-recover.json")));
            AssertSoftSealAfterEleventhRecoverDaAndTwelfthDeposit(
                req => host.PublishDaAsync(req).AsTask(),
                receipt => host.IsDaAvailableAsync(receipt).AsTask(),
                () => host.SupportsLocalDaReader,
                () => host.CreateLocalDaReader(),
                host.ProcessDeposit,
                host.HasConsumedDeposit,
                () => host.ConsumedDepositCount,
                host.RecordRpcDeposit,
                host.GetRpcL1DepositStatus,
                () => host.ScanSharedBridgeDepositsAsync().AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.ExportPrometheusMetrics(),
                path => host.WritePrometheusMetricsAsync(path).AsTask(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-eleventh-recover-da-deposit.json")));
        }
        finally
        {
            if (Directory.Exists(chainDir))
                Directory.Delete(chainDir, recursive: true);
        }
    }

    /// <summary>
    /// Soft seal→local PersistAsync on Optimistic host from deploy report (no L1 settle claim).
    /// </summary>
    [TestMethod]
    public void OptimisticLocalHost_SoftSeal_EmptyBlock_PersistsLocalCheckpoint()
    {
        var reportPath = ResolveDeployReportPath();
        if (!File.Exists(reportPath))
            Assert.Inconclusive($"repo evidence file not found at {reportPath}");

        var chainDir = Path.Combine(
            Path.GetTempPath(),
            "neo-n4-host-opt-soft-seal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(chainDir);
        try
        {
            MaterializeChain(chainDir, reportPath, ProofType.Optimistic, DAMode.Local, "Optimistic");
            RewriteMaxBlocksPerBatch(chainDir, 1);
            using var http = MockL1HttpClient(Root(0x11));
            var sequencer = new KeyPair(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
            using var host = OptimisticLocalHostComposition.Open(
                chainDir,
                new SoftPassThroughExecutor(),
                sequencer,
                UInt160.Parse("0x" + new string('b', 40)),
                UInt256.Parse("0x" + new string('c', 64)),
                new StubSigner(Account(0x46)),
                rpcHttpClient: http,
                submittedAtUnixMs: static () => 1_700_000_000_000UL);

            Assert.AreEqual(1, host.MaxBlocksPerBatch);
            Assert.IsTrue(host.IsOperatorReady);
            Assert.IsNull(host.GetLatestDurableCheckpointAsync().AsTask().GetAwaiter().GetResult());

            var ts = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            host.ProcessCommittedBlock(1, ts, 894710606, Array.Empty<byte[]>());

            Assert.IsFalse(host.HasOpenBatch);
            Assert.IsFalse(host.HasPendingSealedBatch);
            Assert.AreEqual(2UL, host.NextExpectedBlock);
            Assert.AreEqual(1UL, host.LastAcknowledgedBatchNumber);
            Assert.AreEqual(1, host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult());

            var checkpoint = host.GetLatestDurableCheckpointAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsNotNull(checkpoint);
            Assert.AreEqual(1UL, checkpoint!.BatchNumber);
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, checkpoint.PostStateRoot);

            AssertSoftSealLocalDaSurface(
                req => host.PublishDaAsync(req).AsTask(),
                receipt => host.IsDaAvailableAsync(receipt).AsTask(),
                () => host.SupportsLocalDaReader,
                () => host.CreateLocalDaReader(),
                checkpoint.BatchNumber,
                chainDir);

            AssertSoftSealSecondBatchWhilePending(
                (idx, ts, net, txs) => host.ProcessCommittedBlock(idx, ts, net, txs),
                () => host.NextExpectedBlock,
                () => host.LastAcknowledgedBatchNumber,
                () => host.LastAcknowledgedBlock,
                () => host.NextBatchNumber,
                () => host.HasOpenBatch,
                () => host.HasPendingSealedBatch,
                () => host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetLatestDurableCheckpointAsync().AsTask().GetAwaiter().GetResult(),
                req => host.PublishDaAsync(req).AsTask(),
                receipt => host.IsDaAvailableAsync(receipt).AsTask(),
                () => host.SupportsLocalDaReader,
                () => host.CreateLocalDaReader(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                host.HasConsumedDeposit,
                chainDir,
                expectSoftOfflineBookkeeping: false);

            var status = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(2UL, status.LatestCheckpointBatchNumber);
            Assert.IsTrue(status.PendingSettlementCount >= 2);
            Assert.IsFalse(status.IsSettlementIdle);
            Assert.IsTrue(status.IsBatcherCheckpointAligned);
            Assert.IsTrue(status.IsOfflinePassportComplete);
            Assert.IsFalse(status.IsPipelineHealthy);
            Assert.IsTrue(status.IsSettlementRetrying);
            Assert.IsFalse(status.IsSettlementPoisoned);
            CollectionAssert.Contains(
                status.PipelineHealthFailures.ToArray(),
                nameof(status.IsSettlementRetrying));

            AssertSoftSealOfflineBridgeWhileRetrying(
                host.RegisterBridgeAsset,
                host.ProcessDeposit,
                host.HasConsumedDeposit,
                () => host.ConsumedDepositCount,
                host.ProcessReadyDeposits,
                host.StageWithdrawal,
                () => host.StagedWithdrawalCount,
                host.SealWithdrawalBatch,
                msgs => host.EnqueueOutboundMessagesAsync(msgs).AsTask(),
                () => host.MessageOutbox!.L2ToL1Count,
                () => host.MessageOutboxL2ToL1Root,
                host.RecordRpcDeposit,
                host.GetRpcL1DepositStatus,
                host.RegisterRpcAsset,
                host.GetRpcBridgedAsset,
                host.GetRpcCanonicalAsset,
                host.RecordRpcWithdrawalProof,
                host.GetRpcWithdrawalProof,
                host.RecordRpcMessageProof,
                host.GetRpcMessageProof,
                host.RecordMessageRouterFinalizedProof,
                msgHash => host.GetMessageRouterProofAsync(msgHash).AsTask(),
                () => host.ScanSharedBridgeDepositsAsync().AsTask(),
                () => host.ScanAndProcessReadyDepositsAsync().AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                checkpoint.BatchNumber,
                chainDir);

            AssertSoftSealFiInboundWhileRetrying(
                host.RegisterForcedInclusionNonce,
                () => host.KnownForcedInclusionNonceCount,
                () => host.HasBatchForcedInclusionSource,
                () => host.HasOverdueForcedInclusionCached(),
                host.InvalidateForcedInclusionCache,
                () => host.OpenBatchForcedInclusionCount,
                host.RegisterInboundMessageNonce,
                () => host.KnownInboundNonceCount,
                () => host.HasBatchMessageRouter,
                () => host.HasMessageRouter,
                () => host.IsMessagePipelineWiringComplete,
                host.InvalidateInboundMessageCache,
                () => host.OpenBatchL1MessageCount,
                () => host.L1InboxPendingCount,
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir,
                softNonce: 11);

            var softSealStatusPath = Path.Combine(chainDir, "soft-seal-operator-status.json");
            host.WriteOperatorStatusAsync(softSealStatusPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(softSealStatusPath));
            var softSealStatusFile = File.ReadAllText(softSealStatusPath);
            StringAssert.Contains(softSealStatusFile, "\"isSettlementRetrying\": true");
            StringAssert.Contains(softSealStatusFile, "\"latestCheckpointBatchNumber\": 2");
            StringAssert.Contains(softSealStatusFile, "IsSettlementRetrying");
            StringAssert.Contains(softSealStatusFile, "\"consumedDepositCount\": 1");
            StringAssert.Contains(softSealStatusFile, "\"knownForcedInclusionNonceCount\": 1");
            StringAssert.Contains(softSealStatusFile, "\"knownInboundNonceCount\": 1");
            var softSealProbePath = Path.Combine(chainDir, "soft-seal-health-probe.json");
            host.WriteHealthProbeAsync(softSealProbePath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(softSealProbePath));
            var softSealProbeFile = File.ReadAllText(softSealProbePath);
            StringAssert.Contains(softSealProbeFile, "\"isSettlementRetrying\": true");
            StringAssert.Contains(softSealProbeFile, "\"latestCheckpointBatchNumber\": 2");
            StringAssert.Contains(softSealProbeFile, "IsSettlementRetrying");
            StringAssert.Contains(softSealProbeFile, "\"consumedDepositCount\": 1");
            StringAssert.Contains(softSealProbeFile, "\"knownForcedInclusionNonceCount\": 1");
            StringAssert.Contains(softSealProbeFile, "\"knownInboundNonceCount\": 1");

            // Multi-batch soft queue still pending L1 (defer SubmitNext until poison path).
            Assert.IsTrue(host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult() >= 2);
            Assert.IsFalse(host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult().IsSettlementIdle);
            Assert.IsTrue(host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult().IsSettlementRetrying);

            var checkpoint2 = host.GetLatestDurableCheckpointAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsNotNull(checkpoint2);
            Assert.AreEqual(2UL, checkpoint2!.BatchNumber);
            AssertSoftSealFeedsGatewayReceiveBatch(
                host.AddRpcBatch,
                host.FinalizeRpcBatch,
                host.GetRpcBatch,
                host.GetLatestRpcStateRoot,
                checkpoint,
                chainDir,
                host.Metrics,
                ProofType.Optimistic,
                Account(0x57),
                secondCheckpoint: checkpoint2,
                getRpcStateRootAtBatch: host.GetRpcStateRootAtBatch);
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, host.GetLatestRpcStateRoot());
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, host.GetRpcStateRootAtBatch(1));
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, host.GetRpcStateRootAtBatch(2));
            Assert.IsNotNull(host.GetRpcBatch(2));
            var statusAfterGw = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, statusAfterGw.LatestRpcStateRoot);
            Assert.IsTrue(statusAfterGw.IsSettlementRetrying);
            Assert.IsFalse(statusAfterGw.IsSettlementIdle);
            Assert.IsTrue(statusAfterGw.PendingSettlementCount >= 2);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-multi-batch-rpc-gateway.json")));
            var hostProm = host.ExportPrometheusMetrics();
            Assert.IsFalse(string.IsNullOrWhiteSpace(hostProm));
            var hostPromPath = Path.Combine(chainDir, "soft-seal-host.prom");
            host.WritePrometheusMetricsAsync(hostPromPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(hostPromPath));
            Assert.AreEqual(hostProm, File.ReadAllText(hostPromPath));

            // Multisig E2E SoftSeal parity: mock reconcile → Poisoned → local RecoverPoisonedBatch.
            Assert.ThrowsExactly<OverflowException>(
                () => host.ReconcileAsync().GetAwaiter().GetResult());
            host.SubmitNextAsync().GetAwaiter().GetResult();
            var afterPoison = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(afterPoison.IsSettlementPoisoned);
            Assert.IsFalse(afterPoison.IsSettlementRetrying);
            CollectionAssert.Contains(
                afterPoison.PipelineHealthFailures.ToArray(),
                nameof(afterPoison.IsSettlementPoisoned));
            Assert.IsNotNull(afterPoison.Recovery.BlockedBatchNumber);
            Assert.IsNotNull(afterPoison.Recovery.ArtifactContentHash);
            var blockedBatch = afterPoison.Recovery.BlockedBatchNumber!.Value;
            var contentHash = afterPoison.Recovery.ArtifactContentHash!;
            Assert.ThrowsExactly<InvalidOperationException>(
                () => host.RecoverPoisonedBatchAsync(blockedBatch, UInt256.Zero)
                    .GetAwaiter().GetResult());
            Assert.IsTrue(host.IsSettlementPoisonedAsync().AsTask().GetAwaiter().GetResult());
            host.RecoverPoisonedBatchAsync(blockedBatch, contentHash).GetAwaiter().GetResult();
            var afterRecover = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsFalse(afterRecover.IsSettlementPoisoned);
            Assert.IsTrue(afterRecover.IsSettlementRetrying);
            Assert.AreEqual(SettlementRecoveryState.Retrying, afterRecover.Recovery.State);
            Assert.AreEqual(0, afterRecover.Recovery.RetryCount);
            Assert.IsTrue(host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult() >= 1);
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, afterRecover.LatestRpcStateRoot);
            Assert.IsFalse(afterRecover.IsLocalHostHealthy);
            CollectionAssert.Contains(
                afterRecover.LocalHostHealthFailures.ToArray(),
                nameof(afterRecover.IsSettlementRetrying));
            Assert.IsTrue(afterRecover.IsOfflinePassportComplete);
            Assert.IsTrue(afterRecover.IsBatcherCheckpointAligned);
            Assert.IsTrue(afterRecover.IsOperatorReady);
            AssertSoftSealAfterRecoverSoftStateRetention(
                afterRecover,
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                host.HasConsumedDeposit,
                host.GetRpcL1DepositStatus,
                chainDir,
                getRpcBatch: host.GetRpcBatch,
                getRpcStateRootAtBatch: host.GetRpcStateRootAtBatch,
                getRpcBatchStatus: host.GetRpcBatchStatus,
                getLatestRpcStateRoot: host.GetLatestRpcStateRoot);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-recover-multi-batch-rpc.json")));
            var afterRecoverStatusPath = Path.Combine(chainDir, "soft-seal-after-recover-status.json");
            host.WriteOperatorStatusAsync(afterRecoverStatusPath).AsTask().GetAwaiter().GetResult();
            StringAssert.Contains(File.ReadAllText(afterRecoverStatusPath), "\"isSettlementRetrying\": true");
            StringAssert.Contains(File.ReadAllText(afterRecoverStatusPath), "\"isOfflinePassportComplete\": true");
            StringAssert.Contains(File.ReadAllText(afterRecoverStatusPath), "\"consumedDepositCount\": 1");
            StringAssert.Contains(File.ReadAllText(afterRecoverStatusPath), "\"knownForcedInclusionNonceCount\": 1");
            StringAssert.Contains(File.ReadAllText(afterRecoverStatusPath), "\"knownInboundNonceCount\": 1");
            var afterRecoverProbePath = Path.Combine(chainDir, "soft-seal-after-recover-probe.json");
            host.WriteHealthProbeAsync(afterRecoverProbePath).AsTask().GetAwaiter().GetResult();
            StringAssert.Contains(File.ReadAllText(afterRecoverProbePath), "\"isSettlementRetrying\": true");
            StringAssert.Contains(File.ReadAllText(afterRecoverProbePath), "\"consumedDepositCount\": 1");
            StringAssert.Contains(File.ReadAllText(afterRecoverProbePath), "\"knownForcedInclusionNonceCount\": 1");
            StringAssert.Contains(File.ReadAllText(afterRecoverProbePath), "\"knownInboundNonceCount\": 1");
            StringAssert.Contains(File.ReadAllText(afterRecoverStatusPath), "\"latestCheckpointBatchNumber\": 2");
            StringAssert.Contains(File.ReadAllText(afterRecoverProbePath), "\"latestCheckpointBatchNumber\": 2");
            StringAssert.Contains(
                File.ReadAllText(afterRecoverStatusPath),
                "\"latestRpcStateRoot\": \"" + SoftPassThroughExecutor.PostStateRoot + "\"");

            AssertSoftSealAfterRecoverDaAndSecondDeposit(
                req => host.PublishDaAsync(req).AsTask(),
                receipt => host.IsDaAvailableAsync(receipt).AsTask(),
                () => host.SupportsLocalDaReader,
                () => host.CreateLocalDaReader(),
                host.ProcessDeposit,
                host.HasConsumedDeposit,
                () => host.ConsumedDepositCount,
                host.RecordRpcDeposit,
                host.GetRpcL1DepositStatus,
                () => host.ScanSharedBridgeDepositsAsync().AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.ExportPrometheusMetrics(),
                path => host.WritePrometheusMetricsAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-recover-da-offline.json")));
            var secondOutbound = AssertSoftSealAfterRecoverSecondOutboundAndFi(
                host.StageWithdrawal,
                () => host.StagedWithdrawalCount,
                host.SealWithdrawalBatch,
                msgs => host.EnqueueOutboundMessagesAsync(msgs).AsTask(),
                () => host.MessageOutbox!.L2ToL1Count,
                () => host.MessageOutboxL2ToL1Root,
                host.RegisterForcedInclusionNonce,
                () => host.KnownForcedInclusionNonceCount,
                () => host.HasOverdueForcedInclusionCached(),
                host.InvalidateForcedInclusionCache,
                () => host.OpenBatchForcedInclusionCount,
                host.RegisterInboundMessageNonce,
                () => host.KnownInboundNonceCount,
                host.InvalidateInboundMessageCache,
                () => host.OpenBatchL1MessageCount,
                () => host.L1InboxPendingCount,
                host.RecordRpcWithdrawalProof,
                host.GetRpcWithdrawalProof,
                host.RecordRpcMessageProof,
                host.GetRpcMessageProof,
                host.RecordMessageRouterFinalizedProof,
                msgHash => host.GetMessageRouterProofAsync(msgHash).AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-recover-second-outbound.json")));
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-recover-second-outbound-rpc.json")));
            AssertSoftSealSecondPoisonRecoverRetention(
                () => host.ReconcileAsync(),
                () => host.SubmitNextAsync(),
                (batch, hash) => host.RecoverPoisonedBatchAsync(batch, hash),
                () => host.IsSettlementPoisonedAsync().AsTask(),
                () => host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                host.GetRpcBatch,
                host.GetRpcBatchStatus,
                host.GetRpcStateRootAtBatch,
                host.GetLatestRpcStateRoot,
                host.HasConsumedDeposit,
                host.GetRpcL1DepositStatus,
                host.GetRpcWithdrawalProof,
                host.GetRpcMessageProof,
                secondOutbound.WithdrawalLeaf,
                secondOutbound.OutboundMessageHash,
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-second-poison-recover.json")));
            AssertSoftSealAfterSecondRecoverDaAndThirdDeposit(
                req => host.PublishDaAsync(req).AsTask(),
                receipt => host.IsDaAvailableAsync(receipt).AsTask(),
                () => host.SupportsLocalDaReader,
                () => host.CreateLocalDaReader(),
                host.ProcessDeposit,
                host.HasConsumedDeposit,
                () => host.ConsumedDepositCount,
                host.RecordRpcDeposit,
                host.GetRpcL1DepositStatus,
                () => host.ScanSharedBridgeDepositsAsync().AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.ExportPrometheusMetrics(),
                path => host.WritePrometheusMetricsAsync(path).AsTask(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-second-recover-da-deposit.json")));
            var thirdOutbound = AssertSoftSealAfterSecondRecoverThirdOutboundAndFi(
                host.StageWithdrawal,
                () => host.StagedWithdrawalCount,
                host.SealWithdrawalBatch,
                msgs => host.EnqueueOutboundMessagesAsync(msgs).AsTask(),
                () => host.MessageOutbox!.L2ToL1Count,
                () => host.MessageOutboxL2ToL1Root,
                host.RegisterForcedInclusionNonce,
                () => host.KnownForcedInclusionNonceCount,
                () => host.HasOverdueForcedInclusionCached(),
                host.InvalidateForcedInclusionCache,
                () => host.OpenBatchForcedInclusionCount,
                host.RegisterInboundMessageNonce,
                () => host.KnownInboundNonceCount,
                host.InvalidateInboundMessageCache,
                () => host.OpenBatchL1MessageCount,
                () => host.L1InboxPendingCount,
                host.RecordRpcWithdrawalProof,
                host.GetRpcWithdrawalProof,
                host.RecordRpcMessageProof,
                host.GetRpcMessageProof,
                host.RecordMessageRouterFinalizedProof,
                msgHash => host.GetMessageRouterProofAsync(msgHash).AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-second-recover-third-outbound.json")));
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-second-recover-third-outbound-rpc.json")));
            AssertSoftSealThirdPoisonRecoverRetention(
                () => host.ReconcileAsync(),
                () => host.SubmitNextAsync(),
                (batch, hash) => host.RecoverPoisonedBatchAsync(batch, hash),
                () => host.IsSettlementPoisonedAsync().AsTask(),
                () => host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                host.GetRpcBatch,
                host.GetRpcBatchStatus,
                host.GetRpcStateRootAtBatch,
                host.GetLatestRpcStateRoot,
                host.HasConsumedDeposit,
                host.GetRpcL1DepositStatus,
                host.GetRpcWithdrawalProof,
                host.GetRpcMessageProof,
                thirdOutbound.WithdrawalLeaf,
                thirdOutbound.OutboundMessageHash,
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-third-poison-recover.json")));
            AssertSoftSealAfterThirdRecoverDaAndFourthDeposit(
                req => host.PublishDaAsync(req).AsTask(),
                receipt => host.IsDaAvailableAsync(receipt).AsTask(),
                () => host.SupportsLocalDaReader,
                () => host.CreateLocalDaReader(),
                host.ProcessDeposit,
                host.HasConsumedDeposit,
                () => host.ConsumedDepositCount,
                host.RecordRpcDeposit,
                host.GetRpcL1DepositStatus,
                () => host.ScanSharedBridgeDepositsAsync().AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.ExportPrometheusMetrics(),
                path => host.WritePrometheusMetricsAsync(path).AsTask(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-third-recover-da-deposit.json")));
            var fourthOutbound = AssertSoftSealAfterThirdRecoverFourthOutboundAndFi(
                host.StageWithdrawal,
                () => host.StagedWithdrawalCount,
                host.SealWithdrawalBatch,
                msgs => host.EnqueueOutboundMessagesAsync(msgs).AsTask(),
                () => host.MessageOutbox!.L2ToL1Count,
                () => host.MessageOutboxL2ToL1Root,
                host.RegisterForcedInclusionNonce,
                () => host.KnownForcedInclusionNonceCount,
                () => host.HasOverdueForcedInclusionCached(),
                host.InvalidateForcedInclusionCache,
                () => host.OpenBatchForcedInclusionCount,
                host.RegisterInboundMessageNonce,
                () => host.KnownInboundNonceCount,
                host.InvalidateInboundMessageCache,
                () => host.OpenBatchL1MessageCount,
                () => host.L1InboxPendingCount,
                host.RecordRpcWithdrawalProof,
                host.GetRpcWithdrawalProof,
                host.RecordRpcMessageProof,
                host.GetRpcMessageProof,
                host.RecordMessageRouterFinalizedProof,
                msgHash => host.GetMessageRouterProofAsync(msgHash).AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-third-recover-fourth-outbound.json")));
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-third-recover-fourth-outbound-rpc.json")));
            Assert.AreNotEqual(UInt256.Zero, fourthOutbound.WithdrawalLeaf);
            Assert.AreNotEqual(UInt256.Zero, fourthOutbound.OutboundMessageHash);
            AssertSoftSealFourthPoisonRecoverRetention(
                () => host.ReconcileAsync(),
                () => host.SubmitNextAsync(),
                (batch, hash) => host.RecoverPoisonedBatchAsync(batch, hash),
                () => host.IsSettlementPoisonedAsync().AsTask(),
                () => host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                host.GetRpcBatch,
                host.GetRpcBatchStatus,
                host.GetRpcStateRootAtBatch,
                host.GetLatestRpcStateRoot,
                host.HasConsumedDeposit,
                host.GetRpcL1DepositStatus,
                host.GetRpcWithdrawalProof,
                host.GetRpcMessageProof,
                fourthOutbound.WithdrawalLeaf,
                fourthOutbound.OutboundMessageHash,
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-fourth-poison-recover.json")));
            AssertSoftSealAfterFourthRecoverDaAndFifthDeposit(
                req => host.PublishDaAsync(req).AsTask(),
                receipt => host.IsDaAvailableAsync(receipt).AsTask(),
                () => host.SupportsLocalDaReader,
                () => host.CreateLocalDaReader(),
                host.ProcessDeposit,
                host.HasConsumedDeposit,
                () => host.ConsumedDepositCount,
                host.RecordRpcDeposit,
                host.GetRpcL1DepositStatus,
                () => host.ScanSharedBridgeDepositsAsync().AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.ExportPrometheusMetrics(),
                path => host.WritePrometheusMetricsAsync(path).AsTask(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-fourth-recover-da-deposit.json")));
            var fifthOutbound = AssertSoftSealAfterFourthRecoverFifthOutboundAndFi(
                host.StageWithdrawal,
                () => host.StagedWithdrawalCount,
                host.SealWithdrawalBatch,
                msgs => host.EnqueueOutboundMessagesAsync(msgs).AsTask(),
                () => host.MessageOutbox!.L2ToL1Count,
                () => host.MessageOutboxL2ToL1Root,
                host.RegisterForcedInclusionNonce,
                () => host.KnownForcedInclusionNonceCount,
                () => host.HasOverdueForcedInclusionCached(),
                host.InvalidateForcedInclusionCache,
                () => host.OpenBatchForcedInclusionCount,
                host.RegisterInboundMessageNonce,
                () => host.KnownInboundNonceCount,
                host.InvalidateInboundMessageCache,
                () => host.OpenBatchL1MessageCount,
                () => host.L1InboxPendingCount,
                host.RecordRpcWithdrawalProof,
                host.GetRpcWithdrawalProof,
                host.RecordRpcMessageProof,
                host.GetRpcMessageProof,
                host.RecordMessageRouterFinalizedProof,
                msgHash => host.GetMessageRouterProofAsync(msgHash).AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-fourth-recover-fifth-outbound.json")));
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-fourth-recover-fifth-outbound-rpc.json")));
            Assert.AreNotEqual(UInt256.Zero, fifthOutbound.WithdrawalLeaf);
            Assert.AreNotEqual(UInt256.Zero, fifthOutbound.OutboundMessageHash);
            AssertSoftSealFifthPoisonRecoverRetention(
                () => host.ReconcileAsync(),
                () => host.SubmitNextAsync(),
                (batch, hash) => host.RecoverPoisonedBatchAsync(batch, hash),
                () => host.IsSettlementPoisonedAsync().AsTask(),
                () => host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                host.GetRpcBatch,
                host.GetRpcBatchStatus,
                host.GetRpcStateRootAtBatch,
                host.GetLatestRpcStateRoot,
                host.HasConsumedDeposit,
                host.GetRpcL1DepositStatus,
                host.GetRpcWithdrawalProof,
                host.GetRpcMessageProof,
                fifthOutbound.WithdrawalLeaf,
                fifthOutbound.OutboundMessageHash,
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-fifth-poison-recover.json")));
            AssertSoftSealAfterFifthRecoverDaAndSixthDeposit(
                req => host.PublishDaAsync(req).AsTask(),
                receipt => host.IsDaAvailableAsync(receipt).AsTask(),
                () => host.SupportsLocalDaReader,
                () => host.CreateLocalDaReader(),
                host.ProcessDeposit,
                host.HasConsumedDeposit,
                () => host.ConsumedDepositCount,
                host.RecordRpcDeposit,
                host.GetRpcL1DepositStatus,
                () => host.ScanSharedBridgeDepositsAsync().AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.ExportPrometheusMetrics(),
                path => host.WritePrometheusMetricsAsync(path).AsTask(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-fifth-recover-da-deposit.json")));
            var sixthOutbound = AssertSoftSealAfterFifthRecoverSixthOutboundAndFi(
                host.StageWithdrawal,
                () => host.StagedWithdrawalCount,
                host.SealWithdrawalBatch,
                msgs => host.EnqueueOutboundMessagesAsync(msgs).AsTask(),
                () => host.MessageOutbox!.L2ToL1Count,
                () => host.MessageOutboxL2ToL1Root,
                host.RegisterForcedInclusionNonce,
                () => host.KnownForcedInclusionNonceCount,
                () => host.HasOverdueForcedInclusionCached(),
                host.InvalidateForcedInclusionCache,
                () => host.OpenBatchForcedInclusionCount,
                host.RegisterInboundMessageNonce,
                () => host.KnownInboundNonceCount,
                host.InvalidateInboundMessageCache,
                () => host.OpenBatchL1MessageCount,
                () => host.L1InboxPendingCount,
                host.RecordRpcWithdrawalProof,
                host.GetRpcWithdrawalProof,
                host.RecordRpcMessageProof,
                host.GetRpcMessageProof,
                host.RecordMessageRouterFinalizedProof,
                msgHash => host.GetMessageRouterProofAsync(msgHash).AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-fifth-recover-sixth-outbound.json")));
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-fifth-recover-sixth-outbound-rpc.json")));
            Assert.AreNotEqual(UInt256.Zero, sixthOutbound.WithdrawalLeaf);
            Assert.AreNotEqual(UInt256.Zero, sixthOutbound.OutboundMessageHash);
            AssertSoftSealSixthPoisonRecoverRetention(
                () => host.ReconcileAsync(),
                () => host.SubmitNextAsync(),
                (batch, hash) => host.RecoverPoisonedBatchAsync(batch, hash),
                () => host.IsSettlementPoisonedAsync().AsTask(),
                () => host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                host.GetRpcBatch,
                host.GetRpcBatchStatus,
                host.GetRpcStateRootAtBatch,
                host.GetLatestRpcStateRoot,
                host.HasConsumedDeposit,
                host.GetRpcL1DepositStatus,
                host.GetRpcWithdrawalProof,
                host.GetRpcMessageProof,
                sixthOutbound.WithdrawalLeaf,
                sixthOutbound.OutboundMessageHash,
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-sixth-poison-recover.json")));
            AssertSoftSealAfterSixthRecoverDaAndSeventhDeposit(
                req => host.PublishDaAsync(req).AsTask(),
                receipt => host.IsDaAvailableAsync(receipt).AsTask(),
                () => host.SupportsLocalDaReader,
                () => host.CreateLocalDaReader(),
                host.ProcessDeposit,
                host.HasConsumedDeposit,
                () => host.ConsumedDepositCount,
                host.RecordRpcDeposit,
                host.GetRpcL1DepositStatus,
                () => host.ScanSharedBridgeDepositsAsync().AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.ExportPrometheusMetrics(),
                path => host.WritePrometheusMetricsAsync(path).AsTask(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-sixth-recover-da-deposit.json")));
            var seventhOutbound = AssertSoftSealAfterSixthRecoverSeventhOutboundAndFi(
                host.StageWithdrawal,
                () => host.StagedWithdrawalCount,
                host.SealWithdrawalBatch,
                msgs => host.EnqueueOutboundMessagesAsync(msgs).AsTask(),
                () => host.MessageOutbox!.L2ToL1Count,
                () => host.MessageOutboxL2ToL1Root,
                host.RegisterForcedInclusionNonce,
                () => host.KnownForcedInclusionNonceCount,
                () => host.HasOverdueForcedInclusionCached(),
                host.InvalidateForcedInclusionCache,
                () => host.OpenBatchForcedInclusionCount,
                host.RegisterInboundMessageNonce,
                () => host.KnownInboundNonceCount,
                host.InvalidateInboundMessageCache,
                () => host.OpenBatchL1MessageCount,
                () => host.L1InboxPendingCount,
                host.RecordRpcWithdrawalProof,
                host.GetRpcWithdrawalProof,
                host.RecordRpcMessageProof,
                host.GetRpcMessageProof,
                host.RecordMessageRouterFinalizedProof,
                msgHash => host.GetMessageRouterProofAsync(msgHash).AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-sixth-recover-seventh-outbound.json")));
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-sixth-recover-seventh-outbound-rpc.json")));
            Assert.AreNotEqual(UInt256.Zero, seventhOutbound.WithdrawalLeaf);
            Assert.AreNotEqual(UInt256.Zero, seventhOutbound.OutboundMessageHash);
            AssertSoftSealSeventhPoisonRecoverRetention(
                () => host.ReconcileAsync(),
                () => host.SubmitNextAsync(),
                (batch, hash) => host.RecoverPoisonedBatchAsync(batch, hash),
                () => host.IsSettlementPoisonedAsync().AsTask(),
                () => host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                host.GetRpcBatch,
                host.GetRpcBatchStatus,
                host.GetRpcStateRootAtBatch,
                host.GetLatestRpcStateRoot,
                host.HasConsumedDeposit,
                host.GetRpcL1DepositStatus,
                host.GetRpcWithdrawalProof,
                host.GetRpcMessageProof,
                seventhOutbound.WithdrawalLeaf,
                seventhOutbound.OutboundMessageHash,
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-seventh-poison-recover.json")));
            AssertSoftSealAfterSeventhRecoverDaAndEighthDeposit(
                req => host.PublishDaAsync(req).AsTask(),
                receipt => host.IsDaAvailableAsync(receipt).AsTask(),
                () => host.SupportsLocalDaReader,
                () => host.CreateLocalDaReader(),
                host.ProcessDeposit,
                host.HasConsumedDeposit,
                () => host.ConsumedDepositCount,
                host.RecordRpcDeposit,
                host.GetRpcL1DepositStatus,
                () => host.ScanSharedBridgeDepositsAsync().AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.ExportPrometheusMetrics(),
                path => host.WritePrometheusMetricsAsync(path).AsTask(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-seventh-recover-da-deposit.json")));
            var eighthOutbound = AssertSoftSealAfterSeventhRecoverEighthOutboundAndFi(
                host.StageWithdrawal,
                () => host.StagedWithdrawalCount,
                host.SealWithdrawalBatch,
                msgs => host.EnqueueOutboundMessagesAsync(msgs).AsTask(),
                () => host.MessageOutbox!.L2ToL1Count,
                () => host.MessageOutboxL2ToL1Root,
                host.RegisterForcedInclusionNonce,
                () => host.KnownForcedInclusionNonceCount,
                () => host.HasOverdueForcedInclusionCached(),
                host.InvalidateForcedInclusionCache,
                () => host.OpenBatchForcedInclusionCount,
                host.RegisterInboundMessageNonce,
                () => host.KnownInboundNonceCount,
                host.InvalidateInboundMessageCache,
                () => host.OpenBatchL1MessageCount,
                () => host.L1InboxPendingCount,
                host.RecordRpcWithdrawalProof,
                host.GetRpcWithdrawalProof,
                host.RecordRpcMessageProof,
                host.GetRpcMessageProof,
                host.RecordMessageRouterFinalizedProof,
                msgHash => host.GetMessageRouterProofAsync(msgHash).AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-seventh-recover-eighth-outbound.json")));
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-seventh-recover-eighth-outbound-rpc.json")));
            Assert.AreNotEqual(UInt256.Zero, eighthOutbound.WithdrawalLeaf);
            Assert.AreNotEqual(UInt256.Zero, eighthOutbound.OutboundMessageHash);
            AssertSoftSealEighthPoisonRecoverRetention(
                () => host.ReconcileAsync(),
                () => host.SubmitNextAsync(),
                (batch, hash) => host.RecoverPoisonedBatchAsync(batch, hash),
                () => host.IsSettlementPoisonedAsync().AsTask(),
                () => host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                host.GetRpcBatch,
                host.GetRpcBatchStatus,
                host.GetRpcStateRootAtBatch,
                host.GetLatestRpcStateRoot,
                host.HasConsumedDeposit,
                host.GetRpcL1DepositStatus,
                host.GetRpcWithdrawalProof,
                host.GetRpcMessageProof,
                eighthOutbound.WithdrawalLeaf,
                eighthOutbound.OutboundMessageHash,
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-eighth-poison-recover.json")));
            AssertSoftSealAfterEighthRecoverDaAndNinthDeposit(
                req => host.PublishDaAsync(req).AsTask(),
                receipt => host.IsDaAvailableAsync(receipt).AsTask(),
                () => host.SupportsLocalDaReader,
                () => host.CreateLocalDaReader(),
                host.ProcessDeposit,
                host.HasConsumedDeposit,
                () => host.ConsumedDepositCount,
                host.RecordRpcDeposit,
                host.GetRpcL1DepositStatus,
                () => host.ScanSharedBridgeDepositsAsync().AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.ExportPrometheusMetrics(),
                path => host.WritePrometheusMetricsAsync(path).AsTask(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-eighth-recover-da-deposit.json")));
            var ninthOutbound = AssertSoftSealAfterEighthRecoverNinthOutboundAndFi(
                host.StageWithdrawal,
                () => host.StagedWithdrawalCount,
                host.SealWithdrawalBatch,
                msgs => host.EnqueueOutboundMessagesAsync(msgs).AsTask(),
                () => host.MessageOutbox!.L2ToL1Count,
                () => host.MessageOutboxL2ToL1Root,
                host.RegisterForcedInclusionNonce,
                () => host.KnownForcedInclusionNonceCount,
                () => host.HasOverdueForcedInclusionCached(),
                host.InvalidateForcedInclusionCache,
                () => host.OpenBatchForcedInclusionCount,
                host.RegisterInboundMessageNonce,
                () => host.KnownInboundNonceCount,
                host.InvalidateInboundMessageCache,
                () => host.OpenBatchL1MessageCount,
                () => host.L1InboxPendingCount,
                host.RecordRpcWithdrawalProof,
                host.GetRpcWithdrawalProof,
                host.RecordRpcMessageProof,
                host.GetRpcMessageProof,
                host.RecordMessageRouterFinalizedProof,
                msgHash => host.GetMessageRouterProofAsync(msgHash).AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-eighth-recover-ninth-outbound.json")));
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-eighth-recover-ninth-outbound-rpc.json")));
            Assert.AreNotEqual(UInt256.Zero, ninthOutbound.WithdrawalLeaf);
            Assert.AreNotEqual(UInt256.Zero, ninthOutbound.OutboundMessageHash);
            AssertSoftSealNinthPoisonRecoverRetention(
                () => host.ReconcileAsync(),
                () => host.SubmitNextAsync(),
                (batch, hash) => host.RecoverPoisonedBatchAsync(batch, hash),
                () => host.IsSettlementPoisonedAsync().AsTask(),
                () => host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                host.GetRpcBatch,
                host.GetRpcBatchStatus,
                host.GetRpcStateRootAtBatch,
                host.GetLatestRpcStateRoot,
                host.HasConsumedDeposit,
                host.GetRpcL1DepositStatus,
                host.GetRpcWithdrawalProof,
                host.GetRpcMessageProof,
                ninthOutbound.WithdrawalLeaf,
                ninthOutbound.OutboundMessageHash,
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-ninth-poison-recover.json")));
            AssertSoftSealAfterNinthRecoverDaAndTenthDeposit(
                req => host.PublishDaAsync(req).AsTask(),
                receipt => host.IsDaAvailableAsync(receipt).AsTask(),
                () => host.SupportsLocalDaReader,
                () => host.CreateLocalDaReader(),
                host.ProcessDeposit,
                host.HasConsumedDeposit,
                () => host.ConsumedDepositCount,
                host.RecordRpcDeposit,
                host.GetRpcL1DepositStatus,
                () => host.ScanSharedBridgeDepositsAsync().AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.ExportPrometheusMetrics(),
                path => host.WritePrometheusMetricsAsync(path).AsTask(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-ninth-recover-da-deposit.json")));
            var tenthOutbound = AssertSoftSealAfterNinthRecoverTenthOutboundAndFi(
                host.StageWithdrawal,
                () => host.StagedWithdrawalCount,
                host.SealWithdrawalBatch,
                msgs => host.EnqueueOutboundMessagesAsync(msgs).AsTask(),
                () => host.MessageOutbox!.L2ToL1Count,
                () => host.MessageOutboxL2ToL1Root,
                host.RegisterForcedInclusionNonce,
                () => host.KnownForcedInclusionNonceCount,
                () => host.HasOverdueForcedInclusionCached(),
                host.InvalidateForcedInclusionCache,
                () => host.OpenBatchForcedInclusionCount,
                host.RegisterInboundMessageNonce,
                () => host.KnownInboundNonceCount,
                host.InvalidateInboundMessageCache,
                () => host.OpenBatchL1MessageCount,
                () => host.L1InboxPendingCount,
                host.RecordRpcWithdrawalProof,
                host.GetRpcWithdrawalProof,
                host.RecordRpcMessageProof,
                host.GetRpcMessageProof,
                host.RecordMessageRouterFinalizedProof,
                msgHash => host.GetMessageRouterProofAsync(msgHash).AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-ninth-recover-tenth-outbound.json")));
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-ninth-recover-tenth-outbound-rpc.json")));
            Assert.AreNotEqual(UInt256.Zero, tenthOutbound.WithdrawalLeaf);
            Assert.AreNotEqual(UInt256.Zero, tenthOutbound.OutboundMessageHash);
            AssertSoftSealTenthPoisonRecoverRetention(
                () => host.ReconcileAsync(),
                () => host.SubmitNextAsync(),
                (batch, hash) => host.RecoverPoisonedBatchAsync(batch, hash),
                () => host.IsSettlementPoisonedAsync().AsTask(),
                () => host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                host.GetRpcBatch,
                host.GetRpcBatchStatus,
                host.GetRpcStateRootAtBatch,
                host.GetLatestRpcStateRoot,
                host.HasConsumedDeposit,
                host.GetRpcL1DepositStatus,
                host.GetRpcWithdrawalProof,
                host.GetRpcMessageProof,
                tenthOutbound.WithdrawalLeaf,
                tenthOutbound.OutboundMessageHash,
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-tenth-poison-recover.json")));
            AssertSoftSealAfterTenthRecoverDaAndEleventhDeposit(
                req => host.PublishDaAsync(req).AsTask(),
                receipt => host.IsDaAvailableAsync(receipt).AsTask(),
                () => host.SupportsLocalDaReader,
                () => host.CreateLocalDaReader(),
                host.ProcessDeposit,
                host.HasConsumedDeposit,
                () => host.ConsumedDepositCount,
                host.RecordRpcDeposit,
                host.GetRpcL1DepositStatus,
                () => host.ScanSharedBridgeDepositsAsync().AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.ExportPrometheusMetrics(),
                path => host.WritePrometheusMetricsAsync(path).AsTask(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-tenth-recover-da-deposit.json")));
            var eleventhOutbound = AssertSoftSealAfterTenthRecoverEleventhOutboundAndFi(
                host.StageWithdrawal,
                () => host.StagedWithdrawalCount,
                host.SealWithdrawalBatch,
                msgs => host.EnqueueOutboundMessagesAsync(msgs).AsTask(),
                () => host.MessageOutbox!.L2ToL1Count,
                () => host.MessageOutboxL2ToL1Root,
                host.RegisterForcedInclusionNonce,
                () => host.KnownForcedInclusionNonceCount,
                () => host.HasOverdueForcedInclusionCached(),
                host.InvalidateForcedInclusionCache,
                () => host.OpenBatchForcedInclusionCount,
                host.RegisterInboundMessageNonce,
                () => host.KnownInboundNonceCount,
                host.InvalidateInboundMessageCache,
                () => host.OpenBatchL1MessageCount,
                () => host.L1InboxPendingCount,
                host.RecordRpcWithdrawalProof,
                host.GetRpcWithdrawalProof,
                host.RecordRpcMessageProof,
                host.GetRpcMessageProof,
                host.RecordMessageRouterFinalizedProof,
                msgHash => host.GetMessageRouterProofAsync(msgHash).AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-tenth-recover-eleventh-outbound.json")));
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-tenth-recover-eleventh-outbound-rpc.json")));
            Assert.AreNotEqual(UInt256.Zero, eleventhOutbound.WithdrawalLeaf);
            Assert.AreNotEqual(UInt256.Zero, eleventhOutbound.OutboundMessageHash);
            AssertSoftSealEleventhPoisonRecoverRetention(
                () => host.ReconcileAsync(),
                () => host.SubmitNextAsync(),
                (batch, hash) => host.RecoverPoisonedBatchAsync(batch, hash),
                () => host.IsSettlementPoisonedAsync().AsTask(),
                () => host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                host.GetRpcBatch,
                host.GetRpcBatchStatus,
                host.GetRpcStateRootAtBatch,
                host.GetLatestRpcStateRoot,
                host.HasConsumedDeposit,
                host.GetRpcL1DepositStatus,
                host.GetRpcWithdrawalProof,
                host.GetRpcMessageProof,
                eleventhOutbound.WithdrawalLeaf,
                eleventhOutbound.OutboundMessageHash,
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-eleventh-poison-recover.json")));
            AssertSoftSealAfterEleventhRecoverDaAndTwelfthDeposit(
                req => host.PublishDaAsync(req).AsTask(),
                receipt => host.IsDaAvailableAsync(receipt).AsTask(),
                () => host.SupportsLocalDaReader,
                () => host.CreateLocalDaReader(),
                host.ProcessDeposit,
                host.HasConsumedDeposit,
                () => host.ConsumedDepositCount,
                host.RecordRpcDeposit,
                host.GetRpcL1DepositStatus,
                () => host.ScanSharedBridgeDepositsAsync().AsTask(),
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.ExportPrometheusMetrics(),
                path => host.WritePrometheusMetricsAsync(path).AsTask(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir);
            Assert.IsTrue(File.Exists(Path.Combine(chainDir, "soft-seal-after-eleventh-recover-da-deposit.json")));
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
            AssertSoftOpenBatchNoSeal(
                host.MaxBlocksPerBatch,
                (idx, ts, net, txs) => host.ProcessCommittedBlock(idx, ts, net, txs),
                () => host.HasOpenBatch,
                () => host.OpenBatchBlockCount,
                () => host.OpenBatchL1MessageCount,
                () => host.HasPendingSealedBatch,
                () => host.NextExpectedBlock,
                () => host.NextBatchNumber);
            AssertSoftOpenBatchOperatorSurface(
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir,
                expectedOpenBatchBlockCount: host.MaxBlocksPerBatch > 2 ? 2 : 1);
            AssertSoftForcedInclusionOperatorSurface(
                host.RegisterForcedInclusionNonce,
                () => host.KnownForcedInclusionNonceCount,
                () => host.HasBatchForcedInclusionSource,
                () => host.HasOverdueForcedInclusionCached(),
                host.InvalidateForcedInclusionCache,
                () => host.OpenBatchForcedInclusionCount,
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir,
                softNonce: 11);
            AssertSoftInboundMessageOperatorSurface(
                host.RegisterInboundMessageNonce,
                () => host.KnownInboundNonceCount,
                () => host.HasBatchMessageRouter,
                () => host.HasMessageRouter,
                () => host.IsMessagePipelineWiringComplete,
                host.InvalidateInboundMessageCache,
                () => host.OpenBatchL1MessageCount,
                () => host.L1InboxPendingCount,
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir,
                softNonce: 11);
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

            var offlineBridge = AssertOfflineBridgeMintWithdrawalOutbox(
                host.RegisterBridgeAsset,
                host.ProcessDeposit,
                host.HasConsumedDeposit,
                () => host.ConsumedDepositCount,
                host.ProcessReadyDeposits,
                host.StageWithdrawal,
                () => host.StagedWithdrawalCount,
                host.SealWithdrawalBatch,
                msgs => host.EnqueueOutboundMessagesAsync(msgs).AsTask(),
                () => host.MessageOutbox!.L2ToL1Count,
                () => host.MessageOutboxL2ToL1Root);
            AssertOfflineBridgeRpcStoreSurface(
                offlineBridge,
                host.RecordRpcDeposit,
                host.GetRpcL1DepositStatus,
                host.RegisterRpcAsset,
                host.GetRpcBridgedAsset,
                host.GetRpcCanonicalAsset,
                host.RecordRpcWithdrawalProof,
                host.GetRpcWithdrawalProof,
                host.RecordRpcMessageProof,
                host.GetRpcMessageProof,
                host.RecordMessageRouterFinalizedProof,
                msgHash => host.GetMessageRouterProofAsync(msgHash).AsTask(),
                chainDir);
            AssertOfflineBridgeOperatorSurface(
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                () => host.ScanSharedBridgeDepositsAsync().AsTask(),
                () => host.ScanAndProcessReadyDepositsAsync().AsTask(),
                chainDir);

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
                metricsPlugin: host.Metrics);
            Assert.IsNotNull(gatewayHost.Publisher);
            Assert.AreEqual(
                MerklePathRoundProver.ConstBackendId,
                ((BinaryTreeAggregator)gatewayHost.Gateway.Aggregator).RoundProver.BackendId);
            Assert.AreEqual(MerklePathRoundProver.ConstBackendId, gatewayHost.AggregationBackendId);
            Assert.AreNotEqual(UInt160.Zero, gatewayHost.SettlementManagerHash);
            Assert.AreNotEqual(UInt160.Zero, gatewayHost.MessageRouterHash);
            Assert.IsTrue(gatewayHost.HasMetricsPlugin);
            Assert.IsTrue(gatewayHost.IsMetricsHttpHealthy);
            Assert.IsTrue(gatewayHost.IsGatewayHostHealthy);
            var optGwProbe = gatewayHost.GetHealthProbe();
            Assert.IsTrue(optGwProbe.HasMetricsPlugin);
            Assert.AreEqual(gatewayHost.ProofSystem, optGwProbe.ProofSystem);
            Assert.AreEqual(gatewayHost.SettlementManagerHash.ToString(), optGwProbe.SettlementManagerHash);
            var gwStatusPath = Path.Combine(chainDir, "gateway-status.json");
            gatewayHost.WriteOperatorStatusAsync(gwStatusPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(gwStatusPath));
            var gwJson = File.ReadAllText(gwStatusPath);
            StringAssert.Contains(gwJson, "\"settlementManagerHash\":");
            StringAssert.Contains(gwJson, "\"aggregationBackendId\":");
            StringAssert.Contains(gwJson, "\"hasMetricsPlugin\": true");

            AssertSoftRpcStoreAndGatewayReceiveBatch(
                host.AddRpcBatch,
                host.FinalizeRpcBatch,
                host.GetRpcBatchStatus,
                host.GetLatestRpcStateRoot,
                host.RecordRpcDeposit,
                host.GetRpcL1DepositStatus,
                gatewayHost,
                chainDir,
                ProofType.Optimistic,
                softRoot: Root(0xBB));
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
            AssertSoftOpenBatchNoSeal(
                host.MaxBlocksPerBatch,
                (idx, ts, net, txs) => host.ProcessCommittedBlock(idx, ts, net, txs),
                () => host.HasOpenBatch,
                () => host.OpenBatchBlockCount,
                () => host.OpenBatchL1MessageCount,
                () => host.HasPendingSealedBatch,
                () => host.NextExpectedBlock,
                () => host.NextBatchNumber);
            AssertSoftOpenBatchOperatorSurface(
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir,
                expectedOpenBatchBlockCount: host.MaxBlocksPerBatch > 2 ? 2 : 1);
            AssertSoftForcedInclusionOperatorSurface(
                host.RegisterForcedInclusionNonce,
                () => host.KnownForcedInclusionNonceCount,
                () => host.HasBatchForcedInclusionSource,
                () => host.HasOverdueForcedInclusionCached(),
                host.InvalidateForcedInclusionCache,
                () => host.OpenBatchForcedInclusionCount,
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir,
                softNonce: 11);
            AssertSoftInboundMessageOperatorSurface(
                host.RegisterInboundMessageNonce,
                () => host.KnownInboundNonceCount,
                () => host.HasBatchMessageRouter,
                () => host.HasMessageRouter,
                () => host.IsMessagePipelineWiringComplete,
                host.InvalidateInboundMessageCache,
                () => host.OpenBatchL1MessageCount,
                () => host.L1InboxPendingCount,
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                chainDir,
                softNonce: 11);
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

            var offlineBridge = AssertOfflineBridgeMintWithdrawalOutbox(
                host.RegisterBridgeAsset,
                host.ProcessDeposit,
                host.HasConsumedDeposit,
                () => host.ConsumedDepositCount,
                host.ProcessReadyDeposits,
                host.StageWithdrawal,
                () => host.StagedWithdrawalCount,
                host.SealWithdrawalBatch,
                msgs => host.EnqueueOutboundMessagesAsync(msgs).AsTask(),
                () => host.MessageOutbox!.L2ToL1Count,
                () => host.MessageOutboxL2ToL1Root);
            AssertOfflineBridgeRpcStoreSurface(
                offlineBridge,
                host.RecordRpcDeposit,
                host.GetRpcL1DepositStatus,
                host.RegisterRpcAsset,
                host.GetRpcBridgedAsset,
                host.GetRpcCanonicalAsset,
                host.RecordRpcWithdrawalProof,
                host.GetRpcWithdrawalProof,
                host.RecordRpcMessageProof,
                host.GetRpcMessageProof,
                host.RecordMessageRouterFinalizedProof,
                msgHash => host.GetMessageRouterProofAsync(msgHash).AsTask(),
                chainDir);
            AssertOfflineBridgeOperatorSurface(
                () => host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult(),
                () => host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult(),
                () => host.FormatHealthProbeJson(),
                path => host.WriteOperatorStatusAsync(path).AsTask(),
                path => host.WriteHealthProbeAsync(path).AsTask(),
                () => host.ScanSharedBridgeDepositsAsync().AsTask(),
                () => host.ScanAndProcessReadyDepositsAsync().AsTask(),
                chainDir);

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
                metricsPlugin: host.Metrics);
            Assert.IsTrue(gatewayHost.OwnsProofProver);
            Assert.IsTrue(gatewayHost.HasMetricsPlugin);
            Assert.IsTrue(gatewayHost.IsMetricsHttpHealthy);
            var zkGwProbe = gatewayHost.GetHealthProbe();
            Assert.IsTrue(zkGwProbe.OwnsProofProver);
            Assert.AreEqual(gatewayHost.VerificationKeyId.ToString(), zkGwProbe.VerificationKeyId);
            Assert.IsTrue(zkGwProbe.HasMetricsPlugin);
            Assert.IsInstanceOfType(gatewayHost.ProofProver, typeof(Sp1GatewayProofProver));
            Assert.IsNotNull(gatewayHost.Publisher);
            Assert.IsTrue(Directory.Exists(Path.Combine(
                chainDir, NeoHubDeployReport.RelativeGatewayProverQueueDir)));

            AssertSoftRpcStoreAndGatewayReceiveBatch(
                host.AddRpcBatch,
                host.FinalizeRpcBatch,
                host.GetRpcBatchStatus,
                host.GetLatestRpcStateRoot,
                host.RecordRpcDeposit,
                host.GetRpcL1DepositStatus,
                gatewayHost,
                chainDir,
                ProofType.Zk,
                softRoot: Root(0xCB));
        }
        finally
        {
            if (Directory.Exists(chainDir))
                Directory.Delete(chainDir, recursive: true);
            if (Directory.Exists(exeRoot))
                Directory.Delete(exeRoot, recursive: true);
        }
    }

    /// <summary>
    /// SoftSeal multi-batch queue: after batch 1 is still pending L1 settle, seal block 2 as
    /// batch 2 (MaxBlocks=1), pin pending≥2, latest checkpoint=2, soft offline state retained.
    /// Does not claim L1 settle for either batch.
    /// </summary>
    private static void AssertSoftSealSecondBatchWhilePending(
        Action<uint, ulong, uint, IReadOnlyList<byte[]>> processCommittedBlock,
        Func<ulong?> nextExpectedBlock,
        Func<ulong> lastAcknowledgedBatchNumber,
        Func<ulong> lastAcknowledgedBlock,
        Func<ulong> nextBatchNumber,
        Func<bool> hasOpenBatch,
        Func<bool> hasPendingSealedBatch,
        Func<int> getPendingCount,
        Func<SealedBatchCheckpoint?> getLatestDurableCheckpoint,
        Func<DAPublishRequest, Task<DAReceipt>> publishDaAsync,
        Func<DAReceipt, Task<bool>> isDaAvailableAsync,
        Func<bool> supportsLocalDaReader,
        Func<IDAReader> createLocalDaReader,
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> formatHealthProbeJson,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        Func<uint, ulong, bool> hasConsumedDeposit,
        string chainDir,
        bool expectSoftOfflineBookkeeping)
    {
        Assert.AreEqual(2UL, nextExpectedBlock());
        Assert.AreEqual(1UL, lastAcknowledgedBatchNumber());
        Assert.AreEqual(2UL, nextBatchNumber());
        Assert.IsTrue(getPendingCount() >= 1);

        var ts2 = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        processCommittedBlock(2, ts2, 894710606, Array.Empty<byte[]>());

        Assert.IsFalse(hasOpenBatch());
        Assert.IsFalse(hasPendingSealedBatch());
        Assert.AreEqual(3UL, nextExpectedBlock());
        Assert.AreEqual(2UL, lastAcknowledgedBatchNumber());
        Assert.AreEqual(2UL, lastAcknowledgedBlock());
        Assert.AreEqual(3UL, nextBatchNumber());
        Assert.IsTrue(getPendingCount() >= 2);

        var checkpoint2 = getLatestDurableCheckpoint();
        Assert.IsNotNull(checkpoint2);
        Assert.AreEqual(2UL, checkpoint2!.BatchNumber);
        Assert.AreEqual(2UL, checkpoint2.LastBlock);
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, checkpoint2.PostStateRoot);

        // Local DA for sealed batch 2 (batch 1 DA already published earlier on SoftSeal path).
        Assert.IsTrue(supportsLocalDaReader());
        var softDaPayload = new byte[] { 0xDA, 0x52, 0x02 };
        var softDaReceipt = publishDaAsync(new DAPublishRequest
        {
            ChainId = 20260716u,
            BatchNumber = 2,
            Payload = softDaPayload,
        }).GetAwaiter().GetResult();
        Assert.AreEqual(DAMode.Local, softDaReceipt.Layer);
        Assert.AreEqual(DAReceiptKind.LocalPersistence, softDaReceipt.Kind);
        Assert.IsTrue(isDaAvailableAsync(softDaReceipt).GetAwaiter().GetResult());
        var softDaRead = createLocalDaReader().ReadAsync(softDaReceipt).AsTask().GetAwaiter().GetResult();
        Assert.IsTrue(softDaRead is { Length: 3 });
        CollectionAssert.AreEqual(softDaPayload, softDaRead!.Value.ToArray());

        var status = getOperatorStatus();
        Assert.AreEqual(2UL, status.LatestCheckpointBatchNumber);
        Assert.AreEqual(2UL, status.LatestCheckpointLastBlock);
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, status.LatestCheckpointPostStateRoot);
        Assert.IsTrue(status.PendingSettlementCount >= 2);
        Assert.IsFalse(status.IsSettlementIdle);
        Assert.IsTrue(status.IsSettlementRetrying);
        Assert.IsFalse(status.IsSettlementPoisoned);
        Assert.IsFalse(status.IsPipelineHealthy);
        Assert.IsTrue(status.IsOfflinePassportComplete);
        Assert.IsTrue(status.IsOperatorReady);
        Assert.IsTrue(status.IsBatcherCheckpointAligned);
        Assert.IsFalse(status.HasOpenBatch);
        CollectionAssert.Contains(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.IsSettlementRetrying));
        if (expectSoftOfflineBookkeeping)
        {
            Assert.IsTrue(hasConsumedDeposit(0, 1));
            Assert.AreEqual(1, status.ConsumedDepositCount);
            Assert.AreEqual(1, status.MessageOutboxL2ToL1Count);
            Assert.AreEqual(1, status.KnownForcedInclusionNonceCount);
            Assert.AreEqual(1, status.KnownInboundNonceCount);
        }

        var probe = getHealthProbe();
        Assert.AreEqual(2UL, probe.LatestCheckpointBatchNumber);
        Assert.IsTrue(probe.PendingSettlementCount >= 2);
        Assert.IsTrue(probe.IsSettlementRetrying);
        if (expectSoftOfflineBookkeeping)
        {
            Assert.AreEqual(1, probe.ConsumedDepositCount);
            Assert.AreEqual(1, probe.KnownForcedInclusionNonceCount);
            Assert.AreEqual(1, probe.KnownInboundNonceCount);
        }

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": 2");
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusJson, "IsSettlementRetrying");
        if (expectSoftOfflineBookkeeping)
        {
            StringAssert.Contains(statusJson, "\"consumedDepositCount\": 1");
            StringAssert.Contains(statusJson, "\"knownForcedInclusionNonceCount\": 1");
            StringAssert.Contains(statusJson, "\"knownInboundNonceCount\": 1");
        }

        var probeJson = formatHealthProbeJson();
        StringAssert.Contains(probeJson, "\"latestCheckpointBatchNumber\": 2");
        StringAssert.Contains(probeJson, "\"isSettlementRetrying\": true");

        var statusPath = Path.Combine(chainDir, "soft-seal-second-batch-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        var statusFile = File.ReadAllText(statusPath);
        StringAssert.Contains(statusFile, "\"latestCheckpointBatchNumber\": 2");
        StringAssert.Contains(statusFile, "\"isSettlementRetrying\": true");

        var probePath = Path.Combine(chainDir, "soft-seal-second-batch-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        var probeFile = File.ReadAllText(probePath);
        StringAssert.Contains(probeFile, "\"latestCheckpointBatchNumber\": 2");
    }

    /// <summary>
    /// SoftSeal poison→recover: offline deposit/FI/inbound/outbox bookkeeping survives
    /// RecoverPoisonedBatch and remains visible while settlement is still Retrying.
    /// When multi-batch RPC delegates are supplied, also pins batch 1+2 store/tip retention
    /// after recover (does not claim L1 settle / PublishAggregate).
    /// </summary>
    private static void AssertSoftSealAfterRecoverSoftStateRetention(
        LocalHostOperatorStatus afterRecover,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> formatHealthProbeJson,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        Func<uint, ulong, bool> hasConsumedDeposit,
        Func<uint, ulong, DepositStatus?> getRpcL1DepositStatus,
        string chainDir,
        Func<ulong, L2BatchCommitment?>? getRpcBatch = null,
        Func<ulong, UInt256>? getRpcStateRootAtBatch = null,
        Func<ulong, BatchStatus>? getRpcBatchStatus = null,
        Func<UInt256>? getLatestRpcStateRoot = null)
    {
        // Soft offline bridge + FI/inbound bookkeeping established before poison must remain.
        Assert.IsTrue(hasConsumedDeposit(0, 1));
        Assert.AreEqual(1, afterRecover.ConsumedDepositCount);
        Assert.AreEqual(1, afterRecover.MessageOutboxL2ToL1Count);
        Assert.AreEqual(0, afterRecover.StagedWithdrawalCount);
        Assert.IsTrue(afterRecover.HasMessageOutbox);
        Assert.AreNotEqual(UInt256.Zero, afterRecover.MessageOutboxL2ToL1Root);
        Assert.AreEqual(1, afterRecover.KnownForcedInclusionNonceCount);
        Assert.AreEqual(1, afterRecover.KnownInboundNonceCount);
        Assert.IsFalse(afterRecover.HasOverdueForcedInclusion);
        Assert.AreEqual(0, afterRecover.OpenBatchForcedInclusionCount);
        Assert.AreEqual(0, afterRecover.OpenBatchL1MessageCount);
        Assert.AreEqual(0, afterRecover.L1InboxPendingCount);
        Assert.IsFalse(afterRecover.HasOpenBatch);
        Assert.AreEqual(2UL, afterRecover.LatestCheckpointBatchNumber);
        Assert.IsTrue(afterRecover.IsSettlementRetrying);
        Assert.IsFalse(afterRecover.IsSettlementPoisoned);
        Assert.IsFalse(afterRecover.IsSettlementIdle);
        Assert.IsTrue(afterRecover.PendingSettlementCount >= 2);
        Assert.IsTrue(afterRecover.IsOfflinePassportComplete);
        Assert.IsTrue(afterRecover.IsOperatorReady);
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, afterRecover.LatestRpcStateRoot);

        var depositStatus = getRpcL1DepositStatus(0, 1);
        Assert.IsNotNull(depositStatus);
        Assert.IsTrue(depositStatus!.Value.ConsumedOnL2);
        Assert.AreEqual(1UL, depositStatus.Value.IncludedInBatch);

        if (getRpcBatch is not null
            && getRpcStateRootAtBatch is not null
            && getRpcBatchStatus is not null
            && getLatestRpcStateRoot is not null)
        {
            // Multi-batch soft RPC store survives poison→recover (independent of L1 settle).
            Assert.AreEqual(BatchStatus.Finalized, getRpcBatchStatus(1));
            Assert.AreEqual(BatchStatus.Finalized, getRpcBatchStatus(2));
            var rpc1 = getRpcBatch(1);
            var rpc2 = getRpcBatch(2);
            Assert.IsNotNull(rpc1);
            Assert.IsNotNull(rpc2);
            Assert.AreEqual(1UL, rpc1!.BatchNumber);
            Assert.AreEqual(2UL, rpc2!.BatchNumber);
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, rpc1.PostStateRoot);
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, rpc2.PostStateRoot);
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, getRpcStateRootAtBatch(1));
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, getRpcStateRootAtBatch(2));
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, getLatestRpcStateRoot());
            Assert.AreEqual(getLatestRpcStateRoot(), afterRecover.LatestRpcStateRoot);

            var multiRpcPath = Path.Combine(chainDir, "soft-seal-after-recover-multi-batch-rpc.json");
            File.WriteAllText(multiRpcPath, $$"""
                {
                  "batch1Status": "{{getRpcBatchStatus(1)}}",
                  "batch2Status": "{{getRpcBatchStatus(2)}}",
                  "latestRpcStateRoot": "{{getLatestRpcStateRoot()}}",
                  "latestCheckpointBatchNumber": {{afterRecover.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterRecover.PendingSettlementCount}},
                  "isSettlementRetrying": true,
                  "isSettlementPoisoned": false
                }
                """);
            Assert.IsTrue(File.Exists(multiRpcPath));
            var multiRpcFile = File.ReadAllText(multiRpcPath);
            StringAssert.Contains(multiRpcFile, "\"batch1Status\": \"Finalized\"");
            StringAssert.Contains(multiRpcFile, "\"batch2Status\": \"Finalized\"");
            StringAssert.Contains(multiRpcFile, "\"latestCheckpointBatchNumber\": 2");
            StringAssert.Contains(multiRpcFile, "\"isSettlementRetrying\": true");
            StringAssert.Contains(multiRpcFile, "\"isSettlementPoisoned\": false");
        }

        var probe = getHealthProbe();
        Assert.AreEqual(1, probe.ConsumedDepositCount);
        Assert.AreEqual(1, probe.MessageOutboxL2ToL1Count);
        Assert.AreEqual(1, probe.KnownForcedInclusionNonceCount);
        Assert.AreEqual(1, probe.KnownInboundNonceCount);
        Assert.IsTrue(probe.IsSettlementRetrying);
        Assert.IsFalse(probe.IsSettlementPoisoned);
        Assert.AreEqual(2UL, probe.LatestCheckpointBatchNumber);
        Assert.IsTrue(probe.PendingSettlementCount >= 2);
        Assert.AreEqual(
            SoftPassThroughExecutor.PostStateRoot.ToString(),
            probe.LatestRpcStateRoot);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"consumedDepositCount\": 1");
        StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Count\": 1");
        StringAssert.Contains(statusJson, "\"knownForcedInclusionNonceCount\": 1");
        StringAssert.Contains(statusJson, "\"knownInboundNonceCount\": 1");
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusJson, "\"isSettlementPoisoned\": false");
        StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": 2");
        StringAssert.Contains(
            statusJson,
            "\"latestRpcStateRoot\": \"" + SoftPassThroughExecutor.PostStateRoot + "\"");

        var probeJson = formatHealthProbeJson();
        StringAssert.Contains(probeJson, "\"consumedDepositCount\": 1");
        StringAssert.Contains(probeJson, "\"knownForcedInclusionNonceCount\": 1");
        StringAssert.Contains(probeJson, "\"knownInboundNonceCount\": 1");
        StringAssert.Contains(probeJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(probeJson, "\"latestCheckpointBatchNumber\": 2");
        StringAssert.Contains(
            probeJson,
            "\"latestRpcStateRoot\": \"" + SoftPassThroughExecutor.PostStateRoot + "\"");

        var statusPath = Path.Combine(chainDir, "soft-seal-after-recover-retention-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        var statusFile = File.ReadAllText(statusPath);
        StringAssert.Contains(statusFile, "\"consumedDepositCount\": 1");
        StringAssert.Contains(statusFile, "\"knownForcedInclusionNonceCount\": 1");
        StringAssert.Contains(statusFile, "\"knownInboundNonceCount\": 1");
        StringAssert.Contains(statusFile, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusFile, "\"latestCheckpointBatchNumber\": 2");
        StringAssert.Contains(
            statusFile,
            "\"latestRpcStateRoot\": \"" + SoftPassThroughExecutor.PostStateRoot + "\"");

        var probePath = Path.Combine(chainDir, "soft-seal-after-recover-retention-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        var probeFile = File.ReadAllText(probePath);
        StringAssert.Contains(probeFile, "\"consumedDepositCount\": 1");
        StringAssert.Contains(probeFile, "\"knownForcedInclusionNonceCount\": 1");
        StringAssert.Contains(probeFile, "\"knownInboundNonceCount\": 1");
        StringAssert.Contains(probeFile, "\"latestCheckpointBatchNumber\": 2");
        StringAssert.Contains(
            probeFile,
            "\"latestRpcStateRoot\": \"" + SoftPassThroughExecutor.PostStateRoot + "\"");
    }

    /// <summary>
    /// SoftSeal after poison→recover: re-publish multi-batch local DA (batch 1+2), process a
    /// second offline deposit (nonce 2, IncludedInBatch=2), and scrape host Prometheus while
    /// settle remains Retrying with pending≥2. Does not claim L1 settle / production DA.
    /// </summary>
    private static void AssertSoftSealAfterRecoverDaAndSecondDeposit(
        Func<DAPublishRequest, Task<DAReceipt>> publishDaAsync,
        Func<DAReceipt, Task<bool>> isDaAvailableAsync,
        Func<bool> supportsLocalDaReader,
        Func<IDAReader> createLocalDaReader,
        Func<CrossChainMessage, MintInstruction> processDeposit,
        Func<uint, ulong, bool> hasConsumedDeposit,
        Func<int> consumedDepositCount,
        Action<DepositStatus> recordRpcDeposit,
        Func<uint, ulong, DepositStatus?> getRpcL1DepositStatus,
        Func<Task<int>> scanSharedBridgeDepositsAsync,
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> exportPrometheusMetrics,
        Func<string, Task> writePrometheusMetricsAsync,
        string chainDir)
    {
        Assert.IsTrue(supportsLocalDaReader());
        var da1Payload = new byte[] { 0xDA, 0xA1, 0x01 };
        var da1 = publishDaAsync(new DAPublishRequest
        {
            ChainId = 20260716u,
            BatchNumber = 1,
            Payload = da1Payload,
        }).GetAwaiter().GetResult();
        Assert.AreEqual(DAMode.Local, da1.Layer);
        Assert.AreEqual(DAReceiptKind.LocalPersistence, da1.Kind);
        Assert.IsTrue(isDaAvailableAsync(da1).GetAwaiter().GetResult());
        var da1Read = createLocalDaReader().ReadAsync(da1).AsTask().GetAwaiter().GetResult();
        Assert.IsTrue(da1Read is { Length: 3 });
        CollectionAssert.AreEqual(da1Payload, da1Read!.Value.ToArray());

        var da2Payload = new byte[] { 0xDA, 0xA2, 0x02 };
        var da2 = publishDaAsync(new DAPublishRequest
        {
            ChainId = 20260716u,
            BatchNumber = 2,
            Payload = da2Payload,
        }).GetAwaiter().GetResult();
        Assert.AreEqual(DAMode.Local, da2.Layer);
        Assert.IsTrue(isDaAvailableAsync(da2).GetAwaiter().GetResult());
        var da2Read = createLocalDaReader().ReadAsync(da2).AsTask().GetAwaiter().GetResult();
        Assert.IsTrue(da2Read is { Length: 3 });
        CollectionAssert.AreEqual(da2Payload, da2Read!.Value.ToArray());

        // Second offline mint while still Retrying (asset already registered pre-poison).
        var softL1Asset = UInt160.Parse("0x" + new string('1', 40));
        var softL2Asset = UInt160.Parse("0x" + new string('2', 40));
        var softDepositPayload = new DepositPayload
        {
            L1Asset = softL1Asset,
            L2Recipient = Account(0x55),
            Amount = new BigInteger(2_000),
        };
        var softDepositMsg = new CrossChainMessage
        {
            SourceChainId = 0,
            TargetChainId = 20260716u,
            Nonce = 2,
            Sender = Account(0x66),
            Receiver = Account(0x55),
            MessageType = MessageType.Deposit,
            Payload = softDepositPayload.Encode(),
            MessageHash = UInt256.Zero,
        };
        var softMint = processDeposit(softDepositMsg);
        Assert.AreEqual(softL2Asset, softMint.L2Asset);
        Assert.IsTrue(hasConsumedDeposit(0, 1));
        Assert.IsTrue(hasConsumedDeposit(0, 2));
        Assert.AreEqual(2, consumedDepositCount());
        recordRpcDeposit(new DepositStatus(0, 2, ConsumedOnL2: true, IncludedInBatch: 2));
        Assert.IsTrue(getRpcL1DepositStatus(0, 1) is { ConsumedOnL2: true, IncludedInBatch: 1UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 2) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.AreEqual(0, scanSharedBridgeDepositsAsync().GetAwaiter().GetResult());

        var status = getOperatorStatus();
        Assert.AreEqual(2, status.ConsumedDepositCount);
        Assert.AreEqual(2UL, status.LatestCheckpointBatchNumber);
        Assert.IsTrue(status.PendingSettlementCount >= 2);
        Assert.IsTrue(status.IsSettlementRetrying);
        Assert.IsFalse(status.IsSettlementPoisoned);
        Assert.IsFalse(status.IsSettlementIdle);
        Assert.IsTrue(status.IsOfflinePassportComplete);
        Assert.IsTrue(status.IsOperatorReady);
        Assert.IsFalse(status.IsPipelineHealthy);
        CollectionAssert.Contains(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.IsSettlementRetrying));

        var probe = getHealthProbe();
        Assert.AreEqual(2, probe.ConsumedDepositCount);
        Assert.AreEqual(2UL, probe.LatestCheckpointBatchNumber);
        Assert.IsTrue(probe.PendingSettlementCount >= 2);
        Assert.IsTrue(probe.IsSettlementRetrying);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"consumedDepositCount\": 2");
        StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": 2");
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");

        var hostProm = exportPrometheusMetrics();
        Assert.IsFalse(string.IsNullOrWhiteSpace(hostProm));
        var hostPromPath = Path.Combine(chainDir, "soft-seal-after-recover-host.prom");
        writePrometheusMetricsAsync(hostPromPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(hostPromPath));
        Assert.AreEqual(hostProm, File.ReadAllText(hostPromPath));

        var durablePath = Path.Combine(chainDir, "soft-seal-after-recover-da-offline.json");
        File.WriteAllText(durablePath, $$"""
            {
              "daBatch1Layer": "{{da1.Layer}}",
              "daBatch2Layer": "{{da2.Layer}}",
              "daBatch1Available": true,
              "daBatch2Available": true,
              "consumedDepositCount": {{status.ConsumedDepositCount}},
              "deposit2IncludedInBatch": 2,
              "latestCheckpointBatchNumber": {{status.LatestCheckpointBatchNumber}},
              "pendingSettlementCount": {{status.PendingSettlementCount}},
              "isSettlementRetrying": true,
              "isSettlementPoisoned": false
            }
            """);
        Assert.IsTrue(File.Exists(durablePath));
        var durableFile = File.ReadAllText(durablePath);
        StringAssert.Contains(durableFile, "\"daBatch1Layer\": \"Local\"");
        StringAssert.Contains(durableFile, "\"daBatch2Layer\": \"Local\"");
        StringAssert.Contains(durableFile, "\"consumedDepositCount\": 2");
        StringAssert.Contains(durableFile, "\"deposit2IncludedInBatch\": 2");
        StringAssert.Contains(durableFile, "\"latestCheckpointBatchNumber\": 2");
        StringAssert.Contains(durableFile, "\"isSettlementRetrying\": true");
    }

    /// <summary>
    /// SoftSeal after poison→recover (and after second offline deposit): second withdrawal
    /// seal + L2→L1 outbox enqueue + second FI/inbound nonces + RPC withdrawal/message/router
    /// proof durability while settle remains Retrying with multi-batch pending. Does not claim
    /// L1 withdraw claim / FI drain / settle. Returns second withdrawal leaf + outbound hash
    /// for follow-on second-poison retention pins.
    /// </summary>
    private static (UInt256 WithdrawalLeaf, UInt256 OutboundMessageHash) AssertSoftSealAfterRecoverSecondOutboundAndFi(
        Func<WithdrawalRequest, UInt256> stageWithdrawal,
        Func<int> stagedWithdrawalCount,
        Func<(UInt256 Root, WithdrawalTree Tree)> sealWithdrawalBatch,
        Func<IReadOnlyList<CrossChainMessage>, Task> enqueueOutbound,
        Func<int> messageOutboxL2ToL1Count,
        Func<UInt256> messageOutboxL2ToL1Root,
        Func<ulong, bool> registerForcedInclusionNonce,
        Func<int> knownForcedInclusionNonceCount,
        Func<bool> hasOverdueForcedInclusionCached,
        Action invalidateForcedInclusionCache,
        Func<int> openBatchForcedInclusionCount,
        Func<ulong, bool> registerInboundMessageNonce,
        Func<int> knownInboundNonceCount,
        Action invalidateInboundMessageCache,
        Func<int> openBatchL1MessageCount,
        Func<int> l1InboxPendingCount,
        Action<UInt256, byte[]> recordRpcWithdrawalProof,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcWithdrawalProof,
        Action<UInt256, byte[]> recordRpcMessageProof,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcMessageProof,
        Action<UInt256, ReadOnlyMemory<byte>> recordMessageRouterFinalizedProof,
        Func<UInt256, Task<ReadOnlyMemory<byte>?>> getMessageRouterProofAsync,
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> formatHealthProbeJson,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        string chainDir)
    {
        var softL2Asset = UInt160.Parse("0x" + new string('2', 40));
        var softSender = Account(0x77);
        var wdLeaf = stageWithdrawal(new WithdrawalRequest
        {
            ChainId = 20260716u,
            EmittingContract = softSender,
            L2Sender = softSender,
            L1Recipient = softSender,
            L2Asset = softL2Asset,
            Amount = new BigInteger(75),
            Nonce = 2,
        });
        Assert.AreNotEqual(UInt256.Zero, wdLeaf);
        Assert.IsTrue(stagedWithdrawalCount() >= 1);
        var sealedWd = sealWithdrawalBatch();
        Assert.AreNotEqual(UInt256.Zero, sealedWd.Root);
        Assert.AreEqual(0, stagedWithdrawalCount());
        Assert.IsTrue(sealedWd.Tree.Count >= 1);
        var merkleProof = sealedWd.Tree.GetProof(sealedWd.Tree.Count - 1);
        Assert.AreEqual(wdLeaf, merkleProof.Leaf);
        var proofBytes = MerkleProofSerializer.Encode(merkleProof);
        Assert.IsTrue(proofBytes.Length >= MerkleProofSerializer.HeaderSize);
        recordRpcWithdrawalProof(wdLeaf, proofBytes);
        var storedWdProof = getRpcWithdrawalProof(wdLeaf);
        Assert.IsTrue(storedWdProof is { Length: > 0 });
        CollectionAssert.AreEqual(proofBytes, storedWdProof!.Value.ToArray());

        var outboundDraft = new CrossChainMessage
        {
            SourceChainId = 20260716u,
            TargetChainId = 0,
            Nonce = 10,
            Sender = softSender,
            Receiver = softSender,
            MessageType = MessageType.Event,
            Payload = new byte[] { 0x02 },
            MessageHash = UInt256.Zero,
        };
        var outbound = outboundDraft with { MessageHash = MessageHasher.HashMessage(outboundDraft) };
        enqueueOutbound([outbound]).GetAwaiter().GetResult();
        Assert.AreEqual(2, messageOutboxL2ToL1Count());
        Assert.AreNotEqual(UInt256.Zero, messageOutboxL2ToL1Root());

        // Soft message inclusion bytes for RPC explorers (not an L1 MessageRouter claim).
        var messageProofBytes = outbound.MessageHash.GetSpan().ToArray();
        recordRpcMessageProof(outbound.MessageHash, messageProofBytes);
        var storedMsgProof = getRpcMessageProof(outbound.MessageHash);
        Assert.IsTrue(storedMsgProof is { Length: > 0 });
        CollectionAssert.AreEqual(messageProofBytes, storedMsgProof!.Value.ToArray());
        recordMessageRouterFinalizedProof(outbound.MessageHash, messageProofBytes);
        var routerProof = getMessageRouterProofAsync(outbound.MessageHash).GetAwaiter().GetResult();
        Assert.IsTrue(routerProof is { Length: > 0 });
        CollectionAssert.AreEqual(messageProofBytes, routerProof!.Value.ToArray());

        // Second FI/inbound nonces while still Retrying (open-batch remains 0 — sealed).
        Assert.IsTrue(registerForcedInclusionNonce(12));
        Assert.IsFalse(registerForcedInclusionNonce(12));
        Assert.AreEqual(2, knownForcedInclusionNonceCount());
        Assert.AreEqual(0, openBatchForcedInclusionCount());
        Assert.IsFalse(hasOverdueForcedInclusionCached());
        invalidateForcedInclusionCache();
        Assert.AreEqual(2, knownForcedInclusionNonceCount());

        Assert.IsTrue(registerInboundMessageNonce(12));
        Assert.IsFalse(registerInboundMessageNonce(12));
        Assert.AreEqual(2, knownInboundNonceCount());
        Assert.AreEqual(0, openBatchL1MessageCount());
        Assert.AreEqual(0, l1InboxPendingCount());
        invalidateInboundMessageCache();
        Assert.AreEqual(2, knownInboundNonceCount());

        var status = getOperatorStatus();
        Assert.AreEqual(2, status.ConsumedDepositCount);
        Assert.AreEqual(2, status.MessageOutboxL2ToL1Count);
        Assert.AreEqual(0, status.StagedWithdrawalCount);
        Assert.AreEqual(2, status.KnownForcedInclusionNonceCount);
        Assert.AreEqual(2, status.KnownInboundNonceCount);
        // Soft RegisterForcedInclusionNonce updates Known* only; durable Tracked* is settlement-store.
        Assert.IsTrue(status.TrackedForcedInclusionNonceCount >= 0);
        Assert.IsFalse(status.HasOverdueForcedInclusion);
        Assert.AreEqual(0, status.OpenBatchForcedInclusionCount);
        Assert.AreEqual(0, status.OpenBatchL1MessageCount);
        Assert.AreEqual(2UL, status.LatestCheckpointBatchNumber);
        Assert.IsTrue(status.PendingSettlementCount >= 2);
        Assert.IsTrue(status.IsSettlementRetrying);
        Assert.IsFalse(status.IsSettlementPoisoned);
        Assert.IsFalse(status.IsSettlementIdle);
        Assert.IsTrue(status.IsOfflinePassportComplete);
        Assert.IsTrue(status.IsOperatorReady);
        Assert.IsFalse(status.IsPipelineHealthy);
        CollectionAssert.Contains(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.IsSettlementRetrying));
        CollectionAssert.DoesNotContain(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.HasOverdueForcedInclusion));

        var probe = getHealthProbe();
        Assert.AreEqual(2, probe.ConsumedDepositCount);
        Assert.AreEqual(2, probe.MessageOutboxL2ToL1Count);
        Assert.AreEqual(2, probe.KnownForcedInclusionNonceCount);
        Assert.AreEqual(2, probe.KnownInboundNonceCount);
        Assert.IsTrue(probe.IsSettlementRetrying);
        Assert.AreEqual(2UL, probe.LatestCheckpointBatchNumber);
        Assert.IsTrue(probe.PendingSettlementCount >= 2);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"consumedDepositCount\": 2");
        StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Count\": 2");
        StringAssert.Contains(statusJson, "\"knownForcedInclusionNonceCount\": 2");
        StringAssert.Contains(statusJson, "\"knownInboundNonceCount\": 2");
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": 2");

        var probeJson = formatHealthProbeJson();
        StringAssert.Contains(probeJson, "\"messageOutboxL2ToL1Count\": 2");
        StringAssert.Contains(probeJson, "\"knownForcedInclusionNonceCount\": 2");
        StringAssert.Contains(probeJson, "\"knownInboundNonceCount\": 2");
        StringAssert.Contains(probeJson, "\"isSettlementRetrying\": true");

        var statusPath = Path.Combine(chainDir, "soft-seal-after-recover-second-outbound-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        var statusFile = File.ReadAllText(statusPath);
        StringAssert.Contains(statusFile, "\"messageOutboxL2ToL1Count\": 2");
        StringAssert.Contains(statusFile, "\"knownForcedInclusionNonceCount\": 2");
        StringAssert.Contains(statusFile, "\"knownInboundNonceCount\": 2");
        StringAssert.Contains(statusFile, "\"isSettlementRetrying\": true");

        var probePath = Path.Combine(chainDir, "soft-seal-after-recover-second-outbound-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        var probeFile = File.ReadAllText(probePath);
        StringAssert.Contains(probeFile, "\"messageOutboxL2ToL1Count\": 2");
        StringAssert.Contains(probeFile, "\"knownForcedInclusionNonceCount\": 2");
        StringAssert.Contains(probeFile, "\"knownInboundNonceCount\": 2");

        var durablePath = Path.Combine(chainDir, "soft-seal-after-recover-second-outbound.json");
        File.WriteAllText(durablePath, $$"""
            {
              "withdrawalNonce": 2,
              "outboundNonce": 10,
              "messageOutboxL2ToL1Count": {{status.MessageOutboxL2ToL1Count}},
              "stagedWithdrawalCount": {{status.StagedWithdrawalCount}},
              "knownForcedInclusionNonceCount": {{status.KnownForcedInclusionNonceCount}},
              "knownInboundNonceCount": {{status.KnownInboundNonceCount}},
              "trackedForcedInclusionNonceCount": {{status.TrackedForcedInclusionNonceCount}},
              "withdrawalLeaf": "{{wdLeaf}}",
              "withdrawalRoot": "{{sealedWd.Root}}",
              "withdrawalProofBytes": {{proofBytes.Length}},
              "outboundMessageHash": "{{outbound.MessageHash}}",
              "messageProofBytes": {{messageProofBytes.Length}},
              "latestCheckpointBatchNumber": {{status.LatestCheckpointBatchNumber}},
              "pendingSettlementCount": {{status.PendingSettlementCount}},
              "isSettlementRetrying": true,
              "isSettlementPoisoned": false
            }
            """);
        Assert.IsTrue(File.Exists(durablePath));
        var durableFile = File.ReadAllText(durablePath);
        StringAssert.Contains(durableFile, "\"messageOutboxL2ToL1Count\": 2");
        StringAssert.Contains(durableFile, "\"knownForcedInclusionNonceCount\": 2");
        StringAssert.Contains(durableFile, "\"knownInboundNonceCount\": 2");
        StringAssert.Contains(durableFile, "\"withdrawalLeaf\": \"" + wdLeaf + "\"");
        StringAssert.Contains(durableFile, "\"outboundMessageHash\": \"" + outbound.MessageHash + "\"");
        StringAssert.Contains(durableFile, "\"isSettlementRetrying\": true");

        var rpcSurfacePath = Path.Combine(chainDir, "soft-seal-after-recover-second-outbound-rpc.json");
        File.WriteAllText(rpcSurfacePath, $$"""
            {
              "withdrawalLeaf": "{{wdLeaf}}",
              "withdrawalRoot": "{{sealedWd.Root}}",
              "withdrawalProofBytes": {{proofBytes.Length}},
              "outboundMessageHash": "{{outbound.MessageHash}}",
              "messageProofBytes": {{messageProofBytes.Length}},
              "routerProofBytes": {{messageProofBytes.Length}},
              "knownForcedInclusionNonceCount": 2,
              "knownInboundNonceCount": 2,
              "isSettlementRetrying": true
            }
            """);
        Assert.IsTrue(File.Exists(rpcSurfacePath));
        var rpcSurface = File.ReadAllText(rpcSurfacePath);
        StringAssert.Contains(rpcSurface, "\"withdrawalLeaf\": \"" + wdLeaf + "\"");
        StringAssert.Contains(rpcSurface, "\"outboundMessageHash\": \"" + outbound.MessageHash + "\"");
        StringAssert.Contains(rpcSurface, "\"knownForcedInclusionNonceCount\": 2");
        StringAssert.Contains(rpcSurface, "\"knownInboundNonceCount\": 2");
        return (wdLeaf, outbound.MessageHash);
    }

    /// <summary>
    /// SoftSeal full soft path after first recover: second Reconcile→Poison→Recover cycle
    /// must retain multi-batch RPC tip, dual deposits, dual outbox, dual FI/inbound known
    /// counts, and second-outbound withdrawal/message proofs. Does not claim L1 settle.
    /// </summary>
    private static void AssertSoftSealSecondPoisonRecoverRetention(
        Func<Task> reconcileAsync,
        Func<Task> submitNextAsync,
        Func<ulong, UInt256, Task> recoverPoisonedBatchAsync,
        Func<Task<bool>> isSettlementPoisonedAsync,
        Func<int> getPendingCount,
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> formatHealthProbeJson,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        Func<ulong, L2BatchCommitment?> getRpcBatch,
        Func<ulong, BatchStatus> getRpcBatchStatus,
        Func<ulong, UInt256> getRpcStateRootAtBatch,
        Func<UInt256> getLatestRpcStateRoot,
        Func<uint, ulong, bool> hasConsumedDeposit,
        Func<uint, ulong, DepositStatus?> getRpcL1DepositStatus,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcWithdrawalProof,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcMessageProof,
        UInt256 secondWithdrawalLeaf,
        UInt256 secondOutboundMessageHash,
        string chainDir)
    {
        var before = getOperatorStatus();
        Assert.IsTrue(before.IsSettlementRetrying);
        Assert.IsFalse(before.IsSettlementPoisoned);
        Assert.IsTrue(before.PendingSettlementCount >= 2);
        Assert.AreEqual(2UL, before.LatestCheckpointBatchNumber);
        Assert.AreEqual(2, before.ConsumedDepositCount);
        Assert.AreEqual(2, before.MessageOutboxL2ToL1Count);
        Assert.AreEqual(2, before.KnownForcedInclusionNonceCount);
        Assert.AreEqual(2, before.KnownInboundNonceCount);
        Assert.IsTrue(getPendingCount() >= 2);

        // After RecoverPoisonedBatch, RetryCount resets to 0 — re-escalate to Poisoned by
        // repeating fail-closed settle attempts until the automatic-retry budget is exhausted.
        LocalHostOperatorStatus afterPoison = before;
        for (var attempt = 0; attempt < 16; attempt++)
        {
            try
            {
                reconcileAsync().GetAwaiter().GetResult();
            }
            catch (OverflowException)
            {
                // Mock L1 reconcile fails closed (same as first SoftSeal poison path).
            }
            catch (Exception)
            {
                // Other mock failures still drive recovery bookkeeping via SubmitNext.
            }

            submitNextAsync().GetAwaiter().GetResult();
            afterPoison = getOperatorStatus();
            if (afterPoison.IsSettlementPoisoned)
                break;
        }

        Assert.IsTrue(afterPoison.IsSettlementPoisoned);
        Assert.IsFalse(afterPoison.IsSettlementRetrying);
        CollectionAssert.Contains(
            afterPoison.PipelineHealthFailures.ToArray(),
            nameof(afterPoison.IsSettlementPoisoned));
        Assert.IsNotNull(afterPoison.Recovery.BlockedBatchNumber);
        Assert.IsNotNull(afterPoison.Recovery.ArtifactContentHash);
        var blockedBatch = afterPoison.Recovery.BlockedBatchNumber!.Value;
        var contentHash = afterPoison.Recovery.ArtifactContentHash!;
        // Soft multi-batch bookkeeping still visible while Poisoned.
        Assert.IsTrue(afterPoison.PendingSettlementCount >= 2);
        Assert.AreEqual(2UL, afterPoison.LatestCheckpointBatchNumber);
        Assert.AreEqual(2, afterPoison.ConsumedDepositCount);
        Assert.AreEqual(2, afterPoison.MessageOutboxL2ToL1Count);
        Assert.AreEqual(2, afterPoison.KnownForcedInclusionNonceCount);
        Assert.AreEqual(2, afterPoison.KnownInboundNonceCount);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => recoverPoisonedBatchAsync(blockedBatch, UInt256.Zero).GetAwaiter().GetResult());
        Assert.IsTrue(isSettlementPoisonedAsync().GetAwaiter().GetResult());
        recoverPoisonedBatchAsync(blockedBatch, contentHash).GetAwaiter().GetResult();

        var afterRecover = getOperatorStatus();
        Assert.IsFalse(afterRecover.IsSettlementPoisoned);
        Assert.IsTrue(afterRecover.IsSettlementRetrying);
        Assert.AreEqual(SettlementRecoveryState.Retrying, afterRecover.Recovery.State);
        Assert.AreEqual(0, afterRecover.Recovery.RetryCount);
        Assert.IsTrue(getPendingCount() >= 2);
        Assert.IsTrue(afterRecover.PendingSettlementCount >= 2);
        Assert.AreEqual(2UL, afterRecover.LatestCheckpointBatchNumber);
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, afterRecover.LatestCheckpointPostStateRoot);
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, afterRecover.LatestRpcStateRoot);
        Assert.AreEqual(2, afterRecover.ConsumedDepositCount);
        Assert.AreEqual(2, afterRecover.MessageOutboxL2ToL1Count);
        Assert.AreEqual(2, afterRecover.KnownForcedInclusionNonceCount);
        Assert.AreEqual(2, afterRecover.KnownInboundNonceCount);
        Assert.IsTrue(afterRecover.IsOfflinePassportComplete);
        Assert.IsTrue(afterRecover.IsOperatorReady);
        Assert.IsTrue(afterRecover.IsBatcherCheckpointAligned);
        Assert.IsFalse(afterRecover.IsPipelineHealthy);
        CollectionAssert.Contains(
            afterRecover.PipelineHealthFailures.ToArray(),
            nameof(afterRecover.IsSettlementRetrying));
        Assert.IsTrue(hasConsumedDeposit(0, 1));
        Assert.IsTrue(hasConsumedDeposit(0, 2));
        Assert.IsTrue(getRpcL1DepositStatus(0, 1) is { ConsumedOnL2: true, IncludedInBatch: 1UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 2) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.AreEqual(BatchStatus.Finalized, getRpcBatchStatus(1));
        Assert.AreEqual(BatchStatus.Finalized, getRpcBatchStatus(2));
        Assert.IsNotNull(getRpcBatch(1));
        Assert.IsNotNull(getRpcBatch(2));
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, getRpcStateRootAtBatch(1));
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, getRpcStateRootAtBatch(2));
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, getLatestRpcStateRoot());
        Assert.IsTrue(getRpcWithdrawalProof(secondWithdrawalLeaf) is { Length: > 0 });
        Assert.IsTrue(getRpcMessageProof(secondOutboundMessageHash) is { Length: > 0 });

        var probe = getHealthProbe();
        Assert.IsTrue(probe.IsSettlementRetrying);
        Assert.IsFalse(probe.IsSettlementPoisoned);
        Assert.AreEqual(2UL, probe.LatestCheckpointBatchNumber);
        Assert.IsTrue(probe.PendingSettlementCount >= 2);
        Assert.AreEqual(2, probe.ConsumedDepositCount);
        Assert.AreEqual(2, probe.MessageOutboxL2ToL1Count);
        Assert.AreEqual(2, probe.KnownForcedInclusionNonceCount);
        Assert.AreEqual(2, probe.KnownInboundNonceCount);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusJson, "\"isSettlementPoisoned\": false");
        StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": 2");
        StringAssert.Contains(statusJson, "\"consumedDepositCount\": 2");
        StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Count\": 2");
        StringAssert.Contains(statusJson, "\"knownForcedInclusionNonceCount\": 2");
        StringAssert.Contains(statusJson, "\"knownInboundNonceCount\": 2");

        var probeJson = formatHealthProbeJson();
        StringAssert.Contains(probeJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(probeJson, "\"latestCheckpointBatchNumber\": 2");
        StringAssert.Contains(probeJson, "\"consumedDepositCount\": 2");

        var statusPath = Path.Combine(chainDir, "soft-seal-second-poison-recover-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        var statusFile = File.ReadAllText(statusPath);
        StringAssert.Contains(statusFile, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusFile, "\"consumedDepositCount\": 2");
        StringAssert.Contains(statusFile, "\"messageOutboxL2ToL1Count\": 2");
        StringAssert.Contains(statusFile, "\"latestCheckpointBatchNumber\": 2");

        var probePath = Path.Combine(chainDir, "soft-seal-second-poison-recover-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        StringAssert.Contains(File.ReadAllText(probePath), "\"isSettlementRetrying\": true");
        StringAssert.Contains(File.ReadAllText(probePath), "\"consumedDepositCount\": 2");

        var durablePath = Path.Combine(chainDir, "soft-seal-second-poison-recover.json");
        File.WriteAllText(durablePath, $$"""
            {
              "secondPoisonBlockedBatch": {{blockedBatch}},
              "pendingSettlementCount": {{afterRecover.PendingSettlementCount}},
              "latestCheckpointBatchNumber": {{afterRecover.LatestCheckpointBatchNumber}},
              "consumedDepositCount": {{afterRecover.ConsumedDepositCount}},
              "messageOutboxL2ToL1Count": {{afterRecover.MessageOutboxL2ToL1Count}},
              "knownForcedInclusionNonceCount": {{afterRecover.KnownForcedInclusionNonceCount}},
              "knownInboundNonceCount": {{afterRecover.KnownInboundNonceCount}},
              "rpcBatch1Status": "{{getRpcBatchStatus(1)}}",
              "rpcBatch2Status": "{{getRpcBatchStatus(2)}}",
              "secondWithdrawalProofPresent": true,
              "secondMessageProofPresent": true,
              "isSettlementRetrying": true,
              "isSettlementPoisoned": false,
              "isOfflinePassportComplete": true
            }
            """);
        Assert.IsTrue(File.Exists(durablePath));
        var durableFile = File.ReadAllText(durablePath);
        StringAssert.Contains(durableFile, "\"rpcBatch1Status\": \"Finalized\"");
        StringAssert.Contains(durableFile, "\"rpcBatch2Status\": \"Finalized\"");
        StringAssert.Contains(durableFile, "\"consumedDepositCount\": 2");
        StringAssert.Contains(durableFile, "\"messageOutboxL2ToL1Count\": 2");
        StringAssert.Contains(durableFile, "\"isSettlementRetrying\": true");
        StringAssert.Contains(durableFile, "\"isSettlementPoisoned\": false");
    }

    /// <summary>
    /// SoftSeal after second poison→recover: re-publish multi-batch local DA and process a
    /// third offline deposit (nonce 3, IncludedInBatch=2) while settle remains Retrying with
    /// pending≥2. Does not claim L1 settle / production DA / deposit scan.
    /// </summary>
    private static void AssertSoftSealAfterSecondRecoverDaAndThirdDeposit(
        Func<DAPublishRequest, Task<DAReceipt>> publishDaAsync,
        Func<DAReceipt, Task<bool>> isDaAvailableAsync,
        Func<bool> supportsLocalDaReader,
        Func<IDAReader> createLocalDaReader,
        Func<CrossChainMessage, MintInstruction> processDeposit,
        Func<uint, ulong, bool> hasConsumedDeposit,
        Func<int> consumedDepositCount,
        Action<DepositStatus> recordRpcDeposit,
        Func<uint, ulong, DepositStatus?> getRpcL1DepositStatus,
        Func<Task<int>> scanSharedBridgeDepositsAsync,
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> exportPrometheusMetrics,
        Func<string, Task> writePrometheusMetricsAsync,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        string chainDir)
    {
        Assert.IsTrue(supportsLocalDaReader());
        var da1Payload = new byte[] { 0xDA, 0xB1, 0x01 };
        var da1 = publishDaAsync(new DAPublishRequest
        {
            ChainId = 20260716u,
            BatchNumber = 1,
            Payload = da1Payload,
        }).GetAwaiter().GetResult();
        Assert.AreEqual(DAMode.Local, da1.Layer);
        Assert.AreEqual(DAReceiptKind.LocalPersistence, da1.Kind);
        Assert.IsTrue(isDaAvailableAsync(da1).GetAwaiter().GetResult());
        var da1Read = createLocalDaReader().ReadAsync(da1).AsTask().GetAwaiter().GetResult();
        Assert.IsTrue(da1Read is { Length: 3 });
        CollectionAssert.AreEqual(da1Payload, da1Read!.Value.ToArray());

        var da2Payload = new byte[] { 0xDA, 0xB2, 0x02 };
        var da2 = publishDaAsync(new DAPublishRequest
        {
            ChainId = 20260716u,
            BatchNumber = 2,
            Payload = da2Payload,
        }).GetAwaiter().GetResult();
        Assert.AreEqual(DAMode.Local, da2.Layer);
        Assert.IsTrue(isDaAvailableAsync(da2).GetAwaiter().GetResult());
        var da2Read = createLocalDaReader().ReadAsync(da2).AsTask().GetAwaiter().GetResult();
        Assert.IsTrue(da2Read is { Length: 3 });
        CollectionAssert.AreEqual(da2Payload, da2Read!.Value.ToArray());

        // Third offline mint while still Retrying after second recover.
        var softL1Asset = UInt160.Parse("0x" + new string('1', 40));
        var softL2Asset = UInt160.Parse("0x" + new string('2', 40));
        var softDepositPayload = new DepositPayload
        {
            L1Asset = softL1Asset,
            L2Recipient = Account(0x55),
            Amount = new BigInteger(3_000),
        };
        var softDepositMsg = new CrossChainMessage
        {
            SourceChainId = 0,
            TargetChainId = 20260716u,
            Nonce = 3,
            Sender = Account(0x66),
            Receiver = Account(0x55),
            MessageType = MessageType.Deposit,
            Payload = softDepositPayload.Encode(),
            MessageHash = UInt256.Zero,
        };
        var softMint = processDeposit(softDepositMsg);
        Assert.AreEqual(softL2Asset, softMint.L2Asset);
        Assert.IsTrue(hasConsumedDeposit(0, 1));
        Assert.IsTrue(hasConsumedDeposit(0, 2));
        Assert.IsTrue(hasConsumedDeposit(0, 3));
        Assert.AreEqual(3, consumedDepositCount());
        recordRpcDeposit(new DepositStatus(0, 3, ConsumedOnL2: true, IncludedInBatch: 2));
        Assert.IsTrue(getRpcL1DepositStatus(0, 1) is { ConsumedOnL2: true, IncludedInBatch: 1UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 2) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 3) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.AreEqual(0, scanSharedBridgeDepositsAsync().GetAwaiter().GetResult());

        var status = getOperatorStatus();
        Assert.AreEqual(3, status.ConsumedDepositCount);
        Assert.AreEqual(2, status.MessageOutboxL2ToL1Count);
        Assert.AreEqual(2, status.KnownForcedInclusionNonceCount);
        Assert.AreEqual(2, status.KnownInboundNonceCount);
        Assert.AreEqual(2UL, status.LatestCheckpointBatchNumber);
        Assert.IsTrue(status.PendingSettlementCount >= 2);
        Assert.IsTrue(status.IsSettlementRetrying);
        Assert.IsFalse(status.IsSettlementPoisoned);
        Assert.IsFalse(status.IsSettlementIdle);
        Assert.IsTrue(status.IsOfflinePassportComplete);
        Assert.IsTrue(status.IsOperatorReady);
        Assert.IsTrue(status.IsBatcherCheckpointAligned);
        Assert.IsFalse(status.IsPipelineHealthy);
        CollectionAssert.Contains(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.IsSettlementRetrying));

        var probe = getHealthProbe();
        Assert.AreEqual(3, probe.ConsumedDepositCount);
        Assert.AreEqual(2UL, probe.LatestCheckpointBatchNumber);
        Assert.IsTrue(probe.PendingSettlementCount >= 2);
        Assert.IsTrue(probe.IsSettlementRetrying);
        Assert.IsFalse(probe.IsSettlementPoisoned);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"consumedDepositCount\": 3");
        StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Count\": 2");
        StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": 2");
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusJson, "\"isSettlementPoisoned\": false");

        var hostProm = exportPrometheusMetrics();
        Assert.IsFalse(string.IsNullOrWhiteSpace(hostProm));
        var hostPromPath = Path.Combine(chainDir, "soft-seal-after-second-recover-host.prom");
        writePrometheusMetricsAsync(hostPromPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(hostPromPath));
        Assert.AreEqual(hostProm, File.ReadAllText(hostPromPath));

        var statusPath = Path.Combine(chainDir, "soft-seal-after-second-recover-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        StringAssert.Contains(File.ReadAllText(statusPath), "\"consumedDepositCount\": 3");
        StringAssert.Contains(File.ReadAllText(statusPath), "\"isSettlementRetrying\": true");

        var probePath = Path.Combine(chainDir, "soft-seal-after-second-recover-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        StringAssert.Contains(File.ReadAllText(probePath), "\"consumedDepositCount\": 3");

        var durablePath = Path.Combine(chainDir, "soft-seal-after-second-recover-da-deposit.json");
        File.WriteAllText(durablePath, $$"""
            {
              "daBatch1Layer": "{{da1.Layer}}",
              "daBatch2Layer": "{{da2.Layer}}",
              "daBatch1Available": true,
              "daBatch2Available": true,
              "consumedDepositCount": {{status.ConsumedDepositCount}},
              "deposit3IncludedInBatch": 2,
              "messageOutboxL2ToL1Count": {{status.MessageOutboxL2ToL1Count}},
              "knownForcedInclusionNonceCount": {{status.KnownForcedInclusionNonceCount}},
              "knownInboundNonceCount": {{status.KnownInboundNonceCount}},
              "latestCheckpointBatchNumber": {{status.LatestCheckpointBatchNumber}},
              "pendingSettlementCount": {{status.PendingSettlementCount}},
              "isSettlementRetrying": true,
              "isSettlementPoisoned": false,
              "isOfflinePassportComplete": true
            }
            """);
        Assert.IsTrue(File.Exists(durablePath));
        var durableFile = File.ReadAllText(durablePath);
        StringAssert.Contains(durableFile, "\"daBatch1Layer\": \"Local\"");
        StringAssert.Contains(durableFile, "\"daBatch2Layer\": \"Local\"");
        StringAssert.Contains(durableFile, "\"consumedDepositCount\": 3");
        StringAssert.Contains(durableFile, "\"deposit3IncludedInBatch\": 2");
        StringAssert.Contains(durableFile, "\"messageOutboxL2ToL1Count\": 2");
        StringAssert.Contains(durableFile, "\"isSettlementRetrying\": true");
        StringAssert.Contains(durableFile, "\"isOfflinePassportComplete\": true");
    }

    /// <summary>
    /// SoftSeal after second recover + third deposit: third withdrawal seal + L2→L1 outbox
    /// enqueue + third FI/inbound nonces + RPC withdrawal/message/router proofs while settle
    /// remains Retrying with multi-batch pending. Does not claim L1 claim / FI drain / settle.
    /// Returns third withdrawal leaf + outbound hash for follow-on third-poison retention pins.
    /// </summary>
    private static (UInt256 WithdrawalLeaf, UInt256 OutboundMessageHash) AssertSoftSealAfterSecondRecoverThirdOutboundAndFi(
        Func<WithdrawalRequest, UInt256> stageWithdrawal,
        Func<int> stagedWithdrawalCount,
        Func<(UInt256 Root, WithdrawalTree Tree)> sealWithdrawalBatch,
        Func<IReadOnlyList<CrossChainMessage>, Task> enqueueOutbound,
        Func<int> messageOutboxL2ToL1Count,
        Func<UInt256> messageOutboxL2ToL1Root,
        Func<ulong, bool> registerForcedInclusionNonce,
        Func<int> knownForcedInclusionNonceCount,
        Func<bool> hasOverdueForcedInclusionCached,
        Action invalidateForcedInclusionCache,
        Func<int> openBatchForcedInclusionCount,
        Func<ulong, bool> registerInboundMessageNonce,
        Func<int> knownInboundNonceCount,
        Action invalidateInboundMessageCache,
        Func<int> openBatchL1MessageCount,
        Func<int> l1InboxPendingCount,
        Action<UInt256, byte[]> recordRpcWithdrawalProof,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcWithdrawalProof,
        Action<UInt256, byte[]> recordRpcMessageProof,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcMessageProof,
        Action<UInt256, ReadOnlyMemory<byte>> recordMessageRouterFinalizedProof,
        Func<UInt256, Task<ReadOnlyMemory<byte>?>> getMessageRouterProofAsync,
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> formatHealthProbeJson,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        string chainDir)
    {
        var softL2Asset = UInt160.Parse("0x" + new string('2', 40));
        var softSender = Account(0x77);
        var wdLeaf = stageWithdrawal(new WithdrawalRequest
        {
            ChainId = 20260716u,
            EmittingContract = softSender,
            L2Sender = softSender,
            L1Recipient = softSender,
            L2Asset = softL2Asset,
            Amount = new BigInteger(100),
            Nonce = 3,
        });
        Assert.AreNotEqual(UInt256.Zero, wdLeaf);
        Assert.IsTrue(stagedWithdrawalCount() >= 1);
        var sealedWd = sealWithdrawalBatch();
        Assert.AreNotEqual(UInt256.Zero, sealedWd.Root);
        Assert.AreEqual(0, stagedWithdrawalCount());
        Assert.IsTrue(sealedWd.Tree.Count >= 1);
        var merkleProof = sealedWd.Tree.GetProof(sealedWd.Tree.Count - 1);
        Assert.AreEqual(wdLeaf, merkleProof.Leaf);
        var proofBytes = MerkleProofSerializer.Encode(merkleProof);
        Assert.IsTrue(proofBytes.Length >= MerkleProofSerializer.HeaderSize);
        recordRpcWithdrawalProof(wdLeaf, proofBytes);
        var storedWdProof = getRpcWithdrawalProof(wdLeaf);
        Assert.IsTrue(storedWdProof is { Length: > 0 });
        CollectionAssert.AreEqual(proofBytes, storedWdProof!.Value.ToArray());

        var outboundDraft = new CrossChainMessage
        {
            SourceChainId = 20260716u,
            TargetChainId = 0,
            Nonce = 11,
            Sender = softSender,
            Receiver = softSender,
            MessageType = MessageType.Event,
            Payload = new byte[] { 0x03 },
            MessageHash = UInt256.Zero,
        };
        var outbound = outboundDraft with { MessageHash = MessageHasher.HashMessage(outboundDraft) };
        enqueueOutbound([outbound]).GetAwaiter().GetResult();
        Assert.AreEqual(3, messageOutboxL2ToL1Count());
        Assert.AreNotEqual(UInt256.Zero, messageOutboxL2ToL1Root());

        var messageProofBytes = outbound.MessageHash.GetSpan().ToArray();
        recordRpcMessageProof(outbound.MessageHash, messageProofBytes);
        var storedMsgProof = getRpcMessageProof(outbound.MessageHash);
        Assert.IsTrue(storedMsgProof is { Length: > 0 });
        CollectionAssert.AreEqual(messageProofBytes, storedMsgProof!.Value.ToArray());
        recordMessageRouterFinalizedProof(outbound.MessageHash, messageProofBytes);
        var routerProof = getMessageRouterProofAsync(outbound.MessageHash).GetAwaiter().GetResult();
        Assert.IsTrue(routerProof is { Length: > 0 });
        CollectionAssert.AreEqual(messageProofBytes, routerProof!.Value.ToArray());

        Assert.IsTrue(registerForcedInclusionNonce(13));
        Assert.IsFalse(registerForcedInclusionNonce(13));
        Assert.AreEqual(3, knownForcedInclusionNonceCount());
        Assert.AreEqual(0, openBatchForcedInclusionCount());
        Assert.IsFalse(hasOverdueForcedInclusionCached());
        invalidateForcedInclusionCache();
        Assert.AreEqual(3, knownForcedInclusionNonceCount());

        Assert.IsTrue(registerInboundMessageNonce(13));
        Assert.IsFalse(registerInboundMessageNonce(13));
        Assert.AreEqual(3, knownInboundNonceCount());
        Assert.AreEqual(0, openBatchL1MessageCount());
        Assert.AreEqual(0, l1InboxPendingCount());
        invalidateInboundMessageCache();
        Assert.AreEqual(3, knownInboundNonceCount());

        var status = getOperatorStatus();
        Assert.AreEqual(3, status.ConsumedDepositCount);
        Assert.AreEqual(3, status.MessageOutboxL2ToL1Count);
        Assert.AreEqual(0, status.StagedWithdrawalCount);
        Assert.AreEqual(3, status.KnownForcedInclusionNonceCount);
        Assert.AreEqual(3, status.KnownInboundNonceCount);
        Assert.IsFalse(status.HasOverdueForcedInclusion);
        Assert.AreEqual(0, status.OpenBatchForcedInclusionCount);
        Assert.AreEqual(0, status.OpenBatchL1MessageCount);
        Assert.AreEqual(2UL, status.LatestCheckpointBatchNumber);
        Assert.IsTrue(status.PendingSettlementCount >= 2);
        Assert.IsTrue(status.IsSettlementRetrying);
        Assert.IsFalse(status.IsSettlementPoisoned);
        Assert.IsFalse(status.IsSettlementIdle);
        Assert.IsTrue(status.IsOfflinePassportComplete);
        Assert.IsTrue(status.IsOperatorReady);
        Assert.IsTrue(status.IsBatcherCheckpointAligned);
        Assert.IsFalse(status.IsPipelineHealthy);
        CollectionAssert.Contains(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.IsSettlementRetrying));
        CollectionAssert.DoesNotContain(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.HasOverdueForcedInclusion));

        var probe = getHealthProbe();
        Assert.AreEqual(3, probe.ConsumedDepositCount);
        Assert.AreEqual(3, probe.MessageOutboxL2ToL1Count);
        Assert.AreEqual(3, probe.KnownForcedInclusionNonceCount);
        Assert.AreEqual(3, probe.KnownInboundNonceCount);
        Assert.IsTrue(probe.IsSettlementRetrying);
        Assert.AreEqual(2UL, probe.LatestCheckpointBatchNumber);
        Assert.IsTrue(probe.PendingSettlementCount >= 2);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"consumedDepositCount\": 3");
        StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Count\": 3");
        StringAssert.Contains(statusJson, "\"knownForcedInclusionNonceCount\": 3");
        StringAssert.Contains(statusJson, "\"knownInboundNonceCount\": 3");
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": 2");

        var probeJson = formatHealthProbeJson();
        StringAssert.Contains(probeJson, "\"messageOutboxL2ToL1Count\": 3");
        StringAssert.Contains(probeJson, "\"knownForcedInclusionNonceCount\": 3");
        StringAssert.Contains(probeJson, "\"knownInboundNonceCount\": 3");
        StringAssert.Contains(probeJson, "\"isSettlementRetrying\": true");

        var statusPath = Path.Combine(chainDir, "soft-seal-after-second-recover-third-outbound-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        var statusFile = File.ReadAllText(statusPath);
        StringAssert.Contains(statusFile, "\"messageOutboxL2ToL1Count\": 3");
        StringAssert.Contains(statusFile, "\"knownForcedInclusionNonceCount\": 3");
        StringAssert.Contains(statusFile, "\"knownInboundNonceCount\": 3");
        StringAssert.Contains(statusFile, "\"consumedDepositCount\": 3");

        var probePath = Path.Combine(chainDir, "soft-seal-after-second-recover-third-outbound-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        StringAssert.Contains(File.ReadAllText(probePath), "\"messageOutboxL2ToL1Count\": 3");
        StringAssert.Contains(File.ReadAllText(probePath), "\"knownForcedInclusionNonceCount\": 3");

        var durablePath = Path.Combine(chainDir, "soft-seal-after-second-recover-third-outbound.json");
        File.WriteAllText(durablePath, $$"""
            {
              "withdrawalNonce": 3,
              "outboundNonce": 11,
              "consumedDepositCount": {{status.ConsumedDepositCount}},
              "messageOutboxL2ToL1Count": {{status.MessageOutboxL2ToL1Count}},
              "knownForcedInclusionNonceCount": {{status.KnownForcedInclusionNonceCount}},
              "knownInboundNonceCount": {{status.KnownInboundNonceCount}},
              "withdrawalLeaf": "{{wdLeaf}}",
              "withdrawalRoot": "{{sealedWd.Root}}",
              "withdrawalProofBytes": {{proofBytes.Length}},
              "outboundMessageHash": "{{outbound.MessageHash}}",
              "messageProofBytes": {{messageProofBytes.Length}},
              "latestCheckpointBatchNumber": {{status.LatestCheckpointBatchNumber}},
              "pendingSettlementCount": {{status.PendingSettlementCount}},
              "isSettlementRetrying": true,
              "isSettlementPoisoned": false
            }
            """);
        Assert.IsTrue(File.Exists(durablePath));
        var durableFile = File.ReadAllText(durablePath);
        StringAssert.Contains(durableFile, "\"messageOutboxL2ToL1Count\": 3");
        StringAssert.Contains(durableFile, "\"knownForcedInclusionNonceCount\": 3");
        StringAssert.Contains(durableFile, "\"knownInboundNonceCount\": 3");
        StringAssert.Contains(durableFile, "\"consumedDepositCount\": 3");
        StringAssert.Contains(durableFile, "\"withdrawalLeaf\": \"" + wdLeaf + "\"");
        StringAssert.Contains(durableFile, "\"outboundMessageHash\": \"" + outbound.MessageHash + "\"");

        var rpcSurfacePath = Path.Combine(chainDir, "soft-seal-after-second-recover-third-outbound-rpc.json");
        File.WriteAllText(rpcSurfacePath, $$"""
            {
              "withdrawalLeaf": "{{wdLeaf}}",
              "withdrawalRoot": "{{sealedWd.Root}}",
              "withdrawalProofBytes": {{proofBytes.Length}},
              "outboundMessageHash": "{{outbound.MessageHash}}",
              "messageProofBytes": {{messageProofBytes.Length}},
              "routerProofBytes": {{messageProofBytes.Length}},
              "consumedDepositCount": 3,
              "messageOutboxL2ToL1Count": 3,
              "knownForcedInclusionNonceCount": 3,
              "knownInboundNonceCount": 3,
              "isSettlementRetrying": true
            }
            """);
        Assert.IsTrue(File.Exists(rpcSurfacePath));
        var rpcSurface = File.ReadAllText(rpcSurfacePath);
        StringAssert.Contains(rpcSurface, "\"withdrawalLeaf\": \"" + wdLeaf + "\"");
        StringAssert.Contains(rpcSurface, "\"outboundMessageHash\": \"" + outbound.MessageHash + "\"");
        StringAssert.Contains(rpcSurface, "\"messageOutboxL2ToL1Count\": 3");
        StringAssert.Contains(rpcSurface, "\"knownForcedInclusionNonceCount\": 3");
        return (wdLeaf, outbound.MessageHash);
    }

    /// <summary>
    /// SoftSeal full soft path after second recover + triple deposit/outbox/FI: third
    /// Reconcile→Poison→Recover cycle must retain multi-batch RPC tip, triple deposits,
    /// triple outbox, triple FI/inbound known counts, and third-outbound proofs.
    /// Does not claim L1 settle.
    /// </summary>
    private static void AssertSoftSealThirdPoisonRecoverRetention(
        Func<Task> reconcileAsync,
        Func<Task> submitNextAsync,
        Func<ulong, UInt256, Task> recoverPoisonedBatchAsync,
        Func<Task<bool>> isSettlementPoisonedAsync,
        Func<int> getPendingCount,
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> formatHealthProbeJson,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        Func<ulong, L2BatchCommitment?> getRpcBatch,
        Func<ulong, BatchStatus> getRpcBatchStatus,
        Func<ulong, UInt256> getRpcStateRootAtBatch,
        Func<UInt256> getLatestRpcStateRoot,
        Func<uint, ulong, bool> hasConsumedDeposit,
        Func<uint, ulong, DepositStatus?> getRpcL1DepositStatus,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcWithdrawalProof,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcMessageProof,
        UInt256 thirdWithdrawalLeaf,
        UInt256 thirdOutboundMessageHash,
        string chainDir)
    {
        var before = getOperatorStatus();
        Assert.IsTrue(before.IsSettlementRetrying);
        Assert.IsFalse(before.IsSettlementPoisoned);
        Assert.IsTrue(before.PendingSettlementCount >= 2);
        Assert.AreEqual(2UL, before.LatestCheckpointBatchNumber);
        Assert.AreEqual(3, before.ConsumedDepositCount);
        Assert.AreEqual(3, before.MessageOutboxL2ToL1Count);
        Assert.AreEqual(3, before.KnownForcedInclusionNonceCount);
        Assert.AreEqual(3, before.KnownInboundNonceCount);
        Assert.IsTrue(getPendingCount() >= 2);

        // RetryCount was reset by prior recover — re-escalate until Poisoned.
        LocalHostOperatorStatus afterPoison = before;
        for (var attempt = 0; attempt < 16; attempt++)
        {
            try
            {
                reconcileAsync().GetAwaiter().GetResult();
            }
            catch (OverflowException)
            {
            }
            catch (Exception)
            {
            }

            submitNextAsync().GetAwaiter().GetResult();
            afterPoison = getOperatorStatus();
            if (afterPoison.IsSettlementPoisoned)
                break;
        }

        Assert.IsTrue(afterPoison.IsSettlementPoisoned);
        Assert.IsFalse(afterPoison.IsSettlementRetrying);
        CollectionAssert.Contains(
            afterPoison.PipelineHealthFailures.ToArray(),
            nameof(afterPoison.IsSettlementPoisoned));
        Assert.IsNotNull(afterPoison.Recovery.BlockedBatchNumber);
        Assert.IsNotNull(afterPoison.Recovery.ArtifactContentHash);
        var blockedBatch = afterPoison.Recovery.BlockedBatchNumber!.Value;
        var contentHash = afterPoison.Recovery.ArtifactContentHash!;
        Assert.IsTrue(afterPoison.PendingSettlementCount >= 2);
        Assert.AreEqual(2UL, afterPoison.LatestCheckpointBatchNumber);
        Assert.AreEqual(3, afterPoison.ConsumedDepositCount);
        Assert.AreEqual(3, afterPoison.MessageOutboxL2ToL1Count);
        Assert.AreEqual(3, afterPoison.KnownForcedInclusionNonceCount);
        Assert.AreEqual(3, afterPoison.KnownInboundNonceCount);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => recoverPoisonedBatchAsync(blockedBatch, UInt256.Zero).GetAwaiter().GetResult());
        Assert.IsTrue(isSettlementPoisonedAsync().GetAwaiter().GetResult());
        recoverPoisonedBatchAsync(blockedBatch, contentHash).GetAwaiter().GetResult();

        var afterRecover = getOperatorStatus();
        Assert.IsFalse(afterRecover.IsSettlementPoisoned);
        Assert.IsTrue(afterRecover.IsSettlementRetrying);
        Assert.AreEqual(SettlementRecoveryState.Retrying, afterRecover.Recovery.State);
        Assert.AreEqual(0, afterRecover.Recovery.RetryCount);
        Assert.IsTrue(getPendingCount() >= 2);
        Assert.IsTrue(afterRecover.PendingSettlementCount >= 2);
        Assert.AreEqual(2UL, afterRecover.LatestCheckpointBatchNumber);
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, afterRecover.LatestCheckpointPostStateRoot);
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, afterRecover.LatestRpcStateRoot);
        Assert.AreEqual(3, afterRecover.ConsumedDepositCount);
        Assert.AreEqual(3, afterRecover.MessageOutboxL2ToL1Count);
        Assert.AreEqual(3, afterRecover.KnownForcedInclusionNonceCount);
        Assert.AreEqual(3, afterRecover.KnownInboundNonceCount);
        Assert.IsTrue(afterRecover.IsOfflinePassportComplete);
        Assert.IsTrue(afterRecover.IsOperatorReady);
        Assert.IsTrue(afterRecover.IsBatcherCheckpointAligned);
        Assert.IsFalse(afterRecover.IsPipelineHealthy);
        CollectionAssert.Contains(
            afterRecover.PipelineHealthFailures.ToArray(),
            nameof(afterRecover.IsSettlementRetrying));
        Assert.IsTrue(hasConsumedDeposit(0, 1));
        Assert.IsTrue(hasConsumedDeposit(0, 2));
        Assert.IsTrue(hasConsumedDeposit(0, 3));
        Assert.IsTrue(getRpcL1DepositStatus(0, 1) is { ConsumedOnL2: true, IncludedInBatch: 1UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 2) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 3) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.AreEqual(BatchStatus.Finalized, getRpcBatchStatus(1));
        Assert.AreEqual(BatchStatus.Finalized, getRpcBatchStatus(2));
        Assert.IsNotNull(getRpcBatch(1));
        Assert.IsNotNull(getRpcBatch(2));
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, getRpcStateRootAtBatch(1));
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, getRpcStateRootAtBatch(2));
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, getLatestRpcStateRoot());
        Assert.IsTrue(getRpcWithdrawalProof(thirdWithdrawalLeaf) is { Length: > 0 });
        Assert.IsTrue(getRpcMessageProof(thirdOutboundMessageHash) is { Length: > 0 });

        var probe = getHealthProbe();
        Assert.IsTrue(probe.IsSettlementRetrying);
        Assert.IsFalse(probe.IsSettlementPoisoned);
        Assert.AreEqual(2UL, probe.LatestCheckpointBatchNumber);
        Assert.IsTrue(probe.PendingSettlementCount >= 2);
        Assert.AreEqual(3, probe.ConsumedDepositCount);
        Assert.AreEqual(3, probe.MessageOutboxL2ToL1Count);
        Assert.AreEqual(3, probe.KnownForcedInclusionNonceCount);
        Assert.AreEqual(3, probe.KnownInboundNonceCount);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusJson, "\"isSettlementPoisoned\": false");
        StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": 2");
        StringAssert.Contains(statusJson, "\"consumedDepositCount\": 3");
        StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Count\": 3");
        StringAssert.Contains(statusJson, "\"knownForcedInclusionNonceCount\": 3");
        StringAssert.Contains(statusJson, "\"knownInboundNonceCount\": 3");

        var probeJson = formatHealthProbeJson();
        StringAssert.Contains(probeJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(probeJson, "\"consumedDepositCount\": 3");
        StringAssert.Contains(probeJson, "\"messageOutboxL2ToL1Count\": 3");

        var statusPath = Path.Combine(chainDir, "soft-seal-third-poison-recover-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        var statusFile = File.ReadAllText(statusPath);
        StringAssert.Contains(statusFile, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusFile, "\"consumedDepositCount\": 3");
        StringAssert.Contains(statusFile, "\"messageOutboxL2ToL1Count\": 3");
        StringAssert.Contains(statusFile, "\"latestCheckpointBatchNumber\": 2");

        var probePath = Path.Combine(chainDir, "soft-seal-third-poison-recover-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        StringAssert.Contains(File.ReadAllText(probePath), "\"consumedDepositCount\": 3");
        StringAssert.Contains(File.ReadAllText(probePath), "\"isSettlementRetrying\": true");

        var durablePath = Path.Combine(chainDir, "soft-seal-third-poison-recover.json");
        File.WriteAllText(durablePath, $$"""
            {
              "thirdPoisonBlockedBatch": {{blockedBatch}},
              "pendingSettlementCount": {{afterRecover.PendingSettlementCount}},
              "latestCheckpointBatchNumber": {{afterRecover.LatestCheckpointBatchNumber}},
              "consumedDepositCount": {{afterRecover.ConsumedDepositCount}},
              "messageOutboxL2ToL1Count": {{afterRecover.MessageOutboxL2ToL1Count}},
              "knownForcedInclusionNonceCount": {{afterRecover.KnownForcedInclusionNonceCount}},
              "knownInboundNonceCount": {{afterRecover.KnownInboundNonceCount}},
              "rpcBatch1Status": "{{getRpcBatchStatus(1)}}",
              "rpcBatch2Status": "{{getRpcBatchStatus(2)}}",
              "thirdWithdrawalProofPresent": true,
              "thirdMessageProofPresent": true,
              "isSettlementRetrying": true,
              "isSettlementPoisoned": false,
              "isOfflinePassportComplete": true
            }
            """);
        Assert.IsTrue(File.Exists(durablePath));
        var durableFile = File.ReadAllText(durablePath);
        StringAssert.Contains(durableFile, "\"rpcBatch1Status\": \"Finalized\"");
        StringAssert.Contains(durableFile, "\"rpcBatch2Status\": \"Finalized\"");
        StringAssert.Contains(durableFile, "\"consumedDepositCount\": 3");
        StringAssert.Contains(durableFile, "\"messageOutboxL2ToL1Count\": 3");
        StringAssert.Contains(durableFile, "\"knownForcedInclusionNonceCount\": 3");
        StringAssert.Contains(durableFile, "\"isSettlementRetrying\": true");
        StringAssert.Contains(durableFile, "\"isSettlementPoisoned\": false");
    }

    /// <summary>
    /// SoftSeal full soft path after third recover + quadruple deposit/outbox/FI: fourth
    /// Reconcile→Poison→Recover cycle must retain multi-batch RPC tip, quadruple deposits,
    /// quadruple outbox, quadruple FI/inbound known counts, and fourth-outbound proofs.
    /// Does not claim L1 settle.
    /// </summary>
    private static void AssertSoftSealFourthPoisonRecoverRetention(
        Func<Task> reconcileAsync,
        Func<Task> submitNextAsync,
        Func<ulong, UInt256, Task> recoverPoisonedBatchAsync,
        Func<Task<bool>> isSettlementPoisonedAsync,
        Func<int> getPendingCount,
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> formatHealthProbeJson,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        Func<ulong, L2BatchCommitment?> getRpcBatch,
        Func<ulong, BatchStatus> getRpcBatchStatus,
        Func<ulong, UInt256> getRpcStateRootAtBatch,
        Func<UInt256> getLatestRpcStateRoot,
        Func<uint, ulong, bool> hasConsumedDeposit,
        Func<uint, ulong, DepositStatus?> getRpcL1DepositStatus,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcWithdrawalProof,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcMessageProof,
        UInt256 fourthWithdrawalLeaf,
        UInt256 fourthOutboundMessageHash,
        string chainDir)
    {
        var before = getOperatorStatus();
        Assert.IsTrue(before.IsSettlementRetrying);
        Assert.IsFalse(before.IsSettlementPoisoned);
        Assert.IsTrue(before.PendingSettlementCount >= 2);
        Assert.AreEqual(2UL, before.LatestCheckpointBatchNumber);
        Assert.AreEqual(4, before.ConsumedDepositCount);
        Assert.AreEqual(4, before.MessageOutboxL2ToL1Count);
        Assert.AreEqual(4, before.KnownForcedInclusionNonceCount);
        Assert.AreEqual(4, before.KnownInboundNonceCount);
        Assert.IsTrue(getPendingCount() >= 2);

        // RetryCount was reset by prior recover — re-escalate until Poisoned.
        LocalHostOperatorStatus afterPoison = before;
        for (var attempt = 0; attempt < 16; attempt++)
        {
            try
            {
                reconcileAsync().GetAwaiter().GetResult();
            }
            catch (OverflowException)
            {
            }
            catch (Exception)
            {
            }

            submitNextAsync().GetAwaiter().GetResult();
            afterPoison = getOperatorStatus();
            if (afterPoison.IsSettlementPoisoned)
                break;
        }

        Assert.IsTrue(afterPoison.IsSettlementPoisoned);
        Assert.IsFalse(afterPoison.IsSettlementRetrying);
        CollectionAssert.Contains(
            afterPoison.PipelineHealthFailures.ToArray(),
            nameof(afterPoison.IsSettlementPoisoned));
        Assert.IsNotNull(afterPoison.Recovery.BlockedBatchNumber);
        Assert.IsNotNull(afterPoison.Recovery.ArtifactContentHash);
        var blockedBatch = afterPoison.Recovery.BlockedBatchNumber!.Value;
        var contentHash = afterPoison.Recovery.ArtifactContentHash!;
        Assert.IsTrue(afterPoison.PendingSettlementCount >= 2);
        Assert.AreEqual(2UL, afterPoison.LatestCheckpointBatchNumber);
        Assert.AreEqual(4, afterPoison.ConsumedDepositCount);
        Assert.AreEqual(4, afterPoison.MessageOutboxL2ToL1Count);
        Assert.AreEqual(4, afterPoison.KnownForcedInclusionNonceCount);
        Assert.AreEqual(4, afterPoison.KnownInboundNonceCount);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => recoverPoisonedBatchAsync(blockedBatch, UInt256.Zero).GetAwaiter().GetResult());
        Assert.IsTrue(isSettlementPoisonedAsync().GetAwaiter().GetResult());
        recoverPoisonedBatchAsync(blockedBatch, contentHash).GetAwaiter().GetResult();

        var afterRecover = getOperatorStatus();
        Assert.IsFalse(afterRecover.IsSettlementPoisoned);
        Assert.IsTrue(afterRecover.IsSettlementRetrying);
        Assert.AreEqual(SettlementRecoveryState.Retrying, afterRecover.Recovery.State);
        Assert.AreEqual(0, afterRecover.Recovery.RetryCount);
        Assert.IsTrue(getPendingCount() >= 2);
        Assert.IsTrue(afterRecover.PendingSettlementCount >= 2);
        Assert.AreEqual(2UL, afterRecover.LatestCheckpointBatchNumber);
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, afterRecover.LatestCheckpointPostStateRoot);
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, afterRecover.LatestRpcStateRoot);
        Assert.AreEqual(4, afterRecover.ConsumedDepositCount);
        Assert.AreEqual(4, afterRecover.MessageOutboxL2ToL1Count);
        Assert.AreEqual(4, afterRecover.KnownForcedInclusionNonceCount);
        Assert.AreEqual(4, afterRecover.KnownInboundNonceCount);
        Assert.IsTrue(afterRecover.IsOfflinePassportComplete);
        Assert.IsTrue(afterRecover.IsOperatorReady);
        Assert.IsTrue(afterRecover.IsBatcherCheckpointAligned);
        Assert.IsFalse(afterRecover.IsPipelineHealthy);
        CollectionAssert.Contains(
            afterRecover.PipelineHealthFailures.ToArray(),
            nameof(afterRecover.IsSettlementRetrying));
        Assert.IsTrue(hasConsumedDeposit(0, 1));
        Assert.IsTrue(hasConsumedDeposit(0, 2));
        Assert.IsTrue(hasConsumedDeposit(0, 3));
        Assert.IsTrue(hasConsumedDeposit(0, 4));
        Assert.IsTrue(getRpcL1DepositStatus(0, 1) is { ConsumedOnL2: true, IncludedInBatch: 1UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 2) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 3) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 4) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.AreEqual(BatchStatus.Finalized, getRpcBatchStatus(1));
        Assert.AreEqual(BatchStatus.Finalized, getRpcBatchStatus(2));
        Assert.IsNotNull(getRpcBatch(1));
        Assert.IsNotNull(getRpcBatch(2));
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, getRpcStateRootAtBatch(1));
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, getRpcStateRootAtBatch(2));
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, getLatestRpcStateRoot());
        Assert.IsTrue(getRpcWithdrawalProof(fourthWithdrawalLeaf) is { Length: > 0 });
        Assert.IsTrue(getRpcMessageProof(fourthOutboundMessageHash) is { Length: > 0 });

        var probe = getHealthProbe();
        Assert.IsTrue(probe.IsSettlementRetrying);
        Assert.IsFalse(probe.IsSettlementPoisoned);
        Assert.AreEqual(2UL, probe.LatestCheckpointBatchNumber);
        Assert.IsTrue(probe.PendingSettlementCount >= 2);
        Assert.AreEqual(4, probe.ConsumedDepositCount);
        Assert.AreEqual(4, probe.MessageOutboxL2ToL1Count);
        Assert.AreEqual(4, probe.KnownForcedInclusionNonceCount);
        Assert.AreEqual(4, probe.KnownInboundNonceCount);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusJson, "\"isSettlementPoisoned\": false");
        StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": 2");
        StringAssert.Contains(statusJson, "\"consumedDepositCount\": 4");
        StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Count\": 4");
        StringAssert.Contains(statusJson, "\"knownForcedInclusionNonceCount\": 4");
        StringAssert.Contains(statusJson, "\"knownInboundNonceCount\": 4");

        var probeJson = formatHealthProbeJson();
        StringAssert.Contains(probeJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(probeJson, "\"consumedDepositCount\": 4");
        StringAssert.Contains(probeJson, "\"messageOutboxL2ToL1Count\": 4");

        var statusPath = Path.Combine(chainDir, "soft-seal-fourth-poison-recover-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        var statusFile = File.ReadAllText(statusPath);
        StringAssert.Contains(statusFile, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusFile, "\"consumedDepositCount\": 4");
        StringAssert.Contains(statusFile, "\"messageOutboxL2ToL1Count\": 4");
        StringAssert.Contains(statusFile, "\"latestCheckpointBatchNumber\": 2");

        var probePath = Path.Combine(chainDir, "soft-seal-fourth-poison-recover-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        StringAssert.Contains(File.ReadAllText(probePath), "\"consumedDepositCount\": 4");
        StringAssert.Contains(File.ReadAllText(probePath), "\"isSettlementRetrying\": true");

        var durablePath = Path.Combine(chainDir, "soft-seal-fourth-poison-recover.json");
        File.WriteAllText(durablePath, $$"""
            {
              "fourthPoisonBlockedBatch": {{blockedBatch}},
              "pendingSettlementCount": {{afterRecover.PendingSettlementCount}},
              "latestCheckpointBatchNumber": {{afterRecover.LatestCheckpointBatchNumber}},
              "consumedDepositCount": {{afterRecover.ConsumedDepositCount}},
              "messageOutboxL2ToL1Count": {{afterRecover.MessageOutboxL2ToL1Count}},
              "knownForcedInclusionNonceCount": {{afterRecover.KnownForcedInclusionNonceCount}},
              "knownInboundNonceCount": {{afterRecover.KnownInboundNonceCount}},
              "rpcBatch1Status": "{{getRpcBatchStatus(1)}}",
              "rpcBatch2Status": "{{getRpcBatchStatus(2)}}",
              "fourthWithdrawalProofPresent": true,
              "fourthMessageProofPresent": true,
              "isSettlementRetrying": true,
              "isSettlementPoisoned": false,
              "isOfflinePassportComplete": true
            }
            """);
        Assert.IsTrue(File.Exists(durablePath));
        var durableFile = File.ReadAllText(durablePath);
        StringAssert.Contains(durableFile, "\"rpcBatch1Status\": \"Finalized\"");
        StringAssert.Contains(durableFile, "\"rpcBatch2Status\": \"Finalized\"");
        StringAssert.Contains(durableFile, "\"consumedDepositCount\": 4");
        StringAssert.Contains(durableFile, "\"messageOutboxL2ToL1Count\": 4");
        StringAssert.Contains(durableFile, "\"knownForcedInclusionNonceCount\": 4");
        StringAssert.Contains(durableFile, "\"isSettlementRetrying\": true");
        StringAssert.Contains(durableFile, "\"isSettlementPoisoned\": false");
    }

    /// <summary>
    /// SoftSeal full soft path after fourth recover + quintuple deposit/outbox/FI: fifth
    /// Reconcile→Poison→Recover cycle must retain multi-batch RPC tip, quintuple deposits,
    /// quintuple outbox, quintuple FI/inbound known counts, and fifth-outbound proofs.
    /// Does not claim L1 settle.
    /// </summary>
    private static void AssertSoftSealFifthPoisonRecoverRetention(
        Func<Task> reconcileAsync,
        Func<Task> submitNextAsync,
        Func<ulong, UInt256, Task> recoverPoisonedBatchAsync,
        Func<Task<bool>> isSettlementPoisonedAsync,
        Func<int> getPendingCount,
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> formatHealthProbeJson,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        Func<ulong, L2BatchCommitment?> getRpcBatch,
        Func<ulong, BatchStatus> getRpcBatchStatus,
        Func<ulong, UInt256> getRpcStateRootAtBatch,
        Func<UInt256> getLatestRpcStateRoot,
        Func<uint, ulong, bool> hasConsumedDeposit,
        Func<uint, ulong, DepositStatus?> getRpcL1DepositStatus,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcWithdrawalProof,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcMessageProof,
        UInt256 fifthWithdrawalLeaf,
        UInt256 fifthOutboundMessageHash,
        string chainDir)
    {
        var before = getOperatorStatus();
        Assert.IsTrue(before.IsSettlementRetrying);
        Assert.IsFalse(before.IsSettlementPoisoned);
        Assert.IsTrue(before.PendingSettlementCount >= 2);
        Assert.AreEqual(2UL, before.LatestCheckpointBatchNumber);
        Assert.AreEqual(5, before.ConsumedDepositCount);
        Assert.AreEqual(5, before.MessageOutboxL2ToL1Count);
        Assert.AreEqual(5, before.KnownForcedInclusionNonceCount);
        Assert.AreEqual(5, before.KnownInboundNonceCount);
        Assert.IsTrue(getPendingCount() >= 2);

        // RetryCount was reset by prior recover — re-escalate until Poisoned.
        LocalHostOperatorStatus afterPoison = before;
        for (var attempt = 0; attempt < 16; attempt++)
        {
            try
            {
                reconcileAsync().GetAwaiter().GetResult();
            }
            catch (OverflowException)
            {
            }
            catch (Exception)
            {
            }

            submitNextAsync().GetAwaiter().GetResult();
            afterPoison = getOperatorStatus();
            if (afterPoison.IsSettlementPoisoned)
                break;
        }

        Assert.IsTrue(afterPoison.IsSettlementPoisoned);
        Assert.IsFalse(afterPoison.IsSettlementRetrying);
        CollectionAssert.Contains(
            afterPoison.PipelineHealthFailures.ToArray(),
            nameof(afterPoison.IsSettlementPoisoned));
        Assert.IsNotNull(afterPoison.Recovery.BlockedBatchNumber);
        Assert.IsNotNull(afterPoison.Recovery.ArtifactContentHash);
        var blockedBatch = afterPoison.Recovery.BlockedBatchNumber!.Value;
        var contentHash = afterPoison.Recovery.ArtifactContentHash!;
        Assert.IsTrue(afterPoison.PendingSettlementCount >= 2);
        Assert.AreEqual(2UL, afterPoison.LatestCheckpointBatchNumber);
        Assert.AreEqual(5, afterPoison.ConsumedDepositCount);
        Assert.AreEqual(5, afterPoison.MessageOutboxL2ToL1Count);
        Assert.AreEqual(5, afterPoison.KnownForcedInclusionNonceCount);
        Assert.AreEqual(5, afterPoison.KnownInboundNonceCount);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => recoverPoisonedBatchAsync(blockedBatch, UInt256.Zero).GetAwaiter().GetResult());
        Assert.IsTrue(isSettlementPoisonedAsync().GetAwaiter().GetResult());
        recoverPoisonedBatchAsync(blockedBatch, contentHash).GetAwaiter().GetResult();

        var afterRecover = getOperatorStatus();
        Assert.IsFalse(afterRecover.IsSettlementPoisoned);
        Assert.IsTrue(afterRecover.IsSettlementRetrying);
        Assert.AreEqual(SettlementRecoveryState.Retrying, afterRecover.Recovery.State);
        Assert.AreEqual(0, afterRecover.Recovery.RetryCount);
        Assert.IsTrue(getPendingCount() >= 2);
        Assert.IsTrue(afterRecover.PendingSettlementCount >= 2);
        Assert.AreEqual(2UL, afterRecover.LatestCheckpointBatchNumber);
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, afterRecover.LatestCheckpointPostStateRoot);
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, afterRecover.LatestRpcStateRoot);
        Assert.AreEqual(5, afterRecover.ConsumedDepositCount);
        Assert.AreEqual(5, afterRecover.MessageOutboxL2ToL1Count);
        Assert.AreEqual(5, afterRecover.KnownForcedInclusionNonceCount);
        Assert.AreEqual(5, afterRecover.KnownInboundNonceCount);
        Assert.IsTrue(afterRecover.IsOfflinePassportComplete);
        Assert.IsTrue(afterRecover.IsOperatorReady);
        Assert.IsTrue(afterRecover.IsBatcherCheckpointAligned);
        Assert.IsFalse(afterRecover.IsPipelineHealthy);
        CollectionAssert.Contains(
            afterRecover.PipelineHealthFailures.ToArray(),
            nameof(afterRecover.IsSettlementRetrying));
        Assert.IsTrue(hasConsumedDeposit(0, 1));
        Assert.IsTrue(hasConsumedDeposit(0, 2));
        Assert.IsTrue(hasConsumedDeposit(0, 3));
        Assert.IsTrue(hasConsumedDeposit(0, 4));
        Assert.IsTrue(hasConsumedDeposit(0, 5));
        Assert.IsTrue(getRpcL1DepositStatus(0, 1) is { ConsumedOnL2: true, IncludedInBatch: 1UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 2) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 3) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 4) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 5) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.AreEqual(BatchStatus.Finalized, getRpcBatchStatus(1));
        Assert.AreEqual(BatchStatus.Finalized, getRpcBatchStatus(2));
        Assert.IsNotNull(getRpcBatch(1));
        Assert.IsNotNull(getRpcBatch(2));
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, getRpcStateRootAtBatch(1));
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, getRpcStateRootAtBatch(2));
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, getLatestRpcStateRoot());
        Assert.IsTrue(getRpcWithdrawalProof(fifthWithdrawalLeaf) is { Length: > 0 });
        Assert.IsTrue(getRpcMessageProof(fifthOutboundMessageHash) is { Length: > 0 });

        var probe = getHealthProbe();
        Assert.IsTrue(probe.IsSettlementRetrying);
        Assert.IsFalse(probe.IsSettlementPoisoned);
        Assert.AreEqual(2UL, probe.LatestCheckpointBatchNumber);
        Assert.IsTrue(probe.PendingSettlementCount >= 2);
        Assert.AreEqual(5, probe.ConsumedDepositCount);
        Assert.AreEqual(5, probe.MessageOutboxL2ToL1Count);
        Assert.AreEqual(5, probe.KnownForcedInclusionNonceCount);
        Assert.AreEqual(5, probe.KnownInboundNonceCount);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusJson, "\"isSettlementPoisoned\": false");
        StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": 2");
        StringAssert.Contains(statusJson, "\"consumedDepositCount\": 5");
        StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Count\": 5");
        StringAssert.Contains(statusJson, "\"knownForcedInclusionNonceCount\": 5");
        StringAssert.Contains(statusJson, "\"knownInboundNonceCount\": 5");

        var probeJson = formatHealthProbeJson();
        StringAssert.Contains(probeJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(probeJson, "\"consumedDepositCount\": 5");
        StringAssert.Contains(probeJson, "\"messageOutboxL2ToL1Count\": 5");

        var statusPath = Path.Combine(chainDir, "soft-seal-fifth-poison-recover-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        var statusFile = File.ReadAllText(statusPath);
        StringAssert.Contains(statusFile, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusFile, "\"consumedDepositCount\": 5");
        StringAssert.Contains(statusFile, "\"messageOutboxL2ToL1Count\": 5");
        StringAssert.Contains(statusFile, "\"latestCheckpointBatchNumber\": 2");

        var probePath = Path.Combine(chainDir, "soft-seal-fifth-poison-recover-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        StringAssert.Contains(File.ReadAllText(probePath), "\"consumedDepositCount\": 5");
        StringAssert.Contains(File.ReadAllText(probePath), "\"isSettlementRetrying\": true");

        var durablePath = Path.Combine(chainDir, "soft-seal-fifth-poison-recover.json");
        File.WriteAllText(durablePath, $$"""
            {
              "fifthPoisonBlockedBatch": {{blockedBatch}},
              "pendingSettlementCount": {{afterRecover.PendingSettlementCount}},
              "latestCheckpointBatchNumber": {{afterRecover.LatestCheckpointBatchNumber}},
              "consumedDepositCount": {{afterRecover.ConsumedDepositCount}},
              "messageOutboxL2ToL1Count": {{afterRecover.MessageOutboxL2ToL1Count}},
              "knownForcedInclusionNonceCount": {{afterRecover.KnownForcedInclusionNonceCount}},
              "knownInboundNonceCount": {{afterRecover.KnownInboundNonceCount}},
              "rpcBatch1Status": "{{getRpcBatchStatus(1)}}",
              "rpcBatch2Status": "{{getRpcBatchStatus(2)}}",
              "fifthWithdrawalProofPresent": true,
              "fifthMessageProofPresent": true,
              "isSettlementRetrying": true,
              "isSettlementPoisoned": false,
              "isOfflinePassportComplete": true
            }
            """);
        Assert.IsTrue(File.Exists(durablePath));
        var durableFile = File.ReadAllText(durablePath);
        StringAssert.Contains(durableFile, "\"rpcBatch1Status\": \"Finalized\"");
        StringAssert.Contains(durableFile, "\"rpcBatch2Status\": \"Finalized\"");
        StringAssert.Contains(durableFile, "\"consumedDepositCount\": 5");
        StringAssert.Contains(durableFile, "\"messageOutboxL2ToL1Count\": 5");
        StringAssert.Contains(durableFile, "\"knownForcedInclusionNonceCount\": 5");
        StringAssert.Contains(durableFile, "\"isSettlementRetrying\": true");
        StringAssert.Contains(durableFile, "\"isSettlementPoisoned\": false");
    }

    /// <summary>
    /// SoftSeal full soft path after fifth recover + sextuple deposit/outbox/FI: sixth
    /// Reconcile→Poison→Recover cycle must retain multi-batch RPC tip, sextuple deposits,
    /// sextuple outbox, sextuple FI/inbound known counts, and sixth-outbound proofs.
    /// Does not claim L1 settle.
    /// </summary>
    private static void AssertSoftSealSixthPoisonRecoverRetention(
        Func<Task> reconcileAsync,
        Func<Task> submitNextAsync,
        Func<ulong, UInt256, Task> recoverPoisonedBatchAsync,
        Func<Task<bool>> isSettlementPoisonedAsync,
        Func<int> getPendingCount,
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> formatHealthProbeJson,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        Func<ulong, L2BatchCommitment?> getRpcBatch,
        Func<ulong, BatchStatus> getRpcBatchStatus,
        Func<ulong, UInt256> getRpcStateRootAtBatch,
        Func<UInt256> getLatestRpcStateRoot,
        Func<uint, ulong, bool> hasConsumedDeposit,
        Func<uint, ulong, DepositStatus?> getRpcL1DepositStatus,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcWithdrawalProof,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcMessageProof,
        UInt256 sixthWithdrawalLeaf,
        UInt256 sixthOutboundMessageHash,
        string chainDir)
    {
        var before = getOperatorStatus();
        Assert.IsTrue(before.IsSettlementRetrying);
        Assert.IsFalse(before.IsSettlementPoisoned);
        Assert.IsTrue(before.PendingSettlementCount >= 2);
        Assert.AreEqual(2UL, before.LatestCheckpointBatchNumber);
        Assert.AreEqual(6, before.ConsumedDepositCount);
        Assert.AreEqual(6, before.MessageOutboxL2ToL1Count);
        Assert.AreEqual(6, before.KnownForcedInclusionNonceCount);
        Assert.AreEqual(6, before.KnownInboundNonceCount);
        Assert.IsTrue(getPendingCount() >= 2);

        // RetryCount was reset by prior recover — re-escalate until Poisoned.
        LocalHostOperatorStatus afterPoison = before;
        for (var attempt = 0; attempt < 16; attempt++)
        {
            try
            {
                reconcileAsync().GetAwaiter().GetResult();
            }
            catch (OverflowException)
            {
            }
            catch (Exception)
            {
            }

            submitNextAsync().GetAwaiter().GetResult();
            afterPoison = getOperatorStatus();
            if (afterPoison.IsSettlementPoisoned)
                break;
        }

        Assert.IsTrue(afterPoison.IsSettlementPoisoned);
        Assert.IsFalse(afterPoison.IsSettlementRetrying);
        CollectionAssert.Contains(
            afterPoison.PipelineHealthFailures.ToArray(),
            nameof(afterPoison.IsSettlementPoisoned));
        Assert.IsNotNull(afterPoison.Recovery.BlockedBatchNumber);
        Assert.IsNotNull(afterPoison.Recovery.ArtifactContentHash);
        var blockedBatch = afterPoison.Recovery.BlockedBatchNumber!.Value;
        var contentHash = afterPoison.Recovery.ArtifactContentHash!;
        Assert.IsTrue(afterPoison.PendingSettlementCount >= 2);
        Assert.AreEqual(2UL, afterPoison.LatestCheckpointBatchNumber);
        Assert.AreEqual(6, afterPoison.ConsumedDepositCount);
        Assert.AreEqual(6, afterPoison.MessageOutboxL2ToL1Count);
        Assert.AreEqual(6, afterPoison.KnownForcedInclusionNonceCount);
        Assert.AreEqual(6, afterPoison.KnownInboundNonceCount);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => recoverPoisonedBatchAsync(blockedBatch, UInt256.Zero).GetAwaiter().GetResult());
        Assert.IsTrue(isSettlementPoisonedAsync().GetAwaiter().GetResult());
        recoverPoisonedBatchAsync(blockedBatch, contentHash).GetAwaiter().GetResult();

        var afterRecover = getOperatorStatus();
        Assert.IsFalse(afterRecover.IsSettlementPoisoned);
        Assert.IsTrue(afterRecover.IsSettlementRetrying);
        Assert.AreEqual(SettlementRecoveryState.Retrying, afterRecover.Recovery.State);
        Assert.AreEqual(0, afterRecover.Recovery.RetryCount);
        Assert.IsTrue(getPendingCount() >= 2);
        Assert.IsTrue(afterRecover.PendingSettlementCount >= 2);
        Assert.AreEqual(2UL, afterRecover.LatestCheckpointBatchNumber);
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, afterRecover.LatestCheckpointPostStateRoot);
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, afterRecover.LatestRpcStateRoot);
        Assert.AreEqual(6, afterRecover.ConsumedDepositCount);
        Assert.AreEqual(6, afterRecover.MessageOutboxL2ToL1Count);
        Assert.AreEqual(6, afterRecover.KnownForcedInclusionNonceCount);
        Assert.AreEqual(6, afterRecover.KnownInboundNonceCount);
        Assert.IsTrue(afterRecover.IsOfflinePassportComplete);
        Assert.IsTrue(afterRecover.IsOperatorReady);
        Assert.IsTrue(afterRecover.IsBatcherCheckpointAligned);
        Assert.IsFalse(afterRecover.IsPipelineHealthy);
        CollectionAssert.Contains(
            afterRecover.PipelineHealthFailures.ToArray(),
            nameof(afterRecover.IsSettlementRetrying));
        Assert.IsTrue(hasConsumedDeposit(0, 1));
        Assert.IsTrue(hasConsumedDeposit(0, 2));
        Assert.IsTrue(hasConsumedDeposit(0, 3));
        Assert.IsTrue(hasConsumedDeposit(0, 4));
        Assert.IsTrue(hasConsumedDeposit(0, 5));
        Assert.IsTrue(hasConsumedDeposit(0, 6));
        Assert.IsTrue(getRpcL1DepositStatus(0, 1) is { ConsumedOnL2: true, IncludedInBatch: 1UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 2) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 3) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 4) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 5) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 6) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.AreEqual(BatchStatus.Finalized, getRpcBatchStatus(1));
        Assert.AreEqual(BatchStatus.Finalized, getRpcBatchStatus(2));
        Assert.IsNotNull(getRpcBatch(1));
        Assert.IsNotNull(getRpcBatch(2));
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, getRpcStateRootAtBatch(1));
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, getRpcStateRootAtBatch(2));
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, getLatestRpcStateRoot());
        Assert.IsTrue(getRpcWithdrawalProof(sixthWithdrawalLeaf) is { Length: > 0 });
        Assert.IsTrue(getRpcMessageProof(sixthOutboundMessageHash) is { Length: > 0 });

        var probe = getHealthProbe();
        Assert.IsTrue(probe.IsSettlementRetrying);
        Assert.IsFalse(probe.IsSettlementPoisoned);
        Assert.AreEqual(2UL, probe.LatestCheckpointBatchNumber);
        Assert.IsTrue(probe.PendingSettlementCount >= 2);
        Assert.AreEqual(6, probe.ConsumedDepositCount);
        Assert.AreEqual(6, probe.MessageOutboxL2ToL1Count);
        Assert.AreEqual(6, probe.KnownForcedInclusionNonceCount);
        Assert.AreEqual(6, probe.KnownInboundNonceCount);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusJson, "\"isSettlementPoisoned\": false");
        StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": 2");
        StringAssert.Contains(statusJson, "\"consumedDepositCount\": 6");
        StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Count\": 6");
        StringAssert.Contains(statusJson, "\"knownForcedInclusionNonceCount\": 6");
        StringAssert.Contains(statusJson, "\"knownInboundNonceCount\": 6");

        var probeJson = formatHealthProbeJson();
        StringAssert.Contains(probeJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(probeJson, "\"consumedDepositCount\": 6");
        StringAssert.Contains(probeJson, "\"messageOutboxL2ToL1Count\": 6");

        var statusPath = Path.Combine(chainDir, "soft-seal-sixth-poison-recover-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        var statusFile = File.ReadAllText(statusPath);
        StringAssert.Contains(statusFile, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusFile, "\"consumedDepositCount\": 6");
        StringAssert.Contains(statusFile, "\"messageOutboxL2ToL1Count\": 6");
        StringAssert.Contains(statusFile, "\"latestCheckpointBatchNumber\": 2");

        var probePath = Path.Combine(chainDir, "soft-seal-sixth-poison-recover-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        StringAssert.Contains(File.ReadAllText(probePath), "\"consumedDepositCount\": 6");
        StringAssert.Contains(File.ReadAllText(probePath), "\"isSettlementRetrying\": true");

        var durablePath = Path.Combine(chainDir, "soft-seal-sixth-poison-recover.json");
        File.WriteAllText(durablePath, $$"""
            {
              "sixthPoisonBlockedBatch": {{blockedBatch}},
              "pendingSettlementCount": {{afterRecover.PendingSettlementCount}},
              "latestCheckpointBatchNumber": {{afterRecover.LatestCheckpointBatchNumber}},
              "consumedDepositCount": {{afterRecover.ConsumedDepositCount}},
              "messageOutboxL2ToL1Count": {{afterRecover.MessageOutboxL2ToL1Count}},
              "knownForcedInclusionNonceCount": {{afterRecover.KnownForcedInclusionNonceCount}},
              "knownInboundNonceCount": {{afterRecover.KnownInboundNonceCount}},
              "rpcBatch1Status": "{{getRpcBatchStatus(1)}}",
              "rpcBatch2Status": "{{getRpcBatchStatus(2)}}",
              "sixthWithdrawalProofPresent": true,
              "sixthMessageProofPresent": true,
              "isSettlementRetrying": true,
              "isSettlementPoisoned": false,
              "isOfflinePassportComplete": true
            }
            """);
        Assert.IsTrue(File.Exists(durablePath));
        var durableFile = File.ReadAllText(durablePath);
        StringAssert.Contains(durableFile, "\"rpcBatch1Status\": \"Finalized\"");
        StringAssert.Contains(durableFile, "\"rpcBatch2Status\": \"Finalized\"");
        StringAssert.Contains(durableFile, "\"consumedDepositCount\": 6");
        StringAssert.Contains(durableFile, "\"messageOutboxL2ToL1Count\": 6");
        StringAssert.Contains(durableFile, "\"knownForcedInclusionNonceCount\": 6");
        StringAssert.Contains(durableFile, "\"isSettlementRetrying\": true");
        StringAssert.Contains(durableFile, "\"isSettlementPoisoned\": false");
    }

    /// <summary>
    /// SoftSeal after fifth poison→recover: re-publish local DA for sealed batches 1+2
    /// and process a sixth offline deposit (nonce 6, IncludedInBatch=2) while settle
    /// remains Retrying with multi-batch pending and quintuple outbox/FI known retained.
    /// Does not claim L1 deposit scan / production DA / settle.
    /// </summary>
    /// <summary>
    /// SoftSeal after sixth poison→recover: re-publish local DA for sealed batches 1+2
    /// and process a seventh offline deposit (nonce 7, IncludedInBatch=2) while settle
    /// remains Retrying with multi-batch pending and sextuple outbox/FI known retained.
    /// Does not claim L1 deposit scan / production DA / settle.
    /// </summary>
    private static void AssertSoftSealAfterSixthRecoverDaAndSeventhDeposit(
        Func<DAPublishRequest, Task<DAReceipt>> publishDaAsync,
        Func<DAReceipt, Task<bool>> isDaAvailableAsync,
        Func<bool> supportsLocalDaReader,
        Func<IDAReader> createLocalDaReader,
        Func<CrossChainMessage, MintInstruction> processDeposit,
        Func<uint, ulong, bool> hasConsumedDeposit,
        Func<int> consumedDepositCount,
        Action<DepositStatus> recordRpcDeposit,
        Func<uint, ulong, DepositStatus?> getRpcL1DepositStatus,
        Func<Task<int>> scanSharedBridgeDepositsAsync,
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> exportPrometheusMetrics,
        Func<string, Task> writePrometheusMetricsAsync,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        string chainDir)
    {
        Assert.IsTrue(supportsLocalDaReader());
        var da1Payload = new byte[] { 0xDA, 0xF1, 0x01 };
        var da1 = publishDaAsync(new DAPublishRequest
        {
            ChainId = 20260716u,
            BatchNumber = 1,
            Payload = da1Payload,
        }).GetAwaiter().GetResult();
        Assert.AreEqual(DAMode.Local, da1.Layer);
        Assert.AreEqual(DAReceiptKind.LocalPersistence, da1.Kind);
        Assert.IsTrue(isDaAvailableAsync(da1).GetAwaiter().GetResult());
        var da1Read = createLocalDaReader().ReadAsync(da1).AsTask().GetAwaiter().GetResult();
        Assert.IsTrue(da1Read is { Length: 3 });
        CollectionAssert.AreEqual(da1Payload, da1Read!.Value.ToArray());

        var da2Payload = new byte[] { 0xDA, 0xF2, 0x02 };
        var da2 = publishDaAsync(new DAPublishRequest
        {
            ChainId = 20260716u,
            BatchNumber = 2,
            Payload = da2Payload,
        }).GetAwaiter().GetResult();
        Assert.AreEqual(DAMode.Local, da2.Layer);
        Assert.IsTrue(isDaAvailableAsync(da2).GetAwaiter().GetResult());
        var da2Read = createLocalDaReader().ReadAsync(da2).AsTask().GetAwaiter().GetResult();
        Assert.IsTrue(da2Read is { Length: 3 });
        CollectionAssert.AreEqual(da2Payload, da2Read!.Value.ToArray());

        // Seventh offline mint while still Retrying after sixth recover.
        var softL1Asset = UInt160.Parse("0x" + new string('1', 40));
        var softL2Asset = UInt160.Parse("0x" + new string('2', 40));
        var softDepositPayload = new DepositPayload
        {
            L1Asset = softL1Asset,
            L2Recipient = Account(0x55),
            Amount = new BigInteger(7_000),
        };
        var softDepositMsg = new CrossChainMessage
        {
            SourceChainId = 0,
            TargetChainId = 20260716u,
            Nonce = 7,
            Sender = Account(0x66),
            Receiver = Account(0x55),
            MessageType = MessageType.Deposit,
            Payload = softDepositPayload.Encode(),
            MessageHash = UInt256.Zero,
        };
        var softMint = processDeposit(softDepositMsg);
        Assert.AreEqual(softL2Asset, softMint.L2Asset);
        Assert.IsTrue(hasConsumedDeposit(0, 1));
        Assert.IsTrue(hasConsumedDeposit(0, 2));
        Assert.IsTrue(hasConsumedDeposit(0, 3));
        Assert.IsTrue(hasConsumedDeposit(0, 4));
        Assert.IsTrue(hasConsumedDeposit(0, 5));
        Assert.IsTrue(hasConsumedDeposit(0, 6));
        Assert.IsTrue(hasConsumedDeposit(0, 7));
        Assert.AreEqual(7, consumedDepositCount());
        recordRpcDeposit(new DepositStatus(0, 7, ConsumedOnL2: true, IncludedInBatch: 2));
        Assert.IsTrue(getRpcL1DepositStatus(0, 1) is { ConsumedOnL2: true, IncludedInBatch: 1UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 2) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 3) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 4) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 5) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 6) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 7) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.AreEqual(0, scanSharedBridgeDepositsAsync().GetAwaiter().GetResult());

        var status = getOperatorStatus();
        Assert.AreEqual(7, status.ConsumedDepositCount);
        Assert.AreEqual(6, status.MessageOutboxL2ToL1Count);
        Assert.AreEqual(6, status.KnownForcedInclusionNonceCount);
        Assert.AreEqual(6, status.KnownInboundNonceCount);
        Assert.AreEqual(2UL, status.LatestCheckpointBatchNumber);
        Assert.IsTrue(status.PendingSettlementCount >= 2);
        Assert.IsTrue(status.IsSettlementRetrying);
        Assert.IsFalse(status.IsSettlementPoisoned);
        Assert.IsFalse(status.IsSettlementIdle);
        Assert.IsTrue(status.IsOfflinePassportComplete);
        Assert.IsTrue(status.IsOperatorReady);
        Assert.IsTrue(status.IsBatcherCheckpointAligned);
        Assert.IsFalse(status.IsPipelineHealthy);
        CollectionAssert.Contains(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.IsSettlementRetrying));

        var probe = getHealthProbe();
        Assert.AreEqual(7, probe.ConsumedDepositCount);
        Assert.AreEqual(6, probe.MessageOutboxL2ToL1Count);
        Assert.AreEqual(2UL, probe.LatestCheckpointBatchNumber);
        Assert.IsTrue(probe.PendingSettlementCount >= 2);
        Assert.IsTrue(probe.IsSettlementRetrying);
        Assert.IsFalse(probe.IsSettlementPoisoned);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"consumedDepositCount\": 7");
        StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Count\": 6");
        StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": 2");
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusJson, "\"isSettlementPoisoned\": false");

        var hostProm = exportPrometheusMetrics();
        Assert.IsFalse(string.IsNullOrWhiteSpace(hostProm));
        var hostPromPath = Path.Combine(chainDir, "soft-seal-after-sixth-recover-host.prom");
        writePrometheusMetricsAsync(hostPromPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(hostPromPath));
        Assert.AreEqual(hostProm, File.ReadAllText(hostPromPath));

        var statusPath = Path.Combine(chainDir, "soft-seal-after-sixth-recover-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        StringAssert.Contains(File.ReadAllText(statusPath), "\"consumedDepositCount\": 7");
        StringAssert.Contains(File.ReadAllText(statusPath), "\"isSettlementRetrying\": true");

        var probePath = Path.Combine(chainDir, "soft-seal-after-sixth-recover-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        StringAssert.Contains(File.ReadAllText(probePath), "\"consumedDepositCount\": 7");

        var durablePath = Path.Combine(chainDir, "soft-seal-after-sixth-recover-da-deposit.json");
        File.WriteAllText(durablePath, $$"""
            {
              "daBatch1Layer": "{{da1.Layer}}",
              "daBatch2Layer": "{{da2.Layer}}",
              "daBatch1Available": true,
              "daBatch2Available": true,
              "consumedDepositCount": {{status.ConsumedDepositCount}},
              "deposit7IncludedInBatch": 2,
              "messageOutboxL2ToL1Count": {{status.MessageOutboxL2ToL1Count}},
              "knownForcedInclusionNonceCount": {{status.KnownForcedInclusionNonceCount}},
              "knownInboundNonceCount": {{status.KnownInboundNonceCount}},
              "latestCheckpointBatchNumber": {{status.LatestCheckpointBatchNumber}},
              "pendingSettlementCount": {{status.PendingSettlementCount}},
              "isSettlementRetrying": true,
              "isSettlementPoisoned": false,
              "isOfflinePassportComplete": true
            }
            """);
        Assert.IsTrue(File.Exists(durablePath));
        var durableFile = File.ReadAllText(durablePath);
        StringAssert.Contains(durableFile, "\"daBatch1Layer\": \"Local\"");
        StringAssert.Contains(durableFile, "\"daBatch2Layer\": \"Local\"");
        StringAssert.Contains(durableFile, "\"consumedDepositCount\": 7");
        StringAssert.Contains(durableFile, "\"deposit7IncludedInBatch\": 2");
        StringAssert.Contains(durableFile, "\"messageOutboxL2ToL1Count\": 6");
        StringAssert.Contains(durableFile, "\"isSettlementRetrying\": true");
        StringAssert.Contains(durableFile, "\"isOfflinePassportComplete\": true");
    }

    /// <summary>
    /// SoftSeal after sixth recover + seventh deposit: seventh withdrawal seal + L2→L1 outbox
    /// enqueue + seventh FI/inbound nonces + RPC withdrawal/message/router proofs while settle
    /// remains Retrying with multi-batch pending. Does not claim L1 claim / FI drain / settle.
    /// Returns seventh withdrawal leaf + outbound hash for follow-on pins.
    /// </summary>
    private static (UInt256 WithdrawalLeaf, UInt256 OutboundMessageHash) AssertSoftSealAfterSixthRecoverSeventhOutboundAndFi(
        Func<WithdrawalRequest, UInt256> stageWithdrawal,
        Func<int> stagedWithdrawalCount,
        Func<(UInt256 Root, WithdrawalTree Tree)> sealWithdrawalBatch,
        Func<IReadOnlyList<CrossChainMessage>, Task> enqueueOutbound,
        Func<int> messageOutboxL2ToL1Count,
        Func<UInt256> messageOutboxL2ToL1Root,
        Func<ulong, bool> registerForcedInclusionNonce,
        Func<int> knownForcedInclusionNonceCount,
        Func<bool> hasOverdueForcedInclusionCached,
        Action invalidateForcedInclusionCache,
        Func<int> openBatchForcedInclusionCount,
        Func<ulong, bool> registerInboundMessageNonce,
        Func<int> knownInboundNonceCount,
        Action invalidateInboundMessageCache,
        Func<int> openBatchL1MessageCount,
        Func<int> l1InboxPendingCount,
        Action<UInt256, byte[]> recordRpcWithdrawalProof,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcWithdrawalProof,
        Action<UInt256, byte[]> recordRpcMessageProof,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcMessageProof,
        Action<UInt256, ReadOnlyMemory<byte>> recordMessageRouterFinalizedProof,
        Func<UInt256, Task<ReadOnlyMemory<byte>?>> getMessageRouterProofAsync,
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> formatHealthProbeJson,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        string chainDir)
    {
        var softL2Asset = UInt160.Parse("0x" + new string('2', 40));
        var softSender = Account(0xbb);
        var wdLeaf = stageWithdrawal(new WithdrawalRequest
        {
            ChainId = 20260716u,
            EmittingContract = softSender,
            L2Sender = softSender,
            L1Recipient = softSender,
            L2Asset = softL2Asset,
            Amount = new BigInteger(100),
            Nonce = 7,
        });
        Assert.AreNotEqual(UInt256.Zero, wdLeaf);
        Assert.IsTrue(stagedWithdrawalCount() >= 1);
        var sealedWd = sealWithdrawalBatch();
        Assert.AreNotEqual(UInt256.Zero, sealedWd.Root);
        Assert.AreEqual(0, stagedWithdrawalCount());
        Assert.IsTrue(sealedWd.Tree.Count >= 1);
        var merkleProof = sealedWd.Tree.GetProof(sealedWd.Tree.Count - 1);
        Assert.AreEqual(wdLeaf, merkleProof.Leaf);
        var proofBytes = MerkleProofSerializer.Encode(merkleProof);
        Assert.IsTrue(proofBytes.Length >= MerkleProofSerializer.HeaderSize);
        recordRpcWithdrawalProof(wdLeaf, proofBytes);
        var storedWdProof = getRpcWithdrawalProof(wdLeaf);
        Assert.IsTrue(storedWdProof is { Length: > 0 });
        CollectionAssert.AreEqual(proofBytes, storedWdProof!.Value.ToArray());

        var outboundDraft = new CrossChainMessage
        {
            SourceChainId = 20260716u,
            TargetChainId = 0,
            Nonce = 15,
            Sender = softSender,
            Receiver = softSender,
            MessageType = MessageType.Event,
            Payload = new byte[] { 0x07 },
            MessageHash = UInt256.Zero,
        };
        var outbound = outboundDraft with { MessageHash = MessageHasher.HashMessage(outboundDraft) };
        enqueueOutbound([outbound]).GetAwaiter().GetResult();
        Assert.AreEqual(7, messageOutboxL2ToL1Count());
        Assert.AreNotEqual(UInt256.Zero, messageOutboxL2ToL1Root());

        var messageProofBytes = outbound.MessageHash.GetSpan().ToArray();
        recordRpcMessageProof(outbound.MessageHash, messageProofBytes);
        var storedMsgProof = getRpcMessageProof(outbound.MessageHash);
        Assert.IsTrue(storedMsgProof is { Length: > 0 });
        CollectionAssert.AreEqual(messageProofBytes, storedMsgProof!.Value.ToArray());
        recordMessageRouterFinalizedProof(outbound.MessageHash, messageProofBytes);
        var routerProof = getMessageRouterProofAsync(outbound.MessageHash).GetAwaiter().GetResult();
        Assert.IsTrue(routerProof is { Length: > 0 });
        CollectionAssert.AreEqual(messageProofBytes, routerProof!.Value.ToArray());

        Assert.IsTrue(registerForcedInclusionNonce(17));
        Assert.IsFalse(registerForcedInclusionNonce(17));
        Assert.AreEqual(7, knownForcedInclusionNonceCount());
        Assert.AreEqual(0, openBatchForcedInclusionCount());
        Assert.IsFalse(hasOverdueForcedInclusionCached());
        invalidateForcedInclusionCache();
        Assert.AreEqual(7, knownForcedInclusionNonceCount());

        Assert.IsTrue(registerInboundMessageNonce(17));
        Assert.IsFalse(registerInboundMessageNonce(17));
        Assert.AreEqual(7, knownInboundNonceCount());
        Assert.AreEqual(0, openBatchL1MessageCount());
        Assert.AreEqual(0, l1InboxPendingCount());
        invalidateInboundMessageCache();
        Assert.AreEqual(7, knownInboundNonceCount());

        var status = getOperatorStatus();
        Assert.AreEqual(7, status.ConsumedDepositCount);
        Assert.AreEqual(7, status.MessageOutboxL2ToL1Count);
        Assert.AreEqual(0, status.StagedWithdrawalCount);
        Assert.AreEqual(7, status.KnownForcedInclusionNonceCount);
        Assert.AreEqual(7, status.KnownInboundNonceCount);
        Assert.IsFalse(status.HasOverdueForcedInclusion);
        Assert.AreEqual(0, status.OpenBatchForcedInclusionCount);
        Assert.AreEqual(0, status.OpenBatchL1MessageCount);
        Assert.AreEqual(2UL, status.LatestCheckpointBatchNumber);
        Assert.IsTrue(status.PendingSettlementCount >= 2);
        Assert.IsTrue(status.IsSettlementRetrying);
        Assert.IsFalse(status.IsSettlementPoisoned);
        Assert.IsFalse(status.IsSettlementIdle);
        Assert.IsTrue(status.IsOfflinePassportComplete);
        Assert.IsTrue(status.IsOperatorReady);
        Assert.IsTrue(status.IsBatcherCheckpointAligned);
        Assert.IsFalse(status.IsPipelineHealthy);
        CollectionAssert.Contains(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.IsSettlementRetrying));
        CollectionAssert.DoesNotContain(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.HasOverdueForcedInclusion));

        var probe = getHealthProbe();
        Assert.AreEqual(7, probe.ConsumedDepositCount);
        Assert.AreEqual(7, probe.MessageOutboxL2ToL1Count);
        Assert.AreEqual(7, probe.KnownForcedInclusionNonceCount);
        Assert.AreEqual(7, probe.KnownInboundNonceCount);
        Assert.IsTrue(probe.IsSettlementRetrying);
        Assert.AreEqual(2UL, probe.LatestCheckpointBatchNumber);
        Assert.IsTrue(probe.PendingSettlementCount >= 2);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"consumedDepositCount\": 7");
        StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Count\": 7");
        StringAssert.Contains(statusJson, "\"knownForcedInclusionNonceCount\": 7");
        StringAssert.Contains(statusJson, "\"knownInboundNonceCount\": 7");
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": 2");

        var probeJson = formatHealthProbeJson();
        StringAssert.Contains(probeJson, "\"messageOutboxL2ToL1Count\": 7");
        StringAssert.Contains(probeJson, "\"knownForcedInclusionNonceCount\": 7");
        StringAssert.Contains(probeJson, "\"knownInboundNonceCount\": 7");
        StringAssert.Contains(probeJson, "\"isSettlementRetrying\": true");

        var statusPath = Path.Combine(chainDir, "soft-seal-after-sixth-recover-seventh-outbound-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        var statusFile = File.ReadAllText(statusPath);
        StringAssert.Contains(statusFile, "\"messageOutboxL2ToL1Count\": 7");
        StringAssert.Contains(statusFile, "\"knownForcedInclusionNonceCount\": 7");
        StringAssert.Contains(statusFile, "\"knownInboundNonceCount\": 7");
        StringAssert.Contains(statusFile, "\"consumedDepositCount\": 7");

        var probePath = Path.Combine(chainDir, "soft-seal-after-sixth-recover-seventh-outbound-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        StringAssert.Contains(File.ReadAllText(probePath), "\"messageOutboxL2ToL1Count\": 7");
        StringAssert.Contains(File.ReadAllText(probePath), "\"knownForcedInclusionNonceCount\": 7");

        var durablePath = Path.Combine(chainDir, "soft-seal-after-sixth-recover-seventh-outbound.json");
        File.WriteAllText(durablePath, $$"""
            {
              "withdrawalNonce": 7,
              "outboundNonce": 15,
              "consumedDepositCount": {{status.ConsumedDepositCount}},
              "messageOutboxL2ToL1Count": {{status.MessageOutboxL2ToL1Count}},
              "knownForcedInclusionNonceCount": {{status.KnownForcedInclusionNonceCount}},
              "knownInboundNonceCount": {{status.KnownInboundNonceCount}},
              "withdrawalLeaf": "{{wdLeaf}}",
              "withdrawalRoot": "{{sealedWd.Root}}",
              "withdrawalProofBytes": {{proofBytes.Length}},
              "outboundMessageHash": "{{outbound.MessageHash}}",
              "messageProofBytes": {{messageProofBytes.Length}},
              "latestCheckpointBatchNumber": {{status.LatestCheckpointBatchNumber}},
              "pendingSettlementCount": {{status.PendingSettlementCount}},
              "isSettlementRetrying": true,
              "isSettlementPoisoned": false,
              "isOfflinePassportComplete": true
            }
            """);
        Assert.IsTrue(File.Exists(durablePath));
        var durableFile = File.ReadAllText(durablePath);
        StringAssert.Contains(durableFile, "\"messageOutboxL2ToL1Count\": 7");
        StringAssert.Contains(durableFile, "\"knownForcedInclusionNonceCount\": 7");
        StringAssert.Contains(durableFile, "\"consumedDepositCount\": 7");
        StringAssert.Contains(durableFile, "\"isSettlementRetrying\": true");

        var rpcSurfacePath = Path.Combine(chainDir, "soft-seal-after-sixth-recover-seventh-outbound-rpc.json");
        File.WriteAllText(rpcSurfacePath, $$"""
            {
              "withdrawalLeaf": "{{wdLeaf}}",
              "outboundMessageHash": "{{outbound.MessageHash}}",
              "withdrawalProofBytes": {{proofBytes.Length}},
              "messageProofBytes": {{messageProofBytes.Length}},
              "messageOutboxL2ToL1Count": 7,
              "knownForcedInclusionNonceCount": 7,
              "knownInboundNonceCount": 7,
              "consumedDepositCount": 7,
              "isSettlementRetrying": true
            }
            """);
        Assert.IsTrue(File.Exists(rpcSurfacePath));
        var rpcSurface = File.ReadAllText(rpcSurfacePath);
        StringAssert.Contains(rpcSurface, "\"withdrawalLeaf\": \"" + wdLeaf + "\"");
        StringAssert.Contains(rpcSurface, "\"outboundMessageHash\": \"" + outbound.MessageHash + "\"");
        StringAssert.Contains(rpcSurface, "\"messageOutboxL2ToL1Count\": 7");
        StringAssert.Contains(rpcSurface, "\"knownForcedInclusionNonceCount\": 7");
        return (wdLeaf, outbound.MessageHash);
    }

    /// <summary>
    /// SoftSeal full soft path after sixth recover + septuple deposit/outbox/FI: seventh
    /// Reconcile→Poison→Recover cycle must retain multi-batch RPC tip, septuple deposits,
    /// septuple outbox, septuple FI/inbound known counts, and seventh-outbound proofs.
    /// Does not claim L1 settle.
    /// </summary>
    private static void AssertSoftSealSeventhPoisonRecoverRetention(
        Func<Task> reconcileAsync,
        Func<Task> submitNextAsync,
        Func<ulong, UInt256, Task> recoverPoisonedBatchAsync,
        Func<Task<bool>> isSettlementPoisonedAsync,
        Func<int> getPendingCount,
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> formatHealthProbeJson,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        Func<ulong, L2BatchCommitment?> getRpcBatch,
        Func<ulong, BatchStatus> getRpcBatchStatus,
        Func<ulong, UInt256> getRpcStateRootAtBatch,
        Func<UInt256> getLatestRpcStateRoot,
        Func<uint, ulong, bool> hasConsumedDeposit,
        Func<uint, ulong, DepositStatus?> getRpcL1DepositStatus,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcWithdrawalProof,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcMessageProof,
        UInt256 seventhWithdrawalLeaf,
        UInt256 seventhOutboundMessageHash,
        string chainDir)
    {
        var before = getOperatorStatus();
        Assert.IsTrue(before.IsSettlementRetrying);
        Assert.IsFalse(before.IsSettlementPoisoned);
        Assert.IsTrue(before.PendingSettlementCount >= 2);
        Assert.AreEqual(2UL, before.LatestCheckpointBatchNumber);
        Assert.AreEqual(7, before.ConsumedDepositCount);
        Assert.AreEqual(7, before.MessageOutboxL2ToL1Count);
        Assert.AreEqual(7, before.KnownForcedInclusionNonceCount);
        Assert.AreEqual(7, before.KnownInboundNonceCount);
        Assert.IsTrue(getPendingCount() >= 2);

        // RetryCount was reset by prior recover — re-escalate until Poisoned.
        LocalHostOperatorStatus afterPoison = before;
        for (var attempt = 0; attempt < 16; attempt++)
        {
            try
            {
                reconcileAsync().GetAwaiter().GetResult();
            }
            catch (OverflowException)
            {
            }
            catch (Exception)
            {
            }

            submitNextAsync().GetAwaiter().GetResult();
            afterPoison = getOperatorStatus();
            if (afterPoison.IsSettlementPoisoned)
                break;
        }

        Assert.IsTrue(afterPoison.IsSettlementPoisoned);
        Assert.IsFalse(afterPoison.IsSettlementRetrying);
        CollectionAssert.Contains(
            afterPoison.PipelineHealthFailures.ToArray(),
            nameof(afterPoison.IsSettlementPoisoned));
        Assert.IsNotNull(afterPoison.Recovery.BlockedBatchNumber);
        Assert.IsNotNull(afterPoison.Recovery.ArtifactContentHash);
        var blockedBatch = afterPoison.Recovery.BlockedBatchNumber!.Value;
        var contentHash = afterPoison.Recovery.ArtifactContentHash!;
        Assert.IsTrue(afterPoison.PendingSettlementCount >= 2);
        Assert.AreEqual(2UL, afterPoison.LatestCheckpointBatchNumber);
        Assert.AreEqual(7, afterPoison.ConsumedDepositCount);
        Assert.AreEqual(7, afterPoison.MessageOutboxL2ToL1Count);
        Assert.AreEqual(7, afterPoison.KnownForcedInclusionNonceCount);
        Assert.AreEqual(7, afterPoison.KnownInboundNonceCount);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => recoverPoisonedBatchAsync(blockedBatch, UInt256.Zero).GetAwaiter().GetResult());
        Assert.IsTrue(isSettlementPoisonedAsync().GetAwaiter().GetResult());
        recoverPoisonedBatchAsync(blockedBatch, contentHash).GetAwaiter().GetResult();

        var afterRecover = getOperatorStatus();
        Assert.IsFalse(afterRecover.IsSettlementPoisoned);
        Assert.IsTrue(afterRecover.IsSettlementRetrying);
        Assert.AreEqual(SettlementRecoveryState.Retrying, afterRecover.Recovery.State);
        Assert.AreEqual(0, afterRecover.Recovery.RetryCount);
        Assert.IsTrue(getPendingCount() >= 2);
        Assert.IsTrue(afterRecover.PendingSettlementCount >= 2);
        Assert.AreEqual(2UL, afterRecover.LatestCheckpointBatchNumber);
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, afterRecover.LatestCheckpointPostStateRoot);
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, afterRecover.LatestRpcStateRoot);
        Assert.AreEqual(7, afterRecover.ConsumedDepositCount);
        Assert.AreEqual(7, afterRecover.MessageOutboxL2ToL1Count);
        Assert.AreEqual(7, afterRecover.KnownForcedInclusionNonceCount);
        Assert.AreEqual(7, afterRecover.KnownInboundNonceCount);
        Assert.IsTrue(afterRecover.IsOfflinePassportComplete);
        Assert.IsTrue(afterRecover.IsOperatorReady);
        Assert.IsTrue(afterRecover.IsBatcherCheckpointAligned);
        Assert.IsFalse(afterRecover.IsPipelineHealthy);
        CollectionAssert.Contains(
            afterRecover.PipelineHealthFailures.ToArray(),
            nameof(afterRecover.IsSettlementRetrying));
        Assert.IsTrue(hasConsumedDeposit(0, 1));
        Assert.IsTrue(hasConsumedDeposit(0, 2));
        Assert.IsTrue(hasConsumedDeposit(0, 3));
        Assert.IsTrue(hasConsumedDeposit(0, 4));
        Assert.IsTrue(hasConsumedDeposit(0, 5));
        Assert.IsTrue(hasConsumedDeposit(0, 6));
        Assert.IsTrue(hasConsumedDeposit(0, 7));
        Assert.IsTrue(getRpcL1DepositStatus(0, 1) is { ConsumedOnL2: true, IncludedInBatch: 1UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 2) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 3) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 4) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 5) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 6) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 7) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.AreEqual(BatchStatus.Finalized, getRpcBatchStatus(1));
        Assert.AreEqual(BatchStatus.Finalized, getRpcBatchStatus(2));
        Assert.IsNotNull(getRpcBatch(1));
        Assert.IsNotNull(getRpcBatch(2));
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, getRpcStateRootAtBatch(1));
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, getRpcStateRootAtBatch(2));
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, getLatestRpcStateRoot());
        Assert.IsTrue(getRpcWithdrawalProof(seventhWithdrawalLeaf) is { Length: > 0 });
        Assert.IsTrue(getRpcMessageProof(seventhOutboundMessageHash) is { Length: > 0 });

        var probe = getHealthProbe();
        Assert.IsTrue(probe.IsSettlementRetrying);
        Assert.IsFalse(probe.IsSettlementPoisoned);
        Assert.AreEqual(2UL, probe.LatestCheckpointBatchNumber);
        Assert.IsTrue(probe.PendingSettlementCount >= 2);
        Assert.AreEqual(7, probe.ConsumedDepositCount);
        Assert.AreEqual(7, probe.MessageOutboxL2ToL1Count);
        Assert.AreEqual(7, probe.KnownForcedInclusionNonceCount);
        Assert.AreEqual(7, probe.KnownInboundNonceCount);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusJson, "\"isSettlementPoisoned\": false");
        StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": 2");
        StringAssert.Contains(statusJson, "\"consumedDepositCount\": 7");
        StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Count\": 7");
        StringAssert.Contains(statusJson, "\"knownForcedInclusionNonceCount\": 7");
        StringAssert.Contains(statusJson, "\"knownInboundNonceCount\": 7");

        var probeJson = formatHealthProbeJson();
        StringAssert.Contains(probeJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(probeJson, "\"consumedDepositCount\": 7");
        StringAssert.Contains(probeJson, "\"messageOutboxL2ToL1Count\": 7");

        var statusPath = Path.Combine(chainDir, "soft-seal-seventh-poison-recover-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        var statusFile = File.ReadAllText(statusPath);
        StringAssert.Contains(statusFile, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusFile, "\"consumedDepositCount\": 7");
        StringAssert.Contains(statusFile, "\"messageOutboxL2ToL1Count\": 7");
        StringAssert.Contains(statusFile, "\"latestCheckpointBatchNumber\": 2");

        var probePath = Path.Combine(chainDir, "soft-seal-seventh-poison-recover-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        StringAssert.Contains(File.ReadAllText(probePath), "\"consumedDepositCount\": 7");
        StringAssert.Contains(File.ReadAllText(probePath), "\"isSettlementRetrying\": true");

        var durablePath = Path.Combine(chainDir, "soft-seal-seventh-poison-recover.json");
        File.WriteAllText(durablePath, $$"""
            {
              "seventhPoisonBlockedBatch": {{blockedBatch}},
              "pendingSettlementCount": {{afterRecover.PendingSettlementCount}},
              "latestCheckpointBatchNumber": {{afterRecover.LatestCheckpointBatchNumber}},
              "consumedDepositCount": {{afterRecover.ConsumedDepositCount}},
              "messageOutboxL2ToL1Count": {{afterRecover.MessageOutboxL2ToL1Count}},
              "knownForcedInclusionNonceCount": {{afterRecover.KnownForcedInclusionNonceCount}},
              "knownInboundNonceCount": {{afterRecover.KnownInboundNonceCount}},
              "rpcBatch1Status": "{{getRpcBatchStatus(1)}}",
              "rpcBatch2Status": "{{getRpcBatchStatus(2)}}",
              "seventhWithdrawalProofPresent": true,
              "seventhMessageProofPresent": true,
              "isSettlementRetrying": true,
              "isSettlementPoisoned": false,
              "isOfflinePassportComplete": true
            }
            """);
        Assert.IsTrue(File.Exists(durablePath));
        var durableFile = File.ReadAllText(durablePath);
        StringAssert.Contains(durableFile, "\"rpcBatch1Status\": \"Finalized\"");
        StringAssert.Contains(durableFile, "\"rpcBatch2Status\": \"Finalized\"");
        StringAssert.Contains(durableFile, "\"consumedDepositCount\": 7");
        StringAssert.Contains(durableFile, "\"messageOutboxL2ToL1Count\": 7");
        StringAssert.Contains(durableFile, "\"knownForcedInclusionNonceCount\": 7");
        StringAssert.Contains(durableFile, "\"isSettlementRetrying\": true");
        StringAssert.Contains(durableFile, "\"isSettlementPoisoned\": false");
    }

    /// <summary>
    /// <summary>
    /// SoftSeal full soft path after seventh recover + octuple deposit/outbox/FI: eighth
    /// Reconcile→Poison→Recover cycle must retain multi-batch RPC tip, octuple deposits,
    /// octuple outbox, octuple FI/inbound known counts, and eighth-outbound proofs.
    /// Does not claim L1 settle.
    /// </summary>
    private static void AssertSoftSealEighthPoisonRecoverRetention(
        Func<Task> reconcileAsync,
        Func<Task> submitNextAsync,
        Func<ulong, UInt256, Task> recoverPoisonedBatchAsync,
        Func<Task<bool>> isSettlementPoisonedAsync,
        Func<int> getPendingCount,
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> formatHealthProbeJson,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        Func<ulong, L2BatchCommitment?> getRpcBatch,
        Func<ulong, BatchStatus> getRpcBatchStatus,
        Func<ulong, UInt256> getRpcStateRootAtBatch,
        Func<UInt256> getLatestRpcStateRoot,
        Func<uint, ulong, bool> hasConsumedDeposit,
        Func<uint, ulong, DepositStatus?> getRpcL1DepositStatus,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcWithdrawalProof,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcMessageProof,
        UInt256 eighthWithdrawalLeaf,
        UInt256 eighthOutboundMessageHash,
        string chainDir)
    {
        var before = getOperatorStatus();
        Assert.IsTrue(before.IsSettlementRetrying);
        Assert.IsFalse(before.IsSettlementPoisoned);
        Assert.IsTrue(before.PendingSettlementCount >= 2);
        Assert.AreEqual(2UL, before.LatestCheckpointBatchNumber);
        Assert.AreEqual(8, before.ConsumedDepositCount);
        Assert.AreEqual(8, before.MessageOutboxL2ToL1Count);
        Assert.AreEqual(8, before.KnownForcedInclusionNonceCount);
        Assert.AreEqual(8, before.KnownInboundNonceCount);
        Assert.IsTrue(getPendingCount() >= 2);

        // RetryCount was reset by prior recover — re-escalate until Poisoned.
        LocalHostOperatorStatus afterPoison = before;
        for (var attempt = 0; attempt < 16; attempt++)
        {
            try
            {
                reconcileAsync().GetAwaiter().GetResult();
            }
            catch (OverflowException)
            {
            }
            catch (Exception)
            {
            }

            submitNextAsync().GetAwaiter().GetResult();
            afterPoison = getOperatorStatus();
            if (afterPoison.IsSettlementPoisoned)
                break;
        }

        Assert.IsTrue(afterPoison.IsSettlementPoisoned);
        Assert.IsFalse(afterPoison.IsSettlementRetrying);
        CollectionAssert.Contains(
            afterPoison.PipelineHealthFailures.ToArray(),
            nameof(afterPoison.IsSettlementPoisoned));
        Assert.IsNotNull(afterPoison.Recovery.BlockedBatchNumber);
        Assert.IsNotNull(afterPoison.Recovery.ArtifactContentHash);
        var blockedBatch = afterPoison.Recovery.BlockedBatchNumber!.Value;
        var contentHash = afterPoison.Recovery.ArtifactContentHash!;
        Assert.IsTrue(afterPoison.PendingSettlementCount >= 2);
        Assert.AreEqual(2UL, afterPoison.LatestCheckpointBatchNumber);
        Assert.AreEqual(8, afterPoison.ConsumedDepositCount);
        Assert.AreEqual(8, afterPoison.MessageOutboxL2ToL1Count);
        Assert.AreEqual(8, afterPoison.KnownForcedInclusionNonceCount);
        Assert.AreEqual(8, afterPoison.KnownInboundNonceCount);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => recoverPoisonedBatchAsync(blockedBatch, UInt256.Zero).GetAwaiter().GetResult());
        Assert.IsTrue(isSettlementPoisonedAsync().GetAwaiter().GetResult());
        recoverPoisonedBatchAsync(blockedBatch, contentHash).GetAwaiter().GetResult();

        var afterRecover = getOperatorStatus();
        Assert.IsFalse(afterRecover.IsSettlementPoisoned);
        Assert.IsTrue(afterRecover.IsSettlementRetrying);
        Assert.AreEqual(SettlementRecoveryState.Retrying, afterRecover.Recovery.State);
        Assert.AreEqual(0, afterRecover.Recovery.RetryCount);
        Assert.IsTrue(getPendingCount() >= 2);
        Assert.IsTrue(afterRecover.PendingSettlementCount >= 2);
        Assert.AreEqual(2UL, afterRecover.LatestCheckpointBatchNumber);
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, afterRecover.LatestCheckpointPostStateRoot);
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, afterRecover.LatestRpcStateRoot);
        Assert.AreEqual(8, afterRecover.ConsumedDepositCount);
        Assert.AreEqual(8, afterRecover.MessageOutboxL2ToL1Count);
        Assert.AreEqual(8, afterRecover.KnownForcedInclusionNonceCount);
        Assert.AreEqual(8, afterRecover.KnownInboundNonceCount);
        Assert.IsTrue(afterRecover.IsOfflinePassportComplete);
        Assert.IsTrue(afterRecover.IsOperatorReady);
        Assert.IsTrue(afterRecover.IsBatcherCheckpointAligned);
        Assert.IsFalse(afterRecover.IsPipelineHealthy);
        CollectionAssert.Contains(
            afterRecover.PipelineHealthFailures.ToArray(),
            nameof(afterRecover.IsSettlementRetrying));
        Assert.IsTrue(hasConsumedDeposit(0, 1));
        Assert.IsTrue(hasConsumedDeposit(0, 2));
        Assert.IsTrue(hasConsumedDeposit(0, 3));
        Assert.IsTrue(hasConsumedDeposit(0, 4));
        Assert.IsTrue(hasConsumedDeposit(0, 5));
        Assert.IsTrue(hasConsumedDeposit(0, 6));
        Assert.IsTrue(hasConsumedDeposit(0, 7));
        Assert.IsTrue(hasConsumedDeposit(0, 8));
        Assert.IsTrue(getRpcL1DepositStatus(0, 1) is { ConsumedOnL2: true, IncludedInBatch: 1UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 2) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 3) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 4) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 5) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 6) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 7) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 8) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.AreEqual(BatchStatus.Finalized, getRpcBatchStatus(1));
        Assert.AreEqual(BatchStatus.Finalized, getRpcBatchStatus(2));
        Assert.IsNotNull(getRpcBatch(1));
        Assert.IsNotNull(getRpcBatch(2));
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, getRpcStateRootAtBatch(1));
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, getRpcStateRootAtBatch(2));
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, getLatestRpcStateRoot());
        Assert.IsTrue(getRpcWithdrawalProof(eighthWithdrawalLeaf) is { Length: > 0 });
        Assert.IsTrue(getRpcMessageProof(eighthOutboundMessageHash) is { Length: > 0 });

        var probe = getHealthProbe();
        Assert.IsTrue(probe.IsSettlementRetrying);
        Assert.IsFalse(probe.IsSettlementPoisoned);
        Assert.AreEqual(2UL, probe.LatestCheckpointBatchNumber);
        Assert.IsTrue(probe.PendingSettlementCount >= 2);
        Assert.AreEqual(8, probe.ConsumedDepositCount);
        Assert.AreEqual(8, probe.MessageOutboxL2ToL1Count);
        Assert.AreEqual(8, probe.KnownForcedInclusionNonceCount);
        Assert.AreEqual(8, probe.KnownInboundNonceCount);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusJson, "\"isSettlementPoisoned\": false");
        StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": 2");
        StringAssert.Contains(statusJson, "\"consumedDepositCount\": 8");
        StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Count\": 8");
        StringAssert.Contains(statusJson, "\"knownForcedInclusionNonceCount\": 8");
        StringAssert.Contains(statusJson, "\"knownInboundNonceCount\": 8");

        var probeJson = formatHealthProbeJson();
        StringAssert.Contains(probeJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(probeJson, "\"consumedDepositCount\": 8");
        StringAssert.Contains(probeJson, "\"messageOutboxL2ToL1Count\": 8");

        var statusPath = Path.Combine(chainDir, "soft-seal-eighth-poison-recover-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        var statusFile = File.ReadAllText(statusPath);
        StringAssert.Contains(statusFile, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusFile, "\"consumedDepositCount\": 8");
        StringAssert.Contains(statusFile, "\"messageOutboxL2ToL1Count\": 8");
        StringAssert.Contains(statusFile, "\"latestCheckpointBatchNumber\": 2");

        var probePath = Path.Combine(chainDir, "soft-seal-eighth-poison-recover-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        StringAssert.Contains(File.ReadAllText(probePath), "\"consumedDepositCount\": 8");
        StringAssert.Contains(File.ReadAllText(probePath), "\"isSettlementRetrying\": true");

        var durablePath = Path.Combine(chainDir, "soft-seal-eighth-poison-recover.json");
        File.WriteAllText(durablePath, $$"""
            {
              "eighthPoisonBlockedBatch": {{blockedBatch}},
              "pendingSettlementCount": {{afterRecover.PendingSettlementCount}},
              "latestCheckpointBatchNumber": {{afterRecover.LatestCheckpointBatchNumber}},
              "consumedDepositCount": {{afterRecover.ConsumedDepositCount}},
              "messageOutboxL2ToL1Count": {{afterRecover.MessageOutboxL2ToL1Count}},
              "knownForcedInclusionNonceCount": {{afterRecover.KnownForcedInclusionNonceCount}},
              "knownInboundNonceCount": {{afterRecover.KnownInboundNonceCount}},
              "rpcBatch1Status": "{{getRpcBatchStatus(1)}}",
              "rpcBatch2Status": "{{getRpcBatchStatus(2)}}",
              "eighthWithdrawalProofPresent": true,
              "eighthMessageProofPresent": true,
              "isSettlementRetrying": true,
              "isSettlementPoisoned": false,
              "isOfflinePassportComplete": true
            }
            """);
        Assert.IsTrue(File.Exists(durablePath));
        var durableFile = File.ReadAllText(durablePath);
        StringAssert.Contains(durableFile, "\"rpcBatch1Status\": \"Finalized\"");
        StringAssert.Contains(durableFile, "\"rpcBatch2Status\": \"Finalized\"");
        StringAssert.Contains(durableFile, "\"consumedDepositCount\": 8");
        StringAssert.Contains(durableFile, "\"messageOutboxL2ToL1Count\": 8");
        StringAssert.Contains(durableFile, "\"knownForcedInclusionNonceCount\": 8");
        StringAssert.Contains(durableFile, "\"isSettlementRetrying\": true");
        StringAssert.Contains(durableFile, "\"isSettlementPoisoned\": false");
    }

    /// <summary>
    /// SoftSeal after seventh poison→recover: re-publish local DA for sealed batches 1+2
    /// and process an eighth offline deposit (nonce 8, IncludedInBatch=2) while settle
    /// remains Retrying with multi-batch pending and septuple outbox/FI known retained.
    /// Does not claim L1 deposit scan / production DA / settle.
    /// </summary>
    private static void AssertSoftSealAfterSeventhRecoverDaAndEighthDeposit(
        Func<DAPublishRequest, Task<DAReceipt>> publishDaAsync,
        Func<DAReceipt, Task<bool>> isDaAvailableAsync,
        Func<bool> supportsLocalDaReader,
        Func<IDAReader> createLocalDaReader,
        Func<CrossChainMessage, MintInstruction> processDeposit,
        Func<uint, ulong, bool> hasConsumedDeposit,
        Func<int> consumedDepositCount,
        Action<DepositStatus> recordRpcDeposit,
        Func<uint, ulong, DepositStatus?> getRpcL1DepositStatus,
        Func<Task<int>> scanSharedBridgeDepositsAsync,
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> exportPrometheusMetrics,
        Func<string, Task> writePrometheusMetricsAsync,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        string chainDir)
    {
        Assert.IsTrue(supportsLocalDaReader());
        var da1Payload = new byte[] { 0xDA, 0xA1, 0x01 };
        var da1 = publishDaAsync(new DAPublishRequest
        {
            ChainId = 20260716u,
            BatchNumber = 1,
            Payload = da1Payload,
        }).GetAwaiter().GetResult();
        Assert.AreEqual(DAMode.Local, da1.Layer);
        Assert.AreEqual(DAReceiptKind.LocalPersistence, da1.Kind);
        Assert.IsTrue(isDaAvailableAsync(da1).GetAwaiter().GetResult());
        var da1Read = createLocalDaReader().ReadAsync(da1).AsTask().GetAwaiter().GetResult();
        Assert.IsTrue(da1Read is { Length: 3 });
        CollectionAssert.AreEqual(da1Payload, da1Read!.Value.ToArray());

        var da2Payload = new byte[] { 0xDA, 0xA2, 0x02 };
        var da2 = publishDaAsync(new DAPublishRequest
        {
            ChainId = 20260716u,
            BatchNumber = 2,
            Payload = da2Payload,
        }).GetAwaiter().GetResult();
        Assert.AreEqual(DAMode.Local, da2.Layer);
        Assert.IsTrue(isDaAvailableAsync(da2).GetAwaiter().GetResult());
        var da2Read = createLocalDaReader().ReadAsync(da2).AsTask().GetAwaiter().GetResult();
        Assert.IsTrue(da2Read is { Length: 3 });
        CollectionAssert.AreEqual(da2Payload, da2Read!.Value.ToArray());

        // Eighth offline mint while still Retrying after seventh recover.
        var softL1Asset = UInt160.Parse("0x" + new string('1', 40));
        var softL2Asset = UInt160.Parse("0x" + new string('2', 40));
        var softDepositPayload = new DepositPayload
        {
            L1Asset = softL1Asset,
            L2Recipient = Account(0x55),
            Amount = new BigInteger(8_000),
        };
        var softDepositMsg = new CrossChainMessage
        {
            SourceChainId = 0,
            TargetChainId = 20260716u,
            Nonce = 8,
            Sender = Account(0x66),
            Receiver = Account(0x55),
            MessageType = MessageType.Deposit,
            Payload = softDepositPayload.Encode(),
            MessageHash = UInt256.Zero,
        };
        var softMint = processDeposit(softDepositMsg);
        Assert.AreEqual(softL2Asset, softMint.L2Asset);
        Assert.IsTrue(hasConsumedDeposit(0, 1));
        Assert.IsTrue(hasConsumedDeposit(0, 2));
        Assert.IsTrue(hasConsumedDeposit(0, 3));
        Assert.IsTrue(hasConsumedDeposit(0, 4));
        Assert.IsTrue(hasConsumedDeposit(0, 5));
        Assert.IsTrue(hasConsumedDeposit(0, 6));
        Assert.IsTrue(hasConsumedDeposit(0, 7));
        Assert.IsTrue(hasConsumedDeposit(0, 8));
        Assert.AreEqual(8, consumedDepositCount());
        recordRpcDeposit(new DepositStatus(0, 8, ConsumedOnL2: true, IncludedInBatch: 2));
        Assert.IsTrue(getRpcL1DepositStatus(0, 1) is { ConsumedOnL2: true, IncludedInBatch: 1UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 2) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 3) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 4) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 5) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 6) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 7) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 8) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.AreEqual(0, scanSharedBridgeDepositsAsync().GetAwaiter().GetResult());

        var status = getOperatorStatus();
        Assert.AreEqual(8, status.ConsumedDepositCount);
        Assert.AreEqual(7, status.MessageOutboxL2ToL1Count);
        Assert.AreEqual(7, status.KnownForcedInclusionNonceCount);
        Assert.AreEqual(7, status.KnownInboundNonceCount);
        Assert.AreEqual(2UL, status.LatestCheckpointBatchNumber);
        Assert.IsTrue(status.PendingSettlementCount >= 2);
        Assert.IsTrue(status.IsSettlementRetrying);
        Assert.IsFalse(status.IsSettlementPoisoned);
        Assert.IsFalse(status.IsSettlementIdle);
        Assert.IsTrue(status.IsOfflinePassportComplete);
        Assert.IsTrue(status.IsOperatorReady);
        Assert.IsTrue(status.IsBatcherCheckpointAligned);
        Assert.IsFalse(status.IsPipelineHealthy);
        CollectionAssert.Contains(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.IsSettlementRetrying));

        var probe = getHealthProbe();
        Assert.AreEqual(8, probe.ConsumedDepositCount);
        Assert.AreEqual(7, probe.MessageOutboxL2ToL1Count);
        Assert.AreEqual(2UL, probe.LatestCheckpointBatchNumber);
        Assert.IsTrue(probe.PendingSettlementCount >= 2);
        Assert.IsTrue(probe.IsSettlementRetrying);
        Assert.IsFalse(probe.IsSettlementPoisoned);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"consumedDepositCount\": 8");
        StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Count\": 7");
        StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": 2");
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusJson, "\"isSettlementPoisoned\": false");

        var hostProm = exportPrometheusMetrics();
        Assert.IsFalse(string.IsNullOrWhiteSpace(hostProm));
        var hostPromPath = Path.Combine(chainDir, "soft-seal-after-seventh-recover-host.prom");
        writePrometheusMetricsAsync(hostPromPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(hostPromPath));
        Assert.AreEqual(hostProm, File.ReadAllText(hostPromPath));

        var statusPath = Path.Combine(chainDir, "soft-seal-after-seventh-recover-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        StringAssert.Contains(File.ReadAllText(statusPath), "\"consumedDepositCount\": 8");
        StringAssert.Contains(File.ReadAllText(statusPath), "\"isSettlementRetrying\": true");

        var probePath = Path.Combine(chainDir, "soft-seal-after-seventh-recover-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        StringAssert.Contains(File.ReadAllText(probePath), "\"consumedDepositCount\": 8");

        var durablePath = Path.Combine(chainDir, "soft-seal-after-seventh-recover-da-deposit.json");
        File.WriteAllText(durablePath, $$"""
            {
              "daBatch1Layer": "{{da1.Layer}}",
              "daBatch2Layer": "{{da2.Layer}}",
              "daBatch1Available": true,
              "daBatch2Available": true,
              "consumedDepositCount": {{status.ConsumedDepositCount}},
              "deposit8IncludedInBatch": 2,
              "messageOutboxL2ToL1Count": {{status.MessageOutboxL2ToL1Count}},
              "knownForcedInclusionNonceCount": {{status.KnownForcedInclusionNonceCount}},
              "knownInboundNonceCount": {{status.KnownInboundNonceCount}},
              "latestCheckpointBatchNumber": {{status.LatestCheckpointBatchNumber}},
              "pendingSettlementCount": {{status.PendingSettlementCount}},
              "isSettlementRetrying": true,
              "isSettlementPoisoned": false,
              "isOfflinePassportComplete": true
            }
            """);
        Assert.IsTrue(File.Exists(durablePath));
        var durableFile = File.ReadAllText(durablePath);
        StringAssert.Contains(durableFile, "\"daBatch1Layer\": \"Local\"");
        StringAssert.Contains(durableFile, "\"daBatch2Layer\": \"Local\"");
        StringAssert.Contains(durableFile, "\"consumedDepositCount\": 8");
        StringAssert.Contains(durableFile, "\"deposit8IncludedInBatch\": 2");
        StringAssert.Contains(durableFile, "\"messageOutboxL2ToL1Count\": 7");
        StringAssert.Contains(durableFile, "\"isSettlementRetrying\": true");
        StringAssert.Contains(durableFile, "\"isOfflinePassportComplete\": true");
    }

    /// <summary>
    /// <summary>
    /// <summary>
    /// SoftSeal full soft path after eighth recover + nonuple deposit/outbox/FI: ninth
    /// Reconcile→Poison→Recover cycle must retain multi-batch RPC tip, nonuple deposits,
    /// nonuple outbox, nonuple FI/inbound known counts, and ninth-outbound proofs.
    /// Does not claim L1 settle.
    /// </summary>
    private static void AssertSoftSealNinthPoisonRecoverRetention(
        Func<Task> reconcileAsync,
        Func<Task> submitNextAsync,
        Func<ulong, UInt256, Task> recoverPoisonedBatchAsync,
        Func<Task<bool>> isSettlementPoisonedAsync,
        Func<int> getPendingCount,
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> formatHealthProbeJson,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        Func<ulong, L2BatchCommitment?> getRpcBatch,
        Func<ulong, BatchStatus> getRpcBatchStatus,
        Func<ulong, UInt256> getRpcStateRootAtBatch,
        Func<UInt256> getLatestRpcStateRoot,
        Func<uint, ulong, bool> hasConsumedDeposit,
        Func<uint, ulong, DepositStatus?> getRpcL1DepositStatus,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcWithdrawalProof,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcMessageProof,
        UInt256 ninthWithdrawalLeaf,
        UInt256 ninthOutboundMessageHash,
        string chainDir)
    {
        var before = getOperatorStatus();
        Assert.IsTrue(before.IsSettlementRetrying);
        Assert.IsFalse(before.IsSettlementPoisoned);
        Assert.IsTrue(before.PendingSettlementCount >= 2);
        Assert.AreEqual(2UL, before.LatestCheckpointBatchNumber);
        Assert.AreEqual(9, before.ConsumedDepositCount);
        Assert.AreEqual(9, before.MessageOutboxL2ToL1Count);
        Assert.AreEqual(9, before.KnownForcedInclusionNonceCount);
        Assert.AreEqual(9, before.KnownInboundNonceCount);
        Assert.IsTrue(getPendingCount() >= 2);

        // RetryCount was reset by prior recover — re-escalate until Poisoned.
        LocalHostOperatorStatus afterPoison = before;
        for (var attempt = 0; attempt < 16; attempt++)
        {
            try
            {
                reconcileAsync().GetAwaiter().GetResult();
            }
            catch (OverflowException)
            {
            }
            catch (Exception)
            {
            }

            submitNextAsync().GetAwaiter().GetResult();
            afterPoison = getOperatorStatus();
            if (afterPoison.IsSettlementPoisoned)
                break;
        }

        Assert.IsTrue(afterPoison.IsSettlementPoisoned);
        Assert.IsFalse(afterPoison.IsSettlementRetrying);
        CollectionAssert.Contains(
            afterPoison.PipelineHealthFailures.ToArray(),
            nameof(afterPoison.IsSettlementPoisoned));
        Assert.IsNotNull(afterPoison.Recovery.BlockedBatchNumber);
        Assert.IsNotNull(afterPoison.Recovery.ArtifactContentHash);
        var blockedBatch = afterPoison.Recovery.BlockedBatchNumber!.Value;
        var contentHash = afterPoison.Recovery.ArtifactContentHash!;
        Assert.IsTrue(afterPoison.PendingSettlementCount >= 2);
        Assert.AreEqual(2UL, afterPoison.LatestCheckpointBatchNumber);
        Assert.AreEqual(9, afterPoison.ConsumedDepositCount);
        Assert.AreEqual(9, afterPoison.MessageOutboxL2ToL1Count);
        Assert.AreEqual(9, afterPoison.KnownForcedInclusionNonceCount);
        Assert.AreEqual(9, afterPoison.KnownInboundNonceCount);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => recoverPoisonedBatchAsync(blockedBatch, UInt256.Zero).GetAwaiter().GetResult());
        Assert.IsTrue(isSettlementPoisonedAsync().GetAwaiter().GetResult());
        recoverPoisonedBatchAsync(blockedBatch, contentHash).GetAwaiter().GetResult();

        var afterRecover = getOperatorStatus();
        Assert.IsFalse(afterRecover.IsSettlementPoisoned);
        Assert.IsTrue(afterRecover.IsSettlementRetrying);
        Assert.AreEqual(SettlementRecoveryState.Retrying, afterRecover.Recovery.State);
        Assert.AreEqual(0, afterRecover.Recovery.RetryCount);
        Assert.IsTrue(getPendingCount() >= 2);
        Assert.IsTrue(afterRecover.PendingSettlementCount >= 2);
        Assert.AreEqual(2UL, afterRecover.LatestCheckpointBatchNumber);
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, afterRecover.LatestCheckpointPostStateRoot);
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, afterRecover.LatestRpcStateRoot);
        Assert.AreEqual(9, afterRecover.ConsumedDepositCount);
        Assert.AreEqual(9, afterRecover.MessageOutboxL2ToL1Count);
        Assert.AreEqual(9, afterRecover.KnownForcedInclusionNonceCount);
        Assert.AreEqual(9, afterRecover.KnownInboundNonceCount);
        Assert.IsTrue(afterRecover.IsOfflinePassportComplete);
        Assert.IsTrue(afterRecover.IsOperatorReady);
        Assert.IsTrue(afterRecover.IsBatcherCheckpointAligned);
        Assert.IsFalse(afterRecover.IsPipelineHealthy);
        CollectionAssert.Contains(
            afterRecover.PipelineHealthFailures.ToArray(),
            nameof(afterRecover.IsSettlementRetrying));
        Assert.IsTrue(hasConsumedDeposit(0, 1));
        Assert.IsTrue(hasConsumedDeposit(0, 2));
        Assert.IsTrue(hasConsumedDeposit(0, 3));
        Assert.IsTrue(hasConsumedDeposit(0, 4));
        Assert.IsTrue(hasConsumedDeposit(0, 5));
        Assert.IsTrue(hasConsumedDeposit(0, 6));
        Assert.IsTrue(hasConsumedDeposit(0, 7));
        Assert.IsTrue(hasConsumedDeposit(0, 8));
        Assert.IsTrue(hasConsumedDeposit(0, 9));
        Assert.IsTrue(getRpcL1DepositStatus(0, 1) is { ConsumedOnL2: true, IncludedInBatch: 1UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 2) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 3) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 4) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 5) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 6) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 7) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 8) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 9) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.AreEqual(BatchStatus.Finalized, getRpcBatchStatus(1));
        Assert.AreEqual(BatchStatus.Finalized, getRpcBatchStatus(2));
        Assert.IsNotNull(getRpcBatch(1));
        Assert.IsNotNull(getRpcBatch(2));
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, getRpcStateRootAtBatch(1));
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, getRpcStateRootAtBatch(2));
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, getLatestRpcStateRoot());
        Assert.IsTrue(getRpcWithdrawalProof(ninthWithdrawalLeaf) is { Length: > 0 });
        Assert.IsTrue(getRpcMessageProof(ninthOutboundMessageHash) is { Length: > 0 });

        var probe = getHealthProbe();
        Assert.IsTrue(probe.IsSettlementRetrying);
        Assert.IsFalse(probe.IsSettlementPoisoned);
        Assert.AreEqual(2UL, probe.LatestCheckpointBatchNumber);
        Assert.IsTrue(probe.PendingSettlementCount >= 2);
        Assert.AreEqual(9, probe.ConsumedDepositCount);
        Assert.AreEqual(9, probe.MessageOutboxL2ToL1Count);
        Assert.AreEqual(9, probe.KnownForcedInclusionNonceCount);
        Assert.AreEqual(9, probe.KnownInboundNonceCount);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusJson, "\"isSettlementPoisoned\": false");
        StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": 2");
        StringAssert.Contains(statusJson, "\"consumedDepositCount\": 9");
        StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Count\": 9");
        StringAssert.Contains(statusJson, "\"knownForcedInclusionNonceCount\": 9");
        StringAssert.Contains(statusJson, "\"knownInboundNonceCount\": 9");

        var probeJson = formatHealthProbeJson();
        StringAssert.Contains(probeJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(probeJson, "\"consumedDepositCount\": 9");
        StringAssert.Contains(probeJson, "\"messageOutboxL2ToL1Count\": 9");

        var statusPath = Path.Combine(chainDir, "soft-seal-ninth-poison-recover-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        var statusFile = File.ReadAllText(statusPath);
        StringAssert.Contains(statusFile, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusFile, "\"consumedDepositCount\": 9");
        StringAssert.Contains(statusFile, "\"messageOutboxL2ToL1Count\": 9");
        StringAssert.Contains(statusFile, "\"latestCheckpointBatchNumber\": 2");

        var probePath = Path.Combine(chainDir, "soft-seal-ninth-poison-recover-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        StringAssert.Contains(File.ReadAllText(probePath), "\"consumedDepositCount\": 9");
        StringAssert.Contains(File.ReadAllText(probePath), "\"isSettlementRetrying\": true");

        var durablePath = Path.Combine(chainDir, "soft-seal-ninth-poison-recover.json");
        File.WriteAllText(durablePath, $$"""
            {
              "ninthPoisonBlockedBatch": {{blockedBatch}},
              "pendingSettlementCount": {{afterRecover.PendingSettlementCount}},
              "latestCheckpointBatchNumber": {{afterRecover.LatestCheckpointBatchNumber}},
              "consumedDepositCount": {{afterRecover.ConsumedDepositCount}},
              "messageOutboxL2ToL1Count": {{afterRecover.MessageOutboxL2ToL1Count}},
              "knownForcedInclusionNonceCount": {{afterRecover.KnownForcedInclusionNonceCount}},
              "knownInboundNonceCount": {{afterRecover.KnownInboundNonceCount}},
              "rpcBatch1Status": "{{getRpcBatchStatus(1)}}",
              "rpcBatch2Status": "{{getRpcBatchStatus(2)}}",
              "ninthWithdrawalProofPresent": true,
              "ninthMessageProofPresent": true,
              "isSettlementRetrying": true,
              "isSettlementPoisoned": false,
              "isOfflinePassportComplete": true
            }
            """);
        Assert.IsTrue(File.Exists(durablePath));
        var durableFile = File.ReadAllText(durablePath);
        StringAssert.Contains(durableFile, "\"rpcBatch1Status\": \"Finalized\"");
        StringAssert.Contains(durableFile, "\"rpcBatch2Status\": \"Finalized\"");
        StringAssert.Contains(durableFile, "\"consumedDepositCount\": 9");
        StringAssert.Contains(durableFile, "\"messageOutboxL2ToL1Count\": 9");
        StringAssert.Contains(durableFile, "\"knownForcedInclusionNonceCount\": 9");
        StringAssert.Contains(durableFile, "\"isSettlementRetrying\": true");
        StringAssert.Contains(durableFile, "\"isSettlementPoisoned\": false");
    }

    /// <summary>
    /// SoftSeal after eighth poison→recover: re-publish local DA for sealed batches 1+2
    /// and process a ninth offline deposit (nonce 9, IncludedInBatch=2) while settle
    /// remains Retrying with multi-batch pending and octuple outbox/FI known retained.
    /// Does not claim L1 deposit scan / production DA / settle.
    /// </summary>
    private static void AssertSoftSealAfterEighthRecoverDaAndNinthDeposit(
        Func<DAPublishRequest, Task<DAReceipt>> publishDaAsync,
        Func<DAReceipt, Task<bool>> isDaAvailableAsync,
        Func<bool> supportsLocalDaReader,
        Func<IDAReader> createLocalDaReader,
        Func<CrossChainMessage, MintInstruction> processDeposit,
        Func<uint, ulong, bool> hasConsumedDeposit,
        Func<int> consumedDepositCount,
        Action<DepositStatus> recordRpcDeposit,
        Func<uint, ulong, DepositStatus?> getRpcL1DepositStatus,
        Func<Task<int>> scanSharedBridgeDepositsAsync,
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> exportPrometheusMetrics,
        Func<string, Task> writePrometheusMetricsAsync,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        string chainDir)
    {
        Assert.IsTrue(supportsLocalDaReader());
        var da1Payload = new byte[] { 0xDA, 0xA1, 0x01 };
        var da1 = publishDaAsync(new DAPublishRequest
        {
            ChainId = 20260716u,
            BatchNumber = 1,
            Payload = da1Payload,
        }).GetAwaiter().GetResult();
        Assert.AreEqual(DAMode.Local, da1.Layer);
        Assert.AreEqual(DAReceiptKind.LocalPersistence, da1.Kind);
        Assert.IsTrue(isDaAvailableAsync(da1).GetAwaiter().GetResult());
        var da1Read = createLocalDaReader().ReadAsync(da1).AsTask().GetAwaiter().GetResult();
        Assert.IsTrue(da1Read is { Length: 3 });
        CollectionAssert.AreEqual(da1Payload, da1Read!.Value.ToArray());

        var da2Payload = new byte[] { 0xDA, 0xA2, 0x02 };
        var da2 = publishDaAsync(new DAPublishRequest
        {
            ChainId = 20260716u,
            BatchNumber = 2,
            Payload = da2Payload,
        }).GetAwaiter().GetResult();
        Assert.AreEqual(DAMode.Local, da2.Layer);
        Assert.IsTrue(isDaAvailableAsync(da2).GetAwaiter().GetResult());
        var da2Read = createLocalDaReader().ReadAsync(da2).AsTask().GetAwaiter().GetResult();
        Assert.IsTrue(da2Read is { Length: 3 });
        CollectionAssert.AreEqual(da2Payload, da2Read!.Value.ToArray());

        // Ninth offline mint while still Retrying after eighth recover.
        var softL1Asset = UInt160.Parse("0x" + new string('1', 40));
        var softL2Asset = UInt160.Parse("0x" + new string('2', 40));
        var softDepositPayload = new DepositPayload
        {
            L1Asset = softL1Asset,
            L2Recipient = Account(0x55),
            Amount = new BigInteger(9_000),
        };
        var softDepositMsg = new CrossChainMessage
        {
            SourceChainId = 0,
            TargetChainId = 20260716u,
            Nonce = 9,
            Sender = Account(0x66),
            Receiver = Account(0x55),
            MessageType = MessageType.Deposit,
            Payload = softDepositPayload.Encode(),
            MessageHash = UInt256.Zero,
        };
        var softMint = processDeposit(softDepositMsg);
        Assert.AreEqual(softL2Asset, softMint.L2Asset);
        Assert.IsTrue(hasConsumedDeposit(0, 1));
        Assert.IsTrue(hasConsumedDeposit(0, 2));
        Assert.IsTrue(hasConsumedDeposit(0, 3));
        Assert.IsTrue(hasConsumedDeposit(0, 4));
        Assert.IsTrue(hasConsumedDeposit(0, 5));
        Assert.IsTrue(hasConsumedDeposit(0, 6));
        Assert.IsTrue(hasConsumedDeposit(0, 7));
        Assert.IsTrue(hasConsumedDeposit(0, 8));
        Assert.IsTrue(hasConsumedDeposit(0, 9));
        Assert.AreEqual(9, consumedDepositCount());
        recordRpcDeposit(new DepositStatus(0, 9, ConsumedOnL2: true, IncludedInBatch: 2));
        Assert.IsTrue(getRpcL1DepositStatus(0, 1) is { ConsumedOnL2: true, IncludedInBatch: 1UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 2) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 3) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 4) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 5) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 6) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 7) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 8) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 9) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.AreEqual(0, scanSharedBridgeDepositsAsync().GetAwaiter().GetResult());

        var status = getOperatorStatus();
        Assert.AreEqual(9, status.ConsumedDepositCount);
        Assert.AreEqual(8, status.MessageOutboxL2ToL1Count);
        Assert.AreEqual(8, status.KnownForcedInclusionNonceCount);
        Assert.AreEqual(8, status.KnownInboundNonceCount);
        Assert.AreEqual(2UL, status.LatestCheckpointBatchNumber);
        Assert.IsTrue(status.PendingSettlementCount >= 2);
        Assert.IsTrue(status.IsSettlementRetrying);
        Assert.IsFalse(status.IsSettlementPoisoned);
        Assert.IsFalse(status.IsSettlementIdle);
        Assert.IsTrue(status.IsOfflinePassportComplete);
        Assert.IsTrue(status.IsOperatorReady);
        Assert.IsTrue(status.IsBatcherCheckpointAligned);
        Assert.IsFalse(status.IsPipelineHealthy);
        CollectionAssert.Contains(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.IsSettlementRetrying));

        var probe = getHealthProbe();
        Assert.AreEqual(9, probe.ConsumedDepositCount);
        Assert.AreEqual(8, probe.MessageOutboxL2ToL1Count);
        Assert.AreEqual(2UL, probe.LatestCheckpointBatchNumber);
        Assert.IsTrue(probe.PendingSettlementCount >= 2);
        Assert.IsTrue(probe.IsSettlementRetrying);
        Assert.IsFalse(probe.IsSettlementPoisoned);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"consumedDepositCount\": 9");
        StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Count\": 8");
        StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": 2");
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusJson, "\"isSettlementPoisoned\": false");

        var hostProm = exportPrometheusMetrics();
        Assert.IsFalse(string.IsNullOrWhiteSpace(hostProm));
        var hostPromPath = Path.Combine(chainDir, "soft-seal-after-eighth-recover-host.prom");
        writePrometheusMetricsAsync(hostPromPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(hostPromPath));
        Assert.AreEqual(hostProm, File.ReadAllText(hostPromPath));

        var statusPath = Path.Combine(chainDir, "soft-seal-after-eighth-recover-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        StringAssert.Contains(File.ReadAllText(statusPath), "\"consumedDepositCount\": 9");
        StringAssert.Contains(File.ReadAllText(statusPath), "\"isSettlementRetrying\": true");

        var probePath = Path.Combine(chainDir, "soft-seal-after-eighth-recover-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        StringAssert.Contains(File.ReadAllText(probePath), "\"consumedDepositCount\": 9");

        var durablePath = Path.Combine(chainDir, "soft-seal-after-eighth-recover-da-deposit.json");
        File.WriteAllText(durablePath, $$"""
            {
              "daBatch1Layer": "{{da1.Layer}}",
              "daBatch2Layer": "{{da2.Layer}}",
              "daBatch1Available": true,
              "daBatch2Available": true,
              "consumedDepositCount": {{status.ConsumedDepositCount}},
              "deposit9IncludedInBatch": 2,
              "messageOutboxL2ToL1Count": {{status.MessageOutboxL2ToL1Count}},
              "knownForcedInclusionNonceCount": {{status.KnownForcedInclusionNonceCount}},
              "knownInboundNonceCount": {{status.KnownInboundNonceCount}},
              "latestCheckpointBatchNumber": {{status.LatestCheckpointBatchNumber}},
              "pendingSettlementCount": {{status.PendingSettlementCount}},
              "isSettlementRetrying": true,
              "isSettlementPoisoned": false,
              "isOfflinePassportComplete": true
            }
            """);
        Assert.IsTrue(File.Exists(durablePath));
        var durableFile = File.ReadAllText(durablePath);
        StringAssert.Contains(durableFile, "\"daBatch1Layer\": \"Local\"");
        StringAssert.Contains(durableFile, "\"daBatch2Layer\": \"Local\"");
        StringAssert.Contains(durableFile, "\"consumedDepositCount\": 9");
        StringAssert.Contains(durableFile, "\"deposit9IncludedInBatch\": 2");
        StringAssert.Contains(durableFile, "\"messageOutboxL2ToL1Count\": 8");
        StringAssert.Contains(durableFile, "\"isSettlementRetrying\": true");
        StringAssert.Contains(durableFile, "\"isOfflinePassportComplete\": true");
    }

    /// <summary>
    /// SoftSeal after ninth poison→recover: re-publish local DA for sealed batches 1+2
    /// and process a tenth offline deposit (nonce 10, IncludedInBatch=2) while settle
    /// remains Retrying with multi-batch pending and nonuple outbox/FI known retained.
    /// Does not claim L1 deposit scan / production DA / settle.
    /// </summary>
    private static void AssertSoftSealAfterNinthRecoverDaAndTenthDeposit(
        Func<DAPublishRequest, Task<DAReceipt>> publishDaAsync,
        Func<DAReceipt, Task<bool>> isDaAvailableAsync,
        Func<bool> supportsLocalDaReader,
        Func<IDAReader> createLocalDaReader,
        Func<CrossChainMessage, MintInstruction> processDeposit,
        Func<uint, ulong, bool> hasConsumedDeposit,
        Func<int> consumedDepositCount,
        Action<DepositStatus> recordRpcDeposit,
        Func<uint, ulong, DepositStatus?> getRpcL1DepositStatus,
        Func<Task<int>> scanSharedBridgeDepositsAsync,
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> exportPrometheusMetrics,
        Func<string, Task> writePrometheusMetricsAsync,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        string chainDir)
    {
        Assert.IsTrue(supportsLocalDaReader());
        var da1Payload = new byte[] { 0xDA, 0xA1, 0x01 };
        var da1 = publishDaAsync(new DAPublishRequest
        {
            ChainId = 20260716u,
            BatchNumber = 1,
            Payload = da1Payload,
        }).GetAwaiter().GetResult();
        Assert.AreEqual(DAMode.Local, da1.Layer);
        Assert.AreEqual(DAReceiptKind.LocalPersistence, da1.Kind);
        Assert.IsTrue(isDaAvailableAsync(da1).GetAwaiter().GetResult());
        var da1Read = createLocalDaReader().ReadAsync(da1).AsTask().GetAwaiter().GetResult();
        Assert.IsTrue(da1Read is { Length: 3 });
        CollectionAssert.AreEqual(da1Payload, da1Read!.Value.ToArray());

        var da2Payload = new byte[] { 0xDA, 0xA2, 0x02 };
        var da2 = publishDaAsync(new DAPublishRequest
        {
            ChainId = 20260716u,
            BatchNumber = 2,
            Payload = da2Payload,
        }).GetAwaiter().GetResult();
        Assert.AreEqual(DAMode.Local, da2.Layer);
        Assert.IsTrue(isDaAvailableAsync(da2).GetAwaiter().GetResult());
        var da2Read = createLocalDaReader().ReadAsync(da2).AsTask().GetAwaiter().GetResult();
        Assert.IsTrue(da2Read is { Length: 3 });
        CollectionAssert.AreEqual(da2Payload, da2Read!.Value.ToArray());

        // Tenth offline mint while still Retrying after ninth recover.
        var softL1Asset = UInt160.Parse("0x" + new string('1', 40));
        var softL2Asset = UInt160.Parse("0x" + new string('2', 40));
        var softDepositPayload = new DepositPayload
        {
            L1Asset = softL1Asset,
            L2Recipient = Account(0x55),
            Amount = new BigInteger(10_000),
        };
        var softDepositMsg = new CrossChainMessage
        {
            SourceChainId = 0,
            TargetChainId = 20260716u,
            Nonce = 10,
            Sender = Account(0x66),
            Receiver = Account(0x55),
            MessageType = MessageType.Deposit,
            Payload = softDepositPayload.Encode(),
            MessageHash = UInt256.Zero,
        };
        var softMint = processDeposit(softDepositMsg);
        Assert.AreEqual(softL2Asset, softMint.L2Asset);
        Assert.IsTrue(hasConsumedDeposit(0, 1));
        Assert.IsTrue(hasConsumedDeposit(0, 2));
        Assert.IsTrue(hasConsumedDeposit(0, 3));
        Assert.IsTrue(hasConsumedDeposit(0, 4));
        Assert.IsTrue(hasConsumedDeposit(0, 5));
        Assert.IsTrue(hasConsumedDeposit(0, 6));
        Assert.IsTrue(hasConsumedDeposit(0, 7));
        Assert.IsTrue(hasConsumedDeposit(0, 8));
        Assert.IsTrue(hasConsumedDeposit(0, 9));
        Assert.IsTrue(hasConsumedDeposit(0, 10));
        Assert.AreEqual(10, consumedDepositCount());
        recordRpcDeposit(new DepositStatus(0, 10, ConsumedOnL2: true, IncludedInBatch: 2));
        Assert.IsTrue(getRpcL1DepositStatus(0, 1) is { ConsumedOnL2: true, IncludedInBatch: 1UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 2) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 3) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 4) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 5) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 6) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 7) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 8) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 9) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 10) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.AreEqual(0, scanSharedBridgeDepositsAsync().GetAwaiter().GetResult());

        var status = getOperatorStatus();
        Assert.AreEqual(10, status.ConsumedDepositCount);
        Assert.AreEqual(9, status.MessageOutboxL2ToL1Count);
        Assert.AreEqual(9, status.KnownForcedInclusionNonceCount);
        Assert.AreEqual(9, status.KnownInboundNonceCount);
        Assert.AreEqual(2UL, status.LatestCheckpointBatchNumber);
        Assert.IsTrue(status.PendingSettlementCount >= 2);
        Assert.IsTrue(status.IsSettlementRetrying);
        Assert.IsFalse(status.IsSettlementPoisoned);
        Assert.IsFalse(status.IsSettlementIdle);
        Assert.IsTrue(status.IsOfflinePassportComplete);
        Assert.IsTrue(status.IsOperatorReady);
        Assert.IsTrue(status.IsBatcherCheckpointAligned);
        Assert.IsFalse(status.IsPipelineHealthy);
        CollectionAssert.Contains(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.IsSettlementRetrying));

        var probe = getHealthProbe();
        Assert.AreEqual(10, probe.ConsumedDepositCount);
        Assert.AreEqual(9, probe.MessageOutboxL2ToL1Count);
        Assert.AreEqual(2UL, probe.LatestCheckpointBatchNumber);
        Assert.IsTrue(probe.PendingSettlementCount >= 2);
        Assert.IsTrue(probe.IsSettlementRetrying);
        Assert.IsFalse(probe.IsSettlementPoisoned);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"consumedDepositCount\": 10");
        StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Count\": 9");
        StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": 2");
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusJson, "\"isSettlementPoisoned\": false");

        var hostProm = exportPrometheusMetrics();
        Assert.IsFalse(string.IsNullOrWhiteSpace(hostProm));
        var hostPromPath = Path.Combine(chainDir, "soft-seal-after-ninth-recover-host.prom");
        writePrometheusMetricsAsync(hostPromPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(hostPromPath));
        Assert.AreEqual(hostProm, File.ReadAllText(hostPromPath));

        var statusPath = Path.Combine(chainDir, "soft-seal-after-ninth-recover-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        StringAssert.Contains(File.ReadAllText(statusPath), "\"consumedDepositCount\": 10");
        StringAssert.Contains(File.ReadAllText(statusPath), "\"isSettlementRetrying\": true");

        var probePath = Path.Combine(chainDir, "soft-seal-after-ninth-recover-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        StringAssert.Contains(File.ReadAllText(probePath), "\"consumedDepositCount\": 10");

        var durablePath = Path.Combine(chainDir, "soft-seal-after-ninth-recover-da-deposit.json");
        File.WriteAllText(durablePath, $$"""
            {
              "daBatch1Layer": "{{da1.Layer}}",
              "daBatch2Layer": "{{da2.Layer}}",
              "daBatch1Available": true,
              "daBatch2Available": true,
              "consumedDepositCount": {{status.ConsumedDepositCount}},
              "deposit10IncludedInBatch": 2,
              "messageOutboxL2ToL1Count": {{status.MessageOutboxL2ToL1Count}},
              "knownForcedInclusionNonceCount": {{status.KnownForcedInclusionNonceCount}},
              "knownInboundNonceCount": {{status.KnownInboundNonceCount}},
              "latestCheckpointBatchNumber": {{status.LatestCheckpointBatchNumber}},
              "pendingSettlementCount": {{status.PendingSettlementCount}},
              "isSettlementRetrying": true,
              "isSettlementPoisoned": false,
              "isOfflinePassportComplete": true
            }
            """);
        Assert.IsTrue(File.Exists(durablePath));
        var durableFile = File.ReadAllText(durablePath);
        StringAssert.Contains(durableFile, "\"daBatch1Layer\": \"Local\"");
        StringAssert.Contains(durableFile, "\"daBatch2Layer\": \"Local\"");
        StringAssert.Contains(durableFile, "\"consumedDepositCount\": 10");
        StringAssert.Contains(durableFile, "\"deposit10IncludedInBatch\": 2");
        StringAssert.Contains(durableFile, "\"messageOutboxL2ToL1Count\": 9");
        StringAssert.Contains(durableFile, "\"isSettlementRetrying\": true");
        StringAssert.Contains(durableFile, "\"isOfflinePassportComplete\": true");
    }

    /// <summary>
    /// SoftSeal after ninth recover + tenth deposit: tenth withdrawal seal + L2→L1 outbox
    /// enqueue + eighth FI/inbound nonces + RPC withdrawal/message/router proofs while settle
    /// remains Retrying with multi-batch pending. Does not claim L1 claim / FI drain / settle.
    /// Returns tenth withdrawal leaf + outbound hash for follow-on pins.
    /// </summary>
    private static (UInt256 WithdrawalLeaf, UInt256 OutboundMessageHash) AssertSoftSealAfterNinthRecoverTenthOutboundAndFi(
        Func<WithdrawalRequest, UInt256> stageWithdrawal,
        Func<int> stagedWithdrawalCount,
        Func<(UInt256 Root, WithdrawalTree Tree)> sealWithdrawalBatch,
        Func<IReadOnlyList<CrossChainMessage>, Task> enqueueOutbound,
        Func<int> messageOutboxL2ToL1Count,
        Func<UInt256> messageOutboxL2ToL1Root,
        Func<ulong, bool> registerForcedInclusionNonce,
        Func<int> knownForcedInclusionNonceCount,
        Func<bool> hasOverdueForcedInclusionCached,
        Action invalidateForcedInclusionCache,
        Func<int> openBatchForcedInclusionCount,
        Func<ulong, bool> registerInboundMessageNonce,
        Func<int> knownInboundNonceCount,
        Action invalidateInboundMessageCache,
        Func<int> openBatchL1MessageCount,
        Func<int> l1InboxPendingCount,
        Action<UInt256, byte[]> recordRpcWithdrawalProof,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcWithdrawalProof,
        Action<UInt256, byte[]> recordRpcMessageProof,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcMessageProof,
        Action<UInt256, ReadOnlyMemory<byte>> recordMessageRouterFinalizedProof,
        Func<UInt256, Task<ReadOnlyMemory<byte>?>> getMessageRouterProofAsync,
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> formatHealthProbeJson,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        string chainDir)
    {
        var softL2Asset = UInt160.Parse("0x" + new string('2', 40));
        var softSender = Account(0xcc);
        var wdLeaf = stageWithdrawal(new WithdrawalRequest
        {
            ChainId = 20260716u,
            EmittingContract = softSender,
            L2Sender = softSender,
            L1Recipient = softSender,
            L2Asset = softL2Asset,
            Amount = new BigInteger(100),
            Nonce = 10,
        });
        Assert.AreNotEqual(UInt256.Zero, wdLeaf);
        Assert.IsTrue(stagedWithdrawalCount() >= 1);
        var sealedWd = sealWithdrawalBatch();
        Assert.AreNotEqual(UInt256.Zero, sealedWd.Root);
        Assert.AreEqual(0, stagedWithdrawalCount());
        Assert.IsTrue(sealedWd.Tree.Count >= 1);
        var merkleProof = sealedWd.Tree.GetProof(sealedWd.Tree.Count - 1);
        Assert.AreEqual(wdLeaf, merkleProof.Leaf);
        var proofBytes = MerkleProofSerializer.Encode(merkleProof);
        Assert.IsTrue(proofBytes.Length >= MerkleProofSerializer.HeaderSize);
        recordRpcWithdrawalProof(wdLeaf, proofBytes);
        var storedWdProof = getRpcWithdrawalProof(wdLeaf);
        Assert.IsTrue(storedWdProof is { Length: > 0 });
        CollectionAssert.AreEqual(proofBytes, storedWdProof!.Value.ToArray());

        var outboundDraft = new CrossChainMessage
        {
            SourceChainId = 20260716u,
            TargetChainId = 0,
            Nonce = 20,
            Sender = softSender,
            Receiver = softSender,
            MessageType = MessageType.Event,
            Payload = new byte[] { 0x0A },
            MessageHash = UInt256.Zero,
        };
        var outbound = outboundDraft with { MessageHash = MessageHasher.HashMessage(outboundDraft) };
        enqueueOutbound([outbound]).GetAwaiter().GetResult();
        Assert.AreEqual(10, messageOutboxL2ToL1Count());
        Assert.AreNotEqual(UInt256.Zero, messageOutboxL2ToL1Root());

        var messageProofBytes = outbound.MessageHash.GetSpan().ToArray();
        recordRpcMessageProof(outbound.MessageHash, messageProofBytes);
        var storedMsgProof = getRpcMessageProof(outbound.MessageHash);
        Assert.IsTrue(storedMsgProof is { Length: > 0 });
        CollectionAssert.AreEqual(messageProofBytes, storedMsgProof!.Value.ToArray());
        recordMessageRouterFinalizedProof(outbound.MessageHash, messageProofBytes);
        var routerProof = getMessageRouterProofAsync(outbound.MessageHash).GetAwaiter().GetResult();
        Assert.IsTrue(routerProof is { Length: > 0 });
        CollectionAssert.AreEqual(messageProofBytes, routerProof!.Value.ToArray());

        Assert.IsTrue(registerForcedInclusionNonce(22));
        Assert.IsFalse(registerForcedInclusionNonce(22));
        Assert.AreEqual(10, knownForcedInclusionNonceCount());
        Assert.AreEqual(0, openBatchForcedInclusionCount());
        Assert.IsFalse(hasOverdueForcedInclusionCached());
        invalidateForcedInclusionCache();
        Assert.AreEqual(10, knownForcedInclusionNonceCount());

        Assert.IsTrue(registerInboundMessageNonce(22));
        Assert.IsFalse(registerInboundMessageNonce(22));
        Assert.AreEqual(10, knownInboundNonceCount());
        Assert.AreEqual(0, openBatchL1MessageCount());
        Assert.AreEqual(0, l1InboxPendingCount());
        invalidateInboundMessageCache();
        Assert.AreEqual(10, knownInboundNonceCount());

        var status = getOperatorStatus();
        Assert.AreEqual(10, status.ConsumedDepositCount);
        Assert.AreEqual(10, status.MessageOutboxL2ToL1Count);
        Assert.AreEqual(0, status.StagedWithdrawalCount);
        Assert.AreEqual(10, status.KnownForcedInclusionNonceCount);
        Assert.AreEqual(10, status.KnownInboundNonceCount);
        Assert.IsFalse(status.HasOverdueForcedInclusion);
        Assert.AreEqual(0, status.OpenBatchForcedInclusionCount);
        Assert.AreEqual(0, status.OpenBatchL1MessageCount);
        Assert.AreEqual(2UL, status.LatestCheckpointBatchNumber);
        Assert.IsTrue(status.PendingSettlementCount >= 2);
        Assert.IsTrue(status.IsSettlementRetrying);
        Assert.IsFalse(status.IsSettlementPoisoned);
        Assert.IsFalse(status.IsSettlementIdle);
        Assert.IsTrue(status.IsOfflinePassportComplete);
        Assert.IsTrue(status.IsOperatorReady);
        Assert.IsTrue(status.IsBatcherCheckpointAligned);
        Assert.IsFalse(status.IsPipelineHealthy);
        CollectionAssert.Contains(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.IsSettlementRetrying));
        CollectionAssert.DoesNotContain(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.HasOverdueForcedInclusion));

        var probe = getHealthProbe();
        Assert.AreEqual(10, probe.ConsumedDepositCount);
        Assert.AreEqual(10, probe.MessageOutboxL2ToL1Count);
        Assert.AreEqual(10, probe.KnownForcedInclusionNonceCount);
        Assert.AreEqual(10, probe.KnownInboundNonceCount);
        Assert.IsTrue(probe.IsSettlementRetrying);
        Assert.AreEqual(2UL, probe.LatestCheckpointBatchNumber);
        Assert.IsTrue(probe.PendingSettlementCount >= 2);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"consumedDepositCount\": 10");
        StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Count\": 10");
        StringAssert.Contains(statusJson, "\"knownForcedInclusionNonceCount\": 10");
        StringAssert.Contains(statusJson, "\"knownInboundNonceCount\": 10");
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": 2");

        var probeJson = formatHealthProbeJson();
        StringAssert.Contains(probeJson, "\"messageOutboxL2ToL1Count\": 10");
        StringAssert.Contains(probeJson, "\"knownForcedInclusionNonceCount\": 10");
        StringAssert.Contains(probeJson, "\"knownInboundNonceCount\": 10");
        StringAssert.Contains(probeJson, "\"isSettlementRetrying\": true");

        var statusPath = Path.Combine(chainDir, "soft-seal-after-ninth-recover-tenth-outbound-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        var statusFile = File.ReadAllText(statusPath);
        StringAssert.Contains(statusFile, "\"messageOutboxL2ToL1Count\": 10");
        StringAssert.Contains(statusFile, "\"knownForcedInclusionNonceCount\": 10");
        StringAssert.Contains(statusFile, "\"knownInboundNonceCount\": 10");
        StringAssert.Contains(statusFile, "\"consumedDepositCount\": 10");

        var probePath = Path.Combine(chainDir, "soft-seal-after-ninth-recover-tenth-outbound-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        StringAssert.Contains(File.ReadAllText(probePath), "\"messageOutboxL2ToL1Count\": 10");
        StringAssert.Contains(File.ReadAllText(probePath), "\"knownForcedInclusionNonceCount\": 10");

        var durablePath = Path.Combine(chainDir, "soft-seal-after-ninth-recover-tenth-outbound.json");
        File.WriteAllText(durablePath, $$"""
            {
              "withdrawalNonce": 10,
              "outboundNonce": 20,
              "consumedDepositCount": {{status.ConsumedDepositCount}},
              "messageOutboxL2ToL1Count": {{status.MessageOutboxL2ToL1Count}},
              "knownForcedInclusionNonceCount": {{status.KnownForcedInclusionNonceCount}},
              "knownInboundNonceCount": {{status.KnownInboundNonceCount}},
              "withdrawalLeaf": "{{wdLeaf}}",
              "withdrawalRoot": "{{sealedWd.Root}}",
              "withdrawalProofBytes": {{proofBytes.Length}},
              "outboundMessageHash": "{{outbound.MessageHash}}",
              "messageProofBytes": {{messageProofBytes.Length}},
              "latestCheckpointBatchNumber": {{status.LatestCheckpointBatchNumber}},
              "pendingSettlementCount": {{status.PendingSettlementCount}},
              "isSettlementRetrying": true,
              "isSettlementPoisoned": false,
              "isOfflinePassportComplete": true
            }
            """);
        Assert.IsTrue(File.Exists(durablePath));
        var durableFile = File.ReadAllText(durablePath);
        StringAssert.Contains(durableFile, "\"messageOutboxL2ToL1Count\": 10");
        StringAssert.Contains(durableFile, "\"knownForcedInclusionNonceCount\": 10");
        StringAssert.Contains(durableFile, "\"consumedDepositCount\": 10");
        StringAssert.Contains(durableFile, "\"isSettlementRetrying\": true");

        var rpcSurfacePath = Path.Combine(chainDir, "soft-seal-after-ninth-recover-tenth-outbound-rpc.json");
        File.WriteAllText(rpcSurfacePath, $$"""
            {
              "withdrawalLeaf": "{{wdLeaf}}",
              "outboundMessageHash": "{{outbound.MessageHash}}",
              "withdrawalProofBytes": {{proofBytes.Length}},
              "messageProofBytes": {{messageProofBytes.Length}},
              "messageOutboxL2ToL1Count": 10,
              "knownForcedInclusionNonceCount": 10,
              "knownInboundNonceCount": 10,
              "consumedDepositCount": 10,
              "isSettlementRetrying": true
            }
            """);
        Assert.IsTrue(File.Exists(rpcSurfacePath));
        var rpcSurface = File.ReadAllText(rpcSurfacePath);
        StringAssert.Contains(rpcSurface, "\"withdrawalLeaf\": \"" + wdLeaf + "\"");
        StringAssert.Contains(rpcSurface, "\"outboundMessageHash\": \"" + outbound.MessageHash + "\"");
        StringAssert.Contains(rpcSurface, "\"messageOutboxL2ToL1Count\": 10");
        StringAssert.Contains(rpcSurface, "\"knownForcedInclusionNonceCount\": 10");
        return (wdLeaf, outbound.MessageHash);
    }

    /// <summary>
    /// SoftSeal full soft path after ninth recover + decuple deposit/outbox/FI: tenth
    /// Reconcile→Poison→Recover cycle must retain multi-batch RPC tip, decuple deposits,
    /// decuple outbox, decuple FI/inbound known counts, and tenth-outbound proofs.
    /// Does not claim L1 settle.
    /// </summary>
    private static void AssertSoftSealTenthPoisonRecoverRetention(
        Func<Task> reconcileAsync,
        Func<Task> submitNextAsync,
        Func<ulong, UInt256, Task> recoverPoisonedBatchAsync,
        Func<Task<bool>> isSettlementPoisonedAsync,
        Func<int> getPendingCount,
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> formatHealthProbeJson,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        Func<ulong, L2BatchCommitment?> getRpcBatch,
        Func<ulong, BatchStatus> getRpcBatchStatus,
        Func<ulong, UInt256> getRpcStateRootAtBatch,
        Func<UInt256> getLatestRpcStateRoot,
        Func<uint, ulong, bool> hasConsumedDeposit,
        Func<uint, ulong, DepositStatus?> getRpcL1DepositStatus,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcWithdrawalProof,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcMessageProof,
        UInt256 tenthWithdrawalLeaf,
        UInt256 tenthOutboundMessageHash,
        string chainDir)
    {
        var before = getOperatorStatus();
        Assert.IsTrue(before.IsSettlementRetrying);
        Assert.IsFalse(before.IsSettlementPoisoned);
        Assert.IsTrue(before.PendingSettlementCount >= 2);
        Assert.AreEqual(2UL, before.LatestCheckpointBatchNumber);
        Assert.AreEqual(10, before.ConsumedDepositCount);
        Assert.AreEqual(10, before.MessageOutboxL2ToL1Count);
        Assert.AreEqual(10, before.KnownForcedInclusionNonceCount);
        Assert.AreEqual(10, before.KnownInboundNonceCount);
        Assert.IsTrue(getPendingCount() >= 2);

        // RetryCount was reset by prior recover — re-escalate until Poisoned.
        LocalHostOperatorStatus afterPoison = before;
        for (var attempt = 0; attempt < 16; attempt++)
        {
            try
            {
                reconcileAsync().GetAwaiter().GetResult();
            }
            catch (OverflowException)
            {
            }
            catch (Exception)
            {
            }

            submitNextAsync().GetAwaiter().GetResult();
            afterPoison = getOperatorStatus();
            if (afterPoison.IsSettlementPoisoned)
                break;
        }

        Assert.IsTrue(afterPoison.IsSettlementPoisoned);
        Assert.IsFalse(afterPoison.IsSettlementRetrying);
        CollectionAssert.Contains(
            afterPoison.PipelineHealthFailures.ToArray(),
            nameof(afterPoison.IsSettlementPoisoned));
        Assert.IsNotNull(afterPoison.Recovery.BlockedBatchNumber);
        Assert.IsNotNull(afterPoison.Recovery.ArtifactContentHash);
        var blockedBatch = afterPoison.Recovery.BlockedBatchNumber!.Value;
        var contentHash = afterPoison.Recovery.ArtifactContentHash!;
        Assert.IsTrue(afterPoison.PendingSettlementCount >= 2);
        Assert.AreEqual(2UL, afterPoison.LatestCheckpointBatchNumber);
        Assert.AreEqual(10, afterPoison.ConsumedDepositCount);
        Assert.AreEqual(10, afterPoison.MessageOutboxL2ToL1Count);
        Assert.AreEqual(10, afterPoison.KnownForcedInclusionNonceCount);
        Assert.AreEqual(10, afterPoison.KnownInboundNonceCount);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => recoverPoisonedBatchAsync(blockedBatch, UInt256.Zero).GetAwaiter().GetResult());
        Assert.IsTrue(isSettlementPoisonedAsync().GetAwaiter().GetResult());
        recoverPoisonedBatchAsync(blockedBatch, contentHash).GetAwaiter().GetResult();

        var afterRecover = getOperatorStatus();
        Assert.IsFalse(afterRecover.IsSettlementPoisoned);
        Assert.IsTrue(afterRecover.IsSettlementRetrying);
        Assert.AreEqual(SettlementRecoveryState.Retrying, afterRecover.Recovery.State);
        Assert.AreEqual(0, afterRecover.Recovery.RetryCount);
        Assert.IsTrue(getPendingCount() >= 2);
        Assert.IsTrue(afterRecover.PendingSettlementCount >= 2);
        Assert.AreEqual(2UL, afterRecover.LatestCheckpointBatchNumber);
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, afterRecover.LatestCheckpointPostStateRoot);
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, afterRecover.LatestRpcStateRoot);
        Assert.AreEqual(10, afterRecover.ConsumedDepositCount);
        Assert.AreEqual(10, afterRecover.MessageOutboxL2ToL1Count);
        Assert.AreEqual(10, afterRecover.KnownForcedInclusionNonceCount);
        Assert.AreEqual(10, afterRecover.KnownInboundNonceCount);
        Assert.IsTrue(afterRecover.IsOfflinePassportComplete);
        Assert.IsTrue(afterRecover.IsOperatorReady);
        Assert.IsTrue(afterRecover.IsBatcherCheckpointAligned);
        Assert.IsFalse(afterRecover.IsPipelineHealthy);
        CollectionAssert.Contains(
            afterRecover.PipelineHealthFailures.ToArray(),
            nameof(afterRecover.IsSettlementRetrying));
        Assert.IsTrue(hasConsumedDeposit(0, 1));
        Assert.IsTrue(hasConsumedDeposit(0, 2));
        Assert.IsTrue(hasConsumedDeposit(0, 3));
        Assert.IsTrue(hasConsumedDeposit(0, 4));
        Assert.IsTrue(hasConsumedDeposit(0, 5));
        Assert.IsTrue(hasConsumedDeposit(0, 6));
        Assert.IsTrue(hasConsumedDeposit(0, 7));
        Assert.IsTrue(hasConsumedDeposit(0, 8));
        Assert.IsTrue(hasConsumedDeposit(0, 9));
        Assert.IsTrue(hasConsumedDeposit(0, 10));
        Assert.IsTrue(getRpcL1DepositStatus(0, 1) is { ConsumedOnL2: true, IncludedInBatch: 1UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 2) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 3) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 4) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 5) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 6) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 7) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 8) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 9) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 10) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.AreEqual(BatchStatus.Finalized, getRpcBatchStatus(1));
        Assert.AreEqual(BatchStatus.Finalized, getRpcBatchStatus(2));
        Assert.IsNotNull(getRpcBatch(1));
        Assert.IsNotNull(getRpcBatch(2));
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, getRpcStateRootAtBatch(1));
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, getRpcStateRootAtBatch(2));
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, getLatestRpcStateRoot());
        Assert.IsTrue(getRpcWithdrawalProof(tenthWithdrawalLeaf) is { Length: > 0 });
        Assert.IsTrue(getRpcMessageProof(tenthOutboundMessageHash) is { Length: > 0 });

        var probe = getHealthProbe();
        Assert.IsTrue(probe.IsSettlementRetrying);
        Assert.IsFalse(probe.IsSettlementPoisoned);
        Assert.AreEqual(2UL, probe.LatestCheckpointBatchNumber);
        Assert.IsTrue(probe.PendingSettlementCount >= 2);
        Assert.AreEqual(10, probe.ConsumedDepositCount);
        Assert.AreEqual(10, probe.MessageOutboxL2ToL1Count);
        Assert.AreEqual(10, probe.KnownForcedInclusionNonceCount);
        Assert.AreEqual(10, probe.KnownInboundNonceCount);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusJson, "\"isSettlementPoisoned\": false");
        StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": 2");
        StringAssert.Contains(statusJson, "\"consumedDepositCount\": 10");
        StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Count\": 10");
        StringAssert.Contains(statusJson, "\"knownForcedInclusionNonceCount\": 10");
        StringAssert.Contains(statusJson, "\"knownInboundNonceCount\": 10");

        var probeJson = formatHealthProbeJson();
        StringAssert.Contains(probeJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(probeJson, "\"consumedDepositCount\": 10");
        StringAssert.Contains(probeJson, "\"messageOutboxL2ToL1Count\": 10");

        var statusPath = Path.Combine(chainDir, "soft-seal-tenth-poison-recover-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        var statusFile = File.ReadAllText(statusPath);
        StringAssert.Contains(statusFile, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusFile, "\"consumedDepositCount\": 10");
        StringAssert.Contains(statusFile, "\"messageOutboxL2ToL1Count\": 10");
        StringAssert.Contains(statusFile, "\"latestCheckpointBatchNumber\": 2");

        var probePath = Path.Combine(chainDir, "soft-seal-tenth-poison-recover-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        StringAssert.Contains(File.ReadAllText(probePath), "\"consumedDepositCount\": 10");
        StringAssert.Contains(File.ReadAllText(probePath), "\"isSettlementRetrying\": true");

        var durablePath = Path.Combine(chainDir, "soft-seal-tenth-poison-recover.json");
        File.WriteAllText(durablePath, $$"""
            {
              "tenthPoisonBlockedBatch": {{blockedBatch}},
              "pendingSettlementCount": {{afterRecover.PendingSettlementCount}},
              "latestCheckpointBatchNumber": {{afterRecover.LatestCheckpointBatchNumber}},
              "consumedDepositCount": {{afterRecover.ConsumedDepositCount}},
              "messageOutboxL2ToL1Count": {{afterRecover.MessageOutboxL2ToL1Count}},
              "knownForcedInclusionNonceCount": {{afterRecover.KnownForcedInclusionNonceCount}},
              "knownInboundNonceCount": {{afterRecover.KnownInboundNonceCount}},
              "rpcBatch1Status": "{{getRpcBatchStatus(1)}}",
              "rpcBatch2Status": "{{getRpcBatchStatus(2)}}",
              "tenthWithdrawalProofPresent": true,
              "tenthMessageProofPresent": true,
              "isSettlementRetrying": true,
              "isSettlementPoisoned": false,
              "isOfflinePassportComplete": true
            }
            """);
        Assert.IsTrue(File.Exists(durablePath));
        var durableFile = File.ReadAllText(durablePath);
        StringAssert.Contains(durableFile, "\"rpcBatch1Status\": \"Finalized\"");
        StringAssert.Contains(durableFile, "\"rpcBatch2Status\": \"Finalized\"");
        StringAssert.Contains(durableFile, "\"consumedDepositCount\": 10");
        StringAssert.Contains(durableFile, "\"messageOutboxL2ToL1Count\": 10");
        StringAssert.Contains(durableFile, "\"knownForcedInclusionNonceCount\": 10");
        StringAssert.Contains(durableFile, "\"isSettlementRetrying\": true");
        StringAssert.Contains(durableFile, "\"isSettlementPoisoned\": false");
    }

    /// <summary>
    /// SoftSeal after tenth poison→recover: re-publish local DA for sealed batches 1+2
    /// and process an eleventh offline deposit (nonce 11, IncludedInBatch=2) while settle
    /// remains Retrying with multi-batch pending and decuple outbox/FI known retained.
    /// Does not claim L1 deposit scan / production DA / settle.
    /// </summary>
    private static void AssertSoftSealAfterTenthRecoverDaAndEleventhDeposit(
        Func<DAPublishRequest, Task<DAReceipt>> publishDaAsync,
        Func<DAReceipt, Task<bool>> isDaAvailableAsync,
        Func<bool> supportsLocalDaReader,
        Func<IDAReader> createLocalDaReader,
        Func<CrossChainMessage, MintInstruction> processDeposit,
        Func<uint, ulong, bool> hasConsumedDeposit,
        Func<int> consumedDepositCount,
        Action<DepositStatus> recordRpcDeposit,
        Func<uint, ulong, DepositStatus?> getRpcL1DepositStatus,
        Func<Task<int>> scanSharedBridgeDepositsAsync,
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> exportPrometheusMetrics,
        Func<string, Task> writePrometheusMetricsAsync,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        string chainDir)
    {
        Assert.IsTrue(supportsLocalDaReader());
        var da1Payload = new byte[] { 0xDA, 0xA1, 0x01 };
        var da1 = publishDaAsync(new DAPublishRequest
        {
            ChainId = 20260716u,
            BatchNumber = 1,
            Payload = da1Payload,
        }).GetAwaiter().GetResult();
        Assert.AreEqual(DAMode.Local, da1.Layer);
        Assert.AreEqual(DAReceiptKind.LocalPersistence, da1.Kind);
        Assert.IsTrue(isDaAvailableAsync(da1).GetAwaiter().GetResult());
        var da1Read = createLocalDaReader().ReadAsync(da1).AsTask().GetAwaiter().GetResult();
        Assert.IsTrue(da1Read is { Length: 3 });
        CollectionAssert.AreEqual(da1Payload, da1Read!.Value.ToArray());

        var da2Payload = new byte[] { 0xDA, 0xA2, 0x02 };
        var da2 = publishDaAsync(new DAPublishRequest
        {
            ChainId = 20260716u,
            BatchNumber = 2,
            Payload = da2Payload,
        }).GetAwaiter().GetResult();
        Assert.AreEqual(DAMode.Local, da2.Layer);
        Assert.IsTrue(isDaAvailableAsync(da2).GetAwaiter().GetResult());
        var da2Read = createLocalDaReader().ReadAsync(da2).AsTask().GetAwaiter().GetResult();
        Assert.IsTrue(da2Read is { Length: 3 });
        CollectionAssert.AreEqual(da2Payload, da2Read!.Value.ToArray());

        // Eleventh offline mint while still Retrying after tenth recover.
        var softL1Asset = UInt160.Parse("0x" + new string('1', 40));
        var softL2Asset = UInt160.Parse("0x" + new string('2', 40));
        var softDepositPayload = new DepositPayload
        {
            L1Asset = softL1Asset,
            L2Recipient = Account(0x55),
            Amount = new BigInteger(11_000),
        };
        var softDepositMsg = new CrossChainMessage
        {
            SourceChainId = 0,
            TargetChainId = 20260716u,
            Nonce = 11,
            Sender = Account(0x66),
            Receiver = Account(0x55),
            MessageType = MessageType.Deposit,
            Payload = softDepositPayload.Encode(),
            MessageHash = UInt256.Zero,
        };
        var softMint = processDeposit(softDepositMsg);
        Assert.AreEqual(softL2Asset, softMint.L2Asset);
        Assert.IsTrue(hasConsumedDeposit(0, 1));
        Assert.IsTrue(hasConsumedDeposit(0, 2));
        Assert.IsTrue(hasConsumedDeposit(0, 3));
        Assert.IsTrue(hasConsumedDeposit(0, 4));
        Assert.IsTrue(hasConsumedDeposit(0, 5));
        Assert.IsTrue(hasConsumedDeposit(0, 6));
        Assert.IsTrue(hasConsumedDeposit(0, 7));
        Assert.IsTrue(hasConsumedDeposit(0, 8));
        Assert.IsTrue(hasConsumedDeposit(0, 9));
        Assert.IsTrue(hasConsumedDeposit(0, 10));
        Assert.IsTrue(hasConsumedDeposit(0, 11));
        Assert.AreEqual(11, consumedDepositCount());
        recordRpcDeposit(new DepositStatus(0, 11, ConsumedOnL2: true, IncludedInBatch: 2));
        Assert.IsTrue(getRpcL1DepositStatus(0, 1) is { ConsumedOnL2: true, IncludedInBatch: 1UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 2) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 3) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 4) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 5) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 6) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 7) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 8) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 9) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 10) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 11) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.AreEqual(0, scanSharedBridgeDepositsAsync().GetAwaiter().GetResult());

        var status = getOperatorStatus();
        Assert.AreEqual(11, status.ConsumedDepositCount);
        Assert.AreEqual(10, status.MessageOutboxL2ToL1Count);
        Assert.AreEqual(10, status.KnownForcedInclusionNonceCount);
        Assert.AreEqual(10, status.KnownInboundNonceCount);
        Assert.AreEqual(2UL, status.LatestCheckpointBatchNumber);
        Assert.IsTrue(status.PendingSettlementCount >= 2);
        Assert.IsTrue(status.IsSettlementRetrying);
        Assert.IsFalse(status.IsSettlementPoisoned);
        Assert.IsFalse(status.IsSettlementIdle);
        Assert.IsTrue(status.IsOfflinePassportComplete);
        Assert.IsTrue(status.IsOperatorReady);
        Assert.IsTrue(status.IsBatcherCheckpointAligned);
        Assert.IsFalse(status.IsPipelineHealthy);
        CollectionAssert.Contains(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.IsSettlementRetrying));

        var probe = getHealthProbe();
        Assert.AreEqual(11, probe.ConsumedDepositCount);
        Assert.AreEqual(10, probe.MessageOutboxL2ToL1Count);
        Assert.AreEqual(2UL, probe.LatestCheckpointBatchNumber);
        Assert.IsTrue(probe.PendingSettlementCount >= 2);
        Assert.IsTrue(probe.IsSettlementRetrying);
        Assert.IsFalse(probe.IsSettlementPoisoned);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"consumedDepositCount\": 11");
        StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Count\": 10");
        StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": 2");
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusJson, "\"isSettlementPoisoned\": false");

        var hostProm = exportPrometheusMetrics();
        Assert.IsFalse(string.IsNullOrWhiteSpace(hostProm));
        var hostPromPath = Path.Combine(chainDir, "soft-seal-after-tenth-recover-host.prom");
        writePrometheusMetricsAsync(hostPromPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(hostPromPath));
        Assert.AreEqual(hostProm, File.ReadAllText(hostPromPath));

        var statusPath = Path.Combine(chainDir, "soft-seal-after-tenth-recover-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        StringAssert.Contains(File.ReadAllText(statusPath), "\"consumedDepositCount\": 11");
        StringAssert.Contains(File.ReadAllText(statusPath), "\"isSettlementRetrying\": true");

        var probePath = Path.Combine(chainDir, "soft-seal-after-tenth-recover-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        StringAssert.Contains(File.ReadAllText(probePath), "\"consumedDepositCount\": 11");

        var durablePath = Path.Combine(chainDir, "soft-seal-after-tenth-recover-da-deposit.json");
        File.WriteAllText(durablePath, $$"""
            {
              "daBatch1Layer": "{{da1.Layer}}",
              "daBatch2Layer": "{{da2.Layer}}",
              "daBatch1Available": true,
              "daBatch2Available": true,
              "consumedDepositCount": {{status.ConsumedDepositCount}},
              "deposit11IncludedInBatch": 2,
              "messageOutboxL2ToL1Count": {{status.MessageOutboxL2ToL1Count}},
              "knownForcedInclusionNonceCount": {{status.KnownForcedInclusionNonceCount}},
              "knownInboundNonceCount": {{status.KnownInboundNonceCount}},
              "latestCheckpointBatchNumber": {{status.LatestCheckpointBatchNumber}},
              "pendingSettlementCount": {{status.PendingSettlementCount}},
              "isSettlementRetrying": true,
              "isSettlementPoisoned": false,
              "isOfflinePassportComplete": true
            }
            """);
        Assert.IsTrue(File.Exists(durablePath));
        var durableFile = File.ReadAllText(durablePath);
        StringAssert.Contains(durableFile, "\"daBatch1Layer\": \"Local\"");
        StringAssert.Contains(durableFile, "\"daBatch2Layer\": \"Local\"");
        StringAssert.Contains(durableFile, "\"consumedDepositCount\": 11");
        StringAssert.Contains(durableFile, "\"deposit11IncludedInBatch\": 2");
        StringAssert.Contains(durableFile, "\"messageOutboxL2ToL1Count\": 10");
        StringAssert.Contains(durableFile, "\"isSettlementRetrying\": true");
        StringAssert.Contains(durableFile, "\"isOfflinePassportComplete\": true");
    }

    /// <summary>
    /// SoftSeal after tenth recover + eleventh deposit: eleventh withdrawal seal + L2→L1 outbox
    /// enqueue + eighth FI/inbound nonces + RPC withdrawal/message/router proofs while settle
    /// remains Retrying with multi-batch pending. Does not claim L1 claim / FI drain / settle.
    /// Returns eleventh withdrawal leaf + outbound hash for follow-on pins.
    /// </summary>
    private static (UInt256 WithdrawalLeaf, UInt256 OutboundMessageHash) AssertSoftSealAfterTenthRecoverEleventhOutboundAndFi(
        Func<WithdrawalRequest, UInt256> stageWithdrawal,
        Func<int> stagedWithdrawalCount,
        Func<(UInt256 Root, WithdrawalTree Tree)> sealWithdrawalBatch,
        Func<IReadOnlyList<CrossChainMessage>, Task> enqueueOutbound,
        Func<int> messageOutboxL2ToL1Count,
        Func<UInt256> messageOutboxL2ToL1Root,
        Func<ulong, bool> registerForcedInclusionNonce,
        Func<int> knownForcedInclusionNonceCount,
        Func<bool> hasOverdueForcedInclusionCached,
        Action invalidateForcedInclusionCache,
        Func<int> openBatchForcedInclusionCount,
        Func<ulong, bool> registerInboundMessageNonce,
        Func<int> knownInboundNonceCount,
        Action invalidateInboundMessageCache,
        Func<int> openBatchL1MessageCount,
        Func<int> l1InboxPendingCount,
        Action<UInt256, byte[]> recordRpcWithdrawalProof,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcWithdrawalProof,
        Action<UInt256, byte[]> recordRpcMessageProof,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcMessageProof,
        Action<UInt256, ReadOnlyMemory<byte>> recordMessageRouterFinalizedProof,
        Func<UInt256, Task<ReadOnlyMemory<byte>?>> getMessageRouterProofAsync,
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> formatHealthProbeJson,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        string chainDir)
    {
        var softL2Asset = UInt160.Parse("0x" + new string('2', 40));
        var softSender = Account(0xcc);
        var wdLeaf = stageWithdrawal(new WithdrawalRequest
        {
            ChainId = 20260716u,
            EmittingContract = softSender,
            L2Sender = softSender,
            L1Recipient = softSender,
            L2Asset = softL2Asset,
            Amount = new BigInteger(100),
            Nonce = 11,
        });
        Assert.AreNotEqual(UInt256.Zero, wdLeaf);
        Assert.IsTrue(stagedWithdrawalCount() >= 1);
        var sealedWd = sealWithdrawalBatch();
        Assert.AreNotEqual(UInt256.Zero, sealedWd.Root);
        Assert.AreEqual(0, stagedWithdrawalCount());
        Assert.IsTrue(sealedWd.Tree.Count >= 1);
        var merkleProof = sealedWd.Tree.GetProof(sealedWd.Tree.Count - 1);
        Assert.AreEqual(wdLeaf, merkleProof.Leaf);
        var proofBytes = MerkleProofSerializer.Encode(merkleProof);
        Assert.IsTrue(proofBytes.Length >= MerkleProofSerializer.HeaderSize);
        recordRpcWithdrawalProof(wdLeaf, proofBytes);
        var storedWdProof = getRpcWithdrawalProof(wdLeaf);
        Assert.IsTrue(storedWdProof is { Length: > 0 });
        CollectionAssert.AreEqual(proofBytes, storedWdProof!.Value.ToArray());

        var outboundDraft = new CrossChainMessage
        {
            SourceChainId = 20260716u,
            TargetChainId = 0,
            Nonce = 22,
            Sender = softSender,
            Receiver = softSender,
            MessageType = MessageType.Event,
            Payload = new byte[] { 0x0B },
            MessageHash = UInt256.Zero,
        };
        var outbound = outboundDraft with { MessageHash = MessageHasher.HashMessage(outboundDraft) };
        enqueueOutbound([outbound]).GetAwaiter().GetResult();
        Assert.AreEqual(11, messageOutboxL2ToL1Count());
        Assert.AreNotEqual(UInt256.Zero, messageOutboxL2ToL1Root());

        var messageProofBytes = outbound.MessageHash.GetSpan().ToArray();
        recordRpcMessageProof(outbound.MessageHash, messageProofBytes);
        var storedMsgProof = getRpcMessageProof(outbound.MessageHash);
        Assert.IsTrue(storedMsgProof is { Length: > 0 });
        CollectionAssert.AreEqual(messageProofBytes, storedMsgProof!.Value.ToArray());
        recordMessageRouterFinalizedProof(outbound.MessageHash, messageProofBytes);
        var routerProof = getMessageRouterProofAsync(outbound.MessageHash).GetAwaiter().GetResult();
        Assert.IsTrue(routerProof is { Length: > 0 });
        CollectionAssert.AreEqual(messageProofBytes, routerProof!.Value.ToArray());

        Assert.IsTrue(registerForcedInclusionNonce(24));
        Assert.IsFalse(registerForcedInclusionNonce(24));
        Assert.AreEqual(11, knownForcedInclusionNonceCount());
        Assert.AreEqual(0, openBatchForcedInclusionCount());
        Assert.IsFalse(hasOverdueForcedInclusionCached());
        invalidateForcedInclusionCache();
        Assert.AreEqual(11, knownForcedInclusionNonceCount());

        Assert.IsTrue(registerInboundMessageNonce(24));
        Assert.IsFalse(registerInboundMessageNonce(24));
        Assert.AreEqual(11, knownInboundNonceCount());
        Assert.AreEqual(0, openBatchL1MessageCount());
        Assert.AreEqual(0, l1InboxPendingCount());
        invalidateInboundMessageCache();
        Assert.AreEqual(11, knownInboundNonceCount());

        var status = getOperatorStatus();
        Assert.AreEqual(11, status.ConsumedDepositCount);
        Assert.AreEqual(11, status.MessageOutboxL2ToL1Count);
        Assert.AreEqual(0, status.StagedWithdrawalCount);
        Assert.AreEqual(11, status.KnownForcedInclusionNonceCount);
        Assert.AreEqual(11, status.KnownInboundNonceCount);
        Assert.IsFalse(status.HasOverdueForcedInclusion);
        Assert.AreEqual(0, status.OpenBatchForcedInclusionCount);
        Assert.AreEqual(0, status.OpenBatchL1MessageCount);
        Assert.AreEqual(2UL, status.LatestCheckpointBatchNumber);
        Assert.IsTrue(status.PendingSettlementCount >= 2);
        Assert.IsTrue(status.IsSettlementRetrying);
        Assert.IsFalse(status.IsSettlementPoisoned);
        Assert.IsFalse(status.IsSettlementIdle);
        Assert.IsTrue(status.IsOfflinePassportComplete);
        Assert.IsTrue(status.IsOperatorReady);
        Assert.IsTrue(status.IsBatcherCheckpointAligned);
        Assert.IsFalse(status.IsPipelineHealthy);
        CollectionAssert.Contains(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.IsSettlementRetrying));
        CollectionAssert.DoesNotContain(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.HasOverdueForcedInclusion));

        var probe = getHealthProbe();
        Assert.AreEqual(11, probe.ConsumedDepositCount);
        Assert.AreEqual(11, probe.MessageOutboxL2ToL1Count);
        Assert.AreEqual(11, probe.KnownForcedInclusionNonceCount);
        Assert.AreEqual(11, probe.KnownInboundNonceCount);
        Assert.IsTrue(probe.IsSettlementRetrying);
        Assert.AreEqual(2UL, probe.LatestCheckpointBatchNumber);
        Assert.IsTrue(probe.PendingSettlementCount >= 2);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"consumedDepositCount\": 11");
        StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Count\": 11");
        StringAssert.Contains(statusJson, "\"knownForcedInclusionNonceCount\": 11");
        StringAssert.Contains(statusJson, "\"knownInboundNonceCount\": 11");
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": 2");

        var probeJson = formatHealthProbeJson();
        StringAssert.Contains(probeJson, "\"messageOutboxL2ToL1Count\": 11");
        StringAssert.Contains(probeJson, "\"knownForcedInclusionNonceCount\": 11");
        StringAssert.Contains(probeJson, "\"knownInboundNonceCount\": 11");
        StringAssert.Contains(probeJson, "\"isSettlementRetrying\": true");

        var statusPath = Path.Combine(chainDir, "soft-seal-after-tenth-recover-eleventh-outbound-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        var statusFile = File.ReadAllText(statusPath);
        StringAssert.Contains(statusFile, "\"messageOutboxL2ToL1Count\": 11");
        StringAssert.Contains(statusFile, "\"knownForcedInclusionNonceCount\": 11");
        StringAssert.Contains(statusFile, "\"knownInboundNonceCount\": 11");
        StringAssert.Contains(statusFile, "\"consumedDepositCount\": 11");

        var probePath = Path.Combine(chainDir, "soft-seal-after-tenth-recover-eleventh-outbound-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        StringAssert.Contains(File.ReadAllText(probePath), "\"messageOutboxL2ToL1Count\": 11");
        StringAssert.Contains(File.ReadAllText(probePath), "\"knownForcedInclusionNonceCount\": 11");

        var durablePath = Path.Combine(chainDir, "soft-seal-after-tenth-recover-eleventh-outbound.json");
        File.WriteAllText(durablePath, $$"""
            {
              "withdrawalNonce": 11,
              "outboundNonce": 22,
              "consumedDepositCount": {{status.ConsumedDepositCount}},
              "messageOutboxL2ToL1Count": {{status.MessageOutboxL2ToL1Count}},
              "knownForcedInclusionNonceCount": {{status.KnownForcedInclusionNonceCount}},
              "knownInboundNonceCount": {{status.KnownInboundNonceCount}},
              "withdrawalLeaf": "{{wdLeaf}}",
              "withdrawalRoot": "{{sealedWd.Root}}",
              "withdrawalProofBytes": {{proofBytes.Length}},
              "outboundMessageHash": "{{outbound.MessageHash}}",
              "messageProofBytes": {{messageProofBytes.Length}},
              "latestCheckpointBatchNumber": {{status.LatestCheckpointBatchNumber}},
              "pendingSettlementCount": {{status.PendingSettlementCount}},
              "isSettlementRetrying": true,
              "isSettlementPoisoned": false,
              "isOfflinePassportComplete": true
            }
            """);
        Assert.IsTrue(File.Exists(durablePath));
        var durableFile = File.ReadAllText(durablePath);
        StringAssert.Contains(durableFile, "\"messageOutboxL2ToL1Count\": 11");
        StringAssert.Contains(durableFile, "\"knownForcedInclusionNonceCount\": 11");
        StringAssert.Contains(durableFile, "\"consumedDepositCount\": 11");
        StringAssert.Contains(durableFile, "\"isSettlementRetrying\": true");

        var rpcSurfacePath = Path.Combine(chainDir, "soft-seal-after-tenth-recover-eleventh-outbound-rpc.json");
        File.WriteAllText(rpcSurfacePath, $$"""
            {
              "withdrawalLeaf": "{{wdLeaf}}",
              "outboundMessageHash": "{{outbound.MessageHash}}",
              "withdrawalProofBytes": {{proofBytes.Length}},
              "messageProofBytes": {{messageProofBytes.Length}},
              "messageOutboxL2ToL1Count": 11,
              "knownForcedInclusionNonceCount": 11,
              "knownInboundNonceCount": 11,
              "consumedDepositCount": 11,
              "isSettlementRetrying": true
            }
            """);
        Assert.IsTrue(File.Exists(rpcSurfacePath));
        var rpcSurface = File.ReadAllText(rpcSurfacePath);
        StringAssert.Contains(rpcSurface, "\"withdrawalLeaf\": \"" + wdLeaf + "\"");
        StringAssert.Contains(rpcSurface, "\"outboundMessageHash\": \"" + outbound.MessageHash + "\"");
        StringAssert.Contains(rpcSurface, "\"messageOutboxL2ToL1Count\": 11");
        StringAssert.Contains(rpcSurface, "\"knownForcedInclusionNonceCount\": 11");
        return (wdLeaf, outbound.MessageHash);
    }

    /// <summary>
    /// SoftSeal full soft path after tenth recover + undecuple deposit/outbox/FI: eleventh
    /// Reconcile→Poison→Recover cycle must retain multi-batch RPC tip, undecuple deposits,
    /// undecuple outbox, undecuple FI/inbound known counts, and eleventh-outbound proofs.
    /// Does not claim L1 settle.
    /// </summary>
    private static void AssertSoftSealEleventhPoisonRecoverRetention(
        Func<Task> reconcileAsync,
        Func<Task> submitNextAsync,
        Func<ulong, UInt256, Task> recoverPoisonedBatchAsync,
        Func<Task<bool>> isSettlementPoisonedAsync,
        Func<int> getPendingCount,
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> formatHealthProbeJson,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        Func<ulong, L2BatchCommitment?> getRpcBatch,
        Func<ulong, BatchStatus> getRpcBatchStatus,
        Func<ulong, UInt256> getRpcStateRootAtBatch,
        Func<UInt256> getLatestRpcStateRoot,
        Func<uint, ulong, bool> hasConsumedDeposit,
        Func<uint, ulong, DepositStatus?> getRpcL1DepositStatus,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcWithdrawalProof,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcMessageProof,
        UInt256 eleventhWithdrawalLeaf,
        UInt256 eleventhOutboundMessageHash,
        string chainDir)
    {
        var before = getOperatorStatus();
        Assert.IsTrue(before.IsSettlementRetrying);
        Assert.IsFalse(before.IsSettlementPoisoned);
        Assert.IsTrue(before.PendingSettlementCount >= 2);
        Assert.AreEqual(2UL, before.LatestCheckpointBatchNumber);
        Assert.AreEqual(11, before.ConsumedDepositCount);
        Assert.AreEqual(11, before.MessageOutboxL2ToL1Count);
        Assert.AreEqual(11, before.KnownForcedInclusionNonceCount);
        Assert.AreEqual(11, before.KnownInboundNonceCount);
        Assert.IsTrue(getPendingCount() >= 2);

        // RetryCount was reset by prior recover — re-escalate until Poisoned.
        LocalHostOperatorStatus afterPoison = before;
        for (var attempt = 0; attempt < 16; attempt++)
        {
            try
            {
                reconcileAsync().GetAwaiter().GetResult();
            }
            catch (OverflowException)
            {
            }
            catch (Exception)
            {
            }

            submitNextAsync().GetAwaiter().GetResult();
            afterPoison = getOperatorStatus();
            if (afterPoison.IsSettlementPoisoned)
                break;
        }

        Assert.IsTrue(afterPoison.IsSettlementPoisoned);
        Assert.IsFalse(afterPoison.IsSettlementRetrying);
        CollectionAssert.Contains(
            afterPoison.PipelineHealthFailures.ToArray(),
            nameof(afterPoison.IsSettlementPoisoned));
        Assert.IsNotNull(afterPoison.Recovery.BlockedBatchNumber);
        Assert.IsNotNull(afterPoison.Recovery.ArtifactContentHash);
        var blockedBatch = afterPoison.Recovery.BlockedBatchNumber!.Value;
        var contentHash = afterPoison.Recovery.ArtifactContentHash!;
        Assert.IsTrue(afterPoison.PendingSettlementCount >= 2);
        Assert.AreEqual(2UL, afterPoison.LatestCheckpointBatchNumber);
        Assert.AreEqual(11, afterPoison.ConsumedDepositCount);
        Assert.AreEqual(11, afterPoison.MessageOutboxL2ToL1Count);
        Assert.AreEqual(11, afterPoison.KnownForcedInclusionNonceCount);
        Assert.AreEqual(11, afterPoison.KnownInboundNonceCount);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => recoverPoisonedBatchAsync(blockedBatch, UInt256.Zero).GetAwaiter().GetResult());
        Assert.IsTrue(isSettlementPoisonedAsync().GetAwaiter().GetResult());
        recoverPoisonedBatchAsync(blockedBatch, contentHash).GetAwaiter().GetResult();

        var afterRecover = getOperatorStatus();
        Assert.IsFalse(afterRecover.IsSettlementPoisoned);
        Assert.IsTrue(afterRecover.IsSettlementRetrying);
        Assert.AreEqual(SettlementRecoveryState.Retrying, afterRecover.Recovery.State);
        Assert.AreEqual(0, afterRecover.Recovery.RetryCount);
        Assert.IsTrue(getPendingCount() >= 2);
        Assert.IsTrue(afterRecover.PendingSettlementCount >= 2);
        Assert.AreEqual(2UL, afterRecover.LatestCheckpointBatchNumber);
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, afterRecover.LatestCheckpointPostStateRoot);
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, afterRecover.LatestRpcStateRoot);
        Assert.AreEqual(11, afterRecover.ConsumedDepositCount);
        Assert.AreEqual(11, afterRecover.MessageOutboxL2ToL1Count);
        Assert.AreEqual(11, afterRecover.KnownForcedInclusionNonceCount);
        Assert.AreEqual(11, afterRecover.KnownInboundNonceCount);
        Assert.IsTrue(afterRecover.IsOfflinePassportComplete);
        Assert.IsTrue(afterRecover.IsOperatorReady);
        Assert.IsTrue(afterRecover.IsBatcherCheckpointAligned);
        Assert.IsFalse(afterRecover.IsPipelineHealthy);
        CollectionAssert.Contains(
            afterRecover.PipelineHealthFailures.ToArray(),
            nameof(afterRecover.IsSettlementRetrying));
        Assert.IsTrue(hasConsumedDeposit(0, 1));
        Assert.IsTrue(hasConsumedDeposit(0, 2));
        Assert.IsTrue(hasConsumedDeposit(0, 3));
        Assert.IsTrue(hasConsumedDeposit(0, 4));
        Assert.IsTrue(hasConsumedDeposit(0, 5));
        Assert.IsTrue(hasConsumedDeposit(0, 6));
        Assert.IsTrue(hasConsumedDeposit(0, 7));
        Assert.IsTrue(hasConsumedDeposit(0, 8));
        Assert.IsTrue(hasConsumedDeposit(0, 9));
        Assert.IsTrue(hasConsumedDeposit(0, 10));
        Assert.IsTrue(hasConsumedDeposit(0, 11));
        Assert.IsTrue(getRpcL1DepositStatus(0, 1) is { ConsumedOnL2: true, IncludedInBatch: 1UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 2) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 3) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 4) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 5) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 6) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 7) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 8) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 9) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 10) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 11) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.AreEqual(BatchStatus.Finalized, getRpcBatchStatus(1));
        Assert.AreEqual(BatchStatus.Finalized, getRpcBatchStatus(2));
        Assert.IsNotNull(getRpcBatch(1));
        Assert.IsNotNull(getRpcBatch(2));
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, getRpcStateRootAtBatch(1));
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, getRpcStateRootAtBatch(2));
        Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, getLatestRpcStateRoot());
        Assert.IsTrue(getRpcWithdrawalProof(eleventhWithdrawalLeaf) is { Length: > 0 });
        Assert.IsTrue(getRpcMessageProof(eleventhOutboundMessageHash) is { Length: > 0 });

        var probe = getHealthProbe();
        Assert.IsTrue(probe.IsSettlementRetrying);
        Assert.IsFalse(probe.IsSettlementPoisoned);
        Assert.AreEqual(2UL, probe.LatestCheckpointBatchNumber);
        Assert.IsTrue(probe.PendingSettlementCount >= 2);
        Assert.AreEqual(11, probe.ConsumedDepositCount);
        Assert.AreEqual(11, probe.MessageOutboxL2ToL1Count);
        Assert.AreEqual(11, probe.KnownForcedInclusionNonceCount);
        Assert.AreEqual(11, probe.KnownInboundNonceCount);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusJson, "\"isSettlementPoisoned\": false");
        StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": 2");
        StringAssert.Contains(statusJson, "\"consumedDepositCount\": 11");
        StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Count\": 11");
        StringAssert.Contains(statusJson, "\"knownForcedInclusionNonceCount\": 11");
        StringAssert.Contains(statusJson, "\"knownInboundNonceCount\": 11");

        var probeJson = formatHealthProbeJson();
        StringAssert.Contains(probeJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(probeJson, "\"consumedDepositCount\": 11");
        StringAssert.Contains(probeJson, "\"messageOutboxL2ToL1Count\": 11");

        var statusPath = Path.Combine(chainDir, "soft-seal-eleventh-poison-recover-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        var statusFile = File.ReadAllText(statusPath);
        StringAssert.Contains(statusFile, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusFile, "\"consumedDepositCount\": 11");
        StringAssert.Contains(statusFile, "\"messageOutboxL2ToL1Count\": 11");
        StringAssert.Contains(statusFile, "\"latestCheckpointBatchNumber\": 2");

        var probePath = Path.Combine(chainDir, "soft-seal-eleventh-poison-recover-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        StringAssert.Contains(File.ReadAllText(probePath), "\"consumedDepositCount\": 11");
        StringAssert.Contains(File.ReadAllText(probePath), "\"isSettlementRetrying\": true");

        var durablePath = Path.Combine(chainDir, "soft-seal-eleventh-poison-recover.json");
        File.WriteAllText(durablePath, $$"""
            {
              "eleventhPoisonBlockedBatch": {{blockedBatch}},
              "pendingSettlementCount": {{afterRecover.PendingSettlementCount}},
              "latestCheckpointBatchNumber": {{afterRecover.LatestCheckpointBatchNumber}},
              "consumedDepositCount": {{afterRecover.ConsumedDepositCount}},
              "messageOutboxL2ToL1Count": {{afterRecover.MessageOutboxL2ToL1Count}},
              "knownForcedInclusionNonceCount": {{afterRecover.KnownForcedInclusionNonceCount}},
              "knownInboundNonceCount": {{afterRecover.KnownInboundNonceCount}},
              "rpcBatch1Status": "{{getRpcBatchStatus(1)}}",
              "rpcBatch2Status": "{{getRpcBatchStatus(2)}}",
              "eleventhWithdrawalProofPresent": true,
              "eleventhMessageProofPresent": true,
              "isSettlementRetrying": true,
              "isSettlementPoisoned": false,
              "isOfflinePassportComplete": true
            }
            """);
        Assert.IsTrue(File.Exists(durablePath));
        var durableFile = File.ReadAllText(durablePath);
        StringAssert.Contains(durableFile, "\"rpcBatch1Status\": \"Finalized\"");
        StringAssert.Contains(durableFile, "\"rpcBatch2Status\": \"Finalized\"");
        StringAssert.Contains(durableFile, "\"consumedDepositCount\": 11");
        StringAssert.Contains(durableFile, "\"messageOutboxL2ToL1Count\": 11");
        StringAssert.Contains(durableFile, "\"knownForcedInclusionNonceCount\": 11");
        StringAssert.Contains(durableFile, "\"isSettlementRetrying\": true");
        StringAssert.Contains(durableFile, "\"isSettlementPoisoned\": false");
    }

    /// <summary>
    /// SoftSeal after eleventh poison→recover: re-publish local DA for sealed batches 1+2
    /// and process a twelfth offline deposit (nonce 12, IncludedInBatch=2) while settle
    /// remains Retrying with multi-batch pending and undecuple outbox/FI known retained.
    /// Does not claim L1 deposit scan / production DA / settle.
    /// </summary>
    private static void AssertSoftSealAfterEleventhRecoverDaAndTwelfthDeposit(
        Func<DAPublishRequest, Task<DAReceipt>> publishDaAsync,
        Func<DAReceipt, Task<bool>> isDaAvailableAsync,
        Func<bool> supportsLocalDaReader,
        Func<IDAReader> createLocalDaReader,
        Func<CrossChainMessage, MintInstruction> processDeposit,
        Func<uint, ulong, bool> hasConsumedDeposit,
        Func<int> consumedDepositCount,
        Action<DepositStatus> recordRpcDeposit,
        Func<uint, ulong, DepositStatus?> getRpcL1DepositStatus,
        Func<Task<int>> scanSharedBridgeDepositsAsync,
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> exportPrometheusMetrics,
        Func<string, Task> writePrometheusMetricsAsync,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        string chainDir)
    {
        Assert.IsTrue(supportsLocalDaReader());
        var da1Payload = new byte[] { 0xDA, 0xA1, 0x01 };
        var da1 = publishDaAsync(new DAPublishRequest
        {
            ChainId = 20260716u,
            BatchNumber = 1,
            Payload = da1Payload,
        }).GetAwaiter().GetResult();
        Assert.AreEqual(DAMode.Local, da1.Layer);
        Assert.AreEqual(DAReceiptKind.LocalPersistence, da1.Kind);
        Assert.IsTrue(isDaAvailableAsync(da1).GetAwaiter().GetResult());
        var da1Read = createLocalDaReader().ReadAsync(da1).AsTask().GetAwaiter().GetResult();
        Assert.IsTrue(da1Read is { Length: 3 });
        CollectionAssert.AreEqual(da1Payload, da1Read!.Value.ToArray());

        var da2Payload = new byte[] { 0xDA, 0xA2, 0x02 };
        var da2 = publishDaAsync(new DAPublishRequest
        {
            ChainId = 20260716u,
            BatchNumber = 2,
            Payload = da2Payload,
        }).GetAwaiter().GetResult();
        Assert.AreEqual(DAMode.Local, da2.Layer);
        Assert.IsTrue(isDaAvailableAsync(da2).GetAwaiter().GetResult());
        var da2Read = createLocalDaReader().ReadAsync(da2).AsTask().GetAwaiter().GetResult();
        Assert.IsTrue(da2Read is { Length: 3 });
        CollectionAssert.AreEqual(da2Payload, da2Read!.Value.ToArray());

        // Twelfth offline mint while still Retrying after eleventh recover.
        var softL1Asset = UInt160.Parse("0x" + new string('1', 40));
        var softL2Asset = UInt160.Parse("0x" + new string('2', 40));
        var softDepositPayload = new DepositPayload
        {
            L1Asset = softL1Asset,
            L2Recipient = Account(0x55),
            Amount = new BigInteger(12_000),
        };
        var softDepositMsg = new CrossChainMessage
        {
            SourceChainId = 0,
            TargetChainId = 20260716u,
            Nonce = 12,
            Sender = Account(0x66),
            Receiver = Account(0x55),
            MessageType = MessageType.Deposit,
            Payload = softDepositPayload.Encode(),
            MessageHash = UInt256.Zero,
        };
        var softMint = processDeposit(softDepositMsg);
        Assert.AreEqual(softL2Asset, softMint.L2Asset);
        Assert.IsTrue(hasConsumedDeposit(0, 1));
        Assert.IsTrue(hasConsumedDeposit(0, 2));
        Assert.IsTrue(hasConsumedDeposit(0, 3));
        Assert.IsTrue(hasConsumedDeposit(0, 4));
        Assert.IsTrue(hasConsumedDeposit(0, 5));
        Assert.IsTrue(hasConsumedDeposit(0, 6));
        Assert.IsTrue(hasConsumedDeposit(0, 7));
        Assert.IsTrue(hasConsumedDeposit(0, 8));
        Assert.IsTrue(hasConsumedDeposit(0, 9));
        Assert.IsTrue(hasConsumedDeposit(0, 10));
        Assert.IsTrue(hasConsumedDeposit(0, 11));
        Assert.IsTrue(hasConsumedDeposit(0, 12));
        Assert.AreEqual(12, consumedDepositCount());
        recordRpcDeposit(new DepositStatus(0, 12, ConsumedOnL2: true, IncludedInBatch: 2));
        Assert.IsTrue(getRpcL1DepositStatus(0, 1) is { ConsumedOnL2: true, IncludedInBatch: 1UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 2) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 3) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 4) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 5) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 6) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 7) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 8) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 9) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 10) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 11) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 12) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.AreEqual(0, scanSharedBridgeDepositsAsync().GetAwaiter().GetResult());

        var status = getOperatorStatus();
        Assert.AreEqual(12, status.ConsumedDepositCount);
        Assert.AreEqual(11, status.MessageOutboxL2ToL1Count);
        Assert.AreEqual(11, status.KnownForcedInclusionNonceCount);
        Assert.AreEqual(11, status.KnownInboundNonceCount);
        Assert.AreEqual(2UL, status.LatestCheckpointBatchNumber);
        Assert.IsTrue(status.PendingSettlementCount >= 2);
        Assert.IsTrue(status.IsSettlementRetrying);
        Assert.IsFalse(status.IsSettlementPoisoned);
        Assert.IsFalse(status.IsSettlementIdle);
        Assert.IsTrue(status.IsOfflinePassportComplete);
        Assert.IsTrue(status.IsOperatorReady);
        Assert.IsTrue(status.IsBatcherCheckpointAligned);
        Assert.IsFalse(status.IsPipelineHealthy);
        CollectionAssert.Contains(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.IsSettlementRetrying));

        var probe = getHealthProbe();
        Assert.AreEqual(12, probe.ConsumedDepositCount);
        Assert.AreEqual(11, probe.MessageOutboxL2ToL1Count);
        Assert.AreEqual(2UL, probe.LatestCheckpointBatchNumber);
        Assert.IsTrue(probe.PendingSettlementCount >= 2);
        Assert.IsTrue(probe.IsSettlementRetrying);
        Assert.IsFalse(probe.IsSettlementPoisoned);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"consumedDepositCount\": 12");
        StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Count\": 11");
        StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": 2");
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusJson, "\"isSettlementPoisoned\": false");

        var hostProm = exportPrometheusMetrics();
        Assert.IsFalse(string.IsNullOrWhiteSpace(hostProm));
        var hostPromPath = Path.Combine(chainDir, "soft-seal-after-eleventh-recover-host.prom");
        writePrometheusMetricsAsync(hostPromPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(hostPromPath));
        Assert.AreEqual(hostProm, File.ReadAllText(hostPromPath));

        var statusPath = Path.Combine(chainDir, "soft-seal-after-eleventh-recover-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        StringAssert.Contains(File.ReadAllText(statusPath), "\"consumedDepositCount\": 12");
        StringAssert.Contains(File.ReadAllText(statusPath), "\"isSettlementRetrying\": true");

        var probePath = Path.Combine(chainDir, "soft-seal-after-eleventh-recover-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        StringAssert.Contains(File.ReadAllText(probePath), "\"consumedDepositCount\": 12");

        var durablePath = Path.Combine(chainDir, "soft-seal-after-eleventh-recover-da-deposit.json");
        File.WriteAllText(durablePath, $$"""
            {
              "daBatch1Layer": "{{da1.Layer}}",
              "daBatch2Layer": "{{da2.Layer}}",
              "daBatch1Available": true,
              "daBatch2Available": true,
              "consumedDepositCount": {{status.ConsumedDepositCount}},
              "deposit12IncludedInBatch": 2,
              "messageOutboxL2ToL1Count": {{status.MessageOutboxL2ToL1Count}},
              "knownForcedInclusionNonceCount": {{status.KnownForcedInclusionNonceCount}},
              "knownInboundNonceCount": {{status.KnownInboundNonceCount}},
              "latestCheckpointBatchNumber": {{status.LatestCheckpointBatchNumber}},
              "pendingSettlementCount": {{status.PendingSettlementCount}},
              "isSettlementRetrying": true,
              "isSettlementPoisoned": false,
              "isOfflinePassportComplete": true
            }
            """);
        Assert.IsTrue(File.Exists(durablePath));
        var durableFile = File.ReadAllText(durablePath);
        StringAssert.Contains(durableFile, "\"daBatch1Layer\": \"Local\"");
        StringAssert.Contains(durableFile, "\"daBatch2Layer\": \"Local\"");
        StringAssert.Contains(durableFile, "\"consumedDepositCount\": 12");
        StringAssert.Contains(durableFile, "\"deposit12IncludedInBatch\": 2");
        StringAssert.Contains(durableFile, "\"messageOutboxL2ToL1Count\": 11");
        StringAssert.Contains(durableFile, "\"isSettlementRetrying\": true");
        StringAssert.Contains(durableFile, "\"isOfflinePassportComplete\": true");
    }







    /// <summary>
    /// SoftSeal after seventh recover + eighth deposit: eighth withdrawal seal + L2→L1 outbox
    /// enqueue + eighth FI/inbound nonces + RPC withdrawal/message/router proofs while settle
    /// remains Retrying with multi-batch pending. Does not claim L1 claim / FI drain / settle.
    /// Returns eighth withdrawal leaf + outbound hash for follow-on pins.
    /// </summary>
    private static (UInt256 WithdrawalLeaf, UInt256 OutboundMessageHash) AssertSoftSealAfterSeventhRecoverEighthOutboundAndFi(
        Func<WithdrawalRequest, UInt256> stageWithdrawal,
        Func<int> stagedWithdrawalCount,
        Func<(UInt256 Root, WithdrawalTree Tree)> sealWithdrawalBatch,
        Func<IReadOnlyList<CrossChainMessage>, Task> enqueueOutbound,
        Func<int> messageOutboxL2ToL1Count,
        Func<UInt256> messageOutboxL2ToL1Root,
        Func<ulong, bool> registerForcedInclusionNonce,
        Func<int> knownForcedInclusionNonceCount,
        Func<bool> hasOverdueForcedInclusionCached,
        Action invalidateForcedInclusionCache,
        Func<int> openBatchForcedInclusionCount,
        Func<ulong, bool> registerInboundMessageNonce,
        Func<int> knownInboundNonceCount,
        Action invalidateInboundMessageCache,
        Func<int> openBatchL1MessageCount,
        Func<int> l1InboxPendingCount,
        Action<UInt256, byte[]> recordRpcWithdrawalProof,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcWithdrawalProof,
        Action<UInt256, byte[]> recordRpcMessageProof,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcMessageProof,
        Action<UInt256, ReadOnlyMemory<byte>> recordMessageRouterFinalizedProof,
        Func<UInt256, Task<ReadOnlyMemory<byte>?>> getMessageRouterProofAsync,
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> formatHealthProbeJson,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        string chainDir)
    {
        var softL2Asset = UInt160.Parse("0x" + new string('2', 40));
        var softSender = Account(0xcc);
        var wdLeaf = stageWithdrawal(new WithdrawalRequest
        {
            ChainId = 20260716u,
            EmittingContract = softSender,
            L2Sender = softSender,
            L1Recipient = softSender,
            L2Asset = softL2Asset,
            Amount = new BigInteger(100),
            Nonce = 8,
        });
        Assert.AreNotEqual(UInt256.Zero, wdLeaf);
        Assert.IsTrue(stagedWithdrawalCount() >= 1);
        var sealedWd = sealWithdrawalBatch();
        Assert.AreNotEqual(UInt256.Zero, sealedWd.Root);
        Assert.AreEqual(0, stagedWithdrawalCount());
        Assert.IsTrue(sealedWd.Tree.Count >= 1);
        var merkleProof = sealedWd.Tree.GetProof(sealedWd.Tree.Count - 1);
        Assert.AreEqual(wdLeaf, merkleProof.Leaf);
        var proofBytes = MerkleProofSerializer.Encode(merkleProof);
        Assert.IsTrue(proofBytes.Length >= MerkleProofSerializer.HeaderSize);
        recordRpcWithdrawalProof(wdLeaf, proofBytes);
        var storedWdProof = getRpcWithdrawalProof(wdLeaf);
        Assert.IsTrue(storedWdProof is { Length: > 0 });
        CollectionAssert.AreEqual(proofBytes, storedWdProof!.Value.ToArray());

        var outboundDraft = new CrossChainMessage
        {
            SourceChainId = 20260716u,
            TargetChainId = 0,
            Nonce = 16,
            Sender = softSender,
            Receiver = softSender,
            MessageType = MessageType.Event,
            Payload = new byte[] { 0x08 },
            MessageHash = UInt256.Zero,
        };
        var outbound = outboundDraft with { MessageHash = MessageHasher.HashMessage(outboundDraft) };
        enqueueOutbound([outbound]).GetAwaiter().GetResult();
        Assert.AreEqual(8, messageOutboxL2ToL1Count());
        Assert.AreNotEqual(UInt256.Zero, messageOutboxL2ToL1Root());

        var messageProofBytes = outbound.MessageHash.GetSpan().ToArray();
        recordRpcMessageProof(outbound.MessageHash, messageProofBytes);
        var storedMsgProof = getRpcMessageProof(outbound.MessageHash);
        Assert.IsTrue(storedMsgProof is { Length: > 0 });
        CollectionAssert.AreEqual(messageProofBytes, storedMsgProof!.Value.ToArray());
        recordMessageRouterFinalizedProof(outbound.MessageHash, messageProofBytes);
        var routerProof = getMessageRouterProofAsync(outbound.MessageHash).GetAwaiter().GetResult();
        Assert.IsTrue(routerProof is { Length: > 0 });
        CollectionAssert.AreEqual(messageProofBytes, routerProof!.Value.ToArray());

        Assert.IsTrue(registerForcedInclusionNonce(18));
        Assert.IsFalse(registerForcedInclusionNonce(18));
        Assert.AreEqual(8, knownForcedInclusionNonceCount());
        Assert.AreEqual(0, openBatchForcedInclusionCount());
        Assert.IsFalse(hasOverdueForcedInclusionCached());
        invalidateForcedInclusionCache();
        Assert.AreEqual(8, knownForcedInclusionNonceCount());

        Assert.IsTrue(registerInboundMessageNonce(18));
        Assert.IsFalse(registerInboundMessageNonce(18));
        Assert.AreEqual(8, knownInboundNonceCount());
        Assert.AreEqual(0, openBatchL1MessageCount());
        Assert.AreEqual(0, l1InboxPendingCount());
        invalidateInboundMessageCache();
        Assert.AreEqual(8, knownInboundNonceCount());

        var status = getOperatorStatus();
        Assert.AreEqual(8, status.ConsumedDepositCount);
        Assert.AreEqual(8, status.MessageOutboxL2ToL1Count);
        Assert.AreEqual(0, status.StagedWithdrawalCount);
        Assert.AreEqual(8, status.KnownForcedInclusionNonceCount);
        Assert.AreEqual(8, status.KnownInboundNonceCount);
        Assert.IsFalse(status.HasOverdueForcedInclusion);
        Assert.AreEqual(0, status.OpenBatchForcedInclusionCount);
        Assert.AreEqual(0, status.OpenBatchL1MessageCount);
        Assert.AreEqual(2UL, status.LatestCheckpointBatchNumber);
        Assert.IsTrue(status.PendingSettlementCount >= 2);
        Assert.IsTrue(status.IsSettlementRetrying);
        Assert.IsFalse(status.IsSettlementPoisoned);
        Assert.IsFalse(status.IsSettlementIdle);
        Assert.IsTrue(status.IsOfflinePassportComplete);
        Assert.IsTrue(status.IsOperatorReady);
        Assert.IsTrue(status.IsBatcherCheckpointAligned);
        Assert.IsFalse(status.IsPipelineHealthy);
        CollectionAssert.Contains(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.IsSettlementRetrying));
        CollectionAssert.DoesNotContain(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.HasOverdueForcedInclusion));

        var probe = getHealthProbe();
        Assert.AreEqual(8, probe.ConsumedDepositCount);
        Assert.AreEqual(8, probe.MessageOutboxL2ToL1Count);
        Assert.AreEqual(8, probe.KnownForcedInclusionNonceCount);
        Assert.AreEqual(8, probe.KnownInboundNonceCount);
        Assert.IsTrue(probe.IsSettlementRetrying);
        Assert.AreEqual(2UL, probe.LatestCheckpointBatchNumber);
        Assert.IsTrue(probe.PendingSettlementCount >= 2);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"consumedDepositCount\": 8");
        StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Count\": 8");
        StringAssert.Contains(statusJson, "\"knownForcedInclusionNonceCount\": 8");
        StringAssert.Contains(statusJson, "\"knownInboundNonceCount\": 8");
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": 2");

        var probeJson = formatHealthProbeJson();
        StringAssert.Contains(probeJson, "\"messageOutboxL2ToL1Count\": 8");
        StringAssert.Contains(probeJson, "\"knownForcedInclusionNonceCount\": 8");
        StringAssert.Contains(probeJson, "\"knownInboundNonceCount\": 8");
        StringAssert.Contains(probeJson, "\"isSettlementRetrying\": true");

        var statusPath = Path.Combine(chainDir, "soft-seal-after-seventh-recover-eighth-outbound-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        var statusFile = File.ReadAllText(statusPath);
        StringAssert.Contains(statusFile, "\"messageOutboxL2ToL1Count\": 8");
        StringAssert.Contains(statusFile, "\"knownForcedInclusionNonceCount\": 8");
        StringAssert.Contains(statusFile, "\"knownInboundNonceCount\": 8");
        StringAssert.Contains(statusFile, "\"consumedDepositCount\": 8");

        var probePath = Path.Combine(chainDir, "soft-seal-after-seventh-recover-eighth-outbound-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        StringAssert.Contains(File.ReadAllText(probePath), "\"messageOutboxL2ToL1Count\": 8");
        StringAssert.Contains(File.ReadAllText(probePath), "\"knownForcedInclusionNonceCount\": 8");

        var durablePath = Path.Combine(chainDir, "soft-seal-after-seventh-recover-eighth-outbound.json");
        File.WriteAllText(durablePath, $$"""
            {
              "withdrawalNonce": 8,
              "outboundNonce": 16,
              "consumedDepositCount": {{status.ConsumedDepositCount}},
              "messageOutboxL2ToL1Count": {{status.MessageOutboxL2ToL1Count}},
              "knownForcedInclusionNonceCount": {{status.KnownForcedInclusionNonceCount}},
              "knownInboundNonceCount": {{status.KnownInboundNonceCount}},
              "withdrawalLeaf": "{{wdLeaf}}",
              "withdrawalRoot": "{{sealedWd.Root}}",
              "withdrawalProofBytes": {{proofBytes.Length}},
              "outboundMessageHash": "{{outbound.MessageHash}}",
              "messageProofBytes": {{messageProofBytes.Length}},
              "latestCheckpointBatchNumber": {{status.LatestCheckpointBatchNumber}},
              "pendingSettlementCount": {{status.PendingSettlementCount}},
              "isSettlementRetrying": true,
              "isSettlementPoisoned": false,
              "isOfflinePassportComplete": true
            }
            """);
        Assert.IsTrue(File.Exists(durablePath));
        var durableFile = File.ReadAllText(durablePath);
        StringAssert.Contains(durableFile, "\"messageOutboxL2ToL1Count\": 8");
        StringAssert.Contains(durableFile, "\"knownForcedInclusionNonceCount\": 8");
        StringAssert.Contains(durableFile, "\"consumedDepositCount\": 8");
        StringAssert.Contains(durableFile, "\"isSettlementRetrying\": true");

        var rpcSurfacePath = Path.Combine(chainDir, "soft-seal-after-seventh-recover-eighth-outbound-rpc.json");
        File.WriteAllText(rpcSurfacePath, $$"""
            {
              "withdrawalLeaf": "{{wdLeaf}}",
              "outboundMessageHash": "{{outbound.MessageHash}}",
              "withdrawalProofBytes": {{proofBytes.Length}},
              "messageProofBytes": {{messageProofBytes.Length}},
              "messageOutboxL2ToL1Count": 8,
              "knownForcedInclusionNonceCount": 8,
              "knownInboundNonceCount": 8,
              "consumedDepositCount": 8,
              "isSettlementRetrying": true
            }
            """);
        Assert.IsTrue(File.Exists(rpcSurfacePath));
        var rpcSurface = File.ReadAllText(rpcSurfacePath);
        StringAssert.Contains(rpcSurface, "\"withdrawalLeaf\": \"" + wdLeaf + "\"");
        StringAssert.Contains(rpcSurface, "\"outboundMessageHash\": \"" + outbound.MessageHash + "\"");
        StringAssert.Contains(rpcSurface, "\"messageOutboxL2ToL1Count\": 8");
        StringAssert.Contains(rpcSurface, "\"knownForcedInclusionNonceCount\": 8");
        return (wdLeaf, outbound.MessageHash);
    }

    /// <summary>
    /// <summary>
    /// SoftSeal after eighth recover + ninth deposit: ninth withdrawal seal + L2→L1 outbox
    /// enqueue + eighth FI/inbound nonces + RPC withdrawal/message/router proofs while settle
    /// remains Retrying with multi-batch pending. Does not claim L1 claim / FI drain / settle.
    /// Returns ninth withdrawal leaf + outbound hash for follow-on pins.
    /// </summary>
    private static (UInt256 WithdrawalLeaf, UInt256 OutboundMessageHash) AssertSoftSealAfterEighthRecoverNinthOutboundAndFi(
        Func<WithdrawalRequest, UInt256> stageWithdrawal,
        Func<int> stagedWithdrawalCount,
        Func<(UInt256 Root, WithdrawalTree Tree)> sealWithdrawalBatch,
        Func<IReadOnlyList<CrossChainMessage>, Task> enqueueOutbound,
        Func<int> messageOutboxL2ToL1Count,
        Func<UInt256> messageOutboxL2ToL1Root,
        Func<ulong, bool> registerForcedInclusionNonce,
        Func<int> knownForcedInclusionNonceCount,
        Func<bool> hasOverdueForcedInclusionCached,
        Action invalidateForcedInclusionCache,
        Func<int> openBatchForcedInclusionCount,
        Func<ulong, bool> registerInboundMessageNonce,
        Func<int> knownInboundNonceCount,
        Action invalidateInboundMessageCache,
        Func<int> openBatchL1MessageCount,
        Func<int> l1InboxPendingCount,
        Action<UInt256, byte[]> recordRpcWithdrawalProof,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcWithdrawalProof,
        Action<UInt256, byte[]> recordRpcMessageProof,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcMessageProof,
        Action<UInt256, ReadOnlyMemory<byte>> recordMessageRouterFinalizedProof,
        Func<UInt256, Task<ReadOnlyMemory<byte>?>> getMessageRouterProofAsync,
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> formatHealthProbeJson,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        string chainDir)
    {
        var softL2Asset = UInt160.Parse("0x" + new string('2', 40));
        var softSender = Account(0xcc);
        var wdLeaf = stageWithdrawal(new WithdrawalRequest
        {
            ChainId = 20260716u,
            EmittingContract = softSender,
            L2Sender = softSender,
            L1Recipient = softSender,
            L2Asset = softL2Asset,
            Amount = new BigInteger(100),
            Nonce = 9,
        });
        Assert.AreNotEqual(UInt256.Zero, wdLeaf);
        Assert.IsTrue(stagedWithdrawalCount() >= 1);
        var sealedWd = sealWithdrawalBatch();
        Assert.AreNotEqual(UInt256.Zero, sealedWd.Root);
        Assert.AreEqual(0, stagedWithdrawalCount());
        Assert.IsTrue(sealedWd.Tree.Count >= 1);
        var merkleProof = sealedWd.Tree.GetProof(sealedWd.Tree.Count - 1);
        Assert.AreEqual(wdLeaf, merkleProof.Leaf);
        var proofBytes = MerkleProofSerializer.Encode(merkleProof);
        Assert.IsTrue(proofBytes.Length >= MerkleProofSerializer.HeaderSize);
        recordRpcWithdrawalProof(wdLeaf, proofBytes);
        var storedWdProof = getRpcWithdrawalProof(wdLeaf);
        Assert.IsTrue(storedWdProof is { Length: > 0 });
        CollectionAssert.AreEqual(proofBytes, storedWdProof!.Value.ToArray());

        var outboundDraft = new CrossChainMessage
        {
            SourceChainId = 20260716u,
            TargetChainId = 0,
            Nonce = 18,
            Sender = softSender,
            Receiver = softSender,
            MessageType = MessageType.Event,
            Payload = new byte[] { 0x09 },
            MessageHash = UInt256.Zero,
        };
        var outbound = outboundDraft with { MessageHash = MessageHasher.HashMessage(outboundDraft) };
        enqueueOutbound([outbound]).GetAwaiter().GetResult();
        Assert.AreEqual(9, messageOutboxL2ToL1Count());
        Assert.AreNotEqual(UInt256.Zero, messageOutboxL2ToL1Root());

        var messageProofBytes = outbound.MessageHash.GetSpan().ToArray();
        recordRpcMessageProof(outbound.MessageHash, messageProofBytes);
        var storedMsgProof = getRpcMessageProof(outbound.MessageHash);
        Assert.IsTrue(storedMsgProof is { Length: > 0 });
        CollectionAssert.AreEqual(messageProofBytes, storedMsgProof!.Value.ToArray());
        recordMessageRouterFinalizedProof(outbound.MessageHash, messageProofBytes);
        var routerProof = getMessageRouterProofAsync(outbound.MessageHash).GetAwaiter().GetResult();
        Assert.IsTrue(routerProof is { Length: > 0 });
        CollectionAssert.AreEqual(messageProofBytes, routerProof!.Value.ToArray());

        Assert.IsTrue(registerForcedInclusionNonce(20));
        Assert.IsFalse(registerForcedInclusionNonce(20));
        Assert.AreEqual(9, knownForcedInclusionNonceCount());
        Assert.AreEqual(0, openBatchForcedInclusionCount());
        Assert.IsFalse(hasOverdueForcedInclusionCached());
        invalidateForcedInclusionCache();
        Assert.AreEqual(9, knownForcedInclusionNonceCount());

        Assert.IsTrue(registerInboundMessageNonce(20));
        Assert.IsFalse(registerInboundMessageNonce(20));
        Assert.AreEqual(9, knownInboundNonceCount());
        Assert.AreEqual(0, openBatchL1MessageCount());
        Assert.AreEqual(0, l1InboxPendingCount());
        invalidateInboundMessageCache();
        Assert.AreEqual(9, knownInboundNonceCount());

        var status = getOperatorStatus();
        Assert.AreEqual(9, status.ConsumedDepositCount);
        Assert.AreEqual(9, status.MessageOutboxL2ToL1Count);
        Assert.AreEqual(0, status.StagedWithdrawalCount);
        Assert.AreEqual(9, status.KnownForcedInclusionNonceCount);
        Assert.AreEqual(9, status.KnownInboundNonceCount);
        Assert.IsFalse(status.HasOverdueForcedInclusion);
        Assert.AreEqual(0, status.OpenBatchForcedInclusionCount);
        Assert.AreEqual(0, status.OpenBatchL1MessageCount);
        Assert.AreEqual(2UL, status.LatestCheckpointBatchNumber);
        Assert.IsTrue(status.PendingSettlementCount >= 2);
        Assert.IsTrue(status.IsSettlementRetrying);
        Assert.IsFalse(status.IsSettlementPoisoned);
        Assert.IsFalse(status.IsSettlementIdle);
        Assert.IsTrue(status.IsOfflinePassportComplete);
        Assert.IsTrue(status.IsOperatorReady);
        Assert.IsTrue(status.IsBatcherCheckpointAligned);
        Assert.IsFalse(status.IsPipelineHealthy);
        CollectionAssert.Contains(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.IsSettlementRetrying));
        CollectionAssert.DoesNotContain(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.HasOverdueForcedInclusion));

        var probe = getHealthProbe();
        Assert.AreEqual(9, probe.ConsumedDepositCount);
        Assert.AreEqual(9, probe.MessageOutboxL2ToL1Count);
        Assert.AreEqual(9, probe.KnownForcedInclusionNonceCount);
        Assert.AreEqual(9, probe.KnownInboundNonceCount);
        Assert.IsTrue(probe.IsSettlementRetrying);
        Assert.AreEqual(2UL, probe.LatestCheckpointBatchNumber);
        Assert.IsTrue(probe.PendingSettlementCount >= 2);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"consumedDepositCount\": 9");
        StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Count\": 9");
        StringAssert.Contains(statusJson, "\"knownForcedInclusionNonceCount\": 9");
        StringAssert.Contains(statusJson, "\"knownInboundNonceCount\": 9");
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": 2");

        var probeJson = formatHealthProbeJson();
        StringAssert.Contains(probeJson, "\"messageOutboxL2ToL1Count\": 9");
        StringAssert.Contains(probeJson, "\"knownForcedInclusionNonceCount\": 9");
        StringAssert.Contains(probeJson, "\"knownInboundNonceCount\": 9");
        StringAssert.Contains(probeJson, "\"isSettlementRetrying\": true");

        var statusPath = Path.Combine(chainDir, "soft-seal-after-eighth-recover-ninth-outbound-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        var statusFile = File.ReadAllText(statusPath);
        StringAssert.Contains(statusFile, "\"messageOutboxL2ToL1Count\": 9");
        StringAssert.Contains(statusFile, "\"knownForcedInclusionNonceCount\": 9");
        StringAssert.Contains(statusFile, "\"knownInboundNonceCount\": 9");
        StringAssert.Contains(statusFile, "\"consumedDepositCount\": 9");

        var probePath = Path.Combine(chainDir, "soft-seal-after-eighth-recover-ninth-outbound-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        StringAssert.Contains(File.ReadAllText(probePath), "\"messageOutboxL2ToL1Count\": 9");
        StringAssert.Contains(File.ReadAllText(probePath), "\"knownForcedInclusionNonceCount\": 9");

        var durablePath = Path.Combine(chainDir, "soft-seal-after-eighth-recover-ninth-outbound.json");
        File.WriteAllText(durablePath, $$"""
            {
              "withdrawalNonce": 9,
              "outboundNonce": 18,
              "consumedDepositCount": {{status.ConsumedDepositCount}},
              "messageOutboxL2ToL1Count": {{status.MessageOutboxL2ToL1Count}},
              "knownForcedInclusionNonceCount": {{status.KnownForcedInclusionNonceCount}},
              "knownInboundNonceCount": {{status.KnownInboundNonceCount}},
              "withdrawalLeaf": "{{wdLeaf}}",
              "withdrawalRoot": "{{sealedWd.Root}}",
              "withdrawalProofBytes": {{proofBytes.Length}},
              "outboundMessageHash": "{{outbound.MessageHash}}",
              "messageProofBytes": {{messageProofBytes.Length}},
              "latestCheckpointBatchNumber": {{status.LatestCheckpointBatchNumber}},
              "pendingSettlementCount": {{status.PendingSettlementCount}},
              "isSettlementRetrying": true,
              "isSettlementPoisoned": false,
              "isOfflinePassportComplete": true
            }
            """);
        Assert.IsTrue(File.Exists(durablePath));
        var durableFile = File.ReadAllText(durablePath);
        StringAssert.Contains(durableFile, "\"messageOutboxL2ToL1Count\": 9");
        StringAssert.Contains(durableFile, "\"knownForcedInclusionNonceCount\": 9");
        StringAssert.Contains(durableFile, "\"consumedDepositCount\": 9");
        StringAssert.Contains(durableFile, "\"isSettlementRetrying\": true");

        var rpcSurfacePath = Path.Combine(chainDir, "soft-seal-after-eighth-recover-ninth-outbound-rpc.json");
        File.WriteAllText(rpcSurfacePath, $$"""
            {
              "withdrawalLeaf": "{{wdLeaf}}",
              "outboundMessageHash": "{{outbound.MessageHash}}",
              "withdrawalProofBytes": {{proofBytes.Length}},
              "messageProofBytes": {{messageProofBytes.Length}},
              "messageOutboxL2ToL1Count": 9,
              "knownForcedInclusionNonceCount": 9,
              "knownInboundNonceCount": 9,
              "consumedDepositCount": 9,
              "isSettlementRetrying": true
            }
            """);
        Assert.IsTrue(File.Exists(rpcSurfacePath));
        var rpcSurface = File.ReadAllText(rpcSurfacePath);
        StringAssert.Contains(rpcSurface, "\"withdrawalLeaf\": \"" + wdLeaf + "\"");
        StringAssert.Contains(rpcSurface, "\"outboundMessageHash\": \"" + outbound.MessageHash + "\"");
        StringAssert.Contains(rpcSurface, "\"messageOutboxL2ToL1Count\": 9");
        StringAssert.Contains(rpcSurface, "\"knownForcedInclusionNonceCount\": 9");
        return (wdLeaf, outbound.MessageHash);
    }

    /// <summary>
    /// SoftSeal after fourth poison→recover: re-publish local DA for sealed batches 1+2
    /// and process a fifth offline deposit (nonce 5, IncludedInBatch=2) while settle
    /// remains Retrying with multi-batch pending and quadruple outbox/FI known retained.
    /// Does not claim L1 deposit scan / production DA / settle.
    /// </summary>
    private static void AssertSoftSealAfterFifthRecoverDaAndSixthDeposit(
        Func<DAPublishRequest, Task<DAReceipt>> publishDaAsync,
        Func<DAReceipt, Task<bool>> isDaAvailableAsync,
        Func<bool> supportsLocalDaReader,
        Func<IDAReader> createLocalDaReader,
        Func<CrossChainMessage, MintInstruction> processDeposit,
        Func<uint, ulong, bool> hasConsumedDeposit,
        Func<int> consumedDepositCount,
        Action<DepositStatus> recordRpcDeposit,
        Func<uint, ulong, DepositStatus?> getRpcL1DepositStatus,
        Func<Task<int>> scanSharedBridgeDepositsAsync,
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> exportPrometheusMetrics,
        Func<string, Task> writePrometheusMetricsAsync,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        string chainDir)
    {
        Assert.IsTrue(supportsLocalDaReader());
        var da1Payload = new byte[] { 0xDA, 0xE1, 0x01 };
        var da1 = publishDaAsync(new DAPublishRequest
        {
            ChainId = 20260716u,
            BatchNumber = 1,
            Payload = da1Payload,
        }).GetAwaiter().GetResult();
        Assert.AreEqual(DAMode.Local, da1.Layer);
        Assert.AreEqual(DAReceiptKind.LocalPersistence, da1.Kind);
        Assert.IsTrue(isDaAvailableAsync(da1).GetAwaiter().GetResult());
        var da1Read = createLocalDaReader().ReadAsync(da1).AsTask().GetAwaiter().GetResult();
        Assert.IsTrue(da1Read is { Length: 3 });
        CollectionAssert.AreEqual(da1Payload, da1Read!.Value.ToArray());

        var da2Payload = new byte[] { 0xDA, 0xE2, 0x02 };
        var da2 = publishDaAsync(new DAPublishRequest
        {
            ChainId = 20260716u,
            BatchNumber = 2,
            Payload = da2Payload,
        }).GetAwaiter().GetResult();
        Assert.AreEqual(DAMode.Local, da2.Layer);
        Assert.IsTrue(isDaAvailableAsync(da2).GetAwaiter().GetResult());
        var da2Read = createLocalDaReader().ReadAsync(da2).AsTask().GetAwaiter().GetResult();
        Assert.IsTrue(da2Read is { Length: 3 });
        CollectionAssert.AreEqual(da2Payload, da2Read!.Value.ToArray());

        // Sixth offline mint while still Retrying after fifth recover.
        var softL1Asset = UInt160.Parse("0x" + new string('1', 40));
        var softL2Asset = UInt160.Parse("0x" + new string('2', 40));
        var softDepositPayload = new DepositPayload
        {
            L1Asset = softL1Asset,
            L2Recipient = Account(0x55),
            Amount = new BigInteger(6_000),
        };
        var softDepositMsg = new CrossChainMessage
        {
            SourceChainId = 0,
            TargetChainId = 20260716u,
            Nonce = 6,
            Sender = Account(0x66),
            Receiver = Account(0x55),
            MessageType = MessageType.Deposit,
            Payload = softDepositPayload.Encode(),
            MessageHash = UInt256.Zero,
        };
        var softMint = processDeposit(softDepositMsg);
        Assert.AreEqual(softL2Asset, softMint.L2Asset);
        Assert.IsTrue(hasConsumedDeposit(0, 1));
        Assert.IsTrue(hasConsumedDeposit(0, 2));
        Assert.IsTrue(hasConsumedDeposit(0, 3));
        Assert.IsTrue(hasConsumedDeposit(0, 4));
        Assert.IsTrue(hasConsumedDeposit(0, 5));
        Assert.IsTrue(hasConsumedDeposit(0, 6));
        Assert.AreEqual(6, consumedDepositCount());
        recordRpcDeposit(new DepositStatus(0, 6, ConsumedOnL2: true, IncludedInBatch: 2));
        Assert.IsTrue(getRpcL1DepositStatus(0, 1) is { ConsumedOnL2: true, IncludedInBatch: 1UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 2) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 3) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 4) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 5) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 6) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.AreEqual(0, scanSharedBridgeDepositsAsync().GetAwaiter().GetResult());

        var status = getOperatorStatus();
        Assert.AreEqual(6, status.ConsumedDepositCount);
        Assert.AreEqual(5, status.MessageOutboxL2ToL1Count);
        Assert.AreEqual(5, status.KnownForcedInclusionNonceCount);
        Assert.AreEqual(5, status.KnownInboundNonceCount);
        Assert.AreEqual(2UL, status.LatestCheckpointBatchNumber);
        Assert.IsTrue(status.PendingSettlementCount >= 2);
        Assert.IsTrue(status.IsSettlementRetrying);
        Assert.IsFalse(status.IsSettlementPoisoned);
        Assert.IsFalse(status.IsSettlementIdle);
        Assert.IsTrue(status.IsOfflinePassportComplete);
        Assert.IsTrue(status.IsOperatorReady);
        Assert.IsTrue(status.IsBatcherCheckpointAligned);
        Assert.IsFalse(status.IsPipelineHealthy);
        CollectionAssert.Contains(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.IsSettlementRetrying));

        var probe = getHealthProbe();
        Assert.AreEqual(6, probe.ConsumedDepositCount);
        Assert.AreEqual(5, probe.MessageOutboxL2ToL1Count);
        Assert.AreEqual(2UL, probe.LatestCheckpointBatchNumber);
        Assert.IsTrue(probe.PendingSettlementCount >= 2);
        Assert.IsTrue(probe.IsSettlementRetrying);
        Assert.IsFalse(probe.IsSettlementPoisoned);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"consumedDepositCount\": 6");
        StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Count\": 5");
        StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": 2");
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusJson, "\"isSettlementPoisoned\": false");

        var hostProm = exportPrometheusMetrics();
        Assert.IsFalse(string.IsNullOrWhiteSpace(hostProm));
        var hostPromPath = Path.Combine(chainDir, "soft-seal-after-fifth-recover-host.prom");
        writePrometheusMetricsAsync(hostPromPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(hostPromPath));
        Assert.AreEqual(hostProm, File.ReadAllText(hostPromPath));

        var statusPath = Path.Combine(chainDir, "soft-seal-after-fifth-recover-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        StringAssert.Contains(File.ReadAllText(statusPath), "\"consumedDepositCount\": 6");
        StringAssert.Contains(File.ReadAllText(statusPath), "\"isSettlementRetrying\": true");

        var probePath = Path.Combine(chainDir, "soft-seal-after-fifth-recover-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        StringAssert.Contains(File.ReadAllText(probePath), "\"consumedDepositCount\": 6");

        var durablePath = Path.Combine(chainDir, "soft-seal-after-fifth-recover-da-deposit.json");
        File.WriteAllText(durablePath, $$"""
            {
              "daBatch1Layer": "{{da1.Layer}}",
              "daBatch2Layer": "{{da2.Layer}}",
              "daBatch1Available": true,
              "daBatch2Available": true,
              "consumedDepositCount": {{status.ConsumedDepositCount}},
              "deposit6IncludedInBatch": 2,
              "messageOutboxL2ToL1Count": {{status.MessageOutboxL2ToL1Count}},
              "knownForcedInclusionNonceCount": {{status.KnownForcedInclusionNonceCount}},
              "knownInboundNonceCount": {{status.KnownInboundNonceCount}},
              "latestCheckpointBatchNumber": {{status.LatestCheckpointBatchNumber}},
              "pendingSettlementCount": {{status.PendingSettlementCount}},
              "isSettlementRetrying": true,
              "isSettlementPoisoned": false,
              "isOfflinePassportComplete": true
            }
            """);
        Assert.IsTrue(File.Exists(durablePath));
        var durableFile = File.ReadAllText(durablePath);
        StringAssert.Contains(durableFile, "\"daBatch1Layer\": \"Local\"");
        StringAssert.Contains(durableFile, "\"daBatch2Layer\": \"Local\"");
        StringAssert.Contains(durableFile, "\"consumedDepositCount\": 6");
        StringAssert.Contains(durableFile, "\"deposit6IncludedInBatch\": 2");
        StringAssert.Contains(durableFile, "\"messageOutboxL2ToL1Count\": 5");
        StringAssert.Contains(durableFile, "\"isSettlementRetrying\": true");
        StringAssert.Contains(durableFile, "\"isOfflinePassportComplete\": true");
    }

    /// <summary>
    /// SoftSeal after fifth recover + sixth deposit: sixth withdrawal seal + L2→L1 outbox
    /// enqueue + sixth FI/inbound nonces + RPC withdrawal/message/router proofs while settle
    /// remains Retrying with multi-batch pending. Does not claim L1 claim / FI drain / settle.
    /// Returns sixth withdrawal leaf + outbound hash for follow-on pins.
    /// </summary>
    private static (UInt256 WithdrawalLeaf, UInt256 OutboundMessageHash) AssertSoftSealAfterFifthRecoverSixthOutboundAndFi(
        Func<WithdrawalRequest, UInt256> stageWithdrawal,
        Func<int> stagedWithdrawalCount,
        Func<(UInt256 Root, WithdrawalTree Tree)> sealWithdrawalBatch,
        Func<IReadOnlyList<CrossChainMessage>, Task> enqueueOutbound,
        Func<int> messageOutboxL2ToL1Count,
        Func<UInt256> messageOutboxL2ToL1Root,
        Func<ulong, bool> registerForcedInclusionNonce,
        Func<int> knownForcedInclusionNonceCount,
        Func<bool> hasOverdueForcedInclusionCached,
        Action invalidateForcedInclusionCache,
        Func<int> openBatchForcedInclusionCount,
        Func<ulong, bool> registerInboundMessageNonce,
        Func<int> knownInboundNonceCount,
        Action invalidateInboundMessageCache,
        Func<int> openBatchL1MessageCount,
        Func<int> l1InboxPendingCount,
        Action<UInt256, byte[]> recordRpcWithdrawalProof,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcWithdrawalProof,
        Action<UInt256, byte[]> recordRpcMessageProof,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcMessageProof,
        Action<UInt256, ReadOnlyMemory<byte>> recordMessageRouterFinalizedProof,
        Func<UInt256, Task<ReadOnlyMemory<byte>?>> getMessageRouterProofAsync,
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> formatHealthProbeJson,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        string chainDir)
    {
        var softL2Asset = UInt160.Parse("0x" + new string('2', 40));
        var softSender = Account(0xaa);
        var wdLeaf = stageWithdrawal(new WithdrawalRequest
        {
            ChainId = 20260716u,
            EmittingContract = softSender,
            L2Sender = softSender,
            L1Recipient = softSender,
            L2Asset = softL2Asset,
            Amount = new BigInteger(100),
            Nonce = 6,
        });
        Assert.AreNotEqual(UInt256.Zero, wdLeaf);
        Assert.IsTrue(stagedWithdrawalCount() >= 1);
        var sealedWd = sealWithdrawalBatch();
        Assert.AreNotEqual(UInt256.Zero, sealedWd.Root);
        Assert.AreEqual(0, stagedWithdrawalCount());
        Assert.IsTrue(sealedWd.Tree.Count >= 1);
        var merkleProof = sealedWd.Tree.GetProof(sealedWd.Tree.Count - 1);
        Assert.AreEqual(wdLeaf, merkleProof.Leaf);
        var proofBytes = MerkleProofSerializer.Encode(merkleProof);
        Assert.IsTrue(proofBytes.Length >= MerkleProofSerializer.HeaderSize);
        recordRpcWithdrawalProof(wdLeaf, proofBytes);
        var storedWdProof = getRpcWithdrawalProof(wdLeaf);
        Assert.IsTrue(storedWdProof is { Length: > 0 });
        CollectionAssert.AreEqual(proofBytes, storedWdProof!.Value.ToArray());

        var outboundDraft = new CrossChainMessage
        {
            SourceChainId = 20260716u,
            TargetChainId = 0,
            Nonce = 14,
            Sender = softSender,
            Receiver = softSender,
            MessageType = MessageType.Event,
            Payload = new byte[] { 0x06 },
            MessageHash = UInt256.Zero,
        };
        var outbound = outboundDraft with { MessageHash = MessageHasher.HashMessage(outboundDraft) };
        enqueueOutbound([outbound]).GetAwaiter().GetResult();
        Assert.AreEqual(6, messageOutboxL2ToL1Count());
        Assert.AreNotEqual(UInt256.Zero, messageOutboxL2ToL1Root());

        var messageProofBytes = outbound.MessageHash.GetSpan().ToArray();
        recordRpcMessageProof(outbound.MessageHash, messageProofBytes);
        var storedMsgProof = getRpcMessageProof(outbound.MessageHash);
        Assert.IsTrue(storedMsgProof is { Length: > 0 });
        CollectionAssert.AreEqual(messageProofBytes, storedMsgProof!.Value.ToArray());
        recordMessageRouterFinalizedProof(outbound.MessageHash, messageProofBytes);
        var routerProof = getMessageRouterProofAsync(outbound.MessageHash).GetAwaiter().GetResult();
        Assert.IsTrue(routerProof is { Length: > 0 });
        CollectionAssert.AreEqual(messageProofBytes, routerProof!.Value.ToArray());

        Assert.IsTrue(registerForcedInclusionNonce(16));
        Assert.IsFalse(registerForcedInclusionNonce(16));
        Assert.AreEqual(6, knownForcedInclusionNonceCount());
        Assert.AreEqual(0, openBatchForcedInclusionCount());
        Assert.IsFalse(hasOverdueForcedInclusionCached());
        invalidateForcedInclusionCache();
        Assert.AreEqual(6, knownForcedInclusionNonceCount());

        Assert.IsTrue(registerInboundMessageNonce(16));
        Assert.IsFalse(registerInboundMessageNonce(16));
        Assert.AreEqual(6, knownInboundNonceCount());
        Assert.AreEqual(0, openBatchL1MessageCount());
        Assert.AreEqual(0, l1InboxPendingCount());
        invalidateInboundMessageCache();
        Assert.AreEqual(6, knownInboundNonceCount());

        var status = getOperatorStatus();
        Assert.AreEqual(6, status.ConsumedDepositCount);
        Assert.AreEqual(6, status.MessageOutboxL2ToL1Count);
        Assert.AreEqual(0, status.StagedWithdrawalCount);
        Assert.AreEqual(6, status.KnownForcedInclusionNonceCount);
        Assert.AreEqual(6, status.KnownInboundNonceCount);
        Assert.IsFalse(status.HasOverdueForcedInclusion);
        Assert.AreEqual(0, status.OpenBatchForcedInclusionCount);
        Assert.AreEqual(0, status.OpenBatchL1MessageCount);
        Assert.AreEqual(2UL, status.LatestCheckpointBatchNumber);
        Assert.IsTrue(status.PendingSettlementCount >= 2);
        Assert.IsTrue(status.IsSettlementRetrying);
        Assert.IsFalse(status.IsSettlementPoisoned);
        Assert.IsFalse(status.IsSettlementIdle);
        Assert.IsTrue(status.IsOfflinePassportComplete);
        Assert.IsTrue(status.IsOperatorReady);
        Assert.IsTrue(status.IsBatcherCheckpointAligned);
        Assert.IsFalse(status.IsPipelineHealthy);
        CollectionAssert.Contains(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.IsSettlementRetrying));
        CollectionAssert.DoesNotContain(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.HasOverdueForcedInclusion));

        var probe = getHealthProbe();
        Assert.AreEqual(6, probe.ConsumedDepositCount);
        Assert.AreEqual(6, probe.MessageOutboxL2ToL1Count);
        Assert.AreEqual(6, probe.KnownForcedInclusionNonceCount);
        Assert.AreEqual(6, probe.KnownInboundNonceCount);
        Assert.IsTrue(probe.IsSettlementRetrying);
        Assert.AreEqual(2UL, probe.LatestCheckpointBatchNumber);
        Assert.IsTrue(probe.PendingSettlementCount >= 2);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"consumedDepositCount\": 6");
        StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Count\": 6");
        StringAssert.Contains(statusJson, "\"knownForcedInclusionNonceCount\": 6");
        StringAssert.Contains(statusJson, "\"knownInboundNonceCount\": 6");
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": 2");

        var probeJson = formatHealthProbeJson();
        StringAssert.Contains(probeJson, "\"messageOutboxL2ToL1Count\": 6");
        StringAssert.Contains(probeJson, "\"knownForcedInclusionNonceCount\": 6");
        StringAssert.Contains(probeJson, "\"knownInboundNonceCount\": 6");
        StringAssert.Contains(probeJson, "\"isSettlementRetrying\": true");

        var statusPath = Path.Combine(chainDir, "soft-seal-after-fifth-recover-sixth-outbound-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        var statusFile = File.ReadAllText(statusPath);
        StringAssert.Contains(statusFile, "\"messageOutboxL2ToL1Count\": 6");
        StringAssert.Contains(statusFile, "\"knownForcedInclusionNonceCount\": 6");
        StringAssert.Contains(statusFile, "\"knownInboundNonceCount\": 6");
        StringAssert.Contains(statusFile, "\"consumedDepositCount\": 6");

        var probePath = Path.Combine(chainDir, "soft-seal-after-fifth-recover-sixth-outbound-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        StringAssert.Contains(File.ReadAllText(probePath), "\"messageOutboxL2ToL1Count\": 6");
        StringAssert.Contains(File.ReadAllText(probePath), "\"knownForcedInclusionNonceCount\": 6");

        var durablePath = Path.Combine(chainDir, "soft-seal-after-fifth-recover-sixth-outbound.json");
        File.WriteAllText(durablePath, $$"""
            {
              "withdrawalNonce": 6,
              "outboundNonce": 14,
              "consumedDepositCount": {{status.ConsumedDepositCount}},
              "messageOutboxL2ToL1Count": {{status.MessageOutboxL2ToL1Count}},
              "knownForcedInclusionNonceCount": {{status.KnownForcedInclusionNonceCount}},
              "knownInboundNonceCount": {{status.KnownInboundNonceCount}},
              "withdrawalLeaf": "{{wdLeaf}}",
              "withdrawalRoot": "{{sealedWd.Root}}",
              "withdrawalProofBytes": {{proofBytes.Length}},
              "outboundMessageHash": "{{outbound.MessageHash}}",
              "messageProofBytes": {{messageProofBytes.Length}},
              "latestCheckpointBatchNumber": {{status.LatestCheckpointBatchNumber}},
              "pendingSettlementCount": {{status.PendingSettlementCount}},
              "isSettlementRetrying": true,
              "isSettlementPoisoned": false,
              "isOfflinePassportComplete": true
            }
            """);
        Assert.IsTrue(File.Exists(durablePath));
        var durableFile = File.ReadAllText(durablePath);
        StringAssert.Contains(durableFile, "\"messageOutboxL2ToL1Count\": 6");
        StringAssert.Contains(durableFile, "\"knownForcedInclusionNonceCount\": 6");
        StringAssert.Contains(durableFile, "\"consumedDepositCount\": 6");
        StringAssert.Contains(durableFile, "\"isSettlementRetrying\": true");

        var rpcSurfacePath = Path.Combine(chainDir, "soft-seal-after-fifth-recover-sixth-outbound-rpc.json");
        File.WriteAllText(rpcSurfacePath, $$"""
            {
              "withdrawalLeaf": "{{wdLeaf}}",
              "outboundMessageHash": "{{outbound.MessageHash}}",
              "withdrawalProofBytes": {{proofBytes.Length}},
              "messageProofBytes": {{messageProofBytes.Length}},
              "messageOutboxL2ToL1Count": 6,
              "knownForcedInclusionNonceCount": 6,
              "knownInboundNonceCount": 6,
              "consumedDepositCount": 6,
              "isSettlementRetrying": true
            }
            """);
        Assert.IsTrue(File.Exists(rpcSurfacePath));
        var rpcSurface = File.ReadAllText(rpcSurfacePath);
        StringAssert.Contains(rpcSurface, "\"withdrawalLeaf\": \"" + wdLeaf + "\"");
        StringAssert.Contains(rpcSurface, "\"outboundMessageHash\": \"" + outbound.MessageHash + "\"");
        StringAssert.Contains(rpcSurface, "\"messageOutboxL2ToL1Count\": 6");
        StringAssert.Contains(rpcSurface, "\"knownForcedInclusionNonceCount\": 6");
        return (wdLeaf, outbound.MessageHash);
    }

    /// <summary>
    /// SoftSeal after fourth poison→recover: re-publish local DA for sealed batches 1+2
    /// and process a fifth offline deposit (nonce 5, IncludedInBatch=2) while settle
    /// remains Retrying with multi-batch pending and quadruple outbox/FI known retained.
    /// Does not claim L1 deposit scan / production DA / settle.
    /// </summary>
    private static void AssertSoftSealAfterFourthRecoverDaAndFifthDeposit(
        Func<DAPublishRequest, Task<DAReceipt>> publishDaAsync,
        Func<DAReceipt, Task<bool>> isDaAvailableAsync,
        Func<bool> supportsLocalDaReader,
        Func<IDAReader> createLocalDaReader,
        Func<CrossChainMessage, MintInstruction> processDeposit,
        Func<uint, ulong, bool> hasConsumedDeposit,
        Func<int> consumedDepositCount,
        Action<DepositStatus> recordRpcDeposit,
        Func<uint, ulong, DepositStatus?> getRpcL1DepositStatus,
        Func<Task<int>> scanSharedBridgeDepositsAsync,
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> exportPrometheusMetrics,
        Func<string, Task> writePrometheusMetricsAsync,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        string chainDir)
    {
        Assert.IsTrue(supportsLocalDaReader());
        var da1Payload = new byte[] { 0xDA, 0xD1, 0x01 };
        var da1 = publishDaAsync(new DAPublishRequest
        {
            ChainId = 20260716u,
            BatchNumber = 1,
            Payload = da1Payload,
        }).GetAwaiter().GetResult();
        Assert.AreEqual(DAMode.Local, da1.Layer);
        Assert.AreEqual(DAReceiptKind.LocalPersistence, da1.Kind);
        Assert.IsTrue(isDaAvailableAsync(da1).GetAwaiter().GetResult());
        var da1Read = createLocalDaReader().ReadAsync(da1).AsTask().GetAwaiter().GetResult();
        Assert.IsTrue(da1Read is { Length: 3 });
        CollectionAssert.AreEqual(da1Payload, da1Read!.Value.ToArray());

        var da2Payload = new byte[] { 0xDA, 0xD2, 0x02 };
        var da2 = publishDaAsync(new DAPublishRequest
        {
            ChainId = 20260716u,
            BatchNumber = 2,
            Payload = da2Payload,
        }).GetAwaiter().GetResult();
        Assert.AreEqual(DAMode.Local, da2.Layer);
        Assert.IsTrue(isDaAvailableAsync(da2).GetAwaiter().GetResult());
        var da2Read = createLocalDaReader().ReadAsync(da2).AsTask().GetAwaiter().GetResult();
        Assert.IsTrue(da2Read is { Length: 3 });
        CollectionAssert.AreEqual(da2Payload, da2Read!.Value.ToArray());

        // Fifth offline mint while still Retrying after fourth recover.
        var softL1Asset = UInt160.Parse("0x" + new string('1', 40));
        var softL2Asset = UInt160.Parse("0x" + new string('2', 40));
        var softDepositPayload = new DepositPayload
        {
            L1Asset = softL1Asset,
            L2Recipient = Account(0x55),
            Amount = new BigInteger(5_000),
        };
        var softDepositMsg = new CrossChainMessage
        {
            SourceChainId = 0,
            TargetChainId = 20260716u,
            Nonce = 5,
            Sender = Account(0x66),
            Receiver = Account(0x55),
            MessageType = MessageType.Deposit,
            Payload = softDepositPayload.Encode(),
            MessageHash = UInt256.Zero,
        };
        var softMint = processDeposit(softDepositMsg);
        Assert.AreEqual(softL2Asset, softMint.L2Asset);
        Assert.IsTrue(hasConsumedDeposit(0, 1));
        Assert.IsTrue(hasConsumedDeposit(0, 2));
        Assert.IsTrue(hasConsumedDeposit(0, 3));
        Assert.IsTrue(hasConsumedDeposit(0, 4));
        Assert.IsTrue(hasConsumedDeposit(0, 5));
        Assert.AreEqual(5, consumedDepositCount());
        recordRpcDeposit(new DepositStatus(0, 5, ConsumedOnL2: true, IncludedInBatch: 2));
        Assert.IsTrue(getRpcL1DepositStatus(0, 1) is { ConsumedOnL2: true, IncludedInBatch: 1UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 2) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 3) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 4) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 5) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.AreEqual(0, scanSharedBridgeDepositsAsync().GetAwaiter().GetResult());

        var status = getOperatorStatus();
        Assert.AreEqual(5, status.ConsumedDepositCount);
        Assert.AreEqual(4, status.MessageOutboxL2ToL1Count);
        Assert.AreEqual(4, status.KnownForcedInclusionNonceCount);
        Assert.AreEqual(4, status.KnownInboundNonceCount);
        Assert.AreEqual(2UL, status.LatestCheckpointBatchNumber);
        Assert.IsTrue(status.PendingSettlementCount >= 2);
        Assert.IsTrue(status.IsSettlementRetrying);
        Assert.IsFalse(status.IsSettlementPoisoned);
        Assert.IsFalse(status.IsSettlementIdle);
        Assert.IsTrue(status.IsOfflinePassportComplete);
        Assert.IsTrue(status.IsOperatorReady);
        Assert.IsTrue(status.IsBatcherCheckpointAligned);
        Assert.IsFalse(status.IsPipelineHealthy);
        CollectionAssert.Contains(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.IsSettlementRetrying));

        var probe = getHealthProbe();
        Assert.AreEqual(5, probe.ConsumedDepositCount);
        Assert.AreEqual(4, probe.MessageOutboxL2ToL1Count);
        Assert.AreEqual(2UL, probe.LatestCheckpointBatchNumber);
        Assert.IsTrue(probe.PendingSettlementCount >= 2);
        Assert.IsTrue(probe.IsSettlementRetrying);
        Assert.IsFalse(probe.IsSettlementPoisoned);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"consumedDepositCount\": 5");
        StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Count\": 4");
        StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": 2");
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusJson, "\"isSettlementPoisoned\": false");

        var hostProm = exportPrometheusMetrics();
        Assert.IsFalse(string.IsNullOrWhiteSpace(hostProm));
        var hostPromPath = Path.Combine(chainDir, "soft-seal-after-fourth-recover-host.prom");
        writePrometheusMetricsAsync(hostPromPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(hostPromPath));
        Assert.AreEqual(hostProm, File.ReadAllText(hostPromPath));

        var statusPath = Path.Combine(chainDir, "soft-seal-after-fourth-recover-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        StringAssert.Contains(File.ReadAllText(statusPath), "\"consumedDepositCount\": 5");
        StringAssert.Contains(File.ReadAllText(statusPath), "\"isSettlementRetrying\": true");

        var probePath = Path.Combine(chainDir, "soft-seal-after-fourth-recover-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        StringAssert.Contains(File.ReadAllText(probePath), "\"consumedDepositCount\": 5");

        var durablePath = Path.Combine(chainDir, "soft-seal-after-fourth-recover-da-deposit.json");
        File.WriteAllText(durablePath, $$"""
            {
              "daBatch1Layer": "{{da1.Layer}}",
              "daBatch2Layer": "{{da2.Layer}}",
              "daBatch1Available": true,
              "daBatch2Available": true,
              "consumedDepositCount": {{status.ConsumedDepositCount}},
              "deposit5IncludedInBatch": 2,
              "messageOutboxL2ToL1Count": {{status.MessageOutboxL2ToL1Count}},
              "knownForcedInclusionNonceCount": {{status.KnownForcedInclusionNonceCount}},
              "knownInboundNonceCount": {{status.KnownInboundNonceCount}},
              "latestCheckpointBatchNumber": {{status.LatestCheckpointBatchNumber}},
              "pendingSettlementCount": {{status.PendingSettlementCount}},
              "isSettlementRetrying": true,
              "isSettlementPoisoned": false,
              "isOfflinePassportComplete": true
            }
            """);
        Assert.IsTrue(File.Exists(durablePath));
        var durableFile = File.ReadAllText(durablePath);
        StringAssert.Contains(durableFile, "\"daBatch1Layer\": \"Local\"");
        StringAssert.Contains(durableFile, "\"daBatch2Layer\": \"Local\"");
        StringAssert.Contains(durableFile, "\"consumedDepositCount\": 5");
        StringAssert.Contains(durableFile, "\"deposit5IncludedInBatch\": 2");
        StringAssert.Contains(durableFile, "\"messageOutboxL2ToL1Count\": 4");
        StringAssert.Contains(durableFile, "\"isSettlementRetrying\": true");
        StringAssert.Contains(durableFile, "\"isOfflinePassportComplete\": true");
    }

    /// <summary>
    /// SoftSeal after fourth recover + fifth deposit: fifth withdrawal seal + L2→L1 outbox
    /// enqueue + fifth FI/inbound nonces + RPC withdrawal/message/router proofs while settle
    /// remains Retrying with multi-batch pending. Does not claim L1 claim / FI drain / settle.
    /// Returns fifth withdrawal leaf + outbound hash for follow-on pins.
    /// </summary>
    private static (UInt256 WithdrawalLeaf, UInt256 OutboundMessageHash) AssertSoftSealAfterFourthRecoverFifthOutboundAndFi(
        Func<WithdrawalRequest, UInt256> stageWithdrawal,
        Func<int> stagedWithdrawalCount,
        Func<(UInt256 Root, WithdrawalTree Tree)> sealWithdrawalBatch,
        Func<IReadOnlyList<CrossChainMessage>, Task> enqueueOutbound,
        Func<int> messageOutboxL2ToL1Count,
        Func<UInt256> messageOutboxL2ToL1Root,
        Func<ulong, bool> registerForcedInclusionNonce,
        Func<int> knownForcedInclusionNonceCount,
        Func<bool> hasOverdueForcedInclusionCached,
        Action invalidateForcedInclusionCache,
        Func<int> openBatchForcedInclusionCount,
        Func<ulong, bool> registerInboundMessageNonce,
        Func<int> knownInboundNonceCount,
        Action invalidateInboundMessageCache,
        Func<int> openBatchL1MessageCount,
        Func<int> l1InboxPendingCount,
        Action<UInt256, byte[]> recordRpcWithdrawalProof,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcWithdrawalProof,
        Action<UInt256, byte[]> recordRpcMessageProof,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcMessageProof,
        Action<UInt256, ReadOnlyMemory<byte>> recordMessageRouterFinalizedProof,
        Func<UInt256, Task<ReadOnlyMemory<byte>?>> getMessageRouterProofAsync,
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> formatHealthProbeJson,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        string chainDir)
    {
        var softL2Asset = UInt160.Parse("0x" + new string('2', 40));
        var softSender = Account(0x99);
        var wdLeaf = stageWithdrawal(new WithdrawalRequest
        {
            ChainId = 20260716u,
            EmittingContract = softSender,
            L2Sender = softSender,
            L1Recipient = softSender,
            L2Asset = softL2Asset,
            Amount = new BigInteger(100),
            Nonce = 5,
        });
        Assert.AreNotEqual(UInt256.Zero, wdLeaf);
        Assert.IsTrue(stagedWithdrawalCount() >= 1);
        var sealedWd = sealWithdrawalBatch();
        Assert.AreNotEqual(UInt256.Zero, sealedWd.Root);
        Assert.AreEqual(0, stagedWithdrawalCount());
        Assert.IsTrue(sealedWd.Tree.Count >= 1);
        var merkleProof = sealedWd.Tree.GetProof(sealedWd.Tree.Count - 1);
        Assert.AreEqual(wdLeaf, merkleProof.Leaf);
        var proofBytes = MerkleProofSerializer.Encode(merkleProof);
        Assert.IsTrue(proofBytes.Length >= MerkleProofSerializer.HeaderSize);
        recordRpcWithdrawalProof(wdLeaf, proofBytes);
        var storedWdProof = getRpcWithdrawalProof(wdLeaf);
        Assert.IsTrue(storedWdProof is { Length: > 0 });
        CollectionAssert.AreEqual(proofBytes, storedWdProof!.Value.ToArray());

        var outboundDraft = new CrossChainMessage
        {
            SourceChainId = 20260716u,
            TargetChainId = 0,
            Nonce = 13,
            Sender = softSender,
            Receiver = softSender,
            MessageType = MessageType.Event,
            Payload = new byte[] { 0x05 },
            MessageHash = UInt256.Zero,
        };
        var outbound = outboundDraft with { MessageHash = MessageHasher.HashMessage(outboundDraft) };
        enqueueOutbound([outbound]).GetAwaiter().GetResult();
        Assert.AreEqual(5, messageOutboxL2ToL1Count());
        Assert.AreNotEqual(UInt256.Zero, messageOutboxL2ToL1Root());

        var messageProofBytes = outbound.MessageHash.GetSpan().ToArray();
        recordRpcMessageProof(outbound.MessageHash, messageProofBytes);
        var storedMsgProof = getRpcMessageProof(outbound.MessageHash);
        Assert.IsTrue(storedMsgProof is { Length: > 0 });
        CollectionAssert.AreEqual(messageProofBytes, storedMsgProof!.Value.ToArray());
        recordMessageRouterFinalizedProof(outbound.MessageHash, messageProofBytes);
        var routerProof = getMessageRouterProofAsync(outbound.MessageHash).GetAwaiter().GetResult();
        Assert.IsTrue(routerProof is { Length: > 0 });
        CollectionAssert.AreEqual(messageProofBytes, routerProof!.Value.ToArray());

        Assert.IsTrue(registerForcedInclusionNonce(15));
        Assert.IsFalse(registerForcedInclusionNonce(15));
        Assert.AreEqual(5, knownForcedInclusionNonceCount());
        Assert.AreEqual(0, openBatchForcedInclusionCount());
        Assert.IsFalse(hasOverdueForcedInclusionCached());
        invalidateForcedInclusionCache();
        Assert.AreEqual(5, knownForcedInclusionNonceCount());

        Assert.IsTrue(registerInboundMessageNonce(15));
        Assert.IsFalse(registerInboundMessageNonce(15));
        Assert.AreEqual(5, knownInboundNonceCount());
        Assert.AreEqual(0, openBatchL1MessageCount());
        Assert.AreEqual(0, l1InboxPendingCount());
        invalidateInboundMessageCache();
        Assert.AreEqual(5, knownInboundNonceCount());

        var status = getOperatorStatus();
        Assert.AreEqual(5, status.ConsumedDepositCount);
        Assert.AreEqual(5, status.MessageOutboxL2ToL1Count);
        Assert.AreEqual(0, status.StagedWithdrawalCount);
        Assert.AreEqual(5, status.KnownForcedInclusionNonceCount);
        Assert.AreEqual(5, status.KnownInboundNonceCount);
        Assert.IsFalse(status.HasOverdueForcedInclusion);
        Assert.AreEqual(0, status.OpenBatchForcedInclusionCount);
        Assert.AreEqual(0, status.OpenBatchL1MessageCount);
        Assert.AreEqual(2UL, status.LatestCheckpointBatchNumber);
        Assert.IsTrue(status.PendingSettlementCount >= 2);
        Assert.IsTrue(status.IsSettlementRetrying);
        Assert.IsFalse(status.IsSettlementPoisoned);
        Assert.IsFalse(status.IsSettlementIdle);
        Assert.IsTrue(status.IsOfflinePassportComplete);
        Assert.IsTrue(status.IsOperatorReady);
        Assert.IsTrue(status.IsBatcherCheckpointAligned);
        Assert.IsFalse(status.IsPipelineHealthy);
        CollectionAssert.Contains(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.IsSettlementRetrying));
        CollectionAssert.DoesNotContain(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.HasOverdueForcedInclusion));

        var probe = getHealthProbe();
        Assert.AreEqual(5, probe.ConsumedDepositCount);
        Assert.AreEqual(5, probe.MessageOutboxL2ToL1Count);
        Assert.AreEqual(5, probe.KnownForcedInclusionNonceCount);
        Assert.AreEqual(5, probe.KnownInboundNonceCount);
        Assert.IsTrue(probe.IsSettlementRetrying);
        Assert.AreEqual(2UL, probe.LatestCheckpointBatchNumber);
        Assert.IsTrue(probe.PendingSettlementCount >= 2);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"consumedDepositCount\": 5");
        StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Count\": 5");
        StringAssert.Contains(statusJson, "\"knownForcedInclusionNonceCount\": 5");
        StringAssert.Contains(statusJson, "\"knownInboundNonceCount\": 5");
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": 2");

        var probeJson = formatHealthProbeJson();
        StringAssert.Contains(probeJson, "\"messageOutboxL2ToL1Count\": 5");
        StringAssert.Contains(probeJson, "\"knownForcedInclusionNonceCount\": 5");
        StringAssert.Contains(probeJson, "\"knownInboundNonceCount\": 5");
        StringAssert.Contains(probeJson, "\"isSettlementRetrying\": true");

        var statusPath = Path.Combine(chainDir, "soft-seal-after-fourth-recover-fifth-outbound-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        var statusFile = File.ReadAllText(statusPath);
        StringAssert.Contains(statusFile, "\"messageOutboxL2ToL1Count\": 5");
        StringAssert.Contains(statusFile, "\"knownForcedInclusionNonceCount\": 5");
        StringAssert.Contains(statusFile, "\"knownInboundNonceCount\": 5");
        StringAssert.Contains(statusFile, "\"consumedDepositCount\": 5");

        var probePath = Path.Combine(chainDir, "soft-seal-after-fourth-recover-fifth-outbound-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        StringAssert.Contains(File.ReadAllText(probePath), "\"messageOutboxL2ToL1Count\": 5");
        StringAssert.Contains(File.ReadAllText(probePath), "\"knownForcedInclusionNonceCount\": 5");

        var durablePath = Path.Combine(chainDir, "soft-seal-after-fourth-recover-fifth-outbound.json");
        File.WriteAllText(durablePath, $$"""
            {
              "withdrawalNonce": 5,
              "outboundNonce": 13,
              "consumedDepositCount": {{status.ConsumedDepositCount}},
              "messageOutboxL2ToL1Count": {{status.MessageOutboxL2ToL1Count}},
              "knownForcedInclusionNonceCount": {{status.KnownForcedInclusionNonceCount}},
              "knownInboundNonceCount": {{status.KnownInboundNonceCount}},
              "withdrawalLeaf": "{{wdLeaf}}",
              "withdrawalRoot": "{{sealedWd.Root}}",
              "withdrawalProofBytes": {{proofBytes.Length}},
              "outboundMessageHash": "{{outbound.MessageHash}}",
              "messageProofBytes": {{messageProofBytes.Length}},
              "latestCheckpointBatchNumber": {{status.LatestCheckpointBatchNumber}},
              "pendingSettlementCount": {{status.PendingSettlementCount}},
              "isSettlementRetrying": true,
              "isSettlementPoisoned": false,
              "isOfflinePassportComplete": true
            }
            """);
        Assert.IsTrue(File.Exists(durablePath));
        var durableFile = File.ReadAllText(durablePath);
        StringAssert.Contains(durableFile, "\"messageOutboxL2ToL1Count\": 5");
        StringAssert.Contains(durableFile, "\"knownForcedInclusionNonceCount\": 5");
        StringAssert.Contains(durableFile, "\"consumedDepositCount\": 5");
        StringAssert.Contains(durableFile, "\"isSettlementRetrying\": true");

        var rpcSurfacePath = Path.Combine(chainDir, "soft-seal-after-fourth-recover-fifth-outbound-rpc.json");
        File.WriteAllText(rpcSurfacePath, $$"""
            {
              "withdrawalLeaf": "{{wdLeaf}}",
              "outboundMessageHash": "{{outbound.MessageHash}}",
              "withdrawalProofBytes": {{proofBytes.Length}},
              "messageProofBytes": {{messageProofBytes.Length}},
              "messageOutboxL2ToL1Count": 5,
              "knownForcedInclusionNonceCount": 5,
              "knownInboundNonceCount": 5,
              "consumedDepositCount": 5,
              "isSettlementRetrying": true
            }
            """);
        Assert.IsTrue(File.Exists(rpcSurfacePath));
        var rpcSurface = File.ReadAllText(rpcSurfacePath);
        StringAssert.Contains(rpcSurface, "\"withdrawalLeaf\": \"" + wdLeaf + "\"");
        StringAssert.Contains(rpcSurface, "\"outboundMessageHash\": \"" + outbound.MessageHash + "\"");
        StringAssert.Contains(rpcSurface, "\"messageOutboxL2ToL1Count\": 5");
        StringAssert.Contains(rpcSurface, "\"knownForcedInclusionNonceCount\": 5");
        return (wdLeaf, outbound.MessageHash);
    }

    /// <summary>
    /// SoftSeal after third poison→recover: re-publish local DA for sealed batches 1+2
    /// and process a fourth offline deposit (nonce 4, IncludedInBatch=2) while settle
    /// remains Retrying with multi-batch pending and triple outbox/FI known retained.
    /// Does not claim L1 deposit scan / production DA / settle.
    /// </summary>
    private static void AssertSoftSealAfterThirdRecoverDaAndFourthDeposit(
        Func<DAPublishRequest, Task<DAReceipt>> publishDaAsync,
        Func<DAReceipt, Task<bool>> isDaAvailableAsync,
        Func<bool> supportsLocalDaReader,
        Func<IDAReader> createLocalDaReader,
        Func<CrossChainMessage, MintInstruction> processDeposit,
        Func<uint, ulong, bool> hasConsumedDeposit,
        Func<int> consumedDepositCount,
        Action<DepositStatus> recordRpcDeposit,
        Func<uint, ulong, DepositStatus?> getRpcL1DepositStatus,
        Func<Task<int>> scanSharedBridgeDepositsAsync,
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> exportPrometheusMetrics,
        Func<string, Task> writePrometheusMetricsAsync,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        string chainDir)
    {
        Assert.IsTrue(supportsLocalDaReader());
        var da1Payload = new byte[] { 0xDA, 0xC1, 0x01 };
        var da1 = publishDaAsync(new DAPublishRequest
        {
            ChainId = 20260716u,
            BatchNumber = 1,
            Payload = da1Payload,
        }).GetAwaiter().GetResult();
        Assert.AreEqual(DAMode.Local, da1.Layer);
        Assert.AreEqual(DAReceiptKind.LocalPersistence, da1.Kind);
        Assert.IsTrue(isDaAvailableAsync(da1).GetAwaiter().GetResult());
        var da1Read = createLocalDaReader().ReadAsync(da1).AsTask().GetAwaiter().GetResult();
        Assert.IsTrue(da1Read is { Length: 3 });
        CollectionAssert.AreEqual(da1Payload, da1Read!.Value.ToArray());

        var da2Payload = new byte[] { 0xDA, 0xC2, 0x02 };
        var da2 = publishDaAsync(new DAPublishRequest
        {
            ChainId = 20260716u,
            BatchNumber = 2,
            Payload = da2Payload,
        }).GetAwaiter().GetResult();
        Assert.AreEqual(DAMode.Local, da2.Layer);
        Assert.IsTrue(isDaAvailableAsync(da2).GetAwaiter().GetResult());
        var da2Read = createLocalDaReader().ReadAsync(da2).AsTask().GetAwaiter().GetResult();
        Assert.IsTrue(da2Read is { Length: 3 });
        CollectionAssert.AreEqual(da2Payload, da2Read!.Value.ToArray());

        // Fourth offline mint while still Retrying after third recover.
        var softL1Asset = UInt160.Parse("0x" + new string('1', 40));
        var softL2Asset = UInt160.Parse("0x" + new string('2', 40));
        var softDepositPayload = new DepositPayload
        {
            L1Asset = softL1Asset,
            L2Recipient = Account(0x55),
            Amount = new BigInteger(4_000),
        };
        var softDepositMsg = new CrossChainMessage
        {
            SourceChainId = 0,
            TargetChainId = 20260716u,
            Nonce = 4,
            Sender = Account(0x66),
            Receiver = Account(0x55),
            MessageType = MessageType.Deposit,
            Payload = softDepositPayload.Encode(),
            MessageHash = UInt256.Zero,
        };
        var softMint = processDeposit(softDepositMsg);
        Assert.AreEqual(softL2Asset, softMint.L2Asset);
        Assert.IsTrue(hasConsumedDeposit(0, 1));
        Assert.IsTrue(hasConsumedDeposit(0, 2));
        Assert.IsTrue(hasConsumedDeposit(0, 3));
        Assert.IsTrue(hasConsumedDeposit(0, 4));
        Assert.AreEqual(4, consumedDepositCount());
        recordRpcDeposit(new DepositStatus(0, 4, ConsumedOnL2: true, IncludedInBatch: 2));
        Assert.IsTrue(getRpcL1DepositStatus(0, 1) is { ConsumedOnL2: true, IncludedInBatch: 1UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 2) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 3) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(getRpcL1DepositStatus(0, 4) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.AreEqual(0, scanSharedBridgeDepositsAsync().GetAwaiter().GetResult());

        var status = getOperatorStatus();
        Assert.AreEqual(4, status.ConsumedDepositCount);
        Assert.AreEqual(3, status.MessageOutboxL2ToL1Count);
        Assert.AreEqual(3, status.KnownForcedInclusionNonceCount);
        Assert.AreEqual(3, status.KnownInboundNonceCount);
        Assert.AreEqual(2UL, status.LatestCheckpointBatchNumber);
        Assert.IsTrue(status.PendingSettlementCount >= 2);
        Assert.IsTrue(status.IsSettlementRetrying);
        Assert.IsFalse(status.IsSettlementPoisoned);
        Assert.IsFalse(status.IsSettlementIdle);
        Assert.IsTrue(status.IsOfflinePassportComplete);
        Assert.IsTrue(status.IsOperatorReady);
        Assert.IsTrue(status.IsBatcherCheckpointAligned);
        Assert.IsFalse(status.IsPipelineHealthy);
        CollectionAssert.Contains(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.IsSettlementRetrying));

        var probe = getHealthProbe();
        Assert.AreEqual(4, probe.ConsumedDepositCount);
        Assert.AreEqual(3, probe.MessageOutboxL2ToL1Count);
        Assert.AreEqual(2UL, probe.LatestCheckpointBatchNumber);
        Assert.IsTrue(probe.PendingSettlementCount >= 2);
        Assert.IsTrue(probe.IsSettlementRetrying);
        Assert.IsFalse(probe.IsSettlementPoisoned);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"consumedDepositCount\": 4");
        StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Count\": 3");
        StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": 2");
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusJson, "\"isSettlementPoisoned\": false");

        var hostProm = exportPrometheusMetrics();
        Assert.IsFalse(string.IsNullOrWhiteSpace(hostProm));
        var hostPromPath = Path.Combine(chainDir, "soft-seal-after-third-recover-host.prom");
        writePrometheusMetricsAsync(hostPromPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(hostPromPath));
        Assert.AreEqual(hostProm, File.ReadAllText(hostPromPath));

        var statusPath = Path.Combine(chainDir, "soft-seal-after-third-recover-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        StringAssert.Contains(File.ReadAllText(statusPath), "\"consumedDepositCount\": 4");
        StringAssert.Contains(File.ReadAllText(statusPath), "\"isSettlementRetrying\": true");

        var probePath = Path.Combine(chainDir, "soft-seal-after-third-recover-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        StringAssert.Contains(File.ReadAllText(probePath), "\"consumedDepositCount\": 4");

        var durablePath = Path.Combine(chainDir, "soft-seal-after-third-recover-da-deposit.json");
        File.WriteAllText(durablePath, $$"""
            {
              "daBatch1Layer": "{{da1.Layer}}",
              "daBatch2Layer": "{{da2.Layer}}",
              "daBatch1Available": true,
              "daBatch2Available": true,
              "consumedDepositCount": {{status.ConsumedDepositCount}},
              "deposit4IncludedInBatch": 2,
              "messageOutboxL2ToL1Count": {{status.MessageOutboxL2ToL1Count}},
              "knownForcedInclusionNonceCount": {{status.KnownForcedInclusionNonceCount}},
              "knownInboundNonceCount": {{status.KnownInboundNonceCount}},
              "latestCheckpointBatchNumber": {{status.LatestCheckpointBatchNumber}},
              "pendingSettlementCount": {{status.PendingSettlementCount}},
              "isSettlementRetrying": true,
              "isSettlementPoisoned": false,
              "isOfflinePassportComplete": true
            }
            """);
        Assert.IsTrue(File.Exists(durablePath));
        var durableFile = File.ReadAllText(durablePath);
        StringAssert.Contains(durableFile, "\"daBatch1Layer\": \"Local\"");
        StringAssert.Contains(durableFile, "\"daBatch2Layer\": \"Local\"");
        StringAssert.Contains(durableFile, "\"consumedDepositCount\": 4");
        StringAssert.Contains(durableFile, "\"deposit4IncludedInBatch\": 2");
        StringAssert.Contains(durableFile, "\"messageOutboxL2ToL1Count\": 3");
        StringAssert.Contains(durableFile, "\"isSettlementRetrying\": true");
        StringAssert.Contains(durableFile, "\"isOfflinePassportComplete\": true");
    }

    /// <summary>
    /// SoftSeal after third recover + fourth deposit: fourth withdrawal seal + L2→L1 outbox
    /// enqueue + fourth FI/inbound nonces + RPC withdrawal/message/router proofs while settle
    /// remains Retrying with multi-batch pending. Does not claim L1 claim / FI drain / settle.
    /// Returns fourth withdrawal leaf + outbound hash for follow-on pins.
    /// </summary>
    private static (UInt256 WithdrawalLeaf, UInt256 OutboundMessageHash) AssertSoftSealAfterThirdRecoverFourthOutboundAndFi(
        Func<WithdrawalRequest, UInt256> stageWithdrawal,
        Func<int> stagedWithdrawalCount,
        Func<(UInt256 Root, WithdrawalTree Tree)> sealWithdrawalBatch,
        Func<IReadOnlyList<CrossChainMessage>, Task> enqueueOutbound,
        Func<int> messageOutboxL2ToL1Count,
        Func<UInt256> messageOutboxL2ToL1Root,
        Func<ulong, bool> registerForcedInclusionNonce,
        Func<int> knownForcedInclusionNonceCount,
        Func<bool> hasOverdueForcedInclusionCached,
        Action invalidateForcedInclusionCache,
        Func<int> openBatchForcedInclusionCount,
        Func<ulong, bool> registerInboundMessageNonce,
        Func<int> knownInboundNonceCount,
        Action invalidateInboundMessageCache,
        Func<int> openBatchL1MessageCount,
        Func<int> l1InboxPendingCount,
        Action<UInt256, byte[]> recordRpcWithdrawalProof,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcWithdrawalProof,
        Action<UInt256, byte[]> recordRpcMessageProof,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcMessageProof,
        Action<UInt256, ReadOnlyMemory<byte>> recordMessageRouterFinalizedProof,
        Func<UInt256, Task<ReadOnlyMemory<byte>?>> getMessageRouterProofAsync,
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> formatHealthProbeJson,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        string chainDir)
    {
        var softL2Asset = UInt160.Parse("0x" + new string('2', 40));
        var softSender = Account(0x88);
        var wdLeaf = stageWithdrawal(new WithdrawalRequest
        {
            ChainId = 20260716u,
            EmittingContract = softSender,
            L2Sender = softSender,
            L1Recipient = softSender,
            L2Asset = softL2Asset,
            Amount = new BigInteger(100),
            Nonce = 4,
        });
        Assert.AreNotEqual(UInt256.Zero, wdLeaf);
        Assert.IsTrue(stagedWithdrawalCount() >= 1);
        var sealedWd = sealWithdrawalBatch();
        Assert.AreNotEqual(UInt256.Zero, sealedWd.Root);
        Assert.AreEqual(0, stagedWithdrawalCount());
        Assert.IsTrue(sealedWd.Tree.Count >= 1);
        var merkleProof = sealedWd.Tree.GetProof(sealedWd.Tree.Count - 1);
        Assert.AreEqual(wdLeaf, merkleProof.Leaf);
        var proofBytes = MerkleProofSerializer.Encode(merkleProof);
        Assert.IsTrue(proofBytes.Length >= MerkleProofSerializer.HeaderSize);
        recordRpcWithdrawalProof(wdLeaf, proofBytes);
        var storedWdProof = getRpcWithdrawalProof(wdLeaf);
        Assert.IsTrue(storedWdProof is { Length: > 0 });
        CollectionAssert.AreEqual(proofBytes, storedWdProof!.Value.ToArray());

        var outboundDraft = new CrossChainMessage
        {
            SourceChainId = 20260716u,
            TargetChainId = 0,
            Nonce = 12,
            Sender = softSender,
            Receiver = softSender,
            MessageType = MessageType.Event,
            Payload = new byte[] { 0x04 },
            MessageHash = UInt256.Zero,
        };
        var outbound = outboundDraft with { MessageHash = MessageHasher.HashMessage(outboundDraft) };
        enqueueOutbound([outbound]).GetAwaiter().GetResult();
        Assert.AreEqual(4, messageOutboxL2ToL1Count());
        Assert.AreNotEqual(UInt256.Zero, messageOutboxL2ToL1Root());

        var messageProofBytes = outbound.MessageHash.GetSpan().ToArray();
        recordRpcMessageProof(outbound.MessageHash, messageProofBytes);
        var storedMsgProof = getRpcMessageProof(outbound.MessageHash);
        Assert.IsTrue(storedMsgProof is { Length: > 0 });
        CollectionAssert.AreEqual(messageProofBytes, storedMsgProof!.Value.ToArray());
        recordMessageRouterFinalizedProof(outbound.MessageHash, messageProofBytes);
        var routerProof = getMessageRouterProofAsync(outbound.MessageHash).GetAwaiter().GetResult();
        Assert.IsTrue(routerProof is { Length: > 0 });
        CollectionAssert.AreEqual(messageProofBytes, routerProof!.Value.ToArray());

        Assert.IsTrue(registerForcedInclusionNonce(14));
        Assert.IsFalse(registerForcedInclusionNonce(14));
        Assert.AreEqual(4, knownForcedInclusionNonceCount());
        Assert.AreEqual(0, openBatchForcedInclusionCount());
        Assert.IsFalse(hasOverdueForcedInclusionCached());
        invalidateForcedInclusionCache();
        Assert.AreEqual(4, knownForcedInclusionNonceCount());

        Assert.IsTrue(registerInboundMessageNonce(14));
        Assert.IsFalse(registerInboundMessageNonce(14));
        Assert.AreEqual(4, knownInboundNonceCount());
        Assert.AreEqual(0, openBatchL1MessageCount());
        Assert.AreEqual(0, l1InboxPendingCount());
        invalidateInboundMessageCache();
        Assert.AreEqual(4, knownInboundNonceCount());

        var status = getOperatorStatus();
        Assert.AreEqual(4, status.ConsumedDepositCount);
        Assert.AreEqual(4, status.MessageOutboxL2ToL1Count);
        Assert.AreEqual(0, status.StagedWithdrawalCount);
        Assert.AreEqual(4, status.KnownForcedInclusionNonceCount);
        Assert.AreEqual(4, status.KnownInboundNonceCount);
        Assert.IsFalse(status.HasOverdueForcedInclusion);
        Assert.AreEqual(0, status.OpenBatchForcedInclusionCount);
        Assert.AreEqual(0, status.OpenBatchL1MessageCount);
        Assert.AreEqual(2UL, status.LatestCheckpointBatchNumber);
        Assert.IsTrue(status.PendingSettlementCount >= 2);
        Assert.IsTrue(status.IsSettlementRetrying);
        Assert.IsFalse(status.IsSettlementPoisoned);
        Assert.IsFalse(status.IsSettlementIdle);
        Assert.IsTrue(status.IsOfflinePassportComplete);
        Assert.IsTrue(status.IsOperatorReady);
        Assert.IsTrue(status.IsBatcherCheckpointAligned);
        Assert.IsFalse(status.IsPipelineHealthy);
        CollectionAssert.Contains(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.IsSettlementRetrying));
        CollectionAssert.DoesNotContain(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.HasOverdueForcedInclusion));

        var probe = getHealthProbe();
        Assert.AreEqual(4, probe.ConsumedDepositCount);
        Assert.AreEqual(4, probe.MessageOutboxL2ToL1Count);
        Assert.AreEqual(4, probe.KnownForcedInclusionNonceCount);
        Assert.AreEqual(4, probe.KnownInboundNonceCount);
        Assert.IsTrue(probe.IsSettlementRetrying);
        Assert.AreEqual(2UL, probe.LatestCheckpointBatchNumber);
        Assert.IsTrue(probe.PendingSettlementCount >= 2);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"consumedDepositCount\": 4");
        StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Count\": 4");
        StringAssert.Contains(statusJson, "\"knownForcedInclusionNonceCount\": 4");
        StringAssert.Contains(statusJson, "\"knownInboundNonceCount\": 4");
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": 2");

        var probeJson = formatHealthProbeJson();
        StringAssert.Contains(probeJson, "\"messageOutboxL2ToL1Count\": 4");
        StringAssert.Contains(probeJson, "\"knownForcedInclusionNonceCount\": 4");
        StringAssert.Contains(probeJson, "\"knownInboundNonceCount\": 4");
        StringAssert.Contains(probeJson, "\"isSettlementRetrying\": true");

        var statusPath = Path.Combine(chainDir, "soft-seal-after-third-recover-fourth-outbound-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        var statusFile = File.ReadAllText(statusPath);
        StringAssert.Contains(statusFile, "\"messageOutboxL2ToL1Count\": 4");
        StringAssert.Contains(statusFile, "\"knownForcedInclusionNonceCount\": 4");
        StringAssert.Contains(statusFile, "\"knownInboundNonceCount\": 4");
        StringAssert.Contains(statusFile, "\"consumedDepositCount\": 4");

        var probePath = Path.Combine(chainDir, "soft-seal-after-third-recover-fourth-outbound-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        StringAssert.Contains(File.ReadAllText(probePath), "\"messageOutboxL2ToL1Count\": 4");
        StringAssert.Contains(File.ReadAllText(probePath), "\"knownForcedInclusionNonceCount\": 4");

        var durablePath = Path.Combine(chainDir, "soft-seal-after-third-recover-fourth-outbound.json");
        File.WriteAllText(durablePath, $$"""
            {
              "withdrawalNonce": 4,
              "outboundNonce": 12,
              "consumedDepositCount": {{status.ConsumedDepositCount}},
              "messageOutboxL2ToL1Count": {{status.MessageOutboxL2ToL1Count}},
              "knownForcedInclusionNonceCount": {{status.KnownForcedInclusionNonceCount}},
              "knownInboundNonceCount": {{status.KnownInboundNonceCount}},
              "withdrawalLeaf": "{{wdLeaf}}",
              "withdrawalRoot": "{{sealedWd.Root}}",
              "withdrawalProofBytes": {{proofBytes.Length}},
              "outboundMessageHash": "{{outbound.MessageHash}}",
              "messageProofBytes": {{messageProofBytes.Length}},
              "latestCheckpointBatchNumber": {{status.LatestCheckpointBatchNumber}},
              "pendingSettlementCount": {{status.PendingSettlementCount}},
              "isSettlementRetrying": true,
              "isSettlementPoisoned": false,
              "isOfflinePassportComplete": true
            }
            """);
        Assert.IsTrue(File.Exists(durablePath));
        var durableFile = File.ReadAllText(durablePath);
        StringAssert.Contains(durableFile, "\"messageOutboxL2ToL1Count\": 4");
        StringAssert.Contains(durableFile, "\"knownForcedInclusionNonceCount\": 4");
        StringAssert.Contains(durableFile, "\"consumedDepositCount\": 4");
        StringAssert.Contains(durableFile, "\"isSettlementRetrying\": true");

        var rpcSurfacePath = Path.Combine(chainDir, "soft-seal-after-third-recover-fourth-outbound-rpc.json");
        File.WriteAllText(rpcSurfacePath, $$"""
            {
              "withdrawalLeaf": "{{wdLeaf}}",
              "outboundMessageHash": "{{outbound.MessageHash}}",
              "withdrawalProofBytes": {{proofBytes.Length}},
              "messageProofBytes": {{messageProofBytes.Length}},
              "messageOutboxL2ToL1Count": 4,
              "knownForcedInclusionNonceCount": 4,
              "knownInboundNonceCount": 4,
              "consumedDepositCount": 4,
              "isSettlementRetrying": true
            }
            """);
        Assert.IsTrue(File.Exists(rpcSurfacePath));
        var rpcSurface = File.ReadAllText(rpcSurfacePath);
        StringAssert.Contains(rpcSurface, "\"withdrawalLeaf\": \"" + wdLeaf + "\"");
        StringAssert.Contains(rpcSurface, "\"outboundMessageHash\": \"" + outbound.MessageHash + "\"");
        StringAssert.Contains(rpcSurface, "\"messageOutboxL2ToL1Count\": 4");
        StringAssert.Contains(rpcSurface, "\"knownForcedInclusionNonceCount\": 4");
        return (wdLeaf, outbound.MessageHash);
    }

    /// <summary>
    /// SoftSeal path: FI + inbound nonce bookkeeping while settlement is still Retrying.
    /// Open-batch counts stay 0 (sealed), overdue remains false, pipeline unhealthy from
    /// IsSettlementRetrying (not FI/inbound). L1 FI/message drain remain funded gates.
    /// </summary>
    private static void AssertSoftSealFiInboundWhileRetrying(
        Func<ulong, bool> registerForcedInclusionNonce,
        Func<int> knownForcedInclusionNonceCount,
        Func<bool> hasBatchForcedInclusionSource,
        Func<bool> hasOverdueForcedInclusionCached,
        Action invalidateForcedInclusionCache,
        Func<int> openBatchForcedInclusionCount,
        Func<ulong, bool> registerInboundMessageNonce,
        Func<int> knownInboundNonceCount,
        Func<bool> hasBatchMessageRouter,
        Func<bool> hasMessageRouter,
        Func<bool> isMessagePipelineWiringComplete,
        Action invalidateInboundMessageCache,
        Func<int> openBatchL1MessageCount,
        Func<int> l1InboxPendingCount,
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> formatHealthProbeJson,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        string chainDir,
        ulong softNonce)
    {
        Assert.IsTrue(hasBatchForcedInclusionSource());
        Assert.IsTrue(registerForcedInclusionNonce(softNonce));
        Assert.IsFalse(registerForcedInclusionNonce(softNonce));
        Assert.AreEqual(1, knownForcedInclusionNonceCount());
        Assert.AreEqual(0, openBatchForcedInclusionCount());
        Assert.IsFalse(hasOverdueForcedInclusionCached());
        invalidateForcedInclusionCache();
        Assert.AreEqual(1, knownForcedInclusionNonceCount());
        Assert.IsFalse(hasOverdueForcedInclusionCached());

        Assert.IsTrue(hasBatchMessageRouter());
        Assert.IsTrue(hasMessageRouter());
        Assert.IsTrue(isMessagePipelineWiringComplete());
        Assert.IsTrue(registerInboundMessageNonce(softNonce));
        Assert.IsFalse(registerInboundMessageNonce(softNonce));
        Assert.AreEqual(1, knownInboundNonceCount());
        Assert.AreEqual(0, openBatchL1MessageCount());
        Assert.AreEqual(0, l1InboxPendingCount());
        invalidateInboundMessageCache();
        Assert.AreEqual(1, knownInboundNonceCount());
        Assert.AreEqual(0, l1InboxPendingCount());

        var status = getOperatorStatus();
        Assert.AreEqual(1, status.KnownForcedInclusionNonceCount);
        Assert.AreEqual(1, status.KnownInboundNonceCount);
        Assert.IsTrue(status.HasBatchForcedInclusionSource);
        Assert.IsTrue(status.HasBatchMessageRouter);
        Assert.IsTrue(status.IsForcedInclusionPipelineWiringComplete);
        Assert.IsTrue(status.IsMessagePipelineWiringComplete);
        Assert.IsFalse(status.HasOverdueForcedInclusion);
        Assert.AreEqual(0, status.OpenBatchForcedInclusionCount);
        Assert.AreEqual(0, status.OpenBatchL1MessageCount);
        Assert.AreEqual(0, status.L1InboxPendingCount);
        Assert.IsFalse(status.HasOpenBatch);
        Assert.IsTrue(status.IsSettlementRetrying);
        Assert.IsFalse(status.IsSettlementIdle);
        Assert.IsFalse(status.IsPipelineHealthy);
        Assert.IsTrue(status.IsOfflinePassportComplete);
        Assert.IsTrue(status.IsOperatorReady);
        CollectionAssert.Contains(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.IsSettlementRetrying));
        CollectionAssert.DoesNotContain(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.HasOverdueForcedInclusion));

        var probe = getHealthProbe();
        Assert.AreEqual(1, probe.KnownForcedInclusionNonceCount);
        Assert.AreEqual(1, probe.KnownInboundNonceCount);
        Assert.IsFalse(probe.HasOverdueForcedInclusion);
        Assert.AreEqual(0, probe.OpenBatchForcedInclusionCount);
        Assert.AreEqual(0, probe.OpenBatchL1MessageCount);
        Assert.IsTrue(probe.IsSettlementRetrying);
        Assert.IsFalse(probe.IsPipelineHealthy);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"knownForcedInclusionNonceCount\": 1");
        StringAssert.Contains(statusJson, "\"knownInboundNonceCount\": 1");
        StringAssert.Contains(statusJson, "\"hasOverdueForcedInclusion\": false");
        StringAssert.Contains(statusJson, "\"openBatchForcedInclusionCount\": 0");
        StringAssert.Contains(statusJson, "\"openBatchL1MessageCount\": 0");
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusJson, "\"isPipelineHealthy\": false");
        StringAssert.Contains(statusJson, "IsSettlementRetrying");

        var probeJson = formatHealthProbeJson();
        StringAssert.Contains(probeJson, "\"knownForcedInclusionNonceCount\": 1");
        StringAssert.Contains(probeJson, "\"knownInboundNonceCount\": 1");
        StringAssert.Contains(probeJson, "\"isSettlementRetrying\": true");

        var statusPath = Path.Combine(chainDir, "soft-seal-fi-inbound-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        var statusFile = File.ReadAllText(statusPath);
        StringAssert.Contains(statusFile, "\"knownForcedInclusionNonceCount\": 1");
        StringAssert.Contains(statusFile, "\"knownInboundNonceCount\": 1");
        StringAssert.Contains(statusFile, "\"isSettlementRetrying\": true");

        var probePath = Path.Combine(chainDir, "soft-seal-fi-inbound-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        var probeFile = File.ReadAllText(probePath);
        StringAssert.Contains(probeFile, "\"knownForcedInclusionNonceCount\": 1");
        StringAssert.Contains(probeFile, "\"knownInboundNonceCount\": 1");
        StringAssert.Contains(probeFile, "\"isSettlementRetrying\": true");
    }

    /// <summary>
    /// SoftSeal path: offline mint/withdrawal/outbox + RPC store while settlement is still
    /// Retrying after mock L1 fail. Pins deposit/outbox counts without requiring settlement
    /// idle. L1 deposit scan / claim / settle remain funded gates.
    /// </summary>
    private static void AssertSoftSealOfflineBridgeWhileRetrying(
        Action<AssetMapping> registerBridgeAsset,
        Func<CrossChainMessage, MintInstruction> processDeposit,
        Func<uint, ulong, bool> hasConsumedDeposit,
        Func<int> consumedDepositCount,
        Func<int, IReadOnlyList<MintInstruction>> processReadyDeposits,
        Func<WithdrawalRequest, UInt256> stageWithdrawal,
        Func<int> stagedWithdrawalCount,
        Func<(UInt256 Root, WithdrawalTree Tree)> sealWithdrawalBatch,
        Func<IReadOnlyList<CrossChainMessage>, Task> enqueueOutbound,
        Func<int> messageOutboxL2ToL1Count,
        Func<UInt256> messageOutboxL2ToL1Root,
        Action<DepositStatus> recordRpcDeposit,
        Func<uint, ulong, DepositStatus?> getRpcL1DepositStatus,
        Action<UInt160, UInt160> registerRpcAsset,
        Func<UInt160, UInt160?> getRpcBridgedAsset,
        Func<UInt160, UInt160?> getRpcCanonicalAsset,
        Action<UInt256, byte[]> recordRpcWithdrawalProof,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcWithdrawalProof,
        Action<UInt256, byte[]> recordRpcMessageProof,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcMessageProof,
        Action<UInt256, ReadOnlyMemory<byte>> recordMessageRouterFinalizedProof,
        Func<UInt256, Task<ReadOnlyMemory<byte>?>> getMessageRouterProofAsync,
        Func<Task<int>> scanSharedBridgeDepositsAsync,
        Func<Task<IReadOnlyList<MintInstruction>>> scanAndProcessReadyDepositsAsync,
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> formatHealthProbeJson,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        ulong sealedBatchNumber,
        string chainDir)
    {
        var offlineBridge = AssertOfflineBridgeMintWithdrawalOutbox(
            registerBridgeAsset,
            processDeposit,
            hasConsumedDeposit,
            consumedDepositCount,
            processReadyDeposits,
            stageWithdrawal,
            stagedWithdrawalCount,
            sealWithdrawalBatch,
            enqueueOutbound,
            messageOutboxL2ToL1Count,
            messageOutboxL2ToL1Root);
        AssertOfflineBridgeRpcStoreSurface(
            offlineBridge,
            recordRpcDeposit,
            getRpcL1DepositStatus,
            registerRpcAsset,
            getRpcBridgedAsset,
            getRpcCanonicalAsset,
            recordRpcWithdrawalProof,
            getRpcWithdrawalProof,
            recordRpcMessageProof,
            getRpcMessageProof,
            recordMessageRouterFinalizedProof,
            getMessageRouterProofAsync,
            chainDir,
            includedInBatch: sealedBatchNumber);

        // Soft scan remains empty without L1 deposit discovery.
        Assert.AreEqual(0, scanSharedBridgeDepositsAsync().GetAwaiter().GetResult());
        Assert.AreEqual(0, scanAndProcessReadyDepositsAsync().GetAwaiter().GetResult().Count);

        var status = getOperatorStatus();
        Assert.AreEqual(1, status.ConsumedDepositCount);
        Assert.AreEqual(1, status.MessageOutboxL2ToL1Count);
        Assert.AreEqual(0, status.StagedWithdrawalCount);
        Assert.IsTrue(status.HasMessageOutbox);
        Assert.AreNotEqual(UInt256.Zero, status.MessageOutboxL2ToL1Root);
        Assert.IsTrue(status.IsOfflinePassportComplete);
        Assert.AreEqual(0, status.OfflinePassportFailures.Count);
        // SoftSeal path: still waiting on funded L1 settle.
        Assert.IsTrue(status.IsSettlementRetrying);
        Assert.IsFalse(status.IsSettlementIdle);
        Assert.IsFalse(status.IsSettlementPoisoned);
        Assert.IsTrue(status.PendingSettlementCount >= 1);
        Assert.IsTrue(status.IsOperatorReady);
        Assert.IsFalse(status.IsPipelineHealthy);
        CollectionAssert.Contains(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.IsSettlementRetrying));

        var probe = getHealthProbe();
        Assert.AreEqual(1, probe.ConsumedDepositCount);
        Assert.AreEqual(1, probe.MessageOutboxL2ToL1Count);
        Assert.AreEqual(0, probe.StagedWithdrawalCount);
        Assert.IsTrue(probe.IsSettlementRetrying);
        Assert.IsFalse(probe.IsSettlementIdle);
        Assert.IsTrue(probe.IsOfflinePassportComplete);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"consumedDepositCount\": 1");
        StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Count\": 1");
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusJson, "\"isSettlementIdle\": false");
        StringAssert.Contains(statusJson, "IsSettlementRetrying");

        var probeJson = formatHealthProbeJson();
        StringAssert.Contains(probeJson, "\"consumedDepositCount\": 1");
        StringAssert.Contains(probeJson, "\"messageOutboxL2ToL1Count\": 1");
        StringAssert.Contains(probeJson, "\"isSettlementRetrying\": true");

        var statusPath = Path.Combine(chainDir, "soft-seal-offline-bridge-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        var statusFile = File.ReadAllText(statusPath);
        StringAssert.Contains(statusFile, "\"consumedDepositCount\": 1");
        StringAssert.Contains(statusFile, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusFile, "\"messageOutboxL2ToL1Count\": 1");

        var probePath = Path.Combine(chainDir, "soft-seal-offline-bridge-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        var probeFile = File.ReadAllText(probePath);
        StringAssert.Contains(probeFile, "\"consumedDepositCount\": 1");
        StringAssert.Contains(probeFile, "\"isSettlementRetrying\": true");
    }

    /// <summary>
    /// Soft local DA after Multisig/Optimistic SoftSeal checkpoint: publish sealed batch
    /// payload, pin availability + local reader round-trip, durable soft-seal-da-surface.json.
    /// Production DA credentials / L1 DA remain funded gates (Zk SoftSeal still funded).
    /// </summary>
    private static void AssertSoftSealLocalDaSurface(
        Func<DAPublishRequest, Task<DAReceipt>> publishDaAsync,
        Func<DAReceipt, Task<bool>> isDaAvailableAsync,
        Func<bool> supportsLocalDaReader,
        Func<IDAReader> createLocalDaReader,
        ulong batchNumber,
        string chainDir)
    {
        Assert.IsTrue(supportsLocalDaReader());
        var softDaPayload = new byte[] { 0xDA, 0x51, (byte)batchNumber };
        var softDaReceipt = publishDaAsync(new DAPublishRequest
        {
            ChainId = 20260716u,
            BatchNumber = batchNumber,
            Payload = softDaPayload,
        }).GetAwaiter().GetResult();
        Assert.AreEqual(DAMode.Local, softDaReceipt.Layer);
        Assert.AreEqual(DAReceiptKind.LocalPersistence, softDaReceipt.Kind);
        Assert.IsFalse(softDaReceipt.Commitment.Equals(UInt256.Zero));
        Assert.IsTrue(isDaAvailableAsync(softDaReceipt).GetAwaiter().GetResult());
        var softDaReader = createLocalDaReader();
        Assert.IsNotNull(softDaReader);
        var softDaRead = softDaReader.ReadAsync(softDaReceipt).AsTask().GetAwaiter().GetResult();
        Assert.IsTrue(softDaRead is { Length: 3 });
        CollectionAssert.AreEqual(softDaPayload, softDaRead!.Value.ToArray());

        var softDaPath = Path.Combine(chainDir, "soft-seal-da-surface.json");
        File.WriteAllText(softDaPath, $$"""
            {
              "batchNumber": {{batchNumber}},
              "layer": "{{softDaReceipt.Layer}}",
              "kind": "{{softDaReceipt.Kind}}",
              "commitment": "{{softDaReceipt.Commitment}}",
              "payloadBytes": {{softDaPayload.Length}},
              "supportsLocalDaReader": true
            }
            """);
        Assert.IsTrue(File.Exists(softDaPath));
        var softDaFile = File.ReadAllText(softDaPath);
        StringAssert.Contains(softDaFile, "\"supportsLocalDaReader\": true");
        StringAssert.Contains(softDaFile, "\"batchNumber\": " + batchNumber);
        StringAssert.Contains(softDaFile, "\"layer\": \"Local\"");
    }

    /// <summary>
    /// After soft seal checkpoint, pin RPC store + dual-chain Gateway ReceiveBatch
    /// (no L1 PublishAggregate). Commitment roots follow soft seal executor post-state.
    /// When <paramref name="secondCheckpoint"/> is set (SoftSeal multi-batch pending path),
    /// also RPC-finalize batch 2 and ReceiveBatch it so aggregator backlog covers both
    /// sealed batches while host settle remains unsettled.
    /// </summary>
    private static void AssertSoftSealFeedsGatewayReceiveBatch(
        Action<L2BatchCommitment, BatchStatus> addRpcBatch,
        Action<ulong> finalizeRpcBatch,
        Func<ulong, L2BatchCommitment?> getRpcBatch,
        Func<UInt256> getLatestRpcStateRoot,
        SealedBatchCheckpoint checkpoint,
        string chainDir,
        L2MetricsPlugin metricsPlugin,
        ProofType proofType,
        UInt160 gatewaySignerAccount,
        SealedBatchCheckpoint? secondCheckpoint = null,
        Func<ulong, UInt256>? getRpcStateRootAtBatch = null)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(finalizeRpcBatch);
        ArgumentNullException.ThrowIfNull(getRpcBatch);
        ArgumentNullException.ThrowIfNull(getLatestRpcStateRoot);
        var z = UInt256.Zero;
        var genesisRoot = UInt256.Parse("0x" + new string('1', 64));
        var commitment = new L2BatchCommitment
        {
            ChainId = 20260716u,
            BatchNumber = checkpoint.BatchNumber,
            FirstBlock = 1,
            LastBlock = checkpoint.LastBlock,
            PreStateRoot = genesisRoot,
            PostStateRoot = checkpoint.PostStateRoot,
            TxRoot = z,
            ReceiptRoot = z,
            WithdrawalRoot = z,
            L2ToL1MessageRoot = z,
            L2ToL2MessageRoot = z,
            DACommitment = z,
            PublicInputHash = z,
            ProofType = proofType,
            Proof = new byte[] { 0xB1 },
        };
        addRpcBatch(commitment, BatchStatus.Finalized);
        var rpcBatch = getRpcBatch(checkpoint.BatchNumber);
        Assert.IsNotNull(rpcBatch);
        Assert.AreEqual(checkpoint.PostStateRoot, rpcBatch!.PostStateRoot);
        // Latest tip advances only via Finalize (status Finalized alone is not enough).
        finalizeRpcBatch(checkpoint.BatchNumber);
        Assert.AreEqual(checkpoint.PostStateRoot, getLatestRpcStateRoot());
        if (getRpcStateRootAtBatch is not null)
            Assert.AreEqual(checkpoint.PostStateRoot, getRpcStateRootAtBatch(checkpoint.BatchNumber));

        L2BatchCommitment? commitment2 = null;
        if (secondCheckpoint is not null)
        {
            Assert.AreEqual(2UL, secondCheckpoint.BatchNumber);
            Assert.AreEqual(checkpoint.PostStateRoot, secondCheckpoint.PostStateRoot);
            commitment2 = new L2BatchCommitment
            {
                ChainId = 20260716u,
                BatchNumber = secondCheckpoint.BatchNumber,
                FirstBlock = 2,
                LastBlock = secondCheckpoint.LastBlock,
                PreStateRoot = checkpoint.PostStateRoot,
                PostStateRoot = secondCheckpoint.PostStateRoot,
                TxRoot = z,
                ReceiptRoot = z,
                WithdrawalRoot = z,
                L2ToL1MessageRoot = z,
                L2ToL2MessageRoot = z,
                DACommitment = z,
                PublicInputHash = z,
                ProofType = proofType,
                Proof = new byte[] { 0xB4 },
            };
            addRpcBatch(commitment2, BatchStatus.Finalized);
            var rpcBatch2 = getRpcBatch(2);
            Assert.IsNotNull(rpcBatch2);
            Assert.AreEqual(2UL, rpcBatch2!.BatchNumber);
            Assert.AreEqual(secondCheckpoint.PostStateRoot, rpcBatch2.PostStateRoot);
            finalizeRpcBatch(2);
            Assert.AreEqual(secondCheckpoint.PostStateRoot, getLatestRpcStateRoot());
            if (getRpcStateRootAtBatch is not null)
            {
                Assert.AreEqual(checkpoint.PostStateRoot, getRpcStateRootAtBatch(1));
                Assert.AreEqual(secondCheckpoint.PostStateRoot, getRpcStateRootAtBatch(2));
            }
        }

        var gatewayProof = new DelegatingGatewayProofProver(
            proofSystem: 1,
            aggregationBackendId: MerklePathRoundProver.ConstBackendId,
            proofFactory: static (_, _, _) =>
                ValueTask.FromResult<ReadOnlyMemory<byte>>(new byte[] { 0xC1 }));
        using var gatewayHost = GatewayHostComposition.OpenMerkle(
            chainDir,
            gatewayProof,
            new StubSigner(gatewaySignerAccount),
            UInt256.Parse("0x" + new string('d', 64)),
            UInt256.Parse("0x" + new string('e', 64)),
            metricsPlugin: metricsPlugin);

        Assert.IsTrue(gatewayHost.HasMetricsPlugin);
        Assert.IsTrue(gatewayHost.IsOfflinePassportComplete);
        // Dual-chain constituents so aggregator has multi-chain pending input.
        gatewayHost.ReceiveBatch(commitment with
        {
            ChainId = 20260717u,
            L2ToL2MessageRoot = Root(0xAC),
            Proof = new byte[] { 0xB2 },
        });
        gatewayHost.ReceiveBatch(commitment with { Proof = new byte[] { 0xB3 } });
        Assert.IsTrue(gatewayHost.AggregatorPendingCount >= 1);
        var pendingAfterBatch1 = gatewayHost.AggregatorPendingCount;
        Assert.AreEqual(gatewayHost.Aggregator.PendingCount, gatewayHost.AggregatorPendingCount);

        if (commitment2 is not null)
        {
            // Multi-batch soft path: feed sealed batch 2 into the same aggregator (no L1 publish).
            gatewayHost.ReceiveBatch(commitment2 with
            {
                ChainId = 20260717u,
                L2ToL2MessageRoot = Root(0xAD),
                Proof = new byte[] { 0xB5 },
            });
            gatewayHost.ReceiveBatch(commitment2 with { Proof = new byte[] { 0xB6 } });
            Assert.IsTrue(gatewayHost.AggregatorPendingCount > pendingAfterBatch1);
            Assert.IsTrue(gatewayHost.AggregatorPendingCount >= 4);
            Assert.AreEqual(gatewayHost.Aggregator.PendingCount, gatewayHost.AggregatorPendingCount);
        }

        // Durable outbox path: direct PullAggregate bypasses publication outbox (fail-closed).
        Assert.ThrowsExactly<InvalidOperationException>(() => gatewayHost.PullAggregate());
        var gwStatus = gatewayHost.GetOperatorStatus();
        Assert.AreEqual(gatewayHost.AggregatorPendingCount, gwStatus.AggregatorPendingCount);
        // Soft aggregate pending is not L1 outbox publication, but publication health flags the backlog.
        Assert.IsTrue(gatewayHost.IsOfflinePassportComplete);
        Assert.IsFalse(gatewayHost.HasPendingPublication);
        Assert.IsFalse(gatewayHost.IsOutboxIdle);
        Assert.IsFalse(gatewayHost.IsOutboxPoisoned);
        Assert.IsFalse(gatewayHost.IsPublicationHealthy);
        CollectionAssert.Contains(
            gatewayHost.PublicationHealthFailures.ToArray(),
            nameof(gatewayHost.AggregatorPendingCount));
        Assert.IsFalse(gatewayHost.IsGatewayHostHealthy);
        CollectionAssert.Contains(
            gatewayHost.GatewayHostHealthFailures.ToArray(),
            nameof(gatewayHost.AggregatorPendingCount));
        Assert.IsFalse(gwStatus.HasPendingPublication);
        Assert.IsFalse(gwStatus.IsOutboxIdle);
        Assert.IsFalse(gwStatus.IsPublicationHealthy);
        Assert.IsFalse(gwStatus.IsGatewayHostHealthy);
        var gwProbe = gatewayHost.GetHealthProbe();
        Assert.AreEqual(gatewayHost.AggregatorPendingCount, gwProbe.AggregatorPendingCount);
        Assert.IsFalse(gwProbe.HasPendingPublication);
        Assert.IsFalse(gwProbe.IsPublicationHealthy);
        Assert.IsFalse(gwProbe.IsOutboxIdle);
        Assert.IsTrue(gwProbe.IsOfflinePassportComplete);
        CollectionAssert.Contains(gwProbe.PublicationHealthFailures.ToArray(), "AggregatorPendingCount");
        var gwJson = gatewayHost.FormatOperatorStatusJson();
        StringAssert.Contains(gwJson, "\"aggregatorPendingCount\":");
        StringAssert.Contains(gwJson, "\"hasPendingPublication\": false");
        StringAssert.Contains(gwJson, "\"isPublicationHealthy\": false");
        StringAssert.Contains(gwJson, "\"isOutboxIdle\": false");
        StringAssert.Contains(gwJson, "\"isOfflinePassportComplete\": true");
        StringAssert.Contains(gwJson, "AggregatorPendingCount");
        var gwStatusPath = Path.Combine(chainDir, "soft-seal-gateway-status.json");
        gatewayHost.WriteOperatorStatusAsync(gwStatusPath).AsTask().GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(gwStatusPath));
        Assert.AreEqual(gwJson, File.ReadAllText(gwStatusPath));
        var gwProbeJson = gatewayHost.FormatHealthProbeJson();
        StringAssert.Contains(gwProbeJson, "\"aggregatorPendingCount\":");
        StringAssert.Contains(gwProbeJson, "\"hasPendingPublication\": false");
        StringAssert.Contains(gwProbeJson, "\"isPublicationHealthy\": false");
        StringAssert.Contains(gwProbeJson, "\"isOutboxIdle\": false");
        StringAssert.Contains(gwProbeJson, "AggregatorPendingCount");
        var gwProbePath = Path.Combine(chainDir, "soft-seal-gateway-probe.json");
        gatewayHost.WriteHealthProbeAsync(gwProbePath).AsTask().GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(gwProbePath));
        Assert.AreEqual(gwProbeJson, File.ReadAllText(gwProbePath));
        // Soft-seal gateway metrics scrape (shared host metrics plugin when supplied).
        var gwProm = gatewayHost.ExportPrometheusMetrics();
        Assert.IsFalse(string.IsNullOrWhiteSpace(gwProm));
        var gwPromPath = Path.Combine(chainDir, "soft-seal-gateway.prom");
        gatewayHost.WritePrometheusMetricsAsync(gwPromPath).AsTask().GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(gwPromPath));
        Assert.AreEqual(gwProm, File.ReadAllText(gwPromPath));

        if (secondCheckpoint is not null)
        {
            // Durable multi-batch soft surface (operator scripts without metrics HTTP).
            var multiPath = Path.Combine(chainDir, "soft-seal-multi-batch-rpc-gateway.json");
            File.WriteAllText(multiPath, $$"""
                {
                  "batch1": {{checkpoint.BatchNumber}},
                  "batch2": {{secondCheckpoint.BatchNumber}},
                  "latestRpcStateRoot": "{{getLatestRpcStateRoot()}}",
                  "aggregatorPendingCount": {{gatewayHost.AggregatorPendingCount}},
                  "hasPendingPublication": false,
                  "pullAggregateFailClosed": true
                }
                """);
            Assert.IsTrue(File.Exists(multiPath));
            var multiFile = File.ReadAllText(multiPath);
            StringAssert.Contains(multiFile, "\"batch1\": 1");
            StringAssert.Contains(multiFile, "\"batch2\": 2");
            StringAssert.Contains(multiFile, "\"aggregatorPendingCount\": " + gatewayHost.AggregatorPendingCount);
            StringAssert.Contains(multiFile, "\"hasPendingPublication\": false");
        }
        // PublishAggregateAsync remains a funded L1 publication gate.
    }

    /// <summary>
    /// Soft <c>ProcessCommittedBlock</c> open-batch when MaxBlocksPerBatch &gt; 1 (no seal / no
    /// PersistAsync). Shared by Multisig/Optimistic/Zk E2E host compositions.
    /// When MaxBlocksPerBatch &gt; 2, also pins a second empty block still does not seal.
    /// </summary>
    private static void AssertSoftOpenBatchNoSeal(
        int maxBlocksPerBatch,
        Action<uint, ulong, uint, IEnumerable<byte[]>> processCommittedBlock,
        Func<bool> hasOpenBatch,
        Func<int> openBatchBlockCount,
        Func<int> openBatchL1MessageCount,
        Func<bool> hasPendingSealedBatch,
        Func<ulong?> nextExpectedBlock,
        Func<ulong> nextBatchNumber)
    {
        Assert.IsTrue(maxBlocksPerBatch > 1);
        var openBatchTimestampMs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        processCommittedBlock(1, openBatchTimestampMs, 894710606, Array.Empty<byte[]>());
        Assert.IsTrue(hasOpenBatch());
        Assert.AreEqual(1, openBatchBlockCount());
        Assert.AreEqual(0, openBatchL1MessageCount());
        Assert.IsFalse(hasPendingSealedBatch());
        Assert.AreEqual(2UL, nextExpectedBlock());
        Assert.AreEqual(1UL, nextBatchNumber());
        // Second empty block still under MaxBlocksPerBatch: open batch grows, no seal/PersistAsync.
        if (maxBlocksPerBatch > 2)
        {
            processCommittedBlock(2, openBatchTimestampMs + 1, 894710606, Array.Empty<byte[]>());
            Assert.IsTrue(hasOpenBatch());
            Assert.AreEqual(2, openBatchBlockCount());
            Assert.AreEqual(0, openBatchL1MessageCount());
            Assert.IsFalse(hasPendingSealedBatch());
            Assert.AreEqual(3UL, nextExpectedBlock());
            Assert.AreEqual(1UL, nextBatchNumber());
        }
    }

    /// <summary>
    /// Soft open-batch operator surface: passport complete, settlement idle, pipeline healthy
    /// (open batch under max age), durable status/probe files with <c>hasOpenBatch</c>.
    /// Does not claim seal or L1 settle.
    /// </summary>
    private static void AssertSoftOpenBatchOperatorSurface(
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> formatHealthProbeJson,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        string chainDir,
        int expectedOpenBatchBlockCount)
    {
        var status = getOperatorStatus();
        Assert.IsTrue(status.HasOpenBatch);
        Assert.AreEqual(expectedOpenBatchBlockCount, status.OpenBatchBlockCount);
        Assert.IsFalse(status.HasPendingSealedBatch);
        Assert.IsTrue(status.IsSettlementIdle);
        Assert.IsFalse(status.IsSettlementPoisoned);
        Assert.IsFalse(status.IsSettlementRetrying);
        Assert.IsFalse(status.IsOpenBatchPastMaxAge);
        Assert.IsNotNull(status.OpenBatchAgeMillis);
        Assert.IsTrue(status.IsOfflinePassportComplete);
        Assert.AreEqual(0, status.OfflinePassportFailures.Count);
        Assert.IsTrue(status.IsPipelineHealthy);
        Assert.AreEqual(0, status.PipelineHealthFailures.Count);
        Assert.AreEqual(0, status.PendingSettlementCount);
        Assert.IsTrue(status.IsOperatorReady);
        Assert.IsTrue(status.IsBatcherCheckpointAligned);

        var probe = getHealthProbe();
        Assert.IsTrue(probe.HasOpenBatch);
        Assert.AreEqual(expectedOpenBatchBlockCount, probe.OpenBatchBlockCount);
        Assert.IsFalse(probe.HasPendingSealedBatch);
        Assert.IsTrue(probe.IsSettlementIdle);
        Assert.IsFalse(probe.IsOpenBatchPastMaxAge);
        Assert.IsNotNull(probe.OpenBatchAgeMillis);
        Assert.IsTrue(probe.IsOfflinePassportComplete);
        Assert.IsTrue(probe.IsPipelineHealthy);
        Assert.AreEqual(0, probe.PipelineHealthFailures.Count);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"hasOpenBatch\": true");
        StringAssert.Contains(statusJson, "\"isPipelineHealthy\": true");
        StringAssert.Contains(statusJson, "\"isSettlementIdle\": true");
        StringAssert.Contains(statusJson, "\"isOpenBatchPastMaxAge\": false");
        StringAssert.Contains(statusJson, "\"isOfflinePassportComplete\": true");
        StringAssert.Contains(statusJson, "\"pendingSettlementCount\": 0");
        StringAssert.Contains(
            statusJson,
            "\"openBatchBlockCount\": " + expectedOpenBatchBlockCount);

        var probeJson = formatHealthProbeJson();
        StringAssert.Contains(probeJson, "\"hasOpenBatch\": true");
        StringAssert.Contains(probeJson, "\"isPipelineHealthy\": true");
        StringAssert.Contains(probeJson, "\"isSettlementIdle\": true");
        StringAssert.Contains(probeJson, "\"isOpenBatchPastMaxAge\": false");

        var statusPath = Path.Combine(chainDir, "soft-open-batch-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        var statusFile = File.ReadAllText(statusPath);
        StringAssert.Contains(statusFile, "\"hasOpenBatch\": true");
        StringAssert.Contains(statusFile, "\"isPipelineHealthy\": true");
        StringAssert.Contains(statusFile, "\"isSettlementIdle\": true");

        var probePath = Path.Combine(chainDir, "soft-open-batch-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        var probeFile = File.ReadAllText(probePath);
        StringAssert.Contains(probeFile, "\"hasOpenBatch\": true");
        StringAssert.Contains(probeFile, "\"isPipelineHealthy\": true");
    }

    /// <summary>
    /// Soft ForcedInclusion ops surface: register a known L1 nonce offline (no scan),
    /// pin cache/status/probe, open-batch FI count stays 0 without L1 drain entries,
    /// overdue remains false without cached getEntry deadlines. L1 FI drain/finalization
    /// remain funded gates.
    /// </summary>
    private static void AssertSoftForcedInclusionOperatorSurface(
        Func<ulong, bool> registerForcedInclusionNonce,
        Func<int> knownForcedInclusionNonceCount,
        Func<bool> hasBatchForcedInclusionSource,
        Func<bool> hasOverdueForcedInclusionCached,
        Action invalidateForcedInclusionCache,
        Func<int> openBatchForcedInclusionCount,
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> formatHealthProbeJson,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        string chainDir,
        ulong softNonce)
    {
        Assert.IsTrue(hasBatchForcedInclusionSource());
        Assert.IsTrue(registerForcedInclusionNonce(softNonce));
        Assert.IsFalse(registerForcedInclusionNonce(softNonce));
        Assert.AreEqual(1, knownForcedInclusionNonceCount());
        // Soft nonce bookkeeping only — FI txs enter open batches via L1 drain, not Register alone.
        Assert.AreEqual(0, openBatchForcedInclusionCount());
        Assert.IsFalse(hasOverdueForcedInclusionCached());

        invalidateForcedInclusionCache();
        Assert.AreEqual(1, knownForcedInclusionNonceCount());
        Assert.IsFalse(hasOverdueForcedInclusionCached());

        var status = getOperatorStatus();
        Assert.AreEqual(1, status.KnownForcedInclusionNonceCount);
        Assert.IsTrue(status.HasBatchForcedInclusionSource);
        Assert.IsTrue(status.IsForcedInclusionPipelineWiringComplete);
        Assert.IsFalse(status.HasOverdueForcedInclusion);
        Assert.AreEqual(0, status.OpenBatchForcedInclusionCount);
        Assert.IsTrue(status.IsPipelineHealthy);
        Assert.IsTrue(status.IsOfflinePassportComplete);
        Assert.IsTrue(status.IsOperatorReady);
        CollectionAssert.DoesNotContain(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.HasOverdueForcedInclusion));

        var probe = getHealthProbe();
        Assert.AreEqual(1, probe.KnownForcedInclusionNonceCount);
        Assert.IsTrue(probe.HasBatchForcedInclusionSource);
        Assert.IsTrue(probe.IsForcedInclusionPipelineWiringComplete);
        Assert.IsFalse(probe.HasOverdueForcedInclusion);
        Assert.AreEqual(0, probe.OpenBatchForcedInclusionCount);
        Assert.IsTrue(probe.IsPipelineHealthy);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"knownForcedInclusionNonceCount\": 1");
        StringAssert.Contains(statusJson, "\"hasBatchForcedInclusionSource\": true");
        StringAssert.Contains(statusJson, "\"isForcedInclusionPipelineWiringComplete\": true");
        StringAssert.Contains(statusJson, "\"hasOverdueForcedInclusion\": false");
        StringAssert.Contains(statusJson, "\"openBatchForcedInclusionCount\": 0");
        StringAssert.Contains(statusJson, "\"isPipelineHealthy\": true");

        var probeJson = formatHealthProbeJson();
        StringAssert.Contains(probeJson, "\"knownForcedInclusionNonceCount\": 1");
        StringAssert.Contains(probeJson, "\"hasOverdueForcedInclusion\": false");
        StringAssert.Contains(probeJson, "\"openBatchForcedInclusionCount\": 0");

        var statusPath = Path.Combine(chainDir, "soft-fi-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        var statusFile = File.ReadAllText(statusPath);
        StringAssert.Contains(statusFile, "\"knownForcedInclusionNonceCount\": 1");
        StringAssert.Contains(statusFile, "\"hasOverdueForcedInclusion\": false");
        StringAssert.Contains(statusFile, "\"hasBatchForcedInclusionSource\": true");

        var probePath = Path.Combine(chainDir, "soft-fi-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        var probeFile = File.ReadAllText(probePath);
        StringAssert.Contains(probeFile, "\"knownForcedInclusionNonceCount\": 1");
        StringAssert.Contains(probeFile, "\"hasOverdueForcedInclusion\": false");
    }

    /// <summary>
    /// Soft inbound L1 message ops surface: register a known inbound nonce offline (no scan),
    /// pin cache/status/probe, open-batch L1 message count stays 0 without L1 drain entries,
    /// L1InboxPendingCount stays 0. L1 message scan/inclusion remain funded gates.
    /// </summary>
    private static void AssertSoftInboundMessageOperatorSurface(
        Func<ulong, bool> registerInboundMessageNonce,
        Func<int> knownInboundNonceCount,
        Func<bool> hasBatchMessageRouter,
        Func<bool> hasMessageRouter,
        Func<bool> isMessagePipelineWiringComplete,
        Action invalidateInboundMessageCache,
        Func<int> openBatchL1MessageCount,
        Func<int> l1InboxPendingCount,
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> formatHealthProbeJson,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        string chainDir,
        ulong softNonce)
    {
        Assert.IsTrue(hasBatchMessageRouter());
        Assert.IsTrue(hasMessageRouter());
        Assert.IsTrue(isMessagePipelineWiringComplete());
        Assert.IsTrue(registerInboundMessageNonce(softNonce));
        Assert.IsFalse(registerInboundMessageNonce(softNonce));
        Assert.AreEqual(1, knownInboundNonceCount());
        // Soft nonce bookkeeping only — L1 messages enter open batches via drain, not Register alone.
        Assert.AreEqual(0, openBatchL1MessageCount());
        Assert.AreEqual(0, l1InboxPendingCount());

        invalidateInboundMessageCache();
        Assert.AreEqual(1, knownInboundNonceCount());
        Assert.AreEqual(0, l1InboxPendingCount());

        var status = getOperatorStatus();
        Assert.AreEqual(1, status.KnownInboundNonceCount);
        Assert.IsTrue(status.HasBatchMessageRouter);
        Assert.IsTrue(status.HasMessageRouter);
        Assert.IsTrue(status.IsMessagePipelineWiringComplete);
        Assert.AreEqual(0, status.OpenBatchL1MessageCount);
        Assert.AreEqual(0, status.L1InboxPendingCount);
        Assert.IsTrue(status.IsPipelineHealthy);
        Assert.IsTrue(status.IsOfflinePassportComplete);
        Assert.IsTrue(status.IsOperatorReady);

        var probe = getHealthProbe();
        Assert.AreEqual(1, probe.KnownInboundNonceCount);
        Assert.IsTrue(probe.HasBatchMessageRouter);
        Assert.IsTrue(probe.IsMessagePipelineWiringComplete);
        Assert.AreEqual(0, probe.OpenBatchL1MessageCount);
        Assert.AreEqual(0, probe.L1InboxPendingCount);
        Assert.IsTrue(probe.IsPipelineHealthy);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"knownInboundNonceCount\": 1");
        StringAssert.Contains(statusJson, "\"hasBatchMessageRouter\": true");
        StringAssert.Contains(statusJson, "\"hasMessageRouter\": true");
        StringAssert.Contains(statusJson, "\"isMessagePipelineWiringComplete\": true");
        StringAssert.Contains(statusJson, "\"openBatchL1MessageCount\": 0");
        StringAssert.Contains(statusJson, "\"l1InboxPendingCount\": 0");
        StringAssert.Contains(statusJson, "\"isPipelineHealthy\": true");

        var probeJson = formatHealthProbeJson();
        StringAssert.Contains(probeJson, "\"knownInboundNonceCount\": 1");
        StringAssert.Contains(probeJson, "\"openBatchL1MessageCount\": 0");
        StringAssert.Contains(probeJson, "\"l1InboxPendingCount\": 0");

        var statusPath = Path.Combine(chainDir, "soft-inbound-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        var statusFile = File.ReadAllText(statusPath);
        StringAssert.Contains(statusFile, "\"knownInboundNonceCount\": 1");
        StringAssert.Contains(statusFile, "\"hasBatchMessageRouter\": true");
        StringAssert.Contains(statusFile, "\"isMessagePipelineWiringComplete\": true");

        var probePath = Path.Combine(chainDir, "soft-inbound-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        var probeFile = File.ReadAllText(probePath);
        StringAssert.Contains(probeFile, "\"knownInboundNonceCount\": 1");
        StringAssert.Contains(probeFile, "\"l1InboxPendingCount\": 0");
    }

    /// <summary>
    /// Soft LocalHost RPC store finalize + dual-chain Gateway ReceiveBatch (no L1 publish).
    /// Shared by Multisig/Optimistic/Zk E2E host+gateway compositions. Pins SoftSeal-parity
    /// durable-outbox ops: PullAggregate fail-closed, AggregatorPendingCount publication backlog,
    /// status/probe/prom durable files.
    /// </summary>
    private static void AssertSoftRpcStoreAndGatewayReceiveBatch(
        Action<L2BatchCommitment, BatchStatus> addRpcBatch,
        Action<ulong> finalizeRpcBatch,
        Func<ulong, BatchStatus> getRpcBatchStatus,
        Func<UInt256> getLatestRpcStateRoot,
        Action<DepositStatus> recordRpcDeposit,
        Func<uint, ulong, DepositStatus?> getRpcL1DepositStatus,
        GatewayHostComposition gatewayHost,
        string chainDir,
        ProofType proofType,
        UInt256 softRoot)
    {
        ArgumentNullException.ThrowIfNull(gatewayHost);
        var softZ = UInt256.Zero;
        var softBatch = new L2BatchCommitment
        {
            ChainId = 20260716u,
            BatchNumber = 1,
            FirstBlock = 1,
            LastBlock = 1,
            PreStateRoot = softZ,
            PostStateRoot = softRoot,
            TxRoot = softZ,
            ReceiptRoot = softZ,
            WithdrawalRoot = softZ,
            L2ToL1MessageRoot = softZ,
            L2ToL2MessageRoot = softZ,
            DACommitment = softZ,
            PublicInputHash = softZ,
            ProofType = proofType,
            Proof = ReadOnlyMemory<byte>.Empty,
        };
        addRpcBatch(softBatch, BatchStatus.Pending);
        finalizeRpcBatch(1);
        Assert.AreEqual(BatchStatus.Finalized, getRpcBatchStatus(1));
        Assert.AreEqual(softRoot, getLatestRpcStateRoot());
        recordRpcDeposit(new DepositStatus(20260716u, 1, ConsumedOnL2: false, IncludedInBatch: null));
        Assert.IsNotNull(getRpcL1DepositStatus(20260716u, 1));
        // Dual-chain constituents so aggregator has multi-chain pending input (soft local only).
        gatewayHost.ReceiveBatch(softBatch with
        {
            ChainId = 20260717u,
            L2ToL2MessageRoot = Root(0xAC),
            Proof = new byte[] { 0xA1 },
        });
        gatewayHost.ReceiveBatch(softBatch with
        {
            Proof = new byte[] { 0xA2 },
        });
        Assert.IsTrue(gatewayHost.AggregatorPendingCount >= 1);
        Assert.AreEqual(gatewayHost.Aggregator.PendingCount, gatewayHost.AggregatorPendingCount);
        var gwStatus = gatewayHost.GetOperatorStatus();
        Assert.AreEqual(gatewayHost.AggregatorPendingCount, gwStatus.AggregatorPendingCount);
        // Durable outbox path: direct PullAggregate bypasses publication outbox (fail-closed).
        Assert.ThrowsExactly<InvalidOperationException>(() => gatewayHost.PullAggregate());
        // Soft aggregate pending is not L1 outbox publication, but publication health flags the backlog.
        Assert.IsTrue(gatewayHost.IsOfflinePassportComplete);
        Assert.IsFalse(gatewayHost.HasPendingPublication);
        Assert.IsFalse(gatewayHost.IsOutboxIdle);
        Assert.IsFalse(gatewayHost.IsOutboxPoisoned);
        Assert.IsFalse(gatewayHost.IsPublicationHealthy);
        CollectionAssert.Contains(
            gatewayHost.PublicationHealthFailures.ToArray(),
            nameof(gatewayHost.AggregatorPendingCount));
        Assert.IsFalse(gatewayHost.IsGatewayHostHealthy);
        CollectionAssert.Contains(
            gatewayHost.GatewayHostHealthFailures.ToArray(),
            nameof(gatewayHost.AggregatorPendingCount));
        Assert.IsFalse(gwStatus.HasPendingPublication);
        Assert.IsFalse(gwStatus.IsOutboxIdle);
        Assert.IsFalse(gwStatus.IsPublicationHealthy);
        Assert.IsFalse(gwStatus.IsGatewayHostHealthy);
        var gwProbe = gatewayHost.GetHealthProbe();
        Assert.AreEqual(gatewayHost.AggregatorPendingCount, gwProbe.AggregatorPendingCount);
        Assert.IsFalse(gwProbe.HasPendingPublication);
        Assert.IsFalse(gwProbe.IsPublicationHealthy);
        Assert.IsFalse(gwProbe.IsOutboxIdle);
        Assert.IsTrue(gwProbe.IsOfflinePassportComplete);
        CollectionAssert.Contains(gwProbe.PublicationHealthFailures.ToArray(), "AggregatorPendingCount");
        var gwJson = gatewayHost.FormatOperatorStatusJson();
        StringAssert.Contains(gwJson, "\"aggregatorPendingCount\":");
        StringAssert.Contains(gwJson, "\"hasPendingPublication\": false");
        StringAssert.Contains(gwJson, "\"isPublicationHealthy\": false");
        StringAssert.Contains(gwJson, "\"isOutboxIdle\": false");
        StringAssert.Contains(gwJson, "\"isOfflinePassportComplete\": true");
        StringAssert.Contains(gwJson, "AggregatorPendingCount");
        var gwStatusPath = Path.Combine(chainDir, "soft-rpc-gateway-status.json");
        gatewayHost.WriteOperatorStatusAsync(gwStatusPath).AsTask().GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(gwStatusPath));
        Assert.AreEqual(gwJson, File.ReadAllText(gwStatusPath));
        var gwProbeJson = gatewayHost.FormatHealthProbeJson();
        StringAssert.Contains(gwProbeJson, "\"aggregatorPendingCount\":");
        StringAssert.Contains(gwProbeJson, "\"isPublicationHealthy\": false");
        StringAssert.Contains(gwProbeJson, "\"isOutboxIdle\": false");
        StringAssert.Contains(gwProbeJson, "AggregatorPendingCount");
        var gwProbePath = Path.Combine(chainDir, "soft-rpc-gateway-probe.json");
        gatewayHost.WriteHealthProbeAsync(gwProbePath).AsTask().GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(gwProbePath));
        Assert.AreEqual(gwProbeJson, File.ReadAllText(gwProbePath));
        var gwProm = gatewayHost.ExportPrometheusMetrics();
        Assert.IsFalse(string.IsNullOrWhiteSpace(gwProm));
        var gwPromPath = Path.Combine(chainDir, "soft-rpc-gateway.prom");
        gatewayHost.WritePrometheusMetricsAsync(gwPromPath).AsTask().GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(gwPromPath));
        Assert.AreEqual(gwProm, File.ReadAllText(gwPromPath));
        // PublishAggregateAsync remains a funded L1 publication gate (not exercised here).
    }

    /// <summary>
    /// Offline bridge pipeline shared by Multisig/Optimistic/Zk host compositions:
    /// register asset → ProcessDeposit → StageWithdrawal/Seal → EnqueueOutbound (L2→L1).
    /// Does not claim L1 scan, settle, prove-batch, or withdrawal claim (funded gates).
    /// </summary>
    /// <summary>
    /// Soft offline bridge results used to pin L2 RPC-store proof hand-off without L1.
    /// </summary>
    private sealed record OfflineBridgeSoftResult(
        UInt160 L1Asset,
        UInt160 L2Asset,
        ulong DepositNonce,
        UInt256 WithdrawalLeaf,
        UInt256 WithdrawalRoot,
        WithdrawalTree WithdrawalTree,
        UInt256 OutboundMessageHash);

    private static OfflineBridgeSoftResult AssertOfflineBridgeMintWithdrawalOutbox(
        Action<AssetMapping> registerBridgeAsset,
        Func<CrossChainMessage, MintInstruction> processDeposit,
        Func<uint, ulong, bool> hasConsumedDeposit,
        Func<int> consumedDepositCount,
        Func<int, IReadOnlyList<MintInstruction>> processReadyDeposits,
        Func<WithdrawalRequest, UInt256> stageWithdrawal,
        Func<int> stagedWithdrawalCount,
        Func<(UInt256 Root, WithdrawalTree Tree)> sealWithdrawalBatch,
        Func<IReadOnlyList<CrossChainMessage>, Task> enqueueOutbound,
        Func<int> messageOutboxL2ToL1Count,
        Func<UInt256> messageOutboxL2ToL1Root)
    {
        var l1Asset = UInt160.Parse("0x" + new string('1', 40));
        var l2Asset = UInt160.Parse("0x" + new string('2', 40));
        registerBridgeAsset(new AssetMapping
        {
            L1Asset = l1Asset,
            L2Asset = l2Asset,
            L2ChainId = 20260716u,
            L1Decimals = 8,
            L2Decimals = 8,
            AssetType = AssetType.Gas,
            MintBurn = true,
            LockMint = true,
            Active = true,
        });
        var depositPayload = new DepositPayload
        {
            L1Asset = l1Asset,
            L2Recipient = Account(0x55),
            Amount = new BigInteger(1_000),
        };
        var depositMsg = new CrossChainMessage
        {
            SourceChainId = 0,
            TargetChainId = 20260716u,
            Nonce = 1,
            Sender = Account(0x66),
            Receiver = Account(0x55),
            MessageType = MessageType.Deposit,
            Payload = depositPayload.Encode(),
            MessageHash = UInt256.Zero,
        };
        var mint = processDeposit(depositMsg);
        Assert.AreEqual(l2Asset, mint.L2Asset);
        Assert.IsTrue(hasConsumedDeposit(0, 1));
        Assert.AreEqual(1, consumedDepositCount());
        Assert.AreEqual(0, processReadyDeposits(64).Count);

        var sender = Account(0x77);
        var wdLeaf = stageWithdrawal(new WithdrawalRequest
        {
            ChainId = 20260716u,
            EmittingContract = sender,
            L2Sender = sender,
            L1Recipient = sender,
            L2Asset = l2Asset,
            Amount = new BigInteger(50),
            Nonce = 1,
        });
        Assert.AreNotEqual(UInt256.Zero, wdLeaf);
        Assert.AreEqual(1, stagedWithdrawalCount());
        var sealedWd = sealWithdrawalBatch();
        Assert.AreNotEqual(UInt256.Zero, sealedWd.Root);
        Assert.AreEqual(0, stagedWithdrawalCount());

        var outboundDraft = new CrossChainMessage
        {
            SourceChainId = 20260716u,
            TargetChainId = 0,
            Nonce = 9,
            Sender = sender,
            Receiver = sender,
            MessageType = MessageType.Event,
            Payload = new byte[] { 0x01 },
            MessageHash = UInt256.Zero,
        };
        var outbound = outboundDraft with { MessageHash = MessageHasher.HashMessage(outboundDraft) };
        enqueueOutbound([outbound]).GetAwaiter().GetResult();
        Assert.AreEqual(1, messageOutboxL2ToL1Count());
        Assert.AreNotEqual(UInt256.Zero, messageOutboxL2ToL1Root());
        return new OfflineBridgeSoftResult(
            l1Asset,
            l2Asset,
            DepositNonce: 1,
            WithdrawalLeaf: wdLeaf,
            WithdrawalRoot: sealedWd.Root,
            WithdrawalTree: sealedWd.Tree,
            OutboundMessageHash: outbound.MessageHash);
    }

    /// <summary>
    /// After offline mint/withdrawal/outbox, pin L2 RPC-store deposit status + canonical
    /// withdrawal Merkle proof + message/router proof durability (explorer/RPC surface).
    /// Does not broadcast L1 claim txs (funded gate).
    /// </summary>
    private static void AssertOfflineBridgeRpcStoreSurface(
        OfflineBridgeSoftResult soft,
        Action<DepositStatus> recordRpcDeposit,
        Func<uint, ulong, DepositStatus?> getRpcL1DepositStatus,
        Action<UInt160, UInt160> registerRpcAsset,
        Func<UInt160, UInt160?> getRpcBridgedAsset,
        Func<UInt160, UInt160?> getRpcCanonicalAsset,
        Action<UInt256, byte[]> recordRpcWithdrawalProof,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcWithdrawalProof,
        Action<UInt256, byte[]> recordRpcMessageProof,
        Func<UInt256, ReadOnlyMemory<byte>?> getRpcMessageProof,
        Action<UInt256, ReadOnlyMemory<byte>> recordMessageRouterFinalizedProof,
        Func<UInt256, Task<ReadOnlyMemory<byte>?>> getMessageRouterProofAsync,
        string chainDir,
        ulong? includedInBatch = null)
    {
        recordRpcDeposit(new DepositStatus(
            SourceChainId: 0,
            Nonce: soft.DepositNonce,
            ConsumedOnL2: true,
            IncludedInBatch: includedInBatch));
        var depositStatus = getRpcL1DepositStatus(0, soft.DepositNonce);
        Assert.IsNotNull(depositStatus);
        Assert.IsTrue(depositStatus!.Value.ConsumedOnL2);
        Assert.AreEqual(soft.DepositNonce, depositStatus.Value.Nonce);
        Assert.AreEqual(includedInBatch, depositStatus.Value.IncludedInBatch);

        registerRpcAsset(soft.L1Asset, soft.L2Asset);
        Assert.AreEqual(soft.L2Asset, getRpcBridgedAsset(soft.L1Asset));
        Assert.AreEqual(soft.L1Asset, getRpcCanonicalAsset(soft.L2Asset));

        Assert.AreEqual(1, soft.WithdrawalTree.Count);
        var merkleProof = soft.WithdrawalTree.GetProof(0);
        Assert.AreEqual(soft.WithdrawalLeaf, merkleProof.Leaf);
        var proofBytes = MerkleProofSerializer.Encode(merkleProof);
        Assert.IsTrue(proofBytes.Length >= MerkleProofSerializer.HeaderSize);
        recordRpcWithdrawalProof(soft.WithdrawalLeaf, proofBytes);
        var storedWdProof = getRpcWithdrawalProof(soft.WithdrawalLeaf);
        Assert.IsTrue(storedWdProof is { Length: > 0 });
        CollectionAssert.AreEqual(proofBytes, storedWdProof!.Value.ToArray());

        // Soft message inclusion bytes for RPC explorers (not an L1 MessageRouter claim).
        var messageProofBytes = soft.OutboundMessageHash.GetSpan().ToArray();
        recordRpcMessageProof(soft.OutboundMessageHash, messageProofBytes);
        var storedMsgProof = getRpcMessageProof(soft.OutboundMessageHash);
        Assert.IsTrue(storedMsgProof is { Length: > 0 });
        CollectionAssert.AreEqual(messageProofBytes, storedMsgProof!.Value.ToArray());

        recordMessageRouterFinalizedProof(soft.OutboundMessageHash, messageProofBytes);
        var routerProof = getMessageRouterProofAsync(soft.OutboundMessageHash).GetAwaiter().GetResult();
        Assert.IsTrue(routerProof is { Length: > 0 });
        CollectionAssert.AreEqual(messageProofBytes, routerProof!.Value.ToArray());

        var surfacePath = Path.Combine(chainDir, "soft-offline-bridge-rpc-surface.json");
        File.WriteAllText(surfacePath, $$"""
            {
              "depositNonce": {{soft.DepositNonce}},
              "depositConsumedOnL2": true,
              "withdrawalLeaf": "{{soft.WithdrawalLeaf}}",
              "withdrawalRoot": "{{soft.WithdrawalRoot}}",
              "withdrawalProofBytes": {{proofBytes.Length}},
              "outboundMessageHash": "{{soft.OutboundMessageHash}}",
              "messageProofBytes": {{messageProofBytes.Length}},
              "l1Asset": "{{soft.L1Asset}}",
              "l2Asset": "{{soft.L2Asset}}"
            }
            """);
        Assert.IsTrue(File.Exists(surfacePath));
        var surfaceJson = File.ReadAllText(surfacePath);
        StringAssert.Contains(surfaceJson, "\"depositConsumedOnL2\": true");
        StringAssert.Contains(surfaceJson, "\"withdrawalLeaf\": \"" + soft.WithdrawalLeaf + "\"");
        StringAssert.Contains(surfaceJson, "\"outboundMessageHash\": \"" + soft.OutboundMessageHash + "\"");
    }

    /// <summary>
    /// Soft ops surface after offline mint/withdrawal/outbox: L1 scan remains empty,
    /// status/probe expose consumed deposit + L2→L1 outbox counts, durable files written.
    /// Does not claim L1 deposit scan or withdrawal claim (funded gates).
    /// </summary>
    private static void AssertOfflineBridgeOperatorSurface(
        Func<LocalHostOperatorStatus> getOperatorStatus,
        Func<LocalHostHealthProbeDocument> getHealthProbe,
        Func<string> formatOperatorStatusJson,
        Func<string> formatHealthProbeJson,
        Func<string, Task> writeOperatorStatusAsync,
        Func<string, Task> writeHealthProbeAsync,
        Func<Task<int>> scanSharedBridgeDepositsAsync,
        Func<Task<IReadOnlyList<MintInstruction>>> scanAndProcessReadyDepositsAsync,
        string chainDir)
    {
        // Offline ProcessDeposit already consumed nonce 1; L1 scan has nothing new (mock height).
        Assert.AreEqual(0, scanSharedBridgeDepositsAsync().GetAwaiter().GetResult());
        Assert.AreEqual(0, scanAndProcessReadyDepositsAsync().GetAwaiter().GetResult().Count);

        var status = getOperatorStatus();
        Assert.AreEqual(1, status.ConsumedDepositCount);
        Assert.AreEqual(1, status.MessageOutboxL2ToL1Count);
        Assert.AreEqual(0, status.StagedWithdrawalCount);
        Assert.IsTrue(status.HasMessageOutbox);
        Assert.AreNotEqual(UInt256.Zero, status.MessageOutboxL2ToL1Root);
        Assert.IsTrue(status.IsOfflinePassportComplete);
        Assert.AreEqual(0, status.OfflinePassportFailures.Count);
        Assert.IsTrue(status.IsSettlementIdle);
        Assert.IsFalse(status.IsSettlementPoisoned);
        Assert.IsFalse(status.IsSettlementRetrying);
        Assert.AreEqual(0, status.PendingSettlementCount);
        Assert.IsTrue(status.IsOperatorReady);

        var probe = getHealthProbe();
        Assert.AreEqual(1, probe.ConsumedDepositCount);
        Assert.AreEqual(1, probe.MessageOutboxL2ToL1Count);
        Assert.AreEqual(0, probe.StagedWithdrawalCount);
        Assert.AreEqual(status.MessageOutboxL2ToL1Root.ToString(), probe.MessageOutboxL2ToL1Root);
        Assert.IsTrue(probe.IsOfflinePassportComplete);
        Assert.IsTrue(probe.IsSettlementIdle);

        var statusJson = formatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"consumedDepositCount\": 1");
        StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Count\": 1");
        StringAssert.Contains(statusJson, "\"stagedWithdrawalCount\": 0");
        StringAssert.Contains(statusJson, "\"hasMessageOutbox\": true");
        StringAssert.Contains(statusJson, "\"isSettlementIdle\": true");
        StringAssert.Contains(statusJson, "\"pendingSettlementCount\": 0");

        var probeJson = formatHealthProbeJson();
        StringAssert.Contains(probeJson, "\"consumedDepositCount\": 1");
        StringAssert.Contains(probeJson, "\"messageOutboxL2ToL1Count\": 1");
        StringAssert.Contains(probeJson, "\"stagedWithdrawalCount\": 0");
        StringAssert.Contains(probeJson, "\"isSettlementIdle\": true");

        var statusPath = Path.Combine(chainDir, "soft-offline-bridge-status.json");
        writeOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        var statusFile = File.ReadAllText(statusPath);
        StringAssert.Contains(statusFile, "\"consumedDepositCount\": 1");
        StringAssert.Contains(statusFile, "\"messageOutboxL2ToL1Count\": 1");
        StringAssert.Contains(statusFile, "\"hasMessageOutbox\": true");

        var probePath = Path.Combine(chainDir, "soft-offline-bridge-probe.json");
        writeHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        var probeFile = File.ReadAllText(probePath);
        StringAssert.Contains(probeFile, "\"consumedDepositCount\": 1");
        StringAssert.Contains(probeFile, "\"messageOutboxL2ToL1Count\": 1");
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

    private static void RewriteMaxBlocksPerBatch(string chainDir, int maxBlocks)
    {
        var path = Path.Combine(chainDir, "Plugins", "Neo.Plugins.L2Batch", "config.json");
        if (!File.Exists(path))
            Assert.Fail($"expected materialised batch plugin config at {path}");
        var text = File.ReadAllText(path);
        var rewritten = System.Text.RegularExpressions.Regex.Replace(
            text,
            "\"MaxBlocksPerBatch\"\\s*:\\s*\\d+",
            $"\"MaxBlocksPerBatch\": {maxBlocks}");
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
            // Echo request id so concurrent deposit/FI/message drain scans match JSON-RPC ids.
            var idToken = "1";
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("id", out var idEl))
                    idToken = idEl.ValueKind == System.Text.Json.JsonValueKind.Number
                        ? idEl.GetRawText()
                        : System.Text.Json.JsonSerializer.Serialize(idEl.GetString());
            }
            catch
            {
                // keep default id
            }
            if (body.Contains("getversion", StringComparison.Ordinal))
            {
                return Json(
                    $"{{\"jsonrpc\":\"2.0\",\"id\":{idToken},\"result\":{{\"protocol\":{{\"network\":894710606,\"addressversion\":53}}}}}}");
            }
            if (body.Contains("getblockcount", StringComparison.Ordinal))
            {
                return Json($"{{\"jsonrpc\":\"2.0\",\"id\":{idToken},\"result\":100}}");
            }
            if (body.Contains("invokefunction", StringComparison.Ordinal)
                || body.Contains("invokescript", StringComparison.Ordinal))
            {
                var b64 = Convert.ToBase64String(root.GetSpan().ToArray());
                return Json(
                    "{\"jsonrpc\":\"2.0\",\"id\":" + idToken
                    + ",\"result\":{\"state\":\"HALT\",\"gasconsumed\":\"0\","
                    + "\"stack\":[{\"type\":\"ByteString\",\"value\":\"" + b64 + "\"}]}}");
            }
            return Json($"{{\"jsonrpc\":\"2.0\",\"id\":{idToken},\"result\":null}}");
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

    /// <summary>
    /// Local Multisig/Optimistic soft seal executor (non-ZK). L1 settle remains funded.
    /// </summary>
    private sealed class SoftPassThroughExecutor : IProofWitnessBatchExecutor
    {
        public static UInt256 PostStateRoot { get; } =
            new(Enumerable.Repeat((byte)0x44, 32).ToArray());

        public ValueTask<BatchExecutionResult> ApplyBatchAsync(
            BatchExecutionRequest request,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(BuildResult());

        public ValueTask<ProofWitnessExecutionResult> ApplyBatchWithWitnessAsync(
            SealedBatch batch,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(batch);
            return ValueTask.FromResult(new ProofWitnessExecutionResult
            {
                ExecutionResult = BuildResult(),
                ExecutionSemanticId = ExecutionSemanticIds.ReferenceNoOpV1,
                WitnessAuthenticated = false,
                StateWitness = ReadOnlyMemory<byte>.Empty,
                Effects = new byte[] { 0x01 },
            });
        }

        private static BatchExecutionResult BuildResult() => new()
        {
            PostStateRoot = PostStateRoot,
            ReceiptRoot = UInt256.Zero,
            WithdrawalRoot = UInt256.Zero,
            L2ToL1MessageRoot = UInt256.Zero,
            L2ToL2MessageRoot = UInt256.Zero,
            TxRoot = UInt256.Zero,
            GasConsumed = 0,
        };
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
