using System.Buffers.Binary;
using System.Numerics;
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

    private static NeoHubChainRegistry Deploy()
    {
        var engine = new TestEngine(true);
        var owner = engine.Sender; // default tx sender is an auto-witnessed signer
        return engine.Deploy<NeoHubChainRegistry>(NeoHubChainRegistry.Nef, NeoHubChainRegistry.Manifest, owner);
    }

    [TestMethod]
    public void RegisterChain_PauseChain_ResumeChain_TogglesActive()
    {
        var reg = Deploy();
        BigInteger chainId = 1001;

        reg.RegisterChain(chainId, BuildConfig(1001, daMode: 0));
        Assert.IsTrue(reg.IsActive(chainId), "a freshly registered chain must be active");

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
            () => reg.RegisterChain(1001, BuildConfig(1001, daMode: 99)));
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
                () => reg.RegisterChain(1001, config));
            Assert.IsFalse(reg.IsActive(1001), "rejected config must not be persisted");
            return;
        }

        reg.RegisterChain(1001, config);
        CollectionAssert.AreEqual(config, reg.GetChainConfig(1001));
    }

    [TestMethod]
    public void RegisterChain_RejectsOutOfRangeSecurityLevel()
    {
        var reg = Deploy();

        Assert.ThrowsExactly<Neo.SmartContract.Testing.Exceptions.TestException>(
            () => reg.RegisterChain(1001, BuildConfig(1001, securityLevel: 99)));
    }

    [TestMethod]
    public void UpdateChain_RejectsContradictorySecurityAndDaWithoutChangingStoredConfig()
    {
        var reg = Deploy();
        var original = BuildConfig(1001, daMode: 0, securityLevel: 3);
        reg.RegisterChain(1001, original);

        Assert.ThrowsExactly<Neo.SmartContract.Testing.Exceptions.TestException>(
            () => reg.UpdateChain(1001, BuildConfig(1001, daMode: 1, securityLevel: 3)));

        CollectionAssert.AreEqual(original, reg.GetChainConfig(1001));
    }
}
