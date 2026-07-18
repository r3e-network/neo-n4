using System.Text.Json;

namespace Neo.L2.Abstractions.UnitTests;

[TestClass]
public class UT_NeoHubDeployReport
{
    private static string MinimalReportJson(
        uint l2ChainId = 20260716,
        string? chainRegistry = null)
    {
        var cr = chainRegistry ?? "0x65201c5415f6fc13093ad7169c556351c2392d23";
        var smokeHash = "0x" + new string('f', 40);
        return $$"""
            {
              "rpc": "https://n3seed1.ngd.network:20332/",
              "network": 894710606,
              "ownerAddress": "NLtL2v28d7TyMEaXcPqtekunkFRksJ7wxu",
              "ownerScriptHash": "0x13ef519c362973f9a34648a9eac5b71250b2a80a",
              "l2ChainId": {{l2ChainId}},
              "dryRun": false,
              "records": [
                {
                  "category": "deploy",
                  "name": "ChainRegistry",
                  "status": "deployed",
                  "contractHash": "{{cr}}"
                },
                {
                  "category": "deploy",
                  "name": "VerifierRegistry",
                  "status": "deployed",
                  "contractHash": "0xe058ea01d6933f38bad1321d0407f514112016b1"
                },
                {
                  "category": "deploy",
                  "name": "SharedBridge",
                  "status": "deployed",
                  "contractHash": "0xf2f5114b83dd6fed4ddcac0ff9966fd22a77b241",
                  "blockIndex": 17729307
                },
                {
                  "category": "deploy",
                  "name": "MessageRouter",
                  "status": "deployed",
                  "contractHash": "0x3caf3c6e160b5aec2e07672dc37662b5998afe90",
                  "blockIndex": 17729303
                },
                {
                  "category": "deploy",
                  "name": "SettlementManager",
                  "status": "deployed",
                  "contractHash": "0x11448868f1c14422506b9c2360051df34bcbbb51",
                  "blockIndex": 17729293
                },
                {
                  "category": "deploy",
                  "name": "ForcedInclusion",
                  "status": "reused",
                  "contractHash": "0x962829ae28e7f89e5de4b4672b167c8ae2ba55a9",
                  "blockIndex": 17729309
                },
                {
                  "category": "smoke",
                  "name": "ignored",
                  "status": "ok",
                  "contractHash": "{{smokeHash}}"
                }
              ]
            }
            """;
    }

    [TestMethod]
    public void Parse_ExtractsRequiredContractsAndOwner()
    {
        var report = NeoHubDeployReport.Parse(MinimalReportJson());
        Assert.AreEqual(894710606u, report.Network);
        Assert.AreEqual(20260716u, report.L2ChainId);
        Assert.AreEqual(
            UInt160.Parse("0x13ef519c362973f9a34648a9eac5b71250b2a80a"),
            report.OwnerScriptHash);
        Assert.AreEqual(report.OwnerScriptHash, report.DefaultOperatorManager);
        Assert.AreEqual(
            UInt160.Parse("0x65201c5415f6fc13093ad7169c556351c2392d23"),
            report.ChainRegistry);
        Assert.AreEqual(
            UInt160.Parse("0xe058ea01d6933f38bad1321d0407f514112016b1"),
            report.VerifierRegistry);
        Assert.AreEqual(
            UInt160.Parse("0xf2f5114b83dd6fed4ddcac0ff9966fd22a77b241"),
            report.SharedBridge);
        Assert.AreEqual(
            UInt160.Parse("0x3caf3c6e160b5aec2e07672dc37662b5998afe90"),
            report.MessageRouter);
        Assert.IsTrue(report.Contracts.ContainsKey("ForcedInclusion"));
        Assert.AreEqual(17729309u, report.DeployHeights["ForcedInclusion"]);
        Assert.AreEqual(17729307u, report.DeployHeights["SharedBridge"]);
        Assert.AreEqual(17729303u, report.DeployHeights["MessageRouter"]);
    }

    [TestMethod]
    public void WriteOperatorArtifacts_MaterializesDeploymentHeights()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-n4-deploy-heights-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var report = NeoHubDeployReport.Parse(MinimalReportJson());
            report.WriteOperatorArtifacts(dir);
            var notes = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(dir, "l1.wireproduction-notes.json")));
            var heights = notes.RootElement
                .GetProperty("wireProduction")
                .GetProperty("deploymentHeights");
            Assert.AreEqual(17729309u, heights.GetProperty("forcedInclusion").GetUInt32());
            Assert.AreEqual(17729307u, heights.GetProperty("sharedBridge").GetUInt32());
            Assert.AreEqual(17729303u, heights.GetProperty("messageRouter").GetUInt32());
            Assert.AreEqual(
                17729309u,
                heights.GetProperty("all").GetProperty("ForcedInclusion").GetUInt32());
            Assert.AreEqual(
                0,
                notes.RootElement.GetProperty("wireProduction")
                    .GetProperty("missingDeploymentHeights").GetArrayLength());
            Assert.IsTrue(
                notes.RootElement.GetProperty("wireProduction")
                    .GetProperty("heightsInPluginConfig").GetBoolean());

            var settlement = JsonDocument.Parse(File.ReadAllText(Path.Combine(
                dir, "Plugins", "Neo.Plugins.L2Settlement", "config.json")));
            var plugin = settlement.RootElement.GetProperty("PluginConfiguration");
            Assert.AreEqual(17729309u, plugin.GetProperty("ForcedInclusionDeploymentHeight").GetUInt32());
            Assert.AreEqual(17729307u, plugin.GetProperty("SharedBridgeDeploymentHeight").GetUInt32());
            Assert.AreEqual(17729303u, plugin.GetProperty("MessageRouterDeploymentHeight").GetUInt32());

            Assert.IsTrue(Directory.Exists(Path.Combine(
                dir, NeoHubDeployReport.RelativeProofWitnessStoreDir)));
            Assert.IsTrue(Directory.Exists(Path.Combine(
                dir, NeoHubDeployReport.RelativeForcedInclusionEventStoreDir)));
            var stores = notes.RootElement.GetProperty("wireProduction")
                .GetProperty("recommendedDurableStores");
            Assert.AreEqual(
                NeoHubDeployReport.RelativeProofWitnessStoreDir,
                stores.GetProperty("proofWitnessStore").GetString());
            Assert.AreEqual(
                NeoHubDeployReport.RelativeForcedInclusionEventStoreDir,
                stores.GetProperty("forcedInclusionEventStore").GetString());
            Assert.AreEqual(
                NeoHubDeployReport.RelativeLocalDaStoreDir,
                stores.GetProperty("localDaStore").GetString());
            Assert.AreEqual(
                "PersistentDAWriter.OpenLocalFromChainDirectory(chainDirectory)",
                stores.GetProperty("localDaOpenHelper").GetString());
            Assert.AreEqual(
                "MetricsEmittingDAWriter / MetricsEmittingProductionDAWriter (LocalHost Open)",
                stores.GetProperty("daMetricsWrap").GetString());
            Assert.AreEqual(
                "L2SettlementPlugin.WireProductionFromLayout(chainDir, layout, batch, executor, da, prover, signer)",
                stores.GetProperty("wireProductionFromLayout").GetString());
            Assert.AreEqual(
                "ProductionDepositSource / ProductionMessageRouter / "
                + "ProductionForcedInclusionSource / ProductionForcedInclusionFinalizer / "
                + "ProductionSettlementClient / ProductionTransactionSender",
                stores.GetProperty("wireProductionPublicAccessors").GetString());
            Assert.AreEqual(
                "MultisigLocalHostComposition.Open(chainDir, executor, signers, signer, "
                + "startMetricsHttp?) + InMemoryL2RpcStore (data/rpc/proofs)",
                stores.GetProperty("multisigLocalHostComposition").GetString());
            Assert.AreEqual(
                "OptimisticLocalHostComposition.Open(chainDir, executor, sequencerKey, bondContract, bondTxHash, signer, "
                + "startMetricsHttp?) + InMemoryL2RpcStore (data/rpc/proofs)",
                stores.GetProperty("optimisticLocalHostComposition").GetString());
            Assert.AreEqual(
                "ZkLocalHostComposition.Open(chainDir, executorPath, executorSha256, vk, productionDaWriter, signer, "
                + "startMetricsHttp?) + InMemoryL2RpcStore (data/rpc/proofs)",
                stores.GetProperty("zkLocalHostComposition").GetString());
            Assert.AreEqual(
                "L2BatchPlugin.HasSealedBatchSink / HasDepositSource / HasMessageRouter / "
                + "HasForcedInclusionSource / MaxForcedTransactionsPerBatch / MaxL1MessagesPerBatch",
                stores.GetProperty("batchInboxVisibility").GetString());
            Assert.AreEqual(
                "L2SettlementPlugin.IsProductionWired / IsEnabled / L1FinalityDepth / "
                + "ForcedInclusionDeploymentHeight / SharedBridgeDeploymentHeight / "
                + "MessageRouterDeploymentHeight after WireProduction",
                stores.GetProperty("settlementProductionWired").GetString());
            Assert.AreEqual(
                "L2SettlementPlugin.ProductionTransactionSender after WireProduction",
                stores.GetProperty("settlementProductionTransactionSender").GetString());
            Assert.AreEqual(
                "L2SettlementPlugin.ProductionForcedInclusionFinalizer after WireProduction",
                stores.GetProperty("settlementProductionForcedInclusionFinalizer").GetString());
            Assert.AreEqual(
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
                stores.GetProperty("localHostProductionSurfaces").GetString());
            Assert.AreEqual(
                "LocalHost.ChainId / BatcherConfiguredChainId / ProofType / DaMode / "
                + "HasSealedBatchSink / NextExpectedBlock / ProcessCommittedBlock / "
                + "IsOperatorReady / PeekSharedBridgeDeposits / GetOperatorStatusAsync",
                stores.GetProperty("localHostReadiness").GetString());
            Assert.AreEqual(
                "LocalHost.BatcherConfiguredChainId / NextExpectedBlock / "
                + "LastAcknowledgedBatchNumber / LastAcknowledgedBlock / "
                + "NextBatchNumber / HasPendingSealedBatch / PendingSealedBatchNumber / "
                + "PendingSealedBatchLastBlock / IsBatcherEnabled / MaxBlocksPerBatch / MaxTransactionsPerBatch / MaxBatchAgeMillis / "
                + "MaxForcedTransactionsPerBatch / MaxL1MessagesPerBatch / HasBatchDepositSource / "
                + "HasBatchMessageRouter / HasBatchForcedInclusionSource / HasBatchProver / PendingSealedBatch / HasOpenBatch / "
                + "InProgressTxCount / OpenBatchFirstBlock / OpenBatchLastBlock / OpenBatchBlockCount / "
                + "OpenBatchL1MessageCount / OpenBatchL2ToL1MessageCount / OpenBatchL2ToL2MessageCount / "
                + "OpenBatchForcedInclusionCount / OpenBatchWithdrawalCount / ProcessCommittedBlock / "
                + "TryRetryPendingSealedBatch / OnBatchSealed",
                stores.GetProperty("localHostBatcherHelpers").GetString());
            Assert.AreEqual(
                "LocalHost.GetLatestRpcStateRoot / GetRpcStateRootAtBatch / AddRpcBatch / "
                + "FinalizeRpcBatch / RecordRpcDeposit / GetRpcL1DepositStatus / GetRpcBatch / "
                + "GetRpcBatchStatus / RegisterRpcAsset / GetRpcCanonicalAsset / "
                + "GetRpcBridgedAsset / RecordRpcWithdrawalProof / RecordRpcMessageProof / "
                + "GetRpcWithdrawalProof / GetRpcMessageProof",
                stores.GetProperty("localHostRpcStoreHelpers").GetString());
            Assert.AreEqual(
                "LocalHost.MessageOutbox / HasMessageOutbox / MessageOutboxL2ToL1Root / "
                + "MessageOutboxL2ToL2Root / EnqueueOutboundMessagesAsync / "
                + "RecordMessageRouterFinalizedProof / GetMessageRouterProofAsync / "
                + "RegisterInboundMessageNonce / InvalidateInboundMessageCache / "
                + "KnownInboundNonceCount",
                stores.GetProperty("localHostMessageRouterHelpers").GetString());
            Assert.AreEqual(
                "LocalHost.RegisterForcedInclusionNonce / InvalidateForcedInclusionCache / "
                + "KnownForcedInclusionNonceCount / HasBatchForcedInclusionSource",
                stores.GetProperty("localHostForcedInclusionHelpers").GetString());
            Assert.AreEqual(
                "LocalHost.PublishDaAsync / IsDaAvailableAsync / SupportsLocalDaReader / "
                + "CreateLocalDaReader (Multisig/Optimistic local DA)",
                stores.GetProperty("localHostDaHelpers").GetString());
            Assert.AreEqual(
                "LocalHost.CaptureMetricsSnapshot / ExportPrometheusMetrics",
                stores.GetProperty("localHostMetricsExport").GetString());
            Assert.AreEqual(
                "LocalHost.IsMetricsEnabled / MetricsConfiguredPort / MetricsBindAddress / "
                + "MetricsMaxConcurrentConnections / MetricsBoundPort / IsMetricsHttpListening",
                stores.GetProperty("localHostMetricsSettings").GetString());
            Assert.AreEqual(
                "LocalHost.BridgeAssetRegistry / RegisterBridgeAsset / "
                + "SnapshotBridgeAssets / BridgeAssetCount",
                stores.GetProperty("localHostBridgeRegistry").GetString());
            Assert.AreEqual(
                "LocalHost.ProcessDeposit / ProcessReadyDeposits / ScanSharedBridgeDepositsAsync / "
                + "ScanAndProcessReadyDepositsAsync / HasConsumedDeposit / ConsumedDepositCount / "
                + "DepositSourceReadyCount / DepositSourceReservedCount / "
                + "DepositSourceSoftConsumedCount / "
                + "StageWithdrawal / StagedWithdrawalCount / SealWithdrawalBatch / ProveAsync",
                stores.GetProperty("localHostBridgeProcessors").GetString());
            Assert.AreEqual(
                "LocalHost.HasOverdueForcedInclusionAsync(nowUnixSeconds)",
                stores.GetProperty("localHostForcedInclusionOverdue").GetString());
            Assert.AreEqual(
                "LocalHost.WriteOperatorStatusAsync(path) → LocalHostOperatorStatusDocument JSON",
                stores.GetProperty("localHostWriteOperatorStatus").GetString());
            Assert.AreEqual(
                "LocalHost.WritePrometheusMetricsAsync(path) → Prometheus text file",
                stores.GetProperty("localHostWritePrometheusMetrics").GetString());
            Assert.AreEqual(
                "GatewayHostComposition.WriteOperatorStatusAsync(path) → GatewayHostOperatorStatusDocument JSON",
                stores.GetProperty("gatewayHostWriteOperatorStatus").GetString());
            Assert.AreEqual(
                "GatewayHostComposition.WritePrometheusMetricsAsync(path) / ExportPrometheusMetrics "
                + "(when Metrics is IMetricsSource)",
                stores.GetProperty("gatewayHostWritePrometheusMetrics").GetString());
            Assert.AreEqual(
                "LocalHost.ReconcileAsync / SubmitNextAsync / GetPendingCountAsync / "
                + "PersistAsync / EnqueueAsync",
                stores.GetProperty("localHostSettleHelpers").GetString());
            Assert.AreEqual(
                "LocalHost.GetRecoveryStatusAsync / RecoverPoisonedBatchAsync / "
                + "GetTrackedForcedInclusionNoncesAsync / GetLatestCheckpointAsync / "
                + "GetLatestDurableCheckpointAsync / GetInitialStateRootAsync / "
                + "LatestCheckpointBatchNumber / LatestCheckpointLastBlock / "
                + "LatestCheckpointPostStateRoot / InitialStateRoot",
                stores.GetProperty("localHostRecoveryHelpers").GetString());
            Assert.AreEqual(
                "LocalHost.StartMetricsHttp(portOverride?, readiness?) / StopMetricsHttp / "
                + "Open startMetricsHttp",
                stores.GetProperty("localHostStartMetricsHttp").GetString());
            Assert.AreEqual(
                "LocalHost.CreateRpcPlugin() then NeoSystem.AddService(RpcStore)",
                stores.GetProperty("localHostCreateRpcPlugin").GetString());
            Assert.AreEqual(
                "LocalHost Open startMetricsHttp → WithReadinessCheck(HasSealedBatchSink)",
                stores.GetProperty("metricsReadiness").GetString());
            Assert.AreEqual(
                "GatewayHostComposition.OpenMerkle(chainDir, proofProver, signer, replayDomain, vk, metrics?)",
                stores.GetProperty("gatewayHostCompositionMerkle").GetString());
            Assert.AreEqual(
                "GatewayHostComposition.OpenMultisig(chainDir, signers, threshold, proofProver, signer, replayDomain, vk, metrics?)",
                stores.GetProperty("gatewayHostCompositionMultisig").GetString());
            Assert.AreEqual(
                "GatewayHostComposition.OpenSp1(chainDir, gatewayVk, signer, replayDomain, vk, metrics?)",
                stores.GetProperty("gatewayHostCompositionSp1").GetString());
            Assert.AreEqual(
                "GatewayHostComposition.HasPendingPublication / PendingPublicationEpoch / "
                + "AggregatorPendingCount / HasDurableOutbox / IsPublicationConfigured / "
                + "IsEnabled / MaxAutomaticRetries / ProofSystem / AggregationBackendId / "
                + "ExpectedNetwork / HasL1RpcEndpoint / ReplayDomain / VerificationKeyId / "
                + "SettlementManagerHash / MessageRouterHash / "
                + "OutboxStatus / Aggregator / ReceiveBatch / "
                + "PullAggregate (fails closed with durable outbox) / PublishAggregateAsync / "
                + "RecoverPoisonedPublication / GetOperatorStatus / WriteOperatorStatusAsync / "
                + "Metrics / CaptureMetricsSnapshot / ExportPrometheusMetrics / "
                + "WritePrometheusMetricsAsync",
                stores.GetProperty("gatewayHostOpsHelpers").GetString());
            Assert.AreEqual(
                "L2BatchPlugin.CreateFromChainDirectory / NextExpectedBlock / "
                + "HasPendingSealedBatch / PendingSealedBatch / HasOpenBatch / "
                + "InProgressTxCount / OpenBatchFirstBlock / OpenBatchLastBlock / "
                + "OpenBatchBlockCount / ProcessCommittedBlock / TryRetryPendingSealedBatch",
                stores.GetProperty("batchPluginFactory").GetString());
            Assert.AreEqual(
                "L2SettlementPlugin.CreateFromChainDirectory(chainDirectory)",
                stores.GetProperty("settlementPluginFactory").GetString());
            Assert.AreEqual(
                "L2BridgePlugin.CreateFromChainDirectory(chainDirectory)",
                stores.GetProperty("bridgePluginFactory").GetString());
            Assert.AreEqual(
                "L2ProverPlugin.CreateFromChainDirectory(chainDirectory)",
                stores.GetProperty("proverPluginFactory").GetString());
            Assert.IsTrue(File.Exists(Path.Combine(
                dir, "Plugins", "Neo.Plugins.L2Bridge", "config.json")));
            var bridgeCfg = JsonDocument.Parse(File.ReadAllText(Path.Combine(
                dir, "Plugins", "Neo.Plugins.L2Bridge", "config.json")));
            Assert.AreEqual(
                20260716u,
                bridgeCfg.RootElement.GetProperty("PluginConfiguration")
                    .GetProperty("ChainId").GetUInt32());
            Assert.AreEqual(
                "L2MetricsPlugin.CreateFromChainDirectory(chainDirectory)",
                stores.GetProperty("metricsPluginFactory").GetString());
            Assert.AreEqual(
                "L2DAPlugin.CreateLocalFromChainDirectory(chainDirectory)",
                stores.GetProperty("localDaPluginFactory").GetString());
            Assert.AreEqual(
                "L2GatewayPlugin.CreateDurableFromChainDirectory(chainDirectory, aggregator)",
                stores.GetProperty("gatewayPluginFactory").GetString());
            Assert.AreEqual(
                "L2GatewayPlugin.CreateMerkleDurableFromChainDirectory(chainDirectory)",
                stores.GetProperty("gatewayPluginMerkleDurable").GetString());
            Assert.AreEqual(
                "L2GatewayPlugin.CreateMultisigDurableFromChainDirectory(chainDirectory, signers, threshold)",
                stores.GetProperty("gatewayPluginMultisigDurable").GetString());
            Assert.AreEqual(
                "L2GatewayPlugin.CreateSp1DurableFromChainDirectory(chainDirectory)",
                stores.GetProperty("gatewayPluginSp1Durable").GetString());
            Assert.AreEqual(
                "L2GatewayPlugin.CreateFromChainDirectory(chainDirectory)",
                stores.GetProperty("gatewayPluginSettingsOnly").GetString());
            Assert.AreEqual(
                "ProofBoundRpcGlobalRootPublisher.OpenFromChainDirectory(chainDirectory, signer|signAndSend)",
                stores.GetProperty("gatewayPublisherFromChainDirectory").GetString());
            Assert.AreEqual(
                "ProofBoundRpcGlobalRootPublisher.CreateSignAndSend(rpcTransactionSender)",
                stores.GetProperty("gatewayPublisherSignAndSend").GetString());
            Assert.AreEqual(
                NeoHubDeployReport.RelativeGatewayProverQueueDir,
                stores.GetProperty("gatewayProverQueue").GetString());
            Assert.AreEqual(
                "Sp1GatewayProofProver.OpenFromChainDirectory(chainDirectory, gatewayVerificationKey)",
                stores.GetProperty("gatewaySp1ProverFromChainDirectory").GetString());
            Assert.AreEqual(
                NeoHubDeployReport.RelativeProverInboxDir,
                stores.GetProperty("batchProverInbox").GetString());
            Assert.AreEqual(
                "Sp1BatchProofProver.OpenFromChainDirectory(chainDirectory, verificationKeyId)",
                stores.GetProperty("batchSp1ProverFromChainDirectory").GetString());
            Assert.AreEqual(
                "L2ProverPlugin.CreateMultisigWiredFromChainDirectory(chainDirectory, signers)",
                stores.GetProperty("proverPluginMultisigWired").GetString());
            Assert.AreEqual(
                "L2ProverPlugin.CreateZkWiredFromChainDirectory(chainDirectory, verificationKeyId)",
                stores.GetProperty("proverPluginZkWired").GetString());
            Assert.AreEqual(
                "L2ProverPlugin.CreateOptimisticWiredFromChainDirectory(chainDirectory, sequencerKey, bondContract, bondTxHash)",
                stores.GetProperty("proverPluginOptimisticWired").GetString());
            Assert.AreEqual(
                "L1DeployedEndpoints.FromChainDirectory(chainDirectory)",
                stores.GetProperty("l1DeployedEndpoints").GetString());
            Assert.AreEqual(
                "gateway.ConfigureGlobalRootPublicationFromChainDirectory(chainDir, prover, publisher, replayDomain, vk)",
                stores.GetProperty("gatewayConfigurePublicationFromChainDirectory").GetString());
            Assert.AreEqual(
                "L2SettlementPlugin.CreateDepositSourceFromChainDirectory(chainDirectory)",
                stores.GetProperty("depositSourceFromChainDirectory").GetString());
            Assert.AreEqual(
                "RpcSharedBridgeDepositSource.OpenFromChainDirectory(chainDir, rpc, sharedBridge, chainId, l2Bridge, startHeight)",
                stores.GetProperty("depositSourceOpenHelper").GetString());
            Assert.AreEqual(
                "L2SettlementPlugin.CreateForcedInclusionSourceFromChainDirectory(chainDirectory)",
                stores.GetProperty("forcedInclusionSourceFromChainDirectory").GetString());
            Assert.AreEqual(
                "L2SettlementPlugin.CreateMessageRouterFromChainDirectory(chainDirectory)",
                stores.GetProperty("messageRouterFromChainDirectory").GetString());
            Assert.AreEqual(
                "L1InboxFromChainDirectory.Open(chainDirectory) / WireL1InboxFromChainDirectory(chainDir, batch)",
                stores.GetProperty("l1InboxFromChainDirectory").GetString());
            Assert.AreEqual(
                NeoHubDeployReport.RelativeRpcProofStoreDir,
                stores.GetProperty("rpcProofStore").GetString());
            Assert.AreEqual(
                NeoHubDeployReport.RelativeGatewayOutboxStoreDir,
                stores.GetProperty("gatewayOutboxStore").GetString());
            Assert.AreEqual(
                "InMemoryL2RpcStore.OpenFromChainDirectory(chainDirectory)",
                stores.GetProperty("rpcStoreOpenHelper").GetString());
            Assert.AreEqual(
                "PersistentGatewayOutbox.OpenFromChainDirectory(chainDirectory)",
                stores.GetProperty("gatewayOutboxOpenHelper").GetString());
            Assert.AreEqual(
                "Sp1SettlementExecutionStack.CreateFromChainDirectory(chainDir, state, executorPath, executorSha256, vk)",
                stores.GetProperty("sp1StackFromChainDirectory").GetString());
            Assert.IsTrue(Directory.Exists(Path.Combine(
                dir, NeoHubDeployReport.RelativeRpcProofStoreDir)));
            Assert.IsTrue(Directory.Exists(Path.Combine(
                dir, NeoHubDeployReport.RelativeGatewayOutboxStoreDir)));
            Assert.IsTrue(File.Exists(Path.Combine(
                dir, "Plugins", "Neo.Plugins.L2Metrics", "config.json")));
            Assert.IsTrue(File.Exists(Path.Combine(
                dir, "Plugins", "Neo.Plugins.L2Gateway", "config.json")));
            var gatewayCfg = JsonDocument.Parse(File.ReadAllText(Path.Combine(
                dir, "Plugins", "Neo.Plugins.L2Gateway", "config.json")));
            // MinimalReportJson has no chain.config → default gatewayEnabled true.
            Assert.IsTrue(gatewayCfg.RootElement.GetProperty("PluginConfiguration")
                .GetProperty("Enabled").GetBoolean());
            Assert.AreEqual(
                3,
                gatewayCfg.RootElement.GetProperty("PluginConfiguration")
                    .GetProperty("MaxAutomaticRetries").GetInt32());
            Assert.IsTrue(File.Exists(Path.Combine(
                dir, "Plugins", "Neo.Plugins.L2DA", "config.json")));
            var daCfg = JsonDocument.Parse(File.ReadAllText(Path.Combine(
                dir, "Plugins", "Neo.Plugins.L2DA", "config.json")));
            // MinimalReportJson has no chain.config → default Local (255).
            Assert.AreEqual(
                (byte)DAMode.Local,
                daCfg.RootElement.GetProperty("PluginConfiguration")
                    .GetProperty("DAMode").GetByte());
            Assert.AreEqual(
                NeoHubDeployReport.RelativeLocalDaStoreDir,
                daCfg.RootElement.GetProperty("PluginConfiguration")
                    .GetProperty("DataDirectory").GetString());
            Assert.AreEqual(
                NeoHubDeployReport.RelativeStateDir,
                stores.GetProperty("stateStore").GetString());
            Assert.AreEqual(
                "Sp1SettlementExecutionStack.OpenStateFromChainDirectory(chainDirectory)",
                stores.GetProperty("stateOpenHelper").GetString());
            Assert.AreEqual(
                "LocalKeyTransactionSigner.FromEnvironmentVariableWithGlobalScope()",
                stores.GetProperty("nestedNep17Signer").GetString());
            Assert.IsTrue(Directory.Exists(Path.Combine(
                dir, NeoHubDeployReport.RelativeLocalDaStoreDir)));
            Assert.IsTrue(File.Exists(Path.Combine(
                dir, "Plugins", "Neo.Plugins.L2Prover", "config.json")));
            var proverCfg = JsonDocument.Parse(File.ReadAllText(Path.Combine(
                dir, "Plugins", "Neo.Plugins.L2Prover", "config.json")));
            // MinimalReportJson has no chain.config.json → default Multisig (1).
            Assert.AreEqual(
                (byte)ProofType.Multisig,
                proverCfg.RootElement.GetProperty("PluginConfiguration")
                    .GetProperty("ProofType").GetByte());
            Assert.AreEqual(
                plugin.GetProperty("ProofType").GetByte(),
                proverCfg.RootElement.GetProperty("PluginConfiguration")
                    .GetProperty("ProofType").GetByte(),
                "prover and settlement ProofType must match materialization");
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void EnsureSettlementStoreDirectories_Idempotent()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-n4-store-dirs-" + Guid.NewGuid().ToString("N"));
        try
        {
            var first = NeoHubDeployReport.EnsureSettlementStoreDirectories(dir);
            var second = NeoHubDeployReport.EnsureSettlementStoreDirectories(dir);
            Assert.AreEqual(9, first.Count);
            Assert.AreEqual(9, second.Count);
            CollectionAssert.Contains(first.ToList(), NeoHubDeployReport.RelativeLocalDaStoreDir);
            CollectionAssert.Contains(first.ToList(), NeoHubDeployReport.RelativeRpcProofStoreDir);
            CollectionAssert.Contains(first.ToList(), NeoHubDeployReport.RelativeGatewayOutboxStoreDir);
            CollectionAssert.Contains(first.ToList(), NeoHubDeployReport.RelativeGatewayProverQueueDir);
            CollectionAssert.Contains(first.ToList(), NeoHubDeployReport.RelativeProverInboxDir);
            foreach (var relative in first)
                Assert.IsTrue(Directory.Exists(Path.Combine(dir, relative)));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void Parse_MissingChainRegistry_FailsClosed()
    {
        var json = MinimalReportJson().Replace("ChainRegistry", "NotChainRegistry");
        var ex = Assert.ThrowsExactly<ArgumentException>(() => NeoHubDeployReport.Parse(json));
        StringAssert.Contains(ex.Message, "ChainRegistry");
    }

    [TestMethod]
    public void Parse_ZeroContractHash_FailsClosed()
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(() =>
            NeoHubDeployReport.Parse(MinimalReportJson(chainRegistry: "0x" + new string('0', 40))));
        StringAssert.Contains(ex.Message, "zero");
    }

    [TestMethod]
    public void WriteOperatorArtifacts_EmitsDeployedAndSettlementSnippet()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-n4-deploy-report-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "chain.config.json"), """
                {
                  "chainId": 20260716,
                  "proofType": "Zk",
                  "securityLevel": "Validity",
                  "daMode": "L1",
                  "sequencerModel": "DbftCommittee",
                  "exitModel": "Permissionless",
                  "gatewayEnabled": true,
                  "permissionlessExit": true
                }
                """);
            var report = NeoHubDeployReport.Parse(MinimalReportJson());
            report.WriteOperatorArtifacts(dir);

            var deployedPath = Path.Combine(dir, "l1.deployed.json");
            Assert.IsTrue(File.Exists(deployedPath));
            using var deployed = JsonDocument.Parse(File.ReadAllText(deployedPath));
            Assert.AreEqual(
                report.SettlementManager.ToString(),
                deployed.RootElement.GetProperty("settlementManager").GetString());

            var settlementPath = Path.Combine(
                dir, "Plugins", "Neo.Plugins.L2Settlement", "config.json");
            Assert.IsTrue(File.Exists(settlementPath));
            Assert.IsTrue(File.Exists(Path.Combine(
                dir, "Plugins", "Neo.Plugins.L2Settlement", "config.from-deploy.json")));
            using var settlement = JsonDocument.Parse(File.ReadAllText(settlementPath));
            var plugin = settlement.RootElement.GetProperty("PluginConfiguration");
            Assert.AreEqual(20260716u, plugin.GetProperty("ChainId").GetUInt32());
            Assert.AreEqual((byte)ProofType.Zk, plugin.GetProperty("ProofType").GetByte());
            Assert.AreEqual(
                report.MessageRouter.ToString(),
                plugin.GetProperty("MessageRouterHash").GetString());
            Assert.AreEqual(
                report.SharedBridge.ToString(),
                plugin.GetProperty("SharedBridgeHash").GetString());

            var batchPath = Path.Combine(dir, "Plugins", "Neo.Plugins.L2Batch", "config.json");
            Assert.IsTrue(File.Exists(batchPath));
            using var batch = JsonDocument.Parse(File.ReadAllText(batchPath));
            Assert.AreEqual(
                20260716u,
                batch.RootElement.GetProperty("PluginConfiguration").GetProperty("ChainId").GetUInt32());

            Assert.IsTrue(File.Exists(Path.Combine(dir, "l1.wireproduction-notes.json")));
            Assert.AreEqual((byte)ProofType.Zk, NeoHubDeployReport.ResolveProofTypeByte(dir));
            Assert.IsTrue(NeoHubDeployReport.ResolveGatewayEnabled(dir));
            var gatewayPath = Path.Combine(dir, "Plugins", "Neo.Plugins.L2Gateway", "config.json");
            Assert.IsTrue(File.Exists(gatewayPath));
            using var gateway = JsonDocument.Parse(File.ReadAllText(gatewayPath));
            Assert.IsTrue(gateway.RootElement.GetProperty("PluginConfiguration")
                .GetProperty("Enabled").GetBoolean());
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void ResolveGatewayEnabled_DefaultsTrueWithoutConfig()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-n4-gw-default-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            Assert.IsTrue(NeoHubDeployReport.ResolveGatewayEnabled(dir));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void ResolveGatewayEnabled_HonorsChainConfigFalse()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-n4-gw-false-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "chain.config.json"), """
                {
                  "chainId": 1,
                  "proofType": "Multisig",
                  "securityLevel": "Attested",
                  "daMode": "Local",
                  "sequencerModel": "Single",
                  "exitModel": "Permissioned",
                  "gatewayEnabled": false,
                  "permissionlessExit": false
                }
                """);
            Assert.IsFalse(NeoHubDeployReport.ResolveGatewayEnabled(dir));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void ResolveProofTypeByte_DefaultsToMultisigWithoutConfig()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-n4-proof-type-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            Assert.AreEqual((byte)ProofType.Multisig, NeoHubDeployReport.ResolveProofTypeByte(dir));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void Parse_RealTestnetEvidenceReport_IfPresent()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "docs", "audit", "testnet-deployment-20260716-live.json"));
        if (!File.Exists(path))
            Assert.Inconclusive($"repo evidence file not found at {path}");

        var report = NeoHubDeployReport.Load(path);
        Assert.AreEqual(20260716u, report.L2ChainId);
        Assert.AreEqual(24, report.Contracts.Count);
        Assert.AreEqual(
            UInt160.Parse("0x65201c5415f6fc13093ad7169c556351c2392d23"),
            report.ChainRegistry);
    }
}
