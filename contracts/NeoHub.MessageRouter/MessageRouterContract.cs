using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace NeoHub.MessageRouter;

/// <summary>
/// L1-side router for L1↔L2 and L2↔L2 cross-chain messages. Maintains the L1→L2 outbound
/// queue per target chain and the L2→L1 / L2→L2 message-root registry per finalized batch.
/// See doc.md §3.2 (MessageRouter) and §10 (Neo Connect).
/// </summary>
[DisplayName("NeoHub.MessageRouter")]
[ContractAuthor("Neo Project", "dev@neo.org")]
[ContractDescription("Cross-chain message queue + message-root registry for Neo Elastic Network.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/neo-project/neo4/tree/master/contracts/NeoHub.MessageRouter")]
[ContractPermission(Permission.Any, Method.Any)]
public class MessageRouterContract : SmartContract
{
    private const byte PrefixL1ToL2Nonce = 0x01;       // 0x01 + targetChainId(4B) → next nonce
    private const byte PrefixL1ToL2Msg = 0x02;         // 0x02 + targetChainId(4B) + nonce(8B) → encoded msg
    private const byte PrefixL2ToL1Root = 0x03;        // 0x03 + chainId(4B) + batchNum(8B) → root
    private const byte PrefixL2ToL2Root = 0x04;        // 0x04 + chainId(4B) + batchNum(8B) → root
    private const byte PrefixGlobalRoot = 0x05;        // 0x05 + batchEpoch(8B) → global agg root
    private const byte PrefixConsumed = 0x06;          // 0x06 + msgHash(32B) → 1
    private const byte PrefixSettlementManager = 0xFD;
    private const byte KeyOwner = 0xFF;

    /// <summary>Emitted when a new L1→L2 message is enqueued.</summary>
    [DisplayName("L1ToL2Enqueued")]
    public static event Action<uint, ulong, UInt160, UInt160> OnL1ToL2Enqueued = default!;

    /// <summary>Emitted when an L2→L1 message is consumed on L1.</summary>
    [DisplayName("L2ToL1Consumed")]
    public static event Action<uint, UInt256> OnL2ToL1Consumed = default!;

    /// <summary>Set wiring on deploy.</summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var arr = (object[])data;
        var owner = (UInt160)arr[0];
        var settlementManager = (UInt160)arr[1];
        // Without these guards a typo'd zero settlementManager would deploy successfully
        // but every Route call would later fail mysteriously when verifying message
        // proofs against a non-existent contract.
        ExecutionEngine.Assert(owner.IsValid && !owner.IsZero, "invalid owner");
        ExecutionEngine.Assert(settlementManager.IsValid && !settlementManager.IsZero, "invalid settlement manager");
        Storage.Put(new byte[] { KeyOwner }, owner);
        Storage.Put(new byte[] { PrefixSettlementManager }, settlementManager);
    }

    /// <summary>Governance owner.</summary>
    [Safe]
    public static UInt160 GetOwner()
    {
        var raw = Storage.Get(new byte[] { KeyOwner });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>
    /// Enqueue an L1 → L2 message. Anyone may call. The L2 watches its inbox and consumes the
    /// message in its next batch; replay protection lives on the L2 side via the (sourceChain,
    /// nonce) bitmap.
    /// </summary>
    public static ulong EnqueueL1ToL2(uint targetChainId, UInt160 receiver, byte messageType, byte[] payload)
    {
        ExecutionEngine.Assert(receiver.IsValid && !receiver.IsZero, "invalid receiver");
        // chainId 0 is the L1 sentinel — without this guard anyone could enqueue
        // L1→L2 messages bound for chainId 0 that no L2 would ever consume,
        // bloating L1 storage with no-op entries.
        ExecutionEngine.Assert(targetChainId > 0, "targetChainId 0 is reserved for L1");

        var nonceKey = NonceKey(targetChainId);
        var raw = Storage.Get(nonceKey);
        var nonce = raw == null ? 1UL : (ulong)(BigInteger)raw + 1UL;
        Storage.Put(nonceKey, (BigInteger)nonce);

        var sender = Runtime.CallingScriptHash;
        var encoded = EncodeMessage(0u, targetChainId, nonce, sender, receiver, messageType, payload);
        Storage.Put(MessageKey(targetChainId, nonce), encoded);

        OnL1ToL2Enqueued(targetChainId, nonce, sender, receiver);
        return nonce;
    }

    /// <summary>Read a previously enqueued L1→L2 message by (chainId, nonce).</summary>
    [Safe]
    public static byte[] GetL1ToL2(uint chainId, ulong nonce)
    {
        var raw = Storage.Get(MessageKey(chainId, nonce));
        return raw == null ? new byte[0] : (byte[])raw;
    }

    /// <summary>
    /// Settlement manager calls this when finalizing a batch to publish the L2→L1 and L2→L2
    /// message roots so they can be used for inclusion-proof verification.
    /// </summary>
    public static void PublishMessageRoots(uint chainId, ulong batchNumber, UInt256 l2ToL1Root, UInt256 l2ToL2Root)
    {
        var sm = (UInt160)(Storage.Get(new byte[] { PrefixSettlementManager }) ?? throw new Exception("sm unset"));
        ExecutionEngine.Assert(Runtime.CheckWitness(sm), "not settlement manager");
        Storage.Put(BuildKey(PrefixL2ToL1Root, chainId, batchNumber), (byte[])l2ToL1Root);
        Storage.Put(BuildKey(PrefixL2ToL2Root, chainId, batchNumber), (byte[])l2ToL2Root);
    }

    /// <summary>Get the L2→L1 message root committed for a finalized batch.</summary>
    [Safe]
    public static UInt256 GetL2ToL1Root(uint chainId, ulong batchNumber)
    {
        var raw = Storage.Get(BuildKey(PrefixL2ToL1Root, chainId, batchNumber));
        return raw == null ? UInt256.Zero : (UInt256)raw;
    }

    /// <summary>Get the L2→L2 message root committed for a finalized batch.</summary>
    [Safe]
    public static UInt256 GetL2ToL2Root(uint chainId, ulong batchNumber)
    {
        var raw = Storage.Get(BuildKey(PrefixL2ToL2Root, chainId, batchNumber));
        return raw == null ? UInt256.Zero : (UInt256)raw;
    }

    /// <summary>
    /// Mark a message hash as consumed. The Merkle proof check happens off-chain (or via a
    /// separate proof helper) and is the caller's responsibility. Replay-protected.
    /// </summary>
    public static void MarkConsumed(uint sourceChainId, UInt256 messageHash)
    {
        var sm = (UInt160)(Storage.Get(new byte[] { PrefixSettlementManager }) ?? throw new Exception("sm unset"));
        ExecutionEngine.Assert(Runtime.CheckWitness(sm), "not settlement manager");
        var key = ConsumedKey(messageHash);
        ExecutionEngine.Assert(Storage.Get(key) == null, "already consumed");
        Storage.Put(key, new byte[] { 1 });
        OnL2ToL1Consumed(sourceChainId, messageHash);
    }

    /// <summary>True if a message hash has been consumed.</summary>
    [Safe]
    public static bool IsConsumed(UInt256 messageHash)
    {
        return Storage.Get(ConsumedKey(messageHash)) != null;
    }

    private static byte[] NonceKey(uint chainId)
    {
        var k = new byte[5];
        k[0] = PrefixL1ToL2Nonce;
        k[1] = (byte)chainId; k[2] = (byte)(chainId >> 8); k[3] = (byte)(chainId >> 16); k[4] = (byte)(chainId >> 24);
        return k;
    }

    private static byte[] MessageKey(uint chainId, ulong nonce) =>
        BuildKey(PrefixL1ToL2Msg, chainId, nonce);

    private static byte[] ConsumedKey(UInt256 hash)
    {
        var k = new byte[1 + 32];
        k[0] = PrefixConsumed;
        var b = (byte[])hash;
        for (var i = 0; i < 32; i++) k[1 + i] = b[i];
        return k;
    }

    private static byte[] BuildKey(byte prefix, uint chainId, ulong number)
    {
        var k = new byte[13];
        k[0] = prefix;
        k[1] = (byte)chainId; k[2] = (byte)(chainId >> 8); k[3] = (byte)(chainId >> 16); k[4] = (byte)(chainId >> 24);
        k[5] = (byte)number; k[6] = (byte)(number >> 8); k[7] = (byte)(number >> 16); k[8] = (byte)(number >> 24);
        k[9] = (byte)(number >> 32); k[10] = (byte)(number >> 40); k[11] = (byte)(number >> 48); k[12] = (byte)(number >> 56);
        return k;
    }

    private static byte[] EncodeMessage(uint sourceChainId, uint targetChainId, ulong nonce, UInt160 sender, UInt160 receiver, byte messageType, byte[] payload)
    {
        // 4 + 4 + 8 + 20 + 20 + 1 + 4 + payload.Length
        var size = 4 + 4 + 8 + 20 + 20 + 1 + 4 + payload.Length;
        var buf = new byte[size];
        var pos = 0;
        buf[pos++] = (byte)sourceChainId; buf[pos++] = (byte)(sourceChainId >> 8);
        buf[pos++] = (byte)(sourceChainId >> 16); buf[pos++] = (byte)(sourceChainId >> 24);
        buf[pos++] = (byte)targetChainId; buf[pos++] = (byte)(targetChainId >> 8);
        buf[pos++] = (byte)(targetChainId >> 16); buf[pos++] = (byte)(targetChainId >> 24);
        buf[pos++] = (byte)nonce; buf[pos++] = (byte)(nonce >> 8); buf[pos++] = (byte)(nonce >> 16); buf[pos++] = (byte)(nonce >> 24);
        buf[pos++] = (byte)(nonce >> 32); buf[pos++] = (byte)(nonce >> 40); buf[pos++] = (byte)(nonce >> 48); buf[pos++] = (byte)(nonce >> 56);
        var senderBytes = (byte[])sender;
        for (var i = 0; i < 20; i++) buf[pos + i] = senderBytes[i];
        pos += 20;
        var receiverBytes = (byte[])receiver;
        for (var i = 0; i < 20; i++) buf[pos + i] = receiverBytes[i];
        pos += 20;
        buf[pos++] = messageType;
        var len = payload.Length;
        buf[pos++] = (byte)len; buf[pos++] = (byte)(len >> 8); buf[pos++] = (byte)(len >> 16); buf[pos++] = (byte)(len >> 24);
        for (var i = 0; i < len; i++) buf[pos + i] = payload[i];
        return buf;
    }
}
