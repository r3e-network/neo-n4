using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Neo.Stack.Cli.Commands;

internal sealed record OperatorProcessSpec(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    TimeSpan ShutdownGracePeriod)
{
    public string DisplayCommand => string.Join(
        " ",
        new[] { FileName }.Concat(Arguments).Select(static argument => JsonSerializer.Serialize(argument)));
}

internal interface IOperatorProcessRunner
{
    Task<int> RunAsync(OperatorProcessSpec spec, CancellationToken cancellationToken);
}

internal sealed class SystemOperatorProcessRunner : IOperatorProcessRunner
{
    private const int SigTerm = 15;

    public async Task<int> RunAsync(OperatorProcessSpec spec, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var startInfo = new ProcessStartInfo
        {
            FileName = spec.FileName,
            WorkingDirectory = spec.WorkingDirectory,
            UseShellExecute = false,
        };
        foreach (var argument in spec.Arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new InvalidOperationException($"Failed to start {spec.FileName}.");
        Console.WriteLine($"Started PID {process.Id}.");

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await StopAsync(process, spec.ShutdownGracePeriod).ConfigureAwait(false);
            return 130;
        }
    }

    private static async Task StopAsync(Process process, TimeSpan gracePeriod)
    {
        if (process.HasExited) return;

        var signalled = OperatingSystem.IsWindows()
            ? process.CloseMainWindow()
            : Kill(process.Id, SigTerm) == 0;
        if (signalled)
        {
            using var timeout = new CancellationTokenSource(gracePeriod);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
            }
        }

        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().ConfigureAwait(false);
        }
    }

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int processId, int signal);
}

internal sealed class OperatorShutdown : IDisposable
{
    private readonly CancellationTokenSource _source = new();
    private readonly PosixSignalRegistration? _sigterm;

    private OperatorShutdown()
    {
        Console.CancelKeyPress += OnCancelKeyPress;
        if (!OperatingSystem.IsWindows())
        {
            _sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
            {
                context.Cancel = true;
                _source.Cancel();
            });
        }
    }

    public CancellationToken Token => _source.Token;

    public static OperatorShutdown Create() => new();

    public void Dispose()
    {
        Console.CancelKeyPress -= OnCancelKeyPress;
        _sigterm?.Dispose();
        _source.Dispose();
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs eventArgs)
    {
        eventArgs.Cancel = true;
        _source.Cancel();
    }
}

internal sealed record ResolvedOperatorExecutable(
    string FileName,
    IReadOnlyList<string> PrefixArguments,
    string DeploymentRoot);

internal static class OperatorExecutableResolver
{
    public static bool TryResolve(
        string configuredValue,
        string environmentVariable,
        string baseDirectory,
        out ResolvedOperatorExecutable executable,
        out string error)
    {
        executable = null!;
        error = "";
        var value = string.IsNullOrWhiteSpace(configuredValue)
            ? Environment.GetEnvironmentVariable(environmentVariable)
            : configuredValue;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = $"Executable path is required; pass it explicitly or set {environmentVariable}.";
            return false;
        }

        var path = ResolvePath(value, baseDirectory);
        if (path is null)
        {
            error = $"Executable not found: {value}";
            return false;
        }

        var deploymentRoot = Path.GetDirectoryName(path)!;
        var isManagedAssembly = string.Equals(Path.GetExtension(path), ".dll", StringComparison.OrdinalIgnoreCase);
        if (!isManagedAssembly && !OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(path);
            const UnixFileMode executeBits = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            if ((mode & executeBits) == 0)
            {
                error = $"Executable bit is not set: {path}";
                return false;
            }
        }
        executable = isManagedAssembly
            ? new ResolvedOperatorExecutable("dotnet", new[] { path }, deploymentRoot)
            : new ResolvedOperatorExecutable(path, Array.Empty<string>(), deploymentRoot);
        return true;
    }

    private static string? ResolvePath(string value, string baseDirectory)
    {
        if (Path.IsPathFullyQualified(value) || value.Contains(Path.DirectorySeparatorChar))
        {
            var fullPath = Path.GetFullPath(value, baseDirectory);
            return File.Exists(fullPath) ? fullPath : null;
        }

        var localPath = Path.GetFullPath(value, baseDirectory);
        if (File.Exists(localPath)) return localPath;
        foreach (var pathEntry in (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(pathEntry, value);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        return null;
    }
}
