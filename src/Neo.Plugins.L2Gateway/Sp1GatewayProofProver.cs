using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Neo.Cryptography;

namespace Neo.Plugins.L2Gateway;

/// <summary>Production file-queue client for the dedicated Gateway SP1 recursive prover.</summary>
/// <remarks>
/// See doc.md §4 (Neo Gateway). The client derives an idempotency key from Hash256 over the exact
/// canonical request, atomically publishes request artifacts, and accepts only a host-verified
/// 356-byte SP1 Groth16 result for the locked Gateway guest verification key. All protocol,
/// binding, digest, length, backend, proof-system, and public-value mismatches fail closed.
/// </remarks>
public sealed class Sp1GatewayProofProver : IGatewayProofProver
{
    /// <summary>SP1 proof-system discriminator.</summary>
    public const byte Sp1ProofSystem = GatewaySp1FileQueueProtocol.Sp1ProofSystem;

    /// <summary>Dedicated recursive Gateway aggregation backend.</summary>
    public const byte RecursiveAggregationBackendId =
        GatewaySp1FileQueueProtocol.RecursiveAggregationBackendId;

    /// <summary>Canonical SP1 Groth16 on-chain proof size.</summary>
    public const int Groth16ProofSize = 356;

    /// <summary>Status byte plus 32-byte binding hash committed by the Gateway guest.</summary>
    public const int CommittedPublicValuesSize = 33;

    private const int VerificationKeySize = 32;
    private const int MaxManifestBytes = 16 * 1024;
    private static readonly TimeSpan DefaultResultTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly string _queueDirectory;
    private readonly byte[] _gatewayVerificationKey;
    private readonly TimeSpan _resultTimeout;
    private readonly TimeSpan _pollInterval;

    /// <inheritdoc />
    public byte ProofSystem => Sp1ProofSystem;

    /// <inheritdoc />
    public byte AggregationBackendId => RecursiveAggregationBackendId;

    /// <summary>Absolute queue directory used for request and result artifacts.</summary>
    public string QueueDirectory => _queueDirectory;

    /// <summary>Construct a fail-closed Gateway SP1 daemon client.</summary>
    /// <param name="queueDirectory">Dedicated shared directory watched by the Gateway daemon.</param>
    /// <param name="gatewayVerificationKey">Locked raw 32-byte Gateway guest program key.</param>
    /// <param name="resultTimeout">Maximum time to wait for the result readiness manifest.</param>
    /// <param name="pollInterval">Polling interval for the result readiness manifest.</param>
    public Sp1GatewayProofProver(
        string queueDirectory,
        UInt256 gatewayVerificationKey,
        TimeSpan? resultTimeout = null,
        TimeSpan? pollInterval = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueDirectory);
        ArgumentNullException.ThrowIfNull(gatewayVerificationKey);
        if (gatewayVerificationKey.Equals(UInt256.Zero))
            throw new ArgumentException("Gateway verification key must be non-zero", nameof(gatewayVerificationKey));

        _resultTimeout = resultTimeout ?? DefaultResultTimeout;
        _pollInterval = pollInterval ?? DefaultPollInterval;
        if (_resultTimeout <= TimeSpan.Zero || _resultTimeout > TimeSpan.FromHours(24))
            throw new ArgumentOutOfRangeException(
                nameof(resultTimeout),
                "resultTimeout must be in (0, 24 hours]");
        if (_pollInterval <= TimeSpan.Zero || _pollInterval > _resultTimeout)
            throw new ArgumentOutOfRangeException(
                nameof(pollInterval),
                "pollInterval must be positive and no greater than resultTimeout");

        _queueDirectory = Path.GetFullPath(queueDirectory);
        Directory.CreateDirectory(_queueDirectory);
        RejectReparsePoint(_queueDirectory, "queue directory");
        _gatewayVerificationKey = gatewayVerificationKey.GetSpan().ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<ReadOnlyMemory<byte>> ProveAsync(
        GatewayProofBinding binding,
        AggregatedCommitment commitment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(commitment);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateStatement(binding, commitment);

        var requestBytes = GatewaySp1RequestSerializer.Encode(binding, commitment.Constituents);
        var requestHash = Crypto.Hash256(requestBytes);
        var requestId = Hex(requestHash);
        var bindingHash = GatewayProofBindingSerializer.ComputeHash(binding).GetSpan().ToArray();
        var requestFile = GatewaySp1FileQueueProtocol.FileName(
            requestId,
            GatewaySp1FileQueueProtocol.RequestPayloadSuffix);
        var requestManifest = new GatewaySp1ProofRequestManifest
        {
            SchemaVersion = GatewaySp1FileQueueProtocol.SchemaVersion,
            RequestId = requestId,
            RequestHash = requestId,
            BindingHash = Hex(bindingHash),
            ProofSystem = ProofSystem,
            AggregationBackendId = AggregationBackendId,
            VerificationKey = Hex(_gatewayVerificationKey),
            RequestFile = requestFile,
        };
        var requestManifestBytes = JsonSerializer.SerializeToUtf8Bytes(requestManifest, ManifestJson);

        await PublishExactFileAsync(
            Path.Combine(_queueDirectory, requestFile),
            requestBytes,
            cancellationToken).ConfigureAwait(false);
        await PublishExactFileAsync(
            Path.Combine(
                _queueDirectory,
                GatewaySp1FileQueueProtocol.FileName(
                    requestId,
                    GatewaySp1FileQueueProtocol.RequestManifestSuffix)),
            requestManifestBytes,
            cancellationToken).ConfigureAwait(false);

        var resultPath = Path.Combine(
            _queueDirectory,
            GatewaySp1FileQueueProtocol.FileName(
                requestId,
                GatewaySp1FileQueueProtocol.ResultManifestSuffix));
        await WaitForResultAsync(resultPath, cancellationToken).ConfigureAwait(false);
        return await ReadAndValidateResultAsync(
            resultPath,
            requestManifest,
            bindingHash,
            cancellationToken).ConfigureAwait(false);
    }

    private void ValidateStatement(
        GatewayProofBinding binding,
        AggregatedCommitment commitment)
    {
        GatewayProofBindingSerializer.Validate(binding);
        ArgumentNullException.ThrowIfNull(commitment.Constituents);
        if (binding.ProofSystem != ProofSystem)
            throw new ArgumentException("Gateway binding must use SP1 proof system 1", nameof(binding));
        if (binding.AggregationBackendId != AggregationBackendId
            || commitment.BackendId != AggregationBackendId)
        {
            throw new ArgumentException(
                "Gateway binding and aggregate must use recursive backend 0xC2",
                nameof(binding));
        }
        if (!binding.VerificationKeyId.GetSpan().SequenceEqual(_gatewayVerificationKey))
            throw new ArgumentException("Gateway binding verification key is not locked by this prover", nameof(binding));

        var expected = GatewayProofBindingSerializer.Create(
            binding.MessageRouter,
            binding.ReplayDomain,
            binding.BatchEpoch,
            commitment,
            ProofSystem,
            binding.VerificationKeyId);
        if (!GatewayProofBindingSerializer.Encode(expected)
            .AsSpan()
            .SequenceEqual(GatewayProofBindingSerializer.Encode(binding)))
        {
            throw new ArgumentException("Gateway binding does not exactly match aggregate", nameof(binding));
        }
    }

    private async ValueTask<ReadOnlyMemory<byte>> ReadAndValidateResultAsync(
        string resultPath,
        GatewaySp1ProofRequestManifest request,
        byte[] bindingHash,
        CancellationToken cancellationToken)
    {
        var resultBytes = await ReadBoundedFileAsync(
            resultPath,
            MaxManifestBytes,
            cancellationToken).ConfigureAwait(false);
        GatewaySp1ProofResultManifest result;
        try
        {
            result = JsonSerializer.Deserialize<GatewaySp1ProofResultManifest>(
                resultBytes,
                ManifestJson)
                ?? throw new InvalidDataException("Gateway result manifest deserialized to null");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Gateway result manifest is not canonical JSON", exception);
        }

        var proofFile = GatewaySp1FileQueueProtocol.FileName(
            request.RequestId,
            GatewaySp1FileQueueProtocol.ProofSuffix);
        var verificationKeyFile = GatewaySp1FileQueueProtocol.FileName(
            request.RequestId,
            GatewaySp1FileQueueProtocol.VerificationKeySuffix);
        var publicValuesFile = GatewaySp1FileQueueProtocol.FileName(
            request.RequestId,
            GatewaySp1FileQueueProtocol.PublicValuesSuffix);
        ValidateResultManifest(
            result,
            request,
            proofFile,
            verificationKeyFile,
            publicValuesFile);

        var proof = await ReadExactFileAsync(
            Path.Combine(_queueDirectory, proofFile),
            Groth16ProofSize,
            cancellationToken).ConfigureAwait(false);
        var verificationKey = await ReadExactFileAsync(
            Path.Combine(_queueDirectory, verificationKeyFile),
            VerificationKeySize,
            cancellationToken).ConfigureAwait(false);
        var publicValues = await ReadExactFileAsync(
            Path.Combine(_queueDirectory, publicValuesFile),
            CommittedPublicValuesSize,
            cancellationToken).ConfigureAwait(false);

        ValidateSha256(proof, result.ProofSha256, "proof");
        ValidateSha256(verificationKey, result.VerificationKeySha256, "verification key");
        ValidateSha256(publicValues, result.PublicValuesSha256, "public values");
        if (!verificationKey.AsSpan().SequenceEqual(_gatewayVerificationKey)
            || !string.Equals(result.VerificationKey, Hex(_gatewayVerificationKey), StringComparison.Ordinal))
        {
            throw new InvalidDataException("Gateway result verification key does not match the locked program key");
        }
        if (publicValues[0] != 0
            || !publicValues.AsSpan(1).SequenceEqual(bindingHash))
        {
            throw new InvalidDataException(
                "Gateway public values must be exactly 0x00 || Hash256(binding170)");
        }
        return proof;
    }

    private void ValidateResultManifest(
        GatewaySp1ProofResultManifest result,
        GatewaySp1ProofRequestManifest request,
        string proofFile,
        string verificationKeyFile,
        string publicValuesFile)
    {
        if (result.SchemaVersion != GatewaySp1FileQueueProtocol.SchemaVersion)
            throw new InvalidDataException("unsupported Gateway result manifest schema");
        if (!string.Equals(
            result.Status,
            GatewaySp1FileQueueProtocol.SucceededStatus,
            StringComparison.Ordinal))
        {
            throw new InvalidDataException("Gateway result manifest is not successful");
        }
        if (!string.Equals(result.RequestId, request.RequestId, StringComparison.Ordinal)
            || !string.Equals(result.RequestHash, request.RequestHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Gateway result request hash/id mismatch");
        }
        if (!string.Equals(result.BindingHash, request.BindingHash, StringComparison.Ordinal))
            throw new InvalidDataException("Gateway result binding hash mismatch");
        if (result.ProofSystem != ProofSystem)
            throw new InvalidDataException("Gateway result proof system is not SP1");
        if (result.AggregationBackendId != AggregationBackendId)
            throw new InvalidDataException("Gateway result aggregation backend is not recursive 0xC2");
        if (!string.Equals(result.RequestFile, request.RequestFile, StringComparison.Ordinal)
            || !string.Equals(result.ProofFile, proofFile, StringComparison.Ordinal)
            || !string.Equals(result.VerificationKeyFile, verificationKeyFile, StringComparison.Ordinal)
            || !string.Equals(result.PublicValuesFile, publicValuesFile, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Gateway result manifest contains unexpected artifact filenames");
        }
        ValidateLowerHex(result.RequestId, 32, nameof(result.RequestId));
        ValidateLowerHex(result.RequestHash, 32, nameof(result.RequestHash));
        ValidateLowerHex(result.BindingHash, 32, nameof(result.BindingHash));
        ValidateLowerHex(result.VerificationKey, VerificationKeySize, nameof(result.VerificationKey));
        ValidateLowerHex(result.ProofSha256, 32, nameof(result.ProofSha256));
        ValidateLowerHex(result.VerificationKeySha256, 32, nameof(result.VerificationKeySha256));
        ValidateLowerHex(result.PublicValuesSha256, 32, nameof(result.PublicValuesSha256));
    }

    private async ValueTask WaitForResultAsync(
        string resultPath,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!File.Exists(resultPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stopwatch.Elapsed >= _resultTimeout)
            {
                throw new TimeoutException(
                    $"Gateway SP1 daemon did not publish {Path.GetFileName(resultPath)} within {_resultTimeout}");
            }
            var remaining = _resultTimeout - stopwatch.Elapsed;
            await Task.Delay(
                remaining < _pollInterval ? remaining : _pollInterval,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async ValueTask PublishExactFileAsync(
        string path,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            await RequireExactExistingFileAsync(path, bytes, cancellationToken).ConfigureAwait(false);
            return;
        }

        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            try
            {
                File.Move(temporaryPath, path);
            }
            catch (IOException) when (File.Exists(path))
            {
                await RequireExactExistingFileAsync(path, bytes, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static async ValueTask RequireExactExistingFileAsync(
        string path,
        ReadOnlyMemory<byte> expected,
        CancellationToken cancellationToken)
    {
        var existing = await ReadBoundedFileAsync(
            path,
            expected.Length,
            cancellationToken).ConfigureAwait(false);
        if (!existing.AsSpan().SequenceEqual(expected.Span))
            throw new InvalidDataException($"existing idempotent artifact differs: {Path.GetFileName(path)}");
    }

    private static async ValueTask<byte[]> ReadExactFileAsync(
        string path,
        int exactLength,
        CancellationToken cancellationToken)
    {
        var bytes = await ReadBoundedFileAsync(path, exactLength, cancellationToken).ConfigureAwait(false);
        if (bytes.Length != exactLength)
        {
            throw new InvalidDataException(
                $"{Path.GetFileName(path)} must be exactly {exactLength} bytes, got {bytes.Length}");
        }
        return bytes;
    }

    private static async ValueTask<byte[]> ReadBoundedFileAsync(
        string path,
        int maxLength,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            throw new InvalidDataException($"Gateway result artifact is missing: {Path.GetFileName(path)}");
        RejectReparsePoint(path, "Gateway queue artifact");
        var length = new FileInfo(path).Length;
        if (length < 0 || length > maxLength)
        {
            throw new InvalidDataException(
                $"{Path.GetFileName(path)} exceeds the {maxLength}-byte protocol limit");
        }
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        if (bytes.Length > maxLength)
            throw new InvalidDataException($"{Path.GetFileName(path)} changed while being read");
        return bytes;
    }

    private static void ValidateSha256(
        ReadOnlySpan<byte> bytes,
        string expected,
        string artifact)
    {
        var actual = Hex(SHA256.HashData(bytes));
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidDataException($"Gateway {artifact} SHA-256 mismatch");
    }

    private static void ValidateLowerHex(string value, int byteLength, string field)
    {
        if (value is null || value.Length != byteLength * 2)
            throw new InvalidDataException($"{field} must encode exactly {byteLength} bytes");
        foreach (var character in value)
        {
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
                throw new InvalidDataException($"{field} must be lowercase hexadecimal");
        }
    }

    private static void RejectReparsePoint(string path, string description)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"{description} must not be a symbolic link or reparse point");
    }

    private static string Hex(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(bytes).ToLowerInvariant();
}
