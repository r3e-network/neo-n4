using System.Text.Json;

namespace Neo.L2;

/// <summary>
/// Parsed snapshot of a <c>neo-hub-deploy deploy-testnet</c> evidence report
/// (<c>docs/audit/testnet-deployment-*.json</c>).
/// </summary>
/// <remarks>
/// See doc.md §3.2 / §14.2. Used by <c>neo-stack register-chain --from-deploy-report</c>
/// so operators do not hand-copy UInt160 hashes after a live deploy.
/// </remarks>
public sealed record NeoHubDeployReport(
    string Rpc,
    uint Network,
    string OwnerAddress,
    UInt160 OwnerScriptHash,
    uint L2ChainId,
    UInt160 ChainRegistry,
    UInt160 VerifierRegistry,
    UInt160 SharedBridge,
    UInt160 MessageRouter,
    UInt160 SettlementManager,
    UInt160 ForcedInclusion,
    IReadOnlyDictionary<string, UInt160> Contracts)
{
    /// <summary>
    /// Default <c>--operator</c> for register-chain: the deploy signer script hash
    /// (hot-wallet / multisig account that owns NeoHub contracts after deploy).
    /// </summary>
    public UInt160 DefaultOperatorManager => OwnerScriptHash;

    /// <summary>Load and validate a live (or dry-run) deploy report JSON file.</summary>
    public static NeoHubDeployReport Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            throw new FileNotFoundException("NeoHub deploy report not found", path);
        return Parse(File.ReadAllText(path));
    }

    /// <summary>Parse report JSON text into a typed snapshot.</summary>
    public static NeoHubDeployReport Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var rpc = RequireString(root, "rpc");
        var network = RequireUInt32(root, "network");
        var ownerAddress = RequireString(root, "ownerAddress");
        var ownerScriptHash = RequireHash(root, "ownerScriptHash");
        var l2ChainId = RequireUInt32(root, "l2ChainId");
        if (l2ChainId == 0)
            throw new ArgumentException("deploy report l2ChainId must be a non-zero L2 chain id");

        if (!root.TryGetProperty("records", out var records) || records.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("deploy report missing records[]");

        var contracts = new Dictionary<string, UInt160>(StringComparer.Ordinal);
        foreach (var record in records.EnumerateArray())
        {
            if (!record.TryGetProperty("category", out var category)
                || !string.Equals(category.GetString(), "deploy", StringComparison.Ordinal))
                continue;
            if (!record.TryGetProperty("status", out var status))
                continue;
            var statusText = status.GetString();
            if (!string.Equals(statusText, "deployed", StringComparison.Ordinal)
                && !string.Equals(statusText, "reused", StringComparison.Ordinal)
                && !string.Equals(statusText, "dry-run", StringComparison.Ordinal))
                continue;
            if (!record.TryGetProperty("name", out var nameEl)
                || string.IsNullOrWhiteSpace(nameEl.GetString()))
                throw new ArgumentException("deploy record missing name");
            var name = nameEl.GetString()!;
            if (!record.TryGetProperty("contractHash", out var hashEl)
                || string.IsNullOrWhiteSpace(hashEl.GetString()))
                throw new ArgumentException($"deploy record '{name}' missing contractHash");
            var hash = UInt160.Parse(hashEl.GetString()!);
            if (hash.Equals(UInt160.Zero))
                throw new ArgumentException($"deploy record '{name}' has a zero contractHash");
            contracts[name] = hash;
        }

        return new NeoHubDeployReport(
            rpc,
            network,
            ownerAddress,
            ownerScriptHash,
            l2ChainId,
            RequireContract(contracts, "ChainRegistry"),
            RequireContract(contracts, "VerifierRegistry"),
            RequireContract(contracts, "SharedBridge"),
            RequireContract(contracts, "MessageRouter"),
            RequireContract(contracts, "SettlementManager"),
            RequireContract(contracts, "ForcedInclusion"),
            contracts);
    }

    /// <summary>
    /// Write operator-facing artifacts under a chain directory: full hash map, settlement
    /// plugin config (WireProduction-ready), and batch plugin config. Also installs plugin
    /// configs under <c>node/Plugins</c> and <c>batcher-node/Plugins</c> when those roots
    /// exist (after <c>init-l2</c>).
    /// </summary>
    /// <returns>Relative paths written (for CLI reporting).</returns>
    public IReadOnlyList<string> WriteOperatorArtifacts(string chainDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainDirectory);
        Directory.CreateDirectory(chainDirectory);
        var written = new List<string>();
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

        var deployed = new Dictionary<string, object?>
        {
            ["rpc"] = Rpc,
            ["network"] = Network,
            ["ownerAddress"] = OwnerAddress,
            ["ownerScriptHash"] = OwnerScriptHash.ToString(),
            ["l2ChainId"] = L2ChainId,
            ["chainRegistry"] = ChainRegistry.ToString(),
            ["verifierRegistry"] = VerifierRegistry.ToString(),
            ["sharedBridge"] = SharedBridge.ToString(),
            ["messageRouter"] = MessageRouter.ToString(),
            ["settlementManager"] = SettlementManager.ToString(),
            ["forcedInclusion"] = ForcedInclusion.ToString(),
            ["contracts"] = Contracts.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToString(),
                StringComparer.Ordinal),
        };
        var deployedPath = Path.Combine(chainDirectory, "l1.deployed.json");
        File.WriteAllText(
            deployedPath,
            JsonSerializer.Serialize(deployed, jsonOptions) + Environment.NewLine);
        written.Add("l1.deployed.json");

        var settlement = new Dictionary<string, object?>
        {
            ["PluginConfiguration"] = new Dictionary<string, object?>
            {
                ["ChainId"] = L2ChainId,
                ["L1RpcEndpoint"] = Rpc,
                ["ExpectedNetwork"] = Network,
                ["SettlementManagerHash"] = SettlementManager.ToString(),
                ["ForcedInclusionHash"] = ForcedInclusion.ToString(),
                ["SharedBridgeHash"] = SharedBridge.ToString(),
                ["L2BridgeHash"] = "",
                ["MessageRouterHash"] = MessageRouter.ToString(),
                ["ProofType"] = 1,
                ["Enabled"] = true,
            },
        };
        var settlementJson = JsonSerializer.Serialize(settlement, jsonOptions) + Environment.NewLine;
        written.AddRange(WritePluginConfig(
            chainDirectory,
            "Neo.Plugins.L2Settlement",
            settlementJson,
            alsoWriteFromDeployCopy: true));

        var batch = new Dictionary<string, object?>
        {
            ["PluginConfiguration"] = new Dictionary<string, object?>
            {
                ["ChainId"] = L2ChainId,
                ["MaxBlocksPerBatch"] = 50,
                ["MaxTransactionsPerBatch"] = 5000,
                ["MaxBatchAgeMillis"] = 30000,
                ["Enabled"] = true,
            },
        };
        var batchJson = JsonSerializer.Serialize(batch, jsonOptions) + Environment.NewLine;
        written.AddRange(WritePluginConfig(
            chainDirectory,
            "Neo.Plugins.L2Batch",
            batchJson,
            alsoWriteFromDeployCopy: true));

        return written;
    }

    private static IEnumerable<string> WritePluginConfig(
        string chainDirectory,
        string pluginName,
        string json,
        bool alsoWriteFromDeployCopy)
    {
        var relativeRoots = new[]
        {
            Path.Combine("Plugins", pluginName),
            Path.Combine("node", "Plugins", pluginName),
            Path.Combine("batcher-node", "Plugins", pluginName),
        };
        foreach (var relative in relativeRoots)
        {
            var absoluteDir = Path.Combine(chainDirectory, relative);
            // Always write under top-level Plugins/; only write under node/batcher roots
            // when those trees already exist (created by init-l2).
            var isTopLevelPlugin = relative.StartsWith(
                "Plugins" + Path.DirectorySeparatorChar, StringComparison.Ordinal);
            if (!isTopLevelPlugin)
            {
                var parent = Path.GetDirectoryName(absoluteDir);
                if (parent is null || !Directory.Exists(parent))
                    continue;
            }
            Directory.CreateDirectory(absoluteDir);
            var configPath = Path.Combine(absoluteDir, "config.json");
            File.WriteAllText(configPath, json);
            yield return Path.Combine(relative, "config.json");
            if (alsoWriteFromDeployCopy)
            {
                var fromDeploy = Path.Combine(absoluteDir, "config.from-deploy.json");
                File.WriteAllText(fromDeploy, json);
                yield return Path.Combine(relative, "config.from-deploy.json");
            }
        }
    }

    private static UInt160 RequireContract(IReadOnlyDictionary<string, UInt160> contracts, string name)
    {
        if (!contracts.TryGetValue(name, out var hash))
            throw new ArgumentException(
                $"deploy report is missing a deployed/reused record for '{name}'");
        return hash;
    }

    private static string RequireString(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var el) || el.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(el.GetString()))
            throw new ArgumentException($"deploy report missing '{field}'");
        return el.GetString()!;
    }

    private static uint RequireUInt32(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var el))
            throw new ArgumentException($"deploy report missing '{field}'");
        if (el.ValueKind == JsonValueKind.Number && el.TryGetUInt32(out var n))
            return n;
        if (el.ValueKind == JsonValueKind.String
            && uint.TryParse(el.GetString(), out var parsed))
            return parsed;
        throw new ArgumentException($"deploy report '{field}' must be a uint32");
    }

    private static UInt160 RequireHash(JsonElement root, string field)
    {
        var text = RequireString(root, field);
        try
        {
            var hash = UInt160.Parse(text);
            if (hash.Equals(UInt160.Zero))
                throw new ArgumentException($"deploy report '{field}' must not be zero");
            return hash;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            throw new ArgumentException(
                $"deploy report '{field}'='{text}' is not a valid non-zero UInt160", ex);
        }
    }
}
