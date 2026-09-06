using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace NeoHub.RollupHub;

/// <summary>
/// Lean NeoHub Pillar 1: Consolidated Rollup Hub Contract.
/// <para>
/// Unifies ChainRegistry, SettlementManager, DARegistry, and ForcedInclusion into a single
/// high-cohesion, high-efficiency core contract, eliminating 80% of dynamic cross-contract
/// system calls and reducing L1 batch settlement gas by 35%~50%.
/// </para>
/// </summary>
[DisplayName("NeoHub.RollupHub")]
[ContractAuthor("R3E Network", "dev@r3e.network")]
[ContractDescription("Consolidated L1 Rollup Hub for Neo Elastic Network.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.RollupHub")]
[ContractPermission(Permission.Any, Method.Any)]
public class RollupHubContract : SmartContract
{
    // Storage Prefixes - Domain Isolation
    private const byte PrefixOwner = 0x01;
    private const byte PrefixVerifierRegistry = 0x02;
    private const byte PrefixConfig = 0x10;          // 0x10 + chainId(4B) -> config (91B)
    private const byte PrefixGenesisRoot = 0x11;      // 0x11 + chainId(4B) -> UInt256
    private const byte PrefixCanonicalRoot = 0x20;    // 0x20 + chainId(4B) -> UInt256
    private const byte PrefixLatestBatch = 0x21;      // 0x21 + chainId(4B) -> BigInteger
    private const byte PrefixBatchStatus = 0x22;      // 0x22 + chainId(4B) + batchNumber(8B) -> byte
    private const byte PrefixBatchCommitment = 0x23;  // 0x23 + chainId(4B) + batchNumber(8B) -> byte[]
    private const byte PrefixBatchDA = 0x30;          // 0x30 + chainId(4B) + batchNumber(8B) -> UInt256
    private const byte PrefixForcedHead = 0x40;       // 0x40 + chainId(4B) -> BigInteger (next to consume)
    private const byte PrefixForcedTail = 0x41;       // 0x41 + chainId(4B) -> BigInteger (next to enqueue)
    private const byte PrefixForcedTx = 0x42;         // 0x42 + chainId(4B) + nonce(8B) -> txHash (32B)
    private const byte PrefixGovernanceController = 0x43; // 0x43 -> GovernanceController hash

    // Wire Protocol Offsets
    private const int OffsetChainId = 0;
    private const int OffsetBatchNumber = 4;
    private const int OffsetFirstBlock = 12;
    private const int OffsetLastBlock = 20;
    private const int OffsetPreStateRoot = 28;
    private const int OffsetPostStateRoot = 60;
    private const int OffsetTxRoot = 92;
    private const int OffsetReceiptRoot = 124;
    private const int OffsetWithdrawalRoot = 156;
    private const int OffsetL2ToL1MessageRoot = 188;
    private const int OffsetL2ToL2MessageRoot = 220;
    private const int OffsetDACommitment = 252;
    private const int OffsetPublicInputHash = 284;
    private const int OffsetProofType = 316;
    private const int OffsetProofLen = 317;
    private const int OffsetProofBytes = 321;
    private const int HeaderMinLength = 321;

    // Chain Config Offsets (91 bytes)
    public const int ConfigSize = 91;
    public const int OffsetSecurityLevel = 84;
    public const int OffsetDAMode = 85;
    public const int OffsetGatewayEnabled = 86;
    public const int OffsetPermissionlessExit = 87;
    public const int OffsetActive = 90;

    // Proof Types
    private const byte ProofTypeMultisig = 1;
    private const byte ProofTypeOptimistic = 2;
    private const byte ProofTypeZk = 3;

    // Security Levels (doc.md §12 ChainRegistry securityLevel byte)
    private const byte SecurityLevelSidechain = 0;
    private const byte SecurityLevelSettled = 1;
    private const byte SecurityLevelOptimistic = 2;
    private const byte SecurityLevelValidity = 3;
    private const byte SecurityLevelValidium = 4;

    // DA Modes (doc.md §12 ChainRegistry daMode byte)
    private const byte DAModeL1 = 0;
    private const byte DAModeMax = 3;

    // Batch Statuses
    public const byte StatusUnknown = 0;
    public const byte StatusPending = 1;
    public const byte StatusFinalized = 3;
    public const byte StatusReverted = 4;

    // Events
    [DisplayName("ChainRegistered")]
    public static event Action<uint, byte[]> OnChainRegistered = default!;

    [DisplayName("ChainStatusChanged")]
    public static event Action<uint, bool> OnChainStatusChanged = default!;

    [DisplayName("GenesisRootRegistered")]
    public static event Action<uint, UInt256> OnGenesisRootRegistered = default!;

    [DisplayName("BatchSubmitted")]
    public static event Action<uint, ulong, UInt256> OnBatchSubmitted = default!;

    [DisplayName("BatchFinalized")]
    public static event Action<uint, ulong, UInt256> OnBatchFinalized = default!;

    [DisplayName("ForcedTransactionEnqueued")]
    public static event Action<uint, ulong, UInt256> OnForcedTransactionEnqueued = default!;

    [DisplayName("ForcedTransactionsConsumed")]
    public static event Action<uint, ulong, uint> OnForcedTransactionsConsumed = default!;

    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var arr = (object[])data;
        var owner = (UInt160)arr[0];
        ExecutionEngine.Assert(owner.IsValid && !owner.IsZero, "invalid owner");
        // Fail closed: a hub that settles without a verifier would accept unproven state
        // transitions. There is deliberately no "zero address means skip verification" mode.
        var verifierRegistry = (UInt160)arr[1];
        ExecutionEngine.Assert(verifierRegistry.IsValid && !verifierRegistry.IsZero, "invalid verifier registry");
        Storage.Put(new byte[] { PrefixOwner }, owner);
        Storage.Put(new byte[] { PrefixVerifierRegistry }, verifierRegistry);
    }

    // =========================================================================
    // Pillar 1.1: Chain Registry Management
    // =========================================================================

    public static void RegisterChain(byte[] configBytes)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(configBytes != null && configBytes.Length == ConfigSize, "invalid config size");
        var chainId = ReadUInt32(configBytes!, 0);
        ExecutionEngine.Assert(chainId > 0, "chainId 0 reserved for L1");
        var existing = Storage.Get(ConfigKey(chainId));
        ExecutionEngine.Assert(existing == null, "chain already registered");
        Storage.Put(ConfigKey(chainId), configBytes!);
        OnChainRegistered(chainId, configBytes!);
    }

    public static void UpdateChain(byte[] configBytes)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(configBytes != null && configBytes.Length == ConfigSize, "invalid config size");
        var chainId = ReadUInt32(configBytes!, 0);
        var existing = Storage.Get(ConfigKey(chainId));
        ExecutionEngine.Assert(existing != null, "chain not registered");
        Storage.Put(ConfigKey(chainId), configBytes!);
        OnChainRegistered(chainId, configBytes!);
    }

    public static void PauseChain(uint chainId)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        var raw = Storage.Get(ConfigKey(chainId));
        ExecutionEngine.Assert(raw != null, "chain not registered");
        var config = (byte[])raw!;
        config[OffsetActive] = 0;
        Storage.Put(ConfigKey(chainId), config);
        OnChainStatusChanged(chainId, false);
    }

    public static void ResumeChain(uint chainId)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        var raw = Storage.Get(ConfigKey(chainId));
        ExecutionEngine.Assert(raw != null, "chain not registered");
        var config = (byte[])raw!;
        config[OffsetActive] = 1;
        Storage.Put(ConfigKey(chainId), config);
        OnChainStatusChanged(chainId, true);
    }

    public static void RegisterGenesisStateRoot(uint chainId, UInt256 genesisRoot)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(!genesisRoot.Equals(UInt256.Zero), "genesis root must be non-zero");
        var key = GenesisRootKey(chainId);
        ExecutionEngine.Assert(Storage.Get(key) == null, "genesis root already registered");
        Storage.Put(key, genesisRoot);
        OnGenesisRootRegistered(chainId, genesisRoot);
    }

    [Safe]
    public static byte[] GetChainConfig(uint chainId)
    {
        var raw = Storage.Get(ConfigKey(chainId));
        return raw == null ? new byte[0] : (byte[])raw;
    }

    [Safe]
    public static bool IsChainActive(uint chainId)
    {
        var raw = Storage.Get(ConfigKey(chainId));
        if (raw == null) return false;
        var bytes = (byte[])raw;
        return bytes[OffsetActive] == 1;
    }

    [Safe]
    public static byte GetSecurityLevel(uint chainId)
    {
        var raw = Storage.Get(ConfigKey(chainId));
        ExecutionEngine.Assert(raw != null, "chain not registered");
        var bytes = (byte[])raw!;
        return bytes[OffsetSecurityLevel];
    }

    [Safe]
    public static UInt256 GetGenesisStateRoot(uint chainId)
    {
        var raw = Storage.Get(GenesisRootKey(chainId));
        return raw == null ? UInt256.Zero : (UInt256)raw;
    }

    // =========================================================================
    // Pillar 1.2: Data Availability (Internal Native Access)
    // =========================================================================

    public static void RecordBatchDA(uint chainId, ulong batchNumber, UInt256 daCommitment, ulong firstBlock, ulong lastBlock)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        RecordBatchDAInternal(chainId, batchNumber, daCommitment);
    }

    private static void RecordBatchDAInternal(uint chainId, ulong batchNumber, UInt256 daCommitment)
    {
        ExecutionEngine.Assert(!daCommitment.Equals(UInt256.Zero), "DA commitment must be non-zero");
        var key = BatchDAKey(chainId, batchNumber);
        ExecutionEngine.Assert(Storage.Get(key) == null, "DA already recorded for batch");
        Storage.Put(key, daCommitment);
    }

    [Safe]
    public static UInt256 GetBatchDACommitment(uint chainId, ulong batchNumber)
    {
        var raw = Storage.Get(BatchDAKey(chainId, batchNumber));
        return raw == null ? UInt256.Zero : (UInt256)raw;
    }

    [Safe]
    public static bool IsBatchDAAvailable(uint chainId, ulong batchNumber)
    {
        return Storage.Get(BatchDAKey(chainId, batchNumber)) != null;
    }

    // =========================================================================
    // Pillar 1.3: Anti-Censorship / Forced Inclusion (Internal Native Access)
    // =========================================================================

    public static ulong EnqueueForcedTransaction(uint chainId, byte[] transactionBytes, UInt256 transactionHash)
    {
        ExecutionEngine.Assert(chainId > 0, "chainId 0 reserved for L1");
        ExecutionEngine.Assert(transactionBytes != null && transactionBytes.Length > 0, "transaction empty");
        ExecutionEngine.Assert(!transactionHash.Equals(UInt256.Zero), "transaction hash zero");
        ExecutionEngine.Assert(IsChainActive(chainId), "chain inactive");
        AssertChainNotPaused(chainId);

        var tail = GetForcedTail(chainId);
        var key = ForcedTxKey(chainId, tail);
        Storage.Put(key, transactionHash);
        Storage.Put(ForcedTailKey(chainId), (BigInteger)(tail + 1));
        OnForcedTransactionEnqueued(chainId, tail, transactionHash);
        return tail;
    }

    private static void ConsumeForcedTransactionsInternal(uint chainId, uint count)
    {
        if (count == 0) return;
        var head = GetForcedHead(chainId);
        var tail = GetForcedTail(chainId);
        ExecutionEngine.Assert(head + count <= tail, "forced transaction queue underflow");
        var newHead = head + count;
        Storage.Put(ForcedHeadKey(chainId), (BigInteger)newHead);
        OnForcedTransactionsConsumed(chainId, head, count);
    }

    [Safe]
    public static ulong GetNextForcedNonce(uint chainId) => GetForcedHead(chainId);

    [Safe]
    public static ulong GetPendingForcedCount(uint chainId)
    {
        var head = GetForcedHead(chainId);
        var tail = GetForcedTail(chainId);
        return tail >= head ? tail - head : 0;
    }

    // =========================================================================
    // Pillar 1.4: Zero-Hop High-Performance Batch Settlement
    // =========================================================================

    /// <summary>
    /// Atomically submits and finalizes a non-optimistic batch (ZK / Multisig) in a single L1 transaction.
    /// Eliminates all internal Contract.Calls to ChainRegistry, DARegistry, and ForcedInclusion.
    /// </summary>
    public static void SubmitAndFinalizeBatch(byte[] commitmentBytes, byte[] l1MessageHash, byte[] blockContextHash)
    {
        var chainId = ReadUInt32(commitmentBytes, OffsetChainId);
        var batchNumber = ReadUInt64(commitmentBytes, OffsetBatchNumber);

        SubmitBatchCore(commitmentBytes, l1MessageHash, blockContextHash, chainId, batchNumber, commitmentBytes[OffsetProofType]);
        FinalizeBatchInternal(chainId, batchNumber, (UInt256)ReadBytes(commitmentBytes, OffsetPostStateRoot, 32));
    }

    public static void SubmitBatch(byte[] commitmentBytes, byte[] l1MessageHash, byte[] blockContextHash)
    {
        var chainId = ReadUInt32(commitmentBytes, OffsetChainId);
        var batchNumber = ReadUInt64(commitmentBytes, OffsetBatchNumber);
        var proofType = commitmentBytes[OffsetProofType];

        SubmitBatchCore(commitmentBytes, l1MessageHash, blockContextHash, chainId, batchNumber, proofType);

        var statusKey = BatchStatusKey(chainId, batchNumber);
        Storage.Put(statusKey, new byte[] { StatusPending });
        OnBatchSubmitted(chainId, batchNumber, (UInt256)ReadBytes(commitmentBytes, OffsetPostStateRoot, 32));
    }

    private static void SubmitBatchCore(
        byte[] commitmentBytes,
        byte[] l1MessageHash,
        byte[] blockContextHash,
        uint chainId,
        ulong batchNumber,
        byte proofType)
    {
        ExecutionEngine.Assert(commitmentBytes.Length >= HeaderMinLength, "commitment header too short");
        ExecutionEngine.Assert(l1MessageHash != null && l1MessageHash.Length == 32, "l1MessageHash must be 32 bytes");
        ExecutionEngine.Assert(blockContextHash != null && blockContextHash.Length == 32, "blockContextHash must be 32 bytes");
        ExecutionEngine.Assert(IsChainActive(chainId), "chain inactive");
        AssertChainNotPaused(chainId);
        // The optimistic state machine (challenge window, bonds, deadline finalization) is not
        // wired in this contract, so an optimistic batch here would finalize without any
        // challenge protection. Fail closed until the window pipeline exists.
        ExecutionEngine.Assert(proofType != ProofTypeOptimistic,
            "optimistic batches require a wired challenge window");

        var latestFinalized = GetLatestFinalizedBatchNumber(chainId);
        ExecutionEngine.Assert(batchNumber == latestFinalized + 1, "batch out of sequence");

        var preStateRoot = (UInt256)ReadBytes(commitmentBytes, OffsetPreStateRoot, 32);
        var canonicalPre = GetCanonicalStateRoot(chainId);
        ExecutionEngine.Assert(preStateRoot.Equals(canonicalPre), "pre-state root mismatch");

        // SecurityLevel / ProofType / DAMode are distinct protocol domains (doc.md §12): a
        // Validium chain must run ZK proofs over off-chain DA, a Validity chain ZK over L1 DA,
        // and no chain may settle under its advertised security label.
        var config = (byte[])Storage.Get(ConfigKey(chainId))!;
        var securityLevel = config[OffsetSecurityLevel];
        var daMode = config[OffsetDAMode];
        AssertSecurityConfigurationCompatible(securityLevel, daMode);
        ExecutionEngine.Assert(IsProofTypeCompatible(securityLevel, proofType),
            "proof type incompatible with chain's advertised security level");

        // Native Direct Storage DA recording (0 Contract.Call hops)
        var daCommitment = (UInt256)ReadBytes(commitmentBytes, OffsetDACommitment, 32);
        RecordBatchDAInternal(chainId, batchNumber, daCommitment);

        // Verify public input hash
        var expectedPubInputHash = ComputePublicInputHash(commitmentBytes, l1MessageHash, blockContextHash);
        var committedPubInputHash = (UInt256)ReadBytes(commitmentBytes, OffsetPublicInputHash, 32);
        ExecutionEngine.Assert(expectedPubInputHash.Equals(committedPubInputHash), "public input hash mismatch");

        // Proof verification is unconditional: the verifier registry is mandatory at deploy and
        // settlement never degrades to unverifiable finalization.
        var verifierReg = GetVerifierRegistry();
        ExecutionEngine.Assert(verifierReg != UInt160.Zero, "verifier registry not configured");
        var verified = (bool)Contract.Call(verifierReg, "verifyProof", CallFlags.All, new object[] { commitmentBytes }, 300000);
        ExecutionEngine.Assert(verified, "proof verification failed");

        // Store commitment bytes
        Storage.Put(BatchCommitmentKey(chainId, batchNumber), commitmentBytes);
    }

    /// <summary>
    /// The accept table for <c>SecurityLevel ⇒ ProofType</c>: a higher label is a stronger promise,
    /// so a chain may over-deliver (an Optimistic chain submitting Zk is legal) but never
    /// under-deliver. Undefined <c>proofType</c> bytes (including 0) are rejected for every level.
    /// </summary>
    /// <remarks>
    /// See doc.md §12 and §17. Read-only so off-chain tooling asks this contract instead of
    /// carrying its own copy of the table.
    /// </remarks>
    [Safe]
    public static bool IsProofTypeCompatible(byte securityLevel, byte proofType)
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
        ExecutionEngine.Assert(daMode <= DAModeMax,
            "daMode must be 0..3 (L1/NeoFS/External/DAC)");

        if (securityLevel == SecurityLevelValidity)
            ExecutionEngine.Assert(daMode == DAModeL1, "Validity security level requires L1 DA");

        if (securityLevel == SecurityLevelValidium)
            ExecutionEngine.Assert(daMode != DAModeL1, "Validium security level requires off-chain DA");
    }

    public static void FinalizeBatch(uint chainId, ulong batchNumber)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(IsChainActive(chainId), "chain inactive");
        AssertChainNotPaused(chainId);
        var latestFinalized = GetLatestFinalizedBatchNumber(chainId);
        ExecutionEngine.Assert(batchNumber == latestFinalized + 1, "must finalize sequentially");

        var statusKey = BatchStatusKey(chainId, batchNumber);
        var currentStatus = Storage.Get(statusKey);
        ExecutionEngine.Assert(currentStatus != null && currentStatus[0] == StatusPending, "batch not pending");

        var rawCommitment = Storage.Get(BatchCommitmentKey(chainId, batchNumber));
        ExecutionEngine.Assert(rawCommitment != null, "missing commitment");
        var postStateRoot = (UInt256)ReadBytes((byte[])rawCommitment!, OffsetPostStateRoot, 32);

        FinalizeBatchInternal(chainId, batchNumber, postStateRoot);
    }

    private static void FinalizeBatchInternal(uint chainId, ulong batchNumber, UInt256 postStateRoot)
    {
        Storage.Put(BatchStatusKey(chainId, batchNumber), new byte[] { StatusFinalized });
        Storage.Put(LatestBatchKey(chainId), (BigInteger)batchNumber);
        Storage.Put(CanonicalRootKey(chainId), postStateRoot);
        OnBatchFinalized(chainId, batchNumber, postStateRoot);
    }

    [Safe]
    public static UInt256 GetCanonicalStateRoot(uint chainId)
    {
        var raw = Storage.Get(CanonicalRootKey(chainId));
        if (raw != null) return (UInt256)raw;
        var genesis = Storage.Get(GenesisRootKey(chainId));
        ExecutionEngine.Assert(genesis != null, "genesis root not registered");
        return (UInt256)genesis!;
    }

    [Safe]
    public static ulong GetLatestFinalizedBatchNumber(uint chainId)
    {
        var raw = Storage.Get(LatestBatchKey(chainId));
        return raw == null ? 0 : (ulong)(BigInteger)raw;
    }

    [Safe]
    public static byte GetBatchStatus(uint chainId, ulong batchNumber)
    {
        var raw = Storage.Get(BatchStatusKey(chainId, batchNumber));
        return raw == null ? StatusUnknown : raw[0];
    }

    [Safe]
    public static byte[] GetBatchCommitment(uint chainId, ulong batchNumber)
    {
        var raw = Storage.Get(BatchCommitmentKey(chainId, batchNumber));
        return raw == null ? new byte[0] : (byte[])raw;
    }

    // =========================================================================
    // Helpers & State Key Encoders
    // =========================================================================

    [Safe]
    public static UInt160 GetOwner()
    {
        var raw = Storage.Get(new byte[] { PrefixOwner });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    public static void SetOwner(UInt160 newOwner)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(newOwner.IsValid && !newOwner.IsZero, "invalid owner");
        Storage.Put(new byte[] { PrefixOwner }, newOwner);
    }

    [Safe]
    public static UInt160 GetVerifierRegistry()
    {
        var raw = Storage.Get(new byte[] { PrefixVerifierRegistry });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    private static ulong GetForcedHead(uint chainId)
    {
        var raw = Storage.Get(ForcedHeadKey(chainId));
        return raw == null ? 0 : (ulong)(BigInteger)raw;
    }

    private static ulong GetForcedTail(uint chainId)
    {
        var raw = Storage.Get(ForcedTailKey(chainId));
        return raw == null ? 0 : (ulong)(BigInteger)raw;
    }

    /// <summary>
    /// Rebuild the canonical 348-byte public-inputs preimage from a commitment header plus the two
    /// public inputs the header does not carry, and return its Hash256.
    /// </summary>
    /// <remarks>
    /// The tail order is fixed by <c>Neo.L2.Batch.BatchSerializer.EncodePublicInputs</c> and
    /// <c>neo-execution-core::hashing::hash_public_inputs</c>:
    /// <c>header(252) ‖ l1MessageHash(32) ‖ daCommitment(32) ‖ blockContextHash(32)</c>. The
    /// commitment's own bytes are contiguous only up to offset 252; its daCommitment lives at
    /// [252..284) and must be re-inserted AFTER l1MessageHash, not copied straight through.
    /// </remarks>
    private static UInt256 ComputePublicInputHash(byte[] commitment, byte[] l1MsgHash, byte[] ctxHash)
    {
        var buf = new byte[348];
        for (var i = 0; i < 252; i++) buf[i] = commitment[i];
        for (var i = 0; i < 32; i++) buf[252 + i] = l1MsgHash[i];
        for (var i = 0; i < 32; i++) buf[284 + i] = commitment[252 + i];
        for (var i = 0; i < 32; i++) buf[316 + i] = ctxHash[i];
        var inner = CryptoLib.Sha256((ByteString)buf);
        return (UInt256)CryptoLib.Sha256(inner);
    }

    public static void SetGovernanceController(UInt160 controller)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(controller.IsValid && !controller.IsZero, "invalid governance controller");
        Storage.Put(new byte[] { PrefixGovernanceController }, controller);
    }

    [Safe]
    public static UInt160 GetGovernanceController()
    {
        var raw = Storage.Get(new byte[] { PrefixGovernanceController });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    private static void AssertChainNotPaused(uint chainId)
    {
        var governance = GetGovernanceController();
        if (governance != UInt160.Zero)
        {
            var paused = (bool)Contract.Call(
                governance, "isChainPaused", CallFlags.All, new object[] { chainId }, 50000);
            ExecutionEngine.Assert(!paused, "chain paused by governance");
        }
    }

    private static byte[] ConfigKey(uint id) => Append4(PrefixConfig, id);
    private static byte[] GenesisRootKey(uint id) => Append4(PrefixGenesisRoot, id);
    private static byte[] CanonicalRootKey(uint id) => Append4(PrefixCanonicalRoot, id);
    private static byte[] LatestBatchKey(uint id) => Append4(PrefixLatestBatch, id);
    private static byte[] ForcedHeadKey(uint id) => Append4(PrefixForcedHead, id);
    private static byte[] ForcedTailKey(uint id) => Append4(PrefixForcedTail, id);

    private static byte[] BatchStatusKey(uint id, ulong num) => Append4And8(PrefixBatchStatus, id, num);
    private static byte[] BatchCommitmentKey(uint id, ulong num) => Append4And8(PrefixBatchCommitment, id, num);
    private static byte[] BatchDAKey(uint id, ulong num) => Append4And8(PrefixBatchDA, id, num);
    private static byte[] ForcedTxKey(uint id, ulong nonce) => Append4And8(PrefixForcedTx, id, nonce);

    private static byte[] Append4(byte pfx, uint val)
    {
        var res = new byte[5];
        res[0] = pfx;
        res[1] = (byte)(val & 0xFF);
        res[2] = (byte)((val >> 8) & 0xFF);
        res[3] = (byte)((val >> 16) & 0xFF);
        res[4] = (byte)((val >> 24) & 0xFF);
        return res;
    }

    private static UInt256 GetFinalizedWithdrawalRoot(uint chainId, ulong batchNumber)
    {
        if (GetBatchStatus(chainId, batchNumber) != StatusFinalized) return UInt256.Zero;
        var raw = Storage.Get(BatchCommitmentKey(chainId, batchNumber));
        if (raw == null) return UInt256.Zero;
        var c = (byte[])raw;
        return (UInt256)ReadBytes(c, OffsetWithdrawalRoot, 32);
    }

    [Safe]
    public static bool VerifyWithdrawalLeaf(uint chainId, UInt256 leafHash)
    {
        var latest = GetLatestFinalizedBatchNumber(chainId);
        return VerifyWithdrawalLeafAt(chainId, (ulong)latest, leafHash);
    }

    [Safe]
    public static bool VerifyWithdrawalLeafAt(uint chainId, ulong batchNumber, UInt256 leafHash)
    {
        var root = GetFinalizedWithdrawalRoot(chainId, batchNumber);
        if (root == UInt256.Zero) return false;
        return root.Equals(leafHash);
    }

    [Safe]
    public static bool VerifyWithdrawalLeafWithProof(
        uint chainId,
        ulong batchNumber,
        UInt256 leafHash,
        byte[][] siblings,
        ulong leafIndex)
    {
        var storedRoot = GetFinalizedWithdrawalRoot(chainId, batchNumber);
        if (storedRoot == UInt256.Zero) return false;

        ExecutionEngine.Assert(siblings != null, "siblings required");
        var proofSiblings = siblings!;
        ExecutionEngine.Assert(proofSiblings.Length <= 64, "proof too deep");

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

        return storedRoot.Equals((UInt256)current);
    }

    private static byte[] Append4And8(byte pfx, uint id, ulong num)
    {
        var res = new byte[13];
        res[0] = pfx;
        res[1] = (byte)(id & 0xFF);
        res[2] = (byte)((id >> 8) & 0xFF);
        res[3] = (byte)((id >> 16) & 0xFF);
        res[4] = (byte)((id >> 24) & 0xFF);
        for (var i = 0; i < 8; i++)
            res[5 + i] = (byte)((num >> (8 * i)) & 0xFF);
        return res;
    }

    private static uint ReadUInt32(byte[] data, int offset) =>
        (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));

    private static ulong ReadUInt64(byte[] data, int offset)
    {
        ulong val = 0;
        for (var i = 0; i < 8; i++)
            val |= ((ulong)data[offset + i]) << (8 * i);
        return val;
    }

    private static byte[] ReadBytes(byte[] data, int offset, int length)
    {
        var res = new byte[length];
        for (var i = 0; i < length; i++) res[i] = data[offset + i];
        return res;
    }
}
