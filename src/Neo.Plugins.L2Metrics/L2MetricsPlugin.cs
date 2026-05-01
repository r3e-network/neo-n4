using System.Net;
using Neo.L2.Telemetry;

namespace Neo.Plugins.L2;

/// <summary>
/// Composition root for L2 telemetry. Owns the single shared <see cref="IL2Metrics"/> sink
/// the rest of the L2 plugin set wires its <c>WithMetrics(IL2Metrics)</c> calls to, and
/// stands up the <see cref="MetricsHttpServer"/> on the configured port for Prometheus
/// scraping + health/readiness probes.
/// </summary>
/// <remarks>
/// Wiring order (in a host's startup):
/// <code>
/// var metricsPlugin = new L2MetricsPlugin();
/// // each L2 plugin pulls metricsPlugin.Metrics
/// batchPlugin.WithMetrics(metricsPlugin.Metrics);
/// settlementPlugin.WithMetrics(metricsPlugin.Metrics);
/// // …
/// metricsPlugin.Start(); // starts the HTTP server
/// </code>
/// See <c>docs/telemetry.md</c> for the catalog and Prometheus scrape format.
/// </remarks>
public sealed class L2MetricsPlugin : Plugin
{
    private L2MetricsSettings _settings = new();
    private InMemoryMetrics _metrics = new();
    private MetricsHttpServer? _server;
    private Func<bool>? _readinessCheck;

    /// <summary>The shared metrics sink. L2 plugins call <c>WithMetrics(plugin.Metrics)</c>.</summary>
    public IL2Metrics Metrics => _metrics;

    /// <summary>The configured port the HTTP server is bound to (after <see cref="Start"/>). 0 before Start.</summary>
    public int BoundPort => _server?.Endpoint.Port ?? 0;

    /// <inheritdoc />
    public override string Name => "L2MetricsPlugin";

    /// <inheritdoc />
    public override string Description => "Hosts the shared L2 metrics sink and the /metrics + /healthz + /readyz HTTP server.";

    /// <summary>
    /// Wire an optional readiness predicate. <c>/readyz</c> evaluates this on every request;
    /// <c>true</c> → 200, <c>false</c> → 503. When unwired, <c>/readyz</c> always returns 200.
    /// Common predicates: "is the latest batch within N seconds?", "is the prover queue draining?".
    /// </summary>
    public void WithReadinessCheck(Func<bool> check)
    {
        ArgumentNullException.ThrowIfNull(check);
        _readinessCheck = check;
    }

    /// <inheritdoc />
    protected override void Configure()
    {
        _settings = L2MetricsSettings.From(GetConfiguration());
    }

    /// <summary>Start the HTTP server. Idempotent — extra calls are no-ops.</summary>
    /// <param name="portOverride">
    /// Optional port that overrides the configured value. Use <c>0</c> to bind any free port
    /// (useful for tests). Production callers leave this <c>null</c> and let the JSON config drive.
    /// </param>
    public void Start(int? portOverride = null)
    {
        if (!_settings.Enabled || _server is not null) return;
        if (!IPAddress.TryParse(_settings.BindAddress, out var address))
            throw new InvalidOperationException($"L2Metrics: BindAddress '{_settings.BindAddress}' is not a valid IP address");

        var handler = new MetricsRequestHandler(_metrics, _readinessCheck);
        var port = portOverride ?? _settings.Port;
        _server = new MetricsHttpServer(address, port, handler);
        _server.Start();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _server?.Dispose();
        _server = null;
    }
}
