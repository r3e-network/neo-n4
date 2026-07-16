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
        Assert.AreEqual("", s.L1RpcEndpoint, "production RPC must not have an implicit endpoint fallback");
        Assert.IsNull(s.ExpectedNetwork, "production network magic must be explicit");
        Assert.AreEqual("", s.SettlementManagerHash);
        Assert.AreEqual("", s.ForcedInclusionHash);
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
    public void From_RespectsExplicitProductionFields()
    {
        var forcedInclusionHash = "0x" + new string('2', 40);
        var s = L2SettlementSettings.From(BuildSection(
            ("ExpectedNetwork", "860833102"),
            ("ForcedInclusionHash", forcedInclusionHash)));

        Assert.AreEqual(860833102u, s.ExpectedNetwork);
        Assert.AreEqual(forcedInclusionHash, s.ForcedInclusionHash);
    }

    [TestMethod]
    [DataRow("Wif")]
    [DataRow("SignerWif")]
    [DataRow("OperatorWif")]
    [DataRow("PrivateKey")]
    public void From_PrivateSigningMaterial_RejectedWithoutEchoingSecret(string key)
    {
        const string secret = "this-value-must-never-appear-in-an-error";
        var exception = Assert.ThrowsExactly<InvalidDataException>(
            () => L2SettlementSettings.From(BuildSection((key, secret))));

        Assert.IsFalse(exception.Message.Contains(secret, StringComparison.Ordinal));
        StringAssert.Contains(exception.Message, "INeoTransactionSigner");
    }

    [TestMethod]
    public void ValidateProduction_ValidSettings_ReturnsCanonicalValues()
    {
        var settings = ValidProductionSettings();

        var validated = settings.ValidateProduction();

        Assert.AreEqual(1001u, validated.ChainId);
        Assert.AreEqual(new Uri("https://l1.example.invalid:10331/rpc"), validated.RpcEndpoint);
        Assert.AreEqual(860833102u, validated.ExpectedNetwork);
        Assert.AreEqual(UInt160.Parse(settings.SettlementManagerHash), validated.SettlementManagerHash);
        Assert.AreEqual(UInt160.Parse(settings.ForcedInclusionHash), validated.ForcedInclusionHash);
    }

    [TestMethod]
    public void ValidateProduction_MissingChainId_Rejected()
    {
        var settings = ValidProductionSettings(chainId: 0);
        Assert.ThrowsExactly<InvalidDataException>(() => settings.ValidateProduction());
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("localhost:10332")]
    [DataRow("file:///tmp/neo-rpc")]
    public void ValidateProduction_MissingOrInvalidEndpoint_Rejected(string endpoint)
    {
        var settings = ValidProductionSettings(endpoint: endpoint);
        Assert.ThrowsExactly<InvalidDataException>(() => settings.ValidateProduction());
    }

    [TestMethod]
    public void ValidateProduction_MissingExpectedNetwork_Rejected()
    {
        var settings = ValidProductionSettings(expectedNetwork: null);
        Assert.ThrowsExactly<InvalidDataException>(() => settings.ValidateProduction());
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("not-a-hash")]
    [DataRow("0x0000000000000000000000000000000000000000")]
    public void ValidateProduction_MissingInvalidOrZeroSettlementHash_Rejected(string hash)
    {
        var settings = ValidProductionSettings(settlementManagerHash: hash);
        Assert.ThrowsExactly<InvalidDataException>(() => settings.ValidateProduction());
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("not-a-hash")]
    [DataRow("0x0000000000000000000000000000000000000000")]
    public void ValidateProduction_MissingInvalidOrZeroForcedInclusionHash_Rejected(string hash)
    {
        var settings = ValidProductionSettings(forcedInclusionHash: hash);
        Assert.ThrowsExactly<InvalidDataException>(() => settings.ValidateProduction());
    }

    [TestMethod]
    public void ValidateProduction_ContractHashesMustBeDistinct()
    {
        var hash = "0x" + new string('1', 40);
        var settings = ValidProductionSettings(
            settlementManagerHash: hash,
            forcedInclusionHash: hash);

        Assert.ThrowsExactly<InvalidDataException>(() => settings.ValidateProduction());
    }

    [TestMethod]
    public void From_RespectsExplicitEnabledFlag()
    {
        var s = L2SettlementSettings.From(BuildSection(("Enabled", "false")));
        Assert.IsFalse(s.Enabled);
    }

    [TestMethod]
    public void From_ParsesDeploymentHeights()
    {
        var s = L2SettlementSettings.From(BuildSection(
            ("ForcedInclusionDeploymentHeight", "17729309"),
            ("SharedBridgeDeploymentHeight", "17729307"),
            ("MessageRouterDeploymentHeight", "17729303")));
        Assert.AreEqual(17729309u, s.ForcedInclusionDeploymentHeight);
        Assert.AreEqual(17729307u, s.SharedBridgeDeploymentHeight);
        Assert.AreEqual(17729303u, s.MessageRouterDeploymentHeight);
    }

    [TestMethod]
    public void From_DeploymentHeightsDefaultToZero()
    {
        var s = L2SettlementSettings.From(BuildSection());
        Assert.AreEqual(0u, s.ForcedInclusionDeploymentHeight);
        Assert.AreEqual(0u, s.SharedBridgeDeploymentHeight);
        Assert.AreEqual(0u, s.MessageRouterDeploymentHeight);
        Assert.AreEqual(1u, s.L1FinalityDepth);
    }

    [TestMethod]
    public void From_ParsesL1FinalityDepth()
    {
        var s = L2SettlementSettings.From(BuildSection(("L1FinalityDepth", "3")));
        Assert.AreEqual(3u, s.L1FinalityDepth);
    }

    private static L2SettlementSettings ValidProductionSettings(
        uint chainId = 1001,
        string endpoint = "https://l1.example.invalid:10331/rpc",
        uint? expectedNetwork = 860833102,
        string? settlementManagerHash = null,
        string? forcedInclusionHash = null)
        => new()
        {
            ChainId = chainId,
            L1RpcEndpoint = endpoint,
            ExpectedNetwork = expectedNetwork,
            SettlementManagerHash = settlementManagerHash ?? "0x" + new string('1', 40),
            ForcedInclusionHash = forcedInclusionHash ?? "0x" + new string('2', 40),
            ProofType = (byte)ProofType.Multisig,
        };
}
