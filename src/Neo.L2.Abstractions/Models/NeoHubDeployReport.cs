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
    IReadOnlyDictionary<string, UInt160> Contracts,
    IReadOnlyDictionary<string, uint> DeployHeights)
{
    /// <summary>
    /// Default <c>--operator</c> for register-chain: the deploy signer script hash
    /// (hot-wallet / multisig account that owns NeoHub contracts after deploy).
    /// </summary>
    public UInt160 DefaultOperatorManager => OwnerScriptHash;

    // DeployHeights: confirmed L1 block indices for deploy records with blockIndex.
    // Empty when the report predates height materialization or heights were not resolved.

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
        var heights = new Dictionary<string, uint>(StringComparer.Ordinal);
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
            if (record.TryGetProperty("blockIndex", out var heightEl)
                && heightEl.ValueKind == JsonValueKind.Number
                && heightEl.TryGetUInt32(out var height))
                heights[name] = height;
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
            contracts,
            heights);
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

        foreach (var storeDir in EnsureSettlementStoreDirectories(chainDirectory))
            written.Add(storeDir + Path.DirectorySeparatorChar);

        Contracts.TryGetValue("SequencerRegistry", out var sequencerRegistry);
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
            ["sequencerRegistry"] = sequencerRegistry is null || sequencerRegistry.Equals(UInt160.Zero)
                ? null
                : sequencerRegistry.ToString(),
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

        // Prefer chain.config.json proofType so zk-rollup / optimistic templates do not
        // silently materialize Multisig settlement configs.
        var proofTypeByte = ResolveProofTypeByte(chainDirectory);

        // Materialize scanner start heights when the evidence report includes blockIndex
        // so WireProduction can default from plugin config without re-typed args.
        DeployHeights.TryGetValue("ForcedInclusion", out var forcedHeight);
        DeployHeights.TryGetValue("SharedBridge", out var sharedBridgeHeight);
        DeployHeights.TryGetValue("MessageRouter", out var messageRouterHeight);
        var settlementConfig = new Dictionary<string, object?>
        {
            ["ChainId"] = L2ChainId,
            ["L1RpcEndpoint"] = Rpc,
            ["ExpectedNetwork"] = Network,
            ["SettlementManagerHash"] = SettlementManager.ToString(),
            ["ForcedInclusionHash"] = ForcedInclusion.ToString(),
            ["SharedBridgeHash"] = SharedBridge.ToString(),
            ["L2BridgeHash"] = "",
            ["MessageRouterHash"] = MessageRouter.ToString(),
            ["ProofType"] = proofTypeByte,
            // Align scanners + RpcL1FinalizedHeightSource; override in config when needed.
            ["L1FinalityDepth"] = 1u,
            ["Enabled"] = true,
        };
        if (forcedHeight != 0)
            settlementConfig["ForcedInclusionDeploymentHeight"] = forcedHeight;
        if (sharedBridgeHeight != 0)
            settlementConfig["SharedBridgeDeploymentHeight"] = sharedBridgeHeight;
        if (messageRouterHeight != 0)
            settlementConfig["MessageRouterDeploymentHeight"] = messageRouterHeight;
        var settlement = new Dictionary<string, object?>
        {
            ["PluginConfiguration"] = settlementConfig,
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

        // Operator checklist for WireProduction args that are not plugin config fields.
        // Deploy heights materialize from the evidence report when blockIndex is present;
        // durable store paths remain operator-supplied.
        var deployHeights = new Dictionary<string, object?>
        {
            ["forcedInclusion"] = DeployHeights.TryGetValue("ForcedInclusion", out var fiH) ? fiH : null,
            ["sharedBridge"] = DeployHeights.TryGetValue("SharedBridge", out var sbH) ? sbH : null,
            ["messageRouter"] = DeployHeights.TryGetValue("MessageRouter", out var mrH) ? mrH : null,
            ["settlementManager"] = DeployHeights.TryGetValue("SettlementManager", out var smH) ? smH : null,
            ["all"] = DeployHeights.ToDictionary(
                pair => pair.Key,
                pair => (object)pair.Value,
                StringComparer.Ordinal),
        };
        var missingHeights = new[] { "ForcedInclusion", "SharedBridge", "MessageRouter" }
            .Where(name => !DeployHeights.ContainsKey(name))
            .ToArray();
        var notes = new Dictionary<string, object?>
        {
            ["schemaVersion"] = 1,
            ["l2ChainId"] = L2ChainId,
            ["proofType"] = proofTypeByte,
            ["wireProduction"] = new Dictionary<string, object?>
            {
                ["settlementManagerHash"] = SettlementManager.ToString(),
                ["forcedInclusionHash"] = ForcedInclusion.ToString(),
                ["sharedBridgeHash"] = SharedBridge.ToString(),
                ["messageRouterHash"] = MessageRouter.ToString(),
                ["l1RpcEndpoint"] = Rpc,
                ["expectedNetwork"] = Network,
                ["l1FinalityDepth"] = 1,
                ["deploymentHeights"] = deployHeights,
                ["missingDeploymentHeights"] = missingHeights,
                ["heightsInPluginConfig"] = missingHeights.Length == 0,
                ["sequencerRegistry"] = sequencerRegistry is null || sequencerRegistry.Equals(UInt160.Zero)
                    ? null
                    : sequencerRegistry.ToString(),
                ["recommendedDurableStores"] = new Dictionary<string, object?>
                {
                    ["proofWitnessStore"] = RelativeProofWitnessStoreDir,
                    ["forcedInclusionEventStore"] = RelativeForcedInclusionEventStoreDir,
                    ["sharedBridgeDepositEventStore"] = RelativeSharedBridgeDepositEventStoreDir,
                    ["messageRouterEventStore"] = RelativeMessageRouterEventStoreDir,
                    ["localDaStore"] = RelativeLocalDaStoreDir,
                    ["openHelper"] = "L2SettlementStoreLayout.Open(chainDirectory)",
                    ["batchPluginFactory"] = "L2BatchPlugin.CreateFromChainDirectory(chainDirectory)",
                    ["settlementPluginFactory"] = "L2SettlementPlugin.CreateFromChainDirectory(chainDirectory)",
                    ["stateStore"] = RelativeStateDir,
                    ["stateOpenHelper"] =
                        "Sp1SettlementExecutionStack.OpenStateFromChainDirectory(chainDirectory)",
                    ["sp1StackFromChainDirectory"] =
                        "Sp1SettlementExecutionStack.CreateFromChainDirectory(chainDir, state, executorPath, executorSha256, vk)",
                    ["wireProductionFromLayout"] =
                        "L2SettlementPlugin.WireProductionFromLayout(chainDir, layout, batch, executor, da, prover, signer)",
                    ["localDaOpenHelper"] = "PersistentDAWriter.OpenLocalFromChainDirectory(chainDirectory)",
                    ["nestedNep17Signer"] =
                        "LocalKeyTransactionSigner.FromEnvironmentVariableWithGlobalScope()",
                },
                ["requiredCallerArgs"] = new[]
                {
                    "INeoTransactionSigner: LocalKeyTransactionSigner.FromEnvironmentVariable() "
                    + "for local/testnet; nested NEP-17 (SharedBridge.Deposit / ForcedInclusion fees) "
                    + "use FromEnvironmentVariableWithGlobalScope() or --witness-scope Global; "
                    + "production uses HSM/KMS INeoTransactionSigner",
                    "L2SettlementPlugin.CreateFromChainDirectory(chainDir) "
                    + "(or L2SettlementSettings.FromChainDirectory + ctor)",
                    "L2BatchPlugin.CreateFromChainDirectory(chainDir) "
                    + "(or L2BatchSettings.FromChainDirectory + ctor)",
                    "Zk: state = Sp1SettlementExecutionStack.OpenStateFromChainDirectory(chainDir) "
                    + "then CreateFromChainDirectory(chainDir, state, executorPath, executorSha256, vk) "
                    + "after bootstrap-genesis (ensures prover/executor-scratch + prover/inbox; "
                    + "state path " + RelativeStateDir + ")",
                    "L2SettlementStoreLayout.Open(chainDir) then "
                    + "WireProductionFromLayout(chainDir, layout, batch, executor, da, prover, signer) "
                    + "— binds ProofWitness + three scanners, static committee hash from "
                    + "chain.config validators, and Multisig/Optimistic profile via "
                    + "LegacyFromChainDirectory (pass Sp1 stack Profile for Zk)",
                    "durable proofWitnessStore (recommended: "
                    + RelativeProofWitnessStoreDir + ")",
                    "durable forcedInclusionEventStore (recommended: "
                    + RelativeForcedInclusionEventStoreDir
                    + "; ForcedInclusionDeploymentHeight from plugin config when set)",
                    "durable sharedBridgeDepositEventStore (recommended: "
                    + RelativeSharedBridgeDepositEventStoreDir
                    + "; SharedBridgeDeploymentHeight from plugin config when SharedBridgeHash set)",
                    "durable messageRouterEventStore (recommended: "
                    + RelativeMessageRouterEventStoreDir
                    + "; MessageRouterDeploymentHeight from plugin config when MessageRouterHash set)",
                    "local Multisig/Optimistic DA: PersistentDAWriter.OpenLocalFromChainDirectory(chainDir) "
                    + "→ " + RelativeLocalDaStoreDir
                    + " (node-local durability only; Zk/Validity public DA uses L1/NeoFS production writers)",
                    "l1FinalizedHeight optional: WireProduction defaults from production RPC + L1FinalityDepth "
                    + "(or pass RpcL1FinalizedHeightSource.CreateSyncProvider)",
                    "sequencerCommitteeHash: WireProductionFromLayout defaults to "
                    + "SequencerCommitteeConfig.CreateStaticHashProviderFromChainDirectory(chainDir); "
                    + "override with SequencerCommitteeHasher.CreateSyncProvider("
                    + "new RpcSequencerCommitteeProvider(...)) for live registry polling",
                    "genesis state root: L2GenesisManifest.ReadInitialStateRootFromChainDirectory(chainDir) "
                    + "after bootstrap-genesis (also used by LegacyFromChainDirectory)",
                    "profile: default LegacyFromChainDirectory for Multisig/Optimistic; "
                    + "Sp1SettlementExecutionStack.Create for Zk",
                    "executor + DA writer + prover (e.g. Sp1SettlementExecutionStack for Zk; "
                    + "Multisig: AttestationProver + PersistentDAWriter.OpenLocalFromChainDirectory; "
                    + "Optimistic: OptimisticProver(sequencerKey, bondContract, bondTxHash) "
                    + "+ local or production DA)",
                },
            },
            ["genesisManifest"] = BootstrapGenesisManifestRelativePath,
            ["registerChain"] = new Dictionary<string, object?>
            {
                ["chainRegistry"] = ChainRegistry.ToString(),
                ["operatorManager"] = DefaultOperatorManager.ToString(),
                ["verifier"] = VerifierRegistry.ToString(),
                ["bridge"] = SharedBridge.ToString(),
                ["message"] = MessageRouter.ToString(),
            },
        };
        var notesPath = Path.Combine(chainDirectory, "l1.wireproduction-notes.json");
        File.WriteAllText(
            notesPath,
            JsonSerializer.Serialize(notes, jsonOptions) + Environment.NewLine);
        written.Add("l1.wireproduction-notes.json");

        return written;
    }

    /// <summary>Relative path of the genesis manifest written by <c>bootstrap-genesis</c>.</summary>
    public const string BootstrapGenesisManifestRelativePath = "genesis-manifest.json";

    /// <summary>Canonical RocksDB path for proof-witness durable store (relative to chain dir).</summary>
    public const string RelativeProofWitnessStoreDir = "data/settlement/proof-witness";

    /// <summary>Canonical RocksDB path for ForcedInclusion event scanner store.</summary>
    public const string RelativeForcedInclusionEventStoreDir = "data/settlement/forced-inclusion-events";

    /// <summary>Canonical RocksDB path for SharedBridge deposit event scanner store.</summary>
    public const string RelativeSharedBridgeDepositEventStoreDir = "data/settlement/shared-bridge-deposits";

    /// <summary>Canonical RocksDB path for MessageRouter L1→L2 event scanner store.</summary>
    public const string RelativeMessageRouterEventStoreDir = "data/settlement/message-router-events";

    /// <summary>
    /// Canonical RocksDB path for Multisig/Optimistic local <c>PersistentDAWriter</c>
    /// (node-local durability only — not public DA evidence).
    /// </summary>
    public const string RelativeLocalDaStoreDir = "data/settlement/da";

    /// <summary>
    /// Canonical L2 execution state RocksDB path (written by <c>bootstrap-genesis</c>;
    /// opened by Sp1 hosts via <c>Sp1SettlementExecutionStack.OpenStateFromChainDirectory</c>).
    /// </summary>
    public const string RelativeStateDir = "data/state";

    /// <summary>
    /// Create the canonical WireProduction durable-store directories under a chain layout.
    /// Safe to call repeatedly; does not open RocksDB (empty dirs only).
    /// </summary>
    /// <returns>Relative paths created or already present.</returns>
    public static IReadOnlyList<string> EnsureSettlementStoreDirectories(string chainDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainDirectory);
        Directory.CreateDirectory(chainDirectory);
        var relative = new[]
        {
            RelativeProofWitnessStoreDir,
            RelativeForcedInclusionEventStoreDir,
            RelativeSharedBridgeDepositEventStoreDir,
            RelativeMessageRouterEventStoreDir,
            RelativeLocalDaStoreDir,
        };
        foreach (var path in relative)
            Directory.CreateDirectory(Path.Combine(chainDirectory, path));
        return relative;
    }

    /// <summary>
    /// Map <c>chain.config.json</c> <c>proofType</c> name to the settlement plugin byte.
    /// Defaults to Multisig(1) when the file or field is absent.
    /// </summary>
    public static byte ResolveProofTypeByte(string chainDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainDirectory);
        var configPath = Path.Combine(chainDirectory, "chain.config.json");
        if (!File.Exists(configPath))
            return (byte)ProofType.Multisig;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            if (!doc.RootElement.TryGetProperty("proofType", out var prop)
                || prop.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(prop.GetString()))
                return (byte)ProofType.Multisig;
            var name = prop.GetString()!;
            if (!Enum.TryParse<ProofType>(name, ignoreCase: true, out var proofType))
                throw new ArgumentException(
                    $"chain.config.json proofType='{name}' is not a valid ProofType");
            return (byte)proofType;
        }
        catch (JsonException ex)
        {
            throw new ArgumentException(
                $"chain.config.json is not valid JSON: {ex.Message}", ex);
        }
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
