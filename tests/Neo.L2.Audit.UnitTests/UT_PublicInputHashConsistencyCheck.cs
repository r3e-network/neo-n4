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

    [TestMethod]
    public void NameIsStable()
    {
        // Pin the canonical name — ChainAuditor uses it to attribute findings, and
        // operator dashboards / log filters key on it.
        Assert.AreEqual("public_input_hash", new PublicInputHashConsistencyCheck().Name);
    }

    [TestMethod]
    public async Task Resolver_OverridesDefaultReconstruction()
    {
        // The default reconstruction zero-fills L1MessageHash and BlockContextHash
        // (Phase 0-3 settlement convention). Future phases populate them; the resolver
        // is the seam. Build a batch whose PublicInputHash was computed against
        // non-zero L1MessageHash + BlockContextHash, then prove that the default check
        // fails on it but the resolver-aware check passes.
        var nonZeroL1 = H(0x77);
        var nonZeroCtx = H(0x88);
        var hashedInputs = new PublicInputs
        {
            ChainId = 1001,
            BatchNumber = 1,
            PreStateRoot = H(0),
            PostStateRoot = H(1),
            TxRoot = H(2),
            ReceiptRoot = H(3),
            WithdrawalRoot = H(4),
            L2ToL1MessageRoot = H(5),
            L2ToL2MessageRoot = H(6),
            L1MessageHash = nonZeroL1,
            DACommitment = H(7),
            BlockContextHash = nonZeroCtx,
        };
        var batch = Mk(1, StateRootCalculator.HashPublicInputs(hashedInputs));

        // Default reconstruction zero-fills the two extra fields → hash mismatches.
        var defaultCheck = new PublicInputHashConsistencyCheck();
        var defaultFindings = await defaultCheck.RunAsync(new[] { batch });
        Assert.IsFalse(defaultFindings[0].Passed, "default check fails on non-zero L1+context");

        // Resolver-aware check returns the matching public inputs → hash matches.
        var resolverCheck = new PublicInputHashConsistencyCheck(_ => hashedInputs);
        var resolverFindings = await resolverCheck.RunAsync(new[] { batch });
        Assert.IsTrue(resolverFindings[0].Passed, "resolver-aware check passes on same fields");
    }

    [TestMethod]
    public async Task Resolver_NullReturn_Throws()
    {
        // Defense-in-depth: a buggy resolver returning null must fail loudly with the
        // batch number named, not silently NRE deep inside HashPublicInputs.
        var batch = Mk(7, UInt256.Zero);
        var check = new PublicInputHashConsistencyCheck(_ => null!);
        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await check.RunAsync(new[] { batch }));
        StringAssert.Contains(ex.Message, "batch 7");
    }
}
