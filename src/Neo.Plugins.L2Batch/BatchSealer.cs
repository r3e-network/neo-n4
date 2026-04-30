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
    private readonly IL2Metrics _metrics;
    private readonly Func<long> _nowUtcMillis;

    private BatchBuilder? _builder;
    private long _batchStartedAtUtcMillis;
    private ulong _nextBatchNumber = 1;
    private UInt256 _lastPostStateRoot = UInt256.Zero;

    /// <summary>Construct a sealer. <paramref name="nowUtcMillis"/> is injectable so tests can advance time.</summary>
    public BatchSealer(L2BatchSettings settings, IL2Metrics metrics, Func<long>? nowUtcMillis = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(metrics);
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
        foreach (var tx in rawTransactions)
            builder.AddTransaction(tx);

        if (!ShouldSeal(builder)) return null;

        var txCount = builder.Batch.TransactionCount;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var commitment = SealBatch(builder, blockTimestamp, network);
        sw.Stop();

        _metrics.IncrementCounter(MetricNames.BatchesSealed);
        _metrics.RecordHistogram(MetricNames.BatchSealLatencyMs, sw.Elapsed.TotalMilliseconds);
        _metrics.SetGauge(MetricNames.BatchTxCount, txCount);

        _builder = null;
        return commitment;
    }

    private bool ShouldSeal(BatchBuilder builder)
    {
        var blocksInBatch = builder.Batch.LastBlock - builder.Batch.FirstBlock + 1;
        if ((int)blocksInBatch >= _settings.MaxBlocksPerBatch) return true;
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

        // Sealing here is "soft" — we have not actually run the deterministic executor yet.
        // For now, we package zero roots; a downstream executor pass replaces them.
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
