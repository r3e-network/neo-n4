using Microsoft.Extensions.Configuration;
using Neo.L2.Telemetry;

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

    /// <summary>
    /// Hard cap on simultaneous accepted scrapes / probes. Excess clients receive HTTP 503.
    /// Default matches <see cref="MetricsHttpServer.DefaultMaxConcurrentConnections"/>.
    /// </summary>
    public int MaxConcurrentConnections { get; init; } = MetricsHttpServer.DefaultMaxConcurrentConnections;

    /// <summary>Build settings from the plugin's <c>PluginConfiguration</c> section.</summary>
    public static L2MetricsSettings From(IConfigurationSection s)
    {
        var maxConnections = s.GetValue(
            "MaxConcurrentConnections",
            MetricsHttpServer.DefaultMaxConcurrentConnections);
        if (maxConnections < 1)
            throw new InvalidDataException(
                $"L2Metrics MaxConcurrentConnections must be >= 1 (got {maxConnections})");

        return new L2MetricsSettings
        {
            Enabled = s.GetValue("Enabled", true),
            BindAddress = s.GetValue("BindAddress", "127.0.0.1")!,
            Port = ValidatePort(s.GetValue("Port", 9090)),
            MaxConcurrentConnections = maxConnections,
        };
    }

    /// <summary>
    /// Validate a port value at config-parse time. Delegates to
    /// <see cref="Neo.L2.Telemetry.PortValidator.Validate(int, string)"/> so CLI tools
    /// (devnet runner, etc.) can reuse the same check without taking a plugin dependency.
    /// </summary>
    public static int ValidatePort(int port) =>
        Neo.L2.Telemetry.PortValidator.Validate(port, "L2Metrics Port");
}
