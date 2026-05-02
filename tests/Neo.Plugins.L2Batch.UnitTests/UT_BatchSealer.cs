namespace Neo.Plugins.L2Batch.UnitTests;

/// <summary>
/// Tests for <see cref="BatchSealer"/> — the pure batch-accumulation state machine that
/// lives behind <see cref="L2BatchPlugin"/>. Drives all three seal triggers (block-count,
/// tx-count, age) and verifies the metric contract.
/// </summary>
[TestClass]
public class UT_BatchSealer
{
    [TestMethod]
    public void Sealer_AccumulatesBlocks_UntilBlockCountTrigger()
    {
        var settings = new L2BatchSettings
        {
            ChainId = 1001,
            MaxBlocksPerBatch = 3,
            MaxTransactionsPerBatch = 100_000,
            MaxBatchAgeMillis = int.MaxValue,
            Enabled = true,
        };
        var metrics = new InMemoryMetrics();
        long now = 0;
        var sealer = new BatchSealer(settings, metrics, () => now);

        Assert.IsNull(sealer.OnBlockCommit(10, 1000, 11, NoTxs()), "1st block: no seal");
        Assert.IsNull(sealer.OnBlockCommit(11, 1100, 11, NoTxs()), "2nd block: no seal");
        var sealed_ = sealer.OnBlockCommit(12, 1200, 11, NoTxs());
        Assert.IsNotNull(sealed_, "3rd block hits MaxBlocksPerBatch — seals");
        Assert.AreEqual(1u, sealed_!.BatchNumber);
        Assert.AreEqual(10u, (uint)sealed_.FirstBlock);
        Assert.AreEqual(12u, (uint)sealed_.LastBlock);
        Assert.AreEqual(1L, metrics.GetCounter(MetricNames.BatchesSealed));
        Assert.AreEqual(1, metrics.GetHistogram(MetricNames.BatchSealLatencyMs).Count);
    }

    [TestMethod]
    public void Sealer_SealsOnTxCountTrigger()
    {
        var settings = new L2BatchSettings
        {
            ChainId = 1001,
            MaxBlocksPerBatch = 1_000,
            MaxTransactionsPerBatch = 5,
            MaxBatchAgeMillis = int.MaxValue,
            Enabled = true,
        };
        var metrics = new InMemoryMetrics();
        var sealer = new BatchSealer(settings, metrics, () => 0L);

        Assert.IsNull(sealer.OnBlockCommit(1, 1000, 11, MakeTxs(3)));
        var sealed_ = sealer.OnBlockCommit(2, 1100, 11, MakeTxs(3));
        Assert.IsNotNull(sealed_, "6 txs > MaxTransactionsPerBatch=5 — seals");
        Assert.AreEqual(6, (int)metrics.GetGauge(MetricNames.BatchTxCount));
    }

    [TestMethod]
    public void Sealer_SealsOnAgeTrigger()
    {
        var settings = new L2BatchSettings
        {
            ChainId = 1001,
            MaxBlocksPerBatch = 1_000,
            MaxTransactionsPerBatch = 100_000,
            MaxBatchAgeMillis = 5_000,
            Enabled = true,
        };
        var metrics = new InMemoryMetrics();
        long now = 0;
        var sealer = new BatchSealer(settings, metrics, () => now);

        Assert.IsNull(sealer.OnBlockCommit(1, 1000, 11, NoTxs()), "no seal at t=0");
        now = 4_999;
        Assert.IsNull(sealer.OnBlockCommit(2, 2000, 11, NoTxs()), "still under age threshold");
        now = 5_001;
        var sealed_ = sealer.OnBlockCommit(3, 3000, 11, NoTxs());
        Assert.IsNotNull(sealed_, "now >= MaxBatchAgeMillis — seals");
    }

    [TestMethod]
    public void Sealer_BatchNumberMonotonic_AcrossSeals()
    {
        var settings = new L2BatchSettings
        {
            ChainId = 1001,
            MaxBlocksPerBatch = 1,
            MaxTransactionsPerBatch = 100,
            MaxBatchAgeMillis = int.MaxValue,
            Enabled = true,
        };
        var sealer = new BatchSealer(settings, new InMemoryMetrics(), () => 0L);

        var b1 = sealer.OnBlockCommit(10, 1000, 11, NoTxs())!;
        var b2 = sealer.OnBlockCommit(11, 2000, 11, NoTxs())!;
        var b3 = sealer.OnBlockCommit(12, 3000, 11, NoTxs())!;

        Assert.AreEqual(1u, b1.BatchNumber);
        Assert.AreEqual(2u, b2.BatchNumber);
        Assert.AreEqual(3u, b3.BatchNumber);
        Assert.AreEqual(10u, (uint)b1.FirstBlock);
        Assert.AreEqual(11u, (uint)b2.FirstBlock);
        Assert.AreEqual(12u, (uint)b3.FirstBlock);
    }

    [TestMethod]
    public void Sealer_StartsFreshBatchAfterSeal()
    {
        var settings = new L2BatchSettings
        {
            ChainId = 1001,
            MaxBlocksPerBatch = 1,
            MaxTransactionsPerBatch = 100,
            MaxBatchAgeMillis = int.MaxValue,
            Enabled = true,
        };
        var sealer = new BatchSealer(settings, new InMemoryMetrics(), () => 0L);

        Assert.IsFalse(sealer.HasOpenBatch);
        sealer.OnBlockCommit(1, 1000, 11, NoTxs()); // seals immediately (MaxBlocksPerBatch=1)
        Assert.IsFalse(sealer.HasOpenBatch, "post-seal: builder reset");
        Assert.AreEqual(0, sealer.InProgressTxCount);
    }

    [TestMethod]
    public void Sealer_NoOpMetricsByDefault_DoesNotThrow()
    {
        var settings = new L2BatchSettings
        {
            ChainId = 1001,
            MaxBlocksPerBatch = 1,
            MaxTransactionsPerBatch = 100,
            MaxBatchAgeMillis = int.MaxValue,
            Enabled = true,
        };
        var sealer = new BatchSealer(settings, NoOpMetrics.Instance, () => 0L);
        var sealed_ = sealer.OnBlockCommit(1, 1000, 11, NoTxs());
        Assert.IsNotNull(sealed_);
    }

    [TestMethod]
    public void Sealer_TxCountGauge_TracksMostRecentSeal()
    {
        var settings = new L2BatchSettings
        {
            ChainId = 1001,
            MaxBlocksPerBatch = 1,
            MaxTransactionsPerBatch = 100,
            MaxBatchAgeMillis = int.MaxValue,
            Enabled = true,
        };
        var metrics = new InMemoryMetrics();
        var sealer = new BatchSealer(settings, metrics, () => 0L);

        sealer.OnBlockCommit(1, 1000, 11, MakeTxs(2));
        Assert.AreEqual(2, (int)metrics.GetGauge(MetricNames.BatchTxCount));
        sealer.OnBlockCommit(2, 2000, 11, MakeTxs(7));
        Assert.AreEqual(7, (int)metrics.GetGauge(MetricNames.BatchTxCount), "gauge replaces, not accumulates");
        Assert.AreEqual(2L, metrics.GetCounter(MetricNames.BatchesSealed), "counter increments");
    }

    [TestMethod]
    public void Sealer_WithMetrics_PreservesBatchNumberingAndBuilderState()
    {
        // Regression: previously L2BatchPlugin.WithMetrics nulled _sealer, dropping
        // _nextBatchNumber, _lastPostStateRoot, and any in-progress builder. Replaying
        // batch 1 after the rewire would collide with whatever was already submitted.
        // Now WithMetrics swaps the sink in-place on the existing sealer.
        var settings = new L2BatchSettings
        {
            ChainId = 1001,
            MaxBlocksPerBatch = 1,
            MaxTransactionsPerBatch = 100,
            MaxBatchAgeMillis = int.MaxValue,
            Enabled = true,
        };
        var initial = new InMemoryMetrics();
        var sealer = new BatchSealer(settings, initial, () => 0L);

        var b1 = sealer.OnBlockCommit(10, 1000, 11, NoTxs())!;
        Assert.AreEqual(1u, b1.BatchNumber);

        // Mid-flight rewire to a new sink.
        var second = new InMemoryMetrics();
        sealer.WithMetrics(second);

        // Batch numbering must continue from 2, not reset to 1.
        var b2 = sealer.OnBlockCommit(11, 2000, 11, NoTxs())!;
        Assert.AreEqual(2u, b2.BatchNumber, "batch numbering survives the rewire");
        Assert.AreEqual(1, second.GetCounter(MetricNames.BatchesSealed), "post-rewire seal hits new sink");
        Assert.AreEqual(1, initial.GetCounter(MetricNames.BatchesSealed), "pre-rewire seal stayed on old sink");
    }

    [TestMethod]
    public void Settings_ValidatePositive_RejectsZero()
    {
        // Regression: previously MaxBlocksPerBatch: 0 (or any other Max*: 0) made
        // BatchSealer.ShouldSeal return true on every block — every block became its own
        // batch, producing degenerate per-block batches that each carry full settlement /
        // proving overhead. The misconfig surfaces as a runaway L1 submission rate hours
        // later instead of at plugin load.
        var ex = Assert.ThrowsExactly<System.IO.InvalidDataException>(() =>
            L2BatchSettings.ValidatePositive(0, "MaxBlocksPerBatch"));
        StringAssert.Contains(ex.Message, "MaxBlocksPerBatch");
    }

    [TestMethod]
    public void Settings_ValidatePositive_RejectsNegative()
    {
        var ex = Assert.ThrowsExactly<System.IO.InvalidDataException>(() =>
            L2BatchSettings.ValidatePositive(-5, "MaxTransactionsPerBatch"));
        StringAssert.Contains(ex.Message, "MaxTransactionsPerBatch");
        StringAssert.Contains(ex.Message, "-5");
    }

    [TestMethod]
    public void Settings_ValidatePositive_AcceptsOne()
    {
        // Boundary partner: 1 is the smallest valid threshold. A user explicitly opting
        // into per-block batches must be allowed (seal-every-block is degenerate but
        // not incoherent — useful for tests, devnet diagnostics).
        Assert.AreEqual(1, L2BatchSettings.ValidatePositive(1, "MaxBlocksPerBatch"));
        Assert.AreEqual(50, L2BatchSettings.ValidatePositive(50, "MaxBlocksPerBatch"));
    }

    private static IEnumerable<byte[]> NoTxs() => Array.Empty<byte[]>();

    private static IEnumerable<byte[]> MakeTxs(int n)
    {
        for (var i = 0; i < n; i++)
            yield return new byte[] { (byte)i, 0xCA, 0xFE };
    }
}
