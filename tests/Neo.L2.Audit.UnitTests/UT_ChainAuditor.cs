using Neo.L2.Proving;
using Neo.L2.Proving.RiscVZk;

namespace Neo.L2.Audit.UnitTests;

[TestClass]
public class UT_ChainAuditor
{
    private static UInt256 H(byte b)
    {
        var bytes = new byte[32];
        bytes[0] = b;
        return new UInt256(bytes);
    }

    private static L2BatchCommitment Mk(uint chainId, ulong batchNumber, UInt256 pre, UInt256 post, ulong firstBlock, ulong lastBlock)
    {
        return new L2BatchCommitment
        {
            ChainId = chainId,
            BatchNumber = batchNumber,
            FirstBlock = firstBlock,
            LastBlock = lastBlock,
            PreStateRoot = pre,
            PostStateRoot = post,
            TxRoot = UInt256.Zero,
            ReceiptRoot = UInt256.Zero,
            WithdrawalRoot = UInt256.Zero,
            L2ToL1MessageRoot = UInt256.Zero,
            L2ToL2MessageRoot = UInt256.Zero,
            DACommitment = UInt256.Zero,
            PublicInputHash = UInt256.Zero,
            ProofType = ProofType.None,
            Proof = ReadOnlyMemory<byte>.Empty,
        };
    }

    [TestMethod]
    public async Task EmptyBatches_FailingFinding()
    {
        var auditor = new ChainAuditor();
        var report = await auditor.AuditAsync(Array.Empty<L2BatchCommitment>());
        Assert.IsFalse(report.Passed);
        Assert.AreEqual(1, report.Findings.Count);
        Assert.AreEqual("input", report.Findings[0].Check);
    }

    [TestMethod]
    public async Task ContinuityCheck_HappyPath()
    {
        var batches = new[]
        {
            Mk(1001, 1, H(0), H(1), 100, 200),
            Mk(1001, 2, H(1), H(2), 201, 300),
            Mk(1001, 3, H(2), H(3), 301, 400),
        };
        var auditor = new ChainAuditor().Register(new ContinuityCheck());
        var report = await auditor.AuditAsync(batches);
        Assert.IsTrue(report.Passed, report.Summarize());
    }

    [TestMethod]
    public async Task ContinuityCheck_DetectsBatchNumberGap()
    {
        var batches = new[]
        {
            Mk(1001, 1, H(0), H(1), 100, 200),
            Mk(1001, 3, H(1), H(2), 201, 300), // skipped 2
        };
        var auditor = new ChainAuditor().Register(new ContinuityCheck());
        var report = await auditor.AuditAsync(batches);
        Assert.IsFalse(report.Passed);
        StringAssert.Contains(report.Findings[0].Detail, "does not follow");
    }

    [TestMethod]
    public async Task ContinuityCheck_DetectsStateRootMismatch()
    {
        var batches = new[]
        {
            Mk(1001, 1, H(0), H(1), 100, 200),
            Mk(1001, 2, H(99), H(2), 201, 300), // pre 99 != prior post 1
        };
        var auditor = new ChainAuditor().Register(new ContinuityCheck());
        var report = await auditor.AuditAsync(batches);
        Assert.IsFalse(report.Passed);
        StringAssert.Contains(report.Findings[0].Detail, "preStateRoot");
    }

    [TestMethod]
    public async Task ContinuityCheck_DetectsBlockOverlap()
    {
        var batches = new[]
        {
            Mk(1001, 1, H(0), H(1), 100, 200),
            Mk(1001, 2, H(1), H(2), 150, 300), // overlaps with prior lastBlock
        };
        var auditor = new ChainAuditor().Register(new ContinuityCheck());
        var report = await auditor.AuditAsync(batches);
        Assert.IsFalse(report.Passed);
        StringAssert.Contains(report.Findings[0].Detail, "firstBlock");
    }

    [TestMethod]
    public async Task ProofValidityCheck_HappyPath()
    {
        var vkId = UInt256.Parse("0x" + new string('f', 64));
        var prover = new MockRiscVProver(vkId);
        var verifier = new MockRiscVVerifier(vkId);

        var inputs = new PublicInputs
        {
            ChainId = 1001, BatchNumber = 1,
            PreStateRoot = UInt256.Zero, PostStateRoot = H(1),
            TxRoot = UInt256.Zero, ReceiptRoot = UInt256.Zero, WithdrawalRoot = UInt256.Zero,
            L2ToL1MessageRoot = UInt256.Zero, L2ToL2MessageRoot = UInt256.Zero,
            L1MessageHash = UInt256.Zero, DACommitment = UInt256.Zero, BlockContextHash = UInt256.Zero,
        };
        var proof = await prover.ProveAsync(new ProofRequest { PublicInputs = inputs, Witness = ReadOnlyMemory<byte>.Empty, Kind = ProofType.Zk });

        var batch = Mk(1001, 1, UInt256.Zero, H(1), 100, 200) with { ProofType = ProofType.Zk, Proof = proof.Proof, PublicInputHash = proof.PublicInputHash };

        var registry = new VerifierRegistry();
        registry.Register(verifier);

        var auditor = new ChainAuditor().Register(new ProofValidityCheck(registry, _ => inputs));
        var report = await auditor.AuditAsync(new[] { batch });
        Assert.IsTrue(report.Passed, report.Summarize());
    }

    [TestMethod]
    public async Task ProofValidityCheck_DetectsBadProof()
    {
        var vkId = UInt256.Parse("0x" + new string('f', 64));
        var verifier = new MockRiscVVerifier(vkId);

        var inputs = new PublicInputs
        {
            ChainId = 1001, BatchNumber = 1,
            PreStateRoot = UInt256.Zero, PostStateRoot = H(1),
            TxRoot = UInt256.Zero, ReceiptRoot = UInt256.Zero, WithdrawalRoot = UInt256.Zero,
            L2ToL1MessageRoot = UInt256.Zero, L2ToL2MessageRoot = UInt256.Zero,
            L1MessageHash = UInt256.Zero, DACommitment = UInt256.Zero, BlockContextHash = UInt256.Zero,
        };
        // Garbage proof bytes.
        var batch = Mk(1001, 1, UInt256.Zero, H(1), 100, 200) with { ProofType = ProofType.Zk, Proof = new byte[] { 0x01, 0x02 } };

        var registry = new VerifierRegistry();
        registry.Register(verifier);

        var auditor = new ChainAuditor().Register(new ProofValidityCheck(registry, _ => inputs));
        var report = await auditor.AuditAsync(new[] { batch });
        Assert.IsFalse(report.Passed);
    }

    [TestMethod]
    public async Task ProofValidityCheck_NullBatches_ThrowsArgumentNullException()
    {
        // Match the null-guard convention of sibling checks. Without the guard the foreach
        // would surface a NullReferenceException with no link back to the bad input.
        var registry = new VerifierRegistry();
        var check = new ProofValidityCheck(registry, _ => new PublicInputs
        {
            ChainId = 1, BatchNumber = 1,
            PreStateRoot = UInt256.Zero, PostStateRoot = UInt256.Zero,
            TxRoot = UInt256.Zero, ReceiptRoot = UInt256.Zero,
            WithdrawalRoot = UInt256.Zero, L2ToL1MessageRoot = UInt256.Zero,
            L2ToL2MessageRoot = UInt256.Zero, L1MessageHash = UInt256.Zero,
            DACommitment = UInt256.Zero, BlockContextHash = UInt256.Zero,
        });
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(async () =>
            await check.RunAsync(null!));
    }

    [TestMethod]
    public void AuditReport_EmptyFindings_DoesNotVacuouslyPass()
    {
        // Regression: AuditReport.Passed was Findings.All(f => f.Passed), which returns
        // true vacuously on an empty list. A caller who constructed the report directly
        // (bypassing ChainAuditor's guards) would see "passed" with no checks run.
        var report = new AuditReport
        {
            ChainId = 1001,
            FirstBatch = 0,
            LastBatch = 0,
            Findings = Array.Empty<AuditFinding>(),
        };
        Assert.IsFalse(report.Passed, "empty findings must not count as a passed audit");
    }

    [TestMethod]
    public async Task Auditor_RejectsDuplicateBatchNumbers()
    {
        // Regression: previously the sort check was `<` which silently allowed duplicates.
        // Two batches at the same height violates the precondition (a chain can't carry
        // two distinct commitments at the same number). Now strict-ascending: duplicate
        // throws with a clear message instead of being buried as a continuity violation.
        var batches = new[]
        {
            Mk(1001, 5, H(0), H(1), 100, 200),
            Mk(1001, 5, H(1), H(2), 201, 300), // same BatchNumber!
        };
        var auditor = new ChainAuditor().Register(new ContinuityCheck());
        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(async () => await auditor.AuditAsync(batches));
        StringAssert.Contains(ex.Message, "strictly ascending");
    }

    [TestMethod]
    public async Task Auditor_RejectsMixedChainIds()
    {
        var batches = new[]
        {
            Mk(1001, 1, H(0), H(1), 100, 200),
            Mk(1002, 2, H(1), H(2), 201, 300),
        };
        var auditor = new ChainAuditor().Register(new ContinuityCheck());
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () => await auditor.AuditAsync(batches));
    }

    [TestMethod]
    public async Task AuditReport_SummarizeIsHumanReadable()
    {
        var batches = new[]
        {
            Mk(1001, 1, H(0), H(1), 100, 200),
            Mk(1001, 2, H(1), H(2), 201, 300),
        };
        var auditor = new ChainAuditor().Register(new ContinuityCheck());
        var report = await auditor.AuditAsync(batches);
        var summary = report.Summarize();
        StringAssert.Contains(summary, "PASSED");
        StringAssert.Contains(summary, "chain 1001");
        StringAssert.Contains(summary, "continuity");
    }
}
