using System;
using System.IO;
using System.Threading.Tasks;
using Neo.Stack.Cli.Commands;

namespace Neo.Stack.Cli.UnitTests;

/// <summary>
/// Tests for <see cref="RegisterChainCommand"/> — the wallet-gated L1 registration
/// plan-printer. Pins the argument-validation paths + the --output / --path flag
/// precedence (mirroring InitL2 + StartCommandPreflight). Exit-code contract:
/// 0 = plan printed; 1 = caller error (bad chain-id); 2 = config missing; 3 = config
/// unreadable; 4 = config decode failure (when the four hash flags are supplied).
/// </summary>
[TestClass]
public class UT_RegisterChainCommand
{
    private string _tempDir = null!;

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "neo-n4-register-test-" + Guid.NewGuid().ToString("N"));
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private static string MinimalConfigJson(uint chainId) => $@"{{
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
    public async Task Register_HappyPath_PlanOnly_ExitsZero()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "chain.config.json"), MinimalConfigJson(1099));

        var rc = await RegisterChainCommand.RunAsync(new[]
        {
            "--chain-id", "1099",
            "--output", _tempDir,
        });
        Assert.AreEqual(0, rc, "plan-only (no --operator/--verifier/--bridge/--message) must exit 0");
    }

    [TestMethod]
    public async Task Register_NonNumericChainId_ExitsOne()
    {
        var rc = await RegisterChainCommand.RunAsync(new[]
        {
            "--chain-id", "abc",
            "--output", _tempDir,
        });
        Assert.AreEqual(1, rc);
    }

    [TestMethod]
    public async Task Register_ChainIdZero_Throws()
    {
        await Assert.ThrowsExactlyAsync<System.IO.InvalidDataException>(async () =>
            await RegisterChainCommand.RunAsync(new[]
            {
                "--chain-id", "0",
                "--output", _tempDir,
            }));
    }

    [TestMethod]
    public async Task Register_MissingConfig_ExitsTwo()
    {
        // Chain dir exists but no chain.config.json — operator forgot create-chain.
        Directory.CreateDirectory(_tempDir);
        var rc = await RegisterChainCommand.RunAsync(new[]
        {
            "--chain-id", "1099",
            "--output", _tempDir,
        });
        Assert.AreEqual(2, rc, "missing chain.config.json must exit 2 with a 'run create-chain first' diagnostic");
    }

    [TestMethod]
    public async Task Register_AcceptsPathFlag_BackwardsCompat()
    {
        // Original flag was --path; preserved for operator scripts predating --output.
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "chain.config.json"), MinimalConfigJson(1099));

        var rc = await RegisterChainCommand.RunAsync(new[]
        {
            "--chain-id", "1099",
            "--path", _tempDir,
        });
        Assert.AreEqual(0, rc, "--path must continue to work (backwards-compat pin)");
    }

    [TestMethod]
    public async Task Register_OutputTakesPrecedenceOverPath()
    {
        // When both supplied, --output wins. Same precedence as InitL2 + start-*.
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "chain.config.json"), MinimalConfigJson(1099));

        var bogusPath = Path.Combine(_tempDir, "bogus");
        var rc = await RegisterChainCommand.RunAsync(new[]
        {
            "--chain-id", "1099",
            "--output", _tempDir,
            "--path", bogusPath,
        });
        Assert.AreEqual(0, rc, "--output wins over --path; bogus --path doesn't poison the run");
    }

    [TestMethod]
    public async Task Register_WithFourHashes_EmitsConfigBytesHex()
    {
        // When all four UInt160 flags are supplied, the command emits the canonical
        // 91-byte configBytes hex. Pin via stdout capture.
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "chain.config.json"), MinimalConfigJson(1099));

        var origOut = Console.Out;
        try
        {
            var sw = new StringWriter();
            Console.SetOut(sw);
            var rc = await RegisterChainCommand.RunAsync(new[]
            {
                "--chain-id", "1099",
                "--output", _tempDir,
                "--operator", "0x" + new string('a', 40),
                "--verifier", "0x" + new string('b', 40),
                "--bridge",   "0x" + new string('c', 40),
                "--message",  "0x" + new string('d', 40),
            });
            Assert.AreEqual(0, rc);
            var output = sw.ToString();
            // 91-byte canonical encoding → 182 hex chars.
            StringAssert.Contains(output, "configBytes (hex, copy-pasteable):");
            StringAssert.Contains(output, "configBytes=<91 bytes>");
        }
        finally
        {
            Console.SetOut(origOut);
        }
    }
}
