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
/// proofs are canonical sibling lists.
/// </para>
/// <para>
/// Wire format of a leaf: <c>Hash256([4B keyLen LE][keyBytes][4B valueLen LE][valueBytes])</c>.
/// Length-prefixing prevents the (key, value) ↔ (key+suffix, value-suffix) ambiguity
/// that flat concatenation would introduce.
/// </para>
/// <para>
/// Determinism contract: same set of (key, value) pairs → byte-identical root,
/// regardless of insertion order. Iteration order at leaf level is canonical
/// (lexicographic on key bytes); the pairwise reduction mirrors the
/// <c>BinaryTreeAggregator</c> shape used in Phase 5 — odd-trailing leaf
/// promoted unchanged through odd levels.
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
        var leaves = pairs
            .OrderBy(p => p.Key, ByteSeqComparer.Instance)
            .Select(p => HashLeaf(p.Key, p.Value))
            .ToArray();
        return ComputeRootFromLeaves(leaves);
    }

    /// <summary>
    /// Compute the canonical sibling-list inclusion proof for the leaf at
    /// <paramref name="leafIndex"/> in a tree built from <paramref name="pairs"/>
    /// (sorted lexicographically by key).
    /// </summary>
    /// <returns>Sibling hashes in root-to-leaf order.</returns>
    public static IReadOnlyList<UInt256> Prove(IEnumerable<(byte[] Key, byte[] Value)> pairs, int leafIndex)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        var leaves = pairs
            .OrderBy(p => p.Key, ByteSeqComparer.Instance)
            .Select(p => HashLeaf(p.Key, p.Value))
            .ToArray();
        if (leaves.Length <= 1) return Array.Empty<UInt256>();
        if (leafIndex < 0 || leafIndex >= leaves.Length)
            throw new ArgumentOutOfRangeException(nameof(leafIndex), $"leafIndex {leafIndex} out of [0, {leaves.Length})");

        var siblings = new List<UInt256>();
        BuildPath(leaves, leafIndex, siblings);
        return siblings;
    }

    /// <summary>
    /// Verify a leaf inclusion proof. <see langword="true"/> iff hashing the
    /// leaf up through <paramref name="siblings"/> using directions re-derived
    /// from <paramref name="leafIndex"/> + <paramref name="totalLeaves"/>
    /// reproduces <paramref name="root"/>.
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

        var directions = new List<bool>(siblings.Count);
        ComputeDirections(leafIndex, totalLeaves, directions);
        if (directions.Count != siblings.Count) return false;

        var current = HashLeaf(key, value);
        for (var i = siblings.Count - 1; i >= 0; i--)
        {
            current = directions[i]
                ? HashPair(current, siblings[i])
                : HashPair(siblings[i], current);
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

    private static UInt256 ComputeRootFromLeaves(UInt256[] leaves)
    {
        if (leaves.Length == 0) return UInt256.Zero;
        if (leaves.Length == 1) return leaves[0];
        var current = leaves;
        while (current.Length > 1)
        {
            var next = new UInt256[(current.Length + 1) / 2];
            for (var i = 0; i < next.Length; i++)
            {
                var left = current[i * 2];
                next[i] = (i * 2 + 1 < current.Length)
                    ? HashPair(left, current[i * 2 + 1])
                    : left; // odd-trailing leaf promoted unchanged
            }
            current = next;
        }
        return current[0];
    }

    private static void BuildPath(UInt256[] leaves, int leafIndex, List<UInt256> siblings)
    {
        var current = leaves;
        var idx = leafIndex;
        var pathStack = new Stack<UInt256>();
        // Walk bottom-up, recording the sibling at each level.
        while (current.Length > 1)
        {
            var next = new UInt256[(current.Length + 1) / 2];
            for (var i = 0; i < next.Length; i++)
            {
                var left = current[i * 2];
                next[i] = (i * 2 + 1 < current.Length)
                    ? HashPair(left, current[i * 2 + 1])
                    : left;
            }
            // Determine sibling at this level.
            var pairIndex = idx / 2;
            var isLeft = (idx & 1) == 0;
            if (isLeft && idx + 1 < current.Length)
                pathStack.Push(current[idx + 1]);
            else if (!isLeft)
                pathStack.Push(current[idx - 1]);
            // (else: odd-trailing leaf at this level — no sibling, no entry pushed)
            idx = pairIndex;
            current = next;
        }
        while (pathStack.Count > 0) siblings.Add(pathStack.Pop());
    }

    private static void ComputeDirections(int leafIndex, int subtreeSize, List<bool> dirs)
    {
        // Mirrors MerklePathRoundProver's direction-recovery: at each level top-down
        // until subtreeSize == 1, decide whether the leaf is in the left half.
        // The aggregator's rule: leftSize = next-power-of-2-strictly-less-than N
        // (or N/2 when N is a power of 2). For balanced subtrees this collapses to
        // (subtreeSize+1)/2 wouldn't be right — explicit rule below matches the
        // bottom-up reduction in ComputeRootFromLeaves.
        if (subtreeSize <= 1) return;

        var leftSize = LeftHalfSize(subtreeSize);
        if (leafIndex < leftSize)
        {
            dirs.Add(true);
            ComputeDirections(leafIndex, leftSize, dirs);
        }
        else
        {
            dirs.Add(false);
            ComputeDirections(leafIndex - leftSize, subtreeSize - leftSize, dirs);
        }
    }

    /// <summary>
    /// Match the actual reduction shape <see cref="ComputeRootFromLeaves"/> produces.
    /// At each level <c>next.Length = (current.Length+1)/2</c> with odd-trailing leaf
    /// promoted; the resulting tree's root has its left subtree carrying ceil(N/2) of
    /// the original leaves when N is even, and a non-power-of-2 split otherwise.
    /// We compute leftSize by simulating the reduction once and counting how many
    /// of the original leaves land in next[0]'s subtree.
    /// </summary>
    private static int LeftHalfSize(int subtreeSize)
    {
        // Bottom-up simulation: track each original leaf's index through the tree.
        // The leaf at index i lands in next[i / 2] at level 1, etc. The final root
        // comes from next[0] which paired (or promoted) prev[0] and prev[1]. We
        // need the count of original leaves whose final pair-index path hits 0
        // through every level's left side.
        // Equivalent closed form: the leftHalf at the root is the largest power of 2
        // strictly less than subtreeSize (matches BinaryTreeAggregator's split).
        if (subtreeSize <= 1) return 1;
        var p = 1;
        while (p * 2 < subtreeSize) p *= 2;
        return p;
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
