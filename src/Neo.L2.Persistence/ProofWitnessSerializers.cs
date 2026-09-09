using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Neo.Cryptography;
using Neo.L2.Batch;

namespace Neo.L2.Persistence;

internal static class SettlementRollbackCheckpointSerializer
{
    private static ReadOnlySpan<byte> Magic => "NEO4SRBK"u8;
    private static ReadOnlySpan<byte> HashDomain => "neo-n4/settlement-rollback/v1\0"u8;
    private const ushort Version = 1;
    private const int BodySize = 128;
    private const int EncodedSize = BodySize + UInt256.Length;

    public static byte[] Encode(SettlementRollbackCheckpoint checkpoint)
    {
        Validate(checkpoint);
        var writer = new ManifestWireWriter(BodySize);
        writer.WriteBytes(Magic);
        writer.WriteUInt16(Version);
        writer.WriteUInt16(0);
        writer.WriteUInt32(checkpoint.ChainId);
        writer.WriteUInt64(checkpoint.FirstBatchNumber);
        writer.WriteUInt64(checkpoint.LastBatchNumber);
        writer.WriteUInt256(checkpoint.RevertedArtifactContentHash);
        writer.WriteUInt256(checkpoint.ExpectedCurrentStateRoot);
        writer.WriteUInt256(checkpoint.TargetStateRoot);
        if (writer.WrittenCount != BodySize)
            throw new InvalidOperationException(
                $"Settlement rollback checkpoint length mismatch: wrote {writer.WrittenCount}, expected {BodySize}");
        var body = writer.ToArray();
        var encoded = new byte[EncodedSize];
        body.CopyTo(encoded, 0);
        ComputeHash(body).GetSpan().CopyTo(encoded.AsSpan(BodySize));
        return encoded;
    }

    public static SettlementRollbackCheckpoint Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length != EncodedSize)
            throw new InvalidDataException(
                "Settlement rollback checkpoint has an invalid length");
        var reader = new ManifestWireReader(data);
        reader.RequireMagic(Magic);
        if (reader.ReadUInt16("version") != Version)
            throw new InvalidDataException(
                "Unsupported settlement rollback checkpoint version");
        if (reader.ReadUInt16("reserved") != 0)
            throw new InvalidDataException(
                "Settlement rollback checkpoint reserved bytes must be zero");
        var checkpoint = new SettlementRollbackCheckpoint
        {
            ChainId = reader.ReadUInt32("chain id"),
            FirstBatchNumber = reader.ReadUInt64("first batch number"),
            LastBatchNumber = reader.ReadUInt64("last batch number"),
            RevertedArtifactContentHash = reader.ReadUInt256("reverted artifact content hash"),
            ExpectedCurrentStateRoot = reader.ReadUInt256("expected current state root"),
            TargetStateRoot = reader.ReadUInt256("target state root"),
        };
        var contentHash = reader.ReadUInt256("content hash");
        reader.EnsureEnd();
        if (!contentHash.Equals(ComputeHash(data[..BodySize])))
            throw new InvalidDataException(
                "Settlement rollback checkpoint content hash mismatch");
        try
        {
            Validate(checkpoint);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "Settlement rollback checkpoint fields are inconsistent", exception);
        }
        return checkpoint;
    }

    private static UInt256 ComputeHash(ReadOnlySpan<byte> body)
    {
        var bound = new byte[checked(HashDomain.Length + body.Length)];
        HashDomain.CopyTo(bound);
        body.CopyTo(bound.AsSpan(HashDomain.Length));
        return new UInt256(Crypto.Hash256(bound));
    }

    private static void Validate(SettlementRollbackCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(checkpoint.RevertedArtifactContentHash);
        ArgumentNullException.ThrowIfNull(checkpoint.ExpectedCurrentStateRoot);
        ArgumentNullException.ThrowIfNull(checkpoint.TargetStateRoot);
        if (checkpoint.ChainId == 0)
            throw new ArgumentException("Rollback chain id must be non-zero", nameof(checkpoint));
        if (checkpoint.FirstBatchNumber == 0
            || checkpoint.LastBatchNumber < checkpoint.FirstBatchNumber)
            throw new ArgumentException("Invalid rollback batch range", nameof(checkpoint));
        if (checkpoint.RevertedArtifactContentHash.Equals(UInt256.Zero)
            || checkpoint.ExpectedCurrentStateRoot.Equals(UInt256.Zero)
            || checkpoint.TargetStateRoot.Equals(UInt256.Zero))
            throw new ArgumentException("Rollback roots and content hash must be non-zero", nameof(checkpoint));
    }
}

internal static class SettlementRecoveryCheckpointSerializer
{
    private static ReadOnlySpan<byte> Magic => "NEO4SRCV"u8;
    private const byte Version = 1;
    private const int FixedSize = 80;
    private const int MaxErrorBytes = 4096;

    public static byte[] Encode(SettlementRecoveryCheckpoint checkpoint)
    {
        Validate(checkpoint);
        var errorBytes = checkpoint.LastError is null
            ? Array.Empty<byte>()
            : Encoding.UTF8.GetBytes(checkpoint.LastError);
        if (errorBytes.Length > MaxErrorBytes)
            errorBytes = errorBytes.AsSpan(0, MaxErrorBytes).ToArray();
        var encodedSize = checked(FixedSize + errorBytes.Length);
        var writer = new ManifestWireWriter(encodedSize);
        writer.WriteBytes(Magic);
        writer.WriteByte(Version);
        writer.WriteByte((byte)checkpoint.State);
        writer.WriteBytes(stackalloc byte[2]);
        writer.WriteUInt32(checkpoint.ChainId);
        writer.WriteUInt64(checkpoint.BatchNumber);
        writer.WriteUInt256(checkpoint.ArtifactContentHash);
        writer.WriteUInt32(checked((uint)checkpoint.RetryCount));
        writer.WriteUInt64(checked((ulong)checkpoint.FirstFailureAtUnixMilliseconds));
        writer.WriteUInt64(checked((ulong)checkpoint.LastFailureAtUnixMilliseconds));
        writer.WriteLengthPrefixedBytes(errorBytes);
        if (writer.WrittenCount != encodedSize)
            throw new InvalidOperationException("Settlement recovery checkpoint length mismatch");
        return writer.ToArray();
    }

    public static SettlementRecoveryCheckpoint Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < FixedSize || data.Length > FixedSize + MaxErrorBytes)
            throw new InvalidDataException("Settlement recovery checkpoint has an invalid length");
        var reader = new ManifestWireReader(data);
        reader.RequireMagic(Magic);
        var version = reader.ReadByte("version");
        if (version != Version)
            throw new InvalidDataException(
                $"Unsupported settlement recovery checkpoint version {version}");
        var stateByte = reader.ReadByte("state");
        if (!Enum.IsDefined((SettlementRecoveryState)stateByte))
            throw new InvalidDataException(
                $"Unknown settlement recovery state byte {stateByte}");
        var reserved = reader.ReadBytes(2, "reserved bytes");
        if (reserved[0] != 0 || reserved[1] != 0)
            throw new InvalidDataException(
                "Settlement recovery reserved bytes must be zero");
        var checkpoint = new SettlementRecoveryCheckpoint
        {
            ChainId = reader.ReadUInt32("chain id"),
            BatchNumber = reader.ReadUInt64("batch number"),
            ArtifactContentHash = reader.ReadUInt256("artifact hash"),
            State = (SettlementRecoveryState)stateByte,
            RetryCount = checked((int)reader.ReadUInt32("retry count")),
            FirstFailureAtUnixMilliseconds = checked((long)reader.ReadUInt64("first failure")),
            LastFailureAtUnixMilliseconds = checked((long)reader.ReadUInt64("last failure")),
            LastError = DecodeError(reader.ReadLengthPrefixedBytes(MaxErrorBytes, "last error")),
        };
        reader.EnsureEnd();
        try
        {
            Validate(checkpoint);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "Settlement recovery checkpoint fields are inconsistent", exception);
        }
        return checkpoint;
    }

    private static string? DecodeError(ReadOnlyMemory<byte> bytes)
        => bytes.IsEmpty ? null : Encoding.UTF8.GetString(bytes.Span);

    private static void Validate(SettlementRecoveryCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(checkpoint.ArtifactContentHash);
        if (!Enum.IsDefined(checkpoint.State))
            throw new ArgumentException("Unknown settlement recovery state", nameof(checkpoint));
        if (checkpoint.RetryCount < 0)
            throw new ArgumentException("RetryCount must be non-negative", nameof(checkpoint));
        if (checkpoint.State == SettlementRecoveryState.Poisoned
            && checkpoint.RetryCount == 0)
            throw new ArgumentException("Poisoned recovery requires a retry count", nameof(checkpoint));
        if (checkpoint.RetryCount == 0)
        {
            if (checkpoint.FirstFailureAtUnixMilliseconds != 0
                || checkpoint.LastFailureAtUnixMilliseconds != 0
                || checkpoint.LastError is not null)
            {
                throw new ArgumentException(
                    "Reset recovery state must clear failure details", nameof(checkpoint));
            }
            return;
        }
        if (checkpoint.FirstFailureAtUnixMilliseconds <= 0
            || checkpoint.LastFailureAtUnixMilliseconds < checkpoint.FirstFailureAtUnixMilliseconds)
            throw new ArgumentException("Invalid settlement recovery timestamps", nameof(checkpoint));
        if (string.IsNullOrWhiteSpace(checkpoint.LastError))
            throw new ArgumentException("Failed recovery requires LastError", nameof(checkpoint));
    }
}

/// <summary>Canonical serializer for <see cref="ProofResultManifest"/>.</summary>
/// <remarks>See doc.md §7.5 and §8.</remarks>
public static class ProofResultManifestSerializer
{
    private static ReadOnlySpan<byte> Magic => "NEO4PRMF"u8;
    private static ReadOnlySpan<byte> ContentHashDomainV1 => "neo-n4/proof-result/v1\0"u8;
    private static ReadOnlySpan<byte> ContentHashDomainV2 => "neo-n4/proof-result/v2\0"u8;
    private static ReadOnlySpan<byte> ContentHashDomainV3 => "neo-n4/proof-result/v3\0"u8;

    private const ushort SubmittedFlag = 1;
    private const ushort SettlementObservedFlag = 2;
    private const ushort TransactionHashFlag = 4;
    private const ushort ForcedInclusionFinalizedFlag = 8;
    private const ushort SettlementFinalizedFlag = 16;
    private const ushort KnownFlagsV2 = SubmittedFlag
        | SettlementObservedFlag
        | TransactionHashFlag
        | ForcedInclusionFinalizedFlag;
    private const ushort KnownFlags = KnownFlagsV2 | SettlementFinalizedFlag;
    private const ushort V1SettlementObservedFlag = 1;
    private const ushort V1TransactionHashFlag = 2;
    private const ushort V1ForcedInclusionFinalizedFlag = 4;
    private const ushort V1KnownFlags = V1SettlementObservedFlag
        | V1TransactionHashFlag
        | V1ForcedInclusionFinalizedFlag;
    private const int FixedSize = 164;
    private const int ContentHashSize = 32;

    /// <summary>Maximum terminal proof size (16 MiB).</summary>
    public const int MaxProofBytes = 16 * 1024 * 1024;

    /// <summary>Maximum public-values size (16 MiB).</summary>
    public const int MaxPublicValuesBytes = 16 * 1024 * 1024;

    /// <summary>Encode a proof result manifest.</summary>
    public static byte[] Encode(ProofResultManifest manifest)
    {
        Validate(manifest);
        var hasTransactionHash = manifest.L1TransactionHash is not null;
        var flags = manifest.SubmissionState switch
        {
            ProofSubmissionState.ProofReady => (ushort)0,
            ProofSubmissionState.Submitted => SubmittedFlag,
            ProofSubmissionState.SettlementObserved => SettlementObservedFlag,
            _ => throw new ArgumentOutOfRangeException(
                nameof(manifest), manifest.SubmissionState, "unknown proof submission state"),
        };
        if (hasTransactionHash) flags |= TransactionHashFlag;
        if (manifest.ForcedInclusionFinalized) flags |= ForcedInclusionFinalizedFlag;
        if (manifest.SettlementFinalized) flags |= SettlementFinalizedFlag;
        var bodySize = checked(
            FixedSize
            + manifest.Proof.Length
            + manifest.PublicValues.Length
            + (hasTransactionHash ? UInt256.Length : 0));
        var writer = new ManifestWireWriter(bodySize);
        writer.WriteBytes(Magic);
        writer.WriteUInt16(ProofResultManifest.Version);
        writer.WriteUInt16(flags);
        writer.WriteByte((byte)manifest.ProofType);
        writer.WriteByte((byte)manifest.ProofSystem);
        writer.WriteBytes(stackalloc byte[2]);
        writer.WriteUInt32(manifest.ChainId);
        writer.WriteUInt64(manifest.BatchNumber);
        writer.WriteUInt256(manifest.ArtifactContentHash);
        writer.WriteUInt256(manifest.PublicInputHash);
        writer.WriteUInt256(manifest.VerificationKeyId);
        writer.WriteUInt256(manifest.ExecutionSemanticId);
        writer.WriteLengthPrefixedBytes(manifest.Proof.Span);
        writer.WriteLengthPrefixedBytes(manifest.PublicValues.Span);
        if (manifest.L1TransactionHash is not null)
            writer.WriteUInt256(manifest.L1TransactionHash);
        if (writer.WrittenCount != bodySize)
            throw new InvalidOperationException(
                $"Proof result manifest length mismatch: wrote {writer.WrittenCount}, expected {bodySize}");
        var body = writer.ToArray();
        var contentHash = ComputeContentHash(body, ProofResultManifest.Version);
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
        if (version is not (1 or 2 or ProofResultManifest.Version))
            throw new InvalidDataException($"Unsupported proof result version {version}");
        var flags = reader.ReadUInt16("flags");
        var knownFlags = version switch
        {
            1 => V1KnownFlags,
            2 => KnownFlagsV2,
            _ => KnownFlags,
        };
        if ((flags & ~knownFlags) != 0)
            throw new InvalidDataException($"Unknown proof result flags 0x{flags:x4}");
        if (version != 1
            && (flags & SubmittedFlag) != 0
            && (flags & SettlementObservedFlag) != 0)
            throw new InvalidDataException(
                "Proof result cannot be both submitted and settlement-observed");
        var proofTypeByte = reader.ReadByte("proof type");
        if (!Enum.IsDefined((ProofType)proofTypeByte))
            throw new InvalidDataException($"Unknown proof type byte {proofTypeByte}");
        var proofSystemByte = reader.ReadByte("proof system");
        if (!Enum.IsDefined((WitnessProofSystem)proofSystemByte))
            throw new InvalidDataException($"Unknown proof system byte {proofSystemByte}");
        var reserved = reader.ReadBytes(2, "reserved bytes");
        if (reserved[0] != 0 || reserved[1] != 0)
            throw new InvalidDataException("Proof result reserved bytes must be zero");
        var chainId = reader.ReadUInt32("chain id");
        var batchNumber = reader.ReadUInt64("batch number");
        var artifactHash = reader.ReadUInt256("artifact hash");
        var publicInputHash = reader.ReadUInt256("public input hash");
        var verificationKeyId = reader.ReadUInt256("verification key id");
        var executionSemanticId = reader.ReadUInt256("execution semantic id");
        var proof = reader.ReadLengthPrefixedBytes(MaxProofBytes, "proof");
        var publicValues = reader.ReadLengthPrefixedBytes(MaxPublicValuesBytes, "public values");
        var transactionHashFlag = version == 1
            ? V1TransactionHashFlag
            : TransactionHashFlag;
        var l1TransactionHash = (flags & transactionHashFlag) != 0
            ? reader.ReadUInt256("L1 transaction hash")
            : null;
        var contentHash = reader.ReadUInt256("content hash");
        reader.EnsureEnd();
        var expectedContentHash = ComputeContentHash(data[..^ContentHashSize], version);
        if (!expectedContentHash.Equals(contentHash))
            throw new InvalidDataException("Proof result manifest content hash mismatch");
        var submissionState = version == 1
            ? DecodeV1SubmissionState(flags)
            : (flags & SettlementObservedFlag) != 0
                ? ProofSubmissionState.SettlementObserved
                : (flags & SubmittedFlag) != 0
                    ? ProofSubmissionState.Submitted
                    : ProofSubmissionState.ProofReady;
        var forcedFinalizedFlag = version == 1
            ? V1ForcedInclusionFinalizedFlag
            : ForcedInclusionFinalizedFlag;
        var manifest = new ProofResultManifest
        {
            ProofType = (ProofType)proofTypeByte,
            ChainId = chainId,
            BatchNumber = batchNumber,
            ArtifactContentHash = artifactHash,
            PublicInputHash = publicInputHash,
            VerificationKeyId = verificationKeyId,
            ProofSystem = (WitnessProofSystem)proofSystemByte,
            ExecutionSemanticId = executionSemanticId,
            Proof = proof,
            PublicValues = publicValues,
            SubmissionState = submissionState,
            SettlementFinalized = version == ProofResultManifest.Version
                ? (flags & SettlementFinalizedFlag) != 0
                : (flags & forcedFinalizedFlag) != 0,
            ForcedInclusionFinalized = (flags & forcedFinalizedFlag) != 0,
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

    private static ProofSubmissionState DecodeV1SubmissionState(ushort flags)
    {
        if ((flags & V1ForcedInclusionFinalizedFlag) != 0)
            return ProofSubmissionState.SettlementObserved;
        if ((flags & V1TransactionHashFlag) != 0)
            return ProofSubmissionState.Submitted;
        return (flags & V1SettlementObservedFlag) != 0
            ? ProofSubmissionState.SettlementObserved
            : ProofSubmissionState.ProofReady;
    }

    private static UInt256 ComputeContentHash(ReadOnlySpan<byte> body, ushort version)
    {
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha256.AppendData(version switch
        {
            1 => ContentHashDomainV1,
            2 => ContentHashDomainV2,
            _ => ContentHashDomainV3,
        });
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
        ArgumentNullException.ThrowIfNull(manifest.ExecutionSemanticId);
        if (!Enum.IsDefined(manifest.ProofType) || manifest.ProofType == ProofType.None)
            throw new ArgumentException("Unknown proof type", nameof(manifest));
        if (!Enum.IsDefined(manifest.ProofSystem))
            throw new ArgumentException("Unknown proof system", nameof(manifest));
        if (manifest.ExecutionSemanticId.Equals(UInt256.Zero))
            throw new ArgumentException("ExecutionSemanticId must be non-zero", nameof(manifest));
        if (manifest.ProofType == ProofType.Zk)
        {
            if (manifest.ProofSystem == WitnessProofSystem.None
                || manifest.VerificationKeyId.Equals(UInt256.Zero))
                throw new ArgumentException(
                    "ZK proof manifests require a proof system and verification key",
                    nameof(manifest));
        }
        else if (manifest.ProofType is ProofType.Multisig or ProofType.Optimistic)
        {
            if (manifest.ProofSystem != WitnessProofSystem.None
                || !manifest.VerificationKeyId.Equals(UInt256.Zero))
                throw new ArgumentException(
                    "Legacy proof manifests must use ProofSystem.None and zero VK",
                    nameof(manifest));
        }
        else
        {
            throw new ArgumentException(
                "Only explicit ZK, multisig, and optimistic manifests are supported",
                nameof(manifest));
        }
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
        if (!Enum.IsDefined(manifest.SubmissionState))
            throw new ArgumentException(
                "Unknown proof submission state", nameof(manifest));
        if (manifest.SubmissionState == ProofSubmissionState.ProofReady
            && manifest.L1TransactionHash is not null)
            throw new ArgumentException(
                "ProofReady cannot carry an L1 transaction hash", nameof(manifest));
        if (manifest.SubmissionState == ProofSubmissionState.Submitted
            && manifest.L1TransactionHash is null)
            throw new ArgumentException(
                "Submitted requires an L1 transaction hash", nameof(manifest));
        if (manifest.SettlementFinalized && !manifest.SettlementObserved)
            throw new ArgumentException(
                "SettlementFinalized requires SettlementObserved", nameof(manifest));
        if (manifest.ForcedInclusionFinalized && !manifest.SettlementFinalized)
            throw new ArgumentException(
                "forced-inclusion finalization requires SettlementFinalized", nameof(manifest));
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
