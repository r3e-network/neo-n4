using Microsoft.Extensions.Configuration;
using Neo.L2;
using Neo.L2.Persistence;
using Neo.L2.Telemetry;

namespace Neo.Plugins.L2;

/// <summary>
/// Selects the DA writer for the L2 chain based on configured <see cref="DAMode"/>. Other
/// plugins (e.g. <c>L2BatchPlugin</c> + <c>L2SettlementPlugin</c>) call
/// <see cref="GetWriter"/> to publish batch payloads.
/// </summary>
public sealed class L2DAPlugin : Plugin
{
    private DAMode _mode = DAMode.NeoFS;
    private IDAWriter _writer = new NeoFsLikeDAWriter();
    private IL2Metrics _metrics = NoOpMetrics.Instance;

    /// <inheritdoc />
    public override string Name => "L2DAPlugin";

    /// <inheritdoc />
    public override string Description => "Selects the DA writer (NeoFS default; L1 / External / DAC override) per chain configuration.";

    /// <summary>
    /// The currently active writer (already wrapped in <see cref="MetricsEmittingDAWriter"/>
    /// when a non-NoOp metrics sink has been wired).
    /// </summary>
    public IDAWriter Writer => _writer;

    /// <summary>
    /// Wire a metrics sink. Subsequent <see cref="GetWriter"/> calls return a writer
    /// that emits <c>l2.da.published</c>, <c>l2.da.publish_latency_ms</c>, and
    /// <c>l2.da.publish_failures</c> tagged by mode. Defaults to <see cref="NoOpMetrics"/>.
    /// </summary>
    public void WithMetrics(IL2Metrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        _metrics = metrics;
        _writer = WrapWithMetrics(Unwrap(_writer));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Mode → writer wiring:
    /// <list type="bullet">
    /// <item><description><see cref="DAMode.NeoFS"/> → <see cref="NeoFsLikeDAWriter"/> — chain-scoped object store with per-call defensive copies; production swaps this for a real NeoFS SDK by re-implementing <see cref="IDAWriter"/> against the SDK and registering it before <see cref="Configure"/> runs.</description></item>
    /// <item><description><see cref="DAMode.External"/> → <see cref="InMemoryDAWriter"/> — content-addressed in-process store for explicitly configured third-party DA tests and demos.</description></item>
    /// <item><description><see cref="DAMode.L1"/> and <see cref="DAMode.DAC"/> have no first-class default — both require external clients (L1 RPC + signing wallet; DAC committee key set + attestation aggregation) the plugin can't materialize alone. Configuring either without first calling <see cref="WithWriter"/> throws <see cref="NotSupportedException"/> with a clear operator message.</description></item>
    /// </list>
    /// </remarks>
    protected override void Configure()
    {
        var section = GetConfiguration();
        var rawMode = section.GetValue<byte>("DAMode", (byte)DAMode.NeoFS);
        _mode = ResolveDAMode(rawMode);
        // If the operator already provided a writer via WithWriter (e.g. an L1 RPC-backed
        // implementation or a real NeoFS SDK adapter), respect it. Otherwise pick the
        // built-in default for the configured mode.
        if (_writerOverridden) { _writer = WrapWithMetrics(Unwrap(_writer)); return; }
        var dataDir = section.GetValue<string?>("DataDirectory");
        _writer = WrapWithMetrics(BuildDefaultWriter(_mode, dataDir));
    }

    /// <summary>
    /// Pure writer-resolution rule used by <see cref="Configure"/>. Extracted so the
    /// "DataDirectory wins / falls back to mode-specific default" logic can be unit
    /// tested without driving Plugin.GetConfiguration() through a real config.json on
    /// disk. Without this seam the DataDirectory-driven RocksDB path could silently
    /// regress to an in-memory default — which would be invisible until production data
    /// vanished on restart.
    /// </summary>
    /// <remarks>
    /// If <paramref name="dataDir"/> is non-empty, returns a <see cref="PersistentDAWriter"/>
    /// over a <see cref="RocksDbKeyValueStore"/> at that path — independent of mode, since
    /// the writer is mode-tagged. Otherwise picks the built-in default for the configured
    /// mode, throwing <see cref="NotSupportedException"/> for L1 / DAC which need
    /// operator-supplied writers.
    /// </remarks>
    public static IDAWriter BuildDefaultWriter(DAMode mode, string? dataDir)
    {
        // If the operator configured a DataDirectory, use it to back a durable
        // PersistentDAWriter via RocksDB. Without it, fall back to in-memory (suitable
        // for tests + devnets only). Production deployments should always set
        // DataDirectory or wire WithWriter() — pinning this is what makes RocksDB the
        // production default for the Neo Elastic Network.
        if (!string.IsNullOrWhiteSpace(dataDir))
        {
            return new PersistentDAWriter(
                new RocksDbKeyValueStore(dataDir),
                mode,
                ownsStore: true);
        }
        return mode switch
        {
            DAMode.External => new InMemoryDAWriter(),
            DAMode.NeoFS => new NeoFsLikeDAWriter(),
            DAMode.L1 => throw new NotSupportedException(
                "DAMode.L1 has no zero-config default writer (operator must supply RPC client + signer). " +
                "Construct Neo.Plugins.L2DA.JsonRpcL1DAWriter(rpc, daContractHash, signAndSend) and pass it to WithWriter() before Configure(); " +
                "or set DataDirectory in config to use the local PersistentDAWriter fallback."),
            DAMode.DAC => throw new NotSupportedException(
                "DAMode.DAC has no built-in default writer — provide a committee-attestation IDAWriter via WithWriter() before Configure()"),
            // ResolveDAMode guarantees we don't hit this; kept as a defense-in-depth assert.
            _ => throw new InvalidOperationException($"unhandled DAMode {(byte)mode}"),
        };
    }

    private bool _writerOverridden;

    /// <summary>
    /// Override the writer with an operator-provided implementation. Used by production
    /// deployments that wire an L1 RPC client, real NeoFS SDK, or DAC committee. Call
    /// before the plugin host runs <see cref="Configure"/>.
    /// </summary>
    public void WithWriter(IDAWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _writer = writer;
        _mode = writer.Mode;
        _writerOverridden = true;
    }

    /// <summary>
    /// Validate a raw DAMode byte against the defined enum range. Without this an operator
    /// who misconfigures <c>DAMode = 99</c> would silently fall through to the in-memory
    /// writer and lose real DA delivery — by the time something downstream notices, the
    /// batch data is gone.
    /// </summary>
    public static DAMode ResolveDAMode(byte raw)
    {
        var mode = (DAMode)raw;
        if (mode is not (DAMode.L1 or DAMode.NeoFS or DAMode.External or DAMode.DAC))
            throw new InvalidOperationException(
                $"DAMode {raw} is not one of L1(0), NeoFS(1), External(2), DAC(3) — fix config");
        return mode;
    }

    /// <summary>
    /// Get the writer (used by other plugins / tests; in tests you may inject a mock by
    /// reassigning).
    /// </summary>
    public IDAWriter GetWriter() => _writer;

    private IDAWriter WrapWithMetrics(IDAWriter raw)
    {
        if (ReferenceEquals(_metrics, NoOpMetrics.Instance)) return raw;
        return new MetricsEmittingDAWriter(raw, _metrics);
    }

    private static IDAWriter Unwrap(IDAWriter w)
        => w is MetricsEmittingDAWriter wrapped ? wrapped.Inner : w;
}
