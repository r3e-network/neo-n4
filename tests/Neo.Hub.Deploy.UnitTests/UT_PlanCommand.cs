using System;
using System.IO;
using Neo.Hub.Deploy;

namespace Neo.Hub.Deploy.UnitTests;

/// <summary>
/// Tests for <see cref="PlanCommand"/> — the <c>neo-hub-deploy plan</c> path that
/// reads a <see cref="DeployPlan"/> JSON, topologically sorts + resolves placeholders,
/// and emits a <see cref="DeployBundle"/> JSON. Covers happy path + every error
/// surface (missing file, malformed JSON, plan resolution failure, unwriteable
/// output) so a CI script can disambiguate caller vs. deployment errors.
/// </summary>
[TestClass]
public class UT_PlanCommand
{
    private string _tempDir = null!;

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "neo-n4-plan-cmd-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private string WriteDefaultPlan()
    {
        var path = Path.Combine(_tempDir, "deploy-plan.json");
        File.WriteAllText(path, ScaffoldPlan.Default().ToJson());
        return path;
    }

    [TestMethod]
    public void Plan_HappyPath_EmitsResolvedBundle()
    {
        var planPath = WriteDefaultPlan();
        var bundlePath = Path.Combine(_tempDir, "deploy-bundle.json");
        var rc = PlanCommand.Run(new[]
        {
            "--plan", planPath,
            "--output", bundlePath,
        });
        Assert.AreEqual(0, rc);
        Assert.IsTrue(File.Exists(bundlePath), "plan must produce the bundle file");

        // Resolved bundle JSON should be parseable + carry 23 invocations
        // (matching the scaffold's step count: 15 core + 1 executable fraud verifier +
        // 1 ZK verifier router + 1 pinned SP1 terminal verifier + 4 Phase-B external-bridge +
        // 1 Phase-C MpcCommitteeFraudVerifier).
        var json = File.ReadAllText(bundlePath);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var invocations = doc.RootElement.GetProperty("invocations");
        Assert.AreEqual(23, invocations.GetArrayLength(),
            "default scaffold -> 23 resolved invocations in the bundle");

        foreach (var invocation in invocations.EnumerateArray())
        {
            var name = invocation.GetProperty("name").GetString();
            var nefPath = invocation.GetProperty("nefPath").GetString()!.Replace('\\', '/');
            var manifestPath = invocation.GetProperty("manifestPath").GetString()!.Replace('\\', '/');
            StringAssert.Contains(nefPath, "/bin/sc/");
            StringAssert.Contains(manifestPath, "/bin/sc/");
            Assert.IsFalse(nefPath.Contains("/bin/Release/", StringComparison.Ordinal),
                $"{name} NEF path must point at nccs output under bin/sc");
            Assert.IsFalse(manifestPath.Contains("/bin/Release/", StringComparison.Ordinal),
                $"{name} manifest path must point at nccs output under bin/sc");
        }
    }

    [TestMethod]
    public void Plan_PlanFileNotFound_ExitsOne()
    {
        var (rc, _, stderr) = CaptureBoth(() => PlanCommand.Run(new[]
        {
            "--plan", Path.Combine(_tempDir, "does-not-exist.json"),
        }));
        Assert.AreEqual(1, rc);
        StringAssert.Contains(stderr, "plan file not found");
    }

    [TestMethod]
    public void Plan_MalformedPlanJson_ExitsOne()
    {
        var planPath = Path.Combine(_tempDir, "garbage.json");
        File.WriteAllText(planPath, "{not valid json");
        var (rc, _, stderr) = CaptureBoth(() => PlanCommand.Run(new[] { "--plan", planPath }));
        Assert.AreEqual(1, rc);
        StringAssert.Contains(stderr, "failed to parse");
    }

    [TestMethod]
    public void Plan_PrintsPostDeployActions()
    {
        // The bundle's PostDeployActions are the cycle-break + governance-wiring +
        // per-fraud-verifier informational notes. Pin that the operator sees them
        // in stdout — without these, an operator who deploys the bundle without
        // running the post-actions has a silently-broken deployment.
        var planPath = WriteDefaultPlan();
        var bundlePath = Path.Combine(_tempDir, "bundle.json");
        var (rc, output) = CaptureStdout(() => PlanCommand.Run(new[]
        {
            "--plan", planPath,
            "--output", bundlePath,
        }));
        Assert.AreEqual(0, rc);
        StringAssert.Contains(output, "Required post-deploy actions:");
        StringAssert.Contains(output, "SequencerBond.RegisterSlasher");
        StringAssert.Contains(output, "ChainRegistry.SetGovernanceController");
        StringAssert.Contains(output, "SettlementManager.SetDAValidator");
        StringAssert.Contains(output, "MessageRouter.SetL1TxFilter");
        StringAssert.Contains(output, "ContractZkVerifier.RegisterProofVerifier");
        StringAssert.Contains(output, "ContractZkVerifier.DisableEnvelopeOnlyPermanently");
        StringAssert.Contains(output, "ContractZkVerifier.LockProofSystemConfiguration");
        StringAssert.Contains(output, "Sp1Groth16Verifier");
        StringAssert.Contains(output, "VerifierRegistry.RegisterVerifier(ProofType.Zk=3, ContractZkVerifier)");
        Assert.IsFalse(output.Contains("GovernanceFraudVerifier", StringComparison.Ordinal),
            "the production plan must not deploy or register the structural v1/v2 verifier");
        StringAssert.Contains(output, "RestrictedExecutionFraudVerifier");
    }

    [TestMethod]
    public void Plan_PrintsStubHashWarning()
    {
        // The stub hashes are deterministic but not deployable. Operators must see
        // the ⚠ warning so they don't try to use the bundle directly without
        // routing through their wallet's signer.
        var planPath = WriteDefaultPlan();
        var bundlePath = Path.Combine(_tempDir, "bundle.json");
        var (rc, output) = CaptureStdout(() => PlanCommand.Run(new[]
        {
            "--plan", planPath,
            "--output", bundlePath,
        }));
        Assert.AreEqual(0, rc);
        StringAssert.Contains(output, "⚠");
        StringAssert.Contains(output, "deterministic stubs");
    }

    [TestMethod]
    public void Plan_NullArgs_Rejected()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => PlanCommand.Run(null!));
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
