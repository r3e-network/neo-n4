using Neo.L2.Batch;

namespace Neo.Plugins.L2Settlement.UnitTests;

[TestClass]
public class UT_ProofWitnessPipelineProfile
{
    private string _tempDir = null!;

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "neo-n4-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public void LegacyFromChainDirectory_LoadsMultisigProfileFromManifestAndConfig()
    {
        var genesis = new UInt256(Enumerable.Repeat((byte)0x11, 32).ToArray());
        WriteSettlementConfig(ProofType.Multisig);
        WriteGenesisManifest(genesis);

        var profile = ProofWitnessPipelineProfile.LegacyFromChainDirectory(_tempDir);

        Assert.AreEqual(20260716u, profile.ChainId);
        Assert.AreEqual(ProofType.Multisig, profile.ProofType);
        Assert.AreEqual(WitnessProofSystem.None, profile.ProofSystem);
        Assert.AreEqual(genesis, profile.GenesisStateRoot);
        Assert.IsFalse(profile.RequireProductionDA);
        profile.Validate();
    }

    [TestMethod]
    public void LegacyFromChainDirectory_Optimistic_Supported()
    {
        var genesis = new UInt256(Enumerable.Repeat((byte)0x22, 32).ToArray());
        WriteSettlementConfig(ProofType.Optimistic);
        WriteGenesisManifest(genesis);

        var profile = ProofWitnessPipelineProfile.LegacyFromChainDirectory(_tempDir);
        Assert.AreEqual(ProofType.Optimistic, profile.ProofType);
        Assert.AreEqual(genesis, profile.GenesisStateRoot);
    }

    [TestMethod]
    public void LegacyFromChainDirectory_Zk_FailsClosed()
    {
        WriteSettlementConfig(ProofType.Zk);
        WriteGenesisManifest(new UInt256(Enumerable.Repeat((byte)0x33, 32).ToArray()));

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => ProofWitnessPipelineProfile.LegacyFromChainDirectory(_tempDir));
        StringAssert.Contains(ex.Message, "Sp1SettlementExecutionStack");
    }

    [TestMethod]
    public void LegacyFromChainDirectory_MissingGenesis_FailsClosed()
    {
        WriteSettlementConfig(ProofType.Multisig);
        Assert.ThrowsExactly<FileNotFoundException>(
            () => ProofWitnessPipelineProfile.LegacyFromChainDirectory(_tempDir));
    }

    [TestMethod]
    public void LegacyFromChainDirectory_MissingSettlementConfig_FailsClosed()
    {
        WriteGenesisManifest(new UInt256(Enumerable.Repeat((byte)0x44, 32).ToArray()));
        Assert.ThrowsExactly<FileNotFoundException>(
            () => ProofWitnessPipelineProfile.LegacyFromChainDirectory(_tempDir));
    }

    private void WriteSettlementConfig(ProofType proofType)
    {
        var dir = Path.Combine(_tempDir, "Plugins", "Neo.Plugins.L2Settlement");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "config.json"), $$"""
            {
              "PluginConfiguration": {
                "ChainId": 20260716,
                "L1RpcEndpoint": "http://127.0.0.1:10332",
                "ExpectedNetwork": 860833102,
                "SettlementManagerHash": "0x1111111111111111111111111111111111111111",
                "ForcedInclusionHash": "0x2222222222222222222222222222222222222222",
                "ProofType": {{(byte)proofType}},
                "Enabled": true
              }
            }
            """);
    }

    private void WriteGenesisManifest(UInt256 root)
    {
        File.WriteAllText(Path.Combine(_tempDir, L2GenesisManifest.RelativePath), $$"""
            {
              "schemaVersion": 1,
              "chainId": 20260716,
              "initialStateRoot": "{{root}}"
            }
            """);
    }
}
