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
/// Wire layout (65 + sigLen bytes, all little-endian):
/// <code>
/// offset  size      field
/// 0       1         version (currently 1)
/// 1       20        bondContract (UInt160)
/// 21      32        bondTxHash (UInt256)
/// 53      8         submittedAt (uint64)
/// 61      4         sigLen (int32)
/// 65      sigLen    sequencerSignature
/// </code>
/// </para>
/// </remarks>
public sealed record OptimisticProofPayload
{
    /// <summary>Wire-format version (currently <c>1</c>).</summary>
    public const byte Version = 1;

    /// <summary>Hard cap on the sequencer-signature byte length on Decode (defensive — real signatures are 64 bytes).</summary>
    public const int MaxSignatureBytes = 4096;

    /// <summary>L1 contract that holds the sequencer's bond for this claim.</summary>
    public required UInt160 BondContract { get; init; }

    /// <summary>L1 transaction hash that posted the bond.</summary>
    public required UInt256 BondTxHash { get; init; }

    /// <summary>Wall-clock submission time (used by the L1 challenge window).</summary>
    public required ulong SubmittedAt { get; init; }

    /// <summary>Signature of <see cref="PublicInputs"/> by the sequencer key.</summary>
    public required ReadOnlyMemory<byte> SequencerSignature { get; init; }

    /// <summary>Encode to canonical bytes for embedding in <see cref="L2BatchCommitment.Proof"/>.</summary>
    public byte[] Encode()
    {
        var size = 1 + 20 + 32 + 8 + 4 + SequencerSignature.Length;
        var buffer = new byte[size];
        var span = buffer.AsSpan();
        span[0] = Version;
        BondContract.GetSpan().CopyTo(span.Slice(1, 20));
        BondTxHash.GetSpan().CopyTo(span.Slice(21, 32));
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(53, 8), SubmittedAt);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(61, 4), SequencerSignature.Length);
        SequencerSignature.Span.CopyTo(span.Slice(65));
        return buffer;
    }

    /// <summary>Decode a canonical optimistic proof payload.</summary>
    public static OptimisticProofPayload Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 65) throw new ArgumentException("Buffer too small", nameof(bytes));
        if (bytes[0] != Version) throw new InvalidDataException($"Unsupported optimistic proof version {bytes[0]}");

        var bondContract = new UInt160(bytes.Slice(1, 20));
        var bondTxHash = new UInt256(bytes.Slice(21, 32));
        var submittedAt = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(53, 8));
        var sigLen = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(61, 4));
        if (sigLen < 0 || sigLen > MaxSignatureBytes || 65 + sigLen != bytes.Length)
            throw new InvalidDataException($"Bad sequencer signature length {sigLen}");
        var sig = bytes.Slice(65, sigLen).ToArray();

        return new OptimisticProofPayload
        {
            BondContract = bondContract,
            BondTxHash = bondTxHash,
            SubmittedAt = submittedAt,
            SequencerSignature = sig,
        };
    }
}
