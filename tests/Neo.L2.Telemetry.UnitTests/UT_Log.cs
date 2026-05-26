namespace Neo.L2.Telemetry.Tests;

[TestClass]
public class UT_Log
{
    [TestMethod]
    public void Debug_DoesNotThrow()
    {
        Log.Debug("test debug message");
    }

    [TestMethod]
    public void Info_DoesNotThrow()
    {
        Log.Info("test info message");
    }

    [TestMethod]
    public void Info_WithStructuredArgs()
    {
        Log.Info("message with args", ("chain_id", 1099), ("batch", 7));
    }

    [TestMethod]
    public void Warn_DoesNotThrow()
    {
        Log.Warn("test warning");
    }

    [TestMethod]
    public void Error_WithException()
    {
        Log.Error("test error", new InvalidOperationException("test ex"),
            ("component", "test"));
    }

    [TestMethod]
    public void Error_WithoutException()
    {
        Log.Error("error without exception");
    }

    [TestMethod]
    public void IsDebugEnabled_ReturnsTrue()
    {
        Assert.IsTrue(Log.IsDebugEnabled);
    }

    [TestMethod]
    public void WithProvider_ReplacesProvider()
    {
        var custom = new CountingLogProvider();
        Log.WithProvider(custom);
        Log.Info("should go to custom");
        Assert.AreEqual(1, custom.Count);
        Log.WithProvider(ConsoleLogProvider.Instance); // restore
    }

    private sealed class CountingLogProvider : ILogProvider
    {
        public int Count;
        public bool IsEnabled(LogLevel level) => true;
        public void Log(LogLevel level, string message,
            (string Key, object? Value)? arg1, (string Key, object? Value)? arg2,
            Exception? ex = null) => Interlocked.Increment(ref Count);
    }
}
