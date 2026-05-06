namespace Neo.L2.UnitTests;

/// <summary>
/// Pins the 91-byte wire format of <see cref="L2ChainConfigSerializer"/> against the
/// on-chain layout defined by <c>NeoHub.ChainRegistry.ChainRegistryContract.ConfigSize</c>.
/// Any future offset shift on either side fails one of these tests instead of
/// silently mis-parsing operator-submitted configs.
/// </summary>
[TestClass]
public class UT_L2ChainConfigSerializer
{
    private static UInt160 H(byte b)
    {
        var bytes = new byte[20];
        for (var i = 0; i < 20; i++) bytes[i] = b;
        return new UInt160(bytes);
    }

    private static L2ChainConfig SampleConfig() => new()
    {
        ChainId = 0x12345678,
        OperatorManager = H(0x11),
        Verifier = H(0x22),
        BridgeAdapter = H(0x33),
        MessageAdapter = H(0x44),
        SecurityLevel = SecurityLevel.Optimistic,
        DAMode = DAMode.NeoFS,
        GatewayEnabled = true,
        PermissionlessExit = true,
        Sequencer = SequencerModel.DbftCommittee,
        Exit = ExitModel.Permissionless,
        Active = true,
    };

    [TestMethod]
    public void Encode_ProducesExactlyConfigSizeBytes()
    {
        // Pinning ConfigSize matches the contract — if the wire format ever evolves
        // (e.g. a new §16.2 dimension), both sides must move together.
        Assert.AreEqual(91, L2ChainConfigSerializer.ConfigSize);
        var bytes = L2ChainConfigSerializer.Encode(SampleConfig());
        Assert.AreEqual(91, bytes.Length);
    }

    [TestMethod]
    public void Encode_LayoutMatchesSpec()
    {
        var bytes = L2ChainConfigSerializer.Encode(SampleConfig());

        // chainId at 0..3, little-endian.
        Assert.AreEqual(0x78, bytes[0]);
        Assert.AreEqual(0x56, bytes[1]);
        Assert.AreEqual(0x34, bytes[2]);
        Assert.AreEqual(0x12, bytes[3]);

        // 0x11-fill operator at 4..23.
        for (var i = 4; i < 24; i++) Assert.AreEqual(0x11, bytes[i], $"operator byte {i}");

        // 0x22-fill verifier at 24..43.
        for (var i = 24; i < 44; i++) Assert.AreEqual(0x22, bytes[i], $"verifier byte {i}");

        // 0x33-fill bridge at 44..63.
        for (var i = 44; i < 64; i++) Assert.AreEqual(0x33, bytes[i], $"bridge byte {i}");

        // 0x44-fill message at 64..83.
        for (var i = 64; i < 84; i++) Assert.AreEqual(0x44, bytes[i], $"message byte {i}");

        // Single-byte fields at 84..90.
        Assert.AreEqual((byte)SecurityLevel.Optimistic, bytes[84]);
        Assert.AreEqual((byte)DAMode.NeoFS, bytes[85]);
        Assert.AreEqual((byte)1, bytes[86]); // gatewayEnabled
        Assert.AreEqual((byte)1, bytes[87]); // permissionlessExit
        Assert.AreEqual((byte)SequencerModel.DbftCommittee, bytes[88]);
        Assert.AreEqual((byte)ExitModel.Permissionless, bytes[89]);
        Assert.AreEqual((byte)1, bytes[90]); // active
    }

    [TestMethod]
    public void RoundTrip_PreservesAllFields()
    {
        var original = SampleConfig();
        var bytes = L2ChainConfigSerializer.Encode(original);
        var decoded = L2ChainConfigSerializer.Decode(bytes);

        Assert.AreEqual(original.ChainId, decoded.ChainId);
        Assert.AreEqual(original.OperatorManager, decoded.OperatorManager);
        Assert.AreEqual(original.Verifier, decoded.Verifier);
        Assert.AreEqual(original.BridgeAdapter, decoded.BridgeAdapter);
        Assert.AreEqual(original.MessageAdapter, decoded.MessageAdapter);
        Assert.AreEqual(original.SecurityLevel, decoded.SecurityLevel);
        Assert.AreEqual(original.DAMode, decoded.DAMode);
        Assert.AreEqual(original.GatewayEnabled, decoded.GatewayEnabled);
        Assert.AreEqual(original.PermissionlessExit, decoded.PermissionlessExit);
        Assert.AreEqual(original.Sequencer, decoded.Sequencer);
        Assert.AreEqual(original.Exit, decoded.Exit);
        Assert.AreEqual(original.Active, decoded.Active);
    }

    [TestMethod]
    public void RoundTrip_PreservesAllEnumExtremes()
    {
        var config = SampleConfig() with
        {
            SecurityLevel = SecurityLevel.Validium,  // = 4, the highest discriminant
            DAMode = DAMode.DAC,                     // = 3
            GatewayEnabled = false,
            PermissionlessExit = false,
            Sequencer = SequencerModel.Decentralized, // = 2
            Exit = ExitModel.OperatorAssisted,        // = 2
            Active = false,
        };
        var decoded = L2ChainConfigSerializer.Decode(L2ChainConfigSerializer.Encode(config));

        Assert.AreEqual(SecurityLevel.Validium, decoded.SecurityLevel);
        Assert.AreEqual(DAMode.DAC, decoded.DAMode);
        Assert.IsFalse(decoded.GatewayEnabled);
        Assert.IsFalse(decoded.PermissionlessExit);
        Assert.AreEqual(SequencerModel.Decentralized, decoded.Sequencer);
        Assert.AreEqual(ExitModel.OperatorAssisted, decoded.Exit);
        Assert.IsFalse(decoded.Active);
    }

    [TestMethod]
    public void Decode_RejectsWrongLength()
    {
        Assert.ThrowsExactly<ArgumentException>(() => L2ChainConfigSerializer.Decode(new byte[90]));
        Assert.ThrowsExactly<ArgumentException>(() => L2ChainConfigSerializer.Decode(new byte[92]));
        Assert.ThrowsExactly<ArgumentException>(() => L2ChainConfigSerializer.Decode(ReadOnlySpan<byte>.Empty));
    }

    [TestMethod]
    public void Decode_RejectsOutOfRangeEnumBytes()
    {
        // Without the range-check, a corrupted SecurityLevel=99 byte silently round-trips
        // as `(SecurityLevel)99` and the levelName goes "99" — misleading every downstream
        // bridge / RPC consumer.
        var bytes = L2ChainConfigSerializer.Encode(SampleConfig());

        bytes[84] = 99;  // securityLevel
        Assert.ThrowsExactly<ArgumentException>(() => L2ChainConfigSerializer.Decode(bytes));
        bytes[84] = (byte)SecurityLevel.Optimistic;

        bytes[85] = 99;  // daMode
        Assert.ThrowsExactly<ArgumentException>(() => L2ChainConfigSerializer.Decode(bytes));
        bytes[85] = (byte)DAMode.External;

        bytes[88] = 99;  // sequencerModel
        Assert.ThrowsExactly<ArgumentException>(() => L2ChainConfigSerializer.Decode(bytes));
        bytes[88] = (byte)SequencerModel.DbftCommittee;

        bytes[89] = 99;  // exitModel
        Assert.ThrowsExactly<ArgumentException>(() => L2ChainConfigSerializer.Decode(bytes));
    }

    [TestMethod]
    public void Encode_RejectsNullConfig()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => L2ChainConfigSerializer.Encode(null!));
    }

    [TestMethod]
    public void Encode_ChainIdMatchesContractParsing()
    {
        // The contract reads chainId via:
        //   (uint)bytes[0] | ((uint)bytes[1] << 8) | ((uint)bytes[2] << 16) | ((uint)bytes[3] << 24)
        // i.e. little-endian. This test pins that our Encode layout matches that
        // exact parsing — any switch to BE on either side breaks RegisterChain assertion
        // "chainId mismatch" silently.
        var config = SampleConfig() with { ChainId = 0xCAFEF00DU };
        var bytes = L2ChainConfigSerializer.Encode(config);

        var contractParsed = (uint)bytes[0] | ((uint)bytes[1] << 8)
            | ((uint)bytes[2] << 16) | ((uint)bytes[3] << 24);
        Assert.AreEqual(0xCAFEF00DU, contractParsed);
    }
}
