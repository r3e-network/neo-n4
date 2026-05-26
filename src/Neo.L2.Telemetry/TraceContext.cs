using System.Diagnostics;

namespace Neo.L2.Telemetry;

/// <summary>
/// Lightweight trace context propagation. Stores a correlation ID in
/// <see cref="AsyncLocal{T}"/> so it flows through the async call chain
/// automatically. Set once at the entry point (RPC handler, batch sealer
/// tick) and every subsequent log/metric carries the same ID.
/// </summary>
/// <remarks>
/// For production with distributed tracing, replace this with
/// <c>ActivitySource</c> + OpenTelemetry's W3C TraceContext propagation.
/// The <c>AsyncLocal</c> pattern mirrors <c>Activity.Current</c> so
/// migration is mechanical.
/// </remarks>
public static class TraceContext
{
    private static readonly AsyncLocal<string?> _correlationId = new();

    /// <summary>The correlation ID for the current async flow, or a new one
    /// if none has been set. Never returns null.</summary>
    public static string CorrelationId =>
        _correlationId.Value ?? Guid.NewGuid().ToString("N")[..12];

    /// <summary>Set the correlation ID for the current async flow.
    /// Call at entry points (RPC handler, batch sealer tick, watcher loop).</summary>
    public static void SetCorrelationId(string correlationId)
    {
        _correlationId.Value = correlationId ?? Guid.NewGuid().ToString("N")[..12];
    }

    /// <summary>Generate and set a fresh correlation ID. Returns the new ID.</summary>
    public static string NewCorrelationId()
    {
        var id = Guid.NewGuid().ToString("N")[..12];
        _correlationId.Value = id;
        return id;
    }

    /// <summary>Clear the correlation ID for the current async flow.</summary>
    public static void Clear()
    {
        _correlationId.Value = null;
    }

    /// <summary>Wrap an action in a new trace context.</summary>
    public static async Task RunWithTraceAsync(Func<Task> action, string? correlationId = null)
    {
        var previous = _correlationId.Value;
        try
        {
            _correlationId.Value = correlationId ?? Guid.NewGuid().ToString("N")[..12];
            await action().ConfigureAwait(false);
        }
        finally
        {
            _correlationId.Value = previous;
        }
    }
}
