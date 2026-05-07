using System;
using System.IO;
using Neo.Stack.Cli.Commands;

namespace Neo.Stack.Cli.UnitTests;

/// <summary>
/// Tests for the start-* preflight + the three start-{sequencer,batcher,prover}
/// commands. The commands are plan-printers (the actual sequencer / batcher / prover
/// runs as a neo-cli plugin), but they preflight-check that <c>create-chain</c> +
/// <c>init-l2</c> have run by verifying the chain dir + config exist on disk.
/// </summary>
[TestClass]
public class UT_StartCommands
{
    private string _tempDir = null!;

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "neo-n4-start-test-" + Guid.NewGuid().ToString("N"));
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public void StartSequencer_HappyPath_ExitsZero()
    {
        // Set up a chain dir + chain.config.json (the preflight's two existence checks).
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "chain.config.json"), "{}");

        var rc = StartSequencerCommand.Run(new[]
        {
            "--chain-id", "1099",
            "--output", _tempDir,
        });
        Assert.AreEqual(0, rc);
    }

    [TestMethod]
    public void StartBatcher_HappyPath_ExitsZero()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "chain.config.json"), "{}");

        var rc = StartBatcherCommand.Run(new[]
        {
            "--chain-id", "1099",
            "--output", _tempDir,
        });
        Assert.AreEqual(0, rc);
    }

    [TestMethod]
    public void StartProver_HappyPath_ExitsZero()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "chain.config.json"), "{}");

        var rc = StartProverCommand.Run(new[]
        {
            "--chain-id", "1099",
            "--output", _tempDir,
        });
        Assert.AreEqual(0, rc);
    }

    [TestMethod]
    public void Preflight_NonNumericChainId_ExitsOne()
    {
        var rc = StartSequencerCommand.Run(new[]
        {
            "--chain-id", "not-a-number",
            "--output", _tempDir,
        });
        Assert.AreEqual(1, rc, "non-numeric chain-id is a caller error → exit 1");
    }

    [TestMethod]
    public void Preflight_ChainIdZero_Throws()
    {
        // ChainIdValidator.ValidateL2 throws on chainId 0 (the L1 sentinel).
        Assert.ThrowsExactly<System.IO.InvalidDataException>(() =>
            StartSequencerCommand.Run(new[]
            {
                "--chain-id", "0",
                "--output", _tempDir,
            }));
    }

    [TestMethod]
    public void Preflight_MissingChainDir_ExitsTwo()
    {
        // Chain dir doesn't exist on disk → operator forgot create-chain. Pin exit 2.
        var nonExistent = Path.Combine(_tempDir, "does-not-exist");
        var rc = StartSequencerCommand.Run(new[]
        {
            "--chain-id", "1099",
            "--output", nonExistent,
        });
        Assert.AreEqual(2, rc, "missing chain dir → exit 2");
    }

    [TestMethod]
    public void Preflight_MissingChainConfig_ExitsThree()
    {
        // Chain dir exists but chain.config.json doesn't → operator ran init-l2 but
        // forgot create-chain (or deleted the config). Pin exit 3 so the diagnostic
        // distinguishes "no chain dir" from "chain dir but no config."
        Directory.CreateDirectory(_tempDir);
        var rc = StartSequencerCommand.Run(new[]
        {
            "--chain-id", "1099",
            "--output", _tempDir,
        });
        Assert.AreEqual(3, rc, "missing chain.config.json → exit 3");
    }

    [TestMethod]
    public void Preflight_AcceptsPathFlag_ForBackwardsCompat()
    {
        // The original flag was --path (preserved for backwards compat); --output is
        // the matches-create-chain primary. Both should resolve to the same chain dir.
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "chain.config.json"), "{}");

        var rc = StartSequencerCommand.Run(new[]
        {
            "--chain-id", "1099",
            "--path", _tempDir,
        });
        Assert.AreEqual(0, rc, "--path must continue to work (operator scripts depend on it)");
    }

    [TestMethod]
    public void Preflight_OutputTakesPrecedenceOverPath()
    {
        // When both --output and --path are supplied, --output wins. Same precedence
        // pattern as InitL2Command. Pin so a future refactor doesn't reverse it.
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "chain.config.json"), "{}");

        var bogusPath = Path.Combine(_tempDir, "bogus-path");
        var rc = StartSequencerCommand.Run(new[]
        {
            "--chain-id", "1099",
            "--output", _tempDir,
            "--path", bogusPath,  // ignored
        });
        Assert.AreEqual(0, rc, "--output takes precedence; non-existent --path must not poison the run");
    }
}
