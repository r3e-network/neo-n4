using Neo;
using Neo.Stack.Cli.Commands;
using Neo.Wallets;
using Neo.Wallets.NEP6;
using System.Text.Json.Nodes;

namespace Neo.Stack.Cli.UnitTests;

[TestClass]
public class UT_StartCommands
{
    private const string ValidatorPrivateKey = "0102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f20";
    private const string AlternatePrivateKey = "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f";
    private const string CorruptReplacementPrivateKey = "303132333435363738393a3b3c3d3e3f404142434445464748494a4b4c4d4e4f";
    private static readonly string Validator = new KeyPair(Convert.FromHexString(ValidatorPrivateKey)).PublicKey.ToString();
    private static readonly string RotatedValidator = new KeyPair(Convert.FromHexString(AlternatePrivateKey)).PublicKey.ToString();
    private string _tempDir = null!;
    private string _neoCli = null!;
    private string _batcherNeoCli = null!;
    private string _prover = null!;

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
    public async Task StartSequencer_DryRun_ValidatesProductionBundle()
    {
        SeedOperatorLayout();

        var result = await StartSequencerCommand.RunAsync(StartArgs("--dry-run"));

        Assert.AreEqual(0, result);
    }

    [TestMethod]
    public async Task StartBatcher_DryRun_ValidatesProductionBundle()
    {
        SeedOperatorLayout();

        var result = await StartBatcherCommand.RunAsync(BatcherArgs("--dry-run"));

        Assert.AreEqual(0, result);
    }

    [TestMethod]
    public async Task StartProver_DryRun_UsesDurableQueueDirectories()
    {
        SeedOperatorLayout();

        var result = await StartProverCommand.RunAsync(
        [
            "--chain-id", "1099",
            "--output", _tempDir,
            "--prover", _prover,
            "--dry-run",
        ]);

        Assert.AreEqual(0, result);
    }

    [TestMethod]
    public async Task StartProver_RejectsFixedQueueOverrideAfterSeparator()
    {
        SeedOperatorLayout();

        var result = await StartProverCommand.RunAsync(
        [
            "--chain-id", "1099",
            "--output", _tempDir,
            "--prover", _prover,
            "--dry-run",
            "--", "--watch=/tmp/untrusted",
        ]);

        Assert.AreEqual(7, result);
    }

    [TestMethod]
    public async Task StartProver_RejectsSharedWatchAndArchiveDirectory()
    {
        SeedOperatorLayout();
        var watch = Path.Combine(_tempDir, "prover", "inbox");

        var result = await StartProverCommand.RunAsync(
        [
            "--chain-id", "1099",
            "--output", _tempDir,
            "--prover", _prover,
            "--watch", watch,
            "--archive", watch,
            "--dry-run",
        ]);

        Assert.AreEqual(4, result);
    }

    [TestMethod]
    public async Task StartProver_AcceptsBoundedPollInterval()
    {
        SeedOperatorLayout();

        var result = await StartProverCommand.RunAsync(
        [
            "--chain-id", "1099",
            "--output", _tempDir,
            "--prover", _prover,
            "--dry-run",
            "--", "--poll-secs", "5",
        ]);

        Assert.AreEqual(0, result);
    }

    [TestMethod]
    public async Task StartSequencer_PropagatesExitCodeAndUsesArgumentList()
    {
        SeedOperatorLayout();
        var runner = new CapturingRunner(37);

        var result = await StartSequencerCommand.RunAsync(
            StartArgs("--", "--verbose", "Debug"),
            runner);

        Assert.AreEqual(37, result);
        Assert.IsNotNull(runner.Spec);
        CollectionAssert.Contains(runner.Spec.Arguments.ToArray(), "--verbose");
        CollectionAssert.Contains(runner.Spec.Arguments.ToArray(), "Debug");
        CollectionAssert.DoesNotContain(runner.Spec.Arguments.ToArray(), "--config");
        CollectionAssert.DoesNotContain(runner.Spec.Arguments.ToArray(), "--db-path");
    }

    [TestMethod]
    public void StartSequencer_OptionParsingStopsAtForwardSeparator()
    {
        Assert.IsFalse(ArgUtil.HasFlag(["--", "--dry-run"], "--dry-run"));
        Assert.AreEqual("fallback", ArgUtil.Get(["--", "--rpc", "attacker"], "--rpc", "fallback"));
    }

    [TestMethod]
    public async Task StartSequencer_SyncOnlyDryRunBuildsGovernanceTransaction()
    {
        SeedOperatorLayout();
        var originalOutput = Console.Out;
        try
        {
            var output = new StringWriter();
            Console.SetOut(output);

            var result = await StartSequencerCommand.RunAsync(
            [
                "--chain-id", "1099",
                "--output", _tempDir,
                "--sync-only",
                "--dry-run",
            ]);

            Assert.AreEqual(0, result);
            StringAssert.Contains(output.ToString(), "Committee update script: 0x");
        }
        finally
        {
            Console.SetOut(originalOutput);
        }
    }

    [TestMethod]
    public async Task StartSequencer_RejectsInvalidCommitteeActivationTimeoutBeforeSync()
    {
        SeedOperatorLayout();

        var result = await StartSequencerCommand.RunAsync(
        [
            "--chain-id", "1099",
            "--output", _tempDir,
            "--sync-only",
            "--dry-run",
            "--committee-activation-timeout-seconds", "0",
        ]);

        Assert.AreEqual(7, result);
    }

    [TestMethod]
    public async Task StartSequencer_RequiresExplicitBroadcastForCommitteeSync()
    {
        SeedOperatorLayout();

        var result = await StartSequencerCommand.RunAsync(
        [
            "--chain-id", "1099",
            "--output", _tempDir,
            "--sync-only",
        ]);

        Assert.AreEqual(7, result);
    }

    [TestMethod]
    public async Task StartSequencer_RejectsNetworkMismatch()
    {
        SeedOperatorLayout();

        var result = await StartSequencerCommand.RunAsync(StartArgs(
            "--expected-network", "999",
            "--dry-run"));

        Assert.AreEqual(4, result);
    }

    [TestMethod]
    public async Task StartSequencer_RotatedCommitteeRequiresMatchingFinalizedNativeState()
    {
        SeedOperatorLayout();
        File.WriteAllText(Path.Combine(_tempDir, "chain.config.json"), $$"""
            {
              "chainId": 1099,
              "sequencerModel": "DbftCommittee",
              "milestonePerBlockMs": 5000,
              "validators": ["{{RotatedValidator}}"]
            }
            """);
        WriteTestWallet(
            Path.Combine(_tempDir, "node", "validator.json"),
            AlternatePrivateKey);
        var matchingReader = new FixedCommitteeReader(RotatedValidator);

        var matchingResult = await StartSequencerCommand.RunAsync(
            StartArgs("--rpc", "http://l2.invalid", "--dry-run"),
            committeeReader: matchingReader);
        var mismatchingResult = await StartSequencerCommand.RunAsync(
            StartArgs("--rpc", "http://l2.invalid", "--dry-run"),
            committeeReader: new FixedCommitteeReader(Validator));

        Assert.AreEqual(0, matchingResult);
        Assert.AreEqual(4, mismatchingResult);
    }

    [TestMethod]
    public async Task StartSequencer_RejectsMissingDbftPlugin()
    {
        SeedOperatorLayout();
        File.Delete(Path.Combine(_tempDir, "node", "Plugins", "DBFTPlugin", "DBFTPlugin.dll"));

        var result = await StartSequencerCommand.RunAsync(StartArgs("--dry-run"));

        Assert.AreEqual(6, result);
    }

    [TestMethod]
    public async Task StartSequencer_RejectsDisabledWalletAutoUnlock()
    {
        SeedOperatorLayout();
        var configPath = Path.Combine(_tempDir, "node", "config.json");
        File.WriteAllText(
            configPath,
            File.ReadAllText(configPath).Replace("\"IsActive\": true", "\"IsActive\": false", StringComparison.Ordinal));

        var result = await StartSequencerCommand.RunAsync(StartArgs("--dry-run"));

        Assert.AreEqual(4, result);
    }

    [TestMethod]
    public async Task StartSequencer_RejectsMalformedWallet()
    {
        SeedOperatorLayout();
        File.WriteAllText(Path.Combine(_tempDir, "node", "validator.json"), "{}");

        var result = await StartSequencerCommand.RunAsync(StartArgs("--dry-run"));

        Assert.AreEqual(4, result);
    }

    [TestMethod]
    public async Task StartSequencer_RejectsIncorrectWalletPassword()
    {
        SeedOperatorLayout();
        var configPath = Path.Combine(_tempDir, "node", "config.json");
        File.WriteAllText(
            configPath,
            File.ReadAllText(configPath).Replace("test-only", "incorrect", StringComparison.Ordinal));

        var result = await StartSequencerCommand.RunAsync(StartArgs("--dry-run"));

        Assert.AreEqual(4, result);
    }

    [TestMethod]
    public async Task StartSequencer_RejectsUnsupportedWalletFormat()
    {
        SeedOperatorLayout();
        var nodeDirectory = Path.Combine(_tempDir, "node");
        File.Copy(
            Path.Combine(nodeDirectory, "validator.json"),
            Path.Combine(nodeDirectory, "validator.hsm"));
        var configPath = Path.Combine(nodeDirectory, "config.json");
        File.WriteAllText(
            configPath,
            File.ReadAllText(configPath).Replace("validator.json", "validator.hsm", StringComparison.Ordinal));

        var result = await StartSequencerCommand.RunAsync(StartArgs("--dry-run"));

        Assert.AreEqual(4, result);
    }

    [TestMethod]
    public async Task StartSequencer_RejectsWalletWithoutCommitteeKey()
    {
        SeedOperatorLayout();
        WriteTestWallet(
            Path.Combine(_tempDir, "node", "validator.json"),
            AlternatePrivateKey);

        var result = await StartSequencerCommand.RunAsync(StartArgs("--dry-run"));

        Assert.AreEqual(4, result);
    }

    [TestMethod]
    public async Task StartSequencer_RejectsCommitteeAccountWhoseEncryptedKeyDoesNotMatch()
    {
        SeedOperatorLayout();
        WriteWalletWithMismatchedCommitteeKey(
            Path.Combine(_tempDir, "node", "validator.json"));

        var result = await StartSequencerCommand.RunAsync(StartArgs("--dry-run"));

        Assert.AreEqual(4, result);
    }

    [TestMethod]
    public async Task StartSequencer_RejectsDisabledDbftAutoStart()
    {
        SeedOperatorLayout();
        File.WriteAllText(
            Path.Combine(_tempDir, "node", "Plugins", "DBFTPlugin", "DBFTPlugin.json"),
            "{\"PluginConfiguration\":{\"AutoStart\":false}}");

        var result = await StartSequencerCommand.RunAsync(StartArgs("--dry-run"));

        Assert.AreEqual(6, result);
    }

    [TestMethod]
    public async Task StartBatcher_RejectsMismatchedPluginChainId()
    {
        SeedOperatorLayout();
        File.WriteAllText(
            Path.Combine(_tempDir, "batcher-node", "Plugins", "Neo.Plugins.L2Batch", "config.json"),
            "{\"PluginConfiguration\":{\"ChainId\":1100}}");

        var result = await StartBatcherCommand.RunAsync(BatcherArgs("--dry-run"));

        Assert.AreEqual(6, result);
    }

    [TestMethod]
    public async Task StartBatcher_RejectsSharedSequencerStorage()
    {
        SeedOperatorLayout();
        var configPath = Path.Combine(_tempDir, "batcher-node", "config.json");
        File.WriteAllText(
            configPath,
            File.ReadAllText(configPath).Replace("batcher-data", "data", StringComparison.Ordinal));

        var result = await StartBatcherCommand.RunAsync(BatcherArgs("--dry-run"));

        Assert.AreEqual(4, result);
    }

    [TestMethod]
    public async Task StartSequencer_RejectsUnsafeForwardedOptions()
    {
        SeedOperatorLayout();

        var result = await StartSequencerCommand.RunAsync(StartArgs(
            "--dry-run",
            "--",
            "--background"));

        Assert.AreEqual(7, result);
    }

    [TestMethod]
    public async Task SystemRunner_DoesNotInterpretShellMetacharacters()
    {
        if (!File.Exists("/bin/echo")) return;
        Directory.CreateDirectory(_tempDir);
        var marker = Path.Combine(_tempDir, "injected");
        var spec = new OperatorProcessSpec(
            "/bin/echo",
            new[] { $";touch {marker}" },
            _tempDir,
            TimeSpan.FromSeconds(1));

        var result = await new SystemOperatorProcessRunner().RunAsync(spec, CancellationToken.None);

        Assert.AreEqual(0, result);
        Assert.IsFalse(File.Exists(marker));
    }

    [TestMethod]
    public async Task SystemRunner_CancellationTerminatesChildAndReturns130()
    {
        if (!File.Exists("/bin/sleep")) return;
        Directory.CreateDirectory(_tempDir);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var spec = new OperatorProcessSpec(
            "/bin/sleep",
            new[] { "30" },
            _tempDir,
            TimeSpan.FromSeconds(1));

        var result = await new SystemOperatorProcessRunner().RunAsync(spec, cancellation.Token);

        Assert.AreEqual(130, result);
    }

    [TestMethod]
    public void Preflight_NonNumericChainId_ExitsOne()
    {
        var result = StartSequencerCommand.Run(
        [
            "--chain-id", "not-a-number",
            "--output", _tempDir,
        ]);

        Assert.AreEqual(1, result);
    }

    [TestMethod]
    public void Preflight_ChainIdZero_ExitsOne()
    {
        var result = StartSequencerCommand.Run(
        [
            "--chain-id", "0",
            "--output", _tempDir,
        ]);

        Assert.AreEqual(1, result);
    }

    [TestMethod]
    public void Preflight_MissingChainDir_ExitsTwo()
    {
        var result = StartSequencerCommand.Run(
        [
            "--chain-id", "1099",
            "--output", Path.Combine(_tempDir, "missing"),
        ]);

        Assert.AreEqual(2, result);
    }

    [TestMethod]
    public void Preflight_MissingChainConfig_ExitsThree()
    {
        Directory.CreateDirectory(_tempDir);

        var result = StartSequencerCommand.Run(
        [
            "--chain-id", "1099",
            "--output", _tempDir,
        ]);

        Assert.AreEqual(3, result);
    }

    [TestMethod]
    public async Task Preflight_AcceptsPathAndOutputTakesPrecedence()
    {
        SeedOperatorLayout();

        var result = await StartSequencerCommand.RunAsync(
        [
            "--chain-id", "1099",
            "--output", _tempDir,
            "--path", Path.Combine(_tempDir, "bogus"),
            "--neo-cli", _neoCli,
            "--dry-run",
        ]);

        Assert.AreEqual(0, result);
    }

    private string[] StartArgs(params string[] extra)
    {
        return new[]
        {
            "--chain-id", "1099",
            "--output", _tempDir,
            "--neo-cli", _neoCli,
        }.Concat(extra).ToArray();
    }

    private void SeedOperatorLayout()
    {
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, "data"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "batcher-data"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "node", "Plugins", "DBFTPlugin"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "node", "Plugins", "Neo.Plugins.L2Batch"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "batcher-node", "Plugins", "Neo.Plugins.L2Batch"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "prover", "inbox"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "prover", "archive"));

        File.WriteAllText(Path.Combine(_tempDir, "chain.config.json"), $$"""
            {
              "chainId": 1099,
              "sequencerModel": "DbftCommittee",
              "milestonePerBlockMs": 5000,
              "validators": ["{{Validator}}"]
            }
            """);
        File.WriteAllText(Path.Combine(_tempDir, "node", "config.json"), $$"""
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
        File.WriteAllText(Path.Combine(_tempDir, "batcher-node", "config.json"), $$"""
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

        _neoCli = Path.Combine(_tempDir, "node", "Neo.CLI.dll");
        _batcherNeoCli = Path.Combine(_tempDir, "batcher-node", "Neo.CLI.dll");
        _prover = Path.Combine(_tempDir, "prover", "prove-batch.dll");
        File.WriteAllBytes(_neoCli, [0]);
        File.WriteAllBytes(_batcherNeoCli, [0]);
        File.WriteAllBytes(_prover, [0]);
        WriteTestWallet(Path.Combine(_tempDir, "node", "validator.json"), ValidatorPrivateKey);
        File.WriteAllBytes(Path.Combine(_tempDir, "node", "Plugins", "DBFTPlugin", "DBFTPlugin.dll"), [0]);
        File.WriteAllText(Path.Combine(_tempDir, "node", "Plugins", "DBFTPlugin", "DBFTPlugin.json"),
            "{\"PluginConfiguration\":{\"AutoStart\":true}}");
        File.WriteAllBytes(Path.Combine(_tempDir, "node", "Plugins", "Neo.Plugins.L2Batch", "Neo.Plugins.L2Batch.dll"), [0]);
        File.WriteAllText(Path.Combine(_tempDir, "node", "Plugins", "Neo.Plugins.L2Batch", "config.json"),
            "{\"PluginConfiguration\":{\"ChainId\":1099}}");
        File.WriteAllBytes(Path.Combine(_tempDir, "batcher-node", "Plugins", "Neo.Plugins.L2Batch", "Neo.Plugins.L2Batch.dll"), [0]);
        File.WriteAllText(Path.Combine(_tempDir, "batcher-node", "Plugins", "Neo.Plugins.L2Batch", "config.json"),
            "{\"PluginConfiguration\":{\"ChainId\":1099}}");
    }

    private static void WriteTestWallet(string path, string privateKey)
    {
        File.WriteAllText(
            path,
            """
            {"name":"validator","version":"1.0","scrypt":{"n":2,"r":1,"p":1},"accounts":[],"extra":null}
            """);
        var wallet = new NEP6Wallet(path, "test-only", ProtocolSettings.Default);
        wallet.CreateAccount(Convert.FromHexString(privateKey));
        wallet.Save();
    }

    private static void WriteWalletWithMismatchedCommitteeKey(string path)
    {
        File.WriteAllText(
            path,
            """
            {"name":"validator","version":"1.0","scrypt":{"n":2,"r":1,"p":1},"accounts":[],"extra":null}
            """);
        var wallet = new NEP6Wallet(path, "test-only", ProtocolSettings.Default);
        var unrelated = wallet.CreateAccount(Convert.FromHexString(AlternatePrivateKey));
        var validator = wallet.CreateAccount(Convert.FromHexString(ValidatorPrivateKey));
        var replacement = wallet.CreateAccount(Convert.FromHexString(CorruptReplacementPrivateKey));
        wallet.Save();

        var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var accounts = document["accounts"]!.AsArray();
        var validatorJson = accounts
            .Select(node => node!.AsObject())
            .Single(account => account["address"]!.GetValue<string>() == validator.Address);
        var replacementJson = accounts
            .Select(node => node!.AsObject())
            .Single(account => account["address"]!.GetValue<string>() == replacement.Address);
        validatorJson["key"] = replacementJson["key"]!.GetValue<string>();
        accounts.Remove(replacementJson);
        Assert.IsTrue(accounts.Any(node =>
            node!.AsObject()["address"]!.GetValue<string>() == unrelated.Address));
        File.WriteAllText(path, document.ToJsonString());
    }

    private string[] BatcherArgs(params string[] extra)
    {
        return new[]
        {
            "--chain-id", "1099",
            "--output", _tempDir,
            "--neo-cli", _batcherNeoCli,
            "--node-config", Path.Combine(_tempDir, "batcher-node", "config.json"),
        }.Concat(extra).ToArray();
    }

    private sealed class CapturingRunner(int exitCode) : IOperatorProcessRunner
    {
        public OperatorProcessSpec? Spec { get; private set; }

        public Task<int> RunAsync(OperatorProcessSpec spec, CancellationToken cancellationToken)
        {
            Spec = spec;
            return Task.FromResult(exitCode);
        }
    }

    private sealed class FixedCommitteeReader(string validator) : INativeSequencerCommitteeReader
    {
        public Task<IReadOnlyList<Neo.Cryptography.ECC.ECPoint>> ReadAsync(
            string rpcEndpoint,
            int expectedCount,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<Neo.Cryptography.ECC.ECPoint> result =
            [
                Neo.Cryptography.ECC.ECPoint.Parse(
                    validator,
                    Neo.Cryptography.ECC.ECCurve.Secp256r1)
            ];
            return Task.FromResult(result);
        }
    }
}
