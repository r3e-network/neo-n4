using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Neo.L2.Batch;
using Neo.L2.State;

namespace Neo.L2.Proving.RiscVZk;

/// <summary>Production client for the out-of-process SP1 batch prover daemon.</summary>
/// <remarks>
/// See doc.md §7.5 and §8. The canonical <see cref="ProofWitnessArtifactV1"/> content hash
/// is the idempotency key. The client accepts only host-verified 356-byte Groth16 results whose
/// manifest, verification key, semantic identifier, public values, filenames, and artifact
/// digests exactly bind the committed witness.
/// </remarks>
public sealed class Sp1BatchProofProver : RiscVProverBase, IProofArtifactRetention
{
    /// <summary>Canonical SP1 Groth16 on-chain proof size.</summary>
    public const int Groth16ProofSize = 356;

    /// <summary>Status byte plus the canonical 32-byte public-input hash.</summary>
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

    private readonly AtomicFileQueueTransport _queue;
    private readonly UInt256 _verificationKeyId;
    private readonly byte[] _verificationKey;

    /// <summary>Create a fail-closed client for one locked batch guest program.</summary>
    public Sp1BatchProofProver(
        string queueDirectory,
        UInt256 verificationKeyId,
        TimeSpan? resultTimeout = null,
        TimeSpan? pollInterval = null,
        long? maximumQueueBytes = null,
        int? maximumRequestCount = null)
    {
        ArgumentNullException.ThrowIfNull(verificationKeyId);
        if (verificationKeyId.Equals(UInt256.Zero))
            throw new ArgumentException(
                "SP1 batch verification key must be non-zero", nameof(verificationKeyId));
        var timeout = resultTimeout ?? DefaultResultTimeout;
        var interval = pollInterval ?? DefaultPollInterval;
        _queue = maximumQueueBytes is null && maximumRequestCount is null
            ? new AtomicFileQueueTransport(queueDirectory, timeout, interval)
            : new AtomicFileQueueTransport(
                queueDirectory,
                timeout,
                interval,
                maximumQueueBytes ?? 16L * 1024 * 1024 * 1024,
                maximumRequestCount ?? 64);
        _verificationKey = verificationKeyId.GetSpan().ToArray();
        _verificationKeyId = new UInt256(_verificationKey);
    }

    /// <summary>Absolute queue directory watched by <c>prove-batch daemon</c>.</summary>
    public string QueueDirectory => _queue.DirectoryPath;

    /// <inheritdoc />
    public override ProofSystem ProofSystem => ProofSystem.Sp1;

    /// <inheritdoc />
    public override UInt256 VerificationKeyId => _verificationKeyId;

    /// <inheritdoc />
    public override UInt256 ExecutionSemanticId => ExecutionSemanticIds.Sp1StatefulNeoVmV1;

    /// <inheritdoc />
    public override bool ProducesCryptographicProof => true;

    /// <inheritdoc />
    public override async ValueTask<ProofResult> ProveAsync(
        ProofRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.PublicInputs);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Kind != ProofType.Zk)
            throw new ArgumentException(
                $"Expected ProofType.Zk, got {request.Kind}", nameof(request));
        if (request.Witness.IsEmpty)
            throw new ArgumentException("SP1 witness must not be empty", nameof(request));

        ProofWitnessArtifactV1 artifact;
        try
        {
            artifact = ProofWitnessArtifactSerializer.Decode(request.Witness.Span);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
        {
            throw new InvalidDataException(
                "SP1 witness is not a canonical ProofWitnessArtifactV1", exception);
        }
        ValidateArtifact(request, artifact);

        var requestId = AtomicFileQueueTransport.Hex(artifact.ContentHash.GetSpan());
        var requestFile = Sp1BatchFileQueueProtocol.FileName(
            requestId, Sp1BatchFileQueueProtocol.RequestSuffix);
        var resultFile = Sp1BatchFileQueueProtocol.FileName(
            requestId, Sp1BatchFileQueueProtocol.ResultManifestSuffix);
        var resultPath = Path.Combine(QueueDirectory, resultFile);
        if (!File.Exists(resultPath))
            await _queue.PublishRequestExactAsync(
                requestFile, request.Witness, cancellationToken).ConfigureAwait(false);
        await _queue.WaitForAsync(resultFile, cancellationToken).ConfigureAwait(false);
        return await ReadAndValidateResultAsync(
            request,
            artifact,
            requestId,
            requestFile,
            resultFile,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask AcknowledgeSettlementAsync(
        UInt256 artifactContentHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifactContentHash);
        if (artifactContentHash.Equals(UInt256.Zero))
            throw new ArgumentException(
                "artifact content hash must be non-zero", nameof(artifactContentHash));
        var requestId = AtomicFileQueueTransport.Hex(artifactContentHash.GetSpan());
        return _queue.PublishExactAsync(
            Sp1BatchFileQueueProtocol.FileName(
                requestId, Sp1BatchFileQueueProtocol.SettlementAcknowledgementSuffix),
            artifactContentHash.GetSpan().ToArray(),
            cancellationToken);
    }

    private void ValidateArtifact(ProofRequest request, ProofWitnessArtifactV1 artifact)
    {
        if (artifact.ProofType != ProofType.Zk
            || artifact.ProofSystem != WitnessProofSystem.Sp1
            || !artifact.ExecutionWitnessAuthenticated
            || !artifact.VerificationKeyId.Equals(_verificationKeyId)
            || !artifact.ExecutionSemanticId.Equals(ExecutionSemanticId))
            throw new InvalidDataException(
                "SP1 witness metadata differs from the locked prover profile");
        if (!BatchSerializer.EncodePublicInputs(artifact.PublicInputs)
            .AsSpan()
            .SequenceEqual(BatchSerializer.EncodePublicInputs(request.PublicInputs)))
            throw new InvalidDataException(
                "SP1 witness public inputs differ from the proof request");
        var canonical = ProofWitnessArtifactSerializer.Encode(artifact);
        if (!canonical.AsSpan().SequenceEqual(request.Witness.Span))
            throw new InvalidDataException("SP1 witness bytes are not canonical");
    }

    private async ValueTask<ProofResult> ReadAndValidateResultAsync(
        ProofRequest request,
        ProofWitnessArtifactV1 artifact,
        string requestId,
        string requestFile,
        string resultFile,
        CancellationToken cancellationToken)
    {
        var manifestBytes = await _queue.ReadBoundedAsync(
            resultFile, MaxManifestBytes, cancellationToken).ConfigureAwait(false);
        Sp1BatchProofResultManifest result;
        try
        {
            result = JsonSerializer.Deserialize<Sp1BatchProofResultManifest>(
                manifestBytes, ManifestJson)
                ?? throw new InvalidDataException(
                    "SP1 batch result manifest deserialized to null");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "SP1 batch result manifest is not canonical JSON", exception);
        }

        var proofFile = Sp1BatchFileQueueProtocol.FileName(
            requestId, Sp1BatchFileQueueProtocol.ProofSuffix);
        var verificationKeyFile = Sp1BatchFileQueueProtocol.FileName(
            requestId, Sp1BatchFileQueueProtocol.VerificationKeySuffix);
        var publicValuesFile = Sp1BatchFileQueueProtocol.FileName(
            requestId, Sp1BatchFileQueueProtocol.PublicValuesSuffix);
        var publicInputHash = StateRootCalculator.HashPublicInputs(request.PublicInputs);
        ValidateManifest(
            result,
            artifact,
            requestId,
            requestFile,
            proofFile,
            verificationKeyFile,
            publicValuesFile,
            publicInputHash,
            request.Witness.Span);

        var proof = await _queue.ReadExactAsync(
            proofFile, Groth16ProofSize, cancellationToken).ConfigureAwait(false);
        var verificationKey = await _queue.ReadExactAsync(
            verificationKeyFile, VerificationKeySize, cancellationToken).ConfigureAwait(false);
        var publicValues = await _queue.ReadExactAsync(
            publicValuesFile, CommittedPublicValuesSize, cancellationToken).ConfigureAwait(false);
        AtomicFileQueueTransport.ValidateSha256(proof, result.ProofSha256, "batch proof");
        AtomicFileQueueTransport.ValidateSha256(
            verificationKey, result.VerificationKeySha256, "batch verification key");
        AtomicFileQueueTransport.ValidateSha256(
            publicValues, result.PublicValuesSha256, "batch public values");
        if (!verificationKey.AsSpan().SequenceEqual(_verificationKey))
            throw new InvalidDataException(
                "SP1 batch result verification key differs from the locked program key");
        if (publicValues[0] != 0
            || !publicValues.AsSpan(1).SequenceEqual(publicInputHash.GetSpan()))
            throw new InvalidDataException(
                "SP1 batch public values must be exactly 0x00 || publicInputHash");

        var payload = new RiscVProofPayload
        {
            ProofSystem = ProofSystem.Sp1,
            VerificationKeyId = _verificationKeyId,
            ProofBytes = proof,
        };
        return new ProofResult
        {
            Proof = payload.Encode(),
            Kind = ProofType.Zk,
            PublicInputHash = publicInputHash,
        };
    }

    private void ValidateManifest(
        Sp1BatchProofResultManifest result,
        ProofWitnessArtifactV1 artifact,
        string requestId,
        string requestFile,
        string proofFile,
        string verificationKeyFile,
        string publicValuesFile,
        UInt256 publicInputHash,
        ReadOnlySpan<byte> requestBytes)
    {
        if (result.SchemaVersion != Sp1BatchFileQueueProtocol.SchemaVersion)
            throw new InvalidDataException("unsupported SP1 batch result manifest schema");
        if (!string.Equals(
            result.Status,
            Sp1BatchFileQueueProtocol.SucceededStatus,
            StringComparison.Ordinal))
            throw new InvalidDataException("SP1 batch result manifest is not successful");
        if (!string.Equals(result.RequestId, requestId, StringComparison.Ordinal)
            || !string.Equals(result.ArtifactContentHash, requestId, StringComparison.Ordinal))
            throw new InvalidDataException("SP1 batch result content hash/id mismatch");
        var requestSha256 = AtomicFileQueueTransport.Hex(SHA256.HashData(requestBytes));
        if (!string.Equals(result.RequestSha256, requestSha256, StringComparison.Ordinal))
            throw new InvalidDataException("SP1 batch result request SHA-256 mismatch");
        if (!string.Equals(
            result.PublicInputHash,
            AtomicFileQueueTransport.Hex(publicInputHash.GetSpan()),
            StringComparison.Ordinal))
            throw new InvalidDataException("SP1 batch result public-input hash mismatch");
        if (result.ProofSystem != (byte)WitnessProofSystem.Sp1)
            throw new InvalidDataException("SP1 batch result proof system is not SP1");
        if (!string.Equals(
            result.ExecutionSemanticId,
            AtomicFileQueueTransport.Hex(ExecutionSemanticId.GetSpan()),
            StringComparison.Ordinal))
            throw new InvalidDataException("SP1 batch result execution semantic mismatch");
        if (!string.Equals(
            result.VerificationKey,
            AtomicFileQueueTransport.Hex(_verificationKey),
            StringComparison.Ordinal)
            || !artifact.VerificationKeyId.Equals(_verificationKeyId))
            throw new InvalidDataException("SP1 batch result verification key mismatch");
        if (!string.Equals(result.RequestFile, requestFile, StringComparison.Ordinal)
            || !string.Equals(result.ProofFile, proofFile, StringComparison.Ordinal)
            || !string.Equals(
                result.VerificationKeyFile, verificationKeyFile, StringComparison.Ordinal)
            || !string.Equals(result.PublicValuesFile, publicValuesFile, StringComparison.Ordinal))
            throw new InvalidDataException(
                "SP1 batch result manifest contains unexpected artifact filenames");

        AtomicFileQueueTransport.ValidateLowerHex(result.RequestId, 32, nameof(result.RequestId));
        AtomicFileQueueTransport.ValidateLowerHex(
            result.RequestSha256, 32, nameof(result.RequestSha256));
        AtomicFileQueueTransport.ValidateLowerHex(
            result.ArtifactContentHash, 32, nameof(result.ArtifactContentHash));
        AtomicFileQueueTransport.ValidateLowerHex(
            result.PublicInputHash, 32, nameof(result.PublicInputHash));
        AtomicFileQueueTransport.ValidateLowerHex(
            result.ExecutionSemanticId, 32, nameof(result.ExecutionSemanticId));
        AtomicFileQueueTransport.ValidateLowerHex(
            result.VerificationKey, VerificationKeySize, nameof(result.VerificationKey));
        AtomicFileQueueTransport.ValidateLowerHex(
            result.ProofSha256, 32, nameof(result.ProofSha256));
        AtomicFileQueueTransport.ValidateLowerHex(
            result.VerificationKeySha256, 32, nameof(result.VerificationKeySha256));
        AtomicFileQueueTransport.ValidateLowerHex(
            result.PublicValuesSha256, 32, nameof(result.PublicValuesSha256));
    }
}
