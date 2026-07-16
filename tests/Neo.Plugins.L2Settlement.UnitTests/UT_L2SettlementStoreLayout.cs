using Neo.L2.Persistence;

namespace Neo.Plugins.L2Settlement.UnitTests;

[TestClass]
public class UT_L2SettlementStoreLayout
{
    private string _tempDir = null!;

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "neo-n4-store-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public void Open_CreatesCanonicalDirsAndDurableStores()
    {
        using var layout = L2SettlementStoreLayout.Open(_tempDir);

        Assert.IsTrue(layout.ProofWitness.IsDurable);
        Assert.IsInstanceOfType<IDurableL2KeyValueStore>(layout.ForcedInclusionEvents);
        Assert.IsInstanceOfType<IDurableL2KeyValueStore>(layout.SharedBridgeDeposits);
        Assert.IsInstanceOfType<IDurableL2KeyValueStore>(layout.MessageRouterEvents);
        Assert.IsTrue(Directory.Exists(Path.Combine(
            _tempDir, NeoHubDeployReport.RelativeProofWitnessStoreDir)));
        Assert.IsTrue(Directory.Exists(Path.Combine(
            _tempDir, NeoHubDeployReport.RelativeForcedInclusionEventStoreDir)));
        Assert.IsTrue(Directory.Exists(Path.Combine(
            _tempDir, NeoHubDeployReport.RelativeSharedBridgeDepositEventStoreDir)));
        Assert.IsTrue(Directory.Exists(Path.Combine(
            _tempDir, NeoHubDeployReport.RelativeMessageRouterEventStoreDir)));
        StringAssert.Contains(
            layout.ResolvePath(NeoHubDeployReport.RelativeProofWitnessStoreDir),
            "proof-witness");
    }

    [TestMethod]
    public void Open_MissingChainDirectory_FailsClosed()
    {
        var missing = Path.Combine(_tempDir, "does-not-exist");
        Assert.ThrowsExactly<DirectoryNotFoundException>(() => L2SettlementStoreLayout.Open(missing));
    }

    [TestMethod]
    public void Open_EmptyPath_FailsClosed()
    {
        Assert.ThrowsExactly<ArgumentException>(() => L2SettlementStoreLayout.Open(""));
        Assert.ThrowsExactly<ArgumentException>(() => L2SettlementStoreLayout.Open("   "));
    }

    [TestMethod]
    public void Open_IsIdempotentAcrossDisposeAndReopen()
    {
        using (var first = L2SettlementStoreLayout.Open(_tempDir))
        {
            Assert.IsTrue(first.ProofWitness.IsDurable);
        }
        using var second = L2SettlementStoreLayout.Open(_tempDir);
        Assert.IsTrue(second.ProofWitness.IsDurable);
        Assert.IsTrue(Directory.Exists(Path.Combine(
            _tempDir, NeoHubDeployReport.RelativeMessageRouterEventStoreDir)));
    }
}
