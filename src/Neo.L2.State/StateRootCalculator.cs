using System.Buffers.Binary;
using Neo.Cryptography;

namespace Neo.L2.State;

/// <summary>
/// Helpers that combine the three batch-level Merkle roots and the L1 inbox messages
/// into the deterministic public-input hash that NeoHub verifies.
/// </summary>
/// <remarks>
/// See doc.md §7.3 (StateRootGenerator) and §8.3 (Public Inputs).
/// <para>
/// This module does NOT compute the state-storage root itself — that comes from the
/// MPTTrie inside the L2's persistence layer. It only ties together the L2-batch-level
/// derivations the L1 verifier needs to see.
/// </para>
/// </remarks>
public static class StateRootCalculator
{
    /// <summary>
    /// Compute the canonical hash of the L1 messages consumed by a batch. The order is the
    /// order in which the batch executor consumed them.
    /// </summary>
    public static UInt256 HashL1Messages(IReadOnlyList<CrossChainMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count == 0) return UInt256.Zero;

        var leaves = new UInt256[messages.Count];
        for (var i = 0; i < messages.Count; i++)
            leaves[i] = MessageHasher.HashMessage(messages[i]);
        return MerkleTree.ComputeRoot(leaves);
    }

    /// <summary>
    /// Compute the canonical Merkle root of the txs in this batch. <paramref name="txHashes"/>
    /// is expected to already be the per-tx Hash256 (matches Neo's <c>Transaction.Hash</c>).
    /// </summary>
    public static UInt256 ComputeTxRoot(IReadOnlyList<UInt256> txHashes) =>
        MerkleTree.ComputeRoot(txHashes);

    /// <summary>
    /// Compute the canonical Merkle root of receipts.
    /// </summary>
    public static UInt256 ComputeReceiptRoot(IReadOnlyList<UInt256> receiptHashes) =>
        MerkleTree.ComputeRoot(receiptHashes);

    /// <summary>
    /// Hash the deterministic block context (matches <c>blockContextHash</c> in public inputs).
    /// </summary>
    public static UInt256 HashBlockContext(BatchBlockContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Span<byte> buffer = stackalloc byte[4 + 8 + 8 + 32 + 4];
        var pos = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(pos, 4), context.L1FinalizedHeight); pos += 4;
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(pos, 8), context.FirstBlockTimestamp); pos += 8;
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(pos, 8), context.LastBlockTimestamp); pos += 8;
        context.SequencerCommitteeHash.GetSpan().CopyTo(buffer.Slice(pos, 32)); pos += 32;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(pos, 4), context.Network); pos += 4;

        return new UInt256(Crypto.Hash256(buffer));
    }

    /// <summary>
    /// Compute the canonical hash that gets put into <see cref="L2BatchCommitment.PublicInputHash"/>.
    /// </summary>
    public static UInt256 HashPublicInputs(PublicInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        // Use the canonical encoding from BatchSerializer-equivalent layout.
        Span<byte> buffer = stackalloc byte[4 + 8 + 10 * 32];
        var pos = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(pos, 4), inputs.ChainId); pos += 4;
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(pos, 8), inputs.BatchNumber); pos += 8;

        WriteRoot(buffer, ref pos, inputs.PreStateRoot);
        WriteRoot(buffer, ref pos, inputs.PostStateRoot);
        WriteRoot(buffer, ref pos, inputs.TxRoot);
        WriteRoot(buffer, ref pos, inputs.ReceiptRoot);
        WriteRoot(buffer, ref pos, inputs.WithdrawalRoot);
        WriteRoot(buffer, ref pos, inputs.L2ToL1MessageRoot);
        WriteRoot(buffer, ref pos, inputs.L2ToL2MessageRoot);
        WriteRoot(buffer, ref pos, inputs.L1MessageHash);
        WriteRoot(buffer, ref pos, inputs.DACommitment);
        WriteRoot(buffer, ref pos, inputs.BlockContextHash);

        return new UInt256(Crypto.Hash256(buffer));
    }

    private static void WriteRoot(Span<byte> buffer, ref int pos, UInt256 root)
    {
        root.GetSpan().CopyTo(buffer.Slice(pos, 32));
        pos += 32;
    }
}
