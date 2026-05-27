namespace Neo.L2.Telemetry.UnitTests;

/// <summary>
/// Unit tests for <see cref="MetricsExtensions"/> — the Safe* helpers added in iter
/// 163. Indirectly tested via plugins; this pins the contract directly so future
/// refactors don't accidentally remove the swallow.
/// </summary>
[TestClass]
public class UT_MetricsExtensions
{
    [TestMethod]
    public void SafeIncrementCounter_ThrowingSink_DoesNotPropagate()
    {
        // The whole point of the Safe* wrappers is to never let a metric-sink
        // failure surface to business logic. Pin it directly.
        var sink = new ThrowingSink();
        sink.SafeIncrementCounter("foo");
        sink.SafeIncrementCounter("foo", 5);
        sink.SafeIncrementCounter("foo", 5, ("k", "v"));
    }

    [TestMethod]
    public void SafeRecordHistogram_ThrowingSink_DoesNotPropagate()
    {
        var sink = new ThrowingSink();
        sink.SafeRecordHistogram("bar", 1.5);
        sink.SafeRecordHistogram("bar", 1.5, ("k", "v"));
    }

    [TestMethod]
    public void SafeSetGauge_ThrowingSink_DoesNotPropagate()
    {
        var sink = new ThrowingSink();
        sink.SafeSetGauge("baz", 42.0);
        sink.SafeSetGauge("baz", 42.0, ("k", "v"));
    }

    [TestMethod]
    public void Safe_ForwardsToWrappedSink_OnHappyPath()
    {
        // Sanity: the wrapper actually invokes the underlying sink when no throw.
        var sink = new InMemoryMetrics();
        sink.SafeIncrementCounter(MetricNames.BatchesSealed, 3);
        sink.SafeRecordHistogram(MetricNames.BatchSealLatencyMs, 12.5);
        sink.SafeSetGauge(MetricNames.BatchTxCount, 99.0);

        Assert.AreEqual(3, sink.GetCounter(MetricNames.BatchesSealed));
        var hist = sink.GetHistogram(MetricNames.BatchSealLatencyMs);
        Assert.AreEqual(1, hist.Count);
        Assert.AreEqual(12.5, hist[0]);
        Assert.AreEqual(99.0, sink.GetGauge(MetricNames.BatchTxCount));
    }

    private sealed class ThrowingSink : IL2Metrics
    {
        public void IncrementCounter(string name, long delta = 1, params ReadOnlySpan<(string Key, string Value)> tags)
            => throw new InvalidOperationException("sink down");
        public void RecordHistogram(string name, double value, params ReadOnlySpan<(string Key, string Value)> tags)
            => throw new InvalidOperationException("sink down");
        public void SetGauge(string name, double value, params ReadOnlySpan<(string Key, string Value)> tags)
            => throw new InvalidOperationException("sink down");
    }
}
