using System;

namespace Neo.L2.Devnet;

/// <summary>
/// Argument-parsing helpers for the in-process devnet runner. Public so unit tests
/// can exercise each flag's parsing + validation behavior directly without
/// subprocess-invoking the binary.
/// </summary>
public static class DevnetArgs
{
    /// <summary>
    /// Parse <c>--metrics-port &lt;port&gt;</c>. Returns null when the flag is absent
    /// or the value is non-numeric. Throws <see cref="System.IO.InvalidDataException"/>
    /// for out-of-range ports (validated via <c>Neo.L2.Telemetry.PortValidator</c>) so
    /// a bogus port surfaces a clear error here instead of a stack trace from
    /// <c>IPEndPoint</c> construction deep in the wiring path.
    /// </summary>
    public static int? ParseMetricsPort(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--metrics-port" && int.TryParse(args[i + 1], out var port))
            {
                return Telemetry.PortValidator.Validate(port, "--metrics-port");
            }
        }
        return null;
    }

    /// <summary>Parse <c>--data-dir &lt;path&gt;</c>. Returns null when absent.</summary>
    public static string? ParseDataDir(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--data-dir") return args[i + 1];
        }
        return null;
    }

    /// <summary>Parse <c>--config &lt;path&gt;</c>. Returns null when absent.</summary>
    public static string? ParseConfigPath(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--config") return args[i + 1];
        }
        return null;
    }

    /// <summary>
    /// Parse <c>--executor &lt;kind&gt;</c>. Defaults to <c>reference</c> for legacy
    /// compatibility. Recognizes <c>reference</c> (no-op default) and <c>counter</c>
    /// (the <c>Sample.CounterChainExecutor</c> demo). Unknown values fall back to
    /// <c>reference</c> with a warning emitted to <c>Console.Error</c> so a typo
    /// doesn't silently swap executors.
    /// </summary>
    public static string ParseExecutor(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] != "--executor") continue;
            var value = args[i + 1];
            if (value == "reference" || value == "counter") return value;
            Console.Error.WriteLine(
                $"--executor '{value}' not recognized; falling back to 'reference'. " +
                "Valid values: reference, counter.");
            return "reference";
        }
        return "reference";
    }
}
