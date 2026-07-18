using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.L2;
using Neo.L2.Persistence;
using Neo.Plugins.L2;

namespace Neo.Plugins.L2Settlement.UnitTests;

[TestClass]
public sealed class UT_LocalHostOperatorStatusHelpers
{
    [TestMethod]
    public void BuildOfflinePassportFailures_EmptyWhenAllReady()
    {
        var failures = LocalHostOperatorStatus.BuildOfflinePassportFailures(
            isOperatorReady: true,
            isChainIdConfigConsistent: true,
            isProofTypeConfigConsistent: true,
            isDaModeConfigConsistent: true,
            isSecurityLevelProofTypeConsistent: true,
            isSecurityLevelDaModeConsistent: true,
            isNeoHubHashWiringComplete: true,
            isBatcherInboxWiringComplete: true,
            hasBatchProver: true,
            isSettlementEnabled: true,
            isBatcherEnabled: true,
            hasL1RpcEndpoint: true,
            hasExpectedNetwork: true,
            hasScannerDeployHeights: true,
            hasDepositSource: true,
            hasMessageRouter: true,
            hasForcedInclusionFinalizer: true,
            hasSettlementClient: true,
            hasTransactionSender: true,
            hasMessageOutbox: true,
            isDepositPipelineWiringComplete: true,
            isMessagePipelineWiringComplete: true,
            isForcedInclusionPipelineWiringComplete: true,
            isSettlementClientWiringComplete: true);

        Assert.AreEqual(0, failures.Count);
    }

    [TestMethod]
    public void BuildOfflinePassportFailures_NamesOperatorReadyAndDepositSource()
    {
        var failures = LocalHostOperatorStatus.BuildOfflinePassportFailures(
            isOperatorReady: false,
            isChainIdConfigConsistent: true,
            isProofTypeConfigConsistent: true,
            isDaModeConfigConsistent: true,
            isSecurityLevelProofTypeConsistent: true,
            isSecurityLevelDaModeConsistent: true,
            isNeoHubHashWiringComplete: true,
            isBatcherInboxWiringComplete: true,
            hasBatchProver: true,
            isSettlementEnabled: true,
            isBatcherEnabled: true,
            hasL1RpcEndpoint: true,
            hasExpectedNetwork: true,
            hasScannerDeployHeights: true,
            hasDepositSource: false,
            hasMessageRouter: true,
            hasForcedInclusionFinalizer: true,
            hasSettlementClient: true,
            hasTransactionSender: true,
            hasMessageOutbox: true,
            isDepositPipelineWiringComplete: true,
            isMessagePipelineWiringComplete: true,
            isForcedInclusionPipelineWiringComplete: true,
            isSettlementClientWiringComplete: true);

        CollectionAssert.AreEqual(
            new[]
            {
                nameof(LocalHostOperatorStatus.IsOperatorReady),
                nameof(LocalHostOperatorStatus.HasDepositSource),
            },
            failures.ToArray());
    }

    [TestMethod]
    public void IsSecurityLevelPairedWithProofType_RecommendedPairings()
    {
        Assert.IsTrue(LocalHostOperatorStatus.IsSecurityLevelPairedWithProofType(
            SecurityLevel.Validity, ProofType.Zk));
        Assert.IsTrue(LocalHostOperatorStatus.IsSecurityLevelPairedWithProofType(
            SecurityLevel.Validium, ProofType.Zk));
        Assert.IsTrue(LocalHostOperatorStatus.IsSecurityLevelPairedWithProofType(
            SecurityLevel.Optimistic, ProofType.Optimistic));
        Assert.IsTrue(LocalHostOperatorStatus.IsSecurityLevelPairedWithProofType(
            SecurityLevel.Optimistic, ProofType.Multisig));
        Assert.IsTrue(LocalHostOperatorStatus.IsSecurityLevelPairedWithProofType(
            SecurityLevel.Sidechain, ProofType.Multisig));
        Assert.IsTrue(LocalHostOperatorStatus.IsSecurityLevelPairedWithProofType(
            SecurityLevel.Settled, ProofType.None));

        Assert.IsFalse(LocalHostOperatorStatus.IsSecurityLevelPairedWithProofType(
            SecurityLevel.Validity, ProofType.Multisig));
        Assert.IsFalse(LocalHostOperatorStatus.IsSecurityLevelPairedWithProofType(
            SecurityLevel.Optimistic, ProofType.Zk));
        Assert.IsFalse(LocalHostOperatorStatus.IsSecurityLevelPairedWithProofType(
            SecurityLevel.Sidechain, ProofType.Optimistic));
    }

    [TestMethod]
    public void IsSecurityLevelPairedWithDaMode_RecommendedPairings()
    {
        Assert.IsTrue(LocalHostOperatorStatus.IsSecurityLevelPairedWithDaMode(
            SecurityLevel.Validity, DAMode.L1));
        Assert.IsTrue(LocalHostOperatorStatus.IsSecurityLevelPairedWithDaMode(
            SecurityLevel.Validium, DAMode.NeoFS));
        Assert.IsTrue(LocalHostOperatorStatus.IsSecurityLevelPairedWithDaMode(
            SecurityLevel.Validium, DAMode.External));
        Assert.IsTrue(LocalHostOperatorStatus.IsSecurityLevelPairedWithDaMode(
            SecurityLevel.Validium, DAMode.DAC));
        Assert.IsTrue(LocalHostOperatorStatus.IsSecurityLevelPairedWithDaMode(
            SecurityLevel.Optimistic, DAMode.Local));
        Assert.IsTrue(LocalHostOperatorStatus.IsSecurityLevelPairedWithDaMode(
            SecurityLevel.Settled, DAMode.L1));

        Assert.IsFalse(LocalHostOperatorStatus.IsSecurityLevelPairedWithDaMode(
            SecurityLevel.Validity, DAMode.NeoFS));
        Assert.IsFalse(LocalHostOperatorStatus.IsSecurityLevelPairedWithDaMode(
            SecurityLevel.Validium, DAMode.L1));
        Assert.IsFalse(LocalHostOperatorStatus.IsSecurityLevelPairedWithDaMode(
            SecurityLevel.Validium, DAMode.Local));
    }

    [TestMethod]
    public void BuildPipelineHealthFailures_EmptyWhenPassportPipelineAndIdle()
    {
        var recovery = new SettlementRecoveryStatus
        {
            PendingCount = 0,
            ConfirmationLagBatches = 0,
            State = null,
            RetryCount = 0,
            LastError = null,
        };
        var failures = LocalHostOperatorStatus.BuildPipelineHealthFailures(
            offlinePassportComplete: true,
            pipelineEnabled: true,
            hasPendingSealedBatch: false,
            isOpenBatchPastMaxAge: false,
            isBatcherCheckpointAligned: true,
            hasOverdueForcedInclusion: false,
            pendingSettlementCount: 0,
            recovery);
        Assert.AreEqual(0, failures.Count);
    }

    [TestMethod]
    public void AreBatcherAndCheckpointAligned_FreshAndMatching()
    {
        Assert.IsTrue(LocalHostOperatorStatus.AreBatcherAndCheckpointAligned(0, null));
        Assert.IsFalse(LocalHostOperatorStatus.AreBatcherAndCheckpointAligned(1, null));
        Assert.IsTrue(LocalHostOperatorStatus.AreBatcherAndCheckpointAligned(3, 3UL));
        Assert.IsFalse(LocalHostOperatorStatus.AreBatcherAndCheckpointAligned(2, 3UL));
    }

    [TestMethod]
    public void IsSettlementRuntimeIdle_TrueWhenEmptyAndNoFailure()
    {
        var idle = LocalHostOperatorStatus.IsSettlementRuntimeIdle(
            0,
            new SettlementRecoveryStatus
            {
                PendingCount = 0,
                ConfirmationLagBatches = 0,
                State = null,
                RetryCount = 0,
                LastError = null,
            });
        Assert.IsTrue(idle);
    }

    [TestMethod]
    public void IsSettlementRuntimeIdle_FalseWhenPendingPoisonRetryOrError()
    {
        var baseRecovery = new SettlementRecoveryStatus
        {
            PendingCount = 0,
            ConfirmationLagBatches = 0,
            State = null,
            RetryCount = 0,
            LastError = null,
        };
        Assert.IsFalse(LocalHostOperatorStatus.IsSettlementRuntimeIdle(1, baseRecovery));
        Assert.IsFalse(LocalHostOperatorStatus.IsSettlementRuntimeIdle(
            0, baseRecovery with { PendingCount = 2 }));
        Assert.IsFalse(LocalHostOperatorStatus.IsSettlementRuntimeIdle(
            0, baseRecovery with { State = SettlementRecoveryState.Poisoned }));
        Assert.IsFalse(LocalHostOperatorStatus.IsSettlementRuntimeIdle(
            0, baseRecovery with { State = SettlementRecoveryState.Retrying }));
        Assert.IsFalse(LocalHostOperatorStatus.IsSettlementRuntimeIdle(
            0, baseRecovery with { LastError = "fee too low" }));
    }

    [TestMethod]
    public void IsSettlementPoisonedAndRetryingState_ClassifyRecovery()
    {
        var baseRecovery = new SettlementRecoveryStatus
        {
            PendingCount = 0,
            ConfirmationLagBatches = 0,
            State = null,
            RetryCount = 0,
            LastError = null,
        };
        Assert.IsFalse(LocalHostOperatorStatus.IsSettlementPoisonedState(baseRecovery));
        Assert.IsFalse(LocalHostOperatorStatus.IsSettlementRetryingState(baseRecovery));
        Assert.IsTrue(LocalHostOperatorStatus.IsSettlementPoisonedState(
            baseRecovery with { State = SettlementRecoveryState.Poisoned }));
        Assert.IsFalse(LocalHostOperatorStatus.IsSettlementRetryingState(
            baseRecovery with { State = SettlementRecoveryState.Poisoned }));
        Assert.IsTrue(LocalHostOperatorStatus.IsSettlementRetryingState(
            baseRecovery with { State = SettlementRecoveryState.Retrying }));
        Assert.IsFalse(LocalHostOperatorStatus.IsSettlementPoisonedState(
            baseRecovery with { State = SettlementRecoveryState.Retrying }));
    }

    [TestMethod]
    public void BuildPipelineHealthFailures_NamesPassportEnablementPendingSealAgeMisalignedOverdueFiPoison()
    {
        var recovery = new SettlementRecoveryStatus
        {
            PendingCount = 2,
            ConfirmationLagBatches = 1,
            State = SettlementRecoveryState.Poisoned,
            RetryCount = 3,
            LastError = "rpc timeout",
        };
        var failures = LocalHostOperatorStatus.BuildPipelineHealthFailures(
            offlinePassportComplete: false,
            pipelineEnabled: false,
            hasPendingSealedBatch: true,
            isOpenBatchPastMaxAge: true,
            isBatcherCheckpointAligned: false,
            hasOverdueForcedInclusion: true,
            pendingSettlementCount: 1,
            recovery);
        CollectionAssert.AreEqual(
            new[]
            {
                nameof(LocalHostOperatorStatus.IsOfflinePassportComplete),
                nameof(LocalHostOperatorStatus.IsPipelineEnabled),
                nameof(LocalHostOperatorStatus.HasPendingSealedBatch),
                nameof(LocalHostOperatorStatus.IsOpenBatchPastMaxAge),
                nameof(LocalHostOperatorStatus.IsBatcherCheckpointAligned),
                nameof(LocalHostOperatorStatus.HasOverdueForcedInclusion),
                nameof(LocalHostOperatorStatus.IsSettlementPoisoned),
            },
            failures.ToArray());
    }

    [TestMethod]
    public void BuildPipelineHealthFailures_NamesRetryingWithoutGenericIdle()
    {
        var recovery = new SettlementRecoveryStatus
        {
            PendingCount = 1,
            ConfirmationLagBatches = 1,
            State = SettlementRecoveryState.Retrying,
            RetryCount = 1,
            LastError = "transient",
        };
        var failures = LocalHostOperatorStatus.BuildPipelineHealthFailures(
            offlinePassportComplete: true,
            pipelineEnabled: true,
            hasPendingSealedBatch: false,
            isOpenBatchPastMaxAge: false,
            isBatcherCheckpointAligned: true,
            hasOverdueForcedInclusion: false,
            pendingSettlementCount: 0,
            recovery);
        CollectionAssert.AreEqual(
            new[] { nameof(LocalHostOperatorStatus.IsSettlementRetrying) },
            failures.ToArray());
    }

    [TestMethod]
    public void BuildMetricsHttpHealthFailures_EmptyWhenMetricsDisabled()
    {
        var failures = LocalHostOperatorStatus.BuildMetricsHttpHealthFailures(
            metricsEnabled: false,
            metricsWiringComplete: false,
            metricsHttpListening: false,
            hasMetricsReadinessCheck: false,
            hasMetricsHealthProbe: false);
        Assert.AreEqual(0, failures.Count);
    }

    [TestMethod]
    public void BuildMetricsHttpHealthFailures_NamesWiringListeningReadinessAndHealthProbeWhenEnabled()
    {
        var failures = LocalHostOperatorStatus.BuildMetricsHttpHealthFailures(
            metricsEnabled: true,
            metricsWiringComplete: false,
            metricsHttpListening: false,
            hasMetricsReadinessCheck: false,
            hasMetricsHealthProbe: false);
        CollectionAssert.AreEqual(
            new[]
            {
                nameof(LocalHostOperatorStatus.IsMetricsWiringComplete),
                nameof(LocalHostOperatorStatus.IsMetricsHttpListening),
                nameof(LocalHostOperatorStatus.HasMetricsReadinessCheck),
                nameof(LocalHostOperatorStatus.HasMetricsHealthProbe),
            },
            failures.ToArray());

        var ok = LocalHostOperatorStatus.BuildMetricsHttpHealthFailures(
            metricsEnabled: true,
            metricsWiringComplete: true,
            metricsHttpListening: true,
            hasMetricsReadinessCheck: true,
            hasMetricsHealthProbe: true);
        Assert.AreEqual(0, ok.Count);
    }

    [TestMethod]
    public void BuildLocalHostHealthFailures_UnionsPipelineAndMetrics()
    {
        var empty = LocalHostOperatorStatus.BuildLocalHostHealthFailures(
            Array.Empty<string>(), Array.Empty<string>());
        Assert.AreEqual(0, empty.Count);

        var merged = LocalHostOperatorStatus.BuildLocalHostHealthFailures(
            new[] { nameof(LocalHostOperatorStatus.IsPipelineEnabled) },
            new[] { nameof(LocalHostOperatorStatus.IsMetricsHttpListening) });
        CollectionAssert.AreEqual(
            new[]
            {
                nameof(LocalHostOperatorStatus.IsPipelineEnabled),
                nameof(LocalHostOperatorStatus.IsMetricsHttpListening),
            },
            merged.ToArray());
    }
}
