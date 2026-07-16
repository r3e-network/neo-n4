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
    private DAMode _mode = DAMode.Local;
    private IDAWriter _writer;
    private IDAReader? _reader;
    private DADeploymentProfile _profile = DADeploymentProfile.Development;
    private IL2Metrics _metrics = NoOpMetrics.Instance;
    private bool _writerOverridden;
    private bool _productionBackendOverridden;
    private bool _constructed;

    /// <summary>
    /// Relative path of the DA plugin config under a chain working directory
    /// (written by deploy-report materialization).
    /// </summary>
    public const string RelativePluginConfigPath =
        "Plugins/Neo.Plugins.L2DA/config.json";

    /// <summary>Construct with an explicitly local, development-only default backend.</summary>
    public L2DAPlugin()
    {
        var writer = new InMemoryDAWriter();
        _writer = writer;
        _reader = writer.CreateReader();
        _constructed = true;
        Configure();
    }

    /// <summary>
    /// Host composition for Multisig/Optimistic local DA: open
    /// <c>PersistentDAWriter</c> under <c>data/settlement/da</c> and install it as the
    /// active writer. Public DA modes (L1/NeoFS/External/DAC) must use
    /// <see cref="WithProductionBackend"/> instead.
    /// </summary>
    public static L2DAPlugin CreateLocalFromChainDirectory(string chainDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainDirectory);
        var writer = PersistentDAWriter.OpenLocalFromChainDirectory(chainDirectory);
        var plugin = new L2DAPlugin();
        plugin.WithWriter(writer);
        return plugin;
    }

    /// <inheritdoc />
    public override string Name => "L2DAPlugin";

    /// <inheritdoc />
    public override string Description => "Selects a truthfully labeled DA backend and rejects simulated/local storage in production.";

    /// <summary>
    /// The currently active writer (already wrapped in <see cref="MetricsEmittingDAWriter"/>
    /// when a non-NoOp metrics sink has been wired).
    /// </summary>
    public IDAWriter Writer => _writer;

    /// <summary>The assurance profile selected by configuration.</summary>
    public DADeploymentProfile Profile => _profile;

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
    /// Development mode → writer wiring:
    /// <list type="bullet">
    /// <item><description><see cref="DAMode.Local"/> → local in-memory or RocksDB durability.</description></item>
    /// <item><description><see cref="DAMode.NeoFS"/> → <see cref="NeoFsLikeDAWriter"/> semantic simulation.</description></item>
    /// <item><description>L1, External, and DAC require explicitly injected development adapters.</description></item>
    /// </list>
    /// Production mode has no built-in fallback and requires
    /// <see cref="WithProductionBackend"/>.
    /// </remarks>
    protected override void Configure()
    {
        if (!_constructed) return;

        var section = GetConfiguration();
        var rawMode = section.GetValue<byte>("DAMode", (byte)DAMode.Local);
        _mode = ResolveDAMode(rawMode);
        _profile = ResolveProfile(section.GetValue<string?>("Profile"), _mode);
        if (_writerOverridden)
        {
            ValidateConfiguredBackend(
                _profile,
                _mode,
                Unwrap(_writer),
                Unwrap(_reader),
                _productionBackendOverridden);
            _writer = WrapWithMetrics(Unwrap(_writer));
            return;
        }

        var dataDir = section.GetValue<string?>("DataDirectory");
        var writer = BuildDefaultWriter(_mode, dataDir, _profile);
        _reader = CreateDevelopmentReader(writer);
        _writer = WrapWithMetrics(writer);
    }

    /// <summary>
    /// Pure writer-resolution rule used by <see cref="Configure"/>. Extracted so the
    /// local-durability and mode-specific development rules can be tested without driving
    /// Plugin.GetConfiguration() through a real config.json on disk.
    /// </summary>
    /// <remarks>
    /// A data directory is accepted only with <see cref="DAMode.Local"/>. Production never
    /// materializes a built-in writer; operators must inject a production writer/reader pair.
    /// </remarks>
    public static IDAWriter BuildDefaultWriter(
        DAMode mode,
        string? dataDir,
        DADeploymentProfile profile = DADeploymentProfile.Development)
    {
        if (profile == DADeploymentProfile.Production)
            throw new InvalidOperationException(
                $"Production DA profile for {mode} requires WithProductionBackend with a public writer and independent reader; no local or simulated fallback is permitted");

        if (!string.IsNullOrWhiteSpace(dataDir) && mode != DAMode.Local)
            throw new InvalidOperationException(
                $"DataDirectory provides local durability and cannot satisfy public DAMode {mode}; configure DAMode.Local or inject the real backend");

        return mode switch
        {
            DAMode.Local when !string.IsNullOrWhiteSpace(dataDir) => new PersistentDAWriter(
                new RocksDbKeyValueStore(dataDir),
                ownsStore: true),
            DAMode.Local => new InMemoryDAWriter(),
            DAMode.NeoFS => new NeoFsLikeDAWriter(),
            DAMode.L1 => throw new NotSupportedException(
                "DAMode.L1 has no development default — inject an L1 adapter with signed-transaction confirmation evidence"),
            DAMode.External => throw new NotSupportedException(
                "DAMode.External has no local fallback — inject the external provider adapter"),
            DAMode.DAC => throw new NotSupportedException(
                "DAMode.DAC has no zero-config default — inject the committee distribution and attestation adapter"),
            _ => throw new InvalidOperationException($"unhandled DAMode {(byte)mode}"),
        };
    }

    /// <summary>
    /// Override the writer for development and integration environments. Production must
    /// use <see cref="WithProductionBackend"/> so an independent reader is mandatory.
    /// </summary>
    public void WithWriter(IDAWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var previous = Unwrap(_writer);
        if (!ReferenceEquals(previous, writer) && previous is IDisposable disposable)
            disposable.Dispose();
        _profile = DADeploymentProfile.Development;
        _writer = WrapWithMetrics(writer);
        _mode = writer.Mode;
        _reader = CreateDevelopmentReader(writer);
        _writerOverridden = true;
        _productionBackendOverridden = false;
    }

    /// <summary>
    /// Inject a production writer and independently operated reader. Both components must
    /// explicitly opt in to the production contracts and agree on mode and receipt kind.
    /// </summary>
    public void WithProductionBackend(IProductionDAWriter writer, IProductionDAReader reader)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(reader);
        ValidateProductionBackend(writer.Mode, writer, reader);
        var previous = Unwrap(_writer);
        if (!ReferenceEquals(previous, writer) && previous is IDisposable disposable)
            disposable.Dispose();
        _profile = DADeploymentProfile.Production;
        _writer = WrapWithMetrics(writer);
        _reader = new ValidatingDAReader(reader);
        _mode = writer.Mode;
        _writerOverridden = true;
        _productionBackendOverridden = true;
    }

    /// <summary>
    /// Validate a raw DAMode byte against the defined public modes plus the local sentinel. Without this an operator
    /// who misconfigures <c>DAMode = 99</c> would silently fall through to the in-memory
    /// writer and lose real DA delivery — by the time something downstream notices, the
    /// batch data is gone.
    /// </summary>
    public static DAMode ResolveDAMode(byte raw)
    {
        var mode = (DAMode)raw;
        if (mode is not (DAMode.L1 or DAMode.NeoFS or DAMode.External or DAMode.DAC or DAMode.Local))
            throw new InvalidOperationException(
                $"DAMode {raw} is not one of L1(0), NeoFS(1), External(2), DAC(3), Local(255) — fix config");
        return mode;
    }

    /// <summary>
    /// Parse the profile. Omission is development only for the local sentinel; omission
    /// with any public DA mode is treated as production and therefore fails closed.
    /// </summary>
    public static DADeploymentProfile ResolveProfile(string? raw, DAMode configuredMode)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return configuredMode == DAMode.Local
                ? DADeploymentProfile.Development
                : DADeploymentProfile.Production;
        if (!Enum.TryParse<DADeploymentProfile>(raw, ignoreCase: true, out var profile)
            || !Enum.IsDefined(profile))
        {
            throw new InvalidOperationException(
                $"DA Profile '{raw}' is not Development or Production — fix config");
        }

        return profile;
    }

    /// <summary>Validate the production writer/reader trust boundary.</summary>
    public static void ValidateProductionBackend(
        DAMode configuredMode,
        IProductionDAWriter writer,
        IProductionDAReader reader)
        => ValidateConfiguredBackend(
            DADeploymentProfile.Production,
            configuredMode,
            writer,
            reader,
            productionBackendOverridden: true);

    /// <summary>
    /// Get the writer (used by other plugins / tests; in tests you may inject a mock by
    /// reassigning).
    /// </summary>
    public IDAWriter GetWriter() => _writer;

    /// <summary>Get the independently configured or development reader.</summary>
    public IDAReader GetReader()
        => _reader ?? throw new InvalidOperationException(
            $"DA backend {_writer.GetType().Name} does not provide a reader");

    private IDAWriter WrapWithMetrics(IDAWriter raw)
    {
        IDAWriter writer = _profile == DADeploymentProfile.Production
            ? new ValidatingDAWriter(raw)
            : raw;
        if (ReferenceEquals(_metrics, NoOpMetrics.Instance)) return writer;
        return new MetricsEmittingDAWriter(writer, _metrics);
    }

    private static IDAWriter Unwrap(IDAWriter writer)
    {
        while (true)
        {
            switch (writer)
            {
                case MetricsEmittingDAWriter metrics:
                    writer = metrics.Inner;
                    continue;
                case ValidatingDAWriter validating:
                    writer = validating.Inner;
                    continue;
                default:
                    return writer;
            }
        }
    }

    private static IDAReader? Unwrap(IDAReader? reader)
        => reader is ValidatingDAReader validating ? validating.Inner : reader;

    private static IDAReader? CreateDevelopmentReader(IDAWriter writer)
        => writer switch
        {
            InMemoryDAWriter local => local.CreateReader(),
            PersistentDAWriter persistent => persistent.CreateReader(),
            NeoFsLikeDAWriter neoFs => neoFs.CreateReader(),
            CommitteeAttestedDAWriter dac => dac.CreateReader(),
            _ => null,
        };

    private static void ValidateConfiguredBackend(
        DADeploymentProfile profile,
        DAMode configuredMode,
        IDAWriter writer,
        IDAReader? reader,
        bool productionBackendOverridden)
    {
        if (writer.Mode != configuredMode)
            throw new InvalidOperationException(
                $"Configured DAMode {configuredMode} does not match writer mode {writer.Mode}");

        if (profile != DADeploymentProfile.Production) return;
        if (!configuredMode.IsPublic())
            throw new InvalidOperationException("Production DA profile cannot use DAMode.Local");
        if (!productionBackendOverridden || writer is not IProductionDAWriter)
            throw new InvalidOperationException(
                "Production DA profile requires WithProductionBackend and an IProductionDAWriter");
        if (reader is not IProductionDAReader)
            throw new InvalidOperationException(
                "Production DA profile requires an independently operated IProductionDAReader");
        if (ReferenceEquals(writer, reader))
            throw new InvalidOperationException(
                "Production DA writer and reader must be distinct component instances");
        if (reader.Mode != configuredMode)
            throw new InvalidOperationException(
                $"Configured DAMode {configuredMode} does not match reader mode {reader.Mode}");

        var expectedKind = configuredMode switch
        {
            DAMode.L1 => DAReceiptKind.L1Transaction,
            DAMode.NeoFS => DAReceiptKind.NeoFSObject,
            DAMode.External => DAReceiptKind.ExternalPublication,
            DAMode.DAC => DAReceiptKind.DACAttestation,
            _ => throw new InvalidOperationException($"DAMode {configuredMode} is not public"),
        };
        if (writer.ReceiptKind != expectedKind || reader.ReceiptKind != expectedKind)
            throw new InvalidOperationException(
                $"Production {configuredMode} backend must use receipt kind {expectedKind}; writer={writer.ReceiptKind}, reader={reader.ReceiptKind}");
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && Unwrap(_writer) is IDisposable d)
            d.Dispose();
        base.Dispose(disposing);
    }
}
