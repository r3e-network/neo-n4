using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace NeoHub.RestrictedExecutionFraudVerifier;

/// <summary>
/// On-chain v3 fraud verifier — re-derives pre/post state roots from each storage
/// proof's leaf-hash + siblings + leafIndex and checks them against the payload's
/// header roots.
/// </summary>
/// <remarks>
/// <para>
/// Trustless companion to <c>NeoHub.GovernanceFraudVerifier</c>. The governance
/// verifier stops at structural checks (length / version / claimed != replayed) and
/// defers correctness arbitration to the security council; this verifier requires the
/// challenger to supply storage-proof manifests for every key the disputed tx touched
/// and rejects the proof on-chain if the supplied storage manifests don't reconstruct
/// to the payload's <c>PreStateRoot</c> and <c>ReplayedPostStateRoot</c>.
/// </para>
/// <para>
/// What this proves on-chain:
/// </para>
/// <list type="bullet">
///   <item><description>The challenger's storage proofs are well-formed: each proof's
///     pre-leaf folds with its siblings + leafIndex bits to a root that matches the
///     header's <c>PreStateRoot</c>, and same on the post side against
///     <c>ReplayedPostStateRoot</c>.</description></item>
///   <item><description>The challenger claims a real discrepancy:
///     <c>ClaimedPostStateRoot != ReplayedPostStateRoot</c>.</description></item>
/// </list>
/// <para>
/// What this verifier does NOT prove: that re-running the disputed transaction on the
/// pre-state actually produces the challenger's claimed post-state. That requires
/// running NeoVM with restricted state on L1 — substantial multi-iteration work and
/// the natural follow-on to this contract. Until then, "accepted by this verifier"
/// means "the challenger has made a structurally credible claim a downstream re-execution
/// service (or council) should arbitrate."
/// </para>
/// <para>
/// Hash composition mirrors <c>Neo.L2.Executor.State.KeyedStateStore.HashEntry</c>:
/// <c>Hash256(int32LE(keyLen) || key || int32LE(valueLen) || value)</c>. Sibling
/// folding mirrors <c>NeoHub.SettlementManager.VerifyWithdrawalLeafWithProof</c>:
/// <c>Hash256(left || right)</c> with <c>leafIndex</c>'s low bit at level <c>i</c>
/// determining whether <c>current</c> is left (bit=0) or right (bit=1) of the sibling
/// at that level.
/// </para>
/// <para>
/// Wire format the verifier consumes is canonical
/// <c>Neo.L2.Challenge.FraudProofPayload</c> v3 bytes:
/// </para>
/// <code>
/// [0]              version = 3
/// [1..32]          PreStateRoot (32B)
/// [33..64]         ClaimedPostStateRoot (32B)
/// [65..96]         ReplayedPostStateRoot (32B)
/// [97..100]        DisputedTxIndex (uint32 LE)
/// [101..104]       DisputedTxLen (uint32 LE)
/// [105..(105+T-1)] DisputedTxBytes (T bytes — re-execution witness, NOT verified here)
/// [...]            uint32 LE numStorageProofs
/// [for each StorageProof:]
///   uint16 LE keyLen ; key bytes
///   uint32 LE preValueLen ; preValue bytes
///   uint32 LE postValueLen ; postValue bytes
///   uint64 LE leafIndex
///   uint8  preSiblingCount ; preSiblingCount × 32 bytes
///   uint8  postSiblingCount ; postSiblingCount × 32 bytes
/// </code>
/// <para>
/// Wire layout MUST stay in lockstep with <c>FraudProofPayload.Encode</c> /
/// <c>StorageProof.Encode</c>. The off-chain <c>UT_GovernanceFraudVerifierParity</c>
/// pattern + <c>V3StorageProofVerifier</c> reference enable parity tests once we
/// re-host this verifier in tests.
/// </para>
/// <para>
/// See doc.md §15 (optimistic challenge), §17 mitigation #2.
/// </para>
/// </remarks>
[DisplayName("NeoHub.RestrictedExecutionFraudVerifier")]
[ContractAuthor("Neo Project", "dev@neo.org")]
[ContractDescription("Trustless v3 fraud verifier — checks storage-proof manifests against the payload's pre/post state roots.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.RestrictedExecutionFraudVerifier")]
[ContractPermission(Permission.Any, Method.Any)]
public class RestrictedExecutionFraudVerifierContract : SmartContract
{
    /// <summary>v1 wire-format header size (= 101 bytes — must match Neo.L2.Challenge.FraudProofPayload.Size).</summary>
    public const int V1HeaderSize = 1 + 32 + 32 + 32 + 4;

    /// <summary>v2 wire-format header size (= v1 + 4-byte witness length prefix).</summary>
    public const int V2HeaderSize = V1HeaderSize + 4;

    /// <summary>v3 wire-format version byte — must match FraudProofPayload.Version3.</summary>
    public const byte SupportedVersion3 = 3;

    /// <summary>Cap on the v2 disputed-tx witness — must match FraudProofPayload.MaxDisputedTxBytes.</summary>
    public const int MaxDisputedTxBytes = 64 * 1024;

    /// <summary>Cap on the number of storage proofs in one payload — must match FraudProofPayload.MaxStorageProofsPerPayload.</summary>
    public const int MaxStorageProofsPerPayload = 32;

    /// <summary>Cap on a single storage key's length — must match StorageProof.MaxKeyBytes.</summary>
    public const int MaxKeyBytes = 256;

    /// <summary>Cap on a single value's length (pre or post) — must match StorageProof.MaxValueBytes.</summary>
    public const int MaxValueBytes = 4096;

    /// <summary>Cap on Merkle tree depth (= sibling count per side) — must match StorageProof.MaxSiblingDepth.</summary>
    public const int MaxSiblingDepth = 64;

    /// <summary>Reject reason: payload bytes are structurally malformed (truncated / extra trailing bytes / cap violation).</summary>
    public const byte ReasonBadLength = 1;

    /// <summary>Reject reason: payload version byte is not 3 (use GovernanceFraudVerifier for v1/v2).</summary>
    public const byte ReasonBadVersion = 2;

    /// <summary>Reject reason: claimedPostStateRoot equals replayedPostStateRoot — no discrepancy claimed.</summary>
    public const byte ReasonNoDiscrepancy = 3;

    /// <summary>Reject reason: declared disputed-tx witness length exceeds MaxDisputedTxBytes.</summary>
    public const byte ReasonOversizedWitness = 4;

    /// <summary>Reject reason: any storage proof violates per-proof caps (key/value/sibling).</summary>
    public const byte ReasonInvalidStorageProof = 5;

    /// <summary>Reject reason: storage-proof count is zero (use v2 instead) or exceeds MaxStorageProofsPerPayload.</summary>
    public const byte ReasonProofCountInvalid = 6;

    /// <summary>Reject reason: a storage proof's pre-derived Merkle root != payload.PreStateRoot.</summary>
    public const byte ReasonPreStateRootMismatch = 7;

    /// <summary>Reject reason: a storage proof's post-derived Merkle root != payload.ReplayedPostStateRoot.</summary>
    public const byte ReasonReplayedPostStateRootMismatch = 8;

    /// <summary>Emitted on every accepted v3 fraud proof. Surfaces both state roots so a downstream re-execution service can arbitrate without re-decoding.</summary>
    [DisplayName("FraudProofAccepted")]
    public static event Action<uint, ulong, UInt256, UInt256> OnFraudProofAccepted = default!;

    /// <summary>Emitted on every rejected v3 fraud proof. The reason byte names the failure mode (constants above).</summary>
    [DisplayName("FraudProofRejected")]
    public static event Action<uint, ulong, byte> OnFraudProofRejected = default!;

    /// <summary>
    /// Verify a v3 fraud-proof payload by re-deriving pre/post state roots from each
    /// storage proof and matching against the payload's header roots.
    /// </summary>
    /// <param name="chainId">L2 chain id (passed through from <c>OptimisticChallenge.Challenge</c>).</param>
    /// <param name="batchNumber">Disputed batch number (passed through).</param>
    /// <param name="payload">Canonical v3 <c>FraudProofPayload</c> bytes.</param>
    /// <returns>
    /// True when the payload is well-formed v3 AND every storage proof reconstructs to
    /// the header's pre/post roots AND <c>ClaimedPostStateRoot != ReplayedPostStateRoot</c>.
    /// False otherwise — the rejected event names the specific failure mode.
    /// </returns>
    [Safe]
    public static bool VerifyFraud(uint chainId, ulong batchNumber, byte[] payload)
    {
        // Need at least the v1 header to dispatch the version byte.
        if (payload.Length < 1)
        {
            OnFraudProofRejected(chainId, batchNumber, ReasonBadLength);
            return false;
        }
        if (payload[0] != SupportedVersion3)
        {
            OnFraudProofRejected(chainId, batchNumber, ReasonBadVersion);
            return false;
        }
        if (payload.Length < V2HeaderSize)
        {
            OnFraudProofRejected(chainId, batchNumber, ReasonBadLength);
            return false;
        }

        // Read uint32 LE at offset 101..104 = declared disputed-tx witness length.
        var declaredTxLen = ReadUInt32LE(payload, 101);
        if (declaredTxLen > MaxDisputedTxBytes)
        {
            OnFraudProofRejected(chainId, batchNumber, ReasonOversizedWitness);
            return false;
        }
        // v2 header + tx witness = base offset where storage-proof manifests start.
        var pos = (int)(V2HeaderSize + declaredTxLen);
        if (payload.Length < pos + 4)
        {
            OnFraudProofRejected(chainId, batchNumber, ReasonBadLength);
            return false;
        }

        // Discrepancy claim: claimed != replayed. Same NoDiscrepancy short-circuit as
        // the off-chain V3StorageProofVerifier — without this check a challenger could
        // submit a payload with claimed == replayed and (vacuously) verify any proof.
        if (BytesEqual(payload, 33, payload, 65, 32))
        {
            OnFraudProofRejected(chainId, batchNumber, ReasonNoDiscrepancy);
            return false;
        }

        var numProofs = ReadUInt32LE(payload, pos);
        pos += 4;
        // v3 must claim ≥ 1 storage proof (else use v2). Also bounded above.
        if (numProofs == 0 || numProofs > MaxStorageProofsPerPayload)
        {
            OnFraudProofRejected(chainId, batchNumber, ReasonProofCountInvalid);
            return false;
        }

        // For each storage proof: decode + re-derive pre-root + post-root + match.
        for (var i = 0u; i < numProofs; i++)
        {
            // ---- decode keyLen + key ----
            if (payload.Length < pos + 2)
            {
                OnFraudProofRejected(chainId, batchNumber, ReasonBadLength);
                return false;
            }
            var keyLen = ReadUInt16LE(payload, pos); pos += 2;
            if (keyLen > MaxKeyBytes)
            {
                OnFraudProofRejected(chainId, batchNumber, ReasonInvalidStorageProof);
                return false;
            }
            if (payload.Length < pos + keyLen)
            {
                OnFraudProofRejected(chainId, batchNumber, ReasonBadLength);
                return false;
            }
            var keyOffset = pos;
            pos += keyLen;

            // ---- decode preValueLen + preValue ----
            if (payload.Length < pos + 4)
            {
                OnFraudProofRejected(chainId, batchNumber, ReasonBadLength);
                return false;
            }
            var preLen = ReadUInt32LE(payload, pos); pos += 4;
            if (preLen > MaxValueBytes)
            {
                OnFraudProofRejected(chainId, batchNumber, ReasonInvalidStorageProof);
                return false;
            }
            if (payload.Length < pos + (int)preLen)
            {
                OnFraudProofRejected(chainId, batchNumber, ReasonBadLength);
                return false;
            }
            var preOffset = pos;
            pos += (int)preLen;

            // ---- decode postValueLen + postValue ----
            if (payload.Length < pos + 4)
            {
                OnFraudProofRejected(chainId, batchNumber, ReasonBadLength);
                return false;
            }
            var postLen = ReadUInt32LE(payload, pos); pos += 4;
            if (postLen > MaxValueBytes)
            {
                OnFraudProofRejected(chainId, batchNumber, ReasonInvalidStorageProof);
                return false;
            }
            if (payload.Length < pos + (int)postLen)
            {
                OnFraudProofRejected(chainId, batchNumber, ReasonBadLength);
                return false;
            }
            var postOffset = pos;
            pos += (int)postLen;

            // ---- decode leafIndex ----
            if (payload.Length < pos + 8)
            {
                OnFraudProofRejected(chainId, batchNumber, ReasonBadLength);
                return false;
            }
            var leafIndex = ReadUInt64LE(payload, pos); pos += 8;

            // ---- decode preSiblingCount + preSiblings ----
            if (payload.Length < pos + 1)
            {
                OnFraudProofRejected(chainId, batchNumber, ReasonBadLength);
                return false;
            }
            var preSibCount = (int)payload[pos]; pos += 1;
            if (preSibCount > MaxSiblingDepth)
            {
                OnFraudProofRejected(chainId, batchNumber, ReasonInvalidStorageProof);
                return false;
            }
            if (payload.Length < pos + 32 * preSibCount)
            {
                OnFraudProofRejected(chainId, batchNumber, ReasonBadLength);
                return false;
            }
            var preSibOffset = pos;
            pos += 32 * preSibCount;

            // ---- decode postSiblingCount + postSiblings ----
            if (payload.Length < pos + 1)
            {
                OnFraudProofRejected(chainId, batchNumber, ReasonBadLength);
                return false;
            }
            var postSibCount = (int)payload[pos]; pos += 1;
            if (postSibCount > MaxSiblingDepth)
            {
                OnFraudProofRejected(chainId, batchNumber, ReasonInvalidStorageProof);
                return false;
            }
            if (payload.Length < pos + 32 * postSibCount)
            {
                OnFraudProofRejected(chainId, batchNumber, ReasonBadLength);
                return false;
            }
            var postSibOffset = pos;
            pos += 32 * postSibCount;

            // ---- re-derive pre-root + match ----
            var preLeaf = HashEntry(payload, keyOffset, keyLen, preOffset, (int)preLen);
            var preRoot = FoldMerkleProof(preLeaf, payload, preSibOffset, preSibCount, leafIndex);
            if (!BytesEqual(preRoot, 0, payload, 1, 32))
            {
                OnFraudProofRejected(chainId, batchNumber, ReasonPreStateRootMismatch);
                return false;
            }

            // ---- re-derive post-root + match ----
            var postLeaf = HashEntry(payload, keyOffset, keyLen, postOffset, (int)postLen);
            var postRoot = FoldMerkleProof(postLeaf, payload, postSibOffset, postSibCount, leafIndex);
            if (!BytesEqual(postRoot, 0, payload, 65, 32))
            {
                OnFraudProofRejected(chainId, batchNumber, ReasonReplayedPostStateRootMismatch);
                return false;
            }
        }

        // Strict total-length match: extra trailing bytes after all proofs is malformed.
        if (pos != payload.Length)
        {
            OnFraudProofRejected(chainId, batchNumber, ReasonBadLength);
            return false;
        }

        // All proofs reconstruct cleanly + claimed != replayed. Surface both state
        // roots so a downstream re-execution service has them in the event log.
        var claimedRoot = SliceUInt256(payload, 33);
        var replayedRoot = SliceUInt256(payload, 65);
        OnFraudProofAccepted(chainId, batchNumber, claimedRoot, replayedRoot);
        return true;
    }

    /// <summary>
    /// Compute the canonical leaf hash for a (key, value) pair —
    /// <c>Hash256(int32LE(keyLen) || key || int32LE(valueLen) || value)</c>.
    /// Reads from <paramref name="payload"/> by offset to avoid allocating intermediate
    /// key/value byte[]s on every storage proof.
    /// </summary>
    private static byte[] HashEntry(byte[] payload, int keyOffset, int keyLen, int valOffset, int valLen)
    {
        var bufLen = 4 + keyLen + 4 + valLen;
        var buf = new byte[bufLen];
        // int32LE(keyLen)
        buf[0] = (byte)(keyLen & 0xFF);
        buf[1] = (byte)((keyLen >> 8) & 0xFF);
        buf[2] = (byte)((keyLen >> 16) & 0xFF);
        buf[3] = (byte)((keyLen >> 24) & 0xFF);
        for (var i = 0; i < keyLen; i++) buf[4 + i] = payload[keyOffset + i];
        var p = 4 + keyLen;
        // int32LE(valLen)
        buf[p + 0] = (byte)(valLen & 0xFF);
        buf[p + 1] = (byte)((valLen >> 8) & 0xFF);
        buf[p + 2] = (byte)((valLen >> 16) & 0xFF);
        buf[p + 3] = (byte)((valLen >> 24) & 0xFF);
        for (var i = 0; i < valLen; i++) buf[p + 4 + i] = payload[valOffset + i];
        var h1 = CryptoLib.Sha256((ByteString)buf);
        return (byte[])CryptoLib.Sha256(h1);
    }

    /// <summary>
    /// Fold a leaf hash with its sibling list using leaf-index bits to decide left/right
    /// ordering. Mirrors <c>SettlementManager.VerifyWithdrawalLeafWithProof</c>'s fold
    /// loop. Reads siblings directly from <paramref name="payload"/> to avoid copying.
    /// </summary>
    private static byte[] FoldMerkleProof(
        byte[] leafHash,
        byte[] payload,
        int sibOffset,
        int sibCount,
        ulong leafIndex)
    {
        var current = leafHash;
        var index = leafIndex;
        for (var i = 0; i < sibCount; i++)
        {
            var combined = new byte[64];
            var sibBase = sibOffset + 32 * i;
            if ((index & 1UL) == 0UL)
            {
                // current on the left, sibling on the right.
                for (var j = 0; j < 32; j++) combined[j] = current[j];
                for (var j = 0; j < 32; j++) combined[32 + j] = payload[sibBase + j];
            }
            else
            {
                // sibling on the left, current on the right.
                for (var j = 0; j < 32; j++) combined[j] = payload[sibBase + j];
                for (var j = 0; j < 32; j++) combined[32 + j] = current[j];
            }
            // Hash256 = Sha256(Sha256(x)).
            var h1 = CryptoLib.Sha256((ByteString)combined);
            current = (byte[])CryptoLib.Sha256(h1);
            index = index >> 1;
        }
        return current;
    }

    private static UInt256 SliceUInt256(byte[] src, int offset)
    {
        var buf = new byte[32];
        for (var i = 0; i < 32; i++) buf[i] = src[offset + i];
        return (UInt256)buf;
    }

    private static ushort ReadUInt16LE(byte[] src, int offset) =>
        (ushort)(src[offset] | (src[offset + 1] << 8));

    private static uint ReadUInt32LE(byte[] src, int offset) =>
        (uint)src[offset]
        | ((uint)src[offset + 1] << 8)
        | ((uint)src[offset + 2] << 16)
        | ((uint)src[offset + 3] << 24);

    private static ulong ReadUInt64LE(byte[] src, int offset) =>
        ((ulong)src[offset])
        | ((ulong)src[offset + 1] << 8)
        | ((ulong)src[offset + 2] << 16)
        | ((ulong)src[offset + 3] << 24)
        | ((ulong)src[offset + 4] << 32)
        | ((ulong)src[offset + 5] << 40)
        | ((ulong)src[offset + 6] << 48)
        | ((ulong)src[offset + 7] << 56);

    private static bool BytesEqual(byte[] a, int aOffset, byte[] b, int bOffset, int length)
    {
        for (var i = 0; i < length; i++)
        {
            if (a[aOffset + i] != b[bOffset + i]) return false;
        }
        return true;
    }
}
