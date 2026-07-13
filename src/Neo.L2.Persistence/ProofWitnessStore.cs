using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Neo.Cryptography;
using Neo.L2.Batch;

namespace Neo.L2.Persistence;

/// <summary>Durable proof-generation state associated with one witness artifact.</summary>
/// <remarks>
/// See doc.md §7.5 and §8. The manifest is written only after its referenced witness artifact
/// has been committed and validated.
/// </remarks>
public sealed record ProofResultManifest
{
    /// <summary>Wire-format version.</summary>
    public const ushort Version = 1;

    /// <summary>L2 chain identifier of the committed artifact.</summary>
    public required uint ChainId { get; init; }

    /// <summary>Batch sequence number of the committed artifact.</summary>
    public required ulong BatchNumber { get; init; }

    /// <summary>Content hash of the immutable witness artifact.</summary>
    public required UInt256 ArtifactContentHash { get; init; }

    /// <summary>Hash256 of the artifact's canonical 332-byte public inputs.</summary>
    public required UInt256 PublicInputHash { get; init; }

    /// <summary>Verification-key identifier used to generate the proof.</summary>
    public required UInt256 VerificationKeyId { get; init; }

    /// <summary>Proof backend that generated the proof.</summary>
    public required WitnessProofSystem ProofSystem { get; init; }

    /// <summary>Terminal proof bytes.</summary>
    public required ReadOnlyMemory<byte> Proof { get; init; }

    /// <summary>Public values emitted by the zkVM, when the backend exposes them separately.</summary>
    public required ReadOnlyMemory<byte> PublicValues { get; init; }

    /// <summary>L1 submission transaction, or <c>null</c> until submitted.</summary>
    public UInt256? L1TransactionHash { get; init; }

    /// <inheritdoc />
    public bool Equals(ProofResultManifest? other)
        => other is not null
            && ChainId == other.ChainId
            && BatchNumber == other.BatchNumber
            && ArtifactContentHash.Equals(other.ArtifactContentHash)
            && PublicInputHash.Equals(other.PublicInputHash)
            && VerificationKeyId.Equals(other.VerificationKeyId)
            && ProofSystem == other.ProofSystem
            && Proof.Span.SequenceEqual(other.Proof.Span)
            && PublicValues.Span.SequenceEqual(other.PublicValues.Span)
            && Equals(L1TransactionHash, other.L1TransactionHash);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ChainId);
        hash.Add(BatchNumber);
        hash.Add(ArtifactContentHash);
        hash.Add(PublicInputHash);
        hash.Add(VerificationKeyId);
        hash.Add(ProofSystem);
        hash.AddBytes(Proof.Span);
        hash.AddBytes(PublicValues.Span);
        hash.Add(L1TransactionHash);
        return hash.ToHashCode();
    }
}

/// <summary>Persistence boundary for committed proof witnesses and proof results.</summary>
/// <remarks>
/// See doc.md §7.5 and §8. Implementations must make artifact identity immutable: the same
/// chain/batch/content hash is idempotent, while any conflicting content fails closed.
/// </remarks>
public interface IProofWitnessStore : IDisposable
{
    /// <summary>Atomically commit an immutable witness artifact.</summary>
    ValueTask CommitAsync(
        ProofWitnessArtifactV1 artifact,
        CancellationToken cancellationToken = default);

    /// <summary>Get a committed artifact by chain and batch number.</summary>
    ValueTask<ProofWitnessArtifactV1?> GetAsync(
        uint chainId,
        ulong batchNumber,
        CancellationToken cancellationToken = default);

    /// <summary>Enumerate committed artifacts for a chain in ascending batch-number order.</summary>
    IAsyncEnumerable<ProofWitnessArtifactV1> EnumerateCommittedAsync(
        uint chainId,
        CancellationToken cancellationToken = default);

    /// <summary>Atomically store an immutable proof result for a committed artifact.</summary>
    ValueTask PutProofAsync(
        ProofResultManifest manifest,
        CancellationToken cancellationToken = default);

    /// <summary>Get the proof result associated with an artifact content hash.</summary>
    ValueTask<ProofResultManifest?> GetProofAsync(
        UInt256 artifactContentHash,
        CancellationToken cancellationToken = default);

    /// <summary>Atomically bind a proof result to its L1 submission transaction.</summary>
    ValueTask MarkSubmittedAsync(
        UInt256 artifactContentHash,
        UInt256 l1TransactionHash,
        CancellationToken cancellationToken = default);
}

/// <summary><see cref="IL2KeyValueStore"/>-backed proof witness store.</summary>
/// <remarks>
/// See doc.md §7.5 and §8. Artifact keys are exactly
/// <c>"PWIT" | chainId:u32 LE | batchNumber:u64 LE</c>. Each value is one complete canonical
/// artifact, so a RocksDB Put is the crash-recovery commit point.
/// </remarks>
public sealed class KeyValueProofWitnessStore : IProofWitnessStore
{
    private static ReadOnlySpan<byte> ArtifactPrefix => "PWIT"u8;
    private static ReadOnlySpan<byte> ProofPrefix => "PWRF"u8;

    private readonly IL2KeyValueStore _store;
    private readonly bool _ownsStore;
    private bool _disposed;

    /// <summary>Create a witness store over a shared key-value backend.</summary>
    public KeyValueProofWitnessStore(IL2KeyValueStore store, bool ownsStore = false)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _ownsStore = ownsStore;
    }

    /// <inheritdoc />
    public ValueTask CommitAsync(
        ProofWitnessArtifactV1 artifact,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(artifact);
        ThrowIfDisposed();
        var key = ArtifactKey(artifact.ChainId, artifact.BatchNumber);
        var encoded = ProofWitnessArtifactSerializer.Encode(artifact);

        for (var attempt = 0; attempt < 8; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_store.TryPut(key, encoded)) return ValueTask.CompletedTask;
            var currentBytes = _store.Get(key);
            if (currentBytes is null) continue;
            var current = ProofWitnessArtifactSerializer.Decode(currentBytes);
            if (current.ContentHash.Equals(artifact.ContentHash))
                return ValueTask.CompletedTask;
            throw new InvalidOperationException(
                $"Conflicting proof witness for chain {artifact.ChainId}, batch {artifact.BatchNumber}: " +
                $"stored {HashHex(current.ContentHash)}, attempted {HashHex(artifact.ContentHash)}");
        }

        throw new InvalidOperationException(
            "Proof witness key changed repeatedly during atomic commit");
    }

    /// <inheritdoc />
    public ValueTask<ProofWitnessArtifactV1?> GetAsync(
        uint chainId,
        ulong batchNumber,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        var bytes = _store.Get(ArtifactKey(chainId, batchNumber));
        if (bytes is null) return new ValueTask<ProofWitnessArtifactV1?>((ProofWitnessArtifactV1?)null);
        var artifact = ProofWitnessArtifactSerializer.Decode(bytes);
        if (artifact.ChainId != chainId || artifact.BatchNumber != batchNumber)
            throw new InvalidDataException(
                "Proof witness value identity does not match its storage key");
        return new ValueTask<ProofWitnessArtifactV1?>(artifact);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ProofWitnessArtifactV1> EnumerateCommittedAsync(
        uint chainId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var prefix = ArtifactChainPrefix(chainId);
        var artifacts = new List<ProofWitnessArtifactV1>();
        foreach (var (key, value) in _store.EnumeratePrefix(prefix))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (key.Length != ArtifactPrefix.Length + 4 + 8)
                throw new InvalidDataException("Malformed proof witness storage key length");
            var keyBatchNumber = BinaryPrimitives.ReadUInt64LittleEndian(key.AsSpan(8, 8));
            var artifact = ProofWitnessArtifactSerializer.Decode(value);
            if (artifact.ChainId != chainId || artifact.BatchNumber != keyBatchNumber)
                throw new InvalidDataException(
                    "Proof witness value identity does not match its storage key");
            artifacts.Add(artifact);
        }

        foreach (var artifact in artifacts.OrderBy(static artifact => artifact.BatchNumber))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return artifact;
            await Task.Yield();
        }
    }

    /// <inheritdoc />
    public ValueTask PutProofAsync(
        ProofResultManifest manifest,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(manifest);
        ThrowIfDisposed();
        var committedBytes = _store.Get(ArtifactKey(manifest.ChainId, manifest.BatchNumber))
            ?? throw new InvalidOperationException(
                $"Cannot persist proof for unknown chain {manifest.ChainId}, batch {manifest.BatchNumber}");
        var committedArtifact = ProofWitnessArtifactSerializer.Decode(committedBytes);
        ValidateManifestBinding(manifest, committedArtifact);

        var key = ProofKey(manifest.ArtifactContentHash);
        var encoded = ProofResultManifestSerializer.Encode(manifest);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_store.TryPut(key, encoded)) return ValueTask.CompletedTask;
            var currentBytes = _store.Get(key);
            if (currentBytes is null) continue;
            if (currentBytes.AsSpan().SequenceEqual(encoded)) return ValueTask.CompletedTask;
            throw new InvalidOperationException(
                $"Conflicting proof result for artifact {HashHex(manifest.ArtifactContentHash)}");
        }

        throw new InvalidOperationException("Proof result key changed repeatedly during atomic commit");
    }

    /// <inheritdoc />
    public ValueTask<ProofResultManifest?> GetProofAsync(
        UInt256 artifactContentHash,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(artifactContentHash);
        ThrowIfDisposed();
        var bytes = _store.Get(ProofKey(artifactContentHash));
        if (bytes is null) return new ValueTask<ProofResultManifest?>((ProofResultManifest?)null);
        var manifest = ProofResultManifestSerializer.Decode(bytes);
        if (!manifest.ArtifactContentHash.Equals(artifactContentHash))
            throw new InvalidDataException("Proof manifest identity does not match its storage key");
        return new ValueTask<ProofResultManifest?>(manifest);
    }

    /// <inheritdoc />
    public ValueTask MarkSubmittedAsync(
        UInt256 artifactContentHash,
        UInt256 l1TransactionHash,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(artifactContentHash);
        ArgumentNullException.ThrowIfNull(l1TransactionHash);
        if (l1TransactionHash.Equals(UInt256.Zero))
            throw new ArgumentException("L1 transaction hash must be non-zero", nameof(l1TransactionHash));
        ThrowIfDisposed();
        var key = ProofKey(artifactContentHash);

        for (var attempt = 0; attempt < 32; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentBytes = _store.Get(key)
                ?? throw new InvalidOperationException(
                    $"No proof result exists for artifact {HashHex(artifactContentHash)}");
            var current = ProofResultManifestSerializer.Decode(currentBytes);
            if (!current.ArtifactContentHash.Equals(artifactContentHash))
                throw new InvalidDataException("Proof manifest identity does not match its storage key");
            if (current.L1TransactionHash is not null)
            {
                if (current.L1TransactionHash.Equals(l1TransactionHash))
                    return ValueTask.CompletedTask;
                throw new InvalidOperationException(
                    $"Artifact {HashHex(artifactContentHash)} is already bound to a different L1 transaction");
            }

            var updated = current with { L1TransactionHash = l1TransactionHash };
            var updatedBytes = ProofResultManifestSerializer.Encode(updated);
            if (_store.CompareExchange(key, currentBytes, updatedBytes))
                return ValueTask.CompletedTask;
        }

        throw new InvalidOperationException(
            "Proof result changed repeatedly while marking it submitted");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsStore) _store.Dispose();
    }

    private static void ValidateManifestBinding(
        ProofResultManifest manifest,
        ProofWitnessArtifactV1 artifact)
    {
        if (manifest.ChainId != artifact.ChainId || manifest.BatchNumber != artifact.BatchNumber)
            throw new ArgumentException(
                "Proof manifest identity does not match the committed artifact",
                nameof(manifest));
        if (!manifest.ArtifactContentHash.Equals(artifact.ContentHash))
            throw new ArgumentException(
                "Proof manifest content hash does not match the committed artifact",
                nameof(manifest));
        var publicInputHash = new UInt256(
            Crypto.Hash256(BatchSerializer.EncodePublicInputs(artifact.PublicInputs)));
        if (!manifest.PublicInputHash.Equals(publicInputHash))
            throw new ArgumentException(
                "Proof manifest public-input hash does not match the committed artifact",
                nameof(manifest));
        if (!manifest.VerificationKeyId.Equals(artifact.VerificationKeyId))
            throw new ArgumentException(
                "Proof manifest verification key does not match the committed artifact",
                nameof(manifest));
        if (manifest.ProofSystem != artifact.ProofSystem)
            throw new ArgumentException(
                "Proof manifest proof system does not match the committed artifact",
                nameof(manifest));
    }

    private static byte[] ArtifactKey(uint chainId, ulong batchNumber)
    {
        var key = new byte[ArtifactPrefix.Length + 4 + 8];
        ArtifactPrefix.CopyTo(key);
        BinaryPrimitives.WriteUInt32LittleEndian(key.AsSpan(4, 4), chainId);
        BinaryPrimitives.WriteUInt64LittleEndian(key.AsSpan(8, 8), batchNumber);
        return key;
    }

    private static byte[] ArtifactChainPrefix(uint chainId)
    {
        var key = new byte[ArtifactPrefix.Length + 4];
        ArtifactPrefix.CopyTo(key);
        BinaryPrimitives.WriteUInt32LittleEndian(key.AsSpan(4, 4), chainId);
        return key;
    }

    private static byte[] ProofKey(UInt256 artifactContentHash)
    {
        var key = new byte[ProofPrefix.Length + UInt256.Length];
        ProofPrefix.CopyTo(key);
        artifactContentHash.GetSpan().CopyTo(key.AsSpan(ProofPrefix.Length));
        return key;
    }

    private static string HashHex(UInt256 hash)
        => Convert.ToHexString(hash.GetSpan()).ToLowerInvariant();

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(KeyValueProofWitnessStore));
    }
}

/// <summary>Canonical serializer for <see cref="ProofResultManifest"/>.</summary>
/// <remarks>See doc.md §7.5 and §8.</remarks>
public static class ProofResultManifestSerializer
{
    private static ReadOnlySpan<byte> Magic => "NEO4PRMF"u8;
    private static ReadOnlySpan<byte> ContentHashDomain => "neo-n4/proof-result/v1\0"u8;

    private const ushort SubmittedFlag = 1;
    private const ushort KnownFlags = SubmittedFlag;
    private const int FixedSize = 132;
    private const int ContentHashSize = 32;

    /// <summary>Maximum terminal proof size (16 MiB).</summary>
    public const int MaxProofBytes = 16 * 1024 * 1024;

    /// <summary>Maximum public-values size (16 MiB).</summary>
    public const int MaxPublicValuesBytes = 16 * 1024 * 1024;

    /// <summary>Encode a proof result manifest.</summary>
    public static byte[] Encode(ProofResultManifest manifest)
    {
        Validate(manifest);
        var submitted = manifest.L1TransactionHash is not null;
        var bodySize = checked(
            FixedSize
            + manifest.Proof.Length
            + manifest.PublicValues.Length
            + (submitted ? UInt256.Length : 0));
        var writer = new ManifestWireWriter(bodySize);
        writer.WriteBytes(Magic);
        writer.WriteUInt16(ProofResultManifest.Version);
        writer.WriteUInt16(submitted ? SubmittedFlag : (ushort)0);
        writer.WriteByte((byte)manifest.ProofSystem);
        writer.WriteBytes(stackalloc byte[3]);
        writer.WriteUInt32(manifest.ChainId);
        writer.WriteUInt64(manifest.BatchNumber);
        writer.WriteUInt256(manifest.ArtifactContentHash);
        writer.WriteUInt256(manifest.PublicInputHash);
        writer.WriteUInt256(manifest.VerificationKeyId);
        writer.WriteLengthPrefixedBytes(manifest.Proof.Span);
        writer.WriteLengthPrefixedBytes(manifest.PublicValues.Span);
        if (manifest.L1TransactionHash is not null)
            writer.WriteUInt256(manifest.L1TransactionHash);
        if (writer.WrittenCount != bodySize)
            throw new InvalidOperationException(
                $"Proof result manifest length mismatch: wrote {writer.WrittenCount}, expected {bodySize}");
        var body = writer.ToArray();
        var contentHash = ComputeContentHash(body);
        var encoded = new byte[checked(body.Length + ContentHashSize)];
        body.CopyTo(encoded, 0);
        contentHash.GetSpan().CopyTo(encoded.AsSpan(body.Length));
        return encoded;
    }

    /// <summary>Decode a proof result manifest and reject malformed bytes.</summary>
    public static ProofResultManifest Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < FixedSize + ContentHashSize)
            throw new InvalidDataException("Proof result manifest is truncated");
        var reader = new ManifestWireReader(data);
        reader.RequireMagic(Magic);
        var version = reader.ReadUInt16("version");
        if (version != ProofResultManifest.Version)
            throw new InvalidDataException($"Unsupported proof result version {version}");
        var flags = reader.ReadUInt16("flags");
        if ((flags & ~KnownFlags) != 0)
            throw new InvalidDataException($"Unknown proof result flags 0x{flags:x4}");
        var proofSystemByte = reader.ReadByte("proof system");
        if (!Enum.IsDefined((WitnessProofSystem)proofSystemByte))
            throw new InvalidDataException($"Unknown proof system byte {proofSystemByte}");
        var reserved = reader.ReadBytes(3, "reserved bytes");
        if (reserved[0] != 0 || reserved[1] != 0 || reserved[2] != 0)
            throw new InvalidDataException("Proof result reserved bytes must be zero");
        var chainId = reader.ReadUInt32("chain id");
        var batchNumber = reader.ReadUInt64("batch number");
        var artifactHash = reader.ReadUInt256("artifact hash");
        var publicInputHash = reader.ReadUInt256("public input hash");
        var verificationKeyId = reader.ReadUInt256("verification key id");
        var proof = reader.ReadLengthPrefixedBytes(MaxProofBytes, "proof");
        var publicValues = reader.ReadLengthPrefixedBytes(MaxPublicValuesBytes, "public values");
        var l1TransactionHash = (flags & SubmittedFlag) != 0
            ? reader.ReadUInt256("L1 transaction hash")
            : null;
        var contentHash = reader.ReadUInt256("content hash");
        reader.EnsureEnd();
        var expectedContentHash = ComputeContentHash(data[..^ContentHashSize]);
        if (!expectedContentHash.Equals(contentHash))
            throw new InvalidDataException("Proof result manifest content hash mismatch");
        var manifest = new ProofResultManifest
        {
            ChainId = chainId,
            BatchNumber = batchNumber,
            ArtifactContentHash = artifactHash,
            PublicInputHash = publicInputHash,
            VerificationKeyId = verificationKeyId,
            ProofSystem = (WitnessProofSystem)proofSystemByte,
            Proof = proof,
            PublicValues = publicValues,
            L1TransactionHash = l1TransactionHash,
        };
        try
        {
            Validate(manifest);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException("Proof result manifest fields are inconsistent", ex);
        }
        return manifest;
    }

    private static UInt256 ComputeContentHash(ReadOnlySpan<byte> body)
    {
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha256.AppendData(ContentHashDomain);
        sha256.AppendData(body);
        var firstHash = sha256.GetHashAndReset();
        return new UInt256(SHA256.HashData(firstHash));
    }

    private static void Validate(ProofResultManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(manifest.ArtifactContentHash);
        ArgumentNullException.ThrowIfNull(manifest.PublicInputHash);
        ArgumentNullException.ThrowIfNull(manifest.VerificationKeyId);
        if (!Enum.IsDefined(manifest.ProofSystem))
            throw new ArgumentException("Unknown proof system", nameof(manifest));
        if (manifest.Proof.Length == 0)
            throw new ArgumentException("Proof must be non-empty", nameof(manifest));
        if (manifest.Proof.Length > MaxProofBytes)
            throw new ArgumentException($"Proof exceeds {MaxProofBytes} bytes", nameof(manifest));
        if (manifest.PublicValues.Length > MaxPublicValuesBytes)
            throw new ArgumentException(
                $"PublicValues exceeds {MaxPublicValuesBytes} bytes", nameof(manifest));
        if (manifest.L1TransactionHash is not null
            && manifest.L1TransactionHash.Equals(UInt256.Zero))
            throw new ArgumentException("L1 transaction hash must be non-zero", nameof(manifest));
    }
}

internal sealed class ManifestWireWriter
{
    private readonly ArrayBufferWriter<byte> _writer;

    public ManifestWireWriter(int capacity) => _writer = new ArrayBufferWriter<byte>(capacity);

    public int WrittenCount => _writer.WrittenCount;

    public void WriteByte(byte value)
    {
        _writer.GetSpan(1)[0] = value;
        _writer.Advance(1);
    }

    public void WriteUInt16(ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(_writer.GetSpan(2), value);
        _writer.Advance(2);
    }

    public void WriteUInt32(uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(_writer.GetSpan(4), value);
        _writer.Advance(4);
    }

    public void WriteUInt64(ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(_writer.GetSpan(8), value);
        _writer.Advance(8);
    }

    public void WriteUInt256(UInt256 value) => WriteBytes(value.GetSpan());

    public void WriteLengthPrefixedBytes(ReadOnlySpan<byte> value)
    {
        WriteUInt32(checked((uint)value.Length));
        WriteBytes(value);
    }

    public void WriteBytes(ReadOnlySpan<byte> value)
    {
        value.CopyTo(_writer.GetSpan(value.Length));
        _writer.Advance(value.Length);
    }

    public byte[] ToArray() => _writer.WrittenSpan.ToArray();
}

internal ref struct ManifestWireReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _position;

    public ManifestWireReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _position = 0;
    }

    public byte ReadByte(string field)
    {
        EnsureAvailable(1, field);
        return _data[_position++];
    }

    public ushort ReadUInt16(string field)
        => BinaryPrimitives.ReadUInt16LittleEndian(ReadBytes(2, field));

    public uint ReadUInt32(string field)
        => BinaryPrimitives.ReadUInt32LittleEndian(ReadBytes(4, field));

    public ulong ReadUInt64(string field)
        => BinaryPrimitives.ReadUInt64LittleEndian(ReadBytes(8, field));

    public UInt256 ReadUInt256(string field) => new(ReadBytes(UInt256.Length, field));

    public byte[] ReadLengthPrefixedBytes(int maximum, string field)
    {
        var length = ReadUInt32($"{field} length");
        if (length > maximum)
            throw new InvalidDataException($"{field} length {length} exceeds maximum {maximum}");
        return ReadBytes(checked((int)length), field).ToArray();
    }

    public ReadOnlySpan<byte> ReadBytes(int length, string field)
    {
        EnsureAvailable(length, field);
        var result = _data.Slice(_position, length);
        _position += length;
        return result;
    }

    public void RequireMagic(ReadOnlySpan<byte> expected)
    {
        if (!ReadBytes(expected.Length, "magic").SequenceEqual(expected))
            throw new InvalidDataException("Invalid proof result manifest magic");
    }

    public void EnsureEnd()
    {
        if (_position != _data.Length)
            throw new InvalidDataException(
                $"Proof result manifest has {_data.Length - _position} trailing bytes");
    }

    private void EnsureAvailable(int length, string field)
    {
        if (length < 0 || length > _data.Length - _position)
            throw new InvalidDataException($"{field} is truncated");
    }
}
