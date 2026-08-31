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
        ""vm"": ""neovm2-riscv"",
        ""chainMode"": ""L2RollupMode"",
        ""daMode"": ""NeoFS"",
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
        StringAssert.Contains(output, "daMode=NeoFS");
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
    public void Validate_UnknownChainMode_ExitsTwo()
    {
        // chainMode is the §6 enum (L1Mode / SidechainMode / L2RollupMode /
        // L2ValidiumMode). An unrecognized value (typo) must reject loud.
        var typo = ValidRollupConfig().Replace("\"L2RollupMode\"", "\"L2RolupMode\"");  // typo
        var path = WriteConfig(typo);
        var (rc, _, stderr) = CaptureBoth(() => ValidateChainConfigCommand.Run(new[] { path }));
        Assert.AreEqual(2, rc);
        StringAssert.Contains(stderr, "expected one of:");
    }

    [TestMethod]
    public void Validate_MissingChainMode_ExitsTwo()
    {
        var noChainMode = ValidRollupConfig().Replace("\"chainMode\": \"L2RollupMode\",", "");
        var path = WriteConfig(noChainMode);
        var (rc, _, stderr) = CaptureBoth(() => ValidateChainConfigCommand.Run(new[] { path }));
        Assert.AreEqual(2, rc);
        StringAssert.Contains(stderr, "missing 'chainMode'");
    }

    [TestMethod]
    public void Validate_CrossFieldWarning_ValidiumModeWithL1DA()
    {
        // chainMode=L2ValidiumMode + daMode=L1 is a spec contradiction (validium
        // means OFF-chain DA by definition, per doc.md §6 + §12). Pin the warning.
        var contradiction = ValidRollupConfig()
            .Replace("\"chainMode\": \"L2RollupMode\"", "\"chainMode\": \"L2ValidiumMode\"")
            .Replace("\"daMode\": \"NeoFS\"", "\"daMode\": \"L1\"");
        // daMode explicitly changed to "L1" → contradiction
        var path = WriteConfig(contradiction);
        var (rc, output) = CaptureStdout(() => ValidateChainConfigCommand.Run(new[] { path }));
        Assert.AreEqual(0, rc, "cross-field warning is informational, not fatal");
        StringAssert.Contains(output, "⚠");
        StringAssert.Contains(output, "L2ValidiumMode");
        StringAssert.Contains(output, "off-chain DA");
    }

    [TestMethod]
    public void Validate_CrossFieldWarning_OperatorAssistedExitWithPermissionlessTrue()
    {
        // exitModel=OperatorAssisted means 'user exit requires operator
        // co-sign'. That's the opposite of permissionless. A chain claiming
        // both OperatorAssisted AND permissionlessExit=true is contradictory —
        // pin the warning so the operator doesn't ship a chain that promises
        // permissionlessness it can't deliver.
        var contradiction = ValidRollupConfig()
            .Replace("\"exitModel\": \"Delayed\"", "\"exitModel\": \"OperatorAssisted\"");
        // permissionlessExit still true → contradiction
        var path = WriteConfig(contradiction);
        var (rc, output) = CaptureStdout(() => ValidateChainConfigCommand.Run(new[] { path }));
        Assert.AreEqual(0, rc, "cross-field warning is informational, not fatal");
        StringAssert.Contains(output, "⚠");
        StringAssert.Contains(output, "OperatorAssisted");
        StringAssert.Contains(output, "permissionlessExit=true");
    }

    [TestMethod]
    public void Validate_CrossFieldNoWarning_OperatorAssistedExitWithPermissionlessFalse()
    {
        // OperatorAssisted + permissionlessExit=false is consistent.
        var consistent = ValidRollupConfig()
            .Replace("\"exitModel\": \"Delayed\"", "\"exitModel\": \"OperatorAssisted\"")
            .Replace("\"permissionlessExit\": true", "\"permissionlessExit\": false");
        var path = WriteConfig(consistent);
        var (rc, output) = CaptureStdout(() => ValidateChainConfigCommand.Run(new[] { path }));
        Assert.AreEqual(0, rc);
        Assert.IsFalse(output.Contains("OperatorAssisted contradicts"),
            "OperatorAssisted + permissionlessExit=false is consistent — no contradiction warning");
    }

    [TestMethod]
    public void Validate_CrossFieldWarning_L1ModeInL2Config()
    {
        // L1Mode is reserved for the actual Neo L1 (per ChainMode doc: "Plain
        // Neo L1"). A chain.config.json describes an L2; L1Mode in there is
        // internally contradictory. Pin the warning so an operator who
        // copy-pasted a wrong config sees it before deploying against L1.
        var l1Mode = ValidRollupConfig().Replace("\"chainMode\": \"L2RollupMode\"", "\"chainMode\": \"L1Mode\"");
        var path = WriteConfig(l1Mode);
        var (rc, output) = CaptureStdout(() => ValidateChainConfigCommand.Run(new[] { path }));
        Assert.AreEqual(0, rc, "warning is informational, not fatal");
        StringAssert.Contains(output, "⚠");
        StringAssert.Contains(output, "L1Mode is reserved");
    }

    [TestMethod]
    public void Validate_CrossFieldNoWarning_ValidiumModeWithNeoFS()
    {
        // Canonical validium template: L2ValidiumMode + NeoFS DA — no contradiction.
        var validium = ValidRollupConfig()
            .Replace("\"chainMode\": \"L2RollupMode\"", "\"chainMode\": \"L2ValidiumMode\"")
            .Replace("\"securityLevel\": \"Optimistic\"", "\"securityLevel\": \"Validium\"")
            .Replace("\"proofType\": \"Optimistic\"", "\"proofType\": \"Zk\"");
        var path = WriteConfig(validium);
        var (rc, output) = CaptureStdout(() => ValidateChainConfigCommand.Run(new[] { path }));
        Assert.AreEqual(0, rc);
        Assert.IsFalse(output.Contains("L2ValidiumMode contradicts"),
            "L2ValidiumMode + NeoFS DA is canonical — no contradiction warning");
    }

    [TestMethod]
    public void Validate_UnknownDaMode_ExitsTwo()
    {
        var typo = ValidRollupConfig().Replace("\"daMode\": \"NeoFS\"", "\"daMode\": \"L99\"");
        var path = WriteConfig(typo);
        var (rc, _, _) = CaptureBoth(() => ValidateChainConfigCommand.Run(new[] { path }));
        Assert.AreEqual(2, rc);
    }

    [TestMethod]
    public void Validate_UnknownVm_ExitsTwo()
    {
        var typo = ValidRollupConfig().Replace("\"vm\": \"neovm2-riscv\"", "\"vm\": \"neovm3\"");
        var path = WriteConfig(typo);
        var (rc, _, stderr) = CaptureBoth(() => ValidateChainConfigCommand.Run(new[] { path }));
        Assert.AreEqual(2, rc);
        StringAssert.Contains(stderr, "'vm'='neovm3'");
    }

    [TestMethod]
    public void Validate_LegacyNeoVm_ExitsZero_WithWarning()
    {
        var legacy = ValidRollupConfig().Replace("\"vm\": \"neovm2-riscv\"", "\"vm\": \"neovm\"");
        var path = WriteConfig(legacy);
        var (rc, output) = CaptureStdout(() => ValidateChainConfigCommand.Run(new[] { path }));
        Assert.AreEqual(0, rc);
        StringAssert.Contains(output, "legacy compatibility");
        StringAssert.Contains(output, "neovm2-riscv");
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
    public void Validate_CrossFieldNoWarning_OptimisticWithZk()
    {
        // The shipped rollup pairing: SecurityLevel.Optimistic promises at least a
        // challenge window, ProofType.Zk over-delivers on it, and Zk is the route
        // Neo.Hub.Deploy registers. Legal and served — nothing to warn about.
        var config = ValidRollupConfig().Replace("\"proofType\": \"Optimistic\"", "\"proofType\": \"Zk\"");
        var path = WriteConfig(config);
        var (rc, output) = CaptureStdout(() => ValidateChainConfigCommand.Run(new[] { path }));
        Assert.AreEqual(0, rc);
        Assert.IsFalse(output.Contains("⚠"),
            "Optimistic + Zk is legal and has a production verifier route, so it must NOT warn");
    }

    [TestMethod]
    public void Validate_CrossFieldWarning_LegalProofWithNoProductionVerifierRoute()
    {
        // Optimistic + Optimistic passes SettlementManager.IsProofTypeCompatible but is
        // not deployable: Neo.Hub.Deploy registers only the Zk route and then locks
        // VerifierRegistry, so the very first submitBatch faults with
        // "no verifier for proof type". Pre-H18 the validator called this pair
        // consistent, which is how the rollup template shipped a config that could
        // never settle on the hub the same tool builds.
        var path = WriteConfig(ValidRollupConfig());  // proofType=Optimistic, securityLevel=Optimistic
        var (rc, output) = CaptureStdout(() => ValidateChainConfigCommand.Run(new[] { path }));
        Assert.AreEqual(0, rc, "an unserved route is a warning, not a fatal error");
        StringAssert.Contains(output, "⚠");
        StringAssert.Contains(output, "legal for securityLevel=Optimistic");
        StringAssert.Contains(output, "no verifier route in the shipped production bundle");
        StringAssert.Contains(output, "no verifier for proof type");
    }

    [TestMethod]
    public void Validate_CrossFieldWarning_ValidiumWithNonZkProof()
    {
        // Same shape as the Validity warning but for SecurityLevel.Validium → ProofType.Zk.
        // Pin so operators who type a validium template + accidentally non-Zk proofType
        // see the warning instead of silently shipping a misconfigured chain.
        var validiumWithOptimistic = ValidRollupConfig()
            .Replace("\"securityLevel\": \"Optimistic\"", "\"securityLevel\": \"Validium\"");
        // proofType still "Optimistic" → mismatch
        var path = WriteConfig(validiumWithOptimistic);
        var (rc, output) = CaptureStdout(() => ValidateChainConfigCommand.Run(new[] { path }));
        Assert.AreEqual(0, rc, "cross-field warning is informational, not fatal");
        StringAssert.Contains(output, "⚠");
        StringAssert.Contains(output, "Validium");
        StringAssert.Contains(output, "Zk");
    }

    [TestMethod]
    public void Validate_CrossFieldNoWarning_SidechainWithZk()
    {
        // Sidechain is the weakest promise, so a validity proof over-delivers on it and
        // SettlementManager accepts the pair; Zk is also the route the locked
        // VerifierRegistry actually serves. Pre-H18 the CLI called this combination
        // unusual and steered operators toward None — a proof type the contract, the
        // registry and the batcher all refuse.
        var sidechainWithZk = ValidRollupConfig()
            .Replace("\"securityLevel\": \"Optimistic\"", "\"securityLevel\": \"Sidechain\"")
            .Replace("\"chainMode\": \"L2RollupMode\"", "\"chainMode\": \"SidechainMode\"")
            .Replace("\"proofType\": \"Optimistic\"", "\"proofType\": \"Zk\"");
        var path = WriteConfig(sidechainWithZk);
        var (rc, output) = CaptureStdout(() => ValidateChainConfigCommand.Run(new[] { path }));
        Assert.AreEqual(0, rc);
        Assert.IsFalse(output.Contains("⚠"),
            $"Sidechain + Zk settles on the shipped hub and must not warn.\nOutput:\n{output}");
    }

    [TestMethod]
    public void Validate_CrossFieldWarning_SidechainWithMultisig_HasNoProductionVerifierRoute()
    {
        // The sidechain template's own pairing: legal for the label (a committee
        // attestation is exactly what a sidechain promises) but Neo.Hub.Deploy freezes
        // VerifierRegistry with only the Zk route, so this chain cannot settle on that
        // hub unless an operator registers Multisig before the lock. Say so out loud —
        // samples/privacy-sidechain.config.json depends on this warning firing.
        var sidechainWithMultisig = ValidRollupConfig()
            .Replace("\"securityLevel\": \"Optimistic\"", "\"securityLevel\": \"Sidechain\"")
            .Replace("\"chainMode\": \"L2RollupMode\"", "\"chainMode\": \"SidechainMode\"")
            .Replace("\"proofType\": \"Optimistic\"", "\"proofType\": \"Multisig\"");
        var path = WriteConfig(sidechainWithMultisig);
        var (rc, output) = CaptureStdout(() => ValidateChainConfigCommand.Run(new[] { path }));
        Assert.AreEqual(0, rc);
        StringAssert.Contains(output, "⚠");
        StringAssert.Contains(output, "proofType=Multisig is legal for securityLevel=Sidechain");
        StringAssert.Contains(output, "no verifier route in the shipped production bundle");
    }

    [TestMethod]
    public void Validate_CrossFieldNoWarning_ValidiumWithZk()
    {
        // validium template default: chainMode=L2ValidiumMode + SecurityLevel.Validium
        // + ProofType.Zk + daMode=NeoFS — all internally consistent.
        var validiumZk = ValidRollupConfig()
            .Replace("\"chainMode\": \"L2RollupMode\"", "\"chainMode\": \"L2ValidiumMode\"")
            .Replace("\"securityLevel\": \"Optimistic\"", "\"securityLevel\": \"Validium\"")
            .Replace("\"proofType\": \"Optimistic\"", "\"proofType\": \"Zk\"");
        var path = WriteConfig(validiumZk);
        var (rc, output) = CaptureStdout(() => ValidateChainConfigCommand.Run(new[] { path }));
        Assert.AreEqual(0, rc);
        Assert.IsFalse(output.Contains("⚠"),
            "Validium template (L2ValidiumMode + Validium + Zk + NeoFS) is canonical — no warning");
    }

    [TestMethod]
    public void Validate_CrossFieldWarning_SidechainWithNone_IsRejectedByTheContract()
    {
        // The sidechain template used to ship proofType=None. None is not a settlement
        // route anywhere: SettlementManager.IsProofTypeCompatible has no None row,
        // VerifierRegistry.WriteVerifier refuses byte 0, and the batcher has no None
        // proof artifact to build. Pin the warning + the accepted set so a future edit
        // can't restore None as a "sidechains don't prove" shorthand.
        var sidechain = ValidRollupConfig()
            .Replace("\"chainMode\": \"L2RollupMode\"", "\"chainMode\": \"SidechainMode\"")
            .Replace("\"securityLevel\": \"Optimistic\"", "\"securityLevel\": \"Sidechain\"")
            .Replace("\"proofType\": \"Optimistic\"", "\"proofType\": \"None\"");
        var path = WriteConfig(sidechain);
        var (rc, output) = CaptureStdout(() => ValidateChainConfigCommand.Run(new[] { path }));
        Assert.AreEqual(0, rc);
        StringAssert.Contains(output, "⚠");
        StringAssert.Contains(output, "securityLevel=Sidechain accepts only proofType=Multisig / Optimistic / Zk");
        StringAssert.Contains(output, "SettlementManager.IsProofTypeCompatible");
    }

    [TestMethod]
    public void Validate_CrossFieldWarning_RollupModeWithValidiumLevel()
    {
        // L2RollupMode pairs with SecurityLevel ∈ {Optimistic, Validity} — Validium
        // belongs with L2ValidiumMode (off-chain DA), not the rollup-mode (on-chain DA).
        // Operator copying a rollup template + flipping securityLevel to Validium without
        // also flipping chainMode would silently ship an internally-contradictory config.
        var contradictory = ValidRollupConfig()
            .Replace("\"securityLevel\": \"Optimistic\"", "\"securityLevel\": \"Validium\"");
        var path = WriteConfig(contradictory);
        var (rc, output) = CaptureStdout(() => ValidateChainConfigCommand.Run(new[] { path }));
        Assert.AreEqual(0, rc, "warning is informational, not fatal");
        StringAssert.Contains(output, "⚠");
        StringAssert.Contains(output, "L2RollupMode");
        StringAssert.Contains(output, "Optimistic, Validity");
    }

    [TestMethod]
    public void Validate_CrossFieldWarning_SidechainModeWithValidityLevel()
    {
        // SidechainMode pairs with SecurityLevel ∈ {Sidechain, Settled} — sidechains
        // don't verify state-transitions on L1, so claiming Validity (which requires
        // L1 ZK verification) is contradictory. Pin to catch a copy-paste bug where
        // someone leaves chainMode=SidechainMode but raises securityLevel.
        var contradictory = ValidRollupConfig()
            .Replace("\"chainMode\": \"L2RollupMode\"", "\"chainMode\": \"SidechainMode\"")
            .Replace("\"securityLevel\": \"Optimistic\"", "\"securityLevel\": \"Validity\"");
        var path = WriteConfig(contradictory);
        var (rc, output) = CaptureStdout(() => ValidateChainConfigCommand.Run(new[] { path }));
        Assert.AreEqual(0, rc);
        StringAssert.Contains(output, "⚠");
        StringAssert.Contains(output, "SidechainMode");
        StringAssert.Contains(output, "Sidechain, Settled");
    }

    [TestMethod]
    public void Validate_CrossFieldWarning_ValidiumModeWithOptimisticLevel()
    {
        // L2ValidiumMode pairs with SecurityLevel=Validium (its defining property:
        // L1 ZK proof + off-chain DA). Optimistic implies a challenge window with
        // on-chain DA — contradictory with validium's off-chain-DA premise.
        var contradictory = ValidRollupConfig()
            .Replace("\"chainMode\": \"L2RollupMode\"", "\"chainMode\": \"L2ValidiumMode\"");
        // securityLevel stays "Optimistic" from base config
        var path = WriteConfig(contradictory);
        var (rc, output) = CaptureStdout(() => ValidateChainConfigCommand.Run(new[] { path }));
        Assert.AreEqual(0, rc);
        StringAssert.Contains(output, "⚠");
        StringAssert.Contains(output, "L2ValidiumMode pairs with securityLevel=Validium");
    }

    [TestMethod]
    public void Validate_AllShippedSamples_HaveNoCrossFieldWarnings()
    {
        // Templates ship as canonical references; if a future edit introduces a
        // cross-field contradiction in any sample, every operator who copies it
        // inherits the contradiction. Pin all 4 shipped samples against the same
        // policy the create-chain and new-l2 guards use: no under-delivery warning
        // anywhere, and the one sample shipped as a documented missing-verifier-route
        // case (privacy-sidechain) still prints exactly that caveat.
        //
        // This is an integration-style guard: it exercises the actual JSON files
        // under samples/ + the actual ValidateChainConfigCommand path. If a future
        // commit adds a new validate warning that incidentally fires on an existing
        // sample, this test catches it before the sample ships broken.
        foreach (var name in new[] { "general-rollup", "gaming-rollup", "exchange-validium", "privacy-sidechain" })
            ShippedConfigWarningPolicy.AssertConsistent(name, RunSample(name));
    }

    private static string RunSample(string name)
    {
        var path = Path.Combine(FindSamplesRoot(), $"{name}.config.json");
        Assert.IsTrue(File.Exists(path), $"sample missing: {path}");
        var (rc, output) = CaptureStdout(() => ValidateChainConfigCommand.Run(new[] { path }));
        Assert.AreEqual(0, rc, $"{name} must validate cleanly (rc=0); got: {output}");
        return output;
    }

    private static string FindSamplesRoot()
    {
        // Walk up from bin/Debug/net10.0/ to repo root; samples/ lives there.
        // Mirrors the locator in UT_Samples — keeping it inline avoids a
        // cross-test-project dependency.
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
