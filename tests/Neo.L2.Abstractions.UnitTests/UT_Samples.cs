namespace Neo.L2.UnitTests;

/// <summary>
/// Validates each `samples/*.config.json` parses through <see cref="L2ChainConfigJsonReader"/>
/// + survives a round-trip through <see cref="L2ChainConfigSerializer"/>. Without this, a
/// broken sample (typo in a security-label name, malformed JSON) ships green but breaks
/// every operator who copies it.
/// </summary>
[TestClass]
public class UT_Samples
{
    // Stub UInt160 hashes — the samples don't include the operator-supplied addresses
    // (those come from the L1 deploy bundle), so the test supplies its own.
    private const string OpHash = "0x1111111111111111111111111111111111111111";
    private const string VerifierHash = "0x2222222222222222222222222222222222222222";
    private const string BridgeHash = "0x3333333333333333333333333333333333333333";
    private const string MessageHash = "0x4444444444444444444444444444444444444444";

    private static string SamplesRoot()
    {
        // Walk up from bin/Debug/net10.0/ to repo root; samples/ lives there.
        // AppContext.BaseDirectory has a trailing slash on POSIX, which
        // Path.GetDirectoryName strips without walking up — first iteration would
        // be a no-op, costing us one walk-up step. Start by normalizing.
        var dir = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, "samples");
            if (Directory.Exists(candidate)) return candidate;
            var parent = Path.GetDirectoryName(dir);
            if (parent is null || parent == dir) break;
            dir = parent;
        }
        throw new DirectoryNotFoundException(
            $"samples/ not found walking up from {AppContext.BaseDirectory}");
    }

    private static string ReadSample(string name) =>
        File.ReadAllText(Path.Combine(SamplesRoot(), $"{name}.config.json"));

    [TestMethod]
    public void GeneralRollup_Parses_RoundTrips()
    {
        var json = ReadSample("general-rollup");
        var config = L2ChainConfigJsonReader.FromJson(1100, json,
            OpHash, VerifierHash, BridgeHash, MessageHash);

        Assert.AreEqual(1100U, config.ChainId);
        Assert.AreEqual(SecurityLevel.Optimistic, config.SecurityLevel);
        Assert.AreEqual(DAMode.NeoFS, config.DAMode);
        Assert.AreEqual(SequencerModel.DbftCommittee, config.Sequencer);
        Assert.AreEqual(ExitModel.Delayed, config.Exit);
        Assert.IsFalse(config.GatewayEnabled);
        Assert.IsTrue(config.PermissionlessExit);

        // Round-trip through the encoder.
        var bytes = L2ChainConfigSerializer.Encode(config);
        Assert.AreEqual(L2ChainConfigSerializer.ConfigSize, bytes.Length);
    }

    [TestMethod]
    public void GamingRollup_HasCentralizedSequencer_NeoFSDA()
    {
        var json = ReadSample("gaming-rollup");
        var config = L2ChainConfigJsonReader.FromJson(1200, json,
            OpHash, VerifierHash, BridgeHash, MessageHash);

        // The gaming chain's distinguishing parameters (vs the default rollup) — pin so
        // a future edit that flips one of these surfaces here, while DA stays NeoFS.
        Assert.AreEqual(SequencerModel.Centralized, config.Sequencer);
        Assert.AreEqual(DAMode.NeoFS, config.DAMode);
        Assert.IsTrue(config.PermissionlessExit,
            "gaming chain MUST keep permissionlessExit so users can escape a malicious centralized sequencer");
    }

    [TestMethod]
    public void ExchangeValidium_HasZkValidity_NeoFSDA_GatewayEnabled()
    {
        var json = ReadSample("exchange-validium");
        var config = L2ChainConfigJsonReader.FromJson(1300, json,
            OpHash, VerifierHash, BridgeHash, MessageHash);

        Assert.AreEqual(SecurityLevel.Validium, config.SecurityLevel);
        Assert.AreEqual(DAMode.NeoFS, config.DAMode);
        Assert.AreEqual(ExitModel.Delayed, config.Exit);
        Assert.IsTrue(config.GatewayEnabled,
            "exchange validium SHOULD have gatewayEnabled for cross-L2 asset movement");
    }

    [TestMethod]
    public void PrivacySidechain_HasSidechainSecurity_PermissionlessExit()
    {
        var json = ReadSample("privacy-sidechain");
        var config = L2ChainConfigJsonReader.FromJson(1400, json,
            OpHash, VerifierHash, BridgeHash, MessageHash);

        Assert.AreEqual(SecurityLevel.Sidechain, config.SecurityLevel);
        Assert.AreEqual(ExitModel.Permissionless, config.Exit,
            "sidechain users MUST be able to permissionlessly exit since L1 isn't a settlement trust anchor");
    }

    [TestMethod]
    public void AllSamples_RoundTripCleanly()
    {
        // Catch-all guard: any sample's encode → decode roundtrip must preserve all
        // dimensions. Ensures a future sample addition / edit can't ship with a wire-format
        // inconsistency.
        var samples = new[]
        {
            ("general-rollup", 1100u),
            ("gaming-rollup", 1200u),
            ("exchange-validium", 1300u),
            ("privacy-sidechain", 1400u),
        };
        foreach (var (name, chainId) in samples)
        {
            var json = ReadSample(name);
            var config = L2ChainConfigJsonReader.FromJson(chainId, json,
                OpHash, VerifierHash, BridgeHash, MessageHash);
            var bytes = L2ChainConfigSerializer.Encode(config);
            var decoded = L2ChainConfigSerializer.Decode(bytes);

            Assert.AreEqual(config.ChainId, decoded.ChainId, name);
            Assert.AreEqual(config.SecurityLevel, decoded.SecurityLevel, name);
            Assert.AreEqual(config.DAMode, decoded.DAMode, name);
            Assert.AreEqual(config.Sequencer, decoded.Sequencer, name);
            Assert.AreEqual(config.Exit, decoded.Exit, name);
        }
    }
}
