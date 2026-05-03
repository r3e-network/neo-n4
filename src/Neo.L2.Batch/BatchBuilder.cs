namespace Neo.L2.Batch;

/// <summary>
/// Stateful builder that an L2 batcher feeds with blocks, transactions, withdrawals, and
/// cross-chain messages. Once a flush condition is hit, the builder seals the batch and
/// emits an <see cref="L2BatchCommitment"/> via <see cref="Seal"/>.
/// </summary>
/// <remarks>
/// See doc.md §7.2.
/// <para>
/// Flush conditions (size limit, time limit, finality target) are decided externally by the
/// hosting plugin — the builder itself only enforces append-order invariants. Hosts call
/// <see cref="Seal"/> when they decide it is time to settle.
/// </para>
/// </remarks>
public sealed class BatchBuilder
{
    private readonly L2Batch _batch;

    /// <summary>The in-progress batch.</summary>
    public L2Batch Batch => _batch;

    /// <summary>Begin a new batch with the given parameters.</summary>
    public BatchBuilder(uint chainId, ulong batchNumber, ulong firstBlock, UInt256 preStateRoot)
    {
        _batch = new L2Batch(chainId, batchNumber, firstBlock, preStateRoot);
    }

    /// <summary>Mark a new L2 block as included in the batch (raises <see cref="L2Batch.LastBlock"/>).</summary>
    public BatchBuilder AddBlock(ulong blockIndex)
    {
        _batch.AddBlock(blockIndex);
        return this;
    }

    /// <summary>Append a serialized transaction.</summary>
    public BatchBuilder AddTransaction(ReadOnlyMemory<byte> serializedTx)
    {
        _batch.AddTransaction(serializedTx);
        return this;
    }

    /// <summary>Mark an inbound L1 message as consumed by this batch.</summary>
    public BatchBuilder ConsumeL1Message(CrossChainMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _batch.AddL1Message(message);
        return this;
    }

    /// <summary>Record a withdrawal that will go into the batch's withdrawal Merkle tree.</summary>
    public BatchBuilder AddWithdrawal(WithdrawalRequest withdrawal)
    {
        ArgumentNullException.ThrowIfNull(withdrawal);
        _batch.AddWithdrawal(withdrawal);
        return this;
    }

    /// <summary>Record an L2 → L1 message emitted during execution.</summary>
    public BatchBuilder AddL2ToL1Message(CrossChainMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _batch.AddL2ToL1Message(message);
        return this;
    }

    /// <summary>Record an L2 → L2 message emitted during execution.</summary>
    public BatchBuilder AddL2ToL2Message(CrossChainMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _batch.AddL2ToL2Message(message);
        return this;
    }

    /// <summary>Set the deterministic block context that will be hashed into the public inputs.</summary>
    public BatchBuilder WithBlockContext(BatchBlockContext context)
    {
        _batch.BlockContext = context;
        return this;
    }

    /// <summary>
    /// Produce a <see cref="BatchExecutionRequest"/> ready to feed to <see cref="IL2BatchExecutor"/>.
    /// Caller is expected to invoke the executor and pair the result with this request to produce
    /// the final <see cref="L2BatchCommitment"/> via <see cref="ToCommitment"/>.
    /// </summary>
    public BatchExecutionRequest ToExecutionRequest()
    {
        var ctx = _batch.BlockContext ?? throw new InvalidOperationException("BlockContext must be set before sealing");
        return new BatchExecutionRequest
        {
            ChainId = _batch.ChainId,
            BatchNumber = _batch.BatchNumber,
            PreStateRoot = _batch.PreStateRoot,
            Transactions = _batch.Transactions,
            L1MessagesConsumed = _batch.L1MessagesConsumed,
            BlockContext = ctx,
        };
    }

    /// <summary>
    /// Combine an executor result with this builder's metadata to produce the final
    /// <see cref="L2BatchCommitment"/>. Seals the underlying batch.
    /// </summary>
    public L2BatchCommitment ToCommitment(
        BatchExecutionResult executionResult,
        UInt256 daCommitment,
        UInt256 publicInputHash,
        ProofType proofType,
        ReadOnlyMemory<byte> proof)
    {
        ArgumentNullException.ThrowIfNull(executionResult);
        // Defense-in-depth: daCommitment and publicInputHash are UInt256 (reference type),
        // and `_batch.Seal()` below mutates state irreversibly. If a null hash slips through
        // here, the iter-156/157 BatchSerializer.Encode null-guards would catch it later
        // — but only AFTER the batch is sealed. Surface it here so a re-attempt can succeed.
        ArgumentNullException.ThrowIfNull(daCommitment);
        ArgumentNullException.ThrowIfNull(publicInputHash);
        _batch.Seal();
        return new L2BatchCommitment
        {
            ChainId = _batch.ChainId,
            BatchNumber = _batch.BatchNumber,
            FirstBlock = _batch.FirstBlock,
            LastBlock = _batch.LastBlock,
            PreStateRoot = _batch.PreStateRoot,
            PostStateRoot = executionResult.PostStateRoot,
            TxRoot = executionResult.TxRoot,
            ReceiptRoot = executionResult.ReceiptRoot,
            WithdrawalRoot = executionResult.WithdrawalRoot,
            L2ToL1MessageRoot = executionResult.L2ToL1MessageRoot,
            L2ToL2MessageRoot = executionResult.L2ToL2MessageRoot,
            DACommitment = daCommitment,
            PublicInputHash = publicInputHash,
            ProofType = proofType,
            Proof = proof,
        };
    }

    /// <summary>Convenience: seal and return commitment in one call.</summary>
    public L2BatchCommitment Seal(
        BatchExecutionResult executionResult,
        UInt256 daCommitment,
        UInt256 publicInputHash,
        ProofType proofType,
        ReadOnlyMemory<byte> proof) =>
        ToCommitment(executionResult, daCommitment, publicInputHash, proofType, proof);
}
