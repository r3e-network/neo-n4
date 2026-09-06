using Neo.Network.P2P.Payloads;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Neo.L2.Telemetry;

namespace Neo.L2.Settlement.Rpc;

/// <summary>
/// Command-line HSM transaction signer for external hardware security modules.
/// </summary>
/// <remarks>
/// See doc.md §14.2. Production custody systems can integrate custom HSM devices via
/// command-line interface. This implementation uses stdin/stdout JSON protocol for
/// secure communication with external signing devices.
/// 
/// <para>Protocol specification:</para>
/// <list type="bullet">
/// <item>Input: JSON object with fields: "tx" (base64 hex), "network" (int)</item>
/// <item>Output: JSON object with fields: "success", "signature" (base64 hex), "error"</item>
/// <item>Exit codes: 0 = success, 1 = failure, 2 = timeout/error</item>
/// </list>
/// 
/// <para>Example external HSM client script:</para>
/// <code language="bash">
/// #!/bin/bash
/// read json_input
/// echo "{\"result\": \"$(sign_internal \"$json_input\")\"}" | jq .
/// </code>
/// </remarks>
public sealed class HsmCliTransactionSigner : INeoTransactionSigner, IDisposable
{
    private readonly ProcessStartInfo _processStartInfo;
    private readonly UInt160 _account;
    private readonly byte[] _publicKeyBytes;
    private readonly WitnessScope _scope;
    private readonly TimeSpan _commandTimeout;
    private readonly ILogger<HsmCliTransactionSigner>? _logger;
    private readonly IL2Metrics? _metrics;
    private volatile bool _disposed;

    /// <summary>Environment variable for HSM command path.</summary>
    public const string HsmCommandEnvVar = "NEO_N4_HSM_COMMAND";

    /// <summary>Environment variable for HSM output directory.</summary>
    public const string HsmOutputDirEnvVar = "NEO_N4_HSM_OUTPUT_DIR";

    /// <summary>Default command timeout (5 seconds).</summary>
    public static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Construct with specified HSM command.</summary>
    /// <param name="hsmCommand">Path to HSM signing executable.</param>
    /// <param name="args">Optional arguments to pass to HSM command.</param>
    /// <param name="publicKeyBytes">Public key bytes from the HSM.</param>
    /// <param name="scope">Witness scope for the account.</param>
    /// <param name="commandTimeout">Command execution timeout.</param>
    /// <exception cref="ArgumentNullException">Thrown when hsmCommand is null.</exception>
    /// <exception cref="ArgumentException">Thrown when hsmCommand is empty/whitespace.</exception>
    public HsmCliTransactionSigner(
        string hsmCommand,
        string? args = null,
        byte[] publicKeyBytes,
        WitnessScope scope = WitnessScope.CalledByEntry,
        TimeSpan? commandTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(hsmCommand);
        ArgumentException.ThrowIfNullOrWhiteSpace(hsmCommand);
        ArgumentNullException.ThrowIfNull(publicKeyBytes);
        ArgumentException.ThrowIfFalse(publicKeyBytes.Length > 0, nameof(publicKeyBytes));

        _processStartInfo = new ProcessStartInfo
        {
            FileName = hsmCommand,
            Arguments = args ?? string.Empty,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            Timeout = (int)commandTimeout.TotalMilliseconds,
            WorkingDirectory = Environment.CurrentDirectory
        };

        _publicKeyBytes = publicKeyBytes;
        _account = Contract.CreateSignatureContract(_publicKeyBytes).ScriptHash;
        _scope = scope;
        _commandTimeout = commandTimeout ?? DefaultCommandTimeout;
        _logger = null;
        _metrics = null;
    }

    /// <summary>Construct with process factory delegate.</summary>
    /// <param name="processFactory">Function to create new HSM process instances.</param>
    /// <param name="publicKeyBytes">Public key bytes from HSM.</param>
    /// <param name="scope">Witness scope.</param>
    /// <param name="commandTimeout">Command timeout.</param>
    /// <param name="logger">Optional logging instance.</param>
    /// <param name="metrics">Optional metrics instance.</param>
    public HsmCliTransactionSigner(
        Func<Process> processFactory,
        byte[] publicKeyBytes,
        WitnessScope scope = WitnessScope.CalledByEntry,
        TimeSpan? commandTimeout = null,
        ILogger<HsmCliTransactionSigner>? logger = null,
        IL2Metrics? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(processFactory);
        ArgumentNullException.ThrowIfNull(publicKeyBytes);

        _processStartInfo = null!; // Override not needed - use factory directly
        _publicKeyBytes = publicKeyBytes;
        _account = Contract.CreateSignatureContract(_publicKeyBytes).ScriptHash;
        _scope = scope;
        _commandTimeout = commandTimeout ?? DefaultCommandTimeout;
        _logger = logger;
        _metrics = metrics;
    }

    /// <inheritdoc />
    public UInt160 Account => _account;

    /// <inheritdoc />
    public WitnessScope Scope => _scope;

    /// <inheritdoc />
    public Witness CreatePlaceholderWitness()
    {
        ThrowIfDisposed();
        return new Witness
        {
            InvocationScript = Array.Empty<byte>(),
            VerificationScript = Contract.CreateSignatureRedeemScript(_publicKeyBytes),
        };
    }

    /// <inheritdoc />
    public async ValueTask<Witness> SignAsync(
        Transaction transaction,
        uint network,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(transaction);
        cancellationToken.ThrowIfCancellationRequested();

        var startTime = DateTime.UtcNow;
        
        try
        {
            using var process = await SpawnHsmProcessAsync(cancellationToken).ConfigureAwait(false);
            
            if (process == null)
            {
                throw new InvalidOperationException("Failed to spawn HSM signing process");
            }

            var txHash = transaction.Hash.ToByteArray();
            var request = BuildSigningRequest(txHash, network);
            
            await SendRequestAsync(process, request, cancellationToken).ConfigureAwait(false);
            var response = await ReadResponseAsync(process, cancellationToken).ConfigureAwait(false);
            
            if (!response.Success)
            {
                throw new InvalidOperationException(response.Error);
            }

            var witness = new Witness
            {
                InvocationScript = Convert.FromBase64String(response.Signature!),
                VerificationScript = Contract.CreateSignatureRedeemScript(_publicKeyBytes),
            };

            var durationMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
            _metrics?.IncrementCounter(MetricNames.SignSuccess, 1, ("signer_type", "hsm_cli"));
            _metrics?.RecordHistogram(MetricNames.SignLatencyMs, durationMs, ("signer_type", "hsm_cli"));

            _logger?.LogDebug(
                "Successfully signed transaction with HSM CLI in {Duration}ms",
                durationMs);

            return witness;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "HSM CLI signing failed for account {Account}", _account);
            _metrics?.IncrementCounter(MetricNames.SignFailures, 1, ("signer_type", "hsm_cli"), ("error_type", ex.GetType().Name));
            _metrics?.RecordHistogram(MetricNames.SignLatencyMs, (DateTime.UtcNow - startTime).TotalMilliseconds, ("signer_type", "hsm_cli"));

            throw;
        }
    }

    /// <summary>Spawn HSM signing process asynchronously.</summary>
    private Task<Process?> SpawnHsmProcessAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var process = new Process
            {
                StartInfo = _processStartInfo ?? throw new InvalidOperationException("Process factory not configured")
            };

            process.Start();
            return Task.FromResult<Process?>(process);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to spawn HSM process");
            return Task.FromResult<Process?>(null);
        }
    }

    /// <summary>Build JSON signing request.</summary>
    private static string BuildSigningRequest(byte[] txHash, uint network)
    {
        var request = new SigningRequest
        {
            TxHash = Convert.ToBase64String(txHash),
            Network = (int)network,
            Timestamp = DateTime.UtcNow.ToUnixTimeSeconds()
        };

        return JsonSerializer.Serialize(request, GetJsonOptions());
    }

    /// <summary>Send signing request to HSM process.</summary>
    private static async Task SendRequestAsync(Process process, string request, CancellationToken cancellationToken)
    {
        using var writer = new StreamWriter(process.StandardInput!) { AutoFlush = true };
        await writer.WriteAsync(request, cancellationToken).ConfigureAwait(false);
        await writer.WriteLineAsync(string.Empty, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Read signing response from HSM process.</summary>
    private static async Task<SigningResponse> ReadResponseAsync(Process process, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(process.StandardOutput!);
        var responseText = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        
        process.WaitForExit((int)_commandTimeout.TotalMilliseconds);
        var exitCode = process.ExitCode;

        if (exitCode != 0)
        {
            var errorStream = await process.StandardError!.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"HSM process exited with code {exitCode}: {errorStream}");
        }

        try
        {
            return JsonSerializer.Deserialize<SigningResponse>(responseText, GetJsonOptions())
                ?? throw new JsonException("Failed to deserialize HSM response");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid HSM response format: {ex.Message}", ex);
        }
    }

    /// <summary>Get shared JSON serialization options.</summary>
    private static JsonSerializerOptions GetJsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    /// <summary>Dispose resources.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        
        // Process disposal happens automatically via 'using' statement
    }

    /// <summary>HSM signing request format.</summary>
    internal record SigningRequest
    {
        public string TxHash { get; set; } = string.Empty;
        public int Network { get; set; }
        public long Timestamp { get; set; }
    }

    /// <summary>HSM signing response format.</summary>
    internal record SigningResponse
    {
        public bool Success { get; set; }
        public string? Signature { get; set; }
        public string? Error { get; set; }
    }
}
