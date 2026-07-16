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
    /// Write operator-facing artifacts under a chain directory: full hash map plus a
    /// production settlement config snippet matching <c>L2SettlementSettings</c>.
    /// </summary>
    public void WriteOperatorArtifacts(string chainDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainDirectory);
        Directory.CreateDirectory(chainDirectory);

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
            JsonSerializer.Serialize(deployed, new JsonSerializerOptions { WriteIndented = true })
                + Environment.NewLine);

        var settlementDir = Path.Combine(chainDirectory, "Plugins", "Neo.Plugins.L2Settlement");
        Directory.CreateDirectory(settlementDir);
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
        File.WriteAllText(
            Path.Combine(settlementDir, "config.from-deploy.json"),
            JsonSerializer.Serialize(settlement, new JsonSerializerOptions { WriteIndented = true })
                + Environment.NewLine);
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
