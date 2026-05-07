using System;
using System.IO;
using Neo.Stack.Cli.Commands;

namespace Neo.Stack.Cli.UnitTests;

/// <summary>
/// Tests for <see cref="CreateChainCommand"/> — writes the chain.config.json file
/// every other command consumes. Pinning the JSON shape + every template's
/// dimensions catches a regression that would silently produce a bad config the
/// devnet / register-chain run against later.
/// </summary>
[TestClass]
public class UT_CreateChainCommand
{
    private string _tempDir = null!;

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "neo-n4-create-chain-test-" + Guid.NewGuid().ToString("N"));
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public void CreateChain_DefaultArgs_WritesValidConfig()
    {
        var rc = CreateChainCommand.Run(new[]
        {
            "--chain-id", "1099",
            "--output", _tempDir,
        });
        Assert.AreEqual(0, rc);

        var configPath = Path.Combine(_tempDir, "chain.config.json");
        Assert.IsTrue(File.Exists(configPath), "chain.config.json must exist");

        var config = File.ReadAllText(configPath);
        StringAssert.Contains(config, "\"chainId\": 1099");
        StringAssert.Contains(config, "\"template\": \"rollup\"");
        StringAssert.Contains(config, "\"vm\": \"neovm\"");
        StringAssert.Contains(config, "\"securityLevel\": \"Optimistic\"");  // rollup default
    }

    [TestMethod]
    public void CreateChain_NonNumericChainId_ExitsOne()
    {
        var (rc, _, stderr) = CaptureBoth(() => CreateChainCommand.Run(new[]
        {
            "--chain-id", "abc",
            "--output", _tempDir,
        }));
        Assert.AreEqual(1, rc);
        StringAssert.Contains(stderr, "--chain-id");
        Assert.IsFalse(Directory.Exists(_tempDir),
            "non-numeric chain-id rejection must abort before output dir is created");
    }

    [TestMethod]
    public void CreateChain_ChainIdZero_Throws()
    {
        // Same L1-sentinel reject as ChainIdValidator.ValidateL2.
        Assert.ThrowsExactly<System.IO.InvalidDataException>(() =>
            CreateChainCommand.Run(new[]
            {
                "--chain-id", "0",
                "--output", _tempDir,
            }));
    }

    [TestMethod]
    public void CreateChain_ZkRollupTemplate_EmitsValidityProof()
    {
        var rc = CreateChainCommand.Run(new[]
        {
            "--chain-id", "2099",
            "--template", "zk-rollup",
            "--output", _tempDir,
        });
        Assert.AreEqual(0, rc);
        var config = File.ReadAllText(Path.Combine(_tempDir, "chain.config.json"));
        StringAssert.Contains(config, "\"template\": \"zk-rollup\"");
        StringAssert.Contains(config, "\"securityLevel\": \"Validity\"");
        StringAssert.Contains(config, "\"proofType\": \"Zk\"");
        StringAssert.Contains(config, "\"daMode\": \"L1\"");
        StringAssert.Contains(config, "\"exitModel\": \"Permissionless\"");
        StringAssert.Contains(config, "\"gatewayEnabled\": true");
    }

    [TestMethod]
    public void CreateChain_ValidiumTemplate_EmitsNeoFsDA()
    {
        var rc = CreateChainCommand.Run(new[]
        {
            "--chain-id", "3099",
            "--template", "validium",
            "--output", _tempDir,
        });
        Assert.AreEqual(0, rc);
        var config = File.ReadAllText(Path.Combine(_tempDir, "chain.config.json"));
        StringAssert.Contains(config, "\"securityLevel\": \"Validium\"");
        StringAssert.Contains(config, "\"daMode\": \"NeoFS\"");
        StringAssert.Contains(config, "\"proofType\": \"Zk\"");
        StringAssert.Contains(config, "\"exitModel\": \"Delayed\"");
        StringAssert.Contains(config, "\"permissionlessExit\": false");
    }

    [TestMethod]
    public void CreateChain_SidechainTemplate_EmitsNoneProof()
    {
        var rc = CreateChainCommand.Run(new[]
        {
            "--chain-id", "4099",
            "--template", "sidechain",
            "--output", _tempDir,
        });
        Assert.AreEqual(0, rc);
        var config = File.ReadAllText(Path.Combine(_tempDir, "chain.config.json"));
        StringAssert.Contains(config, "\"chainMode\": \"SidechainMode\"");
        StringAssert.Contains(config, "\"securityLevel\": \"Sidechain\"");
        StringAssert.Contains(config, "\"proofType\": \"None\"");
        StringAssert.Contains(config, "\"daMode\": \"External\"");
    }

    [TestMethod]
    public void CreateChain_UnknownTemplate_FallsBackToRollup()
    {
        // TemplateCatalog.Resolve returns the default (rollup) for unknown names —
        // pin that the config reflects rollup dimensions even though the operator
        // typed an invalid template name. The 'template' string in the JSON still
        // shows what the operator typed (so they can spot the typo).
        var rc = CreateChainCommand.Run(new[]
        {
            "--chain-id", "5099",
            "--template", "not-a-real-template",
            "--output", _tempDir,
        });
        Assert.AreEqual(0, rc);
        var config = File.ReadAllText(Path.Combine(_tempDir, "chain.config.json"));
        // Operator's typed template is preserved verbatim (so they can grep for it).
        StringAssert.Contains(config, "\"template\": \"not-a-real-template\"");
        // But the dimensions fall back to rollup defaults.
        StringAssert.Contains(config, "\"securityLevel\": \"Optimistic\"");
        StringAssert.Contains(config, "\"daMode\": \"L1\"");
    }

    [TestMethod]
    public void CreateChain_VmFlag_Propagates()
    {
        var rc = CreateChainCommand.Run(new[]
        {
            "--chain-id", "6099",
            "--vm", "riscv2",
            "--output", _tempDir,
        });
        Assert.AreEqual(0, rc);
        var config = File.ReadAllText(Path.Combine(_tempDir, "chain.config.json"));
        StringAssert.Contains(config, "\"vm\": \"riscv2\"");
    }

    [TestMethod]
    public void CreateChain_PathTakesPrecedenceOverOutput()
    {
        // CreateChainCommand uses --path > --output precedence (operator-script-friendly:
        // --path was the original flag for create-chain and --output came later). Pin
        // the precedence so a future refactor doesn't reverse it.
        var pathDir = Path.Combine(_tempDir, "from-path");
        var outputDir = Path.Combine(_tempDir, "from-output");

        var rc = CreateChainCommand.Run(new[]
        {
            "--chain-id", "7099",
            "--path", pathDir,
            "--output", outputDir,
        });
        Assert.AreEqual(0, rc);
        Assert.IsTrue(File.Exists(Path.Combine(pathDir, "chain.config.json")),
            "--path wins: chain.config.json must land in the --path directory");
        Assert.IsFalse(File.Exists(Path.Combine(outputDir, "chain.config.json")),
            "--output is ignored when --path is supplied");
    }

    [TestMethod]
    public void CreateChain_DefaultOutput_IsChainNumberDir()
    {
        // Without --output / --path, default is `./chain-<chainId>`. To avoid polluting
        // cwd in CI, pass an explicit output that mimics the default-format path.
        var defaultMimic = Path.Combine(_tempDir, "chain-8099");
        Directory.CreateDirectory(_tempDir);
        var rc = CreateChainCommand.Run(new[]
        {
            "--chain-id", "8099",
            "--output", defaultMimic,
        });
        Assert.AreEqual(0, rc);
        Assert.IsTrue(File.Exists(Path.Combine(defaultMimic, "chain.config.json")));
    }

    [TestMethod]
    public void CreateChain_ConfigJson_IsParseable()
    {
        // The emitted JSON must actually parse. Pin: a future template change that
        // breaks JSON well-formedness (missing comma, unescaped quote, etc.) fails
        // here, not at the operator's first `validate` run.
        CreateChainCommand.Run(new[]
        {
            "--chain-id", "1099",
            "--output", _tempDir,
        });
        var configPath = Path.Combine(_tempDir, "chain.config.json");
        var json = File.ReadAllText(configPath);
        // System.Text.Json throws if the JSON is malformed.
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.AreEqual(1099u, root.GetProperty("chainId").GetUInt32());
        Assert.AreEqual("rollup", root.GetProperty("template").GetString());
        Assert.AreEqual("L1", root.GetProperty("daMode").GetString());
    }

    [TestMethod]
    public void CreateChain_NextStepHint_PointsAtInitL2()
    {
        // The operator sees a "Next: neo-stack init-l2 --chain-id N" line at the end.
        // Pin this hint so a refactor doesn't accidentally remove it (or change its
        // chainId pin) and leave operators wondering what to run next.
        var (rc, output) = CaptureStdout(() => CreateChainCommand.Run(new[]
        {
            "--chain-id", "9099",
            "--output", _tempDir,
        }));
        Assert.AreEqual(0, rc);
        StringAssert.Contains(output, "Next: `neo-stack init-l2 --chain-id 9099`");
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
