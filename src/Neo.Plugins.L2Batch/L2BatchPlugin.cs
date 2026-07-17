using System;
using System.Linq;
using Neo.Extensions;
using Neo.Extensions.IO;
using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.Bridge;
using Neo.L2.ForcedInclusion;
using Neo.L2.Messaging;
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
/// See doc.md §7.2 (Batcher) and §15.1 / §15.2 (transaction + deposit flow). This plugin owns
/// the batcher's lifecycle but does NOT submit batches to NeoHub — that is
/// <c>L2SettlementPlugin</c>'s job. SharedBridge deposits wired via
/// <see cref="WithDepositSource"/> are drained into each batch and permanently confirmed only
/// after durable seal persistence succeeds. The actual seal logic lives on
/// <see cref="BatchSealer"/> so it can be unit-tested without a Neo node.
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
    private ISharedBridgeDepositSource? _depositSource;
    private IMessageRouter? _messageRouter;

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
    /// Wire the SharedBridge deposit source. When sealing inputs have not yet been set, a
    /// deposit-only drain is installed automatically (Scan then Drain at seal time). Prefer
    /// <see cref="WireL1MessageInbox"/> when MessageRouter traffic is also present.
    /// </summary>
    public void WithDepositSource(ISharedBridgeDepositSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (_sealer is not null)
            throw new InvalidOperationException(
                "deposit source must be wired before the first block");
        if (_settings.ChainId != 0 && source.ChainId != _settings.ChainId)
            throw new InvalidOperationException(
                "deposit source chain differs from batch settings");
        if (_depositSource is not null && !ReferenceEquals(_depositSource, source))
            throw new InvalidOperationException("a SharedBridge deposit source is already wired");
        _depositSource = source;
        if (_l1MessageDrain is null
            && _l1FinalizedHeight is not null
            && _sequencerCommitteeHash is not null)
        {
            _l1MessageDrain = L1MessageDrain.FromDeposits(source);
        }
    }

    /// <summary>
    /// Compose the production L1 inbox: SharedBridge deposits and/or MessageRouter traffic,
    /// plus block-context providers. Deposit drains call <c>ScanAsync</c> then <c>Drain</c>
    /// so finalized L1 deposits are discovered at seal time. Deposit nonces are confirmed
    /// only after durable seal persistence (see <see cref="PersistAndAcknowledge"/>).
    /// </summary>
    public void WireL1MessageInbox(
        uint chainId,
        Func<uint> l1FinalizedHeight,
        Func<UInt256> sequencerCommitteeHash,
        ISharedBridgeDepositSource? deposits = null,
        IMessageRouter? messageRouter = null)
    {
        ChainIdValidator.ValidateL2(chainId);
        ArgumentNullException.ThrowIfNull(l1FinalizedHeight);
        ArgumentNullException.ThrowIfNull(sequencerCommitteeHash);
        if (deposits is null && messageRouter is null)
            throw new ArgumentException(
                "at least one of deposits or messageRouter is required",
                nameof(deposits));
        if (deposits is not null && deposits.ChainId != chainId)
            throw new ArgumentException(
                $"deposit source chain {deposits.ChainId} differs from {chainId}",
                nameof(deposits));
        if (_settings.ChainId != 0 && _settings.ChainId != chainId)
            throw new InvalidOperationException(
                "L1 inbox chain differs from batch settings");

        var drains = new List<Func<int, IReadOnlyList<CrossChainMessage>>>();
        if (deposits is not null)
        {
            WithDepositSource(deposits);
            drains.Add(L1MessageDrain.FromDeposits(deposits));
        }
        if (messageRouter is not null)
        {
            if (_messageRouter is not null && !ReferenceEquals(_messageRouter, messageRouter))
                throw new InvalidOperationException("a MessageRouter is already wired");
            _messageRouter = messageRouter;
            drains.Add(L1MessageDrain.FromRouter(messageRouter, chainId));
        }

        WithSealingInputs(
            L1MessageDrain.Combine(drains.ToArray()),
            l1FinalizedHeight,
            sequencerCommitteeHash);
    }

    /// <summary>Currently wired SharedBridge deposit source, if any.</summary>
    public ISharedBridgeDepositSource? DepositSource => _depositSource;

    /// <summary>
    /// Currently wired MessageRouter, if any. Production hosts use this to record finalized
    /// message proofs (<c>RecordFinalizedProof</c> / <c>RecordFinalized</c>) after settlement.
    /// </summary>
    public IMessageRouter? MessageRouter => _messageRouter;

    /// <summary>
    /// Currently wired forced-inclusion read source, if any (set by
    /// <see cref="WithForcedInclusionSource"/> / production WireProduction inbox wiring).
    /// </summary>
    public IForcedInclusionSource? ForcedInclusionSource => _forcedInclusionSource;

    /// <summary>
    /// True after <see cref="WithSealedBatchSink"/> has installed the durable settlement sink
    /// (e.g. via <c>L2SettlementPlugin.WireProduction</c>).
    /// </summary>
    public bool HasSealedBatchSink => _sink is not null;

    /// <summary>
    /// Next L2 block index required for a continuous hand-off after
    /// <see cref="WithSealedBatchSink"/>, or null when the sealer is not yet restored.
    /// </summary>
    public ulong? NextExpectedBlock => _sealer?.NextExpectedBlock;

    /// <summary>
    /// True when a sealed batch is awaiting durable persistence / acknowledgement
    /// (<see cref="BatchSealer.PendingBatch"/>).
    /// </summary>
    public bool HasPendingSealedBatch => _sealer?.PendingBatch is not null;

    /// <summary>
    /// Sealed batch awaiting durable persistence, or null when none is pending.
    /// </summary>
    public SealedBatch? PendingSealedBatch => _sealer?.PendingBatch;

    /// <summary>
    /// Pending sealed batch number, or null when none is pending.
    /// </summary>
    public ulong? PendingSealedBatchNumber => PendingSealedBatch?.BatchNumber;

    /// <summary>
    /// Last L2 block of the pending sealed batch, or null when none is pending.
    /// </summary>
    public ulong? PendingSealedBatchLastBlock => PendingSealedBatch?.LastBlock;

    /// <summary>
    /// Whether the batch plugin is enabled in settings (disabled plugins no-op block hand-off).
    /// </summary>
    public bool IsEnabled => _settings.Enabled;

    /// <summary>
    /// Configured max L2 blocks per sealed batch
    /// (<see cref="L2BatchSettings.MaxBlocksPerBatch"/>).
    /// </summary>
    public int MaxBlocksPerBatch => _settings.MaxBlocksPerBatch;

    /// <summary>
    /// Configured max transactions per sealed batch
    /// (<see cref="L2BatchSettings.MaxTransactionsPerBatch"/>).
    /// </summary>
    public int MaxTransactionsPerBatch => _settings.MaxTransactionsPerBatch;

    /// <summary>
    /// Configured max open-batch age in milliseconds
    /// (<see cref="L2BatchSettings.MaxBatchAgeMillis"/>).
    /// </summary>
    public int MaxBatchAgeMillis => _settings.MaxBatchAgeMillis;

    /// <summary>
    /// True when a batch is currently being accumulated
    /// (<see cref="BatchSealer.HasOpenBatch"/>).
    /// </summary>
    public bool HasOpenBatch => _sealer?.HasOpenBatch ?? false;

    /// <summary>
    /// Transaction count in the open batch, or 0 when none is open
    /// (<see cref="BatchSealer.InProgressTxCount"/>).
    /// </summary>
    public int InProgressTxCount => _sealer?.InProgressTxCount ?? 0;

    /// <summary>
    /// First L2 block in the open batch, or null when none is open
    /// (<see cref="BatchSealer.OpenBatchFirstBlock"/>).
    /// </summary>
    public ulong? OpenBatchFirstBlock => _sealer?.OpenBatchFirstBlock;

    /// <summary>
    /// Last L2 block in the open batch, or null when none is open
    /// (<see cref="BatchSealer.OpenBatchLastBlock"/>).
    /// </summary>
    public ulong? OpenBatchLastBlock => _sealer?.OpenBatchLastBlock;

    /// <summary>
    /// Block count in the open batch
    /// (<see cref="BatchSealer.OpenBatchBlockCount"/>).
    /// </summary>
    public int OpenBatchBlockCount => _sealer?.OpenBatchBlockCount ?? 0;

    /// <summary>
    /// L1 messages consumed into the open batch
    /// (<see cref="BatchSealer.OpenBatchL1MessageCount"/>).
    /// </summary>
    public int OpenBatchL1MessageCount => _sealer?.OpenBatchL1MessageCount ?? 0;

    /// <summary>
    /// L2→L1 messages staged in the open batch
    /// (<see cref="BatchSealer.OpenBatchL2ToL1MessageCount"/>).
    /// </summary>
    public int OpenBatchL2ToL1MessageCount => _sealer?.OpenBatchL2ToL1MessageCount ?? 0;

    /// <summary>
    /// Last batch number that completed durable persist + acknowledgement
    /// (<see cref="BatchSealer.LastAcknowledgedBatchNumber"/>).
    /// </summary>
    public ulong LastAcknowledgedBatchNumber => _sealer?.LastAcknowledgedBatchNumber ?? 0UL;

    /// <summary>
    /// Last L2 block covered by the last acknowledged batch
    /// (<see cref="BatchSealer.LastAcknowledgedBlock"/>).
    /// </summary>
    public ulong LastAcknowledgedBlock => _sealer?.LastAcknowledgedBlock ?? 0UL;

    /// <summary>
    /// Batch number that will be assigned to the next sealed batch
    /// (<see cref="BatchSealer.NextBatchNumber"/>).
    /// </summary>
    public ulong NextBatchNumber => _sealer?.NextBatchNumber ?? 1UL;

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
        var initialStateRoot = checkpoint is null
            ? sink.GetInitialStateRootAsync().AsTask().GetAwaiter().GetResult()
            : checkpoint.PostStateRoot;
        ArgumentNullException.ThrowIfNull(initialStateRoot);
        var sealer = CreateSealer(chainId, initialStateRoot);
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

    /// <summary>
    /// Host composition factory: preload batch thresholds and chain id from a chain
    /// working directory (<c>init-l2</c> / <c>--from-deploy-report</c>) without the Neo
    /// plugin config loader.
    /// </summary>
    /// <remarks>
    /// Prefer this over the parameterless constructor when wiring settlement
    /// <c>WireProductionFromLayout</c> outside Neo.CLI so seal-time chain-id checks
    /// match the settlement plugin.
    /// </remarks>
    public static L2BatchPlugin CreateFromChainDirectory(string chainDirectory)
        => new(L2BatchSettings.FromChainDirectory(chainDirectory));

    /// <summary>Loaded batch settings (host composition / tests).</summary>
    internal L2BatchSettings Settings => _settings;

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
    /// Process one committed L2 block through the durable pending/persist/ack hand-off
    /// without Neo.CLI <c>Blockchain.Committed</c> (host/offline composition).
    /// </summary>
    /// <remarks>
    /// See doc.md §7.2. A pending sealed batch is retried and acknowledged before this
    /// method accepts a later block. Requires <see cref="WithSealedBatchSink"/>.
    /// </remarks>
    public void ProcessCommittedBlock(
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

    /// <summary>
    /// Retry durable persistence of a pending sealed batch without feeding a new L2 block
    /// (operator recovery after a transient sink/executor failure).
    /// </summary>
    /// <returns>
    /// <c>true</c> when a pending batch was present and the persist/ack path ran;
    /// <c>false</c> when no pending sealed batch exists (or the sealer/sink is unwired).
    /// </returns>
    /// <remarks>
    /// See doc.md §7.2. Propagates sink/executor failures so the operator can retry.
    /// </remarks>
    public bool TryRetryPendingSealedBatch()
    {
        if (!_settings.Enabled) return false;
        if (_sealer is null || _sink is null) return false;
        var pending = _sealer.PendingBatch;
        if (pending is null) return false;
        PersistAndAcknowledge(_sealer, _sink, pending);
        return true;
    }

    private void PersistAndAcknowledge(
        BatchSealer sealer,
        ISealedBatchSink sink,
        SealedBatch batch)
    {
        try
        {
            var postStateRoot = sink.PersistAsync(batch)
                .AsTask().GetAwaiter().GetResult();
            ArgumentNullException.ThrowIfNull(postStateRoot);
            sealer.AcknowledgeExecution(batch.BatchNumber, postStateRoot);
            ConfirmSharedBridgeDeposits(batch);
            DispatchSealed(this, OnBatchSealed, batch, _metrics);
        }
        catch
        {
            // Durable persistence failed: return reserved SharedBridge deposits to the ready
            // set so a retried seal can include them. MessageRouter dequeues are owned by
            // that component's local-consumed set and are not rolled back here.
            ReleaseSharedBridgeDepositReservations(batch);
            throw;
        }
    }

    private void ConfirmSharedBridgeDeposits(SealedBatch batch)
    {
        if (_depositSource is null) return;
        foreach (var message in batch.L1Messages)
        {
            if (message.MessageType == MessageType.Deposit && message.SourceChainId == 0)
                _depositSource.ConfirmConsumed(message.Nonce);
        }
    }

    private void ReleaseSharedBridgeDepositReservations(SealedBatch batch)
    {
        if (_depositSource is null) return;
        var nonces = batch.L1Messages
            .Where(m => m.MessageType == MessageType.Deposit && m.SourceChainId == 0)
            .Select(m => m.Nonce);
        _depositSource.ReleaseReservations(nonces);
    }

    private BatchSealer CreateSealer(uint chainId, UInt256 initialStateRoot)
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
            sequencerCommitteeHash: _sequencerCommitteeHash,
            initialStateRoot: initialStateRoot);
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
