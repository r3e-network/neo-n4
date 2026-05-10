using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace L2Native.ExternalBridgeContract;

/// <summary>
/// L2-side counterpart to <c>NeoHub.ExternalBridgeEscrow</c>: lets an L2 dApp
/// initiate cross-foreign-chain transfers from inside the L2's NeoVM
/// environment, and accepts inbound deliveries injected by the sequencer.
/// See <c>doc.md</c> §11.3.
/// </summary>
/// <remarks>
/// Mirrors <c>L2Native.L2BridgeContract</c>'s shape — burn-on-send, mint-on-receive
/// — but indexed by <c>externalChainId</c> (foreign-namespace 0xE0_xx_xx_xx)
/// rather than the Neo L2's own settlement nonce. Outbound messages are
/// emitted to a separate "external withdrawal root" the batcher commits
/// alongside the existing batch roots, so the L1 escrow can prove inclusion.
///
/// <para>Inbound (foreign → L2) is sequencer-injected: the L1 escrow's
/// <c>CrossChainInboundFinalized</c> event is observed by the L2 sequencer,
/// which calls <see cref="ApplyInbound"/> as a system-account-witnessed tx
/// — same pattern as L1MessageInbox. The witness check guarantees user code
/// can't fabricate inbound deliveries.</para>
/// </remarks>
[DisplayName("L2Native.ExternalBridgeContract")]
[ContractAuthor("Neo Project", "dev@neo.org")]
[ContractDescription("L2-side bridge to foreign chains (Eth/Tron/Sol).")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/L2Native.ExternalBridgeContract")]
[ContractPermission(Permission.Any, Method.Any)]
public class L2NativeExternalBridgeContract : SmartContract
{
    private const byte PrefixOutboundNonce = 0x01;
    private const byte PrefixConsumedInboundNonce = 0x02;
    private const byte PrefixAssetMapping = 0x03;     // 0x03 + externalChainId(4B) + foreignAsset(20B) → l2Asset(20B)
    private const byte KeySystemAccount = 0xFE;
    private const byte KeyOwner = 0xFF;

    /// <summary>Emitted when an L2 dApp initiates an outbound transfer. The
    /// L2 batcher's withdrawal-root oracle picks these events up and commits
    /// them into the per-batch external-withdrawal root.</summary>
    [DisplayName("ExternalSendInitiated")]
    public static event Action<uint, ulong, UInt160, UInt160, UInt160, BigInteger, byte[]> OnExternalSendInitiated = default!;

    /// <summary>Emitted when an inbound message from a foreign chain has been
    /// delivered + applied on this L2.</summary>
    [DisplayName("ExternalInboundApplied")]
    public static event Action<uint, ulong, UInt160, UInt160, BigInteger> OnExternalInboundApplied = default!;

    /// <summary>Wire owner + system account on deploy.</summary>
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

    /// <summary>Owner — controls asset mappings.</summary>
    [Safe]
    public static UInt160 GetOwner()
    {
        var raw = Storage.Get(new byte[] { KeyOwner });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>System account — only the sequencer (running under this
    /// account) may inject inbound messages.</summary>
    [Safe]
    public static UInt160 GetSystemAccount()
    {
        var raw = Storage.Get(new byte[] { KeySystemAccount });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Map a foreign asset to its L2 representation. Owner only.
    /// Without a mapping, inbound deliveries of that asset will revert.</summary>
    public static void RegisterAssetMapping(uint externalChainId, UInt160 foreignAsset, UInt160 l2Asset)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(
            (externalChainId & 0xFF000000U) == 0xE0000000U,
            "externalChainId must use the 0xE0_xx_xx_xx foreign-namespace prefix");
        ExecutionEngine.Assert(foreignAsset.IsValid, "invalid foreignAsset");
        ExecutionEngine.Assert(l2Asset.IsValid && !l2Asset.IsZero, "invalid l2Asset");
        Storage.Put(AssetMappingKey(externalChainId, foreignAsset), l2Asset);
    }

    /// <summary>Read the L2 representation of a foreign asset, or
    /// <see cref="UInt160.Zero"/> if not mapped.</summary>
    [Safe]
    public static UInt160 GetAssetMapping(uint externalChainId, UInt160 foreignAsset)
    {
        var raw = Storage.Get(AssetMappingKey(externalChainId, foreignAsset));
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>
    /// Initiate an outbound transfer to a foreign chain from inside the L2.
    /// Burns <paramref name="amount"/> of <paramref name="l2Asset"/> from the
    /// caller (the L2 contract reflects the L1 escrow's locked balance).
    /// Returns the assigned outbound nonce.
    /// </summary>
    public static ulong Send(
        uint externalChainId,
        UInt160 recipient,
        UInt160 l2Asset,
        BigInteger amount,
        byte[] calldata,
        ulong deadlineUnixSeconds)
    {
        ExecutionEngine.Assert(
            (externalChainId & 0xFF000000U) == 0xE0000000U,
            "externalChainId must use the 0xE0_xx_xx_xx foreign-namespace prefix");
        ExecutionEngine.Assert(recipient.IsValid && !recipient.IsZero, "invalid recipient");
        ExecutionEngine.Assert(l2Asset.IsValid, "invalid l2Asset");
        ExecutionEngine.Assert(amount > 0, "amount must be positive");

        var sender = (UInt160)Runtime.CallingScriptHash;

        // Burn the L2 asset by transferring it to the zero address.
        // (Assets that don't support direct burn can use a sink contract; the
        // operator wires asset adapters per-asset, same as the existing
        // L2BridgeContract.)
        var ok = (bool)Contract.Call(l2Asset, "transfer", CallFlags.All,
            new object[] { sender, UInt160.Zero, amount, null! });
        ExecutionEngine.Assert(ok, "L2 asset transfer failed (burn)");

        // Allocate the next outbound nonce.
        var nonceKey = OutboundNonceKey(externalChainId);
        var nonceRaw = Storage.Get(nonceKey);
        ulong next;
        if (nonceRaw == null) next = 1;
        else
        {
            var b = (byte[])nonceRaw;
            next = ((ulong)b[0])
                | ((ulong)b[1] << 8) | ((ulong)b[2] << 16) | ((ulong)b[3] << 24)
                | ((ulong)b[4] << 32) | ((ulong)b[5] << 40) | ((ulong)b[6] << 48)
                | ((ulong)b[7] << 56);
            next += 1;
        }
        var nextBytes = new byte[8];
        nextBytes[0] = (byte)next; nextBytes[1] = (byte)(next >> 8);
        nextBytes[2] = (byte)(next >> 16); nextBytes[3] = (byte)(next >> 24);
        nextBytes[4] = (byte)(next >> 32); nextBytes[5] = (byte)(next >> 40);
        nextBytes[6] = (byte)(next >> 48); nextBytes[7] = (byte)(next >> 56);
        Storage.Put(nonceKey, nextBytes);

        OnExternalSendInitiated(externalChainId, next, sender, recipient, l2Asset, amount, calldata);
        return next;
    }

    /// <summary>
    /// Apply an inbound message that the sequencer has already verified came
    /// from the L1 escrow's <c>CrossChainInboundFinalized</c> event. Witness
    /// must be the wired system account.
    /// </summary>
    public static void ApplyInbound(
        uint externalChainId,
        ulong nonce,
        UInt160 foreignSender,
        UInt160 l2Recipient,
        UInt160 l2Asset,
        BigInteger amount)
    {
        var system = GetSystemAccount();
        ExecutionEngine.Assert(Runtime.CheckWitness(system),
            "ApplyInbound requires system-account witness");

        var consumedKey = ConsumedInboundKey(externalChainId, nonce);
        ExecutionEngine.Assert(Storage.Get(consumedKey) == null,
            "inbound nonce already consumed (replay)");
        ExecutionEngine.Assert(amount > 0, "amount must be positive");
        ExecutionEngine.Assert(l2Recipient.IsValid && !l2Recipient.IsZero, "invalid recipient");
        ExecutionEngine.Assert(l2Asset.IsValid && !l2Asset.IsZero, "invalid l2Asset");

        // Mint the L2 asset to the recipient. Same caveat as Send: per-asset
        // adapter handles real mint semantics; this is the canonical hook.
        var ok = (bool)Contract.Call(l2Asset, "transfer", CallFlags.All,
            new object[] { Runtime.ExecutingScriptHash, l2Recipient, amount, null! });
        ExecutionEngine.Assert(ok, "L2 asset transfer failed (mint to recipient)");

        Storage.Put(consumedKey, new byte[] { 1 });
        OnExternalInboundApplied(externalChainId, nonce, foreignSender, l2Recipient, amount);
    }

    /// <summary>Read the last outbound nonce assigned for a chain.</summary>
    [Safe]
    public static ulong GetLastOutboundNonce(uint externalChainId)
    {
        var raw = Storage.Get(OutboundNonceKey(externalChainId));
        if (raw == null) return 0;
        var b = (byte[])raw;
        return ((ulong)b[0])
            | ((ulong)b[1] << 8) | ((ulong)b[2] << 16) | ((ulong)b[3] << 24)
            | ((ulong)b[4] << 32) | ((ulong)b[5] << 40) | ((ulong)b[6] << 48)
            | ((ulong)b[7] << 56);
    }

    /// <summary>Has this inbound nonce been applied already?</summary>
    [Safe]
    public static bool IsInboundConsumed(uint externalChainId, ulong nonce)
    {
        return Storage.Get(ConsumedInboundKey(externalChainId, nonce)) != null;
    }

    private static byte[] OutboundNonceKey(uint externalChainId)
    {
        var k = new byte[1 + 4];
        k[0] = PrefixOutboundNonce;
        k[1] = (byte)externalChainId; k[2] = (byte)(externalChainId >> 8);
        k[3] = (byte)(externalChainId >> 16); k[4] = (byte)(externalChainId >> 24);
        return k;
    }

    private static byte[] ConsumedInboundKey(uint externalChainId, ulong nonce)
    {
        var k = new byte[1 + 4 + 8];
        k[0] = PrefixConsumedInboundNonce;
        k[1] = (byte)externalChainId; k[2] = (byte)(externalChainId >> 8);
        k[3] = (byte)(externalChainId >> 16); k[4] = (byte)(externalChainId >> 24);
        k[5] = (byte)nonce; k[6] = (byte)(nonce >> 8);
        k[7] = (byte)(nonce >> 16); k[8] = (byte)(nonce >> 24);
        k[9] = (byte)(nonce >> 32); k[10] = (byte)(nonce >> 40);
        k[11] = (byte)(nonce >> 48); k[12] = (byte)(nonce >> 56);
        return k;
    }

    private static byte[] AssetMappingKey(uint externalChainId, UInt160 foreignAsset)
    {
        var k = new byte[1 + 4 + 20];
        k[0] = PrefixAssetMapping;
        k[1] = (byte)externalChainId; k[2] = (byte)(externalChainId >> 8);
        k[3] = (byte)(externalChainId >> 16); k[4] = (byte)(externalChainId >> 24);
        var fa = (byte[])foreignAsset;
        for (var i = 0; i < 20; i++) k[5 + i] = fa[i];
        return k;
    }
}
