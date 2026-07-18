using System;
using System.Collections.Generic;
using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.Telemetry;

namespace Neo.Plugins.L2;

/// <summary>
/// Pure batch-accumulation state machine. Lives behind <see cref="L2BatchPlugin"/> so the
/// plugin's role is reduced to forwarding <c>Blockchain.Committed</c> events; the actual
/// "when do we seal" / "what does the seal look like" logic is here so it can be unit-tested
/// without standing up a Neo node.
/// </summary>
/// <remarks>
/// See doc.md §7.2 (Batcher) and §15.1 (transaction flow).
/// </remarks>
public sealed class BatchSealer
{
    private readonly L2BatchSettings _settings;
    private IL2Metrics _metrics;
    private readonly Func<long> _nowUtcMillis;
    private readonly Func<int, IReadOnlyList<(ulong Nonce, UInt256 TxHash, ReadOnlyMemory<byte> SerializedTx)>>? _forcedDrain;
    private readonly Func<int, IReadOnlyList<CrossChainMessage>>? _l1MessageDrain;
    private readonly Func<uint>? _l1FinalizedHeight;
    private readonly Func<UInt256>? _sequencerCommitteeHash;

    /// <summary>Maximum forced-inclusion entries prepended to a single batch — bounds batch size so a
    /// flooded L1 forced-inclusion queue cannot produce an unbounded batch.</summary>
    public const int MaxForcedTransactionsPerBatch = 256;

    /// <summary>Maximum L1 inbox messages consumed by one batch.</summary>
    public const int MaxL1MessagesPerBatch = 1024;

    private BatchBuilder? _builder;
    private long _batchStartedAtUtcMillis;
    private ulong _firstBlockTimestamp;
    private ulong _nextBatchNumber = 1;
    private ulong _lastAcknowledgedBatchNumber;
    private ulong _lastAcknowledgedBlock;
    private UInt256 _lastPostStateRoot;
    private SealedBatch? _pendingBatch;
    private bool _checkpointRestoreApplied;

    /// <summary>Swap the metrics sink in-place. Preserves all batch-numbering + builder state, unlike re-constructing.</summary>
    public void WithMetrics(IL2Metrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        _metrics = metrics;
    }

    /// <summary>
    /// Construct a sealer. <paramref name="nowUtcMillis"/> is injectable so tests can advance time.
    /// <paramref name="forcedDrain"/> (optional) is polled once at the start of each batch and its
    /// entries are PREPENDED to the batch's transaction list (deadline-ordered, oldest first, capped
    /// at <see cref="MaxForcedTransactionsPerBatch"/>) so the sequencer cannot censor L1 forced
    /// transactions. Included nonces are retained in <see cref="SealedBatch"/> and are never
    /// marked consumed by the batcher; settlement finality owns that transition. When
    /// <paramref name="forcedDrain"/> is null the sealer behaves exactly as before.
    /// </summary>
    public BatchSealer(
        L2BatchSettings settings,
        IL2Metrics metrics,
        Func<long>? nowUtcMillis = null,
        Func<int, IReadOnlyList<(ulong Nonce, UInt256 TxHash, ReadOnlyMemory<byte> SerializedTx)>>? forcedDrain = null,
        Func<int, IReadOnlyList<CrossChainMessage>>? l1MessageDrain = null,
        Func<uint>? l1FinalizedHeight = null,
        Func<UInt256>? sequencerCommitteeHash = null,
        UInt256? initialStateRoot = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(metrics);
        // L2BatchSettings.From validates positivity at config-parse time, but `init`
        // setters allow direct construction (tests / programmatic wiring) to bypass
        // that path. Re-validate here so a `new L2BatchSettings { MaxBlocksPerBatch
        // = 0 }` doesn't slip through and make ShouldSeal return true on every block
        // — a degenerate per-block batch storm that surfaces only as a runaway L1
        // submission rate hours later.
        L2BatchSettings.ValidatePositive(settings.MaxBlocksPerBatch, nameof(settings.MaxBlocksPerBatch));
        L2BatchSettings.ValidatePositive(settings.MaxTransactionsPerBatch, nameof(settings.MaxTransactionsPerBatch));
        L2BatchSettings.ValidatePositive(settings.MaxBatchAgeMillis, nameof(settings.MaxBatchAgeMillis));
        _settings = settings;
        _metrics = metrics;
        _nowUtcMillis = nowUtcMillis ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        _forcedDrain = forcedDrain;
        _l1MessageDrain = l1MessageDrain;
        _l1FinalizedHeight = l1FinalizedHeight;
        _sequencerCommitteeHash = sequencerCommitteeHash;
        _lastPostStateRoot = initialStateRoot is null
            ? UInt256.Zero
            : new UInt256(initialStateRoot.GetSpan());
    }

    /// <summary>Number of transactions in the in-progress batch (0 if no batch is open).</summary>
    public int InProgressTxCount => _builder?.Batch.TransactionCount ?? 0;

    /// <summary>Whether a batch is currently being accumulated.</summary>
    public bool HasOpenBatch => _builder is not null && _pendingBatch is null;

    /// <summary>First L2 block in the open batch, or null when none is open.</summary>
    public ulong? OpenBatchFirstBlock => HasOpenBatch ? _builder!.Batch.FirstBlock : null;

    /// <summary>Last L2 block in the open batch, or null when none is open.</summary>
    public ulong? OpenBatchLastBlock => HasOpenBatch ? _builder!.Batch.LastBlock : null;

    /// <summary>Block count in the open batch (0 when none is open).</summary>
    public int OpenBatchBlockCount =>
        HasOpenBatch
            ? checked((int)(_builder!.Batch.LastBlock - _builder.Batch.FirstBlock + 1))
            : 0;

    /// <summary>L1 messages consumed into the open batch (0 when none is open).</summary>
    public int OpenBatchL1MessageCount =>
        HasOpenBatch ? _builder!.Batch.L1MessagesConsumed.Count : 0;

    /// <summary>L2→L1 messages staged in the open batch (0 when none is open).</summary>
    public int OpenBatchL2ToL1MessageCount =>
        HasOpenBatch ? _builder!.Batch.L2ToL1Messages.Count : 0;

    /// <summary>Forced-inclusion entries staged in the open batch (0 when none is open).</summary>
    public int OpenBatchForcedInclusionCount =>
        HasOpenBatch ? _builder!.ForcedInclusionCount : 0;

    /// <summary>Immutable sealed batch awaiting durable persistence and acknowledgement.</summary>
    public SealedBatch? PendingBatch => _pendingBatch;

    /// <summary>
    /// Last batch number that completed durable persist + acknowledgement (0 before any).
    /// </summary>
    public ulong LastAcknowledgedBatchNumber => _lastAcknowledgedBatchNumber;

    /// <summary>
    /// Last L2 block index covered by <see cref="LastAcknowledgedBatchNumber"/> (0 before any).
    /// </summary>
    public ulong LastAcknowledgedBlock => _lastAcknowledgedBlock;

    /// <summary>
    /// Batch number that will be assigned to the next sealed batch (starts at 1).
    /// </summary>
    public ulong NextBatchNumber => _nextBatchNumber;

    /// <summary>
    /// Next block required for a continuous hand-off, or null before an explicit restore or
    /// the first direct block feed.
    /// </summary>
    public ulong? NextExpectedBlock
    {
        get
        {
            if (_pendingBatch is not null)
                return checked(_pendingBatch.LastBlock + 1);
            if (_builder is not null)
                return checked(_builder.Batch.LastBlock + 1);
            if (_lastAcknowledgedBatchNumber != 0)
                return checked(_lastAcknowledgedBlock + 1);
            return _checkpointRestoreApplied ? 1UL : null;
        }
    }

    /// <summary>
    /// Restore the latest continuous durable checkpoint before accepting the first block.
    /// </summary>
    public void RestoreCheckpoint(SealedBatchCheckpoint? checkpoint)
    {
        if (_builder is not null || _pendingBatch is not null
            || _lastAcknowledgedBatchNumber != 0 || _checkpointRestoreApplied)
            throw new InvalidOperationException(
                "batch checkpoint must be restored before accepting blocks");
        _checkpointRestoreApplied = true;
        if (checkpoint is null) return;
        ArgumentNullException.ThrowIfNull(checkpoint.PostStateRoot);
        if (checkpoint.BatchNumber == 0)
            throw new ArgumentException(
                "batch checkpoint number must be non-zero", nameof(checkpoint));
        if (checkpoint.BatchNumber == ulong.MaxValue)
            throw new ArgumentException(
                "batch checkpoint cannot advance beyond the maximum batch number",
                nameof(checkpoint));
        _lastAcknowledgedBatchNumber = checkpoint.BatchNumber;
        _lastAcknowledgedBlock = checkpoint.LastBlock;
        _lastPostStateRoot = new UInt256(checkpoint.PostStateRoot.GetSpan());
        _nextBatchNumber = checkpoint.BatchNumber + 1;
    }

    /// <summary>
    /// Feed in a freshly-committed L2 block. If this commit triggers a seal, returns the
    /// resulting immutable <see cref="SealedBatch"/>; otherwise returns <c>null</c> and the
    /// block is folded into the in-progress batch.
    /// </summary>
    /// <param name="blockIndex">Block height.</param>
    /// <param name="blockTimestamp">Unix-millis timestamp of the block.</param>
    /// <param name="network">Neo network magic — feeds into the block context.</param>
    /// <param name="rawTransactions">Raw bytes of every transaction in the block, in order.</param>
    public SealedBatch? OnBlockCommit(uint blockIndex, ulong blockTimestamp, uint network, IEnumerable<byte[]> rawTransactions)
    {
        ArgumentNullException.ThrowIfNull(rawTransactions);
        if (_pendingBatch is not null)
            throw new InvalidOperationException(
                $"sealed batch {_pendingBatch.BatchNumber} must be durably acknowledged before accepting block {blockIndex}");

        ValidateNextBlock(blockIndex);

        var isFreshBatch = _builder is null;
        var builder = _builder ??= StartFreshBatch(blockIndex, blockTimestamp);
        // Prepend pending L1 forced-inclusion transactions at the START of each batch (before any
        // block txs) so a censoring sequencer cannot exclude them. Done once per batch, oldest-first.
        if (isFreshBatch)
        {
            DrainL1Messages(builder);
            DrainForcedTransactions(builder);
        }
        builder.AddBlock(blockIndex);
        var txIndex = 0;
        foreach (var tx in rawTransactions)
        {
            // The implicit byte[] → ReadOnlyMemory<byte> conversion silently turns null
            // into Empty, so a null entry would be folded into the batch's tx tree as an
            // empty leaf — a deterministic-replay nightmare since the commitment would
            // not match what re-execution produces. Surface the null at the source.
            if (tx is null)
                throw new ArgumentException($"rawTransactions[{txIndex}] is null", nameof(rawTransactions));
            builder.AddTransaction(tx);
            txIndex++;
        }

        if (!ShouldSeal(builder)) return null;

        var txCount = builder.Batch.TransactionCount;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var artifact = SealBatch(builder, blockTimestamp, network);
        _pendingBatch = artifact;
        sw.Stop();

        // Safe* wrappers cannot interfere with the pending hand-off state. The sealed builder
        // remains owned until durable persistence is acknowledged.
        _metrics.SafeIncrementCounter(MetricNames.BatchesSealed);
        _metrics.SafeRecordHistogram(MetricNames.BatchSealLatencyMs, sw.Elapsed.TotalMilliseconds);
        _metrics.SafeSetGauge(MetricNames.BatchTxCount, txCount);

        return artifact;
    }

    /// <summary>
    /// Advance the pre-state root for the next batch after execution and artifact persistence
    /// have completed. Calls are sequential and idempotent.
    /// </summary>
    public void AcknowledgeExecution(ulong batchNumber, UInt256 postStateRoot)
    {
        ArgumentNullException.ThrowIfNull(postStateRoot);
        if (batchNumber == _lastAcknowledgedBatchNumber)
        {
            if (!_lastPostStateRoot.Equals(postStateRoot))
                throw new InvalidOperationException(
                    "execution acknowledgement changed the post-state root");
            return;
        }
        if (_pendingBatch is null)
        {
            throw new InvalidOperationException(
                "no sealed batch is pending execution acknowledgement");
        }
        if (batchNumber != _pendingBatch.BatchNumber)
            throw new InvalidOperationException(
                $"execution acknowledgement targets batch {batchNumber}, pending batch is {_pendingBatch.BatchNumber}");
        if (batchNumber != _nextBatchNumber)
            throw new InvalidOperationException(
                $"execution acknowledgements must be sequential: expected {_nextBatchNumber}, got {batchNumber}");
        if (!_pendingBatch.PreStateRoot.Equals(_lastPostStateRoot))
            throw new InvalidOperationException(
                "pending batch pre-state root differs from the last acknowledged post-state root");

        var nextBatchNumber = checked(batchNumber + 1);
        _lastAcknowledgedBatchNumber = batchNumber;
        _lastAcknowledgedBlock = _pendingBatch.LastBlock;
        _lastPostStateRoot = new UInt256(postStateRoot.GetSpan());
        _nextBatchNumber = nextBatchNumber;
        _pendingBatch = null;
        _builder = null;
    }

    private void ValidateNextBlock(uint blockIndex)
    {
        var expected = NextExpectedBlock;
        if (expected is null || blockIndex == expected.Value) return;
        var kind = blockIndex < expected.Value ? "duplicate or out-of-order" : "gap";
        throw new InvalidOperationException(
            $"{kind} L2 block {blockIndex}; expected {expected.Value}");
    }

    private void DrainL1Messages(BatchBuilder builder)
    {
        if (_l1MessageDrain is null) return;
        var messages = _l1MessageDrain(MaxL1MessagesPerBatch)
            ?? throw new InvalidOperationException("L1 message drain returned null");
        if (messages.Count > MaxL1MessagesPerBatch)
            throw new InvalidOperationException(
                $"L1 message drain returned {messages.Count}, maximum is {MaxL1MessagesPerBatch}");
        for (var index = 0; index < messages.Count; index++)
            builder.ConsumeL1Message(messages[index]
                ?? throw new InvalidOperationException(
                    $"L1 message drain returned null at index {index}"));
    }

    /// <summary>
    /// Poll the forced-inclusion source (if wired) and prepend its entries to the fresh batch.
    /// Entries become part of the batch's transaction list, so the deterministic executor runs them
    /// and the TxRoot/post-state commit to them. No-op when no source is configured.
    /// </summary>
    private void DrainForcedTransactions(BatchBuilder builder)
    {
        if (_forcedDrain is null) return;
        // Same fail-closed contract as L1 message drain: a wired adapter that returns
        // null must not be treated as "no forced txs", or anti-censorship inclusion can
        // silently disappear under a flaky source.
        var forced = _forcedDrain(MaxForcedTransactionsPerBatch)
            ?? throw new InvalidOperationException("forced-inclusion drain returned null");
        if (forced.Count > MaxForcedTransactionsPerBatch)
            throw new InvalidOperationException(
                $"forced-inclusion drain returned {forced.Count}, maximum is {MaxForcedTransactionsPerBatch}");
        foreach (var entry in forced)
        {
            // An empty forced tx would fold into the tx tree as an empty leaf and break
            // deterministic replay (same guard as the block-tx path). Surface it.
            if (entry.SerializedTx.IsEmpty)
                throw new InvalidOperationException(
                    $"forced-inclusion entry nonce {entry.Nonce} has an empty transaction");
            builder.AddForcedTransaction(entry.Nonce, entry.TxHash, entry.SerializedTx);
        }
    }

    private bool ShouldSeal(BatchBuilder builder)
    {
        // Compare in ulong space to avoid the int-cast wrap. The previous (int)cast
        // wrapped when blocksInBatch > int.MaxValue (~2.1B), making the seal-by-blocks
        // check silently never fire — ultra-long-lived sequencers would accumulate
        // unboundedly. iter-191 BatchSealer ctor validates MaxBlocksPerBatch > 0, so
        // the unchecked-cast is safe in the other direction.
        var blocksInBatch = builder.Batch.LastBlock - builder.Batch.FirstBlock + 1;
        if (blocksInBatch >= (ulong)_settings.MaxBlocksPerBatch) return true;
        if (builder.Batch.TransactionCount >= _settings.MaxTransactionsPerBatch) return true;
        var ageMs = _nowUtcMillis() - _batchStartedAtUtcMillis;
        if (ageMs >= _settings.MaxBatchAgeMillis) return true;
        return false;
    }

    private BatchBuilder StartFreshBatch(uint firstBlockIndex, ulong firstBlockTimestamp)
    {
        _batchStartedAtUtcMillis = _nowUtcMillis();
        _firstBlockTimestamp = firstBlockTimestamp;
        return new BatchBuilder(_settings.ChainId, _nextBatchNumber, firstBlockIndex, _lastPostStateRoot);
    }

    private SealedBatch SealBatch(BatchBuilder builder, ulong lastBlockTimestamp, uint network)
    {
        if (lastBlockTimestamp < _firstBlockTimestamp)
            throw new InvalidOperationException(
                "last block timestamp precedes the first block timestamp");
        builder.WithBlockContext(new BatchBlockContext
        {
            L1FinalizedHeight = _l1FinalizedHeight?.Invoke() ?? 0,
            FirstBlockTimestamp = _firstBlockTimestamp,
            LastBlockTimestamp = lastBlockTimestamp,
            SequencerCommitteeHash = _sequencerCommitteeHash?.Invoke() ?? UInt256.Zero,
            Network = network,
        });
        return builder.SealArtifact();
    }
}
