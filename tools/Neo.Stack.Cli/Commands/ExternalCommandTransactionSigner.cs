using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Neo.Cryptography;
using Neo.Extensions.IO;
using Neo.L2.Settlement.Rpc;
using Neo.Network.P2P;
using Neo.Network.P2P.Payloads;
using Neo.SmartContract;
using Neo.VM;
using Neo.Wallets;

namespace Neo.Stack.Cli.Commands;

internal sealed record ExternalSignerCommand(
    string FileName,
    IReadOnlyList<string> Arguments,
    TimeSpan Timeout);

internal sealed record ExternalSignerCommandRequest(
    uint Network,
    UInt160 Account,
    WitnessScope Scope,
    ReadOnlyMemory<byte> SignData,
    ReadOnlyMemory<byte> Transaction)
{
    public bool Equals(ExternalSignerCommandRequest? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Network == other.Network
            && Account == other.Account
            && Scope == other.Scope
            && SignData.Span.SequenceEqual(other.SignData.Span)
            && Transaction.Span.SequenceEqual(other.Transaction.Span);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Network);
        hash.Add(Account);
        hash.Add(Scope);
        hash.AddBytes(SignData.Span);
        hash.AddBytes(Transaction.Span);
        return hash.ToHashCode();
    }
}

internal interface IExternalSignerCommandRunner
{
    Task<ReadOnlyMemory<byte>> SignAsync(
        ExternalSignerCommand command,
        ExternalSignerCommandRequest request,
        CancellationToken cancellationToken);
}

internal sealed class ExternalCommandTransactionSigner : INeoTransactionSigner
{
    private readonly ExternalSignerCommand _command;
    private readonly ReadOnlyMemory<byte> _verificationScript;
    private readonly ReadOnlyMemory<byte> _placeholderInvocationScript;
    private readonly IExternalSignerCommandRunner _runner;

    public ExternalCommandTransactionSigner(
        ExternalSignerCommand command,
        UInt160 account,
        ReadOnlyMemory<byte> verificationScript,
        ReadOnlyMemory<byte> placeholderInvocationScript,
        IExternalSignerCommandRunner? runner = null)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (account == UInt160.Zero)
            throw new ArgumentException("Signer account must be non-zero.", nameof(account));
        if (verificationScript.IsEmpty)
            throw new ArgumentException("Verification script must not be empty.", nameof(verificationScript));
        if (placeholderInvocationScript.IsEmpty)
            throw new ArgumentException("Placeholder invocation script must not be empty.", nameof(placeholderInvocationScript));
        if (verificationScript.Length > 1024 || placeholderInvocationScript.Length > 1024)
            throw new ArgumentOutOfRangeException(nameof(verificationScript), "Witness scripts must not exceed 1024 bytes.");
        if (verificationScript.Span.ToScriptHash() != account)
            throw new ArgumentException("Verification script hash does not match signer account.", nameof(verificationScript));

        _command = command;
        Account = account;
        _verificationScript = verificationScript.ToArray();
        _placeholderInvocationScript = placeholderInvocationScript.ToArray();
        _runner = runner ?? new SystemExternalSignerCommandRunner();
    }

    public UInt160 Account { get; }

    public WitnessScope Scope => WitnessScope.CalledByEntry;

    public Witness CreatePlaceholderWitness()
    {
        return CreateWitness(_placeholderInvocationScript);
    }

    public async ValueTask<Witness> SignAsync(
        Transaction transaction,
        uint network,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        cancellationToken.ThrowIfCancellationRequested();
        var request = new ExternalSignerCommandRequest(
            network,
            Account,
            Scope,
            transaction.GetSignData(network),
            transaction.ToArray());
        var invocationScript = await _runner.SignAsync(_command, request, cancellationToken).ConfigureAwait(false);
        if (invocationScript.IsEmpty)
            throw new InvalidOperationException("External signer returned an empty invocation script.");
        if (invocationScript.Length > 1024)
            throw new InvalidOperationException("External signer returned an oversized invocation script.");
        if (invocationScript.Length != _placeholderInvocationScript.Length)
            throw new InvalidOperationException("External signer invocation script length does not match the fee-estimation witness.");
        return CreateWitness(invocationScript);
    }

    private Witness CreateWitness(ReadOnlyMemory<byte> invocationScript)
    {
        return new Witness
        {
            InvocationScript = invocationScript.ToArray(),
            VerificationScript = _verificationScript.ToArray(),
        };
    }
}

internal sealed class SystemExternalSignerCommandRunner : IExternalSignerCommandRunner
{
    private static readonly JsonSerializerOptions RequestSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<ReadOnlyMemory<byte>> SignAsync(
        ExternalSignerCommand command,
        ExternalSignerCommandRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo
        {
            FileName = command.FileName,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in command.Arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException($"Failed to start external signer {command.FileName}.");

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(command.Timeout);
        try
        {
            var serializedRequest = SerializeRequest(request);
            await process.StandardInput.WriteAsync(serializedRequest.AsMemory(), timeout.Token).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(timeout.Token).ConfigureAwait(false);
            process.StandardInput.Close();

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

            var output = await outputTask.ConfigureAwait(false);
            _ = await errorTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"External signer exited with code {process.ExitCode}.");
            return ParseInvocationScript(output);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await StopAsync(process).ConfigureAwait(false);
            throw new TimeoutException($"External signer exceeded its {command.Timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} second deadline.");
        }
        catch
        {
            await StopAsync(process).ConfigureAwait(false);
            throw;
        }
    }

    internal static string SerializeRequest(ExternalSignerCommandRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return System.Text.Json.JsonSerializer.Serialize(new ExternalSignerWireRequest(
            1,
            request.Network,
            request.Account.ToString(),
            request.Scope.ToString(),
            Convert.ToBase64String(request.SignData.Span),
            Convert.ToBase64String(request.Transaction.Span)), RequestSerializerOptions);
    }

    private static ReadOnlyMemory<byte> ParseInvocationScript(string output)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("invocationScript", out var scriptElement)
                || scriptElement.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException("External signer response must contain a base64 invocationScript.");
            }

            var encodedScript = scriptElement.GetString();
            if (string.IsNullOrWhiteSpace(encodedScript))
                throw new InvalidOperationException("External signer response contains an empty invocationScript.");
            var invocationScript = Convert.FromBase64String(encodedScript);
            if (invocationScript.Length is 0 or > 1024)
                throw new InvalidOperationException("External signer response contains an invalid invocationScript length.");
            return invocationScript;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("External signer returned invalid JSON.", exception);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("External signer returned a non-base64 invocationScript.", exception);
        }
    }

    private static async Task StopAsync(Process process)
    {
        if (process.HasExited) return;
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            return;
        }
        await process.WaitForExitAsync().ConfigureAwait(false);
    }

    private sealed record ExternalSignerWireRequest(
        int Version,
        uint Network,
        string Account,
        string Scope,
        string SignData,
        string Transaction);
}

internal static class OperatorTransactionSignerFactory
{
    private const string DefaultWifEnvironmentVariable = "NEO_N4_OPERATOR_WIF";

    public static bool TryCreate(
        string[] args,
        string optionPrefix,
        IExternalSignerCommandRunner? externalSignerCommandRunner,
        out INeoTransactionSigner signer,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(args);
        signer = null!;
        error = "";

        var signerCommandOption = Option(optionPrefix, "signer-command");
        var wifOption = Option(optionPrefix, "wif-env");
        if (ArgUtil.HasFlag(args, signerCommandOption))
        {
            if (ArgUtil.HasFlag(args, wifOption))
            {
                error = $"{signerCommandOption} cannot be combined with {wifOption}.";
                return false;
            }
            return TryCreateExternalSigner(args, optionPrefix, externalSignerCommandRunner, out signer, out error);
        }

        var wifEnvironmentVariable = ArgUtil.Get(args, wifOption, DefaultWifEnvironmentVariable);
        var wif = Environment.GetEnvironmentVariable(wifEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(wif))
        {
            error = $"{wifEnvironmentVariable} is required; pass {wifOption} <name> or configure {signerCommandOption}.";
            return false;
        }

        try
        {
            signer = ImportLocalKeySigner(wif);
            return true;
        }
        catch (Exception exception)
        {
            error = $"Unable to import {wifEnvironmentVariable}: {exception.Message}";
            return false;
        }
    }

    private static bool TryCreateExternalSigner(
        string[] args,
        string optionPrefix,
        IExternalSignerCommandRunner? externalSignerCommandRunner,
        out INeoTransactionSigner signer,
        out string error)
    {
        signer = null!;
        error = "";
        var signerCommandOption = Option(optionPrefix, "signer-command");
        var commandValue = ArgUtil.Get(args, signerCommandOption, "");
        if (string.IsNullOrWhiteSpace(commandValue))
        {
            error = $"{signerCommandOption} <path> is required.";
            return false;
        }
        if (!OperatorExecutableResolver.TryResolve(
                commandValue,
                "NEO_N4_UNUSED_SIGNER_COMMAND",
                Directory.GetCurrentDirectory(),
                out var executable,
                out error))
        {
            return false;
        }

        var accountOption = Option(optionPrefix, "signer-account");
        var accountValue = ArgUtil.Get(args, accountOption, "");
        if (!UInt160.TryParse(accountValue, out var account) || account == UInt160.Zero)
        {
            error = $"{accountOption} <non-zero UInt160> is required with {signerCommandOption}.";
            return false;
        }
        if (!TryParseHex(
                ArgUtil.Get(args, Option(optionPrefix, "signer-verification-script"), ""),
                Option(optionPrefix, "signer-verification-script"),
                signerCommandOption,
                out var verificationScript,
                out error)
            || !TryParseHex(
                ArgUtil.Get(args, Option(optionPrefix, "signer-placeholder-invocation-script"), ""),
                Option(optionPrefix, "signer-placeholder-invocation-script"),
                signerCommandOption,
                out var placeholderInvocationScript,
                out error))
        {
            return false;
        }

        var timeoutOption = Option(optionPrefix, "signer-timeout-seconds");
        var timeoutValue = ArgUtil.Get(args, timeoutOption, "60");
        if (!uint.TryParse(timeoutValue, NumberStyles.None, CultureInfo.InvariantCulture, out var timeoutSeconds)
            || timeoutSeconds is 0 or > 300)
        {
            error = $"{timeoutOption} must be between 1 and 300.";
            return false;
        }

        try
        {
            signer = new ExternalCommandTransactionSigner(
                new ExternalSignerCommand(
                    executable.FileName,
                    executable.PrefixArguments,
                    TimeSpan.FromSeconds(timeoutSeconds)),
                account,
                verificationScript,
                placeholderInvocationScript,
                externalSignerCommandRunner);
            return true;
        }
        catch (ArgumentException exception)
        {
            error = $"Invalid external signer configuration: {exception.Message}";
            return false;
        }
    }

    private static LocalKeyTransactionSigner ImportLocalKeySigner(string wif)
        => LocalKeyTransactionSigner.FromWif(wif);

    private static bool TryParseHex(
        string value,
        string option,
        string signerCommandOption,
        out byte[] bytes,
        out string error)
    {
        bytes = [];
        error = "";
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) value = value[2..];
        if (string.IsNullOrWhiteSpace(value))
        {
            error = $"{option} <non-empty hex> is required with {signerCommandOption}.";
            return false;
        }
        try
        {
            bytes = Convert.FromHexString(value);
            if (bytes.Length == 0)
            {
                error = $"{option} must not be empty.";
                return false;
            }
            if (bytes.Length > 1024)
            {
                error = $"{option} must not exceed 1024 bytes.";
                return false;
            }
            return true;
        }
        catch (FormatException)
        {
            error = $"{option} must be valid hexadecimal.";
            return false;
        }
    }

    private static string Option(string prefix, string name)
    {
        return string.IsNullOrEmpty(prefix) ? $"--{name}" : $"--{prefix}-{name}";
    }
}
