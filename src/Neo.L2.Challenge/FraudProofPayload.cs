using System.Buffers.Binary;

namespace Neo.L2.Challenge;

/// <summary>
/// Canonical wire format for an optimistic-rollup fraud proof. The bytes go into
/// <c>NeoHub.OptimisticChallenge.Challenge</c>'s <c>fraudProofBytes</c> argument and are
/// re-decoded by the configured fraud-verifier contract.
/// </summary>
/// <remarks>
/// Two versions are supported:
/// <para>
/// <b>v1 (101 bytes)</b> — structural-only payload proving "there is a discrepancy".
/// Sufficient for governance-arbitration fraud verifiers (the human council resolves
/// the dispute given the two state roots in the payload). Layout (all little-endian):
/// </para>
/// <code>
/// offset  size  field
/// 0       1     version (= 1)
/// 1       32    preStateRoot
/// 33      32    claimedPostStateRoot
/// 65      32    replayedPostStateRoot
/// 97      4     disputedTxIndex (uint32)
/// </code>
/// <para>
/// <b>v2 (105 + N bytes)</b> — extends v1 with a length-prefixed disputed-transaction
/// witness, so trustless re-execution verifiers can pull the exact tx bytes the dispute
/// is about. The pre/post state-tree witnesses (storage reads / writes) needed for
/// full on-L1 re-execution aren't carried here — operators implementing real
/// re-execution verifiers extend the witness with their own format on top of v2.
/// </para>
/// <code>
/// offset  size      field
/// 0       1         version (= 2)
/// 1..96   96        same 3 state roots as v1
/// 97      4         disputedTxIndex (uint32)
/// 101     4         disputedTxLen (uint32, ≤ MaxDisputedTxBytes)
/// 105     N         disputedTxBytes (N = disputedTxLen)
/// </code>
/// <para>
/// Encode produces v1 when <see cref="DisputedTxBytes"/> is empty, v2 otherwise. Decode
/// reads the version byte and dispatches accordingly. Off-chain consumers should
/// branch on <c>DisputedTxBytes.IsEmpty</c> after decoding.
/// </para>
/// </remarks>
public sealed record FraudProofPayload
{
    /// <summary>v1 wire-format version.</summary>
    public const byte Version = 1;

    /// <summary>v2 wire-format version (adds disputed-tx witness).</summary>
    public const byte Version2 = 2;

    /// <summary>v1 fixed encoding length.</summary>
    public const int Size = 1 + 32 + 32 + 4 + 32;  // = 101

    /// <summary>v2 minimum encoding length (header before witness bytes).</summary>
    public const int V2HeaderSize = Size + 4;  // = 105

    /// <summary>
    /// Cap on the disputed-tx witness bytes — refuse to encode/decode anything larger.
    /// Without a cap, an attacker / buggy challenger could flood L1 storage with
    /// arbitrarily-large payloads.
    /// </summary>
    public const int MaxDisputedTxBytes = 64 * 1024;

    /// <summary>Pre-state root the batch claimed to start from.</summary>
    public required UInt256 PreStateRoot { get; init; }

    /// <summary>Post-state root the sequencer claimed.</summary>
    public required UInt256 ClaimedPostStateRoot { get; init; }

    /// <summary>Post-state root the challenger's deterministic replay produced.</summary>
    public required UInt256 ReplayedPostStateRoot { get; init; }

    /// <summary>Index of the first transaction the challenger believes mis-executed.</summary>
    public required uint DisputedTxIndex { get; init; }

    /// <summary>
    /// Disputed-transaction witness bytes (v2 only). Empty in v1 encodings. When
    /// non-empty, the encoder produces a v2 payload; when empty, the encoder produces
    /// a v1 payload.
    /// </summary>
    public ReadOnlyMemory<byte> DisputedTxBytes { get; init; } = ReadOnlyMemory<byte>.Empty;

    /// <summary>Encoded size in bytes (varies by version).</summary>
    public int EncodedSize => DisputedTxBytes.IsEmpty
        ? Size
        : V2HeaderSize + DisputedTxBytes.Length;

    /// <summary>Encode to canonical bytes.</summary>
    public byte[] Encode()
    {
        // Defense-in-depth: UInt256 fields are reference types; `required` only forces
        // "must be set," not "non-null." A null root would crash in GetSpan() with no
        // link back to the caller. Same iter-154/155/156/157 hashing-primitive pattern.
        ArgumentNullException.ThrowIfNull(PreStateRoot);
        ArgumentNullException.ThrowIfNull(ClaimedPostStateRoot);
        ArgumentNullException.ThrowIfNull(ReplayedPostStateRoot);
        if (DisputedTxBytes.Length > MaxDisputedTxBytes)
            throw new InvalidOperationException(
                $"DisputedTxBytes length {DisputedTxBytes.Length} exceeds MaxDisputedTxBytes ({MaxDisputedTxBytes})");

        var isV2 = !DisputedTxBytes.IsEmpty;
        var buffer = new byte[isV2 ? V2HeaderSize + DisputedTxBytes.Length : Size];
        var span = buffer.AsSpan();
        span[0] = isV2 ? Version2 : Version;
        PreStateRoot.GetSpan().CopyTo(span.Slice(1, 32));
        ClaimedPostStateRoot.GetSpan().CopyTo(span.Slice(33, 32));
        ReplayedPostStateRoot.GetSpan().CopyTo(span.Slice(65, 32));
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(97, 4), DisputedTxIndex);
        if (isV2)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(101, 4), (uint)DisputedTxBytes.Length);
            DisputedTxBytes.Span.CopyTo(span.Slice(V2HeaderSize));
        }
        return buffer;
    }

    /// <summary>Decode a canonical fraud-proof payload (v1 or v2; auto-dispatched on version byte).</summary>
    public static FraudProofPayload Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 1)
            throw new ArgumentException($"empty payload — expected ≥ 1 byte for version");
        var version = bytes[0];
        if (version == Version)
        {
            if (bytes.Length != Size)
                throw new ArgumentException($"v1 payload must be exactly {Size} bytes, got {bytes.Length}");
            return new FraudProofPayload
            {
                PreStateRoot = new UInt256(bytes.Slice(1, 32)),
                ClaimedPostStateRoot = new UInt256(bytes.Slice(33, 32)),
                ReplayedPostStateRoot = new UInt256(bytes.Slice(65, 32)),
                DisputedTxIndex = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(97, 4)),
                DisputedTxBytes = ReadOnlyMemory<byte>.Empty,
            };
        }
        if (version == Version2)
        {
            if (bytes.Length < V2HeaderSize)
                throw new ArgumentException(
                    $"v2 payload must be ≥ {V2HeaderSize} bytes (header), got {bytes.Length}");
            var declaredLen = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(101, 4));
            if (declaredLen > MaxDisputedTxBytes)
                throw new ArgumentException(
                    $"v2 disputedTxLen {declaredLen} exceeds MaxDisputedTxBytes ({MaxDisputedTxBytes})");
            // Strict length match — extra trailing bytes are a malformed payload, not "ignore".
            // Without this, a fraudulent payload could include junk bytes after the witness
            // and decode as "valid" with a possibly-different effective layout.
            if (bytes.Length != V2HeaderSize + declaredLen)
                throw new ArgumentException(
                    $"v2 payload length {bytes.Length} != header({V2HeaderSize}) + disputedTxLen({declaredLen})");
            var txBytes = new byte[declaredLen];
            bytes.Slice(V2HeaderSize, (int)declaredLen).CopyTo(txBytes);
            return new FraudProofPayload
            {
                PreStateRoot = new UInt256(bytes.Slice(1, 32)),
                ClaimedPostStateRoot = new UInt256(bytes.Slice(33, 32)),
                ReplayedPostStateRoot = new UInt256(bytes.Slice(65, 32)),
                DisputedTxIndex = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(97, 4)),
                DisputedTxBytes = txBytes,
            };
        }
        throw new InvalidDataException($"unsupported version {version}");
    }

    /// <inheritdoc />
    public bool Equals(FraudProofPayload? other)
    {
        // ReadOnlyMemory<byte> doesn't override Equals to compare content — record-default
        // would compare by reference, breaking value-equality semantics any caller would
        // expect. Same iter-148+ pattern as L2BatchCommitment.
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (!Equals(PreStateRoot, other.PreStateRoot)) return false;
        if (!Equals(ClaimedPostStateRoot, other.ClaimedPostStateRoot)) return false;
        if (!Equals(ReplayedPostStateRoot, other.ReplayedPostStateRoot)) return false;
        if (DisputedTxIndex != other.DisputedTxIndex) return false;
        return DisputedTxBytes.Span.SequenceEqual(other.DisputedTxBytes.Span);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hc = new HashCode();
        hc.Add(PreStateRoot);
        hc.Add(ClaimedPostStateRoot);
        hc.Add(ReplayedPostStateRoot);
        hc.Add(DisputedTxIndex);
        // Hash a stable view of the witness — first/last bytes + length is enough
        // for HashCode-bucket spreading without iterating every byte.
        hc.Add(DisputedTxBytes.Length);
        if (!DisputedTxBytes.IsEmpty)
        {
            hc.Add(DisputedTxBytes.Span[0]);
            hc.Add(DisputedTxBytes.Span[^1]);
        }
        return hc.ToHashCode();
    }
}
