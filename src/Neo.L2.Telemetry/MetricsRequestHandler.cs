namespace Neo.L2.Telemetry;

/// <summary>
/// Pure HTTP request handler for the Prometheus <c>/metrics</c> endpoint. Frameworks-agnostic
/// — plug it into <see cref="MetricsHttpServer"/>, ASP.NET, Kestrel, or even an existing
/// RpcServer plugin endpoint by routing GET /metrics to <see cref="Handle"/>.
/// </summary>
/// <remarks>
/// The handler reads the metrics through an <see cref="IMetricsSource"/> on every call so the
/// response always reflects the latest emissions. There is no internal cache — Prometheus
/// scrapes on its own cadence (default 15s), and the snapshot itself is cheap.
/// </remarks>
public sealed class MetricsRequestHandler
{
    private readonly IMetricsSource _source;

    /// <summary>Construct a handler reading from <paramref name="source"/>.</summary>
    public MetricsRequestHandler(IMetricsSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    /// <summary>Handle a request. <paramref name="path"/> is the URL path component (e.g. <c>"/metrics"</c>).</summary>
    public MetricsHttpResponse Handle(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!IsMetricsPath(path))
            return new MetricsHttpResponse(404, "text/plain; charset=utf-8", "Not Found\n");

        var body = PrometheusExporter.Format(_source.Snapshot());
        return new MetricsHttpResponse(200, PrometheusExporter.ContentType, body);
    }

    private static bool IsMetricsPath(string path)
    {
        // Tolerate trailing slashes and query strings.
        var qIdx = path.IndexOf('?');
        var p = qIdx < 0 ? path : path[..qIdx];
        if (p.Length > 1 && p.EndsWith('/')) p = p[..^1];
        return string.Equals(p, "/metrics", StringComparison.Ordinal);
    }
}

/// <summary>
/// Source of <see cref="MetricsSnapshot"/>s — the export read-side of an
/// <see cref="IL2Metrics"/> implementation. <see cref="InMemoryMetrics"/> implements this.
/// </summary>
public interface IMetricsSource
{
    /// <summary>Snapshot the current state for export.</summary>
    MetricsSnapshot Snapshot();
}

/// <summary>HTTP response produced by <see cref="MetricsRequestHandler.Handle"/>.</summary>
public readonly record struct MetricsHttpResponse(int StatusCode, string ContentType, string Body);
