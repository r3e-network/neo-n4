using System.Text.Json;

namespace Neo.L2.Gateway.Rpc;

/// <summary>
/// JSON-serializable projection of <see cref="GatewayHostOperatorStatus"/> for host health files.
/// </summary>
/// <remarks>
/// See doc.md §4 / §14.2. Produced by <see cref="GatewayHostComposition.FormatOperatorStatusJson"/> /
/// <see cref="GatewayHostComposition.WriteOperatorStatusAsync"/>.
/// L1 confirmation remains a funded gate and is not claimed here.
/// </remarks>
public sealed record GatewayHostOperatorStatusDocument
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Serialize as indented camelCase JSON (trailing newline) for ops scripts and
    /// host composition <c>FormatOperatorStatusJson</c>.
    /// </summary>
    public static string FormatJson(GatewayHostOperatorStatusDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(document, JsonOptions) + Environment.NewLine;
    }

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

    /// <summary>Failed offline Gateway passport check names (empty when complete).</summary>
    public required IReadOnlyList<string> OfflinePassportFailures { get; init; }

    /// <summary>Durable outbox is poisoned.</summary>
    public required bool IsOutboxPoisoned { get; init; }

    /// <summary>Durable outbox is idle (no pending work / error / poison).</summary>
    public required bool IsOutboxIdle { get; init; }

    /// <summary>Offline passport complete and outbox idle.</summary>
    public required bool IsPublicationHealthy { get; init; }

    /// <summary>Failed publication health check names (empty when healthy).</summary>
    public required IReadOnlyList<string> PublicationHealthFailures { get; init; }

    /// <summary>Combined Gateway host local health (publication + metrics HTTP).</summary>
    public required bool IsGatewayHostHealthy { get; init; }

    /// <summary>Failed Gateway host health checks (publication + metrics HTTP).</summary>
    public required IReadOnlyList<string> GatewayHostHealthFailures { get; init; }

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

    /// <summary>L2MetricsPlugin attached for metrics HTTP control.</summary>
    public required bool HasMetricsPlugin { get; init; }

    /// <summary>Metrics plugin enabled in settings.</summary>
    public required bool IsMetricsEnabled { get; init; }

    /// <summary>Configured metrics HTTP port from plugin settings (0 when no plugin).</summary>
    public required int MetricsConfiguredPort { get; init; }

    /// <summary>Configured metrics bind address (empty when no plugin).</summary>
    public required string MetricsBindAddress { get; init; }

    /// <summary>Configured max concurrent metrics HTTP connections (0 when no plugin).</summary>
    public required int MetricsMaxConcurrentConnections { get; init; }

    /// <summary>Metrics HTTP is listening.</summary>
    public required bool IsMetricsHttpListening { get; init; }

    /// <summary>Metrics HTTP bound port (0 when not listening).</summary>
    public required int MetricsBoundPort { get; init; }

    /// <summary>Metrics /readyz predicate installed.</summary>
    public required bool HasMetricsReadinessCheck { get; init; }

    /// <summary>Metrics /healthprobe body installed.</summary>
    public required bool HasMetricsHealthProbe { get; init; }

    /// <summary>Metrics /operatorstatus body installed.</summary>
    public required bool HasMetricsOperatorStatus { get; init; }

    /// <summary>Metrics HTTP health failure names.</summary>
    public required IReadOnlyList<string> MetricsHttpHealthFailures { get; init; }

    /// <summary>Metrics HTTP health is clean (or not required).</summary>
    public required bool IsMetricsHttpHealthy { get; init; }

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
            OfflinePassportFailures = status.OfflinePassportFailures,
            IsOutboxPoisoned = status.IsOutboxPoisoned,
            IsOutboxIdle = status.IsOutboxIdle,
            IsPublicationHealthy = status.IsPublicationHealthy,
            PublicationHealthFailures = status.PublicationHealthFailures,
            IsGatewayHostHealthy = status.IsGatewayHostHealthy,
            GatewayHostHealthFailures = status.GatewayHostHealthFailures,
            ReplayDomain = status.ReplayDomain.ToString(),
            VerificationKeyId = status.VerificationKeyId.ToString(),
            SettlementManagerHash = status.SettlementManagerHash.ToString(),
            MessageRouterHash = status.MessageRouterHash.ToString(),
            OwnsProofProver = status.OwnsProofProver,
            HasMetrics = status.HasMetrics,
            MetricsEntryCount = status.MetricsEntryCount,
            HasMetricsPlugin = status.HasMetricsPlugin,
            IsMetricsEnabled = status.IsMetricsEnabled,
            MetricsConfiguredPort = status.MetricsConfiguredPort,
            MetricsBindAddress = status.MetricsBindAddress,
            MetricsMaxConcurrentConnections = status.MetricsMaxConcurrentConnections,
            IsMetricsHttpListening = status.IsMetricsHttpListening,
            MetricsBoundPort = status.MetricsBoundPort,
            HasMetricsReadinessCheck = status.HasMetricsReadinessCheck,
            HasMetricsHealthProbe = status.HasMetricsHealthProbe,
            HasMetricsOperatorStatus = status.HasMetricsOperatorStatus,
            MetricsHttpHealthFailures = status.MetricsHttpHealthFailures,
            IsMetricsHttpHealthy = status.IsMetricsHttpHealthy,
        };
    }
}
