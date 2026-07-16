using Neo.L2;

namespace Neo.L2.Abstractions.UnitTests;

/// <summary>Host composition tests for <see cref="L1DeployedEndpoints"/>.</summary>
[TestClass]
public sealed class UT_L1DeployedEndpoints
{
    private static readonly UInt160 SettlementManager =
        UInt160.Parse("0x" + new string('b', 40));
    private static readonly UInt160 MessageRouter =
        UInt160.Parse("0x" + new string('a', 40));

    [TestMethod]
    public void FromChainDirectory_LoadsDeployedJson()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-n4-l1-ep-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "l1.deployed.json"), $$"""
                {
                  "rpc": "https://n3seed1.ngd.network:20332/",
                  "network": 894710606,
                  "settlementManager": "{{SettlementManager}}",
                  "messageRouter": "{{MessageRouter}}"
                }
                """);
            var ep = L1DeployedEndpoints.FromChainDirectory(dir);
            Assert.AreEqual("https://n3seed1.ngd.network:20332/", ep.RpcEndpoint.AbsoluteUri);
            Assert.AreEqual(SettlementManager, ep.SettlementManager);
            Assert.AreEqual(MessageRouter, ep.MessageRouter);
            Assert.AreEqual(894710606u, ep.ExpectedNetwork);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void FromChainDirectory_PrefersSettlementPluginConfig()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-n4-l1-ep-cfg-" + Guid.NewGuid().ToString("N"));
        var settlementDir = Path.Combine(dir, "Plugins", "Neo.Plugins.L2Settlement");
        Directory.CreateDirectory(settlementDir);
        try
        {
            File.WriteAllText(Path.Combine(settlementDir, "config.json"), $$"""
                {
                  "PluginConfiguration": {
                    "L1RpcEndpoint": "https://l1.example/",
                    "ExpectedNetwork": 894710606,
                    "SettlementManagerHash": "{{SettlementManager}}",
                    "MessageRouterHash": "{{MessageRouter}}"
                  }
                }
                """);
            File.WriteAllText(Path.Combine(dir, "l1.deployed.json"), """
                {
                  "rpc": "https://should-not-use.example/",
                  "settlementManager": "0xcccccccccccccccccccccccccccccccccccccccc",
                  "messageRouter": "0xdddddddddddddddddddddddddddddddddddddddd"
                }
                """);
            var ep = L1DeployedEndpoints.FromChainDirectory(dir);
            Assert.AreEqual("https://l1.example/", ep.RpcEndpoint.AbsoluteUri);
            Assert.AreEqual(SettlementManager, ep.SettlementManager);
            Assert.AreEqual(MessageRouter, ep.MessageRouter);
            Assert.AreEqual(894710606u, ep.ExpectedNetwork);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void FromChainDirectory_MissingEndpoints_FailsClosed()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-n4-l1-ep-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.ThrowsExactly<InvalidDataException>(
                () => L1DeployedEndpoints.FromChainDirectory(dir));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void FromChainDirectory_MissingRoot_FailsClosed()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-n4-l1-ep-missing-" + Guid.NewGuid().ToString("N"));
        Assert.ThrowsExactly<DirectoryNotFoundException>(
            () => L1DeployedEndpoints.FromChainDirectory(dir));
    }
}
