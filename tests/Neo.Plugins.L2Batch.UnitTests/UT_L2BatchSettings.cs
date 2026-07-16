using Microsoft.Extensions.Configuration;

namespace Neo.Plugins.L2Batch.UnitTests;

[TestClass]
public class UT_L2BatchSettings
{
    [TestMethod]
    public void From_EmptyConfig_HasExpectedDefaults()
    {
        var s = L2BatchSettings.From(BuildSection());
        Assert.AreEqual(0u, s.ChainId);
        Assert.AreEqual(50, s.MaxBlocksPerBatch);
        Assert.AreEqual(5_000, s.MaxTransactionsPerBatch);
        Assert.AreEqual(30_000, s.MaxBatchAgeMillis);
        Assert.IsTrue(s.Enabled);
    }

    [TestMethod]
    public void From_ExplicitChainIdZero_Rejected()
    {
        Assert.ThrowsExactly<InvalidDataException>(
            () => L2BatchSettings.From(BuildSection(("ChainId", "0"))));
    }

    [TestMethod]
    public void FromPluginConfigFile_LoadsDeployReportShape()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-n4-batch-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "config.json");
            File.WriteAllText(path, """
                {
                  "PluginConfiguration": {
                    "ChainId": 20260716,
                    "MaxBlocksPerBatch": 50,
                    "MaxTransactionsPerBatch": 5000,
                    "MaxBatchAgeMillis": 30000,
                    "Enabled": true
                  }
                }
                """);

            var s = L2BatchSettings.FromPluginConfigFile(path);
            Assert.AreEqual(20260716u, s.ChainId);
            Assert.AreEqual(50, s.MaxBlocksPerBatch);
            Assert.AreEqual(5_000, s.MaxTransactionsPerBatch);
            Assert.AreEqual(30_000, s.MaxBatchAgeMillis);
            Assert.IsTrue(s.Enabled);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void FromPluginConfigFile_RejectsNonPositiveThresholds()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-n4-batch-bad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "config.json");
            File.WriteAllText(path, """
                {
                  "PluginConfiguration": {
                    "ChainId": 1001,
                    "MaxBlocksPerBatch": 0
                  }
                }
                """);
            var ex = Assert.ThrowsExactly<InvalidDataException>(
                () => L2BatchSettings.FromPluginConfigFile(path));
            StringAssert.Contains(ex.Message, "MaxBlocksPerBatch");
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void FromChainDirectory_LiveDeployReport_LoadsChainId()
    {
        var reportPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "docs", "audit", "testnet-deployment-20260716-live.json"));
        if (!File.Exists(reportPath))
            Assert.Inconclusive($"repo evidence file not found at {reportPath}");

        var dir = Path.Combine(Path.GetTempPath(), "neo-n4-batch-live-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "chain.config.json"), """
                { "chainId": 20260716, "proofType": "Zk" }
                """);
            NeoHubDeployReport.Load(reportPath).WriteOperatorArtifacts(dir);

            var batch = L2BatchSettings.FromChainDirectory(dir);
            Assert.AreEqual(20260716u, batch.ChainId);
            Assert.AreEqual(50, batch.MaxBlocksPerBatch);
            Assert.AreEqual(5_000, batch.MaxTransactionsPerBatch);

            using var plugin = L2BatchPlugin.CreateFromChainDirectory(dir);
            Assert.AreEqual(20260716u, plugin.Settings.ChainId);
            Assert.AreEqual(batch.MaxBlocksPerBatch, plugin.Settings.MaxBlocksPerBatch);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void CreateFromChainDirectory_MissingConfig_FailsClosed()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-n4-batch-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.ThrowsExactly<FileNotFoundException>(
                () => L2BatchPlugin.CreateFromChainDirectory(dir));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void FromPluginConfigFile_MissingFile_FailsClosed()
    {
        Assert.ThrowsExactly<FileNotFoundException>(
            () => L2BatchSettings.FromPluginConfigFile(
                Path.Combine(Path.GetTempPath(), "missing-batch-" + Guid.NewGuid().ToString("N") + ".json")));
    }

    private static IConfigurationSection BuildSection(params (string Key, string? Value)[] entries)
    {
        var dict = entries.ToDictionary(e => "Batch:" + e.Key, e => e.Value);
        var root = new ConfigurationBuilder().AddInMemoryCollection(dict!).Build();
        return root.GetSection("Batch");
    }
}
