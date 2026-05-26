using System.Buffers.Binary;
using Neo.Cryptography;

namespace Neo.Plugins.L2Gateway;

/// <summary>
/// Production <see cref="IRoundProver"/> that builds a binary Merkle tree across rounds.
/// Each round's <see cref="RoundResult.ProofBytes"/> commit to the two children's roots
/// (so a verifier walking the tree top-down can extract any leaf's inclusion proof) and
/// recursively contain the same structure for the two subtrees.
/// </summary>
/// <remarks>
/// <para>
/// Real cryptography, no committee, no toolchain dependency. Pair with
/// <see cref="BinaryTreeAggregator"/> for chains that want each constituent batch's
/// inclusion in the aggregate root to be independently provable. The aggregate root is
/// equivalent to a Merkle root over each batch's <c>L2ToL2MessageRoot</c>;
/// <see cref="ProveLeaf"/> extracts a canonical sibling-list inclusion proof, and
/// <see cref="VerifyLeaf"/> checks it.
/// </para>
/// <para>
/// Wire layout for a round's ProofBytes:
/// <list type="bullet">
///   <item><c>[1B version=1]</c></item>
///   <item><c>[32B leftSubtreeRoot] [32B rightSubtreeRoot]</c></item>
///   <item><c>[4B LE leftSubtreeBytesLen] [leftSubtreeBytes]</c> — recursive (empty for a leaf-level child)</item>
///   <item><c>[4B LE rightSubtreeBytesLen] [rightSubtreeBytes]</c></item>
/// </list>
/// </para>
/// </remarks>
public sealed class MerklePathRoundProver : IRoundProver
{
    /// <summary>Stable backend identifier emitted in <see cref="AggregatedCommitment.BackendId"/>.</summary>
    public const byte ConstBackendId = 0xC1;

    /// <summary>Wire-format version (currently <c>1</c>).</summary>
    public const byte Version = 1;

    /// <summary>Header size before subtree segments: 1B version + 32B leftRoot + 32B rightRoot.</summary>
    private const int HeaderSize = 1 + 32 + 32;

    /// <inheritdoc />
    public byte BackendId => ConstBackendId;

    /// <inheritdoc />
    public RoundResult Combine(RoundResult left, RoundResult? right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(left.MessageRootContribution);
        if (right is null) return left;
        ArgumentNullException.ThrowIfNull(right.MessageRootContribution);

        var leftRoot = left.MessageRootContribution;
        var rightRoot = right.MessageRootContribution;
        var combined = HashPair(leftRoot, rightRoot);

        var leftBytes = left.ProofBytes;
        var rightBytes = right.ProofBytes;
        var size = checked(HeaderSize + 4 + leftBytes.Length + 4 + rightBytes.Length);
        var buf = new byte[size];
        var span = buf.AsSpan();
        span[0] = Version;
        leftRoot.GetSpan().CopyTo(span.Slice(1, 32));
        rightRoot.GetSpan().CopyTo(span.Slice(33, 32));
        var pos = HeaderSize;
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(pos, 4), leftBytes.Length);
        pos += 4;
        leftBytes.Span.CopyTo(span.Slice(pos, leftBytes.Length));
        pos += leftBytes.Length;
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(pos, 4), rightBytes.Length);
        pos += 4;
        rightBytes.Span.CopyTo(span.Slice(pos, rightBytes.Length));
        pos += rightBytes.Length;
        if (pos != size) throw new InvalidOperationException("MerklePath round size mismatch");

        return new RoundResult { MessageRootContribution = combined, ProofBytes = buf };
    }

    /// <summary>
    /// Walk the aggregated tree top-down to extract the canonical sibling-list inclusion
    /// proof for the leaf at <paramref name="leafIndex"/>.
    /// </summary>
    /// <param name="aggregatedProof">The final round's <see cref="RoundResult.ProofBytes"/>.</param>
    /// <param name="leafIndex">0-based leaf index in original submission order.</param>
    /// <param name="totalLeaves">Total constituents that were aggregated.</param>
    /// <returns>
    /// Sibling hashes ordered from root-side to leaf-side. <see cref="VerifyLeaf"/>
    /// re-derives the aggregate root by hashing the leaf up through these siblings.
    /// </returns>
    public static IReadOnlyList<UInt256> ProveLeaf(ReadOnlyMemory<byte> aggregatedProof, int leafIndex, int totalLeaves)
    {
        if (totalLeaves <= 1) return Array.Empty<UInt256>();
        if (leafIndex < 0 || leafIndex >= totalLeaves)
            throw new ArgumentOutOfRangeException(nameof(leafIndex), $"leafIndex {leafIndex} out of [0, {totalLeaves})");
        var siblings = new List<UInt256>();
        Walk(aggregatedProof.Span, leafIndex, totalLeaves, siblings);
        return siblings;
    }

    /// <summary>
    /// Verify a per-leaf inclusion proof. Returns <see langword="true"/> iff
    /// hashing <paramref name="leaf"/> up through <paramref name="siblings"/> using
    /// the canonical aggregator-shape directions (re-derived from
    /// <paramref name="leafIndex"/> + total-leaf count) reproduces
    /// <paramref name="aggregateRoot"/>.
    /// </summary>
    /// <remarks>
    /// For non-power-of-2 leaf counts, depth varies per leaf (a trailing odd leaf
    /// promoted through every odd level has shorter sibling list than a leaf in the
    /// fully-balanced subtree). Each siblings list is paired with a directions list
    /// re-computed from totalLeaves so the verifier knows which side to hash on at
    /// every level.
    /// </remarks>
    public static bool VerifyLeaf(UInt256 aggregateRoot, UInt256 leaf, int leafIndex, IReadOnlyList<UInt256> siblings, int totalLeaves)
    {
        ArgumentNullException.ThrowIfNull(aggregateRoot);
        ArgumentNullException.ThrowIfNull(leaf);
        ArgumentNullException.ThrowIfNull(siblings);
        if (leafIndex < 0 || leafIndex >= totalLeaves) return false;
        if (totalLeaves <= 1) return siblings.Count == 0 && leaf == aggregateRoot;

        var directions = new List<bool>(siblings.Count); // true = leaf went LEFT at that level
        ComputeDirections(leafIndex, totalLeaves, directions);
        if (directions.Count != siblings.Count) return false;

        // Walk leaf-to-root: pair siblings + directions in reverse.
        var current = leaf;
        for (var i = siblings.Count - 1; i >= 0; i--)
        {
            current = directions[i]
                ? HashPair(current, siblings[i])  // leaf was LEFT, sibling on right
                : HashPair(siblings[i], current); // leaf was RIGHT, sibling on left
        }
        return current == aggregateRoot;
    }

    /// <summary>
    /// Re-derive the canonical "leaf is on LEFT (true) or RIGHT (false)" decision at
    /// each tree level (root → leaf order) for the given <paramref name="leafIndex"/> in
    /// a tree of <paramref name="subtreeSize"/> leaves. Mirrors <see cref="Walk"/>'s
    /// recursion; used by <see cref="VerifyLeaf"/>.
    /// </summary>
    private static void ComputeDirections(int leafIndex, int subtreeSize, List<bool> dirs)
    {
        if (subtreeSize <= 1) return;
        var leftSize = 1;
        while (leftSize * 2 < subtreeSize) leftSize *= 2;
        if (leafIndex < leftSize)
        {
            dirs.Add(true); // leaf is on LEFT
            ComputeDirections(leafIndex, leftSize, dirs);
        }
        else
        {
            dirs.Add(false); // leaf is on RIGHT
            ComputeDirections(leafIndex - leftSize, subtreeSize - leftSize, dirs);
        }
    }

    private static void Walk(ReadOnlySpan<byte> bytes, int leafIndex, int subtreeSize, List<UInt256> siblings)
    {
        if (subtreeSize <= 1) return;
        if (bytes.Length < HeaderSize)
            throw new InvalidDataException($"merkle-path proof bytes too small ({bytes.Length} < {HeaderSize})");
        if (bytes[0] != Version)
            throw new InvalidDataException($"unsupported merkle-path version {bytes[0]}");

        var leftRoot = new UInt256(bytes.Slice(1, 32).ToArray());
        var rightRoot = new UInt256(bytes.Slice(33, 32).ToArray());
        var leftLen = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(HeaderSize, 4));
        var leftBytes = bytes.Slice(HeaderSize + 4, leftLen);
        var rightLen = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(HeaderSize + 4 + leftLen, 4));
        var rightBytes = bytes.Slice(HeaderSize + 4 + leftLen + 4, rightLen);

        // Mirror BinaryTreeAggregator's actual reduction. Tracing the algorithm: at each
        // level next.Length = (current.Length+1)/2, with the trailing odd item promoted
        // unchanged. The closed form for the LEFT subtree's leaf count from the root is
        // "the largest power of 2 strictly less than N" — except when N is itself a power
        // of 2, in which case it's N/2 (equal split). Both cases collapse to: start at 1
        // and double while the result is < N.
        var leftSize = 1;
        while (leftSize * 2 < subtreeSize) leftSize *= 2;
        var rightSize = subtreeSize - leftSize;

        if (leafIndex < leftSize)
        {
            siblings.Add(rightRoot);
            Walk(leftBytes, leafIndex, leftSize, siblings);
        }
        else
        {
            siblings.Add(leftRoot);
            Walk(rightBytes, leafIndex - leftSize, rightSize, siblings);
        }
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
}
