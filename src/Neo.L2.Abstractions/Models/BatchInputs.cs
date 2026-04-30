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

    /// <summary>Block-level context for deterministic execution.</summary>
    public required BatchBlockContext BlockContext { get; init; }
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
