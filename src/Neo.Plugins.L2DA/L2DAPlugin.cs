using Microsoft.Extensions.Configuration;
using Neo.L2;
using Neo.L2.Telemetry;

namespace Neo.Plugins.L2;

/// <summary>
/// Selects the DA writer for the L2 chain based on configured <see cref="DAMode"/>. Other
/// plugins (e.g. <c>L2BatchPlugin</c> + <c>L2SettlementPlugin</c>) call
/// <see cref="GetWriter"/> to publish batch payloads.
/// </summary>
public sealed class L2DAPlugin : Plugin
{
    private DAMode _mode = DAMode.External;
    private IDAWriter _writer = new InMemoryDAWriter();
    private IL2Metrics _metrics = NoOpMetrics.Instance;

    /// <inheritdoc />
    public override string Name => "L2DAPlugin";

    /// <inheritdoc />
    public override string Description => "Selects the DA writer (L1 / NeoFS / External / DAC) per chain configuration.";

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
    protected override void Configure()
    {
        var section = GetConfiguration();
        _mode = (DAMode)section.GetValue<byte>("DAMode", (byte)DAMode.External);
        IDAWriter raw = _mode switch
        {
            DAMode.L1 => new L1DAWriter(),
            DAMode.NeoFS => new NeoFSDAWriter(),
            DAMode.External => new ExternalDAWriter(),
            DAMode.DAC => new DACDAWriter(),
            _ => new InMemoryDAWriter(),
        };
        _writer = WrapWithMetrics(raw);
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
