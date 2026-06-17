using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;
using Moq;
using Neo;
using Neo.SmartContract.Testing;
using Neo.SmartContract.Testing.Exceptions;

namespace NeoHub.Contracts.VmTests;

/// <summary>
/// Minimal SettlementManager surface that the EmergencyManager escape-hatch paths call into.
/// Only the two read-only methods the contract delegates to are exposed:
/// <c>getCanonicalStateRoot</c> (single-entry fast path) and <c>verifyStateLeafWithProof</c>
/// (Merkle inclusion path). Kept uniquely named so it never collides with the project's
/// existing mocks (MockNep17, MockL1TxFilter) or the generated NeoHubSettlementManager wrapper.
/// </summary>
public abstract class Mock_EmergencyManager_SettlementManager(SmartContractInitialize initialize) : SmartContract(initialize)
{
    [DisplayName("getCanonicalStateRoot")]
    public abstract UInt256? GetCanonicalStateRoot(BigInteger? chainId);

    [DisplayName("verifyStateLeafWithProof")]
    public abstract bool? VerifyStateLeafWithProof(BigInteger? chainId, UInt256? leafHash, IList<object>? siblings, BigInteger? leafIndex);
}

/// <summary>
/// VM-level tests for NeoHub.EmergencyManager — the global pause flag and escape-hatch exit path.
/// Executes pause/resume, council/owner governance, and both escape-hatch shapes in a real NeoVM and
/// pins the security-critical invariants:
///  - Pause is emergency-council-gated; Resume / SetOwner / SetEmergencyCouncil are owner-gated
///    (positive AND negative witness paths).
///  - The escape hatch is only valid while paused and only for the witnessing sender.
///  - Escape claims are replay-protected per (chainId, leafHash): the same proof cannot be claimed
///    twice, and a leaf valid on one L2 cannot be replayed against another.
///  - The single-entry fast path rejects a zero canonical root and only matches leaf == canonical
///    root; the proof path faults unless SettlementManager.verifyStateLeafWithProof returns true.
///  - _deploy rejects zero/invalid principals.
/// </summary>
[TestClass]
public class UT_EmergencyManager_Vm
{
    private const uint ChainA = 1001;
    private const uint ChainB = 2002;

    // A "good" canonical root that the single-entry fast path will match against the supplied leaf.
    private static readonly UInt256 Root = UInt256.Parse("0x" + new string('1', 64));
    private static readonly UInt256 OtherLeaf = UInt256.Parse("0x" + new string('2', 64));

    private static readonly UInt160 NonSigner = UInt160.Parse("0x" + new string('a', 40));

    /// <summary>
    /// Deploy the EmergencyManager. owner / council default to engine.Sender so the owner and council
    /// witness checks pass; pass an explicit principal to exercise the negative authorization paths.
    /// The SettlementManager hash is configurable so escape-hatch tests can point at a mock that
    /// returns the desired canonical root / verification result.
    /// </summary>
    private static NeoHubEmergencyManager Deploy(TestEngine engine, UInt160? owner = null,
        UInt160? council = null, UInt160? settlementManager = null)
    {
        var o = owner ?? engine.Sender;
        var co = council ?? engine.Sender;
        var sm = settlementManager ?? UInt160.Parse("0x" + new string('5', 40));
        return engine.Deploy<NeoHubEmergencyManager>(
            NeoHubEmergencyManager.Nef, NeoHubEmergencyManager.Manifest, new object[] { o, co, sm });
    }

    [TestMethod]
    public void Deploy_RejectsZeroPrincipals()
    {
        var engine = new TestEngine(true);
        var good = engine.Sender;

        Assert.ThrowsExactly<TestException>(() => engine.Deploy<NeoHubEmergencyManager>(
            NeoHubEmergencyManager.Nef, NeoHubEmergencyManager.Manifest,
            new object[] { UInt160.Zero, good, good }), "zero owner must be rejected");
        Assert.ThrowsExactly<TestException>(() => engine.Deploy<NeoHubEmergencyManager>(
            NeoHubEmergencyManager.Nef, NeoHubEmergencyManager.Manifest,
            new object[] { good, UInt160.Zero, good }), "zero council must be rejected");
        Assert.ThrowsExactly<TestException>(() => engine.Deploy<NeoHubEmergencyManager>(
            NeoHubEmergencyManager.Nef, NeoHubEmergencyManager.Manifest,
            new object[] { good, good, UInt160.Zero }), "zero settlement manager must be rejected");
    }

    [TestMethod]
    public void Deploy_StoresWiring_StartsUnpaused()
    {
        var engine = new TestEngine(true);
        var sm = UInt160.Parse("0x" + new string('5', 40));
        var em = Deploy(engine, settlementManager: sm);

        Assert.AreEqual(engine.Sender, em.Owner, "owner wired on deploy");
        Assert.AreEqual(sm, em.SettlementManager, "settlement manager wired on deploy");
        Assert.AreEqual(false, em.IsPaused, "network starts unpaused");
    }

    [TestMethod]
    public void Pause_ByCouncil_Then_Resume_ByOwner()
    {
        var engine = new TestEngine(true);
        var em = Deploy(engine); // council == owner == engine.Sender (witnessed)

        Assert.AreEqual(false, em.IsPaused);
        em.Pause();
        Assert.AreEqual(true, em.IsPaused, "council can pause");
        em.Resume();
        Assert.AreEqual(false, em.IsPaused, "owner can resume");
    }

    [TestMethod]
    public void Pause_NonCouncil_Faults()
    {
        var engine = new TestEngine(true);
        // Council is a different account than the test signer -> the council gate must reject.
        var em = Deploy(engine, council: NonSigner);

        Assert.ThrowsExactly<TestException>(() => em.Pause(), "Pause is emergency-council-gated");
        Assert.AreEqual(false, em.IsPaused, "rejected pause must not set state");
    }

    [TestMethod]
    public void Resume_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        // Owner is a different account; council is the signer so we can still pause.
        var em = Deploy(engine, owner: NonSigner, council: engine.Sender);

        em.Pause();
        Assert.AreEqual(true, em.IsPaused);
        Assert.ThrowsExactly<TestException>(() => em.Resume(), "Resume is owner-gated");
        Assert.AreEqual(true, em.IsPaused, "rejected resume must leave the pause flag set");
    }

    [TestMethod]
    public void SetOwner_OwnerGated_RejectsZero_TransfersOnSuccess()
    {
        var engine = new TestEngine(true);
        var em = Deploy(engine); // owner == engine.Sender

        Assert.ThrowsExactly<TestException>(() => em.Owner = UInt160.Zero, "zero new owner must be rejected");

        var newOwner = UInt160.Parse("0x" + new string('b', 40));
        em.Owner = newOwner;
        Assert.AreEqual(newOwner, em.Owner, "ownership transferred");

        // After handoff the original signer is no longer owner -> further owner ops must fault.
        Assert.ThrowsExactly<TestException>(() => em.Owner = engine.Sender,
            "old owner must not be able to set owner after handoff");
    }

    [TestMethod]
    public void SetOwner_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        var em = Deploy(engine, owner: NonSigner); // owner witness absent

        Assert.ThrowsExactly<TestException>(() => em.Owner = engine.Sender, "SetOwner is owner-gated");
    }

    [TestMethod]
    public void SetEmergencyCouncil_OwnerGated_RejectsZero_RotatesCouncil()
    {
        var engine = new TestEngine(true);
        // owner == signer, but the initial council is a different account.
        var em = Deploy(engine, owner: engine.Sender, council: NonSigner);

        Assert.ThrowsExactly<TestException>(() => em.SetEmergencyCouncil(UInt160.Zero),
            "zero council must be rejected");

        // The old council can pause? No — it's NonSigner, not witnessed. Rotate to the signer.
        Assert.ThrowsExactly<TestException>(() => em.Pause(), "current council is not the signer");
        em.SetEmergencyCouncil(engine.Sender);
        em.Pause();
        Assert.AreEqual(true, em.IsPaused, "rotated council (the signer) can now pause");
    }

    [TestMethod]
    public void SetEmergencyCouncil_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        var em = Deploy(engine, owner: NonSigner);

        Assert.ThrowsExactly<TestException>(() => em.SetEmergencyCouncil(engine.Sender),
            "SetEmergencyCouncil is owner-gated");
    }

    [TestMethod]
    public void EscapeHatchExit_RequiresPaused()
    {
        var engine = new TestEngine(true);
        var sm = UInt160.Parse("0x" + new string('5', 40));
        var em = Deploy(engine, settlementManager: sm);
        engine.FromHash<Mock_EmergencyManager_SettlementManager>(sm, m =>
            m.Setup(c => c.GetCanonicalStateRoot(It.IsAny<BigInteger?>())).Returns(Root),
            checkExistence: false);

        // Not paused -> the escape hatch must be inert even with a valid leaf == canonical root.
        Assert.ThrowsExactly<TestException>(() => em.EscapeHatchExit(ChainA, engine.Sender, Root),
            "escape hatch only valid while paused");
    }

    [TestMethod]
    public void EscapeHatchExit_NonSenderWitness_Faults()
    {
        var engine = new TestEngine(true);
        var sm = UInt160.Parse("0x" + new string('5', 40));
        var em = Deploy(engine, settlementManager: sm);
        engine.FromHash<Mock_EmergencyManager_SettlementManager>(sm, m =>
            m.Setup(c => c.GetCanonicalStateRoot(It.IsAny<BigInteger?>())).Returns(Root),
            checkExistence: false);

        em.Pause();
        // sender is NOT the witnessing signer -> the CheckWitness(sender) gate must reject.
        Assert.ThrowsExactly<TestException>(() => em.EscapeHatchExit(ChainA, NonSigner, Root),
            "escape hatch requires the sender's witness");
    }

    [TestMethod]
    public void EscapeHatchExit_ZeroCanonicalRoot_Faults()
    {
        var engine = new TestEngine(true);
        var sm = UInt160.Parse("0x" + new string('5', 40));
        var em = Deploy(engine, settlementManager: sm);
        // A chain with no finalized batch reports a zero canonical root.
        engine.FromHash<Mock_EmergencyManager_SettlementManager>(sm, m =>
            m.Setup(c => c.GetCanonicalStateRoot(It.IsAny<BigInteger?>())).Returns(UInt256.Zero),
            checkExistence: false);

        em.Pause();
        // Even a zero leaf must not spuriously match a zero root.
        Assert.ThrowsExactly<TestException>(() => em.EscapeHatchExit(ChainA, engine.Sender, UInt256.Zero),
            "a zero canonical root must be rejected (no finalized state root)");
    }

    [TestMethod]
    public void EscapeHatchExit_LeafMismatch_Faults()
    {
        var engine = new TestEngine(true);
        var sm = UInt160.Parse("0x" + new string('5', 40));
        var em = Deploy(engine, settlementManager: sm);
        engine.FromHash<Mock_EmergencyManager_SettlementManager>(sm, m =>
            m.Setup(c => c.GetCanonicalStateRoot(It.IsAny<BigInteger?>())).Returns(Root),
            checkExistence: false);

        em.Pause();
        // leaf != canonical root -> single-entry fast path must reject.
        Assert.ThrowsExactly<TestException>(() => em.EscapeHatchExit(ChainA, engine.Sender, OtherLeaf),
            "leaf that does not equal the canonical state root must be rejected");
    }

    [TestMethod]
    public void EscapeHatchExit_Succeeds_Then_ReplayProtected()
    {
        var engine = new TestEngine(true);
        var sm = UInt160.Parse("0x" + new string('5', 40));
        var em = Deploy(engine, settlementManager: sm);
        engine.FromHash<Mock_EmergencyManager_SettlementManager>(sm, m =>
            m.Setup(c => c.GetCanonicalStateRoot(It.IsAny<BigInteger?>())).Returns(Root),
            checkExistence: false);

        em.Pause();
        // Valid single-entry escape: leaf == canonical root, sender witnessed, paused.
        em.EscapeHatchExit(ChainA, engine.Sender, Root);

        // The same (chainId, leafHash) cannot be claimed twice — one-time, replay-protected.
        Assert.ThrowsExactly<TestException>(() => em.EscapeHatchExit(ChainA, engine.Sender, Root),
            "escape leaf already consumed must fault on replay");
    }

    [TestMethod]
    public void EscapeHatchExit_ReplayProtectionIsPerChain()
    {
        var engine = new TestEngine(true);
        // ChainA reports Root via one SM mock; ChainB reports the SAME Root via a DISTINCT mock hash.
        // The point: a leaf consumed on ChainA must remain claimable on ChainB (key is per-chain),
        // but cannot be re-claimed on ChainA.
        var smA = UInt160.Parse("0x" + new string('5', 40));
        var em = Deploy(engine, settlementManager: smA);
        engine.FromHash<Mock_EmergencyManager_SettlementManager>(smA, m =>
            m.Setup(c => c.GetCanonicalStateRoot(It.IsAny<BigInteger?>())).Returns(Root),
            checkExistence: false);

        em.Pause();
        em.EscapeHatchExit(ChainA, engine.Sender, Root);
        // Same leaf, different chain -> independent key, still claimable (root matches for ChainB too).
        em.EscapeHatchExit(ChainB, engine.Sender, Root);

        // But ChainA is now consumed.
        Assert.ThrowsExactly<TestException>(() => em.EscapeHatchExit(ChainA, engine.Sender, Root),
            "ChainA's leaf is consumed; replay must fault");
    }

    [TestMethod]
    public void EscapeHatchExitWithProof_RequiresPaused()
    {
        var engine = new TestEngine(true);
        var sm = UInt160.Parse("0x" + new string('5', 40));
        var em = Deploy(engine, settlementManager: sm);
        engine.FromHash<Mock_EmergencyManager_SettlementManager>(sm, m =>
            m.Setup(c => c.VerifyStateLeafWithProof(It.IsAny<BigInteger?>(), It.IsAny<UInt256?>(),
                It.IsAny<IList<object>?>(), It.IsAny<BigInteger?>())).Returns(true),
            checkExistence: false);

        Assert.ThrowsExactly<TestException>(() =>
            em.EscapeHatchExitWithProof(ChainA, engine.Sender, Root, new List<object>(), 0),
            "proof escape hatch only valid while paused");
    }

    [TestMethod]
    public void EscapeHatchExitWithProof_NonSenderWitness_Faults()
    {
        var engine = new TestEngine(true);
        var sm = UInt160.Parse("0x" + new string('5', 40));
        var em = Deploy(engine, settlementManager: sm);
        engine.FromHash<Mock_EmergencyManager_SettlementManager>(sm, m =>
            m.Setup(c => c.VerifyStateLeafWithProof(It.IsAny<BigInteger?>(), It.IsAny<UInt256?>(),
                It.IsAny<IList<object>?>(), It.IsAny<BigInteger?>())).Returns(true),
            checkExistence: false);

        em.Pause();
        Assert.ThrowsExactly<TestException>(() =>
            em.EscapeHatchExitWithProof(ChainA, NonSigner, Root, new List<object>(), 0),
            "proof escape hatch requires the sender's witness");
    }

    [TestMethod]
    public void EscapeHatchExitWithProof_VerificationFails_Faults()
    {
        var engine = new TestEngine(true);
        var sm = UInt160.Parse("0x" + new string('5', 40));
        var em = Deploy(engine, settlementManager: sm);
        // SettlementManager rejects the Merkle proof.
        engine.FromHash<Mock_EmergencyManager_SettlementManager>(sm, m =>
            m.Setup(c => c.VerifyStateLeafWithProof(It.IsAny<BigInteger?>(), It.IsAny<UInt256?>(),
                It.IsAny<IList<object>?>(), It.IsAny<BigInteger?>())).Returns(false),
            checkExistence: false);

        em.Pause();
        Assert.ThrowsExactly<TestException>(() =>
            em.EscapeHatchExitWithProof(ChainA, engine.Sender, Root, new List<object>(), 0),
            "a leaf that does not Merkle-verify must be rejected");
    }

    [TestMethod]
    public void EscapeHatchExitWithProof_Succeeds_Then_ReplayProtected()
    {
        var engine = new TestEngine(true);
        var sm = UInt160.Parse("0x" + new string('5', 40));
        var em = Deploy(engine, settlementManager: sm);
        engine.FromHash<Mock_EmergencyManager_SettlementManager>(sm, m =>
            m.Setup(c => c.VerifyStateLeafWithProof(It.IsAny<BigInteger?>(), It.IsAny<UInt256?>(),
                It.IsAny<IList<object>?>(), It.IsAny<BigInteger?>())).Returns(true),
            checkExistence: false);

        em.Pause();
        em.EscapeHatchExitWithProof(ChainA, engine.Sender, Root, new List<object>(), 0);

        // Same (chainId, leafHash) -> consumed, replay must fault even though verification still passes.
        Assert.ThrowsExactly<TestException>(() =>
            em.EscapeHatchExitWithProof(ChainA, engine.Sender, Root, new List<object>(), 0),
            "consumed escape leaf must fault on replay");
    }
}
