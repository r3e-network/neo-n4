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
    private readonly Lock _startGate = new();

    /// <summary>Construct with default settings (port 9090, loopback).</summary>
    public L2MetricsPlugin()
    {
    }

    internal L2MetricsPlugin(L2MetricsSettings settings) : this()
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    /// <summary>
    /// Host composition factory: preload bind address/port from a chain working directory
    /// without the Neo plugin config loader.
    /// </summary>
    public static L2MetricsPlugin CreateFromChainDirectory(string chainDirectory)
        => new(L2MetricsSettings.FromChainDirectory(chainDirectory));

    /// <summary>The shared metrics sink. L2 plugins call <c>WithMetrics(plugin.Metrics)</c>.</summary>
    public IL2Metrics Metrics => _metrics;

    /// <summary>Loaded metrics settings (host composition / tests).</summary>
    internal L2MetricsSettings Settings => _settings;

    /// <summary>The configured port the HTTP server is bound to (after <see cref="Start"/>). 0 before Start.</summary>
    public int BoundPort => _server?.Endpoint.Port ?? 0;

    /// <inheritdoc />
    public override string Name => "L2MetricsPlugin";

    /// <inheritdoc />
    public override string Description => "Hosts the shared L2 metrics sink and the /metrics + /healthz + /readyz HTTP server.";

    /// <summary>
    /// Wire a readiness predicate. <c>/readyz</c> evaluates this on every request;
    /// <c>true</c> → 200, <c>false</c> → 503. When unwired, <c>/readyz</c> fails closed with 503.
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
        lock (_startGate)
        {
            if (!_settings.Enabled || _server is not null) return;
            var address = ResolveBindAddress(_settings.BindAddress);

            var handler = new MetricsRequestHandler(_metrics, _readinessCheck);
            var port = portOverride ?? _settings.Port;
            var server = new MetricsHttpServer(
                address,
                port,
                handler,
                maxConcurrentConnections: _settings.MaxConcurrentConnections);
            server.Start();
            _server = server;
        }
    }

    /// <summary>
    /// Stop the HTTP server without disposing the metrics sink. Idempotent.
    /// Host compositions can re-<see cref="Start"/> later.
    /// </summary>
    public void Stop()
    {
        lock (_startGate)
        {
            _server?.Dispose();
            _server = null;
        }
    }

    /// <summary>
    /// Resolve <paramref name="bindAddress"/> to an <see cref="IPAddress"/>. Accepts numeric
    /// addresses (<c>127.0.0.1</c>, <c>0.0.0.0</c>, <c>::1</c>) and hostnames (<c>localhost</c>);
    /// for hostnames, returns the first DNS resolution result.
    /// </summary>
    public static IPAddress ResolveBindAddress(string bindAddress)
    {
        ArgumentException.ThrowIfNullOrEmpty(bindAddress);
        if (IPAddress.TryParse(bindAddress, out var direct))
            return direct;

        // DNS lookup. Hostnames like "localhost" land here.
        IPAddress[] resolved;
        try { resolved = System.Net.Dns.GetHostAddresses(bindAddress); }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"L2Metrics: BindAddress '{bindAddress}' is not a valid IP address and DNS resolution failed: {ex.Message}", ex);
        }
        if (resolved.Length == 0)
            throw new InvalidOperationException($"L2Metrics: BindAddress '{bindAddress}' resolved to zero addresses");
        return resolved[0];
    }

    /// <inheritdoc />
    /// <remarks>
    /// neo-project/neo master sealed the public <c>Dispose()</c>; cleanup goes through
    /// the standard <c>Dispose(bool disposing)</c> hook.
    /// </remarks>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _server?.Dispose();
            _server = null;
        }
        base.Dispose(disposing);
    }
}
