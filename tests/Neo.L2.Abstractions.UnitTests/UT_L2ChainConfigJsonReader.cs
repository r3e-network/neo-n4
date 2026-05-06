namespace Neo.L2.UnitTests;

/// <summary>
/// Tests for <see cref="L2ChainConfigJsonReader"/> — the parser that the CLI's
/// <c>register-chain</c> uses to combine <c>chain.config.json</c> + the four operator-
/// supplied L1 contract hashes into a fully-populated <see cref="L2ChainConfig"/>.
///
/// Without these, a refactor to the JSON shape (e.g. dropping a field, renaming a
/// security label) would surface only at the operator's terminal — pinning here
/// makes the contract visible at compile/test time.
/// </summary>
[TestClass]
public class UT_L2ChainConfigJsonReader
{
    private const string SampleJson = """
        {
          "chainId": 4242,
          "template": "rollup",
          "vm": "neovm",
          "chainMode": "L2RollupMode",
          "daMode": "L1",
          "proofType": "Optimistic",
          "securityLevel": "Optimistic",
          "sequencerModel": "DbftCommittee",
          "exitModel": "Delayed",
          "gatewayEnabled": false,
          "permissionlessExit": true,
          "milestonePerBlockMs": 5000,
          "validators": []
        }
        """;

    private const string OpHash = "0x1111111111111111111111111111111111111111";
    private const string VerifierHash = "0x2222222222222222222222222222222222222222";
    private const string BridgeHash = "0x3333333333333333333333333333333333333333";
    private const string MessageHash = "0x4444444444444444444444444444444444444444";

    [TestMethod]
    public void FromJson_PopulatesAllFields()
    {
        var config = L2ChainConfigJsonReader.FromJson(4242, SampleJson,
            OpHash, VerifierHash, BridgeHash, MessageHash);

        Assert.AreEqual(4242U, config.ChainId);
        Assert.AreEqual(UInt160.Parse(OpHash), config.OperatorManager);
        Assert.AreEqual(UInt160.Parse(VerifierHash), config.Verifier);
        Assert.AreEqual(UInt160.Parse(BridgeHash), config.BridgeAdapter);
        Assert.AreEqual(UInt160.Parse(MessageHash), config.MessageAdapter);
        Assert.AreEqual(SecurityLevel.Optimistic, config.SecurityLevel);
        Assert.AreEqual(DAMode.L1, config.DAMode);
        Assert.AreEqual(SequencerModel.DbftCommittee, config.Sequencer);
        Assert.AreEqual(ExitModel.Delayed, config.Exit);
        Assert.IsFalse(config.GatewayEnabled);
        Assert.IsTrue(config.PermissionlessExit);
        Assert.IsTrue(config.Active);  // always true on registration
    }

    [TestMethod]
    public void FromJson_RoundTripsThroughSerializer()
    {
        // The JSON reader → wire encoder pipeline is what the CLI actually does.
        // Pin that the round-trip preserves every dimension.
        var config = L2ChainConfigJsonReader.FromJson(4242, SampleJson,
            OpHash, VerifierHash, BridgeHash, MessageHash);
        var bytes = L2ChainConfigSerializer.Encode(config);
        var decoded = L2ChainConfigSerializer.Decode(bytes);

        Assert.AreEqual(config.ChainId, decoded.ChainId);
        Assert.AreEqual(config.SecurityLevel, decoded.SecurityLevel);
        Assert.AreEqual(config.DAMode, decoded.DAMode);
        Assert.AreEqual(config.Sequencer, decoded.Sequencer);
        Assert.AreEqual(config.Exit, decoded.Exit);
        Assert.AreEqual(config.GatewayEnabled, decoded.GatewayEnabled);
        Assert.AreEqual(config.PermissionlessExit, decoded.PermissionlessExit);
    }

    [TestMethod]
    public void FromJson_RejectsUnknownEnumName_NamesField()
    {
        // An operator who hand-edits chain.config.json to "securityLevel": "Foo" should
        // see exactly which field is wrong, not a generic JsonException at line N.
        var bad = SampleJson.Replace("\"Optimistic\"", "\"Foo\"");
        var ex = Assert.ThrowsExactly<ArgumentException>(
            () => L2ChainConfigJsonReader.FromJson(4242, bad,
                OpHash, VerifierHash, BridgeHash, MessageHash));
        StringAssert.Contains(ex.Message, "securityLevel");
        StringAssert.Contains(ex.Message, "Foo");
        StringAssert.Contains(ex.Message, "SecurityLevel");
    }

    [TestMethod]
    public void FromJson_RejectsMissingField_NamesField()
    {
        // If a future create-chain template forgets a field, surface that here so the
        // operator sees "chain.config.json missing 'daMode'" not a sub-property NRE.
        var withoutDaMode = SampleJson.Replace("\"daMode\": \"L1\",\n", "");
        var ex = Assert.ThrowsExactly<ArgumentException>(
            () => L2ChainConfigJsonReader.FromJson(4242, withoutDaMode,
                OpHash, VerifierHash, BridgeHash, MessageHash));
        StringAssert.Contains(ex.Message, "missing");
        StringAssert.Contains(ex.Message, "daMode");
    }

    [TestMethod]
    public void FromJson_RejectsMalformedHash_NamesFlag()
    {
        // Pin operator-friendly error: the flag that's wrong is in the message so the
        // operator can fix the right CLI argument.
        var ex = Assert.ThrowsExactly<ArgumentException>(
            () => L2ChainConfigJsonReader.FromJson(4242, SampleJson,
                "0xDEAD", VerifierHash, BridgeHash, MessageHash));
        StringAssert.Contains(ex.Message, "--operator");
        StringAssert.Contains(ex.Message, "0xDEAD");
        StringAssert.Contains(ex.Message, "UInt160");
    }

    [TestMethod]
    public void FromJson_RejectsNullJson()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => L2ChainConfigJsonReader.FromJson(4242, null!,
                OpHash, VerifierHash, BridgeHash, MessageHash));
    }

    [TestMethod]
    public void FromJson_RejectsNullHash()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => L2ChainConfigJsonReader.FromJson(4242, SampleJson,
                null!, VerifierHash, BridgeHash, MessageHash));
    }

    [TestMethod]
    public void FromJson_HonorsValidiumTemplateDefaults()
    {
        // Pin the validium template's distinct shape — sequencerModel = DbftCommittee
        // but exitModel = Delayed (off-chain DA so the user can't trivially exit).
        var validiumJson = SampleJson
            .Replace("\"daMode\": \"L1\"", "\"daMode\": \"NeoFS\"")
            .Replace("\"securityLevel\": \"Optimistic\"", "\"securityLevel\": \"Validium\"")
            .Replace("\"exitModel\": \"Delayed\"", "\"exitModel\": \"Delayed\"")
            .Replace("\"gatewayEnabled\": false", "\"gatewayEnabled\": true")
            .Replace("\"permissionlessExit\": true", "\"permissionlessExit\": false");

        var config = L2ChainConfigJsonReader.FromJson(4242, validiumJson,
            OpHash, VerifierHash, BridgeHash, MessageHash);
        Assert.AreEqual(DAMode.NeoFS, config.DAMode);
        Assert.AreEqual(SecurityLevel.Validium, config.SecurityLevel);
        Assert.AreEqual(ExitModel.Delayed, config.Exit);
        Assert.IsTrue(config.GatewayEnabled);
        Assert.IsFalse(config.PermissionlessExit);
    }
}
