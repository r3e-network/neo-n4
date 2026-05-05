namespace Neo.L2.Audit.UnitTests;

/// <summary>
/// Tests for <see cref="BatchRangeCheck"/> — flags batches with intra-batch range
/// inversions or zero batch numbers.
/// </summary>
[TestClass]
public class UT_BatchRangeCheck
{
    private static L2BatchCommitment Mk(ulong batchNumber, ulong firstBlock, ulong lastBlock)
    {
        return new L2BatchCommitment
        {
            ChainId = 1001,
            BatchNumber = batchNumber,
            FirstBlock = firstBlock,
            LastBlock = lastBlock,
            PreStateRoot = UInt256.Zero,
            PostStateRoot = UInt256.Zero,
            TxRoot = UInt256.Zero,
            ReceiptRoot = UInt256.Zero,
            WithdrawalRoot = UInt256.Zero,
            L2ToL1MessageRoot = UInt256.Zero,
            L2ToL2MessageRoot = UInt256.Zero,
            DACommitment = UInt256.Zero,
            PublicInputHash = UInt256.Zero,
            ProofType = ProofType.Multisig,
            Proof = new byte[] { 0xAA },
        };
    }

    [TestMethod]
    public async Task AllValidRanges_PassesWithSummary()
    {
        var check = new BatchRangeCheck();
        var batches = new[]
        {
            Mk(1, 0, 9),
            Mk(2, 10, 19),
            Mk(3, 20, 20), // single-block batch (firstBlock == lastBlock) is valid
        };

        var findings = await check.RunAsync(batches);

        Assert.AreEqual(1, findings.Count);
        Assert.IsTrue(findings[0].Passed);
        StringAssert.Contains(findings[0].Detail, "3 batches");
    }

    [TestMethod]
    public async Task InvertedRange_Fails()
    {
        var check = new BatchRangeCheck();
        var batches = new[] { Mk(1, 100, 50) }; // firstBlock 100 > lastBlock 50

        var findings = await check.RunAsync(batches);

        Assert.AreEqual(1, findings.Count);
        Assert.IsFalse(findings[0].Passed);
        StringAssert.Contains(findings[0].Detail, "inverted");
        StringAssert.Contains(findings[0].Detail, "100");
        StringAssert.Contains(findings[0].Detail, "50");
    }

    [TestMethod]
    public async Task ZeroBatchNumber_Fails()
    {
        var check = new BatchRangeCheck();
        var batches = new[] { Mk(0, 0, 9) };

        var findings = await check.RunAsync(batches);

        Assert.AreEqual(1, findings.Count);
        Assert.IsFalse(findings[0].Passed);
        StringAssert.Contains(findings[0].Detail, "genesis");
    }

    [TestMethod]
    public async Task EmptyBatchList_PassesTrivially()
    {
        // Edge case: zero-batch audit shouldn't fail batch_range — there's nothing to
        // check. The summary just says "0 batches".
        var check = new BatchRangeCheck();
        var findings = await check.RunAsync(Array.Empty<L2BatchCommitment>());
        Assert.AreEqual(1, findings.Count);
        Assert.IsTrue(findings[0].Passed);
        StringAssert.Contains(findings[0].Detail, "0 batches");
    }

    [TestMethod]
    public async Task MultipleFailures_AllReported()
    {
        // Both failure modes in one batch list — both findings should appear.
        var check = new BatchRangeCheck();
        var batches = new[]
        {
            Mk(1, 0, 9),
            Mk(2, 50, 10), // inverted range
            Mk(0, 20, 29), // zero batch number
        };

        var findings = await check.RunAsync(batches);

        Assert.AreEqual(2, findings.Count, "two failed batches → two findings");
        Assert.IsTrue(findings.All(f => !f.Passed));
        Assert.IsTrue(findings.Any(f => f.Detail.Contains("inverted")));
        Assert.IsTrue(findings.Any(f => f.Detail.Contains("genesis")));
    }

    [TestMethod]
    public async Task RunAsync_RejectsNullBatches()
    {
        var check = new BatchRangeCheck();
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await check.RunAsync(null!));
    }

    [TestMethod]
    public void NameIsStable()
    {
        // Pin the canonical name — used by ChainAuditor to attribute findings to a check.
        Assert.AreEqual("batch_range", new BatchRangeCheck().Name);
    }
}
