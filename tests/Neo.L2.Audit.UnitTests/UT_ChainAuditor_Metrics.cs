namespace Neo.L2.Audit.UnitTests;

[TestClass]
public class UT_ChainAuditor_Metrics
{
    private static L2BatchCommitment Mk(ulong batchNumber, UInt256 pre, UInt256 post)
    {
        return new L2BatchCommitment
        {
            ChainId = 1001,
            BatchNumber = batchNumber,
            FirstBlock = batchNumber * 10,
            LastBlock = batchNumber * 10 + 9,
            PreStateRoot = pre,
            PostStateRoot = post,
            TxRoot = UInt256.Zero,
            ReceiptRoot = UInt256.Zero,
            WithdrawalRoot = UInt256.Zero,
            L2ToL1MessageRoot = UInt256.Zero,
            L2ToL2MessageRoot = UInt256.Zero,
            DACommitment = UInt256.Zero,
            PublicInputHash = UInt256.Zero,
            ProofType = ProofType.Multisig,
            Proof = new byte[] { 0x01 },
        };
    }

    private static UInt256 H(byte b)
    {
        var bytes = new byte[32];
        bytes[0] = b;
        return new UInt256(bytes);
    }

    [TestMethod]
    public async Task PassingAudit_IncrementsRunsOnly()
    {
        var metrics = new InMemoryMetrics();
        var auditor = new ChainAuditor(metrics).Register(new ContinuityCheck());

        var batches = new[]
        {
            Mk(1, H(0), H(1)),
            Mk(2, H(1), H(2)),
            Mk(3, H(2), H(3)),
        };
        var report = await auditor.AuditAsync(batches);

        Assert.IsTrue(report.Passed);
        Assert.AreEqual(1, metrics.GetCounter(MetricNames.AuditsRun));
        Assert.AreEqual(0, metrics.GetCounter(MetricNames.AuditFailures));
    }

    [TestMethod]
    public async Task FailingAudit_IncrementsRunsAndFailures()
    {
        var metrics = new InMemoryMetrics();
        var auditor = new ChainAuditor(metrics).Register(new ContinuityCheck());

        // Inconsistent state-root linking → ContinuityCheck fails twice (batches 2 and 3).
        var batches = new[]
        {
            Mk(1, H(0), H(1)),
            Mk(2, H(99), H(2)), // pre != prev post
            Mk(3, H(99), H(3)), // pre != prev post
        };
        var report = await auditor.AuditAsync(batches);

        Assert.IsFalse(report.Passed);
        Assert.AreEqual(1, metrics.GetCounter(MetricNames.AuditsRun));
        Assert.AreEqual(2, metrics.GetCounter(MetricNames.AuditFailures), "delta = number of failed findings");
    }

    [TestMethod]
    public async Task RepeatedAudits_AccumulateRunsCounter()
    {
        var metrics = new InMemoryMetrics();
        var auditor = new ChainAuditor(metrics).Register(new ContinuityCheck());
        var batches = new[] { Mk(1, H(0), H(1)) };

        await auditor.AuditAsync(batches);
        await auditor.AuditAsync(batches);
        await auditor.AuditAsync(batches);

        Assert.AreEqual(3, metrics.GetCounter(MetricNames.AuditsRun));
    }

    [TestMethod]
    public async Task DefaultsToNoOp_WhenNoMetrics()
    {
        var auditor = new ChainAuditor().Register(new ContinuityCheck());
        var report = await auditor.AuditAsync(new[] { Mk(1, H(0), H(1)) });
        Assert.IsTrue(report.Passed);
    }
}
