using System.ComponentModel;
using System.Numerics;
using Moq;
using Neo;
using Neo.SmartContract.Testing;
using Neo.SmartContract.Testing.Exceptions;

namespace NeoHub.Contracts.VmTests;

/// <summary>Minimal GovernanceController surface so the council-proposal path can be mocked.
/// Exposes BOTH read-only checks the registry consults on the same hash:
/// <c>isApprovedAndTimelocked</c> (council multisig + timelock) and <c>matchesProposalPayload</c>
/// (binds the approved proposal payload to the exact (externalChainId, verifier, bridgeKind)
/// action args).</summary>
public abstract class Mock_ExternalBridgeRegistry_GovernanceController(SmartContractInitialize initialize) : SmartContract(initialize)
{
    [DisplayName("isApprovedAndTimelocked")]
    public abstract bool? IsApprovedAndTimelocked(BigInteger? proposalId);

    [DisplayName("matchesProposalPayload")]
    public abstract bool? MatchesProposalPayload(BigInteger? proposalId, byte[]? expectedAction);
}

/// <summary>Minimal external-chain verifier surface. The registry consults two methods on the
/// verifier hash: <c>bridgeKind</c> (self-declared trust model, asserted == requested kind at
/// registration) and <c>verifyInboundMessage</c> (the per-message dispatch target). Both live on
/// the same hash so one mock declares both.</summary>
public abstract class Mock_ExternalBridgeRegistry_Verifier(SmartContractInitialize initialize) : SmartContract(initialize)
{
    [DisplayName("bridgeKind")]
    public abstract BigInteger? BridgeKind();

    [DisplayName("verifyInboundMessage")]
    public abstract bool? VerifyInboundMessage(BigInteger? externalChainId, byte[]? messageBytes, byte[]? proofBytes);
}

/// <summary>
/// VM-level tests for NeoHub.ExternalBridgeRegistry — the pluggable per-foreign-chain verifier
/// dispatch table executed in a real NeoVM. Pins the security-critical invariants:
///  - _deploy / ownership: zero-owner rejection, owner witness gating (positive AND negative),
///    ownership transfer actually moves the effective authority.
///  - SetGovernanceController: owner-gated (positive AND negative), zero/invalid rejection.
///  - RegisterVerifier: owner-gated (positive AND negative); the WriteVerifier guards — non-zero
///    verifier, the 0xE0_xx_xx_xx foreign-namespace prefix on externalChainId, bridgeKind range
///    {1,2,3}, and the load-bearing check that the verifier's self-declared bridgeKind matches the
///    requested production bridgeKind (no registering a ZK chain against an MPC verifier).
///  - UpgradeVerifierViaProposal: requires GC wired, replay-protected per proposalId, and gated on
///    BOTH the council approval+timelock check AND the payload-binding check (council voted on the
///    exact (externalChainId, verifier, bridgeKind) bytes, not a blank check).
///  - VerifyInbound: "no verifier for chain" guard, and faithful dispatch/return of the registered
///    verifier's bool result.
///  - BuildUpgradeVerifierAction: canonical 49-byte tag‖chainId‖verifier‖bridgeKind encoding
///    (load-bearing for the proposal payload binding).
/// </summary>
[TestClass]
public class UT_ExternalBridgeRegistry_Vm
{
    // Distinct fixed hashes — every hash that gets mocked must be unique within a test method.
    private static readonly UInt160 VerifierA = UInt160.Parse("0x" + new string('a', 40));
    private static readonly UInt160 VerifierB = UInt160.Parse("0x" + new string('b', 40));
    private static readonly UInt160 GcHash = UInt160.Parse("0x" + new string('c', 40));
    private static readonly UInt160 Stranger = UInt160.Parse("0x" + new string('d', 40));

    // bridgeKind: MPC(1)/Optimistic(2)/ZK(3).
    private const byte KindMpc = 1;
    private const byte KindOptimistic = 2;
    private const byte KindZk = 3;

    // Valid externalChainIds must carry the 0xE0_xx_xx_xx foreign-namespace prefix.
    private const uint ChainEth = 0xE000_0001;
    private const uint ChainTron = 0xE000_0002;

    /// <summary>Deploy with owner defaulting to engine.Sender so the owner witness check passes;
    /// pass an explicit <paramref name="owner"/> to exercise the negative authorization path.</summary>
    private static NeoHubExternalBridgeRegistry Deploy(TestEngine engine, UInt160? owner = null)
    {
        var o = owner ?? engine.Sender;
        return engine.Deploy<NeoHubExternalBridgeRegistry>(
            NeoHubExternalBridgeRegistry.Nef, NeoHubExternalBridgeRegistry.Manifest, o);
    }

    /// <summary>Register a verifier mock on a hash that self-declares <paramref name="kind"/> and
    /// relays <paramref name="verifyResult"/> from verifyInboundMessage.</summary>
    private static void MockVerifier(TestEngine engine, UInt160 hash, byte kind, bool verifyResult)
    {
        engine.FromHash<Mock_ExternalBridgeRegistry_Verifier>(hash, m =>
        {
            m.Setup(c => c.BridgeKind()).Returns((BigInteger)kind);
            m.Setup(c => c.VerifyInboundMessage(It.IsAny<BigInteger?>(), It.IsAny<byte[]?>(), It.IsAny<byte[]?>()))
                .Returns(verifyResult);
        }, checkExistence: false);
    }

    // ── _deploy / ownership ───────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Deploy_SetsOwner_RejectsZeroOwner()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        Assert.AreEqual(engine.Sender, c.Owner, "deploy must record the owner");

        // A zero owner is an unusable governance principal and must be rejected at deploy.
        // Use a fresh engine so the FAULT is unambiguously the _deploy "invalid owner" assert.
        var engine2 = new TestEngine(true);
        Assert.ThrowsExactly<TestException>(() => Deploy(engine2, UInt160.Zero),
            "zero owner must be rejected at deploy");
    }

    [TestMethod]
    public void SetOwner_OwnerOnly_TransfersAuthority()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine); // owner == engine.Sender

        // New owner must be valid + non-zero.
        Assert.ThrowsExactly<TestException>(() => c.Owner = UInt160.Zero, "zero new owner rejected");

        // Owner (witnessed) may transfer ownership to a stranger.
        c.Owner = Stranger;
        Assert.AreEqual(Stranger, c.Owner, "ownership must move to the new owner");

        // After transfer the old signer (engine.Sender) is no longer authorized — the effective
        // authority actually moved, not just the stored value.
        Assert.ThrowsExactly<TestException>(() => c.Owner = engine.Sender,
            "the former owner must lose authority once ownership is transferred");
    }

    [TestMethod]
    public void SetOwner_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        // Owner is a different account than the test signer -> the owner gate must reject.
        var c = Deploy(engine, owner: Stranger);

        Assert.ThrowsExactly<TestException>(() => c.Owner = VerifierA,
            "SetOwner is owner-gated");
        Assert.AreEqual(Stranger, c.Owner, "rejected SetOwner must not change state");
    }

    // ── SetGovernanceController ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void SetGovernanceController_OwnerOnly_RejectsZero()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        Assert.AreEqual(UInt160.Zero, c.GovernanceController, "no GC wired by default");
        Assert.ThrowsExactly<TestException>(() => c.GovernanceController = UInt160.Zero,
            "zero governance controller rejected");

        c.GovernanceController = GcHash;
        Assert.AreEqual(GcHash, c.GovernanceController, "owner may wire the GC hash");
    }

    [TestMethod]
    public void SetGovernanceController_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine, owner: Stranger);

        Assert.ThrowsExactly<TestException>(() => c.GovernanceController = GcHash,
            "SetGovernanceController is owner-gated");
        Assert.AreEqual(UInt160.Zero, c.GovernanceController, "rejected wire must not set state");
    }

    // ── RegisterVerifier (owner path) ──────────────────────────────────────────────────────────

    [TestMethod]
    public void RegisterVerifier_OwnerOnly_StoresVerifierAndKind()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        MockVerifier(engine, VerifierA, KindMpc, verifyResult: true);

        Assert.AreEqual(UInt160.Zero, c.GetVerifier(ChainEth), "no verifier registered yet");
        Assert.AreEqual((BigInteger)0, c.GetBridgeKind(ChainEth), "no bridgeKind registered yet");

        c.RegisterVerifier(ChainEth, VerifierA, KindMpc);
        Assert.AreEqual(VerifierA, c.GetVerifier(ChainEth), "owner may bind a verifier to the chain");
        Assert.AreEqual((BigInteger)KindMpc, c.GetBridgeKind(ChainEth), "the bridgeKind must be recorded");
    }

    [TestMethod]
    public void RegisterVerifier_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine, owner: Stranger);
        MockVerifier(engine, VerifierA, KindMpc, verifyResult: true);

        Assert.ThrowsExactly<TestException>(() => c.RegisterVerifier(ChainEth, VerifierA, KindMpc),
            "RegisterVerifier is owner-gated");
        Assert.AreEqual(UInt160.Zero, c.GetVerifier(ChainEth), "rejected register must not set state");
    }

    [TestMethod]
    public void RegisterVerifier_RejectsZeroVerifier()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        Assert.ThrowsExactly<TestException>(() => c.RegisterVerifier(ChainEth, UInt160.Zero, KindMpc),
            "a zero verifier must be rejected");
    }

    [TestMethod]
    public void RegisterVerifier_RejectsNonForeignNamespaceChainId()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        MockVerifier(engine, VerifierA, KindMpc, verifyResult: true);

        // chainId 1 is a Neo L2 chainId, NOT a 0xE0_xx_xx_xx foreign id — keeping the keyspaces
        // disjoint prevents a foreign verifier from shadowing an L2 chain entry.
        Assert.ThrowsExactly<TestException>(() => c.RegisterVerifier(1, VerifierA, KindMpc),
            "externalChainId must carry the foreign-namespace prefix");
        Assert.AreEqual(UInt160.Zero, c.GetVerifier(1), "rejected register must not set state");
    }

    [TestMethod]
    public void RegisterVerifier_RejectsOutOfRangeBridgeKind()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        // Mock declares kind 4 so the range guard (not the match guard) is what fires.
        engine.FromHash<Mock_ExternalBridgeRegistry_Verifier>(VerifierA, m =>
        {
            m.Setup(x => x.BridgeKind()).Returns((BigInteger)4);
            m.Setup(x => x.VerifyInboundMessage(It.IsAny<BigInteger?>(), It.IsAny<byte[]?>(), It.IsAny<byte[]?>()))
                .Returns(true);
        }, checkExistence: false);

        Assert.ThrowsExactly<TestException>(() => c.RegisterVerifier(ChainEth, VerifierA, 4),
            "bridgeKind outside {1,2,3} must be rejected");
        Assert.ThrowsExactly<TestException>(() => c.RegisterVerifier(ChainEth, VerifierA, 0),
            "bridgeKind 0 must be rejected");
    }

    [TestMethod]
    public void RegisterVerifier_RejectsVerifierKindMismatch()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        // The verifier self-declares MPC(1) but we request ZK(3): the production trust-model
        // declaration must match the verifier's own bridgeKind, else a UI/dApp surfaces the wrong
        // trust model for the messages traversing that chain.
        MockVerifier(engine, VerifierA, KindMpc, verifyResult: true);

        Assert.ThrowsExactly<TestException>(() => c.RegisterVerifier(ChainEth, VerifierA, KindZk),
            "requested bridgeKind must match the verifier's self-declared bridgeKind");
        Assert.AreEqual(UInt160.Zero, c.GetVerifier(ChainEth), "mismatch must not set state");

        // The matching kind succeeds, proving it was specifically the mismatch that failed.
        c.RegisterVerifier(ChainEth, VerifierA, KindMpc);
        Assert.AreEqual(VerifierA, c.GetVerifier(ChainEth));
    }

    [TestMethod]
    public void RegisterVerifier_CanReplaceExistingBinding()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        MockVerifier(engine, VerifierA, KindMpc, verifyResult: true);
        MockVerifier(engine, VerifierB, KindZk, verifyResult: true);

        c.RegisterVerifier(ChainEth, VerifierA, KindMpc);
        Assert.AreEqual(VerifierA, c.GetVerifier(ChainEth));
        Assert.AreEqual((BigInteger)KindMpc, c.GetBridgeKind(ChainEth));

        // The owner upgrade path (Phase B->D) re-points the chain and its declared trust model.
        c.RegisterVerifier(ChainEth, VerifierB, KindZk);
        Assert.AreEqual(VerifierB, c.GetVerifier(ChainEth), "owner may replace the bound verifier");
        Assert.AreEqual((BigInteger)KindZk, c.GetBridgeKind(ChainEth), "bridgeKind updates with the verifier");
    }

    [TestMethod]
    public void UpgradeVerifier_AliasIsOwnerGatedRegister()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine, owner: Stranger);
        MockVerifier(engine, VerifierA, KindOptimistic, verifyResult: true);

        // The alias routes straight to RegisterVerifier, so the owner gate still applies.
        Assert.ThrowsExactly<TestException>(() => c.UpgradeVerifier(ChainEth, VerifierA, KindOptimistic),
            "UpgradeVerifier alias is owner-gated");
    }

    // ── UpgradeVerifierViaProposal (governance path) ───────────────────────────────────────────

    [TestMethod]
    public void UpgradeVerifierViaProposal_RequiresGcWired()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine); // GC not wired
        MockVerifier(engine, VerifierA, KindMpc, verifyResult: true);

        Assert.ThrowsExactly<TestException>(() => c.UpgradeVerifierViaProposal(ChainEth, VerifierA, KindMpc, 1),
            "proposal path needs the GovernanceController wired first");
    }

    [TestMethod]
    public void UpgradeVerifierViaProposal_NotApproved_Faults()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        MockVerifier(engine, VerifierA, KindMpc, verifyResult: true);

        // GC says the proposal is NOT approved/timelocked (payload binding would pass).
        engine.FromHash<Mock_ExternalBridgeRegistry_GovernanceController>(GcHash, m =>
        {
            m.Setup(x => x.IsApprovedAndTimelocked(It.IsAny<BigInteger?>())).Returns(false);
            m.Setup(x => x.MatchesProposalPayload(It.IsAny<BigInteger?>(), It.IsAny<byte[]?>())).Returns(true);
        }, checkExistence: false);
        c.GovernanceController = GcHash;

        Assert.ThrowsExactly<TestException>(() => c.UpgradeVerifierViaProposal(ChainEth, VerifierA, KindMpc, 1),
            "an un-approved / un-timelocked proposal must not register a verifier");
        Assert.AreEqual(UInt160.Zero, c.GetVerifier(ChainEth), "rejected proposal must not set state");
    }

    [TestMethod]
    public void UpgradeVerifierViaProposal_PayloadMismatch_Faults()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        MockVerifier(engine, VerifierA, KindMpc, verifyResult: true);

        // Approved + timelocked, but the payload does NOT bind to (chainId, verifier, bridgeKind).
        // Pins the "council voted on different bytes" / blank-check protection.
        engine.FromHash<Mock_ExternalBridgeRegistry_GovernanceController>(GcHash, m =>
        {
            m.Setup(x => x.IsApprovedAndTimelocked(It.IsAny<BigInteger?>())).Returns(true);
            m.Setup(x => x.MatchesProposalPayload(It.IsAny<BigInteger?>(), It.IsAny<byte[]?>())).Returns(false);
        }, checkExistence: false);
        c.GovernanceController = GcHash;

        Assert.ThrowsExactly<TestException>(() => c.UpgradeVerifierViaProposal(ChainEth, VerifierA, KindMpc, 1),
            "a proposal whose payload does not match the action args must be rejected");
        Assert.AreEqual(UInt160.Zero, c.GetVerifier(ChainEth));
    }

    [TestMethod]
    public void UpgradeVerifierViaProposal_Approved_Bound_Registers_ThenReplayProtected()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        MockVerifier(engine, VerifierA, KindMpc, verifyResult: true);
        MockVerifier(engine, VerifierB, KindZk, verifyResult: true);

        // Approved + timelocked AND payload binds -> the council path may register.
        engine.FromHash<Mock_ExternalBridgeRegistry_GovernanceController>(GcHash, m =>
        {
            m.Setup(x => x.IsApprovedAndTimelocked(It.IsAny<BigInteger?>())).Returns(true);
            m.Setup(x => x.MatchesProposalPayload(It.IsAny<BigInteger?>(), It.IsAny<byte[]?>())).Returns(true);
        }, checkExistence: false);
        c.GovernanceController = GcHash;

        c.UpgradeVerifierViaProposal(ChainEth, VerifierA, KindMpc, 42);
        Assert.AreEqual(VerifierA, c.GetVerifier(ChainEth), "approved+bound proposal must register the verifier");
        Assert.AreEqual((BigInteger)KindMpc, c.GetBridgeKind(ChainEth));

        // Replay protection: the SAME proposalId can never be applied twice, even though the GC
        // mock still approves it. Prevents re-applying one council vote repeatedly.
        Assert.ThrowsExactly<TestException>(() => c.UpgradeVerifierViaProposal(ChainEth, VerifierB, KindZk, 42),
            "a consumed proposalId must not be applied again");
        Assert.AreEqual(VerifierA, c.GetVerifier(ChainEth), "replayed proposal must not overwrite state");

        // A fresh proposalId is independent and still applicable.
        c.UpgradeVerifierViaProposal(ChainEth, VerifierB, KindZk, 43);
        Assert.AreEqual(VerifierB, c.GetVerifier(ChainEth), "a new proposalId may register");
        Assert.AreEqual((BigInteger)KindZk, c.GetBridgeKind(ChainEth));
    }

    // ── VerifyInbound (dispatch) ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void VerifyInbound_NoVerifierForChain_Faults()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        // No verifier registered for the chain -> dispatch must fault, not silently succeed.
        Assert.ThrowsExactly<TestException>(() => c.VerifyInbound(ChainEth, new byte[] { 0x01 }, new byte[] { 0x02 }),
            "dispatch with no registered verifier must fault");
    }

    [TestMethod]
    public void VerifyInbound_DispatchesToRegisteredVerifier_RelaysResult()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        // VerifierA accepts, VerifierB rejects — VerifyInbound must faithfully relay each result.
        MockVerifier(engine, VerifierA, KindMpc, verifyResult: true);
        MockVerifier(engine, VerifierB, KindMpc, verifyResult: false);

        c.RegisterVerifier(ChainEth, VerifierA, KindMpc);
        Assert.IsTrue(c.VerifyInbound(ChainEth, new byte[] { 0x01 }, new byte[] { 0x02 })!.Value,
            "must relay the registered verifier's true result");

        // Re-point chain to the rejecting verifier (same kind, distinct hash).
        c.RegisterVerifier(ChainEth, VerifierB, KindMpc);
        Assert.IsFalse(c.VerifyInbound(ChainEth, new byte[] { 0x01 }, new byte[] { 0x02 })!.Value,
            "must relay the registered verifier's false result");
    }

    // ── BuildUpgradeVerifierAction (canonical payload encoding) ─────────────────────────────────

    [TestMethod]
    public void BuildUpgradeVerifierAction_CanonicalEncoding()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        // Layout: "neo4-gov:upgradeVerifier"(24B) ‖ chainId(4B LE) ‖ verifier(20B) ‖ bridgeKind(1B) = 49B.
        var action = c.BuildUpgradeVerifierAction(ChainEth, VerifierA, KindZk)!;
        Assert.AreEqual(24 + 4 + 20 + 1, action.Length, "canonical upgrade-verifier action is 49 bytes");

        var tag = System.Text.Encoding.ASCII.GetBytes("neo4-gov:upgradeVerifier");
        for (var i = 0; i < tag.Length; i++)
            Assert.AreEqual(tag[i], action[i], "action must start with the canonical tag");

        // chainId little-endian.
        Assert.AreEqual((byte)(ChainEth & 0xFF), action[24]);
        Assert.AreEqual((byte)((ChainEth >> 8) & 0xFF), action[25]);
        Assert.AreEqual((byte)((ChainEth >> 16) & 0xFF), action[26]);
        Assert.AreEqual((byte)((ChainEth >> 24) & 0xFF), action[27]);

        // verifier 20 bytes follow the chainId.
        var vk = VerifierA.GetSpan();
        for (var i = 0; i < 20; i++)
            Assert.AreEqual(vk[i], action[28 + i], "verifier bytes must be embedded verbatim");

        // bridgeKind trailing byte.
        Assert.AreEqual(KindZk, action[48], "trailing byte is the bridgeKind");

        // Distinct args produce distinct encodings — the binding actually discriminates.
        var other = c.BuildUpgradeVerifierAction(ChainTron, VerifierB, KindMpc)!;
        CollectionAssert.AreNotEqual(action, other, "different action args must yield different payloads");
    }
}
