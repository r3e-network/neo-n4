using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace NeoHub.SharedBridge;

/// <summary>
/// Canonical asset escrow shared across all Neo Elastic Network L2 chains. Locks GAS / NEO /
/// NEP-17 on deposit, releases against finalized <c>withdrawalRoot</c> proofs. See doc.md §11.
/// </summary>
[DisplayName("NeoHub.SharedBridge")]
[ContractAuthor("Neo Project", "dev@neo.org")]
[ContractDescription("Canonical asset escrow + L1↔L2 transfer for Neo Elastic Network.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.SharedBridge")]
[ContractPermission(Permission.Any, Method.Any)]
public class SharedBridgeContract : SmartContract
{
    private const byte PrefixDepositNonce = 0x01;     // 0x01 + chainId(4B) → next nonce (8B)
    private const byte PrefixDeposit = 0x02;          // 0x02 + chainId(4B) + nonce(8B) → encoded deposit msg
    private const byte PrefixWithdrawalConsumed = 0x03; // 0x03 + chainId(4B) + leafHash(32B) → 1
    private const byte PrefixSettlementManager = 0xFD;
    private const byte PrefixTokenRegistry = 0xFE;
    private const byte KeyOwner = 0xFF;

    /// <summary>Emitted when a user deposits into the bridge.</summary>
    [DisplayName("DepositEnqueued")]
    public static event Action<uint, ulong, UInt160, UInt160, BigInteger> OnDepositEnqueued = default!;

    /// <summary>Emitted when a withdrawal is finalized and assets released.</summary>
    [DisplayName("WithdrawalFinalized")]
    public static event Action<uint, UInt160, UInt160, BigInteger> OnWithdrawalFinalized = default!;

    /// <summary>Set bridge wiring on deploy.</summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var arr = (object[])data;
        var owner = (UInt160)arr[0];
        var settlementManager = (UInt160)arr[1];
        var tokenRegistry = (UInt160)arr[2];
        ExecutionEngine.Assert(owner.IsValid && !owner.IsZero, "invalid owner");
        ExecutionEngine.Assert(settlementManager.IsValid, "invalid settlement manager");
        ExecutionEngine.Assert(tokenRegistry.IsValid, "invalid token registry");
        Storage.Put(new byte[] { KeyOwner }, owner);
        Storage.Put(new byte[] { PrefixSettlementManager }, settlementManager);
        Storage.Put(new byte[] { PrefixTokenRegistry }, tokenRegistry);
    }

    /// <summary>Governance owner.</summary>
    [Safe]
    public static UInt160 GetOwner()
    {
        var raw = Storage.Get(new byte[] { KeyOwner });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Hash of the SettlementManager contract whose finalized batches we trust.</summary>
    [Safe]
    public static UInt160 GetSettlementManager()
    {
        var raw = Storage.Get(new byte[] { PrefixSettlementManager });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>
    /// Lock <paramref name="amount"/> of <paramref name="asset"/> from <see cref="Runtime.CallingScriptHash"/>'s
    /// allowance, allocate a deposit nonce for <paramref name="targetChainId"/>, and emit the
    /// canonical L1→L2 message. The L2 then consumes the message in its next batch.
    /// </summary>
    public static ulong Deposit(UInt160 asset, BigInteger amount, uint targetChainId, UInt160 l2Recipient)
    {
        ExecutionEngine.Assert(asset.IsValid && !asset.IsZero, "invalid asset");
        ExecutionEngine.Assert(amount > 0, "amount must be positive");
        ExecutionEngine.Assert(l2Recipient.IsValid && !l2Recipient.IsZero, "invalid recipient");
        // chainId 0 is the L1 sentinel — without this guard a deposit to chainId=0
        // would lock tokens in escrow that no L2 would ever pick up.
        ExecutionEngine.Assert(targetChainId > 0, "targetChainId 0 is reserved for L1");

        var caller = Runtime.CallingScriptHash;

        // Pull tokens into escrow. The asset must be NEP-17.
        var transferred = (bool)Contract.Call(
            asset, "transfer",
            CallFlags.All,
            new object[] { caller, Runtime.ExecutingScriptHash, amount, null! });
        ExecutionEngine.Assert(transferred, "asset transfer failed");

        var nonce = NextDepositNonce(targetChainId);
        var encoded = EncodeDeposit(asset, amount, l2Recipient, caller, nonce);
        Storage.Put(DepositKey(targetChainId, nonce), encoded);
        OnDepositEnqueued(targetChainId, nonce, caller, l2Recipient, amount);
        return nonce;
    }

    /// <summary>Read a previously enqueued deposit (used by L2 nodes when scanning the queue).</summary>
    [Safe]
    public static byte[] GetDeposit(uint chainId, ulong nonce)
    {
        var raw = Storage.Get(DepositKey(chainId, nonce));
        return raw == null ? new byte[0] : (byte[])raw;
    }

    /// <summary>
    /// Finalize a withdrawal anchored in the latest finalized batch on
    /// <paramref name="chainId"/>. Use <see cref="FinalizeWithdrawalAt"/> when the user's
    /// withdrawal is anchored in an older finalized batch.
    /// </summary>
    public static void FinalizeWithdrawal(
        uint chainId,
        UInt256 withdrawalLeafHash,
        UInt160 asset,
        UInt160 recipient,
        BigInteger amount)
    {
        ValidateWithdrawalArgs(chainId, asset, recipient, amount);
        var consumedKey = WithdrawalKey(chainId, withdrawalLeafHash);
        ExecutionEngine.Assert(Storage.Get(consumedKey) == null, "withdrawal already consumed");

        // Delegate Merkle proof verification to the SettlementManager via inter-contract call.
        var sm = GetSettlementManager();
        var verified = (bool)Contract.Call(
            sm, "verifyWithdrawalLeaf",
            CallFlags.ReadOnly,
            new object[] { chainId, withdrawalLeafHash });
        ExecutionEngine.Assert(verified, "withdrawal leaf not in finalized batch");

        ConsumeAndPayout(consumedKey, chainId, asset, recipient, amount);
    }

    /// <summary>
    /// Finalize a withdrawal anchored in a specific finalized batch on <paramref name="chainId"/>.
    /// Lets a user claim a withdrawal whose batch is no longer the latest — without this,
    /// the claim would silently fail once the chain has progressed past their batch.
    /// </summary>
    public static void FinalizeWithdrawalAt(
        uint chainId,
        ulong batchNumber,
        UInt256 withdrawalLeafHash,
        UInt160 asset,
        UInt160 recipient,
        BigInteger amount)
    {
        ValidateWithdrawalArgs(chainId, asset, recipient, amount);
        var consumedKey = WithdrawalKey(chainId, withdrawalLeafHash);
        ExecutionEngine.Assert(Storage.Get(consumedKey) == null, "withdrawal already consumed");

        // Verify against the explicitly-named finalized batch, not just the latest.
        var sm = GetSettlementManager();
        var verified = (bool)Contract.Call(
            sm, "verifyWithdrawalLeafAt",
            CallFlags.ReadOnly,
            new object[] { chainId, batchNumber, withdrawalLeafHash });
        ExecutionEngine.Assert(verified, "withdrawal leaf not in named finalized batch");

        ConsumeAndPayout(consumedKey, chainId, asset, recipient, amount);
    }

    /// <summary>
    /// Finalize a withdrawal using the production-shape Merkle inclusion proof. Use this
    /// when the L2 batch has many withdrawals — the user supplies the per-level sibling
    /// hashes and their leaf index so the L1 contract can re-derive the batch's
    /// withdrawalRoot and verify the user's specific leaf is in it.
    /// </summary>
    /// <remarks>
    /// Hash composition matches the off-chain <c>Neo.L2.State.MerkleTree</c>:
    /// <c>Sha256(Sha256(left || right))</c>, left/right ordered by the leaf-index bit
    /// for that level. See <c>SettlementManager.VerifyWithdrawalLeafWithProof</c>.
    /// </remarks>
    public static void FinalizeWithdrawalWithProof(
        uint chainId,
        ulong batchNumber,
        UInt256 withdrawalLeafHash,
        byte[][] siblings,
        ulong leafIndex,
        UInt160 asset,
        UInt160 recipient,
        BigInteger amount)
    {
        ValidateWithdrawalArgs(chainId, asset, recipient, amount);
        var consumedKey = WithdrawalKey(chainId, withdrawalLeafHash);
        ExecutionEngine.Assert(Storage.Get(consumedKey) == null, "withdrawal already consumed");

        var sm = GetSettlementManager();
        var verified = (bool)Contract.Call(
            sm, "verifyWithdrawalLeafWithProof",
            CallFlags.ReadOnly,
            new object[] { chainId, batchNumber, withdrawalLeafHash, siblings, leafIndex });
        ExecutionEngine.Assert(verified, "withdrawal leaf not in batch's Merkle root (proof failed)");

        ConsumeAndPayout(consumedKey, chainId, asset, recipient, amount);
    }

    private static void ValidateWithdrawalArgs(uint chainId, UInt160 asset, UInt160 recipient, BigInteger amount)
    {
        ExecutionEngine.Assert(chainId > 0, "chainId 0 is reserved for L1");
        ExecutionEngine.Assert(amount > 0, "amount must be positive");
        ExecutionEngine.Assert(asset.IsValid && !asset.IsZero, "invalid asset");
        ExecutionEngine.Assert(recipient.IsValid && !recipient.IsZero, "invalid recipient");
    }

    private static void ConsumeAndPayout(byte[] consumedKey, uint chainId, UInt160 asset, UInt160 recipient, BigInteger amount)
    {
        Storage.Put(consumedKey, new byte[] { 1 });

        var transferred = (bool)Contract.Call(
            asset, "transfer",
            CallFlags.All,
            new object[] { Runtime.ExecutingScriptHash, recipient, amount, null! });
        ExecutionEngine.Assert(transferred, "asset transfer failed");

        OnWithdrawalFinalized(chainId, asset, recipient, amount);
    }

    private static ulong NextDepositNonce(uint chainId)
    {
        var key = NonceKey(chainId);
        var raw = Storage.Get(key);
        ulong current = raw == null ? 0UL : (ulong)(BigInteger)raw;
        var next = current + 1;
        Storage.Put(key, (BigInteger)next);
        return next;
    }

    private static byte[] NonceKey(uint chainId)
    {
        var key = new byte[5];
        key[0] = PrefixDepositNonce;
        key[1] = (byte)chainId;
        key[2] = (byte)(chainId >> 8);
        key[3] = (byte)(chainId >> 16);
        key[4] = (byte)(chainId >> 24);
        return key;
    }

    private static byte[] DepositKey(uint chainId, ulong nonce)
    {
        var key = new byte[13];
        key[0] = PrefixDeposit;
        key[1] = (byte)chainId;
        key[2] = (byte)(chainId >> 8);
        key[3] = (byte)(chainId >> 16);
        key[4] = (byte)(chainId >> 24);
        key[5] = (byte)nonce;
        key[6] = (byte)(nonce >> 8);
        key[7] = (byte)(nonce >> 16);
        key[8] = (byte)(nonce >> 24);
        key[9] = (byte)(nonce >> 32);
        key[10] = (byte)(nonce >> 40);
        key[11] = (byte)(nonce >> 48);
        key[12] = (byte)(nonce >> 56);
        return key;
    }

    private static byte[] WithdrawalKey(uint chainId, UInt256 leafHash)
    {
        var key = new byte[5 + 32];
        key[0] = PrefixWithdrawalConsumed;
        key[1] = (byte)chainId;
        key[2] = (byte)(chainId >> 8);
        key[3] = (byte)(chainId >> 16);
        key[4] = (byte)(chainId >> 24);
        var hashBytes = (byte[])leafHash;
        for (var i = 0; i < 32; i++) key[5 + i] = hashBytes[i];
        return key;
    }

    private static byte[] EncodeDeposit(UInt160 asset, BigInteger amount, UInt160 recipient, UInt160 sender, ulong nonce)
    {
        // 20B asset + 20B recipient + 20B sender + 8B nonce + amount(varbytes)
        var amountBytes = amount.ToByteArray();
        var totalLen = 20 + 20 + 20 + 8 + 4 + amountBytes.Length;
        var buf = new byte[totalLen];
        var pos = 0;

        var assetBytes = (byte[])asset;
        for (var i = 0; i < 20; i++) buf[pos + i] = assetBytes[i];
        pos += 20;

        var recipBytes = (byte[])recipient;
        for (var i = 0; i < 20; i++) buf[pos + i] = recipBytes[i];
        pos += 20;

        var senderBytes = (byte[])sender;
        for (var i = 0; i < 20; i++) buf[pos + i] = senderBytes[i];
        pos += 20;

        buf[pos++] = (byte)nonce;
        buf[pos++] = (byte)(nonce >> 8);
        buf[pos++] = (byte)(nonce >> 16);
        buf[pos++] = (byte)(nonce >> 24);
        buf[pos++] = (byte)(nonce >> 32);
        buf[pos++] = (byte)(nonce >> 40);
        buf[pos++] = (byte)(nonce >> 48);
        buf[pos++] = (byte)(nonce >> 56);

        var len = amountBytes.Length;
        buf[pos++] = (byte)len;
        buf[pos++] = (byte)(len >> 8);
        buf[pos++] = (byte)(len >> 16);
        buf[pos++] = (byte)(len >> 24);
        for (var i = 0; i < len; i++) buf[pos + i] = amountBytes[i];

        return buf;
    }
}
