using Microsoft.Extensions.Configuration;
using Neo.L2;

namespace Neo.Plugins.L2Gateway;

/// <summary>
/// Phase 5 plugin: receives sealed batches from one or more L2 chains via
/// <see cref="ReceiveBatch"/>, aggregates them through the registered
/// <see cref="IGatewayAggregator"/>, and surfaces the aggregated commitment for downstream
/// submission to <c>NeoHub.SettlementManager</c>.
/// </summary>
public sealed class L2GatewayPlugin : Plugin
{
    private volatile IGatewayAggregator _aggregator = new PassThroughAggregator();
    private bool _enabled = true;

    /// <inheritdoc />
    public override string Name => "L2GatewayPlugin";

    /// <inheritdoc />
    public override string Description => "Aggregates per-chain L2 batch commitments into a single Gateway commitment.";

    /// <summary>Replace the active aggregator. This is a configuration-time method;
    /// calling it while <see cref="ReceiveBatch"/> or <see cref="PullAggregate"/> is
    /// in flight on another thread is safe (the write is a single reference swap) but
    /// the old aggregator may receive the in-flight batch.</summary>
    public void UseAggregator(IGatewayAggregator aggregator)
    {
        ArgumentNullException.ThrowIfNull(aggregator);
        _aggregator = aggregator;
    }

    /// <summary>The currently active aggregator.</summary>
    public IGatewayAggregator Aggregator => _aggregator;

    /// <summary>Forward a per-chain sealed batch into the aggregator.</summary>
    public void ReceiveBatch(L2BatchCommitment commitment)
    {
        // Surface null at the API boundary instead of relying on the aggregator's own
        // guard — same iter-148/183 boundary pattern. A null commitment would otherwise
        // produce a generic ArgumentNullException with no link to the L2GatewayPlugin
        // call site.
        ArgumentNullException.ThrowIfNull(commitment);
        if (!_enabled) return;
        _aggregator.Submit(commitment);
    }

    /// <summary>Produce the next aggregated commitment (or <c>null</c> if nothing is pending).</summary>
    public AggregatedCommitment? PullAggregate() => _aggregator.Aggregate();

    /// <inheritdoc />
    protected override void Configure()
    {
        var section = GetConfiguration();
        _enabled = section.GetValue("Enabled", true);
    }
}
