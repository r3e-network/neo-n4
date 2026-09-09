using System.Globalization;
using System.Text.Json;

namespace Neo.L2;

/// <summary>
/// L1 JSON-RPC endpoint and core NeoHub contract hashes resolved from a chain
/// working directory (settlement plugin config and/or <c>l1.deployed.json</c>).
/// </summary>
/// <remarks>
/// See doc.md §4 (Neo Gateway) and §5. Preferred keys are RollupHub + SharedBridge
/// (lean pillars). Legacy SettlementManager / MessageRouter names remain accepted
/// aliases for the same slots.
/// </remarks>
public sealed record L1DeployedEndpoints(
    Uri RpcEndpoint,
    UInt160 SettlementManager,
    UInt160 MessageRouter,
    uint? ExpectedNetwork)
{
    /// <summary>Lean RollupHub hash (preferred name for <see cref="SettlementManager"/>).</summary>
    public UInt160 RollupHub => SettlementManager;

    /// <summary>Lean SharedBridge hash (preferred name for <see cref="MessageRouter"/>).</summary>
    public UInt160 SharedBridge => MessageRouter;

    /// <summary>
    /// Resolve L1 RPC + RollupHub + SharedBridge from settlement plugin
    /// config and/or <c>l1.deployed.json</c> under <paramref name="chainDirectory"/>.
    /// </summary>
    /// <remarks>
    /// Settlement plugin config is preferred when present; missing fields fall back to
    /// <c>l1.deployed.json</c>. Both RollupHub (or SettlementManager alias) and SharedBridge
    /// (or MessageRouter alias) must be non-zero.
    /// </remarks>
    public static L1DeployedEndpoints FromChainDirectory(string chainDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainDirectory);
        var root = Path.GetFullPath(chainDirectory);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(
                $"Chain directory not found: {root}. Run neo-stack init-l2 first.");

        string? rpc = null;
        string? rollupHub = null;
        string? sharedBridge = null;
        uint? expectedNetwork = null;

        foreach (var configPath in SettlementConfigCandidates(root))
        {
            if (!File.Exists(configPath)) continue;
            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            if (!doc.RootElement.TryGetProperty("PluginConfiguration", out var cfg))
                continue;
            rpc ??= ReadString(cfg, "L1RpcEndpoint");
            rollupHub ??= ReadString(cfg, "RollupHubHash")
                ?? ReadString(cfg, "SettlementManagerHash");
            sharedBridge ??= ReadString(cfg, "SharedBridgeHash")
                ?? ReadString(cfg, "MessageRouterHash");
            if (expectedNetwork is null
                && cfg.TryGetProperty("ExpectedNetwork", out var netEl)
                && TryReadUInt32(netEl, out var net))
            {
                expectedNetwork = net;
            }
            break;
        }

        var deployedPath = Path.Combine(root, "l1.deployed.json");
        if (File.Exists(deployedPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(deployedPath));
            var rootEl = doc.RootElement;
            rpc ??= ReadString(rootEl, "rpc");
            rollupHub ??= ReadString(rootEl, "rollupHub")
                ?? ReadString(rootEl, "settlementManager");
            sharedBridge ??= ReadString(rootEl, "sharedBridge")
                ?? ReadString(rootEl, "messageRouter");
            if (expectedNetwork is null
                && rootEl.TryGetProperty("network", out var netEl)
                && TryReadUInt32(netEl, out var net))
            {
                expectedNetwork = net;
            }
        }

        if (string.IsNullOrWhiteSpace(rpc)
            || !Uri.TryCreate(rpc, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("http" or "https"))
        {
            throw new InvalidDataException(
                "L1 RPC endpoint missing or invalid (settlement PluginConfiguration.L1RpcEndpoint "
                + "or l1.deployed.json rpc)");
        }
        if (string.IsNullOrWhiteSpace(rollupHub)
            || !UInt160.TryParse(rollupHub, out var hub)
            || hub.Equals(UInt160.Zero))
        {
            throw new InvalidDataException(
                "RollupHub hash missing or zero (settlement RollupHubHash/SettlementManagerHash "
                + "or l1.deployed.json rollupHub/settlementManager)");
        }
        if (string.IsNullOrWhiteSpace(sharedBridge)
            || !UInt160.TryParse(sharedBridge, out var bridge)
            || bridge.Equals(UInt160.Zero))
        {
            throw new InvalidDataException(
                "SharedBridge hash missing or zero (settlement SharedBridgeHash/MessageRouterHash "
                + "or l1.deployed.json sharedBridge/messageRouter)");
        }

        return new L1DeployedEndpoints(endpoint, hub, bridge, expectedNetwork);
    }

    private static IEnumerable<string> SettlementConfigCandidates(string root) =>
    [
        Path.Combine(root, "Plugins", "Neo.Plugins.L2Settlement", "config.json"),
        Path.Combine(root, "node", "Plugins", "Neo.Plugins.L2Settlement", "config.json"),
        Path.Combine(root, "batcher-node", "Plugins", "Neo.Plugins.L2Settlement", "config.json"),
    ];

    private static string? ReadString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.GetRawText(),
            _ => null,
        };
    }

    private static bool TryReadUInt32(JsonElement el, out uint value)
    {
        value = 0;
        return el.ValueKind switch
        {
            JsonValueKind.Number => el.TryGetUInt32(out value),
            JsonValueKind.String => uint.TryParse(
                el.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value),
            _ => false,
        };
    }
}
