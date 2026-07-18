namespace Neo.L2.Telemetry;

/// <summary>
/// Pure HTTP request handler for the operator-facing endpoints (<c>/metrics</c>,
/// <c>/healthz</c>, <c>/readyz</c>, <c>/healthprobe</c>). Framework-agnostic — plug it into
/// <see cref="MetricsHttpServer"/>, ASP.NET, Kestrel, or an existing RpcServer plugin
/// endpoint by routing GET requests through <see cref="Handle"/>.
/// </summary>
/// <remarks>
/// The handler reads the metrics through an <see cref="IMetricsSource"/> on every call so the
/// response always reflects the latest emissions. There is no internal cache — Prometheus
/// scrapes on its own cadence (default 15s), and the snapshot itself is cheap.
/// <para>
/// <c>/healthz</c> always returns 200 (process liveness). <c>/readyz</c> returns 200 only when
/// an explicitly wired readiness predicate returns <c>true</c>; otherwise 503.
/// Standard pattern for Kubernetes / Docker / load-balancer probes.
/// </para>
/// <para>
/// <c>/healthprobe</c> returns compact operator health JSON when a body provider is wired
/// (LocalHost composition supplies <c>LocalHostHealthProbeDocument</c>); when unwired it
/// fails closed with 503. The JSON body carries healthy/unhealthy flags — HTTP status stays
/// 200 so ops scripts can <c>curl | jq</c> without treating partial local runtime as a scrape outage.
/// Does not claim L1 settle or prove-batch (funded gates).
/// </para>
/// </remarks>
public sealed class MetricsRequestHandler
{
    private const string PlainText = "text/plain; charset=utf-8";
    private const string ApplicationJson = "application/json; charset=utf-8";

    private readonly IMetricsSource _source;
    private readonly Func<bool>? _readinessCheck;
    private readonly Func<bool>? _livenessCheck;
    private readonly Func<string>? _healthProbeBody;

    /// <summary>Construct a handler reading from <paramref name="source"/>.</summary>
    /// <param name="source">Metrics snapshot source.</param>
    /// <param name="readinessCheck">
    /// Optional predicate for <c>/readyz</c>. When <c>null</c>, <c>/readyz</c>
    /// fails closed with 503 so an incompletely composed node cannot receive traffic.
    /// </param>
    /// <param name="livenessCheck">
    /// Optional predicate for <c>/healthz</c>. When <c>null</c>, <c>/healthz</c>
    /// always returns 200. Wire a lightweight check (e.g. "is the main loop
    /// still ticking?") to detect process hangs. Avoid heavy checks — k8s
    /// liveness probes run frequently and a slow check can trigger a restart.
    /// </param>
    /// <param name="healthProbeBody">
    /// Optional provider for <c>/healthprobe</c> JSON body. When <c>null</c>,
    /// <c>/healthprobe</c> fails closed with 503. Prefer compact camelCase JSON
    /// (e.g. serialized <c>LocalHostHealthProbeDocument</c>).
    /// </param>
    public MetricsRequestHandler(
        IMetricsSource source,
        Func<bool>? readinessCheck = null,
        Func<bool>? livenessCheck = null,
        Func<string>? healthProbeBody = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _readinessCheck = readinessCheck;
        _livenessCheck = livenessCheck;
        _healthProbeBody = healthProbeBody;
    }

    /// <summary>Handle a request. <paramref name="path"/> is the URL path component (e.g. <c>"/metrics"</c>).</summary>
    public MetricsHttpResponse Handle(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var normalized = NormalizePath(path);
        return normalized switch
        {
            "/metrics" => HandleMetrics(),
            "/healthz" => HandleLiveness(),
            "/readyz" => HandleReady(),
            "/healthprobe" => HandleHealthProbe(),
            _ => new MetricsHttpResponse(404, PlainText, "Not Found\n"),
        };
    }

    private MetricsHttpResponse HandleMetrics()
    {
        // Wrap the snapshot/format pipeline so a buggy IMetricsSource (or a downstream
        // formatter regression) doesn't surface as a connection close to the scraper.
        // The exception text is intentionally generic in the body — operators should
        // look at logs, not the HTTP response. 500 also flips most Prometheus servers
        // into an "exporter down" alert state, which is the correct outcome.
        try
        {
            var snapshot = _source.Snapshot()
                ?? throw new InvalidOperationException("IMetricsSource.Snapshot returned null");
            var body = PrometheusExporter.Format(snapshot);
            return new MetricsHttpResponse(200, PrometheusExporter.ContentType, body);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new MetricsHttpResponse(500, PlainText, "metrics export failed\n");
        }
    }

    private MetricsHttpResponse HandleLiveness()
    {
        if (_livenessCheck is null) return new MetricsHttpResponse(200, PlainText, "ok\n");

        bool isLive;
        try { isLive = _livenessCheck(); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new MetricsHttpResponse(500, PlainText, "liveness check threw\n");
        }
        return isLive
            ? new MetricsHttpResponse(200, PlainText, "ok\n")
            : new MetricsHttpResponse(500, PlainText, "not live\n");
    }

    private MetricsHttpResponse HandleReady()
    {
        if (_readinessCheck is null)
            return new MetricsHttpResponse(503, PlainText, "readiness check not configured\n");

        bool isReady;
        try { isReady = _readinessCheck(); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // If the predicate itself threw, we definitely aren't ready — surface 503 instead
            // of letting the exception kill the connection. The body is generic on purpose;
            // operators should look at the exception in their logs, not the HTTP response.
            return new MetricsHttpResponse(503, PlainText, "predicate threw\n");
        }
        return isReady
            ? new MetricsHttpResponse(200, PlainText, "ready\n")
            : new MetricsHttpResponse(503, PlainText, "not ready\n");
    }

    private MetricsHttpResponse HandleHealthProbe()
    {
        if (_healthProbeBody is null)
            return new MetricsHttpResponse(503, PlainText, "health probe not configured\n");

        try
        {
            var body = _healthProbeBody()
                ?? throw new InvalidOperationException("health probe body provider returned null");
            if (string.IsNullOrWhiteSpace(body))
                throw new InvalidOperationException("health probe body provider returned empty body");
            if (!body.EndsWith('\n'))
                body += "\n";
            return new MetricsHttpResponse(200, ApplicationJson, body);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new MetricsHttpResponse(500, PlainText, "health probe failed\n");
        }
    }

    private static string NormalizePath(string path)
    {
        var qIdx = path.IndexOf('?');
        var p = qIdx < 0 ? path : path[..qIdx];
        if (p.Length > 1 && p.EndsWith('/')) p = p[..^1];
        return p;
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
