using System.Buffers.Binary;
using System.ComponentModel;
using System.Numerics;
using Moq;
using Neo;
using Neo.SmartContract.Testing;
using Neo.SmartContract.Testing.Exceptions;

namespace NeoHub.Contracts.VmTests;

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
///   * deploy validates owner + registry (both must be valid and non-zero),
///   * SetOwner / SetRegistry are owner-witness-gated (positive AND negative), and reject zero args,
///   * Send validates the 0xE0_xx_xx_xx foreign-namespace prefix, recipient/asset/amount, allocates a
///     per-chain monotonic nonce, accumulates the locked-balance ledger, and FAULT-reverts the
///     pre-transfer accounting when the asset transfer fails (no phantom lock),
///   * Receive validates namespace + message length + signed-chainId domain + direction + deadline,
///     is replay-protected once-only per (chainId, nonce), is gated on the registry verifier accepting,
///     and only then marks the nonce consumed,
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

    /// <summary>A valid foreign-namespace chain id (must match the 0xE0_xx_xx_xx prefix mask).</summary>
    private const uint ChainA = 0xE0000001;
    private const uint ChainB = 0xE0000002;
    /// <summary>A chain id that does NOT carry the foreign-namespace prefix — must be rejected.</summary>
    private const uint BadChain = 1001;

    /// <summary>Direction byte that Receive requires (ForeignToNeo).</summary>
    private const byte DirForeignToNeo = 2;
    /// <summary>Canonical ExternalCrossChainMessage minimum length asserted by Receive.</summary>
    private const int MsgLen = 102;

    /// <summary>Build a canonical inbound ExternalCrossChainMessage matching the offsets Receive parses:
    /// externalChainId@0 (4 LE), nonce@8 (8 LE), direction@16 (1B), deadline@57 (8 LE), messageType@97
    /// (1B). The total length is exactly the minimum (102) Receive requires.</summary>
    private static byte[] InboundMessage(uint externalChainId, ulong nonce, byte direction,
        ulong deadlineUnixSeconds, byte messageType)
    {
        var buf = new byte[MsgLen];
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0, 4), externalChainId);
        BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(8, 8), nonce);
        buf[16] = direction;
        BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(57, 8), deadlineUnixSeconds);
        buf[97] = messageType;
        return buf;
    }

    /// <summary>Register the shared MockNep17 transfer at <see cref="AssetHash"/> returning the given
    /// result (used by Send's lock transfer).</summary>
    private static void WireAsset(TestEngine engine, UInt160 hash, bool transferOk)
    {
        engine.FromHash<MockNep17>(hash, m =>
            m.Setup(c => c.Transfer(It.IsAny<UInt160?>(), It.IsAny<UInt160?>(), It.IsAny<BigInteger?>(), It.IsAny<object?>()))
                .Returns(transferOk),
            checkExistence: false);
    }

    /// <summary>Register an ExternalBridgeRegistry mock whose verifyInbound returns <paramref name="accept"/>.</summary>
    private static void WireRegistry(TestEngine engine, UInt160 hash, bool accept)
    {
        engine.FromHash<Mock_ExternalBridgeEscrow_Registry>(hash, m =>
            m.Setup(c => c.VerifyInbound(It.IsAny<BigInteger?>(), It.IsAny<byte[]?>(), It.IsAny<byte[]?>()))
                .Returns(accept),
            checkExistence: false);
    }

    /// <summary>Deploy the escrow. owner/registry default to engine.Sender / RegistryAccept so the
    /// owner witness checks pass and inbound verification succeeds; pass explicit values to exercise
    /// the negative authorization / verifier-reject paths.</summary>
    private static NeoHubExternalBridgeEscrow Deploy(TestEngine engine, UInt160? owner = null, UInt160? registry = null)
    {
        var o = owner ?? engine.Sender;
        var r = registry ?? RegistryAccept;
        return engine.Deploy<NeoHubExternalBridgeEscrow>(
            NeoHubExternalBridgeEscrow.Nef, NeoHubExternalBridgeEscrow.Manifest, new object[] { o, r });
    }

    // ---------------------------------------------------------------------------------------------
    // Deploy-time input validation
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void Deploy_WiresOwnerAndRegistry()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        Assert.AreEqual(engine.Sender, c.Owner, "owner must be the deploy arg");
        Assert.AreEqual(RegistryAccept, c.Registry, "registry must be the deploy arg");
    }

    [TestMethod]
    public void Deploy_RejectsZeroOwner()
    {
        var engine = new TestEngine(true);
        Assert.ThrowsExactly<TestException>(() => engine.Deploy<NeoHubExternalBridgeEscrow>(
            NeoHubExternalBridgeEscrow.Nef, NeoHubExternalBridgeEscrow.Manifest,
            new object[] { UInt160.Zero, RegistryAccept }),
            "a zero owner must be rejected at deploy");
    }

    [TestMethod]
    public void Deploy_RejectsZeroRegistry()
    {
        var engine = new TestEngine(true);
        Assert.ThrowsExactly<TestException>(() => engine.Deploy<NeoHubExternalBridgeEscrow>(
            NeoHubExternalBridgeEscrow.Nef, NeoHubExternalBridgeEscrow.Manifest,
            new object[] { engine.Sender, UInt160.Zero }),
            "a zero registry must be rejected at deploy");
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

        var msg = InboundMessage(ChainA, 5, DirForeignToNeo, 0, 1);
        Assert.ThrowsExactly<TestException>(() => c.Receive(ChainA, msg, new byte[] { 0x01 }),
            "the rejecting registry must block finalization");
        Assert.IsFalse(c.IsInboundConsumed(ChainA, 5)!.Value);

        // Owner rotates the registry to the accepting verifier; the same inbound now finalizes.
        c.Registry = RegistryAccept;
        c.Receive(ChainA, msg, new byte[] { 0x01 });
        Assert.IsTrue(c.IsInboundConsumed(ChainA, 5)!.Value,
            "after rotating to an accepting verifier the inbound finalizes");
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
