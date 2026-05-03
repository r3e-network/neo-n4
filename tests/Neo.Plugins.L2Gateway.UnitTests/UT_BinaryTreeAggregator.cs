using Neo.L2.State;

namespace Neo.Plugins.L2Gateway.UnitTests;

[TestClass]
public class UT_BinaryTreeAggregator
{
    private static UInt256 H(char c)
    {
        var bytes = new byte[32];
        for (var i = 0; i < 32; i++) bytes[i] = (byte)c;
        return new UInt256(bytes);
    }

    private static L2BatchCommitment MkBatch(uint chainId, byte[] proof, UInt256 l2L2Root)
    {
        var z = UInt256.Zero;
        return new L2BatchCommitment
        {
            ChainId = chainId,
            BatchNumber = 1,
            FirstBlock = 100,
            LastBlock = 200,
            PreStateRoot = z, PostStateRoot = z, TxRoot = z, ReceiptRoot = z,
            WithdrawalRoot = z,
            L2ToL1MessageRoot = z,
            L2ToL2MessageRoot = l2L2Root,
            DACommitment = z, PublicInputHash = z,
            ProofType = ProofType.Multisig,
            Proof = proof,
        };
    }

    [TestMethod]
    public void RoundsFor_HandlesEdges()
    {
        Assert.AreEqual(0, BinaryTreeAggregator.RoundsFor(0));
        Assert.AreEqual(0, BinaryTreeAggregator.RoundsFor(1));
        Assert.AreEqual(1, BinaryTreeAggregator.RoundsFor(2));
        Assert.AreEqual(2, BinaryTreeAggregator.RoundsFor(3));
        Assert.AreEqual(2, BinaryTreeAggregator.RoundsFor(4));
        Assert.AreEqual(3, BinaryTreeAggregator.RoundsFor(8));
        Assert.AreEqual(4, BinaryTreeAggregator.RoundsFor(9));
        Assert.AreEqual(10, BinaryTreeAggregator.RoundsFor(1000));
    }

    [TestMethod]
    public void Empty_AggregateReturnsNull()
    {
        var agg = new BinaryTreeAggregator();
        Assert.IsNull(agg.Aggregate());
    }

    [TestMethod]
    public void SingleBatch_RootEqualsBatchRoot()
    {
        var agg = new BinaryTreeAggregator();
        var batch = MkBatch(1001, new byte[] { 0xAB }, H('1'));
        agg.Submit(batch);

        var aggregated = agg.Aggregate();
        Assert.IsNotNull(aggregated);
        Assert.AreEqual(1, aggregated!.Constituents.Count);
        Assert.AreEqual(H('1'), aggregated.GlobalMessageRoot);
        // Single-leaf passes through proof unchanged.
        CollectionAssert.AreEqual(new byte[] { 0xAB }, aggregated.AggregatedProof.ToArray());
    }

    [TestMethod]
    public void TwoBatches_GlobalRootIsHashOfBoth()
    {
        var agg = new BinaryTreeAggregator();
        var b1 = MkBatch(1001, new byte[] { 1 }, H('a'));
        var b2 = MkBatch(1002, new byte[] { 2, 3 }, H('b'));
        agg.Submit(b1);
        agg.Submit(b2);

        var aggregated = agg.Aggregate()!;
        Assert.AreEqual(2, aggregated.Constituents.Count);

        var expected = MerkleTree.ComputeRoot(new[] { H('a'), H('b') });
        Assert.AreEqual(expected, aggregated.GlobalMessageRoot);

        // Proof: 4B(=1) + 0x01 + 4B(=2) + 0x02 0x03 = 11 bytes
        Assert.AreEqual(11, aggregated.AggregatedProof.Length);
    }

    [TestMethod]
    public void OddCardinality_PromotesTrailingLeafUnchanged()
    {
        var agg = new BinaryTreeAggregator();
        // 3 batches: tree depth = 2.
        // Round 0: [b1, b2, b3] → [combine(b1, b2), b3]
        // Round 1: [combine(b1, b2), b3] → [combine(combine(b1,b2), b3)]
        agg.Submit(MkBatch(1001, new byte[] { 1 }, H('a')));
        agg.Submit(MkBatch(1002, new byte[] { 2 }, H('b')));
        agg.Submit(MkBatch(1003, new byte[] { 3 }, H('c')));

        var aggregated = agg.Aggregate()!;
        Assert.AreEqual(3, aggregated.Constituents.Count);

        // Expected root: Hash256(Hash256(a||b) || c)
        var ab = MerkleTree.ComputeRoot(new[] { H('a'), H('b') });
        var expected = MerkleTree.ComputeRoot(new[] { ab, H('c') });
        Assert.AreEqual(expected, aggregated.GlobalMessageRoot);
    }

    [TestMethod]
    public void EightBatches_DepthMatchesLog2()
    {
        var agg = new BinaryTreeAggregator();
        for (var i = 0; i < 8; i++)
            agg.Submit(MkBatch((uint)(1001 + i), new byte[] { (byte)i }, H((char)('a' + i))));

        var aggregated = agg.Aggregate()!;
        Assert.AreEqual(8, aggregated.Constituents.Count);
        Assert.AreEqual(3, BinaryTreeAggregator.RoundsFor(8));

        // Verify the root matches the equivalent flat MerkleTree.ComputeRoot.
        var leafRoots = aggregated.Constituents.Select(c => c.L2ToL2MessageRoot).ToArray();
        var expected = MerkleTree.ComputeRoot(leafRoots);
        Assert.AreEqual(expected, aggregated.GlobalMessageRoot);
    }

    [TestMethod]
    public void CustomRoundProver_IsInvokedAtEveryLayer()
    {
        var counter = new CountingRoundProver();
        var agg = new BinaryTreeAggregator(counter);

        // 4 leaves → 2 rounds → 3 Combine calls (round 0: 2 pairs; round 1: 1 pair).
        for (var i = 0; i < 4; i++)
            agg.Submit(MkBatch((uint)(1001 + i), new byte[] { (byte)i }, H((char)('a' + i))));

        agg.Aggregate();
        Assert.AreEqual(3, counter.Calls);
    }

    [TestMethod]
    public void Aggregate_ConsumesPending()
    {
        var agg = new BinaryTreeAggregator();
        agg.Submit(MkBatch(1001, new byte[] { 1 }, H('a')));
        agg.Submit(MkBatch(1002, new byte[] { 2 }, H('b')));
        Assert.AreEqual(2, agg.PendingCount);

        agg.Aggregate();
        Assert.AreEqual(0, agg.PendingCount);
    }

    private sealed class CountingRoundProver : IRoundProver
    {
        public int Calls;
        public byte BackendId => 0xAA;

        public RoundResult Combine(RoundResult left, RoundResult? right)
        {
            Calls++;
            return new PassThroughRoundProver().Combine(left, right);
        }
    }

    [TestMethod]
    public void PassThroughRoundProver_Combine_RejectsNullMessageRootContribution()
    {
        // Regression for iter 180: MessageRootContribution is UInt256 (reference type),
        // `required` doesn't prevent null. Without the guard, GetSpan() NREs deep
        // inside the round combine. Same iter-156 hashing-primitive defense pattern.
        var prover = new PassThroughRoundProver();
        var leftBad = new RoundResult { MessageRootContribution = null!, ProofBytes = new byte[] { 0x01 } };
        var goodRight = new RoundResult { MessageRootContribution = UInt256.Zero, ProofBytes = new byte[] { 0x02 } };
        Assert.ThrowsExactly<ArgumentNullException>(() => prover.Combine(leftBad, goodRight));

        var goodLeft = new RoundResult { MessageRootContribution = UInt256.Zero, ProofBytes = new byte[] { 0x01 } };
        var rightBad = new RoundResult { MessageRootContribution = null!, ProofBytes = new byte[] { 0x02 } };
        Assert.ThrowsExactly<ArgumentNullException>(() => prover.Combine(goodLeft, rightBad));
    }
}
