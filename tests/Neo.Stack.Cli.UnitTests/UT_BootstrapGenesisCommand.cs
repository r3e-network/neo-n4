using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Neo;
using Neo.Stack.Cli.Commands;

namespace Neo.Stack.Cli.UnitTests;

[TestClass]
public class UT_BootstrapGenesisCommand
{
    private string _tempDir = null!;

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "neo-n4-bootstrap-" + Guid.NewGuid().ToString("N"));
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private void SeedChain(uint chainId = 20260716)
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "chain.config.json"), $$"""
            {
              "chainId": {{chainId}},
              "template": "zk-rollup",
              "vm": "neovm2-riscv",
              "chainMode": "L2RiscV",
              "daMode": "L1",
              "proofType": "Zk",
              "securityLevel": "Validity",
              "sequencerModel": "DbftCommittee",
              "exitModel": "Permissionless",
              "gatewayEnabled": true,
              "permissionlessExit": true,
              "milestonePerBlockMs": 5000,
              "validators": []
            }
            """);
    }

    [TestMethod]
    public void Bootstrap_Ephemeral_WritesNonZeroManifest()
    {
        SeedChain();
        var rc = BootstrapGenesisCommand.Run(
        [
            "--chain-id", "20260716",
            "--output", _tempDir,
            "--ephemeral",
        ]);
        Assert.AreEqual(0, rc);
        var manifestPath = Path.Combine(_tempDir, BootstrapGenesisCommand.ManifestFileName);
        Assert.IsTrue(File.Exists(manifestPath));
        var root = BootstrapGenesisCommand.ReadInitialStateRoot(manifestPath);
        Assert.AreNotEqual(UInt256.Zero, root);

        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        Assert.AreEqual(20260716u, doc.RootElement.GetProperty("chainId").GetUInt32());
        Assert.AreEqual(
            "InMemoryKeyValueStore",
            doc.RootElement.GetProperty("storeKind").GetString());
    }

    [TestMethod]
    public void Bootstrap_Ephemeral_IsIdempotentUnderForce()
    {
        SeedChain();
        Assert.AreEqual(0, BootstrapGenesisCommand.Run(
        [
            "--chain-id", "20260716", "--output", _tempDir, "--ephemeral",
        ]));
        var first = BootstrapGenesisCommand.ReadInitialStateRoot(
            Path.Combine(_tempDir, BootstrapGenesisCommand.ManifestFileName));

        Assert.AreEqual(0, BootstrapGenesisCommand.Run(
        [
            "--chain-id", "20260716", "--output", _tempDir, "--ephemeral", "--force",
        ]));
        var second = BootstrapGenesisCommand.ReadInitialStateRoot(
            Path.Combine(_tempDir, BootstrapGenesisCommand.ManifestFileName));
        Assert.AreEqual(first, second, "default bootstrap settings must yield a stable genesis root");
    }

    [TestMethod]
    public void Bootstrap_RefusesExistingManifestWithoutForce()
    {
        SeedChain();
        Assert.AreEqual(0, BootstrapGenesisCommand.Run(
        [
            "--chain-id", "20260716", "--output", _tempDir, "--ephemeral",
        ]));
        Assert.AreEqual(3, BootstrapGenesisCommand.Run(
        [
            "--chain-id", "20260716", "--output", _tempDir, "--ephemeral",
        ]));
    }

    [TestMethod]
    public void Bootstrap_MissingChainDir_FailsClosed()
    {
        Assert.AreEqual(1, BootstrapGenesisCommand.Run(
        [
            "--chain-id", "20260716",
            "--output", Path.Combine(_tempDir, "missing"),
            "--ephemeral",
        ]));
    }

    [TestMethod]
    public async Task Register_FromDeployReportAndGenesisManifest_EmitsConfigBytes()
    {
        SeedChain();
        Assert.AreEqual(0, BootstrapGenesisCommand.Run(
        [
            "--chain-id", "20260716", "--output", _tempDir, "--ephemeral",
        ]));
        var reportPath = Path.Combine(_tempDir, "deploy-report.json");
        File.WriteAllText(reportPath, """
            {
              "rpc": "https://n3seed1.ngd.network:20332/",
              "network": 894710606,
              "ownerAddress": "NLtL2v28d7TyMEaXcPqtekunkFRksJ7wxu",
              "ownerScriptHash": "0x13ef519c362973f9a34648a9eac5b71250b2a80a",
              "l2ChainId": 20260716,
              "records": [
                {"category":"deploy","name":"ChainRegistry","status":"deployed","contractHash":"0x65201c5415f6fc13093ad7169c556351c2392d23"},
                {"category":"deploy","name":"VerifierRegistry","status":"deployed","contractHash":"0xe058ea01d6933f38bad1321d0407f514112016b1"},
                {"category":"deploy","name":"SharedBridge","status":"deployed","contractHash":"0xf2f5114b83dd6fed4ddcac0ff9966fd22a77b241"},
                {"category":"deploy","name":"MessageRouter","status":"deployed","contractHash":"0x3caf3c6e160b5aec2e07672dc37662b5998afe90"},
                {"category":"deploy","name":"SettlementManager","status":"deployed","contractHash":"0x11448868f1c14422506b9c2360051df34bcbbb51"},
                {"category":"deploy","name":"ForcedInclusion","status":"deployed","contractHash":"0x962829ae28e7f89e5de4b4672b167c8ae2ba55a9"}
              ]
            }
            """);

        var origOut = Console.Out;
        try
        {
            var sw = new StringWriter();
            Console.SetOut(sw);
            var rc = await RegisterChainCommand.RunAsync(
            [
                "--chain-id", "20260716",
                "--output", _tempDir,
                "--from-deploy-report", reportPath,
                "--genesis-manifest", Path.Combine(_tempDir, BootstrapGenesisCommand.ManifestFileName),
            ]);
            Assert.AreEqual(0, rc);
            var output = sw.ToString();
            StringAssert.Contains(output, "configBytes=<91 bytes>");
            var root = BootstrapGenesisCommand.ReadInitialStateRoot(
                Path.Combine(_tempDir, BootstrapGenesisCommand.ManifestFileName));
            StringAssert.Contains(output, root.ToString());
        }
        finally
        {
            Console.SetOut(origOut);
        }
    }
}
