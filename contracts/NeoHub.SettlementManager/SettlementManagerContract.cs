using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace NeoHub.SettlementManager;

/// <summary>
/// Accepts L2BatchCommitments, dispatches to the right verifier (via VerifierRegistry), and
/// records canonical state roots once batches are finalized. See doc.md §3.2 (SettlementManager).
/// </summary>
[DisplayName("NeoHub.SettlementManager")]
[ContractAuthor("R3E Network", "dev@r3e.network")]
[ContractDescription("Batch settlement + canonical state root tracking for Neo Elastic Network.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.SettlementManager")]
[ContractPermission(Permission.Any, Method.Any)]
public class SettlementManagerContract : SmartContract
{
    private const byte PrefixBatchStatus = 0x01;          // 0x01 + chainId(4B) + batchNum(8B) → status byte
    private const byte PrefixBatchHeader = 0x02;          // 0x02 + chainId(4B) + batchNum(8B) → encoded header
    private const byte PrefixCanonicalRoot = 0x03;        // 0x03 + chainId(4B) → 32B canonical state root
    private const byte PrefixLatestBatch = 0x04;          // 0x04 + chainId(4B) → ulong latest finalized batch
    private const byte PrefixWithdrawalRoot = 0x05;       // 0x05 + chainId(4B) + batchNum(8B) → 32B withdrawalRoot
    private const byte PrefixOptimisticChallenge = 0x06;
    private const byte PrefixDARegistry = 0x07;
    private const byte PrefixDAValidator = 0x08;
    private const byte PrefixChainRegistry = 0xFC;
    private const byte PrefixVerifierRegistry = 0xFD;
    private const byte KeyOwner = 0xFF;

    // Canonical L2BatchCommitment header offsets (see Neo.L2.Batch.BatchSerializer).
    private const int PreStateRootOffset = 28;
    private const int PostStateRootOffset = 60;
    private const int TxRootOffset = 92;
    private const int ReceiptRootOffset = 124;
    private const int WithdrawalRootOffset = 156;
    private const int L2ToL1MessageRootOffset = 188;
    private const int L2ToL2MessageRootOffset = 220;
    private const int DACommitmentOffset = 252;
    private const int PublicInputHashOffset = 284;
    private const int ProofTypeOffset = 316;
    private const int ProofLenOffset = 317;
    private const int ProofBytesOffset = 321;
    private const int OptimisticSequencerOffsetInProof = 61;
    private const int OptimisticMinProofBytes = 85;
    private const int OptimisticMaxProofBytes = 1 * 1024 * 1024;
    private const byte ProofTypeMultisig = 1;
    private const byte ProofTypeOptimistic = 2;
    private const byte ProofTypeZk = 3;

    private const byte SecurityLevelSidechain = 0;
    private const byte SecurityLevelSettled = 1;
    private const byte SecurityLevelOptimistic = 2;
    private const byte SecurityLevelValidity = 3;
    private const byte SecurityLevelValidium = 4;
    private const byte DAModeL1 = 0;

    /// <summary>Status byte values match Neo.L2.BatchStatus.</summary>
    public const byte StatusUnknown = 0;
    public const byte StatusPending = 1;
    public const byte StatusChallengeable = 2;
    public const byte StatusFinalized = 3;
    public const byte StatusReverted = 4;

    /// <summary>Emitted when a batch is submitted (status → Pending).</summary>
    [DisplayName("BatchSubmitted")]
    public static event Action<uint, ulong, UInt256> OnBatchSubmitted = default!;

    /// <summary>Emitted when a batch reaches Finalized.</summary>
    [DisplayName("BatchFinalized")]
    public static event Action<uint, ulong, UInt256> OnBatchFinalized = default!;

    /// <summary>Emitted when a batch is reverted (fraud proven or governance action).</summary>
    [DisplayName("BatchReverted")]
    public static event Action<uint, ulong> OnBatchReverted = default!;

    /// <summary>Emitted when ownership is transferred.</summary>
    [DisplayName("OwnerChanged")]
    public static event Action<UInt160, UInt160> OnOwnerChanged = default!;

    /// <summary>Emitted when OptimisticChallenge contract is wired.</summary>
    [DisplayName("OptimisticChallengeChanged")]
    public static event Action<UInt160> OnOptimisticChallengeChanged = default!;

    /// <summary>Emitted when DARegistry contract is wired.</summary>
    [DisplayName("DARegistryChanged")]
    public static event Action<UInt160> OnDARegistryChanged = default!;

    /// <summary>Emitted when DAValidator contract is wired.</summary>
    [DisplayName("DAValidatorChanged")]
    public static event Action<UInt160> OnDAValidatorChanged = default!;

    /// <summary>Set wiring on deploy.</summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var arr = (object[])data;
        var owner = (UInt160)arr[0];
        var chainRegistry = (UInt160)arr[1];
        var verifierRegistry = (UInt160)arr[2];
        var optimisticChallenge = arr.Length > 3 ? (UInt160)arr[3] : UInt160.Zero;
        ExecutionEngine.Assert(owner.IsValid && !owner.IsZero, "invalid owner");
        // SettlementManager is the load-bearing contract — chainRegistry + verifierRegistry
        // hashes feed into every SubmitBatch call. A typo'd zero here would deploy
        // successfully but every batch submission would later fail with a confusing
        // cross-contract Contract.Call error.
        ExecutionEngine.Assert(chainRegistry.IsValid && !chainRegistry.IsZero, "invalid chain registry");
        ExecutionEngine.Assert(verifierRegistry.IsValid && !verifierRegistry.IsZero, "invalid verifier registry");
        Storage.Put(new byte[] { KeyOwner }, owner);
        Storage.Put(new byte[] { PrefixChainRegistry }, chainRegistry);
        Storage.Put(new byte[] { PrefixVerifierRegistry }, verifierRegistry);
        if (!optimisticChallenge.IsZero)
        {
            ExecutionEngine.Assert(optimisticChallenge.IsValid, "invalid optimistic challenge");
            Storage.Put(new byte[] { PrefixOptimisticChallenge }, optimisticChallenge);
        }
    }

    /// <summary>Governance owner.</summary>
    [Safe]
    public static UInt160 GetOwner()
    {
        var raw = Storage.Get(new byte[] { KeyOwner });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Transfer governance ownership. Owner only.</summary>
    public static void SetOwner(UInt160 newOwner)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(newOwner.IsValid && !newOwner.IsZero, "invalid new owner");
        var oldOwner = GetOwner();
        Storage.Put(new byte[] { KeyOwner }, newOwner);
        OnOwnerChanged(oldOwner, newOwner);
    }

    /// <summary>Contract authorized to open/finalize/revert optimistic challenge windows.</summary>
    [Safe]
    public static UInt160 GetOptimisticChallenge()
    {
        var raw = Storage.Get(new byte[] { PrefixOptimisticChallenge });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Wire the OptimisticChallenge contract. Owner only.</summary>
    public static void SetOptimisticChallenge(UInt160 optimisticChallenge)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(optimisticChallenge.IsValid && !optimisticChallenge.IsZero,
            "invalid optimistic challenge");
        Storage.Put(new byte[] { PrefixOptimisticChallenge }, optimisticChallenge);
        OnOptimisticChallengeChanged(optimisticChallenge);
    }

    /// <summary>DARegistry contract called when a batch is accepted.</summary>
    [Safe]
    public static UInt160 GetDARegistry()
    {
        var raw = Storage.Get(new byte[] { PrefixDARegistry });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Wire the DARegistry contract. Owner only; required before SubmitBatch.</summary>
    public static void SetDARegistry(UInt160 daRegistry)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(daRegistry.IsValid && !daRegistry.IsZero, "invalid DA registry");
        Storage.Put(new byte[] { PrefixDARegistry }, daRegistry);
        OnDARegistryChanged(daRegistry);
    }

    /// <summary>DAValidator contract called before a batch is finalized.</summary>
    [Safe]
    public static UInt160 GetDAValidator()
    {
        var raw = Storage.Get(new byte[] { PrefixDAValidator });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Wire the DAValidator contract. Owner only; required before FinalizeBatch.</summary>
    public static void SetDAValidator(UInt160 daValidator)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(daValidator.IsValid && !daValidator.IsZero, "invalid DA validator");
        Storage.Put(new byte[] { PrefixDAValidator }, daValidator);
        OnDAValidatorChanged(daValidator);
    }

    /// <summary>
    /// Submit an L2BatchCommitment. The chain must be registered + active; the batch number
    /// must equal <c>latestFinalized + 1</c>; the verifier dispatches by ProofType.
    /// </summary>
    /// <param name="commitmentBytes">Canonical L2BatchCommitment encoding (see Neo.L2.Batch.BatchSerializer).</param>
    /// <param name="l1MessageHash">The <c>l1MessageHash</c> public input the proof attests. It is
    /// NOT carried in the commitment header, so the submitter supplies it; it is bound into
    /// <c>publicInputHash</c> below, so a wrong value simply fails verification.</param>
    /// <param name="blockContextHash">The <c>blockContextHash</c> public input the proof attests
    /// (same binding rationale as <paramref name="l1MessageHash"/>).</param>
    /// <remarks>
    /// SECURITY: the recorded canonical roots are bound to the proof. We (1) require the
    /// commitment's preStateRoot to equal the chain's current canonical head (state-root
    /// continuity), and (2) recompute the public-input hash from the commitment header plus the
    /// two supplied public-input hashes and require it to equal the commitment's
    /// <c>publicInputHash</c> — the exact value the registered verifier cryptographically checks.
    /// Without (1)+(2) a valid proof produced for one state could be replayed to finalize an
    /// attacker-chosen postStateRoot / withdrawalRoot and drain the shared bridge.
    /// </remarks>
    public static void SubmitBatch(byte[] commitmentBytes, byte[] l1MessageHash, byte[] blockContextHash)
    {
        // Minimum size must cover: chainId(4) + batchNumber(8) + firstBlock(8) + lastBlock(8) +
        // 9 roots(288) + proofType(1) + proofLen(4) = 321 = ProofBytesOffset.
        ExecutionEngine.Assert(commitmentBytes.Length >= ProofBytesOffset, "commitment too small");
        ExecutionEngine.Assert(l1MessageHash != null && l1MessageHash.Length == 32, "l1MessageHash must be 32 bytes");
        ExecutionEngine.Assert(blockContextHash != null && blockContextHash.Length == 32, "blockContextHash must be 32 bytes");

        var chainId = ReadUInt32(commitmentBytes, 0);
        var batchNumber = ReadUInt64(commitmentBytes, 4);

        // Chain must be registered + active.
        var chainRegistry = (UInt160)(Storage.Get(new byte[] { PrefixChainRegistry }) ?? throw new Exception("registry unset"));
        var isActive = (bool)Contract.Call(chainRegistry, "isActive", CallFlags.ReadOnly, new object[] { chainId });
        ExecutionEngine.Assert(isActive, "chain inactive");

        // Sequential batch numbers only.
        var latest = GetLatestFinalizedBatch(chainId);
        ExecutionEngine.Assert(batchNumber == latest + 1, "batch number out of sequence");

        // Don't double-submit. A previously-reverted slot (fraud-proven or governance-reverted)
        // may be resubmitted with a corrected batch; any other existing status is rejected so the
        // chain is never permanently wedged by a revert.
        var statusKey = StatusKey(chainId, batchNumber);
        var existing = Storage.Get(statusKey);
        ExecutionEngine.Assert(existing == null || ((byte[])existing)[0] == StatusReverted, "batch already submitted");

        // Continuity: the batch must build on the chain's current canonical head. The first batch
        // (latest == 0) establishes genesis and has no prior root to chain to; every later batch
        // must chain exactly. Prevents finalizing a transition from a fabricated preStateRoot.
        if (latest > 0)
        {
            var preStateRoot = ReadUInt256(commitmentBytes, PreStateRootOffset);
            ExecutionEngine.Assert(preStateRoot.Equals(GetCanonicalStateRoot(chainId)),
                "preStateRoot does not match canonical head");
        }

        // Bind every recorded root to the proof: recompute publicInputHash from the committed
        // header fields + the supplied l1MessageHash / blockContextHash and require it to equal
        // the commitment's publicInputHash (the value the verifier attests). Mirrors the off-chain
        // Neo.L2.State.StateRootCalculator.HashPublicInputs layout byte-for-byte.
        var expectedPublicInputHash = ComputePublicInputHash(commitmentBytes, l1MessageHash!, blockContextHash!);
        var declaredPublicInputHash = ReadUInt256(commitmentBytes, PublicInputHashOffset);
        ExecutionEngine.Assert(expectedPublicInputHash.Equals(declaredPublicInputHash),
            "publicInputHash not bound to commitment roots");

        var proofType = commitmentBytes[ProofTypeOffset];
        var securityLevel = GetChainSecurityLevel(chainRegistry, chainId);
        var daMode = GetChainDAMode(chainRegistry, chainId);

        // SecurityLevel, ProofType, and DAMode are distinct protocol domains. In particular,
        // Validium(4) requires Zk(3) plus off-chain DA, so comparing raw enum bytes is invalid.
        // Enforce both compatibility tables before invoking external verifier / DA contracts.
        AssertSecurityConfigurationCompatible(securityLevel, daMode);
        ExecutionEngine.Assert(IsProofTypeCompatible(securityLevel, proofType),
            "proof type incompatible with chain's advertised security level");

        // Hand off proof verification to the registry (it dispatches by ProofType).
        var verifierRegistry = (UInt160)(Storage.Get(new byte[] { PrefixVerifierRegistry }) ?? throw new Exception("verifier registry unset"));
        var verified = (bool)Contract.Call(verifierRegistry, "verifyCommitment", CallFlags.ReadOnly, new object[] { commitmentBytes });
        ExecutionEngine.Assert(verified, "verifier rejected commitment");

        var daCommitment = ReadUInt256(commitmentBytes, DACommitmentOffset);
        RecordDataAvailability(chainId, batchNumber, daCommitment, daMode);

        var status = proofType == ProofTypeOptimistic ? StatusChallengeable : StatusPending;
        Storage.Put(statusKey, new byte[] { status });
        Storage.Put(BatchHeaderKey(chainId, batchNumber), commitmentBytes);

        // Capture the per-batch withdrawalRoot so the SharedBridge can consult it.
        var withdrawalRoot = ReadUInt256(commitmentBytes, WithdrawalRootOffset);
        Storage.Put(WithdrawalRootKey(chainId, batchNumber), (byte[])withdrawalRoot);

        if (proofType == ProofTypeOptimistic)
        {
            var optimisticChallenge = GetOptimisticChallenge();
            ExecutionEngine.Assert(optimisticChallenge.IsValid && !optimisticChallenge.IsZero,
                "optimistic challenge not wired");
            var sequencer = ReadOptimisticSequencer(commitmentBytes);
            Contract.Call(optimisticChallenge, "openWindow", CallFlags.All,
                new object[] { chainId, batchNumber, sequencer });
        }

        var postStateRoot = ReadUInt256(commitmentBytes, PostStateRootOffset);
        OnBatchSubmitted(chainId, batchNumber, postStateRoot);
    }

    private static bool IsProofTypeCompatible(byte securityLevel, byte proofType)
    {
        if (securityLevel == SecurityLevelSidechain || securityLevel == SecurityLevelSettled)
            return proofType == ProofTypeMultisig ||
                   proofType == ProofTypeOptimistic ||
                   proofType == ProofTypeZk;

        if (securityLevel == SecurityLevelOptimistic)
            return proofType == ProofTypeOptimistic || proofType == ProofTypeZk;

        if (securityLevel == SecurityLevelValidity || securityLevel == SecurityLevelValidium)
            return proofType == ProofTypeZk;

        return false;
    }

    private static void AssertSecurityConfigurationCompatible(byte securityLevel, byte daMode)
    {
        ExecutionEngine.Assert(securityLevel <= SecurityLevelValidium,
            "securityLevel must be 0..4 (Sidechain/Settled/Optimistic/Validity/Validium)");
        ExecutionEngine.Assert(daMode <= 3,
            "daMode must be 0..3 (L1/NeoFS/External/DAC)");

        if (securityLevel == SecurityLevelValidity)
            ExecutionEngine.Assert(daMode == DAModeL1,
                "Validity security level requires L1 DA");

        if (securityLevel == SecurityLevelValidium)
            ExecutionEngine.Assert(daMode != DAModeL1,
                "Validium security level requires off-chain DA");
    }

    /// <summary>
    /// Recompute the canonical public-input hash from a commitment header plus the two public
    /// inputs not carried in the header (<paramref name="l1MessageHash"/>,
    /// <paramref name="blockContextHash"/>). Must match
    /// <c>Neo.L2.State.StateRootCalculator.HashPublicInputs</c>:
    /// <c>Hash256(chainId(4 LE) ‖ batchNumber(8 LE) ‖ preStateRoot ‖ postStateRoot ‖ txRoot ‖
    /// receiptRoot ‖ withdrawalRoot ‖ l2ToL1MessageRoot ‖ l2ToL2MessageRoot ‖ l1MessageHash ‖
    /// daCommitment ‖ blockContextHash)</c>, where <c>Hash256(x) = Sha256(Sha256(x))</c>.
    /// </summary>
    private static UInt256 ComputePublicInputHash(byte[] commitmentBytes, byte[] l1MessageHash, byte[] blockContextHash)
    {
        var buf = new byte[4 + 8 + 10 * 32];
        var pos = 0;
        // chainId(4 LE) + batchNumber(8 LE) are already little-endian at the head of the commitment.
        for (var i = 0; i < 12; i++) buf[pos + i] = commitmentBytes[i];
        pos += 12;
        CopyRoot(buf, ref pos, commitmentBytes, PreStateRootOffset);
        CopyRoot(buf, ref pos, commitmentBytes, PostStateRootOffset);
        CopyRoot(buf, ref pos, commitmentBytes, TxRootOffset);
        CopyRoot(buf, ref pos, commitmentBytes, ReceiptRootOffset);
        CopyRoot(buf, ref pos, commitmentBytes, WithdrawalRootOffset);
        CopyRoot(buf, ref pos, commitmentBytes, L2ToL1MessageRootOffset);
        CopyRoot(buf, ref pos, commitmentBytes, L2ToL2MessageRootOffset);
        for (var i = 0; i < 32; i++) buf[pos + i] = l1MessageHash[i];
        pos += 32;
        CopyRoot(buf, ref pos, commitmentBytes, DACommitmentOffset);
        for (var i = 0; i < 32; i++) buf[pos + i] = blockContextHash[i];
        pos += 32;

        var h1 = CryptoLib.Sha256((ByteString)buf);
        return (UInt256)(byte[])CryptoLib.Sha256(h1);
    }

    private static void CopyRoot(byte[] dest, ref int pos, byte[] src, int srcOffset)
    {
        for (var i = 0; i < 32; i++) dest[pos + i] = src[srcOffset + i];
        pos += 32;
    }

    /// <summary>
    /// Move a batch from Pending or Challengeable to Finalized. Records the canonical state
    /// root and bumps <c>latestFinalized</c>. Anyone may call once the conditions are met (the
    /// underlying verifier / challenge logic is the gate, not msg.sender).
    /// </summary>
    public static void FinalizeBatch(uint chainId, ulong batchNumber)
    {
        var key = StatusKey(chainId, batchNumber);
        var rawStatus = Storage.Get(key);
        ExecutionEngine.Assert(rawStatus != null, "batch unknown");
        var status = ((byte[])rawStatus!)[0];
        ExecutionEngine.Assert(status == StatusPending || status == StatusChallengeable,
            "batch not finalizable");
        if (status == StatusChallengeable)
        {
            var optimisticChallenge = GetOptimisticChallenge();
            ExecutionEngine.Assert(optimisticChallenge.IsValid && !optimisticChallenge.IsZero,
                "optimistic challenge not wired");
            ExecutionEngine.Assert(Runtime.CheckWitness(optimisticChallenge),
                "challengeable batch finalization must come from OptimisticChallenge");
        }

        var header = (byte[])(Storage.Get(BatchHeaderKey(chainId, batchNumber)) ?? throw new Exception("header missing"));

        // Chain security configuration is mutable through governance. Revalidate the stored
        // header against the current label before finalization so a Multisig/Optimistic batch
        // submitted under a weaker label cannot finalize after the chain advertises Validity or
        // Validium, and a pre-upgrade contradictory DA label cannot cross the finalization gate.
        var chainRegistry = (UInt160)(Storage.Get(new byte[] { PrefixChainRegistry }) ?? throw new Exception("registry unset"));
        var securityLevel = GetChainSecurityLevel(chainRegistry, chainId);
        var daMode = GetChainDAMode(chainRegistry, chainId);
        AssertSecurityConfigurationCompatible(securityLevel, daMode);
        ExecutionEngine.Assert(IsProofTypeCompatible(securityLevel, header[ProofTypeOffset]),
            "proof type incompatible with current chain security level");

        // Finalize strictly in order onto the current canonical head. Continuity is checked at
        // submit time, but a head RevertBatch rewinds latestFinalized/canonicalRoot afterwards —
        // without re-checking here, an already-submitted descendant of the reverted batch could
        // finalize onto the rewound root, break canonical continuity, and make its withdrawalRoot
        // claimable on the SharedBridge. Re-assert both the sequence and preStateRoot continuity.
        // (To resume after a head revert, an orphaned Pending descendant must itself be reverted
        // and resubmitted against the rewound head.)
        ExecutionEngine.Assert(batchNumber == GetLatestFinalizedBatch(chainId) + 1, "finalize out of sequence");
        if (batchNumber > 1)
        {
            var preStateRoot = ReadUInt256(header, PreStateRootOffset);
            ExecutionEngine.Assert(preStateRoot.Equals(GetCanonicalStateRoot(chainId)),
                "preStateRoot no longer matches canonical head");
        }

        var daCommitment = ReadUInt256(header, DACommitmentOffset);
        var recordedDaMode = GetRecordedDAMode(chainId, batchNumber, daCommitment);
        AssertSecurityConfigurationCompatible(securityLevel, recordedDaMode);
        ValidateDataAvailability(chainId, batchNumber, daCommitment, recordedDaMode);
        var postStateRoot = ReadUInt256(header, PostStateRootOffset);

        Storage.Put(key, new byte[] { StatusFinalized });
        Storage.Put(CanonicalRootKey(chainId), (byte[])postStateRoot);
        SetLatestFinalizedBatch(chainId, batchNumber);

        OnBatchFinalized(chainId, batchNumber, postStateRoot);
    }

    /// <summary>
    /// Mark a batch reverted (governance action, or a successful optimistic fraud proof). The
    /// slot becomes resubmittable via <see cref="SubmitBatch"/> with a corrected batch, so a
    /// revert never permanently wedges the chain. If the reverted batch was the current finalized
    /// head, the canonical state root and the latestFinalized pointer are rewound to the previous
    /// finalized batch so the escape hatch and withdrawal paths stop honoring the reverted state.
    /// </summary>
    public static void RevertBatch(uint chainId, ulong batchNumber)
    {
        var ownerAuthorized = Runtime.CheckWitness(GetOwner());
        var optimisticChallenge = GetOptimisticChallenge();
        var challengeAuthorized = optimisticChallenge.IsValid
            && !optimisticChallenge.IsZero
            && Runtime.CheckWitness(optimisticChallenge);
        ExecutionEngine.Assert(ownerAuthorized || challengeAuthorized, "not authorized");

        var rawStatus = Storage.Get(StatusKey(chainId, batchNumber));
        ExecutionEngine.Assert(rawStatus != null, "batch unknown");
        var status = ((byte[])rawStatus!)[0];
        ExecutionEngine.Assert(status != StatusReverted, "batch already reverted");

        if (challengeAuthorized && !ownerAuthorized)
        {
            ExecutionEngine.Assert(status == StatusChallengeable,
                "OptimisticChallenge can only revert challengeable batches");
        }

        // If the reverted batch is the current finalized head, rewind the canonical root and the
        // latestFinalized pointer to the previous finalized batch. Only the head may be reverted
        // while finalized — deeper rollbacks must revert from the top down so the rewind stays
        // consistent and never leaves a stale canonical root the escape hatch would honor.
        if (status == StatusFinalized)
        {
            ExecutionEngine.Assert(batchNumber == GetLatestFinalizedBatch(chainId),
                "only the latest finalized batch can be reverted");
            if (batchNumber > 1)
            {
                var prevHeaderRaw = Storage.Get(BatchHeaderKey(chainId, batchNumber - 1));
                ExecutionEngine.Assert(prevHeaderRaw != null, "previous batch header missing");
                var prevPostStateRoot = ReadUInt256((byte[])prevHeaderRaw!, PostStateRootOffset);
                Storage.Put(CanonicalRootKey(chainId), (byte[])prevPostStateRoot);
                SetLatestFinalizedBatch(chainId, batchNumber - 1);
            }
            else
            {
                Storage.Delete(CanonicalRootKey(chainId));
                SetLatestFinalizedBatch(chainId, 0);
            }
        }

        Storage.Put(StatusKey(chainId, batchNumber), new byte[] { StatusReverted });
        OnBatchReverted(chainId, batchNumber);
    }

    /// <summary>Get the canonical (finalized) state root for a chain.</summary>
    [Safe]
    public static UInt256 GetCanonicalStateRoot(uint chainId)
    {
        var raw = Storage.Get(CanonicalRootKey(chainId));
        return raw == null ? UInt256.Zero : (UInt256)raw;
    }

    /// <summary>Get the lifecycle status of a submitted batch.</summary>
    [Safe]
    public static byte GetBatchStatus(uint chainId, ulong batchNumber)
    {
        var raw = Storage.Get(StatusKey(chainId, batchNumber));
        return raw == null ? StatusUnknown : ((byte[])raw)[0];
    }

    /// <summary>
    /// Return the L2→L1 message root committed by a finalized batch, or zero while the batch is
    /// unknown, pending, challengeable, or reverted.
    /// </summary>
    /// <remarks>See doc.md §3.2 (MessageRouter) and §10 (Neo Connect).</remarks>
    [Safe]
    public static UInt256 GetL2ToL1MessageRoot(uint chainId, ulong batchNumber) =>
        GetFinalizedBatchRoot(chainId, batchNumber, L2ToL1MessageRootOffset);

    /// <summary>
    /// Return the L2→L2 message root committed by a finalized batch, or zero while the batch is
    /// unknown, pending, challengeable, or reverted.
    /// </summary>
    /// <remarks>See doc.md §3.2 (MessageRouter) and §10 (Neo Connect).</remarks>
    [Safe]
    public static UInt256 GetL2ToL2MessageRoot(uint chainId, ulong batchNumber) =>
        GetFinalizedBatchRoot(chainId, batchNumber, L2ToL2MessageRootOffset);

    /// <summary>
    /// Return the transaction root committed by a finalized batch, or zero while the batch is
    /// unknown, pending, challengeable, or reverted.
    /// </summary>
    /// <remarks>See doc.md §7.2 and §15.4 (Forced Inclusion).</remarks>
    [Safe]
    public static UInt256 GetFinalizedTxRoot(uint chainId, ulong batchNumber) =>
        GetFinalizedBatchRoot(chainId, batchNumber, TxRootOffset);

    /// <summary>
    /// Return the canonical 321-byte commitment header for a challengeable optimistic batch.
    /// </summary>
    /// <remarks>
    /// See doc.md §15 and §17. The returned bytes are the exact fixed portion of
    /// <c>Neo.L2.Batch.BatchSerializer</c>'s stored commitment, including chainId, batchNumber,
    /// pre/post state roots, txRoot, proof type, and proof length. Unknown, non-optimistic,
    /// finalized, or reverted batches fail closed.
    /// </remarks>
    [Safe]
    public static byte[] GetChallengeableBatchHeader(uint chainId, ulong batchNumber)
    {
        var rawStatus = Storage.Get(StatusKey(chainId, batchNumber));
        ExecutionEngine.Assert(rawStatus != null, "batch unknown");
        ExecutionEngine.Assert(((byte[])rawStatus!)[0] == StatusChallengeable,
            "batch is not challengeable");

        var rawHeader = Storage.Get(BatchHeaderKey(chainId, batchNumber));
        ExecutionEngine.Assert(rawHeader != null, "batch header missing");
        var stored = (byte[])rawHeader!;
        ExecutionEngine.Assert(stored.Length >= ProofBytesOffset, "batch header truncated");
        ExecutionEngine.Assert(stored[ProofTypeOffset] == ProofTypeOptimistic,
            "batch is not optimistic");

        var header = new byte[ProofBytesOffset];
        for (var index = 0; index < ProofBytesOffset; index++) header[index] = stored[index];
        return header;
    }

    /// <summary>Latest finalized batch number for a chain.</summary>
    [Safe]
    public static ulong GetLatestFinalizedBatch(uint chainId)
    {
        var raw = Storage.Get(LatestBatchKey(chainId));
        return raw == null ? 0UL : (ulong)(BigInteger)raw;
    }

    private static UInt256 GetFinalizedBatchRoot(uint chainId, ulong batchNumber, int rootOffset)
    {
        if (GetBatchStatus(chainId, batchNumber) != StatusFinalized)
            return UInt256.Zero;

        var header = Storage.Get(BatchHeaderKey(chainId, batchNumber));
        return header == null ? UInt256.Zero : ReadUInt256((byte[])header, rootOffset);
    }

    /// <summary>
    /// True if <paramref name="leafHash"/> is the withdrawal root of the latest finalized batch
    /// on <paramref name="chainId"/>. Used by SharedBridge.FinalizeWithdrawal.
    /// </summary>
    /// <remarks>
    /// Single-leaf fast path: confirms that the latest finalized batch's withdrawalRoot
    /// equals the supplied leaf-hash. Mathematically only valid when the batch's
    /// withdrawal tree has exactly one entry (so root == leaf). Multi-withdrawal batches
    /// MUST use <see cref="VerifyWithdrawalLeafWithProof"/>, which is the standard
    /// production path; that variant takes per-level sibling hashes + a leaf index and
    /// re-derives the root via on-chain Merkle folding.
    /// </remarks>
    [Safe]
    public static bool VerifyWithdrawalLeaf(uint chainId, UInt256 leafHash)
    {
        var latest = GetLatestFinalizedBatch(chainId);
        return VerifyWithdrawalLeafAt(chainId, latest, leafHash);
    }

    /// <summary>
    /// True if <paramref name="leafHash"/> is the withdrawal root of finalized batch
    /// <paramref name="batchNumber"/> on <paramref name="chainId"/>. Lets a withdrawal anchored
    /// in an older batch finalize even after newer batches have shipped.
    /// </summary>
    /// <remarks>
    /// Single-leaf fast path, same shape as <see cref="VerifyWithdrawalLeaf"/>: leaf-hash
    /// must equal the stored batch withdrawalRoot. Mathematically only valid when the
    /// batch had exactly one withdrawal. Multi-withdrawal batches MUST use
    /// <see cref="VerifyWithdrawalLeafWithProof"/> (siblings + leafIndex,
    /// folded on-chain to re-derive the root).
    /// </remarks>
    [Safe]
    public static bool VerifyWithdrawalLeafAt(uint chainId, ulong batchNumber, UInt256 leafHash)
    {
        // Status check: only Finalized batches' roots are valid for withdrawals. Without it,
        // a Pending or Challengeable batch's root could authorize a premature withdraw.
        var statusRaw = Storage.Get(StatusKey(chainId, batchNumber));
        if (statusRaw == null) return false;
        var status = ((byte[])statusRaw)[0];
        if (status != StatusFinalized) return false;

        var raw = Storage.Get(WithdrawalRootKey(chainId, batchNumber));
        if (raw == null) return false;
        var root = (UInt256)raw;
        return root.Equals(leafHash);
    }

    /// <summary>
    /// True if <paramref name="leafHash"/> is included in finalized batch
    /// <paramref name="batchNumber"/>'s <c>withdrawalRoot</c> via the supplied Merkle inclusion
    /// proof. This is the canonical multi-leaf verification path; the
    /// <c>VerifyWithdrawalLeaf</c> / <c>VerifyWithdrawalLeafAt</c> overloads above are
    /// fast-path variants valid only when the withdrawal tree collapses to a single leaf
    /// (root == leaf). Production deployments with multi-leaf batches MUST use this
    /// <c>*WithProof</c> path.
    /// </summary>
    /// <param name="chainId">L2 chain identifier.</param>
    /// <param name="batchNumber">Finalized batch the user's withdrawal was sealed into.</param>
    /// <param name="leafHash">The hash of the user's specific withdrawal entry.</param>
    /// <param name="siblings">Per-level sibling hashes from leaf to just below root, each
    /// exactly 32 bytes. Length is the tree depth (capped at <see cref="MaxProofDepth"/>).</param>
    /// <param name="leafIndex">Position of the leaf in the original leaf list. Determines
    /// whether the leaf sits on the left or right of each sibling at every level (bit i of
    /// leafIndex governs level i: 0 = left, 1 = right).</param>
    /// <remarks>
    /// Hash composition matches the off-chain <c>Neo.L2.State.MerkleTree</c> convention:
    /// <c>Hash256(left || right) = Sha256(Sha256(left || right))</c>, where <c>||</c> is
    /// concatenation. Same construction as Neo's <c>Cryptography.MerkleTree</c>.
    /// </remarks>
    [Safe]
    public static bool VerifyWithdrawalLeafWithProof(
        uint chainId,
        ulong batchNumber,
        UInt256 leafHash,
        byte[][] siblings,
        ulong leafIndex)
    {
        // Same finalized-status precondition as the single-leaf fast path above.
        var statusRaw = Storage.Get(StatusKey(chainId, batchNumber));
        if (statusRaw == null) return false;
        var status = ((byte[])statusRaw)[0];
        if (status != StatusFinalized) return false;

        var rootRaw = Storage.Get(WithdrawalRootKey(chainId, batchNumber));
        if (rootRaw == null) return false;
        var storedRoot = (UInt256)rootRaw;

        // Bound the proof depth so a malicious caller can't force unbounded work.
        // 64 levels = 2^64 leaves, well past any plausible batch size.
        ExecutionEngine.Assert(siblings != null, "siblings required");
        var proofSiblings = siblings!;
        ExecutionEngine.Assert(proofSiblings.Length <= MaxProofDepth, "proof too deep");

        // Empty siblings → leaf is itself the root (single-leaf tree).
        var current = (byte[])leafHash;
        var index = leafIndex;
        for (var i = 0; i < proofSiblings.Length; i++)
        {
            var sibling = proofSiblings[i];
            ExecutionEngine.Assert(sibling.Length == 32, "sibling must be 32 bytes");
            // Concat 64-byte buffer in left/right order driven by index's low bit.
            var combined = new byte[64];
            if ((index & 1UL) == 0UL)
            {
                // current on the left
                for (var j = 0; j < 32; j++) combined[j] = current[j];
                for (var j = 0; j < 32; j++) combined[32 + j] = sibling[j];
            }
            else
            {
                // current on the right
                for (var j = 0; j < 32; j++) combined[j] = sibling[j];
                for (var j = 0; j < 32; j++) combined[32 + j] = current[j];
            }
            // Hash256 = Sha256(Sha256(x)) — Neo MerkleTree convention.
            var h1 = CryptoLib.Sha256((ByteString)combined);
            current = (byte[])CryptoLib.Sha256(h1);
            index = index >> 1;
        }
        return storedRoot.Equals((UInt256)current);
    }

    /// <summary>Maximum tree depth the on-chain verifier will accept (2^64 leaves).</summary>
    public const int MaxProofDepth = 64;

    private static void RecordDataAvailability(
        uint chainId,
        ulong batchNumber,
        UInt256 daCommitment,
        byte daMode)
    {
        ExecutionEngine.Assert(!daCommitment.Equals(UInt256.Zero), "DA commitment must be non-zero");
        ExecutionEngine.Assert(daMode <= 3, "daMode must be 0..3");
        var daRegistry = GetDARegistry();
        ExecutionEngine.Assert(daRegistry.IsValid && !daRegistry.IsZero, "DA registry not wired");
        Contract.Call(daRegistry, "record", CallFlags.All,
            new object[] { chainId, batchNumber, daCommitment, daMode });
    }

    private static void ValidateDataAvailability(
        uint chainId,
        ulong batchNumber,
        UInt256 daCommitment,
        byte daMode)
    {
        var validator = GetDAValidator();
        ExecutionEngine.Assert(validator.IsValid && !validator.IsZero, "DA validator not wired");
        var ok = (bool)Contract.Call(validator, "validate", CallFlags.ReadOnly,
            new object[] { chainId, batchNumber, daCommitment, daMode });
        ExecutionEngine.Assert(ok, "DA validator rejected commitment");
    }

    private static byte GetRecordedDAMode(
        uint chainId,
        ulong batchNumber,
        UInt256 expectedCommitment)
    {
        var daRegistry = GetDARegistry();
        ExecutionEngine.Assert(daRegistry.IsValid && !daRegistry.IsZero, "DA registry not wired");
        var recordedCommitment = (UInt256)Contract.Call(
            daRegistry,
            "getCommitment",
            CallFlags.ReadOnly,
            new object[] { chainId, batchNumber });
        ExecutionEngine.Assert(recordedCommitment.Equals(expectedCommitment),
            "DA registry commitment does not match batch header");

        var recordedMode = (byte)(BigInteger)Contract.Call(
            daRegistry,
            "getMode",
            CallFlags.ReadOnly,
            new object[] { chainId, batchNumber });
        ExecutionEngine.Assert(recordedMode <= 3, "recorded daMode must be 0..3");
        return recordedMode;
    }

    private static byte GetChainDAMode(UInt160 chainRegistry, uint chainId)
    {
        return (byte)(BigInteger)Contract.Call(
            chainRegistry,
            "getDAMode",
            CallFlags.ReadOnly,
            new object[] { chainId });
    }

    private static byte GetChainSecurityLevel(UInt160 chainRegistry, uint chainId)
    {
        return (byte)(BigInteger)Contract.Call(
            chainRegistry,
            "getSecurityLevel",
            CallFlags.ReadOnly,
            new object[] { chainId });
    }

    /// <summary>
    /// True if <paramref name="leafHash"/> is included in <paramref name="chainId"/>'s
    /// current canonical state root via the supplied Merkle inclusion proof. Same proof
    /// shape as <see cref="VerifyWithdrawalLeafWithProof"/> — this verifies against the
    /// canonical state Merkle root, used by <c>EmergencyManager.EscapeHatchExitWithProof</c>
    /// when a user is exiting against the L2's last finalized state.
    /// </summary>
    /// <remarks>
    /// Hash composition matches <c>Neo.L2.State.MerkleTree</c> /
    /// <c>Neo.L2.Executor.State.KeyedStateStore.ComputeRoot</c>: <c>Hash256(left || right)</c>
    /// with left/right ordered by the leaf-index bit at each level.
    /// </remarks>
    [Safe]
    public static bool VerifyStateLeafWithProof(
        uint chainId,
        UInt256 leafHash,
        byte[][] siblings,
        ulong leafIndex)
    {
        var canonicalRoot = GetCanonicalStateRoot(chainId);
        if (canonicalRoot.Equals(UInt256.Zero)) return false;

        ExecutionEngine.Assert(siblings != null, "siblings required");
        var proofSiblings = siblings!;
        ExecutionEngine.Assert(proofSiblings.Length <= MaxProofDepth, "proof too deep");

        var current = (byte[])leafHash;
        var index = leafIndex;
        for (var i = 0; i < proofSiblings.Length; i++)
        {
            var sibling = proofSiblings[i];
            ExecutionEngine.Assert(sibling.Length == 32, "sibling must be 32 bytes");
            var combined = new byte[64];
            if ((index & 1UL) == 0UL)
            {
                for (var j = 0; j < 32; j++) combined[j] = current[j];
                for (var j = 0; j < 32; j++) combined[32 + j] = sibling[j];
            }
            else
            {
                for (var j = 0; j < 32; j++) combined[j] = sibling[j];
                for (var j = 0; j < 32; j++) combined[32 + j] = current[j];
            }
            var h1 = CryptoLib.Sha256((ByteString)combined);
            current = (byte[])CryptoLib.Sha256(h1);
            index = index >> 1;
        }
        return canonicalRoot.Equals((UInt256)current);
    }

    private static void SetLatestFinalizedBatch(uint chainId, ulong batchNumber)
    {
        Storage.Put(LatestBatchKey(chainId), (BigInteger)batchNumber);
    }

    private static byte[] StatusKey(uint chainId, ulong batchNumber) =>
        BuildKey(PrefixBatchStatus, chainId, batchNumber);
    private static byte[] BatchHeaderKey(uint chainId, ulong batchNumber) =>
        BuildKey(PrefixBatchHeader, chainId, batchNumber);
    private static byte[] WithdrawalRootKey(uint chainId, ulong batchNumber) =>
        BuildKey(PrefixWithdrawalRoot, chainId, batchNumber);

    private static byte[] CanonicalRootKey(uint chainId)
    {
        var k = new byte[5];
        k[0] = PrefixCanonicalRoot;
        k[1] = (byte)chainId; k[2] = (byte)(chainId >> 8); k[3] = (byte)(chainId >> 16); k[4] = (byte)(chainId >> 24);
        return k;
    }

    private static byte[] LatestBatchKey(uint chainId)
    {
        var k = new byte[5];
        k[0] = PrefixLatestBatch;
        k[1] = (byte)chainId; k[2] = (byte)(chainId >> 8); k[3] = (byte)(chainId >> 16); k[4] = (byte)(chainId >> 24);
        return k;
    }

    private static byte[] BuildKey(byte prefix, uint chainId, ulong batchNumber)
    {
        var k = new byte[13];
        k[0] = prefix;
        k[1] = (byte)chainId; k[2] = (byte)(chainId >> 8); k[3] = (byte)(chainId >> 16); k[4] = (byte)(chainId >> 24);
        k[5] = (byte)batchNumber; k[6] = (byte)(batchNumber >> 8); k[7] = (byte)(batchNumber >> 16); k[8] = (byte)(batchNumber >> 24);
        k[9] = (byte)(batchNumber >> 32); k[10] = (byte)(batchNumber >> 40); k[11] = (byte)(batchNumber >> 48); k[12] = (byte)(batchNumber >> 56);
        return k;
    }

    private static uint ReadUInt32(byte[] data, int offset)
    {
        return (uint)data[offset]
            | ((uint)data[offset + 1] << 8)
            | ((uint)data[offset + 2] << 16)
            | ((uint)data[offset + 3] << 24);
    }

    private static ulong ReadUInt64(byte[] data, int offset)
    {
        return (ulong)data[offset]
            | ((ulong)data[offset + 1] << 8)
            | ((ulong)data[offset + 2] << 16)
            | ((ulong)data[offset + 3] << 24)
            | ((ulong)data[offset + 4] << 32)
            | ((ulong)data[offset + 5] << 40)
            | ((ulong)data[offset + 6] << 48)
            | ((ulong)data[offset + 7] << 56);
    }

    private static UInt256 ReadUInt256(byte[] data, int offset)
    {
        var slice = new byte[32];
        for (var i = 0; i < 32; i++) slice[i] = data[offset + i];
        return (UInt256)slice;
    }

    private static UInt160 ReadUInt160(byte[] data, int offset)
    {
        var slice = new byte[20];
        for (var i = 0; i < 20; i++) slice[i] = data[offset + i];
        return (UInt160)slice;
    }

    private static UInt160 ReadOptimisticSequencer(byte[] commitmentBytes)
    {
        ExecutionEngine.Assert(commitmentBytes.Length >= ProofBytesOffset, "commitment missing proof length");
        var proofLen = (int)ReadUInt32(commitmentBytes, ProofLenOffset);
        ExecutionEngine.Assert(proofLen >= OptimisticMinProofBytes, "optimistic proof too small");
        ExecutionEngine.Assert(proofLen <= OptimisticMaxProofBytes, "optimistic proof too large");
        ExecutionEngine.Assert(ProofBytesOffset + proofLen == commitmentBytes.Length,
            "commitment proof length mismatch");
        ExecutionEngine.Assert(commitmentBytes[ProofBytesOffset] == 2,
            "unsupported optimistic proof version");

        var sequencer = ReadUInt160(
            commitmentBytes,
            ProofBytesOffset + OptimisticSequencerOffsetInProof);
        ExecutionEngine.Assert(sequencer.IsValid && !sequencer.IsZero,
            "invalid optimistic sequencer");
        return sequencer;
    }
}
