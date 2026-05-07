using System;
using System.IO;
using Neo.Stack.Cli.Commands;

namespace Neo.Stack.Cli.UnitTests;

/// <summary>
/// Tests for <see cref="ValidateChainConfigCommand"/> — the JSON sanity-check that
/// catches operator-edit typos in <c>chain.config.json</c> before they reach
/// <c>register-chain</c> or the devnet. Exit-code contract: 0 = valid (with a
/// human-readable summary line), 1 = caller error (no args), 2 = file or parse
/// failure (with a ❌ diagnostic naming the failing field).
/// </summary>
[TestClass]
public class UT_ValidateChainConfigCommand
{
    private string _tempDir = null!;

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "neo-n4-validate-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private string WriteConfig(string json)
    {
        var path = Path.Combine(_tempDir, "chain.config.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static string ValidRollupConfig(uint chainId = 1099) => $@"{{
        ""chainId"": {chainId},
        ""template"": ""rollup"",
        ""vm"": ""neovm"",
        ""chainMode"": ""L2RollupMode"",
        ""daMode"": ""L1"",
        ""proofType"": ""Optimistic"",
        ""securityLevel"": ""Optimistic"",
        ""sequencerModel"": ""DbftCommittee"",
        ""exitModel"": ""Delayed"",
        ""gatewayEnabled"": false,
        ""permissionlessExit"": true,
        ""milestonePerBlockMs"": 5000,
        ""validators"": []
    }}";

    [TestMethod]
    public void Validate_ValidRollupConfig_ExitsZero()
    {
        var path = WriteConfig(ValidRollupConfig());
        var (rc, output) = CaptureStdout(() => ValidateChainConfigCommand.Run(new[] { path }));
        Assert.AreEqual(0, rc, "valid rollup config must exit 0");
        StringAssert.Contains(output, "✅ valid:");
        StringAssert.Contains(output, "chainId=1099");
        StringAssert.Contains(output, "securityLevel=Optimistic");
        StringAssert.Contains(output, "daMode=L1");
    }

    [TestMethod]
    public void Validate_NoArgs_ExitsOne()
    {
        // Caller error: no path provided. Distinct from exit 2 (validation failure).
        var (rc, _, stderr) = CaptureBoth(() => ValidateChainConfigCommand.Run(Array.Empty<string>()));
        Assert.AreEqual(1, rc);
        StringAssert.Contains(stderr, "Usage:");
    }

    [TestMethod]
    public void Validate_FileNotFound_ExitsTwo()
    {
        var (rc, _, stderr) = CaptureBoth(() => ValidateChainConfigCommand.Run(
            new[] { Path.Combine(_tempDir, "does-not-exist.json") }));
        Assert.AreEqual(2, rc);
        StringAssert.Contains(stderr, "file not found");
    }

    [TestMethod]
    public void Validate_UnparseableJson_ExitsTwo()
    {
        var path = WriteConfig("{not valid json");
        var (rc, _, stderr) = CaptureBoth(() => ValidateChainConfigCommand.Run(new[] { path }));
        Assert.AreEqual(2, rc);
        // The JSON parser's exception message should surface in stderr.
        StringAssert.Contains(stderr, "❌");
    }

    [TestMethod]
    public void Validate_MissingChainId_ExitsTwo_NamesField()
    {
        // Any required field missing → exit 2 with the field name in the diagnostic.
        // The diagnostic is operator-facing — it has to point at the bad field clearly.
        var noChainId = ValidRollupConfig().Replace("\"chainId\": 1099,", "");
        var path = WriteConfig(noChainId);
        var (rc, _, stderr) = CaptureBoth(() => ValidateChainConfigCommand.Run(new[] { path }));
        Assert.AreEqual(2, rc);
        StringAssert.Contains(stderr, "missing 'chainId'");
    }

    [TestMethod]
    public void Validate_ChainIdZero_ExitsTwo()
    {
        // chainId 0 is the L1 sentinel — must be rejected just like ChainIdValidator.ValidateL2 does.
        var path = WriteConfig(ValidRollupConfig(0));
        var (rc, _, _) = CaptureBoth(() => ValidateChainConfigCommand.Run(new[] { path }));
        Assert.AreEqual(2, rc, "chainId=0 (L1 sentinel) must be rejected");
    }

    [TestMethod]
    public void Validate_UnknownSecurityLevel_ExitsTwo_ListsValidValues()
    {
        // Catches operator typos like "securityLevel": "optimistic" (lowercase).
        // The error message must list valid values to be operator-friendly.
        var typo = ValidRollupConfig().Replace("\"Optimistic\"", "\"OPTIMISTIC\"");
        var path = WriteConfig(typo);
        var (rc, _, stderr) = CaptureBoth(() => ValidateChainConfigCommand.Run(new[] { path }));
        Assert.AreEqual(2, rc);
        StringAssert.Contains(stderr, "expected one of:");
    }

    [TestMethod]
    public void Validate_UnknownDaMode_ExitsTwo()
    {
        var typo = ValidRollupConfig().Replace("\"daMode\": \"L1\"", "\"daMode\": \"L99\"");
        var path = WriteConfig(typo);
        var (rc, _, _) = CaptureBoth(() => ValidateChainConfigCommand.Run(new[] { path }));
        Assert.AreEqual(2, rc);
    }

    [TestMethod]
    public void Validate_UnknownSequencerModel_ExitsTwo()
    {
        var typo = ValidRollupConfig().Replace("\"DbftCommittee\"", "\"FOO\"");
        var path = WriteConfig(typo);
        var (rc, _, _) = CaptureBoth(() => ValidateChainConfigCommand.Run(new[] { path }));
        Assert.AreEqual(2, rc);
    }

    [TestMethod]
    public void Validate_MissingGatewayEnabledBool_ExitsTwo()
    {
        var noGateway = ValidRollupConfig().Replace("\"gatewayEnabled\": false,", "");
        var path = WriteConfig(noGateway);
        var (rc, _, stderr) = CaptureBoth(() => ValidateChainConfigCommand.Run(new[] { path }));
        Assert.AreEqual(2, rc);
        StringAssert.Contains(stderr, "missing 'gatewayEnabled'");
    }

    [TestMethod]
    public void Validate_CrossFieldWarning_ValidityWithNonZkProof()
    {
        // doc.md §16.2: SecurityLevel.Validity should pair with ProofType.Zk. A
        // mismatch isn't fatal (operator can still ship if they know what they're
        // doing) but the validator surfaces a ⚠ warning. Pin both: exit 0 + the
        // warning string is in stdout.
        var validityWithOptimistic = ValidRollupConfig()
            .Replace("\"securityLevel\": \"Optimistic\"", "\"securityLevel\": \"Validity\"");
        // proofType still "Optimistic" → mismatch
        var path = WriteConfig(validityWithOptimistic);
        var (rc, output) = CaptureStdout(() => ValidateChainConfigCommand.Run(new[] { path }));
        Assert.AreEqual(0, rc, "cross-field warning is informational, not fatal");
        StringAssert.Contains(output, "⚠");
        StringAssert.Contains(output, "Validity");
        StringAssert.Contains(output, "Zk");
    }

    [TestMethod]
    public void Validate_CrossFieldNoWarning_OptimisticWithOptimistic()
    {
        // Default rollup config: SecurityLevel.Optimistic + ProofType.Optimistic — no
        // warning. Pin so a regression that emits warnings on every config doesn't
        // sneak in.
        var path = WriteConfig(ValidRollupConfig());
        var (rc, output) = CaptureStdout(() => ValidateChainConfigCommand.Run(new[] { path }));
        Assert.AreEqual(0, rc);
        Assert.IsFalse(output.Contains("⚠"),
            "consistent SecurityLevel + ProofType pair should NOT emit a warning");
    }

    [TestMethod]
    public void Validate_CrossFieldNoWarning_SidechainWithNone()
    {
        // sidechain template defaults: SecurityLevel.Sidechain + ProofType.None — also
        // valid. The cross-field warning logic only fires for Validity / Optimistic
        // combinations, so Sidechain shouldn't trigger it.
        var sidechain = ValidRollupConfig()
            .Replace("\"securityLevel\": \"Optimistic\"", "\"securityLevel\": \"Sidechain\"")
            .Replace("\"proofType\": \"Optimistic\"", "\"proofType\": \"None\"");
        var path = WriteConfig(sidechain);
        var (rc, output) = CaptureStdout(() => ValidateChainConfigCommand.Run(new[] { path }));
        Assert.AreEqual(0, rc);
        Assert.IsFalse(output.Contains("⚠"),
            "Sidechain + None should NOT emit a cross-field warning");
    }

    // ---- Helpers ----

    private static (int rc, string stdout) CaptureStdout(Func<int> run)
    {
        var origOut = Console.Out;
        try
        {
            var sw = new StringWriter();
            Console.SetOut(sw);
            var rc = run();
            return (rc, sw.ToString());
        }
        finally
        {
            Console.SetOut(origOut);
        }
    }

    private static (int rc, string stdout, string stderr) CaptureBoth(Func<int> run)
    {
        var origOut = Console.Out;
        var origErr = Console.Error;
        try
        {
            var swOut = new StringWriter();
            var swErr = new StringWriter();
            Console.SetOut(swOut);
            Console.SetError(swErr);
            var rc = run();
            return (rc, swOut.ToString(), swErr.ToString());
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }
    }
}
