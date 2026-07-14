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
/// assets bound for foreign chains (outbound); on inbound proofs that the
/// registered <c>IExternalBridgeVerifier</c> accepts it atomically consumes the
/// (externalChainId,neoChainId,nonce) and dispatches an asset payout through a
/// governance-configured route. See <c>doc.md</c> §11.3.
/// </summary>
/// <remarks>
/// <para>Storage layout per source/target chain domain:</para>
/// <list type="bullet">
///   <item><description><c>0x01 + externalChainId(4B LE) + neoChainId(4B LE)</c> →
///     u64 LE outbound-nonce counter (Send).</description></item>
///   <item><description><c>0x02 + externalChainId(4B LE) + neoChainId(4B LE) + nonce(8B LE)</c> →
///     1B (consumed inbound, replay protection).</description></item>
///   <item><description><c>0x03 + externalChainId(4B LE) + asset(20B)</c> →
///     unsigned integer locked-balance. Incremented by <see cref="Send"/> or
///     <see cref="FundLiquidity"/> and decremented by direct inbound payouts.</description></item>
///   <item><description><c>0x05 + externalChainId(4B LE) + foreignAsset(20B)</c> →
///     <c>neoAsset(20B) + payoutAdapter(20B) + active(1B)</c>.
///     A zero adapter selects direct NEP-17 escrow release only for a contract
///     bound to Neo L1; a non-zero adapter receives the fully domain-bound payout
///     call and is responsible for credit/mint/call logic.</description></item>
///   <item><description><c>0x07 + externalChainId(4B LE) + neoAsset(20B)</c> →
///     immutable foreign-asset reverse mapping. One Neo asset cannot back two
///     foreign assets in the same source-chain domain.</description></item>
///   <item><description><c>0x08 + externalChainId(4B LE) + foreignAsset(20B)</c> →
///     payout-adapter Neo contract update counter (u16 LE). Kept separate from
///     the 41-byte route value so contract upgrades preserve existing route layout.</description></item>
///   <item><description><c>0xFD</c> → immutable Neo destination domain bound at
///     deployment (<c>0</c> = Neo L1, non-zero = Neo L2). Inbound messages must
///     carry this exact value in their signed <c>neoChainId</c> field.</description></item>
/// </list>
///
/// <para>Asset messages are finalized only when their configured payout succeeds.
/// Replay state, balance debit, adapter invocation, and events share one NeoVM
/// transaction, so a failed transfer or adapter call reverts the entire receive.</para>
/// </remarks>
[DisplayName("NeoHub.ExternalBridgeEscrow")]
[ContractAuthor("R3E Network", "dev@r3e.network")]
[ContractDescription("L1 escrow + dispatch for cross-foreign-chain messages.")]
[ContractVersion("0.2.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.ExternalBridgeEscrow")]
[ContractPermission(Permission.Any, Method.Any)]
public class ExternalBridgeEscrowContract : SmartContract
{
    private const byte PrefixOutboundNonce = 0x01;
    private const byte PrefixConsumedInboundNonce = 0x02;
    private const byte PrefixLockedBalance = 0x03;
    private const byte PrefixPendingTransfer = 0x04;
    private const byte PrefixAssetRoute = 0x05;
    private const byte PrefixConsumedGovernanceProposal = 0x06;
    private const byte PrefixReverseAssetRoute = 0x07;
    private const byte PrefixPayoutAdapterUpdateCounter = 0x08;
    private const byte KeyGovernanceLocked = 0xFB;
    private const byte KeyGovernanceController = 0xFC;
    private const byte KeyNeoChainId = 0xFD;
    private const byte KeyRegistry = 0xFE;
    private const byte KeyOwner = 0xFF;
    private const int OffsetExternalChainId = 0;
    private const int OffsetNeoChainId = 4;
    private const int OffsetNonce = 8;
    private const int OffsetDirection = 16;
    private const int OffsetRecipient = 37;
    private const int OffsetDeadlineUnixSeconds = 57;
    private const int OffsetSourceTxRef = 65;
    private const int OffsetMessageType = 97;
    private const int OffsetPayloadLength = 98;
    private const int OffsetPayload = 102;
    private const int RouteOffsetNeoAsset = 0;
    private const int RouteOffsetPayoutAdapter = 20;
    private const int RouteOffsetActive = 40;
    private const int RouteSize = 41;
    private const int MaxPayloadLength = 64 * 1024;
    private const int MaxAmountBytes = 32;
    private const byte PayoutAdapterVersion = 1;
    private const byte MessageTypeAssetTransfer = 0;
    private const byte MessageTypeCall = 1;
    private const byte MessageTypeAssetAndCall = 2;

    /// <summary>Emitted on outbound send (Neo → foreign). Off-chain watchers
    /// listen for this event to attest the message on the foreign chain.</summary>
    [DisplayName("CrossChainSendInitiated")]
    public static event Action<uint, ulong, UInt160, UInt160, UInt160, BigInteger, byte[]> OnCrossChainSendInitiated = default!;

    /// <summary>Emitted on verified inbound message (foreign → Neo) — the
    /// settlement event the on-chain payout side reads.</summary>
    [DisplayName("CrossChainInboundFinalized")]
    public static event Action<uint, ulong, byte> OnCrossChainInboundFinalized = default!;

    /// <summary>Emitted after a verified inbound asset payout succeeds.</summary>
    [DisplayName("CrossChainAssetPaid")]
    public static event Action<uint, uint, ulong, UInt160, UInt160, UInt160, BigInteger, UInt160> OnCrossChainAssetPaid = default!;

    /// <summary>Emitted whenever governance creates, replaces, or disables an asset route.</summary>
    [DisplayName("AssetRouteConfigured")]
    public static event Action<uint, UInt160, UInt160, UInt160, bool> OnAssetRouteConfigured = default!;

    /// <summary>Emitted when the governance controller is wired before the production lock.</summary>
    [DisplayName("GovernanceControllerChanged")]
    public static event Action<UInt160> OnGovernanceControllerChanged = default!;

    /// <summary>Emitted when direct owner route/registry administration is disabled forever.</summary>
    [DisplayName("GovernanceLocked")]
    public static event Action OnGovernanceLocked = default!;

    /// <summary>Emitted when the registry contract hash is changed.</summary>
    [DisplayName("RegistryChanged")]
    public static event Action<UInt160> OnRegistryChanged = default!;

    /// <summary>Emitted when ownership is transferred.</summary>
    [DisplayName("OwnerChanged")]
    public static event Action<UInt160, UInt160> OnOwnerChanged = default!;

    /// <summary>
    /// Set the initial owner, registry hash, and permanent Neo destination domain on deploy.
    /// Zero identifies Neo L1; non-zero values identify Neo L2 chains. Any future code
    /// update must preserve the already-written binding, including an explicit zero.
    /// </summary>
    public static void _deploy(object data, bool update)
    {
        if (update)
        {
            ExecutionEngine.Assert(Storage.Get(new byte[] { KeyNeoChainId }) != null,
                "neoChainId binding missing during upgrade");
            return;
        }
        var arr = (object[])data;
        var owner = (UInt160)arr[0];
        var registry = (UInt160)arr[1];
        var neoChainId = (uint)(BigInteger)arr[2];
        ExecutionEngine.Assert(owner.IsValid && !owner.IsZero, "invalid owner");
        ExecutionEngine.Assert(registry.IsValid && !registry.IsZero, "invalid registry");
        Storage.Put(new byte[] { KeyOwner }, owner);
        Storage.Put(new byte[] { KeyRegistry }, registry);
        Storage.Put(new byte[] { KeyNeoChainId }, (BigInteger)neoChainId);
    }

    /// <summary>Owner — the only address that can rotate the registry pointer.</summary>
    [Safe]
    public static UInt160 GetOwner()
    {
        var raw = Storage.Get(new byte[] { KeyOwner });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Transfer governance ownership. Owner only.</summary>
    public static void SetOwner(UInt160 newOwner)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(newOwner.IsValid && !newOwner.IsZero, "invalid new owner");
        var oldOwner = GetOwner();
        Storage.Put(new byte[] { KeyOwner }, newOwner);
        OnOwnerChanged(oldOwner, newOwner);
    }

    /// <summary>The currently-wired ExternalBridgeRegistry hash.</summary>
    [Safe]
    public static UInt160 GetRegistry()
    {
        var raw = Storage.Get(new byte[] { KeyRegistry });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>The immutable Neo target domain accepted by this escrow (zero means L1).</summary>
    [Safe]
    public static uint GetNeoChainId()
    {
        var raw = Storage.Get(new byte[] { KeyNeoChainId });
        return raw == null ? 0 : (uint)(BigInteger)raw;
    }

    /// <summary>Owner-only: rotate the registry pointer (for governance-mediated
    /// upgrades from MPC → Optimistic → ZK without redeploying the escrow).</summary>
    public static void SetRegistry(UInt160 registry)
    {
        AssertDirectGovernance();
        ExecutionEngine.Assert(registry.IsValid && !registry.IsZero, "invalid registry");
        WriteRegistry(registry);
    }

    /// <summary>Governance/timelock path for registry rotation after the production lock.</summary>
    public static void SetRegistryViaProposal(UInt160 registry, ulong proposalId)
    {
        ExecutionEngine.Assert(registry.IsValid && !registry.IsZero, "invalid registry");
        ConsumeBoundProposal(proposalId, BuildSetRegistryAction(registry));
        WriteRegistry(registry);
    }

    /// <summary>Canonical proposal payload for <see cref="SetRegistryViaProposal"/>.</summary>
    [Safe]
    public static byte[] BuildSetRegistryAction(UInt160 registry)
    {
        var tag = ActionTagSetRegistry;
        var result = new byte[tag.Length + 20 + 4 + 20];
        var pos = 0;
        for (var i = 0; i < tag.Length; i++) result[pos++] = tag[i];
        WriteUInt160(result, pos, Runtime.ExecutingScriptHash);
        pos += 20;
        WriteUInt32(result, pos, GetNeoChainId());
        pos += 4;
        WriteUInt160(result, pos, registry);
        return result;
    }

    /// <summary>Owner-only bootstrap wiring for the proposal/timelock controller.</summary>
    public static void SetGovernanceController(UInt160 governanceController)
    {
        AssertDirectGovernance();
        ExecutionEngine.Assert(
            governanceController.IsValid && !governanceController.IsZero,
            "invalid governance controller");
        Storage.Put(new byte[] { KeyGovernanceController }, governanceController);
        OnGovernanceControllerChanged(governanceController);
    }

    /// <summary>Configured proposal/timelock controller, or zero before production wiring.</summary>
    [Safe]
    public static UInt160 GetGovernanceController()
    {
        var raw = Storage.Get(new byte[] { KeyGovernanceController });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>
    /// Irreversibly disable direct owner changes to the verifier-registry pointer and payout routes.
    /// Liquidity funding remains operational; all value-routing changes must use an exactly bound,
    /// approved, timelocked proposal after this call.
    /// </summary>
    public static void LockGovernance()
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(GetGovernanceController() != UInt160.Zero,
            "governance controller not wired");
        if (!IsGovernanceLocked())
        {
            Storage.Put(new byte[] { KeyGovernanceLocked }, new byte[] { 1 });
            OnGovernanceLocked();
        }
    }

    /// <summary>Whether direct owner route and registry changes are disabled forever.</summary>
    [Safe]
    public static bool IsGovernanceLocked()
    {
        return Storage.Get(new byte[] { KeyGovernanceLocked }) != null;
    }

    /// <summary>
    /// Configure the canonical foreign-asset payout route. On an L1-domain escrow, a zero
    /// adapter releases <paramref name="neoAsset"/> directly from this escrow. A non-zero
    /// adapter is mandatory for L2 destinations and is called through the versioned
    /// <c>payout</c> ABI during <see cref="Receive"/>.
    /// Replacing the adapter is an owner-governed upgrade and does not reset replay state.
    /// </summary>
    public static void SetAssetRoute(
        uint externalChainId,
        UInt160 foreignAsset,
        UInt160 neoAsset,
        UInt160 payoutAdapter)
    {
        AssertDirectGovernance();
        WriteAssetRoute(externalChainId, foreignAsset, neoAsset, payoutAdapter, true);
    }

    /// <summary>Enable or disable an existing asset route without deleting its audit trail.</summary>
    public static void SetAssetRouteActive(uint externalChainId, UInt160 foreignAsset, bool active)
    {
        AssertDirectGovernance();
        var route = ReadAssetRoute(externalChainId, foreignAsset);
        WriteAssetRoute(externalChainId, foreignAsset,
            ReadUInt160(route, RouteOffsetNeoAsset),
            ReadUInt160(route, RouteOffsetPayoutAdapter), active);
    }

    /// <summary>
    /// Create, upgrade, or disable an asset route through an approved and timelocked proposal.
    /// The foreign-to-Neo asset mapping is immutable once created; only the versioned payout
    /// adapter and active flag may change for an existing route.
    /// </summary>
    public static void ConfigureAssetRouteViaProposal(
        uint externalChainId,
        UInt160 foreignAsset,
        UInt160 neoAsset,
        UInt160 payoutAdapter,
        bool active,
        ulong proposalId)
    {
        var action = BuildConfigureAssetRouteAction(
            externalChainId, foreignAsset, neoAsset, payoutAdapter, active);
        ConsumeBoundProposal(proposalId, action);
        WriteAssetRoute(externalChainId, foreignAsset, neoAsset, payoutAdapter, active);
    }

    /// <summary>Canonical proposal payload for <see cref="ConfigureAssetRouteViaProposal"/>.</summary>
    [Safe]
    public static byte[] BuildConfigureAssetRouteAction(
        uint externalChainId,
        UInt160 foreignAsset,
        UInt160 neoAsset,
        UInt160 payoutAdapter,
        bool active)
    {
        var tag = ActionTagConfigureAssetRoute;
        var result = new byte[tag.Length + 20 + 4 + 4 + 20 + 20 + 20 + 1];
        var pos = 0;
        for (var i = 0; i < tag.Length; i++) result[pos++] = tag[i];
        WriteUInt160(result, pos, Runtime.ExecutingScriptHash);
        pos += 20;
        WriteUInt32(result, pos, GetNeoChainId());
        pos += 4;
        WriteUInt32(result, pos, externalChainId);
        pos += 4;
        WriteUInt160(result, pos, foreignAsset);
        pos += 20;
        WriteUInt160(result, pos, neoAsset);
        pos += 20;
        WriteUInt160(result, pos, payoutAdapter);
        pos += 20;
        result[pos] = (byte)(active ? 1 : 0);
        return result;
    }

    /// <summary>Neo-side asset configured for a foreign asset, or zero when absent.</summary>
    [Safe]
    public static UInt160 GetRoutedNeoAsset(uint externalChainId, UInt160 foreignAsset)
    {
        var raw = Storage.Get(AssetRouteKey(externalChainId, foreignAsset));
        return raw == null
            ? UInt160.Zero
            : ReadUInt160(AssertRouteBytes((byte[])raw), RouteOffsetNeoAsset);
    }

    /// <summary>Payout adapter configured for a foreign asset; zero means direct L1 escrow release.</summary>
    [Safe]
    public static UInt160 GetPayoutAdapter(uint externalChainId, UInt160 foreignAsset)
    {
        var raw = Storage.Get(AssetRouteKey(externalChainId, foreignAsset));
        return raw == null
            ? UInt160.Zero
            : ReadUInt160(AssertRouteBytes((byte[])raw), RouteOffsetPayoutAdapter);
    }

    /// <summary>
    /// Neo contract update counter pinned for the configured payout adapter.
    /// Zero is returned for direct-release routes and missing routes.
    /// </summary>
    [Safe]
    public static ushort GetPayoutAdapterUpdateCounter(
        uint externalChainId,
        UInt160 foreignAsset)
    {
        var raw = Storage.Get(AssetRouteKey(externalChainId, foreignAsset));
        if (raw == null)
            return 0;
        AssertRouteBytes((byte[])raw);
        var counter = Storage.Get(PayoutAdapterUpdateCounterKey(externalChainId, foreignAsset));
        return counter == null
            ? (ushort)0
            : ReadUInt16(AssertUpdateCounterBytes((byte[])counter), 0);
    }

    /// <summary>Whether the foreign asset route exists and is enabled.</summary>
    [Safe]
    public static bool IsAssetRouteActive(uint externalChainId, UInt160 foreignAsset)
    {
        var raw = Storage.Get(AssetRouteKey(externalChainId, foreignAsset));
        return raw != null
            && AssertRouteBytes((byte[])raw)[RouteOffsetActive] == 1;
    }

    /// <summary>Foreign asset immutably mapped to a Neo asset, or zero when absent.</summary>
    [Safe]
    public static UInt160 GetRoutedForeignAsset(uint externalChainId, UInt160 neoAsset)
    {
        var raw = Storage.Get(ReverseAssetRouteKey(externalChainId, neoAsset));
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>
    /// Owner-only liquidity deposit for direct-release routes. Unlike <see cref="Send"/>,
    /// this does not allocate a bridge nonce or emit an outbound message.
    /// </summary>
    public static void FundLiquidity(uint externalChainId, UInt160 asset, BigInteger amount)
    {
        var owner = GetOwner();
        ExecutionEngine.Assert(Runtime.CheckWitness(owner), "not authorized");
        ExecutionEngine.Assert(GetNeoChainId() == 0,
            "direct escrow liquidity is only valid for the Neo L1 destination domain");
        AssertForeignChainId(externalChainId);
        ExecutionEngine.Assert(asset.IsValid && !asset.IsZero, "invalid asset");
        ExecutionEngine.Assert(amount > 0, "amount must be positive");
        var foreignAsset = GetRoutedForeignAsset(externalChainId, asset);
        ExecutionEngine.Assert(foreignAsset != UInt160.Zero,
            "direct asset route not found");
        var route = ReadAssetRoute(externalChainId, foreignAsset);
        ExecutionEngine.Assert(route[RouteOffsetActive] == 1,
            "direct asset route inactive");
        ExecutionEngine.Assert(ReadUInt160(route, RouteOffsetPayoutAdapter) == UInt160.Zero,
            "liquidity funding is only valid for direct-release routes");

        TransferIntoCustody(asset, owner, amount, "asset transfer failed (fund liquidity)");
        CreditLockedBalance(externalChainId, asset, amount);
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
    /// and use the (externalChainId, neoChainId, nonce) tuple as the canonical lookup key.</returns>
    public static ulong Send(
        uint externalChainId,
        UInt160 recipient,
        UInt160 asset,
        BigInteger amount,
        byte[] calldata,
        ulong deadlineUnixSeconds)
    {
        var neoChainId = GetNeoChainId();
        AssertForeignChainId(externalChainId);
        ExecutionEngine.Assert(recipient.IsValid && !recipient.IsZero, "invalid recipient");
        ExecutionEngine.Assert(asset.IsValid && !asset.IsZero, "invalid asset");
        ExecutionEngine.Assert(amount > 0, "amount must be positive");

        // Allocate the nonce before the untrusted NEP-17 call so re-entrant sends
        // cannot reuse it. A later FAULT reverts this write atomically.
        var nonceKey = OutboundNonceKey(externalChainId, neoChainId);
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

        var sender = (UInt160)Runtime.CallingScriptHash;
        TransferIntoCustody(asset, sender, amount, "asset transfer failed (lock)");
        CreditLockedBalance(externalChainId, asset, amount);

        // Emit the canonical send event. The `calldata` blob is the payload —
        // off-chain watchers re-encode the full ExternalCrossChainMessage from
        // these args + nonce + their observation timestamp and sign the hash.
        OnCrossChainSendInitiated(externalChainId, next, sender, recipient, asset, amount, calldata);
        return next;
    }

    /// <summary>
    /// Process a verified inbound message. Verifies the proof via the
    /// registered verifier through <c>ExternalBridgeRegistry.VerifyInbound</c>,
    /// rejects on replay (per-(externalChainId,neoChainId,nonce)), and atomically
    /// dispatches asset messages to the configured direct-release or adapter route.
    /// <para>
    /// SECURITY (domain separation): every deployment is permanently bound to one explicit
    /// <c>neoChainId</c>. The signed message field must match that binding before verifier dispatch,
    /// and both source and target domains participate in the replay key. Multiple Neo L2s can
    /// therefore share a committee without accepting one another's messages.
    /// </para>
    /// </summary>
    public static void Receive(uint externalChainId, byte[] messageBytes, byte[] proofBytes)
    {
        // Validate external chain ID namespace (must be in 0xE0_xx_xx_xx range)
        AssertForeignChainId(externalChainId);
        ExecutionEngine.Assert(messageBytes.Length >= 102,
            "messageBytes too short");

        // Parse fields from the canonical ExternalCrossChainMessage layout.
        var signedExternalChainId =
            (uint)messageBytes[OffsetExternalChainId]
            | ((uint)messageBytes[OffsetExternalChainId + 1] << 8)
            | ((uint)messageBytes[OffsetExternalChainId + 2] << 16)
            | ((uint)messageBytes[OffsetExternalChainId + 3] << 24);
        ExecutionEngine.Assert(signedExternalChainId == externalChainId,
            "externalChainId argument does not match signed message domain");

        var neoChainId = GetNeoChainId();
        var signedNeoChainId =
            (uint)messageBytes[OffsetNeoChainId]
            | ((uint)messageBytes[OffsetNeoChainId + 1] << 8)
            | ((uint)messageBytes[OffsetNeoChainId + 2] << 16)
            | ((uint)messageBytes[OffsetNeoChainId + 3] << 24);
        ExecutionEngine.Assert(signedNeoChainId == neoChainId,
            "signed message neoChainId does not match escrow domain");

        var nonce =
            (ulong)messageBytes[OffsetNonce]
            | ((ulong)messageBytes[OffsetNonce + 1] << 8)
            | ((ulong)messageBytes[OffsetNonce + 2] << 16)
            | ((ulong)messageBytes[OffsetNonce + 3] << 24)
            | ((ulong)messageBytes[OffsetNonce + 4] << 32)
            | ((ulong)messageBytes[OffsetNonce + 5] << 40)
            | ((ulong)messageBytes[OffsetNonce + 6] << 48)
            | ((ulong)messageBytes[OffsetNonce + 7] << 56);
        var direction = messageBytes[OffsetDirection];
        ExecutionEngine.Assert(direction == 2, "direction must be 2 (ForeignToNeo)");
        var deadlineUnixSeconds =
            (ulong)messageBytes[OffsetDeadlineUnixSeconds]
            | ((ulong)messageBytes[OffsetDeadlineUnixSeconds + 1] << 8)
            | ((ulong)messageBytes[OffsetDeadlineUnixSeconds + 2] << 16)
            | ((ulong)messageBytes[OffsetDeadlineUnixSeconds + 3] << 24)
            | ((ulong)messageBytes[OffsetDeadlineUnixSeconds + 4] << 32)
            | ((ulong)messageBytes[OffsetDeadlineUnixSeconds + 5] << 40)
            | ((ulong)messageBytes[OffsetDeadlineUnixSeconds + 6] << 48)
            | ((ulong)messageBytes[OffsetDeadlineUnixSeconds + 7] << 56);
        ExecutionEngine.Assert(deadlineUnixSeconds == 0 || Runtime.Time / 1000UL <= deadlineUnixSeconds,
            "external bridge message expired");

        var messageType = messageBytes[OffsetMessageType];
        ExecutionEngine.Assert(
            messageType == MessageTypeAssetTransfer
            || messageType == MessageTypeCall
            || messageType == MessageTypeAssetAndCall,
            "unknown external bridge message type");
        var payloadLength = ReadUInt32(messageBytes, OffsetPayloadLength);
        ExecutionEngine.Assert(payloadLength <= MaxPayloadLength,
            "external bridge payload too large");
        ExecutionEngine.Assert(messageBytes.Length == OffsetPayload + (int)payloadLength,
            "messageBytes length does not match payload length");

        // Replay protection at the escrow layer (the verifier ALSO replay-protects;
        // this catches a verifier that's been swapped to one without nonce tracking).
        var consumedKey = ConsumedInboundKey(externalChainId, neoChainId, nonce);
        ExecutionEngine.Assert(Storage.Get(consumedKey) == null,
            "inbound nonce already consumed (replay)");

        // Dispatch verification through the registry.
        var registry = GetRegistry();
        ExecutionEngine.Assert(registry != UInt160.Zero, "registry not wired");
        var ok = (bool)Contract.Call(registry, "verifyInbound", CallFlags.ReadOnly,
            new object[] { externalChainId, messageBytes, proofBytes });
        ExecutionEngine.Assert(ok, "registry verifier rejected inbound message");

        // Effects precede the external payout call. NeoVM reverts these writes if
        // transfer/adapter execution faults, while re-entrant Receive sees consumed.
        Storage.Put(consumedKey, new byte[] { 1 });

        if (messageType == MessageTypeAssetTransfer || messageType == MessageTypeAssetAndCall)
        {
            PayoutAsset(externalChainId, neoChainId, nonce, deadlineUnixSeconds,
                messageType, messageBytes, payloadLength);
        }

        OnCrossChainInboundFinalized(externalChainId, nonce, messageType);
    }

    /// <summary>Read the next outbound nonce assigned by Send (i.e. the
    /// last-used nonce). Useful for off-chain indexers tracking pending
    /// messages.</summary>
    [Safe]
    public static ulong GetLastOutboundNonce(uint externalChainId)
    {
        var neoChainId = GetNeoChainId();
        var raw = Storage.Get(OutboundNonceKey(externalChainId, neoChainId));
        if (raw == null) return 0;
        var b = (byte[])raw;
        return ((ulong)b[0])
            | ((ulong)b[1] << 8) | ((ulong)b[2] << 16) | ((ulong)b[3] << 24)
            | ((ulong)b[4] << 32) | ((ulong)b[5] << 40) | ((ulong)b[6] << 48)
            | ((ulong)b[7] << 56);
    }

    /// <summary>Read the cumulative outbound locked balance for an
    /// (externalChainId, asset) pair. Direct inbound payouts can never exceed this
    /// on-chain balance.</summary>
    [Safe]
    public static BigInteger GetLockedBalance(uint externalChainId, UInt160 asset)
    {
        var raw = Storage.Get(LockedBalanceKey(externalChainId, asset));
        return raw == null ? BigInteger.Zero : (BigInteger)raw;
    }

    /// <summary>
    /// Has the inbound nonce for this escrow's immutable Neo destination domain been consumed already?
    /// </summary>
    [Safe]
    public static bool IsInboundConsumed(uint externalChainId, ulong nonce)
    {
        var neoChainId = GetNeoChainId();
        return Storage.Get(ConsumedInboundKey(externalChainId, neoChainId, nonce)) != null;
    }

    /// <summary>NEP-17 hook. Accept only transfers initiated by <see cref="Send"/> or <see cref="FundLiquidity"/>.</summary>
    public static void OnNEP17Payment(UInt160 from, BigInteger amount, object data)
    {
        ExecutionEngine.Assert(amount > 0, "amount must be positive");
        var asset = (UInt160)Runtime.CallingScriptHash;
        var pendingKey = PendingTransferKey(asset, from);
        var expected = Storage.Get(pendingKey);
        ExecutionEngine.Assert(expected != null,
            "direct transfer rejected — use an escrow transfer entrypoint");
        ExecutionEngine.Assert((BigInteger)expected! == amount,
            "NEP-17 callback amount does not match pending transfer");
        Storage.Delete(pendingKey);
    }

    private static byte[] OutboundNonceKey(uint externalChainId, uint neoChainId)
    {
        var k = new byte[1 + 4 + 4];
        k[0] = PrefixOutboundNonce;
        k[1] = (byte)externalChainId;
        k[2] = (byte)(externalChainId >> 8);
        k[3] = (byte)(externalChainId >> 16);
        k[4] = (byte)(externalChainId >> 24);
        k[5] = (byte)neoChainId;
        k[6] = (byte)(neoChainId >> 8);
        k[7] = (byte)(neoChainId >> 16);
        k[8] = (byte)(neoChainId >> 24);
        return k;
    }

    private static byte[] ConsumedInboundKey(uint externalChainId, uint neoChainId, ulong nonce)
    {
        var k = new byte[1 + 4 + 4 + 8];
        k[0] = PrefixConsumedInboundNonce;
        k[1] = (byte)externalChainId;
        k[2] = (byte)(externalChainId >> 8);
        k[3] = (byte)(externalChainId >> 16);
        k[4] = (byte)(externalChainId >> 24);
        k[5] = (byte)neoChainId;
        k[6] = (byte)(neoChainId >> 8);
        k[7] = (byte)(neoChainId >> 16);
        k[8] = (byte)(neoChainId >> 24);
        k[9] = (byte)nonce; k[10] = (byte)(nonce >> 8);
        k[11] = (byte)(nonce >> 16); k[12] = (byte)(nonce >> 24);
        k[13] = (byte)(nonce >> 32); k[14] = (byte)(nonce >> 40);
        k[15] = (byte)(nonce >> 48); k[16] = (byte)(nonce >> 56);
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

    private static byte[] PendingTransferKey(UInt160 asset, UInt160 from)
    {
        var key = new byte[1 + 20 + 20];
        key[0] = PrefixPendingTransfer;
        var assetBytes = (byte[])asset;
        var fromBytes = (byte[])from;
        for (var i = 0; i < 20; i++)
        {
            key[1 + i] = assetBytes[i];
            key[21 + i] = fromBytes[i];
        }
        return key;
    }

    private static void PayoutAsset(
        uint externalChainId,
        uint neoChainId,
        ulong nonce,
        ulong deadlineUnixSeconds,
        byte messageType,
        byte[] messageBytes,
        uint payloadLength)
    {
        ExecutionEngine.Assert(payloadLength >= 25,
            "asset payload too short");
        var foreignAsset = ReadUInt160(messageBytes, OffsetPayload);
        ExecutionEngine.Assert(foreignAsset.IsValid && !foreignAsset.IsZero,
            "invalid foreign asset");

        var amountLength = ReadUInt32(messageBytes, OffsetPayload + 20);
        ExecutionEngine.Assert(amountLength > 0 && amountLength <= MaxAmountBytes,
            "amount length out of bounds");
        var assetPrefixLength = 24 + (int)amountLength;
        ExecutionEngine.Assert(payloadLength >= assetPrefixLength,
            "asset payload truncated");
        if (messageType == MessageTypeAssetTransfer)
            ExecutionEngine.Assert(payloadLength == assetPrefixLength,
                "asset-transfer payload contains trailing bytes");

        var amountOffset = OffsetPayload + 24;
        ExecutionEngine.Assert(messageBytes[amountOffset + (int)amountLength - 1] != 0,
            "amount must use minimal unsigned little-endian encoding");
        var amount = BigInteger.Zero;
        for (var i = (int)amountLength - 1; i >= 0; i--)
            amount = amount * 256 + messageBytes[amountOffset + i];
        ExecutionEngine.Assert(amount > 0, "amount must be positive");

        var recipient = ReadUInt160(messageBytes, OffsetRecipient);
        ExecutionEngine.Assert(recipient.IsValid && !recipient.IsZero,
            "invalid Neo recipient");

        var routeRaw = Storage.Get(AssetRouteKey(externalChainId, foreignAsset));
        ExecutionEngine.Assert(routeRaw != null, "asset route not found");
        var route = AssertRouteBytes((byte[])routeRaw!);
        ExecutionEngine.Assert(route[RouteOffsetActive] == 1, "asset route inactive");
        var neoAsset = ReadUInt160(route, RouteOffsetNeoAsset);
        var reverseRaw = Storage.Get(ReverseAssetRouteKey(externalChainId, neoAsset));
        ExecutionEngine.Assert(reverseRaw != null && (UInt160)reverseRaw == foreignAsset,
            "asset route reverse mapping inconsistent — migrate route through governance");
        var adapter = ReadUInt160(route, RouteOffsetPayoutAdapter);

        if (adapter == UInt160.Zero)
        {
            ExecutionEngine.Assert(neoChainId == 0,
                "Neo L2 destination routes require a payout adapter");
            ExecutionEngine.Assert(payloadLength == assetPrefixLength,
                "asset-and-call route requires a payout adapter");
            var balanceKey = LockedBalanceKey(externalChainId, neoAsset);
            var balanceRaw = Storage.Get(balanceKey);
            var balance = balanceRaw == null ? BigInteger.Zero : (BigInteger)balanceRaw;
            ExecutionEngine.Assert(balance >= amount, "insufficient escrow liquidity");
            Storage.Put(balanceKey, balance - amount);
            var transferred = (bool)Contract.Call(neoAsset, "transfer", CallFlags.All,
                new object[] { Runtime.ExecutingScriptHash, recipient, amount, messageBytes });
            ExecutionEngine.Assert(transferred, "asset payout transfer failed");
        }
        else
        {
            AssertPayoutAdapterIntegrity(
                adapter,
                ReadPinnedPayoutAdapterUpdateCounter(externalChainId, foreignAsset));
            var sourceTxRef = ReadUInt256(messageBytes, OffsetSourceTxRef);
            var paid = (bool)Contract.Call(adapter, "payout", CallFlags.All,
                new object[]
                {
                    externalChainId, neoChainId, nonce, foreignAsset, neoAsset,
                    recipient, amount, deadlineUnixSeconds, sourceTxRef, messageBytes
                });
            ExecutionEngine.Assert(paid, "payout adapter rejected inbound asset");
        }

        OnCrossChainAssetPaid(externalChainId, neoChainId, nonce, foreignAsset,
            neoAsset, recipient, amount, adapter);
    }

    private static void CreditLockedBalance(uint externalChainId, UInt160 asset, BigInteger amount)
    {
        var balanceKey = LockedBalanceKey(externalChainId, asset);
        var previous = Storage.Get(balanceKey);
        var previousAmount = previous == null ? BigInteger.Zero : (BigInteger)previous;
        Storage.Put(balanceKey, previousAmount + amount);
    }

    private static void TransferIntoCustody(
        UInt160 asset,
        UInt160 from,
        BigInteger amount,
        string transferFailureMessage)
    {
        var escrow = Runtime.ExecutingScriptHash;
        var balanceBefore = (BigInteger)Contract.Call(asset, "balanceOf", CallFlags.ReadOnly,
            new object[] { escrow });
        var pendingKey = PendingTransferKey(asset, from);
        ExecutionEngine.Assert(Storage.Get(pendingKey) == null,
            "asset transfer already pending");
        Storage.Put(pendingKey, amount);
        var transferred = (bool)Contract.Call(asset, "transfer", CallFlags.All,
            new object[] { from, escrow, amount, null! });
        ExecutionEngine.Assert(transferred, transferFailureMessage);
        ExecutionEngine.Assert(Storage.Get(pendingKey) == null,
            "NEP-17 callback not received");
        var balanceAfter = (BigInteger)Contract.Call(asset, "balanceOf", CallFlags.ReadOnly,
            new object[] { escrow });
        ExecutionEngine.Assert(balanceAfter == balanceBefore + amount,
            "asset custody balance increase does not match amount");
    }

    private static void AssertDirectGovernance()
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(!IsGovernanceLocked(),
            "direct governance disabled — use an approved timelocked proposal");
    }

    private static void WriteRegistry(UInt160 registry)
    {
        Storage.Put(new byte[] { KeyRegistry }, registry);
        OnRegistryChanged(registry);
    }

    private static void ConsumeBoundProposal(ulong proposalId, byte[] expectedAction)
    {
        var governanceController = GetGovernanceController();
        ExecutionEngine.Assert(governanceController != UInt160.Zero,
            "governance controller not wired");
        var consumedKey = GovernanceProposalKey(proposalId);
        ExecutionEngine.Assert(Storage.Get(consumedKey) == null,
            "proposal already consumed");
        var approved = (bool)Contract.Call(governanceController,
            "isApprovedAndTimelocked", CallFlags.ReadOnly, new object[] { proposalId });
        ExecutionEngine.Assert(approved, "proposal not approved + timelocked");
        var bound = (bool)Contract.Call(governanceController,
            "matchesProposalPayload", CallFlags.ReadOnly,
            new object[] { proposalId, expectedAction });
        ExecutionEngine.Assert(bound, "proposal payload does not match action args");
        Storage.Put(consumedKey, new byte[] { 1 });
    }

    private static void WriteAssetRoute(
        uint externalChainId,
        UInt160 foreignAsset,
        UInt160 neoAsset,
        UInt160 payoutAdapter,
        bool active)
    {
        AssertForeignChainId(externalChainId);
        ExecutionEngine.Assert(foreignAsset.IsValid && !foreignAsset.IsZero,
            "invalid foreign asset");
        ExecutionEngine.Assert(neoAsset.IsValid && !neoAsset.IsZero,
            "invalid Neo asset");
        ExecutionEngine.Assert(payoutAdapter == UInt160.Zero || payoutAdapter.IsValid,
            "invalid payout adapter");
        ExecutionEngine.Assert(!active || payoutAdapter != UInt160.Zero || GetNeoChainId() == 0,
            "active Neo L2 destination routes require a payout adapter");
        var routeKey = AssetRouteKey(externalChainId, foreignAsset);
        var existingRaw = Storage.Get(routeKey);
        var existingAdapter = UInt160.Zero;
        if (existingRaw != null)
        {
            var existing = AssertRouteBytes((byte[])existingRaw);
            ExecutionEngine.Assert(ReadUInt160(existing, RouteOffsetNeoAsset) == neoAsset,
                "foreign asset mapping is immutable");
            existingAdapter = ReadUInt160(existing, RouteOffsetPayoutAdapter);
        }

        var reverseKey = ReverseAssetRouteKey(externalChainId, neoAsset);
        var reverseRaw = Storage.Get(reverseKey);
        ExecutionEngine.Assert(!active || reverseRaw == null || (UInt160)reverseRaw == foreignAsset,
            "Neo asset already mapped to another foreign asset");

        ushort adapterUpdateCounter = 0;
        var updateCounterKey = PayoutAdapterUpdateCounterKey(externalChainId, foreignAsset);
        var updateCounterRaw = Storage.Get(updateCounterKey);
        var writeAdapterUpdateCounter = false;
        if (payoutAdapter != UInt160.Zero)
        {
            if (existingRaw != null && existingAdapter == payoutAdapter)
            {
                if (updateCounterRaw != null)
                {
                    adapterUpdateCounter = ReadUInt16(
                        AssertUpdateCounterBytes((byte[])updateCounterRaw), 0);
                    if (active)
                        AssertPayoutAdapterIntegrity(payoutAdapter, adapterUpdateCounter);
                }
                else if (active)
                {
                    adapterUpdateCounter = GetPayoutAdapterUpdateCounter(payoutAdapter);
                    ExecutionEngine.Assert(adapterUpdateCounter == 0,
                        "payout adapter must be a never-updated deployment");
                    AssertPayoutAdapterVersion(payoutAdapter);
                    writeAdapterUpdateCounter = true;
                }
            }
            else
            {
                adapterUpdateCounter = GetPayoutAdapterUpdateCounter(payoutAdapter);
                ExecutionEngine.Assert(adapterUpdateCounter == 0,
                    "payout adapter must be a never-updated deployment");
                AssertPayoutAdapterVersion(payoutAdapter);
                writeAdapterUpdateCounter = true;
            }
        }

        var route = new byte[RouteSize];
        WriteUInt160(route, RouteOffsetNeoAsset, neoAsset);
        WriteUInt160(route, RouteOffsetPayoutAdapter, payoutAdapter);
        route[RouteOffsetActive] = (byte)(active ? 1 : 0);
        Storage.Put(routeKey, route);
        if (payoutAdapter == UInt160.Zero)
            Storage.Delete(updateCounterKey);
        else if (writeAdapterUpdateCounter)
        {
            var updateCounterBytes = new byte[2];
            WriteUInt16(updateCounterBytes, 0, adapterUpdateCounter);
            Storage.Put(updateCounterKey, updateCounterBytes);
        }
        if (active && reverseRaw == null)
            Storage.Put(reverseKey, foreignAsset);
        OnAssetRouteConfigured(externalChainId, foreignAsset, neoAsset, payoutAdapter, active);
    }

    private static byte[] ReadAssetRoute(uint externalChainId, UInt160 foreignAsset)
    {
        var raw = Storage.Get(AssetRouteKey(externalChainId, foreignAsset));
        ExecutionEngine.Assert(raw != null, "asset route not found");
        return AssertRouteBytes((byte[])raw!);
    }

    private static byte[] AssertRouteBytes(byte[] route)
    {
        ExecutionEngine.Assert(route.Length == RouteSize, "asset route storage corrupt");
        return route;
    }

    private static byte[] AssertUpdateCounterBytes(byte[] updateCounter)
    {
        ExecutionEngine.Assert(updateCounter.Length == 2,
            "payout adapter update-counter storage corrupt");
        return updateCounter;
    }

    private static void AssertPayoutAdapterVersion(UInt160 payoutAdapter)
    {
        var version = (byte)Contract.Call(
            payoutAdapter, "payoutVersion", CallFlags.ReadOnly, new object[0]);
        ExecutionEngine.Assert(version == PayoutAdapterVersion,
            "unsupported payout adapter version");
    }

    private static ushort GetPayoutAdapterUpdateCounter(UInt160 payoutAdapter)
    {
        var contract = ContractManagement.GetContract(payoutAdapter);
        ExecutionEngine.Assert(contract != null, "payout adapter contract not found");
        return contract!.UpdateCounter;
    }

    private static ushort ReadPinnedPayoutAdapterUpdateCounter(
        uint externalChainId,
        UInt160 foreignAsset)
    {
        var raw = Storage.Get(PayoutAdapterUpdateCounterKey(externalChainId, foreignAsset));
        ExecutionEngine.Assert(raw != null, "payout adapter update counter not pinned");
        return ReadUInt16(AssertUpdateCounterBytes((byte[])raw!), 0);
    }

    private static void AssertPayoutAdapterIntegrity(
        UInt160 payoutAdapter,
        ushort expectedUpdateCounter)
    {
        ExecutionEngine.Assert(
            GetPayoutAdapterUpdateCounter(payoutAdapter) == expectedUpdateCounter,
            "payout adapter was upgraded in place — configure a new deployment");
        AssertPayoutAdapterVersion(payoutAdapter);
    }

    private static void AssertForeignChainId(uint externalChainId)
    {
        ExecutionEngine.Assert(
            (externalChainId & 0xFF000000U) == 0xE0000000U,
            "externalChainId must use the 0xE0_xx_xx_xx foreign-namespace prefix");
    }

    private static byte[] AssetRouteKey(uint externalChainId, UInt160 foreignAsset)
    {
        var key = new byte[1 + 4 + 20];
        key[0] = PrefixAssetRoute;
        key[1] = (byte)externalChainId;
        key[2] = (byte)(externalChainId >> 8);
        key[3] = (byte)(externalChainId >> 16);
        key[4] = (byte)(externalChainId >> 24);
        WriteUInt160(key, 5, foreignAsset);
        return key;
    }

    private static byte[] ReverseAssetRouteKey(uint externalChainId, UInt160 neoAsset)
    {
        var key = new byte[1 + 4 + 20];
        key[0] = PrefixReverseAssetRoute;
        WriteUInt32(key, 1, externalChainId);
        WriteUInt160(key, 5, neoAsset);
        return key;
    }

    private static byte[] PayoutAdapterUpdateCounterKey(
        uint externalChainId,
        UInt160 foreignAsset)
    {
        var key = new byte[1 + 4 + 20];
        key[0] = PrefixPayoutAdapterUpdateCounter;
        WriteUInt32(key, 1, externalChainId);
        WriteUInt160(key, 5, foreignAsset);
        return key;
    }

    private static byte[] GovernanceProposalKey(ulong proposalId)
    {
        var key = new byte[1 + 8];
        key[0] = PrefixConsumedGovernanceProposal;
        key[1] = (byte)proposalId;
        key[2] = (byte)(proposalId >> 8);
        key[3] = (byte)(proposalId >> 16);
        key[4] = (byte)(proposalId >> 24);
        key[5] = (byte)(proposalId >> 32);
        key[6] = (byte)(proposalId >> 40);
        key[7] = (byte)(proposalId >> 48);
        key[8] = (byte)(proposalId >> 56);
        return key;
    }

    private static uint ReadUInt32(byte[] data, int offset)
    {
        return (uint)data[offset]
            | ((uint)data[offset + 1] << 8)
            | ((uint)data[offset + 2] << 16)
            | ((uint)data[offset + 3] << 24);
    }

    private static ushort ReadUInt16(byte[] data, int offset)
    {
        return (ushort)(data[offset] | (data[offset + 1] << 8));
    }

    private static UInt160 ReadUInt160(byte[] data, int offset)
    {
        var value = new byte[20];
        for (var i = 0; i < 20; i++) value[i] = data[offset + i];
        return (UInt160)value;
    }

    private static UInt256 ReadUInt256(byte[] data, int offset)
    {
        var value = new byte[32];
        for (var i = 0; i < 32; i++) value[i] = data[offset + i];
        return (UInt256)value;
    }

    private static void WriteUInt160(byte[] data, int offset, UInt160 value)
    {
        var bytes = (byte[])value;
        for (var i = 0; i < 20; i++) data[offset + i] = bytes[i];
    }

    private static void WriteUInt32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
        data[offset + 2] = (byte)(value >> 16);
        data[offset + 3] = (byte)(value >> 24);
    }

    private static void WriteUInt16(byte[] data, int offset, ushort value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
    }

    private static readonly byte[] ActionTagSetRegistry = new byte[]
    {
        (byte)'n', (byte)'e', (byte)'o', (byte)'4', (byte)'-',
        (byte)'g', (byte)'o', (byte)'v', (byte)':',
        (byte)'s', (byte)'e', (byte)'t', (byte)'E', (byte)'x', (byte)'t', (byte)'e', (byte)'r',
        (byte)'n', (byte)'a', (byte)'l', (byte)'B', (byte)'r', (byte)'i', (byte)'d', (byte)'g',
        (byte)'e', (byte)'R', (byte)'e', (byte)'g', (byte)'i', (byte)'s', (byte)'t', (byte)'r',
        (byte)'y', (byte)':', (byte)'v', (byte)'1'
    };

    private static readonly byte[] ActionTagConfigureAssetRoute = new byte[]
    {
        (byte)'n', (byte)'e', (byte)'o', (byte)'4', (byte)'-',
        (byte)'g', (byte)'o', (byte)'v', (byte)':',
        (byte)'c', (byte)'o', (byte)'n', (byte)'f', (byte)'i', (byte)'g', (byte)'u', (byte)'r',
        (byte)'e', (byte)'E', (byte)'x', (byte)'t', (byte)'e', (byte)'r', (byte)'n', (byte)'a',
        (byte)'l', (byte)'A', (byte)'s', (byte)'s', (byte)'e', (byte)'t', (byte)'R', (byte)'o',
        (byte)'u', (byte)'t', (byte)'e', (byte)':', (byte)'v', (byte)'1'
    };
}
