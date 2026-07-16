using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Neo.Plugins.L2Gateway;

/// <summary>Configuration for <see cref="L2GatewayPlugin"/>.</summary>
/// <remarks>See doc.md §4 (Neo Gateway).</remarks>
public sealed class L2GatewaySettings
{
    /// <summary>
    /// Relative path of the gateway plugin config under a chain working directory
    /// (written by deploy-report materialization).
    /// </summary>
    public const string RelativePluginConfigPath =
        "Plugins/Neo.Plugins.L2Gateway/config.json";

    /// <summary>Master kill switch.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Automatic publication retries before poison; must be in [1, 100].</summary>
    public int MaxAutomaticRetries { get; init; } = 3;

    /// <summary>Build settings from the plugin's <c>PluginConfiguration</c> section.</summary>
    public static L2GatewaySettings From(IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        var maxRetries = section.GetValue("MaxAutomaticRetries", 3);
        if (maxRetries is < 1 or > 100)
            throw new InvalidDataException(
                $"L2Gateway MaxAutomaticRetries must be in [1, 100] (got {maxRetries})");
        return new L2GatewaySettings
        {
            Enabled = section.GetValue("Enabled", true),
            MaxAutomaticRetries = maxRetries,
        };
    }

    /// <summary>Load settings from a gateway plugin <c>config.json</c> file.</summary>
    public static L2GatewaySettings FromPluginConfigFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = System.IO.Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("gateway plugin config not found", fullPath);

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
    public static L2GatewaySettings FromChainDirectory(string chainDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainDirectory);
        var root = System.IO.Path.GetFullPath(chainDirectory);
        var candidates = new[]
        {
            System.IO.Path.Combine(root, "Plugins", "Neo.Plugins.L2Gateway", "config.json"),
            System.IO.Path.Combine(root, "node", "Plugins", "Neo.Plugins.L2Gateway", "config.json"),
            System.IO.Path.Combine(root, "batcher-node", "Plugins", "Neo.Plugins.L2Gateway", "config.json"),
        };
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return FromPluginConfigFile(candidate);
        }
        throw new FileNotFoundException(
            "gateway plugin config not found under chain directory "
            + $"(expected {RelativePluginConfigPath} or node/batcher-node variants)",
            System.IO.Path.Combine(root, RelativePluginConfigPath));
    }
}
