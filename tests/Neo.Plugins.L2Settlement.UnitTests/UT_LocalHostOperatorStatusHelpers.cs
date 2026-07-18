using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.L2;
using Neo.L2.Persistence;
using Neo.Plugins.L2;

namespace Neo.Plugins.L2Settlement.UnitTests;

[TestClass]
public sealed class UT_LocalHostOperatorStatusHelpers
{
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
    public void BuildPipelineHealthFailures_NamesPassportEnablementPendingSealMisalignedOverdueFiPoison()
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
            hasMetricsReadinessCheck: false);
        Assert.AreEqual(0, failures.Count);
    }

    [TestMethod]
    public void BuildMetricsHttpHealthFailures_NamesWiringListeningAndReadinessWhenEnabled()
    {
        var failures = LocalHostOperatorStatus.BuildMetricsHttpHealthFailures(
            metricsEnabled: true,
            metricsWiringComplete: false,
            metricsHttpListening: false,
            hasMetricsReadinessCheck: false);
        CollectionAssert.AreEqual(
            new[]
            {
                nameof(LocalHostOperatorStatus.IsMetricsWiringComplete),
                nameof(LocalHostOperatorStatus.IsMetricsHttpListening),
                nameof(LocalHostOperatorStatus.HasMetricsReadinessCheck),
            },
            failures.ToArray());

        var ok = LocalHostOperatorStatus.BuildMetricsHttpHealthFailures(
            metricsEnabled: true,
            metricsWiringComplete: true,
            metricsHttpListening: true,
            hasMetricsReadinessCheck: true);
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
