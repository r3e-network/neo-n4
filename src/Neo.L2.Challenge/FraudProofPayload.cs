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

    /// <summary>v3 wire-format version (adds storage-proof manifests).</summary>
    public const byte Version3 = 3;

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

    /// <summary>Cap on the number of storage proofs a v3 payload may carry.</summary>
    public const int MaxStorageProofsPerPayload = 32;

    /// <summary>Pre-state root the batch claimed to start from.</summary>
    public required UInt256 PreStateRoot { get; init; }

    /// <summary>Post-state root the sequencer claimed.</summary>
    public required UInt256 ClaimedPostStateRoot { get; init; }

    /// <summary>Post-state root the challenger's deterministic replay produced.</summary>
    public required UInt256 ReplayedPostStateRoot { get; init; }

    /// <summary>Index of the first transaction the challenger believes mis-executed.</summary>
    public required uint DisputedTxIndex { get; init; }

    /// <summary>
    /// Disputed-transaction witness bytes (v2+). Empty in v1 encodings. When non-empty
    /// (and <see cref="StorageProofs"/> is empty), the encoder produces a v2 payload;
    /// when empty + no storage proofs, the encoder produces a v1 payload.
    /// </summary>
    public ReadOnlyMemory<byte> DisputedTxBytes { get; init; } = ReadOnlyMemory<byte>.Empty;

    /// <summary>
    /// Storage proof manifests (v3 only). Each entry is a key + pre-value + post-value +
    /// Merkle proofs against the pre / post state roots. When non-empty, the encoder
    /// produces a v3 payload.
    /// </summary>
    public IReadOnlyList<StorageProof> StorageProofs { get; init; } = Array.Empty<StorageProof>();

    /// <summary>True if this payload would encode as v3.</summary>
    public bool IsV3 => StorageProofs.Count > 0;

    /// <summary>True if this payload would encode as v2 (has tx witness, no storage proofs).</summary>
    public bool IsV2 => StorageProofs.Count == 0 && !DisputedTxBytes.IsEmpty;

    /// <summary>True if this payload would encode as v1 (no tx witness, no storage proofs).</summary>
    public bool IsV1 => StorageProofs.Count == 0 && DisputedTxBytes.IsEmpty;

    /// <summary>Encoded size in bytes (varies by version).</summary>
    public int EncodedSize
    {
        get
        {
            if (IsV1) return Size;
            var size = V2HeaderSize + DisputedTxBytes.Length;
            if (IsV3)
            {
                size += 4;  // numStorageProofs (uint32)
                foreach (var p in StorageProofs) size += p.EncodedSize;
            }
            return size;
        }
    }

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
        if (StorageProofs.Count > MaxStorageProofsPerPayload)
            throw new InvalidOperationException(
                $"StorageProofs count {StorageProofs.Count} exceeds MaxStorageProofsPerPayload ({MaxStorageProofsPerPayload})");

        // Pre-validate each storage proof's caps (null-safe + size guards) before
        // allocating the buffer, so an oversized proof fails early rather than after
        // a partial write into a too-small buffer.
        foreach (var p in StorageProofs)
        {
            ArgumentNullException.ThrowIfNull(p);
            // EncodedSize getter validates internally; reading it triggers the cap checks.
            _ = p.EncodedSize;
        }

        var totalSize = EncodedSize;
        var buffer = new byte[totalSize];
        var span = buffer.AsSpan();

        // Version byte dispatch: v3 if storage proofs, else v2 if tx witness, else v1.
        if (IsV3) span[0] = Version3;
        else if (IsV2) span[0] = Version2;
        else span[0] = Version;

        // v1 tail (always present in all versions).
        PreStateRoot.GetSpan().CopyTo(span.Slice(1, 32));
        ClaimedPostStateRoot.GetSpan().CopyTo(span.Slice(33, 32));
        ReplayedPostStateRoot.GetSpan().CopyTo(span.Slice(65, 32));
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(97, 4), DisputedTxIndex);
        if (IsV1) return buffer;

        // v2+ tail: disputed-tx witness (length-prefixed).
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(101, 4), (uint)DisputedTxBytes.Length);
        DisputedTxBytes.Span.CopyTo(span.Slice(V2HeaderSize));
        if (IsV2) return buffer;

        // v3 tail: storage-proof manifests (count + N proofs).
        var pos = V2HeaderSize + DisputedTxBytes.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(pos, 4), (uint)StorageProofs.Count);
        pos += 4;
        foreach (var p in StorageProofs)
        {
            pos += p.Encode(span.Slice(pos));
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
        if (version == Version2 || version == Version3)
        {
            if (bytes.Length < V2HeaderSize)
                throw new ArgumentException(
                    $"v{version} payload must be ≥ {V2HeaderSize} bytes (header), got {bytes.Length}");
            var declaredLen = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(101, 4));
            if (declaredLen > MaxDisputedTxBytes)
                throw new ArgumentException(
                    $"v{version} disputedTxLen {declaredLen} exceeds MaxDisputedTxBytes ({MaxDisputedTxBytes})");
            if (bytes.Length < V2HeaderSize + declaredLen)
                throw new ArgumentException(
                    $"v{version} payload length {bytes.Length} < header({V2HeaderSize}) + disputedTxLen({declaredLen})");
            var txBytes = new byte[declaredLen];
            bytes.Slice(V2HeaderSize, (int)declaredLen).CopyTo(txBytes);

            // For v2: strict length match. For v3: continue past the v2 tail to read storage proofs.
            if (version == Version2)
            {
                if (bytes.Length != V2HeaderSize + declaredLen)
                    throw new ArgumentException(
                        $"v2 payload length {bytes.Length} != header({V2HeaderSize}) + disputedTxLen({declaredLen})");
                return new FraudProofPayload
                {
                    PreStateRoot = new UInt256(bytes.Slice(1, 32)),
                    ClaimedPostStateRoot = new UInt256(bytes.Slice(33, 32)),
                    ReplayedPostStateRoot = new UInt256(bytes.Slice(65, 32)),
                    DisputedTxIndex = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(97, 4)),
                    DisputedTxBytes = txBytes,
                };
            }

            // v3: parse storage-proof manifests after the v2 tail.
            var pos = V2HeaderSize + (int)declaredLen;
            if (bytes.Length < pos + 4)
                throw new ArgumentException("v3 payload: truncated numStorageProofs");
            var numProofs = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(pos, 4));
            pos += 4;
            if (numProofs > MaxStorageProofsPerPayload)
                throw new ArgumentException(
                    $"v3 storageProofs count {numProofs} exceeds MaxStorageProofsPerPayload ({MaxStorageProofsPerPayload})");
            // v3 must claim at least one storage proof (otherwise it should be v2).
            if (numProofs == 0)
                throw new ArgumentException("v3 payload claims 0 storage proofs (use v2 instead)");

            var proofs = new List<StorageProof>((int)numProofs);
            for (var i = 0; i < numProofs; i++)
            {
                var (p, consumed) = StorageProof.Decode(bytes.Slice(pos));
                proofs.Add(p);
                pos += consumed;
            }
            // Strict total-length match: extra trailing bytes after all proofs is malformed.
            if (pos != bytes.Length)
                throw new ArgumentException(
                    $"v3 payload has {bytes.Length - pos} trailing bytes after {numProofs} storage proofs");

            return new FraudProofPayload
            {
                PreStateRoot = new UInt256(bytes.Slice(1, 32)),
                ClaimedPostStateRoot = new UInt256(bytes.Slice(33, 32)),
                ReplayedPostStateRoot = new UInt256(bytes.Slice(65, 32)),
                DisputedTxIndex = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(97, 4)),
                DisputedTxBytes = txBytes,
                StorageProofs = proofs,
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
        if (!DisputedTxBytes.Span.SequenceEqual(other.DisputedTxBytes.Span)) return false;
        if (StorageProofs.Count != other.StorageProofs.Count) return false;
        for (var i = 0; i < StorageProofs.Count; i++)
            if (!Equals(StorageProofs[i], other.StorageProofs[i])) return false;
        return true;
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
        // Equals includes every StorageProof; GetHashCode must participate so
        // distinct v3 payloads cannot collide as dictionary/set keys.
        hc.Add(StorageProofs.Count);
        for (var i = 0; i < StorageProofs.Count; i++)
            hc.Add(StorageProofs[i]);
        return hc.ToHashCode();
    }
}
