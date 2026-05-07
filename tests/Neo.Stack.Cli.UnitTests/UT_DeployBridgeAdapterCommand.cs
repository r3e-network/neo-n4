using System;
using System.IO;
using System.Threading.Tasks;
using Neo.Stack.Cli.Commands;

namespace Neo.Stack.Cli.UnitTests;

/// <summary>
/// Tests for <see cref="DeployBridgeAdapterCommand"/> — wallet-gated bridge-adapter
/// deploy plan-printer. Pins the argument validation + the optional --output / --path
/// chain-config preflight (consistent with register-chain + start-*).
/// </summary>
[TestClass]
public class UT_DeployBridgeAdapterCommand
{
    private string _tempDir = null!;

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "neo-n4-deploybridge-test-" + Guid.NewGuid().ToString("N"));
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public async Task DeployBridge_NoChainDir_ChainIdOnly_ExitsZero()
    {
        // Without --output / --path, the command keeps its original chain-id-only
        // behavior — emits a generic plan that's not tied to a specific chain dir.
        var rc = await DeployBridgeAdapterCommand.RunAsync(new[]
        {
            "--chain-id", "1099",
        });
        Assert.AreEqual(0, rc);
    }

    [TestMethod]
    public async Task DeployBridge_WithOutput_ConfigExists_ExitsZero()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "chain.config.json"), "{}");

        var rc = await DeployBridgeAdapterCommand.RunAsync(new[]
        {
            "--chain-id", "1099",
            "--output", _tempDir,
        });
        Assert.AreEqual(0, rc, "config-present preflight passes");
    }

    [TestMethod]
    public async Task DeployBridge_WithOutput_ConfigMissing_ExitsTwo()
    {
        // Operator passed --output ./my-l2 but forgot to run create-chain first.
        // Pin exit 2 so a CI script catches the misconfig instead of a plan
        // that doesn't apply to anything real.
        Directory.CreateDirectory(_tempDir);
        var rc = await DeployBridgeAdapterCommand.RunAsync(new[]
        {
            "--chain-id", "1099",
            "--output", _tempDir,
        });
        Assert.AreEqual(2, rc, "missing chain.config.json must exit 2");
    }

    [TestMethod]
    public async Task DeployBridge_AcceptsPathFlag_BackwardsCompat()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "chain.config.json"), "{}");

        var rc = await DeployBridgeAdapterCommand.RunAsync(new[]
        {
            "--chain-id", "1099",
            "--path", _tempDir,
        });
        Assert.AreEqual(0, rc);
    }

    [TestMethod]
    public async Task DeployBridge_OutputTakesPrecedenceOverPath()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "chain.config.json"), "{}");

        var bogusPath = Path.Combine(_tempDir, "bogus");
        var rc = await DeployBridgeAdapterCommand.RunAsync(new[]
        {
            "--chain-id", "1099",
            "--output", _tempDir,
            "--path", bogusPath,
        });
        Assert.AreEqual(0, rc, "--output wins; bogus --path doesn't poison the run");
    }

    [TestMethod]
    public async Task DeployBridge_NonNumericChainId_ExitsOne()
    {
        var rc = await DeployBridgeAdapterCommand.RunAsync(new[]
        {
            "--chain-id", "not-a-number",
        });
        Assert.AreEqual(1, rc);
    }

    [TestMethod]
    public async Task DeployBridge_ChainIdZero_Throws()
    {
        await Assert.ThrowsExactlyAsync<System.IO.InvalidDataException>(async () =>
            await DeployBridgeAdapterCommand.RunAsync(new[]
            {
                "--chain-id", "0",
            }));
    }
}
