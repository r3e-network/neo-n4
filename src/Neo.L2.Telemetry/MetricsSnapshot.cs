namespace Neo.L2.Telemetry;

/// <summary>
/// Frozen point-in-time read of every counter / gauge / histogram an
/// <see cref="IL2Metrics"/> sink has accumulated. The only entry point an exporter needs.
/// </summary>
/// <remarks>
/// Keys here are the same canonical strings <see cref="InMemoryMetrics"/> uses internally —
/// either the bare metric name (e.g. <c>l2.batch.sealed</c>) or the tagged form
/// (e.g. <c>l2.proving.generated{kind=Multisig}</c>), with tag pairs sorted by key.
/// </remarks>
public sealed record MetricsSnapshot
{
    /// <summary>Counter values keyed by canonical name (with optional tag suffix).</summary>
    public required IReadOnlyDictionary<string, long> Counters { get; init; }

    /// <summary>Gauge values keyed by canonical name (with optional tag suffix).</summary>
    public required IReadOnlyDictionary<string, double> Gauges { get; init; }

    /// <summary>Histogram observations keyed by canonical name (with optional tag suffix).</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<double>> Histograms { get; init; }

    /// <summary>An empty snapshot (useful for tests and as a default).</summary>
    public static MetricsSnapshot Empty { get; } = new()
    {
        Counters = new Dictionary<string, long>(),
        Gauges = new Dictionary<string, double>(),
        Histograms = new Dictionary<string, IReadOnlyList<double>>(),
    };

    /// <summary>Total number of entries across all categories.</summary>
    public int TotalEntries => Counters.Count + Gauges.Count + Histograms.Count;
}
