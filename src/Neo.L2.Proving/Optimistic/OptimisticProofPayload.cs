using System.Buffers.Binary;

namespace Neo.L2.Proving.Optimistic;

/// <summary>
/// Stage 1 (optimistic) proof payload. Carries the sequencer's claim that a batch is valid,
/// plus a reference to the bond that backs it. Verification on L1 just checks the claim is
/// well-formed; the actual fraud proof is a separate, asynchronous challenge.
/// </summary>
/// <remarks>
/// See doc.md §7.5 (Stage 1). The challenge window enforcement is handled by
/// <c>NeoHub.SettlementManager</c>, not by the verifier.
/// <para>
/// Wire layout (85 + sigLen bytes, all little-endian):
/// <code>
/// offset  size      field
/// 0       1         version (currently 2)
/// 1       20        bondContract (UInt160)
/// 21      32        bondTxHash (UInt256)
/// 53      8         submittedAt (uint64)
/// 61      20        sequencer (UInt160; slash target)
/// 81      4         sigLen (int32)
/// 85      sigLen    sequencerSignature
/// </code>
/// </para>
/// </remarks>
public sealed record OptimisticProofPayload
{
    /// <summary>Wire-format version (currently <c>2</c>).</summary>
    public const byte Version = 2;

    /// <summary>Hard cap on the sequencer-signature byte length on Decode (defensive — real signatures are 64 bytes).</summary>
    public const int MaxSignatureBytes = 4096;

    /// <summary>L1 contract that holds the sequencer's bond for this claim.</summary>
    public required UInt160 BondContract { get; init; }

    /// <summary>L1 transaction hash that posted the bond.</summary>
    public required UInt256 BondTxHash { get; init; }

    /// <summary>Wall-clock submission time (used by the L1 challenge window).</summary>
    public required ulong SubmittedAt { get; init; }

    /// <summary>Sequencer account whose bond backs this optimistic claim.</summary>
    public required UInt160 Sequencer { get; init; }

    /// <summary>Signature of <see cref="PublicInputs"/> by the sequencer key.</summary>
    public required ReadOnlyMemory<byte> SequencerSignature { get; init; }

    /// <inheritdoc />
    public bool Equals(OptimisticProofPayload? other)
    {
        return other is not null
            && BondContract.Equals(other.BondContract)
            && BondTxHash.Equals(other.BondTxHash)
            && SubmittedAt == other.SubmittedAt
            && Sequencer.Equals(other.Sequencer)
            && SequencerSignature.Span.SequenceEqual(other.SequencerSignature.Span);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(BondContract);
        hash.Add(BondTxHash);
        hash.Add(SubmittedAt);
        hash.Add(Sequencer);
        hash.AddBytes(SequencerSignature.Span);
        return hash.ToHashCode();
    }

    /// <summary>Encode to canonical bytes for embedding in <see cref="L2BatchCommitment.Proof"/>.</summary>
    public byte[] Encode()
    {
        // Defense-in-depth: UInt160/UInt256 are reference types; `required` only forces
        // "must be set," not "non-null." Same iter-154+ hashing-primitive pattern.
        ArgumentNullException.ThrowIfNull(BondContract);
        ArgumentNullException.ThrowIfNull(BondTxHash);
        ArgumentNullException.ThrowIfNull(Sequencer);
        if (Sequencer.Equals(UInt160.Zero))
            throw new InvalidOperationException("Sequencer must be non-zero");
        // Symmetry with Decode: Decode rejects > MaxSignatureBytes, so refuse to Encode
        // bytes the round-trip would later fail on. Without this an Encode succeeds and
        // the payload is unreadable, masking the producer-side bug at the next consumer.
        if (SequencerSignature.Length > MaxSignatureBytes)
            throw new InvalidOperationException(
                $"SequencerSignature {SequencerSignature.Length} bytes exceeds MaxSignatureBytes {MaxSignatureBytes}");
        var size = 1 + 20 + 32 + 8 + 20 + 4 + SequencerSignature.Length;
        var buffer = new byte[size];
        var span = buffer.AsSpan();
        span[0] = Version;
        BondContract.GetSpan().CopyTo(span.Slice(1, 20));
        BondTxHash.GetSpan().CopyTo(span.Slice(21, 32));
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(53, 8), SubmittedAt);
        Sequencer.GetSpan().CopyTo(span.Slice(61, 20));
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(81, 4), SequencerSignature.Length);
        SequencerSignature.Span.CopyTo(span.Slice(85));
        return buffer;
    }

    /// <summary>Decode a canonical optimistic proof payload.</summary>
    public static OptimisticProofPayload Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 85) throw new ArgumentException("Buffer too small", nameof(bytes));
        if (bytes[0] != Version) throw new InvalidDataException($"Unsupported optimistic proof version {bytes[0]}");

        var bondContract = new UInt160(bytes.Slice(1, 20));
        var bondTxHash = new UInt256(bytes.Slice(21, 32));
        var submittedAt = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(53, 8));
        var sequencer = new UInt160(bytes.Slice(61, 20));
        if (sequencer.Equals(UInt160.Zero)) throw new InvalidDataException("Sequencer must be non-zero");
        var sigLen = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(81, 4));
        if (sigLen < 0 || sigLen > MaxSignatureBytes || 85 + sigLen != bytes.Length)
            throw new InvalidDataException($"Bad sequencer signature length {sigLen}");
        var sig = bytes.Slice(85, sigLen).ToArray();

        return new OptimisticProofPayload
        {
            BondContract = bondContract,
            BondTxHash = bondTxHash,
            SubmittedAt = submittedAt,
            Sequencer = sequencer,
            SequencerSignature = sig,
        };
    }
}
