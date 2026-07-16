using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Neo.Plugins.L2;

/// <summary>
/// Configuration for <see cref="L2BatchPlugin"/>. Mirrors <c>config.json</c>:
/// <code>
/// {
///   "PluginConfiguration": {
///     "ChainId": 1001,
///     "MaxBlocksPerBatch": 50,
///     "MaxTransactionsPerBatch": 5000,
///     "MaxBatchAgeMillis": 30000,
///     "Enabled": true
///   }
/// }
/// </code>
/// </summary>
public sealed class L2BatchSettings
{
    /// <summary>
    /// Relative path of the batch plugin config under a chain working directory
    /// (written by <c>init-l2 --from-deploy-report</c> / <c>register-chain --from-deploy-report</c>).
    /// </summary>
    public const string RelativePluginConfigPath =
        "Plugins/Neo.Plugins.L2Batch/config.json";

    /// <summary>L2 chain identifier this node serves.</summary>
    public uint ChainId { get; init; }

    /// <summary>Seal a batch when it has accumulated this many L2 blocks.</summary>
    public int MaxBlocksPerBatch { get; init; } = 50;

    /// <summary>Seal a batch when it has accumulated this many transactions.</summary>
    public int MaxTransactionsPerBatch { get; init; } = 5_000;

    /// <summary>Force-seal a batch after this many milliseconds even if it has not hit other limits.</summary>
    public int MaxBatchAgeMillis { get; init; } = 30_000;

    /// <summary>Master kill switch. When false the plugin loads but does nothing.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Build settings from the plugin's <c>PluginConfiguration</c> section.</summary>
    public static L2BatchSettings From(IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        // Distinguish "key missing" (test mode, no config supplied — leave ChainId at 0)
        // from "explicitly set to 0" (operator misconfig — reject). The nullable read
        // returns null for the former and 0 for the latter; we only validate the second.
        var rawChainId = section.GetValue<uint?>("ChainId");
        return new L2BatchSettings
        {
            ChainId = rawChainId is null ? 0u : Neo.L2.ChainIdValidator.ValidateL2(rawChainId.Value),
            MaxBlocksPerBatch = ValidatePositive(section.GetValue("MaxBlocksPerBatch", 50), "MaxBlocksPerBatch"),
            MaxTransactionsPerBatch = ValidatePositive(section.GetValue("MaxTransactionsPerBatch", 5_000), "MaxTransactionsPerBatch"),
            MaxBatchAgeMillis = ValidatePositive(section.GetValue("MaxBatchAgeMillis", 30_000), "MaxBatchAgeMillis"),
            Enabled = section.GetValue("Enabled", true),
        };
    }

    /// <summary>
    /// Load settings from a batch plugin <c>config.json</c> file
    /// (<c>{ "PluginConfiguration": { ... } }</c>).
    /// </summary>
    public static L2BatchSettings FromPluginConfigFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("batch plugin config not found", fullPath);

        using var document = JsonDocument.Parse(File.ReadAllText(fullPath));
        if (!document.RootElement.TryGetProperty("PluginConfiguration", out var plugin)
            || plugin.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException(
                $"{fullPath} is missing a PluginConfiguration object");

        var pairs = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in plugin.EnumerateObject())
            pairs["PluginConfiguration:" + property.Name] = JsonValueToConfigString(property.Value);

        var root = new ConfigurationBuilder()
            .AddInMemoryCollection(pairs)
            .Build();
        return From(root.GetSection("PluginConfiguration"));
    }

    /// <summary>
    /// Load settings from a chain working directory written by <c>init-l2</c> /
    /// <c>--from-deploy-report</c>. Tries top-level, <c>node/</c>, then <c>batcher-node/</c>
    /// plugin config paths.
    /// </summary>
    public static L2BatchSettings FromChainDirectory(string chainDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainDirectory);
        var root = Path.GetFullPath(chainDirectory);
        var candidates = new[]
        {
            Path.Combine(root, "Plugins", "Neo.Plugins.L2Batch", "config.json"),
            Path.Combine(root, "node", "Plugins", "Neo.Plugins.L2Batch", "config.json"),
            Path.Combine(root, "batcher-node", "Plugins", "Neo.Plugins.L2Batch", "config.json"),
        };
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return FromPluginConfigFile(candidate);
        }
        throw new FileNotFoundException(
            "batch plugin config not found under chain directory "
            + $"(expected {RelativePluginConfigPath} or node/batcher-node variants)",
            Path.Combine(root, RelativePluginConfigPath));
    }

    private static string? JsonValueToConfigString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => null,
        _ => value.GetRawText(),
    };

    /// <summary>
    /// Reject zero or negative thresholds at config-parse time. Without this, a misconfigured
    /// <c>MaxBlocksPerBatch: 0</c> (or any other Max* set to 0/negative) makes
    /// <c>BatchSealer.ShouldSeal</c> (private) return <c>true</c> on every block — every block
    /// becomes its own batch, producing degenerate per-block batches that each carry full
    /// settlement / proving overhead. The operator's misconfig surfaces as a runaway L1
    /// submission rate hours later instead of at plugin load.
    /// </summary>
    public static int ValidatePositive(int value, string name)
    {
        if (value <= 0)
            throw new InvalidDataException(
                $"L2Batch {name} must be > 0, got {value} — fix config");
        return value;
    }
}
