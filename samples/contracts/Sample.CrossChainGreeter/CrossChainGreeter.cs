using System;
using System.ComponentModel;
using Neo;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace Sample.CrossChainGreeter;

/// <summary>
/// Minimal example dApp that emits an L2 → L1 cross-chain message containing a
/// caller-supplied greeting. Demonstrates how an app contract integrates with
/// Neo Core native <c>L2MessageContract</c>: hold a reference to the L2 message contract's
/// hash, call <c>EmitMessage(targetChainId, receiver, type, payload)</c>, get back
/// the assigned outbound nonce. The next sealed batch's L2→L1 message Merkle tree
/// commits to the message; on L1, <c>NeoHub.MessageRouter</c> delivers it to the
/// declared receiver after the batch finalizes.
/// </summary>
/// <remarks>
/// Wire-up at deploy time: pass <c>(owner, l2MessageContract)</c> as deploy data;
/// the owner can later <c>SetL2MessageContract</c> to point at a new hash if the
/// system upgrades. <c>messageType</c> = 4 in this example (the application-defined
/// "greeting" type) — receivers on L1 dispatch by this byte.
/// </remarks>
[DisplayName("Sample.CrossChainGreeter")]
[ContractAuthor("R3E Network — Sample", "dev@r3e.network")]
[ContractDescription("Sample dApp: emits an L2→L1 cross-chain greeting message.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/samples/contracts/Sample.CrossChainGreeter")]
[ContractPermission(Permission.Any, Method.Any)]
public class CrossChainGreeter : SmartContract
{
    private const byte KeyL2MessageContract = 0x01;
    private const byte KeyOwner = 0xFF;

    /// <summary>Application-defined message type — L1 receivers dispatch on this byte.</summary>
    public const byte GreetingMessageType = 4;

    /// <summary>Emitted when this contract sends a greeting.</summary>
    [DisplayName("GreetingSent")]
    public static event Action<UInt160, uint, byte[], ulong> OnGreetingSent = default!;

    /// <summary>One-shot deploy wiring. Data: <c>[owner, l2MessageContract]</c>.</summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var arr = (object[])data;
        var owner = (UInt160)arr[0];
        var l2Msg = (UInt160)arr[1];
        ExecutionEngine.Assert(owner.IsValid && !owner.IsZero, "invalid owner");
        ExecutionEngine.Assert(l2Msg.IsValid && !l2Msg.IsZero, "invalid l2MessageContract");
        Storage.Put(new byte[] { KeyOwner }, owner);
        Storage.Put(new byte[] { KeyL2MessageContract }, l2Msg);
    }

    /// <summary>Read the wired L2MessageContract hash.</summary>
    [Safe]
    public static UInt160 GetL2MessageContract()
    {
        var raw = Storage.Get(new byte[] { KeyL2MessageContract });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Owner-gated update path (e.g. system upgrade).</summary>
    public static void SetL2MessageContract(UInt160 newHash)
    {
        var owner = (UInt160)(Storage.Get(new byte[] { KeyOwner }) ?? throw new Exception("owner unset"));
        ExecutionEngine.Assert(Runtime.CheckWitness(owner), "not authorized");
        ExecutionEngine.Assert(newHash.IsValid && !newHash.IsZero, "invalid hash");
        Storage.Put(new byte[] { KeyL2MessageContract }, newHash);
    }

    /// <summary>
    /// Send a greeting message to <paramref name="receiver"/> on
    /// <paramref name="targetChainId"/>. <c>targetChainId == 0</c> = L1 (Neo N3 mainnet
    /// or whichever Neo chain hosts NeoHub); any other value targets a sibling L2.
    /// </summary>
    /// <returns>Outbound nonce assigned by L2MessageContract.</returns>
    public static ulong SendGreeting(uint targetChainId, UInt160 receiver, byte[] greeting)
    {
        ExecutionEngine.Assert(receiver.IsValid && !receiver.IsZero, "invalid receiver");
        ExecutionEngine.Assert(greeting.Length > 0, "empty greeting");
        ExecutionEngine.Assert(greeting.Length <= 256, "greeting too long (max 256)");

        var l2Msg = (UInt160)(Storage.Get(new byte[] { KeyL2MessageContract }) ?? throw new Exception("l2Msg unset"));
        // Forward to L2MessageContract.EmitMessage; the Sender on the emitted message
        // will be Runtime.CallingScriptHash on the L2Msg side, which in turn is THIS
        // contract's hash — receivers can authenticate the greeting came from us.
        var nonce = (ulong)Contract.Call(l2Msg, "emitMessage", CallFlags.All,
            new object[] { targetChainId, receiver, GreetingMessageType, greeting });

        OnGreetingSent(receiver, targetChainId, greeting, nonce);
        return nonce;
    }
}
