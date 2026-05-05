using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace L2Native.L2PaymasterContract;

/// <summary>
/// Paymaster: lets users pay L2 fees in stablecoin (or any approved asset) instead of bridged
/// GAS. Maintains per-user balance accounts that L2 fee charging consults; tops up when the
/// user (or a sponsor) deposits the alternate fee asset. See doc.md §9.3 (Fee Abstraction)
/// and §13.1 (L2PaymasterContract).
/// </summary>
[DisplayName("L2Native.L2PaymasterContract")]
[ContractAuthor("Neo Project", "dev@neo.org")]
[ContractDescription("Lets L2 users pay fees in stablecoin / sponsored assets instead of bridged GAS.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/neo-project/neo4/tree/master/contracts/L2Native.L2PaymasterContract")]
[ContractPermission(Permission.Any, Method.Any)]
public class L2PaymasterContract : SmartContract
{
    private const byte PrefixApprovedAsset = 0x01;     // 0x01 + asset(20B) → 1
    private const byte PrefixBalance = 0x02;            // 0x02 + user(20B) + asset(20B) → BigInteger
    private const byte KeyFeeContract = 0xFD;
    private const byte KeyOwner = 0xFF;

    /// <summary>Emitted when a user's account is topped up.</summary>
    [DisplayName("TopUp")]
    public static event Action<UInt160, UInt160, BigInteger> OnTopUp = default!;

    /// <summary>Emitted when fees are charged from an account.</summary>
    [DisplayName("FeeCharged")]
    public static event Action<UInt160, UInt160, BigInteger> OnFeeCharged = default!;

    /// <summary>Set wiring on deploy.</summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var arr = (object[])data;
        var owner = (UInt160)arr[0];
        var feeContract = (UInt160)arr[1];
        // Surface typo'd zero hashes here. A zero feeContract would silently fail to
        // route paymaster-paid fees on first use.
        ExecutionEngine.Assert(owner.IsValid && !owner.IsZero, "invalid owner");
        ExecutionEngine.Assert(feeContract.IsValid && !feeContract.IsZero, "invalid fee contract");
        Storage.Put(new byte[] { KeyOwner }, owner);
        Storage.Put(new byte[] { KeyFeeContract }, feeContract);
    }

    /// <summary>Approve a new fee-payment asset. Owner only.</summary>
    public static void ApproveAsset(UInt160 asset)
    {
        var owner = (UInt160)(Storage.Get(new byte[] { KeyOwner }) ?? throw new Exception("owner unset"));
        ExecutionEngine.Assert(Runtime.CheckWitness(owner), "not authorized");
        Storage.Put(ApprovalKey(asset), new byte[] { 1 });
    }

    /// <summary>True if <paramref name="asset"/> is an approved fee-payment asset.</summary>
    [Safe]
    public static bool IsApproved(UInt160 asset)
    {
        return Storage.Get(ApprovalKey(asset)) != null;
    }

    /// <summary>
    /// Top up a user's balance with <paramref name="amount"/> of an approved
    /// <paramref name="asset"/>. Caller pulls the asset into escrow via NEP-17 transfer.
    /// </summary>
    public static void TopUp(UInt160 user, UInt160 asset, BigInteger amount)
    {
        ExecutionEngine.Assert(amount > 0, "amount must be positive");
        ExecutionEngine.Assert(IsApproved(asset), "asset not approved");

        var caller = Runtime.CallingScriptHash;
        var transferred = (bool)Contract.Call(asset, "transfer", CallFlags.All,
            new object[] { caller, Runtime.ExecutingScriptHash, amount, null! });
        ExecutionEngine.Assert(transferred, "asset transfer failed");

        var key = BalanceKey(user, asset);
        var raw = Storage.Get(key);
        var current = raw == null ? BigInteger.Zero : (BigInteger)raw;
        Storage.Put(key, current + amount);
        OnTopUp(user, asset, amount);
    }

    /// <summary>Read a user's paymaster balance for a given asset.</summary>
    [Safe]
    public static BigInteger GetBalance(UInt160 user, UInt160 asset)
    {
        var raw = Storage.Get(BalanceKey(user, asset));
        return raw == null ? BigInteger.Zero : (BigInteger)raw;
    }

    /// <summary>
    /// Charge <paramref name="amount"/> from <paramref name="user"/>'s balance. Only the
    /// configured L2 fee contract may invoke this.
    /// </summary>
    public static void Charge(UInt160 user, UInt160 asset, BigInteger amount)
    {
        var feeContract = (UInt160)(Storage.Get(new byte[] { KeyFeeContract }) ?? throw new Exception("fee contract unset"));
        ExecutionEngine.Assert(Runtime.CheckWitness(feeContract), "not fee contract");
        ExecutionEngine.Assert(amount > 0, "amount must be positive");

        var key = BalanceKey(user, asset);
        var raw = Storage.Get(key);
        var current = raw == null ? BigInteger.Zero : (BigInteger)raw;
        ExecutionEngine.Assert(current >= amount, "insufficient balance");
        Storage.Put(key, current - amount);
        OnFeeCharged(user, asset, amount);
    }

    private static byte[] ApprovalKey(UInt160 asset)
    {
        var k = new byte[1 + 20];
        k[0] = PrefixApprovedAsset;
        var b = (byte[])asset;
        for (var i = 0; i < 20; i++) k[1 + i] = b[i];
        return k;
    }

    private static byte[] BalanceKey(UInt160 user, UInt160 asset)
    {
        var k = new byte[1 + 20 + 20];
        k[0] = PrefixBalance;
        var u = (byte[])user;
        for (var i = 0; i < 20; i++) k[1 + i] = u[i];
        var a = (byte[])asset;
        for (var i = 0; i < 20; i++) k[21 + i] = a[i];
        return k;
    }
}
