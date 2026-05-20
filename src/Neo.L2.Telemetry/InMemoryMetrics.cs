using System.Collections.Concurrent;

namespace Neo.L2.Telemetry;

/// <summary>
/// In-process <see cref="IL2Metrics"/> for tests + dev — accumulates emissions in
/// thread-safe maps so tests can assert on what plugins emitted.
/// </summary>
public sealed class InMemoryMetrics : IL2Metrics, IMetricsSource
{
    private readonly ConcurrentDictionary<string, long> _counters = new();
    private readonly ConcurrentDictionary<string, double> _gauges = new();
    private readonly ConcurrentDictionary<string, List<double>> _histograms = new();
    private readonly Lock _gate = new();

    /// <inheritdoc />
    public void IncrementCounter(string name, long delta = 1, params (string Key, string Value)[] tags)
    {
        ArgumentNullException.ThrowIfNull(name);
        var key = TaggedKey(name, tags);
        _counters.AddOrUpdate(key, delta, (_, existing) => existing + delta);
    }

    /// <inheritdoc />
    public void RecordHistogram(string name, double value, params (string Key, string Value)[] tags)
    {
        ArgumentNullException.ThrowIfNull(name);
        var key = TaggedKey(name, tags);
        lock (_gate)
        {
            if (!_histograms.TryGetValue(key, out var list))
            {
                list = new List<double>();
                _histograms[key] = list;
            }
            list.Add(value);
        }
    }

    /// <inheritdoc />
    public void SetGauge(string name, double value, params (string Key, string Value)[] tags)
    {
        ArgumentNullException.ThrowIfNull(name);
        var key = TaggedKey(name, tags);
        _gauges[key] = value;
    }

    /// <summary>Read the current value of a counter (or 0).</summary>
    public long GetCounter(string name, params (string Key, string Value)[] tags) =>
        _counters.TryGetValue(TaggedKey(name, tags), out var v) ? v : 0L;

    /// <summary>Read the current gauge value (or 0.0).</summary>
    public double GetGauge(string name, params (string Key, string Value)[] tags) =>
        _gauges.TryGetValue(TaggedKey(name, tags), out var v) ? v : 0.0;

    /// <summary>Snapshot of all histogram observations under a name.</summary>
    public IReadOnlyList<double> GetHistogram(string name, params (string Key, string Value)[] tags)
    {
        lock (_gate)
        {
            if (_histograms.TryGetValue(TaggedKey(name, tags), out var list))
                return list.ToArray();
            return Array.Empty<double>();
        }
    }

    /// <summary>Total number of counter+gauge+histogram entries (test inspection).</summary>
    public int EntryCount => _counters.Count + _gauges.Count + _histograms.Count;

    /// <summary>
    /// Snapshot the current state for export. The result is a frozen copy; further
    /// emissions on this instance won't mutate it.
    /// </summary>
    public MetricsSnapshot Snapshot()
    {
        Dictionary<string, IReadOnlyList<double>> histos;
        lock (_gate)
        {
            histos = _histograms.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<double>)kv.Value.ToArray());
        }
        // ConcurrentDictionary.ToDictionary already enumerates a thread-safe
        // snapshot; the intermediate ToArray() was a wasted KeyValuePair[] alloc.
        return new MetricsSnapshot
        {
            Counters = _counters.ToDictionary(kv => kv.Key, kv => kv.Value),
            Gauges = _gauges.ToDictionary(kv => kv.Key, kv => kv.Value),
            Histograms = histos,
        };
    }

    private static string TaggedKey(string name, (string Key, string Value)[] tags)
    {
        if (tags.Length == 0) return name;
        var ordered = tags.OrderBy(t => t.Key, StringComparer.Ordinal);
        return name + "{" + string.Join(",", ordered.Select(t => $"{t.Key}={t.Value}")) + "}";
    }
}
