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
    /// Does not claim L1 confirmation (funded gate). True when
    /// <see cref="OfflinePassportFailures"/> is empty.
    /// </summary>
    public required bool IsOfflinePassportComplete { get; init; }

    /// <summary>
    /// Names of offline Gateway passport checks that failed (empty when complete).
    /// </summary>
    public required IReadOnlyList<string> OfflinePassportFailures { get; init; }

    /// <summary>True when durable outbox publication state is poisoned.</summary>
    public required bool IsOutboxPoisoned { get; init; }

    /// <summary>
    /// True when no pending publication, empty durable queue, no last error, and not poisoned.
    /// Runtime idle health (not an L1 confirmation claim).
    /// </summary>
    public required bool IsOutboxIdle { get; init; }

    /// <summary>
    /// <see cref="IsOfflinePassportComplete"/> and <see cref="IsOutboxIdle"/>. Offline config +
    /// local queue health; L1 confirmation remains funded.
    /// True when <see cref="PublicationHealthFailures"/> is empty.
    /// </summary>
    public required bool IsPublicationHealthy { get; init; }

    /// <summary>
    /// Names of publication health checks that failed (empty when healthy). Combines offline
    /// passport rollup with outbox/queue idle diagnostics. Not an L1 confirmation claim.
    /// </summary>
    public required IReadOnlyList<string> PublicationHealthFailures { get; init; }

    /// <summary>
    /// Combined Gateway host local health: publication health + metrics HTTP health
    /// (when a metrics plugin is enabled). Naming parity with LocalHost
    /// <c>IsLocalHostHealthy</c>. Not an L1 confirmation claim.
    /// </summary>
    public required bool IsGatewayHostHealthy { get; init; }

    /// <summary>
    /// Failed Gateway host health checks (publication + optional metrics HTTP).
    /// Empty when <see cref="IsGatewayHostHealthy"/>.
    /// </summary>
    public required IReadOnlyList<string> GatewayHostHealthFailures { get; init; }

    /// <summary>True when an <c>L2MetricsPlugin</c> was supplied for metrics HTTP control.</summary>
    public required bool HasMetricsPlugin { get; init; }

    /// <summary>Metrics plugin is enabled in settings (false when no plugin).</summary>
    public required bool IsMetricsEnabled { get; init; }

    /// <summary>Metrics HTTP server is listening (BoundPort &gt; 0).</summary>
    public required bool IsMetricsHttpListening { get; init; }

    /// <summary>Metrics HTTP bound port (0 when not listening).</summary>
    public required int MetricsBoundPort { get; init; }

    /// <summary>Metrics plugin has a <c>/readyz</c> readiness predicate installed.</summary>
    public required bool HasMetricsReadinessCheck { get; init; }

    /// <summary>Metrics plugin has a <c>/healthprobe</c> body provider installed.</summary>
    public required bool HasMetricsHealthProbe { get; init; }

    /// <summary>Metrics plugin has a <c>/operatorstatus</c> body provider installed.</summary>
    public required bool HasMetricsOperatorStatus { get; init; }

    /// <summary>
    /// Metrics HTTP health failure names (empty when metrics disabled / no plugin, or healthy).
    /// </summary>
    public required IReadOnlyList<string> MetricsHttpHealthFailures { get; init; }

    /// <summary>True when <see cref="MetricsHttpHealthFailures"/> is empty.</summary>
    public required bool IsMetricsHttpHealthy { get; init; }

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

    /// <summary>
    /// True when the durable outbox/aggregator is idle: no pending publication, empty queue,
    /// no last error, and not poisoned. Not an L1 confirmation claim.
    /// </summary>
    public static bool IsOutboxRuntimeIdle(
        bool hasPendingPublication,
        int aggregatorPendingCount,
        int outboxQueueDepth,
        string? outboxLastError,
        bool isOutboxPoisoned)
        => !hasPendingPublication
            && aggregatorPendingCount == 0
            && outboxQueueDepth == 0
            && string.IsNullOrEmpty(outboxLastError)
            && !isOutboxPoisoned;

    /// <summary>
    /// Build offline Gateway passport failure names from publication-profile wiring flags.
    /// Empty list means <see cref="IsOfflinePassportComplete"/>. Not an L1 confirmation claim.
    /// </summary>
    public static IReadOnlyList<string> BuildOfflinePassportFailures(
        bool isEnabled,
        bool isPublicationConfigured,
        bool hasDurableOutbox,
        bool hasL1RpcEndpoint,
        bool hasNonZeroReplayDomain,
        bool hasNonZeroVerificationKeyId,
        bool hasNonZeroSettlementManagerHash,
        bool hasNonZeroMessageRouterHash,
        bool hasExpectedNetwork,
        bool hasPositiveMaxAutomaticRetries)
    {
        var failures = new List<string>();
        if (!isEnabled) failures.Add(nameof(IsEnabled));
        if (!isPublicationConfigured) failures.Add(nameof(IsPublicationConfigured));
        if (!hasDurableOutbox) failures.Add(nameof(HasDurableOutbox));
        if (!hasL1RpcEndpoint) failures.Add(nameof(HasL1RpcEndpoint));
        if (!hasNonZeroReplayDomain) failures.Add(nameof(ReplayDomain));
        if (!hasNonZeroVerificationKeyId) failures.Add(nameof(VerificationKeyId));
        if (!hasNonZeroSettlementManagerHash) failures.Add(nameof(SettlementManagerHash));
        if (!hasNonZeroMessageRouterHash) failures.Add(nameof(MessageRouterHash));
        if (!hasExpectedNetwork) failures.Add(nameof(HasExpectedNetwork));
        if (!hasPositiveMaxAutomaticRetries) failures.Add(nameof(MaxAutomaticRetries));
        return failures;
    }

    /// <summary>
    /// Build publication health failure names from offline passport + outbox/queue runtime state.
    /// Empty list means <see cref="IsPublicationHealthy"/>. Not an L1 confirmation claim.
    /// </summary>
    public static IReadOnlyList<string> BuildPublicationHealthFailures(
        bool offlinePassportComplete,
        bool isOutboxPoisoned,
        bool hasPendingPublication,
        int aggregatorPendingCount,
        int outboxQueueDepth,
        string? outboxLastError)
    {
        var failures = new List<string>();
        if (!offlinePassportComplete)
            failures.Add(nameof(IsOfflinePassportComplete));
        if (isOutboxPoisoned)
            failures.Add(nameof(IsOutboxPoisoned));
        if (hasPendingPublication)
            failures.Add(nameof(HasPendingPublication));
        if (aggregatorPendingCount != 0)
            failures.Add(nameof(AggregatorPendingCount));
        if (outboxQueueDepth != 0)
            failures.Add(nameof(OutboxQueueDepth));
        if (!string.IsNullOrEmpty(outboxLastError))
            failures.Add(nameof(OutboxLastError));
        return failures;
    }

    /// <summary>
    /// Build metrics HTTP health failure names. When metrics are disabled or no plugin is
    /// attached, returns empty (not required). When enabled, requires wiring + listening +
    /// <c>/readyz</c> + <c>/healthprobe</c> + <c>/operatorstatus</c> body providers
    /// (LocalHost metrics HTTP parity).
    /// </summary>
    public static IReadOnlyList<string> BuildMetricsHttpHealthFailures(
        bool metricsEnabled,
        bool metricsWiringComplete,
        bool metricsHttpListening,
        bool hasMetricsReadinessCheck,
        bool hasMetricsHealthProbe,
        bool hasMetricsOperatorStatus)
    {
        if (!metricsEnabled)
            return Array.Empty<string>();

        var failures = new List<string>();
        if (!metricsWiringComplete)
            failures.Add(nameof(HasMetricsPlugin));
        if (!metricsHttpListening)
            failures.Add(nameof(IsMetricsHttpListening));
        if (!hasMetricsReadinessCheck)
            failures.Add(nameof(HasMetricsReadinessCheck));
        if (!hasMetricsHealthProbe)
            failures.Add(nameof(HasMetricsHealthProbe));
        if (!hasMetricsOperatorStatus)
            failures.Add(nameof(HasMetricsOperatorStatus));
        return failures;
    }

    /// <summary>
    /// Union of publication and metrics HTTP health failure names for combined Gateway host
    /// health. Empty means <see cref="IsGatewayHostHealthy"/>.
    /// </summary>
    public static IReadOnlyList<string> BuildGatewayHostHealthFailures(
        IReadOnlyList<string> publicationHealthFailures,
        IReadOnlyList<string> metricsHttpHealthFailures)
    {
        ArgumentNullException.ThrowIfNull(publicationHealthFailures);
        ArgumentNullException.ThrowIfNull(metricsHttpHealthFailures);
        if (publicationHealthFailures.Count == 0 && metricsHttpHealthFailures.Count == 0)
            return Array.Empty<string>();
        if (metricsHttpHealthFailures.Count == 0)
            return publicationHealthFailures;
        if (publicationHealthFailures.Count == 0)
            return metricsHttpHealthFailures;
        var merged = new List<string>(publicationHealthFailures.Count + metricsHttpHealthFailures.Count);
        merged.AddRange(publicationHealthFailures);
        merged.AddRange(metricsHttpHealthFailures);
        return merged;
    }
}
