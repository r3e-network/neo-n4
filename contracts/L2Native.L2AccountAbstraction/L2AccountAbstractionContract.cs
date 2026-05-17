using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Services;

namespace L2Native.L2AccountAbstraction;

/// <summary>
/// L2 account-abstraction coordinator. Accounts can bind a validator contract
/// and an optional paymaster, then execute nonce-checked calls through this entry point.
/// </summary>
[DisplayName("L2Native.L2AccountAbstraction")]
[ContractAuthor("Neo Project", "dev@neo.org")]
[ContractDescription("Programmable account abstraction entry point for Neo Elastic Network L2.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/L2Native.L2AccountAbstraction")]
[ContractPermission(Permission.Any, Method.Any)]
public class L2AccountAbstractionContract : SmartContract
{
    private const byte PrefixValidator = 0x01;
    private const byte PrefixPaymaster = 0x02;
    private const byte PrefixNonce = 0x03;
    private const byte KeySystemAccount = 0xFE;
    private const byte KeyOwner = 0xFF;

    /// <summary>Magic returned by ValidateTransaction on success: ASCII "N4AA".</summary>
    public const uint ValidationMagic = 0x4E344141;

    /// <summary>Emitted when account settings change.</summary>
    [DisplayName("AccountConfigured")]
    public static event Action<UInt160, UInt160, UInt160> OnAccountConfigured = default!;

    /// <summary>Emitted after a nonce is consumed by execution or system settlement.</summary>
    [DisplayName("NonceConsumed")]
    public static event Action<UInt160, ulong> OnNonceConsumed = default!;

    /// <summary>Emitted after a successful execute path.</summary>
    [DisplayName("TxExecuted")]
    public static event Action<UInt160, ulong, UInt160, string> OnTxExecuted = default!;

    /// <summary>Deploy data: [owner, systemAccount].</summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var arr = (object[])data;
        var owner = (UInt160)arr[0];
        var systemAccount = (UInt160)arr[1];
        ExecutionEngine.Assert(owner.IsValid && !owner.IsZero, "invalid owner");
        ExecutionEngine.Assert(systemAccount.IsValid && !systemAccount.IsZero, "invalid system account");
        Storage.Put(new byte[] { KeyOwner }, owner);
        Storage.Put(new byte[] { KeySystemAccount }, systemAccount);
    }

    /// <summary>Governance owner.</summary>
    [Safe]
    public static UInt160 GetOwner()
    {
        var raw = Storage.Get(new byte[] { KeyOwner });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Sequencer/system account allowed to consume validated nonces.</summary>
    [Safe]
    public static UInt160 GetSystemAccount()
    {
        var raw = Storage.Get(new byte[] { KeySystemAccount });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Configure account validator and optional paymaster.</summary>
    public static void ConfigureAccount(UInt160 account, UInt160 validator, UInt160 paymaster)
    {
        ExecutionEngine.Assert(account.IsValid && !account.IsZero, "invalid account");
        ExecutionEngine.Assert(validator.IsZero || validator.IsValid, "invalid validator");
        ExecutionEngine.Assert(paymaster.IsZero || paymaster.IsValid, "invalid paymaster");
        var authorized = Runtime.CheckWitness(account) || Runtime.CheckWitness(GetOwner());
        ExecutionEngine.Assert(authorized, "not authorized");

        if (validator.IsZero)
            Storage.Delete(AccountKey(PrefixValidator, account));
        else
            Storage.Put(AccountKey(PrefixValidator, account), validator);

        if (paymaster.IsZero)
            Storage.Delete(AccountKey(PrefixPaymaster, account));
        else
            Storage.Put(AccountKey(PrefixPaymaster, account), paymaster);

        OnAccountConfigured(account, validator, paymaster);
    }

    /// <summary>Read the validator contract for an account, or zero for native Neo witness mode.</summary>
    [Safe]
    public static UInt160 GetValidator(UInt160 account)
    {
        var raw = Storage.Get(AccountKey(PrefixValidator, account));
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Read the paymaster contract for an account, or zero if none.</summary>
    [Safe]
    public static UInt160 GetPaymaster(UInt160 account)
    {
        var raw = Storage.Get(AccountKey(PrefixPaymaster, account));
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Last consumed nonce for the account.</summary>
    [Safe]
    public static ulong GetNonce(UInt160 account)
    {
        var raw = Storage.Get(AccountKey(PrefixNonce, account));
        return raw == null ? 0UL : (ulong)(BigInteger)raw;
    }

    /// <summary>Boolean validation path for executors that do not need a magic value.</summary>
    [Safe]
    public static bool ValidateTx(UInt160 account, ulong nonce, UInt256 txHash, byte[] signature)
    {
        if (!account.IsValid || account.IsZero) return false;
        if (nonce != GetNonce(account) + 1UL) return false;

        var validator = GetValidator(account);
        if (validator.IsZero) return Runtime.CheckWitness(account);

        return (bool)Contract.Call(
            validator,
            "validateTx",
            CallFlags.ReadOnly,
            new object[] { account, nonce, txHash, signature });
    }

    /// <summary>ZKsync-style validation entry: returns "N4AA" magic on success, 0 otherwise.</summary>
    [Safe]
    public static uint ValidateTransaction(UInt160 account, ulong nonce, UInt256 txHash, byte[] signature)
    {
        return ValidateTx(account, nonce, txHash, signature) ? ValidationMagic : 0U;
    }

    /// <summary>Consume a nonce after external validation. Callable by the configured system account.</summary>
    public static void ConsumeNonce(UInt160 account, ulong nonce)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetSystemAccount()), "not system");
        ConsumeNonceInternal(account, nonce);
    }

    /// <summary>
    /// Validate, charge optional paymaster fees, consume nonce, and call the target.
    /// The validator signs over <paramref name="txHash"/>; callers are responsible for
    /// hashing target/method/args into that canonical digest off-chain.
    /// </summary>
    public static object ExecuteTx(
        UInt160 account,
        ulong nonce,
        UInt256 txHash,
        UInt160 target,
        string method,
        object[] args,
        UInt160 feeAsset,
        BigInteger feeAmount,
        byte[] signature)
    {
        ExecutionEngine.Assert(ValidateTx(account, nonce, txHash, signature), "AA validation failed");
        ExecutionEngine.Assert(target.IsValid && !target.IsZero, "invalid target");
        ExecutionEngine.Assert(method.Length > 0, "method required");

        ConsumeNonceInternal(account, nonce);
        ChargePaymaster(account, feeAsset, feeAmount);

        var result = Contract.Call(target, method, CallFlags.All, args);
        OnTxExecuted(account, nonce, target, method);
        return result;
    }

    private static void ChargePaymaster(UInt160 account, UInt160 feeAsset, BigInteger feeAmount)
    {
        if (feeAmount == 0) return;
        ExecutionEngine.Assert(feeAmount > 0, "fee amount must be positive");
        ExecutionEngine.Assert(feeAsset.IsValid && !feeAsset.IsZero, "invalid fee asset");
        var paymaster = GetPaymaster(account);
        ExecutionEngine.Assert(paymaster.IsValid && !paymaster.IsZero, "paymaster not configured");
        Contract.Call(paymaster, "charge", CallFlags.All, new object[] { account, feeAsset, feeAmount });
    }

    private static void ConsumeNonceInternal(UInt160 account, ulong nonce)
    {
        ExecutionEngine.Assert(account.IsValid && !account.IsZero, "invalid account");
        ExecutionEngine.Assert(nonce == GetNonce(account) + 1UL, "nonce out of sequence");
        Storage.Put(AccountKey(PrefixNonce, account), (BigInteger)nonce);
        OnNonceConsumed(account, nonce);
    }

    private static byte[] AccountKey(byte prefix, UInt160 account)
    {
        var k = new byte[21];
        k[0] = prefix;
        var b = (byte[])account;
        for (var i = 0; i < 20; i++) k[1 + i] = b[i];
        return k;
    }
}
