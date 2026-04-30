using System;
using Neo.Extensions;
using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.Executor.Receipts;
using Neo.L2.State;
using Neo.L2.Telemetry;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;

namespace Neo.Plugins.L2;

/// <summary>
/// Listens to the local Neo 4 chain's <c>Blockchain.Committed</c> event and accumulates work
/// into an in-progress <see cref="L2Batch"/>. When the configured size, count, or age
/// thresholds trip, the plugin seals the batch into an <see cref="L2BatchCommitment"/>,
/// publishes it via <see cref="OnBatchSealed"/>, and starts a fresh batch.
/// </summary>
/// <remarks>
/// See doc.md §7.2 (Batcher) and §15.1 (transaction flow). This plugin owns the batcher's
/// lifecycle but does NOT submit batches to NeoHub — that is <c>L2SettlementPlugin</c>'s job.
/// </remarks>
public sealed class L2BatchPlugin : Plugin
{
    private L2BatchSettings _settings = new();
    private BatchBuilder? _builder;
    private long _batchStartedAtUtcMillis;
    private ulong _nextBatchNumber = 1;
    private UInt256 _lastPostStateRoot = UInt256.Zero;
    private IL2Metrics _metrics = NoOpMetrics.Instance;

    /// <summary>Emitted whenever a batch is sealed and ready for submission.</summary>
    public event EventHandler<L2BatchCommitment>? OnBatchSealed;

    /// <summary>
    /// Wire a metrics sink. The plugin emits <c>l2.batch.sealed</c>,
    /// <c>l2.batch.seal_latency_ms</c>, and <c>l2.batch.tx_count</c> against this sink. Call
    /// before the first block commit; defaults to <see cref="NoOpMetrics"/>.
    /// </summary>
    public void WithMetrics(IL2Metrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        _metrics = metrics;
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
    }

    /// <summary>Block-commit hook — entry point for batch accumulation.</summary>
    private void OnBlockCommitted(NeoSystem system, Block block)
    {
        if (!_settings.Enabled) return;
        if (block is null) return;

        var builder = _builder ??= StartFreshBatch(block.Index);
        builder.AddBlock(block.Index);
        foreach (var tx in block.Transactions)
            builder.AddTransaction(tx.ToArray());

        if (ShouldSeal(builder))
        {
            var txCount = builder.Batch.TransactionCount;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var sealed_ = SealBatch(builder, block, system);
            sw.Stop();
            _metrics.IncrementCounter(MetricNames.BatchesSealed);
            _metrics.RecordHistogram(MetricNames.BatchSealLatencyMs, sw.Elapsed.TotalMilliseconds);
            _metrics.SetGauge(MetricNames.BatchTxCount, txCount);
            OnBatchSealed?.Invoke(this, sealed_);
            _builder = null;
        }
    }

    private bool ShouldSeal(BatchBuilder builder)
    {
        var blocksInBatch = builder.Batch.LastBlock - builder.Batch.FirstBlock + 1;
        if ((int)blocksInBatch >= _settings.MaxBlocksPerBatch) return true;
        if (builder.Batch.TransactionCount >= _settings.MaxTransactionsPerBatch) return true;
        var ageMs = NowUtcMillis() - _batchStartedAtUtcMillis;
        if (ageMs >= _settings.MaxBatchAgeMillis) return true;
        return false;
    }

    private BatchBuilder StartFreshBatch(uint firstBlockIndex)
    {
        _batchStartedAtUtcMillis = NowUtcMillis();
        return new BatchBuilder(_settings.ChainId, _nextBatchNumber++, firstBlockIndex, _lastPostStateRoot);
    }

    private L2BatchCommitment SealBatch(BatchBuilder builder, Block lastBlock, NeoSystem system)
    {
        builder.WithBlockContext(BuildBlockContext(builder.Batch, lastBlock, system));

        // Sealing here is "soft" — we have not actually run the deterministic executor yet.
        // For now, we package zero roots; a downstream executor pass replaces them. This is
        // the same shape the prover ultimately sees, but with placeholder roots; the
        // L2SettlementPlugin holds the gate that converts soft → hard before submission.
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

    private static BatchBlockContext BuildBlockContext(L2Batch batch, Block lastBlock, NeoSystem system)
    {
        return new BatchBlockContext
        {
            L1FinalizedHeight = lastBlock.Index,
            FirstBlockTimestamp = lastBlock.Timestamp,
            LastBlockTimestamp = lastBlock.Timestamp,
            SequencerCommitteeHash = UInt256.Zero,
            Network = system.Settings.Network,
        };
    }

    private static long NowUtcMillis() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
