namespace Neo.Plugins.L2;

/// <summary>
/// Compact LocalHost health probe for ops scripts (passport / pipeline / metrics / settlement).
/// </summary>
/// <remarks>
/// See doc.md §7.5 / §14.2. Smaller than <see cref="LocalHostOperatorStatusDocument"/>;
/// does not claim L1 settle, prove-batch, or live scan (funded gates).
/// Produced by Multisig/Optimistic/Zk <c>GetHealthProbeAsync</c> /
/// <c>WriteHealthProbeAsync</c>.
/// </remarks>
public sealed record LocalHostHealthProbeDocument
{
    /// <summary>Offline wiring/config passport is complete.</summary>
    public required bool IsOfflinePassportComplete { get; init; }

    /// <summary>Failed offline passport checks (empty when complete).</summary>
    public required IReadOnlyList<string> OfflinePassportFailures { get; init; }

    /// <summary>Pipeline health (passport + batcher/settlement runtime) is clean.</summary>
    public required bool IsPipelineHealthy { get; init; }

    /// <summary>Failed pipeline health checks (empty when healthy).</summary>
    public required IReadOnlyList<string> PipelineHealthFailures { get; init; }

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
}
