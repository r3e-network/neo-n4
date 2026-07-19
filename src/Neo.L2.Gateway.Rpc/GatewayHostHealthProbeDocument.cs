using System.Text.Json;
using Neo.Plugins.L2Gateway;

namespace Neo.L2.Gateway.Rpc;

/// <summary>
/// Compact Gateway host health probe for ops scripts (passport / outbox / publication).
/// </summary>
/// <remarks>
/// See doc.md §4 / §14.2. Smaller than <see cref="GatewayHostOperatorStatusDocument"/>;
/// does not claim L1 publication confirmation (funded gate).
/// Produced by <see cref="GatewayHostComposition.GetHealthProbe"/> /
/// <see cref="GatewayHostComposition.FormatHealthProbeJson"/> /
/// <see cref="GatewayHostComposition.WriteHealthProbeAsync"/>.
/// </remarks>
public sealed record GatewayHostHealthProbeDocument
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Serialize as indented camelCase JSON (trailing newline) for ops scripts and
    /// host composition <c>FormatHealthProbeJson</c>.
    /// </summary>
    public static string FormatJson(GatewayHostHealthProbeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(document, JsonOptions) + Environment.NewLine;
    }

    /// <summary>Absolute chain working directory (ops identity for multi-host layouts).</summary>
    public required string ChainDirectory { get; init; }

    /// <summary>Offline publication-profile passport is complete.</summary>
    public required bool IsOfflinePassportComplete { get; init; }

    /// <summary>Failed offline passport checks (empty when complete).</summary>
    public required IReadOnlyList<string> OfflinePassportFailures { get; init; }

    /// <summary>Gateway plugin enabled in settings.</summary>
    public required bool IsEnabled { get; init; }

    /// <summary>Production global-root publication is configured offline.</summary>
    public required bool IsPublicationConfigured { get; init; }

    /// <summary>Durable Gateway outbox is attached.</summary>
    public required bool HasDurableOutbox { get; init; }

    /// <summary>L1 JSON-RPC endpoint is configured for publication.</summary>
    public required bool HasL1RpcEndpoint { get; init; }

    /// <summary>Expected network magic is configured (offline).</summary>
    public required bool HasExpectedNetwork { get; init; }

    /// <summary>Configured expected network magic when set; otherwise null.</summary>
    public required uint? ExpectedNetwork { get; init; }

    /// <summary>Publication profile wiring is ready (offline composite).</summary>
    public required bool IsPublicationProfileReady { get; init; }

    /// <summary>Configured max automatic publication retries before poison.</summary>
    public required int MaxAutomaticRetries { get; init; }

    /// <summary>
    /// Terminal proof-system discriminator (helps interpret publication-profile readiness
    /// without the full operator status dump).
    /// </summary>
    public required byte ProofSystem { get; init; }

    /// <summary>Terminal aggregation backend id from the proof prover.</summary>
    public required byte AggregationBackendId { get; init; }

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

    /// <summary>
    /// Metrics snapshot entry count when exportable; otherwise 0 (soft local only).
    /// </summary>
    public required int MetricsEntryCount { get; init; }

    /// <summary>Optional metrics sink was supplied at open.</summary>
    public required bool HasMetrics { get; init; }

    /// <summary>L2MetricsPlugin attached for metrics HTTP control.</summary>
    public required bool HasMetricsPlugin { get; init; }

    /// <summary>Metrics plugin enabled in settings.</summary>
    public required bool IsMetricsEnabled { get; init; }

    /// <summary>
    /// Configured metrics HTTP port from plugin settings (0 when no plugin). Distinct from
    /// bound port after <c>StartMetricsHttp</c>.
    /// </summary>
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

    /// <summary>Metrics HTTP health is clean (or not required).</summary>
    public required bool IsMetricsHttpHealthy { get; init; }

    /// <summary>Metrics HTTP health failure names (empty when healthy / N/A).</summary>
    public required IReadOnlyList<string> MetricsHttpHealthFailures { get; init; }

    /// <summary>Unconfirmed publication remains retryable or poisoned.</summary>
    public required bool HasPendingPublication { get; init; }

    /// <summary>Pending publication epoch when present; otherwise null.</summary>
    public required ulong? PendingPublicationEpoch { get; init; }

    /// <summary>In-process aggregator pending commitment count.</summary>
    public required int AggregatorPendingCount { get; init; }

    /// <summary>Durable outbox queue depth.</summary>
    public required int OutboxQueueDepth { get; init; }

    /// <summary>Consecutive outbox retry count.</summary>
    public required int OutboxRetryCount { get; init; }

    /// <summary>Most recent outbox error, if any.</summary>
    public required string? OutboxLastError { get; init; }

    /// <summary>Confirmation lag in milliseconds from durable outbox status.</summary>
    public required long ConfirmationLagMilliseconds { get; init; }

    /// <summary>Current publication state, or null when none is active.</summary>
    public required GatewayOutboxState? PublicationState { get; init; }

    /// <summary>Durable outbox is poisoned.</summary>
    public required bool IsOutboxPoisoned { get; init; }

    /// <summary>Durable outbox/aggregator is idle.</summary>
    public required bool IsOutboxIdle { get; init; }

    /// <summary>Publication health (passport + outbox idle) is clean.</summary>
    public required bool IsPublicationHealthy { get; init; }

    /// <summary>Failed publication health checks (empty when healthy).</summary>
    public required IReadOnlyList<string> PublicationHealthFailures { get; init; }

    /// <summary>
    /// Combined Gateway host health (publication + metrics HTTP when plugin enabled).
    /// </summary>
    public required bool IsGatewayHostHealthy { get; init; }

    /// <summary>
    /// Failed Gateway host health checks (publication + optional metrics HTTP).
    /// </summary>
    public required IReadOnlyList<string> GatewayHostHealthFailures { get; init; }
}
