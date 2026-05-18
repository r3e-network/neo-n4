using System;
using System.Collections.Generic;
using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.Executor.Receipts;
using Neo.L2.State;
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

    private BatchBuilder? _builder;
    private long _batchStartedAtUtcMillis;
    private ulong _nextBatchNumber = 1;
    private UInt256 _lastPostStateRoot = UInt256.Zero;

    /// <summary>Swap the metrics sink in-place. Preserves all batch-numbering + builder state, unlike re-constructing.</summary>
    public void WithMetrics(IL2Metrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        _metrics = metrics;
    }

    /// <summary>Construct a sealer. <paramref name="nowUtcMillis"/> is injectable so tests can advance time.</summary>
    public BatchSealer(L2BatchSettings settings, IL2Metrics metrics, Func<long>? nowUtcMillis = null)
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
    }

    /// <summary>Number of transactions in the in-progress batch (0 if no batch is open).</summary>
    public int InProgressTxCount => _builder?.Batch.TransactionCount ?? 0;

    /// <summary>Whether a batch is currently being accumulated.</summary>
    public bool HasOpenBatch => _builder is not null;

    /// <summary>
    /// Feed in a freshly-committed L2 block. If this commit triggers a seal, returns the
    /// resulting <see cref="L2BatchCommitment"/>; otherwise returns <c>null</c> and the
    /// block is folded into the in-progress batch.
    /// </summary>
    /// <param name="blockIndex">Block height.</param>
    /// <param name="blockTimestamp">Unix-millis timestamp of the block.</param>
    /// <param name="network">Neo network magic — feeds into the block context.</param>
    /// <param name="rawTransactions">Raw bytes of every transaction in the block, in order.</param>
    public L2BatchCommitment? OnBlockCommit(uint blockIndex, ulong blockTimestamp, uint network, IEnumerable<byte[]> rawTransactions)
    {
        ArgumentNullException.ThrowIfNull(rawTransactions);

        var builder = _builder ??= StartFreshBatch(blockIndex);
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
        var commitment = SealBatch(builder, blockTimestamp, network);
        sw.Stop();

        // Safe* wrappers: the seal is committed and `_builder = null` MUST run so the
        // next OnBlockCommit starts a fresh batch. Without these, a metric throw would
        // leave _builder pointing at the just-sealed builder; the next call would add
        // blocks to a sealed builder (or hit "already sealed"). See iter-162/163 fix.
        _metrics.SafeIncrementCounter(MetricNames.BatchesSealed);
        _metrics.SafeRecordHistogram(MetricNames.BatchSealLatencyMs, sw.Elapsed.TotalMilliseconds);
        _metrics.SafeSetGauge(MetricNames.BatchTxCount, txCount);

        _builder = null;
        return commitment;
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

    private BatchBuilder StartFreshBatch(uint firstBlockIndex)
    {
        _batchStartedAtUtcMillis = _nowUtcMillis();
        return new BatchBuilder(_settings.ChainId, _nextBatchNumber++, firstBlockIndex, _lastPostStateRoot);
    }

    private L2BatchCommitment SealBatch(BatchBuilder builder, ulong lastBlockTimestamp, uint network)
    {
        builder.WithBlockContext(new BatchBlockContext
        {
            L1FinalizedHeight = (uint)builder.Batch.LastBlock,
            FirstBlockTimestamp = lastBlockTimestamp,
            LastBlockTimestamp = lastBlockTimestamp,
            SequencerCommitteeHash = UInt256.Zero,
            Network = network,
        });

        // BatchSealer is the *transaction-collector* phase of the pipeline. Its job is to
        // observe block commits, group transactions into batches by the configured triggers
        // (max-blocks / max-txs / max-age), and produce a "soft" commitment that pins which
        // transactions went into the batch (TxRoot is real). The deterministic executor
        // (`IL2BatchExecutor` — `ReferenceBatchExecutor` for tests, NeoVM2/RISC-V for
        // Neo N4 production, or `ApplicationEngine` for legacy compatibility) runs
        // separately and produces the real
        // post-state / receipts / withdrawals / messages roots, then the L2SettlementPlugin
        // re-seals the batch with the executor's outputs before submitting to L1.
        //
        // The zero-valued roots here aren't placeholder data ever observed on L1 — they're
        // the "execution-not-yet-run" sentinel for the soft commitment. Non-zero values
        // come from the executor pass. See `tools/Neo.L2.Devnet/Program.cs` for the full
        // pipeline: sealer → executor → settlement client.
        var executionResult = new BatchExecutionResult
        {
            PostStateRoot = _lastPostStateRoot,
            ReceiptRoot = UInt256.Zero,
            WithdrawalRoot = UInt256.Zero,
            L2ToL1MessageRoot = UInt256.Zero,
            L2ToL2MessageRoot = UInt256.Zero,
            TxRoot = MerkleTree.ComputeRoot(CollectTxHashes(builder)),
            GasConsumed = 0,
        };

        return builder.Seal(
            executionResult,
            daCommitment: UInt256.Zero,
            publicInputHash: UInt256.Zero,
            proofType: ProofType.None,
            proof: ReadOnlyMemory<byte>.Empty);
    }

    private static UInt256[] CollectTxHashes(BatchBuilder builder)
    {
        var hashes = new UInt256[builder.Batch.TransactionCount];
        for (var i = 0; i < hashes.Length; i++)
        {
            var bytes = builder.Batch.Transactions[i].Span;
            hashes[i] = new UInt256(Cryptography.Crypto.Hash256(bytes));
        }
        return hashes;
    }
}
