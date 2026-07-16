using System.Globalization;
using System.Text.Json;

namespace Neo.L2;

/// <summary>
/// L1 JSON-RPC endpoint and core NeoHub contract hashes resolved from a chain
/// working directory (settlement plugin config and/or <c>l1.deployed.json</c>).
/// </summary>
/// <remarks>
/// See doc.md §3.2 / §14.2. Used by Gateway publication and proof-bound publisher
/// host composition so operators do not re-type hashes after
/// <c>init-l2 --from-deploy-report</c>.
/// </remarks>
public sealed record L1DeployedEndpoints(
    Uri RpcEndpoint,
    UInt160 SettlementManager,
    UInt160 MessageRouter,
    uint? ExpectedNetwork)
{
    /// <summary>
    /// Resolve L1 RPC + SettlementManager + MessageRouter from settlement plugin
    /// config and/or <c>l1.deployed.json</c> under <paramref name="chainDirectory"/>.
    /// </summary>
    /// <remarks>
    /// Settlement plugin config is preferred when present; missing fields fall back to
    /// <c>l1.deployed.json</c>. Both SettlementManager and MessageRouter must be non-zero.
    /// </remarks>
    public static L1DeployedEndpoints FromChainDirectory(string chainDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainDirectory);
        var root = Path.GetFullPath(chainDirectory);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(
                $"Chain directory not found: {root}. Run neo-stack init-l2 first.");

        string? rpc = null;
        string? settlementManager = null;
        string? messageRouter = null;
        uint? expectedNetwork = null;

        foreach (var configPath in SettlementConfigCandidates(root))
        {
            if (!File.Exists(configPath)) continue;
            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            if (!doc.RootElement.TryGetProperty("PluginConfiguration", out var cfg))
                continue;
            rpc ??= ReadString(cfg, "L1RpcEndpoint");
            settlementManager ??= ReadString(cfg, "SettlementManagerHash");
            messageRouter ??= ReadString(cfg, "MessageRouterHash");
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
            settlementManager ??= ReadString(rootEl, "settlementManager");
            messageRouter ??= ReadString(rootEl, "messageRouter");
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
        if (string.IsNullOrWhiteSpace(settlementManager)
            || !UInt160.TryParse(settlementManager, out var sm)
            || sm.Equals(UInt160.Zero))
        {
            throw new InvalidDataException(
                "SettlementManager hash missing or zero (settlement SettlementManagerHash "
                + "or l1.deployed.json settlementManager)");
        }
        if (string.IsNullOrWhiteSpace(messageRouter)
            || !UInt160.TryParse(messageRouter, out var mr)
            || mr.Equals(UInt160.Zero))
        {
            throw new InvalidDataException(
                "MessageRouter hash missing or zero (settlement MessageRouterHash "
                + "or l1.deployed.json messageRouter)");
        }

        return new L1DeployedEndpoints(endpoint, sm, mr, expectedNetwork);
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
