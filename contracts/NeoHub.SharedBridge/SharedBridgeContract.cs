using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace NeoHub.SharedBridge;

/// <summary>
/// Canonical asset escrow shared across all Neo Elastic Network L2 chains. Locks platform assets
/// (GAS / NEO / USDT / USDC / BTC) and NEP-17 tokens on deposit, releases against finalized
/// <c>withdrawalRoot</c> proofs. Also hosts native asset mapping (former TokenRegistry) and
/// cross-chain messaging (former MessageRouter) in a unified Pillar 2 contract. See doc.md §11.
/// </summary>
[DisplayName("NeoHub.SharedBridge")]
[ContractAuthor("R3E Network", "dev@r3e.network")]
[ContractDescription("Canonical asset escrow + L1↔L2 transfer + message router for Neo Elastic Network.")]
[ContractVersion("0.2.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.SharedBridge")]
[ContractPermission(Permission.Any, Method.Any)]
public class SharedBridgeContract : SmartContract
{
    private const byte PrefixDepositNonce = 0x01;       // 0x01 + chainId(4B) → next nonce (8B)
    private const byte PrefixDeposit = 0x02;            // 0x02 + chainId(4B) + nonce(8B) → encoded deposit msg
    private const byte PrefixWithdrawalConsumed = 0x03; // 0x03 + chainId(4B) + leafHash(32B) → 1
    private const byte PrefixPendingTransfer = 0x04;    // 0x04 + asset(20B) + from(20B) → 1
    private const byte PrefixLockedBalance = 0x05;      // 0x05 + chainId(4B) + asset(20B) → per-chain escrowed amount (BigInteger)
    private const byte KeyLockedBalanceMigrationSealed = 0x06; // 1 once the migration backfill is sealed

    // Native asset mapping prefixes (former TokenRegistry)
    private const byte PrefixMapping = 0x10;            // 0x10 + l1Asset(20B) + l2ChainId(4B) → encoded mapping (50B)

    // Native cross-chain messaging prefixes (former MessageRouter)
    private const byte PrefixL1ToL2Nonce = 0x20;         // 0x20 + targetChainId(4B) → next nonce (8B)
    private const byte PrefixL1ToL2Msg = 0x21;           // 0x21 + targetChainId(4B) + nonce(8B) → encoded msg
    private const byte PrefixL2ToL1Root = 0x22;          // 0x22 + chainId(4B) + batchNum(8B) → root
    private const byte PrefixL2ToL2Root = 0x23;          // 0x23 + chainId(4B) + batchNum(8B) → root
    private const byte PrefixConsumedMsg = 0x24;         // 0x24 + msgHash(32B) → 1

    private const byte PrefixEmergencyManager = 0xFC;
    private const byte PrefixSettlementManager = 0xFD;
    private const byte PrefixTokenRegistry = 0xFE;
    private const byte KeyOwner = 0xFF;

    private const byte AssetTypeGas = 0;
    private const byte AssetTypeNeo = 1;
    public const int MappingSize = 20 + 4 + 20 + 6; // 50 bytes

    #region Events

    [DisplayName("DepositEnqueued")]
    public static event Action<uint, ulong, UInt160, UInt160, BigInteger> OnDepositEnqueued = default!;

    [DisplayName("WithdrawalFinalized")]
    public static event Action<uint, UInt160, UInt160, BigInteger> OnWithdrawalFinalized = default!;

    [DisplayName("MappingRegistered")]
    public static event Action<UInt160, uint, UInt160> OnMappingRegistered = default!;

    [DisplayName("MappingActiveChanged")]
    public static event Action<UInt160, uint, bool> OnMappingActiveChanged = default!;

    [DisplayName("L1ToL2Enqueued")]
    public static event Action<uint, ulong, UInt160, UInt160> OnL1ToL2Enqueued = default!;

#pragma warning disable CS0414
    [DisplayName("L2ToL1Consumed")]
    public static event Action<uint, UInt256> OnL2ToL1Consumed = default!;
#pragma warning restore CS0414

    [DisplayName("MessageRootsPublished")]
    public static event Action<uint, ulong, UInt256, UInt256> OnMessageRootsPublished = default!;

    [DisplayName("OwnerChanged")]
    public static event Action<UInt160, UInt160> OnOwnerChanged = default!;

    [DisplayName("EmergencyManagerChanged")]
    public static event Action<UInt160> OnEmergencyManagerChanged = default!;

    [DisplayName("LockedBalanceMigrated")]
    public static event Action<uint, UInt160, BigInteger> OnLockedBalanceMigrated = default!;

    [DisplayName("LockedBalanceMigrationSealed")]
    public static event Action OnLockedBalanceMigrationSealed = default!;

    #endregion

    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var arr = (object[])data;
        var owner = (UInt160)arr[0];
        ExecutionEngine.Assert(owner.IsValid && !owner.IsZero, "invalid owner");
        Storage.Put(new byte[] { KeyOwner }, owner);
        if (arr.Length > 1)
        {
            var sm = (UInt160)arr[1];
            Storage.Put(new byte[] { PrefixSettlementManager }, sm);
        }
        if (arr.Length > 2)
        {
            var tr = (UInt160)arr[2];
            Storage.Put(new byte[] { PrefixTokenRegistry }, tr);
        }
        Storage.Put(new byte[] { KeyLockedBalanceMigrationSealed }, new byte[] { 1 });
    }

    #region Owner & Administration

    [Safe]
    public static UInt160 GetOwner()
    {
        var raw = Storage.Get(new byte[] { KeyOwner });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    public static void SetOwner(UInt160 newOwner)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(newOwner.IsValid && !newOwner.IsZero, "invalid new owner");
        var oldOwner = GetOwner();
        Storage.Put(new byte[] { KeyOwner }, newOwner);
        OnOwnerChanged(oldOwner, newOwner);
    }

    [Safe]
    public static UInt160 GetSettlementManager()
    {
        var raw = Storage.Get(new byte[] { PrefixSettlementManager });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    public static void SetSettlementManager(UInt160 settlementManager)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        Storage.Put(new byte[] { PrefixSettlementManager }, settlementManager);
    }

    [Safe]
    public static UInt160 GetTokenRegistry()
    {
        var raw = Storage.Get(new byte[] { PrefixTokenRegistry });
        return (raw == null || (UInt160)raw == UInt160.Zero) ? Runtime.ExecutingScriptHash : (UInt160)raw;
    }

    public static void SetTokenRegistry(UInt160 tokenRegistry)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        Storage.Put(new byte[] { PrefixTokenRegistry }, tokenRegistry);
    }

    [Safe]
    public static UInt160 GetEmergencyManager()
    {
        var raw = Storage.Get(new byte[] { PrefixEmergencyManager });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    public static void SetEmergencyManager(UInt160 emergencyManager)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        Storage.Put(new byte[] { PrefixEmergencyManager }, emergencyManager);
        OnEmergencyManagerChanged(emergencyManager);
    }

    private static bool IsPaused()
    {
        var em = GetEmergencyManager();
        if (em == UInt160.Zero) return false;
        return (bool)Contract.Call(em, "isPaused", CallFlags.All, new object[0], 50000);
    }

    #endregion

    #region Asset Mapping (former TokenRegistry)

    public static void RegisterMapping(byte[] mappingBytes)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(mappingBytes.Length == MappingSize, "mapping size mismatch");

        var l1Asset = ReadUInt160(mappingBytes, 0);
        var chainId = ReadUInt32(mappingBytes, 20);
        var l2Asset = ReadUInt160(mappingBytes, 24);
        var assetType = mappingBytes[44];
        var l1Decimals = mappingBytes[47];
        var l2Decimals = mappingBytes[48];

        ExecutionEngine.Assert(l1Asset.IsValid && !l1Asset.IsZero, "invalid L1 asset");
        ExecutionEngine.Assert(l2Asset.IsValid && !l2Asset.IsZero, "invalid L2 asset");
        ExecutionEngine.Assert(chainId > 0, "chainId 0 is reserved for L1");
        ExecutionEngine.Assert(l1Decimals <= 18 && l2Decimals <= 18, "invalid decimals");
        if (assetType == AssetTypeNeo)
        {
            ExecutionEngine.Assert(l1Decimals == 0, "L1 NEO decimals must be 0");
            ExecutionEngine.Assert(l2Decimals == 8, "L2 NEO decimals must be 8");
        }
        if (assetType == AssetTypeGas)
        {
            ExecutionEngine.Assert(l1Decimals == 8, "GAS decimals must be 8");
            ExecutionEngine.Assert(l2Decimals == 8, "GAS decimals must be 8");
        }

        var key = MappingKey(l1Asset, chainId);
        Storage.Put(key, mappingBytes);
        OnMappingRegistered(l1Asset, chainId, l2Asset);
    }

    public static void SetActive(UInt160 l1Asset, uint chainId, bool active)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        var key = MappingKey(l1Asset, chainId);
        var raw = Storage.Get(key);
        ExecutionEngine.Assert(raw != null, "mapping not found");

        var buf = (byte[])raw!;
        buf[49] = active ? (byte)1 : (byte)0;
        Storage.Put(key, buf);
        OnMappingActiveChanged(l1Asset, chainId, active);
    }

    [Safe]
    public static byte[]? GetMapping(UInt160 l1Asset, uint chainId)
    {
        var raw = Storage.Get(MappingKey(l1Asset, chainId));
        return raw == null ? null : (byte[])raw;
    }

    [Safe]
    public static UInt160 GetL2Asset(UInt160 l1Asset, uint chainId)
    {
        var raw = Storage.Get(MappingKey(l1Asset, chainId));
        if (raw != null) return ReadUInt160((byte[])raw, 24);

        var tr = Storage.Get(new byte[] { PrefixTokenRegistry });
        if (tr != null && (UInt160)tr != UInt160.Zero && (UInt160)tr != Runtime.ExecutingScriptHash)
        {
            return (UInt160)Contract.Call((UInt160)tr, "getL2Asset", CallFlags.All, new object[] { l1Asset, chainId }, 50000);
        }
        return UInt160.Zero;
    }

    [Safe]
    public static bool IsActive(UInt160 l1Asset, uint chainId)
    {
        var raw = Storage.Get(MappingKey(l1Asset, chainId));
        if (raw != null) return ((byte[])raw)[49] == 1;

        var tr = Storage.Get(new byte[] { PrefixTokenRegistry });
        if (tr != null && (UInt160)tr != UInt160.Zero && (UInt160)tr != Runtime.ExecutingScriptHash)
        {
            return (bool)Contract.Call((UInt160)tr, "isActive", CallFlags.All, new object[] { l1Asset, chainId }, 50000);
        }
        return false;
    }

    [Safe]
    public static BigInteger GetL1Decimals(UInt160 l1Asset, uint chainId)
    {
        var raw = Storage.Get(MappingKey(l1Asset, chainId));
        return raw == null ? -1 : ((byte[])raw)[47];
    }

    [Safe]
    public static BigInteger GetL2Decimals(UInt160 l1Asset, uint chainId)
    {
        var raw = Storage.Get(MappingKey(l1Asset, chainId));
        return raw == null ? -1 : ((byte[])raw)[48];
    }

    #endregion

    #region Asset Escrow: Deposit & Withdrawal

    public static ulong Deposit(UInt160 asset, BigInteger amount, uint targetChainId, UInt160 l2Recipient)
    {
        ExecutionEngine.Assert(!IsPaused(), "network paused");
        ExecutionEngine.Assert(asset.IsValid && !asset.IsZero, "invalid asset");
        ExecutionEngine.Assert(amount > 0, "amount must be positive");
        ExecutionEngine.Assert(l2Recipient.IsValid && !l2Recipient.IsZero, "invalid recipient");
        ExecutionEngine.Assert(targetChainId > 0, "targetChainId 0 is reserved for L1");

        var mappedL2Asset = GetL2Asset(asset, targetChainId);
        ExecutionEngine.Assert(mappedL2Asset.IsValid && !mappedL2Asset.IsZero, "asset not mapped for target chain");
        ExecutionEngine.Assert(IsActive(asset, targetChainId), "asset mapping inactive");

        var depositor = Runtime.Transaction.Sender;
        ExecutionEngine.Assert(depositor.IsValid && !depositor.IsZero, "invalid transaction sender");
        ExecutionEngine.Assert(Runtime.CheckWitness(depositor), "transaction sender witness required");

        var nonce = NextDepositNonce(targetChainId);
        var encoded = EncodeDeposit(asset, amount, l2Recipient, depositor, nonce);
        Storage.Put(DepositKey(targetChainId, nonce), encoded);

        var pendingKey = PendingTransferKey(asset, depositor);
        Storage.Put(pendingKey, new byte[] { 1 });
        var transferred = (bool)Contract.Call(
            asset, "transfer",
            CallFlags.All,
            new object[] { depositor, Runtime.ExecutingScriptHash, amount, null! }, 300000);
        ExecutionEngine.Assert(transferred, "asset transfer failed");
        Storage.Delete(pendingKey);

        CreditLockedBalance(targetChainId, asset, amount);
        OnDepositEnqueued(targetChainId, nonce, asset, l2Recipient, amount);
        return nonce;
    }

    public static void OnNEP17Payment(UInt160 from, BigInteger amount, object data)
    {
        var asset = Runtime.CallingScriptHash;
        var pendingKey = PendingTransferKey(asset, from);
        ExecutionEngine.Assert(Storage.Get(pendingKey) != null, "unsolicited transfer rejected; use deposit()");
    }

    [Safe]
    public static byte[]? GetDeposit(uint chainId, ulong nonce)
    {
        var raw = Storage.Get(DepositKey(chainId, nonce));
        return raw == null ? null : (byte[])raw;
    }

    public static void FinalizeWithdrawal(
        uint chainId,
        UInt256 withdrawalLeafHash,
        UInt160 emittingContract,
        UInt160 l2Sender,
        UInt160 l2Asset,
        ulong withdrawalNonce,
        UInt160 asset,
        UInt160 recipient,
        BigInteger amount)
    {
        ExecutionEngine.Assert(!IsPaused(), "network paused");
        ValidateWithdrawalArgs(chainId, asset, recipient, amount);
        ValidateWithdrawalLeafBinding(
            chainId, withdrawalLeafHash, emittingContract, l2Sender,
            l2Asset, withdrawalNonce, asset, recipient, amount);
        var consumedKey = WithdrawalKey(chainId, withdrawalLeafHash);
        ExecutionEngine.Assert(Storage.Get(consumedKey) == null, "withdrawal already consumed");

        var sm = GetSettlementManager();
        var verified = (bool)Contract.Call(
            sm, "verifyWithdrawalLeaf",
            CallFlags.All,
            new object[] { chainId, withdrawalLeafHash }, 200000);
        ExecutionEngine.Assert(verified, "withdrawal leaf not in finalized batch");

        ConsumeAndPayout(consumedKey, chainId, asset, recipient, amount);
    }

    public static void FinalizeWithdrawalAt(
        uint chainId,
        ulong batchNumber,
        UInt256 withdrawalLeafHash,
        UInt160 emittingContract,
        UInt160 l2Sender,
        UInt160 l2Asset,
        ulong withdrawalNonce,
        UInt160 asset,
        UInt160 recipient,
        BigInteger amount)
    {
        ExecutionEngine.Assert(!IsPaused(), "network paused");
        ValidateWithdrawalArgs(chainId, asset, recipient, amount);
        ValidateWithdrawalLeafBinding(
            chainId, withdrawalLeafHash, emittingContract, l2Sender,
            l2Asset, withdrawalNonce, asset, recipient, amount);
        var consumedKey = WithdrawalKey(chainId, withdrawalLeafHash);
        ExecutionEngine.Assert(Storage.Get(consumedKey) == null, "withdrawal already consumed");

        var sm = GetSettlementManager();
        var verified = (bool)Contract.Call(
            sm, "verifyWithdrawalLeafAt",
            CallFlags.All,
            new object[] { chainId, batchNumber, withdrawalLeafHash }, 200000);
        ExecutionEngine.Assert(verified, "withdrawal leaf not in named finalized batch");

        ConsumeAndPayout(consumedKey, chainId, asset, recipient, amount);
    }

    public static void FinalizeWithdrawalWithProof(
        uint chainId,
        ulong batchNumber,
        UInt256 withdrawalLeafHash,
        byte[][] siblings,
        ulong leafIndex,
        UInt160 emittingContract,
        UInt160 l2Sender,
        UInt160 l2Asset,
        ulong withdrawalNonce,
        UInt160 asset,
        UInt160 recipient,
        BigInteger amount)
    {
        ExecutionEngine.Assert(!IsPaused(), "network paused");
        ValidateWithdrawalArgs(chainId, asset, recipient, amount);
        ValidateWithdrawalLeafBinding(
            chainId, withdrawalLeafHash, emittingContract, l2Sender,
            l2Asset, withdrawalNonce, asset, recipient, amount);
        var consumedKey = WithdrawalKey(chainId, withdrawalLeafHash);
        ExecutionEngine.Assert(Storage.Get(consumedKey) == null, "withdrawal already consumed");

        var sm = GetSettlementManager();
        var verified = (bool)Contract.Call(
            sm, "verifyWithdrawalLeafWithProof",
            CallFlags.All,
            new object[] { chainId, batchNumber, withdrawalLeafHash, siblings, leafIndex }, 500000);
        ExecutionEngine.Assert(verified, "withdrawal leaf not in batch's Merkle root (proof failed)");

        ConsumeAndPayout(consumedKey, chainId, asset, recipient, amount);
    }

    public static void EmergencyFinalizeWithdrawalWithProof(
        uint chainId,
        ulong batchNumber,
        UInt256 withdrawalLeafHash,
        byte[][] siblings,
        ulong leafIndex,
        UInt160 emittingContract,
        UInt160 l2Sender,
        UInt160 l2Asset,
        ulong withdrawalNonce,
        UInt160 asset,
        UInt160 recipient,
        BigInteger amount)
    {
        ExecutionEngine.Assert(IsPaused(), "network must be paused for emergency finalize");
        ValidateWithdrawalArgs(chainId, asset, recipient, amount);
        ValidateWithdrawalLeafBinding(
            chainId, withdrawalLeafHash, emittingContract, l2Sender,
            l2Asset, withdrawalNonce, asset, recipient, amount);
        var consumedKey = WithdrawalKey(chainId, withdrawalLeafHash);
        ExecutionEngine.Assert(Storage.Get(consumedKey) == null, "withdrawal already consumed");

        var sm = GetSettlementManager();
        var verified = (bool)Contract.Call(
            sm, "verifyWithdrawalLeafWithProof",
            CallFlags.All,
            new object[] { chainId, batchNumber, withdrawalLeafHash, siblings, leafIndex }, 500000);
        ExecutionEngine.Assert(verified, "withdrawal leaf not in batch's Merkle root (proof failed)");

        ConsumeAndPayout(consumedKey, chainId, asset, recipient, amount);
    }

    [Safe]
    public static BigInteger GetLockedBalance(uint chainId, UInt160 asset)
    {
        var raw = Storage.Get(LockedBalanceKey(chainId, asset));
        return raw == null ? 0 : (BigInteger)raw;
    }

    public static void MigrateLockedBalance(uint chainId, UInt160 asset, BigInteger amount)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(!IsLockedBalanceMigrationSealed(), "locked-balance migration sealed");
        ExecutionEngine.Assert(chainId > 0, "chainId 0 is reserved for L1");
        ExecutionEngine.Assert(asset.IsValid && !asset.IsZero, "invalid asset");
        ExecutionEngine.Assert(amount >= 0, "migration amount cannot be negative");

        Storage.Put(LockedBalanceKey(chainId, asset), amount);
        OnLockedBalanceMigrated(chainId, asset, amount);
    }

    public static void SealLockedBalanceMigration()
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(!IsLockedBalanceMigrationSealed(), "locked-balance migration already sealed");
        Storage.Put(new byte[] { KeyLockedBalanceMigrationSealed }, new byte[] { 1 });
        OnLockedBalanceMigrationSealed();
    }

    [Safe]
    public static bool IsLockedBalanceMigrationSealed()
    {
        var raw = Storage.Get(new byte[] { KeyLockedBalanceMigrationSealed });
        return raw != null && ((byte[])raw)[0] == 1;
    }

    #endregion

    #region Cross-Chain Messaging (former MessageRouter)

    public static ulong SendMessage(uint targetChainId, UInt160 targetContract, byte[] messagePayload)
    {
        ExecutionEngine.Assert(!IsPaused(), "network paused");
        ExecutionEngine.Assert(targetChainId > 0, "targetChainId 0 is reserved for L1");
        ExecutionEngine.Assert(targetContract.IsValid && !targetContract.IsZero, "invalid target contract");

        var sender = Runtime.Transaction.Sender;
        ExecutionEngine.Assert(sender.IsValid && !sender.IsZero, "invalid sender");
        ExecutionEngine.Assert(Runtime.CheckWitness(sender), "sender witness required");

        var nonce = NextL1ToL2Nonce(targetChainId);
        var key = L1ToL2MsgKey(targetChainId, nonce);
        Storage.Put(key, messagePayload);
        OnL1ToL2Enqueued(targetChainId, nonce, sender, targetContract);
        return nonce;
    }

    [Safe]
    public static ulong GetL1ToL2Nonce(uint targetChainId)
    {
        var raw = Storage.Get(L1ToL2NonceKey(targetChainId));
        return raw == null ? 0 : (ulong)(BigInteger)raw;
    }

    [Safe]
    public static byte[]? GetL1ToL2Message(uint targetChainId, ulong nonce)
    {
        var raw = Storage.Get(L1ToL2MsgKey(targetChainId, nonce));
        return raw == null ? null : (byte[])raw;
    }

    public static void PublishMessageRoots(uint chainId, ulong batchNumber, UInt256 l2ToL1Root, UInt256 l2ToL2Root)
    {
        var sm = GetSettlementManager();
        ExecutionEngine.Assert(Runtime.CallingScriptHash == sm || Runtime.CheckWitness(GetOwner()), "not authorized");
        Storage.Put(L2ToL1RootKey(chainId, batchNumber), (byte[])l2ToL1Root);
        Storage.Put(L2ToL2RootKey(chainId, batchNumber), (byte[])l2ToL2Root);
        OnMessageRootsPublished(chainId, batchNumber, l2ToL1Root, l2ToL2Root);
    }

    [Safe]
    public static UInt256 GetL2ToL1MessageRoot(uint chainId, ulong batchNumber)
    {
        var raw = Storage.Get(L2ToL1RootKey(chainId, batchNumber));
        return raw == null ? UInt256.Zero : (UInt256)raw;
    }

    [Safe]
    public static UInt256 GetL2ToL2MessageRoot(uint chainId, ulong batchNumber)
    {
        var raw = Storage.Get(L2ToL2RootKey(chainId, batchNumber));
        return raw == null ? UInt256.Zero : (UInt256)raw;
    }

    [Safe]
    public static bool IsL2ToL1MessageConsumed(UInt256 messageHash)
    {
        return Storage.Get(ConsumedMsgKey(messageHash)) != null;
    }

    #endregion

    #region Internal Helpers

    private static void ConsumeAndPayout(
        byte[] consumedKey,
        uint chainId,
        UInt160 asset,
        UInt160 recipient,
        BigInteger amount)
    {
        DebitLockedBalance(chainId, asset, amount);
        Storage.Put(consumedKey, new byte[] { 1 });

        var transferred = (bool)Contract.Call(
            asset, "transfer",
            CallFlags.All,
            new object[] { Runtime.ExecutingScriptHash, recipient, amount, null! }, 300000);
        ExecutionEngine.Assert(transferred, "asset payout failed");
        OnWithdrawalFinalized(chainId, asset, recipient, amount);
    }

    private static void CreditLockedBalance(uint chainId, UInt160 asset, BigInteger amount)
    {
        var key = LockedBalanceKey(chainId, asset);
        var current = Storage.Get(key);
        var currentBal = current == null ? 0 : (BigInteger)current;
        Storage.Put(key, currentBal + amount);
    }

    private static void DebitLockedBalance(uint chainId, UInt160 asset, BigInteger amount)
    {
        var key = LockedBalanceKey(chainId, asset);
        var current = Storage.Get(key);
        ExecutionEngine.Assert(current != null, "no locked balance recorded for chain and asset");
        var currentBal = (BigInteger)current!;
        ExecutionEngine.Assert(currentBal >= amount, "withdrawal exceeds chain's escrowed balance");
        Storage.Put(key, currentBal - amount);
    }

    private static void ValidateWithdrawalArgs(uint chainId, UInt160 asset, UInt160 recipient, BigInteger amount)
    {
        ExecutionEngine.Assert(chainId > 0, "chainId 0 is reserved for L1");
        ExecutionEngine.Assert(asset.IsValid && !asset.IsZero, "invalid asset");
        ExecutionEngine.Assert(recipient.IsValid && !recipient.IsZero, "invalid recipient");
        ExecutionEngine.Assert(amount > 0, "amount must be positive");
    }

    private static void ValidateWithdrawalLeafBinding(
        uint chainId,
        UInt256 leafHash,
        UInt160 emittingContract,
        UInt160 l2Sender,
        UInt160 l2Asset,
        ulong withdrawalNonce,
        UInt160 asset,
        UInt160 recipient,
        BigInteger amount)
    {
        ExecutionEngine.Assert(emittingContract.IsValid && !emittingContract.IsZero, "invalid emitting contract");
        ExecutionEngine.Assert(l2Sender.IsValid && !l2Sender.IsZero, "invalid l2Sender");
        ExecutionEngine.Assert(l2Asset.IsValid && !l2Asset.IsZero, "invalid l2Asset");

        var mappedL2Asset = GetL2Asset(asset, chainId);
        ExecutionEngine.Assert(mappedL2Asset.Equals(l2Asset), "l2Asset mismatch with registered mapping");

        var recomputed = ComputeWithdrawalLeafHash(
            chainId, emittingContract, l2Sender, l2Asset, withdrawalNonce, asset, recipient, amount);
        ExecutionEngine.Assert(recomputed.Equals(leafHash), "leaf hash mismatch with supplied fields");
    }

    private static UInt256 ComputeWithdrawalLeafHash(
        uint chainId,
        UInt160 emittingContract,
        UInt160 l2Sender,
        UInt160 l2Asset,
        ulong withdrawalNonce,
        UInt160 l1Asset,
        UInt160 l1Recipient,
        BigInteger amount)
    {
        var amountBytes = ToUnsignedLittleEndian(amount);
        var totalLen = 4 + 20 + 20 + 20 + 8 + 20 + 20 + 4 + amountBytes.Length;
        var buf = new byte[totalLen];
        var pos = 0;

        buf[pos++] = (byte)chainId;
        buf[pos++] = (byte)(chainId >> 8);
        buf[pos++] = (byte)(chainId >> 16);
        buf[pos++] = (byte)(chainId >> 24);

        CopyBytes((byte[])emittingContract, buf, ref pos, 20);
        CopyBytes((byte[])l2Sender, buf, ref pos, 20);
        CopyBytes((byte[])l2Asset, buf, ref pos, 20);

        buf[pos++] = (byte)withdrawalNonce;
        buf[pos++] = (byte)(withdrawalNonce >> 8);
        buf[pos++] = (byte)(withdrawalNonce >> 16);
        buf[pos++] = (byte)(withdrawalNonce >> 24);
        buf[pos++] = (byte)(withdrawalNonce >> 32);
        buf[pos++] = (byte)(withdrawalNonce >> 40);
        buf[pos++] = (byte)(withdrawalNonce >> 48);
        buf[pos++] = (byte)(withdrawalNonce >> 56);

        CopyBytes((byte[])l1Asset, buf, ref pos, 20);
        CopyBytes((byte[])l1Recipient, buf, ref pos, 20);

        var len = amountBytes.Length;
        buf[pos++] = (byte)len;
        buf[pos++] = (byte)(len >> 8);
        buf[pos++] = (byte)(len >> 16);
        buf[pos++] = (byte)(len >> 24);
        CopyBytes(amountBytes, buf, ref pos, len);

        var h1 = CryptoLib.Sha256((ByteString)buf);
        var h2 = CryptoLib.Sha256(h1);
        return (UInt256)h2;
    }

    private static void CopyBytes(byte[] src, byte[] dest, ref int pos, int length)
    {
        for (var i = 0; i < length; i++) dest[pos + i] = src[i];
        pos += length;
    }

    private static byte[] ToUnsignedLittleEndian(BigInteger value)
    {
        var signed = value.ToByteArray();
        if (signed.Length > 1 && signed[signed.Length - 1] == 0)
        {
            var trimmed = new byte[signed.Length - 1];
            for (var i = 0; i < trimmed.Length; i++) trimmed[i] = signed[i];
            return trimmed;
        }
        return signed;
    }

    private static ulong NextDepositNonce(uint chainId)
    {
        var key = DepositNonceKey(chainId);
        var raw = Storage.Get(key);
        var next = raw == null ? 1 : (ulong)(BigInteger)raw + 1;
        Storage.Put(key, (BigInteger)next);
        return next;
    }

    private static ulong NextL1ToL2Nonce(uint chainId)
    {
        var key = L1ToL2NonceKey(chainId);
        var raw = Storage.Get(key);
        var next = raw == null ? 1 : (ulong)(BigInteger)raw + 1;
        Storage.Put(key, (BigInteger)next);
        return next;
    }

    private static byte[] DepositNonceKey(uint chainId) => Append4(PrefixDepositNonce, chainId);
    private static byte[] L1ToL2NonceKey(uint chainId) => Append4(PrefixL1ToL2Nonce, chainId);
    private static byte[] L1ToL2MsgKey(uint chainId, ulong nonce) => Append4And8(PrefixL1ToL2Msg, chainId, nonce);
    private static byte[] L2ToL1RootKey(uint chainId, ulong batchNum) => Append4And8(PrefixL2ToL1Root, chainId, batchNum);
    private static byte[] L2ToL2RootKey(uint chainId, ulong batchNum) => Append4And8(PrefixL2ToL2Root, chainId, batchNum);
    private static byte[] ConsumedMsgKey(UInt256 hash) => AppendBytes(PrefixConsumedMsg, (byte[])hash);

    private static byte[] MappingKey(UInt160 l1Asset, uint chainId)
    {
        var key = new byte[1 + 20 + 4];
        key[0] = PrefixMapping;
        WriteUInt160(key, 1, l1Asset);
        key[21] = (byte)chainId;
        key[22] = (byte)(chainId >> 8);
        key[23] = (byte)(chainId >> 16);
        key[24] = (byte)(chainId >> 24);
        return key;
    }

    private static byte[] DepositKey(uint chainId, ulong nonce) => Append4And8(PrefixDeposit, chainId, nonce);

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

    private static byte[] PendingTransferKey(UInt160 asset, UInt160 from)
    {
        var key = new byte[1 + 20 + 20];
        key[0] = PrefixPendingTransfer;
        WriteUInt160(key, 1, asset);
        WriteUInt160(key, 21, from);
        return key;
    }

    private static byte[] LockedBalanceKey(uint chainId, UInt160 asset)
    {
        var key = new byte[1 + 4 + 20];
        key[0] = PrefixLockedBalance;
        key[1] = (byte)chainId;
        key[2] = (byte)(chainId >> 8);
        key[3] = (byte)(chainId >> 16);
        key[4] = (byte)(chainId >> 24);
        WriteUInt160(key, 5, asset);
        return key;
    }

    private static byte[] EncodeDeposit(UInt160 asset, BigInteger amount, UInt160 recipient, UInt160 sender, ulong nonce)
    {
        var amountBytes = ToUnsignedLittleEndian(amount);
        var totalLen = 20 + 20 + 20 + 8 + 4 + amountBytes.Length;
        var buf = new byte[totalLen];
        var pos = 0;

        CopyBytes((byte[])asset, buf, ref pos, 20);
        CopyBytes((byte[])recipient, buf, ref pos, 20);
        CopyBytes((byte[])sender, buf, ref pos, 20);

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
        CopyBytes(amountBytes, buf, ref pos, len);

        return buf;
    }

    private static byte[] Append4(byte pfx, uint id)
    {
        return new byte[] { pfx, (byte)id, (byte)(id >> 8), (byte)(id >> 16), (byte)(id >> 24) };
    }

    private static byte[] Append4And8(byte pfx, uint id, ulong num)
    {
        var res = new byte[13];
        res[0] = pfx;
        res[1] = (byte)id;
        res[2] = (byte)(id >> 8);
        res[3] = (byte)(id >> 16);
        res[4] = (byte)(id >> 24);
        for (var i = 0; i < 8; i++)
            res[5 + i] = (byte)((num >> (8 * i)) & 0xFF);
        return res;
    }

    private static byte[] AppendBytes(byte pfx, byte[] payload)
    {
        var res = new byte[1 + payload.Length];
        res[0] = pfx;
        for (var i = 0; i < payload.Length; i++) res[1 + i] = payload[i];
        return res;
    }

    private static void WriteUInt160(byte[] dest, int offset, UInt160 val)
    {
        var b = (byte[])val;
        for (var i = 0; i < 20; i++) dest[offset + i] = b[i];
    }

    private static UInt160 ReadUInt160(byte[] data, int offset)
    {
        var b = new byte[20];
        for (var i = 0; i < 20; i++) b[i] = data[offset + i];
        return (UInt160)b;
    }

    private static uint ReadUInt32(byte[] data, int offset) =>
        (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));

    #endregion
}
