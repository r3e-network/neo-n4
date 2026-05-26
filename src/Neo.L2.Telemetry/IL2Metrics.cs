namespace Neo.L2.Telemetry;

/// <summary>
/// Minimal metrics surface that L2 plugins emit to. Pluggable backend so production wires
/// OpenTelemetry / Prometheus / etc. without each plugin depending on a specific exporter.
/// </summary>
/// <remarks>
/// Metric names follow the <c>l2.&lt;component&gt;.&lt;metric&gt;</c> convention with dot
/// separators; values are dimensionless unless documented otherwise.
/// </remarks>
public interface IL2Metrics
{
    /// <summary>Increment a monotonic counter.</summary>
    void IncrementCounter(string name, long delta = 1, params ReadOnlySpan<(string Key, string Value)> tags);

    /// <summary>Record a histogram observation (e.g. sealing-time milliseconds).</summary>
    void RecordHistogram(string name, double value, params ReadOnlySpan<(string Key, string Value)> tags);

    /// <summary>Set an instantaneous gauge value.</summary>
    void SetGauge(string name, double value, params ReadOnlySpan<(string Key, string Value)> tags);
}

/// <summary>Null implementation — safe default that costs nothing.</summary>
public sealed class NoOpMetrics : IL2Metrics
{
    /// <summary>Shared singleton — every component can default to this.</summary>
    public static readonly NoOpMetrics Instance = new();

    /// <inheritdoc />
    public void IncrementCounter(string name, long delta = 1, params ReadOnlySpan<(string Key, string Value)> tags) { }

    /// <inheritdoc />
    public void RecordHistogram(string name, double value, params ReadOnlySpan<(string Key, string Value)> tags) { }

    /// <inheritdoc />
    public void SetGauge(string name, double value, params ReadOnlySpan<(string Key, string Value)> tags) { }
}
