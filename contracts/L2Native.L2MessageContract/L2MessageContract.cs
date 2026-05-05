using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace L2Native.L2MessageContract;

/// <summary>
/// L2-side outbox: any contract on L2 can call <see cref="EmitMessage"/> to publish an
/// L2 → L1 / L2 → L2 message that the next batch will commit to. The system also calls
/// <see cref="ApplyInbound"/> to deliver L1 → L2 messages received by the sequencer.
/// See doc.md §13.1 (L2MessageContract) and §10 (Neo Connect).
/// </summary>
[DisplayName("L2Native.L2MessageContract")]
[ContractAuthor("Neo Project", "dev@neo.org")]
[ContractDescription("L2-side cross-chain message I/O for Neo Elastic Network.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/neo-project/neo4/tree/master/contracts/L2Native.L2MessageContract")]
[ContractPermission(Permission.Any, Method.Any)]
public class L2MessageContract : SmartContract
{
    private const byte PrefixOutboundNonce = 0x01;     // 0x01 + sender(20B) → next nonce
    private const byte PrefixInboundConsumed = 0x02;   // 0x02 + (sourceChain(4B) + nonce(8B)) → 1
    private const byte KeyChainId = 0x03;
    private const byte KeySystemAccount = 0xFE;
    private const byte KeyOwner = 0xFF;

    /// <summary>Emitted on every outbound message.</summary>
    [DisplayName("MessageEmitted")]
    public static event Action<uint, uint, ulong, UInt160, UInt160, byte> OnMessageEmitted = default!;

    /// <summary>Emitted when an inbound L1 → L2 message is applied.</summary>
    [DisplayName("InboundApplied")]
    public static event Action<uint, ulong, UInt160> OnInboundApplied = default!;

    /// <summary>Set wiring on deploy.</summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var arr = (object[])data;
        var owner = (UInt160)arr[0];
        var systemAccount = (UInt160)arr[1];
        var chainId = (uint)(BigInteger)arr[2];
        // Surface typo'd zero hashes / chainId here. chainId == 0 is the L1 sentinel
        // (see L2Outbox.L1ChainId) — an L2 with chainId=0 would misroute every L2→L2
        // message as L2→L1.
        ExecutionEngine.Assert(owner.IsValid && !owner.IsZero, "invalid owner");
        ExecutionEngine.Assert(systemAccount.IsValid && !systemAccount.IsZero, "invalid system account");
        ExecutionEngine.Assert(chainId > 0, "chainId 0 is reserved for L1");
        Storage.Put(new byte[] { KeyOwner }, owner);
        Storage.Put(new byte[] { KeySystemAccount }, systemAccount);
        Storage.Put(new byte[] { KeyChainId }, (BigInteger)chainId);
    }

    /// <summary>The local L2 chain identifier.</summary>
    [Safe]
    public static uint GetChainId()
    {
        var raw = Storage.Get(new byte[] { KeyChainId });
        return raw == null ? 0u : (uint)(BigInteger)raw;
    }

    /// <summary>
    /// Emit an outbound message from the calling contract. <paramref name="targetChainId"/> = 0
    /// means L2 → L1; any other value means L2 → L2.
    /// </summary>
    public static ulong EmitMessage(uint targetChainId, UInt160 receiver, byte messageType, byte[] payload)
    {
        ExecutionEngine.Assert(receiver.IsValid && !receiver.IsZero, "invalid receiver");
        var sender = Runtime.CallingScriptHash;
        var nonce = NextNonce(sender);
        OnMessageEmitted(GetChainId(), targetChainId, nonce, sender, receiver, messageType);
        return nonce;
    }

    /// <summary>
    /// Apply a verified inbound L1 → L2 message. Only callable by the system account
    /// (sequencer pre-batch hook). Replay-protected on (sourceChainId, nonce).
    /// </summary>
    public static void ApplyInbound(uint sourceChainId, ulong nonce, UInt160 receiver, byte messageType, byte[] payload)
    {
        var system = (UInt160)(Storage.Get(new byte[] { KeySystemAccount }) ?? throw new Exception("system unset"));
        ExecutionEngine.Assert(Runtime.CheckWitness(system), "not system");
        var dedupe = ConsumedKey(sourceChainId, nonce);
        ExecutionEngine.Assert(Storage.Get(dedupe) == null, "inbound replayed");
        Storage.Put(dedupe, new byte[] { 1 });

        // Forward to the receiver's `onCrossChainMessage(sourceChainId, sender, type, payload)`.
        // The framework call will revert if the method is missing or fails.
        if (messageType != 0)
        {
            Contract.Call(receiver, "onCrossChainMessage", CallFlags.All,
                new object[] { sourceChainId, nonce, messageType, payload });
        }

        OnInboundApplied(sourceChainId, nonce, receiver);
    }

    /// <summary>True if (sourceChainId, nonce) has been consumed.</summary>
    [Safe]
    public static bool HasConsumed(uint sourceChainId, ulong nonce)
    {
        return Storage.Get(ConsumedKey(sourceChainId, nonce)) != null;
    }

    private static ulong NextNonce(UInt160 sender)
    {
        var k = new byte[1 + 20];
        k[0] = PrefixOutboundNonce;
        var b = (byte[])sender;
        for (var i = 0; i < 20; i++) k[1 + i] = b[i];
        var raw = Storage.Get(k);
        var current = raw == null ? 0UL : (ulong)(BigInteger)raw;
        var next = current + 1;
        Storage.Put(k, (BigInteger)next);
        return next;
    }

    private static byte[] ConsumedKey(uint sourceChainId, ulong nonce)
    {
        var k = new byte[1 + 4 + 8];
        k[0] = PrefixInboundConsumed;
        k[1] = (byte)sourceChainId; k[2] = (byte)(sourceChainId >> 8); k[3] = (byte)(sourceChainId >> 16); k[4] = (byte)(sourceChainId >> 24);
        k[5] = (byte)nonce; k[6] = (byte)(nonce >> 8); k[7] = (byte)(nonce >> 16); k[8] = (byte)(nonce >> 24);
        k[9] = (byte)(nonce >> 32); k[10] = (byte)(nonce >> 40); k[11] = (byte)(nonce >> 48); k[12] = (byte)(nonce >> 56);
        return k;
    }
}
