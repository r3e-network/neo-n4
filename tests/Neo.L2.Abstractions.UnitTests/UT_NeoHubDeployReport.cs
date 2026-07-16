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
                "L2SettlementPlugin.WireProductionFromLayout(chainDir, layout, batch, executor, da, prover, signer)",
                stores.GetProperty("wireProductionFromLayout").GetString());
            Assert.AreEqual(
                "L2BatchPlugin.CreateFromChainDirectory(chainDirectory)",
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
                "L2GatewayPlugin.CreateFromChainDirectory(chainDirectory)",
                stores.GetProperty("gatewayPluginFactory").GetString());
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
            Assert.AreEqual(7, first.Count);
            Assert.AreEqual(7, second.Count);
            CollectionAssert.Contains(first.ToList(), NeoHubDeployReport.RelativeLocalDaStoreDir);
            CollectionAssert.Contains(first.ToList(), NeoHubDeployReport.RelativeRpcProofStoreDir);
            CollectionAssert.Contains(first.ToList(), NeoHubDeployReport.RelativeGatewayOutboxStoreDir);
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
