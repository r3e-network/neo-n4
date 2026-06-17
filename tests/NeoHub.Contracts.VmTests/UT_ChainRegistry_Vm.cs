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
    private const int OffsetDAMode = 85;
    private const int OffsetActive = 90;

    private static byte[] BuildConfig(uint chainId, byte daMode = 0, byte securityLevel = 0)
    {
        // 91-byte L2ChainConfig. Only chainId@0, daMode@85 (must be <=3), and active@90 are
        // validated at registration; the UInt160 fields are not, so they may stay zero.
        var c = new byte[ConfigSize];
        BinaryPrimitives.WriteUInt32LittleEndian(c.AsSpan(0, 4), chainId);
        c[84] = securityLevel;
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
}
