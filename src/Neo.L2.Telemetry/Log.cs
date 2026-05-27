namespace Neo.L2.Telemetry;

/// <summary>
/// Minimal structured logging abstraction. Zero external dependencies — swap
/// the default <see cref="ConsoleLogProvider"/> for Serilog/NLog/OpenTelemetry
/// by implementing <see cref="ILogProvider"/> and calling
/// <c>Log.WithProvider(...)</c> at startup.
/// </summary>
/// <remarks>
/// API surface mirrors <c>Microsoft.Extensions.Logging.ILogger</c> so migration
/// to MEL is a search-and-replace of <c>Log.Xxx(...)</c> → <c>_logger.Xxx(...)</c>.
/// </remarks>
public static class Log
{
    private static ILogProvider _provider = ConsoleLogProvider.Instance;

    /// <summary>Replace the log provider at startup.</summary>
    public static void WithProvider(ILogProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
    }

    /// <summary>Log at Debug level.</summary>
    public static void Debug(string message,
        (string Key, object? Value)? arg1 = null, (string Key, object? Value)? arg2 = null)
        => _provider.Log(LogLevel.Debug, message, arg1, arg2);

    /// <summary>Log at Information level.</summary>
    public static void Info(string message,
        (string Key, object? Value)? arg1 = null, (string Key, object? Value)? arg2 = null)
        => _provider.Log(LogLevel.Information, message, arg1, arg2);

    /// <summary>Log at Warning level.</summary>
    public static void Warn(string message,
        (string Key, object? Value)? arg1 = null, (string Key, object? Value)? arg2 = null)
        => _provider.Log(LogLevel.Warning, message, arg1, arg2);

    /// <summary>Log at Error level with optional exception.</summary>
    public static void Error(string message, Exception? ex = null,
        (string Key, object? Value)? arg1 = null, (string Key, object? Value)? arg2 = null)
        => _provider.Log(LogLevel.Error, message, arg1, arg2, ex);

    /// <summary>True if Debug-level logs are being emitted.</summary>
    public static bool IsDebugEnabled => _provider.IsEnabled(LogLevel.Debug);
}

/// <summary>Log severity level.</summary>
public enum LogLevel { Debug, Information, Warning, Error }

/// <summary>Pluggable log sink.</summary>
public interface ILogProvider
{
    /// <summary>True if <paramref name="level"/> is enabled.</summary>
    bool IsEnabled(LogLevel level);
    /// <summary>Emit a log entry.</summary>
    void Log(LogLevel level, string message,
        (string Key, object? Value)? arg1, (string Key, object? Value)? arg2,
        Exception? ex = null);
}

/// <summary>Default console provider. Writes structured text to stderr so
/// stdout remains clean for RPC responses / piping.</summary>
public sealed class ConsoleLogProvider : ILogProvider
{
    /// <summary>Singleton.</summary>
    public static readonly ConsoleLogProvider Instance = new();
    private ConsoleLogProvider() { }

    /// <inheritdoc />
    public bool IsEnabled(LogLevel level) => true;

    /// <inheritdoc />
    public void Log(LogLevel level, string message,
        (string Key, object? Value)? arg1, (string Key, object? Value)? arg2,
        Exception? ex = null)
    {
        var ts = DateTimeOffset.UtcNow.ToString("o");
        var levelStr = level switch
        {
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            _ => "???",
        };
        var args = FormatArgs(arg1, arg2);
        var line = args.Length == 0
            ? $"{ts} [{levelStr}] {message}"
            : $"{ts} [{levelStr}] {message} {args}";
        if (ex is not null)
            line += $" exception={ex.GetType().Name}: {ex.Message}";
        Console.Error.WriteLine(line);
    }

    private static string FormatArgs(
        (string Key, object? Value)? arg1, (string Key, object? Value)? arg2)
    {
        if (arg1 is null && arg2 is null) return "";
        var parts = new System.Text.StringBuilder();
        if (arg1 is var a1 && a1.HasValue)
            parts.Append($"{a1.Value.Key}={a1.Value.Value}");
        if (arg2 is var a2 && a2.HasValue)
            parts.Append($" {a2.Value.Key}={a2.Value.Value}");
        return parts.ToString();
    }
}
