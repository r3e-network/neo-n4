using System.Buffers.Binary;

namespace Neo.L2.Challenge;

/// <summary>
/// Canonical wire format for an optimistic-rollup fraud proof. The bytes go into
/// <c>NeoHub.OptimisticChallenge.Challenge</c>'s <c>fraudProofBytes</c> argument and are
/// re-decoded by the configured fraud-verifier contract.
/// </summary>
/// <remarks>
/// MVP layout (101 bytes, all little-endian):
/// <code>
/// offset  size  field
/// 0       1     version (currently 1)
/// 1       32    preStateRoot
/// 33      32    claimedPostStateRoot
/// 65      32    replayedPostStateRoot
/// 97      4     disputedTxIndex (uint32)
/// </code>
/// Real production: extend with execution-trace witness bytes that the verifier replays
/// step-by-step inside the L1 contract. The MVP version proves *that there is a discrepancy*
/// — sufficient for a multisig governance verifier to pause the chain pending arbitration.
/// </remarks>
public sealed record FraudProofPayload
{
    /// <summary>Wire-format version (currently 1).</summary>
    public const byte Version = 1;

    /// <summary>Fixed-size encoding length.</summary>
    public const int Size = 1 + 32 + 32 + 4 + 32;

    /// <summary>Pre-state root the batch claimed to start from.</summary>
    public required UInt256 PreStateRoot { get; init; }

    /// <summary>Post-state root the sequencer claimed.</summary>
    public required UInt256 ClaimedPostStateRoot { get; init; }

    /// <summary>Post-state root the challenger's deterministic replay produced.</summary>
    public required UInt256 ReplayedPostStateRoot { get; init; }

    /// <summary>Index of the first transaction the challenger believes mis-executed.</summary>
    public required uint DisputedTxIndex { get; init; }

    /// <summary>Encode to canonical bytes.</summary>
    public byte[] Encode()
    {
        // Defense-in-depth: UInt256 fields are reference types; `required` only forces
        // "must be set," not "non-null." A null root would crash in GetSpan() with no
        // link back to the caller. Same iter-154/155/156/157 hashing-primitive pattern.
        ArgumentNullException.ThrowIfNull(PreStateRoot);
        ArgumentNullException.ThrowIfNull(ClaimedPostStateRoot);
        ArgumentNullException.ThrowIfNull(ReplayedPostStateRoot);
        var buffer = new byte[Size];
        var span = buffer.AsSpan();
        span[0] = Version;
        PreStateRoot.GetSpan().CopyTo(span.Slice(1, 32));
        ClaimedPostStateRoot.GetSpan().CopyTo(span.Slice(33, 32));
        ReplayedPostStateRoot.GetSpan().CopyTo(span.Slice(65, 32));
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(97, 4), DisputedTxIndex);
        return buffer;
    }

    /// <summary>Decode a canonical fraud-proof payload.</summary>
    public static FraudProofPayload Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Size) throw new ArgumentException($"expected {Size} bytes, got {bytes.Length}");
        if (bytes[0] != Version) throw new InvalidDataException($"unsupported version {bytes[0]}");
        return new FraudProofPayload
        {
            PreStateRoot = new UInt256(bytes.Slice(1, 32)),
            ClaimedPostStateRoot = new UInt256(bytes.Slice(33, 32)),
            ReplayedPostStateRoot = new UInt256(bytes.Slice(65, 32)),
            DisputedTxIndex = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(97, 4)),
        };
    }
}
