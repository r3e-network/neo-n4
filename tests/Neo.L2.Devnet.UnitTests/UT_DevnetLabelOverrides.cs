using System;
using System.IO;
using Neo.L2;
using Neo.L2.Devnet;

namespace Neo.L2.Devnet.UnitTests;

/// <summary>
/// Tests for <see cref="DevnetLabelOverrides"/> — the §16.2 security-label
/// override reader. Pins the permissive-fallback semantics (devnet should still
/// run rather than abort on a malformed config) + each per-field default vs.
/// override path.
/// </summary>
[TestClass]
public class UT_DevnetLabelOverrides
{
    private string _tempDir = null!;

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "neo-n4-label-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public void Defaults_MatchInMemoryStoreSaneDefaults()
    {
        // Pin the canonical default values so a refactor that changes them
        // (e.g. switching to a different default DAMode) breaks loud here, not
        // when an operator without --config sees a different label in the RPC.
        var d = DevnetLabelOverrides.Defaults;
        Assert.AreEqual(SecurityLevel.Optimistic, d.SecurityLevel);
        Assert.AreEqual(DAMode.External, d.DAMode);
        Assert.IsFalse(d.GatewayEnabled);
        Assert.AreEqual(SequencerModel.DbftCommittee, d.Sequencer);
        Assert.AreEqual(ExitModel.Permissionless, d.Exit);
    }

    [TestMethod]
    public void NullPath_ReturnsDefaults()
    {
        var result = DevnetLabelOverrides.ReadFromConfig(null);
        Assert.AreEqual(DevnetLabelOverrides.Defaults, result);
    }

    [TestMethod]
    public void NonExistentPath_ReturnsDefaults_WithWarning()
    {
        var origErr = Console.Error;
        try
        {
            var sw = new StringWriter();
            Console.SetError(sw);
            var result = DevnetLabelOverrides.ReadFromConfig(
                Path.Combine(_tempDir, "does-not-exist.json"));
            Assert.AreEqual(DevnetLabelOverrides.Defaults, result);
            StringAssert.Contains(sw.ToString(), "not found");
            StringAssert.Contains(sw.ToString(), "falling back to defaults");
        }
        finally
        {
            Console.SetError(origErr);
        }
    }

    [TestMethod]
    public void MalformedJson_ReturnsDefaults_WithWarning()
    {
        // Devnet should still run with defaults rather than abort. Pin so a
        // future refactor that throws on malformed JSON would surface here.
        var path = Path.Combine(_tempDir, "garbage.json");
        File.WriteAllText(path, "{not valid json");

        var origErr = Console.Error;
        try
        {
            var sw = new StringWriter();
            Console.SetError(sw);
            var result = DevnetLabelOverrides.ReadFromConfig(path);
            Assert.AreEqual(DevnetLabelOverrides.Defaults, result);
            StringAssert.Contains(sw.ToString(), "parse failed");
            StringAssert.Contains(sw.ToString(), "falling back to defaults");
        }
        finally
        {
            Console.SetError(origErr);
        }
    }

    [TestMethod]
    public void ValidConfig_AllFieldsOverride()
    {
        var path = Path.Combine(_tempDir, "validium.json");
        File.WriteAllText(path, @"{
            ""securityLevel"": ""Validium"",
            ""daMode"": ""NeoFS"",
            ""gatewayEnabled"": true,
            ""sequencerModel"": ""Centralized"",
            ""exitModel"": ""Delayed""
        }");

        var result = DevnetLabelOverrides.ReadFromConfig(path);
        Assert.AreEqual(SecurityLevel.Validium, result.SecurityLevel);
        Assert.AreEqual(DAMode.NeoFS, result.DAMode);
        Assert.IsTrue(result.GatewayEnabled);
        Assert.AreEqual(SequencerModel.Centralized, result.Sequencer);
        Assert.AreEqual(ExitModel.Delayed, result.Exit);
    }

    [TestMethod]
    public void PartialConfig_MissingFieldsUseDefaults()
    {
        // Only securityLevel + daMode set — other three fields use defaults.
        // Pin per-field independence: a partial config still produces a valid
        // overrides record without throwing.
        var path = Path.Combine(_tempDir, "partial.json");
        File.WriteAllText(path, @"{
            ""securityLevel"": ""Validity"",
            ""daMode"": ""L1""
        }");

        var result = DevnetLabelOverrides.ReadFromConfig(path);
        Assert.AreEqual(SecurityLevel.Validity, result.SecurityLevel);
        Assert.AreEqual(DAMode.L1, result.DAMode);
        // Others fall back to defaults.
        Assert.IsFalse(result.GatewayEnabled);
        Assert.AreEqual(SequencerModel.DbftCommittee, result.Sequencer);
        Assert.AreEqual(ExitModel.Permissionless, result.Exit);
    }

    [TestMethod]
    public void UnknownEnumValue_FallsBackToFieldDefault()
    {
        // Permissive-fallback semantics: unknown enum values for any field
        // produce that field's default rather than aborting the whole devnet
        // run. The operator's `neo-stack validate` step is where they get a
        // hard error; the devnet itself still boots.
        var path = Path.Combine(_tempDir, "typo.json");
        File.WriteAllText(path, @"{
            ""securityLevel"": ""Optimistic"",
            ""daMode"": ""TYPO_HERE""
        }");

        var result = DevnetLabelOverrides.ReadFromConfig(path);
        Assert.AreEqual(SecurityLevel.Optimistic, result.SecurityLevel,
            "valid field still applies");
        Assert.AreEqual(DAMode.External, result.DAMode,
            "invalid enum value falls back to that field's default");
    }

    [TestMethod]
    public void ParseEnumOrDefault_PerFieldBehavior()
    {
        // Direct test of the per-field parser: missing field, valid value,
        // invalid value all produce sensible results.
        using var doc = System.Text.Json.JsonDocument.Parse(
            @"{ ""good"": ""Validium"", ""bad"": ""nope"" }");
        var root = doc.RootElement;

        Assert.AreEqual(SecurityLevel.Validium,
            DevnetLabelOverrides.ParseEnumOrDefault(root, "good", SecurityLevel.Optimistic));
        Assert.AreEqual(SecurityLevel.Optimistic,
            DevnetLabelOverrides.ParseEnumOrDefault(root, "bad", SecurityLevel.Optimistic),
            "invalid string → fallback");
        Assert.AreEqual(SecurityLevel.Optimistic,
            DevnetLabelOverrides.ParseEnumOrDefault(root, "absent", SecurityLevel.Optimistic),
            "missing field → fallback");
    }

    [TestMethod]
    public void GatewayEnabled_StringInsteadOfBool_FallsBackToDefaults()
    {
        // JSON type mismatch: gatewayEnabled emitted as a string instead of bool.
        // GetBoolean() throws on non-bool → caught by outer try/catch → all five
        // dimensions fall back to defaults. Pin this so a refactor that changes
        // the recovery path (e.g. making just gatewayEnabled tolerant) doesn't
        // silently accept malformed config.
        var path = Path.Combine(_tempDir, "type-mismatch.json");
        File.WriteAllText(path, @"{ ""gatewayEnabled"": ""true"" }");

        var origErr = Console.Error;
        try
        {
            var sw = new StringWriter();
            Console.SetError(sw);
            var result = DevnetLabelOverrides.ReadFromConfig(path);
            Assert.AreEqual(DevnetLabelOverrides.Defaults, result,
                "JSON type mismatch on any field falls back to ALL defaults (whole-document recovery)");
            StringAssert.Contains(sw.ToString(), "parse failed");
        }
        finally
        {
            Console.SetError(origErr);
        }
    }

    [TestMethod]
    public void GatewayEnabled_NullValue_FallsBackToDefaults()
    {
        // Same shape as above but with explicit null. JsonElement.GetBoolean()
        // throws on null; outer catch falls back.
        var path = Path.Combine(_tempDir, "null-bool.json");
        File.WriteAllText(path, @"{ ""gatewayEnabled"": null }");

        var origErr = Console.Error;
        try
        {
            var sw = new StringWriter();
            Console.SetError(sw);
            var result = DevnetLabelOverrides.ReadFromConfig(path);
            Assert.AreEqual(DevnetLabelOverrides.Defaults, result);
            StringAssert.Contains(sw.ToString(), "parse failed");
        }
        finally
        {
            Console.SetError(origErr);
        }
    }

    [TestMethod]
    public void EmptyJsonObject_AllFieldsFallToDefaults()
    {
        // {} — completely empty config. All five fields fall back to per-field
        // defaults via TryGetProperty's miss path. This is the "operator
        // pointed --config at an unrelated JSON file" recovery.
        var path = Path.Combine(_tempDir, "empty.json");
        File.WriteAllText(path, "{}");
        var result = DevnetLabelOverrides.ReadFromConfig(path);
        Assert.AreEqual(DevnetLabelOverrides.Defaults, result);
    }

    [TestMethod]
    public void EnumIsCaseSensitive()
    {
        // Pin: lowercase "optimistic" doesn't match SecurityLevel.Optimistic.
        // The neo-stack validate step rejects this; here we verify the devnet's
        // permissive-fallback returns the default (not the case-insensitive match).
        var path = Path.Combine(_tempDir, "lower.json");
        File.WriteAllText(path, @"{ ""securityLevel"": ""optimistic"" }");
        var result = DevnetLabelOverrides.ReadFromConfig(path);
        Assert.AreEqual(SecurityLevel.Optimistic, result.SecurityLevel,
            "case-mismatch falls back to default — which here happens to also be Optimistic");

        var path2 = Path.Combine(_tempDir, "lower2.json");
        File.WriteAllText(path2, @"{ ""securityLevel"": ""validium"" }");
        var result2 = DevnetLabelOverrides.ReadFromConfig(path2);
        Assert.AreEqual(SecurityLevel.Optimistic, result2.SecurityLevel,
            "lowercase 'validium' does NOT match Validium — defaults to Optimistic");
    }
}
