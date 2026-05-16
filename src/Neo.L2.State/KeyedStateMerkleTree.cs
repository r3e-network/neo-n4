using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using Neo.Cryptography;

namespace Neo.L2.State;

/// <summary>
/// Binary Merkle tree over sorted-key (key, value) pairs. The L2 state-root
/// commitment used by <see cref="L2BatchCommitment.PostStateRoot"/> when running
/// the production <c>MerkleStateBatchExecutor</c>.
/// </summary>
/// <remarks>
/// <para>
/// Same cryptographic primitive as the production L2s use (ZKsync, Polygon zkEVM,
/// Optimism) for state roots — not Ethereum mainnet's MPT. Real Merkle commitments
/// over sorted (key, value) pairs hashed pairwise up to a single root. Inclusion
/// proofs are canonical sibling lists in leaf-to-root order.
/// </para>
/// <para>
/// Wire format of a leaf: <c>Hash256([4B keyLen LE][keyBytes][4B valueLen LE][valueBytes])</c>.
/// Length-prefixing prevents the (key, value) ↔ (key+suffix, value-suffix) ambiguity
/// that flat concatenation would introduce.
/// </para>
/// <para>
/// Tree composition delegates to <see cref="MerkleTree"/> (Neo classic Hash256 with
/// odd-leaf duplication). This convention matches the on-chain
/// <c>NeoHub.SettlementManager.VerifyStateLeafWithProof</c> verifier byte-for-byte:
/// every level has a sibling (the trailing odd leaf is paired with itself), and the
/// verifier folds leaf-to-root using the leaf-index's low bit at level i to decide
/// whether the running hash sits left (bit=0) or right (bit=1) of the sibling.
/// </para>
/// <para>
/// Determinism contract: same set of (key, value) pairs → byte-identical root,
/// regardless of insertion order. Iteration order at leaf level is canonical
/// (lexicographic on key bytes).
/// </para>
/// </remarks>
public static class KeyedStateMerkleTree
{
    /// <summary>
    /// Compute the root of a binary Merkle tree over <paramref name="pairs"/>.
    /// Empty input → <see cref="UInt256.Zero"/>.
    /// </summary>
    public static UInt256 ComputeRoot(IEnumerable<(byte[] Key, byte[] Value)> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        var leaves = SortAndHash(pairs);
        return MerkleTree.ComputeRoot(leaves);
    }

    /// <summary>
    /// Compute the canonical sibling-list inclusion proof for the leaf at
    /// <paramref name="leafIndex"/> in a tree built from <paramref name="pairs"/>
    /// (sorted lexicographically by key).
    /// </summary>
    /// <returns>Sibling hashes in leaf-to-root order — i.e. element 0 pairs with
    /// the leaf at level 0, element 1 pairs with the result at level 1, etc.
    /// Matches the order consumed by the on-chain <c>VerifyStateLeafWithProof</c>
    /// fold loop.</returns>
    public static IReadOnlyList<UInt256> Prove(IEnumerable<(byte[] Key, byte[] Value)> pairs, int leafIndex)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        var leaves = SortAndHash(pairs);
        if (leaves.Length <= 1) return Array.Empty<UInt256>();
        if (leafIndex < 0 || leafIndex >= leaves.Length)
            throw new ArgumentOutOfRangeException(nameof(leafIndex), $"leafIndex {leafIndex} out of [0, {leaves.Length})");

        var tree = new MerkleTree(leaves);
        var proof = tree.GetProof(leafIndex);
        return proof.Siblings;
    }

    /// <summary>
    /// Verify a leaf inclusion proof. <see langword="true"/> iff hashing the
    /// leaf up through <paramref name="siblings"/> in leaf-to-root order, using
    /// the leaf-index bits to decide left/right pairing at each level, reproduces
    /// <paramref name="root"/>. Fold logic matches <c>SettlementManager.VerifyStateLeafWithProof</c>
    /// byte-for-byte.
    /// </summary>
    public static bool Verify(UInt256 root, byte[] key, byte[] value, int leafIndex, int totalLeaves, IReadOnlyList<UInt256> siblings)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(siblings);
        if (totalLeaves <= 0) return false;
        if (leafIndex < 0 || leafIndex >= totalLeaves) return false;
        if (totalLeaves == 1) return siblings.Count == 0 && HashLeaf(key, value) == root;

        // Neo classic walk: leaf-to-root, every level has a sibling, idx bit
        // decides left/right. Same loop the on-chain verifier runs.
        var current = HashLeaf(key, value);
        var idx = (ulong)leafIndex;
        for (var i = 0; i < siblings.Count; i++)
        {
            var sib = siblings[i];
            if (sib is null) return false;
            current = ((idx & 1UL) == 0UL)
                ? HashPair(current, sib)
                : HashPair(sib, current);
            idx >>= 1;
        }
        return current == root;
    }

    /// <summary>Hash one leaf with length-prefixed key + value (canonical, unambiguous).</summary>
    public static UInt256 HashLeaf(byte[] key, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        var size = checked(4 + key.Length + 4 + value.Length);
        var buf = new byte[size];
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(0, 4), key.Length);
        key.CopyTo(buf.AsSpan(4));
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(4 + key.Length, 4), value.Length);
        value.CopyTo(buf.AsSpan(8 + key.Length));
        return new UInt256(Crypto.Hash256(buf));
    }

    private static UInt256[] SortAndHash(IEnumerable<(byte[] Key, byte[] Value)> pairs)
    {
        return pairs
            .OrderBy(p => p.Key, ByteSeqComparer.Instance)
            .Select(p => HashLeaf(p.Key, p.Value))
            .ToArray();
    }

    private static UInt256 HashPair(UInt256 left, UInt256 right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        Span<byte> buf = stackalloc byte[64];
        left.GetSpan().CopyTo(buf);
        right.GetSpan().CopyTo(buf[32..]);
        return new UInt256(Crypto.Hash256(buf));
    }

    private sealed class ByteSeqComparer : IComparer<byte[]>
    {
        public static readonly ByteSeqComparer Instance = new();
        public int Compare(byte[]? x, byte[]? y)
        {
            if (x is null || y is null) return (x is null ? 0 : 1) - (y is null ? 0 : 1);
            var min = Math.Min(x.Length, y.Length);
            for (var i = 0; i < min; i++)
            {
                var d = x[i] - y[i];
                if (d != 0) return d;
            }
            return x.Length - y.Length;
        }
    }
}
