using Neo.Cryptography;
using Neo.L2.State;

namespace Neo.L2.Batch;

/// <summary>
/// Stateful builder that an L2 batcher feeds with blocks, transactions, withdrawals, and
/// cross-chain messages. Once a flush condition is hit, the builder seals the batch and
/// emits an immutable <see cref="SealedBatch"/> via <see cref="SealArtifact"/>.
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
    private readonly List<(ulong Nonce, UInt256 TxHash)> _forcedInclusions = new();

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

    /// <summary>
    /// Prepend metadata for a forced-inclusion transaction without marking its L1 nonce consumed.
    /// </summary>
    public BatchBuilder AddForcedTransaction(
        ulong nonce,
        UInt256 txHash,
        ReadOnlyMemory<byte> serializedTx)
    {
        ArgumentNullException.ThrowIfNull(txHash);
        if (_batch.TransactionCount != _forcedInclusions.Count)
            throw new InvalidOperationException(
                "forced-inclusion transactions must precede ordinary transactions");
        if (_forcedInclusions.Any(entry => entry.Nonce == nonce))
            throw new InvalidOperationException(
                $"forced-inclusion nonce {nonce} is already in this batch");
        var encodedHash = new UInt256(Crypto.Hash256(serializedTx.Span));
        if (!encodedHash.Equals(txHash))
            throw new InvalidOperationException(
                $"forced-inclusion nonce {nonce} tx hash does not match encoded transaction");
        _batch.AddTransaction(serializedTx);
        _forcedInclusions.Add((nonce, new UInt256(txHash.GetSpan())));
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
    /// Seal the exact executable inputs without inventing execution roots, a DA commitment,
    /// public-input hash, or proof.
    /// </summary>
    public SealedBatch SealArtifact()
    {
        var context = _batch.BlockContext
            ?? throw new InvalidOperationException("BlockContext must be set before sealing");
        var transactionHashes = _batch.Transactions
            .Select(static transaction => new UInt256(Crypto.Hash256(transaction.Span)))
            .ToArray();
        var transactionTree = new Neo.L2.State.MerkleTree(transactionHashes);
        var forcedProofs = new ForcedInclusionConsumptionProof[_forcedInclusions.Count];
        for (var index = 0; index < forcedProofs.Length; index++)
        {
            var entry = _forcedInclusions[index];
            var proof = transactionTree.GetProof(index);
            forcedProofs[index] = new ForcedInclusionConsumptionProof
            {
                Nonce = entry.Nonce,
                LeafIndex = checked((uint)index),
                TxHash = entry.TxHash,
                Siblings = proof.Siblings,
            };
        }
        _batch.Seal();
        return new SealedBatch(
            _batch.ChainId,
            _batch.BatchNumber,
            _batch.FirstBlock,
            _batch.LastBlock,
            _batch.PreStateRoot,
            _batch.Transactions,
            _batch.L1MessagesConsumed,
            context,
            forcedProofs);
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
