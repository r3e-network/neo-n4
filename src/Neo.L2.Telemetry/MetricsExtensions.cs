namespace Neo.L2.Telemetry;

/// <summary>
/// Convenience extensions that swallow exceptions from the metrics sink so that telemetry
/// failures can never affect business logic.
/// </summary>
/// <remarks>
/// The <see cref="IL2Metrics"/> contract does not promise the implementation is non-throwing —
/// any HTTP-pushing or socket-based sink can raise. Business code that mutates state and then
/// emits a metric must use these wrappers; otherwise a metric throw after a state commit makes
/// the caller see an exception and assume the operation failed, while the state has already
/// been mutated. See iter-162 fix for <c>WithdrawalProcessor</c> and <c>DepositProcessor</c>.
/// </remarks>
public static class MetricsExtensions
{
    /// <summary>Increment a counter; silently swallow any exception from the sink.</summary>
    public static void SafeIncrementCounter(this IL2Metrics metrics, string name, long delta = 1, params (string Key, string Value)[] tags)
    {
        try { metrics.IncrementCounter(name, delta, tags); } catch { }
    }

    /// <summary>Record a histogram value; silently swallow any exception from the sink.</summary>
    public static void SafeRecordHistogram(this IL2Metrics metrics, string name, double value, params (string Key, string Value)[] tags)
    {
        try { metrics.RecordHistogram(name, value, tags); } catch { }
    }

    /// <summary>Set a gauge value; silently swallow any exception from the sink.</summary>
    public static void SafeSetGauge(this IL2Metrics metrics, string name, double value, params (string Key, string Value)[] tags)
    {
        try { metrics.SetGauge(name, value, tags); } catch { }
    }
}
