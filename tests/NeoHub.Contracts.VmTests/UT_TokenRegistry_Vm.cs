using System.Buffers.Binary;
using System.Numerics;
using Neo;
using Neo.SmartContract.Testing;

namespace NeoHub.Contracts.VmTests;

/// <summary>
/// VM-level (NeoVM TestEngine) tests for NeoHub.TokenRegistry. Unlike the rest of the contract
/// suite — which only build/nccs-compile the contracts — these deploy the compiled NEF into a real
/// NeoVM and invoke methods, so contract LOGIC (owner gating, decimals validation, the SetActive
/// flag added in the security review) is executable-tested, not just type-checked.
/// </summary>
[TestClass]
public class UT_TokenRegistry_Vm
{
    private static byte[] BuildMapping(UInt160 l1Asset, uint chainId, UInt160 l2Asset,
        byte assetType, byte l1Decimals, byte l2Decimals)
    {
        // Layout per TokenRegistryContract.RegisterMapping (MappingSize = 50):
        // 20 l1Asset | 4 chainId(LE) | 20 l2Asset | 1 assetType | 1 mintBurn | 1 lockMint
        // | 1 l1Decimals | 1 l2Decimals | 1 active
        var m = new byte[50];
        l1Asset.GetSpan().CopyTo(m.AsSpan(0, 20));
        BinaryPrimitives.WriteUInt32LittleEndian(m.AsSpan(20, 4), chainId);
        l2Asset.GetSpan().CopyTo(m.AsSpan(24, 20));
        m[44] = assetType;
        m[45] = 0; // mintBurn
        m[46] = 0; // lockMint
        m[47] = l1Decimals;
        m[48] = l2Decimals;
        m[49] = 1; // active (RegisterMapping forces this to 1 regardless)
        return m;
    }

    private static (TestEngine engine, NeoHubTokenRegistry reg, UInt160 owner) Deploy()
    {
        var engine = new TestEngine(true);
        var owner = engine.Sender; // the default tx sender is an auto-witnessed signer
        var reg = engine.Deploy<NeoHubTokenRegistry>(NeoHubTokenRegistry.Nef, NeoHubTokenRegistry.Manifest, owner);
        return (engine, reg, owner);
    }

    [TestMethod]
    public void RegisterMapping_ThenSetActive_TogglesIsActive()
    {
        var (_, reg, _) = Deploy();
        var l1Asset = UInt160.Parse("0x" + new string('a', 40));
        var l2Asset = UInt160.Parse("0x" + new string('b', 40));
        BigInteger chainId = 1001;

        reg.RegisterMapping(BuildMapping(l1Asset, 1001, l2Asset, assetType: 10, l1Decimals: 8, l2Decimals: 8));

        // Mapping is active by default and resolves the L2 asset.
        Assert.IsTrue(reg.IsActive(l1Asset, chainId), "mapping must be active right after registration");
        Assert.AreEqual(l2Asset, reg.GetL2Asset(l1Asset, chainId));

        // SetActive(false) — the gate added in the security review — must take effect.
        reg.SetActive(l1Asset, chainId, false);
        Assert.IsFalse(reg.IsActive(l1Asset, chainId), "SetActive(false) must deactivate the mapping");

        // And re-activation works.
        reg.SetActive(l1Asset, chainId, true);
        Assert.IsTrue(reg.IsActive(l1Asset, chainId), "SetActive(true) must re-activate the mapping");
    }

    [TestMethod]
    public void SetActive_OnMissingMapping_Faults()
    {
        var (_, reg, _) = Deploy();
        var l1Asset = UInt160.Parse("0x" + new string('c', 40));
        // No mapping registered for this pair → SetActive must abort (VM FAULT surfaces as a throw).
        Assert.ThrowsExactly<Neo.SmartContract.Testing.Exceptions.TestException>(
            () => reg.SetActive(l1Asset, 1001, false));
    }

    [TestMethod]
    public void RegisterMapping_RejectsBadNeoDecimals()
    {
        var (_, reg, _) = Deploy();
        var l1Asset = UInt160.Parse("0x" + new string('a', 40));
        var l2Asset = UInt160.Parse("0x" + new string('b', 40));
        // assetType 1 = NEO requires l1Decimals==0 && l2Decimals==8; supply 8/8 → must abort.
        Assert.ThrowsExactly<Neo.SmartContract.Testing.Exceptions.TestException>(
            () => reg.RegisterMapping(BuildMapping(l1Asset, 1001, l2Asset, assetType: 1, l1Decimals: 8, l2Decimals: 8)));
    }
}
