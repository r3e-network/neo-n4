using System.Buffers.Binary;
using System.Numerics;
using Moq;
using Neo;
using Neo.SmartContract.Testing;

namespace NeoHub.Contracts.VmTests;

/// <summary>
/// VM-level tests for NeoHub.ChainRegistry. These pin the fix for the ByteString-mutation fault in
/// PauseChain/ResumeChain (which mutated a storage-read <c>byte[]</c> — an immutable NeoVM
/// ByteString — and FAULTed at runtime, silently breaking the censorship/emergency pause path).
/// </summary>
[TestClass]
public class UT_ChainRegistry_Vm
{
    private const int ConfigSize = 91;
    private const int OffsetSecurityLevel = 84;
    private const int OffsetDAMode = 85;
    private const int OffsetActive = 90;
    private static readonly UInt256 GenesisStateRoot = new(Enumerable.Repeat((byte)0xA5, 32).ToArray());

    private static byte[] BuildConfig(uint chainId, byte daMode = 0, byte securityLevel = 0)
    {
        // 91-byte L2ChainConfig. The UInt160 fields are not relevant to these registration
        // compatibility tests, so they may stay zero.
        var c = new byte[ConfigSize];
        BinaryPrimitives.WriteUInt32LittleEndian(c.AsSpan(0, 4), chainId);
        c[OffsetSecurityLevel] = securityLevel;
        c[OffsetDAMode] = daMode;
        c[OffsetActive] = 1; // active
        return c;
    }

    private static NeoHubChainRegistry Deploy() => Deploy(new TestEngine(true));

    private static NeoHubChainRegistry Deploy(TestEngine engine)
    {
        var owner = engine.Sender; // default tx sender is an auto-witnessed signer
        return engine.Deploy<NeoHubChainRegistry>(NeoHubChainRegistry.Nef, NeoHubChainRegistry.Manifest, owner);
    }

    [TestMethod]
    public void RegisterChain_PauseChain_ResumeChain_TogglesActive()
    {
        var reg = Deploy();
        BigInteger chainId = 1001;

        reg.RegisterChain(chainId, BuildConfig(1001, daMode: 0), GenesisStateRoot);
        Assert.IsTrue(reg.IsActive(chainId), "a freshly registered chain must be active");
        Assert.AreEqual(GenesisStateRoot, reg.GetGenesisStateRoot(chainId));

        // PauseChain rewrites the stored config's active byte. Before the fix this FAULTed
        // (SETITEM on a ByteString), so the censorship/emergency pause was a no-op-that-throws.
        reg.PauseChain(chainId);
        Assert.IsFalse(reg.IsActive(chainId), "PauseChain must deactivate the chain");

        reg.ResumeChain(chainId);
        Assert.IsTrue(reg.IsActive(chainId), "ResumeChain must reactivate the chain");
    }

    [TestMethod]
    public void RegisterChain_RejectsOutOfRangeDaMode()
    {
        var reg = Deploy();
        // daMode 99 is out of the 0..3 range — registration must abort (VM FAULT → throw).
        Assert.ThrowsExactly<Neo.SmartContract.Testing.Exceptions.TestException>(
            () => reg.RegisterChain(1001, BuildConfig(1001, daMode: 99), GenesisStateRoot));
    }

    [TestMethod]
    [DataRow(0, 0, true)]
    [DataRow(0, 1, true)]
    [DataRow(0, 2, true)]
    [DataRow(0, 3, true)]
    [DataRow(1, 0, true)]
    [DataRow(1, 1, true)]
    [DataRow(1, 2, true)]
    [DataRow(1, 3, true)]
    [DataRow(2, 0, true)]
    [DataRow(2, 1, true)]
    [DataRow(2, 2, true)]
    [DataRow(2, 3, true)]
    [DataRow(3, 0, true)]
    [DataRow(3, 1, false)]
    [DataRow(3, 2, false)]
    [DataRow(3, 3, false)]
    [DataRow(4, 0, false)]
    [DataRow(4, 1, true)]
    [DataRow(4, 2, true)]
    [DataRow(4, 3, true)]
    public void RegisterChain_EnforcesSecurityAndDaCompatibility(
        int securityLevel,
        int daMode,
        bool expectedCompatible)
    {
        var reg = Deploy();
        var config = BuildConfig(1001, (byte)daMode, (byte)securityLevel);

        if (!expectedCompatible)
        {
            Assert.ThrowsExactly<Neo.SmartContract.Testing.Exceptions.TestException>(
                () => reg.RegisterChain(1001, config, GenesisStateRoot));
            Assert.IsFalse(reg.IsActive(1001), "rejected config must not be persisted");
            return;
        }

        reg.RegisterChain(1001, config, GenesisStateRoot);
        CollectionAssert.AreEqual(config, reg.GetChainConfig(1001));
    }

    [TestMethod]
    public void RegisterChain_RejectsOutOfRangeSecurityLevel()
    {
        var reg = Deploy();

        Assert.ThrowsExactly<Neo.SmartContract.Testing.Exceptions.TestException>(
            () => reg.RegisterChain(1001, BuildConfig(1001, securityLevel: 99), GenesisStateRoot));
    }

    [TestMethod]
    public void UpdateChain_RejectsContradictorySecurityAndDaWithoutChangingStoredConfig()
    {
        var reg = Deploy();
        var original = BuildConfig(1001, daMode: 0, securityLevel: 3);
        reg.RegisterChain(1001, original, GenesisStateRoot);

        Assert.ThrowsExactly<Neo.SmartContract.Testing.Exceptions.TestException>(
            () => reg.UpdateChain(1001, BuildConfig(1001, daMode: 1, securityLevel: 3)));

        CollectionAssert.AreEqual(original, reg.GetChainConfig(1001));
    }

    [TestMethod]
    public void RegisterChain_GenesisStateRoot_IsRequiredAndImmutable()
    {
        var reg = Deploy();
        var config = BuildConfig(1001);

        Assert.ThrowsExactly<Neo.SmartContract.Testing.Exceptions.TestException>(
            () => reg.RegisterChain(1001, config, UInt256.Zero));
        Assert.IsFalse(reg.IsActive(1001));
        Assert.AreEqual(UInt256.Zero, reg.GetGenesisStateRoot(1001));

        reg.RegisterChain(1001, config, GenesisStateRoot);
        reg.RegisterChain(1001, config, GenesisStateRoot);
        Assert.ThrowsExactly<Neo.SmartContract.Testing.Exceptions.TestException>(
            () => reg.RegisterChain(
                1001,
                config,
                new UInt256(Enumerable.Repeat((byte)0x5A, 32).ToArray())));
        Assert.AreEqual(GenesisStateRoot, reg.GetGenesisStateRoot(1001));
    }

    [TestMethod]
    public void RegisterChainPublic_RequiresAndPersistsImmutableGenesisStateRoot()
    {
        var engine = new TestEngine(true);
        var reg = Deploy(engine);
        var governanceHash = UInt160.Parse("0x" + new string('7', 40));
        engine.FromHash<NeoHubGovernanceController>(
            governanceHash,
            governance => governance
                .Setup(controller => controller.AdmissionMode)
                .Returns((BigInteger?)2),
            checkExistence: false);
        reg.GovernanceController = governanceHash;
        var config = BuildConfig(1001);

        Assert.ThrowsExactly<Neo.SmartContract.Testing.Exceptions.TestException>(
            () => reg.RegisterChainPublic(1001, config, UInt256.Zero));
        Assert.AreEqual(UInt256.Zero, reg.GetGenesisStateRoot(1001));

        reg.RegisterChainPublic(1001, config, GenesisStateRoot);
        Assert.AreEqual(GenesisStateRoot, reg.GetGenesisStateRoot(1001));
        CollectionAssert.AreEqual(config, reg.GetChainConfig(1001));
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(3)]
    [DataRow(258)]
    public void RegisterChainPublic_RejectsInvalidAdmissionModeWithoutPersistingState(int admissionMode)
    {
        var engine = new TestEngine(true);
        var reg = Deploy(engine);
        var governanceHash = UInt160.Parse("0x" + new string('7', 40));
        engine.FromHash<NeoHubGovernanceController>(
            governanceHash,
            governance => governance
                .Setup(controller => controller.AdmissionMode)
                .Returns((BigInteger?)admissionMode),
            checkExistence: false);
        reg.GovernanceController = governanceHash;

        Assert.ThrowsExactly<Neo.SmartContract.Testing.Exceptions.TestException>(
            () => reg.RegisterChainPublic(1001, BuildConfig(1001), GenesisStateRoot));

        Assert.IsFalse(reg.IsActive(1001));
        Assert.AreEqual(UInt256.Zero, reg.GetGenesisStateRoot(1001));
        CollectionAssert.AreEqual(Array.Empty<byte>(), reg.GetChainConfig(1001));
    }

    [TestMethod]
    public void SetGovernanceController_AfterGovernanceLock_RejectsReplacement()
    {
        var reg = Deploy();
        var original = UInt160.Parse("0x" + new string('7', 40));
        var replacement = UInt160.Parse("0x" + new string('8', 40));
        reg.GovernanceController = original;
        reg.LockGovernance();

        Assert.ThrowsExactly<Neo.SmartContract.Testing.Exceptions.TestException>(
            () => reg.GovernanceController = replacement);

        Assert.IsTrue(reg.IsGovernanceLocked);
        Assert.AreEqual(original, reg.GovernanceController);
    }
}
