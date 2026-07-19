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
            Assert.IsTrue(host.RegisterInboundMessageNonce(3));
            Assert.AreEqual(1, host.KnownInboundNonceCount);
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
            Assert.AreEqual(1, probe.OpenBatchBlockCount);
            Assert.IsTrue(probe.IsBatcherCheckpointAligned);
            Assert.AreEqual(0UL, probe.LastAcknowledgedBatchNumber);
            Assert.AreEqual(1UL, probe.NextBatchNumber);
            Assert.AreEqual(2UL, probe.NextExpectedBlock);
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
            StringAssert.Contains(probeJson, "\"openBatchBlockCount\": 1");
            StringAssert.Contains(probeJson, "\"nextExpectedBlock\": 2");
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

            var status = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(1UL, status.LatestCheckpointBatchNumber);
            Assert.AreEqual(SoftPassThroughExecutor.PostStateRoot, status.LatestCheckpointPostStateRoot);
            Assert.AreEqual(1, status.PendingSettlementCount);
            Assert.IsFalse(status.IsSettlementIdle);
            Assert.IsTrue(status.IsBatcherCheckpointAligned);
            // Offline passport still complete; pipeline unhealthy from settlement Retrying
            // after mock L1 settle fails (preferred recovery label over generic IsSettlementIdle).
            Assert.IsTrue(status.IsOfflinePassportComplete);
            Assert.AreEqual(0, status.OfflinePassportFailures.Count);
            Assert.IsFalse(status.IsPipelineHealthy);
            Assert.IsTrue(status.IsSettlementRetrying);
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
            Assert.AreEqual(1, recovery.PendingCount);
            Assert.IsFalse(string.IsNullOrEmpty(recovery.LastError));
            Assert.IsTrue(recovery.RetryCount >= 1);

            var probe = host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(1, probe.PendingSettlementCount);
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
            StringAssert.Contains(softSealStatusJson, "\"pendingSettlementCount\": 1");

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

            // SubmitNext/Reconcile against mock L1 cannot clear the L1 settle queue (funded gate).
            host.SubmitNextAsync().GetAwaiter().GetResult();
            Assert.IsTrue(host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult() >= 1);
            Assert.IsFalse(host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult().IsSettlementIdle);
            Assert.IsTrue(host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult().IsSettlementRetrying);
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
