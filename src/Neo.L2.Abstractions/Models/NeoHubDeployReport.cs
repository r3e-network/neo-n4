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

        // L2-side bridge: same chain id as batch/settlement for deposit consumer + withdrawal staging.
        var bridge = new Dictionary<string, object?>
        {
            ["PluginConfiguration"] = new Dictionary<string, object?>
            {
                ["ChainId"] = L2ChainId,
            },
        };
        var bridgeJson = JsonSerializer.Serialize(bridge, jsonOptions) + Environment.NewLine;
        written.AddRange(WritePluginConfig(
            chainDirectory,
            "Neo.Plugins.L2Bridge",
            bridgeJson,
            alsoWriteFromDeployCopy: true));

        // Prover plugin: same ProofType byte as settlement so Stage-0/1/2 selection matches
        // WireProduction without re-typing chain.config.
        var prover = new Dictionary<string, object?>
        {
            ["PluginConfiguration"] = new Dictionary<string, object?>
            {
                ["ProofType"] = proofTypeByte,
            },
        };
        var proverJson = JsonSerializer.Serialize(prover, jsonOptions) + Environment.NewLine;
        written.AddRange(WritePluginConfig(
            chainDirectory,
            "Neo.Plugins.L2Prover",
            proverJson,
            alsoWriteFromDeployCopy: true));

        // Metrics: default loopback scrape endpoint for host composition / Neo.CLI plugins.
        var metrics = new Dictionary<string, object?>
        {
            ["PluginConfiguration"] = new Dictionary<string, object?>
            {
                ["Enabled"] = true,
                ["BindAddress"] = "127.0.0.1",
                ["Port"] = 9090,
                ["MaxConcurrentConnections"] = 32,
            },
        };
        var metricsJson = JsonSerializer.Serialize(metrics, jsonOptions) + Environment.NewLine;
        written.AddRange(WritePluginConfig(
            chainDirectory,
            "Neo.Plugins.L2Metrics",
            metricsJson,
            alsoWriteFromDeployCopy: true));

        // DA plugin: advertise chain.config daMode; Local Multisig points DataDirectory at
        // the settlement local DA store. Public modes leave DataDirectory unset so hosts
        // inject production backends (no simulated/local fallback).
        var daModeByte = ResolveDAModeByte(chainDirectory);
        var daPluginConfig = new Dictionary<string, object?>
        {
            ["Profile"] = "Development",
            ["DAMode"] = daModeByte,
        };
        if (daModeByte == (byte)DAMode.Local)
            daPluginConfig["DataDirectory"] = RelativeLocalDaStoreDir;
        var da = new Dictionary<string, object?>
        {
            ["PluginConfiguration"] = daPluginConfig,
        };
        var daJson = JsonSerializer.Serialize(da, jsonOptions) + Environment.NewLine;
        written.AddRange(WritePluginConfig(
            chainDirectory,
            "Neo.Plugins.L2DA",
            daJson,
            alsoWriteFromDeployCopy: true));

        // Gateway: default durable publication settings; host attaches outbox + production
        // aggregator/prover/publisher before accepting work when gatewayEnabled.
        var gatewayEnabled = ResolveGatewayEnabled(chainDirectory);
        var gateway = new Dictionary<string, object?>
        {
            ["PluginConfiguration"] = new Dictionary<string, object?>
            {
                ["Enabled"] = gatewayEnabled,
                ["MaxAutomaticRetries"] = 3,
            },
        };
        var gatewayJson = JsonSerializer.Serialize(gateway, jsonOptions) + Environment.NewLine;
        written.AddRange(WritePluginConfig(
            chainDirectory,
            "Neo.Plugins.L2Gateway",
            gatewayJson,
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
                    ["batchPluginFactory"] =
                        "L2BatchPlugin.CreateFromChainDirectory / NextExpectedBlock / "
                        + "HasPendingSealedBatch / PendingSealedBatch / HasOpenBatch / "
                        + "InProgressTxCount / OpenBatchFirstBlock / OpenBatchLastBlock / "
                        + "OpenBatchBlockCount / ProcessCommittedBlock / TryRetryPendingSealedBatch",
                    ["settlementPluginFactory"] = "L2SettlementPlugin.CreateFromChainDirectory(chainDirectory)",
                    ["bridgePluginFactory"] = "L2BridgePlugin.CreateFromChainDirectory(chainDirectory)",
                    ["proverPluginFactory"] = "L2ProverPlugin.CreateFromChainDirectory(chainDirectory)",
                    ["metricsPluginFactory"] = "L2MetricsPlugin.CreateFromChainDirectory(chainDirectory)",
                    ["localDaPluginFactory"] = "L2DAPlugin.CreateLocalFromChainDirectory(chainDirectory)",
                    ["gatewayPluginFactory"] =
                        "L2GatewayPlugin.CreateDurableFromChainDirectory(chainDirectory, aggregator)",
                    ["gatewayPluginMerkleDurable"] =
                        "L2GatewayPlugin.CreateMerkleDurableFromChainDirectory(chainDirectory)",
                    ["gatewayPluginMultisigDurable"] =
                        "L2GatewayPlugin.CreateMultisigDurableFromChainDirectory(chainDirectory, signers, threshold)",
                    ["gatewayPluginSp1Durable"] =
                        "L2GatewayPlugin.CreateSp1DurableFromChainDirectory(chainDirectory)",
                    ["gatewayPluginSettingsOnly"] =
                        "L2GatewayPlugin.CreateFromChainDirectory(chainDirectory)",
                    ["gatewayPublisherFromChainDirectory"] =
                        "ProofBoundRpcGlobalRootPublisher.OpenFromChainDirectory(chainDirectory, signer|signAndSend)",
                    ["gatewayPublisherSignAndSend"] =
                        "ProofBoundRpcGlobalRootPublisher.CreateSignAndSend(rpcTransactionSender)",
                    ["l1DeployedEndpoints"] =
                        "L1DeployedEndpoints.FromChainDirectory(chainDirectory)",
                    ["gatewayConfigurePublicationFromChainDirectory"] =
                        "gateway.ConfigureGlobalRootPublicationFromChainDirectory(chainDir, prover, publisher, replayDomain, vk)",
                    ["gatewayProverQueue"] = RelativeGatewayProverQueueDir,
                    ["gatewaySp1ProverFromChainDirectory"] =
                        "Sp1GatewayProofProver.OpenFromChainDirectory(chainDirectory, gatewayVerificationKey)",
                    ["batchProverInbox"] = RelativeProverInboxDir,
                    ["batchSp1ProverFromChainDirectory"] =
                        "Sp1BatchProofProver.OpenFromChainDirectory(chainDirectory, verificationKeyId)",
                    ["proverPluginMultisigWired"] =
                        "L2ProverPlugin.CreateMultisigWiredFromChainDirectory(chainDirectory, signers)",
                    ["proverPluginZkWired"] =
                        "L2ProverPlugin.CreateZkWiredFromChainDirectory(chainDirectory, verificationKeyId)",
                    ["proverPluginOptimisticWired"] =
                        "L2ProverPlugin.CreateOptimisticWiredFromChainDirectory(chainDirectory, sequencerKey, bondContract, bondTxHash)",
                    ["stateStore"] = RelativeStateDir,
                    ["rpcProofStore"] = RelativeRpcProofStoreDir,
                    ["gatewayOutboxStore"] = RelativeGatewayOutboxStoreDir,
                    ["stateOpenHelper"] =
                        "Sp1SettlementExecutionStack.OpenStateFromChainDirectory(chainDirectory)",
                    ["rpcStoreOpenHelper"] =
                        "InMemoryL2RpcStore.OpenFromChainDirectory(chainDirectory)",
                    ["gatewayOutboxOpenHelper"] =
                        "PersistentGatewayOutbox.OpenFromChainDirectory(chainDirectory)",
                    ["sp1StackFromChainDirectory"] =
                        "Sp1SettlementExecutionStack.CreateFromChainDirectory(chainDir, state, executorPath, executorSha256, vk)",
                    ["wireProductionFromLayout"] =
                        "L2SettlementPlugin.WireProductionFromLayout(chainDir, layout, batch, executor, da, prover, signer)",
                    ["wireProductionPublicAccessors"] =
                        "ProductionDepositSource / ProductionMessageRouter / "
                        + "ProductionForcedInclusionSource / ProductionForcedInclusionFinalizer / "
                        + "ProductionSettlementClient / ProductionTransactionSender",
                    ["multisigLocalHostComposition"] =
                        "MultisigLocalHostComposition.Open(chainDir, executor, signers, signer, "
                        + "startMetricsHttp?) + InMemoryL2RpcStore (data/rpc/proofs)",
                    ["optimisticLocalHostComposition"] =
                        "OptimisticLocalHostComposition.Open(chainDir, executor, sequencerKey, bondContract, bondTxHash, signer, "
                        + "startMetricsHttp?) + InMemoryL2RpcStore (data/rpc/proofs)",
                    ["zkLocalHostComposition"] =
                        "ZkLocalHostComposition.Open(chainDir, executorPath, executorSha256, vk, productionDaWriter, signer, "
                        + "startMetricsHttp?) + InMemoryL2RpcStore (data/rpc/proofs)",
                    ["batchInboxVisibility"] =
                        "L2BatchPlugin.HasSealedBatchSink / HasDepositSource / HasMessageRouter / "
                        + "HasForcedInclusionSource / MaxForcedTransactionsPerBatch / MaxL1MessagesPerBatch",
                    ["settlementProductionWired"] =
                        "L2SettlementPlugin.IsProductionWired / IsEnabled / L1FinalityDepth / "
                        + "ForcedInclusionDeploymentHeight / SharedBridgeDeploymentHeight / "
                        + "MessageRouterDeploymentHeight after WireProduction",
                    ["settlementProductionTransactionSender"] =
                        "L2SettlementPlugin.ProductionTransactionSender after WireProduction",
                    ["settlementProductionForcedInclusionFinalizer"] =
                        "L2SettlementPlugin.ProductionForcedInclusionFinalizer after WireProduction",
                    ["localHostProductionSurfaces"] =
                        "LocalHost.DepositSource / MessageRouter / ForcedInclusionFinalizer / "
                        + "SettlementClient / TransactionSender / HasForcedInclusionFinalizer / "
                        + "HasSettlementClient / HasTransactionSender / IsSettlementEnabled / "
                        + "L1FinalityDepth / HasL1RpcEndpoint / ExpectedNetwork / "
                        + "HasSettlementManagerHash / HasForcedInclusionHash / "
                        + "HasSharedBridgeHash / HasMessageRouterHash / HasL2BridgeHash / "
                        + "HasMessageOutbox / DepositSourceReadyCount / DepositSourceReservedCount / "
                        + "DepositSourceSoftConsumedCount / IsMetricsEnabled / MetricsConfiguredPort / "
                        + "MetricsBindAddress / ForcedInclusionDeploymentHeight / "
                        + "SharedBridgeDeploymentHeight / MessageRouterDeploymentHeight / "
                        + "L1InboxPendingCount / L1InboxConsumedCount / KnownInboundNonceCount / "
                        + "MetricsBoundPort / IsMetricsHttpListening",
                    ["localHostReadiness"] =
                        "LocalHost.ChainId / BatcherConfiguredChainId / SettlementConfiguredChainId / "
                        + "RpcChainId / ProofType / SettlementConfiguredProofType / IsChainIdConfigConsistent / "
                        + "IsProofTypeConfigConsistent / DaMode / RpcDaMode / IsDaModeConfigConsistent / "
                        + "IsNeoHubHashWiringComplete / IsBatcherInboxWiringComplete / "
                        + "IsSecurityLevelProofTypeConsistent / IsSecurityLevelDaModeConsistent / "
                        + "IsMetricsWiringComplete / HasMetricsReadinessCheck / HasMetricsHealthProbe / "
                        + "IsDepositPipelineWiringComplete / IsMessagePipelineWiringComplete / "
                        + "IsForcedInclusionPipelineWiringComplete / IsSettlementClientWiringComplete / "
                        + "IsPipelineEnabled / IsSettlementPoisoned / IsSettlementRetrying / IsSettlementIdle / "
                        + "IsSettlementRuntimeIdle / IsSettlementPoisonedState / IsSettlementRetryingState / "
                        + "IsSettlementRuntimeIdleAsync / IsSettlementPoisonedAsync / IsSettlementRetryingAsync / "
                        + "IsBatcherCheckpointAlignedAsync / "
                        + "IsPipelineHealthy / "
                        + "PipelineHealthFailures / HasOverdueForcedInclusion / IsOpenBatchPastMaxAge / OpenBatchAgeMillis / "
                        + "IsBatcherCheckpointAligned / "
                        + "IsMetricsHttpHealthy / MetricsHttpHealthFailures / "
                        + "IsLocalHostHealthy / LocalHostHealthFailures / IsLocalHostHealthyAsync / "
                        + "IsPipelineHealthyAsync / GetPipelineHealthFailuresAsync / "
                        + "StartMetricsHttp(/readyz defaults to IsOfflinePassportComplete; /healthprobe → FormatHealthProbeJson) / "
                        + "HasExpectedNetwork / HasScannerDeployHeights / IsOfflinePassportComplete / "
                        + "OfflinePassportFailures / BuildOfflinePassportFailures / "
                        + "HasSealedBatchSink / NextExpectedBlock / ProcessCommittedBlock / IsOperatorReady / "
                        + "PeekSharedBridgeDeposits / GetOperatorStatusAsync",
                    ["localHostBatcherHelpers"] =
                        "LocalHost.BatcherConfiguredChainId / NextExpectedBlock / "
                        + "LastAcknowledgedBatchNumber / LastAcknowledgedBlock / "
                        + "NextBatchNumber / HasPendingSealedBatch / PendingSealedBatchNumber / "
                        + "PendingSealedBatchLastBlock / IsBatcherEnabled / MaxBlocksPerBatch / "
                        + "MaxTransactionsPerBatch / MaxBatchAgeMillis / MaxForcedTransactionsPerBatch / "
                        + "MaxL1MessagesPerBatch / HasBatchDepositSource / HasBatchMessageRouter / "
                        + "HasBatchForcedInclusionSource / HasBatchProver / PendingSealedBatch / HasOpenBatch / "
                        + "InProgressTxCount / OpenBatchFirstBlock / OpenBatchLastBlock / OpenBatchBlockCount / "
                        + "OpenBatchL1MessageCount / OpenBatchL2ToL1MessageCount / OpenBatchL2ToL2MessageCount / "
                        + "OpenBatchForcedInclusionCount / OpenBatchWithdrawalCount / ProcessCommittedBlock / "
                        + "TryRetryPendingSealedBatch / OnBatchSealed",
                    ["localHostRpcStoreHelpers"] =
                        "LocalHost.GetLatestRpcStateRoot / GetRpcStateRootAtBatch / AddRpcBatch / "
                        + "FinalizeRpcBatch / RecordRpcDeposit / GetRpcL1DepositStatus / GetRpcBatch / "
                        + "GetRpcBatchStatus / RegisterRpcAsset / GetRpcCanonicalAsset / "
                        + "GetRpcBridgedAsset / RecordRpcWithdrawalProof / RecordRpcMessageProof / "
                        + "GetRpcWithdrawalProof / GetRpcMessageProof",
                    ["localHostMessageRouterHelpers"] =
                        "LocalHost.MessageOutbox / HasMessageOutbox / MessageOutboxL2ToL1Root / "
                        + "MessageOutboxL2ToL2Root / EnqueueOutboundMessagesAsync / "
                        + "RecordMessageRouterFinalizedProof / GetMessageRouterProofAsync / "
                        + "RegisterInboundMessageNonce / InvalidateInboundMessageCache / "
                        + "KnownInboundNonceCount",
                    ["localHostForcedInclusionHelpers"] =
                        "LocalHost.RegisterForcedInclusionNonce / InvalidateForcedInclusionCache / "
                        + "KnownForcedInclusionNonceCount / HasBatchForcedInclusionSource",
                    ["localHostDaHelpers"] =
                        "LocalHost.PublishDaAsync / IsDaAvailableAsync / SupportsLocalDaReader / "
                        + "CreateLocalDaReader (Multisig/Optimistic local DA)",
                    ["localHostMetricsExport"] =
                        "LocalHost.CaptureMetricsSnapshot / ExportPrometheusMetrics",
                    ["localHostMetricsSettings"] =
                        "LocalHost.IsMetricsEnabled / MetricsConfiguredPort / MetricsBindAddress / "
                        + "IsMetricsWiringComplete / HasMetricsReadinessCheck / HasMetricsHealthProbe / "
                        + "MetricsMaxConcurrentConnections / MetricsBoundPort / IsMetricsHttpListening",
                    ["localHostBridgeRegistry"] =
                        "LocalHost.BridgeAssetRegistry / RegisterBridgeAsset / "
                        + "SnapshotBridgeAssets / BridgeAssetCount",
                    ["localHostBridgeProcessors"] =
                        "LocalHost.ProcessDeposit / ProcessReadyDeposits / ScanSharedBridgeDepositsAsync / "
                        + "ScanAndProcessReadyDepositsAsync / HasConsumedDeposit / ConsumedDepositCount / "
                        + "DepositSourceReadyCount / DepositSourceReservedCount / "
                        + "DepositSourceSoftConsumedCount / "
                        + "StageWithdrawal / StagedWithdrawalCount / SealWithdrawalBatch / ProveAsync",
                    ["localHostForcedInclusionOverdue"] =
                        "LocalHost.HasOverdueForcedInclusionAsync(nowUnixSeconds)",
                    ["localHostWriteOperatorStatus"] =
                        "LocalHost.FormatOperatorStatusJsonAsync() / WriteOperatorStatusAsync(path) → "
                        + "LocalHostOperatorStatusDocument JSON",
                    ["localHostWriteHealthProbe"] =
                        "LocalHost.GetHealthProbeAsync() / FormatHealthProbeJson() / "
                        + "WriteHealthProbeAsync(path) / metrics HTTP GET /healthprobe → "
                        + "LocalHostHealthProbeDocument JSON "
                        + "(passport/pipeline/metrics/settlement + pending-seal/open-batch counts + "
                        + "batcher ack/next + durable checkpoint numbers + "
                        + "FI/inbox + deposit ready/reserved/soft-consumed + "
                        + "L1Inbox consumed + staged withdrawals + "
                        + "HasMetricsReadinessCheck/HasMetricsHealthProbe flags)",
                    ["localHostWritePrometheusMetrics"] =
                        "LocalHost.WritePrometheusMetricsAsync(path) → Prometheus text file",
                    ["gatewayHostWriteOperatorStatus"] =
                        "GatewayHostComposition.FormatOperatorStatusJson() / WriteOperatorStatusAsync(path) → "
                        + "GatewayHostOperatorStatusDocument JSON",
                    ["gatewayHostWriteHealthProbe"] =
                        "GatewayHostComposition.GetHealthProbe() / FormatHealthProbeJson() / "
                        + "WriteHealthProbeAsync(path) → "
                        + "GatewayHostHealthProbeDocument JSON "
                        + "(passport/outbox/publication + pending/queue/retry/lag flags)",
                    ["gatewayHostWritePrometheusMetrics"] =
                        "GatewayHostComposition.WritePrometheusMetricsAsync(path) / ExportPrometheusMetrics "
                        + "(when Metrics is IMetricsSource)",
                    ["localHostSettleHelpers"] =
                        "LocalHost.ReconcileAsync / SubmitNextAsync / GetPendingCountAsync / "
                        + "PersistAsync / EnqueueAsync",
                    ["localHostRecoveryHelpers"] =
                        "LocalHost.GetRecoveryStatusAsync / RecoverPoisonedBatchAsync / "
                        + "GetTrackedForcedInclusionNoncesAsync / GetLatestCheckpointAsync / "
                        + "GetLatestDurableCheckpointAsync / GetInitialStateRootAsync / "
                        + "LatestCheckpointBatchNumber / LatestCheckpointLastBlock / "
                        + "LatestCheckpointPostStateRoot / InitialStateRoot",
                    ["localHostStartMetricsHttp"] =
                        "LocalHost.StartMetricsHttp(portOverride?, readiness?) / StopMetricsHttp / "
                        + "Open startMetricsHttp "
                        + "(wires /readyz + /healthprobe before Metrics.Start)",
                    ["localHostCreateRpcPlugin"] =
                        "LocalHost.CreateRpcPlugin() then NeoSystem.AddService(RpcStore)",
                    ["metricsReadiness"] =
                        "LocalHost StartMetricsHttp → WithReadinessCheck(IsOfflinePassportComplete) + "
                        + "WithHealthProbe(FormatHealthProbeJson) → /readyz + /healthprobe",
                    ["gatewayHostCompositionMerkle"] =
                        "GatewayHostComposition.OpenMerkle(chainDir, proofProver, signer, replayDomain, vk, metrics?)",
                    ["gatewayHostCompositionMultisig"] =
                        "GatewayHostComposition.OpenMultisig(chainDir, signers, threshold, proofProver, signer, replayDomain, vk, metrics?)",
                    ["gatewayHostCompositionSp1"] =
                        "GatewayHostComposition.OpenSp1(chainDir, gatewayVk, signer, replayDomain, vk, metrics?)",
                    ["gatewayHostOpsHelpers"] =
                        "GatewayHostComposition.HasPendingPublication / PendingPublicationEpoch / "
                        + "AggregatorPendingCount / HasDurableOutbox / IsPublicationConfigured / "
                        + "IsEnabled / MaxAutomaticRetries / ProofSystem / AggregationBackendId / "
                        + "ExpectedNetwork / HasL1RpcEndpoint / IsPublicationProfileReady / "
                        + "HasExpectedNetwork / IsOfflinePassportComplete / OfflinePassportFailures / "
                        + "BuildOfflinePassportFailures / "
                        + "IsOutboxPoisoned / IsOutboxIdle / IsOutboxRuntimeIdle / IsPublicationHealthy / "
                        + "PublicationHealthFailures / BuildPublicationHealthFailures / "
                        + "IsGatewayHostHealthy / GatewayHostHealthFailures / "
                        + "ReplayDomain / VerificationKeyId / SettlementManagerHash / MessageRouterHash / "
                        + "OutboxStatus / Aggregator / ReceiveBatch / "
                        + "PullAggregate (fails closed with durable outbox) / PublishAggregateAsync / "
                        + "RecoverPoisonedPublication / GetOperatorStatus / FormatOperatorStatusJson / "
                        + "WriteOperatorStatusAsync / "
                        + "GetHealthProbe / FormatHealthProbeJson / WriteHealthProbeAsync / "
                        + "Metrics / CaptureMetricsSnapshot / ExportPrometheusMetrics / "
                        + "WritePrometheusMetricsAsync",
                    ["localDaOpenHelper"] = "PersistentDAWriter.OpenLocalFromChainDirectory(chainDirectory)",
                    ["daMetricsWrap"] =
                        "MetricsEmittingDAWriter / MetricsEmittingProductionDAWriter (LocalHost Open)",
                    ["depositSourceFromChainDirectory"] =
                        "L2SettlementPlugin.CreateDepositSourceFromChainDirectory(chainDirectory)",
                    ["depositSourceOpenHelper"] =
                        "RpcSharedBridgeDepositSource.OpenFromChainDirectory(chainDir, rpc, sharedBridge, chainId, l2Bridge, startHeight)",
                    ["forcedInclusionSourceFromChainDirectory"] =
                        "L2SettlementPlugin.CreateForcedInclusionSourceFromChainDirectory(chainDirectory)",
                    ["forcedInclusionSourceOpenHelper"] =
                        "RpcForcedInclusionSource.OpenFromChainDirectory(chainDir, rpc, fiHash, chainId, startHeight)",
                    ["messageRouterFromChainDirectory"] =
                        "L2SettlementPlugin.CreateMessageRouterFromChainDirectory(chainDirectory)",
                    ["messageRouterOpenHelper"] =
                        "RpcMessageRouter.OpenFromChainDirectory(chainDir, rpc, routerHash, chainId, startHeight)",
                    ["l1InboxFromChainDirectory"] =
                        "L1InboxFromChainDirectory.Open(chainDirectory) / WireL1InboxFromChainDirectory(chainDir, batch)",
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
                    "L1 inbox: L2SettlementPlugin.WireL1InboxFromChainDirectory(chainDir, batch) "
                    + "or L1InboxFromChainDirectory.Open(chainDir).WireBatch(batch) — one shared L1 "
                    + "RPC for deposit + ForcedInclusion + MessageRouter; also "
                    + "Create*FromChainDirectory / OpenFromChainDirectory per type when needed",
                    "L2ProverPlugin.CreateFromChainDirectory(chainDir) then Wire(signerSet / "
                    + "optimisticProver / zkProver from Sp1 stack)",
                    "L2MetricsPlugin.CreateFromChainDirectory(chainDir) then WithMetrics on batch/"
                    + "settlement/DA plugins and Start()",
                    "Multisig/Optimistic local DA: L2DAPlugin.CreateLocalFromChainDirectory(chainDir) "
                    + "or PersistentDAWriter.OpenLocalFromChainDirectory; public DAMode needs "
                    + "WithProductionBackend",
                    "L2 RPC: InMemoryL2RpcStore.OpenFromChainDirectory(chainDir) then "
                    + "NeoSystem.AddService(store) before L2RpcPlugin registers methods "
                    + "(durable proofs under " + RelativeRpcProofStoreDir + ")",
                    "Gateway: L2GatewayPlugin.CreateDurableFromChainDirectory(chainDir, aggregator) "
                    + "(settings → UseAggregator → AttachOutboxFromChainDirectory at "
                    + RelativeGatewayOutboxStoreDir + "); then ConfigureGlobalRootPublication "
                    + "(production proof prover + proof-bound publisher). CreateFromChainDirectory "
                    + "alone loads settings only so UseAggregator is not blocked by an early outbox",
                    "Zk: ZkLocalHostComposition.Open(chainDir, executorPath, executorSha256, vk, "
                    + "daWriter, signer) after bootstrap-genesis — or manually "
                    + "state = Sp1SettlementExecutionStack.OpenStateFromChainDirectory(chainDir) "
                    + "then CreateFromChainDirectory(chainDir, state, executorPath, executorSha256, vk) "
                    + "(ensures prover/executor-scratch + prover/inbox; state path "
                    + RelativeStateDir + "); production DA writer + prove-batch daemon remain funded",
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
    /// Canonical RocksDB path for durable L2 RPC withdrawal/message inclusion proofs
    /// (opened by <c>InMemoryL2RpcStore.OpenFromChainDirectory</c>).
    /// </summary>
    public const string RelativeRpcProofStoreDir = "data/rpc/proofs";

    /// <summary>
    /// Canonical RocksDB path for durable Gateway outbox / publication recovery
    /// (opened by <c>PersistentGatewayOutbox.OpenFromChainDirectory</c>).
    /// </summary>
    public const string RelativeGatewayOutboxStoreDir = "data/gateway/outbox";

    /// <summary>
    /// Canonical shared file-queue directory for the Gateway SP1 recursive prover daemon
    /// (opened by <c>Sp1GatewayProofProver.OpenFromChainDirectory</c>).
    /// </summary>
    public const string RelativeGatewayProverQueueDir = "prover/gateway-inbox";

    /// <summary>
    /// Canonical shared file-queue directory for the batch SP1 <c>prove-batch</c> daemon
    /// (opened by <c>Sp1BatchProofProver.OpenFromChainDirectory</c>; same path as
    /// <c>Sp1SettlementExecutionStack.RelativeProverQueueDir</c> / <c>init-l2</c>).
    /// </summary>
    public const string RelativeProverInboxDir = "prover/inbox";

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
            RelativeRpcProofStoreDir,
            RelativeGatewayOutboxStoreDir,
            RelativeGatewayProverQueueDir,
            RelativeProverInboxDir,
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

    /// <summary>
    /// Map <c>chain.config.json</c> <c>daMode</c> name to the DA plugin byte.
    /// Defaults to <see cref="DAMode.Local"/> when the file or field is absent (host-local
    /// Multisig durability — not a ChainRegistry public label).
    /// </summary>
    public static byte ResolveDAModeByte(string chainDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainDirectory);
        var configPath = Path.Combine(chainDirectory, "chain.config.json");
        if (!File.Exists(configPath))
            return (byte)DAMode.Local;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            if (!doc.RootElement.TryGetProperty("daMode", out var prop)
                || prop.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(prop.GetString()))
                return (byte)DAMode.Local;
            var name = prop.GetString()!;
            if (!Enum.TryParse<DAMode>(name, ignoreCase: true, out var daMode))
                throw new ArgumentException(
                    $"chain.config.json daMode='{name}' is not a valid DAMode");
            return (byte)daMode;
        }
        catch (JsonException ex)
        {
            throw new ArgumentException(
                $"chain.config.json is not valid JSON: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Read <c>chain.config.json</c> <c>gatewayEnabled</c>. Defaults to <c>true</c> when
    /// absent so gateway-capable templates keep the plugin enabled.
    /// </summary>
    public static bool ResolveGatewayEnabled(string chainDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainDirectory);
        var configPath = Path.Combine(chainDirectory, "chain.config.json");
        if (!File.Exists(configPath))
            return true;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            if (!doc.RootElement.TryGetProperty("gatewayEnabled", out var prop))
                return true;
            if (prop.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new ArgumentException("chain.config.json gatewayEnabled must be a boolean");
            return prop.GetBoolean();
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
