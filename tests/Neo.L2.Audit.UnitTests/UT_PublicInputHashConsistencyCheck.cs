using Neo.L2.State;

namespace Neo.L2.Audit.UnitTests;

[TestClass]
public class UT_PublicInputHashConsistencyCheck
{
    private static UInt256 H(byte b)
    {
        var bytes = new byte[32];
        bytes[0] = b;
        return new UInt256(bytes);
    }

    private static L2BatchCommitment Mk(ulong batchNumber, UInt256 publicInputHash)
    {
        return new L2BatchCommitment
        {
            ChainId = 1001,
            BatchNumber = batchNumber,
            FirstBlock = batchNumber * 10,
            LastBlock = batchNumber * 10 + 9,
            PreStateRoot = H(0),
            PostStateRoot = H(1),
            TxRoot = H(2),
            ReceiptRoot = H(3),
            WithdrawalRoot = H(4),
            L2ToL1MessageRoot = H(5),
            L2ToL2MessageRoot = H(6),
            DACommitment = H(7),
            PublicInputHash = publicInputHash,
            ProofType = ProofType.Multisig,
            Proof = new byte[] { 0x01 },
        };
    }

    private static UInt256 ExpectedHashFor(L2BatchCommitment c)
    {
        return StateRootCalculator.HashPublicInputs(new PublicInputs
        {
            ChainId = c.ChainId,
            BatchNumber = c.BatchNumber,
            PreStateRoot = c.PreStateRoot,
            PostStateRoot = c.PostStateRoot,
            TxRoot = c.TxRoot,
            ReceiptRoot = c.ReceiptRoot,
            WithdrawalRoot = c.WithdrawalRoot,
            L2ToL1MessageRoot = c.L2ToL1MessageRoot,
            L2ToL2MessageRoot = c.L2ToL2MessageRoot,
            L1MessageHash = UInt256.Zero,
            DACommitment = c.DACommitment,
            BlockContextHash = UInt256.Zero,
        });
    }

    [TestMethod]
    public async Task ConsistentHash_PassesWithSummary()
    {
        var batchSeed = Mk(1, UInt256.Zero);
        var correctHash = ExpectedHashFor(batchSeed);
        var batches = new[] { Mk(1, correctHash), Mk(2, correctHash) };
        // Each batch's hash matches its own fields (batch 2 has same fields except batchNumber,
        // so the hash differs — let's recompute per-batch).
        batches[1] = Mk(2, ExpectedHashFor(Mk(2, UInt256.Zero)));
        batches[0] = Mk(1, ExpectedHashFor(Mk(1, UInt256.Zero)));

        var check = new PublicInputHashConsistencyCheck();
        var findings = await check.RunAsync(batches);

        Assert.AreEqual(1, findings.Count);
        Assert.IsTrue(findings[0].Passed);
        StringAssert.Contains(findings[0].Detail, "2 batches");
    }

    [TestMethod]
    public async Task TamperedHash_FailsWithBatchNumber()
    {
        var bad = Mk(5, H(0xFF));  // wrong PublicInputHash
        var check = new PublicInputHashConsistencyCheck();
        var findings = await check.RunAsync(new[] { bad });

        Assert.AreEqual(1, findings.Count);
        Assert.IsFalse(findings[0].Passed);
        Assert.AreEqual(5u, findings[0].BatchNumber);
        StringAssert.Contains(findings[0].Detail, "PublicInputHash mismatch");
    }

    [TestMethod]
    public async Task EmptyBatchList_PassesWithSummary()
    {
        var check = new PublicInputHashConsistencyCheck();
        var findings = await check.RunAsync(Array.Empty<L2BatchCommitment>());

        Assert.AreEqual(1, findings.Count);
        Assert.IsTrue(findings[0].Passed);
    }

    [TestMethod]
    public async Task RunAsync_RejectsNullBatches()
    {
        // Pin PublicInputHashConsistencyCheck.cs:29. Match sibling-check convention.
        var check = new PublicInputHashConsistencyCheck();
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await check.RunAsync(null!));
    }
}
