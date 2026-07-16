using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Neo.L2;

namespace Neo.Plugins.L2;

/// <summary>Configuration for <see cref="L2BridgePlugin"/>.</summary>
/// <remarks>See doc.md §13.1 (L2BridgeContract) and §15.2 / §15.3.</remarks>
public sealed class L2BridgeSettings
{
    /// <summary>
    /// Relative path of the bridge plugin config under a chain working directory
    /// (written by deploy-report materialization).
    /// </summary>
    public const string RelativePluginConfigPath =
        "Plugins/Neo.Plugins.L2Bridge/config.json";

    /// <summary>L2 chain identifier this bridge serves.</summary>
    public uint ChainId { get; init; }

    /// <summary>Build settings from the plugin's <c>PluginConfiguration</c> section.</summary>
    public static L2BridgeSettings From(IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        var rawChainId = section.GetValue<uint?>("ChainId");
        return new L2BridgeSettings
        {
            ChainId = rawChainId is null ? 0u : ChainIdValidator.ValidateL2(rawChainId.Value),
        };
    }

    /// <summary>Load settings from a bridge plugin <c>config.json</c> file.</summary>
    public static L2BridgeSettings FromPluginConfigFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("bridge plugin config not found", fullPath);

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
    /// Load settings from a chain working directory (top-level, node/, batcher-node variants).
    /// </summary>
    public static L2BridgeSettings FromChainDirectory(string chainDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainDirectory);
        var root = Path.GetFullPath(chainDirectory);
        var candidates = new[]
        {
            Path.Combine(root, "Plugins", "Neo.Plugins.L2Bridge", "config.json"),
            Path.Combine(root, "node", "Plugins", "Neo.Plugins.L2Bridge", "config.json"),
            Path.Combine(root, "batcher-node", "Plugins", "Neo.Plugins.L2Bridge", "config.json"),
        };
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return FromPluginConfigFile(candidate);
        }
        throw new FileNotFoundException(
            "bridge plugin config not found under chain directory "
            + $"(expected {RelativePluginConfigPath} or node/batcher-node variants)",
            Path.Combine(root, RelativePluginConfigPath));
    }
}
