namespace Neo.L2.Gateway.Rpc;

/// <summary>
/// Compact Gateway host health probe for ops scripts (passport / outbox / publication).
/// </summary>
/// <remarks>
/// See doc.md §4 / §14.2. Smaller than <see cref="GatewayHostOperatorStatusDocument"/>;
/// does not claim L1 publication confirmation (funded gate).
/// Produced by <see cref="GatewayHostComposition.GetHealthProbe"/> /
/// <see cref="GatewayHostComposition.WriteHealthProbeAsync"/>.
/// </remarks>
public sealed record GatewayHostHealthProbeDocument
{
    /// <summary>Offline publication-profile passport is complete.</summary>
    public required bool IsOfflinePassportComplete { get; init; }

    /// <summary>Failed offline passport checks (empty when complete).</summary>
    public required IReadOnlyList<string> OfflinePassportFailures { get; init; }

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
