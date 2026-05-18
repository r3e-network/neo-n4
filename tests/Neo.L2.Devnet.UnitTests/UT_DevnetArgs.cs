using System;
using System.IO;
using Neo.L2.Devnet;

namespace Neo.L2.Devnet.UnitTests;

/// <summary>
/// Tests for <see cref="DevnetArgs"/> — the argument-parsing helpers used by the
/// in-process devnet runner. Pinning each flag's parsing + validation behavior
/// (especially `--executor`'s fallback warning + `--metrics-port`'s range check)
/// catches regressions that would surface only when an operator runs the binary.
/// </summary>
[TestClass]
public class UT_DevnetArgs
{
    // ---- ParseMetricsPort ----

    [TestMethod]
    public void MetricsPort_ValidValue_Returned()
    {
        Assert.AreEqual(9090, DevnetArgs.ParseMetricsPort(new[] { "--metrics-port", "9090" }));
        Assert.AreEqual(0, DevnetArgs.ParseMetricsPort(new[] { "--metrics-port", "0" }),
            "port 0 (any-free) is valid");
        Assert.AreEqual(65535, DevnetArgs.ParseMetricsPort(new[] { "--metrics-port", "65535" }),
            "port 65535 (max) is valid");
    }

    [TestMethod]
    public void MetricsPort_AbsentFlag_ReturnsNull()
    {
        Assert.IsNull(DevnetArgs.ParseMetricsPort(Array.Empty<string>()));
        Assert.IsNull(DevnetArgs.ParseMetricsPort(new[] { "--config", "x" }));
    }

    [TestMethod]
    public void MetricsPort_NonNumeric_ReturnsNull()
    {
        // int.TryParse fails → command treats the flag as absent. Pin so a future
        // refactor doesn't accidentally throw on non-numeric (which would break
        // legacy scripts).
        Assert.IsNull(DevnetArgs.ParseMetricsPort(new[] { "--metrics-port", "abc" }));
    }

    [TestMethod]
    public void MetricsPort_OutOfRange_Throws()
    {
        // PortValidator.Validate throws InvalidDataException on negative / >65535.
        Assert.ThrowsExactly<InvalidDataException>(() =>
            DevnetArgs.ParseMetricsPort(new[] { "--metrics-port", "-1" }));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            DevnetArgs.ParseMetricsPort(new[] { "--metrics-port", "65536" }));
    }

    [TestMethod]
    public void MetricsPort_NullArgs_Rejected()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => DevnetArgs.ParseMetricsPort(null!));
    }

    // ---- ParseDataDir ----

    [TestMethod]
    public void DataDir_ValueReturned()
    {
        Assert.AreEqual("/tmp/x", DevnetArgs.ParseDataDir(new[] { "--data-dir", "/tmp/x" }));
    }

    [TestMethod]
    public void DataDir_AbsentFlag_ReturnsNull()
    {
        Assert.IsNull(DevnetArgs.ParseDataDir(Array.Empty<string>()));
    }

    // ---- ParseConfigPath ----

    [TestMethod]
    public void ConfigPath_ValueReturned()
    {
        Assert.AreEqual("./my-l2/chain.config.json",
            DevnetArgs.ParseConfigPath(new[] { "--config", "./my-l2/chain.config.json" }));
    }

    [TestMethod]
    public void ConfigPath_AbsentFlag_ReturnsNull()
    {
        Assert.IsNull(DevnetArgs.ParseConfigPath(Array.Empty<string>()));
    }

    // ---- ParseExecutor ----

    [TestMethod]
    public void Executor_DefaultsToReference_WhenAbsent()
    {
        Assert.AreEqual("reference", DevnetArgs.ParseExecutor(Array.Empty<string>()));
        Assert.AreEqual("reference", DevnetArgs.ParseExecutor(new[] { "5" }));
    }

    [TestMethod]
    public void Executor_RecognizesReference()
    {
        Assert.AreEqual("reference", DevnetArgs.ParseExecutor(new[] { "--executor", "reference" }));
    }

    [TestMethod]
    public void Executor_RecognizesCounter()
    {
        Assert.AreEqual("counter", DevnetArgs.ParseExecutor(new[] { "--executor", "counter" }));
    }

    [TestMethod]
    public void Executor_RecognizesNeovm()
    {
        // Pin: 'neovm' stays recognized as a legacy compatibility mode via
        // ApplicationEngineTransactionExecutor + NeoVMGenesisBootstrap. A
        // regression that drops it from the allowlist would silently fall back
        // to ReferenceTransactionExecutor.
        Assert.AreEqual("neovm", DevnetArgs.ParseExecutor(new[] { "--executor", "neovm" }));
    }

    [TestMethod]
    public void Executor_RecognizesNeoVm2RiscV()
    {
        Assert.AreEqual("riscv", DevnetArgs.ParseExecutor(new[] { "--executor", "riscv" }));
        Assert.AreEqual("riscv", DevnetArgs.ParseExecutor(new[] { "--executor", "neovm2-riscv" }));
        Assert.AreEqual("riscv", DevnetArgs.ParseExecutor(new[] { "--executor", "riscv2" }));
    }

    [TestMethod]
    public void Executor_UnknownValue_FallsBackToReference_WithWarning()
    {
        // Pin the warning + fallback. Without this, a typo like '--executor counte'
        // would silently swap executors with no signal to the operator.
        var origErr = Console.Error;
        try
        {
            var sw = new StringWriter();
            Console.SetError(sw);
            var result = DevnetArgs.ParseExecutor(new[] { "--executor", "counte" });
            Assert.AreEqual("reference", result, "unknown values fall back to 'reference'");
            var stderr = sw.ToString();
            StringAssert.Contains(stderr, "--executor 'counte' not recognized");
            StringAssert.Contains(stderr, "Valid values: reference, counter, riscv, neovm2-riscv, neovm");
        }
        finally
        {
            Console.SetError(origErr);
        }
    }

    [TestMethod]
    public void Executor_NullArgs_Rejected()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => DevnetArgs.ParseExecutor(null!));
    }

    [TestMethod]
    public void DataDir_NullArgs_Rejected()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => DevnetArgs.ParseDataDir(null!));
    }

    [TestMethod]
    public void ConfigPath_NullArgs_Rejected()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => DevnetArgs.ParseConfigPath(null!));
    }
}
