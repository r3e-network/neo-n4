using System.Net;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.Bridge;
using Neo.L2.Executor;
using Neo.L2.Executor.ProofWitness;
using Neo.L2.Persistence;
using Neo.L2.Proving;
using Neo.L2.Proving.RiscVZk;
using Neo.L2.Settlement.Rpc;
using Neo.L2.State;
using Neo.Network.P2P.Payloads;
using Neo.L2.SoftSeal.TestSupport;
using Neo.Plugins.L2Rpc;
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
            Assert.IsTrue(host.RegisterInboundMessageNonce(3));
            Assert.AreEqual(1, host.KnownInboundNonceCount);
            Assert.AreEqual(0, host.L1InboxPendingCount);
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
            Assert.AreEqual(ProofType.Zk, host.SettlementConfiguredProofType);
            Assert.IsTrue(host.IsChainIdConfigConsistent);
            Assert.IsTrue(host.IsProofTypeConfigConsistent);
            Assert.AreEqual(host.ChainId, host.RpcChainId);
            Assert.AreEqual(DAMode.L1, host.RpcDaMode);
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
            Assert.AreEqual(DAMode.L1, status.RpcDaMode);
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
            Assert.IsFalse(status.SupportsLocalDaReader);
            Assert.IsFalse(host.SupportsLocalDaReader);
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
            Assert.IsTrue(status.HasDepositSource);
            Assert.IsTrue(status.HasMessageRouter);
            Assert.IsTrue(status.HasForcedInclusionFinalizer);
            Assert.IsTrue(status.HasSettlementClient);
            Assert.IsTrue(status.HasTransactionSender);
            Assert.AreEqual(1, status.KnownInboundNonceCount);
            Assert.AreEqual(0, status.L1InboxPendingCount);
            Assert.AreEqual(host.GetLatestRpcStateRoot(), status.LatestRpcStateRoot);
            Assert.AreEqual(host.RpcStore.GatewayEnabled, status.GatewayEnabled);
            // Host RPC store helpers (no Neo.CLI / funded prove-batch).
            var rpcRoot = new UInt256(Enumerable.Repeat((byte)0xCD, 32).ToArray());
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
                ProofType = ProofType.Zk,
                Proof = ReadOnlyMemory<byte>.Empty,
            };
            host.AddRpcBatch(rpcBatch, BatchStatus.Pending);
            Assert.AreEqual(BatchStatus.Pending, host.GetRpcBatchStatus(1));
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
            Assert.IsNotNull(host.MessageOutbox);
            Assert.IsNull(
                host.GetMessageRouterProofAsync(new UInt256(new byte[32])).AsTask().GetAwaiter().GetResult());
            Assert.IsTrue(host.RegisterForcedInclusionNonce(9));
            Assert.AreEqual(1, host.KnownForcedInclusionNonceCount);
            Assert.IsTrue(host.HasBatchForcedInclusionSource);
            Assert.IsTrue(host.HasBatchDepositSource);
            Assert.IsTrue(host.HasBatchMessageRouter);
            Assert.IsTrue(host.MaxForcedTransactionsPerBatch > 0);
            Assert.IsTrue(host.MetricsMaxConcurrentConnections > 0);
            host.InvalidateForcedInclusionCache();
            Assert.AreEqual(0, host.BridgeAssetCount);
            // Offline deposit mint path (no funded L1 scan / prove-batch).
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
            Assert.AreEqual(1, host.SnapshotBridgeAssets().Count);
            Assert.AreEqual(l2Asset, host.SnapshotBridgeAssets()[0].L2Asset);
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
            // Offline scan+process composition (no funded L1 deposit events).
            Assert.AreEqual(0, host.ScanAndProcessReadyDepositsAsync().AsTask().GetAwaiter().GetResult().Count);
            Assert.AreEqual(0, host.ScanSharedBridgeDepositsAsync().AsTask().GetAwaiter().GetResult());
            // Offline withdrawal staging + L2→L1 outbox (no funded L1 / prove-batch).
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
            Assert.IsFalse(string.IsNullOrWhiteSpace(host.ExportPrometheusMetrics()));
            Assert.IsNotNull(host.BatchProver);
            Assert.IsTrue(host.HasBatchProver);
            var statusPath = Path.Combine(chainDir, "operator-status.json");
            host.WriteOperatorStatusAsync(statusPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(statusPath));
            var statusJson = File.ReadAllText(statusPath);
            StringAssert.Contains(statusJson, "\"proofType\": \"Zk\"");
            StringAssert.Contains(statusJson, "\"hasBatchProver\": true");
            StringAssert.Contains(statusJson, "\"initialStateRoot\":");
            StringAssert.Contains(statusJson, "\"settlementRetryCount\": 0");
            StringAssert.Contains(statusJson, "\"settlementConfirmationLagBatches\":");
            StringAssert.Contains(statusJson, "\"consumedDepositCount\": 1");
            StringAssert.Contains(statusJson, "\"isSettlementIdle\": true");
            StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Count\": 1");
            StringAssert.Contains(statusJson, "\"stagedWithdrawalCount\": 0");
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
            Assert.AreEqual(nameof(ProofType.Zk), probe.ProofType);
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
            Assert.AreEqual(host.SupportsLocalDaReader, probe.SupportsLocalDaReader);
            Assert.IsFalse(probe.SupportsLocalDaReader);
            Assert.IsTrue(probe.HasBatchProver);
            Assert.IsTrue(probe.HasSettlementManagerHash);
            Assert.IsTrue(probe.HasBatchDepositSource);
            Assert.IsTrue(probe.HasBatchForcedInclusionSource);
            Assert.IsTrue(probe.HasScannerDeployHeights);
            Assert.AreEqual(host.L1FinalityDepth, probe.L1FinalityDepth);
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
            Assert.AreEqual(0, probe.L1InboxPendingCount);
            Assert.AreEqual(host.DepositSourceReservedCount, probe.DepositSourceReservedCount);
            Assert.AreEqual(1, probe.ConsumedDepositCount);
            Assert.AreEqual(host.ConsumedDepositCount, probe.ConsumedDepositCount);
            Assert.AreEqual(host.L1InboxConsumedCount, probe.L1InboxConsumedCount);
            Assert.IsTrue(probe.HasMessageOutbox);
            Assert.AreEqual(host.KnownInboundNonceCount, probe.KnownInboundNonceCount);
            Assert.AreEqual(host.KnownForcedInclusionNonceCount, probe.KnownForcedInclusionNonceCount);
            Assert.AreEqual(host.StagedWithdrawalCount, probe.StagedWithdrawalCount);
            Assert.IsFalse(probe.IsMetricsHttpListening);
            var probePath = Path.Combine(chainDir, "health-probe.json");
            host.WriteHealthProbeAsync(probePath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(probePath));
            var probeJson = File.ReadAllText(probePath);
            StringAssert.Contains(probeJson, "\"pendingSettlementCount\": 0");
            StringAssert.Contains(probeJson, "\"isOperatorReady\": true");
            StringAssert.Contains(probeJson, "\"settlementRetryCount\": 0");
            StringAssert.Contains(probeJson, "\"isBatcherCheckpointAligned\": true");
            StringAssert.Contains(probeJson, "\"nextBatchNumber\": 1");
            StringAssert.Contains(probeJson, "\"hasOpenBatch\": true");
            StringAssert.Contains(probeJson, "\"openBatchBlockCount\": " + host.OpenBatchBlockCount);
            StringAssert.Contains(probeJson, "\"nextExpectedBlock\": " + host.NextExpectedBlock);
            StringAssert.Contains(probeJson, "\"depositSourceReservedCount\":");
            StringAssert.Contains(probeJson, $"\"consumedDepositCount\": {host.ConsumedDepositCount}");
            StringAssert.Contains(probeJson, "\"hasMessageOutbox\": true");
            StringAssert.Contains(probeJson, "\"knownForcedInclusionNonceCount\":");
            StringAssert.Contains(probeJson, "\"stagedWithdrawalCount\":");
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
            var probeAfterMetrics = host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(probeAfterMetrics.IsMetricsHttpListening);
            Assert.IsTrue(probeAfterMetrics.MetricsBoundPort > 0);
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

    /// <summary>
    /// Offline Zk host DA publish/availability via shared recording production-DA marker.
    /// Does not claim funded NeoFS/L1 credentials, SoftSeal multi-cycle, or SP1 prove-batch.
    /// </summary>
    [TestMethod]
    public void SoftOffline_RecordingProductionDa_PublishAndAvailability()
    {
        var reportPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "docs", "audit", "testnet-deployment-20260716-live.json"));
        if (!File.Exists(reportPath))
            Assert.Inconclusive($"repo evidence file not found at {reportPath}");

        var chainDir = Path.Combine(
            Path.GetTempPath(),
            "neo-n4-zk-soft-da-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(chainDir);
        var exeRoot = Path.Combine(
            Path.GetTempPath(),
            "neo-n4-zk-soft-da-exe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(exeRoot);
        try
        {
            var genesisRoot = MaterializeZkChain(chainDir, reportPath);
            var exePath = Path.Combine(exeRoot, "neo-zkvm-executor");
            File.WriteAllBytes(exePath, [0x0A, 0x0B]);
            var sha = SHA256.HashData(File.ReadAllBytes(exePath));
            var da = new RecordingProductionDaWriter();

            using var http = CanonicalRootHttpClient(genesisRoot);
            using var host = ZkLocalHostComposition.Open(
                chainDir,
                exePath,
                sha,
                Hash(0x7A),
                da,
                new StubSigner(Account(0x44)),
                rpcHttpClient: http);

            Assert.IsFalse(host.SupportsLocalDaReader);
            Assert.AreSame(da, host.DaWriter);
            Assert.ThrowsExactly<NotSupportedException>(() => host.CreateLocalDaReader());

            var payload = new byte[] { 0xDA, 0x5A, 0x01 };
            var receipt = host.PublishDaAsync(new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 1,
                Payload = payload,
            }).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(DAMode.L1, receipt.Layer);
            Assert.AreEqual(DAReceiptKind.L1Transaction, receipt.Kind);
            Assert.IsTrue(host.IsDaAvailableAsync(receipt).AsTask().GetAwaiter().GetResult());
            Assert.AreEqual(1, da.PublishCount);
            Assert.IsTrue(da.TryGetPayload(receipt.Commitment, out var readBack));
            CollectionAssert.AreEqual(payload, readBack.ToArray());

            var status = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.IsFalse(status.SupportsLocalDaReader);
            Assert.AreEqual(DAMode.L1, status.DaMode);
            Assert.IsTrue(status.IsOperatorReady);

            // SoftSeal multi-cycle is Multisig/Optimistic local-DA only — architecture gate.
            var softEx = Assert.ThrowsExactly<NotSupportedException>(() =>
                SoftSealMultiCycle.FromHost(host, Hash(0x5E)));
            StringAssert.Contains(softEx.Message, "SupportsLocalDaReader");
            StringAssert.Contains(softEx.Message, "Zk");

            var pinPath = Path.Combine(chainDir, "soft-offline-zk-production-da.json");
            File.WriteAllText(pinPath, $$"""
                {
                  "supportsLocalDaReader": false,
                  "daMode": "{{receipt.Layer}}",
                  "kind": "{{receipt.Kind}}",
                  "publishCount": {{da.PublishCount}},
                  "payloadBytes": {{payload.Length}},
                  "softSealFromHost": "rejected"
                }
                """);
            Assert.IsTrue(File.Exists(pinPath));
            StringAssert.Contains(File.ReadAllText(pinPath), "\"supportsLocalDaReader\": false");
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
    /// Zk Open composition root: deferred <see cref="LocalHostCompositionBase.StartMetricsHttp"/>
    /// + <see cref="LocalHostCompositionBase.CreateRpcPlugin"/> (ops parity with Multisig/Optimistic).
    /// </summary>
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
            "neo-n4-zk-host-deferred-metrics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(chainDir);
        var exeRoot = Path.Combine(
            Path.GetTempPath(),
            "neo-n4-zk-exec-deferred-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(exeRoot);
        try
        {
            var genesisRoot = MaterializeZkChain(chainDir, reportPath);
            var exePath = Path.Combine(exeRoot, "neo-zkvm-executor");
            File.WriteAllBytes(exePath, [0x0C, 0x0D]);
            var sha = SHA256.HashData(File.ReadAllBytes(exePath));
            var da = new RecordingProductionDaWriter();

            using var http = CanonicalRootHttpClient(genesisRoot);
            using var host = ZkLocalHostComposition.Open(
                chainDir,
                exePath,
                sha,
                Hash(0x7B),
                da,
                new StubSigner(Account(0x44)),
                rpcHttpClient: http);

            Assert.AreEqual(0, host.MetricsBoundPort);
            Assert.IsFalse(host.IsMetricsHttpListening);

            host.StartMetricsHttp(portOverride: 0);
            Assert.IsTrue(host.IsMetricsHttpListening);
            Assert.IsTrue(host.MetricsBoundPort > 0);
            Assert.IsTrue(host.IsProductionWired);
            Assert.IsTrue(host.IsOfflinePassportComplete);
            Assert.IsTrue(host.IsMetricsHttpHealthy);
            Assert.AreEqual(ProofType.Zk, host.ProofType);
            Assert.IsFalse(host.SupportsLocalDaReader);

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

    /// <summary>
    /// Zk Open with <c>startMetricsHttp: true</c> wires /readyz + /healthprobe
    /// (ops composition root; no funded L1 / prove-batch).
    /// </summary>
    [TestMethod]
    public async Task Open_StartMetricsHttp_ReadyzOk()
    {
        var reportPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "docs", "audit", "testnet-deployment-20260716-live.json"));
        if (!File.Exists(reportPath))
            Assert.Inconclusive($"repo evidence file not found at {reportPath}");

        var chainDir = Path.Combine(
            Path.GetTempPath(),
            "neo-n4-zk-host-metrics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(chainDir);
        var exeRoot = Path.Combine(
            Path.GetTempPath(),
            "neo-n4-zk-exec-metrics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(exeRoot);
        try
        {
            var genesisRoot = MaterializeZkChain(chainDir, reportPath);
            var exePath = Path.Combine(exeRoot, "neo-zkvm-executor");
            File.WriteAllBytes(exePath, [0x0E, 0x0F]);
            var sha = SHA256.HashData(File.ReadAllBytes(exePath));
            var da = new RecordingProductionDaWriter();

            using var http = CanonicalRootHttpClient(genesisRoot);
            using var host = ZkLocalHostComposition.Open(
                chainDir,
                exePath,
                sha,
                Hash(0x7C),
                da,
                new StubSigner(Account(0x44)),
                rpcHttpClient: http,
                startMetricsHttp: true,
                metricsPortOverride: 0);

            Assert.IsTrue(host.IsMetricsHttpListening);
            Assert.IsTrue(host.MetricsBoundPort > 0);
            Assert.IsTrue(host.HasMetricsReadinessCheck);
            Assert.IsTrue(host.HasMetricsHealthProbe);
            Assert.IsTrue(host.HasMetricsOperatorStatus);
            Assert.IsTrue(host.IsOfflinePassportComplete);
            Assert.IsTrue(host.IsMetricsHttpHealthy);
            Assert.IsTrue(await host.IsLocalHostHealthyAsync());

            using var client = new HttpClient();
            var ready = await client.GetAsync($"http://127.0.0.1:{host.MetricsBoundPort}/readyz");
            Assert.AreEqual(HttpStatusCode.OK, ready.StatusCode);
            var probeHttp = await client.GetAsync($"http://127.0.0.1:{host.MetricsBoundPort}/healthprobe");
            Assert.AreEqual(HttpStatusCode.OK, probeHttp.StatusCode);
            var probeBody = await probeHttp.Content.ReadAsStringAsync();
            StringAssert.Contains(probeBody, "isOfflinePassportComplete");
            StringAssert.Contains(probeBody, "\"proofType\":");
            StringAssert.Contains(probeBody, "Zk");
            StringAssert.Contains(probeBody, "hasMetricsHealthProbe");
            StringAssert.Contains(probeBody, "\"supportsLocalDaReader\": false");

            var status = await host.GetOperatorStatusAsync();
            Assert.IsTrue(status.IsMetricsHttpHealthy);
            Assert.IsTrue(status.IsLocalHostHealthy);
            Assert.AreEqual(ProofType.Zk, status.ProofType);
            Assert.IsFalse(status.SupportsLocalDaReader);
            // Idle settle helpers (no pending artifacts; Multisig ReadyzOk parity).
            Assert.AreEqual(0, await host.GetPendingCountAsync());
            await host.ReconcileAsync();
            await host.SubmitNextAsync();
            Assert.AreEqual(0, await host.GetPendingCountAsync());
            Assert.IsTrue(await host.IsSettlementRuntimeIdleAsync());

            host.StopMetricsHttp();
            Assert.IsFalse(host.IsMetricsHttpListening);
            Assert.AreEqual(0, host.MetricsBoundPort);
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
    /// Zk Open rejects Optimistic settlement config fail-closed (mode-only Open gate).
    /// </summary>
    [TestMethod]
    public void Open_OptimisticSettlementConfig_FailsClosed()
    {
        var reportPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "docs", "audit", "testnet-deployment-20260716-live.json"));
        if (!File.Exists(reportPath))
            Assert.Inconclusive($"repo evidence file not found at {reportPath}");

        var chainDir = Path.Combine(
            Path.GetTempPath(),
            "neo-n4-zk-host-opt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(chainDir);
        var exeRoot = Path.Combine(
            Path.GetTempPath(),
            "neo-n4-zk-exec-opt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(exeRoot);
        try
        {
            File.WriteAllText(Path.Combine(chainDir, "chain.config.json"), """
                {
                  "chainId": 20260716,
                  "proofType": "Optimistic",
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
            RewriteProofType(chainDir, "Neo.Plugins.L2Settlement", (byte)ProofType.Optimistic);
            RewriteProofType(chainDir, "Neo.Plugins.L2Prover", (byte)ProofType.Optimistic);
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
            var da = new RecordingProductionDaWriter();
            var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
                ZkLocalHostComposition.Open(
                    chainDir,
                    exePath,
                    sha,
                    Hash(0x55),
                    da,
                    new StubSigner(Account(0x55))));
            StringAssert.Contains(ex.Message, "Zk");
            StringAssert.Contains(ex.Message, "Optimistic");
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
                var root = Convert.ToBase64String(genesisRoot.GetSpan().ToArray());
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
