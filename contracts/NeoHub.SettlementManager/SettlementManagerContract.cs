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
[ContractAuthor("Neo Project", "dev@neo.org")]
[ContractDescription("Batch settlement + canonical state root tracking for Neo Elastic Network.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/neo-project/neo4/tree/master/contracts/NeoHub.SettlementManager")]
[ContractPermission(Permission.Any, Method.Any)]
public class SettlementManagerContract : SmartContract
{
    private const byte PrefixBatchStatus = 0x01;          // 0x01 + chainId(4B) + batchNum(8B) → status byte
    private const byte PrefixBatchHeader = 0x02;          // 0x02 + chainId(4B) + batchNum(8B) → encoded header
    private const byte PrefixCanonicalRoot = 0x03;        // 0x03 + chainId(4B) → 32B canonical state root
    private const byte PrefixLatestBatch = 0x04;          // 0x04 + chainId(4B) → ulong latest finalized batch
    private const byte PrefixWithdrawalRoot = 0x05;       // 0x05 + chainId(4B) + batchNum(8B) → 32B withdrawalRoot
    private const byte PrefixChainRegistry = 0xFC;
    private const byte PrefixVerifierRegistry = 0xFD;
    private const byte KeyOwner = 0xFF;

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

    /// <summary>Set wiring on deploy.</summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var arr = (object[])data;
        var owner = (UInt160)arr[0];
        var chainRegistry = (UInt160)arr[1];
        var verifierRegistry = (UInt160)arr[2];
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
    }

    /// <summary>Governance owner.</summary>
    [Safe]
    public static UInt160 GetOwner()
    {
        var raw = Storage.Get(new byte[] { KeyOwner });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>
    /// Submit an L2BatchCommitment. The chain must be registered + active; the batch number
    /// must equal <c>latestFinalized + 1</c>; the verifier dispatches by ProofType.
    /// </summary>
    public static void SubmitBatch(byte[] commitmentBytes)
    {
        ExecutionEngine.Assert(commitmentBytes.Length >= 317, "commitment too small");

        var chainId = ReadUInt32(commitmentBytes, 0);
        var batchNumber = ReadUInt64(commitmentBytes, 4);

        // Chain must be registered + active.
        var chainRegistry = (UInt160)(Storage.Get(new byte[] { PrefixChainRegistry }) ?? throw new Exception("registry unset"));
        var isActive = (bool)Contract.Call(chainRegistry, "isActive", CallFlags.ReadOnly, new object[] { chainId });
        ExecutionEngine.Assert(isActive, "chain inactive");

        // Sequential batch numbers only.
        var latest = GetLatestFinalizedBatch(chainId);
        ExecutionEngine.Assert(batchNumber == latest + 1, "batch number out of sequence");

        // Don't double-submit.
        var statusKey = StatusKey(chainId, batchNumber);
        ExecutionEngine.Assert(Storage.Get(statusKey) == null, "batch already submitted");

        // Hand off proof verification to the registry (it dispatches by ProofType).
        var verifierRegistry = (UInt160)(Storage.Get(new byte[] { PrefixVerifierRegistry }) ?? throw new Exception("verifier registry unset"));
        var verified = (bool)Contract.Call(verifierRegistry, "verifyCommitment", CallFlags.ReadOnly, new object[] { commitmentBytes });
        ExecutionEngine.Assert(verified, "verifier rejected commitment");

        // Stage the batch as Pending. Whether it transitions to Finalized immediately or waits
        // out a challenge window depends on the chain's SecurityLevel; we leave that decision
        // to the verifier registry's response (it sets the appropriate next status).
        Storage.Put(statusKey, new byte[] { StatusPending });
        Storage.Put(BatchHeaderKey(chainId, batchNumber), commitmentBytes);

        // Capture the per-batch withdrawalRoot so the SharedBridge can consult it.
        var withdrawalRoot = ReadUInt256(commitmentBytes, 4 + 8 + 8 + 8 + 4 * 32);
        Storage.Put(WithdrawalRootKey(chainId, batchNumber), (byte[])withdrawalRoot);

        var postStateRoot = ReadUInt256(commitmentBytes, 4 + 8 + 8 + 8 + 32);
        OnBatchSubmitted(chainId, batchNumber, postStateRoot);
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

        var header = (byte[])(Storage.Get(BatchHeaderKey(chainId, batchNumber)) ?? throw new Exception("header missing"));
        var postStateRoot = ReadUInt256(header, 4 + 8 + 8 + 8 + 32);

        Storage.Put(key, new byte[] { StatusFinalized });
        Storage.Put(CanonicalRootKey(chainId), (byte[])postStateRoot);
        SetLatestFinalizedBatch(chainId, batchNumber);

        OnBatchFinalized(chainId, batchNumber, postStateRoot);
    }

    /// <summary>Mark a batch reverted (governance / successful fraud proof). Owner only.</summary>
    public static void RevertBatch(uint chainId, ulong batchNumber)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
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

    /// <summary>Latest finalized batch number for a chain.</summary>
    [Safe]
    public static ulong GetLatestFinalizedBatch(uint chainId)
    {
        var raw = Storage.Get(LatestBatchKey(chainId));
        return raw == null ? 0UL : (ulong)(BigInteger)raw;
    }

    /// <summary>
    /// True if <paramref name="leafHash"/> is the withdrawal root (or root prefix) of any
    /// finalized batch on <paramref name="chainId"/>. Used by SharedBridge.FinalizeWithdrawal.
    /// </summary>
    /// <remarks>
    /// MVP version: just confirms that the latest finalized batch's withdrawalRoot equals the
    /// supplied leaf-hash. A production version traverses every finalized batch and accepts
    /// any match, paired with an off-chain Merkle proof verification step.
    /// </remarks>
    [Safe]
    public static bool VerifyWithdrawalLeaf(uint chainId, UInt256 leafHash)
    {
        var latest = GetLatestFinalizedBatch(chainId);
        var raw = Storage.Get(WithdrawalRootKey(chainId, latest));
        if (raw == null) return false;
        var root = (UInt256)raw;
        return root.Equals(leafHash);
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
}
