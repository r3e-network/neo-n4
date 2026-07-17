using Neo.Plugins.L2Gateway;

namespace Neo.L2.Gateway.Rpc;

/// <summary>
/// Aggregated Gateway host operator snapshot for health/ops without Neo.CLI.
/// </summary>
/// <remarks>
/// See doc.md §4 / §14.2. Built by <see cref="GatewayHostComposition.GetOperatorStatus"/>
/// from in-process outbox/publication state. L1 confirmation remains a funded gate.
/// </remarks>
public sealed record GatewayHostOperatorStatus
{
    /// <summary>Absolute chain working directory.</summary>
    public required string ChainDirectory { get; init; }

    /// <summary>True when an unconfirmed publication remains retryable or poisoned.</summary>
    public required bool HasPendingPublication { get; init; }

    /// <summary>Pending publication epoch, or null when none awaits confirmation.</summary>
    public required ulong? PendingPublicationEpoch { get; init; }

    /// <summary>Durable outbox queue depth.</summary>
    public required int OutboxQueueDepth { get; init; }

    /// <summary>Current publication state, or null when no aggregate is active.</summary>
    public required GatewayOutboxState? PublicationState { get; init; }

    /// <summary>Consecutive outbox retry count.</summary>
    public required int OutboxRetryCount { get; init; }

    /// <summary>Last durable outbox failure, if any.</summary>
    public required string? OutboxLastError { get; init; }

    /// <summary>Age of the current publication in milliseconds.</summary>
    public required long ConfirmationLagMilliseconds { get; init; }

    /// <summary>Terminal proof prover aggregation backend id.</summary>
    public required byte AggregationBackendId { get; init; }

    /// <summary>True when this composition owns proof-prover disposal.</summary>
    public required bool OwnsProofProver { get; init; }
}
