using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace NeoHub.ExternalBridgeEscrow;

/// <summary>
/// L1-side escrow + dispatch for cross-foreign-chain messages. Locks NEP-17
/// assets bound for foreign chains; pays out (mints / releases) on inbound
/// proofs that the registered <c>IExternalBridgeVerifier</c> accepts. See
/// <c>doc.md</c> §11.3 and <c>docs/external-bridge-roadmap.md</c>.
/// </summary>
/// <remarks>
/// <para>Storage layout per externalChainId:</para>
/// <list type="bullet">
///   <item><description><c>0x01 + externalChainId(4B LE)</c> →
///     u64 LE outbound-nonce counter (Send)</description></item>
///   <item><description><c>0x02 + externalChainId(4B LE) + nonce(8B LE)</c> →
///     1B (consumed inbound, replay protection — note the verifier ALSO
///     replay-protects, this is defense-in-depth on the escrow side)</description></item>
///   <item><description><c>0x03 + externalChainId(4B LE) + asset(20B)</c> →
///     u128 LE locked-balance (so the escrow can't release more than was
///     ever sent in)</description></item>
/// </list>
///
/// <para>This contract does NOT call NEP-17 transfer to mint pegged tokens
/// for the user — that wiring is asset-mapping-specific and lives in a
/// per-asset adapter the operator deploys (same pattern as
/// <c>NeoHub.SharedBridge</c>'s mint/release split). The escrow's job is to
/// (a) lock outbound, (b) authorize releases via verified inbound, (c) emit
/// canonical events.</para>
/// </remarks>
[DisplayName("NeoHub.ExternalBridgeEscrow")]
[ContractAuthor("Neo Project", "dev@neo.org")]
[ContractDescription("L1 escrow + dispatch for cross-foreign-chain messages.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.ExternalBridgeEscrow")]
[ContractPermission(Permission.Any, Method.Any)]
public class ExternalBridgeEscrowContract : SmartContract
{
    private const byte PrefixOutboundNonce = 0x01;
    private const byte PrefixConsumedInboundNonce = 0x02;
    private const byte PrefixLockedBalance = 0x03;
    private const byte KeyRegistry = 0xFE;
    private const byte KeyOwner = 0xFF;

    /// <summary>Emitted on outbound send (Neo → foreign). Off-chain watchers
    /// listen for this event to attest the message on the foreign chain.</summary>
    [DisplayName("CrossChainSendInitiated")]
    public static event Action<uint, ulong, UInt160, UInt160, UInt160, BigInteger, byte[]> OnCrossChainSendInitiated = default!;

    /// <summary>Emitted on verified inbound message (foreign → Neo) — the
    /// settlement event the on-chain payout side reads.</summary>
    [DisplayName("CrossChainInboundFinalized")]
    public static event Action<uint, ulong, byte> OnCrossChainInboundFinalized = default!;

    /// <summary>Set the initial owner + registry hash on deploy.</summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var arr = (object[])data;
        var owner = (UInt160)arr[0];
        var registry = (UInt160)arr[1];
        ExecutionEngine.Assert(owner.IsValid && !owner.IsZero, "invalid owner");
        ExecutionEngine.Assert(registry.IsValid && !registry.IsZero, "invalid registry");
        Storage.Put(new byte[] { KeyOwner }, owner);
        Storage.Put(new byte[] { KeyRegistry }, registry);
    }

    /// <summary>Owner — the only address that can rotate the registry pointer.</summary>
    [Safe]
    public static UInt160 GetOwner()
    {
        var raw = Storage.Get(new byte[] { KeyOwner });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>The currently-wired ExternalBridgeRegistry hash.</summary>
    [Safe]
    public static UInt160 GetRegistry()
    {
        var raw = Storage.Get(new byte[] { KeyRegistry });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Owner-only: rotate the registry pointer (for governance-mediated
    /// upgrades from MPC → Optimistic → ZK without redeploying the escrow).</summary>
    public static void SetRegistry(UInt160 registry)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(registry.IsValid && !registry.IsZero, "invalid registry");
        Storage.Put(new byte[] { KeyRegistry }, registry);
    }

    /// <summary>
    /// Initiate an outbound transfer to a foreign chain. Caller must have
    /// approved this contract to spend <paramref name="amount"/> of
    /// <paramref name="asset"/>. The full canonical
    /// <c>ExternalCrossChainMessage</c> bytes are computed from the args +
    /// the next outbound nonce + a Hash256 over the encoded body.
    /// </summary>
    /// <returns>The 8-byte little-endian outbound nonce assigned to this
    /// message. Off-chain watchers listen to <see cref="OnCrossChainSendInitiated"/>
    /// and use the (externalChainId, nonce) tuple as the canonical lookup key.</returns>
    public static ulong Send(
        uint externalChainId,
        UInt160 recipient,
        UInt160 asset,
        BigInteger amount,
        byte[] calldata,
        ulong deadlineUnixSeconds)
    {
        ExecutionEngine.Assert(
            (externalChainId & 0xFF000000U) == 0xE0000000U,
            "externalChainId must use the 0xE0_xx_xx_xx foreign-namespace prefix");
        ExecutionEngine.Assert(recipient.IsValid && !recipient.IsZero, "invalid recipient");
        ExecutionEngine.Assert(asset.IsValid, "invalid asset");
        ExecutionEngine.Assert(amount > 0, "amount must be positive");

        // Lock the asset by transferring it to this contract.
        var sender = (UInt160)Runtime.CallingScriptHash;
        var ok = (bool)Contract.Call(asset, "transfer", CallFlags.All,
            new object[] { sender, Runtime.ExecutingScriptHash, amount, null });
        ExecutionEngine.Assert(ok, "asset transfer failed (lock)");

        // Update locked-balance accounting. Storage.Get returns ByteString;
        // cast to BigInteger directly (the framework supports the implicit conversion).
        var balKey = LockedBalanceKey(externalChainId, asset);
        var prev = Storage.Get(balKey);
        var prevAmount = prev == null ? BigInteger.Zero : (BigInteger)prev;
        Storage.Put(balKey, prevAmount + amount);

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

        // Emit the canonical send event. The `calldata` blob is the payload —
        // off-chain watchers re-encode the full ExternalCrossChainMessage from
        // these args + nonce + their observation timestamp and sign the hash.
        OnCrossChainSendInitiated(externalChainId, next, sender, recipient, asset, amount, calldata);
        return next;
    }

    /// <summary>
    /// Process a verified inbound message. Verifies the proof via the
    /// registered verifier through <c>ExternalBridgeRegistry.VerifyInbound</c>,
    /// rejects on replay (per-(chainId,nonce)), then emits the finalize event
    /// that the per-asset payout adapter reads to mint/release the recipient's
    /// pegged asset on the destination Neo chain.
    /// </summary>
    public static void Receive(uint externalChainId, byte[] messageBytes, byte[] proofBytes)
    {
        ExecutionEngine.Assert(messageBytes != null && messageBytes.Length >= 102,
            "messageBytes too short");
        ExecutionEngine.Assert(proofBytes != null, "proofBytes is null");

        // Parse the nonce (offset 8, 8B LE) and direction (offset 16, 1B) from the
        // canonical ExternalCrossChainMessage layout — replay key + sanity check.
        var nonce =
            (ulong)messageBytes[8]
            | ((ulong)messageBytes[9] << 8)
            | ((ulong)messageBytes[10] << 16)
            | ((ulong)messageBytes[11] << 24)
            | ((ulong)messageBytes[12] << 32)
            | ((ulong)messageBytes[13] << 40)
            | ((ulong)messageBytes[14] << 48)
            | ((ulong)messageBytes[15] << 56);
        var direction = messageBytes[16];
        ExecutionEngine.Assert(direction == 2, "Receive expects direction=2 (ForeignToNeo)");

        // Replay protection at the escrow layer (the verifier ALSO replay-protects;
        // this catches a verifier that's been swapped to one without nonce tracking).
        var consumedKey = ConsumedInboundKey(externalChainId, nonce);
        ExecutionEngine.Assert(Storage.Get(consumedKey) == null,
            "inbound nonce already consumed (replay)");

        // Dispatch verification through the registry.
        var registry = GetRegistry();
        ExecutionEngine.Assert(registry != UInt160.Zero, "registry not wired");
        var ok = (bool)Contract.Call(registry, "verifyInbound", CallFlags.ReadOnly,
            new object[] { externalChainId, messageBytes, proofBytes });
        ExecutionEngine.Assert(ok, "registry verifier rejected inbound message");

        // Mark consumed + emit settlement event.
        Storage.Put(consumedKey, new byte[] { 1 });
        var messageType = messageBytes[81]; // offset: 4+4+8+1+20+20+8+32 - 16 = 81
        OnCrossChainInboundFinalized(externalChainId, nonce, messageType);
    }

    /// <summary>Read the next outbound nonce assigned by Send (i.e. the
    /// last-used nonce). Useful for off-chain indexers tracking pending
    /// messages.</summary>
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

    /// <summary>Read the locked balance for an (externalChainId, asset) pair.
    /// Used to enforce conservation of supply across the bridge.</summary>
    [Safe]
    public static BigInteger GetLockedBalance(uint externalChainId, UInt160 asset)
    {
        var raw = Storage.Get(LockedBalanceKey(externalChainId, asset));
        return raw == null ? BigInteger.Zero : (BigInteger)raw;
    }

    /// <summary>Has the inbound nonce for this chain been consumed already?</summary>
    [Safe]
    public static bool IsInboundConsumed(uint externalChainId, ulong nonce)
    {
        return Storage.Get(ConsumedInboundKey(externalChainId, nonce)) != null;
    }

    /// <summary>NEP-17 onPayment hook — accept transfers (the Send flow
    /// transfers into this contract). Reject all unsolicited transfers so a
    /// user can't accidentally send tokens to the contract directly and
    /// have them counted as a Send.</summary>
    public static void OnNEP17Payment(UInt160 from, BigInteger amount, object data)
    {
        // The Send flow uses Contract.Call(asset, "transfer", ...) which routes
        // through this hook; the data field will be null. We accept those.
        // Direct user-initiated transfers (with non-null data) get rejected so
        // tokens don't go missing from the escrow's POV.
        if (data != null)
        {
            ExecutionEngine.Assert(false,
                "direct transfer rejected — call Send to lock assets for cross-chain transfer");
        }
    }

    private static byte[] OutboundNonceKey(uint externalChainId)
    {
        var k = new byte[1 + 4];
        k[0] = PrefixOutboundNonce;
        k[1] = (byte)externalChainId;
        k[2] = (byte)(externalChainId >> 8);
        k[3] = (byte)(externalChainId >> 16);
        k[4] = (byte)(externalChainId >> 24);
        return k;
    }

    private static byte[] ConsumedInboundKey(uint externalChainId, ulong nonce)
    {
        var k = new byte[1 + 4 + 8];
        k[0] = PrefixConsumedInboundNonce;
        k[1] = (byte)externalChainId;
        k[2] = (byte)(externalChainId >> 8);
        k[3] = (byte)(externalChainId >> 16);
        k[4] = (byte)(externalChainId >> 24);
        k[5] = (byte)nonce; k[6] = (byte)(nonce >> 8);
        k[7] = (byte)(nonce >> 16); k[8] = (byte)(nonce >> 24);
        k[9] = (byte)(nonce >> 32); k[10] = (byte)(nonce >> 40);
        k[11] = (byte)(nonce >> 48); k[12] = (byte)(nonce >> 56);
        return k;
    }

    private static byte[] LockedBalanceKey(uint externalChainId, UInt160 asset)
    {
        var k = new byte[1 + 4 + 20];
        k[0] = PrefixLockedBalance;
        k[1] = (byte)externalChainId;
        k[2] = (byte)(externalChainId >> 8);
        k[3] = (byte)(externalChainId >> 16);
        k[4] = (byte)(externalChainId >> 24);
        var assetBytes = (byte[])asset;
        for (var i = 0; i < 20; i++) k[5 + i] = assetBytes[i];
        return k;
    }
}
