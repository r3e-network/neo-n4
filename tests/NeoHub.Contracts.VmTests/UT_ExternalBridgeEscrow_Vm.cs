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
///   * deploy validates owner + registry and permanently binds a non-zero Neo L2 chain domain,
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

    /// <summary>Register the shared MockNep17 transfer at <see cref="AssetHash"/> returning the given
    /// result (used by Send's lock transfer).</summary>
    private static void WireAsset(
        TestEngine engine,
        UInt160 hash,
        bool transferOk,
        Action<UInt160?, UInt160?, BigInteger?>? onTransfer = null)
    {
        engine.FromHash<MockNep17>(hash, m =>
            m.Setup(c => c.Transfer(It.IsAny<UInt160?>(), It.IsAny<UInt160?>(), It.IsAny<BigInteger?>(), It.IsAny<object?>()))
                .Returns((UInt160? from, UInt160? to, BigInteger? amount, object? _) =>
                {
                    onTransfer?.Invoke(from, to, amount);
                    return transferOk;
                }),
            checkExistence: false);
    }

    private static void WirePayoutAdapter(TestEngine engine, UInt160 hash, bool accept, Action? onPayout = null)
    {
        engine.FromHash<MockExternalBridgePayoutAdapter>(hash, m =>
            m.Setup(c => c.Payout(
                    It.IsAny<BigInteger?>(), It.IsAny<BigInteger?>(), It.IsAny<BigInteger?>(),
                    It.IsAny<UInt160?>(), It.IsAny<UInt160?>(), It.IsAny<UInt160?>(),
                    It.IsAny<BigInteger?>(), It.IsAny<BigInteger?>(), It.IsAny<UInt256?>(),
                    It.IsAny<byte[]?>()))
                .Returns(() =>
                {
                    onPayout?.Invoke();
                    return accept;
                }),
            checkExistence: false);
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
    public void Deploy_RejectsZeroNeoChainId()
    {
        var engine = new TestEngine(true);
        Assert.ThrowsExactly<TestException>(() => engine.Deploy<NeoHubExternalBridgeEscrow>(
            NeoHubExternalBridgeEscrow.Nef, NeoHubExternalBridgeEscrow.Manifest,
            new object[] { engine.Sender, RegistryAccept, 0u }),
            "an escrow deployment must be permanently bound to a non-zero Neo L2 chain id");
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
    public void Receive_RejectsZeroNeoChainDomainBeforeVerifierAndNonceConsumption()
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
            "a zero target domain must never reach a shared committee verifier");
        Assert.AreEqual(0, verifierCalls, "zero-domain rejection must happen before verifier dispatch");
        Assert.IsFalse(c.IsInboundConsumed(ChainA, nonce)!.Value,
            "zero-domain rejection must leave the legitimate nonce available");
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
    public void AssetRoute_OwnerCanConfigureReplaceAndDisable()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        c.SetAssetRoute(ChainA, ForeignAsset, AssetHash, UInt160.Zero);
        Assert.AreEqual(AssetHash, c.GetRoutedNeoAsset(ChainA, ForeignAsset));
        Assert.AreEqual(UInt160.Zero, c.GetPayoutAdapter(ChainA, ForeignAsset));
        Assert.IsTrue(c.IsAssetRouteActive(ChainA, ForeignAsset)!.Value);

        c.SetAssetRoute(ChainA, ForeignAsset, AssetHash2, AdapterAccept);
        Assert.AreEqual(AssetHash2, c.GetRoutedNeoAsset(ChainA, ForeignAsset));
        Assert.AreEqual(AdapterAccept, c.GetPayoutAdapter(ChainA, ForeignAsset));

        c.SetAssetRouteActive(ChainA, ForeignAsset, false);
        Assert.IsFalse(c.IsAssetRouteActive(ChainA, ForeignAsset)!.Value);
        Assert.AreEqual(AssetHash2, c.GetRoutedNeoAsset(ChainA, ForeignAsset),
            "disabling preserves the auditable route data");
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
    public void Receive_AssetTransfer_DirectRoutePaysRecipientAndDebitsEscrow()
    {
        var engine = new TestEngine(true);
        WireRegistry(engine, RegistryAccept, accept: true);
        var transfers = new List<(UInt160? From, UInt160? To, BigInteger? Amount)>();
        WireAsset(engine, AssetHash, transferOk: true,
            (from, to, amount) => transfers.Add((from, to, amount)));
        var c = Deploy(engine);
        c.SetAssetRoute(ChainA, ForeignAsset, AssetHash, UInt160.Zero);
        c.FundLiquidity(ChainA, AssetHash, 1_000);

        c.Receive(ChainA,
            InboundAssetMessage(ChainA, 71, ForeignAsset, Recipient, 400),
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
        var c = Deploy(engine);
        c.SetAssetRoute(ChainA, ForeignAsset, AssetHash, UInt160.Zero);
        c.FundLiquidity(ChainA, AssetHash, 50);

        Assert.ThrowsExactly<TestException>(
            () => c.Receive(ChainA,
                InboundAssetMessage(ChainA, 72, ForeignAsset, Recipient, 51),
                new byte[] { 0x01 }),
            "an inbound payout cannot draw more than the chain-specific escrow pool");

        Assert.AreEqual((BigInteger)50, c.GetLockedBalance(ChainA, AssetHash));
        Assert.IsFalse(c.IsInboundConsumed(ChainA, 72)!.Value,
            "failed payout must roll back the replay marker so a funded retry can succeed");
    }

    [TestMethod]
    public void Receive_AssetTransfer_RejectsWrongOrInactiveAssetMapping()
    {
        var engine = new TestEngine(true);
        WireRegistry(engine, RegistryAccept, accept: true);
        var c = Deploy(engine);
        c.SetAssetRoute(ChainA, ForeignAsset, AssetHash, UInt160.Zero);

        Assert.ThrowsExactly<TestException>(
            () => c.Receive(ChainA,
                InboundAssetMessage(ChainA, 73, ForeignAsset2, Recipient, 1),
                new byte[] { 0x01 }),
            "a signed foreign asset cannot be substituted into another route");
        Assert.IsFalse(c.IsInboundConsumed(ChainA, 73)!.Value);

        c.SetAssetRouteActive(ChainA, ForeignAsset, false);
        Assert.ThrowsExactly<TestException>(
            () => c.Receive(ChainA,
                InboundAssetMessage(ChainA, 74, ForeignAsset, Recipient, 1),
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
