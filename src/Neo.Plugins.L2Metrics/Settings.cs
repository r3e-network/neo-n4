using Microsoft.Extensions.Configuration;

namespace Neo.Plugins.L2;

/// <summary>Configuration for <see cref="L2MetricsPlugin"/>.</summary>
public sealed class L2MetricsSettings
{
    /// <summary>Master kill switch. When false, the plugin loads but does not start the HTTP server.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>IP address the HTTP server binds to. Default <c>127.0.0.1</c> — front with a reverse proxy for external access.</summary>
    public string BindAddress { get; init; } = "127.0.0.1";

    /// <summary>TCP port. <c>0</c> picks any free port (useful for tests / dev).</summary>
    public int Port { get; init; } = 9090;

    /// <summary>Build settings from the plugin's <c>PluginConfiguration</c> section.</summary>
    public static L2MetricsSettings From(IConfigurationSection s)
    {
        return new L2MetricsSettings
        {
            Enabled = s.GetValue("Enabled", true),
            BindAddress = s.GetValue("BindAddress", "127.0.0.1")!,
            Port = ValidatePort(s.GetValue("Port", 9090)),
        };
    }

    /// <summary>
    /// Validate a port value at config-parse time. Without this a typo like
    /// <c>Port: 90909</c> propagates to <see cref="System.Net.IPEndPoint"/> construction
    /// at <c>Start()</c>; the resulting <see cref="ArgumentOutOfRangeException"/> there
    /// is real but the operator has to dig through the stack trace to map
    /// "value must be between 0..65535" back to a config typo.
    /// </summary>
    public static int ValidatePort(int port)
    {
        if (port < 0 || port > 65535)
            throw new InvalidDataException(
                $"L2Metrics Port {port} out of range — must be 0 (any free) or 1..65535");
        return port;
    }
}
