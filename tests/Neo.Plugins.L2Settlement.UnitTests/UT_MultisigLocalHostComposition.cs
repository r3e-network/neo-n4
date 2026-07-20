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
            var pendingAfterBatch1 = gatewayHost.AggregatorPendingCount;
            // Multi-batch soft RPC + gateway: batch 2 while host settle still pending ≥2.
            Assert.AreEqual(2UL, host.LastAcknowledgedBatchNumber);
            var commitment2 = commitment with
            {
                BatchNumber = 2,
                FirstBlock = 2,
                LastBlock = 2,
                PreStateRoot = SoftPassThroughExecutor.PostStateRoot,
                PostStateRoot = SoftPassThroughExecutor.PostStateRoot,
                Proof = new byte[] { 0xB4 },
            };
            host.AddRpcBatch(commitment2, BatchStatus.Finalized);
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(2));
            Assert.IsNotNull(host.GetRpcBatch(2));
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, host.GetRpcStateRootAtBatch(2));
            host.FinalizeRpcBatch(2);
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, host.GetLatestRpcStateRoot());
            gatewayHost.ReceiveBatch(commitment2 with
            {
                ChainId = 20260717u,
                L2ToL2MessageRoot = Root(0xAD),
                Proof = new byte[] { 0xB5 },
            });
            gatewayHost.ReceiveBatch(commitment2 with { Proof = new byte[] { 0xB6 } });
            Assert.IsTrue(gatewayHost.AggregatorPendingCount > pendingAfterBatch1);
            Assert.IsTrue(gatewayHost.AggregatorPendingCount >= 4);
            Assert.IsTrue(host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult() >= 2);
            Assert.IsTrue(host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult().IsSettlementRetrying);
            var multiPath = Path.Combine(chainDir, "soft-seal-multi-batch-rpc-gateway.json");
            File.WriteAllText(multiPath, $$"""
                {
                  "batch1": 1,
                  "batch2": 2,
                  "latestRpcStateRoot": "{{host.GetLatestRpcStateRoot()}}",
                  "aggregatorPendingCount": {{gatewayHost.AggregatorPendingCount}},
                  "hasPendingPublication": false,
                  "hostPendingSettlementCount": {{host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult()}}
                }
                """);
            Assert.IsTrue(File.Exists(multiPath));
            StringAssert.Contains(File.ReadAllText(multiPath), "\"batch2\": 2");
            StringAssert.Contains(File.ReadAllText(multiPath), "\"aggregatorPendingCount\": " + gatewayHost.AggregatorPendingCount);
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
            // Host recovery does not clear Gateway multi-batch aggregator backlog (independent soft paths).
            Assert.IsTrue(gatewayHost.AggregatorPendingCount >= 4);
            Assert.IsFalse(gatewayHost.HasPendingPublication);
            Assert.IsFalse(gatewayHost.IsPublicationHealthy);
            CollectionAssert.Contains(
                gatewayHost.PublicationHealthFailures.ToArray(),
                nameof(gatewayHost.AggregatorPendingCount));
            // Multi-batch soft RPC store survives poison→recover.
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(1));
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(2));
            Assert.IsNotNull(host.GetRpcBatch(1));
            Assert.IsNotNull(host.GetRpcBatch(2));
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, host.GetRpcStateRootAtBatch(1));
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, host.GetRpcStateRootAtBatch(2));
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, host.GetLatestRpcStateRoot());
            Assert.AreEqual(2UL, afterRecover.LatestCheckpointBatchNumber);
            Assert.IsTrue(afterRecover.PendingSettlementCount >= 2);
            var afterRecoverMultiPath = Path.Combine(chainDir, "soft-seal-after-recover-multi-batch-rpc.json");
            File.WriteAllText(afterRecoverMultiPath, $$"""
                {
                  "batch1Status": "{{host.GetRpcBatchStatus(1)}}",
                  "batch2Status": "{{host.GetRpcBatchStatus(2)}}",
                  "latestRpcStateRoot": "{{host.GetLatestRpcStateRoot()}}",
                  "aggregatorPendingCount": {{gatewayHost.AggregatorPendingCount}},
                  "pendingSettlementCount": {{afterRecover.PendingSettlementCount}},
                  "latestCheckpointBatchNumber": {{afterRecover.LatestCheckpointBatchNumber}},
                  "isSettlementRetrying": true,
                  "isSettlementPoisoned": false
                }
                """);
            Assert.IsTrue(File.Exists(afterRecoverMultiPath));
            StringAssert.Contains(File.ReadAllText(afterRecoverMultiPath), "\"batch2Status\": \"Finalized\"");
            StringAssert.Contains(File.ReadAllText(afterRecoverMultiPath), "\"aggregatorPendingCount\": " + gatewayHost.AggregatorPendingCount);
            StringAssert.Contains(File.ReadAllText(afterRecoverMultiPath), "\"latestCheckpointBatchNumber\": 2");
            // After recover: multi-batch local DA re-publish + second offline deposit (no L1).
            Assert.IsTrue(host.SupportsLocalDaReader);
            var recoverDa1Payload = new byte[] { 0xDA, 0xA1, 0x01 };
            var recoverDa1 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 1,
                Payload = recoverDa1Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(DAMode.Local, recoverDa1.Layer);
            Assert.IsTrue(host.IsDaAvailableAsync(recoverDa1).AsTask().GetAwaiter().GetResult());
            var recoverDa1Read = host.CreateLocalDaReader().ReadAsync(recoverDa1).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(recoverDa1Read is { Length: 3 });
            CollectionAssert.AreEqual(recoverDa1Payload, recoverDa1Read!.Value.ToArray());
            var recoverDa2Payload = new byte[] { 0xDA, 0xA2, 0x02 };
            var recoverDa2 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 2,
                Payload = recoverDa2Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(host.IsDaAvailableAsync(recoverDa2).AsTask().GetAwaiter().GetResult());
            var recoverDa2Read = host.CreateLocalDaReader().ReadAsync(recoverDa2).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(recoverDa2Read is { Length: 3 });
            CollectionAssert.AreEqual(recoverDa2Payload, recoverDa2Read!.Value.ToArray());
            var recoverL1Asset = UInt160.Parse("0x" + new string('1', 40));
            var recoverL2Asset = UInt160.Parse("0x" + new string('2', 40));
            var softDeposit2 = new CrossChainMessage
            {
                SourceChainId = 0,
                TargetChainId = 20260716u,
                Nonce = 2,
                Sender = Account(0x66),
                Receiver = Account(0x55),
                MessageType = MessageType.Deposit,
                Payload = new DepositPayload
                {
                    L1Asset = recoverL1Asset,
                    L2Recipient = Account(0x55),
                    Amount = new BigInteger(2_000),
                }.Encode(),
                MessageHash = UInt256.Zero,
            };
            var softMint2 = host.ProcessDeposit(softDeposit2);
            Assert.AreEqual(recoverL2Asset, softMint2.L2Asset);
            Assert.IsTrue(host.HasConsumedDeposit(0, 1));
            Assert.IsTrue(host.HasConsumedDeposit(0, 2));
            Assert.AreEqual(2, host.ConsumedDepositCount);
            host.RecordRpcDeposit(new DepositStatus(0, 2, ConsumedOnL2: true, IncludedInBatch: 2));
            Assert.IsTrue(host.GetRpcL1DepositStatus(0, 2) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
            var afterDaOffline = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(2, afterDaOffline.ConsumedDepositCount);
            Assert.IsTrue(afterDaOffline.IsSettlementRetrying);
            Assert.IsTrue(afterDaOffline.PendingSettlementCount >= 2);
            Assert.AreEqual(2UL, afterDaOffline.LatestCheckpointBatchNumber);
            var afterRecoverDaPath = Path.Combine(chainDir, "soft-seal-after-recover-da-offline.json");
            File.WriteAllText(afterRecoverDaPath, $$"""
                {
                  "daBatch1Layer": "{{recoverDa1.Layer}}",
                  "daBatch2Layer": "{{recoverDa2.Layer}}",
                  "consumedDepositCount": {{afterDaOffline.ConsumedDepositCount}},
                  "deposit2IncludedInBatch": 2,
                  "latestCheckpointBatchNumber": {{afterDaOffline.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterDaOffline.PendingSettlementCount}},
                  "isSettlementRetrying": true
                }
                """);
            Assert.IsTrue(File.Exists(afterRecoverDaPath));
            StringAssert.Contains(File.ReadAllText(afterRecoverDaPath), "\"consumedDepositCount\": 2");
            StringAssert.Contains(File.ReadAllText(afterRecoverDaPath), "\"daBatch2Layer\": \"Local\"");
            var afterRecoverProm = host.ExportPrometheusMetrics();
            Assert.IsFalse(string.IsNullOrWhiteSpace(afterRecoverProm));
            var afterRecoverPromPath = Path.Combine(chainDir, "soft-seal-after-recover-host.prom");
            host.WritePrometheusMetricsAsync(afterRecoverPromPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(afterRecoverPromPath));
            Assert.AreEqual(afterRecoverProm, File.ReadAllText(afterRecoverPromPath));

            // Second offline withdrawal/outbox + FI/inbound nonces after recover (no L1).
            var recoverSender = Account(0x77);
            var recoverWdLeaf = host.StageWithdrawal(new WithdrawalRequest
            {
                ChainId = 20260716u,
                EmittingContract = recoverSender,
                L2Sender = recoverSender,
                L1Recipient = recoverSender,
                L2Asset = recoverL2Asset,
                Amount = new BigInteger(75),
                Nonce = 2,
            });
            Assert.AreNotEqual(UInt256.Zero, recoverWdLeaf);
            var recoverSealedWd = host.SealWithdrawalBatch();
            Assert.AreNotEqual(UInt256.Zero, recoverSealedWd.Root);
            Assert.AreEqual(0, host.StagedWithdrawalCount);
            Assert.IsTrue(recoverSealedWd.Tree.Count >= 1);
            var recoverMerkle = recoverSealedWd.Tree.GetProof(recoverSealedWd.Tree.Count - 1);
            Assert.AreEqual(recoverWdLeaf, recoverMerkle.Leaf);
            var recoverWdProofBytes = MerkleProofSerializer.Encode(recoverMerkle);
            Assert.IsTrue(recoverWdProofBytes.Length >= MerkleProofSerializer.HeaderSize);
            host.RecordRpcWithdrawalProof(recoverWdLeaf, recoverWdProofBytes);
            var storedRecoverWdProof = host.GetRpcWithdrawalProof(recoverWdLeaf);
            Assert.IsTrue(storedRecoverWdProof is { Length: > 0 });
            CollectionAssert.AreEqual(recoverWdProofBytes, storedRecoverWdProof!.Value.ToArray());
            var recoverOutboundDraft = new CrossChainMessage
            {
                SourceChainId = 20260716u,
                TargetChainId = 0,
                Nonce = 10,
                Sender = recoverSender,
                Receiver = recoverSender,
                MessageType = MessageType.Event,
                Payload = new byte[] { 0x02 },
                MessageHash = UInt256.Zero,
            };
            var recoverOutbound = recoverOutboundDraft with
            {
                MessageHash = MessageHasher.HashMessage(recoverOutboundDraft),
            };
            host.EnqueueOutboundMessagesAsync([recoverOutbound]).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(2, host.MessageOutbox!.L2ToL1Count);
            var recoverMsgProofBytes = recoverOutbound.MessageHash.GetSpan().ToArray();
            host.RecordRpcMessageProof(recoverOutbound.MessageHash, recoverMsgProofBytes);
            CollectionAssert.AreEqual(
                recoverMsgProofBytes,
                host.GetRpcMessageProof(recoverOutbound.MessageHash)!.Value.ToArray());
            host.RecordMessageRouterFinalizedProof(recoverOutbound.MessageHash, recoverMsgProofBytes);
            CollectionAssert.AreEqual(
                recoverMsgProofBytes,
                host.GetMessageRouterProofAsync(recoverOutbound.MessageHash).AsTask().GetAwaiter().GetResult()!.Value.ToArray());
            Assert.IsTrue(host.RegisterForcedInclusionNonce(12));
            Assert.IsFalse(host.RegisterForcedInclusionNonce(12));
            Assert.AreEqual(2, host.KnownForcedInclusionNonceCount);
            Assert.IsTrue(host.RegisterInboundMessageNonce(12));
            Assert.IsFalse(host.RegisterInboundMessageNonce(12));
            Assert.AreEqual(2, host.KnownInboundNonceCount);
            Assert.AreEqual(0, host.OpenBatchForcedInclusionCount);
            Assert.AreEqual(0, host.OpenBatchL1MessageCount);
            var afterSecondOutbound = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(2, afterSecondOutbound.ConsumedDepositCount);
            Assert.AreEqual(2, afterSecondOutbound.MessageOutboxL2ToL1Count);
            Assert.AreEqual(2, afterSecondOutbound.KnownForcedInclusionNonceCount);
            Assert.AreEqual(2, afterSecondOutbound.KnownInboundNonceCount);
            Assert.IsTrue(afterSecondOutbound.IsSettlementRetrying);
            Assert.IsTrue(afterSecondOutbound.PendingSettlementCount >= 2);
            var afterSecondOutboundPath = Path.Combine(chainDir, "soft-seal-after-recover-second-outbound.json");
            File.WriteAllText(afterSecondOutboundPath, $$"""
                {
                  "messageOutboxL2ToL1Count": {{afterSecondOutbound.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterSecondOutbound.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterSecondOutbound.KnownInboundNonceCount}},
                  "withdrawalLeaf": "{{recoverWdLeaf}}",
                  "outboundMessageHash": "{{recoverOutbound.MessageHash}}",
                  "withdrawalProofBytes": {{recoverWdProofBytes.Length}},
                  "messageProofBytes": {{recoverMsgProofBytes.Length}},
                  "latestCheckpointBatchNumber": {{afterSecondOutbound.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterSecondOutbound.PendingSettlementCount}},
                  "isSettlementRetrying": true
                }
                """);
            Assert.IsTrue(File.Exists(afterSecondOutboundPath));
            StringAssert.Contains(File.ReadAllText(afterSecondOutboundPath), "\"messageOutboxL2ToL1Count\": 2");
            StringAssert.Contains(File.ReadAllText(afterSecondOutboundPath), "\"knownForcedInclusionNonceCount\": 2");
            StringAssert.Contains(File.ReadAllText(afterSecondOutboundPath), "\"knownInboundNonceCount\": 2");
            StringAssert.Contains(File.ReadAllText(afterSecondOutboundPath), "\"withdrawalLeaf\": \"" + recoverWdLeaf + "\"");
            var afterSecondOutboundRpcPath = Path.Combine(chainDir, "soft-seal-after-recover-second-outbound-rpc.json");
            File.WriteAllText(afterSecondOutboundRpcPath, $$"""
                {
                  "withdrawalLeaf": "{{recoverWdLeaf}}",
                  "outboundMessageHash": "{{recoverOutbound.MessageHash}}",
                  "withdrawalProofBytes": {{recoverWdProofBytes.Length}},
                  "messageProofBytes": {{recoverMsgProofBytes.Length}},
                  "knownForcedInclusionNonceCount": 2,
                  "knownInboundNonceCount": 2,
                  "isSettlementRetrying": true
                }
                """);
            Assert.IsTrue(File.Exists(afterSecondOutboundRpcPath));
            StringAssert.Contains(File.ReadAllText(afterSecondOutboundRpcPath), "\"outboundMessageHash\": \"" + recoverOutbound.MessageHash + "\"");

            // Second poison→recover after full soft multi-batch path retains soft state.
            // RetryCount was reset by first recover — escalate until Poisoned again.
            LocalHostOperatorStatus afterSecondPoison = afterSecondOutbound;
            for (var attempt = 0; attempt < 16; attempt++)
            {
                try
                {
                    host.ReconcileAsync().GetAwaiter().GetResult();
                }
                catch (OverflowException)
                {
                }
                catch (Exception)
                {
                }

                host.SubmitNextAsync().GetAwaiter().GetResult();
                afterSecondPoison = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
                if (afterSecondPoison.IsSettlementPoisoned)
                    break;
            }

            Assert.IsTrue(afterSecondPoison.IsSettlementPoisoned);
            Assert.IsFalse(afterSecondPoison.IsSettlementRetrying);
            Assert.IsTrue(afterSecondPoison.PendingSettlementCount >= 2);
            Assert.AreEqual(2, afterSecondPoison.ConsumedDepositCount);
            Assert.AreEqual(2, afterSecondPoison.MessageOutboxL2ToL1Count);
            Assert.AreEqual(2, afterSecondPoison.KnownForcedInclusionNonceCount);
            Assert.AreEqual(2, afterSecondPoison.KnownInboundNonceCount);
            Assert.IsNotNull(afterSecondPoison.Recovery.BlockedBatchNumber);
            Assert.IsNotNull(afterSecondPoison.Recovery.ArtifactContentHash);
            var secondBlocked = afterSecondPoison.Recovery.BlockedBatchNumber!.Value;
            var secondHash = afterSecondPoison.Recovery.ArtifactContentHash!;
            Assert.ThrowsExactly<InvalidOperationException>(
                () => host.RecoverPoisonedBatchAsync(secondBlocked, UInt256.Zero)
                    .GetAwaiter().GetResult());
            host.RecoverPoisonedBatchAsync(secondBlocked, secondHash).GetAwaiter().GetResult();
            var afterSecondRecover = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsFalse(afterSecondRecover.IsSettlementPoisoned);
            Assert.IsTrue(afterSecondRecover.IsSettlementRetrying);
            Assert.AreEqual(SettlementRecoveryState.Retrying, afterSecondRecover.Recovery.State);
            Assert.AreEqual(0, afterSecondRecover.Recovery.RetryCount);
            Assert.IsTrue(host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult() >= 2);
            Assert.AreEqual(2UL, afterSecondRecover.LatestCheckpointBatchNumber);
            Assert.AreEqual(2, afterSecondRecover.ConsumedDepositCount);
            Assert.AreEqual(2, afterSecondRecover.MessageOutboxL2ToL1Count);
            Assert.AreEqual(2, afterSecondRecover.KnownForcedInclusionNonceCount);
            Assert.AreEqual(2, afterSecondRecover.KnownInboundNonceCount);
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(1));
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(2));
            Assert.IsTrue(host.GetRpcWithdrawalProof(recoverWdLeaf) is { Length: > 0 });
            Assert.IsTrue(host.GetRpcMessageProof(recoverOutbound.MessageHash) is { Length: > 0 });
            Assert.IsTrue(afterSecondRecover.IsOfflinePassportComplete);
            Assert.IsTrue(afterSecondRecover.IsOperatorReady);
            var secondPoisonPath = Path.Combine(chainDir, "soft-seal-second-poison-recover.json");
            File.WriteAllText(secondPoisonPath, $$"""
                {
                  "secondPoisonBlockedBatch": {{secondBlocked}},
                  "pendingSettlementCount": {{afterSecondRecover.PendingSettlementCount}},
                  "latestCheckpointBatchNumber": {{afterSecondRecover.LatestCheckpointBatchNumber}},
                  "consumedDepositCount": {{afterSecondRecover.ConsumedDepositCount}},
                  "messageOutboxL2ToL1Count": {{afterSecondRecover.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterSecondRecover.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterSecondRecover.KnownInboundNonceCount}},
                  "rpcBatch1Status": "{{host.GetRpcBatchStatus(1)}}",
                  "rpcBatch2Status": "{{host.GetRpcBatchStatus(2)}}",
                  "secondWithdrawalProofPresent": true,
                  "secondMessageProofPresent": true,
                  "isSettlementRetrying": true,
                  "isSettlementPoisoned": false
                }
                """);
            Assert.IsTrue(File.Exists(secondPoisonPath));
            StringAssert.Contains(File.ReadAllText(secondPoisonPath), "\"rpcBatch2Status\": \"Finalized\"");
            StringAssert.Contains(File.ReadAllText(secondPoisonPath), "\"consumedDepositCount\": 2");
            StringAssert.Contains(File.ReadAllText(secondPoisonPath), "\"isSettlementRetrying\": true");

            // After second recover: multi-batch local DA + third offline deposit (no L1).
            Assert.IsTrue(host.SupportsLocalDaReader);
            var postDa1Payload = new byte[] { 0xDA, 0xB1, 0x01 };
            var postDa1 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 1,
                Payload = postDa1Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(DAMode.Local, postDa1.Layer);
            Assert.IsTrue(host.IsDaAvailableAsync(postDa1).AsTask().GetAwaiter().GetResult());
            var postDa1Read = host.CreateLocalDaReader().ReadAsync(postDa1).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(postDa1Read is { Length: 3 });
            CollectionAssert.AreEqual(postDa1Payload, postDa1Read!.Value.ToArray());
            var postDa2Payload = new byte[] { 0xDA, 0xB2, 0x02 };
            var postDa2 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 2,
                Payload = postDa2Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(host.IsDaAvailableAsync(postDa2).AsTask().GetAwaiter().GetResult());
            var postDa2Read = host.CreateLocalDaReader().ReadAsync(postDa2).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(postDa2Read is { Length: 3 });
            CollectionAssert.AreEqual(postDa2Payload, postDa2Read!.Value.ToArray());
            var postL1Asset = UInt160.Parse("0x" + new string('1', 40));
            var postL2Asset = UInt160.Parse("0x" + new string('2', 40));
            var deposit3 = new CrossChainMessage
            {
                SourceChainId = 0,
                TargetChainId = 20260716u,
                Nonce = 3,
                Sender = Account(0x66),
                Receiver = Account(0x55),
                MessageType = MessageType.Deposit,
                Payload = new DepositPayload
                {
                    L1Asset = postL1Asset,
                    L2Recipient = Account(0x55),
                    Amount = new BigInteger(3_000),
                }.Encode(),
                MessageHash = UInt256.Zero,
            };
            var mint3 = host.ProcessDeposit(deposit3);
            Assert.AreEqual(postL2Asset, mint3.L2Asset);
            Assert.IsTrue(host.HasConsumedDeposit(0, 1));
            Assert.IsTrue(host.HasConsumedDeposit(0, 2));
            Assert.IsTrue(host.HasConsumedDeposit(0, 3));
            Assert.AreEqual(3, host.ConsumedDepositCount);
            host.RecordRpcDeposit(new DepositStatus(0, 3, ConsumedOnL2: true, IncludedInBatch: 2));
            Assert.IsTrue(host.GetRpcL1DepositStatus(0, 3) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
            var afterThirdDeposit = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(3, afterThirdDeposit.ConsumedDepositCount);
            Assert.AreEqual(2, afterThirdDeposit.MessageOutboxL2ToL1Count);
            Assert.IsTrue(afterThirdDeposit.IsSettlementRetrying);
            Assert.IsTrue(afterThirdDeposit.PendingSettlementCount >= 2);
            Assert.AreEqual(2UL, afterThirdDeposit.LatestCheckpointBatchNumber);
            Assert.IsTrue(afterThirdDeposit.IsOfflinePassportComplete);
            var afterSecondRecoverDaPath = Path.Combine(chainDir, "soft-seal-after-second-recover-da-deposit.json");
            File.WriteAllText(afterSecondRecoverDaPath, $$"""
                {
                  "daBatch1Layer": "{{postDa1.Layer}}",
                  "daBatch2Layer": "{{postDa2.Layer}}",
                  "consumedDepositCount": {{afterThirdDeposit.ConsumedDepositCount}},
                  "deposit3IncludedInBatch": 2,
                  "messageOutboxL2ToL1Count": {{afterThirdDeposit.MessageOutboxL2ToL1Count}},
                  "latestCheckpointBatchNumber": {{afterThirdDeposit.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterThirdDeposit.PendingSettlementCount}},
                  "isSettlementRetrying": true,
                  "isOfflinePassportComplete": true
                }
                """);
            Assert.IsTrue(File.Exists(afterSecondRecoverDaPath));
            StringAssert.Contains(File.ReadAllText(afterSecondRecoverDaPath), "\"consumedDepositCount\": 3");
            StringAssert.Contains(File.ReadAllText(afterSecondRecoverDaPath), "\"daBatch2Layer\": \"Local\"");
            var afterSecondRecoverProm = host.ExportPrometheusMetrics();
            Assert.IsFalse(string.IsNullOrWhiteSpace(afterSecondRecoverProm));
            var afterSecondRecoverPromPath = Path.Combine(chainDir, "soft-seal-after-second-recover-host.prom");
            host.WritePrometheusMetricsAsync(afterSecondRecoverPromPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(afterSecondRecoverPromPath));
            Assert.AreEqual(afterSecondRecoverProm, File.ReadAllText(afterSecondRecoverPromPath));

            // Third offline withdrawal/outbox + FI/inbound after second recover (no L1).
            var thirdSender = Account(0x77);
            var thirdWdLeaf = host.StageWithdrawal(new WithdrawalRequest
            {
                ChainId = 20260716u,
                EmittingContract = thirdSender,
                L2Sender = thirdSender,
                L1Recipient = thirdSender,
                L2Asset = postL2Asset,
                Amount = new BigInteger(100),
                Nonce = 3,
            });
            Assert.AreNotEqual(UInt256.Zero, thirdWdLeaf);
            var thirdSealedWd = host.SealWithdrawalBatch();
            Assert.AreNotEqual(UInt256.Zero, thirdSealedWd.Root);
            Assert.AreEqual(0, host.StagedWithdrawalCount);
            Assert.IsTrue(thirdSealedWd.Tree.Count >= 1);
            var thirdMerkle = thirdSealedWd.Tree.GetProof(thirdSealedWd.Tree.Count - 1);
            Assert.AreEqual(thirdWdLeaf, thirdMerkle.Leaf);
            var thirdWdProofBytes = MerkleProofSerializer.Encode(thirdMerkle);
            Assert.IsTrue(thirdWdProofBytes.Length >= MerkleProofSerializer.HeaderSize);
            host.RecordRpcWithdrawalProof(thirdWdLeaf, thirdWdProofBytes);
            CollectionAssert.AreEqual(
                thirdWdProofBytes,
                host.GetRpcWithdrawalProof(thirdWdLeaf)!.Value.ToArray());
            var thirdOutboundDraft = new CrossChainMessage
            {
                SourceChainId = 20260716u,
                TargetChainId = 0,
                Nonce = 11,
                Sender = thirdSender,
                Receiver = thirdSender,
                MessageType = MessageType.Event,
                Payload = new byte[] { 0x03 },
                MessageHash = UInt256.Zero,
            };
            var thirdOutbound = thirdOutboundDraft with
            {
                MessageHash = MessageHasher.HashMessage(thirdOutboundDraft),
            };
            host.EnqueueOutboundMessagesAsync([thirdOutbound]).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(3, host.MessageOutbox!.L2ToL1Count);
            var thirdMsgProofBytes = thirdOutbound.MessageHash.GetSpan().ToArray();
            host.RecordRpcMessageProof(thirdOutbound.MessageHash, thirdMsgProofBytes);
            CollectionAssert.AreEqual(
                thirdMsgProofBytes,
                host.GetRpcMessageProof(thirdOutbound.MessageHash)!.Value.ToArray());
            host.RecordMessageRouterFinalizedProof(thirdOutbound.MessageHash, thirdMsgProofBytes);
            CollectionAssert.AreEqual(
                thirdMsgProofBytes,
                host.GetMessageRouterProofAsync(thirdOutbound.MessageHash).AsTask().GetAwaiter().GetResult()!.Value.ToArray());
            Assert.IsTrue(host.RegisterForcedInclusionNonce(13));
            Assert.IsFalse(host.RegisterForcedInclusionNonce(13));
            Assert.AreEqual(3, host.KnownForcedInclusionNonceCount);
            Assert.IsTrue(host.RegisterInboundMessageNonce(13));
            Assert.IsFalse(host.RegisterInboundMessageNonce(13));
            Assert.AreEqual(3, host.KnownInboundNonceCount);
            Assert.AreEqual(0, host.OpenBatchForcedInclusionCount);
            Assert.AreEqual(0, host.OpenBatchL1MessageCount);
            var afterThirdOutbound = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(3, afterThirdOutbound.ConsumedDepositCount);
            Assert.AreEqual(3, afterThirdOutbound.MessageOutboxL2ToL1Count);
            Assert.AreEqual(3, afterThirdOutbound.KnownForcedInclusionNonceCount);
            Assert.AreEqual(3, afterThirdOutbound.KnownInboundNonceCount);
            Assert.IsTrue(afterThirdOutbound.IsSettlementRetrying);
            Assert.IsTrue(afterThirdOutbound.PendingSettlementCount >= 2);
            Assert.AreEqual(2UL, afterThirdOutbound.LatestCheckpointBatchNumber);
            var thirdOutboundPath = Path.Combine(chainDir, "soft-seal-after-second-recover-third-outbound.json");
            File.WriteAllText(thirdOutboundPath, $$"""
                {
                  "consumedDepositCount": {{afterThirdOutbound.ConsumedDepositCount}},
                  "messageOutboxL2ToL1Count": {{afterThirdOutbound.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterThirdOutbound.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterThirdOutbound.KnownInboundNonceCount}},
                  "withdrawalLeaf": "{{thirdWdLeaf}}",
                  "outboundMessageHash": "{{thirdOutbound.MessageHash}}",
                  "withdrawalProofBytes": {{thirdWdProofBytes.Length}},
                  "messageProofBytes": {{thirdMsgProofBytes.Length}},
                  "latestCheckpointBatchNumber": {{afterThirdOutbound.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterThirdOutbound.PendingSettlementCount}},
                  "isSettlementRetrying": true
                }
                """);
            Assert.IsTrue(File.Exists(thirdOutboundPath));
            StringAssert.Contains(File.ReadAllText(thirdOutboundPath), "\"messageOutboxL2ToL1Count\": 3");
            StringAssert.Contains(File.ReadAllText(thirdOutboundPath), "\"knownForcedInclusionNonceCount\": 3");
            StringAssert.Contains(File.ReadAllText(thirdOutboundPath), "\"consumedDepositCount\": 3");
            var thirdOutboundRpcPath = Path.Combine(chainDir, "soft-seal-after-second-recover-third-outbound-rpc.json");
            File.WriteAllText(thirdOutboundRpcPath, $$"""
                {
                  "withdrawalLeaf": "{{thirdWdLeaf}}",
                  "outboundMessageHash": "{{thirdOutbound.MessageHash}}",
                  "withdrawalProofBytes": {{thirdWdProofBytes.Length}},
                  "messageProofBytes": {{thirdMsgProofBytes.Length}},
                  "messageOutboxL2ToL1Count": 3,
                  "knownForcedInclusionNonceCount": 3,
                  "knownInboundNonceCount": 3,
                  "isSettlementRetrying": true
                }
                """);
            Assert.IsTrue(File.Exists(thirdOutboundRpcPath));
            StringAssert.Contains(File.ReadAllText(thirdOutboundRpcPath), "\"outboundMessageHash\": \"" + thirdOutbound.MessageHash + "\"");

            // Third poison→recover after triple soft multi-batch path retains soft state.
            LocalHostOperatorStatus afterThirdPoison = afterThirdOutbound;
            for (var attempt = 0; attempt < 16; attempt++)
            {
                try
                {
                    host.ReconcileAsync().GetAwaiter().GetResult();
                }
                catch (OverflowException)
                {
                }
                catch (Exception)
                {
                }

                host.SubmitNextAsync().GetAwaiter().GetResult();
                afterThirdPoison = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
                if (afterThirdPoison.IsSettlementPoisoned)
                    break;
            }

            Assert.IsTrue(afterThirdPoison.IsSettlementPoisoned);
            Assert.IsFalse(afterThirdPoison.IsSettlementRetrying);
            Assert.IsTrue(afterThirdPoison.PendingSettlementCount >= 2);
            Assert.AreEqual(3, afterThirdPoison.ConsumedDepositCount);
            Assert.AreEqual(3, afterThirdPoison.MessageOutboxL2ToL1Count);
            Assert.AreEqual(3, afterThirdPoison.KnownForcedInclusionNonceCount);
            Assert.AreEqual(3, afterThirdPoison.KnownInboundNonceCount);
            Assert.IsNotNull(afterThirdPoison.Recovery.BlockedBatchNumber);
            Assert.IsNotNull(afterThirdPoison.Recovery.ArtifactContentHash);
            var thirdBlocked = afterThirdPoison.Recovery.BlockedBatchNumber!.Value;
            var thirdHash = afterThirdPoison.Recovery.ArtifactContentHash!;
            Assert.ThrowsExactly<InvalidOperationException>(
                () => host.RecoverPoisonedBatchAsync(thirdBlocked, UInt256.Zero)
                    .GetAwaiter().GetResult());
            host.RecoverPoisonedBatchAsync(thirdBlocked, thirdHash).GetAwaiter().GetResult();
            var afterThirdRecover = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsFalse(afterThirdRecover.IsSettlementPoisoned);
            Assert.IsTrue(afterThirdRecover.IsSettlementRetrying);
            Assert.AreEqual(SettlementRecoveryState.Retrying, afterThirdRecover.Recovery.State);
            Assert.AreEqual(0, afterThirdRecover.Recovery.RetryCount);
            Assert.IsTrue(host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult() >= 2);
            Assert.AreEqual(2UL, afterThirdRecover.LatestCheckpointBatchNumber);
            Assert.AreEqual(3, afterThirdRecover.ConsumedDepositCount);
            Assert.AreEqual(3, afterThirdRecover.MessageOutboxL2ToL1Count);
            Assert.AreEqual(3, afterThirdRecover.KnownForcedInclusionNonceCount);
            Assert.AreEqual(3, afterThirdRecover.KnownInboundNonceCount);
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(1));
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(2));
            Assert.IsTrue(host.GetRpcWithdrawalProof(thirdWdLeaf) is { Length: > 0 });
            Assert.IsTrue(host.GetRpcMessageProof(thirdOutbound.MessageHash) is { Length: > 0 });
            Assert.IsTrue(afterThirdRecover.IsOfflinePassportComplete);
            Assert.IsTrue(afterThirdRecover.IsOperatorReady);
            var thirdPoisonPath = Path.Combine(chainDir, "soft-seal-third-poison-recover.json");
            File.WriteAllText(thirdPoisonPath, $$"""
                {
                  "thirdPoisonBlockedBatch": {{thirdBlocked}},
                  "pendingSettlementCount": {{afterThirdRecover.PendingSettlementCount}},
                  "latestCheckpointBatchNumber": {{afterThirdRecover.LatestCheckpointBatchNumber}},
                  "consumedDepositCount": {{afterThirdRecover.ConsumedDepositCount}},
                  "messageOutboxL2ToL1Count": {{afterThirdRecover.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterThirdRecover.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterThirdRecover.KnownInboundNonceCount}},
                  "rpcBatch1Status": "{{host.GetRpcBatchStatus(1)}}",
                  "rpcBatch2Status": "{{host.GetRpcBatchStatus(2)}}",
                  "thirdWithdrawalProofPresent": true,
                  "thirdMessageProofPresent": true,
                  "isSettlementRetrying": true,
                  "isSettlementPoisoned": false
                }
                """);
            Assert.IsTrue(File.Exists(thirdPoisonPath));
            StringAssert.Contains(File.ReadAllText(thirdPoisonPath), "\"rpcBatch2Status\": \"Finalized\"");
            StringAssert.Contains(File.ReadAllText(thirdPoisonPath), "\"consumedDepositCount\": 3");
            StringAssert.Contains(File.ReadAllText(thirdPoisonPath), "\"messageOutboxL2ToL1Count\": 3");
            StringAssert.Contains(File.ReadAllText(thirdPoisonPath), "\"isSettlementRetrying\": true");

            // After third recover: re-publish local DA + fourth offline deposit while Retrying.
            Assert.IsTrue(host.SupportsLocalDaReader);
            var thirdRecoverDa1Payload = new byte[] { 0xDA, 0xC1, 0x01 };
            var thirdRecoverDa1 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 1,
                Payload = thirdRecoverDa1Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(DAMode.Local, thirdRecoverDa1.Layer);
            Assert.IsTrue(host.IsDaAvailableAsync(thirdRecoverDa1).AsTask().GetAwaiter().GetResult());
            var thirdRecoverDa1Read = host.CreateLocalDaReader().ReadAsync(thirdRecoverDa1).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(thirdRecoverDa1Read is { Length: 3 });
            CollectionAssert.AreEqual(thirdRecoverDa1Payload, thirdRecoverDa1Read!.Value.ToArray());
            var thirdRecoverDa2Payload = new byte[] { 0xDA, 0xC2, 0x02 };
            var thirdRecoverDa2 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 2,
                Payload = thirdRecoverDa2Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(host.IsDaAvailableAsync(thirdRecoverDa2).AsTask().GetAwaiter().GetResult());
            var thirdRecoverDa2Read = host.CreateLocalDaReader().ReadAsync(thirdRecoverDa2).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(thirdRecoverDa2Read is { Length: 3 });
            CollectionAssert.AreEqual(thirdRecoverDa2Payload, thirdRecoverDa2Read!.Value.ToArray());
            var thirdRecoverL1Asset = UInt160.Parse("0x" + new string('1', 40));
            var thirdRecoverL2Asset = UInt160.Parse("0x" + new string('2', 40));
            var deposit4 = new CrossChainMessage
            {
                SourceChainId = 0,
                TargetChainId = 20260716u,
                Nonce = 4,
                Sender = Account(0x66),
                Receiver = Account(0x55),
                MessageType = MessageType.Deposit,
                Payload = new DepositPayload
                {
                    L1Asset = thirdRecoverL1Asset,
                    L2Recipient = Account(0x55),
                    Amount = new BigInteger(4_000),
                }.Encode(),
                MessageHash = UInt256.Zero,
            };
            var mint4 = host.ProcessDeposit(deposit4);
            Assert.AreEqual(thirdRecoverL2Asset, mint4.L2Asset);
            Assert.IsTrue(host.HasConsumedDeposit(0, 1));
            Assert.IsTrue(host.HasConsumedDeposit(0, 2));
            Assert.IsTrue(host.HasConsumedDeposit(0, 3));
            Assert.IsTrue(host.HasConsumedDeposit(0, 4));
            Assert.AreEqual(4, host.ConsumedDepositCount);
            host.RecordRpcDeposit(new DepositStatus(0, 4, ConsumedOnL2: true, IncludedInBatch: 2));
            Assert.IsTrue(host.GetRpcL1DepositStatus(0, 4) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
            Assert.AreEqual(0, host.ScanSharedBridgeDepositsAsync().AsTask().GetAwaiter().GetResult());
            var afterFourthDeposit = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(4, afterFourthDeposit.ConsumedDepositCount);
            Assert.AreEqual(3, afterFourthDeposit.MessageOutboxL2ToL1Count);
            Assert.AreEqual(3, afterFourthDeposit.KnownForcedInclusionNonceCount);
            Assert.AreEqual(3, afterFourthDeposit.KnownInboundNonceCount);
            Assert.IsTrue(afterFourthDeposit.IsSettlementRetrying);
            Assert.IsTrue(afterFourthDeposit.PendingSettlementCount >= 2);
            Assert.AreEqual(2UL, afterFourthDeposit.LatestCheckpointBatchNumber);
            Assert.IsTrue(afterFourthDeposit.IsOfflinePassportComplete);
            var afterThirdRecoverDaPath = Path.Combine(chainDir, "soft-seal-after-third-recover-da-deposit.json");
            File.WriteAllText(afterThirdRecoverDaPath, $$"""
                {
                  "daBatch1Layer": "{{thirdRecoverDa1.Layer}}",
                  "daBatch2Layer": "{{thirdRecoverDa2.Layer}}",
                  "consumedDepositCount": {{afterFourthDeposit.ConsumedDepositCount}},
                  "deposit4IncludedInBatch": 2,
                  "messageOutboxL2ToL1Count": {{afterFourthDeposit.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterFourthDeposit.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterFourthDeposit.KnownInboundNonceCount}},
                  "latestCheckpointBatchNumber": {{afterFourthDeposit.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterFourthDeposit.PendingSettlementCount}},
                  "isSettlementRetrying": true,
                  "isOfflinePassportComplete": true
                }
                """);
            Assert.IsTrue(File.Exists(afterThirdRecoverDaPath));
            StringAssert.Contains(File.ReadAllText(afterThirdRecoverDaPath), "\"consumedDepositCount\": 4");
            StringAssert.Contains(File.ReadAllText(afterThirdRecoverDaPath), "\"deposit4IncludedInBatch\": 2");
            StringAssert.Contains(File.ReadAllText(afterThirdRecoverDaPath), "\"daBatch2Layer\": \"Local\"");
            var afterThirdRecoverProm = host.ExportPrometheusMetrics();
            Assert.IsFalse(string.IsNullOrWhiteSpace(afterThirdRecoverProm));
            var afterThirdRecoverPromPath = Path.Combine(chainDir, "soft-seal-after-third-recover-host.prom");
            host.WritePrometheusMetricsAsync(afterThirdRecoverPromPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(afterThirdRecoverPromPath));
            Assert.AreEqual(afterThirdRecoverProm, File.ReadAllText(afterThirdRecoverPromPath));

            // Fourth offline withdrawal/outbox + FI/inbound after third recover (no L1).
            var fourthSender = Account(0x88);
            var fourthL2Asset = UInt160.Parse("0x" + new string('2', 40));
            var fourthWdLeaf = host.StageWithdrawal(new WithdrawalRequest
            {
                ChainId = 20260716u,
                EmittingContract = fourthSender,
                L2Sender = fourthSender,
                L1Recipient = fourthSender,
                L2Asset = fourthL2Asset,
                Amount = new BigInteger(100),
                Nonce = 4,
            });
            Assert.AreNotEqual(UInt256.Zero, fourthWdLeaf);
            var fourthSealedWd = host.SealWithdrawalBatch();
            Assert.AreNotEqual(UInt256.Zero, fourthSealedWd.Root);
            Assert.AreEqual(0, host.StagedWithdrawalCount);
            Assert.IsTrue(fourthSealedWd.Tree.Count >= 1);
            var fourthMerkle = fourthSealedWd.Tree.GetProof(fourthSealedWd.Tree.Count - 1);
            Assert.AreEqual(fourthWdLeaf, fourthMerkle.Leaf);
            var fourthWdProofBytes = MerkleProofSerializer.Encode(fourthMerkle);
            Assert.IsTrue(fourthWdProofBytes.Length >= MerkleProofSerializer.HeaderSize);
            host.RecordRpcWithdrawalProof(fourthWdLeaf, fourthWdProofBytes);
            CollectionAssert.AreEqual(
                fourthWdProofBytes,
                host.GetRpcWithdrawalProof(fourthWdLeaf)!.Value.ToArray());
            var fourthOutboundDraft = new CrossChainMessage
            {
                SourceChainId = 20260716u,
                TargetChainId = 0,
                Nonce = 12,
                Sender = fourthSender,
                Receiver = fourthSender,
                MessageType = MessageType.Event,
                Payload = new byte[] { 0x04 },
                MessageHash = UInt256.Zero,
            };
            var fourthOutbound = fourthOutboundDraft with
            {
                MessageHash = MessageHasher.HashMessage(fourthOutboundDraft),
            };
            host.EnqueueOutboundMessagesAsync([fourthOutbound]).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(4, host.MessageOutbox!.L2ToL1Count);
            var fourthMsgProofBytes = fourthOutbound.MessageHash.GetSpan().ToArray();
            host.RecordRpcMessageProof(fourthOutbound.MessageHash, fourthMsgProofBytes);
            CollectionAssert.AreEqual(
                fourthMsgProofBytes,
                host.GetRpcMessageProof(fourthOutbound.MessageHash)!.Value.ToArray());
            host.RecordMessageRouterFinalizedProof(fourthOutbound.MessageHash, fourthMsgProofBytes);
            CollectionAssert.AreEqual(
                fourthMsgProofBytes,
                host.GetMessageRouterProofAsync(fourthOutbound.MessageHash).AsTask().GetAwaiter().GetResult()!.Value.ToArray());
            Assert.IsTrue(host.RegisterForcedInclusionNonce(14));
            Assert.IsFalse(host.RegisterForcedInclusionNonce(14));
            Assert.AreEqual(4, host.KnownForcedInclusionNonceCount);
            Assert.IsTrue(host.RegisterInboundMessageNonce(14));
            Assert.IsFalse(host.RegisterInboundMessageNonce(14));
            Assert.AreEqual(4, host.KnownInboundNonceCount);
            Assert.AreEqual(0, host.OpenBatchForcedInclusionCount);
            Assert.AreEqual(0, host.OpenBatchL1MessageCount);
            var afterFourthOutbound = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(4, afterFourthOutbound.ConsumedDepositCount);
            Assert.AreEqual(4, afterFourthOutbound.MessageOutboxL2ToL1Count);
            Assert.AreEqual(4, afterFourthOutbound.KnownForcedInclusionNonceCount);
            Assert.AreEqual(4, afterFourthOutbound.KnownInboundNonceCount);
            Assert.IsTrue(afterFourthOutbound.IsSettlementRetrying);
            Assert.IsTrue(afterFourthOutbound.PendingSettlementCount >= 2);
            Assert.AreEqual(2UL, afterFourthOutbound.LatestCheckpointBatchNumber);
            var fourthOutboundPath = Path.Combine(chainDir, "soft-seal-after-third-recover-fourth-outbound.json");
            File.WriteAllText(fourthOutboundPath, $$"""
                {
                  "consumedDepositCount": {{afterFourthOutbound.ConsumedDepositCount}},
                  "messageOutboxL2ToL1Count": {{afterFourthOutbound.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterFourthOutbound.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterFourthOutbound.KnownInboundNonceCount}},
                  "withdrawalLeaf": "{{fourthWdLeaf}}",
                  "outboundMessageHash": "{{fourthOutbound.MessageHash}}",
                  "withdrawalProofBytes": {{fourthWdProofBytes.Length}},
                  "messageProofBytes": {{fourthMsgProofBytes.Length}},
                  "latestCheckpointBatchNumber": {{afterFourthOutbound.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterFourthOutbound.PendingSettlementCount}},
                  "isSettlementRetrying": true
                }
                """);
            Assert.IsTrue(File.Exists(fourthOutboundPath));
            StringAssert.Contains(File.ReadAllText(fourthOutboundPath), "\"messageOutboxL2ToL1Count\": 4");
            StringAssert.Contains(File.ReadAllText(fourthOutboundPath), "\"knownForcedInclusionNonceCount\": 4");
            StringAssert.Contains(File.ReadAllText(fourthOutboundPath), "\"consumedDepositCount\": 4");
            var fourthOutboundRpcPath = Path.Combine(chainDir, "soft-seal-after-third-recover-fourth-outbound-rpc.json");
            File.WriteAllText(fourthOutboundRpcPath, $$"""
                {
                  "withdrawalLeaf": "{{fourthWdLeaf}}",
                  "outboundMessageHash": "{{fourthOutbound.MessageHash}}",
                  "withdrawalProofBytes": {{fourthWdProofBytes.Length}},
                  "messageProofBytes": {{fourthMsgProofBytes.Length}},
                  "messageOutboxL2ToL1Count": 4,
                  "knownForcedInclusionNonceCount": 4,
                  "knownInboundNonceCount": 4,
                  "consumedDepositCount": 4,
                  "isSettlementRetrying": true
                }
                """);
            Assert.IsTrue(File.Exists(fourthOutboundRpcPath));
            StringAssert.Contains(File.ReadAllText(fourthOutboundRpcPath), "\"outboundMessageHash\": \"" + fourthOutbound.MessageHash + "\"");

            // Fourth poison→recover after quadruple soft multi-batch path retains soft state.
            LocalHostOperatorStatus afterFourthPoison = afterFourthOutbound;
            for (var attempt = 0; attempt < 16; attempt++)
            {
                try
                {
                    host.ReconcileAsync().GetAwaiter().GetResult();
                }
                catch (OverflowException)
                {
                }
                catch (Exception)
                {
                }

                host.SubmitNextAsync().GetAwaiter().GetResult();
                afterFourthPoison = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
                if (afterFourthPoison.IsSettlementPoisoned)
                    break;
            }

            Assert.IsTrue(afterFourthPoison.IsSettlementPoisoned);
            Assert.IsFalse(afterFourthPoison.IsSettlementRetrying);
            Assert.IsTrue(afterFourthPoison.PendingSettlementCount >= 2);
            Assert.AreEqual(4, afterFourthPoison.ConsumedDepositCount);
            Assert.AreEqual(4, afterFourthPoison.MessageOutboxL2ToL1Count);
            Assert.AreEqual(4, afterFourthPoison.KnownForcedInclusionNonceCount);
            Assert.AreEqual(4, afterFourthPoison.KnownInboundNonceCount);
            Assert.IsNotNull(afterFourthPoison.Recovery.BlockedBatchNumber);
            Assert.IsNotNull(afterFourthPoison.Recovery.ArtifactContentHash);
            var fourthBlocked = afterFourthPoison.Recovery.BlockedBatchNumber!.Value;
            var fourthHash = afterFourthPoison.Recovery.ArtifactContentHash!;
            Assert.ThrowsExactly<InvalidOperationException>(
                () => host.RecoverPoisonedBatchAsync(fourthBlocked, UInt256.Zero)
                    .GetAwaiter().GetResult());
            host.RecoverPoisonedBatchAsync(fourthBlocked, fourthHash).GetAwaiter().GetResult();
            var afterFourthRecover = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsFalse(afterFourthRecover.IsSettlementPoisoned);
            Assert.IsTrue(afterFourthRecover.IsSettlementRetrying);
            Assert.AreEqual(SettlementRecoveryState.Retrying, afterFourthRecover.Recovery.State);
            Assert.AreEqual(0, afterFourthRecover.Recovery.RetryCount);
            Assert.IsTrue(host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult() >= 2);
            Assert.AreEqual(2UL, afterFourthRecover.LatestCheckpointBatchNumber);
            Assert.AreEqual(4, afterFourthRecover.ConsumedDepositCount);
            Assert.AreEqual(4, afterFourthRecover.MessageOutboxL2ToL1Count);
            Assert.AreEqual(4, afterFourthRecover.KnownForcedInclusionNonceCount);
            Assert.AreEqual(4, afterFourthRecover.KnownInboundNonceCount);
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(1));
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(2));
            Assert.IsTrue(host.GetRpcWithdrawalProof(fourthWdLeaf) is { Length: > 0 });
            Assert.IsTrue(host.GetRpcMessageProof(fourthOutbound.MessageHash) is { Length: > 0 });
            Assert.IsTrue(afterFourthRecover.IsOfflinePassportComplete);
            Assert.IsTrue(afterFourthRecover.IsOperatorReady);
            var fourthPoisonPath = Path.Combine(chainDir, "soft-seal-fourth-poison-recover.json");
            File.WriteAllText(fourthPoisonPath, $$"""
                {
                  "fourthPoisonBlockedBatch": {{fourthBlocked}},
                  "pendingSettlementCount": {{afterFourthRecover.PendingSettlementCount}},
                  "latestCheckpointBatchNumber": {{afterFourthRecover.LatestCheckpointBatchNumber}},
                  "consumedDepositCount": {{afterFourthRecover.ConsumedDepositCount}},
                  "messageOutboxL2ToL1Count": {{afterFourthRecover.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterFourthRecover.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterFourthRecover.KnownInboundNonceCount}},
                  "rpcBatch1Status": "{{host.GetRpcBatchStatus(1)}}",
                  "rpcBatch2Status": "{{host.GetRpcBatchStatus(2)}}",
                  "fourthWithdrawalProofPresent": true,
                  "fourthMessageProofPresent": true,
                  "isSettlementRetrying": true,
                  "isSettlementPoisoned": false
                }
                """);
            Assert.IsTrue(File.Exists(fourthPoisonPath));
            StringAssert.Contains(File.ReadAllText(fourthPoisonPath), "\"rpcBatch2Status\": \"Finalized\"");
            StringAssert.Contains(File.ReadAllText(fourthPoisonPath), "\"consumedDepositCount\": 4");
            StringAssert.Contains(File.ReadAllText(fourthPoisonPath), "\"messageOutboxL2ToL1Count\": 4");
            StringAssert.Contains(File.ReadAllText(fourthPoisonPath), "\"isSettlementRetrying\": true");

            // After fourth recover: re-publish local DA + fifth offline deposit while Retrying.
            Assert.IsTrue(host.SupportsLocalDaReader);
            var fourthRecoverDa1Payload = new byte[] { 0xDA, 0xD1, 0x01 };
            var fourthRecoverDa1 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 1,
                Payload = fourthRecoverDa1Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(DAMode.Local, fourthRecoverDa1.Layer);
            Assert.IsTrue(host.IsDaAvailableAsync(fourthRecoverDa1).AsTask().GetAwaiter().GetResult());
            var fourthRecoverDa1Read = host.CreateLocalDaReader().ReadAsync(fourthRecoverDa1).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(fourthRecoverDa1Read is { Length: 3 });
            CollectionAssert.AreEqual(fourthRecoverDa1Payload, fourthRecoverDa1Read!.Value.ToArray());
            var fourthRecoverDa2Payload = new byte[] { 0xDA, 0xD2, 0x02 };
            var fourthRecoverDa2 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 2,
                Payload = fourthRecoverDa2Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(host.IsDaAvailableAsync(fourthRecoverDa2).AsTask().GetAwaiter().GetResult());
            var fourthRecoverDa2Read = host.CreateLocalDaReader().ReadAsync(fourthRecoverDa2).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(fourthRecoverDa2Read is { Length: 3 });
            CollectionAssert.AreEqual(fourthRecoverDa2Payload, fourthRecoverDa2Read!.Value.ToArray());
            var fourthRecoverL1Asset = UInt160.Parse("0x" + new string('1', 40));
            var fourthRecoverL2Asset = UInt160.Parse("0x" + new string('2', 40));
            var deposit5 = new CrossChainMessage
            {
                SourceChainId = 0,
                TargetChainId = 20260716u,
                Nonce = 5,
                Sender = Account(0x66),
                Receiver = Account(0x55),
                MessageType = MessageType.Deposit,
                Payload = new DepositPayload
                {
                    L1Asset = fourthRecoverL1Asset,
                    L2Recipient = Account(0x55),
                    Amount = new BigInteger(5_000),
                }.Encode(),
                MessageHash = UInt256.Zero,
            };
            var mint5 = host.ProcessDeposit(deposit5);
            Assert.AreEqual(fourthRecoverL2Asset, mint5.L2Asset);
            Assert.IsTrue(host.HasConsumedDeposit(0, 1));
            Assert.IsTrue(host.HasConsumedDeposit(0, 2));
            Assert.IsTrue(host.HasConsumedDeposit(0, 3));
            Assert.IsTrue(host.HasConsumedDeposit(0, 4));
            Assert.IsTrue(host.HasConsumedDeposit(0, 5));
            Assert.AreEqual(5, host.ConsumedDepositCount);
            host.RecordRpcDeposit(new DepositStatus(0, 5, ConsumedOnL2: true, IncludedInBatch: 2));
            Assert.IsTrue(host.GetRpcL1DepositStatus(0, 5) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
            Assert.AreEqual(0, host.ScanSharedBridgeDepositsAsync().AsTask().GetAwaiter().GetResult());
            var afterFifthDeposit = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(5, afterFifthDeposit.ConsumedDepositCount);
            Assert.AreEqual(4, afterFifthDeposit.MessageOutboxL2ToL1Count);
            Assert.AreEqual(4, afterFifthDeposit.KnownForcedInclusionNonceCount);
            Assert.AreEqual(4, afterFifthDeposit.KnownInboundNonceCount);
            Assert.IsTrue(afterFifthDeposit.IsSettlementRetrying);
            Assert.IsTrue(afterFifthDeposit.PendingSettlementCount >= 2);
            Assert.AreEqual(2UL, afterFifthDeposit.LatestCheckpointBatchNumber);
            Assert.IsTrue(afterFifthDeposit.IsOfflinePassportComplete);
            var afterFourthRecoverDaPath = Path.Combine(chainDir, "soft-seal-after-fourth-recover-da-deposit.json");
            File.WriteAllText(afterFourthRecoverDaPath, $$"""
                {
                  "daBatch1Layer": "{{fourthRecoverDa1.Layer}}",
                  "daBatch2Layer": "{{fourthRecoverDa2.Layer}}",
                  "consumedDepositCount": {{afterFifthDeposit.ConsumedDepositCount}},
                  "deposit5IncludedInBatch": 2,
                  "messageOutboxL2ToL1Count": {{afterFifthDeposit.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterFifthDeposit.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterFifthDeposit.KnownInboundNonceCount}},
                  "latestCheckpointBatchNumber": {{afterFifthDeposit.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterFifthDeposit.PendingSettlementCount}},
                  "isSettlementRetrying": true,
                  "isOfflinePassportComplete": true
                }
                """);
            Assert.IsTrue(File.Exists(afterFourthRecoverDaPath));
            StringAssert.Contains(File.ReadAllText(afterFourthRecoverDaPath), "\"consumedDepositCount\": 5");
            StringAssert.Contains(File.ReadAllText(afterFourthRecoverDaPath), "\"deposit5IncludedInBatch\": 2");
            StringAssert.Contains(File.ReadAllText(afterFourthRecoverDaPath), "\"daBatch2Layer\": \"Local\"");
            var afterFourthRecoverProm = host.ExportPrometheusMetrics();
            Assert.IsFalse(string.IsNullOrWhiteSpace(afterFourthRecoverProm));
            var afterFourthRecoverPromPath = Path.Combine(chainDir, "soft-seal-after-fourth-recover-host.prom");
            host.WritePrometheusMetricsAsync(afterFourthRecoverPromPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(afterFourthRecoverPromPath));
            Assert.AreEqual(afterFourthRecoverProm, File.ReadAllText(afterFourthRecoverPromPath));

            // Fifth offline withdrawal/outbox + FI/inbound after fourth recover (no L1).
            var fifthSender = Account(0x99);
            var fifthL2Asset = UInt160.Parse("0x" + new string('2', 40));
            var fifthWdLeaf = host.StageWithdrawal(new WithdrawalRequest
            {
                ChainId = 20260716u,
                EmittingContract = fifthSender,
                L2Sender = fifthSender,
                L1Recipient = fifthSender,
                L2Asset = fifthL2Asset,
                Amount = new BigInteger(100),
                Nonce = 5,
            });
            Assert.AreNotEqual(UInt256.Zero, fifthWdLeaf);
            var fifthSealedWd = host.SealWithdrawalBatch();
            Assert.AreNotEqual(UInt256.Zero, fifthSealedWd.Root);
            Assert.AreEqual(0, host.StagedWithdrawalCount);
            Assert.IsTrue(fifthSealedWd.Tree.Count >= 1);
            var fifthMerkle = fifthSealedWd.Tree.GetProof(fifthSealedWd.Tree.Count - 1);
            Assert.AreEqual(fifthWdLeaf, fifthMerkle.Leaf);
            var fifthWdProofBytes = MerkleProofSerializer.Encode(fifthMerkle);
            Assert.IsTrue(fifthWdProofBytes.Length >= MerkleProofSerializer.HeaderSize);
            host.RecordRpcWithdrawalProof(fifthWdLeaf, fifthWdProofBytes);
            CollectionAssert.AreEqual(
                fifthWdProofBytes,
                host.GetRpcWithdrawalProof(fifthWdLeaf)!.Value.ToArray());
            var fifthOutboundDraft = new CrossChainMessage
            {
                SourceChainId = 20260716u,
                TargetChainId = 0,
                Nonce = 13,
                Sender = fifthSender,
                Receiver = fifthSender,
                MessageType = MessageType.Event,
                Payload = new byte[] { 0x05 },
                MessageHash = UInt256.Zero,
            };
            var fifthOutbound = fifthOutboundDraft with
            {
                MessageHash = MessageHasher.HashMessage(fifthOutboundDraft),
            };
            host.EnqueueOutboundMessagesAsync([fifthOutbound]).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(5, host.MessageOutbox!.L2ToL1Count);
            var fifthMsgProofBytes = fifthOutbound.MessageHash.GetSpan().ToArray();
            host.RecordRpcMessageProof(fifthOutbound.MessageHash, fifthMsgProofBytes);
            CollectionAssert.AreEqual(
                fifthMsgProofBytes,
                host.GetRpcMessageProof(fifthOutbound.MessageHash)!.Value.ToArray());
            host.RecordMessageRouterFinalizedProof(fifthOutbound.MessageHash, fifthMsgProofBytes);
            CollectionAssert.AreEqual(
                fifthMsgProofBytes,
                host.GetMessageRouterProofAsync(fifthOutbound.MessageHash).AsTask().GetAwaiter().GetResult()!.Value.ToArray());
            Assert.IsTrue(host.RegisterForcedInclusionNonce(15));
            Assert.IsFalse(host.RegisterForcedInclusionNonce(15));
            Assert.AreEqual(5, host.KnownForcedInclusionNonceCount);
            Assert.IsTrue(host.RegisterInboundMessageNonce(15));
            Assert.IsFalse(host.RegisterInboundMessageNonce(15));
            Assert.AreEqual(5, host.KnownInboundNonceCount);
            Assert.AreEqual(0, host.OpenBatchForcedInclusionCount);
            Assert.AreEqual(0, host.OpenBatchL1MessageCount);
            var afterFifthOutbound = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(5, afterFifthOutbound.ConsumedDepositCount);
            Assert.AreEqual(5, afterFifthOutbound.MessageOutboxL2ToL1Count);
            Assert.AreEqual(5, afterFifthOutbound.KnownForcedInclusionNonceCount);
            Assert.AreEqual(5, afterFifthOutbound.KnownInboundNonceCount);
            Assert.IsTrue(afterFifthOutbound.IsSettlementRetrying);
            Assert.IsTrue(afterFifthOutbound.PendingSettlementCount >= 2);
            Assert.AreEqual(2UL, afterFifthOutbound.LatestCheckpointBatchNumber);
            var fifthOutboundPath = Path.Combine(chainDir, "soft-seal-after-fourth-recover-fifth-outbound.json");
            File.WriteAllText(fifthOutboundPath, $$"""
                {
                  "consumedDepositCount": {{afterFifthOutbound.ConsumedDepositCount}},
                  "messageOutboxL2ToL1Count": {{afterFifthOutbound.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterFifthOutbound.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterFifthOutbound.KnownInboundNonceCount}},
                  "withdrawalLeaf": "{{fifthWdLeaf}}",
                  "outboundMessageHash": "{{fifthOutbound.MessageHash}}",
                  "withdrawalProofBytes": {{fifthWdProofBytes.Length}},
                  "messageProofBytes": {{fifthMsgProofBytes.Length}},
                  "latestCheckpointBatchNumber": {{afterFifthOutbound.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterFifthOutbound.PendingSettlementCount}},
                  "isSettlementRetrying": true
                }
                """);
            Assert.IsTrue(File.Exists(fifthOutboundPath));
            StringAssert.Contains(File.ReadAllText(fifthOutboundPath), "\"messageOutboxL2ToL1Count\": 5");
            StringAssert.Contains(File.ReadAllText(fifthOutboundPath), "\"knownForcedInclusionNonceCount\": 5");
            StringAssert.Contains(File.ReadAllText(fifthOutboundPath), "\"consumedDepositCount\": 5");
            var fifthOutboundRpcPath = Path.Combine(chainDir, "soft-seal-after-fourth-recover-fifth-outbound-rpc.json");
            File.WriteAllText(fifthOutboundRpcPath, $$"""
                {
                  "withdrawalLeaf": "{{fifthWdLeaf}}",
                  "outboundMessageHash": "{{fifthOutbound.MessageHash}}",
                  "withdrawalProofBytes": {{fifthWdProofBytes.Length}},
                  "messageProofBytes": {{fifthMsgProofBytes.Length}},
                  "messageOutboxL2ToL1Count": 5,
                  "knownForcedInclusionNonceCount": 5,
                  "knownInboundNonceCount": 5,
                  "consumedDepositCount": 5,
                  "isSettlementRetrying": true
                }
                """);
            Assert.IsTrue(File.Exists(fifthOutboundRpcPath));
            StringAssert.Contains(File.ReadAllText(fifthOutboundRpcPath), "\"outboundMessageHash\": \"" + fifthOutbound.MessageHash + "\"");

            // Fifth poison→recover after quintuple soft multi-batch path retains soft state.
            LocalHostOperatorStatus afterFifthPoison = afterFifthOutbound;
            for (var attempt = 0; attempt < 16; attempt++)
            {
                try
                {
                    host.ReconcileAsync().GetAwaiter().GetResult();
                }
                catch (OverflowException)
                {
                }
                catch (Exception)
                {
                }

                host.SubmitNextAsync().GetAwaiter().GetResult();
                afterFifthPoison = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
                if (afterFifthPoison.IsSettlementPoisoned)
                    break;
            }

            Assert.IsTrue(afterFifthPoison.IsSettlementPoisoned);
            Assert.IsFalse(afterFifthPoison.IsSettlementRetrying);
            Assert.IsTrue(afterFifthPoison.PendingSettlementCount >= 2);
            Assert.AreEqual(5, afterFifthPoison.ConsumedDepositCount);
            Assert.AreEqual(5, afterFifthPoison.MessageOutboxL2ToL1Count);
            Assert.AreEqual(5, afterFifthPoison.KnownForcedInclusionNonceCount);
            Assert.AreEqual(5, afterFifthPoison.KnownInboundNonceCount);
            Assert.IsNotNull(afterFifthPoison.Recovery.BlockedBatchNumber);
            Assert.IsNotNull(afterFifthPoison.Recovery.ArtifactContentHash);
            var fifthBlocked = afterFifthPoison.Recovery.BlockedBatchNumber!.Value;
            var fifthHash = afterFifthPoison.Recovery.ArtifactContentHash!;
            Assert.ThrowsExactly<InvalidOperationException>(
                () => host.RecoverPoisonedBatchAsync(fifthBlocked, UInt256.Zero)
                    .GetAwaiter().GetResult());
            host.RecoverPoisonedBatchAsync(fifthBlocked, fifthHash).GetAwaiter().GetResult();
            var afterFifthRecover = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsFalse(afterFifthRecover.IsSettlementPoisoned);
            Assert.IsTrue(afterFifthRecover.IsSettlementRetrying);
            Assert.AreEqual(SettlementRecoveryState.Retrying, afterFifthRecover.Recovery.State);
            Assert.AreEqual(0, afterFifthRecover.Recovery.RetryCount);
            Assert.IsTrue(host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult() >= 2);
            Assert.AreEqual(2UL, afterFifthRecover.LatestCheckpointBatchNumber);
            Assert.AreEqual(5, afterFifthRecover.ConsumedDepositCount);
            Assert.AreEqual(5, afterFifthRecover.MessageOutboxL2ToL1Count);
            Assert.AreEqual(5, afterFifthRecover.KnownForcedInclusionNonceCount);
            Assert.AreEqual(5, afterFifthRecover.KnownInboundNonceCount);
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(1));
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(2));
            Assert.IsTrue(host.GetRpcWithdrawalProof(fifthWdLeaf) is { Length: > 0 });
            Assert.IsTrue(host.GetRpcMessageProof(fifthOutbound.MessageHash) is { Length: > 0 });
            Assert.IsTrue(afterFifthRecover.IsOfflinePassportComplete);
            Assert.IsTrue(afterFifthRecover.IsOperatorReady);
            var fifthPoisonPath = Path.Combine(chainDir, "soft-seal-fifth-poison-recover.json");
            File.WriteAllText(fifthPoisonPath, $$"""
                {
                  "fifthPoisonBlockedBatch": {{fifthBlocked}},
                  "pendingSettlementCount": {{afterFifthRecover.PendingSettlementCount}},
                  "latestCheckpointBatchNumber": {{afterFifthRecover.LatestCheckpointBatchNumber}},
                  "consumedDepositCount": {{afterFifthRecover.ConsumedDepositCount}},
                  "messageOutboxL2ToL1Count": {{afterFifthRecover.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterFifthRecover.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterFifthRecover.KnownInboundNonceCount}},
                  "rpcBatch1Status": "{{host.GetRpcBatchStatus(1)}}",
                  "rpcBatch2Status": "{{host.GetRpcBatchStatus(2)}}",
                  "fifthWithdrawalProofPresent": true,
                  "fifthMessageProofPresent": true,
                  "isSettlementRetrying": true,
                  "isSettlementPoisoned": false
                }
                """);
            Assert.IsTrue(File.Exists(fifthPoisonPath));
            StringAssert.Contains(File.ReadAllText(fifthPoisonPath), "\"rpcBatch2Status\": \"Finalized\"");
            StringAssert.Contains(File.ReadAllText(fifthPoisonPath), "\"consumedDepositCount\": 5");
            StringAssert.Contains(File.ReadAllText(fifthPoisonPath), "\"messageOutboxL2ToL1Count\": 5");
            StringAssert.Contains(File.ReadAllText(fifthPoisonPath), "\"isSettlementRetrying\": true");

            // After fifth recover: re-publish local DA + sixth offline deposit while Retrying.
            Assert.IsTrue(host.SupportsLocalDaReader);
            var fifthRecoverDa1Payload = new byte[] { 0xDA, 0xE1, 0x01 };
            var fifthRecoverDa1 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 1,
                Payload = fifthRecoverDa1Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(DAMode.Local, fifthRecoverDa1.Layer);
            Assert.IsTrue(host.IsDaAvailableAsync(fifthRecoverDa1).AsTask().GetAwaiter().GetResult());
            var fifthRecoverDa1Read = host.CreateLocalDaReader().ReadAsync(fifthRecoverDa1).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(fifthRecoverDa1Read is { Length: 3 });
            CollectionAssert.AreEqual(fifthRecoverDa1Payload, fifthRecoverDa1Read!.Value.ToArray());
            var fifthRecoverDa2Payload = new byte[] { 0xDA, 0xE2, 0x02 };
            var fifthRecoverDa2 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 2,
                Payload = fifthRecoverDa2Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(host.IsDaAvailableAsync(fifthRecoverDa2).AsTask().GetAwaiter().GetResult());
            var fifthRecoverDa2Read = host.CreateLocalDaReader().ReadAsync(fifthRecoverDa2).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(fifthRecoverDa2Read is { Length: 3 });
            CollectionAssert.AreEqual(fifthRecoverDa2Payload, fifthRecoverDa2Read!.Value.ToArray());
            var fifthRecoverL1Asset = UInt160.Parse("0x" + new string('1', 40));
            var fifthRecoverL2Asset = UInt160.Parse("0x" + new string('2', 40));
            var deposit6 = new CrossChainMessage
            {
                SourceChainId = 0,
                TargetChainId = 20260716u,
                Nonce = 6,
                Sender = Account(0x66),
                Receiver = Account(0x55),
                MessageType = MessageType.Deposit,
                Payload = new DepositPayload
                {
                    L1Asset = fifthRecoverL1Asset,
                    L2Recipient = Account(0x55),
                    Amount = new BigInteger(6_000),
                }.Encode(),
                MessageHash = UInt256.Zero,
            };
            var mint6 = host.ProcessDeposit(deposit6);
            Assert.AreEqual(fifthRecoverL2Asset, mint6.L2Asset);
            Assert.IsTrue(host.HasConsumedDeposit(0, 1));
            Assert.IsTrue(host.HasConsumedDeposit(0, 2));
            Assert.IsTrue(host.HasConsumedDeposit(0, 3));
            Assert.IsTrue(host.HasConsumedDeposit(0, 4));
            Assert.IsTrue(host.HasConsumedDeposit(0, 5));
            Assert.IsTrue(host.HasConsumedDeposit(0, 6));
            Assert.AreEqual(6, host.ConsumedDepositCount);
            host.RecordRpcDeposit(new DepositStatus(0, 6, ConsumedOnL2: true, IncludedInBatch: 2));
            Assert.IsTrue(host.GetRpcL1DepositStatus(0, 6) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
            Assert.AreEqual(0, host.ScanSharedBridgeDepositsAsync().AsTask().GetAwaiter().GetResult());
            var afterSixthDeposit = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(6, afterSixthDeposit.ConsumedDepositCount);
            Assert.AreEqual(5, afterSixthDeposit.MessageOutboxL2ToL1Count);
            Assert.AreEqual(5, afterSixthDeposit.KnownForcedInclusionNonceCount);
            Assert.AreEqual(5, afterSixthDeposit.KnownInboundNonceCount);
            Assert.IsTrue(afterSixthDeposit.IsSettlementRetrying);
            Assert.IsTrue(afterSixthDeposit.PendingSettlementCount >= 2);
            Assert.AreEqual(2UL, afterSixthDeposit.LatestCheckpointBatchNumber);
            Assert.IsTrue(afterSixthDeposit.IsOfflinePassportComplete);
            var afterFifthRecoverDaPath = Path.Combine(chainDir, "soft-seal-after-fifth-recover-da-deposit.json");
            File.WriteAllText(afterFifthRecoverDaPath, $$"""
                {
                  "daBatch1Layer": "{{fifthRecoverDa1.Layer}}",
                  "daBatch2Layer": "{{fifthRecoverDa2.Layer}}",
                  "consumedDepositCount": {{afterSixthDeposit.ConsumedDepositCount}},
                  "deposit6IncludedInBatch": 2,
                  "messageOutboxL2ToL1Count": {{afterSixthDeposit.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterSixthDeposit.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterSixthDeposit.KnownInboundNonceCount}},
                  "latestCheckpointBatchNumber": {{afterSixthDeposit.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterSixthDeposit.PendingSettlementCount}},
                  "isSettlementRetrying": true,
                  "isOfflinePassportComplete": true
                }
                """);
            Assert.IsTrue(File.Exists(afterFifthRecoverDaPath));
            StringAssert.Contains(File.ReadAllText(afterFifthRecoverDaPath), "\"consumedDepositCount\": 6");
            StringAssert.Contains(File.ReadAllText(afterFifthRecoverDaPath), "\"deposit6IncludedInBatch\": 2");
            StringAssert.Contains(File.ReadAllText(afterFifthRecoverDaPath), "\"daBatch2Layer\": \"Local\"");
            var afterFifthRecoverProm = host.ExportPrometheusMetrics();
            Assert.IsFalse(string.IsNullOrWhiteSpace(afterFifthRecoverProm));
            var afterFifthRecoverPromPath = Path.Combine(chainDir, "soft-seal-after-fifth-recover-host.prom");
            host.WritePrometheusMetricsAsync(afterFifthRecoverPromPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(afterFifthRecoverPromPath));
            Assert.AreEqual(afterFifthRecoverProm, File.ReadAllText(afterFifthRecoverPromPath));

            // Sixth offline withdrawal/outbox + FI/inbound after fifth recover (no L1).
            var sixthSender = Account(0xaa);
            var sixthL2Asset = UInt160.Parse("0x" + new string('2', 40));
            var sixthWdLeaf = host.StageWithdrawal(new WithdrawalRequest
            {
                ChainId = 20260716u,
                EmittingContract = sixthSender,
                L2Sender = sixthSender,
                L1Recipient = sixthSender,
                L2Asset = sixthL2Asset,
                Amount = new BigInteger(100),
                Nonce = 6,
            });
            Assert.AreNotEqual(UInt256.Zero, sixthWdLeaf);
            var sixthSealedWd = host.SealWithdrawalBatch();
            Assert.AreNotEqual(UInt256.Zero, sixthSealedWd.Root);
            Assert.AreEqual(0, host.StagedWithdrawalCount);
            Assert.IsTrue(sixthSealedWd.Tree.Count >= 1);
            var sixthMerkle = sixthSealedWd.Tree.GetProof(sixthSealedWd.Tree.Count - 1);
            Assert.AreEqual(sixthWdLeaf, sixthMerkle.Leaf);
            var sixthWdProofBytes = MerkleProofSerializer.Encode(sixthMerkle);
            Assert.IsTrue(sixthWdProofBytes.Length >= MerkleProofSerializer.HeaderSize);
            host.RecordRpcWithdrawalProof(sixthWdLeaf, sixthWdProofBytes);
            CollectionAssert.AreEqual(
                sixthWdProofBytes,
                host.GetRpcWithdrawalProof(sixthWdLeaf)!.Value.ToArray());
            var sixthOutboundDraft = new CrossChainMessage
            {
                SourceChainId = 20260716u,
                TargetChainId = 0,
                Nonce = 14,
                Sender = sixthSender,
                Receiver = sixthSender,
                MessageType = MessageType.Event,
                Payload = new byte[] { 0x06 },
                MessageHash = UInt256.Zero,
            };
            var sixthOutbound = sixthOutboundDraft with
            {
                MessageHash = MessageHasher.HashMessage(sixthOutboundDraft),
            };
            host.EnqueueOutboundMessagesAsync([sixthOutbound]).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(6, host.MessageOutbox!.L2ToL1Count);
            var sixthMsgProofBytes = sixthOutbound.MessageHash.GetSpan().ToArray();
            host.RecordRpcMessageProof(sixthOutbound.MessageHash, sixthMsgProofBytes);
            CollectionAssert.AreEqual(
                sixthMsgProofBytes,
                host.GetRpcMessageProof(sixthOutbound.MessageHash)!.Value.ToArray());
            host.RecordMessageRouterFinalizedProof(sixthOutbound.MessageHash, sixthMsgProofBytes);
            CollectionAssert.AreEqual(
                sixthMsgProofBytes,
                host.GetMessageRouterProofAsync(sixthOutbound.MessageHash).AsTask().GetAwaiter().GetResult()!.Value.ToArray());
            Assert.IsTrue(host.RegisterForcedInclusionNonce(16));
            Assert.IsFalse(host.RegisterForcedInclusionNonce(16));
            Assert.AreEqual(6, host.KnownForcedInclusionNonceCount);
            Assert.IsTrue(host.RegisterInboundMessageNonce(16));
            Assert.IsFalse(host.RegisterInboundMessageNonce(16));
            Assert.AreEqual(6, host.KnownInboundNonceCount);
            Assert.AreEqual(0, host.OpenBatchForcedInclusionCount);
            Assert.AreEqual(0, host.OpenBatchL1MessageCount);
            var afterSixthOutbound = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(6, afterSixthOutbound.ConsumedDepositCount);
            Assert.AreEqual(6, afterSixthOutbound.MessageOutboxL2ToL1Count);
            Assert.AreEqual(6, afterSixthOutbound.KnownForcedInclusionNonceCount);
            Assert.AreEqual(6, afterSixthOutbound.KnownInboundNonceCount);
            Assert.IsTrue(afterSixthOutbound.IsSettlementRetrying);
            Assert.IsTrue(afterSixthOutbound.PendingSettlementCount >= 2);
            Assert.AreEqual(2UL, afterSixthOutbound.LatestCheckpointBatchNumber);
            var sixthOutboundPath = Path.Combine(chainDir, "soft-seal-after-fifth-recover-sixth-outbound.json");
            File.WriteAllText(sixthOutboundPath, $$"""
                {
                  "consumedDepositCount": {{afterSixthOutbound.ConsumedDepositCount}},
                  "messageOutboxL2ToL1Count": {{afterSixthOutbound.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterSixthOutbound.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterSixthOutbound.KnownInboundNonceCount}},
                  "withdrawalLeaf": "{{sixthWdLeaf}}",
                  "outboundMessageHash": "{{sixthOutbound.MessageHash}}",
                  "withdrawalProofBytes": {{sixthWdProofBytes.Length}},
                  "messageProofBytes": {{sixthMsgProofBytes.Length}},
                  "latestCheckpointBatchNumber": {{afterSixthOutbound.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterSixthOutbound.PendingSettlementCount}},
                  "isSettlementRetrying": true
                }
                """);
            Assert.IsTrue(File.Exists(sixthOutboundPath));
            StringAssert.Contains(File.ReadAllText(sixthOutboundPath), "\"messageOutboxL2ToL1Count\": 6");
            StringAssert.Contains(File.ReadAllText(sixthOutboundPath), "\"knownForcedInclusionNonceCount\": 6");
            StringAssert.Contains(File.ReadAllText(sixthOutboundPath), "\"consumedDepositCount\": 6");
            var sixthOutboundRpcPath = Path.Combine(chainDir, "soft-seal-after-fifth-recover-sixth-outbound-rpc.json");
            File.WriteAllText(sixthOutboundRpcPath, $$"""
                {
                  "withdrawalLeaf": "{{sixthWdLeaf}}",
                  "outboundMessageHash": "{{sixthOutbound.MessageHash}}",
                  "withdrawalProofBytes": {{sixthWdProofBytes.Length}},
                  "messageProofBytes": {{sixthMsgProofBytes.Length}},
                  "messageOutboxL2ToL1Count": 6,
                  "knownForcedInclusionNonceCount": 6,
                  "knownInboundNonceCount": 6,
                  "consumedDepositCount": 6,
                  "isSettlementRetrying": true
                }
                """);
            Assert.IsTrue(File.Exists(sixthOutboundRpcPath));
            StringAssert.Contains(File.ReadAllText(sixthOutboundRpcPath), "\"outboundMessageHash\": \"" + sixthOutbound.MessageHash + "\"");

            // Sixth poison→recover after sextuple soft multi-batch path retains soft state.
            LocalHostOperatorStatus afterSixthPoison = afterSixthOutbound;
            for (var attempt = 0; attempt < 16; attempt++)
            {
                try
                {
                    host.ReconcileAsync().GetAwaiter().GetResult();
                }
                catch (OverflowException)
                {
                }
                catch (Exception)
                {
                }

                host.SubmitNextAsync().GetAwaiter().GetResult();
                afterSixthPoison = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
                if (afterSixthPoison.IsSettlementPoisoned)
                    break;
            }

            Assert.IsTrue(afterSixthPoison.IsSettlementPoisoned);
            Assert.IsFalse(afterSixthPoison.IsSettlementRetrying);
            Assert.IsTrue(afterSixthPoison.PendingSettlementCount >= 2);
            Assert.AreEqual(6, afterSixthPoison.ConsumedDepositCount);
            Assert.AreEqual(6, afterSixthPoison.MessageOutboxL2ToL1Count);
            Assert.AreEqual(6, afterSixthPoison.KnownForcedInclusionNonceCount);
            Assert.AreEqual(6, afterSixthPoison.KnownInboundNonceCount);
            Assert.IsNotNull(afterSixthPoison.Recovery.BlockedBatchNumber);
            Assert.IsNotNull(afterSixthPoison.Recovery.ArtifactContentHash);
            var sixthBlocked = afterSixthPoison.Recovery.BlockedBatchNumber!.Value;
            var sixthHash = afterSixthPoison.Recovery.ArtifactContentHash!;
            Assert.ThrowsExactly<InvalidOperationException>(
                () => host.RecoverPoisonedBatchAsync(sixthBlocked, UInt256.Zero)
                    .GetAwaiter().GetResult());
            host.RecoverPoisonedBatchAsync(sixthBlocked, sixthHash).GetAwaiter().GetResult();
            var afterSixthRecover = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsFalse(afterSixthRecover.IsSettlementPoisoned);
            Assert.IsTrue(afterSixthRecover.IsSettlementRetrying);
            Assert.AreEqual(SettlementRecoveryState.Retrying, afterSixthRecover.Recovery.State);
            Assert.AreEqual(0, afterSixthRecover.Recovery.RetryCount);
            Assert.IsTrue(host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult() >= 2);
            Assert.AreEqual(2UL, afterSixthRecover.LatestCheckpointBatchNumber);
            Assert.AreEqual(6, afterSixthRecover.ConsumedDepositCount);
            Assert.AreEqual(6, afterSixthRecover.MessageOutboxL2ToL1Count);
            Assert.AreEqual(6, afterSixthRecover.KnownForcedInclusionNonceCount);
            Assert.AreEqual(6, afterSixthRecover.KnownInboundNonceCount);
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(1));
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(2));
            Assert.IsTrue(host.GetRpcWithdrawalProof(sixthWdLeaf) is { Length: > 0 });
            Assert.IsTrue(host.GetRpcMessageProof(sixthOutbound.MessageHash) is { Length: > 0 });
            Assert.IsTrue(afterSixthRecover.IsOfflinePassportComplete);
            Assert.IsTrue(afterSixthRecover.IsOperatorReady);
            var sixthPoisonPath = Path.Combine(chainDir, "soft-seal-sixth-poison-recover.json");
            File.WriteAllText(sixthPoisonPath, $$"""
                {
                  "sixthPoisonBlockedBatch": {{sixthBlocked}},
                  "pendingSettlementCount": {{afterSixthRecover.PendingSettlementCount}},
                  "latestCheckpointBatchNumber": {{afterSixthRecover.LatestCheckpointBatchNumber}},
                  "consumedDepositCount": {{afterSixthRecover.ConsumedDepositCount}},
                  "messageOutboxL2ToL1Count": {{afterSixthRecover.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterSixthRecover.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterSixthRecover.KnownInboundNonceCount}},
                  "rpcBatch1Status": "{{host.GetRpcBatchStatus(1)}}",
                  "rpcBatch2Status": "{{host.GetRpcBatchStatus(2)}}",
                  "sixthWithdrawalProofPresent": true,
                  "sixthMessageProofPresent": true,
                  "isSettlementRetrying": true,
                  "isSettlementPoisoned": false
                }
                """);
            Assert.IsTrue(File.Exists(sixthPoisonPath));
            StringAssert.Contains(File.ReadAllText(sixthPoisonPath), "\"rpcBatch2Status\": \"Finalized\"");
            StringAssert.Contains(File.ReadAllText(sixthPoisonPath), "\"consumedDepositCount\": 6");
            StringAssert.Contains(File.ReadAllText(sixthPoisonPath), "\"messageOutboxL2ToL1Count\": 6");
            StringAssert.Contains(File.ReadAllText(sixthPoisonPath), "\"isSettlementRetrying\": true");

            // After sixth recover: re-publish local DA + seventh offline deposit while Retrying.
            Assert.IsTrue(host.SupportsLocalDaReader);
            var sixthRecoverDa1Payload = new byte[] { 0xDA, 0xF1, 0x01 };
            var sixthRecoverDa1 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 1,
                Payload = sixthRecoverDa1Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(DAMode.Local, sixthRecoverDa1.Layer);
            Assert.IsTrue(host.IsDaAvailableAsync(sixthRecoverDa1).AsTask().GetAwaiter().GetResult());
            var sixthRecoverDa1Read = host.CreateLocalDaReader().ReadAsync(sixthRecoverDa1).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(sixthRecoverDa1Read is { Length: 3 });
            CollectionAssert.AreEqual(sixthRecoverDa1Payload, sixthRecoverDa1Read!.Value.ToArray());
            var sixthRecoverDa2Payload = new byte[] { 0xDA, 0xF2, 0x02 };
            var sixthRecoverDa2 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 2,
                Payload = sixthRecoverDa2Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(host.IsDaAvailableAsync(sixthRecoverDa2).AsTask().GetAwaiter().GetResult());
            var sixthRecoverDa2Read = host.CreateLocalDaReader().ReadAsync(sixthRecoverDa2).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(sixthRecoverDa2Read is { Length: 3 });
            CollectionAssert.AreEqual(sixthRecoverDa2Payload, sixthRecoverDa2Read!.Value.ToArray());
            var sixthRecoverL1Asset = UInt160.Parse("0x" + new string('1', 40));
            var sixthRecoverL2Asset = UInt160.Parse("0x" + new string('2', 40));
            var deposit7 = new CrossChainMessage
            {
                SourceChainId = 0,
                TargetChainId = 20260716u,
                Nonce = 7,
                Sender = Account(0x66),
                Receiver = Account(0x55),
                MessageType = MessageType.Deposit,
                Payload = new DepositPayload
                {
                    L1Asset = sixthRecoverL1Asset,
                    L2Recipient = Account(0x55),
                    Amount = new BigInteger(7_000),
                }.Encode(),
                MessageHash = UInt256.Zero,
            };
            var mint7 = host.ProcessDeposit(deposit7);
            Assert.AreEqual(sixthRecoverL2Asset, mint7.L2Asset);
            Assert.IsTrue(host.HasConsumedDeposit(0, 1));
            Assert.IsTrue(host.HasConsumedDeposit(0, 2));
            Assert.IsTrue(host.HasConsumedDeposit(0, 3));
            Assert.IsTrue(host.HasConsumedDeposit(0, 4));
            Assert.IsTrue(host.HasConsumedDeposit(0, 5));
            Assert.IsTrue(host.HasConsumedDeposit(0, 6));
            Assert.IsTrue(host.HasConsumedDeposit(0, 7));
            Assert.AreEqual(7, host.ConsumedDepositCount);
            host.RecordRpcDeposit(new DepositStatus(0, 7, ConsumedOnL2: true, IncludedInBatch: 2));
            Assert.IsTrue(host.GetRpcL1DepositStatus(0, 7) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
            Assert.AreEqual(0, host.ScanSharedBridgeDepositsAsync().AsTask().GetAwaiter().GetResult());
            var afterSeventhDeposit = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(7, afterSeventhDeposit.ConsumedDepositCount);
            Assert.AreEqual(6, afterSeventhDeposit.MessageOutboxL2ToL1Count);
            Assert.AreEqual(6, afterSeventhDeposit.KnownForcedInclusionNonceCount);
            Assert.AreEqual(6, afterSeventhDeposit.KnownInboundNonceCount);
            Assert.IsTrue(afterSeventhDeposit.IsSettlementRetrying);
            Assert.IsTrue(afterSeventhDeposit.PendingSettlementCount >= 2);
            Assert.AreEqual(2UL, afterSeventhDeposit.LatestCheckpointBatchNumber);
            Assert.IsTrue(afterSeventhDeposit.IsOfflinePassportComplete);
            var afterSixthRecoverDaPath = Path.Combine(chainDir, "soft-seal-after-sixth-recover-da-deposit.json");
            File.WriteAllText(afterSixthRecoverDaPath, $$"""
                {
                  "daBatch1Layer": "{{sixthRecoverDa1.Layer}}",
                  "daBatch2Layer": "{{sixthRecoverDa2.Layer}}",
                  "consumedDepositCount": {{afterSeventhDeposit.ConsumedDepositCount}},
                  "deposit7IncludedInBatch": 2,
                  "messageOutboxL2ToL1Count": {{afterSeventhDeposit.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterSeventhDeposit.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterSeventhDeposit.KnownInboundNonceCount}},
                  "latestCheckpointBatchNumber": {{afterSeventhDeposit.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterSeventhDeposit.PendingSettlementCount}},
                  "isSettlementRetrying": true,
                  "isOfflinePassportComplete": true
                }
                """);
            Assert.IsTrue(File.Exists(afterSixthRecoverDaPath));
            StringAssert.Contains(File.ReadAllText(afterSixthRecoverDaPath), "\"consumedDepositCount\": 7");
            StringAssert.Contains(File.ReadAllText(afterSixthRecoverDaPath), "\"deposit7IncludedInBatch\": 2");
            StringAssert.Contains(File.ReadAllText(afterSixthRecoverDaPath), "\"daBatch2Layer\": \"Local\"");
            var afterSixthRecoverProm = host.ExportPrometheusMetrics();
            Assert.IsFalse(string.IsNullOrWhiteSpace(afterSixthRecoverProm));
            var afterSixthRecoverPromPath = Path.Combine(chainDir, "soft-seal-after-sixth-recover-host.prom");
            host.WritePrometheusMetricsAsync(afterSixthRecoverPromPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(afterSixthRecoverPromPath));
            Assert.AreEqual(afterSixthRecoverProm, File.ReadAllText(afterSixthRecoverPromPath));

            // Seventh offline withdrawal/outbox + FI/inbound after sixth recover (no L1).
            var seventhSender = Account(0xbb);
            var seventhL2Asset = UInt160.Parse("0x" + new string('2', 40));
            var seventhWdLeaf = host.StageWithdrawal(new WithdrawalRequest
            {
                ChainId = 20260716u,
                EmittingContract = seventhSender,
                L2Sender = seventhSender,
                L1Recipient = seventhSender,
                L2Asset = seventhL2Asset,
                Amount = new BigInteger(100),
                Nonce = 7,
            });
            Assert.AreNotEqual(UInt256.Zero, seventhWdLeaf);
            var seventhSealedWd = host.SealWithdrawalBatch();
            Assert.AreNotEqual(UInt256.Zero, seventhSealedWd.Root);
            Assert.AreEqual(0, host.StagedWithdrawalCount);
            Assert.IsTrue(seventhSealedWd.Tree.Count >= 1);
            var seventhMerkle = seventhSealedWd.Tree.GetProof(seventhSealedWd.Tree.Count - 1);
            Assert.AreEqual(seventhWdLeaf, seventhMerkle.Leaf);
            var seventhWdProofBytes = MerkleProofSerializer.Encode(seventhMerkle);
            Assert.IsTrue(seventhWdProofBytes.Length >= MerkleProofSerializer.HeaderSize);
            host.RecordRpcWithdrawalProof(seventhWdLeaf, seventhWdProofBytes);
            CollectionAssert.AreEqual(
                seventhWdProofBytes,
                host.GetRpcWithdrawalProof(seventhWdLeaf)!.Value.ToArray());
            var seventhOutboundDraft = new CrossChainMessage
            {
                SourceChainId = 20260716u,
                TargetChainId = 0,
                Nonce = 15,
                Sender = seventhSender,
                Receiver = seventhSender,
                MessageType = MessageType.Event,
                Payload = new byte[] { 0x07 },
                MessageHash = UInt256.Zero,
            };
            var seventhOutbound = seventhOutboundDraft with
            {
                MessageHash = MessageHasher.HashMessage(seventhOutboundDraft),
            };
            host.EnqueueOutboundMessagesAsync([seventhOutbound]).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(7, host.MessageOutbox!.L2ToL1Count);
            var seventhMsgProofBytes = seventhOutbound.MessageHash.GetSpan().ToArray();
            host.RecordRpcMessageProof(seventhOutbound.MessageHash, seventhMsgProofBytes);
            CollectionAssert.AreEqual(
                seventhMsgProofBytes,
                host.GetRpcMessageProof(seventhOutbound.MessageHash)!.Value.ToArray());
            host.RecordMessageRouterFinalizedProof(seventhOutbound.MessageHash, seventhMsgProofBytes);
            CollectionAssert.AreEqual(
                seventhMsgProofBytes,
                host.GetMessageRouterProofAsync(seventhOutbound.MessageHash).AsTask().GetAwaiter().GetResult()!.Value.ToArray());
            Assert.IsTrue(host.RegisterForcedInclusionNonce(17));
            Assert.IsFalse(host.RegisterForcedInclusionNonce(17));
            Assert.AreEqual(7, host.KnownForcedInclusionNonceCount);
            Assert.IsTrue(host.RegisterInboundMessageNonce(17));
            Assert.IsFalse(host.RegisterInboundMessageNonce(17));
            Assert.AreEqual(7, host.KnownInboundNonceCount);
            Assert.AreEqual(0, host.OpenBatchForcedInclusionCount);
            Assert.AreEqual(0, host.OpenBatchL1MessageCount);
            var afterSeventhOutbound = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(7, afterSeventhOutbound.ConsumedDepositCount);
            Assert.AreEqual(7, afterSeventhOutbound.MessageOutboxL2ToL1Count);
            Assert.AreEqual(7, afterSeventhOutbound.KnownForcedInclusionNonceCount);
            Assert.AreEqual(7, afterSeventhOutbound.KnownInboundNonceCount);
            Assert.IsTrue(afterSeventhOutbound.IsSettlementRetrying);
            Assert.IsTrue(afterSeventhOutbound.PendingSettlementCount >= 2);
            Assert.AreEqual(2UL, afterSeventhOutbound.LatestCheckpointBatchNumber);
            var seventhOutboundPath = Path.Combine(chainDir, "soft-seal-after-sixth-recover-seventh-outbound.json");
            File.WriteAllText(seventhOutboundPath, $$"""
                {
                  "consumedDepositCount": {{afterSeventhOutbound.ConsumedDepositCount}},
                  "messageOutboxL2ToL1Count": {{afterSeventhOutbound.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterSeventhOutbound.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterSeventhOutbound.KnownInboundNonceCount}},
                  "withdrawalLeaf": "{{seventhWdLeaf}}",
                  "outboundMessageHash": "{{seventhOutbound.MessageHash}}",
                  "withdrawalProofBytes": {{seventhWdProofBytes.Length}},
                  "messageProofBytes": {{seventhMsgProofBytes.Length}},
                  "latestCheckpointBatchNumber": {{afterSeventhOutbound.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterSeventhOutbound.PendingSettlementCount}},
                  "isSettlementRetrying": true
                }
                """);
            Assert.IsTrue(File.Exists(seventhOutboundPath));
            StringAssert.Contains(File.ReadAllText(seventhOutboundPath), "\"messageOutboxL2ToL1Count\": 7");
            StringAssert.Contains(File.ReadAllText(seventhOutboundPath), "\"knownForcedInclusionNonceCount\": 7");
            StringAssert.Contains(File.ReadAllText(seventhOutboundPath), "\"consumedDepositCount\": 7");
            var seventhOutboundRpcPath = Path.Combine(chainDir, "soft-seal-after-sixth-recover-seventh-outbound-rpc.json");
            File.WriteAllText(seventhOutboundRpcPath, $$"""
                {
                  "withdrawalLeaf": "{{seventhWdLeaf}}",
                  "outboundMessageHash": "{{seventhOutbound.MessageHash}}",
                  "withdrawalProofBytes": {{seventhWdProofBytes.Length}},
                  "messageProofBytes": {{seventhMsgProofBytes.Length}},
                  "messageOutboxL2ToL1Count": 7,
                  "knownForcedInclusionNonceCount": 7,
                  "knownInboundNonceCount": 7,
                  "consumedDepositCount": 7,
                  "isSettlementRetrying": true
                }
                """);
            Assert.IsTrue(File.Exists(seventhOutboundRpcPath));
            StringAssert.Contains(File.ReadAllText(seventhOutboundRpcPath), "\"outboundMessageHash\": \"" + seventhOutbound.MessageHash + "\"");

            // Seventh poison→recover after septuple soft multi-batch path retains soft state.
            LocalHostOperatorStatus afterSeventhPoison = afterSeventhOutbound;
            for (var attempt = 0; attempt < 16; attempt++)
            {
                try
                {
                    host.ReconcileAsync().GetAwaiter().GetResult();
                }
                catch (OverflowException)
                {
                }
                catch (Exception)
                {
                }

                host.SubmitNextAsync().GetAwaiter().GetResult();
                afterSeventhPoison = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
                if (afterSeventhPoison.IsSettlementPoisoned)
                    break;
            }

            Assert.IsTrue(afterSeventhPoison.IsSettlementPoisoned);
            Assert.IsFalse(afterSeventhPoison.IsSettlementRetrying);
            Assert.IsTrue(afterSeventhPoison.PendingSettlementCount >= 2);
            Assert.AreEqual(7, afterSeventhPoison.ConsumedDepositCount);
            Assert.AreEqual(7, afterSeventhPoison.MessageOutboxL2ToL1Count);
            Assert.AreEqual(7, afterSeventhPoison.KnownForcedInclusionNonceCount);
            Assert.AreEqual(7, afterSeventhPoison.KnownInboundNonceCount);
            Assert.IsNotNull(afterSeventhPoison.Recovery.BlockedBatchNumber);
            Assert.IsNotNull(afterSeventhPoison.Recovery.ArtifactContentHash);
            var seventhBlocked = afterSeventhPoison.Recovery.BlockedBatchNumber!.Value;
            var seventhHash = afterSeventhPoison.Recovery.ArtifactContentHash!;
            Assert.ThrowsExactly<InvalidOperationException>(
                () => host.RecoverPoisonedBatchAsync(seventhBlocked, UInt256.Zero)
                    .GetAwaiter().GetResult());
            host.RecoverPoisonedBatchAsync(seventhBlocked, seventhHash).GetAwaiter().GetResult();
            var afterSeventhRecover = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsFalse(afterSeventhRecover.IsSettlementPoisoned);
            Assert.IsTrue(afterSeventhRecover.IsSettlementRetrying);
            Assert.AreEqual(SettlementRecoveryState.Retrying, afterSeventhRecover.Recovery.State);
            Assert.AreEqual(0, afterSeventhRecover.Recovery.RetryCount);
            Assert.IsTrue(host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult() >= 2);
            Assert.AreEqual(2UL, afterSeventhRecover.LatestCheckpointBatchNumber);
            Assert.AreEqual(7, afterSeventhRecover.ConsumedDepositCount);
            Assert.AreEqual(7, afterSeventhRecover.MessageOutboxL2ToL1Count);
            Assert.AreEqual(7, afterSeventhRecover.KnownForcedInclusionNonceCount);
            Assert.AreEqual(7, afterSeventhRecover.KnownInboundNonceCount);
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(1));
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(2));
            Assert.IsTrue(host.GetRpcWithdrawalProof(seventhWdLeaf) is { Length: > 0 });
            Assert.IsTrue(host.GetRpcMessageProof(seventhOutbound.MessageHash) is { Length: > 0 });
            Assert.IsTrue(afterSeventhRecover.IsOfflinePassportComplete);
            Assert.IsTrue(afterSeventhRecover.IsOperatorReady);
            var seventhPoisonPath = Path.Combine(chainDir, "soft-seal-seventh-poison-recover.json");
            File.WriteAllText(seventhPoisonPath, $$"""
                {
                  "seventhPoisonBlockedBatch": {{seventhBlocked}},
                  "pendingSettlementCount": {{afterSeventhRecover.PendingSettlementCount}},
                  "latestCheckpointBatchNumber": {{afterSeventhRecover.LatestCheckpointBatchNumber}},
                  "consumedDepositCount": {{afterSeventhRecover.ConsumedDepositCount}},
                  "messageOutboxL2ToL1Count": {{afterSeventhRecover.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterSeventhRecover.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterSeventhRecover.KnownInboundNonceCount}},
                  "rpcBatch1Status": "{{host.GetRpcBatchStatus(1)}}",
                  "rpcBatch2Status": "{{host.GetRpcBatchStatus(2)}}",
                  "seventhWithdrawalProofPresent": true,
                  "seventhMessageProofPresent": true,
                  "isSettlementRetrying": true,
                  "isSettlementPoisoned": false
                }
                """);
            Assert.IsTrue(File.Exists(seventhPoisonPath));
            StringAssert.Contains(File.ReadAllText(seventhPoisonPath), "\"rpcBatch2Status\": \"Finalized\"");
            StringAssert.Contains(File.ReadAllText(seventhPoisonPath), "\"consumedDepositCount\": 7");
            StringAssert.Contains(File.ReadAllText(seventhPoisonPath), "\"messageOutboxL2ToL1Count\": 7");
            StringAssert.Contains(File.ReadAllText(seventhPoisonPath), "\"isSettlementRetrying\": true");

            // After seventh recover: re-publish local DA + eighth offline deposit while Retrying.
            Assert.IsTrue(host.SupportsLocalDaReader);
            var seventhRecoverDa1Payload = new byte[] { 0xDA, 0xA1, 0x01 };
            var seventhRecoverDa1 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 1,
                Payload = seventhRecoverDa1Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(DAMode.Local, seventhRecoverDa1.Layer);
            Assert.IsTrue(host.IsDaAvailableAsync(seventhRecoverDa1).AsTask().GetAwaiter().GetResult());
            var seventhRecoverDa1Read = host.CreateLocalDaReader().ReadAsync(seventhRecoverDa1).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(seventhRecoverDa1Read is { Length: 3 });
            CollectionAssert.AreEqual(seventhRecoverDa1Payload, seventhRecoverDa1Read!.Value.ToArray());
            var seventhRecoverDa2Payload = new byte[] { 0xDA, 0xA2, 0x02 };
            var seventhRecoverDa2 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 2,
                Payload = seventhRecoverDa2Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(host.IsDaAvailableAsync(seventhRecoverDa2).AsTask().GetAwaiter().GetResult());
            var seventhRecoverDa2Read = host.CreateLocalDaReader().ReadAsync(seventhRecoverDa2).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(seventhRecoverDa2Read is { Length: 3 });
            CollectionAssert.AreEqual(seventhRecoverDa2Payload, seventhRecoverDa2Read!.Value.ToArray());
            var seventhRecoverL1Asset = UInt160.Parse("0x" + new string('1', 40));
            var seventhRecoverL2Asset = UInt160.Parse("0x" + new string('2', 40));
            var deposit8 = new CrossChainMessage
            {
                SourceChainId = 0,
                TargetChainId = 20260716u,
                Nonce = 8,
                Sender = Account(0x66),
                Receiver = Account(0x55),
                MessageType = MessageType.Deposit,
                Payload = new DepositPayload
                {
                    L1Asset = seventhRecoverL1Asset,
                    L2Recipient = Account(0x55),
                    Amount = new BigInteger(8_000),
                }.Encode(),
                MessageHash = UInt256.Zero,
            };
            var mint8 = host.ProcessDeposit(deposit8);
            Assert.AreEqual(seventhRecoverL2Asset, mint8.L2Asset);
            Assert.IsTrue(host.HasConsumedDeposit(0, 1));
            Assert.IsTrue(host.HasConsumedDeposit(0, 2));
            Assert.IsTrue(host.HasConsumedDeposit(0, 3));
            Assert.IsTrue(host.HasConsumedDeposit(0, 4));
            Assert.IsTrue(host.HasConsumedDeposit(0, 5));
            Assert.IsTrue(host.HasConsumedDeposit(0, 6));
            Assert.IsTrue(host.HasConsumedDeposit(0, 7));
            Assert.IsTrue(host.HasConsumedDeposit(0, 8));
            Assert.AreEqual(8, host.ConsumedDepositCount);
            host.RecordRpcDeposit(new DepositStatus(0, 8, ConsumedOnL2: true, IncludedInBatch: 2));
            Assert.IsTrue(host.GetRpcL1DepositStatus(0, 8) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
            Assert.AreEqual(0, host.ScanSharedBridgeDepositsAsync().AsTask().GetAwaiter().GetResult());
            var afterEighthDeposit = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(8, afterEighthDeposit.ConsumedDepositCount);
            Assert.AreEqual(7, afterEighthDeposit.MessageOutboxL2ToL1Count);
            Assert.AreEqual(7, afterEighthDeposit.KnownForcedInclusionNonceCount);
            Assert.AreEqual(7, afterEighthDeposit.KnownInboundNonceCount);
            Assert.IsTrue(afterEighthDeposit.IsSettlementRetrying);
            Assert.IsTrue(afterEighthDeposit.PendingSettlementCount >= 2);
            Assert.AreEqual(2UL, afterEighthDeposit.LatestCheckpointBatchNumber);
            Assert.IsTrue(afterEighthDeposit.IsOfflinePassportComplete);
            var afterSeventhRecoverDaPath = Path.Combine(chainDir, "soft-seal-after-seventh-recover-da-deposit.json");
            File.WriteAllText(afterSeventhRecoverDaPath, $$"""
                {
                  "daBatch1Layer": "{{seventhRecoverDa1.Layer}}",
                  "daBatch2Layer": "{{seventhRecoverDa2.Layer}}",
                  "consumedDepositCount": {{afterEighthDeposit.ConsumedDepositCount}},
                  "deposit8IncludedInBatch": 2,
                  "messageOutboxL2ToL1Count": {{afterEighthDeposit.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterEighthDeposit.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterEighthDeposit.KnownInboundNonceCount}},
                  "latestCheckpointBatchNumber": {{afterEighthDeposit.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterEighthDeposit.PendingSettlementCount}},
                  "isSettlementRetrying": true,
                  "isOfflinePassportComplete": true
                }
                """);
            Assert.IsTrue(File.Exists(afterSeventhRecoverDaPath));
            StringAssert.Contains(File.ReadAllText(afterSeventhRecoverDaPath), "\"consumedDepositCount\": 8");
            StringAssert.Contains(File.ReadAllText(afterSeventhRecoverDaPath), "\"deposit8IncludedInBatch\": 2");
            StringAssert.Contains(File.ReadAllText(afterSeventhRecoverDaPath), "\"daBatch2Layer\": \"Local\"");
            var afterSeventhRecoverProm = host.ExportPrometheusMetrics();
            Assert.IsFalse(string.IsNullOrWhiteSpace(afterSeventhRecoverProm));
            var afterSeventhRecoverPromPath = Path.Combine(chainDir, "soft-seal-after-seventh-recover-host.prom");
            host.WritePrometheusMetricsAsync(afterSeventhRecoverPromPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(afterSeventhRecoverPromPath));
            Assert.AreEqual(afterSeventhRecoverProm, File.ReadAllText(afterSeventhRecoverPromPath));

            // Eighth offline withdrawal/outbox + FI/inbound after seventh recover (no L1).
            var eighthSender = Account(0xcc);
            var eighthL2Asset = UInt160.Parse("0x" + new string('2', 40));
            var eighthWdLeaf = host.StageWithdrawal(new WithdrawalRequest
            {
                ChainId = 20260716u,
                EmittingContract = eighthSender,
                L2Sender = eighthSender,
                L1Recipient = eighthSender,
                L2Asset = eighthL2Asset,
                Amount = new BigInteger(100),
                Nonce = 8,
            });
            Assert.AreNotEqual(UInt256.Zero, eighthWdLeaf);
            var eighthSealedWd = host.SealWithdrawalBatch();
            Assert.AreNotEqual(UInt256.Zero, eighthSealedWd.Root);
            Assert.AreEqual(0, host.StagedWithdrawalCount);
            Assert.IsTrue(eighthSealedWd.Tree.Count >= 1);
            var eighthMerkle = eighthSealedWd.Tree.GetProof(eighthSealedWd.Tree.Count - 1);
            Assert.AreEqual(eighthWdLeaf, eighthMerkle.Leaf);
            var eighthWdProofBytes = MerkleProofSerializer.Encode(eighthMerkle);
            Assert.IsTrue(eighthWdProofBytes.Length >= MerkleProofSerializer.HeaderSize);
            host.RecordRpcWithdrawalProof(eighthWdLeaf, eighthWdProofBytes);
            CollectionAssert.AreEqual(
                eighthWdProofBytes,
                host.GetRpcWithdrawalProof(eighthWdLeaf)!.Value.ToArray());
            var eighthOutboundDraft = new CrossChainMessage
            {
                SourceChainId = 20260716u,
                TargetChainId = 0,
                Nonce = 16,
                Sender = eighthSender,
                Receiver = eighthSender,
                MessageType = MessageType.Event,
                Payload = new byte[] { 0x08 },
                MessageHash = UInt256.Zero,
            };
            var eighthOutbound = eighthOutboundDraft with
            {
                MessageHash = MessageHasher.HashMessage(eighthOutboundDraft),
            };
            host.EnqueueOutboundMessagesAsync([eighthOutbound]).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(8, host.MessageOutbox!.L2ToL1Count);
            var eighthMsgProofBytes = eighthOutbound.MessageHash.GetSpan().ToArray();
            host.RecordRpcMessageProof(eighthOutbound.MessageHash, eighthMsgProofBytes);
            CollectionAssert.AreEqual(
                eighthMsgProofBytes,
                host.GetRpcMessageProof(eighthOutbound.MessageHash)!.Value.ToArray());
            host.RecordMessageRouterFinalizedProof(eighthOutbound.MessageHash, eighthMsgProofBytes);
            CollectionAssert.AreEqual(
                eighthMsgProofBytes,
                host.GetMessageRouterProofAsync(eighthOutbound.MessageHash).AsTask().GetAwaiter().GetResult()!.Value.ToArray());
            Assert.IsTrue(host.RegisterForcedInclusionNonce(18));
            Assert.IsFalse(host.RegisterForcedInclusionNonce(18));
            Assert.AreEqual(8, host.KnownForcedInclusionNonceCount);
            Assert.IsTrue(host.RegisterInboundMessageNonce(18));
            Assert.IsFalse(host.RegisterInboundMessageNonce(18));
            Assert.AreEqual(8, host.KnownInboundNonceCount);
            Assert.AreEqual(0, host.OpenBatchForcedInclusionCount);
            Assert.AreEqual(0, host.OpenBatchL1MessageCount);
            var afterEighthOutbound = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(8, afterEighthOutbound.ConsumedDepositCount);
            Assert.AreEqual(8, afterEighthOutbound.MessageOutboxL2ToL1Count);
            Assert.AreEqual(8, afterEighthOutbound.KnownForcedInclusionNonceCount);
            Assert.AreEqual(8, afterEighthOutbound.KnownInboundNonceCount);
            Assert.IsTrue(afterEighthOutbound.IsSettlementRetrying);
            Assert.IsTrue(afterEighthOutbound.PendingSettlementCount >= 2);
            Assert.AreEqual(2UL, afterEighthOutbound.LatestCheckpointBatchNumber);
            var eighthOutboundPath = Path.Combine(chainDir, "soft-seal-after-seventh-recover-eighth-outbound.json");
            File.WriteAllText(eighthOutboundPath, $$"""
                {
                  "consumedDepositCount": {{afterEighthOutbound.ConsumedDepositCount}},
                  "messageOutboxL2ToL1Count": {{afterEighthOutbound.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterEighthOutbound.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterEighthOutbound.KnownInboundNonceCount}},
                  "withdrawalLeaf": "{{eighthWdLeaf}}",
                  "outboundMessageHash": "{{eighthOutbound.MessageHash}}",
                  "withdrawalProofBytes": {{eighthWdProofBytes.Length}},
                  "messageProofBytes": {{eighthMsgProofBytes.Length}},
                  "latestCheckpointBatchNumber": {{afterEighthOutbound.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterEighthOutbound.PendingSettlementCount}},
                  "isSettlementRetrying": true
                }
                """);
            Assert.IsTrue(File.Exists(eighthOutboundPath));
            StringAssert.Contains(File.ReadAllText(eighthOutboundPath), "\"messageOutboxL2ToL1Count\": 8");
            StringAssert.Contains(File.ReadAllText(eighthOutboundPath), "\"knownForcedInclusionNonceCount\": 8");
            StringAssert.Contains(File.ReadAllText(eighthOutboundPath), "\"consumedDepositCount\": 8");
            var eighthOutboundRpcPath = Path.Combine(chainDir, "soft-seal-after-seventh-recover-eighth-outbound-rpc.json");
            File.WriteAllText(eighthOutboundRpcPath, $$"""
                {
                  "withdrawalLeaf": "{{eighthWdLeaf}}",
                  "outboundMessageHash": "{{eighthOutbound.MessageHash}}",
                  "withdrawalProofBytes": {{eighthWdProofBytes.Length}},
                  "messageProofBytes": {{eighthMsgProofBytes.Length}},
                  "messageOutboxL2ToL1Count": 8,
                  "knownForcedInclusionNonceCount": 8,
                  "knownInboundNonceCount": 8,
                  "consumedDepositCount": 8,
                  "isSettlementRetrying": true
                }
                """);
            Assert.IsTrue(File.Exists(eighthOutboundRpcPath));
            StringAssert.Contains(File.ReadAllText(eighthOutboundRpcPath), "\"outboundMessageHash\": \"" + eighthOutbound.MessageHash + "\"");

            // Eighth poison→recover after octuple soft multi-batch path retains soft state.
            LocalHostOperatorStatus afterEighthPoison = afterEighthOutbound;
            for (var attempt = 0; attempt < 16; attempt++)
            {
                try
                {
                    host.ReconcileAsync().GetAwaiter().GetResult();
                }
                catch (OverflowException)
                {
                }
                catch (Exception)
                {
                }

                host.SubmitNextAsync().GetAwaiter().GetResult();
                afterEighthPoison = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
                if (afterEighthPoison.IsSettlementPoisoned)
                    break;
            }

            Assert.IsTrue(afterEighthPoison.IsSettlementPoisoned);
            Assert.IsFalse(afterEighthPoison.IsSettlementRetrying);
            Assert.IsTrue(afterEighthPoison.PendingSettlementCount >= 2);
            Assert.AreEqual(8, afterEighthPoison.ConsumedDepositCount);
            Assert.AreEqual(8, afterEighthPoison.MessageOutboxL2ToL1Count);
            Assert.AreEqual(8, afterEighthPoison.KnownForcedInclusionNonceCount);
            Assert.AreEqual(8, afterEighthPoison.KnownInboundNonceCount);
            Assert.IsNotNull(afterEighthPoison.Recovery.BlockedBatchNumber);
            Assert.IsNotNull(afterEighthPoison.Recovery.ArtifactContentHash);
            var eighthBlocked = afterEighthPoison.Recovery.BlockedBatchNumber!.Value;
            var eighthHash = afterEighthPoison.Recovery.ArtifactContentHash!;
            Assert.ThrowsExactly<InvalidOperationException>(
                () => host.RecoverPoisonedBatchAsync(eighthBlocked, UInt256.Zero)
                    .GetAwaiter().GetResult());
            host.RecoverPoisonedBatchAsync(eighthBlocked, eighthHash).GetAwaiter().GetResult();
            var afterEighthRecover = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsFalse(afterEighthRecover.IsSettlementPoisoned);
            Assert.IsTrue(afterEighthRecover.IsSettlementRetrying);
            Assert.AreEqual(SettlementRecoveryState.Retrying, afterEighthRecover.Recovery.State);
            Assert.AreEqual(0, afterEighthRecover.Recovery.RetryCount);
            Assert.IsTrue(host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult() >= 2);
            Assert.AreEqual(2UL, afterEighthRecover.LatestCheckpointBatchNumber);
            Assert.AreEqual(8, afterEighthRecover.ConsumedDepositCount);
            Assert.AreEqual(8, afterEighthRecover.MessageOutboxL2ToL1Count);
            Assert.AreEqual(8, afterEighthRecover.KnownForcedInclusionNonceCount);
            Assert.AreEqual(8, afterEighthRecover.KnownInboundNonceCount);
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(1));
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(2));
            Assert.IsTrue(host.GetRpcWithdrawalProof(eighthWdLeaf) is { Length: > 0 });
            Assert.IsTrue(host.GetRpcMessageProof(eighthOutbound.MessageHash) is { Length: > 0 });
            Assert.IsTrue(afterEighthRecover.IsOfflinePassportComplete);
            Assert.IsTrue(afterEighthRecover.IsOperatorReady);
            var eighthPoisonPath = Path.Combine(chainDir, "soft-seal-eighth-poison-recover.json");
            File.WriteAllText(eighthPoisonPath, $$"""
                {
                  "eighthPoisonBlockedBatch": {{eighthBlocked}},
                  "pendingSettlementCount": {{afterEighthRecover.PendingSettlementCount}},
                  "latestCheckpointBatchNumber": {{afterEighthRecover.LatestCheckpointBatchNumber}},
                  "consumedDepositCount": {{afterEighthRecover.ConsumedDepositCount}},
                  "messageOutboxL2ToL1Count": {{afterEighthRecover.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterEighthRecover.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterEighthRecover.KnownInboundNonceCount}},
                  "rpcBatch1Status": "{{host.GetRpcBatchStatus(1)}}",
                  "rpcBatch2Status": "{{host.GetRpcBatchStatus(2)}}",
                  "eighthWithdrawalProofPresent": true,
                  "eighthMessageProofPresent": true,
                  "isSettlementRetrying": true,
                  "isSettlementPoisoned": false
                }
                """);
            Assert.IsTrue(File.Exists(eighthPoisonPath));
            StringAssert.Contains(File.ReadAllText(eighthPoisonPath), "\"rpcBatch2Status\": \"Finalized\"");
            StringAssert.Contains(File.ReadAllText(eighthPoisonPath), "\"consumedDepositCount\": 8");
            StringAssert.Contains(File.ReadAllText(eighthPoisonPath), "\"messageOutboxL2ToL1Count\": 8");
            StringAssert.Contains(File.ReadAllText(eighthPoisonPath), "\"isSettlementRetrying\": true");

            
            // After eighth recover: re-publish local DA + ninth offline deposit while Retrying.
            Assert.IsTrue(host.SupportsLocalDaReader);
            var eighthRecoverDa1Payload = new byte[] { 0xDA, 0xA1, 0x01 };
            var eighthRecoverDa1 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 1,
                Payload = eighthRecoverDa1Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(DAMode.Local, eighthRecoverDa1.Layer);
            Assert.IsTrue(host.IsDaAvailableAsync(eighthRecoverDa1).AsTask().GetAwaiter().GetResult());
            var eighthRecoverDa1Read = host.CreateLocalDaReader().ReadAsync(eighthRecoverDa1).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(eighthRecoverDa1Read is { Length: 3 });
            CollectionAssert.AreEqual(eighthRecoverDa1Payload, eighthRecoverDa1Read!.Value.ToArray());
            var eighthRecoverDa2Payload = new byte[] { 0xDA, 0xA2, 0x02 };
            var eighthRecoverDa2 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 2,
                Payload = eighthRecoverDa2Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(host.IsDaAvailableAsync(eighthRecoverDa2).AsTask().GetAwaiter().GetResult());
            var eighthRecoverDa2Read = host.CreateLocalDaReader().ReadAsync(eighthRecoverDa2).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(eighthRecoverDa2Read is { Length: 3 });
            CollectionAssert.AreEqual(eighthRecoverDa2Payload, eighthRecoverDa2Read!.Value.ToArray());
            var eighthRecoverL1Asset = UInt160.Parse("0x" + new string('1', 40));
            var eighthRecoverL2Asset = UInt160.Parse("0x" + new string('2', 40));
            var deposit9 = new CrossChainMessage
            {
                SourceChainId = 0,
                TargetChainId = 20260716u,
                Nonce = 9,
                Sender = Account(0x66),
                Receiver = Account(0x55),
                MessageType = MessageType.Deposit,
                Payload = new DepositPayload
                {
                    L1Asset = eighthRecoverL1Asset,
                    L2Recipient = Account(0x55),
                    Amount = new BigInteger(9_000),
                }.Encode(),
                MessageHash = UInt256.Zero,
            };
            var mint9 = host.ProcessDeposit(deposit9);
            Assert.AreEqual(eighthRecoverL2Asset, mint9.L2Asset);
            Assert.IsTrue(host.HasConsumedDeposit(0, 1));
            Assert.IsTrue(host.HasConsumedDeposit(0, 2));
            Assert.IsTrue(host.HasConsumedDeposit(0, 3));
            Assert.IsTrue(host.HasConsumedDeposit(0, 4));
            Assert.IsTrue(host.HasConsumedDeposit(0, 5));
            Assert.IsTrue(host.HasConsumedDeposit(0, 6));
            Assert.IsTrue(host.HasConsumedDeposit(0, 7));
            Assert.IsTrue(host.HasConsumedDeposit(0, 8));
            Assert.IsTrue(host.HasConsumedDeposit(0, 9));
            Assert.AreEqual(9, host.ConsumedDepositCount);
            host.RecordRpcDeposit(new DepositStatus(0, 9, ConsumedOnL2: true, IncludedInBatch: 2));
            Assert.IsTrue(host.GetRpcL1DepositStatus(0, 9) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
            Assert.AreEqual(0, host.ScanSharedBridgeDepositsAsync().AsTask().GetAwaiter().GetResult());
            var afterNinthDeposit = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(9, afterNinthDeposit.ConsumedDepositCount);
            Assert.AreEqual(8, afterNinthDeposit.MessageOutboxL2ToL1Count);
            Assert.AreEqual(8, afterNinthDeposit.KnownForcedInclusionNonceCount);
            Assert.AreEqual(8, afterNinthDeposit.KnownInboundNonceCount);
            Assert.IsTrue(afterNinthDeposit.IsSettlementRetrying);
            Assert.IsTrue(afterNinthDeposit.PendingSettlementCount >= 2);
            Assert.AreEqual(2UL, afterNinthDeposit.LatestCheckpointBatchNumber);
            Assert.IsTrue(afterNinthDeposit.IsOfflinePassportComplete);
            var afterEighthRecoverDaPath = Path.Combine(chainDir, "soft-seal-after-eighth-recover-da-deposit.json");
            File.WriteAllText(afterEighthRecoverDaPath, $$"""
                {
                  "daBatch1Layer": "{{eighthRecoverDa1.Layer}}",
                  "daBatch2Layer": "{{eighthRecoverDa2.Layer}}",
                  "consumedDepositCount": {{afterNinthDeposit.ConsumedDepositCount}},
                  "deposit9IncludedInBatch": 2,
                  "messageOutboxL2ToL1Count": {{afterNinthDeposit.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterNinthDeposit.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterNinthDeposit.KnownInboundNonceCount}},
                  "latestCheckpointBatchNumber": {{afterNinthDeposit.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterNinthDeposit.PendingSettlementCount}},
                  "isSettlementRetrying": true,
                  "isOfflinePassportComplete": true
                }
                """);
            Assert.IsTrue(File.Exists(afterEighthRecoverDaPath));
            StringAssert.Contains(File.ReadAllText(afterEighthRecoverDaPath), "\"consumedDepositCount\": 9");
            StringAssert.Contains(File.ReadAllText(afterEighthRecoverDaPath), "\"deposit9IncludedInBatch\": 2");
            StringAssert.Contains(File.ReadAllText(afterEighthRecoverDaPath), "\"daBatch2Layer\": \"Local\"");
            var afterEighthRecoverProm = host.ExportPrometheusMetrics();
            Assert.IsFalse(string.IsNullOrWhiteSpace(afterEighthRecoverProm));
            var afterEighthRecoverPromPath = Path.Combine(chainDir, "soft-seal-after-eighth-recover-host.prom");
            host.WritePrometheusMetricsAsync(afterEighthRecoverPromPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(afterEighthRecoverPromPath));
            Assert.AreEqual(afterEighthRecoverProm, File.ReadAllText(afterEighthRecoverPromPath));

            // Ninth offline withdrawal/outbox + FI/inbound after eighth recover (no L1).
            var ninthSender = Account(0xcc);
            var ninthL2Asset = UInt160.Parse("0x" + new string('2', 40));
            var ninthWdLeaf = host.StageWithdrawal(new WithdrawalRequest
            {
                ChainId = 20260716u,
                EmittingContract = ninthSender,
                L2Sender = ninthSender,
                L1Recipient = ninthSender,
                L2Asset = ninthL2Asset,
                Amount = new BigInteger(100),
                Nonce = 9,
            });
            Assert.AreNotEqual(UInt256.Zero, ninthWdLeaf);
            var ninthSealedWd = host.SealWithdrawalBatch();
            Assert.AreNotEqual(UInt256.Zero, ninthSealedWd.Root);
            Assert.AreEqual(0, host.StagedWithdrawalCount);
            Assert.IsTrue(ninthSealedWd.Tree.Count >= 1);
            var ninthMerkle = ninthSealedWd.Tree.GetProof(ninthSealedWd.Tree.Count - 1);
            Assert.AreEqual(ninthWdLeaf, ninthMerkle.Leaf);
            var ninthWdProofBytes = MerkleProofSerializer.Encode(ninthMerkle);
            Assert.IsTrue(ninthWdProofBytes.Length >= MerkleProofSerializer.HeaderSize);
            host.RecordRpcWithdrawalProof(ninthWdLeaf, ninthWdProofBytes);
            CollectionAssert.AreEqual(
                ninthWdProofBytes,
                host.GetRpcWithdrawalProof(ninthWdLeaf)!.Value.ToArray());
            var ninthOutboundDraft = new CrossChainMessage
            {
                SourceChainId = 20260716u,
                TargetChainId = 0,
                Nonce = 18,
                Sender = ninthSender,
                Receiver = ninthSender,
                MessageType = MessageType.Event,
                Payload = new byte[] { 0x09 },
                MessageHash = UInt256.Zero,
            };
            var ninthOutbound = ninthOutboundDraft with
            {
                MessageHash = MessageHasher.HashMessage(ninthOutboundDraft),
            };
            host.EnqueueOutboundMessagesAsync([ninthOutbound]).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(9, host.MessageOutbox!.L2ToL1Count);
            var ninthMsgProofBytes = ninthOutbound.MessageHash.GetSpan().ToArray();
            host.RecordRpcMessageProof(ninthOutbound.MessageHash, ninthMsgProofBytes);
            CollectionAssert.AreEqual(
                ninthMsgProofBytes,
                host.GetRpcMessageProof(ninthOutbound.MessageHash)!.Value.ToArray());
            host.RecordMessageRouterFinalizedProof(ninthOutbound.MessageHash, ninthMsgProofBytes);
            CollectionAssert.AreEqual(
                ninthMsgProofBytes,
                host.GetMessageRouterProofAsync(ninthOutbound.MessageHash).AsTask().GetAwaiter().GetResult()!.Value.ToArray());
            Assert.IsTrue(host.RegisterForcedInclusionNonce(20));
            Assert.IsFalse(host.RegisterForcedInclusionNonce(20));
            Assert.AreEqual(9, host.KnownForcedInclusionNonceCount);
            Assert.IsTrue(host.RegisterInboundMessageNonce(20));
            Assert.IsFalse(host.RegisterInboundMessageNonce(20));
            Assert.AreEqual(9, host.KnownInboundNonceCount);
            Assert.AreEqual(0, host.OpenBatchForcedInclusionCount);
            Assert.AreEqual(0, host.OpenBatchL1MessageCount);
            var afterNinthOutbound = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(9, afterNinthOutbound.ConsumedDepositCount);
            Assert.AreEqual(9, afterNinthOutbound.MessageOutboxL2ToL1Count);
            Assert.AreEqual(9, afterNinthOutbound.KnownForcedInclusionNonceCount);
            Assert.AreEqual(9, afterNinthOutbound.KnownInboundNonceCount);
            Assert.IsTrue(afterNinthOutbound.IsSettlementRetrying);
            Assert.IsTrue(afterNinthOutbound.PendingSettlementCount >= 2);
            Assert.AreEqual(2UL, afterNinthOutbound.LatestCheckpointBatchNumber);
            var ninthOutboundPath = Path.Combine(chainDir, "soft-seal-after-eighth-recover-ninth-outbound.json");
            File.WriteAllText(ninthOutboundPath, $$"""
                {
                  "consumedDepositCount": {{afterNinthOutbound.ConsumedDepositCount}},
                  "messageOutboxL2ToL1Count": {{afterNinthOutbound.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterNinthOutbound.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterNinthOutbound.KnownInboundNonceCount}},
                  "withdrawalLeaf": "{{ninthWdLeaf}}",
                  "outboundMessageHash": "{{ninthOutbound.MessageHash}}",
                  "withdrawalProofBytes": {{ninthWdProofBytes.Length}},
                  "messageProofBytes": {{ninthMsgProofBytes.Length}},
                  "latestCheckpointBatchNumber": {{afterNinthOutbound.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterNinthOutbound.PendingSettlementCount}},
                  "isSettlementRetrying": true
                }
                """);
            Assert.IsTrue(File.Exists(ninthOutboundPath));
            StringAssert.Contains(File.ReadAllText(ninthOutboundPath), "\"messageOutboxL2ToL1Count\": 9");
            StringAssert.Contains(File.ReadAllText(ninthOutboundPath), "\"knownForcedInclusionNonceCount\": 9");
            StringAssert.Contains(File.ReadAllText(ninthOutboundPath), "\"consumedDepositCount\": 9");
            var ninthOutboundRpcPath = Path.Combine(chainDir, "soft-seal-after-eighth-recover-ninth-outbound-rpc.json");
            File.WriteAllText(ninthOutboundRpcPath, $$"""
                {
                  "withdrawalLeaf": "{{ninthWdLeaf}}",
                  "outboundMessageHash": "{{ninthOutbound.MessageHash}}",
                  "withdrawalProofBytes": {{ninthWdProofBytes.Length}},
                  "messageProofBytes": {{ninthMsgProofBytes.Length}},
                  "messageOutboxL2ToL1Count": 9,
                  "knownForcedInclusionNonceCount": 9,
                  "knownInboundNonceCount": 9,
                  "consumedDepositCount": 9,
                  "isSettlementRetrying": true
                }
                """);
            Assert.IsTrue(File.Exists(ninthOutboundRpcPath));
            StringAssert.Contains(File.ReadAllText(ninthOutboundRpcPath), "\"outboundMessageHash\": \"" + ninthOutbound.MessageHash + "\"");

            // Ninth poison→recover after nonuple soft multi-batch path retains soft state.
            LocalHostOperatorStatus afterNinthPoison = afterNinthOutbound;
            for (var attempt = 0; attempt < 16; attempt++)
            {
                try
                {
                    host.ReconcileAsync().GetAwaiter().GetResult();
                }
                catch (OverflowException)
                {
                }
                catch (Exception)
                {
                }

                host.SubmitNextAsync().GetAwaiter().GetResult();
                afterNinthPoison = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
                if (afterNinthPoison.IsSettlementPoisoned)
                    break;
            }

            Assert.IsTrue(afterNinthPoison.IsSettlementPoisoned);
            Assert.IsFalse(afterNinthPoison.IsSettlementRetrying);
            Assert.IsTrue(afterNinthPoison.PendingSettlementCount >= 2);
            Assert.AreEqual(9, afterNinthPoison.ConsumedDepositCount);
            Assert.AreEqual(9, afterNinthPoison.MessageOutboxL2ToL1Count);
            Assert.AreEqual(9, afterNinthPoison.KnownForcedInclusionNonceCount);
            Assert.AreEqual(9, afterNinthPoison.KnownInboundNonceCount);
            Assert.IsNotNull(afterNinthPoison.Recovery.BlockedBatchNumber);
            Assert.IsNotNull(afterNinthPoison.Recovery.ArtifactContentHash);
            var ninthBlocked = afterNinthPoison.Recovery.BlockedBatchNumber!.Value;
            var ninthHash = afterNinthPoison.Recovery.ArtifactContentHash!;
            Assert.ThrowsExactly<InvalidOperationException>(
                () => host.RecoverPoisonedBatchAsync(ninthBlocked, UInt256.Zero)
                    .GetAwaiter().GetResult());
            host.RecoverPoisonedBatchAsync(ninthBlocked, ninthHash).GetAwaiter().GetResult();
            var afterNinthRecover = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsFalse(afterNinthRecover.IsSettlementPoisoned);
            Assert.IsTrue(afterNinthRecover.IsSettlementRetrying);
            Assert.AreEqual(SettlementRecoveryState.Retrying, afterNinthRecover.Recovery.State);
            Assert.AreEqual(0, afterNinthRecover.Recovery.RetryCount);
            Assert.IsTrue(host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult() >= 2);
            Assert.AreEqual(2UL, afterNinthRecover.LatestCheckpointBatchNumber);
            Assert.AreEqual(9, afterNinthRecover.ConsumedDepositCount);
            Assert.AreEqual(9, afterNinthRecover.MessageOutboxL2ToL1Count);
            Assert.AreEqual(9, afterNinthRecover.KnownForcedInclusionNonceCount);
            Assert.AreEqual(9, afterNinthRecover.KnownInboundNonceCount);
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(1));
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(2));
            Assert.IsTrue(host.GetRpcWithdrawalProof(ninthWdLeaf) is { Length: > 0 });
            Assert.IsTrue(host.GetRpcMessageProof(ninthOutbound.MessageHash) is { Length: > 0 });
            Assert.IsTrue(afterNinthRecover.IsOfflinePassportComplete);
            Assert.IsTrue(afterNinthRecover.IsOperatorReady);
            var ninthPoisonPath = Path.Combine(chainDir, "soft-seal-ninth-poison-recover.json");
            File.WriteAllText(ninthPoisonPath, $$"""
                {
                  "ninthPoisonBlockedBatch": {{ninthBlocked}},
                  "pendingSettlementCount": {{afterNinthRecover.PendingSettlementCount}},
                  "latestCheckpointBatchNumber": {{afterNinthRecover.LatestCheckpointBatchNumber}},
                  "consumedDepositCount": {{afterNinthRecover.ConsumedDepositCount}},
                  "messageOutboxL2ToL1Count": {{afterNinthRecover.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterNinthRecover.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterNinthRecover.KnownInboundNonceCount}},
                  "rpcBatch1Status": "{{host.GetRpcBatchStatus(1)}}",
                  "rpcBatch2Status": "{{host.GetRpcBatchStatus(2)}}",
                  "ninthWithdrawalProofPresent": true,
                  "ninthMessageProofPresent": true,
                  "isSettlementRetrying": true,
                  "isSettlementPoisoned": false
                }
                """);
            Assert.IsTrue(File.Exists(ninthPoisonPath));
            StringAssert.Contains(File.ReadAllText(ninthPoisonPath), "\"rpcBatch2Status\": \"Finalized\"");
            StringAssert.Contains(File.ReadAllText(ninthPoisonPath), "\"consumedDepositCount\": 9");
            StringAssert.Contains(File.ReadAllText(ninthPoisonPath), "\"messageOutboxL2ToL1Count\": 9");
            StringAssert.Contains(File.ReadAllText(ninthPoisonPath), "\"isSettlementRetrying\": true");

            
            // After ninth recover: re-publish local DA + tenth offline deposit while Retrying.
            Assert.IsTrue(host.SupportsLocalDaReader);
            var ninthRecoverDa1Payload = new byte[] { 0xDA, 0xA1, 0x01 };
            var ninthRecoverDa1 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 1,
                Payload = ninthRecoverDa1Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(DAMode.Local, ninthRecoverDa1.Layer);
            Assert.IsTrue(host.IsDaAvailableAsync(ninthRecoverDa1).AsTask().GetAwaiter().GetResult());
            var ninthRecoverDa1Read = host.CreateLocalDaReader().ReadAsync(ninthRecoverDa1).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(ninthRecoverDa1Read is { Length: 3 });
            CollectionAssert.AreEqual(ninthRecoverDa1Payload, ninthRecoverDa1Read!.Value.ToArray());
            var ninthRecoverDa2Payload = new byte[] { 0xDA, 0xA2, 0x02 };
            var ninthRecoverDa2 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 2,
                Payload = ninthRecoverDa2Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(host.IsDaAvailableAsync(ninthRecoverDa2).AsTask().GetAwaiter().GetResult());
            var ninthRecoverDa2Read = host.CreateLocalDaReader().ReadAsync(ninthRecoverDa2).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(ninthRecoverDa2Read is { Length: 3 });
            CollectionAssert.AreEqual(ninthRecoverDa2Payload, ninthRecoverDa2Read!.Value.ToArray());
            var ninthRecoverL1Asset = UInt160.Parse("0x" + new string('1', 40));
            var ninthRecoverL2Asset = UInt160.Parse("0x" + new string('2', 40));
            var deposit10 = new CrossChainMessage
            {
                SourceChainId = 0,
                TargetChainId = 20260716u,
                Nonce = 10,
                Sender = Account(0x66),
                Receiver = Account(0x55),
                MessageType = MessageType.Deposit,
                Payload = new DepositPayload
                {
                    L1Asset = ninthRecoverL1Asset,
                    L2Recipient = Account(0x55),
                    Amount = new BigInteger(10_000),
                }.Encode(),
                MessageHash = UInt256.Zero,
            };
            var mint10 = host.ProcessDeposit(deposit10);
            Assert.AreEqual(ninthRecoverL2Asset, mint10.L2Asset);
            Assert.IsTrue(host.HasConsumedDeposit(0, 1));
            Assert.IsTrue(host.HasConsumedDeposit(0, 2));
            Assert.IsTrue(host.HasConsumedDeposit(0, 3));
            Assert.IsTrue(host.HasConsumedDeposit(0, 4));
            Assert.IsTrue(host.HasConsumedDeposit(0, 5));
            Assert.IsTrue(host.HasConsumedDeposit(0, 6));
            Assert.IsTrue(host.HasConsumedDeposit(0, 7));
            Assert.IsTrue(host.HasConsumedDeposit(0, 8));
            Assert.IsTrue(host.HasConsumedDeposit(0, 9));
            Assert.IsTrue(host.HasConsumedDeposit(0, 10));
            Assert.AreEqual(10, host.ConsumedDepositCount);
            host.RecordRpcDeposit(new DepositStatus(0, 10, ConsumedOnL2: true, IncludedInBatch: 2));
            Assert.IsTrue(host.GetRpcL1DepositStatus(0, 10) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
            Assert.AreEqual(0, host.ScanSharedBridgeDepositsAsync().AsTask().GetAwaiter().GetResult());
            var afterTenthDeposit = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(10, afterTenthDeposit.ConsumedDepositCount);
            Assert.AreEqual(9, afterTenthDeposit.MessageOutboxL2ToL1Count);
            Assert.AreEqual(9, afterTenthDeposit.KnownForcedInclusionNonceCount);
            Assert.AreEqual(9, afterTenthDeposit.KnownInboundNonceCount);
            Assert.IsTrue(afterTenthDeposit.IsSettlementRetrying);
            Assert.IsTrue(afterTenthDeposit.PendingSettlementCount >= 2);
            Assert.AreEqual(2UL, afterTenthDeposit.LatestCheckpointBatchNumber);
            Assert.IsTrue(afterTenthDeposit.IsOfflinePassportComplete);
            var afterNinthRecoverDaPath = Path.Combine(chainDir, "soft-seal-after-ninth-recover-da-deposit.json");
            File.WriteAllText(afterNinthRecoverDaPath, $$"""
                {
                  "daBatch1Layer": "{{ninthRecoverDa1.Layer}}",
                  "daBatch2Layer": "{{ninthRecoverDa2.Layer}}",
                  "consumedDepositCount": {{afterTenthDeposit.ConsumedDepositCount}},
                  "deposit10IncludedInBatch": 2,
                  "messageOutboxL2ToL1Count": {{afterTenthDeposit.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterTenthDeposit.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterTenthDeposit.KnownInboundNonceCount}},
                  "latestCheckpointBatchNumber": {{afterTenthDeposit.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterTenthDeposit.PendingSettlementCount}},
                  "isSettlementRetrying": true,
                  "isOfflinePassportComplete": true
                }
                """);
            Assert.IsTrue(File.Exists(afterNinthRecoverDaPath));
            StringAssert.Contains(File.ReadAllText(afterNinthRecoverDaPath), "\"consumedDepositCount\": 10");
            StringAssert.Contains(File.ReadAllText(afterNinthRecoverDaPath), "\"deposit10IncludedInBatch\": 2");
            StringAssert.Contains(File.ReadAllText(afterNinthRecoverDaPath), "\"daBatch2Layer\": \"Local\"");
            var afterNinthRecoverProm = host.ExportPrometheusMetrics();
            Assert.IsFalse(string.IsNullOrWhiteSpace(afterNinthRecoverProm));
            var afterNinthRecoverPromPath = Path.Combine(chainDir, "soft-seal-after-ninth-recover-host.prom");
            host.WritePrometheusMetricsAsync(afterNinthRecoverPromPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(afterNinthRecoverPromPath));
            Assert.AreEqual(afterNinthRecoverProm, File.ReadAllText(afterNinthRecoverPromPath));

            // Tenth offline withdrawal/outbox + FI/inbound after ninth recover (no L1).
            var tenthSender = Account(0xcc);
            var tenthL2Asset = UInt160.Parse("0x" + new string('2', 40));
            var tenthWdLeaf = host.StageWithdrawal(new WithdrawalRequest
            {
                ChainId = 20260716u,
                EmittingContract = tenthSender,
                L2Sender = tenthSender,
                L1Recipient = tenthSender,
                L2Asset = tenthL2Asset,
                Amount = new BigInteger(100),
                Nonce = 10,
            });
            Assert.AreNotEqual(UInt256.Zero, tenthWdLeaf);
            var tenthSealedWd = host.SealWithdrawalBatch();
            Assert.AreNotEqual(UInt256.Zero, tenthSealedWd.Root);
            Assert.AreEqual(0, host.StagedWithdrawalCount);
            Assert.IsTrue(tenthSealedWd.Tree.Count >= 1);
            var tenthMerkle = tenthSealedWd.Tree.GetProof(tenthSealedWd.Tree.Count - 1);
            Assert.AreEqual(tenthWdLeaf, tenthMerkle.Leaf);
            var tenthWdProofBytes = MerkleProofSerializer.Encode(tenthMerkle);
            Assert.IsTrue(tenthWdProofBytes.Length >= MerkleProofSerializer.HeaderSize);
            host.RecordRpcWithdrawalProof(tenthWdLeaf, tenthWdProofBytes);
            CollectionAssert.AreEqual(
                tenthWdProofBytes,
                host.GetRpcWithdrawalProof(tenthWdLeaf)!.Value.ToArray());
            var tenthOutboundDraft = new CrossChainMessage
            {
                SourceChainId = 20260716u,
                TargetChainId = 0,
                Nonce = 20,
                Sender = tenthSender,
                Receiver = tenthSender,
                MessageType = MessageType.Event,
                Payload = new byte[] { 0x0A },
                MessageHash = UInt256.Zero,
            };
            var tenthOutbound = tenthOutboundDraft with
            {
                MessageHash = MessageHasher.HashMessage(tenthOutboundDraft),
            };
            host.EnqueueOutboundMessagesAsync([tenthOutbound]).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(10, host.MessageOutbox!.L2ToL1Count);
            var tenthMsgProofBytes = tenthOutbound.MessageHash.GetSpan().ToArray();
            host.RecordRpcMessageProof(tenthOutbound.MessageHash, tenthMsgProofBytes);
            CollectionAssert.AreEqual(
                tenthMsgProofBytes,
                host.GetRpcMessageProof(tenthOutbound.MessageHash)!.Value.ToArray());
            host.RecordMessageRouterFinalizedProof(tenthOutbound.MessageHash, tenthMsgProofBytes);
            CollectionAssert.AreEqual(
                tenthMsgProofBytes,
                host.GetMessageRouterProofAsync(tenthOutbound.MessageHash).AsTask().GetAwaiter().GetResult()!.Value.ToArray());
            Assert.IsTrue(host.RegisterForcedInclusionNonce(22));
            Assert.IsFalse(host.RegisterForcedInclusionNonce(22));
            Assert.AreEqual(10, host.KnownForcedInclusionNonceCount);
            Assert.IsTrue(host.RegisterInboundMessageNonce(22));
            Assert.IsFalse(host.RegisterInboundMessageNonce(22));
            Assert.AreEqual(10, host.KnownInboundNonceCount);
            Assert.AreEqual(0, host.OpenBatchForcedInclusionCount);
            Assert.AreEqual(0, host.OpenBatchL1MessageCount);
            var afterTenthOutbound = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(10, afterTenthOutbound.ConsumedDepositCount);
            Assert.AreEqual(10, afterTenthOutbound.MessageOutboxL2ToL1Count);
            Assert.AreEqual(10, afterTenthOutbound.KnownForcedInclusionNonceCount);
            Assert.AreEqual(10, afterTenthOutbound.KnownInboundNonceCount);
            Assert.IsTrue(afterTenthOutbound.IsSettlementRetrying);
            Assert.IsTrue(afterTenthOutbound.PendingSettlementCount >= 2);
            Assert.AreEqual(2UL, afterTenthOutbound.LatestCheckpointBatchNumber);
            var tenthOutboundPath = Path.Combine(chainDir, "soft-seal-after-ninth-recover-tenth-outbound.json");
            File.WriteAllText(tenthOutboundPath, $$"""
                {
                  "consumedDepositCount": {{afterTenthOutbound.ConsumedDepositCount}},
                  "messageOutboxL2ToL1Count": {{afterTenthOutbound.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterTenthOutbound.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterTenthOutbound.KnownInboundNonceCount}},
                  "withdrawalLeaf": "{{tenthWdLeaf}}",
                  "outboundMessageHash": "{{tenthOutbound.MessageHash}}",
                  "withdrawalProofBytes": {{tenthWdProofBytes.Length}},
                  "messageProofBytes": {{tenthMsgProofBytes.Length}},
                  "latestCheckpointBatchNumber": {{afterTenthOutbound.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterTenthOutbound.PendingSettlementCount}},
                  "isSettlementRetrying": true
                }
                """);
            Assert.IsTrue(File.Exists(tenthOutboundPath));
            StringAssert.Contains(File.ReadAllText(tenthOutboundPath), "\"messageOutboxL2ToL1Count\": 10");
            StringAssert.Contains(File.ReadAllText(tenthOutboundPath), "\"knownForcedInclusionNonceCount\": 10");
            StringAssert.Contains(File.ReadAllText(tenthOutboundPath), "\"consumedDepositCount\": 10");
            var tenthOutboundRpcPath = Path.Combine(chainDir, "soft-seal-after-ninth-recover-tenth-outbound-rpc.json");
            File.WriteAllText(tenthOutboundRpcPath, $$"""
                {
                  "withdrawalLeaf": "{{tenthWdLeaf}}",
                  "outboundMessageHash": "{{tenthOutbound.MessageHash}}",
                  "withdrawalProofBytes": {{tenthWdProofBytes.Length}},
                  "messageProofBytes": {{tenthMsgProofBytes.Length}},
                  "messageOutboxL2ToL1Count": 10,
                  "knownForcedInclusionNonceCount": 10,
                  "knownInboundNonceCount": 10,
                  "consumedDepositCount": 10,
                  "isSettlementRetrying": true
                }
                """);
            Assert.IsTrue(File.Exists(tenthOutboundRpcPath));
            StringAssert.Contains(File.ReadAllText(tenthOutboundRpcPath), "\"outboundMessageHash\": \"" + tenthOutbound.MessageHash + "\"");

            // Tenth poison→recover after decuple soft multi-batch path retains soft state.
            LocalHostOperatorStatus afterTenthPoison = afterTenthOutbound;
            for (var attempt = 0; attempt < 16; attempt++)
            {
                try
                {
                    host.ReconcileAsync().GetAwaiter().GetResult();
                }
                catch (OverflowException)
                {
                }
                catch (Exception)
                {
                }

                host.SubmitNextAsync().GetAwaiter().GetResult();
                afterTenthPoison = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
                if (afterTenthPoison.IsSettlementPoisoned)
                    break;
            }

            Assert.IsTrue(afterTenthPoison.IsSettlementPoisoned);
            Assert.IsFalse(afterTenthPoison.IsSettlementRetrying);
            Assert.IsTrue(afterTenthPoison.PendingSettlementCount >= 2);
            Assert.AreEqual(10, afterTenthPoison.ConsumedDepositCount);
            Assert.AreEqual(10, afterTenthPoison.MessageOutboxL2ToL1Count);
            Assert.AreEqual(10, afterTenthPoison.KnownForcedInclusionNonceCount);
            Assert.AreEqual(10, afterTenthPoison.KnownInboundNonceCount);
            Assert.IsNotNull(afterTenthPoison.Recovery.BlockedBatchNumber);
            Assert.IsNotNull(afterTenthPoison.Recovery.ArtifactContentHash);
            var tenthBlocked = afterTenthPoison.Recovery.BlockedBatchNumber!.Value;
            var tenthHash = afterTenthPoison.Recovery.ArtifactContentHash!;
            Assert.ThrowsExactly<InvalidOperationException>(
                () => host.RecoverPoisonedBatchAsync(tenthBlocked, UInt256.Zero)
                    .GetAwaiter().GetResult());
            host.RecoverPoisonedBatchAsync(tenthBlocked, tenthHash).GetAwaiter().GetResult();
            var afterTenthRecover = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsFalse(afterTenthRecover.IsSettlementPoisoned);
            Assert.IsTrue(afterTenthRecover.IsSettlementRetrying);
            Assert.AreEqual(SettlementRecoveryState.Retrying, afterTenthRecover.Recovery.State);
            Assert.AreEqual(0, afterTenthRecover.Recovery.RetryCount);
            Assert.IsTrue(host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult() >= 2);
            Assert.AreEqual(2UL, afterTenthRecover.LatestCheckpointBatchNumber);
            Assert.AreEqual(10, afterTenthRecover.ConsumedDepositCount);
            Assert.AreEqual(10, afterTenthRecover.MessageOutboxL2ToL1Count);
            Assert.AreEqual(10, afterTenthRecover.KnownForcedInclusionNonceCount);
            Assert.AreEqual(10, afterTenthRecover.KnownInboundNonceCount);
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(1));
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(2));
            Assert.IsTrue(host.GetRpcWithdrawalProof(tenthWdLeaf) is { Length: > 0 });
            Assert.IsTrue(host.GetRpcMessageProof(tenthOutbound.MessageHash) is { Length: > 0 });
            Assert.IsTrue(afterTenthRecover.IsOfflinePassportComplete);
            Assert.IsTrue(afterTenthRecover.IsOperatorReady);
            var tenthPoisonPath = Path.Combine(chainDir, "soft-seal-tenth-poison-recover.json");
            File.WriteAllText(tenthPoisonPath, $$"""
                {
                  "tenthPoisonBlockedBatch": {{tenthBlocked}},
                  "pendingSettlementCount": {{afterTenthRecover.PendingSettlementCount}},
                  "latestCheckpointBatchNumber": {{afterTenthRecover.LatestCheckpointBatchNumber}},
                  "consumedDepositCount": {{afterTenthRecover.ConsumedDepositCount}},
                  "messageOutboxL2ToL1Count": {{afterTenthRecover.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterTenthRecover.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterTenthRecover.KnownInboundNonceCount}},
                  "rpcBatch1Status": "{{host.GetRpcBatchStatus(1)}}",
                  "rpcBatch2Status": "{{host.GetRpcBatchStatus(2)}}",
                  "tenthWithdrawalProofPresent": true,
                  "tenthMessageProofPresent": true,
                  "isSettlementRetrying": true,
                  "isSettlementPoisoned": false
                }
                """);
            Assert.IsTrue(File.Exists(tenthPoisonPath));
            StringAssert.Contains(File.ReadAllText(tenthPoisonPath), "\"rpcBatch2Status\": \"Finalized\"");
            StringAssert.Contains(File.ReadAllText(tenthPoisonPath), "\"consumedDepositCount\": 10");
            StringAssert.Contains(File.ReadAllText(tenthPoisonPath), "\"messageOutboxL2ToL1Count\": 10");
            StringAssert.Contains(File.ReadAllText(tenthPoisonPath), "\"isSettlementRetrying\": true");

            
            // After tenth recover: re-publish local DA + eleventh offline deposit while Retrying.
            Assert.IsTrue(host.SupportsLocalDaReader);
            var tenthRecoverDa1Payload = new byte[] { 0xDA, 0xA1, 0x01 };
            var tenthRecoverDa1 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 1,
                Payload = tenthRecoverDa1Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(DAMode.Local, tenthRecoverDa1.Layer);
            Assert.IsTrue(host.IsDaAvailableAsync(tenthRecoverDa1).AsTask().GetAwaiter().GetResult());
            var tenthRecoverDa1Read = host.CreateLocalDaReader().ReadAsync(tenthRecoverDa1).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(tenthRecoverDa1Read is { Length: 3 });
            CollectionAssert.AreEqual(tenthRecoverDa1Payload, tenthRecoverDa1Read!.Value.ToArray());
            var tenthRecoverDa2Payload = new byte[] { 0xDA, 0xA2, 0x02 };
            var tenthRecoverDa2 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 2,
                Payload = tenthRecoverDa2Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(host.IsDaAvailableAsync(tenthRecoverDa2).AsTask().GetAwaiter().GetResult());
            var tenthRecoverDa2Read = host.CreateLocalDaReader().ReadAsync(tenthRecoverDa2).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(tenthRecoverDa2Read is { Length: 3 });
            CollectionAssert.AreEqual(tenthRecoverDa2Payload, tenthRecoverDa2Read!.Value.ToArray());
            var tenthRecoverL1Asset = UInt160.Parse("0x" + new string('1', 40));
            var tenthRecoverL2Asset = UInt160.Parse("0x" + new string('2', 40));
            var deposit11 = new CrossChainMessage
            {
                SourceChainId = 0,
                TargetChainId = 20260716u,
                Nonce = 11,
                Sender = Account(0x66),
                Receiver = Account(0x55),
                MessageType = MessageType.Deposit,
                Payload = new DepositPayload
                {
                    L1Asset = tenthRecoverL1Asset,
                    L2Recipient = Account(0x55),
                    Amount = new BigInteger(11_000),
                }.Encode(),
                MessageHash = UInt256.Zero,
            };
            var mint11 = host.ProcessDeposit(deposit11);
            Assert.AreEqual(tenthRecoverL2Asset, mint11.L2Asset);
            Assert.IsTrue(host.HasConsumedDeposit(0, 1));
            Assert.IsTrue(host.HasConsumedDeposit(0, 2));
            Assert.IsTrue(host.HasConsumedDeposit(0, 3));
            Assert.IsTrue(host.HasConsumedDeposit(0, 4));
            Assert.IsTrue(host.HasConsumedDeposit(0, 5));
            Assert.IsTrue(host.HasConsumedDeposit(0, 6));
            Assert.IsTrue(host.HasConsumedDeposit(0, 7));
            Assert.IsTrue(host.HasConsumedDeposit(0, 8));
            Assert.IsTrue(host.HasConsumedDeposit(0, 9));
            Assert.IsTrue(host.HasConsumedDeposit(0, 10));
            Assert.IsTrue(host.HasConsumedDeposit(0, 11));
            Assert.AreEqual(11, host.ConsumedDepositCount);
            host.RecordRpcDeposit(new DepositStatus(0, 11, ConsumedOnL2: true, IncludedInBatch: 2));
            Assert.IsTrue(host.GetRpcL1DepositStatus(0, 11) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
            Assert.AreEqual(0, host.ScanSharedBridgeDepositsAsync().AsTask().GetAwaiter().GetResult());
            var afterEleventhDeposit = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(11, afterEleventhDeposit.ConsumedDepositCount);
            Assert.AreEqual(10, afterEleventhDeposit.MessageOutboxL2ToL1Count);
            Assert.AreEqual(10, afterEleventhDeposit.KnownForcedInclusionNonceCount);
            Assert.AreEqual(10, afterEleventhDeposit.KnownInboundNonceCount);
            Assert.IsTrue(afterEleventhDeposit.IsSettlementRetrying);
            Assert.IsTrue(afterEleventhDeposit.PendingSettlementCount >= 2);
            Assert.AreEqual(2UL, afterEleventhDeposit.LatestCheckpointBatchNumber);
            Assert.IsTrue(afterEleventhDeposit.IsOfflinePassportComplete);
            var afterTenthRecoverDaPath = Path.Combine(chainDir, "soft-seal-after-tenth-recover-da-deposit.json");
            File.WriteAllText(afterTenthRecoverDaPath, $$"""
                {
                  "daBatch1Layer": "{{tenthRecoverDa1.Layer}}",
                  "daBatch2Layer": "{{tenthRecoverDa2.Layer}}",
                  "consumedDepositCount": {{afterEleventhDeposit.ConsumedDepositCount}},
                  "deposit11IncludedInBatch": 2,
                  "messageOutboxL2ToL1Count": {{afterEleventhDeposit.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterEleventhDeposit.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterEleventhDeposit.KnownInboundNonceCount}},
                  "latestCheckpointBatchNumber": {{afterEleventhDeposit.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterEleventhDeposit.PendingSettlementCount}},
                  "isSettlementRetrying": true,
                  "isOfflinePassportComplete": true
                }
                """);
            Assert.IsTrue(File.Exists(afterTenthRecoverDaPath));
            StringAssert.Contains(File.ReadAllText(afterTenthRecoverDaPath), "\"consumedDepositCount\": 11");
            StringAssert.Contains(File.ReadAllText(afterTenthRecoverDaPath), "\"deposit11IncludedInBatch\": 2");
            StringAssert.Contains(File.ReadAllText(afterTenthRecoverDaPath), "\"daBatch2Layer\": \"Local\"");
            var afterTenthRecoverProm = host.ExportPrometheusMetrics();
            Assert.IsFalse(string.IsNullOrWhiteSpace(afterTenthRecoverProm));
            var afterTenthRecoverPromPath = Path.Combine(chainDir, "soft-seal-after-tenth-recover-host.prom");
            host.WritePrometheusMetricsAsync(afterTenthRecoverPromPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(afterTenthRecoverPromPath));
            Assert.AreEqual(afterTenthRecoverProm, File.ReadAllText(afterTenthRecoverPromPath));

            // Eleventh offline withdrawal/outbox + FI/inbound after tenth recover (no L1).
            var eleventhSender = Account(0xcc);
            var eleventhL2Asset = UInt160.Parse("0x" + new string('2', 40));
            var eleventhWdLeaf = host.StageWithdrawal(new WithdrawalRequest
            {
                ChainId = 20260716u,
                EmittingContract = eleventhSender,
                L2Sender = eleventhSender,
                L1Recipient = eleventhSender,
                L2Asset = eleventhL2Asset,
                Amount = new BigInteger(100),
                Nonce = 11,
            });
            Assert.AreNotEqual(UInt256.Zero, eleventhWdLeaf);
            var eleventhSealedWd = host.SealWithdrawalBatch();
            Assert.AreNotEqual(UInt256.Zero, eleventhSealedWd.Root);
            Assert.AreEqual(0, host.StagedWithdrawalCount);
            Assert.IsTrue(eleventhSealedWd.Tree.Count >= 1);
            var eleventhMerkle = eleventhSealedWd.Tree.GetProof(eleventhSealedWd.Tree.Count - 1);
            Assert.AreEqual(eleventhWdLeaf, eleventhMerkle.Leaf);
            var eleventhWdProofBytes = MerkleProofSerializer.Encode(eleventhMerkle);
            Assert.IsTrue(eleventhWdProofBytes.Length >= MerkleProofSerializer.HeaderSize);
            host.RecordRpcWithdrawalProof(eleventhWdLeaf, eleventhWdProofBytes);
            CollectionAssert.AreEqual(
                eleventhWdProofBytes,
                host.GetRpcWithdrawalProof(eleventhWdLeaf)!.Value.ToArray());
            var eleventhOutboundDraft = new CrossChainMessage
            {
                SourceChainId = 20260716u,
                TargetChainId = 0,
                Nonce = 22,
                Sender = eleventhSender,
                Receiver = eleventhSender,
                MessageType = MessageType.Event,
                Payload = new byte[] { 0x0B },
                MessageHash = UInt256.Zero,
            };
            var eleventhOutbound = eleventhOutboundDraft with
            {
                MessageHash = MessageHasher.HashMessage(eleventhOutboundDraft),
            };
            host.EnqueueOutboundMessagesAsync([eleventhOutbound]).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(11, host.MessageOutbox!.L2ToL1Count);
            var eleventhMsgProofBytes = eleventhOutbound.MessageHash.GetSpan().ToArray();
            host.RecordRpcMessageProof(eleventhOutbound.MessageHash, eleventhMsgProofBytes);
            CollectionAssert.AreEqual(
                eleventhMsgProofBytes,
                host.GetRpcMessageProof(eleventhOutbound.MessageHash)!.Value.ToArray());
            host.RecordMessageRouterFinalizedProof(eleventhOutbound.MessageHash, eleventhMsgProofBytes);
            CollectionAssert.AreEqual(
                eleventhMsgProofBytes,
                host.GetMessageRouterProofAsync(eleventhOutbound.MessageHash).AsTask().GetAwaiter().GetResult()!.Value.ToArray());
            Assert.IsTrue(host.RegisterForcedInclusionNonce(24));
            Assert.IsFalse(host.RegisterForcedInclusionNonce(24));
            Assert.AreEqual(11, host.KnownForcedInclusionNonceCount);
            Assert.IsTrue(host.RegisterInboundMessageNonce(24));
            Assert.IsFalse(host.RegisterInboundMessageNonce(24));
            Assert.AreEqual(11, host.KnownInboundNonceCount);
            Assert.AreEqual(0, host.OpenBatchForcedInclusionCount);
            Assert.AreEqual(0, host.OpenBatchL1MessageCount);
            var afterEleventhOutbound = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(11, afterEleventhOutbound.ConsumedDepositCount);
            Assert.AreEqual(11, afterEleventhOutbound.MessageOutboxL2ToL1Count);
            Assert.AreEqual(11, afterEleventhOutbound.KnownForcedInclusionNonceCount);
            Assert.AreEqual(11, afterEleventhOutbound.KnownInboundNonceCount);
            Assert.IsTrue(afterEleventhOutbound.IsSettlementRetrying);
            Assert.IsTrue(afterEleventhOutbound.PendingSettlementCount >= 2);
            Assert.AreEqual(2UL, afterEleventhOutbound.LatestCheckpointBatchNumber);
            var eleventhOutboundPath = Path.Combine(chainDir, "soft-seal-after-tenth-recover-eleventh-outbound.json");
            File.WriteAllText(eleventhOutboundPath, $$"""
                {
                  "consumedDepositCount": {{afterEleventhOutbound.ConsumedDepositCount}},
                  "messageOutboxL2ToL1Count": {{afterEleventhOutbound.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterEleventhOutbound.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterEleventhOutbound.KnownInboundNonceCount}},
                  "withdrawalLeaf": "{{eleventhWdLeaf}}",
                  "outboundMessageHash": "{{eleventhOutbound.MessageHash}}",
                  "withdrawalProofBytes": {{eleventhWdProofBytes.Length}},
                  "messageProofBytes": {{eleventhMsgProofBytes.Length}},
                  "latestCheckpointBatchNumber": {{afterEleventhOutbound.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterEleventhOutbound.PendingSettlementCount}},
                  "isSettlementRetrying": true
                }
                """);
            Assert.IsTrue(File.Exists(eleventhOutboundPath));
            StringAssert.Contains(File.ReadAllText(eleventhOutboundPath), "\"messageOutboxL2ToL1Count\": 11");
            StringAssert.Contains(File.ReadAllText(eleventhOutboundPath), "\"knownForcedInclusionNonceCount\": 11");
            StringAssert.Contains(File.ReadAllText(eleventhOutboundPath), "\"consumedDepositCount\": 11");
            var eleventhOutboundRpcPath = Path.Combine(chainDir, "soft-seal-after-tenth-recover-eleventh-outbound-rpc.json");
            File.WriteAllText(eleventhOutboundRpcPath, $$"""
                {
                  "withdrawalLeaf": "{{eleventhWdLeaf}}",
                  "outboundMessageHash": "{{eleventhOutbound.MessageHash}}",
                  "withdrawalProofBytes": {{eleventhWdProofBytes.Length}},
                  "messageProofBytes": {{eleventhMsgProofBytes.Length}},
                  "messageOutboxL2ToL1Count": 11,
                  "knownForcedInclusionNonceCount": 11,
                  "knownInboundNonceCount": 11,
                  "consumedDepositCount": 11,
                  "isSettlementRetrying": true
                }
                """);
            Assert.IsTrue(File.Exists(eleventhOutboundRpcPath));
            StringAssert.Contains(File.ReadAllText(eleventhOutboundRpcPath), "\"outboundMessageHash\": \"" + eleventhOutbound.MessageHash + "\"");

            // Eleventh poison→recover after undecuple soft multi-batch path retains soft state.
            LocalHostOperatorStatus afterEleventhPoison = afterEleventhOutbound;
            for (var attempt = 0; attempt < 16; attempt++)
            {
                try
                {
                    host.ReconcileAsync().GetAwaiter().GetResult();
                }
                catch (OverflowException)
                {
                }
                catch (Exception)
                {
                }

                host.SubmitNextAsync().GetAwaiter().GetResult();
                afterEleventhPoison = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
                if (afterEleventhPoison.IsSettlementPoisoned)
                    break;
            }

            Assert.IsTrue(afterEleventhPoison.IsSettlementPoisoned);
            Assert.IsFalse(afterEleventhPoison.IsSettlementRetrying);
            Assert.IsTrue(afterEleventhPoison.PendingSettlementCount >= 2);
            Assert.AreEqual(11, afterEleventhPoison.ConsumedDepositCount);
            Assert.AreEqual(11, afterEleventhPoison.MessageOutboxL2ToL1Count);
            Assert.AreEqual(11, afterEleventhPoison.KnownForcedInclusionNonceCount);
            Assert.AreEqual(11, afterEleventhPoison.KnownInboundNonceCount);
            Assert.IsNotNull(afterEleventhPoison.Recovery.BlockedBatchNumber);
            Assert.IsNotNull(afterEleventhPoison.Recovery.ArtifactContentHash);
            var eleventhBlocked = afterEleventhPoison.Recovery.BlockedBatchNumber!.Value;
            var eleventhHash = afterEleventhPoison.Recovery.ArtifactContentHash!;
            Assert.ThrowsExactly<InvalidOperationException>(
                () => host.RecoverPoisonedBatchAsync(eleventhBlocked, UInt256.Zero)
                    .GetAwaiter().GetResult());
            host.RecoverPoisonedBatchAsync(eleventhBlocked, eleventhHash).GetAwaiter().GetResult();
            var afterEleventhRecover = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsFalse(afterEleventhRecover.IsSettlementPoisoned);
            Assert.IsTrue(afterEleventhRecover.IsSettlementRetrying);
            Assert.AreEqual(SettlementRecoveryState.Retrying, afterEleventhRecover.Recovery.State);
            Assert.AreEqual(0, afterEleventhRecover.Recovery.RetryCount);
            Assert.IsTrue(host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult() >= 2);
            Assert.AreEqual(2UL, afterEleventhRecover.LatestCheckpointBatchNumber);
            Assert.AreEqual(11, afterEleventhRecover.ConsumedDepositCount);
            Assert.AreEqual(11, afterEleventhRecover.MessageOutboxL2ToL1Count);
            Assert.AreEqual(11, afterEleventhRecover.KnownForcedInclusionNonceCount);
            Assert.AreEqual(11, afterEleventhRecover.KnownInboundNonceCount);
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(1));
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(2));
            Assert.IsTrue(host.GetRpcWithdrawalProof(eleventhWdLeaf) is { Length: > 0 });
            Assert.IsTrue(host.GetRpcMessageProof(eleventhOutbound.MessageHash) is { Length: > 0 });
            Assert.IsTrue(afterEleventhRecover.IsOfflinePassportComplete);
            Assert.IsTrue(afterEleventhRecover.IsOperatorReady);
            var eleventhPoisonPath = Path.Combine(chainDir, "soft-seal-eleventh-poison-recover.json");
            File.WriteAllText(eleventhPoisonPath, $$"""
                {
                  "eleventhPoisonBlockedBatch": {{eleventhBlocked}},
                  "pendingSettlementCount": {{afterEleventhRecover.PendingSettlementCount}},
                  "latestCheckpointBatchNumber": {{afterEleventhRecover.LatestCheckpointBatchNumber}},
                  "consumedDepositCount": {{afterEleventhRecover.ConsumedDepositCount}},
                  "messageOutboxL2ToL1Count": {{afterEleventhRecover.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterEleventhRecover.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterEleventhRecover.KnownInboundNonceCount}},
                  "rpcBatch1Status": "{{host.GetRpcBatchStatus(1)}}",
                  "rpcBatch2Status": "{{host.GetRpcBatchStatus(2)}}",
                  "eleventhWithdrawalProofPresent": true,
                  "eleventhMessageProofPresent": true,
                  "isSettlementRetrying": true,
                  "isSettlementPoisoned": false
                }
                """);
            Assert.IsTrue(File.Exists(eleventhPoisonPath));
            StringAssert.Contains(File.ReadAllText(eleventhPoisonPath), "\"rpcBatch2Status\": \"Finalized\"");
            StringAssert.Contains(File.ReadAllText(eleventhPoisonPath), "\"consumedDepositCount\": 11");
            StringAssert.Contains(File.ReadAllText(eleventhPoisonPath), "\"messageOutboxL2ToL1Count\": 11");
            StringAssert.Contains(File.ReadAllText(eleventhPoisonPath), "\"isSettlementRetrying\": true");

            
            // After eleventh recover: re-publish local DA + twelfth offline deposit while Retrying.
            Assert.IsTrue(host.SupportsLocalDaReader);
            var eleventhRecoverDa1Payload = new byte[] { 0xDA, 0xA1, 0x01 };
            var eleventhRecoverDa1 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 1,
                Payload = eleventhRecoverDa1Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(DAMode.Local, eleventhRecoverDa1.Layer);
            Assert.IsTrue(host.IsDaAvailableAsync(eleventhRecoverDa1).AsTask().GetAwaiter().GetResult());
            var eleventhRecoverDa1Read = host.CreateLocalDaReader().ReadAsync(eleventhRecoverDa1).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(eleventhRecoverDa1Read is { Length: 3 });
            CollectionAssert.AreEqual(eleventhRecoverDa1Payload, eleventhRecoverDa1Read!.Value.ToArray());
            var eleventhRecoverDa2Payload = new byte[] { 0xDA, 0xA2, 0x02 };
            var eleventhRecoverDa2 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 2,
                Payload = eleventhRecoverDa2Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(host.IsDaAvailableAsync(eleventhRecoverDa2).AsTask().GetAwaiter().GetResult());
            var eleventhRecoverDa2Read = host.CreateLocalDaReader().ReadAsync(eleventhRecoverDa2).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(eleventhRecoverDa2Read is { Length: 3 });
            CollectionAssert.AreEqual(eleventhRecoverDa2Payload, eleventhRecoverDa2Read!.Value.ToArray());
            var eleventhRecoverL1Asset = UInt160.Parse("0x" + new string('1', 40));
            var eleventhRecoverL2Asset = UInt160.Parse("0x" + new string('2', 40));
            var deposit12 = new CrossChainMessage
            {
                SourceChainId = 0,
                TargetChainId = 20260716u,
                Nonce = 12,
                Sender = Account(0x66),
                Receiver = Account(0x55),
                MessageType = MessageType.Deposit,
                Payload = new DepositPayload
                {
                    L1Asset = eleventhRecoverL1Asset,
                    L2Recipient = Account(0x55),
                    Amount = new BigInteger(12_000),
                }.Encode(),
                MessageHash = UInt256.Zero,
            };
            var mint12 = host.ProcessDeposit(deposit12);
            Assert.AreEqual(eleventhRecoverL2Asset, mint12.L2Asset);
            Assert.IsTrue(host.HasConsumedDeposit(0, 1));
            Assert.IsTrue(host.HasConsumedDeposit(0, 2));
            Assert.IsTrue(host.HasConsumedDeposit(0, 3));
            Assert.IsTrue(host.HasConsumedDeposit(0, 4));
            Assert.IsTrue(host.HasConsumedDeposit(0, 5));
            Assert.IsTrue(host.HasConsumedDeposit(0, 6));
            Assert.IsTrue(host.HasConsumedDeposit(0, 7));
            Assert.IsTrue(host.HasConsumedDeposit(0, 8));
            Assert.IsTrue(host.HasConsumedDeposit(0, 9));
            Assert.IsTrue(host.HasConsumedDeposit(0, 10));
            Assert.IsTrue(host.HasConsumedDeposit(0, 11));
            Assert.IsTrue(host.HasConsumedDeposit(0, 12));
            Assert.AreEqual(12, host.ConsumedDepositCount);
            host.RecordRpcDeposit(new DepositStatus(0, 12, ConsumedOnL2: true, IncludedInBatch: 2));
            Assert.IsTrue(host.GetRpcL1DepositStatus(0, 12) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
            Assert.AreEqual(0, host.ScanSharedBridgeDepositsAsync().AsTask().GetAwaiter().GetResult());
            var afterTwelfthDeposit = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(12, afterTwelfthDeposit.ConsumedDepositCount);
            Assert.AreEqual(11, afterTwelfthDeposit.MessageOutboxL2ToL1Count);
            Assert.AreEqual(11, afterTwelfthDeposit.KnownForcedInclusionNonceCount);
            Assert.AreEqual(11, afterTwelfthDeposit.KnownInboundNonceCount);
            Assert.IsTrue(afterTwelfthDeposit.IsSettlementRetrying);
            Assert.IsTrue(afterTwelfthDeposit.PendingSettlementCount >= 2);
            Assert.AreEqual(2UL, afterTwelfthDeposit.LatestCheckpointBatchNumber);
            Assert.IsTrue(afterTwelfthDeposit.IsOfflinePassportComplete);
            var afterEleventhRecoverDaPath = Path.Combine(chainDir, "soft-seal-after-eleventh-recover-da-deposit.json");
            File.WriteAllText(afterEleventhRecoverDaPath, $$"""
                {
                  "daBatch1Layer": "{{eleventhRecoverDa1.Layer}}",
                  "daBatch2Layer": "{{eleventhRecoverDa2.Layer}}",
                  "consumedDepositCount": {{afterTwelfthDeposit.ConsumedDepositCount}},
                  "deposit12IncludedInBatch": 2,
                  "messageOutboxL2ToL1Count": {{afterTwelfthDeposit.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterTwelfthDeposit.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterTwelfthDeposit.KnownInboundNonceCount}},
                  "latestCheckpointBatchNumber": {{afterTwelfthDeposit.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterTwelfthDeposit.PendingSettlementCount}},
                  "isSettlementRetrying": true,
                  "isOfflinePassportComplete": true
                }
                """);
            Assert.IsTrue(File.Exists(afterEleventhRecoverDaPath));
            StringAssert.Contains(File.ReadAllText(afterEleventhRecoverDaPath), "\"consumedDepositCount\": 12");
            StringAssert.Contains(File.ReadAllText(afterEleventhRecoverDaPath), "\"deposit12IncludedInBatch\": 2");
            StringAssert.Contains(File.ReadAllText(afterEleventhRecoverDaPath), "\"daBatch2Layer\": \"Local\"");
            var afterEleventhRecoverProm = host.ExportPrometheusMetrics();
            Assert.IsFalse(string.IsNullOrWhiteSpace(afterEleventhRecoverProm));
            var afterEleventhRecoverPromPath = Path.Combine(chainDir, "soft-seal-after-eleventh-recover-host.prom");
            host.WritePrometheusMetricsAsync(afterEleventhRecoverPromPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(afterEleventhRecoverPromPath));
            Assert.AreEqual(afterEleventhRecoverProm, File.ReadAllText(afterEleventhRecoverPromPath));

            // Twelfth offline withdrawal/outbox + FI/inbound after eleventh recover (no L1).
            var twelfthSender = Account(0xcc);
            var twelfthL2Asset = UInt160.Parse("0x" + new string('2', 40));
            var twelfthWdLeaf = host.StageWithdrawal(new WithdrawalRequest
            {
                ChainId = 20260716u,
                EmittingContract = twelfthSender,
                L2Sender = twelfthSender,
                L1Recipient = twelfthSender,
                L2Asset = twelfthL2Asset,
                Amount = new BigInteger(100),
                Nonce = 12,
            });
            Assert.AreNotEqual(UInt256.Zero, twelfthWdLeaf);
            var twelfthSealedWd = host.SealWithdrawalBatch();
            Assert.AreNotEqual(UInt256.Zero, twelfthSealedWd.Root);
            Assert.AreEqual(0, host.StagedWithdrawalCount);
            Assert.IsTrue(twelfthSealedWd.Tree.Count >= 1);
            var twelfthMerkle = twelfthSealedWd.Tree.GetProof(twelfthSealedWd.Tree.Count - 1);
            Assert.AreEqual(twelfthWdLeaf, twelfthMerkle.Leaf);
            var twelfthWdProofBytes = MerkleProofSerializer.Encode(twelfthMerkle);
            Assert.IsTrue(twelfthWdProofBytes.Length >= MerkleProofSerializer.HeaderSize);
            host.RecordRpcWithdrawalProof(twelfthWdLeaf, twelfthWdProofBytes);
            CollectionAssert.AreEqual(
                twelfthWdProofBytes,
                host.GetRpcWithdrawalProof(twelfthWdLeaf)!.Value.ToArray());
            var twelfthOutboundDraft = new CrossChainMessage
            {
                SourceChainId = 20260716u,
                TargetChainId = 0,
                Nonce = 24,
                Sender = twelfthSender,
                Receiver = twelfthSender,
                MessageType = MessageType.Event,
                Payload = new byte[] { 0x0C },
                MessageHash = UInt256.Zero,
            };
            var twelfthOutbound = twelfthOutboundDraft with
            {
                MessageHash = MessageHasher.HashMessage(twelfthOutboundDraft),
            };
            host.EnqueueOutboundMessagesAsync([twelfthOutbound]).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(12, host.MessageOutbox!.L2ToL1Count);
            var twelfthMsgProofBytes = twelfthOutbound.MessageHash.GetSpan().ToArray();
            host.RecordRpcMessageProof(twelfthOutbound.MessageHash, twelfthMsgProofBytes);
            CollectionAssert.AreEqual(
                twelfthMsgProofBytes,
                host.GetRpcMessageProof(twelfthOutbound.MessageHash)!.Value.ToArray());
            host.RecordMessageRouterFinalizedProof(twelfthOutbound.MessageHash, twelfthMsgProofBytes);
            CollectionAssert.AreEqual(
                twelfthMsgProofBytes,
                host.GetMessageRouterProofAsync(twelfthOutbound.MessageHash).AsTask().GetAwaiter().GetResult()!.Value.ToArray());
            Assert.IsTrue(host.RegisterForcedInclusionNonce(26));
            Assert.IsFalse(host.RegisterForcedInclusionNonce(26));
            Assert.AreEqual(12, host.KnownForcedInclusionNonceCount);
            Assert.IsTrue(host.RegisterInboundMessageNonce(26));
            Assert.IsFalse(host.RegisterInboundMessageNonce(26));
            Assert.AreEqual(12, host.KnownInboundNonceCount);
            Assert.AreEqual(0, host.OpenBatchForcedInclusionCount);
            Assert.AreEqual(0, host.OpenBatchL1MessageCount);
            var afterTwelfthOutbound = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(12, afterTwelfthOutbound.ConsumedDepositCount);
            Assert.AreEqual(12, afterTwelfthOutbound.MessageOutboxL2ToL1Count);
            Assert.AreEqual(12, afterTwelfthOutbound.KnownForcedInclusionNonceCount);
            Assert.AreEqual(12, afterTwelfthOutbound.KnownInboundNonceCount);
            Assert.IsTrue(afterTwelfthOutbound.IsSettlementRetrying);
            Assert.IsTrue(afterTwelfthOutbound.PendingSettlementCount >= 2);
            Assert.AreEqual(2UL, afterTwelfthOutbound.LatestCheckpointBatchNumber);
            var twelfthOutboundPath = Path.Combine(chainDir, "soft-seal-after-eleventh-recover-twelfth-outbound.json");
            File.WriteAllText(twelfthOutboundPath, $$"""
                {
                  "consumedDepositCount": {{afterTwelfthOutbound.ConsumedDepositCount}},
                  "messageOutboxL2ToL1Count": {{afterTwelfthOutbound.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterTwelfthOutbound.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterTwelfthOutbound.KnownInboundNonceCount}},
                  "withdrawalLeaf": "{{twelfthWdLeaf}}",
                  "outboundMessageHash": "{{twelfthOutbound.MessageHash}}",
                  "withdrawalProofBytes": {{twelfthWdProofBytes.Length}},
                  "messageProofBytes": {{twelfthMsgProofBytes.Length}},
                  "latestCheckpointBatchNumber": {{afterTwelfthOutbound.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterTwelfthOutbound.PendingSettlementCount}},
                  "isSettlementRetrying": true
                }
                """);
            Assert.IsTrue(File.Exists(twelfthOutboundPath));
            StringAssert.Contains(File.ReadAllText(twelfthOutboundPath), "\"messageOutboxL2ToL1Count\": 12");
            StringAssert.Contains(File.ReadAllText(twelfthOutboundPath), "\"knownForcedInclusionNonceCount\": 12");
            StringAssert.Contains(File.ReadAllText(twelfthOutboundPath), "\"consumedDepositCount\": 12");
            var twelfthOutboundRpcPath = Path.Combine(chainDir, "soft-seal-after-eleventh-recover-twelfth-outbound-rpc.json");
            File.WriteAllText(twelfthOutboundRpcPath, $$"""
                {
                  "withdrawalLeaf": "{{twelfthWdLeaf}}",
                  "outboundMessageHash": "{{twelfthOutbound.MessageHash}}",
                  "withdrawalProofBytes": {{twelfthWdProofBytes.Length}},
                  "messageProofBytes": {{twelfthMsgProofBytes.Length}},
                  "messageOutboxL2ToL1Count": 12,
                  "knownForcedInclusionNonceCount": 12,
                  "knownInboundNonceCount": 12,
                  "consumedDepositCount": 12,
                  "isSettlementRetrying": true
                }
                """);
            Assert.IsTrue(File.Exists(twelfthOutboundRpcPath));
            StringAssert.Contains(File.ReadAllText(twelfthOutboundRpcPath), "\"outboundMessageHash\": \"" + twelfthOutbound.MessageHash + "\"");

            // SubmitNext after eleventh recover / twelfth outbound remains best-effort; does not clear pending without funded L1.
            host.SubmitNextAsync().GetAwaiter().GetResult();
            Assert.IsTrue(host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult() >= 2);
            Assert.IsFalse(host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult().IsSettlementIdle);
            // Gateway multi-batch backlog still independent after eleventh recover / twelfth outbound / SubmitNext.
            Assert.IsTrue(gatewayHost.AggregatorPendingCount >= 4);
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
