using Neo.Plugins.L2Gateway;

namespace Neo.Plugins.L2Gateway.UnitTests;

/// <summary>Canonical-tree tests for the dedicated SP1 recursive backend.</summary>
[TestClass]
public sealed class UT_Sp1RecursiveRoundProver
{
    [TestMethod]
    public void BackendId_IsDedicatedRecursiveValue()
    {
        Assert.AreEqual((byte)0xC2, Sp1RecursiveRoundProver.ConstBackendId);
        Assert.AreEqual(
            Sp1GatewayProofProver.RecursiveAggregationBackendId,
            new Sp1RecursiveRoundProver().BackendId);
    }

    [TestMethod]
    public void BinaryTreeAggregator_RootMatchesCanonicalRecursiveComputation()
    {
        var constituents = new[] { Batch(3, 0x33), Batch(1, 0x11), Batch(2, 0x22) };
        var aggregator = new BinaryTreeAggregator(new Sp1RecursiveRoundProver());
        foreach (var constituent in constituents) aggregator.Submit(constituent);

        var aggregate = aggregator.Aggregate();

        Assert.IsNotNull(aggregate);
        var ordered = constituents.OrderBy(static item => item.ChainId).ToArray();
        Assert.AreEqual(
            Sp1RecursiveRoundProver.ComputeGlobalMessageRoot(ordered),
            aggregate.GlobalMessageRoot);
        Assert.AreEqual(Sp1RecursiveRoundProver.ConstBackendId, aggregate.BackendId);
        Assert.AreEqual(32, aggregate.AggregatedProof.Length);
    }

    private static L2BatchCommitment Batch(uint chainId, byte root) => new()
    {
        ChainId = chainId,
        BatchNumber = 1,
        FirstBlock = 1,
        LastBlock = 1,
        PreStateRoot = H(0x01),
        PostStateRoot = H(0x02),
        TxRoot = H(0x03),
        ReceiptRoot = H(0x04),
        WithdrawalRoot = H(0x05),
        L2ToL1MessageRoot = H(0x06),
        L2ToL2MessageRoot = H(root),
        DACommitment = H(0x08),
        PublicInputHash = H(0x09),
        ProofType = ProofType.Zk,
        Proof = new byte[] { 0xAA },
    };

    private static UInt256 H(byte value) => new(Enumerable.Repeat(value, 32).ToArray());
}
