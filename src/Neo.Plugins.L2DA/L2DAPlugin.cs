using Microsoft.Extensions.Configuration;
using Neo.L2;

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

    /// <inheritdoc />
    public override string Name => "L2DAPlugin";

    /// <inheritdoc />
    public override string Description => "Selects the DA writer (L1 / NeoFS / External / DAC) per chain configuration.";

    /// <summary>The currently active writer.</summary>
    public IDAWriter Writer => _writer;

    /// <inheritdoc />
    protected override void Configure()
    {
        var section = GetConfiguration();
        _mode = (DAMode)section.GetValue<byte>("DAMode", (byte)DAMode.External);
        _writer = _mode switch
        {
            DAMode.L1 => new L1DAWriter(),
            DAMode.NeoFS => new NeoFSDAWriter(),
            DAMode.External => new ExternalDAWriter(),
            DAMode.DAC => new DACDAWriter(),
            _ => new InMemoryDAWriter(),
        };
    }

    /// <summary>
    /// Get the writer (used by other plugins / tests; in tests you may inject a mock by
    /// reassigning).
    /// </summary>
    public IDAWriter GetWriter() => _writer;
}
