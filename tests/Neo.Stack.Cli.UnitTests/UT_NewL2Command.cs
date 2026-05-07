using System;
using System.IO;
using Neo.Stack.Cli.Commands;

namespace Neo.Stack.Cli.UnitTests;

/// <summary>
/// Tests for <see cref="NewL2Command"/> — the composite that strings together
/// <c>create-chain</c> + <c>init-l2</c> + <c>scaffold-executor --with-tests</c>.
/// Argument validation + refuse-to-overwrite behavior is delegated to the underlying
/// commands; these tests pin the composite's wiring + exit-code propagation.
/// </summary>
[TestClass]
public class UT_NewL2Command
{
    private string _tempDir = null!;

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "neo-n4-newl2-test-" + Guid.NewGuid().ToString("N"));
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public void HappyPath_CreatesAllArtifacts_AndExitsZero()
    {
        var rc = NewL2Command.Run(new[]
        {
            "--name", "TestChain",
            "--chain-id", "5555",
            "--template", "rollup",
            "--output", _tempDir,
        });
        Assert.AreEqual(0, rc, "happy-path composite must exit 0");

        // create-chain artifact: chain.config.json
        Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "chain.config.json")),
            "create-chain step must produce chain.config.json");

        // init-l2 artifacts: data/ logs/ Plugins/
        Assert.IsTrue(Directory.Exists(Path.Combine(_tempDir, "data")), "data/ must exist");
        Assert.IsTrue(Directory.Exists(Path.Combine(_tempDir, "logs")), "logs/ must exist");
        Assert.IsTrue(Directory.Exists(Path.Combine(_tempDir, "Plugins")), "Plugins/ must exist");

        // scaffold-executor artifacts (main project at <output>/<Name>Executor/).
        var execDir = Path.Combine(_tempDir, "TestChainExecutor");
        Assert.IsTrue(File.Exists(Path.Combine(execDir, "TestChainExecutor.csproj")),
            "scaffold step must produce executor csproj");
        Assert.IsTrue(File.Exists(Path.Combine(execDir, "TestChainExecutor.cs")));
        Assert.IsTrue(File.Exists(Path.Combine(execDir, "ITestChainState.cs")));
        Assert.IsTrue(File.Exists(Path.Combine(execDir, "TestChainTxBuilder.cs")));
        Assert.IsTrue(File.Exists(Path.Combine(execDir, "TestChainKeyedStateStoreAdapter.cs")));
        Assert.IsTrue(File.Exists(Path.Combine(execDir, "README.md")));

        // --with-tests: companion tests project.
        var testsDir = Path.Combine(_tempDir, "TestChainExecutor.UnitTests");
        Assert.IsTrue(Directory.Exists(testsDir),
            "--with-tests is implicit for new-l2; .UnitTests/ must be created");
        Assert.IsTrue(File.Exists(Path.Combine(testsDir, "TestChainExecutor.UnitTests.csproj")));
        Assert.IsTrue(File.Exists(Path.Combine(testsDir, "UT_TestChainExecutor.cs")));
    }

    [TestMethod]
    public void MissingName_Rejected_BeforeAnyCommandRuns()
    {
        // --name is required at the composite level. The error must surface here, not
        // trickle through the underlying scaffold-executor's identifier validator.
        var rc = NewL2Command.Run(new[]
        {
            "--chain-id", "5555",
            "--output", _tempDir,
        });
        Assert.AreEqual(1, rc, "missing --name must reject with exit 1");
        // No artifacts created.
        Assert.IsFalse(Directory.Exists(_tempDir),
            "missing --name must reject before create-chain runs (atomic)");
    }

    [TestMethod]
    public void ChainIdZero_Rejected()
    {
        Assert.ThrowsExactly<System.IO.InvalidDataException>(() =>
            NewL2Command.Run(new[]
            {
                "--name", "Foo",
                "--chain-id", "0",
                "--output", _tempDir,
            }));
        Assert.IsFalse(Directory.Exists(_tempDir),
            "L1-sentinel chainId rejection must abort before any artifact creation");
    }

    [TestMethod]
    public void NonNumericChainId_Rejected()
    {
        var rc = NewL2Command.Run(new[]
        {
            "--name", "Foo",
            "--chain-id", "abc",
            "--output", _tempDir,
        });
        Assert.AreEqual(1, rc);
    }

    [TestMethod]
    public void InvalidName_RejectedByScaffoldStep_AfterEarlierStepsRan()
    {
        // create-chain + init-l2 don't validate the name (it's scaffold-executor's
        // concern). So an invalid name reaches scaffold-executor mid-flow; the
        // composite must propagate scaffold-executor's exit code (1) and surface the
        // failed-step diagnostic. The earlier-step artifacts ARE on disk by then —
        // operators can inspect what was written before the failure.
        var rc = NewL2Command.Run(new[]
        {
            "--name", "1Bad",  // digit-first → invalid identifier
            "--chain-id", "5555",
            "--output", _tempDir,
        });
        Assert.AreEqual(1, rc);
        // create-chain ran successfully before the abort.
        Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "chain.config.json")),
            "create-chain ran (and produced its artifact) before scaffold-executor rejected the name");
        // scaffold-executor never created its dir.
        Assert.IsFalse(Directory.Exists(Path.Combine(_tempDir, "1BadExecutor")),
            "invalid-name scaffold step must not create the executor dir");
    }

    [TestMethod]
    public void PathFlagAlias_UsedWhenOutputAbsent()
    {
        // Mirror the create-chain / scaffold-executor behavior: --path is an alias for
        // --output so an operator can use one flag name throughout their automation.
        var rc = NewL2Command.Run(new[]
        {
            "--name", "Foo",
            "--chain-id", "5555",
            "--path", _tempDir,
        });
        Assert.AreEqual(0, rc);
        Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "chain.config.json")));
    }

    [TestMethod]
    public void DefaultOutput_IsChainNumberDir()
    {
        // Without --output / --path, default is `./chain-<chainId>` matching
        // create-chain's behavior. To avoid polluting cwd in CI, run with an explicit
        // --output that mimics the default-format path, then verify the dir was created.
        var defaultMimic = Path.Combine(_tempDir, "chain-5555");
        Directory.CreateDirectory(_tempDir);
        var rc = NewL2Command.Run(new[]
        {
            "--name", "Foo",
            "--chain-id", "5555",
            "--output", defaultMimic,
        });
        Assert.AreEqual(0, rc);
        Assert.IsTrue(File.Exists(Path.Combine(defaultMimic, "chain.config.json")),
            "default-style ./chain-<id> output works");
    }

    [TestMethod]
    public void TemplatePassedThrough_ToCreateChain()
    {
        // --template is consumed by create-chain. Pin that the composite's --template
        // value reaches the chain.config.json.
        var rc = NewL2Command.Run(new[]
        {
            "--name", "Foo",
            "--chain-id", "5555",
            "--template", "validium",
            "--output", _tempDir,
        });
        Assert.AreEqual(0, rc);
        var config = File.ReadAllText(Path.Combine(_tempDir, "chain.config.json"));
        StringAssert.Contains(config, "\"template\": \"validium\"",
            "--template must propagate to chain.config.json");
        StringAssert.Contains(config, "\"securityLevel\": \"Validium\"",
            "validium template must produce SecurityLevel=Validium");
    }
}
