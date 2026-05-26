using Neo.Cryptography;

namespace Neo.L2.State;

/// <summary>
/// Binary Merkle tree with inclusion-proof support, matching Neo's hashing convention
/// (<c>Hash256</c> over the concatenated children, odd-leaf duplication).
/// </summary>
/// <remarks>
/// Uses the same root construction as <see cref="Neo.Cryptography.MerkleTree"/> so the
/// roots produced here match what Neo's existing MerkleTree.ComputeRoot would produce.
/// We add proof generation and verification, which Neo's tree does not expose.
/// </remarks>
public sealed class MerkleTree
{
    private readonly UInt256[][] _levels;

    /// <summary>Root of the tree (or <see cref="UInt256.Zero"/> when empty).</summary>
    public UInt256 Root { get; }

    /// <summary>Number of leaves the tree was built from.</summary>
    public int LeafCount => _levels[0].Length;

    /// <summary>Depth of the tree (number of edges from leaf to root, 0 for single-leaf).</summary>
    public int Depth => _levels.Length - 1;

    /// <summary>
    /// Build a Merkle tree from <paramref name="leaves"/>. The order of leaves is preserved.
    /// </summary>
    public MerkleTree(IReadOnlyList<UInt256> leaves)
    {
        ArgumentNullException.ThrowIfNull(leaves);
        // Defense-in-depth: leaves[i] is UInt256 (reference type) and `required` doesn't
        // prevent null entries even when the collection itself is non-null. Same iter-158
        // / iter-168 pattern. Without this, CombineHash's GetSpan() would NRE deep in
        // the tree-build loop with no link to the bad index.
        for (var i = 0; i < leaves.Count; i++)
            if (leaves[i] is null)
                throw new ArgumentException($"leaves[{i}] is null", nameof(leaves));
        if (leaves.Count == 0)
        {
            _levels = [Array.Empty<UInt256>()];
            Root = UInt256.Zero;
            return;
        }

        var levels = new List<UInt256[]> { leaves.ToArray() };
        var current = levels[0];

        while (current.Length > 1)
        {
            var next = new UInt256[(current.Length + 1) / 2];
            for (var i = 0; i < next.Length; i++)
            {
                var left = current[i * 2];
                var right = (i * 2 + 1 < current.Length) ? current[i * 2 + 1] : left;
                next[i] = CombineHash(left, right);
            }
            levels.Add(next);
            current = next;
        }

        _levels = [.. levels];
        Root = current[0];
    }

    /// <summary>Compute only the root, without retaining intermediate levels.</summary>
    public static UInt256 ComputeRoot(IReadOnlyList<UInt256> leaves)
    {
        ArgumentNullException.ThrowIfNull(leaves);
        // Defense-in-depth: same per-entry null-guard as the constructor above. Without
        // this, leaves[0] is null returns null (single-leaf path), or CombineHash NREs
        // in the loop. Same iter-158/168 pattern.
        for (var i = 0; i < leaves.Count; i++)
            if (leaves[i] is null)
                throw new ArgumentException($"leaves[{i}] is null", nameof(leaves));
        if (leaves.Count == 0) return UInt256.Zero;
        if (leaves.Count == 1) return leaves[0];

        var current = leaves.ToArray();
        var n = current.Length;
        while (n > 1)
        {
            // Compute parent hashes in-place: parent[i] overwrites current[2*i],
            // which is safe because current[2*i] is not read after parent[i] is computed.
            var half = n / 2;
            for (var i = 0; i < half; i++)
            {
                var left = current[i * 2];
                var right = (i * 2 + 1 < n) ? current[i * 2 + 1] : left;
                current[i] = CombineHash(left, right);
            }
            // If n is odd, the last leaf is promoted (paired with itself).
            if (n % 2 != 0)
            {
                current[half] = current[n - 1];
                n = half + 1;
            }
            else
            {
                n = half;
            }
        }
        return current[0];
    }

    /// <summary>Generate an inclusion proof for the leaf at <paramref name="leafIndex"/>.</summary>
    public MerkleProof GetProof(int leafIndex)
    {
        if ((uint)leafIndex >= (uint)LeafCount)
            throw new ArgumentOutOfRangeException(nameof(leafIndex));

        var siblings = new List<UInt256>();
        ulong pathBitmap = 0;
        var idx = leafIndex;

        for (var d = 0; d < Depth; d++)
        {
            var level = _levels[d];
            var siblingIdx = (idx % 2 == 0) ? idx + 1 : idx - 1;
            var siblingIsRight = (idx % 2 == 0);

            var sibling = siblingIdx < level.Length ? level[siblingIdx] : level[idx];
            siblings.Add(sibling);

            if (!siblingIsRight) pathBitmap |= 1UL << d;

            idx /= 2;
        }

        return new MerkleProof
        {
            Leaf = _levels[0][leafIndex],
            LeafIndex = leafIndex,
            Siblings = siblings,
            PathBitmap = pathBitmap,
        };
    }

    /// <summary>Verify a proof against an expected root.</summary>
    public static bool Verify(MerkleProof proof, UInt256 expectedRoot)
    {
        ArgumentNullException.ThrowIfNull(proof);
        // Defense-in-depth: Leaf and Siblings entries are UInt256 (reference type);
        // `required` only forces "must be set," not "non-null." A null would NRE inside
        // CombineHash's GetSpan() with no link to the bad caller. Same iter-158 pattern
        // applied to MerkleProofSerializer.Encode.
        ArgumentNullException.ThrowIfNull(proof.Leaf);
        ArgumentNullException.ThrowIfNull(proof.Siblings);
        if (proof.Siblings.Count > 64)
            throw new ArgumentException("Path depth exceeds 64", nameof(proof));

        var current = proof.Leaf;
        for (var d = 0; d < proof.Siblings.Count; d++)
        {
            var sibling = proof.Siblings[d] ?? throw new ArgumentException(
                $"Siblings[{d}] is null", nameof(proof));
            var siblingIsLeft = ((proof.PathBitmap >> d) & 1UL) == 1UL;
            current = siblingIsLeft ? CombineHash(sibling, current) : CombineHash(current, sibling);
        }
        return current.Equals(expectedRoot);
    }

    private static UInt256 CombineHash(UInt256 left, UInt256 right)
    {
        Span<byte> buf = stackalloc byte[64];
        left.GetSpan().CopyTo(buf);
        right.GetSpan().CopyTo(buf[32..]);
        return new UInt256(Crypto.Hash256(buf));
    }
}

/// <summary>
/// Inclusion proof for a single leaf in a Merkle tree.
/// </summary>
/// <remarks>
/// <see cref="PathBitmap"/> encodes for each level whether the sibling is the left child (bit set)
/// or the right child (bit clear). Bit 0 corresponds to the leaf level.
/// </remarks>
public sealed record MerkleProof
{
    /// <summary>Leaf hash this proof is for.</summary>
    public required UInt256 Leaf { get; init; }

    /// <summary>Index of the leaf within its level (0-based).</summary>
    public required int LeafIndex { get; init; }

    /// <summary>Sibling hashes from leaf level to root level.</summary>
    public required IReadOnlyList<UInt256> Siblings { get; init; }

    /// <summary>Bit <c>i</c> set means the level-<c>i</c> sibling is the left child.</summary>
    public required ulong PathBitmap { get; init; }

    /// <summary>Verify this proof reconstructs to <paramref name="expectedRoot"/>. Equivalent to <c>MerkleTree.Verify(this, expectedRoot)</c>.</summary>
    public bool Verify(UInt256 expectedRoot) => MerkleTree.Verify(this, expectedRoot);
}
