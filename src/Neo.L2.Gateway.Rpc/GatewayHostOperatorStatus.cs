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

    /// <summary>
    /// Commitments pending aggregation in the in-process aggregator (0 when empty).
    /// Distinct from durable outbox depth / L1 confirmation lag.
    /// </summary>
    public required int AggregatorPendingCount { get; init; }

    /// <summary>True when a durable Gateway outbox is attached.</summary>
    public required bool HasDurableOutbox { get; init; }

    /// <summary>
    /// True when production global-root publication is configured (proof prover + L1
    /// publisher wiring). L1 confirmation of a specific epoch remains a funded gate.
    /// </summary>
    public required bool IsPublicationConfigured { get; init; }

    /// <summary>Whether the Gateway plugin is enabled in settings.</summary>
    public required bool IsEnabled { get; init; }

    /// <summary>Configured max automatic publication retries before poison.</summary>
    public required int MaxAutomaticRetries { get; init; }

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

    /// <summary>Terminal proof-system discriminator.</summary>
    public required byte ProofSystem { get; init; }

    /// <summary>Expected L1 network magic from chain layout, if any.</summary>
    public required uint? ExpectedNetwork { get; init; }

    /// <summary>L1 RPC endpoint resolved from chain layout (not connectivity-probed).</summary>
    public required bool HasL1RpcEndpoint { get; init; }

    /// <summary>
    /// Offline publication-profile readiness: enabled + publication configured + durable outbox
    /// + L1 endpoint present + non-zero replay domain / verification key / publisher hashes.
    /// Does not claim L1 confirmation (funded gate).
    /// </summary>
    public required bool IsPublicationProfileReady { get; init; }

    /// <summary>True when expected L1 network magic is configured.</summary>
    public required bool HasExpectedNetwork { get; init; }

    /// <summary>
    /// Offline Gateway passport: publication profile ready + expected network + retry budget.
    /// Does not claim L1 confirmation (funded gate).
    /// </summary>
    public required bool IsOfflinePassportComplete { get; init; }

    /// <summary>Publication-profile replay domain bound at open.</summary>
    public required UInt256 ReplayDomain { get; init; }

    /// <summary>Publication-profile verification key id bound at open.</summary>
    public required UInt256 VerificationKeyId { get; init; }

    /// <summary>SettlementManager script hash bound on the publisher.</summary>
    public required UInt160 SettlementManagerHash { get; init; }

    /// <summary>MessageRouter script hash bound on the publisher.</summary>
    public required UInt160 MessageRouterHash { get; init; }

    /// <summary>True when this composition owns proof-prover disposal.</summary>
    public required bool OwnsProofProver { get; init; }

    /// <summary>True when an optional metrics sink was supplied at open.</summary>
    public required bool HasMetrics { get; init; }

    /// <summary>
    /// Counter+gauge+histogram entry count when <see cref="HasMetrics"/> and the sink
    /// implements <see cref="Neo.L2.Telemetry.IMetricsSource"/>; otherwise 0.
    /// </summary>
    public required int MetricsEntryCount { get; init; }
}
