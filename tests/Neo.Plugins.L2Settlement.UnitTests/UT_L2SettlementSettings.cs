using Microsoft.Extensions.Configuration;

namespace Neo.Plugins.L2Settlement.UnitTests;

/// <summary>
/// Tests for <see cref="L2SettlementSettings.From"/>. The .From method validates
/// ProofType + ChainId at parse time so a misconfigured plugin fails at load
/// rather than later at SubmitNextAsync. Without these pins, a refactor that
/// drops a default or skips a validator surfaces only when the first batch
/// hits the broken setting.
/// </summary>
[TestClass]
public class UT_L2SettlementSettings
{
    private static IConfigurationSection BuildSection(params (string Key, string? Value)[] entries)
    {
        var dict = entries.ToDictionary(e => "Settlement:" + e.Key, e => e.Value);
        var root = new ConfigurationBuilder().AddInMemoryCollection(dict!).Build();
        return root.GetSection("Settlement");
    }

    [TestMethod]
    public void From_EmptyConfig_HasExpectedDefaults()
    {
        var s = L2SettlementSettings.From(BuildSection());
        Assert.AreEqual(0u, s.ChainId, "missing ChainId reads as 0 (no validation since unset is legal in test mode)");
        Assert.AreEqual("http://localhost:10332", s.L1RpcEndpoint);
        Assert.AreEqual("", s.SettlementManagerHash);
        Assert.AreEqual((byte)1, s.ProofType, "default ProofType is 1 = Multisig");
        Assert.IsTrue(s.Enabled, "default Enabled is true");
    }

    [TestMethod]
    public void From_ExplicitChainIdZero_RejectedByValidator()
    {
        // Distinguishes "missing ChainId" (default 0, accepted) from "explicitly set
        // to 0" (operator misconfig, rejected). The L1 sentinel rule: chainId 0 is
        // reserved for L1 routing.
        Assert.ThrowsExactly<InvalidDataException>(
            () => L2SettlementSettings.From(BuildSection(("ChainId", "0"))));
    }

    [TestMethod]
    public void From_NonZeroChainId_AcceptedAndStored()
    {
        var s = L2SettlementSettings.From(BuildSection(("ChainId", "1001")));
        Assert.AreEqual(1001u, s.ChainId);
    }

    [TestMethod]
    public void From_InvalidProofTypeByte_Rejected()
    {
        // ProofTypeExtensions.Resolve runs at parse time. Without this pin a
        // misconfigured ProofType byte would only surface later at the prover.
        Assert.ThrowsExactly<InvalidDataException>(
            () => L2SettlementSettings.From(BuildSection(("ProofType", "99"))));
    }

    [TestMethod]
    public void From_ValidProofTypeByte_StoredVerbatim()
    {
        var s = L2SettlementSettings.From(BuildSection(("ProofType", "2")));
        Assert.AreEqual((byte)2, s.ProofType, "ProofType=2 = Optimistic");
    }

    [TestMethod]
    public void From_RespectsExplicitL1RpcEndpoint()
    {
        var s = L2SettlementSettings.From(BuildSection(("L1RpcEndpoint", "http://example.invalid:9999")));
        Assert.AreEqual("http://example.invalid:9999", s.L1RpcEndpoint);
    }

    [TestMethod]
    public void From_RespectsExplicitEnabledFlag()
    {
        var s = L2SettlementSettings.From(BuildSection(("Enabled", "false")));
        Assert.IsFalse(s.Enabled);
    }
}
