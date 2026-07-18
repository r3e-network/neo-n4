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

    /// <summary>Offline publication-profile passport is complete.</summary>
    public required bool IsOfflinePassportComplete { get; init; }

    /// <summary>Failed offline passport checks (empty when complete).</summary>
    public required IReadOnlyList<string> OfflinePassportFailures { get; init; }

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

    /// <summary>Alias of <see cref="IsPublicationHealthy"/>.</summary>
    public required bool IsGatewayHostHealthy { get; init; }

    /// <summary>Alias of <see cref="PublicationHealthFailures"/>.</summary>
    public required IReadOnlyList<string> GatewayHostHealthFailures { get; init; }
}
