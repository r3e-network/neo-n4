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
            MaxBlocksPerBatch = ValidatePositive(section.GetValue("MaxBlocksPerBatch", 50), "MaxBlocksPerBatch"),
            MaxTransactionsPerBatch = ValidatePositive(section.GetValue("MaxTransactionsPerBatch", 5_000), "MaxTransactionsPerBatch"),
            MaxBatchAgeMillis = ValidatePositive(section.GetValue("MaxBatchAgeMillis", 30_000), "MaxBatchAgeMillis"),
            Enabled = section.GetValue("Enabled", true),
        };
    }

    /// <summary>
    /// Reject zero or negative thresholds at config-parse time. Without this, a misconfigured
    /// <c>MaxBlocksPerBatch: 0</c> (or any other Max* set to 0/negative) makes
    /// <see cref="BatchSealer.ShouldSeal"/> return <c>true</c> on every block — every block
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
