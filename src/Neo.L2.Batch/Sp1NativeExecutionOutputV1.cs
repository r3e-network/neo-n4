using System.Security.Cryptography;
using Neo.Cryptography;
using Neo.L2.State;

namespace Neo.L2.Batch;

/// <summary>Canonical result emitted by the native SP1 execution runtime.</summary>
/// <remarks>See doc.md §7.3, §7.5, and §8.1–§8.4.</remarks>
public sealed record Sp1NativeExecutionOutputV1
{
    /// <summary>Wire-format version.</summary>
    public const ushort Version = 1;

    /// <summary>Hash256 of the exact canonical execution payload request.</summary>
    public required UInt256 RequestPayloadHash { get; init; }

    /// <summary>Hash256 of the exact canonical pre-state witness request.</summary>
    public required UInt256 RequestStateWitnessHash { get; init; }

    /// <summary>Exact VM and state-transition semantic executed.</summary>
    public required UInt256 ExecutionSemanticId { get; init; }

    /// <summary>Canonical roots and gas returned by deterministic execution.</summary>
    public required BatchExecutionResult ExecutionResult { get; init; }

    /// <summary>Canonical <c>NEO4EFX1</c> transaction effects.</summary>
    public required ReadOnlyMemory<byte> Effects { get; init; }

    /// <summary>Canonical complete <c>NEO4STW1</c> post-state witness.</summary>
    public required ReadOnlyMemory<byte> PostStateWitness { get; init; }

    /// <summary>Hash256 of the canonical settlement public inputs.</summary>
    public required UInt256 PublicInputHash { get; init; }

    /// <inheritdoc />
    public bool Equals(Sp1NativeExecutionOutputV1? other) => other is not null
        && RequestPayloadHash.Equals(other.RequestPayloadHash)
        && RequestStateWitnessHash.Equals(other.RequestStateWitnessHash)
        && ExecutionSemanticId.Equals(other.ExecutionSemanticId)
        && ExecutionResult.Equals(other.ExecutionResult)
        && Effects.Span.SequenceEqual(other.Effects.Span)
        && PostStateWitness.Span.SequenceEqual(other.PostStateWitness.Span)
        && PublicInputHash.Equals(other.PublicInputHash);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(RequestPayloadHash);
        hash.Add(RequestStateWitnessHash);
        hash.Add(ExecutionSemanticId);
        hash.Add(ExecutionResult);
        hash.AddBytes(Effects.Span);
        hash.AddBytes(PostStateWitness.Span);
        hash.Add(PublicInputHash);
        return hash.ToHashCode();
    }
}

/// <summary>Canonical <c>NEO4EXR1</c> serializer shared with <c>neo-execution-core</c>.</summary>
/// <remarks>See doc.md §7.5 and §8. All integers and lengths are little-endian.</remarks>
public static class Sp1NativeExecutionOutputSerializer
{
    private static ReadOnlySpan<byte> Magic => "NEO4EXR1"u8;
    private static ReadOnlySpan<byte> ContentHashDomain =>
        "neo-n4/native-execution-output/v1\0"u8;
    private static ReadOnlySpan<byte> EffectsMagic => "NEO4EFX1"u8;

    /// <summary>Maximum canonical effects size.</summary>
    public const int MaxEffectsBytes = 64 * 1024 * 1024;

    /// <summary>Maximum complete native execution output size.</summary>
    public const int MaxEncodedBytes =
        StateWitnessV1Serializer.MaxEncodedBytes + MaxEffectsBytes + 512;

    /// <summary>Encode and fully validate a native execution result.</summary>
    public static byte[] Encode(Sp1NativeExecutionOutputV1 output)
    {
        ArgumentNullException.ThrowIfNull(output);
        Validate(output);
        var writer = new CanonicalWireWriter(checked(
            8 + 2 + 2 + 3 * UInt256.Length + 6 * UInt256.Length + 8
            + 4 + output.Effects.Length + 4 + output.PostStateWitness.Length
            + UInt256.Length + UInt256.Length));
        writer.WriteBytes(Magic);
        writer.WriteUInt16(Sp1NativeExecutionOutputV1.Version);
        writer.WriteUInt16(0);
        writer.WriteUInt256(output.RequestPayloadHash);
        writer.WriteUInt256(output.RequestStateWitnessHash);
        writer.WriteUInt256(output.ExecutionSemanticId);
        WriteExecutionResult(writer, output.ExecutionResult);
        writer.WriteLengthPrefixedBytes(output.Effects.Span);
        writer.WriteLengthPrefixedBytes(output.PostStateWitness.Span);
        writer.WriteUInt256(output.PublicInputHash);
        var body = writer.ToArray();
        var encoded = new byte[checked(body.Length + UInt256.Length)];
        body.CopyTo(encoded, 0);
        ComputeContentHash(body).GetSpan().CopyTo(encoded.AsSpan(body.Length));
        if (encoded.Length > MaxEncodedBytes)
            throw new ArgumentException(
                $"Native execution output exceeds {MaxEncodedBytes} bytes", nameof(output));
        return encoded;
    }

    /// <summary>Decode and fully validate canonical native execution bytes.</summary>
    public static Sp1NativeExecutionOutputV1 Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaxEncodedBytes)
            throw new InvalidDataException(
                $"Native execution output exceeds {MaxEncodedBytes} bytes: {bytes.Length}");
        if (bytes.Length < UInt256.Length)
            throw new InvalidDataException("Native execution output is truncated");
        var body = bytes[..^UInt256.Length];
        if (!ComputeContentHash(body).GetSpan().SequenceEqual(bytes[^UInt256.Length..]))
            throw new InvalidDataException("Native execution output content hash mismatch");

        var reader = new StrictWireReader(body);
        reader.RequireMagic(Magic, "native execution output");
        if (reader.ReadUInt16("version") != Sp1NativeExecutionOutputV1.Version)
            throw new InvalidDataException("Unsupported native execution output version");
        if (reader.ReadUInt16("flags") != 0)
            throw new InvalidDataException("Native execution output flags must be zero");
        var output = new Sp1NativeExecutionOutputV1
        {
            RequestPayloadHash = reader.ReadUInt256("request payload hash"),
            RequestStateWitnessHash = reader.ReadUInt256("request state witness hash"),
            ExecutionSemanticId = reader.ReadUInt256("execution semantic id"),
            ExecutionResult = ReadExecutionResult(ref reader),
            Effects = reader.ReadLengthPrefixedBytes(MaxEffectsBytes, "effects"),
            PostStateWitness = reader.ReadLengthPrefixedBytes(
                StateWitnessV1Serializer.MaxEncodedBytes, "post-state witness"),
            PublicInputHash = reader.ReadUInt256("public input hash"),
        };
        reader.EnsureEnd("native execution output");
        try
        {
            Validate(output);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "Native execution output is not canonical", exception);
        }
        return output;
    }

    private static void Validate(Sp1NativeExecutionOutputV1 output)
    {
        ArgumentNullException.ThrowIfNull(output.RequestPayloadHash);
        ArgumentNullException.ThrowIfNull(output.RequestStateWitnessHash);
        ArgumentNullException.ThrowIfNull(output.ExecutionSemanticId);
        ArgumentNullException.ThrowIfNull(output.ExecutionResult);
        ArgumentNullException.ThrowIfNull(output.ExecutionResult.PostStateRoot);
        ArgumentNullException.ThrowIfNull(output.ExecutionResult.TxRoot);
        ArgumentNullException.ThrowIfNull(output.ExecutionResult.ReceiptRoot);
        ArgumentNullException.ThrowIfNull(output.ExecutionResult.WithdrawalRoot);
        ArgumentNullException.ThrowIfNull(output.ExecutionResult.L2ToL1MessageRoot);
        ArgumentNullException.ThrowIfNull(output.ExecutionResult.L2ToL2MessageRoot);
        ArgumentNullException.ThrowIfNull(output.PublicInputHash);
        if (!output.ExecutionSemanticId.Equals(ExecutionSemanticIds.Sp1StatefulNeoVmV1))
            throw new ArgumentException(
                "Native execution output semantic ID is not SP1 stateful NeoVM V1",
                nameof(output));
        if (output.ExecutionResult.GasConsumed < 0)
            throw new ArgumentException(
                "Native execution output gas must be non-negative", nameof(output));
        ValidateEffectsHeader(output.Effects.Span);
        var postState = StateWitnessV1Serializer.Decode(output.PostStateWitness.Span);
        var canonicalPostState = StateWitnessV1Serializer.Encode(postState);
        if (!canonicalPostState.AsSpan().SequenceEqual(output.PostStateWitness.Span))
            throw new ArgumentException(
                "Native execution post-state is not canonical", nameof(output));
        var postStateRoot = KeyedStateMerkleTree.ComputeRoot(postState.Entries.Select(
            static entry => (entry.Key.ToArray(), entry.Value.ToArray())));
        if (!postStateRoot.Equals(output.ExecutionResult.PostStateRoot))
            throw new ArgumentException(
                "Native execution post-state root differs from the execution result",
                nameof(output));
    }

    private static void ValidateEffectsHeader(ReadOnlySpan<byte> effects)
    {
        var reader = new StrictWireReader(effects);
        reader.RequireMagic(EffectsMagic, "batch effects");
        if (reader.ReadUInt16("effects version") != 1
            || reader.ReadUInt16("effects flags") != 0
            || reader.ReadUInt32("effects transaction count") == 0)
            throw new ArgumentException("Native execution effects header is invalid");
    }

    private static BatchExecutionResult ReadExecutionResult(ref StrictWireReader reader)
        => new()
        {
            PostStateRoot = reader.ReadUInt256("post-state root"),
            TxRoot = reader.ReadUInt256("transaction root"),
            ReceiptRoot = reader.ReadUInt256("receipt root"),
            WithdrawalRoot = reader.ReadUInt256("withdrawal root"),
            L2ToL1MessageRoot = reader.ReadUInt256("L2-to-L1 message root"),
            L2ToL2MessageRoot = reader.ReadUInt256("L2-to-L2 message root"),
            GasConsumed = reader.ReadInt64("gas consumed"),
        };

    private static void WriteExecutionResult(
        CanonicalWireWriter writer,
        BatchExecutionResult result)
    {
        writer.WriteUInt256(result.PostStateRoot);
        writer.WriteUInt256(result.TxRoot);
        writer.WriteUInt256(result.ReceiptRoot);
        writer.WriteUInt256(result.WithdrawalRoot);
        writer.WriteUInt256(result.L2ToL1MessageRoot);
        writer.WriteUInt256(result.L2ToL2MessageRoot);
        writer.WriteInt64(result.GasConsumed);
    }

    private static UInt256 ComputeContentHash(ReadOnlySpan<byte> body)
    {
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha256.AppendData(ContentHashDomain);
        sha256.AppendData(body);
        var firstHash = sha256.GetHashAndReset();
        return new UInt256(SHA256.HashData(firstHash));
    }
}
