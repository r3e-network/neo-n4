using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Neo.L2.Telemetry;

namespace Neo.Plugins.L2;

/// <summary>Configuration for <see cref="L2MetricsPlugin"/>.</summary>
public sealed class L2MetricsSettings
{
    /// <summary>
    /// Relative path of the metrics plugin config under a chain working directory
    /// (written by deploy-report materialization / host composition).
    /// </summary>
    public const string RelativePluginConfigPath =
        "Plugins/Neo.Plugins.L2Metrics/config.json";

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
    /// Load settings from a metrics plugin <c>config.json</c>
    /// (<c>{ "PluginConfiguration": { ... } }</c>).
    /// </summary>
    public static L2MetricsSettings FromPluginConfigFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = System.IO.Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("metrics plugin config not found", fullPath);

        using var document = JsonDocument.Parse(File.ReadAllText(fullPath));
        if (!document.RootElement.TryGetProperty("PluginConfiguration", out var plugin)
            || plugin.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException(
                $"{fullPath} is missing a PluginConfiguration object");

        var pairs = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in plugin.EnumerateObject())
            pairs["PluginConfiguration:" + property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => null,
                _ => property.Value.GetRawText(),
            };

        var root = new ConfigurationBuilder()
            .AddInMemoryCollection(pairs)
            .Build();
        return From(root.GetSection("PluginConfiguration"));
    }

    /// <summary>
    /// Load settings from a chain working directory (top-level, node/, batcher-node/ variants).
    /// </summary>
    public static L2MetricsSettings FromChainDirectory(string chainDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainDirectory);
        var root = System.IO.Path.GetFullPath(chainDirectory);
        var candidates = new[]
        {
            System.IO.Path.Combine(root, "Plugins", "Neo.Plugins.L2Metrics", "config.json"),
            System.IO.Path.Combine(root, "node", "Plugins", "Neo.Plugins.L2Metrics", "config.json"),
            System.IO.Path.Combine(root, "batcher-node", "Plugins", "Neo.Plugins.L2Metrics", "config.json"),
        };
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return FromPluginConfigFile(candidate);
        }
        throw new FileNotFoundException(
            "metrics plugin config not found under chain directory "
            + $"(expected {RelativePluginConfigPath} or node/batcher-node variants)",
            System.IO.Path.Combine(root, RelativePluginConfigPath));
    }

    /// <summary>
    /// Validate a port value at config-parse time. Delegates to
    /// <see cref="Neo.L2.Telemetry.PortValidator.Validate(int, string)"/> so CLI tools
    /// (devnet runner, etc.) can reuse the same check without taking a plugin dependency.
    /// </summary>
    public static int ValidatePort(int port) =>
        Neo.L2.Telemetry.PortValidator.Validate(port, "L2Metrics Port");
}
