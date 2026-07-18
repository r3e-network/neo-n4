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

    /// <summary>In-process aggregator pending commitment count.</summary>
    public required int AggregatorPendingCount { get; init; }

    /// <summary>True when a durable Gateway outbox is attached.</summary>
    public required bool HasDurableOutbox { get; init; }

    /// <summary>True when production global-root publication is configured.</summary>
    public required bool IsPublicationConfigured { get; init; }

    /// <summary>Whether the Gateway plugin is enabled in settings.</summary>
    public required bool IsEnabled { get; init; }

    /// <summary>Configured max automatic publication retries before poison.</summary>
    public required int MaxAutomaticRetries { get; init; }

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

    /// <summary>Terminal proof-system discriminator.</summary>
    public required byte ProofSystem { get; init; }

    /// <summary>Expected L1 network magic, if any.</summary>
    public required uint? ExpectedNetwork { get; init; }

    /// <summary>L1 RPC endpoint resolved from chain layout.</summary>
    public required bool HasL1RpcEndpoint { get; init; }

    /// <summary>
    /// Offline publication-profile readiness (enabled + configured + outbox + L1 endpoint +
    /// non-zero profile/publisher bindings). L1 confirmation remains funded.
    /// </summary>
    public required bool IsPublicationProfileReady { get; init; }

    /// <summary>Expected L1 network magic is configured.</summary>
    public required bool HasExpectedNetwork { get; init; }

    /// <summary>Offline Gateway passport (profile + network + retry budget).</summary>
    public required bool IsOfflinePassportComplete { get; init; }

    /// <summary>Publication-profile replay domain as 0x-hex.</summary>
    public required string ReplayDomain { get; init; }

    /// <summary>Publication-profile verification key id as 0x-hex.</summary>
    public required string VerificationKeyId { get; init; }

    /// <summary>SettlementManager script hash as 0x-hex.</summary>
    public required string SettlementManagerHash { get; init; }

    /// <summary>MessageRouter script hash as 0x-hex.</summary>
    public required string MessageRouterHash { get; init; }

    /// <summary>True when this composition owns proof-prover disposal.</summary>
    public required bool OwnsProofProver { get; init; }

    /// <summary>True when an optional metrics sink was supplied at open.</summary>
    public required bool HasMetrics { get; init; }

    /// <summary>Metrics snapshot entry count when exportable; otherwise 0.</summary>
    public required int MetricsEntryCount { get; init; }

    /// <summary>Map a live status snapshot into a JSON document.</summary>
    public static GatewayHostOperatorStatusDocument From(GatewayHostOperatorStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new GatewayHostOperatorStatusDocument
        {
            ChainDirectory = status.ChainDirectory,
            HasPendingPublication = status.HasPendingPublication,
            PendingPublicationEpoch = status.PendingPublicationEpoch,
            AggregatorPendingCount = status.AggregatorPendingCount,
            HasDurableOutbox = status.HasDurableOutbox,
            IsPublicationConfigured = status.IsPublicationConfigured,
            IsEnabled = status.IsEnabled,
            MaxAutomaticRetries = status.MaxAutomaticRetries,
            OutboxQueueDepth = status.OutboxQueueDepth,
            PublicationState = status.PublicationState?.ToString(),
            OutboxRetryCount = status.OutboxRetryCount,
            OutboxLastError = status.OutboxLastError,
            ConfirmationLagMilliseconds = status.ConfirmationLagMilliseconds,
            AggregationBackendId = status.AggregationBackendId,
            ProofSystem = status.ProofSystem,
            ExpectedNetwork = status.ExpectedNetwork,
            HasL1RpcEndpoint = status.HasL1RpcEndpoint,
            IsPublicationProfileReady = status.IsPublicationProfileReady,
            HasExpectedNetwork = status.HasExpectedNetwork,
            IsOfflinePassportComplete = status.IsOfflinePassportComplete,
            ReplayDomain = status.ReplayDomain.ToString(),
            VerificationKeyId = status.VerificationKeyId.ToString(),
            SettlementManagerHash = status.SettlementManagerHash.ToString(),
            MessageRouterHash = status.MessageRouterHash.ToString(),
            OwnsProofProver = status.OwnsProofProver,
            HasMetrics = status.HasMetrics,
            MetricsEntryCount = status.MetricsEntryCount,
        };
    }
}
