namespace Neo.L2.Gateway.Rpc;

/// <summary>
/// JSON-serializable projection of <see cref="GatewayHostOperatorStatus"/> for host health files.
/// </summary>
/// <remarks>
/// See doc.md §4 / §14.2. Produced by <see cref="GatewayHostComposition.WriteOperatorStatusAsync"/>.
/// L1 confirmation remains a funded gate and is not claimed here.
/// </remarks>
public sealed record GatewayHostOperatorStatusDocument
{
    /// <summary>Absolute chain working directory.</summary>
    public required string ChainDirectory { get; init; }

    /// <summary>True when an unconfirmed publication remains retryable or poisoned.</summary>
    public required bool HasPendingPublication { get; init; }

    /// <summary>Pending publication epoch, if any.</summary>
    public required ulong? PendingPublicationEpoch { get; init; }

    /// <summary>Durable outbox queue depth.</summary>
    public required int OutboxQueueDepth { get; init; }

    /// <summary>Publication state name, if any.</summary>
    public string? PublicationState { get; init; }

    /// <summary>Consecutive outbox retry count.</summary>
    public required int OutboxRetryCount { get; init; }

    /// <summary>Last durable outbox failure, if any.</summary>
    public string? OutboxLastError { get; init; }

    /// <summary>Age of the current publication in milliseconds.</summary>
    public required long ConfirmationLagMilliseconds { get; init; }

    /// <summary>Terminal proof prover aggregation backend id.</summary>
    public required byte AggregationBackendId { get; init; }

    /// <summary>True when this composition owns proof-prover disposal.</summary>
    public required bool OwnsProofProver { get; init; }

    /// <summary>Map a live status snapshot into a JSON document.</summary>
    public static GatewayHostOperatorStatusDocument From(GatewayHostOperatorStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new GatewayHostOperatorStatusDocument
        {
            ChainDirectory = status.ChainDirectory,
            HasPendingPublication = status.HasPendingPublication,
            PendingPublicationEpoch = status.PendingPublicationEpoch,
            OutboxQueueDepth = status.OutboxQueueDepth,
            PublicationState = status.PublicationState?.ToString(),
            OutboxRetryCount = status.OutboxRetryCount,
            OutboxLastError = status.OutboxLastError,
            ConfirmationLagMilliseconds = status.ConfirmationLagMilliseconds,
            AggregationBackendId = status.AggregationBackendId,
            OwnsProofProver = status.OwnsProofProver,
        };
    }
}
