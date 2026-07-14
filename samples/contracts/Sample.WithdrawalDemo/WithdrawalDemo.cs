using System;
using System.ComponentModel;
using System.Numerics;
using Neo;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace Sample.WithdrawalDemo;

/// <summary>
/// Minimal example dApp that initiates an L2 → L1 withdrawal on behalf of the caller
/// via Neo Core native <c>L2BridgeContract.InitiateWithdrawal</c>. Demonstrates the
/// user-initiated burn path: caller holds a balance of <c>l2Asset</c> on this L2,
/// the contract calls <c>L2BridgeContract</c> which burns the amount and emits a
/// withdrawal record into the next batch's withdrawal Merkle tree. Once the batch
/// finalizes on L1 the user calls <c>NeoHub.SharedBridge.FinalizeWithdrawalWithProof</c>
/// to claim on L1.
/// </summary>
/// <remarks>
/// Wire-up: pass <c>(owner, l2BridgeContract)</c> at deploy. The owner can update
/// the L2 bridge hash if it ever changes (system upgrade). The actual burn happens
/// inside <c>L2BridgeContract</c> — this contract is a thin pass-through showing
/// the standard NEP-17 withdraw pattern an app can offer its users.
/// </remarks>
[DisplayName("Sample.WithdrawalDemo")]
[ContractAuthor("R3E Network — Sample", "dev@r3e.network")]
[ContractDescription("Sample dApp: initiate an L2→L1 withdrawal via L2BridgeContract.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/samples/contracts/Sample.WithdrawalDemo")]
[ContractPermission(Permission.Any, Method.Any)]
public class WithdrawalDemo : SmartContract
{
    private const byte KeyL2BridgeContract = 0x01;
    private const byte KeyOwner = 0xFF;

    /// <summary>Emitted when this contract initiates a withdrawal on behalf of a user.</summary>
    [DisplayName("WithdrawalInitiated")]
    public static event Action<UInt160, UInt160, UInt160, BigInteger, ulong> OnWithdrawalInitiated = default!;

    /// <summary>One-shot deploy wiring. Data: <c>[owner, l2BridgeContract]</c>.</summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var arr = (object[])data;
        var owner = (UInt160)arr[0];
        var l2Bridge = (UInt160)arr[1];
        ExecutionEngine.Assert(owner.IsValid && !owner.IsZero, "invalid owner");
        ExecutionEngine.Assert(l2Bridge.IsValid && !l2Bridge.IsZero, "invalid l2BridgeContract");
        Storage.Put(new byte[] { KeyOwner }, owner);
        Storage.Put(new byte[] { KeyL2BridgeContract }, l2Bridge);
    }

    /// <summary>Read the wired L2BridgeContract hash.</summary>
    [Safe]
    public static UInt160 GetL2BridgeContract()
    {
        var raw = Storage.Get(new byte[] { KeyL2BridgeContract });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Owner-gated update path.</summary>
    public static void SetL2BridgeContract(UInt160 newHash)
    {
        var owner = (UInt160)(Storage.Get(new byte[] { KeyOwner }) ?? throw new Exception("owner unset"));
        ExecutionEngine.Assert(Runtime.CheckWitness(owner), "not authorized");
        ExecutionEngine.Assert(newHash.IsValid && !newHash.IsZero, "invalid hash");
        Storage.Put(new byte[] { KeyL2BridgeContract }, newHash);
    }

    /// <summary>
    /// Initiate an L2 → L1 withdrawal of <paramref name="amount"/> of
    /// <paramref name="l2Asset"/> from the caller, payable to <paramref name="l1Recipient"/>
    /// on L1. Returns the per-(chain, sender) nonce assigned by the bridge contract.
    /// </summary>
    /// <remarks>
    /// The caller must already have an <paramref name="amount"/> balance on
    /// <paramref name="l2Asset"/> (the bridge calls the asset's NEP-17 <c>burn</c>
    /// method, which checks balance + decrements it). The withdrawal record lands
    /// in the next sealed batch's withdrawal Merkle tree.
    /// </remarks>
    public static ulong WithdrawTo(UInt160 l2Asset, BigInteger amount, UInt160 l1Recipient)
    {
        ExecutionEngine.Assert(amount > 0, "amount must be positive");
        ExecutionEngine.Assert(l1Recipient.IsValid && !l1Recipient.IsZero, "invalid l1Recipient");
        ExecutionEngine.Assert(l2Asset.IsValid && !l2Asset.IsZero, "invalid l2Asset");

        var l2Bridge = (UInt160)(Storage.Get(new byte[] { KeyL2BridgeContract }) ?? throw new Exception("l2Bridge unset"));
        // L2BridgeContract.InitiateWithdrawal burns from Runtime.CallingScriptHash —
        // which on the bridge's side is THIS contract. So users initiate by calling
        // this contract, the bridge sees the call coming from us, and the burn
        // resolves against this contract's balance. For a true pass-through (burn
        // from the user's own balance), the bridge would need to be called directly
        // by the user. This sample demonstrates the contract-mediated pattern; an
        // app that wants user-direct withdrawal omits this contract entirely and
        // points users at L2BridgeContract.
        var nonce = (ulong)Contract.Call(l2Bridge, "initiateWithdrawal", CallFlags.All,
            new object[] { l2Asset, amount, l1Recipient });

        OnWithdrawalInitiated(Runtime.CallingScriptHash, l1Recipient, l2Asset, amount, nonce);
        return nonce;
    }
}
