using Neo.Cryptography.ECC;
using Neo.Wallets;

namespace Neo.L2.Sequencer.UnitTests;

[TestClass]
public class UT_SequencerCommitteeConfig
{
    private string _tempDir = null!;

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "neo-n4-committee-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public void ReadValidators_ParsesCompressedHexKeys()
    {
        var a = Key(1);
        var b = Key(2);
        WriteChainConfig(a, b);

        var keys = SequencerCommitteeConfig.ReadValidatorsFromChainDirectory(_tempDir);
        Assert.AreEqual(2, keys.Count);
        Assert.IsTrue(keys.Contains(a));
        Assert.IsTrue(keys.Contains(b));
    }

    [TestMethod]
    public void CreateStaticHashProvider_MatchesHasherCompute()
    {
        var a = Key(3);
        var b = Key(4);
        WriteChainConfig(a, b);

        var provider = SequencerCommitteeConfig.CreateStaticHashProviderFromChainDirectory(_tempDir);
        Assert.AreEqual(SequencerCommitteeHasher.Compute([a, b]), provider());
        Assert.AreEqual(provider(), provider()); // stable
    }

    [TestMethod]
    public void CreateStaticHashProvider_EmptyValidators_FailsClosed()
    {
        File.WriteAllText(Path.Combine(_tempDir, "chain.config.json"), """
            {
              "chainId": 1001,
              "validators": []
            }
            """);

        var ex = Assert.ThrowsExactly<InvalidDataException>(
            () => SequencerCommitteeConfig.CreateStaticHashProviderFromChainDirectory(_tempDir));
        StringAssert.Contains(ex.Message, "validators is empty");
    }

    [TestMethod]
    public void ReadValidators_Duplicates_FailClosed()
    {
        var a = Key(5);
        var hex = Convert.ToHexString(a.EncodePoint(true)).ToLowerInvariant();
        File.WriteAllText(Path.Combine(_tempDir, "chain.config.json"), $$"""
            {
              "chainId": 1001,
              "validators": [ "{{hex}}", "{{hex}}" ]
            }
            """);

        var ex = Assert.ThrowsExactly<InvalidDataException>(
            () => SequencerCommitteeConfig.ReadValidatorsFromChainDirectory(_tempDir));
        StringAssert.Contains(ex.Message, "duplicate");
    }

    [TestMethod]
    public void ReadValidators_MissingFile_FailsClosed()
    {
        Assert.ThrowsExactly<FileNotFoundException>(
            () => SequencerCommitteeConfig.ReadValidators(
                Path.Combine(_tempDir, "missing-chain.config.json")));
    }

    [TestMethod]
    public void CreateStaticHashProvider_InvalidKey_FailsClosed()
    {
        File.WriteAllText(Path.Combine(_tempDir, "chain.config.json"), """
            {
              "chainId": 1001,
              "validators": [ "not-a-key" ]
            }
            """);

        Assert.ThrowsExactly<InvalidDataException>(
            () => SequencerCommitteeConfig.CreateStaticHashProviderFromChainDirectory(_tempDir));
    }

    [TestMethod]
    public async Task CreateInMemoryProviderFromChainDirectory_RegistersActiveMembers()
    {
        var a = Key(6);
        var b = Key(7);
        WriteChainConfig(a, b);

        using var provider = SequencerCommitteeConfig.CreateInMemoryProviderFromChainDirectory(_tempDir);
        Assert.AreEqual(1001u, provider.ChainId);
        var committee = await provider.GetActiveCommitteeAsync();
        Assert.AreEqual(2, committee.Count);
        Assert.IsTrue(await provider.IsRegisteredAsync(a));
        Assert.IsTrue(await provider.IsRegisteredAsync(b));
        Assert.AreEqual(
            SequencerCommitteeHasher.Compute([a, b]),
            SequencerCommitteeHasher.Compute(committee));
    }

    [TestMethod]
    public void CreateInMemoryProvider_EmptyValidators_FailsClosed()
    {
        File.WriteAllText(Path.Combine(_tempDir, "chain.config.json"), """
            { "chainId": 1001, "validators": [] }
            """);
        Assert.ThrowsExactly<InvalidDataException>(
            () => SequencerCommitteeConfig.CreateInMemoryProviderFromChainDirectory(_tempDir));
    }

    private void WriteChainConfig(params ECPoint[] keys)
    {
        var hex = keys.Select(k => "\"" + Convert.ToHexString(k.EncodePoint(true)).ToLowerInvariant() + "\"");
        File.WriteAllText(Path.Combine(_tempDir, "chain.config.json"), $$"""
            {
              "chainId": 1001,
              "validators": [ {{string.Join(", ", hex)}} ]
            }
            """);
    }

    private static ECPoint Key(byte seed)
    {
        var privateKey = new byte[32];
        privateKey[^1] = seed;
        privateKey[^2] = (byte)(seed + 3);
        return new KeyPair(privateKey).PublicKey;
    }
}
