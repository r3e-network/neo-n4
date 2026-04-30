namespace Neo.L2.Telemetry.UnitTests;

[TestClass]
public class UT_Metrics
{
    [TestMethod]
    public void NoOp_AllMethodsAreNoOps()
    {
        var m = NoOpMetrics.Instance;
        m.IncrementCounter(MetricNames.BatchesSealed);
        m.RecordHistogram(MetricNames.BatchSealLatencyMs, 1234.5);
        m.SetGauge(MetricNames.BatchTxCount, 42);
        // Just survives without throwing.
    }

    [TestMethod]
    public void InMemory_CounterAccumulates()
    {
        var m = new InMemoryMetrics();
        m.IncrementCounter(MetricNames.BatchesSealed);
        m.IncrementCounter(MetricNames.BatchesSealed, 3);
        Assert.AreEqual(4L, m.GetCounter(MetricNames.BatchesSealed));
    }

    [TestMethod]
    public void InMemory_GaugeReplacesValue()
    {
        var m = new InMemoryMetrics();
        m.SetGauge(MetricNames.BatchTxCount, 10);
        m.SetGauge(MetricNames.BatchTxCount, 50);
        Assert.AreEqual(50.0, m.GetGauge(MetricNames.BatchTxCount));
    }

    [TestMethod]
    public void InMemory_HistogramAccumulatesObservations()
    {
        var m = new InMemoryMetrics();
        m.RecordHistogram(MetricNames.BatchSealLatencyMs, 100);
        m.RecordHistogram(MetricNames.BatchSealLatencyMs, 200);
        m.RecordHistogram(MetricNames.BatchSealLatencyMs, 150);
        var observations = m.GetHistogram(MetricNames.BatchSealLatencyMs);
        Assert.AreEqual(3, observations.Count);
        Assert.AreEqual(450, observations.Sum());
    }

    [TestMethod]
    public void InMemory_TaggedMetricsAreSeparate()
    {
        var m = new InMemoryMetrics();
        m.IncrementCounter(MetricNames.ProofsGenerated, 1, ("kind", "Multisig"));
        m.IncrementCounter(MetricNames.ProofsGenerated, 1, ("kind", "Zk"));
        m.IncrementCounter(MetricNames.ProofsGenerated, 5, ("kind", "Multisig"));

        Assert.AreEqual(6L, m.GetCounter(MetricNames.ProofsGenerated, ("kind", "Multisig")));
        Assert.AreEqual(1L, m.GetCounter(MetricNames.ProofsGenerated, ("kind", "Zk")));
    }

    [TestMethod]
    public void InMemory_TagOrderingIsCanonical()
    {
        // Same tags in different order must produce the same metric key.
        var m = new InMemoryMetrics();
        m.IncrementCounter("test", 1, ("a", "1"), ("b", "2"));
        m.IncrementCounter("test", 1, ("b", "2"), ("a", "1"));
        Assert.AreEqual(2L, m.GetCounter("test", ("a", "1"), ("b", "2")));
    }

    [TestMethod]
    public void InMemory_MissingMetricsReturnZero()
    {
        var m = new InMemoryMetrics();
        Assert.AreEqual(0L, m.GetCounter("nope"));
        Assert.AreEqual(0.0, m.GetGauge("nope"));
        Assert.AreEqual(0, m.GetHistogram("nope").Count);
    }

    [TestMethod]
    public void MetricNames_AreStableConstants()
    {
        // Smoke test: every name exists and starts with the canonical prefix.
        Assert.IsTrue(MetricNames.BatchesSealed.StartsWith("l2."));
        Assert.IsTrue(MetricNames.ProofsGenerated.StartsWith("l2."));
        Assert.IsTrue(MetricNames.AuditsRun.StartsWith("l2."));
        Assert.IsTrue(MetricNames.BisectionRounds.StartsWith("l2."));
    }
}
