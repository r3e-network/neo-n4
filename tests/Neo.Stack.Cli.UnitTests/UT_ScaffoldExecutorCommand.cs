using System;
using System.IO;
using Neo.Stack.Cli.Commands;

namespace Neo.Stack.Cli.UnitTests;

/// <summary>
/// Tests for <see cref="ScaffoldExecutorCommand"/> — the operator-facing scaffold for
/// custom <c>ITransactionExecutor</c> projects. Covers each emitted file, argument
/// validation (name + chainId), and the refuse-to-overwrite guard.
/// </summary>
[TestClass]
public class UT_ScaffoldExecutorCommand
{
    private string _tempDir = null!;

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "neo-n4-scaffold-test-" + Guid.NewGuid().ToString("N"));
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public void HappyPath_EmitsAllSixFiles_AndExitsZero()
    {
        var rc = ScaffoldExecutorCommand.Run(new[]
        {
            "--name", "TestChain",
            "--chain-id", "5555",
            "--output", _tempDir,
        });
        Assert.AreEqual(0, rc, "happy-path scaffold must exit 0");

        // The 6 expected outputs.
        Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "TestChainExecutor.csproj")), "csproj missing");
        Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "TestChainExecutor.cs")), "executor missing");
        Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "ITestChainState.cs")), "state seam missing");
        Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "TestChainTxBuilder.cs")), "tx builder missing");
        Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "TestChainKeyedStateStoreAdapter.cs")), "adapter missing");
        Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "README.md")), "README missing");
    }

    [TestMethod]
    public void EmittedCsproj_HasCorrectProjectAndAssemblyNames()
    {
        ScaffoldExecutorCommand.Run(new[] { "--name", "Foo", "--output", _tempDir });
        var csproj = File.ReadAllText(Path.Combine(_tempDir, "FooExecutor.csproj"));
        StringAssert.Contains(csproj, "<RootNamespace>FooExecutor</RootNamespace>",
            "RootNamespace must match the project name");
        StringAssert.Contains(csproj, "<AssemblyName>FooExecutor</AssemblyName>",
            "AssemblyName must match the project name");
        StringAssert.Contains(csproj, "..\\..\\..\\src\\Neo.L2.Abstractions\\Neo.L2.Abstractions.csproj",
            "must reference Abstractions via 3-up relative path (matches in-monorepo placement)");
        StringAssert.Contains(csproj, "..\\..\\..\\src\\Neo.L2.Executor\\Neo.L2.Executor.csproj",
            "must reference Executor via 3-up relative path");
    }

    [TestMethod]
    public void EmittedExecutor_ContainsExpectedSkeleton()
    {
        ScaffoldExecutorCommand.Run(new[] { "--name", "Foo", "--output", _tempDir });
        var src = File.ReadAllText(Path.Combine(_tempDir, "FooExecutor.cs"));
        StringAssert.Contains(src, "namespace FooExecutor;");
        StringAssert.Contains(src, "public sealed class FooExecutor : ITransactionExecutor");
        StringAssert.Contains(src, "public ValueTask<TransactionExecutionResult> ExecuteAsync");
        StringAssert.Contains(src, "Opcode.NoOp", "must contain the placeholder NoOp dispatch");
        StringAssert.Contains(src, "// TODO: add your chain's opcodes here.",
            "must contain the customization marker");
    }

    [TestMethod]
    public void EmittedReadme_LinksToReferenceSample()
    {
        ScaffoldExecutorCommand.Run(new[] { "--name", "Foo", "--output", _tempDir });
        var readme = File.ReadAllText(Path.Combine(_tempDir, "README.md"));
        StringAssert.Contains(readme, "Sample.CounterChainExecutor",
            "README must link to the working reference sample");
        StringAssert.Contains(readme, "5-step customization checklist",
            "README must include the customization checklist");
    }

    [TestMethod]
    public void InvalidName_DigitFirst_Rejected()
    {
        var rc = ScaffoldExecutorCommand.Run(new[]
        {
            "--name", "1Invalid",
            "--output", _tempDir,
        });
        Assert.AreEqual(1, rc, "leading-digit name must be rejected (would emit invalid C# identifier)");
        Assert.IsFalse(Directory.Exists(_tempDir), "rejection must not create the output directory");
    }

    [TestMethod]
    public void InvalidName_Hyphen_Rejected()
    {
        var rc = ScaffoldExecutorCommand.Run(new[]
        {
            "--name", "my-chain",
            "--output", _tempDir,
        });
        Assert.AreEqual(1, rc, "hyphen in name must be rejected (would emit invalid C# identifier)");
    }

    [TestMethod]
    public void InvalidName_Empty_Rejected()
    {
        var rc = ScaffoldExecutorCommand.Run(new[]
        {
            "--name", "",
            "--output", _tempDir,
        });
        Assert.AreEqual(1, rc, "empty name must be rejected");
    }

    [TestMethod]
    public void ChainIdZero_Rejected()
    {
        // ChainIdValidator.ValidateL2 throws InvalidDataException on chainId 0
        // (the L1 sentinel). The CLI's outer try/catch in Program.Main converts that
        // to exit code 1; here we observe the throw directly since we're calling Run.
        Assert.ThrowsExactly<System.IO.InvalidDataException>(() =>
            ScaffoldExecutorCommand.Run(new[]
            {
                "--name", "Foo",
                "--chain-id", "0",
                "--output", _tempDir,
            }));
    }

    [TestMethod]
    public void ChainIdNonNumeric_Rejected()
    {
        var rc = ScaffoldExecutorCommand.Run(new[]
        {
            "--name", "Foo",
            "--chain-id", "abc",
            "--output", _tempDir,
        });
        Assert.AreEqual(1, rc, "non-numeric chain-id must be rejected");
    }

    [TestMethod]
    public void NonEmptyOutputDir_Rejected_RefusesToOverwrite()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "preexisting.txt"), "hands off!");
        var rc = ScaffoldExecutorCommand.Run(new[]
        {
            "--name", "Foo",
            "--output", _tempDir,
        });
        Assert.AreEqual(1, rc, "non-empty output dir must be rejected");
        // Verify the existing file is untouched.
        Assert.AreEqual("hands off!", File.ReadAllText(Path.Combine(_tempDir, "preexisting.txt")));
    }

    [TestMethod]
    public void DefaultChainId_When_NotProvided()
    {
        // No --chain-id flag; the README should embed the default (1001).
        ScaffoldExecutorCommand.Run(new[] { "--name", "Foo", "--output", _tempDir });
        var readme = File.ReadAllText(Path.Combine(_tempDir, "README.md"));
        StringAssert.Contains(readme, "(chainId `1001` by default)");
    }

    [TestMethod]
    public void NamesWithDifferentCasing_Honored()
    {
        // The scaffold preserves the operator's casing. Verify executor + state seam files
        // reflect the input as-typed (no case normalization).
        ScaffoldExecutorCommand.Run(new[] { "--name", "MyDeFi", "--output", _tempDir });
        Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "MyDeFiExecutor.cs")));
        Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "IMyDeFiState.cs")));
        Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "MyDeFiTxBuilder.cs")));
        Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "MyDeFiKeyedStateStoreAdapter.cs")));
    }

    [TestMethod]
    public void PathFlagAlias_UsedWhenOutputAbsent()
    {
        // Mirror the CreateChainCommand behavior: --path is an alias for --output so
        // scripts can string subcommands together with a single flag name.
        var rc = ScaffoldExecutorCommand.Run(new[]
        {
            "--name", "Foo",
            "--path", _tempDir,
        });
        Assert.AreEqual(0, rc);
        Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "FooExecutor.csproj")));
    }
}
