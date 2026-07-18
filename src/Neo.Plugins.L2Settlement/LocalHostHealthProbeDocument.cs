using System.Text.Json;

namespace Neo.Plugins.L2;

/// <summary>
/// Compact LocalHost health probe for ops scripts (passport / pipeline / metrics / settlement).
/// </summary>
/// <remarks>
/// See doc.md §7.5 / §14.2. Smaller than <see cref="LocalHostOperatorStatusDocument"/>;
/// does not claim L1 settle, prove-batch, or live scan (funded gates).
/// Produced by Multisig/Optimistic/Zk <c>GetHealthProbeAsync</c> /
/// <c>WriteHealthProbeAsync</c> / metrics HTTP <c>/healthprobe</c>.
/// </remarks>
public sealed record LocalHostHealthProbeDocument
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Serialize as indented camelCase JSON (trailing newline) for ops scripts and
    /// metrics HTTP <c>/healthprobe</c>.
    /// </summary>
    public static string FormatJson(LocalHostHealthProbeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(document, JsonOptions) + Environment.NewLine;
    }

    /// <summary>Offline wiring/config passport is complete.</summary>
    public required bool IsOfflinePassportComplete { get; init; }

    /// <summary>Failed offline passport checks (empty when complete).</summary>
    public required IReadOnlyList<string> OfflinePassportFailures { get; init; }

    /// <summary>Batcher + settlement settings both enabled.</summary>
    public required bool IsPipelineEnabled { get; init; }

    /// <summary>Sealed batch awaits settlement ack (local durable).</summary>
    public required bool HasPendingSealedBatch { get; init; }

    /// <summary>Pending sealed batch number when present; otherwise null.</summary>
    public required ulong? PendingSealedBatchNumber { get; init; }

    /// <summary>Open batch is at or past MaxBatchAgeMillis (seal-by-age overdue).</summary>
    public required bool IsOpenBatchPastMaxAge { get; init; }

    /// <summary>
    /// Batcher last-acked batch aligns with durable settlement checkpoint (local artifacts).
    /// </summary>
    public required bool IsBatcherCheckpointAligned { get; init; }

    /// <summary>
    /// Soft forced-inclusion overdue from local cache only (no L1 scan).
    /// Live poll remains <c>HasOverdueForcedInclusionAsync</c>.
    /// </summary>
    public required bool HasOverdueForcedInclusion { get; init; }

    /// <summary>Pipeline health (passport + batcher/settlement runtime) is clean.</summary>
    public required bool IsPipelineHealthy { get; init; }

    /// <summary>Failed pipeline health checks (empty when healthy).</summary>
    public required IReadOnlyList<string> PipelineHealthFailures { get; init; }

    /// <summary>Metrics HTTP server is listening.</summary>
    public required bool IsMetricsHttpListening { get; init; }

    /// <summary>Bound metrics port when listening; otherwise 0.</summary>
    public required int MetricsBoundPort { get; init; }

    /// <summary>Metrics <c>/readyz</c> predicate is installed.</summary>
    public required bool HasMetricsReadinessCheck { get; init; }

    /// <summary>Metrics <c>/healthprobe</c> JSON body provider is installed.</summary>
    public required bool HasMetricsHealthProbe { get; init; }

    /// <summary>Metrics HTTP health is clean (or metrics disabled).</summary>
    public required bool IsMetricsHttpHealthy { get; init; }

    /// <summary>Failed metrics HTTP checks (empty when healthy or metrics disabled).</summary>
    public required IReadOnlyList<string> MetricsHttpHealthFailures { get; init; }

    /// <summary>Combined pipeline + metrics HTTP health is clean.</summary>
    public required bool IsLocalHostHealthy { get; init; }

    /// <summary>Failed combined host health checks.</summary>
    public required IReadOnlyList<string> LocalHostHealthFailures { get; init; }

    /// <summary>Local settlement queue is idle (no L1 settle claim).</summary>
    public required bool IsSettlementRuntimeIdle { get; init; }

    /// <summary>Durable recovery is poisoned (no L1 settle claim).</summary>
    public required bool IsSettlementPoisoned { get; init; }

    /// <summary>Durable recovery is retrying (no L1 settle claim).</summary>
    public required bool IsSettlementRetrying { get; init; }

    /// <summary>Local pending settlement artifact count (no L1 claim).</summary>
    public required int PendingSettlementCount { get; init; }

    /// <summary>Soft deposit-source ready count (in-memory; no L1 scan).</summary>
    public required int DepositSourceReadyCount { get; init; }

    /// <summary>Local L1 inbox pending count (soft cache; no L1 scan claim).</summary>
    public required int L1InboxPendingCount { get; init; }
}
