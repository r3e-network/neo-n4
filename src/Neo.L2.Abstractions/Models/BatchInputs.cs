namespace Neo.L2;

/// <summary>
/// Deterministic block-level context that the batch executor cannot derive from transactions alone
/// (timestamps, validator set, L1 finalized height, etc.).
/// </summary>
/// <remarks>
/// Hash of this struct becomes <c>blockContextHash</c> in the public-input bundle (doc.md §8.3).
/// </remarks>
public sealed record BatchBlockContext
{
    /// <summary>L1 block height at the moment this batch was sealed.</summary>
    public required uint L1FinalizedHeight { get; init; }

    /// <summary>Wall-clock timestamp the sequencer committee assigned to the first block.</summary>
    public required ulong FirstBlockTimestamp { get; init; }

    /// <summary>Wall-clock timestamp the sequencer committee assigned to the last block.</summary>
    public required ulong LastBlockTimestamp { get; init; }

    /// <summary>Hash of the validator set committee that signed off on the batch.</summary>
    public required UInt256 SequencerCommitteeHash { get; init; }

    /// <summary>Network protocol magic the batch was produced under.</summary>
    public required uint Network { get; init; }
}

/// <summary>
/// One L2 block of an in-memory batch execution request, in execution order. The entry binds the
/// transactions that follow it (up to <see cref="TransactionCount"/>) to the block index and
/// timestamp the executing engine must surface through <c>Runtime.Block.Index</c> and
/// <c>Runtime.Time</c>.
/// </summary>
/// <remarks>
/// In-memory execution-seam data only (doc.md §7.2). It is NOT part of <c>ExecutionPayloadV1</c>,
/// the witness artifacts, or the public-input preimage; the SP1 guest pins its own batch-level
/// view, so deterministic contracts MUST NOT read <c>Runtime.Time</c> or the block index.
/// </remarks>
public sealed record L2BatchBlock
{
    /// <summary>L2 block index this entry covers.</summary>
    public required ulong BlockIndex { get; init; }

    /// <summary>Wall-clock timestamp assigned to this block.</summary>
    public required ulong BlockTimestamp { get; init; }

    /// <summary>Transactions of the batch attributed to this block, in execution order.</summary>
    public required int TransactionCount { get; init; }
}

/// <summary>
/// Per-transaction execution header derived from the <see cref="L2BatchBlock"/> that owns the
/// transaction. Both production executors map these values onto the persisted block header so
/// <c>Runtime.Block.Index</c>/<c>Runtime.Time</c> report the executing L2 block, never the batch
/// context's L1 finalized height or frozen first-block timestamp.
/// </summary>
/// <remarks>See doc.md §7.2 and §8.1.</remarks>
public sealed record L2BlockContext
{
    /// <summary>L2 block index being executed.</summary>
    public required ulong BlockIndex { get; init; }

    /// <summary>Wall-clock timestamp of the block being executed.</summary>
    public required ulong BlockTimestamp { get; init; }
}

/// <summary>
/// Inputs needed to produce or prove an L2 batch.
/// </summary>
public sealed record BatchExecutionRequest
{
    /// <summary>L2 chain identifier.</summary>
    public required uint ChainId { get; init; }

    /// <summary>Batch sequence number (matches <see cref="L2BatchCommitment.BatchNumber"/>).</summary>
    public required ulong BatchNumber { get; init; }

    /// <summary>State root the executor must start from.</summary>
    public required UInt256 PreStateRoot { get; init; }

    /// <summary>
    /// Ordered transactions. Bytes are the canonical Neo serialization; the executor decodes them
    /// using the same path as <c>Transaction.DeserializeFrom</c>.
    /// </summary>
    public required IReadOnlyList<ReadOnlyMemory<byte>> Transactions { get; init; }

    /// <summary>Cross-chain messages this batch consumes from the L1 inbox queue.</summary>
    public required IReadOnlyList<CrossChainMessage> L1MessagesConsumed { get; init; }

    /// <summary>
    /// Per-block attribution of <see cref="Transactions"/> in execution order, one entry per L2
    /// block the batch covers. Entries must be contiguous, cover exactly the batch's block range,
    /// and carry timestamps within the <see cref="BlockContext"/> timestamp range. May be empty
    /// only when <see cref="Transactions"/> is empty.
    /// </summary>
    /// <remarks>
    /// In-memory execution-seam data (doc.md §7.2); it is not serialized into the DA payload or
    /// witness artifacts.
    /// </remarks>
    public required IReadOnlyList<L2BatchBlock> BlockTimeline { get; init; }

    /// <summary>Block-level context for deterministic execution.</summary>
    public required BatchBlockContext BlockContext { get; init; }

    /// <inheritdoc />
    public bool Equals(BatchExecutionRequest? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (ChainId != other.ChainId
            || BatchNumber != other.BatchNumber
            || !PreStateRoot.Equals(other.PreStateRoot)
            || !BlockContext.Equals(other.BlockContext)) return false;
        if (Transactions.Count != other.Transactions.Count) return false;
        for (var i = 0; i < Transactions.Count; i++)
        {
            if (!Transactions[i].Span.SequenceEqual(other.Transactions[i].Span)) return false;
        }
        if (L1MessagesConsumed.Count != other.L1MessagesConsumed.Count) return false;
        for (var i = 0; i < L1MessagesConsumed.Count; i++)
        {
            if (!L1MessagesConsumed[i].Equals(other.L1MessagesConsumed[i])) return false;
        }
        if (BlockTimeline.Count != other.BlockTimeline.Count) return false;
        for (var i = 0; i < BlockTimeline.Count; i++)
        {
            if (!BlockTimeline[i].Equals(other.BlockTimeline[i])) return false;
        }
        return true;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ChainId);
        hash.Add(BatchNumber);
        hash.Add(PreStateRoot);
        hash.Add(BlockContext);
        // Iterate explicitly so byte-content participates and list-reference identity does not.
        foreach (var tx in Transactions) hash.AddBytes(tx.Span);
        foreach (var msg in L1MessagesConsumed) hash.Add(msg);
        foreach (var block in BlockTimeline) hash.Add(block);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Output of <see cref="IL2BatchExecutor.ApplyBatchAsync"/>. Together with the request inputs, this is
/// enough to reconstruct an <see cref="L2BatchCommitment"/>.
/// </summary>
public sealed record BatchExecutionResult
{
    /// <summary>State root after every transaction in the batch is applied.</summary>
    public required UInt256 PostStateRoot { get; init; }

    /// <summary>Merkle root of receipts produced by the batch.</summary>
    public required UInt256 ReceiptRoot { get; init; }

    /// <summary>Merkle root of withdrawal records produced by the batch.</summary>
    public required UInt256 WithdrawalRoot { get; init; }

    /// <summary>Merkle root of L2 → L1 messages emitted during the batch.</summary>
    public required UInt256 L2ToL1MessageRoot { get; init; }

    /// <summary>Merkle root of L2 → L2 messages emitted during the batch.</summary>
    public required UInt256 L2ToL2MessageRoot { get; init; }

    /// <summary>Merkle root of the transaction list (matches <see cref="L2BatchCommitment.TxRoot"/>).</summary>
    public required UInt256 TxRoot { get; init; }

    /// <summary>Total gas consumed by the batch.</summary>
    public required long GasConsumed { get; init; }
}
