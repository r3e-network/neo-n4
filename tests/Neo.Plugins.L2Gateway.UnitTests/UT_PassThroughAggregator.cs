using Neo.L2.State;

namespace Neo.Plugins.L2Gateway.UnitTests;

[TestClass]
public class UT_PassThroughAggregator
{
    private static UInt256 H(char c) => UInt256.Parse("0x" + new string(c, 64));

    private static L2BatchCommitment MkBatch(uint chainId, ulong batchNumber, byte[]? proof = null) => new()
    {
        ChainId = chainId,
        BatchNumber = batchNumber,
        FirstBlock = batchNumber * 100,
        LastBlock = batchNumber * 100 + 50,
        PreStateRoot = H('1'),
        PostStateRoot = H('2'),
        TxRoot = H('3'),
        ReceiptRoot = H('4'),
        WithdrawalRoot = H('5'),
        L2ToL1MessageRoot = H('6'),
        L2ToL2MessageRoot = UInt256.Parse("0x" + new string((char)('a' + (int)(chainId % 10)), 64)),
        DACommitment = H('8'),
        PublicInputHash = H('9'),
        ProofType = ProofType.Multisig,
        Proof = proof ?? new byte[] { (byte)chainId, (byte)batchNumber },
    };

    [TestMethod]
    public void Empty_AggregateReturnsNull()
    {
        var agg = new PassThroughAggregator();
        Assert.AreEqual(0, agg.PendingCount);
        Assert.IsNull(agg.Aggregate());
    }

    [TestMethod]
    public void Aggregate_ConsumesPending()
    {
        var agg = new PassThroughAggregator();
        agg.Submit(MkBatch(1001, 1));
        agg.Submit(MkBatch(1002, 1));
        Assert.AreEqual(2, agg.PendingCount);

        var aggregated = agg.Aggregate();
        Assert.IsNotNull(aggregated);
        Assert.AreEqual(2, aggregated!.Constituents.Count);
        Assert.AreEqual(0, agg.PendingCount); // queue drained
    }

    [TestMethod]
    public void Aggregate_GlobalRootIsMerkleOfL2L2Roots()
    {
        var agg = new PassThroughAggregator();
        var b1 = MkBatch(1001, 1);
        var b2 = MkBatch(1002, 1);
        agg.Submit(b1);
        agg.Submit(b2);

        var aggregated = agg.Aggregate()!;
        var expected = MerkleTree.ComputeRoot(new[] { b1.L2ToL2MessageRoot, b2.L2ToL2MessageRoot });
        Assert.AreEqual(expected, aggregated.GlobalMessageRoot);
    }

    [TestMethod]
    public void Aggregate_ProofIsLengthPrefixedConcatenation()
    {
        var agg = new PassThroughAggregator();
        var b1 = MkBatch(1001, 1, new byte[] { 0x01, 0x02 });
        var b2 = MkBatch(1002, 1, new byte[] { 0xFF });
        agg.Submit(b1);
        agg.Submit(b2);

        var aggregated = agg.Aggregate()!;
        // 4B count(=2) + (4B len=2 + 2 bytes) + (4B len=1 + 1 byte) = 4 + 6 + 5 = 15
        Assert.AreEqual(15, aggregated.AggregatedProof.Length);
        Assert.AreEqual(PassThroughAggregator.BackendId, aggregated.BackendId);
    }

    [TestMethod]
    public void Submit_NullThrows()
    {
        var agg = new PassThroughAggregator();
        Assert.ThrowsExactly<ArgumentNullException>(() => agg.Submit(null!));
    }
}
