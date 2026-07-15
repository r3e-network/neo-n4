using System;
using System.IO;
using System.Threading.Tasks;
using Neo;
using Neo.Stack.Cli.Commands;
using Neo.Wallets;
using Neo.Wallets.NEP6;

namespace Neo.Stack.Cli.UnitTests;

/// <summary>
/// End-to-end integration tests for the documented Quick path in
/// <c>docs/launching-an-l2.md</c>:
/// </summary>
/// <remarks>
/// <para>
/// Walks through the full operator-facing sequence:
/// </para>
/// <code>
///   neo-stack create-chain      → chain.config.json
///   neo-stack validate          → ✅ valid
///   neo-stack init-l2           → data/ logs/ Plugins/
///   neo-stack register-chain    → plan-only (no four-hash flags)
///   neo-stack deploy-bridge-adapter
///   neo-stack start-sequencer
///   neo-stack start-batcher
///   neo-stack start-prover
/// </code>
/// <para>
/// Without this test, a regression in any one command-to-command interaction
/// (e.g. create-chain emitting JSON validate can't parse, or init-l2's chain.config.json
/// check breaking register-chain's preflight, or start-* not recognizing
/// the file that init-l2 didn't touch) would only surface when an operator
/// follows the published walkthrough — by which point the broken commit is
/// already shipped.
/// </para>
/// </remarks>
[TestClass]
public class UT_QuickPathIntegration
{
    private const string ValidatorPrivateKey = "0102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f20";
    private static readonly string Validator = new KeyPair(Convert.FromHexString(ValidatorPrivateKey)).PublicKey.ToString();
    private string _tempDir = null!;

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "neo-n4-quickpath-test-" + Guid.NewGuid().ToString("N"));
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public async Task QuickPath_AllSevenCommands_SucceedInSequence()
    {
        // 1. create-chain
        var createRc = CreateChainCommand.Run(new[]
        {
            "--chain-id", "1099",
            "--template", "rollup",
            "--output", _tempDir,
        });
        Assert.AreEqual(0, createRc, "create-chain must exit 0");
        var configPath = Path.Combine(_tempDir, "chain.config.json");
        Assert.IsTrue(File.Exists(configPath));

        // 2. validate (against the just-emitted config — defense-in-depth check that
        //    create-chain output is what validate accepts)
        var validateRc = ValidateChainConfigCommand.Run(new[] { configPath });
        Assert.AreEqual(0, validateRc, "validate must accept create-chain's output");

        File.WriteAllText(
            configPath,
            File.ReadAllText(configPath).Replace(
                "\"validators\": []",
                $"\"validators\": [\"{Validator}\"]",
                StringComparison.Ordinal));
        var nodeConfigSource = Path.Combine(_tempDir, "node-config-source.json");
        File.WriteAllText(nodeConfigSource, $$"""
            {
              "ApplicationConfiguration": {
                "Storage": { "Path": "{{Path.Combine(_tempDir, "data")}}" },
                "UnlockWallet": {
                  "Path": "validator.json",
                  "Password": "test-only",
                  "IsActive": true
                }
              },
              "ProtocolConfiguration": {
                "Network": 123456789,
                "MillisecondsPerBlock": 5000,
                "ValidatorsCount": 1,
                "StandbyCommittee": ["{{Validator}}"]
              }
            }
            """);
        var batcherConfigSource = Path.Combine(_tempDir, "batcher-node-config-source.json");
        File.WriteAllText(batcherConfigSource, $$"""
            {
              "ApplicationConfiguration": {
                "Storage": { "Path": "{{Path.Combine(_tempDir, "batcher-data")}}" }
              },
              "ProtocolConfiguration": {
                "Network": 123456789,
                "MillisecondsPerBlock": 5000,
                "ValidatorsCount": 1,
                "StandbyCommittee": ["{{Validator}}"]
              }
            }
            """);

        // 3. init-l2
        var initRc = InitL2Command.Run(new[]
        {
            "--chain-id", "1099",
            "--output", _tempDir,
            "--node-config", nodeConfigSource,
            "--batcher-node-config", batcherConfigSource,
        });
        Assert.AreEqual(0, initRc, "init-l2 must accept the created chain dir + config");
        Assert.IsTrue(Directory.Exists(Path.Combine(_tempDir, "data")));
        Assert.IsTrue(Directory.Exists(Path.Combine(_tempDir, "logs")));
        Assert.IsTrue(Directory.Exists(Path.Combine(_tempDir, "Plugins")));
        var neoCli = Path.Combine(_tempDir, "node", "Neo.CLI.dll");
        var batcherNeoCli = Path.Combine(_tempDir, "batcher-node", "Neo.CLI.dll");
        var prover = Path.Combine(_tempDir, "prover", "prove-batch.dll");
        Directory.CreateDirectory(Path.Combine(_tempDir, "node", "Plugins", "DBFTPlugin"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "batcher-node", "Plugins", "Neo.Plugins.L2Batch"));
        File.WriteAllBytes(neoCli, [0]);
        File.WriteAllBytes(batcherNeoCli, [0]);
        File.WriteAllBytes(prover, [0]);
        var walletPath = Path.Combine(_tempDir, "node", "validator.json");
        File.WriteAllText(
            walletPath,
            """
            {"name":"validator","version":"1.0","scrypt":{"n":2,"r":1,"p":1},"accounts":[],"extra":null}
            """);
        var wallet = new NEP6Wallet(walletPath, "test-only", ProtocolSettings.Default);
        wallet.CreateAccount(Convert.FromHexString(ValidatorPrivateKey));
        wallet.Save();
        File.WriteAllBytes(Path.Combine(_tempDir, "node", "Plugins", "DBFTPlugin", "DBFTPlugin.dll"), [0]);
        File.WriteAllText(
            Path.Combine(_tempDir, "node", "Plugins", "DBFTPlugin", "DBFTPlugin.json"),
            "{\"PluginConfiguration\":{\"AutoStart\":true}}");
        File.WriteAllBytes(
            Path.Combine(_tempDir, "batcher-node", "Plugins", "Neo.Plugins.L2Batch", "Neo.Plugins.L2Batch.dll"),
            [0]);
        File.WriteAllText(
            Path.Combine(_tempDir, "batcher-node", "Plugins", "Neo.Plugins.L2Batch", "config.json"),
            "{\"PluginConfiguration\":{\"ChainId\":1099}}");

        // 4. register-chain (plan-only, no four-hash flags)
        var registerRc = await RegisterChainCommand.RunAsync(new[]
        {
            "--chain-id", "1099",
            "--output", _tempDir,
        });
        Assert.AreEqual(0, registerRc, "register-chain plan-only path must exit 0");

        // 5. deploy-bridge-adapter (with --output → preflights config)
        var deployRc = await DeployBridgeAdapterCommand.RunAsync(new[]
        {
            "--chain-id", "1099",
            "--output", _tempDir,
        });
        Assert.AreEqual(0, deployRc, "deploy-bridge-adapter must accept the created chain config");

        // 6/7/8. start-* (each preflight-checks chain dir + chain.config.json)
        var seqRc = StartSequencerCommand.Run(new[]
        {
            "--chain-id", "1099",
            "--output", _tempDir,
            "--neo-cli", neoCli,
            "--dry-run",
        });
        Assert.AreEqual(0, seqRc, "start-sequencer preflight must pass");

        var batRc = StartBatcherCommand.Run(new[]
        {
            "--chain-id", "1099",
            "--output", _tempDir,
            "--neo-cli", batcherNeoCli,
            "--dry-run",
        });
        Assert.AreEqual(0, batRc, "start-batcher preflight must pass");

        var provRc = StartProverCommand.Run(new[]
        {
            "--chain-id", "1099",
            "--output", _tempDir,
            "--prover", prover,
            "--dry-run",
        });
        Assert.AreEqual(0, provRc, "start-prover preflight must pass");
    }

    [TestMethod]
    public async Task QuickPath_RegisterWithFourHashes_EmitsConfigBytesHex()
    {
        // Variant of the happy path where register-chain is called with all four
        // UInt160 flags — produces the canonical 91-byte configBytes hex.
        // Pins the full bring-up flow including the wallet-paste-ready hex output.
        Directory.CreateDirectory(_tempDir);
        CreateChainCommand.Run(new[]
        {
            "--chain-id", "1099",
            "--template", "validium",
            "--output", _tempDir,
        });
        ValidateChainConfigCommand.Run(new[] { Path.Combine(_tempDir, "chain.config.json") });
        InitL2Command.Run(new[] { "--chain-id", "1099", "--output", _tempDir });

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
                "--genesis-state-root",
                "0x0101010101010101010101010101010101010101010101010101010101010101",
            });
            Assert.AreEqual(0, rc);
            var output = sw.ToString();
            // Validium template → SecurityLevel.Validium, gateway enabled.
            StringAssert.Contains(output, "configBytes (hex, copy-pasteable):");
            StringAssert.Contains(output, "configBytes=<91 bytes>");
        }
        finally
        {
            Console.SetOut(origOut);
        }
    }

    [TestMethod]
    public void QuickPath_NewL2Composite_AllStepsSucceed()
    {
        // Variant: the `new-l2` composite path that strings create-chain + validate +
        // init-l2 + scaffold-executor --with-tests together. Pin that the composite
        // exits 0 + produces all the expected artifacts.
        var rc = NewL2Command.Run(new[]
        {
            "--name", "QuickPath",
            "--chain-id", "1099",
            "--template", "rollup",
            "--output", _tempDir,
        });
        Assert.AreEqual(0, rc);
        Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "chain.config.json")));
        Assert.IsTrue(Directory.Exists(Path.Combine(_tempDir, "data")));
        Assert.IsTrue(Directory.Exists(Path.Combine(_tempDir, "logs")));
        Assert.IsTrue(Directory.Exists(Path.Combine(_tempDir, "Plugins")));
        // Composite emits scaffold-executor --with-tests → executor dir + tests dir.
        Assert.IsTrue(Directory.Exists(Path.Combine(_tempDir, "QuickPathExecutor")));
        Assert.IsTrue(Directory.Exists(Path.Combine(_tempDir, "QuickPathExecutor.UnitTests")));
    }
}
