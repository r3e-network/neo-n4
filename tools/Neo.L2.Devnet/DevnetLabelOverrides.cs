using System;
using System.IO;

namespace Neo.L2.Devnet;

/// <summary>
/// §16.2 security-label dimensions read from an operator's <c>chain.config.json</c>
/// (typically a <c>neo-stack create-chain --template &lt;X&gt;</c> output) and applied
/// to the devnet's <c>Neo.Plugins.L2.InMemoryL2RpcStore</c>. Defaults match the
/// NeoFS-backed defaults; operators supply <c>--config</c> to preview a
/// template-specific label end-to-end.
/// </summary>
public readonly record struct DevnetLabelOverrides(
    SecurityLevel SecurityLevel,
    DAMode DAMode,
    bool GatewayEnabled,
    SequencerModel Sequencer,
    ExitModel Exit)
{
    /// <summary>Devnet defaults (matches <c>InMemoryL2RpcStore</c> sane defaults — Optimistic / NeoFS / DbftCommittee / Permissionless / gateway off).</summary>
    public static DevnetLabelOverrides Defaults { get; } = new(
        SecurityLevel.Optimistic, DAMode.NeoFS, false,
        SequencerModel.DbftCommittee, ExitModel.Permissionless);

    /// <summary>
    /// Read the §16.2 label overrides from <paramref name="configPath"/>. When
    /// <paramref name="configPath"/> is null, returns <see cref="Defaults"/>. When the
    /// file doesn't exist or is malformed, emits a warning to <c>Console.Error</c> +
    /// returns <see cref="Defaults"/> — devnet should still run rather than abort, so
    /// an operator hits a clear "falling back" diagnostic instead of a stack trace.
    /// </summary>
    public static DevnetLabelOverrides ReadFromConfig(string? configPath)
    {
        if (configPath is null) return Defaults;

        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine($"--config '{configPath}' not found; falling back to defaults");
            return Defaults;
        }
        try
        {
            var json = File.ReadAllText(configPath);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Each field is optional — missing or invalid → use the default for that
            // dimension. ParseEnumOrDefault is the per-field fallback.
            return new DevnetLabelOverrides(
                ParseEnumOrDefault(root, "securityLevel", Defaults.SecurityLevel),
                ParseEnumOrDefault(root, "daMode", Defaults.DAMode),
                root.TryGetProperty("gatewayEnabled", out var ge) && ge.GetBoolean(),
                ParseEnumOrDefault(root, "sequencerModel", Defaults.Sequencer),
                ParseEnumOrDefault(root, "exitModel", Defaults.Exit));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"--config parse failed ({ex.Message}); falling back to defaults");
            return Defaults;
        }
    }

    /// <summary>
    /// Per-field enum parser: missing field → fallback; field present but value
    /// doesn't parse → fallback. Matches the legacy devnet's permissive-fallback
    /// semantics — operators get the warning above, not a hard abort, so partial
    /// configs still let the devnet run.
    /// </summary>
    public static T ParseEnumOrDefault<T>(System.Text.Json.JsonElement root, string field, T fallback)
        where T : struct, Enum
    {
        if (!root.TryGetProperty(field, out var prop)) return fallback;
        var name = prop.GetString();
        return Enum.TryParse<T>(name, ignoreCase: false, out var value) ? value : fallback;
    }
}
