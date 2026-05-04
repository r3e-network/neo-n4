namespace Neo.L2.Audit.UnitTests;

/// <summary>
/// Tests for <see cref="NoZeroProofCheck"/> — flags batches that were soft-sealed but
/// never had a real proof attached.
/// </summary>
[TestClass]
public class UT_NoZeroProofCheck
{
    private static L2BatchCommitment Mk(ulong batchNumber, ProofType proofType, byte[] proof)
    {
        return new L2BatchCommitment
        {
            ChainId = 1001,
            BatchNumber = batchNumber,
            FirstBlock = batchNumber * 10,
            LastBlock = batchNumber * 10 + 9,
            PreStateRoot = UInt256.Zero,
            PostStateRoot = UInt256.Zero,
            TxRoot = UInt256.Zero,
            ReceiptRoot = UInt256.Zero,
            WithdrawalRoot = UInt256.Zero,
            L2ToL1MessageRoot = UInt256.Zero,
            L2ToL2MessageRoot = UInt256.Zero,
            DACommitment = UInt256.Zero,
            PublicInputHash = UInt256.Zero,
            ProofType = proofType,
            Proof = proof,
        };
    }

    [TestMethod]
    public async Task AllBatchesProved_PassesWithSummaryFinding()
    {
        var check = new NoZeroProofCheck();
        var batches = new[]
        {
            Mk(1, ProofType.Multisig, new byte[] { 0xAA }),
            Mk(2, ProofType.Multisig, new byte[] { 0xBB }),
            Mk(3, ProofType.Optimistic, new byte[] { 0xCC }),
        };

        var findings = await check.RunAsync(batches);

        Assert.AreEqual(1, findings.Count);
        Assert.IsTrue(findings[0].Passed);
        StringAssert.Contains(findings[0].Detail, "3 batches");
    }

    [TestMethod]
    public async Task BatchWithProofTypeNone_FailsCheck()
    {
        var check = new NoZeroProofCheck();
        var batches = new[]
        {
            Mk(1, ProofType.Multisig, new byte[] { 0xAA }),
            Mk(2, ProofType.None, new byte[] { 0xBB }),
            Mk(3, ProofType.Multisig, new byte[] { 0xCC }),
        };

        var findings = await check.RunAsync(batches);

        Assert.AreEqual(1, findings.Count);
        Assert.IsFalse(findings[0].Passed);
        Assert.AreEqual(2u, findings[0].BatchNumber);
        StringAssert.Contains(findings[0].Detail, "ProofType=None");
    }

    [TestMethod]
    public async Task BatchWithEmptyProofBytes_FailsCheck()
    {
        var check = new NoZeroProofCheck();
        var batches = new[]
        {
            Mk(1, ProofType.Multisig, Array.Empty<byte>()),
        };

        var findings = await check.RunAsync(batches);

        Assert.AreEqual(1, findings.Count);
        Assert.IsFalse(findings[0].Passed);
        StringAssert.Contains(findings[0].Detail, "Proof bytes are empty");
    }

    [TestMethod]
    public async Task MultipleFailures_AllReported()
    {
        var check = new NoZeroProofCheck();
        var batches = new[]
        {
            Mk(1, ProofType.None, new byte[] { 0x01 }),       // ProofType=None
            Mk(2, ProofType.Multisig, Array.Empty<byte>()),   // empty bytes
            Mk(3, ProofType.Multisig, new byte[] { 0x03 }),   // ok
        };

        var findings = await check.RunAsync(batches);

        Assert.AreEqual(2, findings.Count);
        Assert.IsFalse(findings[0].Passed);
        Assert.IsFalse(findings[1].Passed);
        Assert.AreEqual(1u, findings[0].BatchNumber);
        Assert.AreEqual(2u, findings[1].BatchNumber);
    }

    [TestMethod]
    public async Task EmptyBatchList_PassesWithSummary()
    {
        var check = new NoZeroProofCheck();
        var findings = await check.RunAsync(Array.Empty<L2BatchCommitment>());

        Assert.AreEqual(1, findings.Count);
        Assert.IsTrue(findings[0].Passed);
    }

    [TestMethod]
    public async Task RunAsync_RejectsNullBatches()
    {
        // Pin NoZeroProofCheck.cs:27. Without it a null batches array would NRE inside
        // the foreach, masking the real input-shape problem. Same iter-148 boundary
        // pattern the other checks use.
        var check = new NoZeroProofCheck();
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await check.RunAsync(null!));
    }
}
