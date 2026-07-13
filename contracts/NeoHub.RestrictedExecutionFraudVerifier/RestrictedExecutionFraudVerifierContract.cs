using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace NeoHub.RestrictedExecutionFraudVerifier;

/// <summary>
/// Versioned fraud verifier with a governance-only structural v3 path and a
/// SettlementManager-bound executable v4 path.
/// </summary>
/// <remarks>
/// <para>
/// Version 3 preserves the canonical <c>FraudProofPayload</c> storage-proof format, but
/// validates only challenger-supplied roots. It is structural evidence for governance
/// arbitration and is never sufficient for permissionless slashing.
/// </para>
/// <para>
/// Version 4 reads the exact 321-byte optimistic batch header from
/// <c>SettlementManager.GetChallengeableBatchHeader</c>, binds the header hash, chain,
/// batch, transaction root, pre-state root, committed post-state root, transaction index,
/// settled bisection bounds, executor semantic id, replay domain, transcript hash, witness
/// hash, and claim id. It then verifies the canonical single-leaf transaction proof and
/// executes the supported existing-key Counter Increment transition against an old leaf/path
/// bound to the committed pre-state root and a new leaf/path bound to the committed post-state root.
/// </para>
/// <para>
/// A correct committed transition returns false. The verifier returns true only when the
/// supported transition reconstructs the committed pre-state root and derives a post-state
/// root different from the committed batch post-state root. Malformed, substituted,
/// replay-domain-mismatched, or unsupported payloads fail closed.
/// </para>
/// <para>
/// The v4 semantic id is
/// <c>Hash256("neo4-executor:counter-increment-existing-key:v1")</c>. This is not a
/// general NeoVM verifier. It supports exactly one 29-byte Counter Increment transaction,
/// transaction index 0, transaction count 1, bisection interval [0,1], and mutation of one
/// existing state key. Multi-transaction batches, key insertion/deletion, other custom
/// executors, and general NeoVM opcodes are not covered.
/// </para>
/// <para>
/// Hash composition and wire layout stay in lockstep with
/// <c>Neo.L2.Challenge.RestrictedFraudProofV4</c>,
/// <c>Neo.L2.State.MerkleProofSerializer</c>, and
/// <c>Neo.L2.Challenge.StorageProof</c>. See doc.md §15 and §17.
/// </para>
/// </remarks>
[DisplayName("NeoHub.RestrictedExecutionFraudVerifier")]
[ContractAuthor("Neo Project", "dev@neo.org")]
[ContractDescription("Versioned fraud verifier: governance-only structural v3 plus SettlementManager-bound executable v4 Counter transitions.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.RestrictedExecutionFraudVerifier")]
[ContractPermission(Permission.Any, Method.Any)]
public class RestrictedExecutionFraudVerifierContract : SmartContract
{
    private const byte KeySettlementManager = 0x01;
    private const byte KeyReplayDomain = 0x02;

    /// <summary>v1 wire-format header size (= 101 bytes — must match Neo.L2.Challenge.FraudProofPayload.Size).</summary>
    public const int V1HeaderSize = 1 + 32 + 32 + 32 + 4;

    /// <summary>v2 wire-format header size (= v1 + 4-byte witness length prefix).</summary>
    public const int V2HeaderSize = V1HeaderSize + 4;

    /// <summary>v3 wire-format version byte — must match FraudProofPayload.Version3.</summary>
    public const byte SupportedVersion3 = 3;

    /// <summary>v4 trustless restricted-execution version.</summary>
    public const byte SupportedVersion4 = 4;

    /// <summary>Canonical v4 fixed header size through disputedTxLen.</summary>
    public const int V4FixedHeaderSize = 353;

    /// <summary>Canonical L2BatchCommitment fixed header size.</summary>
    public const int CommitmentHeaderSize = 321;

    /// <summary>Canonical MerkleProofSerializer header size.</summary>
    public const int MerkleProofHeaderSize = 48;

    private const int V4ReplayDomainOffset = 1;
    private const int V4ExecutorSemanticIdOffset = 33;
    private const int V4ClaimIdOffset = 65;
    private const int V4TranscriptHashOffset = 97;
    private const int V4WitnessHashOffset = 129;
    private const int V4CommittedHeaderHashOffset = 161;
    private const int V4ChainIdOffset = 193;
    private const int V4BatchNumberOffset = 197;
    private const int V4DisputedTxIndexOffset = 205;
    private const int V4TransactionCountOffset = 209;
    private const int V4LowerBoundOffset = 213;
    private const int V4UpperBoundOffset = 217;
    private const int V4PreStateRootOffset = 221;
    private const int V4CommittedPostStateRootOffset = 253;
    private const int V4ExpectedPostStateRootOffset = 285;
    private const int V4TxRootOffset = 317;
    private const int V4TxLengthOffset = 349;

    private const int HeaderChainIdOffset = 0;
    private const int HeaderBatchNumberOffset = 4;
    private const int HeaderPreStateRootOffset = 28;
    private const int HeaderPostStateRootOffset = 60;
    private const int HeaderTxRootOffset = 92;
    private const int HeaderProofTypeOffset = 316;
    private const byte OptimisticProofType = 2;
    private const byte IncrementCounterOpcode = 1;

    private static readonly byte[] ExecutorSemanticTag = new byte[]
    {
        110, 101, 111, 52, 45, 101, 120, 101, 99, 117, 116, 111, 114, 58, 99, 111,
        117, 110, 116, 101, 114, 45, 105, 110, 99, 114, 101, 109, 101, 110, 116, 45,
        101, 120, 105, 115, 116, 105, 110, 103, 45, 107, 101, 121, 58, 118, 49
    };

    private static readonly byte[] TranscriptTag = new byte[]
    {
        110, 101, 111, 52, 45, 102, 114, 97, 117, 100, 45, 98, 105, 115, 101, 99,
        116, 105, 111, 110, 45, 116, 114, 97, 110, 115, 99, 114, 105, 112, 116, 58,
        118, 49
    };

    private static readonly byte[] WitnessTag = new byte[]
    {
        110, 101, 111, 52, 45, 102, 114, 97, 117, 100, 45, 119, 105, 116, 110, 101,
        115, 115, 58, 118, 49
    };

    private static readonly byte[] ClaimTag = new byte[]
    {
        110, 101, 111, 52, 45, 102, 114, 97, 117, 100, 45, 99, 108, 97, 105, 109,
        58, 118, 52
    };

    private static readonly byte[] CounterKeyPrefix = new byte[]
    {
        99, 111, 117, 110, 116, 101, 114, 58
    };

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

    /// <summary>Reject reason: v4 verifier was deployed without SettlementManager/replay-domain binding.</summary>
    public const byte ReasonV4NotConfigured = 9;

    /// <summary>Reject reason: replay domain does not match the configured deployment.</summary>
    public const byte ReasonReplayDomainMismatch = 10;

    /// <summary>Reject reason: executor semantic id is not the supported restricted profile.</summary>
    public const byte ReasonExecutorSemanticMismatch = 11;

    /// <summary>Reject reason: payload chain, batch, or settled single-step bounds are invalid.</summary>
    public const byte ReasonContextMismatch = 12;

    /// <summary>Reject reason: payload roots/header hash do not match SettlementManager storage.</summary>
    public const byte ReasonCommittedHeaderMismatch = 13;

    /// <summary>Reject reason: canonical final-step transcript hash mismatch.</summary>
    public const byte ReasonTranscriptMismatch = 14;

    /// <summary>Reject reason: transaction/state witness hash mismatch.</summary>
    public const byte ReasonWitnessMismatch = 15;

    /// <summary>Reject reason: replay-protected claim id mismatch.</summary>
    public const byte ReasonClaimIdMismatch = 16;

    /// <summary>Reject reason: transaction is not the single canonical tx committed by txRoot.</summary>
    public const byte ReasonTransactionProofMismatch = 17;

    /// <summary>Reject reason: transaction is outside the supported Counter Increment semantics.</summary>
    public const byte ReasonUnsupportedTransition = 18;

    /// <summary>Reject reason: storage key/value/path witness is invalid.</summary>
    public const byte ReasonStorageWitnessMismatch = 19;

    /// <summary>Reject reason: payload expectedPostStateRoot differs from executed semantics.</summary>
    public const byte ReasonExpectedPostStateRootMismatch = 20;

    /// <summary>Reject reason: the committed post-state root is correct, so no fraud exists.</summary>
    public const byte ReasonNoFraud = 21;

    /// <summary>Emitted on every accepted v3 fraud proof. Surfaces both state roots so a downstream re-execution service can arbitrate without re-decoding.</summary>
    [DisplayName("FraudProofAccepted")]
    public static event Action<uint, ulong, UInt256, UInt256> OnFraudProofAccepted = default!;

    /// <summary>Emitted on every rejected v3 fraud proof. The reason byte names the failure mode (constants above).</summary>
    [DisplayName("FraudProofRejected")]
    public static event Action<uint, ulong, byte> OnFraudProofRejected = default!;

    /// <summary>Emitted when an executable v4 claim proves actual fraud.</summary>
    [DisplayName("TrustlessFraudProofAccepted")]
    public static event Action<uint, ulong, UInt256> OnTrustlessFraudProofAccepted = default!;

    /// <summary>
    /// Configure the immutable v4 SettlementManager and replay-domain binding at deployment.
    /// Empty deployment data keeps governance-only v3 compatibility but leaves v4 fail closed.
    /// </summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var values = (object[])data;
        if (values.Length == 0) return;
        ExecutionEngine.Assert(values.Length == 2, "expected [settlementManager, replayDomain]");
        var settlementManager = (UInt160)values[0];
        var replayDomain = (UInt256)values[1];
        ExecutionEngine.Assert(settlementManager.IsValid && !settlementManager.IsZero,
            "invalid settlement manager");
        ExecutionEngine.Assert(!replayDomain.Equals(UInt256.Zero), "replay domain is zero");
        Storage.Put(new byte[] { KeySettlementManager }, settlementManager);
        Storage.Put(new byte[] { KeyReplayDomain }, replayDomain);
    }

    /// <summary>SettlementManager whose committed headers v4 reads.</summary>
    [Safe]
    public static UInt160 GetSettlementManager()
    {
        var raw = Storage.Get(new byte[] { KeySettlementManager });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Deployment-specific v4 replay domain.</summary>
    [Safe]
    public static UInt256 GetReplayDomain()
    {
        var raw = Storage.Get(new byte[] { KeyReplayDomain });
        return raw == null ? UInt256.Zero : (UInt256)raw;
    }

    /// <summary>Semantic id for the supported existing-key Counter Increment transition.</summary>
    [Safe]
    public static UInt256 GetExecutorSemanticId()
    {
        return (UInt256)Hash256(ExecutorSemanticTag);
    }

    /// <summary>
    /// Verify a v3 fraud-proof payload by re-deriving pre/post state roots from each
    /// storage proof and matching against the payload's own header roots. NOTE: this binds
    /// the proofs only to the challenger-supplied header, NOT to the sequencer's on-chain
    /// batch commitment — <paramref name="chainId"/>/<paramref name="batchNumber"/> are used
    /// only for event emission. See the class remarks for the trust limitation.
    /// </summary>
    /// <param name="chainId">L2 chain id (passed through from <c>OptimisticChallenge.Challenge</c>; event-only here).</param>
    /// <param name="batchNumber">Disputed batch number (passed through; event-only here).</param>
    /// <param name="payload">Canonical v3 <c>FraudProofPayload</c> bytes.</param>
    /// <returns>
    /// True when the payload is well-formed v3 AND every storage proof reconstructs to
    /// the payload header's pre/post roots AND <c>ClaimedPostStateRoot != ReplayedPostStateRoot</c>.
    /// False otherwise — the rejected event names the specific failure mode.
    /// </returns>
    // NOT [Safe]: this method emits diagnostic events (Runtime.Notify), which requires the
    // AllowNotify call flag. A [Safe] method is invoked read-only (ReadStates|AllowCall), so the
    // Notify would FAULT — OptimisticChallenge.Challenge dispatches here with
    // CallFlags.ReadOnly|AllowNotify expressly so these reason-coded events can fire.
    public static bool VerifyFraud(uint chainId, ulong batchNumber, byte[] payload)
    {
        if (payload.Length < 1)
        {
            OnFraudProofRejected(chainId, batchNumber, ReasonBadLength);
            return false;
        }
        if (payload[0] == SupportedVersion4)
            return VerifyTrustlessV4(chainId, batchNumber, payload);
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
    /// Verify a SettlementManager-bound v4 proof by executing the supported existing-key Counter
    /// Increment transition and comparing its derived root with the committed batch post-state root.
    /// </summary>
    private static bool VerifyTrustlessV4(uint chainId, ulong batchNumber, byte[] payload)
    {
        if (payload.Length < V4FixedHeaderSize)
            return Reject(chainId, batchNumber, ReasonBadLength);

        var settlementManager = GetSettlementManager();
        var replayDomain = GetReplayDomain();
        if (settlementManager.Equals(UInt160.Zero) || replayDomain.Equals(UInt256.Zero))
            return Reject(chainId, batchNumber, ReasonV4NotConfigured);
        if (!BytesEqual(payload, V4ReplayDomainOffset, (byte[])replayDomain, 0, 32))
            return Reject(chainId, batchNumber, ReasonReplayDomainMismatch);

        var executorSemanticId = GetExecutorSemanticId();
        if (!BytesEqual(payload, V4ExecutorSemanticIdOffset, (byte[])executorSemanticId, 0, 32))
            return Reject(chainId, batchNumber, ReasonExecutorSemanticMismatch);

        if (ReadUInt32LE(payload, V4ChainIdOffset) != chainId
            || ReadUInt64LE(payload, V4BatchNumberOffset) != batchNumber
            || ReadUInt32LE(payload, V4DisputedTxIndexOffset) != 0
            || ReadUInt32LE(payload, V4TransactionCountOffset) != 1
            || ReadUInt32LE(payload, V4LowerBoundOffset) != 0
            || ReadUInt32LE(payload, V4UpperBoundOffset) != 1)
            return Reject(chainId, batchNumber, ReasonContextMismatch);

        var committedHeader = (byte[])Contract.Call(
            settlementManager,
            "getChallengeableBatchHeader",
            CallFlags.ReadOnly,
            new object[] { chainId, batchNumber });
        if (committedHeader.Length != CommitmentHeaderSize
            || ReadUInt32LE(committedHeader, HeaderChainIdOffset) != chainId
            || ReadUInt64LE(committedHeader, HeaderBatchNumberOffset) != batchNumber
            || committedHeader[HeaderProofTypeOffset] != OptimisticProofType)
            return Reject(chainId, batchNumber, ReasonCommittedHeaderMismatch);

        var committedHeaderHash = Hash256(committedHeader);
        if (!BytesEqual(payload, V4CommittedHeaderHashOffset, committedHeaderHash, 0, 32)
            || !BytesEqual(payload, V4PreStateRootOffset, committedHeader, HeaderPreStateRootOffset, 32)
            || !BytesEqual(payload, V4CommittedPostStateRootOffset, committedHeader, HeaderPostStateRootOffset, 32)
            || !BytesEqual(payload, V4TxRootOffset, committedHeader, HeaderTxRootOffset, 32))
            return Reject(chainId, batchNumber, ReasonCommittedHeaderMismatch);

        var txLength = ReadUInt32LE(payload, V4TxLengthOffset);
        if (txLength > MaxDisputedTxBytes)
            return Reject(chainId, batchNumber, ReasonOversizedWitness);
        var position = V4FixedHeaderSize + (int)txLength;
        if (payload.Length < position + 4)
            return Reject(chainId, batchNumber, ReasonBadLength);
        var transactionProofLength = ReadUInt32LE(payload, position);
        position += 4;
        if (transactionProofLength != MerkleProofHeaderSize || payload.Length < position + MerkleProofHeaderSize + 4)
            return Reject(chainId, batchNumber, ReasonTransactionProofMismatch);
        var transactionProofOffset = position;
        position += MerkleProofHeaderSize;

        var stateProofLength = ReadUInt32LE(payload, position);
        position += 4;
        if (stateProofLength > (uint)payload.Length || payload.Length != position + (int)stateProofLength)
            return Reject(chainId, batchNumber, ReasonBadLength);
        var stateProofOffset = position;
        var stateProofEnd = position + (int)stateProofLength;

        var transcriptHash = ComputeTranscriptHash(payload);
        if (!BytesEqual(payload, V4TranscriptHashOffset, transcriptHash, 0, 32))
            return Reject(chainId, batchNumber, ReasonTranscriptMismatch);
        var witnessHash = ComputeWitnessHash(payload);
        if (!BytesEqual(payload, V4WitnessHashOffset, witnessHash, 0, 32))
            return Reject(chainId, batchNumber, ReasonWitnessMismatch);
        var claimId = ComputeClaimId(payload, settlementManager, transcriptHash, witnessHash);
        if (!BytesEqual(payload, V4ClaimIdOffset, claimId, 0, 32))
            return Reject(chainId, batchNumber, ReasonClaimIdMismatch);

        if (txLength != 29 || payload[V4FixedHeaderSize] != IncrementCounterOpcode)
            return Reject(chainId, batchNumber, ReasonUnsupportedTransition);
        var disputedTx = Slice(payload, V4FixedHeaderSize, (int)txLength);
        var transactionHash = Hash256(disputedTx);
        if (!BytesEqual(payload, transactionProofOffset, transactionHash, 0, 32)
            || ReadUInt32LE(payload, transactionProofOffset + 32) != 0
            || ReadUInt64LE(payload, transactionProofOffset + 36) != 0
            || ReadUInt32LE(payload, transactionProofOffset + 44) != 0
            || !BytesEqual(payload, V4TxRootOffset, transactionHash, 0, 32))
            return Reject(chainId, batchNumber, ReasonTransactionProofMismatch);

        var storagePosition = stateProofOffset;
        if (stateProofEnd < storagePosition + 2)
            return Reject(chainId, batchNumber, ReasonBadLength);
        var keyLength = ReadUInt16LE(payload, storagePosition);
        storagePosition += 2;
        if (keyLength > MaxKeyBytes || stateProofEnd < storagePosition + keyLength)
            return Reject(chainId, batchNumber, ReasonStorageWitnessMismatch);
        var keyOffset = storagePosition;
        storagePosition += keyLength;

        if (stateProofEnd < storagePosition + 4)
            return Reject(chainId, batchNumber, ReasonBadLength);
        var preValueLength = ReadUInt32LE(payload, storagePosition);
        storagePosition += 4;
        if (preValueLength != 8 || stateProofEnd < storagePosition + 8)
            return Reject(chainId, batchNumber, ReasonStorageWitnessMismatch);
        var preValueOffset = storagePosition;
        storagePosition += 8;

        if (stateProofEnd < storagePosition + 4)
            return Reject(chainId, batchNumber, ReasonBadLength);
        var postValueLength = ReadUInt32LE(payload, storagePosition);
        storagePosition += 4;
        if (postValueLength != 8 || stateProofEnd < storagePosition + 8)
            return Reject(chainId, batchNumber, ReasonStorageWitnessMismatch);
        var postValueOffset = storagePosition;
        storagePosition += 8;

        if (stateProofEnd < storagePosition + 8)
            return Reject(chainId, batchNumber, ReasonBadLength);
        var leafIndex = ReadUInt64LE(payload, storagePosition);
        storagePosition += 8;

        if (stateProofEnd < storagePosition + 1)
            return Reject(chainId, batchNumber, ReasonBadLength);
        var preSiblingCount = (int)payload[storagePosition];
        storagePosition += 1;
        if (preSiblingCount > MaxSiblingDepth || stateProofEnd < storagePosition + 32 * preSiblingCount)
            return Reject(chainId, batchNumber, ReasonStorageWitnessMismatch);
        var preSiblingOffset = storagePosition;
        storagePosition += 32 * preSiblingCount;

        if (stateProofEnd < storagePosition + 1)
            return Reject(chainId, batchNumber, ReasonBadLength);
        var postSiblingCount = (int)payload[storagePosition];
        storagePosition += 1;
        if (postSiblingCount > MaxSiblingDepth || stateProofEnd < storagePosition + 32 * postSiblingCount)
            return Reject(chainId, batchNumber, ReasonStorageWitnessMismatch);
        var postSiblingOffset = storagePosition;
        storagePosition += 32 * postSiblingCount;
        if (storagePosition != stateProofEnd
            || !IsCanonicalLeafIndex(leafIndex, preSiblingCount)
            || !IsCanonicalLeafIndex(leafIndex, postSiblingCount))
            return Reject(chainId, batchNumber, ReasonStorageWitnessMismatch);

        if (keyLength != 28)
            return Reject(chainId, batchNumber, ReasonUnsupportedTransition);
        var counterPrefix = CounterKeyPrefix;
        for (var index = 0; index < counterPrefix.Length; index++)
            if (payload[keyOffset + index] != counterPrefix[index])
                return Reject(chainId, batchNumber, ReasonUnsupportedTransition);
        for (var index = 0; index < 20; index++)
            if (payload[keyOffset + 8 + index] != payload[V4FixedHeaderSize + 1 + index])
                return Reject(chainId, batchNumber, ReasonUnsupportedTransition);

        var previousValue = ReadUInt64LE(payload, preValueOffset);
        var amount = ReadUInt64LE(payload, V4FixedHeaderSize + 21);
        var expectedValue = unchecked(previousValue + amount);

        var preLeaf = HashEntry(payload, keyOffset, keyLength, preValueOffset, 8);
        var preRoot = FoldMerkleProof(preLeaf, payload, preSiblingOffset, preSiblingCount, leafIndex);
        if (!BytesEqual(preRoot, 0, committedHeader, HeaderPreStateRootOffset, 32))
            return Reject(chainId, batchNumber, ReasonPreStateRootMismatch);

        var committedPostLeaf = HashEntry(payload, keyOffset, keyLength, postValueOffset, 8);
        var witnessedCommittedPostRoot = FoldMerkleProof(
            committedPostLeaf,
            payload,
            postSiblingOffset,
            postSiblingCount,
            leafIndex);
        if (!BytesEqual(witnessedCommittedPostRoot, 0, committedHeader, HeaderPostStateRootOffset, 32))
            return Reject(chainId, batchNumber, ReasonStorageWitnessMismatch);

        var expectedPostLeaf = HashExpectedCounterEntry(payload, keyOffset, keyLength, expectedValue);
        var expectedPostRoot = FoldMerkleProof(
            expectedPostLeaf,
            payload,
            preSiblingOffset,
            preSiblingCount,
            leafIndex);
        if (!BytesEqual(payload, V4ExpectedPostStateRootOffset, expectedPostRoot, 0, 32))
            return Reject(chainId, batchNumber, ReasonExpectedPostStateRootMismatch);

        if (BytesEqual(expectedPostRoot, 0, committedHeader, HeaderPostStateRootOffset, 32))
            return Reject(chainId, batchNumber, ReasonNoFraud);

        var acceptedClaimId = SliceUInt256(payload, V4ClaimIdOffset);
        OnFraudProofAccepted(
            chainId,
            batchNumber,
            SliceUInt256(committedHeader, HeaderPostStateRootOffset),
            (UInt256)expectedPostRoot);
        OnTrustlessFraudProofAccepted(chainId, batchNumber, acceptedClaimId);
        return true;
    }

    private static bool Reject(uint chainId, ulong batchNumber, byte reason)
    {
        OnFraudProofRejected(chainId, batchNumber, reason);
        return false;
    }

    private static byte[] ComputeTranscriptHash(byte[] payload)
    {
        var transcriptLength = 32 * 7 + 4 * 5 + 8;
        var preimage = new byte[TranscriptTag.Length + transcriptLength];
        var position = 0;
        Copy(TranscriptTag, 0, preimage, ref position, TranscriptTag.Length);
        Copy(payload, V4ReplayDomainOffset, preimage, ref position, 32);
        Copy(payload, V4ExecutorSemanticIdOffset, preimage, ref position, 32);
        Copy(payload, V4CommittedHeaderHashOffset, preimage, ref position, 32);
        Copy(payload, V4ChainIdOffset, preimage, ref position, 4);
        Copy(payload, V4BatchNumberOffset, preimage, ref position, 8);
        Copy(payload, V4DisputedTxIndexOffset, preimage, ref position, 4);
        Copy(payload, V4TransactionCountOffset, preimage, ref position, 4);
        Copy(payload, V4LowerBoundOffset, preimage, ref position, 4);
        Copy(payload, V4UpperBoundOffset, preimage, ref position, 4);
        Copy(payload, V4PreStateRootOffset, preimage, ref position, 32);
        Copy(payload, V4CommittedPostStateRootOffset, preimage, ref position, 32);
        Copy(payload, V4ExpectedPostStateRootOffset, preimage, ref position, 32);
        Copy(payload, V4TxRootOffset, preimage, ref position, 32);
        return Hash256(preimage);
    }

    private static byte[] ComputeWitnessHash(byte[] payload)
    {
        var tailLength = payload.Length - V4TxLengthOffset;
        var preimage = new byte[WitnessTag.Length + tailLength];
        var position = 0;
        Copy(WitnessTag, 0, preimage, ref position, WitnessTag.Length);
        Copy(payload, V4TxLengthOffset, preimage, ref position, tailLength);
        return Hash256(preimage);
    }

    private static byte[] ComputeClaimId(
        byte[] payload,
        UInt160 settlementManager,
        byte[] transcriptHash,
        byte[] witnessHash)
    {
        var preimage = new byte[ClaimTag.Length + 20 + 20 + 32 * 5 + 4 + 8 + 4];
        var position = 0;
        Copy(ClaimTag, 0, preimage, ref position, ClaimTag.Length);
        Copy((byte[])settlementManager, 0, preimage, ref position, 20);
        Copy((byte[])Runtime.ExecutingScriptHash, 0, preimage, ref position, 20);
        Copy(payload, V4ReplayDomainOffset, preimage, ref position, 32);
        Copy(payload, V4ExecutorSemanticIdOffset, preimage, ref position, 32);
        Copy(payload, V4CommittedHeaderHashOffset, preimage, ref position, 32);
        Copy(payload, V4ChainIdOffset, preimage, ref position, 4);
        Copy(payload, V4BatchNumberOffset, preimage, ref position, 8);
        Copy(payload, V4DisputedTxIndexOffset, preimage, ref position, 4);
        Copy(transcriptHash, 0, preimage, ref position, 32);
        Copy(witnessHash, 0, preimage, ref position, 32);
        return Hash256(preimage);
    }

    private static void Copy(byte[] source, int sourceOffset, byte[] destination, ref int position, int length)
    {
        for (var index = 0; index < length; index++)
            destination[position + index] = source[sourceOffset + index];
        position += length;
    }

    private static byte[] Slice(byte[] source, int offset, int length)
    {
        var result = new byte[length];
        for (var index = 0; index < length; index++) result[index] = source[offset + index];
        return result;
    }

    private static byte[] Hash256(byte[] bytes)
    {
        var first = CryptoLib.Sha256((ByteString)bytes);
        return (byte[])CryptoLib.Sha256(first);
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

    private static byte[] HashExpectedCounterEntry(byte[] payload, int keyOffset, int keyLen, ulong value)
    {
        var buffer = new byte[4 + keyLen + 4 + 8];
        buffer[0] = (byte)keyLen;
        buffer[1] = (byte)(keyLen >> 8);
        buffer[2] = (byte)(keyLen >> 16);
        buffer[3] = (byte)(keyLen >> 24);
        for (var index = 0; index < keyLen; index++) buffer[4 + index] = payload[keyOffset + index];
        var valueLengthOffset = 4 + keyLen;
        buffer[valueLengthOffset] = 8;
        var valueOffset = valueLengthOffset + 4;
        for (var index = 0; index < 8; index++) buffer[valueOffset + index] = (byte)(value >> (8 * index));
        return Hash256(buffer);
    }

    private static bool IsCanonicalLeafIndex(ulong leafIndex, int siblingCount) =>
        siblingCount == 64 || (leafIndex >> siblingCount) == 0;

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
