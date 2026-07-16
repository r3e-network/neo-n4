namespace Neo.L2.Abstractions.UnitTests;

[TestClass]
public class UT_L2GenesisManifest
{
    private string _tempDir = null!;

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "neo-n4-genesis-mf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public void ReadInitialStateRoot_AcceptsCamelCase()
    {
        var root = new UInt256(Enumerable.Repeat((byte)0xab, 32).ToArray());
        File.WriteAllText(Path.Combine(_tempDir, L2GenesisManifest.RelativePath), $$"""
            {
              "schemaVersion": 1,
              "chainId": 20260716,
              "initialStateRoot": "{{root}}"
            }
            """);

        Assert.AreEqual(root, L2GenesisManifest.ReadInitialStateRootFromChainDirectory(_tempDir));
    }

    [TestMethod]
    public void ReadInitialStateRoot_AcceptsPascalCase()
    {
        var root = new UInt256(Enumerable.Repeat((byte)0xcd, 32).ToArray());
        File.WriteAllText(Path.Combine(_tempDir, "genesis-manifest.json"), $$"""
            { "InitialStateRoot": "{{root}}" }
            """);

        Assert.AreEqual(root, L2GenesisManifest.ReadInitialStateRoot(
            Path.Combine(_tempDir, "genesis-manifest.json")));
    }

    [TestMethod]
    public void ReadInitialStateRoot_ZeroRoot_FailsClosed()
    {
        File.WriteAllText(Path.Combine(_tempDir, "genesis-manifest.json"), """
            { "initialStateRoot": "0x0000000000000000000000000000000000000000000000000000000000000000" }
            """);

        var ex = Assert.ThrowsExactly<InvalidDataException>(
            () => L2GenesisManifest.ReadInitialStateRootFromChainDirectory(_tempDir));
        StringAssert.Contains(ex.Message, "non-zero");
    }

    [TestMethod]
    public void ReadInitialStateRoot_MissingFile_FailsClosed()
    {
        Assert.ThrowsExactly<FileNotFoundException>(
            () => L2GenesisManifest.ReadInitialStateRootFromChainDirectory(_tempDir));
    }
}
