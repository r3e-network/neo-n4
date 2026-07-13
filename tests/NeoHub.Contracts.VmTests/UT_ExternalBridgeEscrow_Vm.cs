using System.Buffers.Binary;
using System.ComponentModel;
using System.Numerics;
using Moq;
using Neo;
using Neo.SmartContract.Testing;
using Neo.SmartContract.Testing.Exceptions;

namespace NeoHub.Contracts.VmTests;

/// <summary>Versioned payout-adapter ABI consumed by ExternalBridgeEscrow.</summary>
public abstract class MockExternalBridgePayoutAdapter(SmartContractInitialize initialize) : SmartContract(initialize)
{
    [DisplayName("payoutVersion")]
    public abstract BigInteger? PayoutVersion();

    [DisplayName("payout")]
    public abstract bool? Payout(
        BigInteger? externalChainId,
        BigInteger? neoChainId,
        BigInteger? nonce,
        UInt160? foreignAsset,
        UInt160? neoAsset,
        UInt160? recipient,
        BigInteger? amount,
        BigInteger? deadlineUnixSeconds,
        UInt256? sourceTxRef,
        byte[]? messageBytes);
}

/// <summary>Governance proposal checks consumed by the escrow's production-locked admin path.</summary>
public abstract class MockExternalBridgeEscrowGovernanceController(SmartContractInitialize initialize) : SmartContract(initialize)
{
    [DisplayName("isApprovedAndTimelocked")]
    public abstract bool? IsApprovedAndTimelocked(BigInteger? proposalId);

    [DisplayName("matchesProposalPayload")]
    public abstract bool? MatchesProposalPayload(BigInteger? proposalId, byte[]? expectedAction);
}

/// <summary>
/// Minimal ExternalBridgeRegistry surface so the escrow's inbound verification hook can be mocked.
/// Receive() dispatches verification through <c>registry.verifyInbound(externalChainId, messageBytes,
/// proofBytes)</c>; we register a mock at a hash and have it return accept/reject.
/// </summary>
public abstract class Mock_ExternalBridgeEscrow_Registry(SmartContractInitialize initialize) : SmartContract(initialize)
{
    [DisplayName("verifyInbound")]
    public abstract bool? VerifyInbound(BigInteger? externalChainId, byte[]? messageBytes, byte[]? proofBytes);
}

/// <summary>
/// VM-level tests for NeoHub.ExternalBridgeEscrow — the L1-side escrow + dispatch for cross-foreign-
/// chain messages. With the NEP-17 asset and the ExternalBridgeRegistry verifier replaced by mocks,
/// these execute the deploy / send (outbound lock) / receive (inbound finalize) / NEP-17 hook paths
/// in a real NeoVM and pin the security-critical invariants:
///   * deploy validates owner + registry and permanently binds a Neo destination domain
///     (zero for L1, non-zero for L2),
///   * SetOwner / SetRegistry are owner-witness-gated (positive AND negative), and reject zero args,
///   * Send validates the 0xE0_xx_xx_xx foreign-namespace prefix, recipient/asset/amount, allocates a
///     per-chain monotonic nonce, accumulates the locked-balance ledger, and FAULT-reverts the
///     pre-transfer accounting when the asset transfer fails (no phantom lock),
///   * Receive validates namespace + message length + both signed chain domains + direction + deadline,
///     is replay-protected once-only per (externalChainId, neoChainId, nonce), is gated on the registry
///     verifier accepting, and only then marks the nonce consumed,
///   * the NEP-17 hook rejects unsolicited direct transfers (only Send-initiated transfers are accepted).
///
/// The NEP-17 asset is mocked with the shared <see cref="MockNep17"/> (defined in UT_SharedBridge_Vm.cs).
/// </summary>
[TestClass]
public class UT_ExternalBridgeEscrow_Vm
{
    private static readonly UInt160 AssetHash = UInt160.Parse("0x" + new string('a', 40));
    private static readonly UInt160 AssetHash2 = UInt160.Parse("0x" + new string('b', 40));
    private static readonly UInt160 RegistryAccept = UInt160.Parse("0x" + new string('5', 40));
    private static readonly UInt160 RegistryReject = UInt160.Parse("0x" + new string('6', 40));
    private static readonly UInt160 Recipient = UInt160.Parse("0x" + new string('c', 40));
    private static readonly UInt160 Stranger = UInt160.Parse("0x" + new string('d', 40));
    private static readonly UInt160 ForeignAsset = UInt160.Parse("0x" + new string('e', 40));
    private static readonly UInt160 ForeignAsset2 = UInt160.Parse("0x" + new string('f', 40));
    private static readonly UInt160 AdapterReject = UInt160.Parse("0x" + new string('1', 40));
    private static readonly UInt160 AdapterAccept = UInt160.Parse("0x" + new string('2', 40));
    private static readonly UInt160 GovernanceController = UInt160.Parse("0x" + new string('3', 40));

    /// <summary>A valid foreign-namespace chain id (must match the 0xE0_xx_xx_xx prefix mask).</summary>
    private const uint ChainA = 0xE0000001;
    private const uint ChainB = 0xE0000002;
    private const uint NeoChainA = 1099;
    private const uint NeoChainB = 1100;
    /// <summary>A chain id that does NOT carry the foreign-namespace prefix — must be rejected.</summary>
    private const uint BadChain = 1001;

    /// <summary>Direction byte that Receive requires (ForeignToNeo).</summary>
    private const byte DirForeignToNeo = 2;
    /// <summary>Canonical ExternalCrossChainMessage minimum length asserted by Receive.</summary>
    private const int MsgLen = 102;

    /// <summary>Build a canonical inbound ExternalCrossChainMessage matching the offsets Receive parses:
    /// externalChainId@0 (4 LE), neoChainId@4 (4 LE), nonce@8 (8 LE), direction@16 (1B),
    /// deadline@57 (8 LE), messageType@97 (1B). The total length is exactly the minimum (102)
    /// Receive requires.</summary>
    private static byte[] InboundMessage(uint externalChainId, ulong nonce, byte direction,
        ulong deadlineUnixSeconds, byte messageType, uint neoChainId = NeoChainA)
    {
        var buf = new byte[MsgLen];
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0, 4), externalChainId);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4, 4), neoChainId);
        BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(8, 8), nonce);
        buf[16] = direction;
        BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(57, 8), deadlineUnixSeconds);
        buf[97] = messageType;
        return buf;
    }

    private static byte[] InboundAssetMessage(
        uint externalChainId,
        ulong nonce,
        UInt160 foreignAsset,
        UInt160 recipient,
        BigInteger amount,
        byte messageType = 0,
        byte[]? calldata = null,
        uint neoChainId = NeoChainA,
        ulong deadlineUnixSeconds = 0)
    {
        var amountBytes = amount.ToByteArray(isUnsigned: true, isBigEndian: false);
        calldata ??= Array.Empty<byte>();
        var payloadLength = 24 + amountBytes.Length + calldata.Length;
        var buf = new byte[MsgLen + payloadLength];
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0, 4), externalChainId);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4, 4), neoChainId);
        BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(8, 8), nonce);
        buf[16] = DirForeignToNeo;
        recipient.GetSpan().CopyTo(buf.AsSpan(37, 20));
        BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(57, 8), deadlineUnixSeconds);
        Enumerable.Range(0, 32).Select(i => (byte)(i + 1)).ToArray().CopyTo(buf, 65);
        buf[97] = messageType;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(98, 4), (uint)payloadLength);
        foreignAsset.GetSpan().CopyTo(buf.AsSpan(102, 20));
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(122, 4), (uint)amountBytes.Length);
        amountBytes.CopyTo(buf, 126);
        calldata.CopyTo(buf, 126 + amountBytes.Length);
        return buf;
    }

    private static byte[] AssetRouteStorageKey(uint externalChainId, UInt160 foreignAsset)
    {
        var key = new byte[25];
        key[0] = 0x05;
        BinaryPrimitives.WriteUInt32LittleEndian(key.AsSpan(1, 4), externalChainId);
        foreignAsset.GetSpan().CopyTo(key.AsSpan(5, 20));
        return key;
    }

    /// <summary>Register the shared MockNep17 transfer at <see cref="AssetHash"/> returning the given
    /// result (used by Send's lock transfer).</summary>
    private static void WireAsset(
        TestEngine engine,
        UInt160 hash,
        bool transferOk,
        Action<UInt160?, UInt160?, BigInteger?>? onTransfer = null,
        Func<bool>? transferResult = null)
    {
        engine.FromHash<MockNep17>(hash, m =>
            m.Setup(c => c.Transfer(It.IsAny<UInt160?>(), It.IsAny<UInt160?>(), It.IsAny<BigInteger?>(), It.IsAny<object?>()))
                .Returns((UInt160? from, UInt160? to, BigInteger? amount, object? _) =>
                {
                    onTransfer?.Invoke(from, to, amount);
                    return transferResult?.Invoke() ?? transferOk;
                }),
            checkExistence: false);
    }

    private static void WirePayoutAdapter(
        TestEngine engine,
        UInt160 hash,
        bool accept,
        Action? onPayout = null,
        byte version = 1,
        ushort updateCounter = 0)
    {
        WireAdapterContractState(engine, hash, updateCounter);
        engine.FromHash<MockExternalBridgePayoutAdapter>(hash, m =>
        {
            m.Setup(c => c.PayoutVersion()).Returns((BigInteger)version);
            m.Setup(c => c.Payout(
                    It.IsAny<BigInteger?>(), It.IsAny<BigInteger?>(), It.IsAny<BigInteger?>(),
                    It.IsAny<UInt160?>(), It.IsAny<UInt160?>(), It.IsAny<UInt160?>(),
                    It.IsAny<BigInteger?>(), It.IsAny<BigInteger?>(), It.IsAny<UInt256?>(),
                    It.IsAny<byte[]?>()))
                .Returns(() =>
                {
                    onPayout?.Invoke();
                    return accept;
                });
        },
            checkExistence: false);
    }

    private static void WireAdapterContractState(
        TestEngine engine,
        UInt160 hash,
        ushort updateCounter)
    {
        var state = new Neo.SmartContract.ContractState
        {
            Id = -1,
            UpdateCounter = updateCounter,
            Hash = hash,
            Nef = NeoHubExternalBridgeEscrow.Nef,
            Manifest = NeoHubExternalBridgeEscrow.Manifest,
        };
        engine.Storage.Snapshot.Add(
            new Neo.SmartContract.KeyBuilder(
                Neo.SmartContract.Native.NativeContract.ContractManagement.Id, 8).Add(hash),
            new Neo.SmartContract.StorageItem(state));
    }

    private static void SetAdapterUpdateCounter(
        TestEngine engine,
        UInt160 hash,
        ushort updateCounter)
    {
        var key = new Neo.SmartContract.KeyBuilder(
            Neo.SmartContract.Native.NativeContract.ContractManagement.Id, 8).Add(hash);
        var item = engine.Storage.Snapshot.GetAndChange(key)
            ?? throw new InvalidOperationException("mock adapter contract state is missing");
        item.GetInteroperable<Neo.SmartContract.ContractState>(false).UpdateCounter = updateCounter;
    }

    private static void WireGovernance(
        TestEngine engine,
        bool approved,
        bool payloadMatches,
        Action<byte[]?>? onPayload = null,
        Func<bool>? approvedResult = null,
        Func<bool>? payloadMatchesResult = null)
    {
        engine.FromHash<MockExternalBridgeEscrowGovernanceController>(GovernanceController, m =>
        {
            m.Setup(c => c.IsApprovedAndTimelocked(It.IsAny<BigInteger?>()))
                .Returns(() => approvedResult?.Invoke() ?? approved);
            m.Setup(c => c.MatchesProposalPayload(It.IsAny<BigInteger?>(), It.IsAny<byte[]?>()))
                .Returns((BigInteger? _, byte[]? expectedAction) =>
                {
                    onPayload?.Invoke(expectedAction);
                    return payloadMatchesResult?.Invoke() ?? payloadMatches;
                });
        }, checkExistence: false);
    }

    /// <summary>Register an ExternalBridgeRegistry mock whose verifyInbound returns <paramref name="accept"/>.</summary>
    private static void WireRegistry(TestEngine engine, UInt160 hash, bool accept, Action? onVerify = null)
    {
        engine.FromHash<Mock_ExternalBridgeEscrow_Registry>(hash, m =>
            m.Setup(c => c.VerifyInbound(It.IsAny<BigInteger?>(), It.IsAny<byte[]?>(), It.IsAny<byte[]?>()))
                .Returns(() =>
                {
                    onVerify?.Invoke();
                    return accept;
                }),
            checkExistence: false);
    }

    /// <summary>Deploy the escrow. owner/registry default to engine.Sender / RegistryAccept so the
    /// owner witness checks pass and inbound verification succeeds; pass explicit values to exercise
    /// the negative authorization / verifier-reject paths.</summary>
    private static NeoHubExternalBridgeEscrow Deploy(
        TestEngine engine,
        UInt160? owner = null,
        UInt160? registry = null,
        uint neoChainId = NeoChainA)
    {
        var o = owner ?? engine.Sender;
        var r = registry ?? RegistryAccept;
        return engine.Deploy<NeoHubExternalBridgeEscrow>(
            NeoHubExternalBridgeEscrow.Nef, NeoHubExternalBridgeEscrow.Manifest,
            new object[] { o, r, neoChainId });
    }

    // ---------------------------------------------------------------------------------------------
    // Deploy-time input validation
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void Deploy_WiresOwnerRegistryAndNeoChainId()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        Assert.AreEqual(engine.Sender, c.Owner, "owner must be the deploy arg");
        Assert.AreEqual(RegistryAccept, c.Registry, "registry must be the deploy arg");
        Assert.AreEqual((BigInteger)NeoChainA, c.NeoChainId,
            "neoChainId must be permanently bound from the deploy arg");
    }

    [TestMethod]
    public void Deploy_RejectsZeroOwner()
    {
        var engine = new TestEngine(true);
        Assert.ThrowsExactly<TestException>(() => engine.Deploy<NeoHubExternalBridgeEscrow>(
            NeoHubExternalBridgeEscrow.Nef, NeoHubExternalBridgeEscrow.Manifest,
            new object[] { UInt160.Zero, RegistryAccept, NeoChainA }),
            "a zero owner must be rejected at deploy");
    }

    [TestMethod]
    public void Deploy_RejectsZeroRegistry()
    {
        var engine = new TestEngine(true);
        Assert.ThrowsExactly<TestException>(() => engine.Deploy<NeoHubExternalBridgeEscrow>(
            NeoHubExternalBridgeEscrow.Nef, NeoHubExternalBridgeEscrow.Manifest,
            new object[] { engine.Sender, UInt160.Zero, NeoChainA }),
            "a zero registry must be rejected at deploy");
    }

    [TestMethod]
    public void Deploy_AllowsZeroNeoChainIdForExplicitL1Domain()
    {
        var engine = new TestEngine(true);
        var c = engine.Deploy<NeoHubExternalBridgeEscrow>(
            NeoHubExternalBridgeEscrow.Nef, NeoHubExternalBridgeEscrow.Manifest,
            new object[] { engine.Sender, RegistryAccept, 0u });

        Assert.AreEqual(BigInteger.Zero, c.NeoChainId,
            "zero is the canonical immutable domain identifier for Neo L1");
    }

    // ---------------------------------------------------------------------------------------------
    // Owner-gated governance: SetOwner / SetRegistry
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void SetOwner_ByOwner_RotatesOwnership()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine); // owner == engine.Sender (auto-witnessed)

        c.Owner = Stranger;
        Assert.AreEqual(Stranger, c.Owner, "owner rotation must persist");

        // The old owner (engine.Sender) no longer carries the owner witness, so it can't rotate back.
        Assert.ThrowsExactly<TestException>(() => { c.Owner = engine.Sender; },
            "after rotation the previous owner is no longer authorized");
    }

    [TestMethod]
    public void SetOwner_ByNonOwner_Faults()
    {
        var engine = new TestEngine(true);
        // Owner is a different account than the test signer -> the owner gate must reject.
        var c = Deploy(engine, owner: Stranger);

        Assert.ThrowsExactly<TestException>(() => { c.Owner = engine.Sender; },
            "SetOwner is owner-witness-gated");
        Assert.AreEqual(Stranger, c.Owner, "rejected SetOwner must not change state");
    }

    [TestMethod]
    public void SetOwner_RejectsZeroNewOwner()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        Assert.ThrowsExactly<TestException>(() => { c.Owner = UInt160.Zero; },
            "a zero new owner must be rejected");
    }

    [TestMethod]
    public void SetRegistry_ByOwner_RotatesPointer()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        c.Registry = RegistryReject;
        Assert.AreEqual(RegistryReject, c.Registry, "registry rotation must persist");
    }

    [TestMethod]
    public void SetRegistry_ByNonOwner_Faults()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine, owner: Stranger);

        Assert.ThrowsExactly<TestException>(() => { c.Registry = RegistryReject; },
            "SetRegistry is owner-witness-gated");
        Assert.AreEqual(RegistryAccept, c.Registry, "rejected SetRegistry must not change state");
    }

    [TestMethod]
    public void SetRegistry_RejectsZeroRegistry()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        Assert.ThrowsExactly<TestException>(() => { c.Registry = UInt160.Zero; },
            "a zero registry must be rejected");
    }

    // ---------------------------------------------------------------------------------------------
    // Send: outbound lock — namespace + arg validation, nonce allocation, locked-balance accounting
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void Send_AllocatesPerChainMonotonicNonce_AndAccumulatesLockedBalance()
    {
        var engine = new TestEngine(true);
        WireAsset(engine, AssetHash, transferOk: true);
        var c = Deploy(engine);

        Assert.AreEqual((BigInteger)0, c.GetLastOutboundNonce(ChainA), "no nonce assigned yet");
        Assert.AreEqual((BigInteger)0, c.GetLockedBalance(ChainA, AssetHash), "no balance locked yet");

        Assert.AreEqual((BigInteger)1, c.Send(ChainA, Recipient, AssetHash, 1000, new byte[] { 0xAB }, 0)!,
            "first outbound nonce is 1");
        Assert.AreEqual((BigInteger)2, c.Send(ChainA, Recipient, AssetHash, 500, new byte[] { 0xCD }, 0)!,
            "nonce is monotonic per chain");

        Assert.AreEqual((BigInteger)2, c.GetLastOutboundNonce(ChainA), "last nonce reflects allocations");
        Assert.AreEqual((BigInteger)1500, c.GetLockedBalance(ChainA, AssetHash),
            "locked balance accumulates across sends");

        // A different chain starts its own nonce sequence at 1 and has an isolated locked-balance row.
        Assert.AreEqual((BigInteger)1, c.Send(ChainB, Recipient, AssetHash, 700, new byte[] { 0xEF }, 0)!,
            "per-chain nonce restarts for a new chain");
        Assert.AreEqual((BigInteger)700, c.GetLockedBalance(ChainB, AssetHash), "chain B ledger is isolated");
        Assert.AreEqual((BigInteger)1500, c.GetLockedBalance(ChainA, AssetHash), "chain A ledger untouched");

        // A different asset on the same chain is a separate ledger row.
        Assert.AreEqual((BigInteger)0, c.GetLockedBalance(ChainA, AssetHash2),
            "a different asset has its own locked-balance row");
    }

    [TestMethod]
    public void Send_RejectsNonForeignNamespaceChainId()
    {
        var engine = new TestEngine(true);
        WireAsset(engine, AssetHash, transferOk: true);
        var c = Deploy(engine);

        Assert.ThrowsExactly<TestException>(() => c.Send(BadChain, Recipient, AssetHash, 100, new byte[] { 0x01 }, 0),
            "a chain id without the 0xE0 foreign-namespace prefix must be rejected");
    }

    [TestMethod]
    public void Send_RejectsZeroRecipientAssetAndNonPositiveAmount()
    {
        var engine = new TestEngine(true);
        WireAsset(engine, AssetHash, transferOk: true);
        var c = Deploy(engine);

        Assert.ThrowsExactly<TestException>(() => c.Send(ChainA, UInt160.Zero, AssetHash, 100, new byte[] { 0x01 }, 0),
            "a zero recipient must be rejected");
        Assert.ThrowsExactly<TestException>(() => c.Send(ChainA, Recipient, UInt160.Zero, 100, new byte[] { 0x01 }, 0),
            "a zero asset must be rejected");
        Assert.ThrowsExactly<TestException>(() => c.Send(ChainA, Recipient, AssetHash, 0, new byte[] { 0x01 }, 0),
            "a zero amount must be rejected");
        Assert.ThrowsExactly<TestException>(() => c.Send(ChainA, Recipient, AssetHash, -1, new byte[] { 0x01 }, 0),
            "a negative amount must be rejected");

        // None of the rejected calls allocated a nonce or locked anything.
        Assert.AreEqual((BigInteger)0, c.GetLastOutboundNonce(ChainA), "rejected sends allocate no nonce");
        Assert.AreEqual((BigInteger)0, c.GetLockedBalance(ChainA, AssetHash), "rejected sends lock nothing");
    }

    [TestMethod]
    public void Send_FailedAssetTransfer_FaultsAndRevertsAccounting()
    {
        // The asset transfer returns false (e.g. paused/frozen). Send increments the locked-balance
        // ledger and allocates the nonce BEFORE the lock transfer, so a false return MUST FAULT and
        // the NeoVM must revert both writes — otherwise a phantom lock / consumed nonce would persist.
        var engine = new TestEngine(true);
        WireAsset(engine, AssetHash, transferOk: false);
        var c = Deploy(engine);

        Assert.ThrowsExactly<TestException>(() => c.Send(ChainA, Recipient, AssetHash, 1000, new byte[] { 0x01 }, 0),
            "a failed lock transfer must FAULT the send");
        Assert.AreEqual((BigInteger)0, c.GetLockedBalance(ChainA, AssetHash),
            "FAULT must revert the pre-transfer locked-balance credit — no phantom lock");
        Assert.AreEqual((BigInteger)0, c.GetLastOutboundNonce(ChainA),
            "FAULT must revert the pre-transfer nonce allocation");
    }

    // ---------------------------------------------------------------------------------------------
    // Receive: inbound finalize — domain/format validation, replay protection, verifier gating
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void Receive_VerifiedMessage_MarksConsumed_OnlyOnce()
    {
        var engine = new TestEngine(true);
        WireRegistry(engine, RegistryAccept, accept: true);
        var c = Deploy(engine); // registry == RegistryAccept

        var msg = InboundMessage(ChainA, nonce: 7, direction: DirForeignToNeo, deadlineUnixSeconds: 0, messageType: 1);

        Assert.IsFalse(c.IsInboundConsumed(ChainA, 7)!.Value, "nonce not consumed yet");
        c.Receive(ChainA, msg, new byte[] { 0x99 });
        Assert.IsTrue(c.IsInboundConsumed(ChainA, 7)!.Value, "a verified inbound must mark the nonce consumed");

        // Replay of the same (chainId, nonce) must fault even though the verifier would still accept.
        Assert.ThrowsExactly<TestException>(() => c.Receive(ChainA, msg, new byte[] { 0x99 }),
            "a consumed inbound nonce must be replay-protected");
    }

    [TestMethod]
    public void Receive_TwoL2sSharingCommittee_RejectsCrossL2ReplayBeforeVerifierAndNonceConsumption()
    {
        var l2AEngine = new TestEngine(true);
        var l2BEngine = new TestEngine(true);
        var l2AVerifierCalls = 0;
        var l2BVerifierCalls = 0;

        WireRegistry(l2AEngine, RegistryAccept, accept: true, () => l2AVerifierCalls++);
        WireRegistry(l2BEngine, RegistryAccept, accept: true, () => l2BVerifierCalls++);
        var l2AEscrow = Deploy(l2AEngine, neoChainId: NeoChainA);
        var l2BEscrow = Deploy(l2BEngine, neoChainId: NeoChainB);

        const ulong sharedNonce = 41;
        var messageForL2A = InboundMessage(
            ChainA, sharedNonce, DirForeignToNeo, deadlineUnixSeconds: 0, messageType: 1,
            neoChainId: NeoChainA);

        l2AEscrow.Receive(ChainA, messageForL2A, new byte[] { 0x01 });
        Assert.AreEqual(1, l2AVerifierCalls, "the intended L2 dispatches to the shared committee once");
        Assert.IsTrue(l2AEscrow.IsInboundConsumed(ChainA, sharedNonce)!.Value,
            "the intended L2 consumes the verified nonce");

        Assert.ThrowsExactly<TestException>(
            () => l2BEscrow.Receive(ChainA, messageForL2A, new byte[] { 0x01 }),
            "a committee-approved message signed for L2-A must not be accepted by L2-B");
        Assert.AreEqual(0, l2BVerifierCalls,
            "the deployment-domain mismatch must fault before verifier dispatch");
        Assert.IsFalse(l2BEscrow.IsInboundConsumed(ChainA, sharedNonce)!.Value,
            "the rejected cross-L2 replay must not consume L2-B's nonce");

        var messageForL2B = InboundMessage(
            ChainA, sharedNonce, DirForeignToNeo, deadlineUnixSeconds: 0, messageType: 1,
            neoChainId: NeoChainB);
        l2BEscrow.Receive(ChainA, messageForL2B, new byte[] { 0x01 });
        Assert.AreEqual(1, l2BVerifierCalls, "L2-B still accepts its own correctly scoped message");
        Assert.IsTrue(l2BEscrow.IsInboundConsumed(ChainA, sharedNonce)!.Value,
            "the mismatch rejection leaves the nonce available for the legitimate L2-B message");
    }

    [TestMethod]
    public void Receive_ReplayProtection_IsPerChainAndPerNonce()
    {
        var engine = new TestEngine(true);
        WireRegistry(engine, RegistryAccept, accept: true);
        var c = Deploy(engine);

        c.Receive(ChainA, InboundMessage(ChainA, 1, DirForeignToNeo, 0, 1), new byte[] { 0x01 });
        Assert.IsTrue(c.IsInboundConsumed(ChainA, 1)!.Value);

        // Same nonce on a different chain is an independent slot and must still be finalizable.
        Assert.IsFalse(c.IsInboundConsumed(ChainB, 1)!.Value, "nonce 1 on chain B is independent of chain A");
        c.Receive(ChainB, InboundMessage(ChainB, 1, DirForeignToNeo, 0, 1), new byte[] { 0x01 });
        Assert.IsTrue(c.IsInboundConsumed(ChainB, 1)!.Value);

        // A different nonce on chain A is also independent.
        Assert.IsFalse(c.IsInboundConsumed(ChainA, 2)!.Value);
    }

    [TestMethod]
    public void Receive_RejectsNonForeignNamespaceChainId()
    {
        var engine = new TestEngine(true);
        WireRegistry(engine, RegistryAccept, accept: true);
        var c = Deploy(engine);

        var msg = InboundMessage(BadChain, 1, DirForeignToNeo, 0, 1);
        Assert.ThrowsExactly<TestException>(() => c.Receive(BadChain, msg, new byte[] { 0x01 }),
            "a chain id outside the foreign namespace must be rejected");
    }

    [TestMethod]
    public void Receive_RejectsShortMessage()
    {
        var engine = new TestEngine(true);
        WireRegistry(engine, RegistryAccept, accept: true);
        var c = Deploy(engine);

        Assert.ThrowsExactly<TestException>(() => c.Receive(ChainA, new byte[101], new byte[] { 0x01 }),
            "a message shorter than the canonical layout must be rejected");
    }

    [TestMethod]
    public void Receive_RejectsSignedChainIdDomainMismatch()
    {
        // The chainId argument must match the chainId baked into the signed message body — otherwise a
        // message signed for chain A could be presented under chain B's domain.
        var engine = new TestEngine(true);
        WireRegistry(engine, RegistryAccept, accept: true);
        var c = Deploy(engine);

        var msgForB = InboundMessage(ChainB, 1, DirForeignToNeo, 0, 1);
        Assert.ThrowsExactly<TestException>(() => c.Receive(ChainA, msgForB, new byte[] { 0x01 }),
            "the chainId arg must match the signed message domain");
        Assert.IsFalse(c.IsInboundConsumed(ChainA, 1)!.Value, "a rejected inbound must not mark consumed");
    }

    [TestMethod]
    public void Receive_RejectsWrongL1DomainBeforeVerifierAndNonceConsumption()
    {
        var engine = new TestEngine(true);
        var verifierCalls = 0;
        WireRegistry(engine, RegistryAccept, accept: true, () => verifierCalls++);
        var c = Deploy(engine, neoChainId: NeoChainA);

        const ulong nonce = 19;
        var zeroDomainMessage = InboundMessage(
            ChainA, nonce, DirForeignToNeo, 0, 1, neoChainId: 0);

        Assert.ThrowsExactly<TestException>(
            () => c.Receive(ChainA, zeroDomainMessage, new byte[] { 0x01 }),
            "an L1-targeted message must not execute in an L2-bound escrow");
        Assert.AreEqual(0, verifierCalls, "domain rejection must happen before verifier dispatch");
        Assert.IsFalse(c.IsInboundConsumed(ChainA, nonce)!.Value,
            "domain rejection must leave the legitimate nonce available");
    }

    [TestMethod]
    public void Receive_AcceptsExplicitL1DomainAndConsumesItsOwnReplayKey()
    {
        var engine = new TestEngine(true);
        WireRegistry(engine, RegistryAccept, accept: true);
        var c = Deploy(engine, neoChainId: 0);

        const ulong nonce = 20;
        c.Receive(ChainA,
            InboundMessage(ChainA, nonce, DirForeignToNeo, 0, 1, neoChainId: 0),
            new byte[] { 0x01 });

        Assert.IsTrue(c.IsInboundConsumed(ChainA, nonce)!.Value,
            "the explicit L1 domain must have normal replay protection");
    }

    [TestMethod]
    public void Receive_RejectsWrongDirection()
    {
        var engine = new TestEngine(true);
        WireRegistry(engine, RegistryAccept, accept: true);
        var c = Deploy(engine);

        // direction 1 (NeoToForeign) is not a valid inbound direction.
        var msg = InboundMessage(ChainA, 1, direction: 1, deadlineUnixSeconds: 0, messageType: 1);
        Assert.ThrowsExactly<TestException>(() => c.Receive(ChainA, msg, new byte[] { 0x01 }),
            "only direction 2 (ForeignToNeo) may be received");
    }

    [TestMethod]
    public void Receive_RejectsExpiredDeadline()
    {
        var engine = new TestEngine(true);
        WireRegistry(engine, RegistryAccept, accept: true);
        var c = Deploy(engine);

        // A deadline strictly in the past (relative to Runtime.Time) must be rejected. Runtime.Time
        // is in ms; the contract compares Runtime.Time/1000 <= deadline. A deadline of 1 second is far
        // below the genesis block time, so it is expired.
        var expired = InboundMessage(ChainA, 1, DirForeignToNeo, deadlineUnixSeconds: 1, messageType: 1);
        Assert.ThrowsExactly<TestException>(() => c.Receive(ChainA, expired, new byte[] { 0x01 }),
            "an expired external-bridge message must be rejected");
        Assert.IsFalse(c.IsInboundConsumed(ChainA, 1)!.Value, "an expired inbound must not mark consumed");
    }

    [TestMethod]
    public void Receive_RejectsWhenRegistryVerifierRejects()
    {
        var engine = new TestEngine(true);
        WireRegistry(engine, RegistryReject, accept: false);
        var c = Deploy(engine, registry: RegistryReject); // verifier returns false

        var msg = InboundMessage(ChainA, 1, DirForeignToNeo, 0, 1);
        Assert.ThrowsExactly<TestException>(() => c.Receive(ChainA, msg, new byte[] { 0x01 }),
            "Receive must fault when the registry verifier rejects the proof");
        Assert.IsFalse(c.IsInboundConsumed(ChainA, 1)!.Value,
            "a verifier-rejected inbound must not mark the nonce consumed");
    }

    [TestMethod]
    public void Receive_RegistryRotation_FlipsVerificationOutcome()
    {
        // Pin that Receive actually dispatches through the *currently wired* registry: starting with a
        // rejecting verifier the inbound faults; rotating the pointer to an accepting verifier lets the
        // same message finalize. Distinct hashes are used because a hash can be mocked only once.
        var engine = new TestEngine(true);
        WireRegistry(engine, RegistryReject, accept: false);
        WireRegistry(engine, RegistryAccept, accept: true);
        var c = Deploy(engine, registry: RegistryReject);
        var deploymentDomain = c.NeoChainId;

        var msg = InboundMessage(ChainA, 5, DirForeignToNeo, 0, 1);
        Assert.ThrowsExactly<TestException>(() => c.Receive(ChainA, msg, new byte[] { 0x01 }),
            "the rejecting registry must block finalization");
        Assert.IsFalse(c.IsInboundConsumed(ChainA, 5)!.Value);

        // Owner rotates the registry to the accepting verifier; the same inbound now finalizes.
        c.Registry = RegistryAccept;
        Assert.AreEqual(deploymentDomain, c.NeoChainId,
            "verifier upgrades must preserve the immutable Neo L2 domain binding");
        c.Receive(ChainA, msg, new byte[] { 0x01 });
        Assert.IsTrue(c.IsInboundConsumed(ChainA, 5)!.Value,
            "after rotating to an accepting verifier the inbound finalizes");
    }

    // ---------------------------------------------------------------------------------------------
    // Inbound asset payout — governed routes, direct escrow release, and adapter upgrades
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void AssetRoute_OwnerCanConfigureUpgradeAdapterAndDisableWithoutRemappingAsset()
    {
        var engine = new TestEngine(true);
        WirePayoutAdapter(engine, AdapterAccept, accept: true);
        var c = Deploy(engine, neoChainId: 0);

        c.SetAssetRoute(ChainA, ForeignAsset, AssetHash, UInt160.Zero);
        Assert.AreEqual(AssetHash, c.GetRoutedNeoAsset(ChainA, ForeignAsset));
        Assert.AreEqual(ForeignAsset, c.GetRoutedForeignAsset(ChainA, AssetHash));
        Assert.AreEqual(UInt160.Zero, c.GetPayoutAdapter(ChainA, ForeignAsset));
        Assert.IsTrue(c.IsAssetRouteActive(ChainA, ForeignAsset)!.Value);

        c.SetAssetRoute(ChainA, ForeignAsset, AssetHash, AdapterAccept);
        Assert.AreEqual(AssetHash, c.GetRoutedNeoAsset(ChainA, ForeignAsset));
        Assert.AreEqual(AdapterAccept, c.GetPayoutAdapter(ChainA, ForeignAsset));
        Assert.AreEqual((BigInteger)0,
            c.GetPayoutAdapterUpdateCounter(ChainA, ForeignAsset),
            "a newly deployed adapter must pin update counter zero");

        Assert.ThrowsExactly<TestException>(
            () => c.SetAssetRoute(ChainA, ForeignAsset, AssetHash2, AdapterAccept),
            "a signed foreign asset must never be remapped to a different Neo asset");

        c.SetAssetRouteActive(ChainA, ForeignAsset, false);
        Assert.IsFalse(c.IsAssetRouteActive(ChainA, ForeignAsset)!.Value);
        Assert.AreEqual(AssetHash, c.GetRoutedNeoAsset(ChainA, ForeignAsset),
            "disabling preserves the auditable route data");
    }

    [TestMethod]
    public void AssetRoute_RejectsReverseMappingCollisionAndUnsupportedAdapterVersion()
    {
        var engine = new TestEngine(true);
        WirePayoutAdapter(engine, AdapterReject, accept: true, version: 2);
        var c = Deploy(engine, neoChainId: 0);
        c.SetAssetRoute(ChainA, ForeignAsset, AssetHash, UInt160.Zero);

        Assert.ThrowsExactly<TestException>(
            () => c.SetAssetRoute(ChainA, ForeignAsset2, AssetHash, UInt160.Zero),
            "one Neo asset cannot collateralize two foreign assets in the same source domain");
        Assert.AreEqual(UInt160.Zero, c.GetRoutedNeoAsset(ChainA, ForeignAsset2));

        Assert.ThrowsExactly<TestException>(
            () => c.SetAssetRoute(ChainA, ForeignAsset, AssetHash, AdapterReject),
            "route configuration must reject an adapter that does not implement payout ABI v1");
        Assert.AreEqual(UInt160.Zero, c.GetPayoutAdapter(ChainA, ForeignAsset));
    }

    [TestMethod]
    public void AssetRoute_L2DomainRequiresPayoutAdapterAndRejectsDirectLiquidity()
    {
        var engine = new TestEngine(true);
        WirePayoutAdapter(engine, AdapterAccept, accept: true);
        WireAsset(engine, AssetHash, transferOk: true);
        var c = Deploy(engine, neoChainId: NeoChainA);

        Assert.ThrowsExactly<TestException>(
            () => c.SetAssetRoute(ChainA, ForeignAsset, AssetHash, UInt160.Zero),
            "an L2-targeted inbound must not release Neo L1 escrow directly");
        Assert.ThrowsExactly<TestException>(
            () => c.FundLiquidity(ChainA, AssetHash, 1),
            "an L2-targeted escrow must not accept direct-release liquidity");

        c.SetAssetRoute(ChainA, ForeignAsset, AssetHash, AdapterAccept);
        Assert.AreEqual(AdapterAccept, c.GetPayoutAdapter(ChainA, ForeignAsset),
            "an L2 route must install a versioned target-credit adapter");
    }

    [TestMethod]
    public void FundLiquidity_RequiresActiveDirectRouteAndMatchingAsset()
    {
        var engine = new TestEngine(true);
        WireAsset(engine, AssetHash, transferOk: true);
        WirePayoutAdapter(engine, AdapterAccept, accept: true);
        var c = Deploy(engine, neoChainId: 0);

        Assert.ThrowsExactly<TestException>(
            () => c.FundLiquidity(ChainA, AssetHash, 1),
            "funding an unmapped token would create permanently orphaned escrow liquidity");

        c.SetAssetRoute(ChainA, ForeignAsset, AssetHash, AdapterAccept);
        Assert.ThrowsExactly<TestException>(
            () => c.FundLiquidity(ChainA, AssetHash, 1),
            "adapter routes own their credit accounting and must not use the direct pool");

        c.SetAssetRoute(ChainA, ForeignAsset, AssetHash, UInt160.Zero);
        c.SetAssetRouteActive(ChainA, ForeignAsset, false);
        Assert.ThrowsExactly<TestException>(
            () => c.FundLiquidity(ChainA, AssetHash, 1),
            "a frozen route must not accept new liquidity");

        c.SetAssetRouteActive(ChainA, ForeignAsset, true);
        c.FundLiquidity(ChainA, AssetHash, 1);
        Assert.AreEqual(BigInteger.One, c.GetLockedBalance(ChainA, AssetHash));

        c.Owner = Stranger;
        Assert.ThrowsExactly<TestException>(
            () => c.FundLiquidity(ChainA, AssetHash, 1),
            "only the current owner may fund the direct-release pool");
        Assert.AreEqual(BigInteger.One, c.GetLockedBalance(ChainA, AssetHash),
            "an unauthorized funding attempt must not change accounting");
    }

    [TestMethod]
    public void AssetRoute_NonOwnerCannotConfigureOrDisable()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine, owner: Stranger);

        Assert.ThrowsExactly<TestException>(
            () => c.SetAssetRoute(ChainA, ForeignAsset, AssetHash, UInt160.Zero),
            "asset-route configuration is governance-owned");
        Assert.IsFalse(c.IsAssetRouteActive(ChainA, ForeignAsset)!.Value);
    }

    [TestMethod]
    public void GovernanceLock_DisablesDirectAdminAndProposalPathIsBoundAndReplayProtected()
    {
        var engine = new TestEngine(true);
        byte[]? observedAction = null;
        WireGovernance(engine, approved: true, payloadMatches: true,
            action => observedAction = action);
        WirePayoutAdapter(engine, AdapterAccept, accept: true);
        var c = Deploy(engine);

        c.GovernanceController = GovernanceController;
        c.LockGovernance();
        c.LockGovernance();
        Assert.IsTrue(c.IsGovernanceLocked!.Value);

        Assert.ThrowsExactly<TestException>(
            () => c.SetAssetRoute(ChainA, ForeignAsset, AssetHash, UInt160.Zero),
            "the production lock must remove the direct owner route-upgrade path");
        Assert.ThrowsExactly<TestException>(
            () => c.Registry = RegistryReject,
            "the production lock must remove the direct owner verifier-registry rotation path");

        c.ConfigureAssetRouteViaProposal(
            ChainA, ForeignAsset, AssetHash, AdapterAccept, true, 42);
        Assert.AreEqual(AdapterAccept, c.GetPayoutAdapter(ChainA, ForeignAsset));
        Assert.IsNotNull(observedAction);
        var boundRouteAction = observedAction!;
        CollectionAssert.AreEqual(
            c.BuildConfigureAssetRouteAction(
                ChainA, ForeignAsset, AssetHash, AdapterAccept, true),
            boundRouteAction,
            "governance must approve the exact chain, asset mapping, adapter, and active flag bytes");
        var routeTagLength = System.Text.Encoding.ASCII.GetByteCount(
            "neo4-gov:configureExternalAssetRoute:v1");
        Assert.IsTrue(boundRouteAction.AsSpan(routeTagLength, 20).SequenceEqual(c.Hash.GetSpan()),
            "the proposal action must bind the exact escrow contract instance");
        Assert.AreEqual(NeoChainA,
            BinaryPrimitives.ReadUInt32LittleEndian(
                boundRouteAction.AsSpan(routeTagLength + 20, 4)),
            "the proposal action must bind the immutable destination L2 domain");

        Assert.ThrowsExactly<TestException>(
            () => c.ConfigureAssetRouteViaProposal(
                ChainA, ForeignAsset, AssetHash, UInt160.Zero, true, 42),
            "one proposal id cannot be replayed to install another payout adapter");
        Assert.AreEqual(AdapterAccept, c.GetPayoutAdapter(ChainA, ForeignAsset));

        c.SetRegistryViaProposal(RegistryReject, 43);
        Assert.AreEqual(RegistryReject, c.Registry);
        CollectionAssert.AreEqual(c.BuildSetRegistryAction(RegistryReject), observedAction!,
            "registry rotation approval must bind the exact escrow instance and target domain");
        Assert.ThrowsExactly<TestException>(
            () => c.SetRegistryViaProposal(RegistryAccept, 43),
            "registry proposal replay must also fail closed");
    }

    [TestMethod]
    public void GovernanceProposal_UnapprovedOrPayloadMismatchDoesNotChangeRouteOrConsumeId()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine, neoChainId: 0);
        c.GovernanceController = GovernanceController;
        var approved = false;
        var payloadMatches = true;

        WireGovernance(engine, approved: false, payloadMatches: true,
            approvedResult: () => approved,
            payloadMatchesResult: () => payloadMatches);
        Assert.ThrowsExactly<TestException>(
            () => c.ConfigureAssetRouteViaProposal(
                ChainA, ForeignAsset, AssetHash, UInt160.Zero, true, 51));
        Assert.IsFalse(c.IsAssetRouteActive(ChainA, ForeignAsset)!.Value);

        approved = true;
        payloadMatches = false;
        Assert.ThrowsExactly<TestException>(
            () => c.ConfigureAssetRouteViaProposal(
                ChainA, ForeignAsset, AssetHash, UInt160.Zero, true, 51));
        Assert.IsFalse(c.IsAssetRouteActive(ChainA, ForeignAsset)!.Value);

        payloadMatches = true;
        c.ConfigureAssetRouteViaProposal(
            ChainA, ForeignAsset, AssetHash, UInt160.Zero, true, 51);
        Assert.IsTrue(c.IsAssetRouteActive(ChainA, ForeignAsset)!.Value,
            "failed proposal checks must not consume the proposal id needed by a corrected retry");
    }

    [TestMethod]
    public void Receive_AssetTransfer_DirectRoutePaysRecipientAndDebitsEscrow()
    {
        var engine = new TestEngine(true);
        WireRegistry(engine, RegistryAccept, accept: true);
        var transfers = new List<(UInt160? From, UInt160? To, BigInteger? Amount)>();
        WireAsset(engine, AssetHash, transferOk: true,
            (from, to, amount) => transfers.Add((from, to, amount)));
        var c = Deploy(engine, neoChainId: 0);
        c.SetAssetRoute(ChainA, ForeignAsset, AssetHash, UInt160.Zero);
        c.FundLiquidity(ChainA, AssetHash, 1_000);

        c.Receive(ChainA,
            InboundAssetMessage(ChainA, 71, ForeignAsset, Recipient, 400, neoChainId: 0),
            new byte[] { 0x01 });

        Assert.AreEqual((BigInteger)600, c.GetLockedBalance(ChainA, AssetHash),
            "successful direct payout must debit the same chain+asset pool");
        Assert.IsTrue(c.IsInboundConsumed(ChainA, 71)!.Value);
        Assert.AreEqual(2, transfers.Count, "one funding transfer plus one payout transfer");
        Assert.AreEqual(Recipient, transfers[1].To);
        Assert.AreEqual((BigInteger)400, transfers[1].Amount);
    }

    [TestMethod]
    public void Receive_AssetTransfer_InsufficientEscrowRevertsConsumptionAndBalance()
    {
        var engine = new TestEngine(true);
        WireRegistry(engine, RegistryAccept, accept: true);
        WireAsset(engine, AssetHash, transferOk: true);
        var c = Deploy(engine, neoChainId: 0);
        c.SetAssetRoute(ChainA, ForeignAsset, AssetHash, UInt160.Zero);
        c.FundLiquidity(ChainA, AssetHash, 50);

        Assert.ThrowsExactly<TestException>(
            () => c.Receive(ChainA,
                InboundAssetMessage(ChainA, 72, ForeignAsset, Recipient, 51, neoChainId: 0),
                new byte[] { 0x01 }),
            "an inbound payout cannot draw more than the chain-specific escrow pool");

        Assert.AreEqual((BigInteger)50, c.GetLockedBalance(ChainA, AssetHash));
        Assert.IsFalse(c.IsInboundConsumed(ChainA, 72)!.Value,
            "failed payout must roll back the replay marker so a funded retry can succeed");
    }

    [TestMethod]
    public void Receive_AssetTransfer_FailedNep17PayoutRevertsConsumptionAndBalance()
    {
        var engine = new TestEngine(true);
        WireRegistry(engine, RegistryAccept, accept: true);
        var transferOk = true;
        WireAsset(engine, AssetHash, transferOk: true,
            transferResult: () => transferOk);
        var c = Deploy(engine, neoChainId: 0);
        c.SetAssetRoute(ChainA, ForeignAsset, AssetHash, UInt160.Zero);
        c.FundLiquidity(ChainA, AssetHash, 50);
        transferOk = false;

        Assert.ThrowsExactly<TestException>(
            () => c.Receive(ChainA,
                InboundAssetMessage(ChainA, 721, ForeignAsset, Recipient, 25, neoChainId: 0),
                new byte[] { 0x01 }),
            "a false NEP-17 transfer result must fault the whole inbound finalization");
        Assert.AreEqual((BigInteger)50, c.GetLockedBalance(ChainA, AssetHash),
            "failed payout must roll back the pre-call balance debit");
        Assert.IsFalse(c.IsInboundConsumed(ChainA, 721)!.Value,
            "failed payout must roll back the replay marker");
    }

    [TestMethod]
    public void Receive_AssetTransfer_RejectsWrongOrInactiveAssetMapping()
    {
        var engine = new TestEngine(true);
        WireRegistry(engine, RegistryAccept, accept: true);
        var c = Deploy(engine, neoChainId: 0);
        c.SetAssetRoute(ChainA, ForeignAsset, AssetHash, UInt160.Zero);

        Assert.ThrowsExactly<TestException>(
            () => c.Receive(ChainA,
                InboundAssetMessage(ChainA, 73, ForeignAsset2, Recipient, 1, neoChainId: 0),
                new byte[] { 0x01 }),
            "a signed foreign asset cannot be substituted into another route");
        Assert.IsFalse(c.IsInboundConsumed(ChainA, 73)!.Value);

        c.SetAssetRouteActive(ChainA, ForeignAsset, false);
        Assert.ThrowsExactly<TestException>(
            () => c.Receive(ChainA,
                InboundAssetMessage(ChainA, 74, ForeignAsset, Recipient, 1, neoChainId: 0),
                new byte[] { 0x01 }),
            "governance must be able to freeze a compromised asset route");
        Assert.IsFalse(c.IsInboundConsumed(ChainA, 74)!.Value);
    }

    [TestMethod]
    public void Receive_AssetAndCall_AdapterUpgradeRecoversWithoutConsumingFailedNonce()
    {
        var engine = new TestEngine(true);
        WireRegistry(engine, RegistryAccept, accept: true);
        var rejectCalls = 0;
        var acceptCalls = 0;
        WirePayoutAdapter(engine, AdapterReject, accept: false, () => rejectCalls++);
        WirePayoutAdapter(engine, AdapterAccept, accept: true, () => acceptCalls++);
        var c = Deploy(engine);
        var message = InboundAssetMessage(
            ChainA, 75, ForeignAsset, Recipient, 25,
            messageType: 2, calldata: new byte[] { 0xCA, 0xFE });

        c.SetAssetRoute(ChainA, ForeignAsset, AssetHash, AdapterReject);
        Assert.ThrowsExactly<TestException>(
            () => c.Receive(ChainA, message, new byte[] { 0x01 }),
            "a rejecting payout adapter must fault the entire finalization");
        Assert.AreEqual(1, rejectCalls);
        Assert.IsFalse(c.IsInboundConsumed(ChainA, 75)!.Value);

        c.SetAssetRoute(ChainA, ForeignAsset, AssetHash, AdapterAccept);
        c.Receive(ChainA, message, new byte[] { 0x01 });
        Assert.AreEqual(1, acceptCalls);
        Assert.IsTrue(c.IsInboundConsumed(ChainA, 75)!.Value,
            "the same verified message succeeds after a governance adapter replacement");
        Assert.AreEqual(AdapterAccept, c.GetPayoutAdapter(ChainA, ForeignAsset));
    }

    [TestMethod]
    public void Receive_AssetAndCall_BindsEveryPayoutFieldAndRejectsRuntimeAdapterDrift()
    {
        var engine = new TestEngine(true);
        WireRegistry(engine, RegistryAccept, accept: true);
        const ulong deadline = 4_000_000_000UL;
        var message = InboundAssetMessage(
            ChainA, 751, ForeignAsset, Recipient, 25,
            messageType: 2, calldata: new byte[] { 0xCA, 0xFE },
            deadlineUnixSeconds: deadline);
        var sourceTxRef = new UInt256(message.AsSpan(65, 32));
        var payoutCalls = 0;
        byte adapterVersion = 1;

        WireAdapterContractState(engine, AdapterAccept, 0);
        engine.FromHash<MockExternalBridgePayoutAdapter>(AdapterAccept, m =>
        {
            m.Setup(c => c.PayoutVersion()).Returns(() => (BigInteger)adapterVersion);
            m.Setup(c => c.Payout(
                    ChainA, NeoChainA, 751, ForeignAsset, AssetHash, Recipient, 25,
                    deadline, sourceTxRef, It.Is<byte[]?>(actual => actual != null && actual.SequenceEqual(message))))
                .Returns(() =>
                {
                    payoutCalls++;
                    return true;
                });
        }, checkExistence: false);

        var c = Deploy(engine);
        c.SetAssetRoute(ChainA, ForeignAsset, AssetHash, AdapterAccept);
        c.Receive(ChainA, message, new byte[] { 0x01 });
        Assert.AreEqual(1, payoutCalls,
            "adapter receives the exact source/destination chains, nonce, assets, recipient, amount, deadline, source tx, and signed bytes");

        adapterVersion = 2;
        Assert.ThrowsExactly<TestException>(
            () => c.Receive(ChainA,
                InboundAssetMessage(ChainA, 752, ForeignAsset, Recipient, 1,
                    messageType: 2, calldata: new byte[] { 0x01 }),
                new byte[] { 0x01 }),
            "an in-place adapter code upgrade that changes the ABI version must fail closed");
        Assert.IsFalse(c.IsInboundConsumed(ChainA, 752)!.Value);

        adapterVersion = 1;
        SetAdapterUpdateCounter(engine, AdapterAccept, 1);
        Assert.ThrowsExactly<TestException>(
            () => c.Receive(ChainA,
                InboundAssetMessage(ChainA, 754, ForeignAsset, Recipient, 1,
                    messageType: 2, calldata: new byte[] { 0x02 }),
                new byte[] { 0x01 }),
            "an in-place adapter update must fail closed even when it still reports ABI v1");
        Assert.IsFalse(c.IsInboundConsumed(ChainA, 754)!.Value);
    }

    [TestMethod]
    public void AssetRoute_AdapterUpgradeDriftCanBeDisabledButNotSilentlyRepinned()
    {
        var engine = new TestEngine(true);
        WirePayoutAdapter(engine, AdapterAccept, accept: true,
            updateCounter: 0);
        var c = Deploy(engine);

        c.SetAssetRoute(ChainA, ForeignAsset, AssetHash, AdapterAccept);
        SetAdapterUpdateCounter(engine, AdapterAccept, 1);

        c.SetAssetRouteActive(ChainA, ForeignAsset, false);
        Assert.IsFalse(c.IsAssetRouteActive(ChainA, ForeignAsset)!.Value,
            "governance must retain an emergency disable path after adapter drift");
        Assert.AreEqual((BigInteger)0,
            c.GetPayoutAdapterUpdateCounter(ChainA, ForeignAsset),
            "disabling must preserve the originally approved implementation generation");
        Assert.ThrowsExactly<TestException>(
            () => c.SetAssetRouteActive(ChainA, ForeignAsset, true),
            "the same updated contract hash cannot be silently re-authorized");
    }

    [TestMethod]
    public void AssetRoute_RejectsPreviouslyUpdatedAdapterDeployment()
    {
        var engine = new TestEngine(true);
        WirePayoutAdapter(engine, AdapterAccept, accept: true,
            updateCounter: 1);
        var c = Deploy(engine);

        Assert.ThrowsExactly<TestException>(
            () => c.SetAssetRoute(ChainA, ForeignAsset, AssetHash, AdapterAccept),
            "a payout adapter must be a fresh immutable deployment, not an already-updated hash");
        Assert.IsFalse(c.IsAssetRouteActive(ChainA, ForeignAsset)!.Value);
    }

    [TestMethod]
    public void AssetRoute_LegacyFortyOneByteRouteCanBeDisabledAndPinnedOnReenable()
    {
        var engine = new TestEngine(true);
        WireRegistry(engine, RegistryAccept, accept: true);
        WirePayoutAdapter(engine, AdapterAccept, accept: true, updateCounter: 0);
        var c = Deploy(engine);
        var legacyRoute = new byte[41];
        AssetHash.GetSpan().CopyTo(legacyRoute.AsSpan(0, 20));
        AdapterAccept.GetSpan().CopyTo(legacyRoute.AsSpan(20, 20));
        legacyRoute[40] = 1;
        c.Storage.Put(AssetRouteStorageKey(ChainA, ForeignAsset), legacyRoute);

        c.SetAssetRouteActive(ChainA, ForeignAsset, false);
        Assert.IsFalse(c.IsAssetRouteActive(ChainA, ForeignAsset)!.Value,
            "an upgraded escrow must retain an emergency disable path for legacy route values");
        c.SetAssetRouteActive(ChainA, ForeignAsset, true);
        Assert.IsTrue(c.IsAssetRouteActive(ChainA, ForeignAsset)!.Value,
            "re-enabling a never-updated legacy adapter must create its generation pin");

        SetAdapterUpdateCounter(engine, AdapterAccept, 1);
        Assert.ThrowsExactly<TestException>(
            () => c.Receive(ChainA,
                InboundAssetMessage(ChainA, 755, ForeignAsset, Recipient, 1,
                    messageType: 2, calldata: new byte[] { 0x03 }),
                new byte[] { 0x01 }),
            "the migrated route must detect later in-place adapter updates");
        Assert.IsFalse(c.IsInboundConsumed(ChainA, 755)!.Value);
    }

    [TestMethod]
    public void AssetRoute_LegacyL2DirectRouteCanBeDisabledButNotReenabled()
    {
        var engine = new TestEngine(true);
        WireRegistry(engine, RegistryAccept, accept: true);
        var c = Deploy(engine, neoChainId: NeoChainA);
        var legacyRoute = new byte[41];
        AssetHash.GetSpan().CopyTo(legacyRoute.AsSpan(0, 20));
        legacyRoute[40] = 1;
        c.Storage.Put(AssetRouteStorageKey(ChainA, ForeignAsset), legacyRoute);

        Assert.ThrowsExactly<TestException>(
            () => c.Receive(ChainA,
                InboundAssetMessage(ChainA, 756, ForeignAsset, Recipient, 1),
                new byte[] { 0x01 }),
            "an active legacy zero-adapter route must not release L1 value for an L2 destination");
        Assert.IsFalse(c.IsInboundConsumed(ChainA, 756)!.Value,
            "the runtime domain guard must roll back replay consumption");

        c.SetAssetRouteActive(ChainA, ForeignAsset, false);
        Assert.IsFalse(c.IsAssetRouteActive(ChainA, ForeignAsset)!.Value,
            "governance must be able to freeze a legacy unsafe route after upgrade");
        Assert.ThrowsExactly<TestException>(
            () => c.SetAssetRouteActive(ChainA, ForeignAsset, true),
            "a legacy zero-adapter L2 route must never be re-enabled");
    }

    [TestMethod]
    public void Receive_AdapterReentrantReplayIsRejectedBeforeSecondPayout()
    {
        var engine = new TestEngine(true);
        WireRegistry(engine, RegistryAccept, accept: true);
        var message = InboundAssetMessage(
            ChainA, 753, ForeignAsset, Recipient, 5,
            messageType: 2, calldata: new byte[] { 0x01 });
        NeoHubExternalBridgeEscrow? escrow = null;
        var replayRejected = false;
        var payoutCalls = 0;
        WirePayoutAdapter(engine, AdapterAccept, accept: true, () =>
        {
            payoutCalls++;
            try
            {
                escrow!.Receive(ChainA, message, new byte[] { 0x01 });
            }
            catch (TestException)
            {
                replayRejected = true;
            }
        });

        escrow = Deploy(engine);
        escrow.SetAssetRoute(ChainA, ForeignAsset, AssetHash, AdapterAccept);
        escrow.Receive(ChainA, message, new byte[] { 0x01 });

        Assert.IsTrue(replayRejected,
            "the replay marker must be visible before untrusted payout-adapter code runs");
        Assert.AreEqual(1, payoutCalls, "reentrancy must not execute a second payout");
        Assert.IsTrue(escrow.IsInboundConsumed(ChainA, 753)!.Value);
    }

    [TestMethod]
    public void Receive_RejectsPayloadLengthMismatchBeforeVerifierDispatch()
    {
        var engine = new TestEngine(true);
        var verifierCalls = 0;
        WireRegistry(engine, RegistryAccept, accept: true, () => verifierCalls++);
        var c = Deploy(engine);
        var message = InboundAssetMessage(ChainA, 76, ForeignAsset, Recipient, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(98, 4), 1);

        Assert.ThrowsExactly<TestException>(
            () => c.Receive(ChainA, message, new byte[] { 0x01 }),
            "the signed message envelope must not admit uncommitted trailing payload bytes");
        Assert.AreEqual(0, verifierCalls,
            "malformed wire length is rejected before calling the external verifier");
        Assert.IsFalse(c.IsInboundConsumed(ChainA, 76)!.Value);
    }

    // ---------------------------------------------------------------------------------------------
    // NEP-17 hook: reject unsolicited direct transfers
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void OnNEP17Payment_DirectTransfer_WithoutPendingSend_Rejected()
    {
        // A raw NEP-17 transfer that was NOT initiated by Send has no pending-transfer marker and must
        // be rejected, so stray tokens cannot be smuggled into the escrow out-of-band.
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        Assert.ThrowsExactly<TestException>(() => c.OnNEP17Payment(Stranger, 100, null),
            "a direct (non-Send) transfer must be rejected by the NEP-17 hook");
    }

    [TestMethod]
    public void OnNEP17Payment_RejectsNonPositiveAmount()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        Assert.ThrowsExactly<TestException>(() => c.OnNEP17Payment(Stranger, 0, null),
            "a zero-amount NEP-17 callback must be rejected");
    }
}
