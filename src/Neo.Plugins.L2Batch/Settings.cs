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
        return new L2BatchSettings
        {
            ChainId = section.GetValue<uint>("ChainId"),
            MaxBlocksPerBatch = section.GetValue("MaxBlocksPerBatch", 50),
            MaxTransactionsPerBatch = section.GetValue("MaxTransactionsPerBatch", 5_000),
            MaxBatchAgeMillis = section.GetValue("MaxBatchAgeMillis", 30_000),
            Enabled = section.GetValue("Enabled", true),
        };
    }
}
