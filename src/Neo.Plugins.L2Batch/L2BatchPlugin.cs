using System;
using System.Linq;
using Neo.Extensions;
using Neo.L2;
using Neo.L2.Telemetry;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;

namespace Neo.Plugins.L2;

/// <summary>
/// Listens to the local Neo 4 chain's <c>Blockchain.Committed</c> event and accumulates work
/// into an in-progress <c>L2Batch</c>. When the configured size, count, or age
/// thresholds trip, the plugin seals the batch into an <see cref="L2BatchCommitment"/>,
/// publishes it via <see cref="OnBatchSealed"/>, and starts a fresh batch.
/// </summary>
/// <remarks>
/// See doc.md §7.2 (Batcher) and §15.1 (transaction flow). This plugin owns the batcher's
/// lifecycle but does NOT submit batches to NeoHub — that is <c>L2SettlementPlugin</c>'s job.
/// The actual seal logic lives on <see cref="BatchSealer"/> so it's testable without a node.
/// </remarks>
public sealed class L2BatchPlugin : Plugin
{
    private L2BatchSettings _settings = new();
    private IL2Metrics _metrics = NoOpMetrics.Instance;
    private BatchSealer? _sealer;

    /// <summary>Emitted whenever a batch is sealed and ready for submission.</summary>
    public event EventHandler<L2BatchCommitment>? OnBatchSealed;

    /// <summary>
    /// Wire a metrics sink. The plugin's <see cref="BatchSealer"/> emits
    /// <c>l2.batch.sealed</c>, <c>l2.batch.seal_latency_ms</c>, and <c>l2.batch.tx_count</c>
    /// against this sink. Defaults to <see cref="NoOpMetrics"/>.
    /// Swaps the sink in-place on the existing sealer so batch-numbering / state-root /
    /// in-progress-builder state survives a re-wire.
    /// </summary>
    public void WithMetrics(IL2Metrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        _metrics = metrics;
        _sealer?.WithMetrics(metrics);
    }

    /// <inheritdoc />
    public override string Name => "L2BatchPlugin";

    /// <inheritdoc />
    public override string Description => "Accumulates L2 blocks into batch commitments for the Neo Elastic Network.";

    /// <summary>Construct and register the block-commit handler.</summary>
    public L2BatchPlugin()
    {
        Blockchain.Committed += OnBlockCommitted;
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        Blockchain.Committed -= OnBlockCommitted;
    }

    /// <inheritdoc />
    protected override void Configure()
    {
        _settings = L2BatchSettings.From(GetConfiguration());
        _sealer = null;
    }

    /// <summary>Block-commit hook — entry point for batch accumulation.</summary>
    private void OnBlockCommitted(NeoSystem system, Block block)
    {
        if (!_settings.Enabled) return;
        if (block is null) return;

        var sealer = _sealer ??= new BatchSealer(_settings, _metrics);
        var rawTxs = block.Transactions.Select(tx => tx.ToArray());
        var commitment = sealer.OnBlockCommit(block.Index, block.Timestamp, system.Settings.Network, rawTxs);
        if (commitment is not null)
            OnBatchSealed?.Invoke(this, commitment);
    }
}
