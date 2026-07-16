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
                  "contractHash": "0xf2f5114b83dd6fed4ddcac0ff9966fd22a77b241"
                },
                {
                  "category": "deploy",
                  "name": "MessageRouter",
                  "status": "deployed",
                  "contractHash": "0x3caf3c6e160b5aec2e07672dc37662b5998afe90"
                },
                {
                  "category": "deploy",
                  "name": "SettlementManager",
                  "status": "deployed",
                  "contractHash": "0x11448868f1c14422506b9c2360051df34bcbbb51"
                },
                {
                  "category": "deploy",
                  "name": "ForcedInclusion",
                  "status": "reused",
                  "contractHash": "0x962829ae28e7f89e5de4b4672b167c8ae2ba55a9"
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
