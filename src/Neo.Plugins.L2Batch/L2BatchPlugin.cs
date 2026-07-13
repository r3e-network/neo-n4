using System;
using System.Linq;
using Neo.Extensions;
using Neo.Extensions.IO;
using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.ForcedInclusion;
using Neo.L2.Telemetry;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using Neo.SmartContract.Native;

namespace Neo.Plugins.L2;

/// <summary>
/// Listens to the local Neo 4 chain's <c>Blockchain.Committed</c> event and accumulates work
/// into an in-progress <c>L2Batch</c>. When the configured size, count, or age
/// thresholds trip, the plugin seals the exact execution inputs into a <see cref="SealedBatch"/>,
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
    private ISealedBatchSink? _sink;
    private Func<int, IReadOnlyList<CrossChainMessage>>? _l1MessageDrain;
    private Func<uint>? _l1FinalizedHeight;
    private Func<UInt256>? _sequencerCommitteeHash;
    private IForcedInclusionSource? _forcedInclusionSource;

    /// <summary>Emitted after a sealed batch has been durably accepted by the configured sink.</summary>
    public event EventHandler<SealedBatch>? OnBatchSealed;

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

    /// <summary>
    /// Wire real L1 inbox and block-context sources. Must be called before the first block is
    /// observed so an in-progress batch cannot mix input providers.
    /// </summary>
    public void WithSealingInputs(
        Func<int, IReadOnlyList<CrossChainMessage>> l1MessageDrain,
        Func<uint> l1FinalizedHeight,
        Func<UInt256> sequencerCommitteeHash)
    {
        ArgumentNullException.ThrowIfNull(l1MessageDrain);
        ArgumentNullException.ThrowIfNull(l1FinalizedHeight);
        ArgumentNullException.ThrowIfNull(sequencerCommitteeHash);
        if (_sealer is not null)
            throw new InvalidOperationException(
                "sealing inputs must be wired before the first block");
        _l1MessageDrain = l1MessageDrain;
        _l1FinalizedHeight = l1FinalizedHeight;
        _sequencerCommitteeHash = sequencerCommitteeHash;
    }

    /// <summary>
    /// Wire the L1 forced-inclusion read source. The durable settlement sink remains the
    /// reservation source of truth; this method never calls
    /// <see cref="IForcedInclusionSource.ConfirmConsumedAsync"/>.
    /// </summary>
    public void WithForcedInclusionSource(IForcedInclusionSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (_sealer is not null)
            throw new InvalidOperationException(
                "forced-inclusion source must be wired before the first block");
        if (_settings.ChainId != 0 && source.ChainId != _settings.ChainId)
            throw new InvalidOperationException(
                "forced-inclusion source chain differs from batch settings");
        _forcedInclusionSource = source;
    }

    /// <summary>
    /// Wire the single durable execution/DA/witness sink. A production batch is not acknowledged
    /// until this sink returns a validated post-state root.
    /// </summary>
    public void WithSealedBatchSink(ISealedBatchSink sink, uint chainId)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ChainIdValidator.ValidateL2(chainId);
        if (_sink is not null && !ReferenceEquals(_sink, sink))
            throw new InvalidOperationException("a sealed-batch sink is already wired");
        if (_sink is not null) return;
        if (_sealer is not null)
            throw new InvalidOperationException(
                "sealed-batch sink must be wired before the first block");
        if (_settings.ChainId != 0 && _settings.ChainId != chainId)
            throw new InvalidOperationException(
                "sealed-batch sink chain differs from batch settings");
        if (_forcedInclusionSource is not null
            && _forcedInclusionSource.ChainId != chainId)
            throw new InvalidOperationException(
                "forced-inclusion source chain differs from sealed-batch sink");

        var checkpoint = sink.GetLatestCheckpointAsync()
            .AsTask().GetAwaiter().GetResult();
        var sealer = CreateSealer(chainId);
        sealer.RestoreCheckpoint(checkpoint);
        _sink = sink;
        _sealer = sealer;
    }

    /// <summary>Remove a previously wired sink during orderly plugin shutdown.</summary>
    public void RemoveSealedBatchSink(ISealedBatchSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (ReferenceEquals(_sink, sink)) _sink = null;
    }

    /// <inheritdoc />
    public override string Name => "L2BatchPlugin";

    /// <inheritdoc />
    public override string Description =>
        "Seals immutable L2 execution batches for the Neo Elastic Network.";

    /// <summary>Construct and register the block-commit handler.</summary>
    public L2BatchPlugin()
    {
        Blockchain.Committed += OnBlockCommitted;
    }

    internal L2BatchPlugin(L2BatchSettings settings) : this()
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    /// <inheritdoc />
    /// <remarks>
    /// neo-project/neo master sealed the public <c>Dispose()</c> and routes cleanup through
    /// the standard <c>Dispose(bool disposing)</c> hook. Override that instead so the
    /// Blockchain.Committed handler is unsubscribed deterministically.
    /// </remarks>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Blockchain.Committed -= OnBlockCommitted;
        }
        base.Dispose(disposing);
    }

    /// <inheritdoc />
    protected override void Configure()
    {
        _settings = L2BatchSettings.From(GetConfiguration());
        // Do NOT reset _sealer here — that would discard batch-numbering state and the
        // in-progress builder if Configure ever runs more than once (config-watcher
        // re-fire, host re-init). Matches the iter-144 lazy-init pattern in the bridge
        // plugin. Settings updates here only take effect for a future fresh sealer; the
        // existing sealer keeps its captured settings (acceptable: re-configuration of
        // batch thresholds mid-flight isn't a supported operation).
    }

    /// <summary>Block-commit hook — entry point for batch accumulation.</summary>
    private void OnBlockCommitted(NeoSystem system, Block block)
    {
        try
        {
            if (!_settings.Enabled) return;
            if (block is null) return;

            RecoverAndProcessCommittedBlocks(system, block);
        }
        catch (Exception ex)
        {
            _metrics.SafeIncrementCounter("l2_batch_on_block_committed_error");
            Logs.RuntimeLogger.Error(ex, "L2Batch OnBlockCommitted handler failed");
            throw;
        }
    }

    private void RecoverAndProcessCommittedBlocks(NeoSystem system, Block currentBlock)
    {
        var sealer = _sealer
            ?? throw new InvalidOperationException(
                "sealed-batch checkpoint was not restored before block recovery");
        var expected = sealer.NextExpectedBlock
            ?? throw new InvalidOperationException(
                "sealed-batch recovery has no expected block checkpoint");
        if (expected > uint.MaxValue)
            throw new InvalidOperationException(
                $"durable block checkpoint {expected} exceeds the supported L2 block index");

        for (var index = expected; index < currentBlock.Index; index++)
        {
            var recovered = NativeContract.Ledger.GetBlock(
                system.StoreView, checked((uint)index))
                ?? throw new InvalidOperationException(
                    $"committed L2 block {index} is missing from the local ledger; recovery cannot skip it");
            ProcessCommittedBlock(
                recovered.Index,
                recovered.Timestamp,
                system.Settings.Network,
                recovered.Transactions.Select(transaction => transaction.ToArray()));
        }

        ProcessCommittedBlock(
            currentBlock.Index,
            currentBlock.Timestamp,
            system.Settings.Network,
            currentBlock.Transactions.Select(transaction => transaction.ToArray()));
    }

    /// <summary>
    /// Process one committed block through the durable pending/persist/ack hand-off.
    /// </summary>
    /// <remarks>
    /// Internal for deterministic recovery tests. A pending sealed batch is retried and
    /// acknowledged before this method accepts a later block.
    /// </remarks>
    internal void ProcessCommittedBlock(
        uint blockIndex,
        ulong blockTimestamp,
        uint network,
        IEnumerable<byte[]> rawTransactions)
    {
        ArgumentNullException.ThrowIfNull(rawTransactions);
        if (!_settings.Enabled) return;
        var sink = _sink
            ?? throw new InvalidOperationException(
                "committed blocks require a durable sealed-batch sink");
        var sealer = _sealer
            ?? throw new InvalidOperationException(
                "sealed-batch checkpoint was not restored before block processing");

        var pending = sealer.PendingBatch;
        if (pending is not null)
        {
            PersistAndAcknowledge(sealer, sink, pending);
            if (blockIndex == pending.LastBlock) return;
            if (blockIndex < pending.LastBlock)
                throw new InvalidOperationException(
                    $"duplicate or out-of-order L2 block {blockIndex}; pending batch ended at {pending.LastBlock}");
        }

        var artifact = sealer.OnBlockCommit(
            blockIndex, blockTimestamp, network, rawTransactions);
        if (artifact is not null)
            PersistAndAcknowledge(sealer, sink, artifact);
    }

    private void PersistAndAcknowledge(
        BatchSealer sealer,
        ISealedBatchSink sink,
        SealedBatch batch)
    {
        var postStateRoot = sink.PersistAsync(batch)
            .AsTask().GetAwaiter().GetResult();
        ArgumentNullException.ThrowIfNull(postStateRoot);
        sealer.AcknowledgeExecution(batch.BatchNumber, postStateRoot);
        DispatchSealed(this, OnBatchSealed, batch, _metrics);
    }

    private BatchSealer CreateSealer(uint chainId)
    {
        var settings = _settings.ChainId == chainId
            ? _settings
            : new L2BatchSettings
            {
                ChainId = chainId,
                MaxBlocksPerBatch = _settings.MaxBlocksPerBatch,
                MaxTransactionsPerBatch = _settings.MaxTransactionsPerBatch,
                MaxBatchAgeMillis = _settings.MaxBatchAgeMillis,
                Enabled = _settings.Enabled,
            };
        return new BatchSealer(
            settings,
            _metrics,
            forcedDrain: _forcedInclusionSource is null
                ? null
                : DrainUnreservedForcedTransactions,
            l1MessageDrain: _l1MessageDrain,
            l1FinalizedHeight: _l1FinalizedHeight,
            sequencerCommitteeHash: _sequencerCommitteeHash);
    }

    private IReadOnlyList<(ulong Nonce, UInt256 TxHash, ReadOnlyMemory<byte> SerializedTx)>
        DrainUnreservedForcedTransactions(int maximum)
    {
        var source = _forcedInclusionSource
            ?? throw new InvalidOperationException(
                "forced-inclusion source is not wired");
        var sink = _sink
            ?? throw new InvalidOperationException(
                "forced inclusion requires a durable sealed-batch sink");
        var tracked = sink.GetTrackedForcedInclusionNoncesAsync(source.ChainId)
            .AsTask().GetAwaiter().GetResult();
        var trackedSet = tracked.ToHashSet();
        var entries = source.DrainAsync(int.MaxValue)
            .AsTask().GetAwaiter().GetResult()
            ?? throw new InvalidOperationException(
                "forced-inclusion source returned null");
        return entries
            .Where(entry => entry is not null && !trackedSet.Contains(entry.Nonce))
            .Take(maximum)
            .Select(entry => (entry.Nonce, entry.TxHash, entry.SerializedTx))
            .ToArray();
    }

    /// <summary>
    /// Dispatch a sealed-batch event to every subscriber, isolating each subscriber's failure
    /// so one buggy listener can't surface its exception to Neo's Blockchain.Committed and
    /// destabilize block import. Failures bump <see cref="MetricNames.BatchSealedSubscriberFailures"/>.
    /// </summary>
    /// <remarks>
    /// Internal so unit tests can drive it without spinning up a NeoSystem; the real
    /// production caller is the private OnBlockCommitted handler above.
    /// </remarks>
    internal static void DispatchSealed(
        object sender,
        EventHandler<SealedBatch>? handler,
        SealedBatch artifact,
        IL2Metrics metrics)
    {
        var subscribers = handler?.GetInvocationList();
        if (subscribers is null) return;
        foreach (var sub in subscribers)
        {
            // Subscriber isolation: a subscriber failure must not surface as a
            // caller-visible error, which would leave the caller out of sync
            // with the sealed batch. Catch all non-fatal exceptions.
            // Fatal exceptions (OOM, SO) terminate the process and can't be caught.
            try { ((EventHandler<SealedBatch>)sub).Invoke(sender, artifact); }
            catch (Exception) { metrics.SafeIncrementCounter(MetricNames.BatchSealedSubscriberFailures); }
        }
    }
}
