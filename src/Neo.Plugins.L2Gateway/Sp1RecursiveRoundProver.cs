using Neo.Cryptography;
using Neo.L2;

namespace Neo.Plugins.L2Gateway;

/// <summary>Canonical binary-tree frontend for the recursive Gateway SP1 terminal prover.</summary>
/// <remarks>
/// See doc.md §4 (Neo Gateway). This component computes the deterministic global message root and
/// a bounded per-round witness commitment under backend 0xC2. It does not claim to be the terminal
/// ZK proof: <see cref="Sp1GatewayProofProver"/> proves the complete canonical constituent set and
/// MessageRouter verifies that 356-byte proof before publication.
/// </remarks>
public sealed class Sp1RecursiveRoundProver : IRoundProver
{
    /// <summary>Dedicated recursive Gateway backend.</summary>
    public const byte ConstBackendId = Sp1GatewayProofProver.RecursiveAggregationBackendId;

    private static ReadOnlySpan<byte> NodeDomain => "NEO4GWN1"u8;

    /// <inheritdoc />
    public byte BackendId => ConstBackendId;

    /// <inheritdoc />
    public RoundResult Combine(RoundResult left, RoundResult? right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(left.MessageRootContribution);
        if (right is null) return left;
        ArgumentNullException.ThrowIfNull(right.MessageRootContribution);

        var root = HashPair(left.MessageRootContribution, right.MessageRootContribution);
        Span<byte> witness = stackalloc byte[8 + 4 * 32];
        NodeDomain.CopyTo(witness);
        left.MessageRootContribution.GetSpan().CopyTo(witness.Slice(8, 32));
        right.MessageRootContribution.GetSpan().CopyTo(witness.Slice(40, 32));
        Crypto.Hash256(left.ProofBytes.Span).CopyTo(witness.Slice(72, 32));
        Crypto.Hash256(right.ProofBytes.Span).CopyTo(witness.Slice(104, 32));
        return new RoundResult
        {
            MessageRootContribution = root,
            ProofBytes = Crypto.Hash256(witness),
        };
    }

    /// <summary>Compute the canonical odd-leaf-promoting root over constituent message roots.</summary>
    public static UInt256 ComputeGlobalMessageRoot(IReadOnlyList<L2BatchCommitment> constituents)
    {
        ArgumentNullException.ThrowIfNull(constituents);
        if (constituents.Count == 0)
            throw new ArgumentException("at least one constituent is required", nameof(constituents));
        var current = new UInt256[constituents.Count];
        for (var index = 0; index < constituents.Count; index++)
        {
            var constituent = constituents[index]
                ?? throw new ArgumentException($"constituents[{index}] is null", nameof(constituents));
            ArgumentNullException.ThrowIfNull(constituent.L2ToL2MessageRoot);
            current[index] = constituent.L2ToL2MessageRoot;
        }
        return ComputeGlobalMessageRootFromRoots(current);
    }

    /// <summary>
    /// Fold message roots with the canonical ODD-LEAF-PROMOTING rule.
    /// </summary>
    /// <remarks>
    /// When a level has an odd number of nodes, the last node is PROMOTED to the parent
    /// level unchanged (not hashed against a copy of itself).
    ///
    /// This is the rule the on-chain binding and the SP1 gateway guest use. The generic
    /// <c>Neo.L2.State.MerkleTree.ComputeRoot</c> uses the DUPLICATE rule (the last node
    /// is hashed against a copy of itself), so the two disagree whenever the number of
    /// constituents is odd. Every aggregator must use this method — a mixed convention
    /// produces a binding that the guest rejects, which shows up as "odd-numbered rounds
    /// fail while even-numbered rounds succeed".
    /// </remarks>
    public static UInt256 ComputeGlobalMessageRootFromRoots(IReadOnlyList<UInt256> messageRoots)
    {
        ArgumentNullException.ThrowIfNull(messageRoots);
        if (messageRoots.Count == 0)
            throw new ArgumentException("at least one message root is required", nameof(messageRoots));
        var current = new UInt256[messageRoots.Count];
        for (var index = 0; index < messageRoots.Count; index++)
        {
            current[index] = messageRoots[index]
                ?? throw new ArgumentException($"messageRoots[{index}] is null", nameof(messageRoots));
        }
        while (current.Length > 1)
        {
            var next = new UInt256[(current.Length + 1) / 2];
            for (var index = 0; index < next.Length; index++)
            {
                var left = current[index * 2];
                next[index] = index * 2 + 1 < current.Length
                    ? HashPair(left, current[index * 2 + 1])
                    : left;
            }
            current = next;
        }
        return current[0];
    }

    private static UInt256 HashPair(UInt256 left, UInt256 right)
    {
        Span<byte> pair = stackalloc byte[64];
        left.GetSpan().CopyTo(pair);
        right.GetSpan().CopyTo(pair[32..]);
        return new UInt256(Crypto.Hash256(pair));
    }
}
