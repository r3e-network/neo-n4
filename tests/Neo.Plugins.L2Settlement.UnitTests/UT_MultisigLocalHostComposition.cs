using System.Net;
using System.Numerics;
using System.Text;
using Neo.Cryptography.ECC;
using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.Bridge;
using Neo.L2.Executor.ProofWitness;
using Neo.L2.Gateway.Rpc;
using Neo.L2.Persistence;
using Neo.L2.Proving;
using Neo.L2.Proving.Attestation;
using Neo.L2.Settlement.Rpc;
using Neo.L2.State;
using Neo.Network.P2P.Payloads;
using Neo.Plugins.L2Gateway;
using Neo.Plugins.L2Rpc;
using Neo.Wallets;

namespace Neo.Plugins.L2Settlement.UnitTests;

/// <summary>Host composition tests for <see cref="MultisigLocalHostComposition"/>.</summary>
[TestClass]
public sealed class UT_MultisigLocalHostComposition
{
    [TestMethod]
    public async Task Open_FromDeployReport_WiresMultisigLocalStack()
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
            Assert.IsTrue(host.HasSealedBatchSink);
            Assert.AreEqual(1UL, host.NextExpectedBlock);
            Assert.AreEqual(host.Batch.NextExpectedBlock, host.NextExpectedBlock);
            Assert.AreEqual(0UL, host.LastAcknowledgedBatchNumber);
            Assert.AreEqual(0UL, host.LastAcknowledgedBlock);
            Assert.AreEqual(1UL, host.NextBatchNumber);
            Assert.IsFalse(host.HasPendingSealedBatch);
            Assert.IsNull(host.PendingSealedBatch);
            Assert.IsNull(host.PendingSealedBatchNumber);
            Assert.IsTrue(host.IsBatcherEnabled);
            Assert.IsTrue(host.MaxBlocksPerBatch > 0);
            Assert.IsTrue(host.MaxTransactionsPerBatch > 0);
            Assert.IsTrue(host.MaxBatchAgeMillis > 0);
            Assert.IsFalse(host.Batch.HasPendingSealedBatch);
            Assert.IsFalse(host.HasOpenBatch);
            Assert.AreEqual(0, host.InProgressTxCount);
            Assert.IsNull(host.OpenBatchFirstBlock);
            Assert.IsNull(host.OpenBatchLastBlock);
            Assert.AreEqual(0, host.OpenBatchBlockCount);
            Assert.AreEqual(0, host.OpenBatchL1MessageCount);
            Assert.AreEqual(0, host.OpenBatchL2ToL1MessageCount);
            Assert.AreEqual(0, host.OpenBatchL2ToL2MessageCount);
            Assert.AreEqual(0, host.OpenBatchForcedInclusionCount);
            Assert.AreEqual(0, host.OpenBatchWithdrawalCount);
            Assert.IsTrue(host.SupportsLocalDaReader);
            Assert.IsTrue(host.HasL1RpcEndpoint);
            Assert.IsNotNull(host.ExpectedNetwork);
            Assert.IsTrue(host.HasSettlementManagerHash);
            Assert.IsTrue(host.HasForcedInclusionHash);
            Assert.IsTrue(host.HasSharedBridgeHash);
            Assert.IsTrue(host.HasMessageRouterHash);
            // Deploy-report templates leave L2BridgeHash empty (native L2Bridge default).
            Assert.IsFalse(host.HasL2BridgeHash);
            Assert.IsTrue(host.HasMessageOutbox);
            Assert.AreEqual(0, host.ConsumedDepositCount);
            Assert.IsFalse(host.TryRetryPendingSealedBatch());
            // Soft open-batch (no seal): MaxBlocksPerBatch > 1 so one empty block only drains
            // empty L1 inboxes (deploy height >> mock getblockcount → scan no-ops; no known FI
            // nonces yet). Seal + sink PersistAsync remain L2BatchPlugin unit / funded paths.
            Assert.IsTrue(host.MaxBlocksPerBatch > 1);
            var openBatchTimestampMs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            host.ProcessCommittedBlock(1, openBatchTimestampMs, 894710606, Array.Empty<byte[]>());
            Assert.IsTrue(host.HasOpenBatch);
            Assert.AreEqual(1UL, host.OpenBatchFirstBlock);
            Assert.AreEqual(1UL, host.OpenBatchLastBlock);
            Assert.AreEqual(1, host.OpenBatchBlockCount);
            Assert.AreEqual(0, host.OpenBatchL1MessageCount);
            Assert.AreEqual(0, host.OpenBatchForcedInclusionCount);
            Assert.AreEqual(0, host.InProgressTxCount);
            Assert.IsFalse(host.HasPendingSealedBatch);
            Assert.AreEqual(2UL, host.NextExpectedBlock);
            Assert.AreEqual(1UL, host.NextBatchNumber);
            // Second empty block still under MaxBlocksPerBatch: open batch grows without seal.
            if (host.MaxBlocksPerBatch > 2)
            {
                host.ProcessCommittedBlock(2, openBatchTimestampMs + 1, 894710606, Array.Empty<byte[]>());
                Assert.IsTrue(host.HasOpenBatch);
                Assert.AreEqual(1UL, host.OpenBatchFirstBlock);
                Assert.AreEqual(2UL, host.OpenBatchLastBlock);
                Assert.AreEqual(2, host.OpenBatchBlockCount);
                Assert.IsFalse(host.HasPendingSealedBatch);
                Assert.AreEqual(3UL, host.NextExpectedBlock);
                Assert.AreEqual(1UL, host.NextBatchNumber);
            }
            Assert.IsTrue(host.RegisterInboundMessageNonce(7));            Assert.AreEqual(1, host.KnownInboundNonceCount);
            host.InvalidateInboundMessageCache();
            Assert.AreEqual(0, host.L1InboxPendingCount);
            Assert.AreEqual(0, host.L1InboxConsumedCount);
            Assert.IsTrue(host.Settlement.IsProductionWired);
            Assert.IsTrue(host.IsProductionWired);
            Assert.IsTrue(host.IsOperatorReady);
            Assert.AreEqual(20260716u, host.ChainId);
            Assert.AreEqual(20260716u, host.BatcherConfiguredChainId);
            Assert.AreEqual(20260716u, host.SettlementConfiguredChainId);
            Assert.AreEqual(host.ChainId, host.BatcherConfiguredChainId);
            Assert.AreEqual(host.ChainId, host.SettlementConfiguredChainId);
            Assert.AreEqual(ProofType.Multisig, host.ProofType);
            Assert.AreEqual(ProofType.Multisig, host.SettlementConfiguredProofType);
            Assert.AreEqual(host.ProofType, host.SettlementConfiguredProofType);
            Assert.IsTrue(host.IsChainIdConfigConsistent);
            Assert.IsTrue(host.IsProofTypeConfigConsistent);
            Assert.AreEqual(host.ChainId, host.RpcChainId);
            Assert.AreEqual(DAMode.Local, host.DaMode);
            Assert.AreEqual(DAMode.Local, host.RpcDaMode);
            Assert.IsTrue(host.IsDaModeConfigConsistent);
            Assert.IsTrue(host.IsNeoHubHashWiringComplete);
            Assert.IsTrue(host.IsBatcherInboxWiringComplete);
            Assert.IsTrue(host.IsSecurityLevelProofTypeConsistent);
            Assert.IsTrue(host.IsSecurityLevelDaModeConsistent);
            Assert.IsTrue(host.IsMetricsWiringComplete);
            Assert.IsFalse(host.HasMetricsReadinessCheck);
            Assert.IsFalse(host.HasMetricsHealthProbe);
            Assert.IsFalse(host.HasMetricsOperatorStatus);
            Assert.IsTrue(host.IsDepositPipelineWiringComplete);
            Assert.IsTrue(host.IsMessagePipelineWiringComplete);
            Assert.IsTrue(host.IsForcedInclusionPipelineWiringComplete);
            Assert.IsTrue(host.IsSettlementClientWiringComplete);
            Assert.IsTrue(host.IsPipelineEnabled);
            Assert.IsTrue(host.HasDepositSource);
            Assert.IsTrue(host.HasSettlementClient);
            Assert.IsTrue(host.HasTransactionSender);
            Assert.IsTrue(host.HasExpectedNetwork);
            Assert.IsTrue(host.HasScannerDeployHeights);
            Assert.IsTrue(host.IsOfflinePassportComplete);
            Assert.AreEqual(0, host.OfflinePassportFailures.Count);
            Assert.AreEqual(0, host.PeekSharedBridgeDeposits(8).Count);
            var status = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(20260716u, status.ChainId);
            Assert.AreEqual(20260716u, status.BatcherConfiguredChainId);
            Assert.AreEqual(20260716u, status.SettlementConfiguredChainId);
            Assert.AreEqual(status.ChainId, status.BatcherConfiguredChainId);
            Assert.AreEqual(status.ChainId, status.SettlementConfiguredChainId);
            Assert.AreEqual(ProofType.Multisig, status.ProofType);
            Assert.AreEqual(ProofType.Multisig, status.SettlementConfiguredProofType);
            Assert.IsTrue(status.IsChainIdConfigConsistent);
            Assert.IsTrue(status.IsProofTypeConfigConsistent);
            Assert.AreEqual(status.ChainId, status.RpcChainId);
            Assert.AreEqual(DAMode.Local, status.DaMode);
            Assert.AreEqual(DAMode.Local, status.RpcDaMode);
            Assert.IsTrue(status.IsDaModeConfigConsistent);
            Assert.IsTrue(status.IsNeoHubHashWiringComplete);
            Assert.IsTrue(status.IsBatcherInboxWiringComplete);
            Assert.IsTrue(status.IsSecurityLevelProofTypeConsistent);
            Assert.IsTrue(status.IsSecurityLevelDaModeConsistent);
            Assert.IsTrue(status.IsMetricsWiringComplete);
            Assert.IsFalse(status.HasMetricsReadinessCheck);
            Assert.IsFalse(status.HasMetricsHealthProbe);
            Assert.IsFalse(status.HasMetricsOperatorStatus);
            Assert.IsTrue(status.IsDepositPipelineWiringComplete);
            Assert.IsTrue(status.IsMessagePipelineWiringComplete);
            Assert.IsTrue(status.IsForcedInclusionPipelineWiringComplete);
            Assert.IsTrue(status.IsSettlementClientWiringComplete);
            Assert.IsTrue(status.IsPipelineEnabled);
            Assert.IsFalse(status.IsSettlementPoisoned);
            Assert.IsTrue(status.IsSettlementIdle);
            Assert.AreEqual(0, status.SettlementRetryCount);
            Assert.AreEqual(status.Recovery.RetryCount, status.SettlementRetryCount);
            Assert.AreEqual(
                status.Recovery.ConfirmationLagBatches,
                status.SettlementConfirmationLagBatches);
            Assert.IsFalse(status.HasOverdueForcedInclusion);
            Assert.IsFalse(host.HasOverdueForcedInclusionCached());
            Assert.IsFalse(status.IsSettlementRetrying);
            Assert.IsTrue(status.IsBatcherCheckpointAligned);
            Assert.IsFalse(status.IsOpenBatchPastMaxAge);
            Assert.IsNotNull(status.OpenBatchAgeMillis);
            Assert.IsTrue(status.IsPipelineHealthy);
            Assert.AreEqual(0, status.PipelineHealthFailures.Count);
            Assert.IsTrue(await host.IsPipelineHealthyAsync());
            Assert.AreEqual(0, (await host.GetPipelineHealthFailuresAsync()).Count);
            Assert.IsTrue(await host.IsSettlementRuntimeIdleAsync());
            Assert.IsFalse(await host.IsSettlementPoisonedAsync());
            Assert.IsFalse(await host.IsSettlementRetryingAsync());
            Assert.IsFalse(await host.IsLocalHostHealthyAsync());
            var healthFailures = await host.GetLocalHostHealthFailuresAsync();
            Assert.IsTrue(healthFailures.Count >= 1);
            Assert.IsFalse(status.IsMetricsHttpHealthy);
            Assert.IsFalse(host.IsMetricsHttpHealthy);
            CollectionAssert.Contains(
                status.MetricsHttpHealthFailures.ToArray(),
                nameof(LocalHostOperatorStatus.IsMetricsHttpListening));
            CollectionAssert.Contains(
                status.MetricsHttpHealthFailures.ToArray(),
                nameof(LocalHostOperatorStatus.HasMetricsReadinessCheck));
            CollectionAssert.Contains(
                status.MetricsHttpHealthFailures.ToArray(),
                nameof(LocalHostOperatorStatus.HasMetricsHealthProbe));
            CollectionAssert.Contains(
                status.MetricsHttpHealthFailures.ToArray(),
                nameof(LocalHostOperatorStatus.HasMetricsOperatorStatus));
            CollectionAssert.AreEqual(
                host.MetricsHttpHealthFailures.ToArray(),
                status.MetricsHttpHealthFailures.ToArray());
            Assert.IsFalse(status.IsLocalHostHealthy);
            Assert.IsTrue(status.LocalHostHealthFailures.Count >= 1);
            Assert.IsTrue(status.HasExpectedNetwork);
            Assert.IsTrue(status.HasScannerDeployHeights);
            Assert.IsTrue(status.IsOfflinePassportComplete);
            Assert.AreEqual(0, status.OfflinePassportFailures.Count);
            Assert.AreEqual(host.RpcStore.SecurityLevel, status.SecurityLevel);
            Assert.IsTrue(status.IsOperatorReady);
            Assert.IsTrue(status.IsProductionWired);
            Assert.IsTrue(status.HasSealedBatchSink);
            Assert.AreEqual(host.NextExpectedBlock, status.NextExpectedBlock);
            Assert.IsFalse(status.HasPendingSealedBatch);
            Assert.IsTrue(status.HasOpenBatch);
            Assert.AreEqual(0, status.InProgressTxCount);
            Assert.AreEqual(1UL, status.OpenBatchFirstBlock);
            Assert.AreEqual(host.OpenBatchLastBlock, status.OpenBatchLastBlock);
            Assert.AreEqual(host.OpenBatchBlockCount, status.OpenBatchBlockCount);
            Assert.AreEqual(0, status.OpenBatchL2ToL2MessageCount);
            Assert.AreEqual(0, status.OpenBatchForcedInclusionCount);
            Assert.AreEqual(0, status.OpenBatchWithdrawalCount);
            Assert.IsTrue(status.SupportsLocalDaReader);
            Assert.IsTrue(status.HasL1RpcEndpoint);
            Assert.AreEqual(host.ExpectedNetwork, status.ExpectedNetwork);
            Assert.IsTrue(status.HasSettlementManagerHash);
            Assert.IsTrue(status.HasForcedInclusionHash);
            Assert.IsTrue(status.HasSharedBridgeHash);
            Assert.IsTrue(status.HasMessageRouterHash);
            Assert.IsFalse(status.HasL2BridgeHash);
            Assert.IsTrue(status.HasMessageOutbox);
            Assert.IsTrue(status.HasDepositSource);
            Assert.IsTrue(status.HasMessageRouter);
            Assert.IsTrue(status.HasForcedInclusionFinalizer);
            Assert.IsTrue(status.HasSettlementClient);
            Assert.IsTrue(status.HasTransactionSender);
            Assert.AreEqual(0, status.L1InboxPendingCount);
            Assert.AreEqual(0, status.L1InboxConsumedCount);
            Assert.AreEqual(1, status.KnownInboundNonceCount);
            Assert.IsFalse(status.IsMetricsHttpListening);
            Assert.AreEqual(0, status.PendingSettlementCount);
            Assert.AreEqual(0, status.ReadyDepositCount);
            Assert.AreEqual(0, status.DepositSourceReadyCount);
            Assert.AreEqual(0, status.DepositSourceReservedCount);
            Assert.AreEqual(0, status.DepositSourceSoftConsumedCount);
            Assert.IsTrue(status.IsMetricsEnabled);
            Assert.IsTrue(status.MetricsConfiguredPort > 0);
            Assert.IsFalse(string.IsNullOrWhiteSpace(status.MetricsBindAddress));
            Assert.AreEqual(host.ForcedInclusionDeploymentHeight, status.ForcedInclusionDeploymentHeight);
            Assert.AreEqual(host.SharedBridgeDeploymentHeight, status.SharedBridgeDeploymentHeight);
            Assert.AreEqual(host.MessageRouterDeploymentHeight, status.MessageRouterDeploymentHeight);
            Assert.IsTrue(status.IsSettlementEnabled);
            Assert.IsTrue(status.L1FinalityDepth >= 1);
            Assert.AreEqual(0, status.TrackedForcedInclusionNonceCount);
            Assert.AreEqual(0, status.Recovery.PendingCount);
            Assert.IsNull(status.Recovery.FirstFailureAtUnixMilliseconds);
            Assert.IsNull(status.Recovery.LastFailureAtUnixMilliseconds);
            Assert.IsTrue(status.HasBatchProver);
            Assert.IsTrue(host.HasBatchProver);
            Assert.IsNull(status.LatestCheckpointBatchNumber);
            Assert.IsNull(status.LatestCheckpointLastBlock);
            Assert.AreEqual(UInt256.Zero, status.LatestCheckpointPostStateRoot);
            Assert.AreEqual(
                host.GetInitialStateRootAsync().AsTask().GetAwaiter().GetResult(),
                status.InitialStateRoot);
            Assert.AreEqual(host.GetLatestRpcStateRoot(), status.LatestRpcStateRoot);
            // Host RPC store helpers (no Neo.CLI).
            var root = new UInt256(Enumerable.Repeat((byte)0xAB, 32).ToArray());
            var z = UInt256.Zero;
            var batch = new L2BatchCommitment
            {
                ChainId = 20260716u,
                BatchNumber = 1,
                FirstBlock = 1,
                LastBlock = 1,
                PreStateRoot = z,
                PostStateRoot = root,
                TxRoot = z,
                ReceiptRoot = z,
                WithdrawalRoot = z,
                L2ToL1MessageRoot = z,
                L2ToL2MessageRoot = z,
                DACommitment = z,
                PublicInputHash = z,
                ProofType = ProofType.Multisig,
                Proof = ReadOnlyMemory<byte>.Empty,
            };
            host.AddRpcBatch(batch, BatchStatus.Pending);
            Assert.AreEqual(BatchStatus.Pending, host.GetRpcBatchStatus(1));
            Assert.IsNotNull(host.GetRpcBatch(1));
            host.FinalizeRpcBatch(1);
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(1));
            Assert.AreEqual(root, host.GetLatestRpcStateRoot());
            Assert.AreEqual(root, host.GetRpcStateRootAtBatch(1));
            host.RecordRpcDeposit(new DepositStatus(20260716u, 1, ConsumedOnL2: false, IncludedInBatch: null));
            Assert.IsNotNull(host.GetRpcL1DepositStatus(20260716u, 1));
            var l1Asset = UInt160.Parse("0x" + new string('a', 40));
            var l2Asset = UInt160.Parse("0x" + new string('b', 40));
            host.RegisterRpcAsset(l1Asset, l2Asset);
            Assert.AreEqual(l2Asset, host.GetRpcBridgedAsset(l1Asset));
            Assert.AreEqual(l1Asset, host.GetRpcCanonicalAsset(l2Asset));
            var leaf = new UInt256(Enumerable.Repeat((byte)0xCD, 32).ToArray());
            host.RecordRpcWithdrawalProof(leaf, [0x01, 0x02]);
            Assert.IsTrue(host.GetRpcWithdrawalProof(leaf) is { Length: 2 });
            var msgHash = new UInt256(Enumerable.Repeat((byte)0xEF, 32).ToArray());
            host.RecordRpcMessageProof(msgHash, [0x03]);
            Assert.IsTrue(host.GetRpcMessageProof(msgHash) is { Length: 1 });
            Assert.IsNotNull(host.MessageOutbox);
            Assert.AreEqual(0, host.MessageOutbox!.L2ToL1Count);
            host.RecordMessageRouterFinalizedProof(msgHash, new byte[] { 0x04 });
            var routerProof = host.GetMessageRouterProofAsync(msgHash).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(routerProof is { Length: 1 });
            Assert.IsTrue(status.GatewayEnabled);
            Assert.IsTrue(host.RegisterForcedInclusionNonce(42));
            Assert.IsFalse(host.RegisterForcedInclusionNonce(42)); // already known
            Assert.AreEqual(1, host.KnownForcedInclusionNonceCount);
            Assert.IsTrue(host.HasBatchForcedInclusionSource);
            Assert.IsTrue(host.HasBatchDepositSource);
            Assert.IsTrue(host.HasBatchMessageRouter);
            Assert.IsTrue(host.MaxForcedTransactionsPerBatch > 0);
            Assert.IsTrue(host.MaxL1MessagesPerBatch > 0);
            Assert.IsTrue(host.MetricsMaxConcurrentConnections > 0);
            host.InvalidateForcedInclusionCache();
            var statusFi = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(1, statusFi.KnownForcedInclusionNonceCount);
            Assert.IsTrue(statusFi.HasBatchForcedInclusionSource);
            Assert.IsTrue(statusFi.HasBatchDepositSource);
            Assert.IsTrue(statusFi.HasBatchMessageRouter);
            Assert.AreEqual(host.MaxForcedTransactionsPerBatch, statusFi.MaxForcedTransactionsPerBatch);
            Assert.AreEqual(host.MaxL1MessagesPerBatch, statusFi.MaxL1MessagesPerBatch);
            Assert.AreEqual(host.MetricsMaxConcurrentConnections, statusFi.MetricsMaxConcurrentConnections);
            var daReq = new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 1,
                Payload = new byte[] { 0x10, 0x20, 0x30 },
            };
            var receipt = host.PublishDaAsync(daReq).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(DAMode.Local, receipt.Layer);
            Assert.IsTrue(host.IsDaAvailableAsync(receipt).AsTask().GetAwaiter().GetResult());
            var daReader = host.CreateLocalDaReader();
            Assert.IsNotNull(daReader);
            var payload = daReader.ReadAsync(receipt).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(payload is { Length: 3 });
            Assert.IsNotNull(host.Metrics.Metrics);
            Assert.AreEqual(0, host.BridgeAssetCount);
            host.RegisterBridgeAsset(new AssetMapping
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
            Assert.AreEqual(1, host.BridgeAssetCount);
            Assert.AreEqual(1, host.SnapshotBridgeAssets().Count);
            var prom = host.ExportPrometheusMetrics();
            Assert.IsFalse(string.IsNullOrWhiteSpace(prom));
            var snap = host.CaptureMetricsSnapshot();
            Assert.IsTrue(snap.TotalEntries >= 0);
            var status2 = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(1, status2.BridgeAssetCount);
            Assert.AreEqual(0, status2.MessageOutboxL2ToL1Count);
            Assert.AreEqual(0, status2.StagedWithdrawalCount);
            // Offline deposit mint path + Multisig prove (no funded L1).
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
            var mint = host.ProcessDeposit(depositMsg);
            Assert.AreEqual(l2Asset, mint.L2Asset);
            Assert.IsTrue(host.HasConsumedDeposit(0, 1));
            Assert.AreEqual(1, host.ConsumedDepositCount);
            // Ready-deposit drain is peek-only; empty ready set yields empty without L1 scan.
            Assert.AreEqual(0, host.ProcessReadyDeposits().Count);
            Assert.AreEqual(0, host.ProcessReadyDeposits(0).Count);
            Assert.IsNotNull(host.DepositSource);
            Assert.IsNotNull(host.ForcedInclusion);
            // Soft mock: deploy height >> getblockcount so deposit ScanAsync no-ops (0 blocks).
            Assert.AreEqual(0, await host.ScanSharedBridgeDepositsAsync());
            // After RegisterForcedInclusionNonce, overdue check fans out getEntry/isConsumed —
            // mock returns ByteString roots (committee path), so parse fails closed (funded L1 gate).
            await Assert.ThrowsExactlyAsync<OverflowException>(
                async () => await host.HasOverdueForcedInclusionAsync(0));
            Assert.AreEqual(0, host.StagedWithdrawalCount);
            var proof = host.ProveAsync(new ProofRequest
            {
                PublicInputs = new PublicInputs
                {
                    ChainId = 20260716u,
                    BatchNumber = 1,
                    PreStateRoot = z,
                    PostStateRoot = root,
                    TxRoot = z,
                    ReceiptRoot = z,
                    WithdrawalRoot = z,
                    L2ToL1MessageRoot = z,
                    L2ToL2MessageRoot = z,
                    L1MessageHash = z,
                    DACommitment = z,
                    BlockContextHash = z,
                },
                Witness = ReadOnlyMemory<byte>.Empty,
                Kind = ProofType.Multisig,
            }).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(ProofType.Multisig, proof.Kind);
            Assert.IsFalse(proof.Proof.IsEmpty);
            Assert.IsNotNull(host.BatchProver);
            Assert.IsTrue(host.HasBatchProver);
            // Withdrawal staging + outbound message outbox + status JSON dump.
            var sender = Account(0x77);
            var wdLeaf = host.StageWithdrawal(new WithdrawalRequest
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
            Assert.AreEqual(1, host.StagedWithdrawalCount);
            var sealedWd = host.SealWithdrawalBatch();
            Assert.AreNotEqual(UInt256.Zero, sealedWd.Root);
            Assert.AreEqual(0, host.StagedWithdrawalCount);
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
            await host.EnqueueOutboundMessagesAsync([outbound]);
            Assert.AreEqual(1, host.MessageOutbox!.L2ToL1Count);
            Assert.AreNotEqual(UInt256.Zero, outbound.MessageHash);
            Assert.AreNotEqual(UInt256.Zero, host.MessageOutboxL2ToL1Root);
            Assert.AreEqual(host.MessageOutbox.L2ToL1Root, host.MessageOutboxL2ToL1Root);
            var statusPath = Path.Combine(chainDir, "operator-status.json");
            await host.WriteOperatorStatusAsync(statusPath);
            Assert.IsTrue(File.Exists(statusPath));
            // OpenBatchAgeMillis is wall-clock; pin a single write snapshot.
            var statusJson = await File.ReadAllTextAsync(statusPath);
            StringAssert.Contains(statusJson, "\"isOfflinePassportComplete\"");
            StringAssert.Contains(statusJson, "\"settlementRetryCount\":");
            StringAssert.Contains(statusJson, "\"settlementConfirmationLagBatches\":");
            StringAssert.Contains(statusJson, "\"isSettlementIdle\": true");
            StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Count\": 1");
            StringAssert.Contains(statusJson, "\"hasOpenBatch\": true");
            Assert.IsTrue(statusJson.EndsWith('\n') || statusJson.EndsWith(Environment.NewLine));
            Assert.IsTrue(await host.IsBatcherCheckpointAlignedAsync());
            var probe = await host.GetHealthProbeAsync();
            Assert.IsTrue(probe.IsOfflinePassportComplete);
            Assert.AreEqual(host.ChainId, probe.ChainId);
            Assert.AreEqual(host.BatcherConfiguredChainId, probe.BatcherConfiguredChainId);
            Assert.AreEqual(host.SettlementConfiguredChainId, probe.SettlementConfiguredChainId);
            Assert.AreEqual(host.RpcChainId, probe.RpcChainId);
            Assert.AreEqual(host.ProofType.ToString(), probe.ProofType);
            Assert.AreEqual(host.SettlementConfiguredProofType.ToString(), probe.SettlementConfiguredProofType);
            Assert.AreEqual(host.DaMode.ToString(), probe.DaMode);
            Assert.AreEqual(host.RpcDaMode.ToString(), probe.RpcDaMode);
            Assert.AreEqual(host.RpcStore.SecurityLevel.ToString(), probe.SecurityLevel);
            Assert.AreEqual(host.RpcStore.Sequencer.ToString(), probe.Sequencer);
            Assert.AreEqual(host.RpcStore.Exit.ToString(), probe.Exit);
            Assert.AreEqual(host.ExpectedNetwork, probe.ExpectedNetwork);
            Assert.IsFalse(string.IsNullOrWhiteSpace(probe.InitialStateRoot));
            Assert.IsFalse(string.IsNullOrWhiteSpace(probe.LatestRpcStateRoot));
            Assert.IsNull(probe.LatestCheckpointPostStateRoot);
            Assert.IsFalse(string.IsNullOrWhiteSpace(probe.MessageOutboxL2ToL1Root));
            Assert.IsFalse(string.IsNullOrWhiteSpace(probe.MessageOutboxL2ToL2Root));
            Assert.AreEqual(0, probe.TrackedForcedInclusionNonceCount);
            Assert.AreEqual(host.IsMetricsEnabled, probe.IsMetricsEnabled);
            Assert.AreEqual(host.IsMetricsWiringComplete, probe.IsMetricsWiringComplete);
            Assert.AreEqual(host.MetricsConfiguredPort, probe.MetricsConfiguredPort);
            Assert.AreEqual(host.MetricsBindAddress, probe.MetricsBindAddress);
            Assert.AreEqual(host.MetricsMaxConcurrentConnections, probe.MetricsMaxConcurrentConnections);
            Assert.IsTrue(probe.MetricsEntryCount >= 0);
            Assert.AreEqual(host.RpcStore.GatewayEnabled, probe.GatewayEnabled);
            Assert.IsTrue(probe.SupportsLocalDaReader);
            Assert.AreEqual(host.BridgeAssetCount, probe.BridgeAssetCount);
            Assert.IsTrue(probe.IsOperatorReady);
            Assert.IsTrue(probe.IsProductionWired);
            Assert.IsTrue(probe.HasSealedBatchSink);
            Assert.IsTrue(probe.HasBatchProver);
            Assert.IsTrue(probe.IsSettlementEnabled);
            Assert.IsTrue(probe.IsBatcherEnabled);
            Assert.IsTrue(probe.HasL1RpcEndpoint);
            Assert.IsTrue(probe.HasExpectedNetwork);
            Assert.IsTrue(probe.HasScannerDeployHeights);
            Assert.AreEqual(host.ForcedInclusionDeploymentHeight, probe.ForcedInclusionDeploymentHeight);
            Assert.AreEqual(host.SharedBridgeDeploymentHeight, probe.SharedBridgeDeploymentHeight);
            Assert.AreEqual(host.MessageRouterDeploymentHeight, probe.MessageRouterDeploymentHeight);
            Assert.IsTrue(probe.ForcedInclusionDeploymentHeight > 0);
            Assert.IsTrue(probe.HasSettlementManagerHash);
            Assert.IsTrue(probe.HasForcedInclusionHash);
            Assert.IsTrue(probe.HasSharedBridgeHash);
            Assert.IsTrue(probe.HasMessageRouterHash);
            Assert.AreEqual(host.HasL2BridgeHash, probe.HasL2BridgeHash);
            Assert.AreEqual(host.L1FinalityDepth, probe.L1FinalityDepth);
            Assert.IsTrue(probe.HasDepositSource);
            Assert.IsTrue(probe.HasMessageRouter);
            Assert.IsTrue(probe.HasForcedInclusionFinalizer);
            Assert.IsTrue(probe.HasSettlementClient);
            Assert.IsTrue(probe.HasTransactionSender);
            Assert.IsTrue(probe.HasBatchDepositSource);
            Assert.IsTrue(probe.HasBatchMessageRouter);
            Assert.IsTrue(probe.HasBatchForcedInclusionSource);
            Assert.IsTrue(probe.IsDepositPipelineWiringComplete);
            Assert.IsTrue(probe.IsMessagePipelineWiringComplete);
            Assert.IsTrue(probe.IsForcedInclusionPipelineWiringComplete);
            Assert.IsTrue(probe.IsSettlementClientWiringComplete);
            Assert.IsTrue(probe.IsChainIdConfigConsistent);
            Assert.IsTrue(probe.IsProofTypeConfigConsistent);
            Assert.IsTrue(probe.IsDaModeConfigConsistent);
            Assert.IsTrue(probe.IsSecurityLevelProofTypeConsistent);
            Assert.IsTrue(probe.IsSecurityLevelDaModeConsistent);
            Assert.IsTrue(probe.IsNeoHubHashWiringComplete);
            Assert.IsTrue(probe.IsBatcherInboxWiringComplete);
            Assert.IsTrue(probe.IsPipelineEnabled);
            Assert.IsFalse(probe.HasPendingSealedBatch);
            Assert.IsNull(probe.PendingSealedBatchNumber);
            Assert.IsNull(probe.PendingSealedBatchLastBlock);
            Assert.IsTrue(probe.HasOpenBatch);
            Assert.AreEqual(host.MaxBlocksPerBatch, probe.MaxBlocksPerBatch);
            Assert.AreEqual(host.MaxTransactionsPerBatch, probe.MaxTransactionsPerBatch);
            Assert.AreEqual(host.MaxBatchAgeMillis, probe.MaxBatchAgeMillis);
            Assert.AreEqual(host.MaxForcedTransactionsPerBatch, probe.MaxForcedTransactionsPerBatch);
            Assert.AreEqual(host.MaxL1MessagesPerBatch, probe.MaxL1MessagesPerBatch);
            Assert.IsTrue(probe.MaxBlocksPerBatch > 0);
            Assert.IsTrue(probe.MaxBatchAgeMillis > 0);
            Assert.IsNotNull(probe.OpenBatchAgeMillis);
            Assert.IsFalse(probe.IsOpenBatchPastMaxAge);
            Assert.AreEqual(0, probe.InProgressTxCount);
            Assert.AreEqual(1UL, probe.OpenBatchFirstBlock);
            Assert.AreEqual(host.OpenBatchLastBlock, probe.OpenBatchLastBlock);
            Assert.AreEqual(host.OpenBatchBlockCount, probe.OpenBatchBlockCount);
            Assert.AreEqual(0, probe.OpenBatchL1MessageCount);
            Assert.AreEqual(0, probe.OpenBatchL2ToL1MessageCount);
            Assert.AreEqual(0, probe.OpenBatchL2ToL2MessageCount);
            Assert.AreEqual(0, probe.OpenBatchForcedInclusionCount);
            Assert.AreEqual(0, probe.OpenBatchWithdrawalCount);
            Assert.IsTrue(probe.IsBatcherCheckpointAligned);
            // Soft ProcessCommittedBlock advanced expected block without sealing.
            Assert.AreEqual(host.NextExpectedBlock, probe.NextExpectedBlock);
            Assert.AreEqual(0UL, probe.LastAcknowledgedBatchNumber);
            Assert.AreEqual(0UL, probe.LastAcknowledgedBlock);
            Assert.AreEqual(1UL, probe.NextBatchNumber);
            Assert.IsNull(probe.LatestCheckpointBatchNumber);
            Assert.IsNull(probe.LatestCheckpointLastBlock);
            Assert.IsNull(probe.LatestCheckpointPostStateRoot);
            Assert.IsFalse(probe.HasOverdueForcedInclusion);
            Assert.IsTrue(probe.IsPipelineHealthy);
            Assert.AreEqual(0, probe.PipelineHealthFailures.Count);
            Assert.IsFalse(probe.IsMetricsHttpListening);
            Assert.AreEqual(0, probe.MetricsBoundPort);
            Assert.IsTrue(probe.IsSettlementRuntimeIdle);
            Assert.IsTrue(probe.IsSettlementIdle);
            Assert.IsFalse(probe.IsSettlementPoisoned);
            Assert.IsFalse(probe.IsSettlementRetrying);
            Assert.AreEqual(0, probe.SettlementRetryCount);
            Assert.AreEqual(0, probe.SettlementConfirmationLagBatches);
            Assert.AreEqual(0, probe.PendingSettlementCount);
            Assert.IsNotNull(probe.Recovery);
            Assert.AreEqual(0, probe.Recovery.PendingCount);
            Assert.AreEqual(0, probe.Recovery.RetryCount);
            Assert.IsNull(probe.Recovery.State);
            Assert.IsNull(probe.Recovery.LastError);
            Assert.AreEqual(host.DepositSourceReadyCount, probe.DepositSourceReadyCount);
            Assert.AreEqual(0, probe.ReadyDepositCount);
            Assert.AreEqual(host.DepositSourceReservedCount, probe.DepositSourceReservedCount);
            Assert.AreEqual(host.DepositSourceSoftConsumedCount, probe.DepositSourceSoftConsumedCount);
            Assert.AreEqual(host.ConsumedDepositCount, probe.ConsumedDepositCount);
            Assert.AreEqual(host.L1InboxPendingCount, probe.L1InboxPendingCount);
            Assert.AreEqual(host.L1InboxConsumedCount, probe.L1InboxConsumedCount);
            Assert.IsTrue(probe.HasMessageOutbox);
            Assert.AreEqual(host.MessageOutbox?.L2ToL1Count ?? 0, probe.MessageOutboxL2ToL1Count);
            Assert.AreEqual(host.MessageOutbox?.L2ToL2Count ?? 0, probe.MessageOutboxL2ToL2Count);
            Assert.AreEqual(host.KnownInboundNonceCount, probe.KnownInboundNonceCount);
            Assert.AreEqual(host.KnownForcedInclusionNonceCount, probe.KnownForcedInclusionNonceCount);
            Assert.AreEqual(0, probe.TrackedForcedInclusionNonceCount);
            Assert.AreEqual(host.MessageOutboxL2ToL1Root.ToString(), probe.MessageOutboxL2ToL1Root);
            Assert.AreEqual(host.MessageOutboxL2ToL2Root.ToString(), probe.MessageOutboxL2ToL2Root);
            Assert.AreEqual(host.StagedWithdrawalCount, probe.StagedWithdrawalCount);
            var probePath = Path.Combine(chainDir, "health-probe.json");
            await host.WriteHealthProbeAsync(probePath);
            Assert.IsTrue(File.Exists(probePath));
            var probeJson = await File.ReadAllTextAsync(probePath);
            StringAssert.Contains(probeJson, "\"isOfflinePassportComplete\": true");
            StringAssert.Contains(probeJson, "\"isOperatorReady\": true");
            StringAssert.Contains(probeJson, "\"isProductionWired\": true");
            StringAssert.Contains(probeJson, "\"hasBatchProver\": true");
            StringAssert.Contains(probeJson, "\"isSettlementEnabled\": true");
            StringAssert.Contains(probeJson, "\"isBatcherEnabled\": true");
            StringAssert.Contains(probeJson, "\"isDepositPipelineWiringComplete\": true");
            StringAssert.Contains(probeJson, "\"isMessagePipelineWiringComplete\": true");
            StringAssert.Contains(probeJson, "\"isPipelineEnabled\": true");
            StringAssert.Contains(probeJson, "\"hasPendingSealedBatch\": false");
            StringAssert.Contains(probeJson, "\"settlementRetryCount\": 0");
            StringAssert.Contains(probeJson, "\"settlementConfirmationLagBatches\": 0");
            StringAssert.Contains(probeJson, "\"hasOpenBatch\": true");
            StringAssert.Contains(probeJson, "\"isOpenBatchPastMaxAge\": false");
            StringAssert.Contains(probeJson, "\"inProgressTxCount\": 0");
            StringAssert.Contains(probeJson, "\"openBatchBlockCount\": " + host.OpenBatchBlockCount);
            StringAssert.Contains(probeJson, "\"openBatchL1MessageCount\": 0");
            StringAssert.Contains(probeJson, "\"openBatchForcedInclusionCount\": 0");
            StringAssert.Contains(probeJson, "\"openBatchWithdrawalCount\": 0");
            StringAssert.Contains(probeJson, "\"isBatcherCheckpointAligned\": true");
            StringAssert.Contains(probeJson, "\"lastAcknowledgedBatchNumber\": 0");
            StringAssert.Contains(probeJson, "\"nextBatchNumber\": 1");
            StringAssert.Contains(probeJson, "\"hasOverdueForcedInclusion\": false");
            StringAssert.Contains(probeJson, "\"isPipelineHealthy\": true");
            StringAssert.Contains(probeJson, "\"isSettlementRuntimeIdle\": true");
            StringAssert.Contains(probeJson, "\"isSettlementPoisoned\": false");
            StringAssert.Contains(probeJson, "\"isSettlementRetrying\": false");
            StringAssert.Contains(probeJson, "\"pendingSettlementCount\": 0");
            StringAssert.Contains(probeJson, "\"depositSourceReadyCount\": 0");
            StringAssert.Contains(probeJson, "\"depositSourceReservedCount\": 0");
            StringAssert.Contains(probeJson, "\"depositSourceSoftConsumedCount\":");
            StringAssert.Contains(probeJson, $"\"consumedDepositCount\": {host.ConsumedDepositCount}");
            StringAssert.Contains(probeJson, "\"l1InboxPendingCount\":");
            StringAssert.Contains(probeJson, "\"l1InboxConsumedCount\":");
            StringAssert.Contains(probeJson, "\"hasMessageOutbox\": true");
            StringAssert.Contains(probeJson, "\"messageOutboxL2ToL1Count\":");
            StringAssert.Contains(probeJson, "\"messageOutboxL2ToL2Count\":");
            StringAssert.Contains(probeJson, "\"knownInboundNonceCount\":");
            StringAssert.Contains(probeJson, "\"knownForcedInclusionNonceCount\":");
            StringAssert.Contains(probeJson, "\"stagedWithdrawalCount\":");
            StringAssert.Contains(probeJson, "\"nextExpectedBlock\": " + host.NextExpectedBlock);
            StringAssert.Contains(probeJson, "\"latestCheckpointBatchNumber\": null");
            StringAssert.Contains(probeJson, "\"chainId\": 20260716");
            StringAssert.Contains(probeJson, "\"proofType\": \"Multisig\"");
            StringAssert.Contains(probeJson, "\"daMode\":");
            StringAssert.Contains(probeJson, "\"securityLevel\":");
            StringAssert.Contains(probeJson, "\"isMetricsWiringComplete\":");
            StringAssert.Contains(probeJson, "\"metricsConfiguredPort\":");
            StringAssert.Contains(probeJson, "\"hasSettlementManagerHash\": true");
            StringAssert.Contains(probeJson, "\"hasForcedInclusionHash\": true");
            StringAssert.Contains(probeJson, "\"hasSharedBridgeHash\": true");
            StringAssert.Contains(probeJson, "\"hasMessageRouterHash\": true");
            StringAssert.Contains(probeJson, "\"hasBatchDepositSource\": true");
            StringAssert.Contains(probeJson, "\"hasBatchMessageRouter\": true");
            StringAssert.Contains(probeJson, "\"hasBatchForcedInclusionSource\": true");
            StringAssert.Contains(probeJson, "\"forcedInclusionDeploymentHeight\":");
            StringAssert.Contains(probeJson, "\"sharedBridgeDeploymentHeight\":");
            StringAssert.Contains(probeJson, "\"messageRouterDeploymentHeight\":");
            StringAssert.Contains(probeJson, "\"l1FinalityDepth\":");
            StringAssert.Contains(probeJson, "\"maxBlocksPerBatch\":");
            StringAssert.Contains(probeJson, "\"maxTransactionsPerBatch\":");
            StringAssert.Contains(probeJson, "\"maxBatchAgeMillis\":");
            StringAssert.Contains(probeJson, "\"maxForcedTransactionsPerBatch\":");
            StringAssert.Contains(probeJson, "\"maxL1MessagesPerBatch\":");
            StringAssert.Contains(probeJson, "\"metricsBindAddress\":");
            StringAssert.Contains(probeJson, "\"supportsLocalDaReader\": true");
            StringAssert.Contains(probeJson, "\"bridgeAssetCount\":");
            StringAssert.Contains(probeJson, "\"metricsEntryCount\":");
            StringAssert.Contains(probeJson, "\"gatewayEnabled\":");
            StringAssert.Contains(probeJson, "\"sequencer\":");
            StringAssert.Contains(probeJson, "\"exit\":");
            StringAssert.Contains(probeJson, "\"initialStateRoot\":");
            StringAssert.Contains(probeJson, "\"latestRpcStateRoot\":");
            StringAssert.Contains(probeJson, "\"latestCheckpointPostStateRoot\": null");
            StringAssert.Contains(probeJson, "\"messageOutboxL2ToL1Root\":");
            StringAssert.Contains(probeJson, "\"messageOutboxL2ToL2Root\":");
            StringAssert.Contains(probeJson, "\"trackedForcedInclusionNonceCount\": 0");
            StringAssert.Contains(probeJson, "\"isSettlementIdle\": true");
            StringAssert.Contains(probeJson, "\"readyDepositCount\": 0");
            StringAssert.Contains(probeJson, "\"recovery\":");
            StringAssert.Contains(probeJson, "\"pendingCount\": 0");
            StringAssert.Contains(probeJson, "\"retryCount\": 0");
            // Compact probe still omits full inventory (e.g. full deposit peeks).
            Assert.IsFalse(probeJson.Contains("\"readyDeposits\"", StringComparison.Ordinal));
            StringAssert.Contains(statusJson, "\"chainId\": 20260716");
            StringAssert.Contains(statusJson, "\"batcherConfiguredChainId\": 20260716");
            StringAssert.Contains(statusJson, "\"settlementConfiguredChainId\": 20260716");
            StringAssert.Contains(statusJson, "\"settlementConfiguredProofType\": \"Multisig\"");
            StringAssert.Contains(statusJson, "\"isOperatorReady\": true");
            StringAssert.Contains(statusJson, "\"nextExpectedBlock\": " + host.NextExpectedBlock);
            StringAssert.Contains(statusJson, "\"hasPendingSealedBatch\": false");
            StringAssert.Contains(statusJson, "\"isBatcherEnabled\": true");
            StringAssert.Contains(statusJson, "\"maxBlocksPerBatch\":");
            StringAssert.Contains(statusJson, "\"maxTransactionsPerBatch\":");
            StringAssert.Contains(statusJson, "\"maxBatchAgeMillis\":");
            StringAssert.Contains(statusJson, "\"hasOpenBatch\": true");
            StringAssert.Contains(statusJson, "\"inProgressTxCount\": 0");
            StringAssert.Contains(statusJson, "\"openBatchBlockCount\": " + host.OpenBatchBlockCount);
            StringAssert.Contains(statusJson, "\"openBatchL1MessageCount\": 0");
            StringAssert.Contains(statusJson, "\"openBatchForcedInclusionCount\": 0");
            StringAssert.Contains(statusJson, "\"openBatchL2ToL2MessageCount\": 0");
            StringAssert.Contains(statusJson, "\"openBatchWithdrawalCount\": 0");
            StringAssert.Contains(statusJson, "\"supportsLocalDaReader\": true");
            StringAssert.Contains(statusJson, "\"hasL1RpcEndpoint\": true");
            StringAssert.Contains(statusJson, "\"expectedNetwork\":");
            StringAssert.Contains(statusJson, "\"hasSettlementManagerHash\": true");
            StringAssert.Contains(statusJson, "\"hasForcedInclusionHash\": true");
            StringAssert.Contains(statusJson, "\"hasSharedBridgeHash\": true");
            StringAssert.Contains(statusJson, "\"hasMessageRouterHash\": true");
            StringAssert.Contains(statusJson, "\"hasL2BridgeHash\": false");
            StringAssert.Contains(statusJson, "\"hasMessageOutbox\": true");
            StringAssert.Contains(statusJson, "\"consumedDepositCount\": 1");
            StringAssert.Contains(statusJson, "\"lastAcknowledgedBatchNumber\": 0");
            StringAssert.Contains(statusJson, "\"nextBatchNumber\": 1");
            StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Count\": 1");
            StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Root\":");
            StringAssert.Contains(statusJson, "\"stagedWithdrawalCount\": 0");
            StringAssert.Contains(statusJson, "\"hasBatchProver\": true");
            StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": null");
            StringAssert.Contains(statusJson, "\"initialStateRoot\":");
            StringAssert.Contains(statusJson, "\"firstFailureAtUnixMilliseconds\": null");
            StringAssert.Contains(statusJson, "\"lastFailureAtUnixMilliseconds\": null");
            var promPath = Path.Combine(chainDir, "metrics.prom");
            await host.WritePrometheusMetricsAsync(promPath);
            Assert.IsTrue(File.Exists(promPath));
            Assert.IsFalse(string.IsNullOrWhiteSpace(await File.ReadAllTextAsync(promPath)));
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

    /// <summary>
    /// Soft seal→settlement hand-off: MaxBlocksPerBatch=1 + pass-through executor + local DA.
    /// Does not claim L1 settle broadcast (funded gate).
    /// </summary>
    [TestMethod]
    public void SoftSeal_EmptyBlock_PersistsLocalCheckpoint_Multisig()
    {
        var reportPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "docs", "audit", "testnet-deployment-20260716-live.json"));
        if (!File.Exists(reportPath))
            Assert.Inconclusive($"repo evidence file not found at {reportPath}");

        var chainDir = Path.Combine(
            Path.GetTempPath(),
            "neo-n4-msig-soft-seal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(chainDir);
        try
        {
            MaterializeMultisigChain(chainDir, reportPath);
            RewriteMaxBlocksPerBatch(chainDir, 1);
            using var http = CanonicalRootHttpClient();
            using var host = MultisigLocalHostComposition.Open(
                chainDir,
                new SoftPassThroughExecutor(),
                SampleSigners(),
                new StubSigner(Account(0x44)),
                rpcHttpClient: http);

            Assert.AreEqual(1, host.MaxBlocksPerBatch);
            Assert.IsTrue(host.IsOperatorReady);
            Assert.IsTrue(host.HasSealedBatchSink);
            Assert.IsNull(host.GetLatestDurableCheckpointAsync().AsTask().GetAwaiter().GetResult());

            var openBatchTimestampMs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            host.ProcessCommittedBlock(1, openBatchTimestampMs, 894710606, Array.Empty<byte[]>());

            // Seal + local PersistAsync completed: no open batch / pending seal; durable checkpoint #1.
            // GetPendingCountAsync remains 1 until L1 settle (funded gate) — not settlement-idle.
            Assert.IsFalse(host.HasOpenBatch);
            Assert.IsFalse(host.HasPendingSealedBatch);
            Assert.AreEqual(2UL, host.NextExpectedBlock);
            Assert.AreEqual(1UL, host.LastAcknowledgedBatchNumber);
            Assert.AreEqual(1UL, host.LastAcknowledgedBlock);
            Assert.AreEqual(2UL, host.NextBatchNumber);
            Assert.AreEqual(1, host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult());

            var checkpoint = host.GetLatestDurableCheckpointAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsNotNull(checkpoint);
            Assert.AreEqual(1UL, checkpoint!.BatchNumber);
            Assert.AreEqual(1UL, checkpoint.LastBlock);
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, checkpoint.PostStateRoot);

            // Soft local DA after seal: publish sealed batch payload, availability + reader round-trip.
            Assert.IsTrue(host.SupportsLocalDaReader);
            var softDaPayload = new byte[] { 0xDA, 0x51, 0x01 };
            var softDaReceipt = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = checkpoint.BatchNumber,
                Payload = softDaPayload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(DAMode.Local, softDaReceipt.Layer);
            Assert.AreEqual(DAReceiptKind.LocalPersistence, softDaReceipt.Kind);
            Assert.IsTrue(host.IsDaAvailableAsync(softDaReceipt).AsTask().GetAwaiter().GetResult());
            var softDaReader = host.CreateLocalDaReader();
            Assert.IsNotNull(softDaReader);
            var softDaRead = softDaReader.ReadAsync(softDaReceipt).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(softDaRead is { Length: 3 });
            CollectionAssert.AreEqual(softDaPayload, softDaRead!.Value.ToArray());
            var softDaPath = Path.Combine(chainDir, "soft-seal-da-surface.json");
            File.WriteAllText(softDaPath, $$"""
                {
                  "batchNumber": {{checkpoint.BatchNumber}},
                  "layer": "{{softDaReceipt.Layer}}",
                  "kind": "{{softDaReceipt.Kind}}",
                  "commitment": "{{softDaReceipt.Commitment}}",
                  "payloadBytes": {{softDaPayload.Length}},
                  "supportsLocalDaReader": true
                }
                """);
            Assert.IsTrue(File.Exists(softDaPath));
            StringAssert.Contains(File.ReadAllText(softDaPath), "\"supportsLocalDaReader\": true");

            // Second soft seal while batch 1 still pending L1 (before inbound nonce registration).
            Assert.AreEqual(2UL, host.NextExpectedBlock);
            Assert.AreEqual(1UL, host.LastAcknowledgedBatchNumber);
            var ts2 = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            host.ProcessCommittedBlock(2, ts2, 894710606, Array.Empty<byte[]>());
            Assert.IsFalse(host.HasOpenBatch);
            Assert.IsFalse(host.HasPendingSealedBatch);
            Assert.AreEqual(3UL, host.NextExpectedBlock);
            Assert.AreEqual(2UL, host.LastAcknowledgedBatchNumber);
            Assert.AreEqual(2UL, host.LastAcknowledgedBlock);
            Assert.AreEqual(3UL, host.NextBatchNumber);
            Assert.IsTrue(host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult() >= 2);
            var checkpoint2 = host.GetLatestDurableCheckpointAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsNotNull(checkpoint2);
            Assert.AreEqual(2UL, checkpoint2!.BatchNumber);
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, checkpoint2.PostStateRoot);
            var da2 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 2,
                Payload = new byte[] { 0xDA, 0x52, 0x02 },
            }).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(DAMode.Local, da2.Layer);
            Assert.IsTrue(host.IsDaAvailableAsync(da2).AsTask().GetAwaiter().GetResult());
            var afterBatch2Path = Path.Combine(chainDir, "soft-seal-second-batch-status.json");
            host.WriteOperatorStatusAsync(afterBatch2Path).AsTask().GetAwaiter().GetResult();
            StringAssert.Contains(File.ReadAllText(afterBatch2Path), "\"latestCheckpointBatchNumber\": 2");

            // Offline bridge while settlement still Retrying (no L1 settle required).
            var softL1Asset = UInt160.Parse("0x" + new string('1', 40));
            var softL2Asset = UInt160.Parse("0x" + new string('2', 40));
            host.RegisterBridgeAsset(new AssetMapping
            {
                L1Asset = softL1Asset,
                L2Asset = softL2Asset,
                L2ChainId = 20260716u,
                L1Decimals = 8,
                L2Decimals = 8,
                AssetType = AssetType.Gas,
                MintBurn = true,
                LockMint = true,
                Active = true,
            });
            var softDepositPayload = new DepositPayload
            {
                L1Asset = softL1Asset,
                L2Recipient = Account(0x55),
                Amount = new BigInteger(1_000),
            };
            var softDepositMsg = new CrossChainMessage
            {
                SourceChainId = 0,
                TargetChainId = 20260716u,
                Nonce = 1,
                Sender = Account(0x66),
                Receiver = Account(0x55),
                MessageType = MessageType.Deposit,
                Payload = softDepositPayload.Encode(),
                MessageHash = UInt256.Zero,
            };
            var softMint = host.ProcessDeposit(softDepositMsg);
            Assert.AreEqual(softL2Asset, softMint.L2Asset);
            Assert.IsTrue(host.HasConsumedDeposit(0, 1));
            Assert.AreEqual(1, host.ConsumedDepositCount);
            host.RecordRpcDeposit(new DepositStatus(0, 1, ConsumedOnL2: true, IncludedInBatch: checkpoint.BatchNumber));
            Assert.IsTrue(host.GetRpcL1DepositStatus(0, 1) is { ConsumedOnL2: true, IncludedInBatch: 1UL });
            var softSender = Account(0x77);
            var softWdLeaf = host.StageWithdrawal(new WithdrawalRequest
            {
                ChainId = 20260716u,
                EmittingContract = softSender,
                L2Sender = softSender,
                L1Recipient = softSender,
                L2Asset = softL2Asset,
                Amount = new BigInteger(50),
                Nonce = 1,
            });
            Assert.AreNotEqual(UInt256.Zero, softWdLeaf);
            var softSealedWd = host.SealWithdrawalBatch();
            Assert.AreNotEqual(UInt256.Zero, softSealedWd.Root);
            Assert.AreEqual(0, host.StagedWithdrawalCount);
            var softOutboundDraft = new CrossChainMessage
            {
                SourceChainId = 20260716u,
                TargetChainId = 0,
                Nonce = 9,
                Sender = softSender,
                Receiver = softSender,
                MessageType = MessageType.Event,
                Payload = new byte[] { 0x01 },
                MessageHash = UInt256.Zero,
            };
            var softOutbound = softOutboundDraft with { MessageHash = MessageHasher.HashMessage(softOutboundDraft) };
            host.EnqueueOutboundMessagesAsync([softOutbound]).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(1, host.MessageOutbox!.L2ToL1Count);

            // Soft FI + inbound bookkeeping while settlement still Retrying (no L1 drain).
            Assert.IsTrue(host.RegisterForcedInclusionNonce(11));
            Assert.IsFalse(host.RegisterForcedInclusionNonce(11));
            Assert.AreEqual(1, host.KnownForcedInclusionNonceCount);
            Assert.AreEqual(0, host.OpenBatchForcedInclusionCount);
            Assert.IsFalse(host.HasOverdueForcedInclusionCached());
            host.InvalidateForcedInclusionCache();
            Assert.AreEqual(1, host.KnownForcedInclusionNonceCount);
            Assert.IsTrue(host.RegisterInboundMessageNonce(11));
            Assert.IsFalse(host.RegisterInboundMessageNonce(11));
            Assert.AreEqual(1, host.KnownInboundNonceCount);
            Assert.AreEqual(0, host.OpenBatchL1MessageCount);
            Assert.AreEqual(0, host.L1InboxPendingCount);
            host.InvalidateInboundMessageCache();
            Assert.AreEqual(1, host.KnownInboundNonceCount);

            var status = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(2UL, status.LatestCheckpointBatchNumber);
            Assert.AreEqual(2UL, status.LatestCheckpointLastBlock);
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, status.LatestCheckpointPostStateRoot);
            Assert.IsTrue(status.PendingSettlementCount >= 2);
            Assert.IsFalse(status.IsSettlementIdle);
            Assert.IsFalse(status.HasPendingSealedBatch);
            Assert.IsFalse(status.HasOpenBatch);
            Assert.IsTrue(status.IsBatcherCheckpointAligned);
            Assert.IsTrue(host.IsBatcherCheckpointAlignedAsync().AsTask().GetAwaiter().GetResult());
            // Offline passport still complete; pipeline unhealthy from settlement Retrying
            // after mock L1 settle fails (preferred recovery label over generic IsSettlementIdle).
            Assert.IsTrue(status.IsOfflinePassportComplete);
            Assert.AreEqual(0, status.OfflinePassportFailures.Count);
            Assert.IsFalse(status.IsPipelineHealthy);
            Assert.IsTrue(status.IsSettlementRetrying);
            Assert.AreEqual(1, status.ConsumedDepositCount);
            Assert.AreEqual(1, status.MessageOutboxL2ToL1Count);
            Assert.AreEqual(1, status.KnownForcedInclusionNonceCount);
            Assert.AreEqual(1, status.KnownInboundNonceCount);
            Assert.IsFalse(status.HasOverdueForcedInclusion);
            CollectionAssert.DoesNotContain(
                status.PipelineHealthFailures.ToArray(),
                nameof(status.HasOverdueForcedInclusion));
            Assert.IsFalse(status.IsSettlementPoisoned);
            CollectionAssert.Contains(
                status.PipelineHealthFailures.ToArray(),
                nameof(status.IsSettlementRetrying));
            CollectionAssert.DoesNotContain(
                status.PipelineHealthFailures.ToArray(),
                nameof(status.IsSettlementIdle));
            Assert.IsTrue(status.SettlementRetryCount >= 1);
            var recovery = host.GetRecoveryStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(SettlementRecoveryState.Retrying, recovery.State);
            Assert.IsTrue(recovery.PendingCount >= 2);
            Assert.IsFalse(string.IsNullOrEmpty(recovery.LastError));
            Assert.IsTrue(recovery.RetryCount >= 1);

            var probe = host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(2UL, probe.LatestCheckpointBatchNumber);
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot.ToString(), probe.LatestCheckpointPostStateRoot);
            Assert.IsTrue(probe.PendingSettlementCount >= 2);
            Assert.IsFalse(probe.IsSettlementIdle);
            Assert.IsFalse(probe.HasOpenBatch);
            Assert.IsFalse(probe.HasPendingSealedBatch);
            Assert.IsTrue(probe.IsOfflinePassportComplete);
            Assert.IsFalse(probe.IsPipelineHealthy);
            Assert.IsTrue(probe.IsSettlementRetrying);
            CollectionAssert.Contains(probe.PipelineHealthFailures.ToArray(), "IsSettlementRetrying");
            // Async helpers + rollup host health + ops JSON (retrying soft-seal surface).
            CollectionAssert.Contains(
                host.GetPipelineHealthFailuresAsync().AsTask().GetAwaiter().GetResult().ToArray(),
                "IsSettlementRetrying");
            Assert.IsFalse(status.IsLocalHostHealthy);
            CollectionAssert.Contains(status.LocalHostHealthFailures.ToArray(), "IsSettlementRetrying");
            Assert.IsFalse(host.IsLocalHostHealthyAsync().AsTask().GetAwaiter().GetResult());
            var softSealStatusJson = host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult();
            StringAssert.Contains(softSealStatusJson, "\"isSettlementRetrying\": true");
            StringAssert.Contains(softSealStatusJson, "\"isPipelineHealthy\": false");
            StringAssert.Contains(softSealStatusJson, "\"isOfflinePassportComplete\": true");
            StringAssert.Contains(softSealStatusJson, "IsSettlementRetrying");
            StringAssert.Contains(softSealStatusJson, "\"latestCheckpointBatchNumber\": 2");
            StringAssert.Contains(softSealStatusJson, "\"pendingSettlementCount\": " + status.PendingSettlementCount);
            StringAssert.Contains(
                softSealStatusJson,
                "\"latestCheckpointPostStateRoot\": \"" + SoftPassThroughExecutor.PostStateRoot + "\"");
            // Durable ops files for soft-seal retry surface (operator scripts without metrics HTTP).
            var softSealStatusPath = Path.Combine(chainDir, "soft-seal-operator-status.json");
            host.WriteOperatorStatusAsync(softSealStatusPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(softSealStatusPath));
            var softSealStatusFile = File.ReadAllText(softSealStatusPath);
            StringAssert.Contains(softSealStatusFile, "\"isSettlementRetrying\": true");
            StringAssert.Contains(softSealStatusFile, "\"isPipelineHealthy\": false");
            StringAssert.Contains(softSealStatusFile, "IsSettlementRetrying");
            StringAssert.Contains(softSealStatusFile, "\"latestCheckpointBatchNumber\": 2");
            var softSealProbeJson = host.FormatHealthProbeJson();
            StringAssert.Contains(softSealProbeJson, "\"isSettlementRetrying\": true");
            StringAssert.Contains(softSealProbeJson, "\"isPipelineHealthy\": false");
            StringAssert.Contains(softSealProbeJson, "IsSettlementRetrying");
            StringAssert.Contains(softSealProbeJson, "\"latestCheckpointBatchNumber\": 2");
            StringAssert.Contains(
                softSealProbeJson,
                "\"latestCheckpointPostStateRoot\": \"" + SoftPassThroughExecutor.PostStateRoot + "\"");
            var softSealProbePath = Path.Combine(chainDir, "soft-seal-health-probe.json");
            host.WriteHealthProbeAsync(softSealProbePath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(softSealProbePath));
            var softSealProbeFile = File.ReadAllText(softSealProbePath);
            StringAssert.Contains(softSealProbeFile, "\"isSettlementRetrying\": true");
            StringAssert.Contains(softSealProbeFile, "\"isPipelineHealthy\": false");
            StringAssert.Contains(softSealProbeFile, "IsSettlementRetrying");
            StringAssert.Contains(softSealProbeFile, "\"latestCheckpointBatchNumber\": 2");

            // Soft seal → gateway aggregator (no L1 PublishAggregate).
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
                ProofType = ProofType.Multisig,
                Proof = new byte[] { 0xB1 },
            };
            host.AddRpcBatch(commitment, BatchStatus.Finalized);
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(1));
            var rpcBatch = host.GetRpcBatch(1);
            Assert.IsNotNull(rpcBatch);
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, rpcBatch!.PostStateRoot);
            // Per-batch root is recorded on Add; latest tip advances only via Finalize.
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, host.GetRpcStateRootAtBatch(1));
            host.FinalizeRpcBatch(1);
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, host.GetLatestRpcStateRoot());
            var statusAfterFinalize = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, statusAfterFinalize.LatestRpcStateRoot);
            Assert.IsTrue(statusAfterFinalize.IsSettlementRetrying);
            Assert.IsFalse(statusAfterFinalize.IsPipelineHealthy);
            var statusAfterFinalizeJson = host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult();
            StringAssert.Contains(
                statusAfterFinalizeJson,
                "\"latestRpcStateRoot\": \"" + SoftPassThroughExecutor.PostStateRoot + "\"");
            StringAssert.Contains(statusAfterFinalizeJson, "\"isSettlementRetrying\": true");
            var probeAfterFinalize = host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(
                SoftPassThroughExecutor.PostStateRoot.ToString(),
                probeAfterFinalize.LatestRpcStateRoot);
            Assert.IsTrue(probeAfterFinalize.IsSettlementRetrying);
            // Soft-seal host metrics scrape file (no metrics HTTP required).
            var hostProm = host.ExportPrometheusMetrics();
            Assert.IsFalse(string.IsNullOrWhiteSpace(hostProm));
            var hostPromPath = Path.Combine(chainDir, "soft-seal-host.prom");
            host.WritePrometheusMetricsAsync(hostPromPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(hostPromPath));
            Assert.AreEqual(hostProm, File.ReadAllText(hostPromPath));

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
                metricsPlugin: host.Metrics);
            gatewayHost.ReceiveBatch(commitment with
            {
                ChainId = 20260717u,
                L2ToL2MessageRoot = Root(0xAC),
                Proof = new byte[] { 0xB2 },
            });
            gatewayHost.ReceiveBatch(commitment with { Proof = new byte[] { 0xB3 } });
            Assert.IsTrue(gatewayHost.AggregatorPendingCount >= 1);
            // Durable outbox path: direct PullAggregate bypasses publication outbox (fail-closed).
            Assert.ThrowsExactly<InvalidOperationException>(() => gatewayHost.PullAggregate());
            var gwStatus = gatewayHost.GetOperatorStatus();
            Assert.AreEqual(gatewayHost.AggregatorPendingCount, gwStatus.AggregatorPendingCount);
            // Soft aggregate pending is not L1 publication: HasPendingPublication stays false,
            // but outbox/publication health flags the aggregator backlog until PublishAggregate.
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
            var gwStatusFile = File.ReadAllText(gwStatusPath);
            Assert.AreEqual(gwJson, gwStatusFile);
            StringAssert.Contains(gwStatusFile, "AggregatorPendingCount");
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
            // Shared metrics plugin: gateway scrape file after ReceiveBatch backlog.
            var gwProm = gatewayHost.ExportPrometheusMetrics();
            Assert.IsFalse(string.IsNullOrWhiteSpace(gwProm));
            var gwPromPath = Path.Combine(chainDir, "soft-seal-gateway.prom");
            gatewayHost.WritePrometheusMetricsAsync(gwPromPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(gwPromPath));
            Assert.AreEqual(gwProm, File.ReadAllText(gwPromPath));

            // Reconcile against mock L1 fails closed; further SubmitNext escalates durable recovery
            // to Poisoned (preferred label over Retrying). Pending settle remains until funded L1.
            Assert.ThrowsExactly<OverflowException>(
                () => host.ReconcileAsync().GetAwaiter().GetResult());
            host.SubmitNextAsync().GetAwaiter().GetResult();
            Assert.IsTrue(host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult() >= 1);
            var afterSubmit = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsFalse(afterSubmit.IsSettlementIdle);
            Assert.IsTrue(afterSubmit.IsSettlementPoisoned);
            Assert.IsFalse(afterSubmit.IsSettlementRetrying);
            Assert.IsFalse(afterSubmit.IsPipelineHealthy);
            CollectionAssert.Contains(
                afterSubmit.PipelineHealthFailures.ToArray(),
                nameof(afterSubmit.IsSettlementPoisoned));
            CollectionAssert.DoesNotContain(
                afterSubmit.PipelineHealthFailures.ToArray(),
                nameof(afterSubmit.IsSettlementRetrying));
            Assert.AreEqual(SettlementRecoveryState.Poisoned, afterSubmit.Recovery.State);
            Assert.IsFalse(string.IsNullOrEmpty(afterSubmit.Recovery.LastError));
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, afterSubmit.LatestRpcStateRoot);
            Assert.IsTrue(afterSubmit.SettlementRetryCount >= 1);
            CollectionAssert.Contains(
                host.GetPipelineHealthFailuresAsync().AsTask().GetAwaiter().GetResult().ToArray(),
                "IsSettlementPoisoned");
            var afterSubmitJson = host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult();
            StringAssert.Contains(afterSubmitJson, "\"isSettlementPoisoned\": true");
            StringAssert.Contains(afterSubmitJson, "\"isSettlementRetrying\": false");
            StringAssert.Contains(afterSubmitJson, "\"isPipelineHealthy\": false");
            StringAssert.Contains(afterSubmitJson, "IsSettlementPoisoned");
            StringAssert.Contains(
                afterSubmitJson,
                "\"latestRpcStateRoot\": \"" + SoftPassThroughExecutor.PostStateRoot + "\"");
            var afterSubmitStatusPath = Path.Combine(chainDir, "soft-seal-after-submit-status.json");
            host.WriteOperatorStatusAsync(afterSubmitStatusPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(afterSubmitStatusPath));
            StringAssert.Contains(File.ReadAllText(afterSubmitStatusPath), "\"isSettlementPoisoned\": true");
            Assert.IsTrue(host.IsSettlementPoisonedAsync().AsTask().GetAwaiter().GetResult());
            Assert.IsFalse(host.IsSettlementRetryingAsync().AsTask().GetAwaiter().GetResult());
            var afterPoisonProbeJson = host.FormatHealthProbeJson();
            StringAssert.Contains(afterPoisonProbeJson, "\"isSettlementPoisoned\": true");
            StringAssert.Contains(afterPoisonProbeJson, "\"isSettlementRetrying\": false");
            StringAssert.Contains(afterPoisonProbeJson, "IsSettlementPoisoned");
            var afterPoisonProbePath = Path.Combine(chainDir, "soft-seal-after-poison-probe.json");
            host.WriteHealthProbeAsync(afterPoisonProbePath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(afterPoisonProbePath));
            StringAssert.Contains(File.ReadAllText(afterPoisonProbePath), "\"isSettlementPoisoned\": true");

            // Local operator recovery: wrong content hash fail-closed; correct hash resets Retrying.
            Assert.IsNotNull(afterSubmit.Recovery.BlockedBatchNumber);
            Assert.IsNotNull(afterSubmit.Recovery.ArtifactContentHash);
            var blockedBatch = afterSubmit.Recovery.BlockedBatchNumber!.Value;
            var contentHash = afterSubmit.Recovery.ArtifactContentHash!;
            Assert.ThrowsExactly<InvalidOperationException>(
                () => host.RecoverPoisonedBatchAsync(blockedBatch, UInt256.Zero)
                    .GetAwaiter().GetResult());
            Assert.IsTrue(host.IsSettlementPoisonedAsync().AsTask().GetAwaiter().GetResult());
            Assert.ThrowsExactly<InvalidOperationException>(
                () => host.RecoverPoisonedBatchAsync(blockedBatch + 99, contentHash)
                    .GetAwaiter().GetResult());
            Assert.IsTrue(host.IsSettlementPoisonedAsync().AsTask().GetAwaiter().GetResult());
            host.RecoverPoisonedBatchAsync(blockedBatch, contentHash).GetAwaiter().GetResult();
            Assert.IsFalse(host.IsSettlementPoisonedAsync().AsTask().GetAwaiter().GetResult());
            Assert.IsTrue(host.IsSettlementRetryingAsync().AsTask().GetAwaiter().GetResult());
            var afterRecover = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            var recoveryAfter = host.GetRecoveryStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(afterRecover.Recovery.State, recoveryAfter.State);
            Assert.AreEqual(afterRecover.Recovery.RetryCount, recoveryAfter.RetryCount);
            Assert.IsFalse(afterRecover.IsSettlementPoisoned);
            Assert.IsTrue(afterRecover.IsSettlementRetrying);
            Assert.IsFalse(afterRecover.IsSettlementIdle);
            Assert.IsFalse(afterRecover.IsPipelineHealthy);
            CollectionAssert.Contains(
                afterRecover.PipelineHealthFailures.ToArray(),
                nameof(afterRecover.IsSettlementRetrying));
            CollectionAssert.DoesNotContain(
                afterRecover.PipelineHealthFailures.ToArray(),
                nameof(afterRecover.IsSettlementPoisoned));
            Assert.AreEqual(SettlementRecoveryState.Retrying, afterRecover.Recovery.State);
            Assert.AreEqual(0, afterRecover.Recovery.RetryCount);
            Assert.AreEqual(0, afterRecover.SettlementRetryCount);
            Assert.IsTrue(host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult() >= 1);
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, afterRecover.LatestRpcStateRoot);
            // Wiring passport and batcher/checkpoint alignment remain complete after recover.
            Assert.IsTrue(afterRecover.IsOfflinePassportComplete);
            Assert.AreEqual(0, afterRecover.OfflinePassportFailures.Count);
            Assert.IsTrue(afterRecover.IsOperatorReady);
            Assert.IsTrue(afterRecover.IsBatcherCheckpointAligned);
            Assert.IsTrue(host.IsBatcherCheckpointAlignedAsync().AsTask().GetAwaiter().GetResult());
            // Soft offline bridge + FI/inbound bookkeeping survives poison→recover.
            Assert.IsTrue(host.HasConsumedDeposit(0, 1));
            Assert.AreEqual(1, afterRecover.ConsumedDepositCount);
            Assert.AreEqual(1, afterRecover.MessageOutboxL2ToL1Count);
            Assert.AreEqual(1, afterRecover.KnownForcedInclusionNonceCount);
            Assert.AreEqual(1, afterRecover.KnownInboundNonceCount);
            Assert.IsFalse(afterRecover.HasOverdueForcedInclusion);
            Assert.AreEqual(2UL, afterRecover.LatestCheckpointBatchNumber);
            Assert.IsTrue(host.GetRpcL1DepositStatus(0, 1) is { ConsumedOnL2: true, IncludedInBatch: 1UL });
            var afterRecoverJson = host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult();
            StringAssert.Contains(afterRecoverJson, "\"isSettlementPoisoned\": false");
            StringAssert.Contains(afterRecoverJson, "\"isSettlementRetrying\": true");
            StringAssert.Contains(afterRecoverJson, "IsSettlementRetrying");
            StringAssert.Contains(afterRecoverJson, "\"isOfflinePassportComplete\": true");
            StringAssert.Contains(afterRecoverJson, "\"isBatcherCheckpointAligned\": true");
            StringAssert.Contains(afterRecoverJson, "\"consumedDepositCount\": 1");
            StringAssert.Contains(afterRecoverJson, "\"knownForcedInclusionNonceCount\": 1");
            StringAssert.Contains(afterRecoverJson, "\"knownInboundNonceCount\": 1");
            StringAssert.Contains(afterRecoverJson, "\"latestCheckpointBatchNumber\": 2");
            var afterRecoverStatusPath = Path.Combine(chainDir, "soft-seal-after-recover-status.json");
            host.WriteOperatorStatusAsync(afterRecoverStatusPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(afterRecoverStatusPath));
            StringAssert.Contains(File.ReadAllText(afterRecoverStatusPath), "\"isSettlementRetrying\": true");
            StringAssert.Contains(File.ReadAllText(afterRecoverStatusPath), "\"consumedDepositCount\": 1");
            StringAssert.Contains(File.ReadAllText(afterRecoverStatusPath), "\"knownForcedInclusionNonceCount\": 1");
            StringAssert.Contains(File.ReadAllText(afterRecoverStatusPath), "\"knownInboundNonceCount\": 1");
            StringAssert.Contains(File.ReadAllText(afterRecoverStatusPath), "\"latestCheckpointBatchNumber\": 2");
            var afterRecoverProbePath = Path.Combine(chainDir, "soft-seal-after-recover-probe.json");
            host.WriteHealthProbeAsync(afterRecoverProbePath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(afterRecoverProbePath));
            StringAssert.Contains(File.ReadAllText(afterRecoverProbePath), "\"isSettlementRetrying\": true");
            StringAssert.Contains(File.ReadAllText(afterRecoverProbePath), "\"consumedDepositCount\": 1");
            StringAssert.Contains(File.ReadAllText(afterRecoverProbePath), "\"knownForcedInclusionNonceCount\": 1");
            StringAssert.Contains(File.ReadAllText(afterRecoverProbePath), "\"knownInboundNonceCount\": 1");
            StringAssert.Contains(File.ReadAllText(afterRecoverProbePath), "\"latestCheckpointBatchNumber\": 2");
            // Rollup host health still unhealthy while settle pending (retrying after recover).
            Assert.IsFalse(afterRecover.IsLocalHostHealthy);
            CollectionAssert.Contains(
                afterRecover.LocalHostHealthFailures.ToArray(),
                nameof(afterRecover.IsSettlementRetrying));
            Assert.IsFalse(host.IsLocalHostHealthyAsync().AsTask().GetAwaiter().GetResult());
            CollectionAssert.Contains(
                host.GetLocalHostHealthFailuresAsync().AsTask().GetAwaiter().GetResult().ToArray(),
                "IsSettlementRetrying");
            Assert.IsFalse(host.IsSettlementRuntimeIdleAsync().AsTask().GetAwaiter().GetResult());
            Assert.IsTrue(host.IsPipelineHealthyAsync().AsTask().GetAwaiter().GetResult() == false);
            CollectionAssert.Contains(
                host.GetPipelineHealthFailuresAsync().AsTask().GetAwaiter().GetResult().ToArray(),
                "IsSettlementRetrying");
            Assert.AreEqual(2UL, afterRecover.LatestCheckpointBatchNumber);
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, afterRecover.LatestCheckpointPostStateRoot);
            StringAssert.Contains(afterRecoverJson, "\"latestCheckpointBatchNumber\": 2");
            StringAssert.Contains(
                afterRecoverJson,
                "\"latestRpcStateRoot\": \"" + SoftPassThroughExecutor.PostStateRoot + "\"");
            // Host recovery does not clear Gateway aggregator backlog (independent soft paths).
            Assert.IsTrue(gatewayHost.AggregatorPendingCount >= 1);
            Assert.IsFalse(gatewayHost.HasPendingPublication);
            Assert.IsFalse(gatewayHost.IsPublicationHealthy);
            CollectionAssert.Contains(
                gatewayHost.PublicationHealthFailures.ToArray(),
                nameof(gatewayHost.AggregatorPendingCount));
            // SubmitNext after recover remains best-effort; does not clear pending without funded L1.
            host.SubmitNextAsync().GetAwaiter().GetResult();
            Assert.IsTrue(host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult() >= 2);
            Assert.IsFalse(host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult().IsSettlementIdle);
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
            Assert.IsTrue(host.HasMetricsReadinessCheck);
            Assert.IsTrue(host.HasMetricsHealthProbe);
            Assert.IsTrue(host.HasMetricsOperatorStatus);
            Assert.IsTrue(host.IsOfflinePassportComplete);
            Assert.IsTrue(host.IsMetricsHttpHealthy);
            Assert.AreEqual(0, host.MetricsHttpHealthFailures.Count);
            var metricsStatus = await host.GetOperatorStatusAsync();
            Assert.IsTrue(metricsStatus.HasMetricsReadinessCheck);
            Assert.IsTrue(metricsStatus.HasMetricsHealthProbe);
            Assert.IsTrue(metricsStatus.HasMetricsOperatorStatus);
            Assert.IsTrue(metricsStatus.IsMetricsHttpHealthy);
            Assert.AreEqual(0, metricsStatus.MetricsHttpHealthFailures.Count);
            Assert.IsTrue(metricsStatus.IsLocalHostHealthy);
            Assert.AreEqual(0, metricsStatus.LocalHostHealthFailures.Count);
            Assert.IsTrue(await host.IsPipelineHealthyAsync());
            Assert.AreEqual(0, (await host.GetPipelineHealthFailuresAsync()).Count);
            Assert.IsTrue(await host.IsLocalHostHealthyAsync());
            Assert.AreEqual(0, (await host.GetLocalHostHealthFailuresAsync()).Count);
            Assert.IsTrue(host.Batch.HasSealedBatchSink);
            Assert.IsTrue(host.Settlement.IsProductionWired);
            Assert.IsNotNull(host.TransactionSender);
            Assert.AreSame(host.Settlement.ProductionTransactionSender, host.TransactionSender);

            using var client = new HttpClient();
            var ready = await client.GetAsync($"http://127.0.0.1:{host.MetricsBoundPort}/readyz");
            Assert.AreEqual(System.Net.HttpStatusCode.OK, ready.StatusCode);
            var health = await client.GetAsync($"http://127.0.0.1:{host.MetricsBoundPort}/healthz");
            Assert.AreEqual(System.Net.HttpStatusCode.OK, health.StatusCode);

            Assert.IsTrue(host.Metrics.HasHealthProbe);
            Assert.IsTrue(host.Metrics.HasOperatorStatus);
            var probeHttp = await client.GetAsync($"http://127.0.0.1:{host.MetricsBoundPort}/healthprobe");
            Assert.AreEqual(System.Net.HttpStatusCode.OK, probeHttp.StatusCode);
            Assert.IsTrue(probeHttp.Content.Headers.ContentType?.MediaType is "application/json");
            var probeBody = await probeHttp.Content.ReadAsStringAsync();
            StringAssert.Contains(probeBody, "isOfflinePassportComplete");
            StringAssert.Contains(probeBody, "isLocalHostHealthy");
            StringAssert.Contains(probeBody, "isBatcherCheckpointAligned");
            StringAssert.Contains(probeBody, "hasMetricsHealthProbe");
            StringAssert.Contains(probeBody, "hasMetricsOperatorStatus");
            StringAssert.Contains(probeBody, "hasMetricsReadinessCheck");
            StringAssert.Contains(probeBody, "isSecurityLevelProofTypeConsistent");
            StringAssert.Contains(probeBody, "isSecurityLevelDaModeConsistent");
            StringAssert.Contains(probeBody, "\"chainId\":");
            StringAssert.Contains(probeBody, "\"proofType\":");
            StringAssert.Contains(probeBody, "\"daMode\":");
            StringAssert.Contains(probeBody, "\"securityLevel\":");
            StringAssert.Contains(probeBody, "isMetricsWiringComplete");
            StringAssert.Contains(probeBody, "metricsConfiguredPort");
            var formatted = host.FormatHealthProbeJson();
            StringAssert.Contains(formatted, "isOfflinePassportComplete");
            StringAssert.Contains(formatted, "hasMetricsHealthProbe");
            StringAssert.Contains(formatted, "hasMetricsOperatorStatus");
            StringAssert.Contains(formatted, "isSecurityLevelProofTypeConsistent");
            StringAssert.Contains(formatted, "\"chainId\":");
            StringAssert.Contains(formatted, "isMetricsWiringComplete");

            var statusHttp = await client.GetAsync($"http://127.0.0.1:{host.MetricsBoundPort}/operatorstatus");
            Assert.AreEqual(System.Net.HttpStatusCode.OK, statusHttp.StatusCode);
            Assert.IsTrue(statusHttp.Content.Headers.ContentType?.MediaType is "application/json");
            var statusBody = await statusHttp.Content.ReadAsStringAsync();
            StringAssert.Contains(statusBody, "isOperatorReady");
            StringAssert.Contains(statusBody, "chainId");
            var formattedStatus = await host.FormatOperatorStatusJsonAsync();
            StringAssert.Contains(formattedStatus, "isOperatorReady");

            Assert.AreEqual(0, await host.GetPendingCountAsync());
            await host.ReconcileAsync();
            await host.SubmitNextAsync();
            Assert.IsTrue(host.IsProductionWired);

            // Recovery helpers that read only durable local stores (no extra L1 RPC).
            var recovery = await host.GetRecoveryStatusAsync();
            Assert.AreEqual(0, recovery.PendingCount);
            Assert.IsNull(recovery.State);
            var tracked = await host.GetTrackedForcedInclusionNoncesAsync(20260716u);
            Assert.AreEqual(0, tracked.Count);

            var metricsBody = await client.GetStringAsync(
                $"http://127.0.0.1:{host.MetricsBoundPort}/metrics");
            Assert.IsFalse(string.IsNullOrWhiteSpace(metricsBody));
            // Prometheus exposition always includes TYPE/HELP or at least scrapeable text.
            StringAssert.Contains(metricsBody, "#");

            host.StopMetricsHttp();
            Assert.IsFalse(host.IsMetricsHttpListening);
            Assert.AreEqual(0, host.MetricsBoundPort);
            // Can restart after stop without disposing the host metrics sink.
            host.StartMetricsHttp(portOverride: 0);
            Assert.IsTrue(host.IsMetricsHttpListening);
            Assert.IsTrue(host.MetricsBoundPort > 0);
            Assert.IsTrue(host.Metrics.HasHealthProbe);
            Assert.IsTrue(host.Metrics.HasOperatorStatus);
            var probeAfterRestart = await client.GetAsync(
                $"http://127.0.0.1:{host.MetricsBoundPort}/healthprobe");
            Assert.AreEqual(System.Net.HttpStatusCode.OK, probeAfterRestart.StatusCode);
            var statusAfterRestart = await client.GetAsync(
                $"http://127.0.0.1:{host.MetricsBoundPort}/operatorstatus");
            Assert.AreEqual(System.Net.HttpStatusCode.OK, statusAfterRestart.StatusCode);
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

    private static void RewriteMaxBlocksPerBatch(string chainDir, int maxBlocks)
    {
        var path = Path.Combine(chainDir, "Plugins", "Neo.Plugins.L2Batch", "config.json");
        Assert.IsTrue(File.Exists(path), path);
        var text = File.ReadAllText(path);
        var rewritten = System.Text.RegularExpressions.Regex.Replace(
            text,
            "\"MaxBlocksPerBatch\"\\s*:\\s*\\d+",
            $"\"MaxBlocksPerBatch\": {maxBlocks}");
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
            // Echo request id so concurrent L1 scans (deposit/FI/message) match JSON-RPC ids.
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
            // Canonical state root for committee / profile paths.
            if (body.Contains("invokefunction", StringComparison.Ordinal)
                || body.Contains("invokescript", StringComparison.Ordinal))
            {
                var root = Convert.ToBase64String(Root(0x11).GetSpan().ToArray());
                return Json(
                    "{\"jsonrpc\":\"2.0\",\"id\":" + idToken
                    + ",\"result\":{\"state\":\"HALT\",\"gasconsumed\":\"0\","
                    + "\"stack\":[{\"type\":\"ByteString\",\"value\":\"" + root + "\"}]}}");
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
    /// Local Multisig/Optimistic soft seal executor: returns fixed post-state + non-empty effects.
    /// Not a ZK semantic; L1 settle broadcast remains operator-funded.
    /// </summary>
    private sealed class SoftPassThroughExecutor : IProofWitnessBatchExecutor
    {
        public static UInt256 PostStateRoot { get; } =
            new(Enumerable.Repeat((byte)0x22, 32).ToArray());

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
}
