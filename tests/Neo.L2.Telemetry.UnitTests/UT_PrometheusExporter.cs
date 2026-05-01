namespace Neo.L2.Telemetry.UnitTests;

/// <summary>
/// Tests for <see cref="PrometheusExporter"/> — verifies the Prometheus exposition format
/// for counters, gauges, histograms, and the dot-to-underscore name mapping.
/// </summary>
[TestClass]
public class UT_PrometheusExporter
{
    [TestMethod]
    public void Format_EmptySnapshot_ProducesEmptyOrPreambleOnly()
    {
        var output = PrometheusExporter.Format(MetricsSnapshot.Empty);
        Assert.AreEqual("", output, "empty snapshot should produce empty output");
    }

    [TestMethod]
    public void Format_Counter_RendersWithTotalSuffix()
    {
        var m = new InMemoryMetrics();
        m.IncrementCounter(MetricNames.BatchesSealed, 5);

        var output = PrometheusExporter.Format(m.Snapshot());

        StringAssert.Contains(output, "# TYPE l2_batch_sealed_total counter");
        StringAssert.Contains(output, "# HELP l2_batch_sealed_total");
        StringAssert.Contains(output, "l2_batch_sealed_total 5");
    }

    [TestMethod]
    public void Format_Counter_WithTags_RendersAsLabels()
    {
        var m = new InMemoryMetrics();
        m.IncrementCounter(MetricNames.ProofsGenerated, 3, ("kind", "Multisig"));
        m.IncrementCounter(MetricNames.ProofsGenerated, 2, ("kind", "Optimistic"));

        var output = PrometheusExporter.Format(m.Snapshot());

        StringAssert.Contains(output, "l2_proving_generated_total{kind=\"Multisig\"} 3");
        StringAssert.Contains(output, "l2_proving_generated_total{kind=\"Optimistic\"} 2");
        // HELP/TYPE only appears once for the family
        var typeLineCount = output.Split('\n').Count(l => l.StartsWith("# TYPE l2_proving_generated_total", StringComparison.Ordinal));
        Assert.AreEqual(1, typeLineCount, "TYPE preamble must appear once per metric family");
    }

    [TestMethod]
    public void Format_Gauge_RendersAsGaugeType()
    {
        var m = new InMemoryMetrics();
        m.SetGauge(MetricNames.BatchTxCount, 42);

        var output = PrometheusExporter.Format(m.Snapshot());

        StringAssert.Contains(output, "# TYPE l2_batch_tx_count gauge");
        StringAssert.Contains(output, "l2_batch_tx_count 42");
    }

    [TestMethod]
    public void Format_Histogram_RendersCount_Sum_Max()
    {
        var m = new InMemoryMetrics();
        m.RecordHistogram(MetricNames.BatchSealLatencyMs, 10.0);
        m.RecordHistogram(MetricNames.BatchSealLatencyMs, 20.0);
        m.RecordHistogram(MetricNames.BatchSealLatencyMs, 30.0);

        var output = PrometheusExporter.Format(m.Snapshot());

        StringAssert.Contains(output, "# TYPE l2_batch_seal_latency_ms summary");
        StringAssert.Contains(output, "l2_batch_seal_latency_ms_count 3");
        StringAssert.Contains(output, "l2_batch_seal_latency_ms_sum 60");
        StringAssert.Contains(output, "l2_batch_seal_latency_ms_max 30");
    }

    [TestMethod]
    public void Format_Mixed_AllThreeKindsCoexist()
    {
        var m = new InMemoryMetrics();
        m.IncrementCounter(MetricNames.BatchesSealed, 7);
        m.SetGauge(MetricNames.BatchTxCount, 100);
        m.RecordHistogram(MetricNames.BatchSealLatencyMs, 5.0);

        var output = PrometheusExporter.Format(m.Snapshot());

        StringAssert.Contains(output, "l2_batch_sealed_total 7");
        StringAssert.Contains(output, "l2_batch_tx_count 100");
        StringAssert.Contains(output, "l2_batch_seal_latency_ms_count 1");
    }

    [TestMethod]
    public void ContentType_IsTheStandardPrometheusValue()
    {
        Assert.AreEqual("text/plain; version=0.0.4; charset=utf-8", PrometheusExporter.ContentType);
    }

    [TestMethod]
    public void Format_DotsAndDashes_ConvertToUnderscores_InNames()
    {
        // Simulates a non-canonical name with dashes — exporter should still produce a valid
        // Prometheus identifier. Direct dictionary so we can craft an unusual key.
        var snapshot = new MetricsSnapshot
        {
            Counters = new Dictionary<string, long> { ["weird.name-with-dashes"] = 1 },
            Gauges = new Dictionary<string, double>(),
            Histograms = new Dictionary<string, IReadOnlyList<double>>(),
        };

        var output = PrometheusExporter.Format(snapshot);

        StringAssert.Contains(output, "weird_name_with_dashes_total 1");
    }

    [TestMethod]
    public void Format_TaggedHistogram_RendersLabelsOnAggregates()
    {
        var m = new InMemoryMetrics();
        m.RecordHistogram(MetricNames.ProveLatencyMs, 100.0, ("kind", "Multisig"));
        m.RecordHistogram(MetricNames.ProveLatencyMs, 200.0, ("kind", "Multisig"));
        m.RecordHistogram(MetricNames.ProveLatencyMs, 1000.0, ("kind", "Optimistic"));

        var output = PrometheusExporter.Format(m.Snapshot());

        StringAssert.Contains(output, "l2_proving_latency_ms_count{kind=\"Multisig\"} 2");
        StringAssert.Contains(output, "l2_proving_latency_ms_sum{kind=\"Multisig\"} 300");
        StringAssert.Contains(output, "l2_proving_latency_ms_count{kind=\"Optimistic\"} 1");
    }

    [TestMethod]
    public void Snapshot_FrozenCopy_ImmuneToFurtherEmissions()
    {
        var m = new InMemoryMetrics();
        m.IncrementCounter(MetricNames.BatchesSealed, 1);
        var snap = m.Snapshot();

        m.IncrementCounter(MetricNames.BatchesSealed, 99); // post-snapshot

        Assert.AreEqual(1, snap.Counters[MetricNames.BatchesSealed], "snapshot is frozen");
        Assert.AreEqual(100, m.GetCounter(MetricNames.BatchesSealed), "live sink keeps mutating");
    }

    [TestMethod]
    public void Format_LabelValue_EscapesQuote()
    {
        var m = new InMemoryMetrics();
        m.IncrementCounter(MetricNames.RpcCalls, 1, ("method", "weird\"name"));

        var output = PrometheusExporter.Format(m.Snapshot());

        StringAssert.Contains(output, @"l2_rpc_calls_total{method=""weird\""name""} 1");
    }

    [TestMethod]
    public void Format_LabelValue_EscapesBackslash()
    {
        var m = new InMemoryMetrics();
        m.IncrementCounter(MetricNames.RpcCalls, 1, ("method", @"path\with\slash"));

        var output = PrometheusExporter.Format(m.Snapshot());

        StringAssert.Contains(output, @"l2_rpc_calls_total{method=""path\\with\\slash""} 1");
    }

    [TestMethod]
    public void Format_PositiveInfinityGauge_RendersAsPlusInf()
    {
        var m = new InMemoryMetrics();
        m.SetGauge(MetricNames.BatchTxCount, double.PositiveInfinity);

        var output = PrometheusExporter.Format(m.Snapshot());

        StringAssert.Contains(output, "l2_batch_tx_count +Inf");
        StringAssert.DoesNotMatch(output, new System.Text.RegularExpressions.Regex(@"\bInfinity\b"));
    }

    [TestMethod]
    public void Format_NegativeInfinityGauge_RendersAsMinusInf()
    {
        var m = new InMemoryMetrics();
        m.SetGauge(MetricNames.BatchTxCount, double.NegativeInfinity);

        var output = PrometheusExporter.Format(m.Snapshot());

        StringAssert.Contains(output, "l2_batch_tx_count -Inf");
    }

    [TestMethod]
    public void Format_NaNGauge_RendersAsNaN()
    {
        var m = new InMemoryMetrics();
        m.SetGauge(MetricNames.BatchTxCount, double.NaN);

        var output = PrometheusExporter.Format(m.Snapshot());

        StringAssert.Contains(output, "l2_batch_tx_count NaN");
    }

    [TestMethod]
    public void Format_LabelValue_EscapesNewline()
    {
        var m = new InMemoryMetrics();
        m.IncrementCounter(MetricNames.RpcCalls, 1, ("method", "line1\nline2"));

        var output = PrometheusExporter.Format(m.Snapshot());

        StringAssert.Contains(output, @"l2_rpc_calls_total{method=""line1\nline2""} 1");
        // Critical: the literal \n must NOT appear unescaped — that would break the parser
        // by making a new line look like a separate metric line.
        var lines = output.Split('\n');
        Assert.IsFalse(lines.Any(l => l.StartsWith("line2")), "raw newline must be escaped");
    }
}
