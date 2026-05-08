using System;
using System.Linq;
using Neo;
using Neo.Cryptography;
using Neo.L2;
using Neo.L2.Batch;
using Neo.Plugins.L2Gateway;

namespace Neo.Plugins.L2Gateway.UnitTests;

/// <summary>
/// Tests for the production-grade <see cref="MerklePathRoundProver"/>: each
/// constituent batch's L2-to-L2 message root must be independently provable
/// against the aggregate root via the canonical sibling-list proof carried in
/// the aggregated proof bytes.
/// </summary>
[TestClass]
public class UT_MerklePathRoundProver
{
    private static UInt256 LeafRoot(byte seed)
    {
        var bytes = new byte[32];
        for (var i = 0; i < 32; i++) bytes[i] = (byte)(seed + i);
        return new UInt256(bytes);
    }

    private static L2BatchCommitment MakeBatch(byte seed) => new()
    {
        ChainId = 1099,
        BatchNumber = seed,
        FirstBlock = seed * 100UL,
        LastBlock = seed * 100UL + 99,
        PreStateRoot = UInt256.Zero,
        PostStateRoot = UInt256.Zero,
        TxRoot = UInt256.Zero,
        ReceiptRoot = UInt256.Zero,
        WithdrawalRoot = UInt256.Zero,
        L2ToL1MessageRoot = UInt256.Zero,
        L2ToL2MessageRoot = LeafRoot(seed),
        DACommitment = UInt256.Zero,
        PublicInputHash = UInt256.Zero,
        ProofType = ProofType.Multisig,
        Proof = new byte[] { seed },
    };

    [TestMethod]
    public void EndToEnd_FourLeaves_EveryLeafProvableAgainstRoot()
    {
        var aggregator = new BinaryTreeAggregator(new MerklePathRoundProver());
        for (var i = 0; i < 4; i++) aggregator.Submit(MakeBatch((byte)(0x10 + i)));
        var aggregate = aggregator.Aggregate();
        Assert.IsNotNull(aggregate);
        Assert.AreEqual(MerklePathRoundProver.ConstBackendId, aggregate.BackendId);

        // Each of the 4 constituents must be provable.
        for (var leafIndex = 0; leafIndex < 4; leafIndex++)
        {
            var siblings = MerklePathRoundProver.ProveLeaf(aggregate.AggregatedProof, leafIndex, totalLeaves: 4);
            Assert.AreEqual(2, siblings.Count, $"4-leaf tree → 2-deep proof; leaf {leafIndex}");
            var leaf = aggregate.Constituents[leafIndex].L2ToL2MessageRoot;
            Assert.IsTrue(
                MerklePathRoundProver.VerifyLeaf(aggregate.GlobalMessageRoot, leaf, leafIndex, siblings, aggregate.Constituents.Count),
                $"leaf {leafIndex} inclusion proof must verify against the aggregate root");
        }
    }

    [TestMethod]
    public void EndToEnd_EightLeaves_ProofDepthIsLogN()
    {
        var aggregator = new BinaryTreeAggregator(new MerklePathRoundProver());
        for (var i = 0; i < 8; i++) aggregator.Submit(MakeBatch((byte)(0x20 + i)));
        var aggregate = aggregator.Aggregate();
        Assert.IsNotNull(aggregate);

        for (var leafIndex = 0; leafIndex < 8; leafIndex++)
        {
            var siblings = MerklePathRoundProver.ProveLeaf(aggregate.AggregatedProof, leafIndex, 8);
            Assert.AreEqual(3, siblings.Count, $"8-leaf tree → 3-deep proof; leaf {leafIndex}");
            var leaf = aggregate.Constituents[leafIndex].L2ToL2MessageRoot;
            Assert.IsTrue(
                MerklePathRoundProver.VerifyLeaf(aggregate.GlobalMessageRoot, leaf, leafIndex, siblings, aggregate.Constituents.Count),
                $"leaf {leafIndex} of 8 must verify");
        }
    }

    [TestMethod]
    public void Verify_RejectsAlteredLeaf()
    {
        var aggregator = new BinaryTreeAggregator(new MerklePathRoundProver());
        for (var i = 0; i < 4; i++) aggregator.Submit(MakeBatch((byte)(0x30 + i)));
        var aggregate = aggregator.Aggregate()!;

        var siblings = MerklePathRoundProver.ProveLeaf(aggregate.AggregatedProof, leafIndex: 1, totalLeaves: 4);
        var realLeaf = aggregate.Constituents[1].L2ToL2MessageRoot;
        var falseLeaf = LeafRoot(0xFF); // not actually in the tree

        Assert.IsTrue(MerklePathRoundProver.VerifyLeaf(aggregate.GlobalMessageRoot, realLeaf, 1, siblings, aggregate.Constituents.Count));
        Assert.IsFalse(MerklePathRoundProver.VerifyLeaf(aggregate.GlobalMessageRoot, falseLeaf, 1, siblings, aggregate.Constituents.Count),
            "a leaf NOT in the tree must fail inclusion verification");
    }

    [TestMethod]
    public void Verify_RejectsCorrectLeafAtWrongIndex()
    {
        // A real leaf claimed at a different index must fail — the bit pattern of the
        // index drives left/right hashing direction at each level.
        var aggregator = new BinaryTreeAggregator(new MerklePathRoundProver());
        for (var i = 0; i < 4; i++) aggregator.Submit(MakeBatch((byte)(0x40 + i)));
        var aggregate = aggregator.Aggregate()!;

        var siblings = MerklePathRoundProver.ProveLeaf(aggregate.AggregatedProof, leafIndex: 2, totalLeaves: 4);
        var leafAt2 = aggregate.Constituents[2].L2ToL2MessageRoot;

        Assert.IsTrue(MerklePathRoundProver.VerifyLeaf(aggregate.GlobalMessageRoot, leafAt2, 2, siblings, aggregate.Constituents.Count));
        Assert.IsFalse(MerklePathRoundProver.VerifyLeaf(aggregate.GlobalMessageRoot, leafAt2, 3, siblings, aggregate.Constituents.Count),
            "claiming the leaf is at index 3 (off by one) must fail");
        Assert.IsFalse(MerklePathRoundProver.VerifyLeaf(aggregate.GlobalMessageRoot, leafAt2, 0, siblings, aggregate.Constituents.Count),
            "claiming the leaf is at index 0 must fail");
    }

    [TestMethod]
    public void OddCardinality_TrailingLeafPromotedThroughOddLevels()
    {
        // 5 leaves: round 1 forms 3 nodes (pairs (0,1) and (2,3); leaf 4 promoted unchanged
        // since right=null). Round 2 forms 2 nodes (pair (round1[0], round1[1]); round1[2]
        // promoted). Round 3 forms the root.
        // Every leaf — including the trailing one — must be provable against the root.
        var aggregator = new BinaryTreeAggregator(new MerklePathRoundProver());
        for (var i = 0; i < 5; i++) aggregator.Submit(MakeBatch((byte)(0x50 + i)));
        var aggregate = aggregator.Aggregate()!;

        for (var leafIndex = 0; leafIndex < 5; leafIndex++)
        {
            var siblings = MerklePathRoundProver.ProveLeaf(aggregate.AggregatedProof, leafIndex, totalLeaves: 5);
            var leaf = aggregate.Constituents[leafIndex].L2ToL2MessageRoot;
            // Note: for odd-cardinality trees, depth varies per leaf; we don't assert a
            // fixed depth, only that verification succeeds.
            Assert.IsTrue(
                MerklePathRoundProver.VerifyLeaf(aggregate.GlobalMessageRoot, leaf, leafIndex, siblings, aggregate.Constituents.Count),
                $"leaf {leafIndex} of 5 (odd) must verify");
        }
    }

    [TestMethod]
    public void SingleLeaf_NoProofNeeded()
    {
        // 1-leaf "tree": the leaf IS the root. ProveLeaf returns empty siblings.
        var aggregator = new BinaryTreeAggregator(new MerklePathRoundProver());
        aggregator.Submit(MakeBatch(0x60));
        var aggregate = aggregator.Aggregate()!;
        Assert.AreEqual(1, aggregate.Constituents.Count);

        var siblings = MerklePathRoundProver.ProveLeaf(aggregate.AggregatedProof, leafIndex: 0, totalLeaves: 1);
        Assert.AreEqual(0, siblings.Count);
        // VerifyLeaf with empty siblings means "the leaf must equal the root".
        Assert.IsTrue(MerklePathRoundProver.VerifyLeaf(
            aggregate.GlobalMessageRoot, aggregate.Constituents[0].L2ToL2MessageRoot, 0, siblings, totalLeaves: 1));
    }

    [TestMethod]
    public void ProveLeaf_OutOfRange_Throws()
    {
        var aggregator = new BinaryTreeAggregator(new MerklePathRoundProver());
        for (var i = 0; i < 4; i++) aggregator.Submit(MakeBatch((byte)(0x70 + i)));
        var aggregate = aggregator.Aggregate()!;

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => MerklePathRoundProver.ProveLeaf(aggregate.AggregatedProof, leafIndex: -1, totalLeaves: 4));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => MerklePathRoundProver.ProveLeaf(aggregate.AggregatedProof, leafIndex: 4, totalLeaves: 4));
    }
}
