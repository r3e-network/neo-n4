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
using Neo.L2.Settlement.Rpc;
using Neo.L2.State;
using Neo.Network.P2P.Payloads;
using Neo.Plugins.L2Gateway;
using Neo.Plugins.L2Rpc;
using Neo.Wallets;

namespace Neo.Plugins.L2Settlement.UnitTests;

/// <summary>Host composition tests for <see cref="OptimisticLocalHostComposition"/>.</summary>
[TestClass]
public sealed class UT_OptimisticLocalHostComposition
{
    [TestMethod]
    public void Open_FromDeployReport_WiresOptimisticLocalStack()
    {
        var reportPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "docs", "audit", "testnet-deployment-20260716-live.json"));
        if (!File.Exists(reportPath))
            Assert.Inconclusive($"repo evidence file not found at {reportPath}");

        var chainDir = Path.Combine(
            Path.GetTempPath(),
            "neo-n4-opt-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(chainDir);
        try
        {
            MaterializeOptimisticChain(chainDir, reportPath);
            using var http = CanonicalRootHttpClient();
            var key = new KeyPair(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
            using var host = OptimisticLocalHostComposition.Open(
                chainDir,
                new StubExecutor(),
                key,
                UInt160.Parse("0x" + new string('b', 40)),
                UInt256.Parse("0x" + new string('c', 64)),
                new StubSigner(Account(0x44)),
                rpcHttpClient: http,
                submittedAtUnixMs: static () => 1_700_000_000_000UL);

            Assert.AreEqual(Path.GetFullPath(chainDir), host.ChainDirectory);
            Assert.AreEqual(ProofType.Optimistic, host.Prover.Kind);
            Assert.AreEqual(ProofType.Optimistic, host.Prover.Prover!.Kind);
            Assert.AreEqual(DAMode.Local, host.DaWriter.Mode);
            Assert.AreEqual(20260716u, host.ForcedInclusion.ChainId);
            Assert.IsNotNull(host.Settlement.ProductionForcedInclusionSource);
            Assert.IsNotNull(host.Settlement.ProductionForcedInclusionFinalizer);
            Assert.IsNotNull(host.Settlement.ProductionSettlementClient);
            Assert.AreSame(host.ForcedInclusion, host.Settlement.ProductionForcedInclusionSource);
            Assert.AreSame(
                host.Settlement.ProductionForcedInclusionFinalizer,
                host.ForcedInclusionFinalizer);
            Assert.AreSame(host.Settlement.ProductionSettlementClient, host.SettlementClient);
            Assert.AreSame(host.Settlement.ProductionTransactionSender, host.TransactionSender);
            Assert.IsFalse(host.IsMetricsHttpListening);
            Assert.AreEqual(20260716u, host.Bridge.ChainId);
            Assert.IsNotNull(host.Metrics.Metrics);
            Assert.AreEqual(20260716u, host.RpcStore.ChainId);
            if (host.Settlement.ProductionDepositSource is not null)
            {
                Assert.AreSame(host.Settlement.ProductionDepositSource, host.DepositSource);
                Assert.AreSame(
                    host.Settlement.ProductionDepositSource,
                    host.Bridge.DepositSource);
                Assert.AreSame(host.Settlement.ProductionDepositSource, host.Batch.DepositSource);
            }
            if (host.Settlement.ProductionMessageRouter is not null)
            {
                Assert.AreSame(host.Settlement.ProductionMessageRouter, host.MessageRouter);
                Assert.AreSame(host.Settlement.ProductionMessageRouter, host.Batch.MessageRouter);
            }
            Assert.AreSame(host.ForcedInclusion, host.Batch.ForcedInclusionSource);
            Assert.IsTrue(host.Batch.HasSealedBatchSink);
            Assert.IsTrue(host.HasSealedBatchSink);
            Assert.AreEqual(1UL, host.NextExpectedBlock);
            Assert.AreEqual(0UL, host.LastAcknowledgedBatchNumber);
            Assert.AreEqual(1UL, host.NextBatchNumber);
            Assert.IsFalse(host.HasPendingSealedBatch);
            Assert.IsFalse(host.HasOpenBatch);
            Assert.AreEqual(0, host.OpenBatchBlockCount);
            Assert.IsFalse(host.TryRetryPendingSealedBatch());
            // Soft open-batch (no seal): empty L1 drain + MaxBlocksPerBatch > 1.
            Assert.IsTrue(host.MaxBlocksPerBatch > 1);
            var openBatchTimestampMs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            host.ProcessCommittedBlock(1, openBatchTimestampMs, 894710606, Array.Empty<byte[]>());
            Assert.IsTrue(host.HasOpenBatch);
            Assert.AreEqual(1, host.OpenBatchBlockCount);
            Assert.AreEqual(0, host.OpenBatchL1MessageCount);
            Assert.IsFalse(host.HasPendingSealedBatch);
            Assert.AreEqual(2UL, host.NextExpectedBlock);
            if (host.MaxBlocksPerBatch > 2)
            {
                host.ProcessCommittedBlock(2, openBatchTimestampMs + 1, 894710606, Array.Empty<byte[]>());
                Assert.IsTrue(host.HasOpenBatch);
                Assert.AreEqual(2, host.OpenBatchBlockCount);
                Assert.IsFalse(host.HasPendingSealedBatch);
                Assert.AreEqual(3UL, host.NextExpectedBlock);
                Assert.AreEqual(1UL, host.NextBatchNumber);
            }
            Assert.IsTrue(host.RegisterInboundMessageNonce(3));            Assert.AreEqual(1, host.KnownInboundNonceCount);
            Assert.AreEqual(0, host.L1InboxPendingCount);
            Assert.IsTrue(host.Settlement.IsProductionWired);
            Assert.IsNotNull(host.Settlement.ProductionTransactionSender);
            Assert.AreEqual(0, host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult());
            Assert.IsTrue(host.IsProductionWired);
            Assert.IsTrue(host.IsOperatorReady);
            Assert.AreEqual(20260716u, host.ChainId);
            Assert.AreEqual(ProofType.Optimistic, host.ProofType);
            Assert.AreEqual(DAMode.Local, host.DaMode);
            Assert.AreEqual(0, host.PeekSharedBridgeDeposits(8).Count);
            var status = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(status.IsOperatorReady);
            Assert.AreEqual(ProofType.Optimistic, status.ProofType);
            Assert.IsTrue(status.HasForcedInclusionFinalizer);
            Assert.IsTrue(status.HasSettlementClient);
            Assert.IsTrue(status.HasTransactionSender);
            Assert.AreEqual(1, status.KnownInboundNonceCount);
            Assert.AreEqual(0, status.L1InboxPendingCount);
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
            Assert.IsTrue(status.HasBatchProver);
            Assert.IsTrue(host.HasBatchProver);
            Assert.AreEqual(host.ChainId, host.SettlementConfiguredChainId);
            Assert.AreEqual(host.ProofType, host.SettlementConfiguredProofType);
            Assert.IsTrue(host.IsChainIdConfigConsistent);
            Assert.IsTrue(host.IsProofTypeConfigConsistent);
            Assert.AreEqual(host.ChainId, host.RpcChainId);
            Assert.AreEqual(host.DaMode, host.RpcDaMode);
            Assert.IsTrue(host.IsDaModeConfigConsistent);
            Assert.IsTrue(host.IsNeoHubHashWiringComplete);
            Assert.IsTrue(host.IsBatcherInboxWiringComplete);
            Assert.IsTrue(host.IsSecurityLevelProofTypeConsistent);
            Assert.IsTrue(host.IsSecurityLevelDaModeConsistent);
            Assert.IsTrue(host.IsMetricsWiringComplete);
            Assert.IsTrue(host.IsDepositPipelineWiringComplete);
            Assert.IsTrue(host.IsMessagePipelineWiringComplete);
            Assert.IsTrue(host.IsForcedInclusionPipelineWiringComplete);
            Assert.IsTrue(host.IsSettlementClientWiringComplete);
            Assert.IsTrue(host.IsPipelineEnabled);
            Assert.IsTrue(host.HasDepositSource);
            Assert.IsTrue(host.HasExpectedNetwork);
            Assert.IsTrue(host.HasScannerDeployHeights);
            Assert.IsTrue(host.IsOfflinePassportComplete);
            Assert.AreEqual(0, host.OfflinePassportFailures.Count);
            Assert.AreEqual(status.ProofType, status.SettlementConfiguredProofType);
            Assert.IsTrue(status.IsChainIdConfigConsistent);
            Assert.IsTrue(status.IsProofTypeConfigConsistent);
            Assert.AreEqual(status.ChainId, status.RpcChainId);
            Assert.AreEqual(status.DaMode, status.RpcDaMode);
            Assert.IsTrue(status.IsDaModeConfigConsistent);
            Assert.IsTrue(status.IsNeoHubHashWiringComplete);
            Assert.IsTrue(status.IsBatcherInboxWiringComplete);
            Assert.IsTrue(status.IsSecurityLevelProofTypeConsistent);
            Assert.IsTrue(status.IsSecurityLevelDaModeConsistent);
            Assert.IsTrue(status.IsMetricsWiringComplete);
            Assert.IsTrue(status.IsDepositPipelineWiringComplete);
            Assert.IsTrue(status.IsMessagePipelineWiringComplete);
            Assert.IsTrue(status.IsForcedInclusionPipelineWiringComplete);
            Assert.IsTrue(status.IsSettlementClientWiringComplete);
            Assert.IsTrue(status.IsPipelineEnabled);
            Assert.IsFalse(status.IsSettlementPoisoned);
            Assert.IsTrue(status.IsSettlementIdle);
            Assert.IsFalse(status.HasOverdueForcedInclusion);
            Assert.IsFalse(host.HasOverdueForcedInclusionCached());
            Assert.IsFalse(status.IsSettlementRetrying);
            Assert.IsTrue(status.IsBatcherCheckpointAligned);
            Assert.IsFalse(status.IsOpenBatchPastMaxAge);
            Assert.IsNotNull(status.OpenBatchAgeMillis);
            Assert.IsTrue(status.IsPipelineHealthy);
            Assert.AreEqual(0, status.PipelineHealthFailures.Count);
            Assert.IsTrue(host.IsPipelineHealthyAsync().AsTask().GetAwaiter().GetResult());
            Assert.AreEqual(
                0, host.GetPipelineHealthFailuresAsync().AsTask().GetAwaiter().GetResult().Count);
            Assert.IsTrue(host.IsSettlementRuntimeIdleAsync().AsTask().GetAwaiter().GetResult());
            Assert.IsFalse(host.IsSettlementPoisonedAsync().AsTask().GetAwaiter().GetResult());
            Assert.IsFalse(host.IsSettlementRetryingAsync().AsTask().GetAwaiter().GetResult());
            Assert.IsFalse(host.IsLocalHostHealthyAsync().AsTask().GetAwaiter().GetResult());
            Assert.IsFalse(status.IsMetricsHttpHealthy);
            Assert.IsFalse(host.IsMetricsHttpHealthy);
            Assert.IsTrue(status.MetricsHttpHealthFailures.Count >= 1);
            Assert.IsFalse(status.IsLocalHostHealthy);
            Assert.IsTrue(status.HasExpectedNetwork);
            Assert.IsTrue(status.HasScannerDeployHeights);
            Assert.IsTrue(status.IsOfflinePassportComplete);
            Assert.AreEqual(0, status.OfflinePassportFailures.Count);
            Assert.AreEqual(0, status.OpenBatchL2ToL2MessageCount);
            Assert.AreEqual(0, status.OpenBatchWithdrawalCount);
            Assert.IsTrue(status.SupportsLocalDaReader);
            Assert.IsTrue(host.SupportsLocalDaReader);
            Assert.IsTrue(status.HasL1RpcEndpoint);
            Assert.IsTrue(host.HasL1RpcEndpoint);
            Assert.IsTrue(status.HasSettlementManagerHash);
            Assert.IsTrue(status.HasSharedBridgeHash);
            Assert.IsTrue(host.HasMessageRouterHash);
            Assert.IsFalse(status.HasL2BridgeHash);
            Assert.IsTrue(status.HasMessageOutbox);
            Assert.IsTrue(host.HasMessageOutbox);
            Assert.IsNull(status.LatestCheckpointBatchNumber);
            Assert.AreEqual(UInt256.Zero, status.LatestCheckpointPostStateRoot);
            Assert.AreEqual(
                host.GetInitialStateRootAsync().AsTask().GetAwaiter().GetResult(),
                status.InitialStateRoot);
            Assert.AreEqual(host.GetLatestRpcStateRoot(), status.LatestRpcStateRoot);
            Assert.AreEqual(BatchStatus.Unknown, host.GetRpcBatchStatus(1));
            Assert.AreEqual(host.RpcStore.GatewayEnabled, status.GatewayEnabled);
            Assert.AreEqual(host.RpcStore.Sequencer, status.Sequencer);
            Assert.AreEqual(host.RpcStore.Exit, status.Exit);
            // Host RPC store helpers (no Neo.CLI / funded L1).
            var rpcRoot = new UInt256(Enumerable.Repeat((byte)0xAB, 32).ToArray());
            var z = UInt256.Zero;
            var rpcBatch = new L2BatchCommitment
            {
                ChainId = 20260716u,
                BatchNumber = 1,
                FirstBlock = 1,
                LastBlock = 1,
                PreStateRoot = z,
                PostStateRoot = rpcRoot,
                TxRoot = z,
                ReceiptRoot = z,
                WithdrawalRoot = z,
                L2ToL1MessageRoot = z,
                L2ToL2MessageRoot = z,
                DACommitment = z,
                PublicInputHash = z,
                ProofType = ProofType.Optimistic,
                Proof = ReadOnlyMemory<byte>.Empty,
            };
            host.AddRpcBatch(rpcBatch, BatchStatus.Pending);
            Assert.AreEqual(BatchStatus.Pending, host.GetRpcBatchStatus(1));
            Assert.IsNotNull(host.GetRpcBatch(1));
            host.FinalizeRpcBatch(1);
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(1));
            Assert.AreEqual(rpcRoot, host.GetLatestRpcStateRoot());
            Assert.AreEqual(rpcRoot, host.GetRpcStateRootAtBatch(1));
            host.RecordRpcDeposit(new DepositStatus(20260716u, 1, ConsumedOnL2: false, IncludedInBatch: null));
            Assert.IsNotNull(host.GetRpcL1DepositStatus(20260716u, 1));
            var leaf = new UInt256(Enumerable.Repeat((byte)0xCD, 32).ToArray());
            host.RecordRpcWithdrawalProof(leaf, [0x01, 0x02]);
            Assert.IsTrue(host.GetRpcWithdrawalProof(leaf) is { Length: 2 });
            var msgHash = new UInt256(Enumerable.Repeat((byte)0xEF, 32).ToArray());
            host.RecordRpcMessageProof(msgHash, [0x03]);
            Assert.IsTrue(host.GetRpcMessageProof(msgHash) is { Length: 1 });
            host.RecordMessageRouterFinalizedProof(msgHash, new byte[] { 0x04 });
            Assert.IsTrue(
                host.GetMessageRouterProofAsync(msgHash).AsTask().GetAwaiter().GetResult() is { Length: 1 });
            var l1 = UInt160.Parse("0x" + new string('1', 40));
            var l2 = UInt160.Parse("0x" + new string('2', 40));
            host.RegisterRpcAsset(l1, l2);
            Assert.AreEqual(l2, host.GetRpcBridgedAsset(l1));
            Assert.IsNotNull(host.MessageOutbox);
            Assert.IsTrue(host.RegisterForcedInclusionNonce(7));
            Assert.AreEqual(1, host.KnownForcedInclusionNonceCount);
            Assert.IsTrue(host.HasBatchForcedInclusionSource);
            Assert.IsTrue(host.HasBatchDepositSource);
            Assert.IsTrue(host.HasBatchMessageRouter);
            Assert.IsTrue(host.MaxForcedTransactionsPerBatch > 0);
            Assert.IsTrue(host.MetricsMaxConcurrentConnections > 0);
            var daReceipt = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 1,
                Payload = new byte[] { 0xAA },
            }).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(host.IsDaAvailableAsync(daReceipt).AsTask().GetAwaiter().GetResult());
            Assert.IsNotNull(host.CreateLocalDaReader());
            Assert.AreEqual(0, host.BridgeAssetCount);
            // Offline deposit mint path (no funded L1 scan).
            var l1Asset = UInt160.Parse("0x" + new string('1', 40));
            var l2Asset = UInt160.Parse("0x" + new string('2', 40));
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
            Assert.AreEqual(0, host.ProcessReadyDeposits().Count);
            // Offline withdrawal staging + L2→L1 outbox (no funded L1).
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
            host.EnqueueOutboundMessagesAsync([outbound]).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(1, host.MessageOutbox!.L2ToL1Count);
            Assert.AreNotEqual(UInt256.Zero, host.MessageOutboxL2ToL1Root);
            var prom = host.ExportPrometheusMetrics();
            Assert.IsFalse(string.IsNullOrWhiteSpace(prom));
            Assert.IsNotNull(host.CaptureMetricsSnapshot());
            Assert.IsNotNull(host.DepositProcessor);
            Assert.IsNotNull(host.WithdrawalProcessor);
            Assert.IsNotNull(host.BatchProver);
            var statusPath = Path.Combine(chainDir, "operator-status.json");
            host.WriteOperatorStatusAsync(statusPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(statusPath));
            // OpenBatchAgeMillis is wall-clock; compare a single write snapshot (not dual Format).
            var formattedStatus = File.ReadAllText(statusPath);
            StringAssert.Contains(formattedStatus, "\"proofType\": \"Optimistic\"");
            StringAssert.Contains(formattedStatus, "\"settlementRetryCount\": 0");
            StringAssert.Contains(formattedStatus, "\"settlementConfirmationLagBatches\":");
            StringAssert.Contains(formattedStatus, "\"consumedDepositCount\": 1");
            StringAssert.Contains(formattedStatus, "\"isSettlementIdle\": true");
            StringAssert.Contains(formattedStatus, "\"messageOutboxL2ToL1Count\": 1");
            StringAssert.Contains(formattedStatus, "\"stagedWithdrawalCount\": 0");
            StringAssert.Contains(formattedStatus, "\"hasOpenBatch\": true");
            Assert.IsTrue(host.IsBatcherCheckpointAlignedAsync().AsTask().GetAwaiter().GetResult());
            var statusAfterDeposit = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(1, statusAfterDeposit.ConsumedDepositCount);
            Assert.AreEqual(0, statusAfterDeposit.SettlementRetryCount);
            Assert.IsTrue(statusAfterDeposit.IsSettlementIdle);
            Assert.AreEqual(1, statusAfterDeposit.MessageOutboxL2ToL1Count);
            Assert.AreEqual(
                statusAfterDeposit.Recovery.ConfirmationLagBatches,
                statusAfterDeposit.SettlementConfirmationLagBatches);
            Assert.AreEqual(
                statusAfterDeposit.Recovery.RetryCount,
                statusAfterDeposit.SettlementRetryCount);
            var probe = host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(probe.IsOperatorReady);
            Assert.AreEqual(host.ChainId, probe.ChainId);
            Assert.AreEqual(host.ProofType.ToString(), probe.ProofType);
            Assert.AreEqual(host.DaMode.ToString(), probe.DaMode);
            Assert.AreEqual(host.RpcStore.SecurityLevel.ToString(), probe.SecurityLevel);
            Assert.AreEqual(host.RpcStore.Sequencer.ToString(), probe.Sequencer);
            Assert.AreEqual(host.RpcStore.Exit.ToString(), probe.Exit);
            Assert.AreEqual(host.ExpectedNetwork, probe.ExpectedNetwork);
            Assert.IsFalse(string.IsNullOrWhiteSpace(probe.InitialStateRoot));
            Assert.IsFalse(string.IsNullOrWhiteSpace(probe.LatestRpcStateRoot));
            Assert.AreEqual(0, probe.TrackedForcedInclusionNonceCount);
            Assert.IsTrue(probe.IsMetricsWiringComplete);
            Assert.AreEqual(host.MaxBlocksPerBatch, probe.MaxBlocksPerBatch);
            Assert.AreEqual(host.MaxBatchAgeMillis, probe.MaxBatchAgeMillis);
            Assert.AreEqual(host.MetricsBindAddress, probe.MetricsBindAddress);
            Assert.IsTrue(probe.SupportsLocalDaReader);
            Assert.IsTrue(probe.HasBatchProver);
            Assert.IsTrue(probe.HasSettlementManagerHash);
            Assert.IsTrue(probe.HasBatchDepositSource);
            Assert.IsTrue(probe.HasBatchMessageRouter);
            Assert.IsTrue(probe.HasBatchForcedInclusionSource);
            Assert.IsTrue(probe.HasScannerDeployHeights);
            Assert.AreEqual(host.ForcedInclusionDeploymentHeight, probe.ForcedInclusionDeploymentHeight);
            Assert.IsTrue(probe.IsDepositPipelineWiringComplete);
            Assert.IsTrue(probe.IsSecurityLevelProofTypeConsistent);
            Assert.IsTrue(probe.IsSecurityLevelDaModeConsistent);
            Assert.IsTrue(probe.IsPipelineEnabled);
            Assert.IsFalse(probe.HasPendingSealedBatch);
            Assert.IsNull(probe.PendingSealedBatchLastBlock);
            Assert.AreEqual(0, probe.SettlementRetryCount);
            Assert.AreEqual(statusAfterDeposit.SettlementRetryCount, probe.SettlementRetryCount);
            Assert.AreEqual(
                statusAfterDeposit.SettlementConfirmationLagBatches,
                probe.SettlementConfirmationLagBatches);
            Assert.IsTrue(probe.HasOpenBatch);
            Assert.AreEqual(0, probe.InProgressTxCount);
            Assert.AreEqual(host.OpenBatchBlockCount, probe.OpenBatchBlockCount);
            Assert.IsTrue(probe.IsBatcherCheckpointAligned);
            Assert.AreEqual(0UL, probe.LastAcknowledgedBatchNumber);
            Assert.AreEqual(1UL, probe.NextBatchNumber);
            Assert.AreEqual(host.NextExpectedBlock, probe.NextExpectedBlock);
            Assert.IsNull(probe.LatestCheckpointBatchNumber);
            Assert.IsFalse(probe.HasOverdueForcedInclusion);
            Assert.IsTrue(probe.IsPipelineHealthy);
            Assert.IsTrue(probe.IsSettlementRuntimeIdle);
            Assert.IsTrue(probe.IsSettlementIdle);
            Assert.IsFalse(probe.IsSettlementPoisoned);
            Assert.AreEqual(0, probe.PendingSettlementCount);
            Assert.IsNotNull(probe.Recovery);
            Assert.AreEqual(0, probe.Recovery.PendingCount);
            Assert.AreEqual(0, probe.ReadyDepositCount);
            Assert.AreEqual(host.DepositSourceReadyCount, probe.DepositSourceReadyCount);
            Assert.AreEqual(host.DepositSourceReservedCount, probe.DepositSourceReservedCount);
            Assert.AreEqual(host.DepositSourceSoftConsumedCount, probe.DepositSourceSoftConsumedCount);
            Assert.AreEqual(1, probe.ConsumedDepositCount);
            Assert.AreEqual(host.ConsumedDepositCount, probe.ConsumedDepositCount);
            Assert.AreEqual(host.L1InboxConsumedCount, probe.L1InboxConsumedCount);
            Assert.IsTrue(probe.HasMessageOutbox);
            Assert.AreEqual(host.MessageOutbox?.L2ToL1Count ?? 0, probe.MessageOutboxL2ToL1Count);
            Assert.AreEqual(host.KnownInboundNonceCount, probe.KnownInboundNonceCount);
            Assert.AreEqual(host.KnownForcedInclusionNonceCount, probe.KnownForcedInclusionNonceCount);
            Assert.AreEqual(host.StagedWithdrawalCount, probe.StagedWithdrawalCount);
            var probePath = Path.Combine(chainDir, "health-probe.json");
            host.WriteHealthProbeAsync(probePath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(probePath));
            var probeJson = File.ReadAllText(probePath);
            StringAssert.Contains(probeJson, "\"isSettlementRuntimeIdle\": true");
            StringAssert.Contains(probeJson, "\"isOperatorReady\": true");
            StringAssert.Contains(probeJson, "\"isPipelineEnabled\": true");
            StringAssert.Contains(probeJson, "\"settlementRetryCount\": 0");
            StringAssert.Contains(probeJson, "\"isBatcherCheckpointAligned\": true");
            StringAssert.Contains(probeJson, "\"nextBatchNumber\": 1");
            StringAssert.Contains(probeJson, "\"hasOpenBatch\": true");
            StringAssert.Contains(probeJson, "\"openBatchBlockCount\": " + host.OpenBatchBlockCount);
            StringAssert.Contains(probeJson, "\"nextExpectedBlock\": " + host.NextExpectedBlock);
            StringAssert.Contains(probeJson, "\"depositSourceReservedCount\":");
            StringAssert.Contains(probeJson, $"\"consumedDepositCount\": {host.ConsumedDepositCount}");
            StringAssert.Contains(probeJson, "\"l1InboxConsumedCount\":");
            StringAssert.Contains(probeJson, "\"hasMessageOutbox\": true");
            StringAssert.Contains(probeJson, "\"knownForcedInclusionNonceCount\":");
            StringAssert.Contains(probeJson, "\"stagedWithdrawalCount\":");
            var promPath = Path.Combine(chainDir, "metrics.prom");
            host.WritePrometheusMetricsAsync(promPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(promPath));
            var recovery = host.GetRecoveryStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(0, recovery.PendingCount);
            Assert.AreEqual(
                0,
                host.GetTrackedForcedInclusionNoncesAsync(20260716u).AsTask().GetAwaiter().GetResult()
                    .Count);

            host.StartMetricsHttp(portOverride: 0);
            Assert.IsTrue(host.IsMetricsHttpListening);
            Assert.IsTrue(host.MetricsBoundPort > 0);
            Assert.IsTrue(host.HasMetricsHealthProbe);
            Assert.IsTrue(host.HasMetricsOperatorStatus);
            Assert.IsTrue(host.IsMetricsHttpHealthy);
            var probeAfterStart = host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(probeAfterStart.HasMetricsHealthProbe);
            Assert.IsTrue(probeAfterStart.HasMetricsOperatorStatus);
            Assert.IsTrue(probeAfterStart.HasMetricsReadinessCheck);
            Assert.IsTrue(probeAfterStart.IsMetricsHttpHealthy);
            var rpcPlugin = host.CreateRpcPlugin();
            Assert.IsNotNull(rpcPlugin);
            Assert.IsFalse(rpcPlugin.IsRegistered(894710606));
        }
        finally
        {
            if (Directory.Exists(chainDir))
                Directory.Delete(chainDir, recursive: true);
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
            "neo-n4-opt-host-ms-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(chainDir);
        try
        {
            var validatorA = new KeyPair(Enumerable.Range(3, 32).Select(i => (byte)i).ToArray()).PublicKey;
            var hexA = Convert.ToHexString(validatorA.EncodePoint(true)).ToLowerInvariant();
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
                  "validators": [ "{{hexA}}" ]
                }
                """);
            NeoHubDeployReport.Load(reportPath).WriteOperatorArtifacts(chainDir);
            RewriteProofType(chainDir, "Neo.Plugins.L2Settlement", (byte)ProofType.Multisig);
            RewriteProofType(chainDir, "Neo.Plugins.L2Prover", (byte)ProofType.Multisig);
            File.WriteAllText(Path.Combine(chainDir, "genesis-manifest.json"), """
                { "chainId": 20260716, "initialStateRoot": "0x1111111111111111111111111111111111111111111111111111111111111111" }
                """);

            var key = new KeyPair(Enumerable.Range(2, 32).Select(i => (byte)i).ToArray());
            var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
                OptimisticLocalHostComposition.Open(
                    chainDir,
                    new StubExecutor(),
                    key,
                    UInt160.Parse("0x" + new string('d', 40)),
                    UInt256.Parse("0x" + new string('e', 64)),
                    new StubSigner(Account(0x55))));
            StringAssert.Contains(ex.Message, "Optimistic");
            StringAssert.Contains(ex.Message, "Multisig");
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
    public void SoftSeal_EmptyBlock_PersistsLocalCheckpoint_Optimistic()
    {
        var reportPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "docs", "audit", "testnet-deployment-20260716-live.json"));
        if (!File.Exists(reportPath))
            Assert.Inconclusive($"repo evidence file not found at {reportPath}");

        var chainDir = Path.Combine(
            Path.GetTempPath(),
            "neo-n4-opt-soft-seal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(chainDir);
        try
        {
            MaterializeOptimisticChain(chainDir, reportPath);
            RewriteMaxBlocksPerBatch(chainDir, 1);
            using var http = CanonicalRootHttpClient();
            var key = new KeyPair(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
            using var host = OptimisticLocalHostComposition.Open(
                chainDir,
                new SoftPassThroughExecutor(),
                key,
                UInt160.Parse("0x" + new string('b', 40)),
                UInt256.Parse("0x" + new string('c', 64)),
                new StubSigner(Account(0x44)),
                rpcHttpClient: http,
                submittedAtUnixMs: static () => 1_700_000_000_000UL);

            Assert.AreEqual(1, host.MaxBlocksPerBatch);
            Assert.IsTrue(host.IsOperatorReady);
            Assert.IsNull(host.GetLatestDurableCheckpointAsync().AsTask().GetAwaiter().GetResult());

            var openBatchTimestampMs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            host.ProcessCommittedBlock(1, openBatchTimestampMs, 894710606, Array.Empty<byte[]>());

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
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, status.LatestCheckpointPostStateRoot);
            Assert.IsTrue(status.PendingSettlementCount >= 2);
            Assert.IsFalse(status.IsSettlementIdle);
            Assert.IsTrue(status.IsBatcherCheckpointAligned);
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
            Assert.IsTrue(probe.PendingSettlementCount >= 2);
            Assert.IsFalse(probe.IsSettlementIdle);
            Assert.IsTrue(probe.IsOfflinePassportComplete);
            Assert.IsFalse(probe.IsPipelineHealthy);
            Assert.IsTrue(probe.IsSettlementRetrying);
            CollectionAssert.Contains(probe.PipelineHealthFailures.ToArray(), "IsSettlementRetrying");
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
            StringAssert.Contains(
                softSealStatusJson,
                "\"latestCheckpointPostStateRoot\": \"" + SoftPassThroughExecutor.PostStateRoot + "\"");
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
            StringAssert.Contains(softSealProbeFile, "IsSettlementRetrying");
            StringAssert.Contains(softSealProbeFile, "\"latestCheckpointBatchNumber\": 2");

            // Soft seal → gateway aggregator (no L1 PublishAggregate). Multisig unit parity.
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
                ProofType = ProofType.Optimistic,
                Proof = new byte[] { 0xB1 },
            };
            host.AddRpcBatch(commitment, BatchStatus.Finalized);
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(1));
            var rpcBatch = host.GetRpcBatch(1);
            Assert.IsNotNull(rpcBatch);
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, rpcBatch!.PostStateRoot);
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
            Assert.ThrowsExactly<InvalidOperationException>(() => gatewayHost.PullAggregate());
            var gwStatus = gatewayHost.GetOperatorStatus();
            Assert.AreEqual(gatewayHost.AggregatorPendingCount, gwStatus.AggregatorPendingCount);
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
            Assert.AreEqual(gwJson, File.ReadAllText(gwStatusPath));
            var gwProbeJson = gatewayHost.FormatHealthProbeJson();
            StringAssert.Contains(gwProbeJson, "\"aggregatorPendingCount\":");
            StringAssert.Contains(gwProbeJson, "\"isPublicationHealthy\": false");
            StringAssert.Contains(gwProbeJson, "\"isOutboxIdle\": false");
            StringAssert.Contains(gwProbeJson, "AggregatorPendingCount");
            var gwProbePath = Path.Combine(chainDir, "soft-seal-gateway-probe.json");
            gatewayHost.WriteHealthProbeAsync(gwProbePath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(gwProbePath));
            Assert.AreEqual(gwProbeJson, File.ReadAllText(gwProbePath));
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
            StringAssert.Contains(afterPoisonProbeJson, "IsSettlementPoisoned");
            var afterPoisonProbePath = Path.Combine(chainDir, "soft-seal-after-poison-probe.json");
            host.WriteHealthProbeAsync(afterPoisonProbePath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(afterPoisonProbePath));
            StringAssert.Contains(File.ReadAllText(afterPoisonProbePath), "\"isSettlementPoisoned\": true");

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
            Assert.IsTrue(host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult() >= 2);
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, afterRecover.LatestRpcStateRoot);
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
            Assert.IsFalse(afterRecover.IsLocalHostHealthy);
            CollectionAssert.Contains(
                afterRecover.LocalHostHealthFailures.ToArray(),
                nameof(afterRecover.IsSettlementRetrying));
            Assert.IsFalse(host.IsLocalHostHealthyAsync().AsTask().GetAwaiter().GetResult());
            CollectionAssert.Contains(
                host.GetLocalHostHealthFailuresAsync().AsTask().GetAwaiter().GetResult().ToArray(),
                "IsSettlementRetrying");
            Assert.IsFalse(host.IsSettlementRuntimeIdleAsync().AsTask().GetAwaiter().GetResult());
            Assert.IsFalse(host.IsPipelineHealthyAsync().AsTask().GetAwaiter().GetResult());
            CollectionAssert.Contains(
                host.GetPipelineHealthFailuresAsync().AsTask().GetAwaiter().GetResult().ToArray(),
                "IsSettlementRetrying");
            Assert.AreEqual(2UL, afterRecover.LatestCheckpointBatchNumber);
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, afterRecover.LatestCheckpointPostStateRoot);
            StringAssert.Contains(afterRecoverJson, "\"latestCheckpointBatchNumber\": 2");
            StringAssert.Contains(
                afterRecoverJson,
                "\"latestRpcStateRoot\": \"" + SoftPassThroughExecutor.PostStateRoot + "\"");
            // Host recovery does not clear Gateway multi-batch aggregator backlog.
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

            // Twelfth poison→recover after duodecuple soft multi-batch path retains soft state.
            LocalHostOperatorStatus afterTwelfthPoison = afterTwelfthOutbound;
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
                afterTwelfthPoison = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
                if (afterTwelfthPoison.IsSettlementPoisoned)
                    break;
            }

            Assert.IsTrue(afterTwelfthPoison.IsSettlementPoisoned);
            Assert.IsFalse(afterTwelfthPoison.IsSettlementRetrying);
            Assert.IsTrue(afterTwelfthPoison.PendingSettlementCount >= 2);
            Assert.AreEqual(12, afterTwelfthPoison.ConsumedDepositCount);
            Assert.AreEqual(12, afterTwelfthPoison.MessageOutboxL2ToL1Count);
            Assert.AreEqual(12, afterTwelfthPoison.KnownForcedInclusionNonceCount);
            Assert.AreEqual(12, afterTwelfthPoison.KnownInboundNonceCount);
            Assert.IsNotNull(afterTwelfthPoison.Recovery.BlockedBatchNumber);
            Assert.IsNotNull(afterTwelfthPoison.Recovery.ArtifactContentHash);
            var twelfthBlocked = afterTwelfthPoison.Recovery.BlockedBatchNumber!.Value;
            var twelfthHash = afterTwelfthPoison.Recovery.ArtifactContentHash!;
            Assert.ThrowsExactly<InvalidOperationException>(
                () => host.RecoverPoisonedBatchAsync(twelfthBlocked, UInt256.Zero)
                    .GetAwaiter().GetResult());
            host.RecoverPoisonedBatchAsync(twelfthBlocked, twelfthHash).GetAwaiter().GetResult();
            var afterTwelfthRecover = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsFalse(afterTwelfthRecover.IsSettlementPoisoned);
            Assert.IsTrue(afterTwelfthRecover.IsSettlementRetrying);
            Assert.AreEqual(SettlementRecoveryState.Retrying, afterTwelfthRecover.Recovery.State);
            Assert.AreEqual(0, afterTwelfthRecover.Recovery.RetryCount);
            Assert.IsTrue(host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult() >= 2);
            Assert.AreEqual(2UL, afterTwelfthRecover.LatestCheckpointBatchNumber);
            Assert.AreEqual(12, afterTwelfthRecover.ConsumedDepositCount);
            Assert.AreEqual(12, afterTwelfthRecover.MessageOutboxL2ToL1Count);
            Assert.AreEqual(12, afterTwelfthRecover.KnownForcedInclusionNonceCount);
            Assert.AreEqual(12, afterTwelfthRecover.KnownInboundNonceCount);
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(1));
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(2));
            Assert.IsTrue(host.GetRpcWithdrawalProof(twelfthWdLeaf) is { Length: > 0 });
            Assert.IsTrue(host.GetRpcMessageProof(twelfthOutbound.MessageHash) is { Length: > 0 });
            Assert.IsTrue(afterTwelfthRecover.IsOfflinePassportComplete);
            Assert.IsTrue(afterTwelfthRecover.IsOperatorReady);
            var twelfthPoisonPath = Path.Combine(chainDir, "soft-seal-twelfth-poison-recover.json");
            File.WriteAllText(twelfthPoisonPath, $$"""
                {
                  "twelfthPoisonBlockedBatch": {{twelfthBlocked}},
                  "pendingSettlementCount": {{afterTwelfthRecover.PendingSettlementCount}},
                  "latestCheckpointBatchNumber": {{afterTwelfthRecover.LatestCheckpointBatchNumber}},
                  "consumedDepositCount": {{afterTwelfthRecover.ConsumedDepositCount}},
                  "messageOutboxL2ToL1Count": {{afterTwelfthRecover.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterTwelfthRecover.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterTwelfthRecover.KnownInboundNonceCount}},
                  "rpcBatch1Status": "{{host.GetRpcBatchStatus(1)}}",
                  "rpcBatch2Status": "{{host.GetRpcBatchStatus(2)}}",
                  "twelfthWithdrawalProofPresent": true,
                  "twelfthMessageProofPresent": true,
                  "isSettlementRetrying": true,
                  "isSettlementPoisoned": false
                }
                """);
            Assert.IsTrue(File.Exists(twelfthPoisonPath));
            StringAssert.Contains(File.ReadAllText(twelfthPoisonPath), "\"rpcBatch2Status\": \"Finalized\"");
            StringAssert.Contains(File.ReadAllText(twelfthPoisonPath), "\"consumedDepositCount\": 12");
            StringAssert.Contains(File.ReadAllText(twelfthPoisonPath), "\"messageOutboxL2ToL1Count\": 12");
            StringAssert.Contains(File.ReadAllText(twelfthPoisonPath), "\"isSettlementRetrying\": true");
            // After twelfth recover: re-publish local DA + thirteenth offline deposit while Retrying.
            Assert.IsTrue(host.SupportsLocalDaReader);
            var twelfthRecoverDa1Payload = new byte[] { 0xDA, 0xA1, 0x01 };
            var twelfthRecoverDa1 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 1,
                Payload = twelfthRecoverDa1Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(DAMode.Local, twelfthRecoverDa1.Layer);
            Assert.IsTrue(host.IsDaAvailableAsync(twelfthRecoverDa1).AsTask().GetAwaiter().GetResult());
            var twelfthRecoverDa1Read = host.CreateLocalDaReader().ReadAsync(twelfthRecoverDa1).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(twelfthRecoverDa1Read is { Length: 3 });
            CollectionAssert.AreEqual(twelfthRecoverDa1Payload, twelfthRecoverDa1Read!.Value.ToArray());
            var twelfthRecoverDa2Payload = new byte[] { 0xDA, 0xA2, 0x02 };
            var twelfthRecoverDa2 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 2,
                Payload = twelfthRecoverDa2Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(host.IsDaAvailableAsync(twelfthRecoverDa2).AsTask().GetAwaiter().GetResult());
            var twelfthRecoverDa2Read = host.CreateLocalDaReader().ReadAsync(twelfthRecoverDa2).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(twelfthRecoverDa2Read is { Length: 3 });
            CollectionAssert.AreEqual(twelfthRecoverDa2Payload, twelfthRecoverDa2Read!.Value.ToArray());
            var twelfthRecoverL1Asset = UInt160.Parse("0x" + new string('1', 40));
            var twelfthRecoverL2Asset = UInt160.Parse("0x" + new string('2', 40));
            var deposit13 = new CrossChainMessage
            {
                SourceChainId = 0,
                TargetChainId = 20260716u,
                Nonce = 13,
                Sender = Account(0x66),
                Receiver = Account(0x55),
                MessageType = MessageType.Deposit,
                Payload = new DepositPayload
                {
                    L1Asset = twelfthRecoverL1Asset,
                    L2Recipient = Account(0x55),
                    Amount = new BigInteger(13_000),
                }.Encode(),
                MessageHash = UInt256.Zero,
            };
            var mint13 = host.ProcessDeposit(deposit13);
            Assert.AreEqual(twelfthRecoverL2Asset, mint13.L2Asset);
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
            Assert.IsTrue(host.HasConsumedDeposit(0, 13));
            Assert.AreEqual(13, host.ConsumedDepositCount);
            host.RecordRpcDeposit(new DepositStatus(0, 13, ConsumedOnL2: true, IncludedInBatch: 2));
            Assert.IsTrue(host.GetRpcL1DepositStatus(0, 13) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
            Assert.AreEqual(0, host.ScanSharedBridgeDepositsAsync().AsTask().GetAwaiter().GetResult());
            var afterThirteenthDeposit = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(13, afterThirteenthDeposit.ConsumedDepositCount);
            Assert.AreEqual(12, afterThirteenthDeposit.MessageOutboxL2ToL1Count);
            Assert.AreEqual(12, afterThirteenthDeposit.KnownForcedInclusionNonceCount);
            Assert.AreEqual(12, afterThirteenthDeposit.KnownInboundNonceCount);
            Assert.IsTrue(afterThirteenthDeposit.IsSettlementRetrying);
            Assert.IsTrue(afterThirteenthDeposit.PendingSettlementCount >= 2);
            Assert.AreEqual(2UL, afterThirteenthDeposit.LatestCheckpointBatchNumber);
            Assert.IsTrue(afterThirteenthDeposit.IsOfflinePassportComplete);
            var afterTwelfthRecoverDaPath = Path.Combine(chainDir, "soft-seal-after-twelfth-recover-da-deposit.json");
            File.WriteAllText(afterTwelfthRecoverDaPath, $$"""
                {
                  "daBatch1Layer": "{{twelfthRecoverDa1.Layer}}",
                  "daBatch2Layer": "{{twelfthRecoverDa2.Layer}}",
                  "consumedDepositCount": {{afterThirteenthDeposit.ConsumedDepositCount}},
                  "deposit13IncludedInBatch": 2,
                  "messageOutboxL2ToL1Count": {{afterThirteenthDeposit.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterThirteenthDeposit.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterThirteenthDeposit.KnownInboundNonceCount}},
                  "latestCheckpointBatchNumber": {{afterThirteenthDeposit.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterThirteenthDeposit.PendingSettlementCount}},
                  "isSettlementRetrying": true,
                  "isOfflinePassportComplete": true
                }
                """);
            Assert.IsTrue(File.Exists(afterTwelfthRecoverDaPath));
            StringAssert.Contains(File.ReadAllText(afterTwelfthRecoverDaPath), "\"consumedDepositCount\": 13");
            StringAssert.Contains(File.ReadAllText(afterTwelfthRecoverDaPath), "\"deposit13IncludedInBatch\": 2");
            StringAssert.Contains(File.ReadAllText(afterTwelfthRecoverDaPath), "\"daBatch2Layer\": \"Local\"");
            var afterTwelfthRecoverProm = host.ExportPrometheusMetrics();
            Assert.IsFalse(string.IsNullOrWhiteSpace(afterTwelfthRecoverProm));
            var afterTwelfthRecoverPromPath = Path.Combine(chainDir, "soft-seal-after-twelfth-recover-host.prom");
            host.WritePrometheusMetricsAsync(afterTwelfthRecoverPromPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(afterTwelfthRecoverPromPath));
            Assert.AreEqual(afterTwelfthRecoverProm, File.ReadAllText(afterTwelfthRecoverPromPath));

            // Thirteenth offline withdrawal/outbox + FI/inbound after twelfth recover (no L1).
            var thirteenthSender = Account(0xcc);
            var thirteenthL2Asset = UInt160.Parse("0x" + new string('2', 40));
            var thirteenthWdLeaf = host.StageWithdrawal(new WithdrawalRequest
            {
                ChainId = 20260716u,
                EmittingContract = thirteenthSender,
                L2Sender = thirteenthSender,
                L1Recipient = thirteenthSender,
                L2Asset = thirteenthL2Asset,
                Amount = new BigInteger(100),
                Nonce = 13,
            });
            Assert.AreNotEqual(UInt256.Zero, thirteenthWdLeaf);
            var thirteenthSealedWd = host.SealWithdrawalBatch();
            Assert.AreNotEqual(UInt256.Zero, thirteenthSealedWd.Root);
            Assert.AreEqual(0, host.StagedWithdrawalCount);
            Assert.IsTrue(thirteenthSealedWd.Tree.Count >= 1);
            var thirteenthMerkle = thirteenthSealedWd.Tree.GetProof(thirteenthSealedWd.Tree.Count - 1);
            Assert.AreEqual(thirteenthWdLeaf, thirteenthMerkle.Leaf);
            var thirteenthWdProofBytes = MerkleProofSerializer.Encode(thirteenthMerkle);
            Assert.IsTrue(thirteenthWdProofBytes.Length >= MerkleProofSerializer.HeaderSize);
            host.RecordRpcWithdrawalProof(thirteenthWdLeaf, thirteenthWdProofBytes);
            CollectionAssert.AreEqual(
                thirteenthWdProofBytes,
                host.GetRpcWithdrawalProof(thirteenthWdLeaf)!.Value.ToArray());
            var thirteenthOutboundDraft = new CrossChainMessage
            {
                SourceChainId = 20260716u,
                TargetChainId = 0,
                Nonce = 26,
                Sender = thirteenthSender,
                Receiver = thirteenthSender,
                MessageType = MessageType.Event,
                Payload = new byte[] { 0x0D },
                MessageHash = UInt256.Zero,
            };
            var thirteenthOutbound = thirteenthOutboundDraft with
            {
                MessageHash = MessageHasher.HashMessage(thirteenthOutboundDraft),
            };
            host.EnqueueOutboundMessagesAsync([thirteenthOutbound]).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(13, host.MessageOutbox!.L2ToL1Count);
            var thirteenthMsgProofBytes = thirteenthOutbound.MessageHash.GetSpan().ToArray();
            host.RecordRpcMessageProof(thirteenthOutbound.MessageHash, thirteenthMsgProofBytes);
            CollectionAssert.AreEqual(
                thirteenthMsgProofBytes,
                host.GetRpcMessageProof(thirteenthOutbound.MessageHash)!.Value.ToArray());
            host.RecordMessageRouterFinalizedProof(thirteenthOutbound.MessageHash, thirteenthMsgProofBytes);
            CollectionAssert.AreEqual(
                thirteenthMsgProofBytes,
                host.GetMessageRouterProofAsync(thirteenthOutbound.MessageHash).AsTask().GetAwaiter().GetResult()!.Value.ToArray());
            Assert.IsTrue(host.RegisterForcedInclusionNonce(28));
            Assert.IsFalse(host.RegisterForcedInclusionNonce(28));
            Assert.AreEqual(13, host.KnownForcedInclusionNonceCount);
            Assert.IsTrue(host.RegisterInboundMessageNonce(28));
            Assert.IsFalse(host.RegisterInboundMessageNonce(28));
            Assert.AreEqual(13, host.KnownInboundNonceCount);
            Assert.AreEqual(0, host.OpenBatchForcedInclusionCount);
            Assert.AreEqual(0, host.OpenBatchL1MessageCount);
            var afterThirteenthOutbound = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(13, afterThirteenthOutbound.ConsumedDepositCount);
            Assert.AreEqual(13, afterThirteenthOutbound.MessageOutboxL2ToL1Count);
            Assert.AreEqual(13, afterThirteenthOutbound.KnownForcedInclusionNonceCount);
            Assert.AreEqual(13, afterThirteenthOutbound.KnownInboundNonceCount);
            Assert.IsTrue(afterThirteenthOutbound.IsSettlementRetrying);
            Assert.IsTrue(afterThirteenthOutbound.PendingSettlementCount >= 2);
            Assert.AreEqual(2UL, afterThirteenthOutbound.LatestCheckpointBatchNumber);
            var thirteenthOutboundPath = Path.Combine(chainDir, "soft-seal-after-twelfth-recover-thirteenth-outbound.json");
            File.WriteAllText(thirteenthOutboundPath, $$"""
                {
                  "consumedDepositCount": {{afterThirteenthOutbound.ConsumedDepositCount}},
                  "messageOutboxL2ToL1Count": {{afterThirteenthOutbound.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterThirteenthOutbound.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterThirteenthOutbound.KnownInboundNonceCount}},
                  "withdrawalLeaf": "{{thirteenthWdLeaf}}",
                  "outboundMessageHash": "{{thirteenthOutbound.MessageHash}}",
                  "withdrawalProofBytes": {{thirteenthWdProofBytes.Length}},
                  "messageProofBytes": {{thirteenthMsgProofBytes.Length}},
                  "latestCheckpointBatchNumber": {{afterThirteenthOutbound.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterThirteenthOutbound.PendingSettlementCount}},
                  "isSettlementRetrying": true
                }
                """);
            Assert.IsTrue(File.Exists(thirteenthOutboundPath));
            StringAssert.Contains(File.ReadAllText(thirteenthOutboundPath), "\"messageOutboxL2ToL1Count\": 13");
            StringAssert.Contains(File.ReadAllText(thirteenthOutboundPath), "\"knownForcedInclusionNonceCount\": 13");
            StringAssert.Contains(File.ReadAllText(thirteenthOutboundPath), "\"consumedDepositCount\": 13");
            var thirteenthOutboundRpcPath = Path.Combine(chainDir, "soft-seal-after-twelfth-recover-thirteenth-outbound-rpc.json");
            File.WriteAllText(thirteenthOutboundRpcPath, $$"""
                {
                  "withdrawalLeaf": "{{thirteenthWdLeaf}}",
                  "outboundMessageHash": "{{thirteenthOutbound.MessageHash}}",
                  "withdrawalProofBytes": {{thirteenthWdProofBytes.Length}},
                  "messageProofBytes": {{thirteenthMsgProofBytes.Length}},
                  "messageOutboxL2ToL1Count": 13,
                  "knownForcedInclusionNonceCount": 13,
                  "knownInboundNonceCount": 13,
                  "consumedDepositCount": 13,
                  "isSettlementRetrying": true
                }
                """);
            Assert.IsTrue(File.Exists(thirteenthOutboundRpcPath));
            StringAssert.Contains(File.ReadAllText(thirteenthOutboundRpcPath), "\"outboundMessageHash\": \"" + thirteenthOutbound.MessageHash + "\"");

            // Thirteenth poison→recover after tredecuple soft multi-batch path retains soft state.
            LocalHostOperatorStatus afterThirteenthPoison = afterThirteenthOutbound;
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
                afterThirteenthPoison = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
                if (afterThirteenthPoison.IsSettlementPoisoned)
                    break;
            }

            Assert.IsTrue(afterThirteenthPoison.IsSettlementPoisoned);
            Assert.IsFalse(afterThirteenthPoison.IsSettlementRetrying);
            Assert.IsTrue(afterThirteenthPoison.PendingSettlementCount >= 2);
            Assert.AreEqual(13, afterThirteenthPoison.ConsumedDepositCount);
            Assert.AreEqual(13, afterThirteenthPoison.MessageOutboxL2ToL1Count);
            Assert.AreEqual(13, afterThirteenthPoison.KnownForcedInclusionNonceCount);
            Assert.AreEqual(13, afterThirteenthPoison.KnownInboundNonceCount);
            Assert.IsNotNull(afterThirteenthPoison.Recovery.BlockedBatchNumber);
            Assert.IsNotNull(afterThirteenthPoison.Recovery.ArtifactContentHash);
            var thirteenthBlocked = afterThirteenthPoison.Recovery.BlockedBatchNumber!.Value;
            var thirteenthHash = afterThirteenthPoison.Recovery.ArtifactContentHash!;
            Assert.ThrowsExactly<InvalidOperationException>(
                () => host.RecoverPoisonedBatchAsync(thirteenthBlocked, UInt256.Zero)
                    .GetAwaiter().GetResult());
            host.RecoverPoisonedBatchAsync(thirteenthBlocked, thirteenthHash).GetAwaiter().GetResult();
            var afterThirteenthRecover = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsFalse(afterThirteenthRecover.IsSettlementPoisoned);
            Assert.IsTrue(afterThirteenthRecover.IsSettlementRetrying);
            Assert.AreEqual(SettlementRecoveryState.Retrying, afterThirteenthRecover.Recovery.State);
            Assert.AreEqual(0, afterThirteenthRecover.Recovery.RetryCount);
            Assert.IsTrue(host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult() >= 2);
            Assert.AreEqual(2UL, afterThirteenthRecover.LatestCheckpointBatchNumber);
            Assert.AreEqual(13, afterThirteenthRecover.ConsumedDepositCount);
            Assert.AreEqual(13, afterThirteenthRecover.MessageOutboxL2ToL1Count);
            Assert.AreEqual(13, afterThirteenthRecover.KnownForcedInclusionNonceCount);
            Assert.AreEqual(13, afterThirteenthRecover.KnownInboundNonceCount);
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(1));
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(2));
            Assert.IsTrue(host.GetRpcWithdrawalProof(thirteenthWdLeaf) is { Length: > 0 });
            Assert.IsTrue(host.GetRpcMessageProof(thirteenthOutbound.MessageHash) is { Length: > 0 });
            Assert.IsTrue(afterThirteenthRecover.IsOfflinePassportComplete);
            Assert.IsTrue(afterThirteenthRecover.IsOperatorReady);
            var thirteenthPoisonPath = Path.Combine(chainDir, "soft-seal-thirteenth-poison-recover.json");
            File.WriteAllText(thirteenthPoisonPath, $$"""
                {
                  "thirteenthPoisonBlockedBatch": {{thirteenthBlocked}},
                  "pendingSettlementCount": {{afterThirteenthRecover.PendingSettlementCount}},
                  "latestCheckpointBatchNumber": {{afterThirteenthRecover.LatestCheckpointBatchNumber}},
                  "consumedDepositCount": {{afterThirteenthRecover.ConsumedDepositCount}},
                  "messageOutboxL2ToL1Count": {{afterThirteenthRecover.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterThirteenthRecover.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterThirteenthRecover.KnownInboundNonceCount}},
                  "rpcBatch1Status": "{{host.GetRpcBatchStatus(1)}}",
                  "rpcBatch2Status": "{{host.GetRpcBatchStatus(2)}}",
                  "thirteenthWithdrawalProofPresent": true,
                  "thirteenthMessageProofPresent": true,
                  "isSettlementRetrying": true,
                  "isSettlementPoisoned": false
                }
                """);
            Assert.IsTrue(File.Exists(thirteenthPoisonPath));
            StringAssert.Contains(File.ReadAllText(thirteenthPoisonPath), "\"rpcBatch2Status\": \"Finalized\"");
            StringAssert.Contains(File.ReadAllText(thirteenthPoisonPath), "\"consumedDepositCount\": 13");
            StringAssert.Contains(File.ReadAllText(thirteenthPoisonPath), "\"messageOutboxL2ToL1Count\": 13");
            StringAssert.Contains(File.ReadAllText(thirteenthPoisonPath), "\"isSettlementRetrying\": true");
            // After thirteenth recover: re-publish local DA + fourteenth offline deposit while Retrying.
            Assert.IsTrue(host.SupportsLocalDaReader);
            var thirteenthRecoverDa1Payload = new byte[] { 0xDA, 0xA1, 0x01 };
            var thirteenthRecoverDa1 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 1,
                Payload = thirteenthRecoverDa1Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(DAMode.Local, thirteenthRecoverDa1.Layer);
            Assert.IsTrue(host.IsDaAvailableAsync(thirteenthRecoverDa1).AsTask().GetAwaiter().GetResult());
            var thirteenthRecoverDa1Read = host.CreateLocalDaReader().ReadAsync(thirteenthRecoverDa1).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(thirteenthRecoverDa1Read is { Length: 3 });
            CollectionAssert.AreEqual(thirteenthRecoverDa1Payload, thirteenthRecoverDa1Read!.Value.ToArray());
            var thirteenthRecoverDa2Payload = new byte[] { 0xDA, 0xA2, 0x02 };
            var thirteenthRecoverDa2 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 2,
                Payload = thirteenthRecoverDa2Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(host.IsDaAvailableAsync(thirteenthRecoverDa2).AsTask().GetAwaiter().GetResult());
            var thirteenthRecoverDa2Read = host.CreateLocalDaReader().ReadAsync(thirteenthRecoverDa2).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(thirteenthRecoverDa2Read is { Length: 3 });
            CollectionAssert.AreEqual(thirteenthRecoverDa2Payload, thirteenthRecoverDa2Read!.Value.ToArray());
            var thirteenthRecoverL1Asset = UInt160.Parse("0x" + new string('1', 40));
            var thirteenthRecoverL2Asset = UInt160.Parse("0x" + new string('2', 40));
            var deposit14 = new CrossChainMessage
            {
                SourceChainId = 0,
                TargetChainId = 20260716u,
                Nonce = 14,
                Sender = Account(0x66),
                Receiver = Account(0x55),
                MessageType = MessageType.Deposit,
                Payload = new DepositPayload
                {
                    L1Asset = thirteenthRecoverL1Asset,
                    L2Recipient = Account(0x55),
                    Amount = new BigInteger(14_000),
                }.Encode(),
                MessageHash = UInt256.Zero,
            };
            var mint14 = host.ProcessDeposit(deposit14);
            Assert.AreEqual(thirteenthRecoverL2Asset, mint14.L2Asset);
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
            Assert.IsTrue(host.HasConsumedDeposit(0, 13));
            Assert.IsTrue(host.HasConsumedDeposit(0, 14));
            Assert.AreEqual(14, host.ConsumedDepositCount);
            host.RecordRpcDeposit(new DepositStatus(0, 14, ConsumedOnL2: true, IncludedInBatch: 2));
            Assert.IsTrue(host.GetRpcL1DepositStatus(0, 14) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
            Assert.AreEqual(0, host.ScanSharedBridgeDepositsAsync().AsTask().GetAwaiter().GetResult());
            var afterFourteenthDeposit = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(14, afterFourteenthDeposit.ConsumedDepositCount);
            Assert.AreEqual(13, afterFourteenthDeposit.MessageOutboxL2ToL1Count);
            Assert.AreEqual(13, afterFourteenthDeposit.KnownForcedInclusionNonceCount);
            Assert.AreEqual(13, afterFourteenthDeposit.KnownInboundNonceCount);
            Assert.IsTrue(afterFourteenthDeposit.IsSettlementRetrying);
            Assert.IsTrue(afterFourteenthDeposit.PendingSettlementCount >= 2);
            Assert.AreEqual(2UL, afterFourteenthDeposit.LatestCheckpointBatchNumber);
            Assert.IsTrue(afterFourteenthDeposit.IsOfflinePassportComplete);
            var afterThirteenthRecoverDaPath = Path.Combine(chainDir, "soft-seal-after-thirteenth-recover-da-deposit.json");
            File.WriteAllText(afterThirteenthRecoverDaPath, $$"""
                {
                  "daBatch1Layer": "{{thirteenthRecoverDa1.Layer}}",
                  "daBatch2Layer": "{{thirteenthRecoverDa2.Layer}}",
                  "consumedDepositCount": {{afterFourteenthDeposit.ConsumedDepositCount}},
                  "deposit14IncludedInBatch": 2,
                  "messageOutboxL2ToL1Count": {{afterFourteenthDeposit.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterFourteenthDeposit.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterFourteenthDeposit.KnownInboundNonceCount}},
                  "latestCheckpointBatchNumber": {{afterFourteenthDeposit.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterFourteenthDeposit.PendingSettlementCount}},
                  "isSettlementRetrying": true,
                  "isOfflinePassportComplete": true
                }
                """);
            Assert.IsTrue(File.Exists(afterThirteenthRecoverDaPath));
            StringAssert.Contains(File.ReadAllText(afterThirteenthRecoverDaPath), "\"consumedDepositCount\": 14");
            StringAssert.Contains(File.ReadAllText(afterThirteenthRecoverDaPath), "\"deposit14IncludedInBatch\": 2");
            StringAssert.Contains(File.ReadAllText(afterThirteenthRecoverDaPath), "\"daBatch2Layer\": \"Local\"");
            var afterThirteenthRecoverProm = host.ExportPrometheusMetrics();
            Assert.IsFalse(string.IsNullOrWhiteSpace(afterThirteenthRecoverProm));
            var afterThirteenthRecoverPromPath = Path.Combine(chainDir, "soft-seal-after-thirteenth-recover-host.prom");
            host.WritePrometheusMetricsAsync(afterThirteenthRecoverPromPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(afterThirteenthRecoverPromPath));
            Assert.AreEqual(afterThirteenthRecoverProm, File.ReadAllText(afterThirteenthRecoverPromPath));

            // Fourteenth offline withdrawal/outbox + FI/inbound after thirteenth recover (no L1).
            var fourteenthSender = Account(0xcc);
            var fourteenthL2Asset = UInt160.Parse("0x" + new string('2', 40));
            var fourteenthWdLeaf = host.StageWithdrawal(new WithdrawalRequest
            {
                ChainId = 20260716u,
                EmittingContract = fourteenthSender,
                L2Sender = fourteenthSender,
                L1Recipient = fourteenthSender,
                L2Asset = fourteenthL2Asset,
                Amount = new BigInteger(100),
                Nonce = 14,
            });
            Assert.AreNotEqual(UInt256.Zero, fourteenthWdLeaf);
            var fourteenthSealedWd = host.SealWithdrawalBatch();
            Assert.AreNotEqual(UInt256.Zero, fourteenthSealedWd.Root);
            Assert.AreEqual(0, host.StagedWithdrawalCount);
            Assert.IsTrue(fourteenthSealedWd.Tree.Count >= 1);
            var fourteenthMerkle = fourteenthSealedWd.Tree.GetProof(fourteenthSealedWd.Tree.Count - 1);
            Assert.AreEqual(fourteenthWdLeaf, fourteenthMerkle.Leaf);
            var fourteenthWdProofBytes = MerkleProofSerializer.Encode(fourteenthMerkle);
            Assert.IsTrue(fourteenthWdProofBytes.Length >= MerkleProofSerializer.HeaderSize);
            host.RecordRpcWithdrawalProof(fourteenthWdLeaf, fourteenthWdProofBytes);
            CollectionAssert.AreEqual(
                fourteenthWdProofBytes,
                host.GetRpcWithdrawalProof(fourteenthWdLeaf)!.Value.ToArray());
            var fourteenthOutboundDraft = new CrossChainMessage
            {
                SourceChainId = 20260716u,
                TargetChainId = 0,
                Nonce = 28,
                Sender = fourteenthSender,
                Receiver = fourteenthSender,
                MessageType = MessageType.Event,
                Payload = new byte[] { 0x0E },
                MessageHash = UInt256.Zero,
            };
            var fourteenthOutbound = fourteenthOutboundDraft with
            {
                MessageHash = MessageHasher.HashMessage(fourteenthOutboundDraft),
            };
            host.EnqueueOutboundMessagesAsync([fourteenthOutbound]).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(14, host.MessageOutbox!.L2ToL1Count);
            var fourteenthMsgProofBytes = fourteenthOutbound.MessageHash.GetSpan().ToArray();
            host.RecordRpcMessageProof(fourteenthOutbound.MessageHash, fourteenthMsgProofBytes);
            CollectionAssert.AreEqual(
                fourteenthMsgProofBytes,
                host.GetRpcMessageProof(fourteenthOutbound.MessageHash)!.Value.ToArray());
            host.RecordMessageRouterFinalizedProof(fourteenthOutbound.MessageHash, fourteenthMsgProofBytes);
            CollectionAssert.AreEqual(
                fourteenthMsgProofBytes,
                host.GetMessageRouterProofAsync(fourteenthOutbound.MessageHash).AsTask().GetAwaiter().GetResult()!.Value.ToArray());
            Assert.IsTrue(host.RegisterForcedInclusionNonce(30));
            Assert.IsFalse(host.RegisterForcedInclusionNonce(30));
            Assert.AreEqual(14, host.KnownForcedInclusionNonceCount);
            Assert.IsTrue(host.RegisterInboundMessageNonce(30));
            Assert.IsFalse(host.RegisterInboundMessageNonce(30));
            Assert.AreEqual(14, host.KnownInboundNonceCount);
            Assert.AreEqual(0, host.OpenBatchForcedInclusionCount);
            Assert.AreEqual(0, host.OpenBatchL1MessageCount);
            var afterFourteenthOutbound = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(14, afterFourteenthOutbound.ConsumedDepositCount);
            Assert.AreEqual(14, afterFourteenthOutbound.MessageOutboxL2ToL1Count);
            Assert.AreEqual(14, afterFourteenthOutbound.KnownForcedInclusionNonceCount);
            Assert.AreEqual(14, afterFourteenthOutbound.KnownInboundNonceCount);
            Assert.IsTrue(afterFourteenthOutbound.IsSettlementRetrying);
            Assert.IsTrue(afterFourteenthOutbound.PendingSettlementCount >= 2);
            Assert.AreEqual(2UL, afterFourteenthOutbound.LatestCheckpointBatchNumber);
            var fourteenthOutboundPath = Path.Combine(chainDir, "soft-seal-after-thirteenth-recover-fourteenth-outbound.json");
            File.WriteAllText(fourteenthOutboundPath, $$"""
                {
                  "consumedDepositCount": {{afterFourteenthOutbound.ConsumedDepositCount}},
                  "messageOutboxL2ToL1Count": {{afterFourteenthOutbound.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterFourteenthOutbound.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterFourteenthOutbound.KnownInboundNonceCount}},
                  "withdrawalLeaf": "{{fourteenthWdLeaf}}",
                  "outboundMessageHash": "{{fourteenthOutbound.MessageHash}}",
                  "withdrawalProofBytes": {{fourteenthWdProofBytes.Length}},
                  "messageProofBytes": {{fourteenthMsgProofBytes.Length}},
                  "latestCheckpointBatchNumber": {{afterFourteenthOutbound.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterFourteenthOutbound.PendingSettlementCount}},
                  "isSettlementRetrying": true
                }
                """);
            Assert.IsTrue(File.Exists(fourteenthOutboundPath));
            StringAssert.Contains(File.ReadAllText(fourteenthOutboundPath), "\"messageOutboxL2ToL1Count\": 14");
            StringAssert.Contains(File.ReadAllText(fourteenthOutboundPath), "\"knownForcedInclusionNonceCount\": 14");
            StringAssert.Contains(File.ReadAllText(fourteenthOutboundPath), "\"consumedDepositCount\": 14");
            var fourteenthOutboundRpcPath = Path.Combine(chainDir, "soft-seal-after-thirteenth-recover-fourteenth-outbound-rpc.json");
            File.WriteAllText(fourteenthOutboundRpcPath, $$"""
                {
                  "withdrawalLeaf": "{{fourteenthWdLeaf}}",
                  "outboundMessageHash": "{{fourteenthOutbound.MessageHash}}",
                  "withdrawalProofBytes": {{fourteenthWdProofBytes.Length}},
                  "messageProofBytes": {{fourteenthMsgProofBytes.Length}},
                  "messageOutboxL2ToL1Count": 14,
                  "knownForcedInclusionNonceCount": 14,
                  "knownInboundNonceCount": 14,
                  "consumedDepositCount": 14,
                  "isSettlementRetrying": true
                }
                """);
            Assert.IsTrue(File.Exists(fourteenthOutboundRpcPath));
            StringAssert.Contains(File.ReadAllText(fourteenthOutboundRpcPath), "\"outboundMessageHash\": \"" + fourteenthOutbound.MessageHash + "\"");

            // Fourteenth poison→recover after quattuordecuple soft multi-batch path retains soft state.
            LocalHostOperatorStatus afterFourteenthPoison = afterFourteenthOutbound;
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
                afterFourteenthPoison = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
                if (afterFourteenthPoison.IsSettlementPoisoned)
                    break;
            }

            Assert.IsTrue(afterFourteenthPoison.IsSettlementPoisoned);
            Assert.IsFalse(afterFourteenthPoison.IsSettlementRetrying);
            Assert.IsTrue(afterFourteenthPoison.PendingSettlementCount >= 2);
            Assert.AreEqual(14, afterFourteenthPoison.ConsumedDepositCount);
            Assert.AreEqual(14, afterFourteenthPoison.MessageOutboxL2ToL1Count);
            Assert.AreEqual(14, afterFourteenthPoison.KnownForcedInclusionNonceCount);
            Assert.AreEqual(14, afterFourteenthPoison.KnownInboundNonceCount);
            Assert.IsNotNull(afterFourteenthPoison.Recovery.BlockedBatchNumber);
            Assert.IsNotNull(afterFourteenthPoison.Recovery.ArtifactContentHash);
            var fourteenthBlocked = afterFourteenthPoison.Recovery.BlockedBatchNumber!.Value;
            var fourteenthHash = afterFourteenthPoison.Recovery.ArtifactContentHash!;
            Assert.ThrowsExactly<InvalidOperationException>(
                () => host.RecoverPoisonedBatchAsync(fourteenthBlocked, UInt256.Zero)
                    .GetAwaiter().GetResult());
            host.RecoverPoisonedBatchAsync(fourteenthBlocked, fourteenthHash).GetAwaiter().GetResult();
            var afterFourteenthRecover = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsFalse(afterFourteenthRecover.IsSettlementPoisoned);
            Assert.IsTrue(afterFourteenthRecover.IsSettlementRetrying);
            Assert.AreEqual(SettlementRecoveryState.Retrying, afterFourteenthRecover.Recovery.State);
            Assert.AreEqual(0, afterFourteenthRecover.Recovery.RetryCount);
            Assert.IsTrue(host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult() >= 2);
            Assert.AreEqual(2UL, afterFourteenthRecover.LatestCheckpointBatchNumber);
            Assert.AreEqual(14, afterFourteenthRecover.ConsumedDepositCount);
            Assert.AreEqual(14, afterFourteenthRecover.MessageOutboxL2ToL1Count);
            Assert.AreEqual(14, afterFourteenthRecover.KnownForcedInclusionNonceCount);
            Assert.AreEqual(14, afterFourteenthRecover.KnownInboundNonceCount);
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(1));
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(2));
            Assert.IsTrue(host.GetRpcWithdrawalProof(fourteenthWdLeaf) is { Length: > 0 });
            Assert.IsTrue(host.GetRpcMessageProof(fourteenthOutbound.MessageHash) is { Length: > 0 });
            Assert.IsTrue(afterFourteenthRecover.IsOfflinePassportComplete);
            Assert.IsTrue(afterFourteenthRecover.IsOperatorReady);
            var fourteenthPoisonPath = Path.Combine(chainDir, "soft-seal-fourteenth-poison-recover.json");
            File.WriteAllText(fourteenthPoisonPath, $$"""
                {
                  "fourteenthPoisonBlockedBatch": {{fourteenthBlocked}},
                  "pendingSettlementCount": {{afterFourteenthRecover.PendingSettlementCount}},
                  "latestCheckpointBatchNumber": {{afterFourteenthRecover.LatestCheckpointBatchNumber}},
                  "consumedDepositCount": {{afterFourteenthRecover.ConsumedDepositCount}},
                  "messageOutboxL2ToL1Count": {{afterFourteenthRecover.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterFourteenthRecover.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterFourteenthRecover.KnownInboundNonceCount}},
                  "rpcBatch1Status": "{{host.GetRpcBatchStatus(1)}}",
                  "rpcBatch2Status": "{{host.GetRpcBatchStatus(2)}}",
                  "fourteenthWithdrawalProofPresent": true,
                  "fourteenthMessageProofPresent": true,
                  "isSettlementRetrying": true,
                  "isSettlementPoisoned": false
                }
                """);
            Assert.IsTrue(File.Exists(fourteenthPoisonPath));
            StringAssert.Contains(File.ReadAllText(fourteenthPoisonPath), "\"rpcBatch2Status\": \"Finalized\"");
            StringAssert.Contains(File.ReadAllText(fourteenthPoisonPath), "\"consumedDepositCount\": 14");
            StringAssert.Contains(File.ReadAllText(fourteenthPoisonPath), "\"messageOutboxL2ToL1Count\": 14");
            StringAssert.Contains(File.ReadAllText(fourteenthPoisonPath), "\"isSettlementRetrying\": true");
            // After fourteenth recover: re-publish local DA + fifteenth offline deposit while Retrying.
            Assert.IsTrue(host.SupportsLocalDaReader);
            var fourteenthRecoverDa1Payload = new byte[] { 0xDA, 0xA1, 0x01 };
            var fourteenthRecoverDa1 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 1,
                Payload = fourteenthRecoverDa1Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(DAMode.Local, fourteenthRecoverDa1.Layer);
            Assert.IsTrue(host.IsDaAvailableAsync(fourteenthRecoverDa1).AsTask().GetAwaiter().GetResult());
            var fourteenthRecoverDa1Read = host.CreateLocalDaReader().ReadAsync(fourteenthRecoverDa1).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(fourteenthRecoverDa1Read is { Length: 3 });
            CollectionAssert.AreEqual(fourteenthRecoverDa1Payload, fourteenthRecoverDa1Read!.Value.ToArray());
            var fourteenthRecoverDa2Payload = new byte[] { 0xDA, 0xA2, 0x02 };
            var fourteenthRecoverDa2 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 2,
                Payload = fourteenthRecoverDa2Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(host.IsDaAvailableAsync(fourteenthRecoverDa2).AsTask().GetAwaiter().GetResult());
            var fourteenthRecoverDa2Read = host.CreateLocalDaReader().ReadAsync(fourteenthRecoverDa2).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(fourteenthRecoverDa2Read is { Length: 3 });
            CollectionAssert.AreEqual(fourteenthRecoverDa2Payload, fourteenthRecoverDa2Read!.Value.ToArray());
            var fourteenthRecoverL1Asset = UInt160.Parse("0x" + new string('1', 40));
            var fourteenthRecoverL2Asset = UInt160.Parse("0x" + new string('2', 40));
            var deposit15 = new CrossChainMessage
            {
                SourceChainId = 0,
                TargetChainId = 20260716u,
                Nonce = 15,
                Sender = Account(0x66),
                Receiver = Account(0x55),
                MessageType = MessageType.Deposit,
                Payload = new DepositPayload
                {
                    L1Asset = fourteenthRecoverL1Asset,
                    L2Recipient = Account(0x55),
                    Amount = new BigInteger(15_000),
                }.Encode(),
                MessageHash = UInt256.Zero,
            };
            var mint15 = host.ProcessDeposit(deposit15);
            Assert.AreEqual(fourteenthRecoverL2Asset, mint15.L2Asset);
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
            Assert.IsTrue(host.HasConsumedDeposit(0, 13));
            Assert.IsTrue(host.HasConsumedDeposit(0, 14));
            Assert.IsTrue(host.HasConsumedDeposit(0, 15));
            Assert.AreEqual(15, host.ConsumedDepositCount);
            host.RecordRpcDeposit(new DepositStatus(0, 15, ConsumedOnL2: true, IncludedInBatch: 2));
            Assert.IsTrue(host.GetRpcL1DepositStatus(0, 15) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
            Assert.AreEqual(0, host.ScanSharedBridgeDepositsAsync().AsTask().GetAwaiter().GetResult());
            var afterFifteenthDeposit = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(15, afterFifteenthDeposit.ConsumedDepositCount);
            Assert.AreEqual(14, afterFifteenthDeposit.MessageOutboxL2ToL1Count);
            Assert.AreEqual(14, afterFifteenthDeposit.KnownForcedInclusionNonceCount);
            Assert.AreEqual(14, afterFifteenthDeposit.KnownInboundNonceCount);
            Assert.IsTrue(afterFifteenthDeposit.IsSettlementRetrying);
            Assert.IsTrue(afterFifteenthDeposit.PendingSettlementCount >= 2);
            Assert.AreEqual(2UL, afterFifteenthDeposit.LatestCheckpointBatchNumber);
            Assert.IsTrue(afterFifteenthDeposit.IsOfflinePassportComplete);
            var afterFourteenthRecoverDaPath = Path.Combine(chainDir, "soft-seal-after-fourteenth-recover-da-deposit.json");
            File.WriteAllText(afterFourteenthRecoverDaPath, $$"""
                {
                  "daBatch1Layer": "{{fourteenthRecoverDa1.Layer}}",
                  "daBatch2Layer": "{{fourteenthRecoverDa2.Layer}}",
                  "consumedDepositCount": {{afterFifteenthDeposit.ConsumedDepositCount}},
                  "deposit15IncludedInBatch": 2,
                  "messageOutboxL2ToL1Count": {{afterFifteenthDeposit.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterFifteenthDeposit.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterFifteenthDeposit.KnownInboundNonceCount}},
                  "latestCheckpointBatchNumber": {{afterFifteenthDeposit.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterFifteenthDeposit.PendingSettlementCount}},
                  "isSettlementRetrying": true,
                  "isOfflinePassportComplete": true
                }
                """);
            Assert.IsTrue(File.Exists(afterFourteenthRecoverDaPath));
            StringAssert.Contains(File.ReadAllText(afterFourteenthRecoverDaPath), "\"consumedDepositCount\": 15");
            StringAssert.Contains(File.ReadAllText(afterFourteenthRecoverDaPath), "\"deposit15IncludedInBatch\": 2");
            StringAssert.Contains(File.ReadAllText(afterFourteenthRecoverDaPath), "\"daBatch2Layer\": \"Local\"");
            var afterFourteenthRecoverProm = host.ExportPrometheusMetrics();
            Assert.IsFalse(string.IsNullOrWhiteSpace(afterFourteenthRecoverProm));
            var afterFourteenthRecoverPromPath = Path.Combine(chainDir, "soft-seal-after-fourteenth-recover-host.prom");
            host.WritePrometheusMetricsAsync(afterFourteenthRecoverPromPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(afterFourteenthRecoverPromPath));
            Assert.AreEqual(afterFourteenthRecoverProm, File.ReadAllText(afterFourteenthRecoverPromPath));

            // Fifteenth offline withdrawal/outbox + FI/inbound after fourteenth recover (no L1).
            var fifteenthSender = Account(0xcc);
            var fifteenthL2Asset = UInt160.Parse("0x" + new string('2', 40));
            var fifteenthWdLeaf = host.StageWithdrawal(new WithdrawalRequest
            {
                ChainId = 20260716u,
                EmittingContract = fifteenthSender,
                L2Sender = fifteenthSender,
                L1Recipient = fifteenthSender,
                L2Asset = fifteenthL2Asset,
                Amount = new BigInteger(100),
                Nonce = 15,
            });
            Assert.AreNotEqual(UInt256.Zero, fifteenthWdLeaf);
            var fifteenthSealedWd = host.SealWithdrawalBatch();
            Assert.AreNotEqual(UInt256.Zero, fifteenthSealedWd.Root);
            Assert.AreEqual(0, host.StagedWithdrawalCount);
            Assert.IsTrue(fifteenthSealedWd.Tree.Count >= 1);
            var fifteenthMerkle = fifteenthSealedWd.Tree.GetProof(fifteenthSealedWd.Tree.Count - 1);
            Assert.AreEqual(fifteenthWdLeaf, fifteenthMerkle.Leaf);
            var fifteenthWdProofBytes = MerkleProofSerializer.Encode(fifteenthMerkle);
            Assert.IsTrue(fifteenthWdProofBytes.Length >= MerkleProofSerializer.HeaderSize);
            host.RecordRpcWithdrawalProof(fifteenthWdLeaf, fifteenthWdProofBytes);
            CollectionAssert.AreEqual(
                fifteenthWdProofBytes,
                host.GetRpcWithdrawalProof(fifteenthWdLeaf)!.Value.ToArray());
            var fifteenthOutboundDraft = new CrossChainMessage
            {
                SourceChainId = 20260716u,
                TargetChainId = 0,
                Nonce = 30,
                Sender = fifteenthSender,
                Receiver = fifteenthSender,
                MessageType = MessageType.Event,
                Payload = new byte[] { 0x0F },
                MessageHash = UInt256.Zero,
            };
            var fifteenthOutbound = fifteenthOutboundDraft with
            {
                MessageHash = MessageHasher.HashMessage(fifteenthOutboundDraft),
            };
            host.EnqueueOutboundMessagesAsync([fifteenthOutbound]).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(15, host.MessageOutbox!.L2ToL1Count);
            var fifteenthMsgProofBytes = fifteenthOutbound.MessageHash.GetSpan().ToArray();
            host.RecordRpcMessageProof(fifteenthOutbound.MessageHash, fifteenthMsgProofBytes);
            CollectionAssert.AreEqual(
                fifteenthMsgProofBytes,
                host.GetRpcMessageProof(fifteenthOutbound.MessageHash)!.Value.ToArray());
            host.RecordMessageRouterFinalizedProof(fifteenthOutbound.MessageHash, fifteenthMsgProofBytes);
            CollectionAssert.AreEqual(
                fifteenthMsgProofBytes,
                host.GetMessageRouterProofAsync(fifteenthOutbound.MessageHash).AsTask().GetAwaiter().GetResult()!.Value.ToArray());
            Assert.IsTrue(host.RegisterForcedInclusionNonce(32));
            Assert.IsFalse(host.RegisterForcedInclusionNonce(32));
            Assert.AreEqual(15, host.KnownForcedInclusionNonceCount);
            Assert.IsTrue(host.RegisterInboundMessageNonce(32));
            Assert.IsFalse(host.RegisterInboundMessageNonce(32));
            Assert.AreEqual(15, host.KnownInboundNonceCount);
            Assert.AreEqual(0, host.OpenBatchForcedInclusionCount);
            Assert.AreEqual(0, host.OpenBatchL1MessageCount);
            var afterFifteenthOutbound = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(15, afterFifteenthOutbound.ConsumedDepositCount);
            Assert.AreEqual(15, afterFifteenthOutbound.MessageOutboxL2ToL1Count);
            Assert.AreEqual(15, afterFifteenthOutbound.KnownForcedInclusionNonceCount);
            Assert.AreEqual(15, afterFifteenthOutbound.KnownInboundNonceCount);
            Assert.IsTrue(afterFifteenthOutbound.IsSettlementRetrying);
            Assert.IsTrue(afterFifteenthOutbound.PendingSettlementCount >= 2);
            Assert.AreEqual(2UL, afterFifteenthOutbound.LatestCheckpointBatchNumber);
            var fifteenthOutboundPath = Path.Combine(chainDir, "soft-seal-after-fourteenth-recover-fifteenth-outbound.json");
            File.WriteAllText(fifteenthOutboundPath, $$"""
                {
                  "consumedDepositCount": {{afterFifteenthOutbound.ConsumedDepositCount}},
                  "messageOutboxL2ToL1Count": {{afterFifteenthOutbound.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterFifteenthOutbound.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterFifteenthOutbound.KnownInboundNonceCount}},
                  "withdrawalLeaf": "{{fifteenthWdLeaf}}",
                  "outboundMessageHash": "{{fifteenthOutbound.MessageHash}}",
                  "withdrawalProofBytes": {{fifteenthWdProofBytes.Length}},
                  "messageProofBytes": {{fifteenthMsgProofBytes.Length}},
                  "latestCheckpointBatchNumber": {{afterFifteenthOutbound.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterFifteenthOutbound.PendingSettlementCount}},
                  "isSettlementRetrying": true
                }
                """);
            Assert.IsTrue(File.Exists(fifteenthOutboundPath));
            StringAssert.Contains(File.ReadAllText(fifteenthOutboundPath), "\"messageOutboxL2ToL1Count\": 15");
            StringAssert.Contains(File.ReadAllText(fifteenthOutboundPath), "\"knownForcedInclusionNonceCount\": 15");
            StringAssert.Contains(File.ReadAllText(fifteenthOutboundPath), "\"consumedDepositCount\": 15");
            var fifteenthOutboundRpcPath = Path.Combine(chainDir, "soft-seal-after-fourteenth-recover-fifteenth-outbound-rpc.json");
            File.WriteAllText(fifteenthOutboundRpcPath, $$"""
                {
                  "withdrawalLeaf": "{{fifteenthWdLeaf}}",
                  "outboundMessageHash": "{{fifteenthOutbound.MessageHash}}",
                  "withdrawalProofBytes": {{fifteenthWdProofBytes.Length}},
                  "messageProofBytes": {{fifteenthMsgProofBytes.Length}},
                  "messageOutboxL2ToL1Count": 15,
                  "knownForcedInclusionNonceCount": 15,
                  "knownInboundNonceCount": 15,
                  "consumedDepositCount": 15,
                  "isSettlementRetrying": true
                }
                """);
            Assert.IsTrue(File.Exists(fifteenthOutboundRpcPath));
            StringAssert.Contains(File.ReadAllText(fifteenthOutboundRpcPath), "\"outboundMessageHash\": \"" + fifteenthOutbound.MessageHash + "\"");

            // Fifteenth poison→recover after quindecuple soft multi-batch path retains soft state.
            LocalHostOperatorStatus afterFifteenthPoison = afterFifteenthOutbound;
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
                afterFifteenthPoison = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
                if (afterFifteenthPoison.IsSettlementPoisoned)
                    break;
            }

            Assert.IsTrue(afterFifteenthPoison.IsSettlementPoisoned);
            Assert.IsFalse(afterFifteenthPoison.IsSettlementRetrying);
            Assert.IsTrue(afterFifteenthPoison.PendingSettlementCount >= 2);
            Assert.AreEqual(15, afterFifteenthPoison.ConsumedDepositCount);
            Assert.AreEqual(15, afterFifteenthPoison.MessageOutboxL2ToL1Count);
            Assert.AreEqual(15, afterFifteenthPoison.KnownForcedInclusionNonceCount);
            Assert.AreEqual(15, afterFifteenthPoison.KnownInboundNonceCount);
            Assert.IsNotNull(afterFifteenthPoison.Recovery.BlockedBatchNumber);
            Assert.IsNotNull(afterFifteenthPoison.Recovery.ArtifactContentHash);
            var fifteenthBlocked = afterFifteenthPoison.Recovery.BlockedBatchNumber!.Value;
            var fifteenthHash = afterFifteenthPoison.Recovery.ArtifactContentHash!;
            Assert.ThrowsExactly<InvalidOperationException>(
                () => host.RecoverPoisonedBatchAsync(fifteenthBlocked, UInt256.Zero)
                    .GetAwaiter().GetResult());
            host.RecoverPoisonedBatchAsync(fifteenthBlocked, fifteenthHash).GetAwaiter().GetResult();
            var afterFifteenthRecover = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsFalse(afterFifteenthRecover.IsSettlementPoisoned);
            Assert.IsTrue(afterFifteenthRecover.IsSettlementRetrying);
            Assert.AreEqual(SettlementRecoveryState.Retrying, afterFifteenthRecover.Recovery.State);
            Assert.AreEqual(0, afterFifteenthRecover.Recovery.RetryCount);
            Assert.IsTrue(host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult() >= 2);
            Assert.AreEqual(2UL, afterFifteenthRecover.LatestCheckpointBatchNumber);
            Assert.AreEqual(15, afterFifteenthRecover.ConsumedDepositCount);
            Assert.AreEqual(15, afterFifteenthRecover.MessageOutboxL2ToL1Count);
            Assert.AreEqual(15, afterFifteenthRecover.KnownForcedInclusionNonceCount);
            Assert.AreEqual(15, afterFifteenthRecover.KnownInboundNonceCount);
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(1));
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(2));
            Assert.IsTrue(host.GetRpcWithdrawalProof(fifteenthWdLeaf) is { Length: > 0 });
            Assert.IsTrue(host.GetRpcMessageProof(fifteenthOutbound.MessageHash) is { Length: > 0 });
            Assert.IsTrue(afterFifteenthRecover.IsOfflinePassportComplete);
            Assert.IsTrue(afterFifteenthRecover.IsOperatorReady);
            var fifteenthPoisonPath = Path.Combine(chainDir, "soft-seal-fifteenth-poison-recover.json");
            File.WriteAllText(fifteenthPoisonPath, $$"""
                {
                  "fifteenthPoisonBlockedBatch": {{fifteenthBlocked}},
                  "pendingSettlementCount": {{afterFifteenthRecover.PendingSettlementCount}},
                  "latestCheckpointBatchNumber": {{afterFifteenthRecover.LatestCheckpointBatchNumber}},
                  "consumedDepositCount": {{afterFifteenthRecover.ConsumedDepositCount}},
                  "messageOutboxL2ToL1Count": {{afterFifteenthRecover.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterFifteenthRecover.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterFifteenthRecover.KnownInboundNonceCount}},
                  "rpcBatch1Status": "{{host.GetRpcBatchStatus(1)}}",
                  "rpcBatch2Status": "{{host.GetRpcBatchStatus(2)}}",
                  "fifteenthWithdrawalProofPresent": true,
                  "fifteenthMessageProofPresent": true,
                  "isSettlementRetrying": true,
                  "isSettlementPoisoned": false
                }
                """);
            Assert.IsTrue(File.Exists(fifteenthPoisonPath));
            StringAssert.Contains(File.ReadAllText(fifteenthPoisonPath), "\"rpcBatch2Status\": \"Finalized\"");
            StringAssert.Contains(File.ReadAllText(fifteenthPoisonPath), "\"consumedDepositCount\": 15");
            StringAssert.Contains(File.ReadAllText(fifteenthPoisonPath), "\"messageOutboxL2ToL1Count\": 15");
            StringAssert.Contains(File.ReadAllText(fifteenthPoisonPath), "\"isSettlementRetrying\": true");

            // After fifteenth recover: re-publish local DA + sixteenth offline deposit while Retrying.
            Assert.IsTrue(host.SupportsLocalDaReader);
            var fifteenthRecoverDa1Payload = new byte[] { 0xDA, 0xA1, 0x01 };
            var fifteenthRecoverDa1 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 1,
                Payload = fifteenthRecoverDa1Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(DAMode.Local, fifteenthRecoverDa1.Layer);
            Assert.IsTrue(host.IsDaAvailableAsync(fifteenthRecoverDa1).AsTask().GetAwaiter().GetResult());
            var fifteenthRecoverDa1Read = host.CreateLocalDaReader().ReadAsync(fifteenthRecoverDa1).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(fifteenthRecoverDa1Read is { Length: 3 });
            CollectionAssert.AreEqual(fifteenthRecoverDa1Payload, fifteenthRecoverDa1Read!.Value.ToArray());
            var fifteenthRecoverDa2Payload = new byte[] { 0xDA, 0xA2, 0x02 };
            var fifteenthRecoverDa2 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 2,
                Payload = fifteenthRecoverDa2Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(host.IsDaAvailableAsync(fifteenthRecoverDa2).AsTask().GetAwaiter().GetResult());
            var fifteenthRecoverDa2Read = host.CreateLocalDaReader().ReadAsync(fifteenthRecoverDa2).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(fifteenthRecoverDa2Read is { Length: 3 });
            CollectionAssert.AreEqual(fifteenthRecoverDa2Payload, fifteenthRecoverDa2Read!.Value.ToArray());
            var fifteenthRecoverL1Asset = UInt160.Parse("0x" + new string('1', 40));
            var fifteenthRecoverL2Asset = UInt160.Parse("0x" + new string('2', 40));
            var deposit16 = new CrossChainMessage
            {
                SourceChainId = 0,
                TargetChainId = 20260716u,
                Nonce = 16,
                Sender = Account(0x66),
                Receiver = Account(0x55),
                MessageType = MessageType.Deposit,
                Payload = new DepositPayload
                {
                    L1Asset = fifteenthRecoverL1Asset,
                    L2Recipient = Account(0x55),
                    Amount = new BigInteger(16_000),
                }.Encode(),
                MessageHash = UInt256.Zero,
            };
            var mint16 = host.ProcessDeposit(deposit16);
            Assert.AreEqual(fifteenthRecoverL2Asset, mint16.L2Asset);
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
            Assert.IsTrue(host.HasConsumedDeposit(0, 13));
            Assert.IsTrue(host.HasConsumedDeposit(0, 14));
            Assert.IsTrue(host.HasConsumedDeposit(0, 15));
            Assert.IsTrue(host.HasConsumedDeposit(0, 16));
            Assert.AreEqual(16, host.ConsumedDepositCount);
            host.RecordRpcDeposit(new DepositStatus(0, 16, ConsumedOnL2: true, IncludedInBatch: 2));
            Assert.IsTrue(host.GetRpcL1DepositStatus(0, 16) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
            Assert.AreEqual(0, host.ScanSharedBridgeDepositsAsync().AsTask().GetAwaiter().GetResult());
            var afterSixteenthDeposit = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(16, afterSixteenthDeposit.ConsumedDepositCount);
            Assert.AreEqual(15, afterSixteenthDeposit.MessageOutboxL2ToL1Count);
            Assert.AreEqual(15, afterSixteenthDeposit.KnownForcedInclusionNonceCount);
            Assert.AreEqual(15, afterSixteenthDeposit.KnownInboundNonceCount);
            Assert.IsTrue(afterSixteenthDeposit.IsSettlementRetrying);
            Assert.IsTrue(afterSixteenthDeposit.PendingSettlementCount >= 2);
            Assert.AreEqual(2UL, afterSixteenthDeposit.LatestCheckpointBatchNumber);
            Assert.IsTrue(afterSixteenthDeposit.IsOfflinePassportComplete);
            var afterFifteenthRecoverDaPath = Path.Combine(chainDir, "soft-seal-after-fifteenth-recover-da-deposit.json");
            File.WriteAllText(afterFifteenthRecoverDaPath, $$"""
                {
                  "daBatch1Layer": "{{fifteenthRecoverDa1.Layer}}",
                  "daBatch2Layer": "{{fifteenthRecoverDa2.Layer}}",
                  "consumedDepositCount": {{afterSixteenthDeposit.ConsumedDepositCount}},
                  "deposit16IncludedInBatch": 2,
                  "messageOutboxL2ToL1Count": {{afterSixteenthDeposit.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterSixteenthDeposit.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterSixteenthDeposit.KnownInboundNonceCount}},
                  "latestCheckpointBatchNumber": {{afterSixteenthDeposit.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterSixteenthDeposit.PendingSettlementCount}},
                  "isSettlementRetrying": true,
                  "isOfflinePassportComplete": true
                }
                """);
            Assert.IsTrue(File.Exists(afterFifteenthRecoverDaPath));
            StringAssert.Contains(File.ReadAllText(afterFifteenthRecoverDaPath), "\"consumedDepositCount\": 16");
            StringAssert.Contains(File.ReadAllText(afterFifteenthRecoverDaPath), "\"deposit16IncludedInBatch\": 2");
            StringAssert.Contains(File.ReadAllText(afterFifteenthRecoverDaPath), "\"daBatch2Layer\": \"Local\"");
            var afterFifteenthRecoverProm = host.ExportPrometheusMetrics();
            Assert.IsFalse(string.IsNullOrWhiteSpace(afterFifteenthRecoverProm));
            var afterFifteenthRecoverPromPath = Path.Combine(chainDir, "soft-seal-after-fifteenth-recover-host.prom");
            host.WritePrometheusMetricsAsync(afterFifteenthRecoverPromPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(afterFifteenthRecoverPromPath));
            Assert.AreEqual(afterFifteenthRecoverProm, File.ReadAllText(afterFifteenthRecoverPromPath));

            // Sixteenth offline withdrawal/outbox + FI/inbound after fifteenth recover (no L1).
            var sixteenthSender = Account(0xcc);
            var sixteenthL2Asset = UInt160.Parse("0x" + new string('2', 40));
            var sixteenthWdLeaf = host.StageWithdrawal(new WithdrawalRequest
            {
                ChainId = 20260716u,
                EmittingContract = sixteenthSender,
                L2Sender = sixteenthSender,
                L1Recipient = sixteenthSender,
                L2Asset = sixteenthL2Asset,
                Amount = new BigInteger(100),
                Nonce = 16,
            });
            Assert.AreNotEqual(UInt256.Zero, sixteenthWdLeaf);
            var sixteenthSealedWd = host.SealWithdrawalBatch();
            Assert.AreNotEqual(UInt256.Zero, sixteenthSealedWd.Root);
            Assert.AreEqual(0, host.StagedWithdrawalCount);
            Assert.IsTrue(sixteenthSealedWd.Tree.Count >= 1);
            var sixteenthMerkle = sixteenthSealedWd.Tree.GetProof(sixteenthSealedWd.Tree.Count - 1);
            Assert.AreEqual(sixteenthWdLeaf, sixteenthMerkle.Leaf);
            var sixteenthWdProofBytes = MerkleProofSerializer.Encode(sixteenthMerkle);
            Assert.IsTrue(sixteenthWdProofBytes.Length >= MerkleProofSerializer.HeaderSize);
            host.RecordRpcWithdrawalProof(sixteenthWdLeaf, sixteenthWdProofBytes);
            CollectionAssert.AreEqual(
                sixteenthWdProofBytes,
                host.GetRpcWithdrawalProof(sixteenthWdLeaf)!.Value.ToArray());
            var sixteenthOutboundDraft = new CrossChainMessage
            {
                SourceChainId = 20260716u,
                TargetChainId = 0,
                Nonce = 32,
                Sender = sixteenthSender,
                Receiver = sixteenthSender,
                MessageType = MessageType.Event,
                Payload = new byte[] { 0x10 },
                MessageHash = UInt256.Zero,
            };
            var sixteenthOutbound = sixteenthOutboundDraft with
            {
                MessageHash = MessageHasher.HashMessage(sixteenthOutboundDraft),
            };
            host.EnqueueOutboundMessagesAsync([sixteenthOutbound]).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(16, host.MessageOutbox!.L2ToL1Count);
            var sixteenthMsgProofBytes = sixteenthOutbound.MessageHash.GetSpan().ToArray();
            host.RecordRpcMessageProof(sixteenthOutbound.MessageHash, sixteenthMsgProofBytes);
            CollectionAssert.AreEqual(
                sixteenthMsgProofBytes,
                host.GetRpcMessageProof(sixteenthOutbound.MessageHash)!.Value.ToArray());
            host.RecordMessageRouterFinalizedProof(sixteenthOutbound.MessageHash, sixteenthMsgProofBytes);
            CollectionAssert.AreEqual(
                sixteenthMsgProofBytes,
                host.GetMessageRouterProofAsync(sixteenthOutbound.MessageHash).AsTask().GetAwaiter().GetResult()!.Value.ToArray());
            Assert.IsTrue(host.RegisterForcedInclusionNonce(34));
            Assert.IsFalse(host.RegisterForcedInclusionNonce(34));
            Assert.AreEqual(16, host.KnownForcedInclusionNonceCount);
            Assert.IsTrue(host.RegisterInboundMessageNonce(34));
            Assert.IsFalse(host.RegisterInboundMessageNonce(34));
            Assert.AreEqual(16, host.KnownInboundNonceCount);
            Assert.AreEqual(0, host.OpenBatchForcedInclusionCount);
            Assert.AreEqual(0, host.OpenBatchL1MessageCount);
            var afterSixteenthOutbound = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(16, afterSixteenthOutbound.ConsumedDepositCount);
            Assert.AreEqual(16, afterSixteenthOutbound.MessageOutboxL2ToL1Count);
            Assert.AreEqual(16, afterSixteenthOutbound.KnownForcedInclusionNonceCount);
            Assert.AreEqual(16, afterSixteenthOutbound.KnownInboundNonceCount);
            Assert.IsTrue(afterSixteenthOutbound.IsSettlementRetrying);
            Assert.IsTrue(afterSixteenthOutbound.PendingSettlementCount >= 2);
            Assert.AreEqual(2UL, afterSixteenthOutbound.LatestCheckpointBatchNumber);
            var sixteenthOutboundPath = Path.Combine(chainDir, "soft-seal-after-fifteenth-recover-sixteenth-outbound.json");
            File.WriteAllText(sixteenthOutboundPath, $$"""
                {
                  "consumedDepositCount": {{afterSixteenthOutbound.ConsumedDepositCount}},
                  "messageOutboxL2ToL1Count": {{afterSixteenthOutbound.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterSixteenthOutbound.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterSixteenthOutbound.KnownInboundNonceCount}},
                  "withdrawalLeaf": "{{sixteenthWdLeaf}}",
                  "outboundMessageHash": "{{sixteenthOutbound.MessageHash}}",
                  "withdrawalProofBytes": {{sixteenthWdProofBytes.Length}},
                  "messageProofBytes": {{sixteenthMsgProofBytes.Length}},
                  "latestCheckpointBatchNumber": {{afterSixteenthOutbound.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterSixteenthOutbound.PendingSettlementCount}},
                  "isSettlementRetrying": true
                }
                """);
            Assert.IsTrue(File.Exists(sixteenthOutboundPath));
            StringAssert.Contains(File.ReadAllText(sixteenthOutboundPath), "\"messageOutboxL2ToL1Count\": 16");
            StringAssert.Contains(File.ReadAllText(sixteenthOutboundPath), "\"knownForcedInclusionNonceCount\": 16");
            StringAssert.Contains(File.ReadAllText(sixteenthOutboundPath), "\"consumedDepositCount\": 16");
            var sixteenthOutboundRpcPath = Path.Combine(chainDir, "soft-seal-after-fifteenth-recover-sixteenth-outbound-rpc.json");
            File.WriteAllText(sixteenthOutboundRpcPath, $$"""
                {
                  "withdrawalLeaf": "{{sixteenthWdLeaf}}",
                  "outboundMessageHash": "{{sixteenthOutbound.MessageHash}}",
                  "withdrawalProofBytes": {{sixteenthWdProofBytes.Length}},
                  "messageProofBytes": {{sixteenthMsgProofBytes.Length}},
                  "messageOutboxL2ToL1Count": 16,
                  "knownForcedInclusionNonceCount": 16,
                  "knownInboundNonceCount": 16,
                  "consumedDepositCount": 16,
                  "isSettlementRetrying": true
                }
                """);
            Assert.IsTrue(File.Exists(sixteenthOutboundRpcPath));
            StringAssert.Contains(File.ReadAllText(sixteenthOutboundRpcPath), "\"outboundMessageHash\": \"" + sixteenthOutbound.MessageHash + "\"");

            // Sixteenth poison→recover after sexdecuple soft multi-batch path retains soft state.
            LocalHostOperatorStatus afterSixteenthPoison = afterSixteenthOutbound;
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
                afterSixteenthPoison = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
                if (afterSixteenthPoison.IsSettlementPoisoned)
                    break;
            }

            Assert.IsTrue(afterSixteenthPoison.IsSettlementPoisoned);
            Assert.IsFalse(afterSixteenthPoison.IsSettlementRetrying);
            Assert.IsTrue(afterSixteenthPoison.PendingSettlementCount >= 2);
            Assert.AreEqual(16, afterSixteenthPoison.ConsumedDepositCount);
            Assert.AreEqual(16, afterSixteenthPoison.MessageOutboxL2ToL1Count);
            Assert.AreEqual(16, afterSixteenthPoison.KnownForcedInclusionNonceCount);
            Assert.AreEqual(16, afterSixteenthPoison.KnownInboundNonceCount);
            Assert.IsNotNull(afterSixteenthPoison.Recovery.BlockedBatchNumber);
            Assert.IsNotNull(afterSixteenthPoison.Recovery.ArtifactContentHash);
            var sixteenthBlocked = afterSixteenthPoison.Recovery.BlockedBatchNumber!.Value;
            var sixteenthHash = afterSixteenthPoison.Recovery.ArtifactContentHash!;
            Assert.ThrowsExactly<InvalidOperationException>(
                () => host.RecoverPoisonedBatchAsync(sixteenthBlocked, UInt256.Zero)
                    .GetAwaiter().GetResult());
            host.RecoverPoisonedBatchAsync(sixteenthBlocked, sixteenthHash).GetAwaiter().GetResult();
            var afterSixteenthRecover = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsFalse(afterSixteenthRecover.IsSettlementPoisoned);
            Assert.IsTrue(afterSixteenthRecover.IsSettlementRetrying);
            Assert.AreEqual(SettlementRecoveryState.Retrying, afterSixteenthRecover.Recovery.State);
            Assert.AreEqual(0, afterSixteenthRecover.Recovery.RetryCount);
            Assert.IsTrue(host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult() >= 2);
            Assert.AreEqual(2UL, afterSixteenthRecover.LatestCheckpointBatchNumber);
            Assert.AreEqual(16, afterSixteenthRecover.ConsumedDepositCount);
            Assert.AreEqual(16, afterSixteenthRecover.MessageOutboxL2ToL1Count);
            Assert.AreEqual(16, afterSixteenthRecover.KnownForcedInclusionNonceCount);
            Assert.AreEqual(16, afterSixteenthRecover.KnownInboundNonceCount);
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(1));
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(2));
            Assert.IsTrue(host.GetRpcWithdrawalProof(sixteenthWdLeaf) is { Length: > 0 });
            Assert.IsTrue(host.GetRpcMessageProof(sixteenthOutbound.MessageHash) is { Length: > 0 });
            Assert.IsTrue(afterSixteenthRecover.IsOfflinePassportComplete);
            Assert.IsTrue(afterSixteenthRecover.IsOperatorReady);
            var sixteenthPoisonPath = Path.Combine(chainDir, "soft-seal-sixteenth-poison-recover.json");
            File.WriteAllText(sixteenthPoisonPath, $$"""
                {
                  "sixteenthPoisonBlockedBatch": {{sixteenthBlocked}},
                  "pendingSettlementCount": {{afterSixteenthRecover.PendingSettlementCount}},
                  "latestCheckpointBatchNumber": {{afterSixteenthRecover.LatestCheckpointBatchNumber}},
                  "consumedDepositCount": {{afterSixteenthRecover.ConsumedDepositCount}},
                  "messageOutboxL2ToL1Count": {{afterSixteenthRecover.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterSixteenthRecover.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterSixteenthRecover.KnownInboundNonceCount}},
                  "rpcBatch1Status": "{{host.GetRpcBatchStatus(1)}}",
                  "rpcBatch2Status": "{{host.GetRpcBatchStatus(2)}}",
                  "sixteenthWithdrawalProofPresent": true,
                  "sixteenthMessageProofPresent": true,
                  "isSettlementRetrying": true,
                  "isSettlementPoisoned": false
                }
                """);
            Assert.IsTrue(File.Exists(sixteenthPoisonPath));
            StringAssert.Contains(File.ReadAllText(sixteenthPoisonPath), "\"rpcBatch2Status\": \"Finalized\"");
            StringAssert.Contains(File.ReadAllText(sixteenthPoisonPath), "\"consumedDepositCount\": 16");
            StringAssert.Contains(File.ReadAllText(sixteenthPoisonPath), "\"messageOutboxL2ToL1Count\": 16");
            StringAssert.Contains(File.ReadAllText(sixteenthPoisonPath), "\"isSettlementRetrying\": true");

            // After sixteenth recover: re-publish local DA + seventeenth offline deposit while Retrying.
            Assert.IsTrue(host.SupportsLocalDaReader);
            var sixteenthRecoverDa1Payload = new byte[] { 0xDA, 0xA1, 0x01 };
            var sixteenthRecoverDa1 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 1,
                Payload = sixteenthRecoverDa1Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(DAMode.Local, sixteenthRecoverDa1.Layer);
            Assert.IsTrue(host.IsDaAvailableAsync(sixteenthRecoverDa1).AsTask().GetAwaiter().GetResult());
            var sixteenthRecoverDa1Read = host.CreateLocalDaReader().ReadAsync(sixteenthRecoverDa1).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(sixteenthRecoverDa1Read is { Length: 3 });
            CollectionAssert.AreEqual(sixteenthRecoverDa1Payload, sixteenthRecoverDa1Read!.Value.ToArray());
            var sixteenthRecoverDa2Payload = new byte[] { 0xDA, 0xA2, 0x02 };
            var sixteenthRecoverDa2 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 2,
                Payload = sixteenthRecoverDa2Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(host.IsDaAvailableAsync(sixteenthRecoverDa2).AsTask().GetAwaiter().GetResult());
            var sixteenthRecoverDa2Read = host.CreateLocalDaReader().ReadAsync(sixteenthRecoverDa2).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(sixteenthRecoverDa2Read is { Length: 3 });
            CollectionAssert.AreEqual(sixteenthRecoverDa2Payload, sixteenthRecoverDa2Read!.Value.ToArray());
            var sixteenthRecoverL1Asset = UInt160.Parse("0x" + new string('1', 40));
            var sixteenthRecoverL2Asset = UInt160.Parse("0x" + new string('2', 40));
            var deposit17 = new CrossChainMessage
            {
                SourceChainId = 0,
                TargetChainId = 20260716u,
                Nonce = 17,
                Sender = Account(0x66),
                Receiver = Account(0x55),
                MessageType = MessageType.Deposit,
                Payload = new DepositPayload
                {
                    L1Asset = sixteenthRecoverL1Asset,
                    L2Recipient = Account(0x55),
                    Amount = new BigInteger(17_000),
                }.Encode(),
                MessageHash = UInt256.Zero,
            };
            var mint17 = host.ProcessDeposit(deposit17);
            Assert.AreEqual(sixteenthRecoverL2Asset, mint17.L2Asset);
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
            Assert.IsTrue(host.HasConsumedDeposit(0, 13));
            Assert.IsTrue(host.HasConsumedDeposit(0, 14));
            Assert.IsTrue(host.HasConsumedDeposit(0, 15));
            Assert.IsTrue(host.HasConsumedDeposit(0, 16));
            Assert.IsTrue(host.HasConsumedDeposit(0, 17));
            Assert.AreEqual(17, host.ConsumedDepositCount);
            host.RecordRpcDeposit(new DepositStatus(0, 17, ConsumedOnL2: true, IncludedInBatch: 2));
            Assert.IsTrue(host.GetRpcL1DepositStatus(0, 17) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
            Assert.AreEqual(0, host.ScanSharedBridgeDepositsAsync().AsTask().GetAwaiter().GetResult());
            var afterSeventeenthDeposit = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(17, afterSeventeenthDeposit.ConsumedDepositCount);
            Assert.AreEqual(16, afterSeventeenthDeposit.MessageOutboxL2ToL1Count);
            Assert.AreEqual(16, afterSeventeenthDeposit.KnownForcedInclusionNonceCount);
            Assert.AreEqual(16, afterSeventeenthDeposit.KnownInboundNonceCount);
            Assert.IsTrue(afterSeventeenthDeposit.IsSettlementRetrying);
            Assert.IsTrue(afterSeventeenthDeposit.PendingSettlementCount >= 2);
            Assert.AreEqual(2UL, afterSeventeenthDeposit.LatestCheckpointBatchNumber);
            Assert.IsTrue(afterSeventeenthDeposit.IsOfflinePassportComplete);
            var afterSixteenthRecoverDaPath = Path.Combine(chainDir, "soft-seal-after-sixteenth-recover-da-deposit.json");
            File.WriteAllText(afterSixteenthRecoverDaPath, $$"""
                {
                  "daBatch1Layer": "{{sixteenthRecoverDa1.Layer}}",
                  "daBatch2Layer": "{{sixteenthRecoverDa2.Layer}}",
                  "consumedDepositCount": {{afterSeventeenthDeposit.ConsumedDepositCount}},
                  "deposit17IncludedInBatch": 2,
                  "messageOutboxL2ToL1Count": {{afterSeventeenthDeposit.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterSeventeenthDeposit.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterSeventeenthDeposit.KnownInboundNonceCount}},
                  "latestCheckpointBatchNumber": {{afterSeventeenthDeposit.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterSeventeenthDeposit.PendingSettlementCount}},
                  "isSettlementRetrying": true,
                  "isOfflinePassportComplete": true
                }
                """);
            Assert.IsTrue(File.Exists(afterSixteenthRecoverDaPath));
            StringAssert.Contains(File.ReadAllText(afterSixteenthRecoverDaPath), "\"consumedDepositCount\": 17");
            StringAssert.Contains(File.ReadAllText(afterSixteenthRecoverDaPath), "\"deposit17IncludedInBatch\": 2");
            StringAssert.Contains(File.ReadAllText(afterSixteenthRecoverDaPath), "\"daBatch2Layer\": \"Local\"");
            var afterSixteenthRecoverProm = host.ExportPrometheusMetrics();
            Assert.IsFalse(string.IsNullOrWhiteSpace(afterSixteenthRecoverProm));
            var afterSixteenthRecoverPromPath = Path.Combine(chainDir, "soft-seal-after-sixteenth-recover-host.prom");
            host.WritePrometheusMetricsAsync(afterSixteenthRecoverPromPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(afterSixteenthRecoverPromPath));
            Assert.AreEqual(afterSixteenthRecoverProm, File.ReadAllText(afterSixteenthRecoverPromPath));

            // Seventeenth offline withdrawal/outbox + FI/inbound after sixteenth recover (no L1).
            var seventeenthSender = Account(0xcc);
            var seventeenthL2Asset = UInt160.Parse("0x" + new string('2', 40));
            var seventeenthWdLeaf = host.StageWithdrawal(new WithdrawalRequest
            {
                ChainId = 20260716u,
                EmittingContract = seventeenthSender,
                L2Sender = seventeenthSender,
                L1Recipient = seventeenthSender,
                L2Asset = seventeenthL2Asset,
                Amount = new BigInteger(100),
                Nonce = 17,
            });
            Assert.AreNotEqual(UInt256.Zero, seventeenthWdLeaf);
            var seventeenthSealedWd = host.SealWithdrawalBatch();
            Assert.AreNotEqual(UInt256.Zero, seventeenthSealedWd.Root);
            Assert.AreEqual(0, host.StagedWithdrawalCount);
            Assert.IsTrue(seventeenthSealedWd.Tree.Count >= 1);
            var seventeenthMerkle = seventeenthSealedWd.Tree.GetProof(seventeenthSealedWd.Tree.Count - 1);
            Assert.AreEqual(seventeenthWdLeaf, seventeenthMerkle.Leaf);
            var seventeenthWdProofBytes = MerkleProofSerializer.Encode(seventeenthMerkle);
            Assert.IsTrue(seventeenthWdProofBytes.Length >= MerkleProofSerializer.HeaderSize);
            host.RecordRpcWithdrawalProof(seventeenthWdLeaf, seventeenthWdProofBytes);
            CollectionAssert.AreEqual(
                seventeenthWdProofBytes,
                host.GetRpcWithdrawalProof(seventeenthWdLeaf)!.Value.ToArray());
            var seventeenthOutboundDraft = new CrossChainMessage
            {
                SourceChainId = 20260716u,
                TargetChainId = 0,
                Nonce = 34,
                Sender = seventeenthSender,
                Receiver = seventeenthSender,
                MessageType = MessageType.Event,
                Payload = new byte[] { 0x11 },
                MessageHash = UInt256.Zero,
            };
            var seventeenthOutbound = seventeenthOutboundDraft with
            {
                MessageHash = MessageHasher.HashMessage(seventeenthOutboundDraft),
            };
            host.EnqueueOutboundMessagesAsync([seventeenthOutbound]).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(17, host.MessageOutbox!.L2ToL1Count);
            var seventeenthMsgProofBytes = seventeenthOutbound.MessageHash.GetSpan().ToArray();
            host.RecordRpcMessageProof(seventeenthOutbound.MessageHash, seventeenthMsgProofBytes);
            CollectionAssert.AreEqual(
                seventeenthMsgProofBytes,
                host.GetRpcMessageProof(seventeenthOutbound.MessageHash)!.Value.ToArray());
            host.RecordMessageRouterFinalizedProof(seventeenthOutbound.MessageHash, seventeenthMsgProofBytes);
            CollectionAssert.AreEqual(
                seventeenthMsgProofBytes,
                host.GetMessageRouterProofAsync(seventeenthOutbound.MessageHash).AsTask().GetAwaiter().GetResult()!.Value.ToArray());
            Assert.IsTrue(host.RegisterForcedInclusionNonce(36));
            Assert.IsFalse(host.RegisterForcedInclusionNonce(36));
            Assert.AreEqual(17, host.KnownForcedInclusionNonceCount);
            Assert.IsTrue(host.RegisterInboundMessageNonce(36));
            Assert.IsFalse(host.RegisterInboundMessageNonce(36));
            Assert.AreEqual(17, host.KnownInboundNonceCount);
            Assert.AreEqual(0, host.OpenBatchForcedInclusionCount);
            Assert.AreEqual(0, host.OpenBatchL1MessageCount);
            var afterSeventeenthOutbound = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(17, afterSeventeenthOutbound.ConsumedDepositCount);
            Assert.AreEqual(17, afterSeventeenthOutbound.MessageOutboxL2ToL1Count);
            Assert.AreEqual(17, afterSeventeenthOutbound.KnownForcedInclusionNonceCount);
            Assert.AreEqual(17, afterSeventeenthOutbound.KnownInboundNonceCount);
            Assert.IsTrue(afterSeventeenthOutbound.IsSettlementRetrying);
            Assert.IsTrue(afterSeventeenthOutbound.PendingSettlementCount >= 2);
            Assert.AreEqual(2UL, afterSeventeenthOutbound.LatestCheckpointBatchNumber);
            var seventeenthOutboundPath = Path.Combine(chainDir, "soft-seal-after-sixteenth-recover-seventeenth-outbound.json");
            File.WriteAllText(seventeenthOutboundPath, $$"""
                {
                  "consumedDepositCount": {{afterSeventeenthOutbound.ConsumedDepositCount}},
                  "messageOutboxL2ToL1Count": {{afterSeventeenthOutbound.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterSeventeenthOutbound.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterSeventeenthOutbound.KnownInboundNonceCount}},
                  "withdrawalLeaf": "{{seventeenthWdLeaf}}",
                  "outboundMessageHash": "{{seventeenthOutbound.MessageHash}}",
                  "withdrawalProofBytes": {{seventeenthWdProofBytes.Length}},
                  "messageProofBytes": {{seventeenthMsgProofBytes.Length}},
                  "latestCheckpointBatchNumber": {{afterSeventeenthOutbound.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterSeventeenthOutbound.PendingSettlementCount}},
                  "isSettlementRetrying": true
                }
                """);
            Assert.IsTrue(File.Exists(seventeenthOutboundPath));
            StringAssert.Contains(File.ReadAllText(seventeenthOutboundPath), "\"messageOutboxL2ToL1Count\": 17");
            StringAssert.Contains(File.ReadAllText(seventeenthOutboundPath), "\"knownForcedInclusionNonceCount\": 17");
            StringAssert.Contains(File.ReadAllText(seventeenthOutboundPath), "\"consumedDepositCount\": 17");
            var seventeenthOutboundRpcPath = Path.Combine(chainDir, "soft-seal-after-sixteenth-recover-seventeenth-outbound-rpc.json");
            File.WriteAllText(seventeenthOutboundRpcPath, $$"""
                {
                  "withdrawalLeaf": "{{seventeenthWdLeaf}}",
                  "outboundMessageHash": "{{seventeenthOutbound.MessageHash}}",
                  "withdrawalProofBytes": {{seventeenthWdProofBytes.Length}},
                  "messageProofBytes": {{seventeenthMsgProofBytes.Length}},
                  "messageOutboxL2ToL1Count": 17,
                  "knownForcedInclusionNonceCount": 17,
                  "knownInboundNonceCount": 17,
                  "consumedDepositCount": 17,
                  "isSettlementRetrying": true
                }
                """);
            Assert.IsTrue(File.Exists(seventeenthOutboundRpcPath));
            StringAssert.Contains(File.ReadAllText(seventeenthOutboundRpcPath), "\"outboundMessageHash\": \"" + seventeenthOutbound.MessageHash + "\"");

            // Seventeenth poison→recover after septendecuple soft multi-batch path retains soft state.
            LocalHostOperatorStatus afterSeventeenthPoison = afterSeventeenthOutbound;
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
                afterSeventeenthPoison = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
                if (afterSeventeenthPoison.IsSettlementPoisoned)
                    break;
            }

            Assert.IsTrue(afterSeventeenthPoison.IsSettlementPoisoned);
            Assert.IsFalse(afterSeventeenthPoison.IsSettlementRetrying);
            Assert.IsTrue(afterSeventeenthPoison.PendingSettlementCount >= 2);
            Assert.AreEqual(17, afterSeventeenthPoison.ConsumedDepositCount);
            Assert.AreEqual(17, afterSeventeenthPoison.MessageOutboxL2ToL1Count);
            Assert.AreEqual(17, afterSeventeenthPoison.KnownForcedInclusionNonceCount);
            Assert.AreEqual(17, afterSeventeenthPoison.KnownInboundNonceCount);
            Assert.IsNotNull(afterSeventeenthPoison.Recovery.BlockedBatchNumber);
            Assert.IsNotNull(afterSeventeenthPoison.Recovery.ArtifactContentHash);
            var seventeenthBlocked = afterSeventeenthPoison.Recovery.BlockedBatchNumber!.Value;
            var seventeenthHash = afterSeventeenthPoison.Recovery.ArtifactContentHash!;
            Assert.ThrowsExactly<InvalidOperationException>(
                () => host.RecoverPoisonedBatchAsync(seventeenthBlocked, UInt256.Zero)
                    .GetAwaiter().GetResult());
            host.RecoverPoisonedBatchAsync(seventeenthBlocked, seventeenthHash).GetAwaiter().GetResult();
            var afterSeventeenthRecover = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsFalse(afterSeventeenthRecover.IsSettlementPoisoned);
            Assert.IsTrue(afterSeventeenthRecover.IsSettlementRetrying);
            Assert.AreEqual(SettlementRecoveryState.Retrying, afterSeventeenthRecover.Recovery.State);
            Assert.AreEqual(0, afterSeventeenthRecover.Recovery.RetryCount);
            Assert.IsTrue(host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult() >= 2);
            Assert.AreEqual(2UL, afterSeventeenthRecover.LatestCheckpointBatchNumber);
            Assert.AreEqual(17, afterSeventeenthRecover.ConsumedDepositCount);
            Assert.AreEqual(17, afterSeventeenthRecover.MessageOutboxL2ToL1Count);
            Assert.AreEqual(17, afterSeventeenthRecover.KnownForcedInclusionNonceCount);
            Assert.AreEqual(17, afterSeventeenthRecover.KnownInboundNonceCount);
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(1));
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(2));
            Assert.IsTrue(host.GetRpcWithdrawalProof(seventeenthWdLeaf) is { Length: > 0 });
            Assert.IsTrue(host.GetRpcMessageProof(seventeenthOutbound.MessageHash) is { Length: > 0 });
            Assert.IsTrue(afterSeventeenthRecover.IsOfflinePassportComplete);
            Assert.IsTrue(afterSeventeenthRecover.IsOperatorReady);
            var seventeenthPoisonPath = Path.Combine(chainDir, "soft-seal-seventeenth-poison-recover.json");
            File.WriteAllText(seventeenthPoisonPath, $$"""
                {
                  "seventeenthPoisonBlockedBatch": {{seventeenthBlocked}},
                  "pendingSettlementCount": {{afterSeventeenthRecover.PendingSettlementCount}},
                  "latestCheckpointBatchNumber": {{afterSeventeenthRecover.LatestCheckpointBatchNumber}},
                  "consumedDepositCount": {{afterSeventeenthRecover.ConsumedDepositCount}},
                  "messageOutboxL2ToL1Count": {{afterSeventeenthRecover.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterSeventeenthRecover.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterSeventeenthRecover.KnownInboundNonceCount}},
                  "rpcBatch1Status": "{{host.GetRpcBatchStatus(1)}}",
                  "rpcBatch2Status": "{{host.GetRpcBatchStatus(2)}}",
                  "seventeenthWithdrawalProofPresent": true,
                  "seventeenthMessageProofPresent": true,
                  "isSettlementRetrying": true,
                  "isSettlementPoisoned": false
                }
                """);
            Assert.IsTrue(File.Exists(seventeenthPoisonPath));
            StringAssert.Contains(File.ReadAllText(seventeenthPoisonPath), "\"rpcBatch2Status\": \"Finalized\"");
            StringAssert.Contains(File.ReadAllText(seventeenthPoisonPath), "\"consumedDepositCount\": 17");
            StringAssert.Contains(File.ReadAllText(seventeenthPoisonPath), "\"messageOutboxL2ToL1Count\": 17");
            StringAssert.Contains(File.ReadAllText(seventeenthPoisonPath), "\"isSettlementRetrying\": true");

            // After seventeenth recover: re-publish local DA + eighteenth offline deposit while Retrying.
            Assert.IsTrue(host.SupportsLocalDaReader);
            var seventeenthRecoverDa1Payload = new byte[] { 0xDA, 0xA1, 0x01 };
            var seventeenthRecoverDa1 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 1,
                Payload = seventeenthRecoverDa1Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(DAMode.Local, seventeenthRecoverDa1.Layer);
            Assert.IsTrue(host.IsDaAvailableAsync(seventeenthRecoverDa1).AsTask().GetAwaiter().GetResult());
            var seventeenthRecoverDa1Read = host.CreateLocalDaReader().ReadAsync(seventeenthRecoverDa1).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(seventeenthRecoverDa1Read is { Length: 3 });
            CollectionAssert.AreEqual(seventeenthRecoverDa1Payload, seventeenthRecoverDa1Read!.Value.ToArray());
            var seventeenthRecoverDa2Payload = new byte[] { 0xDA, 0xA2, 0x02 };
            var seventeenthRecoverDa2 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 2,
                Payload = seventeenthRecoverDa2Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(host.IsDaAvailableAsync(seventeenthRecoverDa2).AsTask().GetAwaiter().GetResult());
            var seventeenthRecoverDa2Read = host.CreateLocalDaReader().ReadAsync(seventeenthRecoverDa2).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(seventeenthRecoverDa2Read is { Length: 3 });
            CollectionAssert.AreEqual(seventeenthRecoverDa2Payload, seventeenthRecoverDa2Read!.Value.ToArray());
            var seventeenthRecoverL1Asset = UInt160.Parse("0x" + new string('1', 40));
            var seventeenthRecoverL2Asset = UInt160.Parse("0x" + new string('2', 40));
            var deposit18 = new CrossChainMessage
            {
                SourceChainId = 0,
                TargetChainId = 20260716u,
                Nonce = 18,
                Sender = Account(0x66),
                Receiver = Account(0x55),
                MessageType = MessageType.Deposit,
                Payload = new DepositPayload
                {
                    L1Asset = seventeenthRecoverL1Asset,
                    L2Recipient = Account(0x55),
                    Amount = new BigInteger(18_000),
                }.Encode(),
                MessageHash = UInt256.Zero,
            };
            var mint18 = host.ProcessDeposit(deposit18);
            Assert.AreEqual(seventeenthRecoverL2Asset, mint18.L2Asset);
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
            Assert.IsTrue(host.HasConsumedDeposit(0, 13));
            Assert.IsTrue(host.HasConsumedDeposit(0, 14));
            Assert.IsTrue(host.HasConsumedDeposit(0, 15));
            Assert.IsTrue(host.HasConsumedDeposit(0, 16));
            Assert.IsTrue(host.HasConsumedDeposit(0, 17));
            Assert.IsTrue(host.HasConsumedDeposit(0, 18));
            Assert.AreEqual(18, host.ConsumedDepositCount);
            host.RecordRpcDeposit(new DepositStatus(0, 18, ConsumedOnL2: true, IncludedInBatch: 2));
            Assert.IsTrue(host.GetRpcL1DepositStatus(0, 18) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
            Assert.AreEqual(0, host.ScanSharedBridgeDepositsAsync().AsTask().GetAwaiter().GetResult());
            var afterEighteenthDeposit = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(18, afterEighteenthDeposit.ConsumedDepositCount);
            Assert.AreEqual(17, afterEighteenthDeposit.MessageOutboxL2ToL1Count);
            Assert.AreEqual(17, afterEighteenthDeposit.KnownForcedInclusionNonceCount);
            Assert.AreEqual(17, afterEighteenthDeposit.KnownInboundNonceCount);
            Assert.IsTrue(afterEighteenthDeposit.IsSettlementRetrying);
            Assert.IsTrue(afterEighteenthDeposit.PendingSettlementCount >= 2);
            Assert.AreEqual(2UL, afterEighteenthDeposit.LatestCheckpointBatchNumber);
            Assert.IsTrue(afterEighteenthDeposit.IsOfflinePassportComplete);
            var afterSeventeenthRecoverDaPath = Path.Combine(chainDir, "soft-seal-after-seventeenth-recover-da-deposit.json");
            File.WriteAllText(afterSeventeenthRecoverDaPath, $$"""
                {
                  "daBatch1Layer": "{{seventeenthRecoverDa1.Layer}}",
                  "daBatch2Layer": "{{seventeenthRecoverDa2.Layer}}",
                  "consumedDepositCount": {{afterEighteenthDeposit.ConsumedDepositCount}},
                  "deposit18IncludedInBatch": 2,
                  "messageOutboxL2ToL1Count": {{afterEighteenthDeposit.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterEighteenthDeposit.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterEighteenthDeposit.KnownInboundNonceCount}},
                  "latestCheckpointBatchNumber": {{afterEighteenthDeposit.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterEighteenthDeposit.PendingSettlementCount}},
                  "isSettlementRetrying": true,
                  "isOfflinePassportComplete": true
                }
                """);
            Assert.IsTrue(File.Exists(afterSeventeenthRecoverDaPath));
            StringAssert.Contains(File.ReadAllText(afterSeventeenthRecoverDaPath), "\"consumedDepositCount\": 18");
            StringAssert.Contains(File.ReadAllText(afterSeventeenthRecoverDaPath), "\"deposit18IncludedInBatch\": 2");
            StringAssert.Contains(File.ReadAllText(afterSeventeenthRecoverDaPath), "\"daBatch2Layer\": \"Local\"");
            var afterSeventeenthRecoverProm = host.ExportPrometheusMetrics();
            Assert.IsFalse(string.IsNullOrWhiteSpace(afterSeventeenthRecoverProm));
            var afterSeventeenthRecoverPromPath = Path.Combine(chainDir, "soft-seal-after-seventeenth-recover-host.prom");
            host.WritePrometheusMetricsAsync(afterSeventeenthRecoverPromPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(afterSeventeenthRecoverPromPath));
            Assert.AreEqual(afterSeventeenthRecoverProm, File.ReadAllText(afterSeventeenthRecoverPromPath));

            // Eighteenth offline withdrawal/outbox + FI/inbound after seventeenth recover (no L1).
            var eighteenthSender = Account(0xcc);
            var eighteenthL2Asset = UInt160.Parse("0x" + new string('2', 40));
            var eighteenthWdLeaf = host.StageWithdrawal(new WithdrawalRequest
            {
                ChainId = 20260716u,
                EmittingContract = eighteenthSender,
                L2Sender = eighteenthSender,
                L1Recipient = eighteenthSender,
                L2Asset = eighteenthL2Asset,
                Amount = new BigInteger(100),
                Nonce = 18,
            });
            Assert.AreNotEqual(UInt256.Zero, eighteenthWdLeaf);
            var eighteenthSealedWd = host.SealWithdrawalBatch();
            Assert.AreNotEqual(UInt256.Zero, eighteenthSealedWd.Root);
            Assert.AreEqual(0, host.StagedWithdrawalCount);
            Assert.IsTrue(eighteenthSealedWd.Tree.Count >= 1);
            var eighteenthMerkle = eighteenthSealedWd.Tree.GetProof(eighteenthSealedWd.Tree.Count - 1);
            Assert.AreEqual(eighteenthWdLeaf, eighteenthMerkle.Leaf);
            var eighteenthWdProofBytes = MerkleProofSerializer.Encode(eighteenthMerkle);
            Assert.IsTrue(eighteenthWdProofBytes.Length >= MerkleProofSerializer.HeaderSize);
            host.RecordRpcWithdrawalProof(eighteenthWdLeaf, eighteenthWdProofBytes);
            CollectionAssert.AreEqual(
                eighteenthWdProofBytes,
                host.GetRpcWithdrawalProof(eighteenthWdLeaf)!.Value.ToArray());
            var eighteenthOutboundDraft = new CrossChainMessage
            {
                SourceChainId = 20260716u,
                TargetChainId = 0,
                Nonce = 36,
                Sender = eighteenthSender,
                Receiver = eighteenthSender,
                MessageType = MessageType.Event,
                Payload = new byte[] { 0x12 },
                MessageHash = UInt256.Zero,
            };
            var eighteenthOutbound = eighteenthOutboundDraft with
            {
                MessageHash = MessageHasher.HashMessage(eighteenthOutboundDraft),
            };
            host.EnqueueOutboundMessagesAsync([eighteenthOutbound]).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(18, host.MessageOutbox!.L2ToL1Count);
            var eighteenthMsgProofBytes = eighteenthOutbound.MessageHash.GetSpan().ToArray();
            host.RecordRpcMessageProof(eighteenthOutbound.MessageHash, eighteenthMsgProofBytes);
            CollectionAssert.AreEqual(
                eighteenthMsgProofBytes,
                host.GetRpcMessageProof(eighteenthOutbound.MessageHash)!.Value.ToArray());
            host.RecordMessageRouterFinalizedProof(eighteenthOutbound.MessageHash, eighteenthMsgProofBytes);
            CollectionAssert.AreEqual(
                eighteenthMsgProofBytes,
                host.GetMessageRouterProofAsync(eighteenthOutbound.MessageHash).AsTask().GetAwaiter().GetResult()!.Value.ToArray());
            Assert.IsTrue(host.RegisterForcedInclusionNonce(38));
            Assert.IsFalse(host.RegisterForcedInclusionNonce(38));
            Assert.AreEqual(18, host.KnownForcedInclusionNonceCount);
            Assert.IsTrue(host.RegisterInboundMessageNonce(38));
            Assert.IsFalse(host.RegisterInboundMessageNonce(38));
            Assert.AreEqual(18, host.KnownInboundNonceCount);
            Assert.AreEqual(0, host.OpenBatchForcedInclusionCount);
            Assert.AreEqual(0, host.OpenBatchL1MessageCount);
            var afterEighteenthOutbound = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(18, afterEighteenthOutbound.ConsumedDepositCount);
            Assert.AreEqual(18, afterEighteenthOutbound.MessageOutboxL2ToL1Count);
            Assert.AreEqual(18, afterEighteenthOutbound.KnownForcedInclusionNonceCount);
            Assert.AreEqual(18, afterEighteenthOutbound.KnownInboundNonceCount);
            Assert.IsTrue(afterEighteenthOutbound.IsSettlementRetrying);
            Assert.IsTrue(afterEighteenthOutbound.PendingSettlementCount >= 2);
            Assert.AreEqual(2UL, afterEighteenthOutbound.LatestCheckpointBatchNumber);
            var eighteenthOutboundPath = Path.Combine(chainDir, "soft-seal-after-seventeenth-recover-eighteenth-outbound.json");
            File.WriteAllText(eighteenthOutboundPath, $$"""
                {
                  "consumedDepositCount": {{afterEighteenthOutbound.ConsumedDepositCount}},
                  "messageOutboxL2ToL1Count": {{afterEighteenthOutbound.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterEighteenthOutbound.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterEighteenthOutbound.KnownInboundNonceCount}},
                  "withdrawalLeaf": "{{eighteenthWdLeaf}}",
                  "outboundMessageHash": "{{eighteenthOutbound.MessageHash}}",
                  "withdrawalProofBytes": {{eighteenthWdProofBytes.Length}},
                  "messageProofBytes": {{eighteenthMsgProofBytes.Length}},
                  "latestCheckpointBatchNumber": {{afterEighteenthOutbound.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterEighteenthOutbound.PendingSettlementCount}},
                  "isSettlementRetrying": true
                }
                """);
            Assert.IsTrue(File.Exists(eighteenthOutboundPath));
            StringAssert.Contains(File.ReadAllText(eighteenthOutboundPath), "\"messageOutboxL2ToL1Count\": 18");
            StringAssert.Contains(File.ReadAllText(eighteenthOutboundPath), "\"knownForcedInclusionNonceCount\": 18");
            StringAssert.Contains(File.ReadAllText(eighteenthOutboundPath), "\"consumedDepositCount\": 18");
            var eighteenthOutboundRpcPath = Path.Combine(chainDir, "soft-seal-after-seventeenth-recover-eighteenth-outbound-rpc.json");
            File.WriteAllText(eighteenthOutboundRpcPath, $$"""
                {
                  "withdrawalLeaf": "{{eighteenthWdLeaf}}",
                  "outboundMessageHash": "{{eighteenthOutbound.MessageHash}}",
                  "withdrawalProofBytes": {{eighteenthWdProofBytes.Length}},
                  "messageProofBytes": {{eighteenthMsgProofBytes.Length}},
                  "messageOutboxL2ToL1Count": 18,
                  "knownForcedInclusionNonceCount": 18,
                  "knownInboundNonceCount": 18,
                  "consumedDepositCount": 18,
                  "isSettlementRetrying": true
                }
                """);
            Assert.IsTrue(File.Exists(eighteenthOutboundRpcPath));
            StringAssert.Contains(File.ReadAllText(eighteenthOutboundRpcPath), "\"outboundMessageHash\": \"" + eighteenthOutbound.MessageHash + "\"");

            // Eighteenth poison→recover after octodecuple soft multi-batch path retains soft state.
            LocalHostOperatorStatus afterEighteenthPoison = afterEighteenthOutbound;
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
                afterEighteenthPoison = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
                if (afterEighteenthPoison.IsSettlementPoisoned)
                    break;
            }

            Assert.IsTrue(afterEighteenthPoison.IsSettlementPoisoned);
            Assert.IsFalse(afterEighteenthPoison.IsSettlementRetrying);
            Assert.IsTrue(afterEighteenthPoison.PendingSettlementCount >= 2);
            Assert.AreEqual(18, afterEighteenthPoison.ConsumedDepositCount);
            Assert.AreEqual(18, afterEighteenthPoison.MessageOutboxL2ToL1Count);
            Assert.AreEqual(18, afterEighteenthPoison.KnownForcedInclusionNonceCount);
            Assert.AreEqual(18, afterEighteenthPoison.KnownInboundNonceCount);
            Assert.IsNotNull(afterEighteenthPoison.Recovery.BlockedBatchNumber);
            Assert.IsNotNull(afterEighteenthPoison.Recovery.ArtifactContentHash);
            var eighteenthBlocked = afterEighteenthPoison.Recovery.BlockedBatchNumber!.Value;
            var eighteenthHash = afterEighteenthPoison.Recovery.ArtifactContentHash!;
            Assert.ThrowsExactly<InvalidOperationException>(
                () => host.RecoverPoisonedBatchAsync(eighteenthBlocked, UInt256.Zero)
                    .GetAwaiter().GetResult());
            host.RecoverPoisonedBatchAsync(eighteenthBlocked, eighteenthHash).GetAwaiter().GetResult();
            var afterEighteenthRecover = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsFalse(afterEighteenthRecover.IsSettlementPoisoned);
            Assert.IsTrue(afterEighteenthRecover.IsSettlementRetrying);
            Assert.AreEqual(SettlementRecoveryState.Retrying, afterEighteenthRecover.Recovery.State);
            Assert.AreEqual(0, afterEighteenthRecover.Recovery.RetryCount);
            Assert.IsTrue(host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult() >= 2);
            Assert.AreEqual(2UL, afterEighteenthRecover.LatestCheckpointBatchNumber);
            Assert.AreEqual(18, afterEighteenthRecover.ConsumedDepositCount);
            Assert.AreEqual(18, afterEighteenthRecover.MessageOutboxL2ToL1Count);
            Assert.AreEqual(18, afterEighteenthRecover.KnownForcedInclusionNonceCount);
            Assert.AreEqual(18, afterEighteenthRecover.KnownInboundNonceCount);
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(1));
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(2));
            Assert.IsTrue(host.GetRpcWithdrawalProof(eighteenthWdLeaf) is { Length: > 0 });
            Assert.IsTrue(host.GetRpcMessageProof(eighteenthOutbound.MessageHash) is { Length: > 0 });
            Assert.IsTrue(afterEighteenthRecover.IsOfflinePassportComplete);
            Assert.IsTrue(afterEighteenthRecover.IsOperatorReady);
            var eighteenthPoisonPath = Path.Combine(chainDir, "soft-seal-eighteenth-poison-recover.json");
            File.WriteAllText(eighteenthPoisonPath, $$"""
                {
                  "eighteenthPoisonBlockedBatch": {{eighteenthBlocked}},
                  "pendingSettlementCount": {{afterEighteenthRecover.PendingSettlementCount}},
                  "latestCheckpointBatchNumber": {{afterEighteenthRecover.LatestCheckpointBatchNumber}},
                  "consumedDepositCount": {{afterEighteenthRecover.ConsumedDepositCount}},
                  "messageOutboxL2ToL1Count": {{afterEighteenthRecover.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterEighteenthRecover.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterEighteenthRecover.KnownInboundNonceCount}},
                  "rpcBatch1Status": "{{host.GetRpcBatchStatus(1)}}",
                  "rpcBatch2Status": "{{host.GetRpcBatchStatus(2)}}",
                  "eighteenthWithdrawalProofPresent": true,
                  "eighteenthMessageProofPresent": true,
                  "isSettlementRetrying": true,
                  "isSettlementPoisoned": false
                }
                """);
            Assert.IsTrue(File.Exists(eighteenthPoisonPath));
            StringAssert.Contains(File.ReadAllText(eighteenthPoisonPath), "\"rpcBatch2Status\": \"Finalized\"");
            StringAssert.Contains(File.ReadAllText(eighteenthPoisonPath), "\"consumedDepositCount\": 18");
            StringAssert.Contains(File.ReadAllText(eighteenthPoisonPath), "\"messageOutboxL2ToL1Count\": 18");
            StringAssert.Contains(File.ReadAllText(eighteenthPoisonPath), "\"isSettlementRetrying\": true");

            // After eighteenth recover: re-publish local DA + nineteenth offline deposit while Retrying.
            Assert.IsTrue(host.SupportsLocalDaReader);
            var eighteenthRecoverDa1Payload = new byte[] { 0xDA, 0xA1, 0x01 };
            var eighteenthRecoverDa1 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 1,
                Payload = eighteenthRecoverDa1Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(DAMode.Local, eighteenthRecoverDa1.Layer);
            Assert.IsTrue(host.IsDaAvailableAsync(eighteenthRecoverDa1).AsTask().GetAwaiter().GetResult());
            var eighteenthRecoverDa1Read = host.CreateLocalDaReader().ReadAsync(eighteenthRecoverDa1).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(eighteenthRecoverDa1Read is { Length: 3 });
            CollectionAssert.AreEqual(eighteenthRecoverDa1Payload, eighteenthRecoverDa1Read!.Value.ToArray());
            var eighteenthRecoverDa2Payload = new byte[] { 0xDA, 0xA2, 0x02 };
            var eighteenthRecoverDa2 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 2,
                Payload = eighteenthRecoverDa2Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(host.IsDaAvailableAsync(eighteenthRecoverDa2).AsTask().GetAwaiter().GetResult());
            var eighteenthRecoverDa2Read = host.CreateLocalDaReader().ReadAsync(eighteenthRecoverDa2).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(eighteenthRecoverDa2Read is { Length: 3 });
            CollectionAssert.AreEqual(eighteenthRecoverDa2Payload, eighteenthRecoverDa2Read!.Value.ToArray());
            var eighteenthRecoverL1Asset = UInt160.Parse("0x" + new string('1', 40));
            var eighteenthRecoverL2Asset = UInt160.Parse("0x" + new string('2', 40));
            var deposit19 = new CrossChainMessage
            {
                SourceChainId = 0,
                TargetChainId = 20260716u,
                Nonce = 19,
                Sender = Account(0x66),
                Receiver = Account(0x55),
                MessageType = MessageType.Deposit,
                Payload = new DepositPayload
                {
                    L1Asset = eighteenthRecoverL1Asset,
                    L2Recipient = Account(0x55),
                    Amount = new BigInteger(19_000),
                }.Encode(),
                MessageHash = UInt256.Zero,
            };
            var mint19 = host.ProcessDeposit(deposit19);
            Assert.AreEqual(eighteenthRecoverL2Asset, mint19.L2Asset);
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
            Assert.IsTrue(host.HasConsumedDeposit(0, 13));
            Assert.IsTrue(host.HasConsumedDeposit(0, 14));
            Assert.IsTrue(host.HasConsumedDeposit(0, 15));
            Assert.IsTrue(host.HasConsumedDeposit(0, 16));
            Assert.IsTrue(host.HasConsumedDeposit(0, 17));
            Assert.IsTrue(host.HasConsumedDeposit(0, 18));
            Assert.IsTrue(host.HasConsumedDeposit(0, 19));
            Assert.AreEqual(19, host.ConsumedDepositCount);
            host.RecordRpcDeposit(new DepositStatus(0, 19, ConsumedOnL2: true, IncludedInBatch: 2));
            Assert.IsTrue(host.GetRpcL1DepositStatus(0, 19) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
            Assert.AreEqual(0, host.ScanSharedBridgeDepositsAsync().AsTask().GetAwaiter().GetResult());
            var afterNineteenthDeposit = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(19, afterNineteenthDeposit.ConsumedDepositCount);
            Assert.AreEqual(18, afterNineteenthDeposit.MessageOutboxL2ToL1Count);
            Assert.AreEqual(18, afterNineteenthDeposit.KnownForcedInclusionNonceCount);
            Assert.AreEqual(18, afterNineteenthDeposit.KnownInboundNonceCount);
            Assert.IsTrue(afterNineteenthDeposit.IsSettlementRetrying);
            Assert.IsTrue(afterNineteenthDeposit.PendingSettlementCount >= 2);
            Assert.AreEqual(2UL, afterNineteenthDeposit.LatestCheckpointBatchNumber);
            Assert.IsTrue(afterNineteenthDeposit.IsOfflinePassportComplete);
            var afterEighteenthRecoverDaPath = Path.Combine(chainDir, "soft-seal-after-eighteenth-recover-da-deposit.json");
            File.WriteAllText(afterEighteenthRecoverDaPath, $$"""
                {
                  "daBatch1Layer": "{{eighteenthRecoverDa1.Layer}}",
                  "daBatch2Layer": "{{eighteenthRecoverDa2.Layer}}",
                  "consumedDepositCount": {{afterNineteenthDeposit.ConsumedDepositCount}},
                  "deposit19IncludedInBatch": 2,
                  "messageOutboxL2ToL1Count": {{afterNineteenthDeposit.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterNineteenthDeposit.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterNineteenthDeposit.KnownInboundNonceCount}},
                  "latestCheckpointBatchNumber": {{afterNineteenthDeposit.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterNineteenthDeposit.PendingSettlementCount}},
                  "isSettlementRetrying": true,
                  "isOfflinePassportComplete": true
                }
                """);
            Assert.IsTrue(File.Exists(afterEighteenthRecoverDaPath));
            StringAssert.Contains(File.ReadAllText(afterEighteenthRecoverDaPath), "\"consumedDepositCount\": 19");
            StringAssert.Contains(File.ReadAllText(afterEighteenthRecoverDaPath), "\"deposit19IncludedInBatch\": 2");
            StringAssert.Contains(File.ReadAllText(afterEighteenthRecoverDaPath), "\"daBatch2Layer\": \"Local\"");
            var afterEighteenthRecoverProm = host.ExportPrometheusMetrics();
            Assert.IsFalse(string.IsNullOrWhiteSpace(afterEighteenthRecoverProm));
            var afterEighteenthRecoverPromPath = Path.Combine(chainDir, "soft-seal-after-eighteenth-recover-host.prom");
            host.WritePrometheusMetricsAsync(afterEighteenthRecoverPromPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(afterEighteenthRecoverPromPath));
            Assert.AreEqual(afterEighteenthRecoverProm, File.ReadAllText(afterEighteenthRecoverPromPath));

            // Nineteenth offline withdrawal/outbox + FI/inbound after eighteenth recover (no L1).
            var nineteenthSender = Account(0xcc);
            var nineteenthL2Asset = UInt160.Parse("0x" + new string('2', 40));
            var nineteenthWdLeaf = host.StageWithdrawal(new WithdrawalRequest
            {
                ChainId = 20260716u,
                EmittingContract = nineteenthSender,
                L2Sender = nineteenthSender,
                L1Recipient = nineteenthSender,
                L2Asset = nineteenthL2Asset,
                Amount = new BigInteger(100),
                Nonce = 19,
            });
            Assert.AreNotEqual(UInt256.Zero, nineteenthWdLeaf);
            var nineteenthSealedWd = host.SealWithdrawalBatch();
            Assert.AreNotEqual(UInt256.Zero, nineteenthSealedWd.Root);
            Assert.AreEqual(0, host.StagedWithdrawalCount);
            Assert.IsTrue(nineteenthSealedWd.Tree.Count >= 1);
            var nineteenthMerkle = nineteenthSealedWd.Tree.GetProof(nineteenthSealedWd.Tree.Count - 1);
            Assert.AreEqual(nineteenthWdLeaf, nineteenthMerkle.Leaf);
            var nineteenthWdProofBytes = MerkleProofSerializer.Encode(nineteenthMerkle);
            Assert.IsTrue(nineteenthWdProofBytes.Length >= MerkleProofSerializer.HeaderSize);
            host.RecordRpcWithdrawalProof(nineteenthWdLeaf, nineteenthWdProofBytes);
            CollectionAssert.AreEqual(
                nineteenthWdProofBytes,
                host.GetRpcWithdrawalProof(nineteenthWdLeaf)!.Value.ToArray());
            var nineteenthOutboundDraft = new CrossChainMessage
            {
                SourceChainId = 20260716u,
                TargetChainId = 0,
                Nonce = 38,
                Sender = nineteenthSender,
                Receiver = nineteenthSender,
                MessageType = MessageType.Event,
                Payload = new byte[] { 0x13 },
                MessageHash = UInt256.Zero,
            };
            var nineteenthOutbound = nineteenthOutboundDraft with
            {
                MessageHash = MessageHasher.HashMessage(nineteenthOutboundDraft),
            };
            host.EnqueueOutboundMessagesAsync([nineteenthOutbound]).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(19, host.MessageOutbox!.L2ToL1Count);
            var nineteenthMsgProofBytes = nineteenthOutbound.MessageHash.GetSpan().ToArray();
            host.RecordRpcMessageProof(nineteenthOutbound.MessageHash, nineteenthMsgProofBytes);
            CollectionAssert.AreEqual(
                nineteenthMsgProofBytes,
                host.GetRpcMessageProof(nineteenthOutbound.MessageHash)!.Value.ToArray());
            host.RecordMessageRouterFinalizedProof(nineteenthOutbound.MessageHash, nineteenthMsgProofBytes);
            CollectionAssert.AreEqual(
                nineteenthMsgProofBytes,
                host.GetMessageRouterProofAsync(nineteenthOutbound.MessageHash).AsTask().GetAwaiter().GetResult()!.Value.ToArray());
            Assert.IsTrue(host.RegisterForcedInclusionNonce(40));
            Assert.IsFalse(host.RegisterForcedInclusionNonce(40));
            Assert.AreEqual(19, host.KnownForcedInclusionNonceCount);
            Assert.IsTrue(host.RegisterInboundMessageNonce(40));
            Assert.IsFalse(host.RegisterInboundMessageNonce(40));
            Assert.AreEqual(19, host.KnownInboundNonceCount);
            Assert.AreEqual(0, host.OpenBatchForcedInclusionCount);
            Assert.AreEqual(0, host.OpenBatchL1MessageCount);
            var afterNineteenthOutbound = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(19, afterNineteenthOutbound.ConsumedDepositCount);
            Assert.AreEqual(19, afterNineteenthOutbound.MessageOutboxL2ToL1Count);
            Assert.AreEqual(19, afterNineteenthOutbound.KnownForcedInclusionNonceCount);
            Assert.AreEqual(19, afterNineteenthOutbound.KnownInboundNonceCount);
            Assert.IsTrue(afterNineteenthOutbound.IsSettlementRetrying);
            Assert.IsTrue(afterNineteenthOutbound.PendingSettlementCount >= 2);
            Assert.AreEqual(2UL, afterNineteenthOutbound.LatestCheckpointBatchNumber);
            var nineteenthOutboundPath = Path.Combine(chainDir, "soft-seal-after-eighteenth-recover-nineteenth-outbound.json");
            File.WriteAllText(nineteenthOutboundPath, $$"""
                {
                  "consumedDepositCount": {{afterNineteenthOutbound.ConsumedDepositCount}},
                  "messageOutboxL2ToL1Count": {{afterNineteenthOutbound.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterNineteenthOutbound.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterNineteenthOutbound.KnownInboundNonceCount}},
                  "withdrawalLeaf": "{{nineteenthWdLeaf}}",
                  "outboundMessageHash": "{{nineteenthOutbound.MessageHash}}",
                  "withdrawalProofBytes": {{nineteenthWdProofBytes.Length}},
                  "messageProofBytes": {{nineteenthMsgProofBytes.Length}},
                  "latestCheckpointBatchNumber": {{afterNineteenthOutbound.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterNineteenthOutbound.PendingSettlementCount}},
                  "isSettlementRetrying": true
                }
                """);
            Assert.IsTrue(File.Exists(nineteenthOutboundPath));
            StringAssert.Contains(File.ReadAllText(nineteenthOutboundPath), "\"messageOutboxL2ToL1Count\": 19");
            StringAssert.Contains(File.ReadAllText(nineteenthOutboundPath), "\"knownForcedInclusionNonceCount\": 19");
            StringAssert.Contains(File.ReadAllText(nineteenthOutboundPath), "\"consumedDepositCount\": 19");
            var nineteenthOutboundRpcPath = Path.Combine(chainDir, "soft-seal-after-eighteenth-recover-nineteenth-outbound-rpc.json");
            File.WriteAllText(nineteenthOutboundRpcPath, $$"""
                {
                  "withdrawalLeaf": "{{nineteenthWdLeaf}}",
                  "outboundMessageHash": "{{nineteenthOutbound.MessageHash}}",
                  "withdrawalProofBytes": {{nineteenthWdProofBytes.Length}},
                  "messageProofBytes": {{nineteenthMsgProofBytes.Length}},
                  "messageOutboxL2ToL1Count": 19,
                  "knownForcedInclusionNonceCount": 19,
                  "knownInboundNonceCount": 19,
                  "consumedDepositCount": 19,
                  "isSettlementRetrying": true
                }
                """);
            Assert.IsTrue(File.Exists(nineteenthOutboundRpcPath));
            StringAssert.Contains(File.ReadAllText(nineteenthOutboundRpcPath), "\"outboundMessageHash\": \"" + nineteenthOutbound.MessageHash + "\"");

            // Nineteenth poison→recover after novemdecuple soft multi-batch path retains soft state.
            LocalHostOperatorStatus afterNineteenthPoison = afterNineteenthOutbound;
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
                afterNineteenthPoison = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
                if (afterNineteenthPoison.IsSettlementPoisoned)
                    break;
            }

            Assert.IsTrue(afterNineteenthPoison.IsSettlementPoisoned);
            Assert.IsFalse(afterNineteenthPoison.IsSettlementRetrying);
            Assert.IsTrue(afterNineteenthPoison.PendingSettlementCount >= 2);
            Assert.AreEqual(19, afterNineteenthPoison.ConsumedDepositCount);
            Assert.AreEqual(19, afterNineteenthPoison.MessageOutboxL2ToL1Count);
            Assert.AreEqual(19, afterNineteenthPoison.KnownForcedInclusionNonceCount);
            Assert.AreEqual(19, afterNineteenthPoison.KnownInboundNonceCount);
            Assert.IsNotNull(afterNineteenthPoison.Recovery.BlockedBatchNumber);
            Assert.IsNotNull(afterNineteenthPoison.Recovery.ArtifactContentHash);
            var nineteenthBlocked = afterNineteenthPoison.Recovery.BlockedBatchNumber!.Value;
            var nineteenthHash = afterNineteenthPoison.Recovery.ArtifactContentHash!;
            Assert.ThrowsExactly<InvalidOperationException>(
                () => host.RecoverPoisonedBatchAsync(nineteenthBlocked, UInt256.Zero)
                    .GetAwaiter().GetResult());
            host.RecoverPoisonedBatchAsync(nineteenthBlocked, nineteenthHash).GetAwaiter().GetResult();
            var afterNineteenthRecover = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsFalse(afterNineteenthRecover.IsSettlementPoisoned);
            Assert.IsTrue(afterNineteenthRecover.IsSettlementRetrying);
            Assert.AreEqual(SettlementRecoveryState.Retrying, afterNineteenthRecover.Recovery.State);
            Assert.AreEqual(0, afterNineteenthRecover.Recovery.RetryCount);
            Assert.IsTrue(host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult() >= 2);
            Assert.AreEqual(2UL, afterNineteenthRecover.LatestCheckpointBatchNumber);
            Assert.AreEqual(19, afterNineteenthRecover.ConsumedDepositCount);
            Assert.AreEqual(19, afterNineteenthRecover.MessageOutboxL2ToL1Count);
            Assert.AreEqual(19, afterNineteenthRecover.KnownForcedInclusionNonceCount);
            Assert.AreEqual(19, afterNineteenthRecover.KnownInboundNonceCount);
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(1));
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(2));
            Assert.IsTrue(host.GetRpcWithdrawalProof(nineteenthWdLeaf) is { Length: > 0 });
            Assert.IsTrue(host.GetRpcMessageProof(nineteenthOutbound.MessageHash) is { Length: > 0 });
            Assert.IsTrue(afterNineteenthRecover.IsOfflinePassportComplete);
            Assert.IsTrue(afterNineteenthRecover.IsOperatorReady);
            var nineteenthPoisonPath = Path.Combine(chainDir, "soft-seal-nineteenth-poison-recover.json");
            File.WriteAllText(nineteenthPoisonPath, $$"""
                {
                  "nineteenthPoisonBlockedBatch": {{nineteenthBlocked}},
                  "pendingSettlementCount": {{afterNineteenthRecover.PendingSettlementCount}},
                  "latestCheckpointBatchNumber": {{afterNineteenthRecover.LatestCheckpointBatchNumber}},
                  "consumedDepositCount": {{afterNineteenthRecover.ConsumedDepositCount}},
                  "messageOutboxL2ToL1Count": {{afterNineteenthRecover.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterNineteenthRecover.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterNineteenthRecover.KnownInboundNonceCount}},
                  "rpcBatch1Status": "{{host.GetRpcBatchStatus(1)}}",
                  "rpcBatch2Status": "{{host.GetRpcBatchStatus(2)}}",
                  "nineteenthWithdrawalProofPresent": true,
                  "nineteenthMessageProofPresent": true,
                  "isSettlementRetrying": true,
                  "isSettlementPoisoned": false
                }
                """);
            Assert.IsTrue(File.Exists(nineteenthPoisonPath));
            StringAssert.Contains(File.ReadAllText(nineteenthPoisonPath), "\"rpcBatch2Status\": \"Finalized\"");
            StringAssert.Contains(File.ReadAllText(nineteenthPoisonPath), "\"consumedDepositCount\": 19");
            StringAssert.Contains(File.ReadAllText(nineteenthPoisonPath), "\"messageOutboxL2ToL1Count\": 19");
            StringAssert.Contains(File.ReadAllText(nineteenthPoisonPath), "\"isSettlementRetrying\": true");

            // After nineteenth recover: re-publish local DA + twentieth offline deposit while Retrying.
            Assert.IsTrue(host.SupportsLocalDaReader);
            var nineteenthRecoverDa1Payload = new byte[] { 0xDA, 0xA1, 0x01 };
            var nineteenthRecoverDa1 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 1,
                Payload = nineteenthRecoverDa1Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(DAMode.Local, nineteenthRecoverDa1.Layer);
            Assert.IsTrue(host.IsDaAvailableAsync(nineteenthRecoverDa1).AsTask().GetAwaiter().GetResult());
            var nineteenthRecoverDa1Read = host.CreateLocalDaReader().ReadAsync(nineteenthRecoverDa1).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(nineteenthRecoverDa1Read is { Length: 3 });
            CollectionAssert.AreEqual(nineteenthRecoverDa1Payload, nineteenthRecoverDa1Read!.Value.ToArray());
            var nineteenthRecoverDa2Payload = new byte[] { 0xDA, 0xA2, 0x02 };
            var nineteenthRecoverDa2 = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 2,
                Payload = nineteenthRecoverDa2Payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(host.IsDaAvailableAsync(nineteenthRecoverDa2).AsTask().GetAwaiter().GetResult());
            var nineteenthRecoverDa2Read = host.CreateLocalDaReader().ReadAsync(nineteenthRecoverDa2).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(nineteenthRecoverDa2Read is { Length: 3 });
            CollectionAssert.AreEqual(nineteenthRecoverDa2Payload, nineteenthRecoverDa2Read!.Value.ToArray());
            var nineteenthRecoverL1Asset = UInt160.Parse("0x" + new string('1', 40));
            var nineteenthRecoverL2Asset = UInt160.Parse("0x" + new string('2', 40));
            var deposit20 = new CrossChainMessage
            {
                SourceChainId = 0,
                TargetChainId = 20260716u,
                Nonce = 20,
                Sender = Account(0x66),
                Receiver = Account(0x55),
                MessageType = MessageType.Deposit,
                Payload = new DepositPayload
                {
                    L1Asset = nineteenthRecoverL1Asset,
                    L2Recipient = Account(0x55),
                    Amount = new BigInteger(20_000),
                }.Encode(),
                MessageHash = UInt256.Zero,
            };
            var mint20 = host.ProcessDeposit(deposit20);
            Assert.AreEqual(nineteenthRecoverL2Asset, mint20.L2Asset);
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
            Assert.IsTrue(host.HasConsumedDeposit(0, 13));
            Assert.IsTrue(host.HasConsumedDeposit(0, 14));
            Assert.IsTrue(host.HasConsumedDeposit(0, 15));
            Assert.IsTrue(host.HasConsumedDeposit(0, 16));
            Assert.IsTrue(host.HasConsumedDeposit(0, 17));
            Assert.IsTrue(host.HasConsumedDeposit(0, 18));
            Assert.IsTrue(host.HasConsumedDeposit(0, 19));
            Assert.IsTrue(host.HasConsumedDeposit(0, 20));
            Assert.AreEqual(20, host.ConsumedDepositCount);
            host.RecordRpcDeposit(new DepositStatus(0, 20, ConsumedOnL2: true, IncludedInBatch: 2));
            Assert.IsTrue(host.GetRpcL1DepositStatus(0, 20) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
            Assert.AreEqual(0, host.ScanSharedBridgeDepositsAsync().AsTask().GetAwaiter().GetResult());
            var afterTwentiethDeposit = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(20, afterTwentiethDeposit.ConsumedDepositCount);
            Assert.AreEqual(19, afterTwentiethDeposit.MessageOutboxL2ToL1Count);
            Assert.AreEqual(19, afterTwentiethDeposit.KnownForcedInclusionNonceCount);
            Assert.AreEqual(19, afterTwentiethDeposit.KnownInboundNonceCount);
            Assert.IsTrue(afterTwentiethDeposit.IsSettlementRetrying);
            Assert.IsTrue(afterTwentiethDeposit.PendingSettlementCount >= 2);
            Assert.AreEqual(2UL, afterTwentiethDeposit.LatestCheckpointBatchNumber);
            Assert.IsTrue(afterTwentiethDeposit.IsOfflinePassportComplete);
            var afterNineteenthRecoverDaPath = Path.Combine(chainDir, "soft-seal-after-nineteenth-recover-da-deposit.json");
            File.WriteAllText(afterNineteenthRecoverDaPath, $$"""
                {
                  "daBatch1Layer": "{{nineteenthRecoverDa1.Layer}}",
                  "daBatch2Layer": "{{nineteenthRecoverDa2.Layer}}",
                  "consumedDepositCount": {{afterTwentiethDeposit.ConsumedDepositCount}},
                  "deposit20IncludedInBatch": 2,
                  "messageOutboxL2ToL1Count": {{afterTwentiethDeposit.MessageOutboxL2ToL1Count}},
                  "knownForcedInclusionNonceCount": {{afterTwentiethDeposit.KnownForcedInclusionNonceCount}},
                  "knownInboundNonceCount": {{afterTwentiethDeposit.KnownInboundNonceCount}},
                  "latestCheckpointBatchNumber": {{afterTwentiethDeposit.LatestCheckpointBatchNumber}},
                  "pendingSettlementCount": {{afterTwentiethDeposit.PendingSettlementCount}},
                  "isSettlementRetrying": true,
                  "isOfflinePassportComplete": true
                }
                """);
            Assert.IsTrue(File.Exists(afterNineteenthRecoverDaPath));
            StringAssert.Contains(File.ReadAllText(afterNineteenthRecoverDaPath), "\"consumedDepositCount\": 20");
            StringAssert.Contains(File.ReadAllText(afterNineteenthRecoverDaPath), "\"deposit20IncludedInBatch\": 2");
            StringAssert.Contains(File.ReadAllText(afterNineteenthRecoverDaPath), "\"daBatch2Layer\": \"Local\"");
            var afterNineteenthRecoverProm = host.ExportPrometheusMetrics();
            Assert.IsFalse(string.IsNullOrWhiteSpace(afterNineteenthRecoverProm));
            var afterNineteenthRecoverPromPath = Path.Combine(chainDir, "soft-seal-after-nineteenth-recover-host.prom");
            host.WritePrometheusMetricsAsync(afterNineteenthRecoverPromPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(afterNineteenthRecoverPromPath));
            Assert.AreEqual(afterNineteenthRecoverProm, File.ReadAllText(afterNineteenthRecoverPromPath));

            // SubmitNext after nineteenth recover remains best-effort; does not clear pending without funded L1.
            host.SubmitNextAsync().GetAwaiter().GetResult();
            Assert.IsTrue(host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult() >= 2);
            Assert.IsFalse(host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult().IsSettlementIdle);
            Assert.IsTrue(gatewayHost.AggregatorPendingCount >= 4);
        }
        finally
        {
            if (Directory.Exists(chainDir))
                Directory.Delete(chainDir, recursive: true);
        }
    }

    private static void MaterializeOptimisticChain(string chainDir, string reportPath)
    {
        var validatorA = new KeyPair(Enumerable.Range(5, 32).Select(i => (byte)i).ToArray()).PublicKey;
        var hexA = Convert.ToHexString(validatorA.EncodePoint(true)).ToLowerInvariant();
        File.WriteAllText(Path.Combine(chainDir, "chain.config.json"), $$"""
            {
              "chainId": 20260716,
              "proofType": "Optimistic",
              "securityLevel": "Optimistic",
              "daMode": "Local",
              "sequencerModel": "DbftCommittee",
              "exitModel": "Permissionless",
              "gatewayEnabled": true,
              "permissionlessExit": true,
              "validators": [ "{{hexA}}" ]
            }
            """);
        NeoHubDeployReport.Load(reportPath).WriteOperatorArtifacts(chainDir);
        RewriteProofType(chainDir, "Neo.Plugins.L2Settlement", (byte)ProofType.Optimistic);
        RewriteProofType(chainDir, "Neo.Plugins.L2Prover", (byte)ProofType.Optimistic);
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

    private static UInt160 Account(byte fill) =>
        UInt160.Parse("0x" + Convert.ToHexString(Enumerable.Repeat(fill, 20).ToArray()).ToLowerInvariant());

    private static UInt256 Root(byte fill) =>
        new(Enumerable.Repeat(fill, 32).ToArray());

    private static HttpClient CanonicalRootHttpClient()
    {
        return new HttpClient(new MockHandler(async (request, _) =>
        {
            var body = await request.Content!.ReadAsStringAsync();
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
            => throw new NotSupportedException();

        public ValueTask<ProofWitnessExecutionResult> ApplyBatchWithWitnessAsync(
            SealedBatch batch,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class SoftPassThroughExecutor : IProofWitnessBatchExecutor
    {
        public static UInt256 PostStateRoot { get; } =
            new(Enumerable.Repeat((byte)0x33, 32).ToArray());

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
            => throw new NotSupportedException();
    }
}
