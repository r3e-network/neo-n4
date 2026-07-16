using System;
using System.IO;
using System.Threading.Tasks;
using Neo;
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
    private const string GenesisStateRoot =
        "0x0101010101010101010101010101010101010101010101010101010101010101";
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
        ""vm"": ""neovm2-riscv"",
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
                "--genesis-state-root", GenesisStateRoot,
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

    [TestMethod]
    public async Task Register_WithFourHashes_EmittedHex_DecodesBackToValidConfig()
    {
        // Stronger pin than the previous test's StringAssert.Contains: extract the
        // emitted hex from stdout, decode via L2ChainConfigSerializer, and verify
        // the decoded ChainId / SecurityLevel match the input. Catches a refactor
        // that breaks the operator-pasteable round-trip (e.g. hex casing changes,
        // accidental separators, byte ordering bugs) — the current StringAssert
        // tests would still pass even if the hex itself were wrong.
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
                "--genesis-state-root", GenesisStateRoot,
            });
            Assert.AreEqual(0, rc);
            var output = sw.ToString();

            // Extract the hex line: '0x' followed by 182 lowercase hex chars (91 bytes).
            var match = System.Text.RegularExpressions.Regex.Match(
                output, @"\b0x([0-9a-f]{182})\b");
            Assert.IsTrue(match.Success, "must emit a 91-byte (182-hex-char) lowercase configBytes line");
            var hexBody = match.Groups[1].Value;
            var bytes = Convert.FromHexString(hexBody);
            Assert.AreEqual(91, bytes.Length, "L2ChainConfig wire format is exactly 91 bytes");

            // Round-trip decode + verify the chainId and §16.2 dimensions came through.
            var decoded = Neo.L2.L2ChainConfigSerializer.Decode(bytes);
            Assert.AreEqual(1099u, decoded.ChainId, "round-trip preserves chainId");
            Assert.AreEqual(Neo.L2.SecurityLevel.Optimistic, decoded.SecurityLevel,
                "rollup template default → SecurityLevel.Optimistic");
            Assert.AreEqual(Neo.L2.DAMode.L1, decoded.DAMode, "rollup template default → DAMode.L1");
            Assert.AreEqual(Neo.L2.SequencerModel.DbftCommittee, decoded.Sequencer);
            Assert.AreEqual(Neo.L2.ExitModel.Delayed, decoded.Exit);
            Assert.IsFalse(decoded.GatewayEnabled);
            Assert.IsTrue(decoded.PermissionlessExit);

            // The four UInt160 hashes round-trip too: operator=0xaa..., verifier=0xbb...,
            // bridge=0xcc..., message=0xdd... — all 20 bytes of repeating-byte fills.
            Assert.AreEqual((byte)0xaa, decoded.OperatorManager.GetSpan()[0], "operator hash[0] = 0xaa");
            Assert.AreEqual((byte)0xbb, decoded.Verifier.GetSpan()[0], "verifier hash[0] = 0xbb");
            Assert.AreEqual((byte)0xcc, decoded.BridgeAdapter.GetSpan()[0], "bridge hash[0] = 0xcc");
            Assert.AreEqual((byte)0xdd, decoded.MessageAdapter.GetSpan()[0], "message hash[0] = 0xdd");
        }
        finally
        {
            Console.SetOut(origOut);
        }
    }

    [TestMethod]
    public async Task Register_BroadcastRequiresChainRegistry()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "chain.config.json"), MinimalConfigJson(1099));

        var rc = await RegisterChainCommand.RunAsync(new[]
        {
            "--chain-id", "1099",
            "--output", _tempDir,
            "--operator", "0x" + new string('a', 40),
            "--verifier", "0x" + new string('b', 40),
            "--bridge", "0x" + new string('c', 40),
            "--message", "0x" + new string('d', 40),
            "--genesis-state-root", GenesisStateRoot,
            "--broadcast",
        });

        Assert.AreEqual(5, rc);
    }

    [TestMethod]
    public async Task Register_WithFourHashesRequiresNonZeroGenesisStateRoot()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "chain.config.json"), MinimalConfigJson(1099));
        string[] baseArgs =
        [
            "--chain-id", "1099",
            "--output", _tempDir,
            "--operator", "0x" + new string('a', 40),
            "--verifier", "0x" + new string('b', 40),
            "--bridge", "0x" + new string('c', 40),
            "--message", "0x" + new string('d', 40),
        ];

        Assert.AreEqual(4, await RegisterChainCommand.RunAsync(baseArgs));
        Assert.AreEqual(4, await RegisterChainCommand.RunAsync(
            [.. baseArgs, "--genesis-state-root", UInt256.Zero.ToString()]));
    }

    [TestMethod]
    public async Task Register_FromDeployReport_MaterializesArtifactsAndConfigBytes()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "chain.config.json"), MinimalConfigJson(20260716));
        var reportPath = Path.Combine(_tempDir, "deploy-report.json");
        File.WriteAllText(reportPath, """
            {
              "rpc": "https://n3seed1.ngd.network:20332/",
              "network": 894710606,
              "ownerAddress": "NLtL2v28d7TyMEaXcPqtekunkFRksJ7wxu",
              "ownerScriptHash": "0x13ef519c362973f9a34648a9eac5b71250b2a80a",
              "l2ChainId": 20260716,
              "records": [
                {"category":"deploy","name":"ChainRegistry","status":"deployed","contractHash":"0x65201c5415f6fc13093ad7169c556351c2392d23"},
                {"category":"deploy","name":"VerifierRegistry","status":"deployed","contractHash":"0xe058ea01d6933f38bad1321d0407f514112016b1"},
                {"category":"deploy","name":"SharedBridge","status":"deployed","contractHash":"0xf2f5114b83dd6fed4ddcac0ff9966fd22a77b241"},
                {"category":"deploy","name":"MessageRouter","status":"deployed","contractHash":"0x3caf3c6e160b5aec2e07672dc37662b5998afe90"},
                {"category":"deploy","name":"SettlementManager","status":"deployed","contractHash":"0x11448868f1c14422506b9c2360051df34bcbbb51"},
                {"category":"deploy","name":"ForcedInclusion","status":"deployed","contractHash":"0x962829ae28e7f89e5de4b4672b167c8ae2ba55a9"}
              ]
            }
            """);

        var origOut = Console.Out;
        try
        {
            var sw = new StringWriter();
            Console.SetOut(sw);
            var rc = await RegisterChainCommand.RunAsync(
            [
                "--chain-id", "20260716",
                "--output", _tempDir,
                "--from-deploy-report", reportPath,
                "--genesis-state-root", GenesisStateRoot,
            ]);
            Assert.AreEqual(0, rc);
            var output = sw.ToString();
            StringAssert.Contains(output, "l1.deployed.json");
            StringAssert.Contains(output, "0xe058ea01d6933f38bad1321d0407f514112016b1");
            Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "l1.deployed.json")));
            Assert.IsTrue(File.Exists(Path.Combine(
                _tempDir, "Plugins", "Neo.Plugins.L2Settlement", "config.json")));
            Assert.IsTrue(File.Exists(Path.Combine(
                _tempDir, "Plugins", "Neo.Plugins.L2Batch", "config.json")));
        }
        finally
        {
            Console.SetOut(origOut);
        }
    }

    [TestMethod]
    public async Task Register_FromDeployReport_ChainIdMismatch_FailsClosed()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "chain.config.json"), MinimalConfigJson(1099));
        var reportPath = Path.Combine(_tempDir, "deploy-report.json");
        File.WriteAllText(reportPath, """
            {
              "rpc": "https://example/",
              "network": 1,
              "ownerAddress": "NLtL2v28d7TyMEaXcPqtekunkFRksJ7wxu",
              "ownerScriptHash": "0x13ef519c362973f9a34648a9eac5b71250b2a80a",
              "l2ChainId": 20260716,
              "records": [
                {"category":"deploy","name":"ChainRegistry","status":"deployed","contractHash":"0x65201c5415f6fc13093ad7169c556351c2392d23"},
                {"category":"deploy","name":"VerifierRegistry","status":"deployed","contractHash":"0xe058ea01d6933f38bad1321d0407f514112016b1"},
                {"category":"deploy","name":"SharedBridge","status":"deployed","contractHash":"0xf2f5114b83dd6fed4ddcac0ff9966fd22a77b241"},
                {"category":"deploy","name":"MessageRouter","status":"deployed","contractHash":"0x3caf3c6e160b5aec2e07672dc37662b5998afe90"},
                {"category":"deploy","name":"SettlementManager","status":"deployed","contractHash":"0x11448868f1c14422506b9c2360051df34bcbbb51"},
                {"category":"deploy","name":"ForcedInclusion","status":"deployed","contractHash":"0x962829ae28e7f89e5de4b4672b167c8ae2ba55a9"}
              ]
            }
            """);

        var rc = await RegisterChainCommand.RunAsync(
        [
            "--chain-id", "1099",
            "--output", _tempDir,
            "--from-deploy-report", reportPath,
            "--genesis-state-root", GenesisStateRoot,
        ]);
        Assert.AreEqual(4, rc);
    }
}
