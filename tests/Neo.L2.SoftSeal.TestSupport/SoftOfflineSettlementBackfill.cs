using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.Sequencer;
using Neo.L2.State;
using Neo.Plugins.L2;

namespace Neo.L2.SoftSeal.TestSupport;

/// <summary>
/// Shared soft-offline settlement backfill surface for Multisig EnqueueAsync /
/// Optimistic PersistAsync (unit + E2E). One code path pins settlement tip advance
/// without batcher tip advance — no Multisig/Optimistic N-way clones.
/// </summary>
/// <remarks>
/// Architecture: <see cref="LocalHostCompositionBase.EnqueueAsync"/> aliases
/// <see cref="LocalHostCompositionBase.PersistAsync"/>; both modes share this pin.
/// Re-align requires batcher seal or L1 settle (funded / operator gates).
/// </remarks>
public static class SoftOfflineSettlementBackfill
{
    public const uint ChainId = SoftSealMultiCycle.ChainId;
    public const uint NetworkMagic = 894710606;

    /// <summary>
    /// After opening a Multisig/Optimistic host: seal batch 1 via ProcessCommittedBlock,
    /// then call <paramref name="persistOrEnqueue"/> for batch 2 without advancing the
    /// batcher tip. Pins misalignment health rollup, mock-L1
    /// <see cref="LocalHostCompositionBase.GetLatestCheckpointAsync"/> fail-closed, and
    /// durable operatorstatus/healthprobe/prometheus writers while unhealthy.
    /// </summary>
    /// <param name="host">Opened Multisig/Optimistic LocalHost composition.</param>
    /// <param name="chainDir">Chain directory used for committee hash + durable ops artifacts.</param>
    /// <param name="expectedPostStateRoot">Executor post-state root (e.g. SoftPassThroughExecutor).</param>
    /// <param name="expectedProofType">ProofType expected on operator status (Multisig/Optimistic).</param>
    /// <param name="persistOrEnqueue">Host PersistAsync or EnqueueAsync entry (aliases).</param>
    /// <param name="artifactPrefix">
    /// File stem under <paramref name="chainDir"/> (e.g. <c>soft-offline-enqueue</c>
    /// or <c>e2e-soft-offline-persist</c>).
    /// </param>
    /// <param name="backfillFlagName">
    /// JSON pin flag name documenting which host entry was used
    /// (<c>enqueueBackfill</c> or <c>persistBackfill</c>).
    /// </param>
    public static void AssertBackfillWithoutAdvancingBatcher(
        LocalHostCompositionBase host,
        string chainDir,
        UInt256 expectedPostStateRoot,
        ProofType expectedProofType,
        Func<SealedBatch, ValueTask<UInt256>> persistOrEnqueue,
        string artifactPrefix,
        string backfillFlagName)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(chainDir);
        ArgumentNullException.ThrowIfNull(expectedPostStateRoot);
        ArgumentNullException.ThrowIfNull(persistOrEnqueue);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(backfillFlagName);

        var ts1 = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        host.ProcessCommittedBlock(1, ts1, NetworkMagic, Array.Empty<byte[]>());
        Assert.AreEqual(1, host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult());
        Assert.AreEqual(1UL, host.LastAcknowledgedBatchNumber);
        Assert.AreEqual(2UL, host.NextExpectedBlock);
        Assert.AreEqual(2UL, host.NextBatchNumber);
        Assert.IsFalse(host.TryRetryPendingSealedBatch());
        // After seal-1: batcher tip matches durable tip (aligned) while settle still pending L1.
        Assert.IsTrue(host.IsBatcherCheckpointAlignedAsync().AsTask().GetAwaiter().GetResult());

        var committeeHash =
            SequencerCommitteeConfig.CreateStaticHashProviderFromChainDirectory(chainDir)();
        Assert.IsFalse(committeeHash.Equals(UInt256.Zero));

        var postRoot = persistOrEnqueue(new SealedBatch(
            chainId: ChainId,
            batchNumber: 2,
            firstBlock: 2,
            lastBlock: 2,
            preStateRoot: expectedPostStateRoot,
            transactions: Array.Empty<ReadOnlyMemory<byte>>(),
            l1Messages: Array.Empty<CrossChainMessage>(),
            blockContext: new BatchBlockContext
            {
                L1FinalizedHeight = 1,
                FirstBlockTimestamp = ts1 + 1,
                LastBlockTimestamp = ts1 + 1,
                SequencerCommitteeHash = committeeHash,
                Network = NetworkMagic,
            })).AsTask().GetAwaiter().GetResult();
        Assert.AreEqual(expectedPostStateRoot, postRoot);
        Assert.IsTrue(host.GetPendingCountAsync().AsTask().GetAwaiter().GetResult() >= 2);

        var tip = host.GetLatestDurableCheckpointAsync().AsTask().GetAwaiter().GetResult();
        Assert.IsNotNull(tip);
        Assert.AreEqual(2UL, tip!.BatchNumber);
        Assert.AreEqual(2UL, tip.LastBlock);
        Assert.AreEqual(expectedPostStateRoot, tip.PostStateRoot);

        // GetLatestCheckpointAsync refreshes L1 settlement lifecycle — mock RPC fails closed
        // (funded gate). Durable tip remains available offline.
        Assert.ThrowsExactly<OverflowException>(() =>
            host.GetLatestCheckpointAsync().AsTask().GetAwaiter().GetResult());

        // Batcher still sits at post-seal-1 tip; settlement tip advanced via Persist/Enqueue only.
        Assert.AreEqual(1UL, host.LastAcknowledgedBatchNumber);
        Assert.AreEqual(1UL, host.LastAcknowledgedBlock);
        Assert.AreEqual(2UL, host.NextExpectedBlock);
        Assert.AreEqual(2UL, host.NextBatchNumber);
        Assert.IsFalse(host.HasPendingSealedBatch);
        Assert.IsFalse(host.TryRetryPendingSealedBatch());

        var status = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
        Assert.AreEqual(expectedProofType, status.ProofType);
        Assert.IsTrue(status.PendingSettlementCount >= 2);
        Assert.AreEqual(2UL, status.LatestCheckpointBatchNumber);
        Assert.AreEqual(1UL, status.LastAcknowledgedBatchNumber);
        Assert.IsFalse(status.IsSettlementIdle);
        // Operator surface: settlement tip advanced without batcher seal advance → misaligned.
        Assert.IsFalse(status.IsBatcherCheckpointAligned);
        Assert.IsFalse(host.IsBatcherCheckpointAlignedAsync().AsTask().GetAwaiter().GetResult());
        var pipelineFailures = host.GetPipelineHealthFailuresAsync().AsTask().GetAwaiter().GetResult();
        CollectionAssert.Contains(
            pipelineFailures.ToArray(),
            nameof(LocalHostOperatorStatus.IsBatcherCheckpointAligned));
        Assert.IsFalse(host.IsPipelineHealthyAsync().AsTask().GetAwaiter().GetResult());
        Assert.IsFalse(host.IsLocalHostHealthyAsync().AsTask().GetAwaiter().GetResult());
        var hostFailures = host.GetLocalHostHealthFailuresAsync().AsTask().GetAwaiter().GetResult();
        CollectionAssert.Contains(
            hostFailures.ToArray(),
            nameof(LocalHostOperatorStatus.IsBatcherCheckpointAligned));
        CollectionAssert.Contains(
            status.LocalHostHealthFailures.ToArray(),
            nameof(LocalHostOperatorStatus.IsBatcherCheckpointAligned));
        var probe = host.GetHealthProbeAsync().AsTask().GetAwaiter().GetResult();
        Assert.IsFalse(probe.IsBatcherCheckpointAligned);
        Assert.IsFalse(probe.IsPipelineHealthy);
        Assert.IsFalse(probe.IsLocalHostHealthy);
        CollectionAssert.Contains(
            probe.PipelineHealthFailures.ToArray(),
            nameof(LocalHostOperatorStatus.IsBatcherCheckpointAligned));

        // Durable operator JSON writers still work offline while unhealthy.
        var proofTypeName = expectedProofType.ToString();
        var statusJson = host.FormatOperatorStatusJsonAsync().AsTask().GetAwaiter().GetResult();
        StringAssert.Contains(statusJson, "\"isBatcherCheckpointAligned\": false");
        StringAssert.Contains(statusJson, "\"isLocalHostHealthy\": false");
        StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": 2");
        StringAssert.Contains(statusJson, $"\"proofType\": \"{proofTypeName}\"");
        var probeJson = host.FormatHealthProbeJson();
        StringAssert.Contains(probeJson, "\"isBatcherCheckpointAligned\": false");
        StringAssert.Contains(probeJson, "\"isLocalHostHealthy\": false");

        var statusPath = Path.Combine(chainDir, artifactPrefix + "-operator-status.json");
        host.WriteOperatorStatusAsync(statusPath).AsTask().GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        StringAssert.Contains(File.ReadAllText(statusPath), "\"isBatcherCheckpointAligned\": false");
        var probePath = Path.Combine(chainDir, artifactPrefix + "-health-probe.json");
        host.WriteHealthProbeAsync(probePath).AsTask().GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        StringAssert.Contains(File.ReadAllText(probePath), "\"isBatcherCheckpointAligned\": false");
        var promPath = Path.Combine(chainDir, artifactPrefix + "-metrics.prom");
        host.WritePrometheusMetricsAsync(promPath).AsTask().GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(promPath));
        Assert.IsFalse(string.IsNullOrWhiteSpace(File.ReadAllText(promPath)));

        var pinPath = Path.Combine(chainDir, artifactPrefix + "-backfill.json");
        File.WriteAllText(pinPath, $$"""
            {
              "proofType": "{{proofTypeName}}",
              "settlementPending": {{status.PendingSettlementCount}},
              "latestCheckpointBatchNumber": {{status.LatestCheckpointBatchNumber}},
              "batcherLastAcknowledgedBatchNumber": {{status.LastAcknowledgedBatchNumber}},
              "nextExpectedBlock": {{status.NextExpectedBlock}},
              "isBatcherCheckpointAligned": false,
              "isPipelineHealthy": false,
              "isLocalHostHealthy": false,
              "operatorStatusJsonWritten": true,
              "healthProbeJsonWritten": true,
              "prometheusWritten": true,
              "{{backfillFlagName}}": true,
              "getLatestCheckpointAsync": "fail-closed-mock-l1"
            }
            """);
        Assert.IsTrue(File.Exists(pinPath));
        var pinText = File.ReadAllText(pinPath);
        StringAssert.Contains(pinText, $"\"{backfillFlagName}\": true");
        StringAssert.Contains(pinText, "\"isBatcherCheckpointAligned\": false");
        StringAssert.Contains(pinText, "\"isLocalHostHealthy\": false");
        StringAssert.Contains(pinText, "\"operatorStatusJsonWritten\": true");
        StringAssert.Contains(pinText, $"\"proofType\": \"{proofTypeName}\"");
        StringAssert.Contains(pinText, "\"latestCheckpointBatchNumber\": 2");
        StringAssert.Contains(pinText, "\"batcherLastAcknowledgedBatchNumber\": 1");
    }
}
