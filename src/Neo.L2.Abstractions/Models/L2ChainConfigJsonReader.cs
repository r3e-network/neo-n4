using System.Text.Json;

namespace Neo.L2;

/// <summary>
/// Parse the JSON shape that <c>neo-stack create-chain</c> writes to <c>chain.config.json</c>
/// + the four operator-supplied L1 contract hashes (UInt160 hex) into a fully-populated
/// <see cref="L2ChainConfig"/> record ready to feed
/// <see cref="L2ChainConfigSerializer.Encode(L2ChainConfig)"/>.
/// </summary>
/// <remarks>
/// Lives in <c>Neo.L2.Abstractions</c> (not the CLI) so the parser is unit-testable
/// without spawning a CLI process. Errors throw <see cref="ArgumentException"/> naming
/// the field that's missing or invalid — operators see "chain.config.json
/// 'securityLevel'='Foo' is not a valid SecurityLevel" instead of a generic
/// JsonException with no field hint.
/// </remarks>
public static class L2ChainConfigJsonReader
{
    /// <summary>
    /// Build an <see cref="L2ChainConfig"/> from the create-chain JSON + four UInt160
    /// hex hashes. The chain is assumed active (<c>Active = true</c>); operators
    /// pause/resume via <c>ChainRegistry.PauseChain</c> after registration.
    /// </summary>
    /// <param name="chainId">Chain id (must match the JSON's <c>chainId</c> field; the
    /// CLI passes its own validated chainId here).</param>
    /// <param name="json">The full chain.config.json string.</param>
    /// <param name="operatorHash">UInt160 hex (with or without 0x prefix, 40 hex chars).</param>
    /// <param name="verifierHash">UInt160 hex.</param>
    /// <param name="bridgeHash">UInt160 hex.</param>
    /// <param name="messageHash">UInt160 hex.</param>
    public static L2ChainConfig FromJson(uint chainId, string json,
        string operatorHash, string verifierHash, string bridgeHash, string messageHash)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var daMode = ParseEnum<DAMode>(root, "daMode");
        if (!daMode.IsPublic())
            throw new ArgumentException(
                $"chain.config.json 'daMode'='{daMode}' is local-only and cannot be registered");

        return new L2ChainConfig
        {
            ChainId = chainId,
            OperatorManager = ParseHash(operatorHash, "--operator"),
            Verifier = ParseHash(verifierHash, "--verifier"),
            BridgeAdapter = ParseHash(bridgeHash, "--bridge"),
            MessageAdapter = ParseHash(messageHash, "--message"),
            SecurityLevel = ParseEnum<SecurityLevel>(root, "securityLevel"),
            DAMode = daMode,
            GatewayEnabled = ParseBool(root, "gatewayEnabled"),
            PermissionlessExit = ParseBool(root, "permissionlessExit"),
            Sequencer = ParseEnum<SequencerModel>(root, "sequencerModel"),
            Exit = ParseEnum<ExitModel>(root, "exitModel"),
            Active = true,
        };
    }

    private static T ParseEnum<T>(JsonElement root, string field) where T : struct, Enum
    {
        if (!root.TryGetProperty(field, out var prop))
            throw new ArgumentException($"chain.config.json missing '{field}'");
        var name = prop.GetString();
        if (!Enum.TryParse<T>(name, ignoreCase: false, out var value))
            throw new ArgumentException($"chain.config.json '{field}'='{name}' is not a valid {typeof(T).Name}");
        return value;
    }

    private static bool ParseBool(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var prop))
            throw new ArgumentException($"chain.config.json missing '{field}'");
        return prop.GetBoolean();
    }

    private static UInt160 ParseHash(string hex, string flag)
    {
        ArgumentNullException.ThrowIfNull(hex);
        try
        {
            return UInt160.Parse(hex);
        }
        catch (Exception ex)
        {
            throw new ArgumentException(
                $"{flag}='{hex}' is not a valid UInt160 (expected 0x + 40 hex chars): {ex.Message}");
        }
    }
}
