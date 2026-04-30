using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace L2Native.L2BridgeContract;

/// <summary>
/// L2-side bridge contract: mints bridged assets on deposit and emits withdrawal records on
/// user-initiated burn. See doc.md §13.1 (L2BridgeContract) and §15.2/§15.3.
/// </summary>
/// <remarks>
/// Replay-protects on (sourceChainId, nonce). Uses the canonical deposit-payload format defined
/// in <c>Neo.L2.Bridge.DepositPayload</c> off-chain, so L1 ↔ L2 stay byte-identical.
/// </remarks>
[DisplayName("L2Native.L2BridgeContract")]
[ContractAuthor("Neo Project", "dev@neo.org")]
[ContractDescription("L2-side bridge: mints / burns assets that NeoHub.SharedBridge holds in escrow.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/neo-project/neo4/tree/master/contracts/L2Native.L2BridgeContract")]
[ContractPermission(Permission.Any, Method.Any)]
public class L2BridgeContract : SmartContract
{
    private const byte PrefixMapping = 0x01;          // 0x01 + l1Asset(20B) → l2Asset(20B)
    private const byte PrefixDepositConsumed = 0x02;  // 0x02 + sourceChainId(4B) + nonce(8B) → 1
    private const byte PrefixWithdrawalNonce = 0x03;  // 0x03 + sender(20B) → next nonce
    private const byte KeySystemAccount = 0xFE;
    private const byte KeyOwner = 0xFF;

    /// <summary>Emitted when a deposit from L1 mints L2 assets.</summary>
    [DisplayName("Mint")]
    public static event Action<UInt160, UInt160, BigInteger, uint, ulong> OnMint = default!;

    /// <summary>Emitted when an L2 user burns to withdraw to L1.</summary>
    [DisplayName("WithdrawalEmitted")]
    public static event Action<UInt160, UInt160, UInt160, BigInteger, ulong> OnWithdrawalEmitted = default!;

    /// <summary>Set wiring on deploy.</summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var arr = (object[])data;
        var owner = (UInt160)arr[0];
        var systemAccount = (UInt160)arr[1];
        Storage.Put(new byte[] { KeyOwner }, owner);
        Storage.Put(new byte[] { KeySystemAccount }, systemAccount);
    }

    /// <summary>Map an L1 asset hash to its L2 representation. Owner only.</summary>
    public static void RegisterMapping(UInt160 l1Asset, UInt160 l2Asset)
    {
        var owner = (UInt160)(Storage.Get(new byte[] { KeyOwner }) ?? throw new Exception("owner unset"));
        ExecutionEngine.Assert(Runtime.CheckWitness(owner), "not authorized");
        ExecutionEngine.Assert(l1Asset.IsValid && !l1Asset.IsZero, "invalid l1 asset");
        ExecutionEngine.Assert(l2Asset.IsValid && !l2Asset.IsZero, "invalid l2 asset");
        Storage.Put(MappingKey(l1Asset), l2Asset);
    }

    /// <summary>Get the L2 asset hash mapped to an L1 asset.</summary>
    [Safe]
    public static UInt160 GetL2Asset(UInt160 l1Asset)
    {
        var raw = Storage.Get(MappingKey(l1Asset));
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>
    /// Apply an inbound deposit from L1. Only callable by the system account (sequencer hook).
    /// Replay-protected on (sourceChainId, nonce).
    /// </summary>
    public static void ApplyDeposit(uint sourceChainId, ulong nonce, UInt160 l1Asset, UInt160 recipient, BigInteger amount)
    {
        var system = (UInt160)(Storage.Get(new byte[] { KeySystemAccount }) ?? throw new Exception("system unset"));
        ExecutionEngine.Assert(Runtime.CheckWitness(system), "not system");
        ExecutionEngine.Assert(amount > 0, "amount must be positive");
        ExecutionEngine.Assert(recipient.IsValid && !recipient.IsZero, "invalid recipient");

        var dedupeKey = DepositConsumedKey(sourceChainId, nonce);
        ExecutionEngine.Assert(Storage.Get(dedupeKey) == null, "deposit replayed");
        Storage.Put(dedupeKey, new byte[] { 1 });

        var l2Asset = (UInt160)(Storage.Get(MappingKey(l1Asset)) ?? throw new Exception("no mapping"));

        // Mint by calling the L2 asset contract's `mint`.
        Contract.Call(l2Asset, "mint", CallFlags.All, new object[] { recipient, amount });

        OnMint(l1Asset, recipient, amount, sourceChainId, nonce);
    }

    /// <summary>
    /// User-initiated withdrawal: burn <paramref name="amount"/> of <paramref name="l2Asset"/>
    /// from the caller and emit a withdrawal record that the next batch's withdrawalRoot
    /// will commit to.
    /// </summary>
    public static ulong InitiateWithdrawal(UInt160 l2Asset, BigInteger amount, UInt160 l1Recipient)
    {
        ExecutionEngine.Assert(amount > 0, "amount must be positive");
        ExecutionEngine.Assert(l1Recipient.IsValid && !l1Recipient.IsZero, "invalid l1 recipient");
        ExecutionEngine.Assert(l2Asset.IsValid && !l2Asset.IsZero, "invalid l2 asset");

        var caller = Runtime.CallingScriptHash;
        var nonce = NextWithdrawalNonce(caller);

        // Burn from caller's balance.
        Contract.Call(l2Asset, "burn", CallFlags.All, new object[] { caller, amount });

        OnWithdrawalEmitted(caller, l1Recipient, l2Asset, amount, nonce);
        return nonce;
    }

    private static byte[] MappingKey(UInt160 l1Asset)
    {
        var k = new byte[1 + 20];
        k[0] = PrefixMapping;
        var bytes = (byte[])l1Asset;
        for (var i = 0; i < 20; i++) k[1 + i] = bytes[i];
        return k;
    }

    private static byte[] DepositConsumedKey(uint sourceChainId, ulong nonce)
    {
        var k = new byte[1 + 4 + 8];
        k[0] = PrefixDepositConsumed;
        k[1] = (byte)sourceChainId; k[2] = (byte)(sourceChainId >> 8); k[3] = (byte)(sourceChainId >> 16); k[4] = (byte)(sourceChainId >> 24);
        k[5] = (byte)nonce; k[6] = (byte)(nonce >> 8); k[7] = (byte)(nonce >> 16); k[8] = (byte)(nonce >> 24);
        k[9] = (byte)(nonce >> 32); k[10] = (byte)(nonce >> 40); k[11] = (byte)(nonce >> 48); k[12] = (byte)(nonce >> 56);
        return k;
    }

    private static ulong NextWithdrawalNonce(UInt160 sender)
    {
        var k = new byte[1 + 20];
        k[0] = PrefixWithdrawalNonce;
        var bytes = (byte[])sender;
        for (var i = 0; i < 20; i++) k[1 + i] = bytes[i];
        var raw = Storage.Get(k);
        var current = raw == null ? 0UL : (ulong)(BigInteger)raw;
        var next = current + 1;
        Storage.Put(k, (BigInteger)next);
        return next;
    }
}
