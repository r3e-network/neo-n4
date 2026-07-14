using System;
using System.IO;
using Neo.Hub.Deploy;

namespace Neo.Hub.Deploy.UnitTests;

/// <summary>
/// Tests for <see cref="ScaffoldCommand"/> — the <c>neo-hub-deploy scaffold</c> path
/// that writes a starter <see cref="DeployPlan"/> JSON. Covers the happy path + the
/// boundary defenses (null args, unwriteable output).
/// </summary>
[TestClass]
public class UT_ScaffoldCommand
{
    private string _tempDir = null!;

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "neo-n4-scaffold-cmd-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public void Scaffold_HappyPath_WritesParseablePlan()
    {
        var output = Path.Combine(_tempDir, "deploy-plan.json");
        var rc = ScaffoldCommand.Run(new[] { "--output", output });
        Assert.AreEqual(0, rc);
        Assert.IsTrue(File.Exists(output), "scaffold must produce the output file");

        // Content must be parseable back into a DeployPlan + match the canonical
        // 24-step scaffold (ScaffoldPlan.Default: 15 core + 1 executable fraud verifier
        // + 1 ZK verifier router + 1 pinned SP1 terminal verifier + 4 Phase-B external-bridge
        // + 1 Phase-C MpcCommitteeFraudVerifier + 1 immutable L2 payout adapter).
        var json = File.ReadAllText(output);
        var roundtripped = DeployPlan.FromJson(json);
        Assert.AreEqual(24, roundtripped.Steps.Count);
        Assert.AreEqual(ScaffoldPlan.Default().Steps.Count, roundtripped.Steps.Count);
    }

    [TestMethod]
    public void Scaffold_DefaultOutputPath_UsedWhenOmitted()
    {
        // Without --output, the default is "deploy-plan.json" relative to CWD.
        // Run from the temp dir so we don't pollute the project root.
        var origCwd = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(_tempDir);
            var rc = ScaffoldCommand.Run(Array.Empty<string>());
            Assert.AreEqual(0, rc);
            Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "deploy-plan.json")),
                "default output is relative to CWD");
        }
        finally
        {
            Directory.SetCurrentDirectory(origCwd);
        }
    }

    [TestMethod]
    public void Scaffold_NullArgs_Rejected()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            ScaffoldCommand.Run(null!));
    }

    [TestMethod]
    public void Scaffold_UnwriteableOutput_ExitsOne()
    {
        // Output path that can't be written to (it's a directory, not a file).
        // The defensive try/catch catches the exception + emits a diagnostic to stderr.
        var origErr = Console.Error;
        try
        {
            var sw = new StringWriter();
            Console.SetError(sw);
            var rc = ScaffoldCommand.Run(new[] { "--output", _tempDir });
            Assert.AreEqual(1, rc, "unwriteable output (target is a directory) must exit 1");
            StringAssert.Contains(sw.ToString(), "failed to write");
        }
        finally
        {
            Console.SetError(origErr);
        }
    }
}
