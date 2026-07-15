using System.ComponentModel;
using System.Numerics;
using Moq;
using Neo;
using Neo.SmartContract.Testing;
using Neo.SmartContract.Testing.Exceptions;

namespace NeoHub.Contracts.VmTests;

/// <summary>Minimal GovernanceController surface so the council-veto proposal path can be mocked.
/// Exposes BOTH read-only checks the registry consults on the same hash:
/// <c>isApprovedAndTimelocked</c> (council multisig + timelock) and <c>matchesProposalPayload</c>
/// (binds the approved proposal payload to the exact (proofType, verifier) action args).</summary>
public abstract class Mock_VerifierRegistry_GovernanceController(SmartContractInitialize initialize) : SmartContract(initialize)
{
    [DisplayName("isApprovedAndTimelocked")]
    public abstract bool? IsApprovedAndTimelocked(BigInteger? proposalId);

    [DisplayName("matchesProposalPayload")]
    public abstract bool? MatchesProposalPayload(BigInteger? proposalId, byte[]? expectedAction);
}

/// <summary>Minimal verifier surface so VerifyCommitment's dispatch (Contract.Call(verifier,"verify",...))
/// can be mocked to return a controlled bool.</summary>
public abstract class Mock_VerifierRegistry_Verifier(SmartContractInitialize initialize) : SmartContract(initialize)
{
    [DisplayName("verify")]
    public abstract bool? Verify(byte[]? commitmentBytes);
}

/// <summary>
/// VM-level tests for NeoHub.VerifierRegistry — the pluggable proof-verifier dispatch table executed
/// in a real NeoVM. Pins the security-critical invariants:
///  - _deploy / ownership: owner witness gating (positive AND negative), zero-owner rejection,
///    ownership transfer actually moves the effective authority.
///  - RegisterVerifier: owner-gated, proofType range (1..3) + non-zero verifier validation, and the
///    round-hardening that the instant owner path is PERMANENTLY disabled once governance is locked
///    (closing the rogue-owner "swap to a return-true verifier" hole).
///  - LockGovernance: owner-gated, refuses to lock before the GovernanceController is wired (else no
///    verifier could ever be registered again), one-way + idempotent.
///  - RegisterVerifierViaProposal: requires GC wired, replay-protected per proposalId, and gated on
///    BOTH the council approval+timelock check and the payload-binding check (council voted on the
///    exact (proofType, verifier) bytes, not a blank check).
///  - VerifyCommitment: input-size guard, "no verifier for proof type" guard, and faithful
///    dispatch/return of the registered verifier's bool result.
///  - BuildRegisterVerifierAction: canonical 46-byte tag‖proofType‖verifier encoding (load-bearing
///    for the proposal payload binding).
/// </summary>
[TestClass]
public class UT_VerifierRegistry_Vm
{
    // Distinct fixed hashes — every hash that gets mocked must be unique within a test method.
    private static readonly UInt160 VerifierA = UInt160.Parse("0x" + new string('a', 40));
    private static readonly UInt160 VerifierB = UInt160.Parse("0x" + new string('b', 40));
    private static readonly UInt160 GcHash = UInt160.Parse("0x" + new string('c', 40));
    private static readonly UInt160 Stranger = UInt160.Parse("0x" + new string('d', 40));

    // ProofType: None(0)/Multisig(1)/Optimistic(2)/Zk(3).
    private const byte ProofZk = 3;

    /// <summary>ProofTypeOffset from the contract — the proofType byte lives at index 316 of the
    /// canonical L2BatchCommitment encoding; a valid commitment must be longer than that.</summary>
    private const int ProofTypeOffset = 316;

    /// <summary>Deploy the registry. owner defaults to engine.Sender so the owner witness checks pass;
    /// pass an explicit <paramref name="owner"/> to exercise the negative authorization paths.</summary>
    private static NeoHubVerifierRegistry Deploy(TestEngine engine, UInt160? owner = null)
    {
        var o = owner ?? engine.Sender;
        return engine.Deploy<NeoHubVerifierRegistry>(
            NeoHubVerifierRegistry.Nef, NeoHubVerifierRegistry.Manifest, o);
    }

    /// <summary>Build a commitment buffer whose proofType byte (index 316) is <paramref name="proofType"/>.</summary>
    private static byte[] Commitment(byte proofType, int len = ProofTypeOffset + 1)
    {
        var buf = new byte[len];
        if (len > ProofTypeOffset) buf[ProofTypeOffset] = proofType;
        return buf;
    }

    [TestMethod]
    public void Deploy_RejectsZeroOwner()
    {
        var engine = new TestEngine(true);
        Assert.ThrowsExactly<TestException>(() =>
            engine.Deploy<NeoHubVerifierRegistry>(
                NeoHubVerifierRegistry.Nef, NeoHubVerifierRegistry.Manifest, UInt160.Zero),
            "deploy must reject a zero owner");
    }

    [TestMethod]
    public void Deploy_SetsOwner()
    {
        var engine = new TestEngine(true);
        var vr = Deploy(engine);
        Assert.AreEqual(engine.Sender, vr.Owner, "deployed owner must be the sender");
    }

    [TestMethod]
    public void RegisterVerifier_OwnerOnly_StoresAndReadsBack()
    {
        var engine = new TestEngine(true);
        var vr = Deploy(engine); // owner == engine.Sender

        Assert.AreEqual(UInt160.Zero, vr.GetVerifier(ProofZk), "no verifier registered yet");
        vr.RegisterVerifier(ProofZk, VerifierA);
        Assert.AreEqual(VerifierA, vr.GetVerifier(ProofZk), "owner registration must persist");

        // Owner may replace the binding (bring-up swap).
        vr.RegisterVerifier(ProofZk, VerifierB);
        Assert.AreEqual(VerifierB, vr.GetVerifier(ProofZk), "owner may replace the verifier");
    }

    [TestMethod]
    public void RegisterVerifier_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        // Owner is a different account than the test signer -> the owner gate must reject.
        var vr = Deploy(engine, owner: Stranger);

        Assert.ThrowsExactly<TestException>(() => vr.RegisterVerifier(ProofZk, VerifierA),
            "RegisterVerifier is owner-gated");
        Assert.AreEqual(UInt160.Zero, vr.GetVerifier(ProofZk), "rejected registration must not set state");
    }

    [TestMethod]
    public void RegisterVerifier_RejectsZeroVerifier()
    {
        var engine = new TestEngine(true);
        var vr = Deploy(engine);
        Assert.ThrowsExactly<TestException>(() => vr.RegisterVerifier(ProofZk, UInt160.Zero),
            "zero verifier must be rejected");
    }

    [TestMethod]
    public void RegisterVerifier_RejectsProofTypeOutOfRange()
    {
        var engine = new TestEngine(true);
        var vr = Deploy(engine);

        // proofType 0 == "None" can't be verified by anything.
        Assert.ThrowsExactly<TestException>(() => vr.RegisterVerifier(0, VerifierA),
            "proofType 0 (None) must be rejected");
        // proofType 4 is above the Multisig/Optimistic/Zk range.
        Assert.ThrowsExactly<TestException>(() => vr.RegisterVerifier(4, VerifierA),
            "proofType above 3 must be rejected");
        // Valid boundaries 1..3 succeed.
        vr.RegisterVerifier(1, VerifierA);
        vr.RegisterVerifier(3, VerifierA);
        Assert.AreEqual(VerifierA, vr.GetVerifier(1));
        Assert.AreEqual(VerifierA, vr.GetVerifier(3));
    }

    [TestMethod]
    public void SetOwner_TransfersAuthority_OldOwnerLosesIt()
    {
        var engine = new TestEngine(true);
        var vr = Deploy(engine); // owner == engine.Sender

        // Hand ownership to a different account.
        vr.Owner = Stranger;
        Assert.AreEqual(Stranger, vr.Owner, "ownership must transfer");

        // The original signer is no longer owner -> owner-gated calls must now fault.
        Assert.ThrowsExactly<TestException>(() => vr.RegisterVerifier(ProofZk, VerifierA),
            "former owner must lose RegisterVerifier authority after transfer");
    }

    [TestMethod]
    public void SetOwner_NonOwner_Faults_AndRejectsZero()
    {
        var engine = new TestEngine(true);
        var owner = engine.Sender;
        var vr = Deploy(engine); // owner == engine.Sender

        // Non-owner path: switch the active signer to a stranger so the owner-witness gate rejects.
        engine.SetTransactionSigners(Stranger);
        Assert.ThrowsExactly<TestException>(() => vr.Owner = VerifierA,
            "SetOwner is owner-gated");

        // Restore the owner signer and confirm the owner path still rejects a zero new owner.
        engine.SetTransactionSigners(owner);
        Assert.ThrowsExactly<TestException>(() => vr.Owner = UInt160.Zero,
            "SetOwner must reject a zero new owner");
    }

    [TestMethod]
    public void SetGovernanceController_OwnerOnly_StoresAndReadsBack()
    {
        var engine = new TestEngine(true);
        var vr = Deploy(engine);

        Assert.AreEqual(UInt160.Zero, vr.GovernanceController, "GC unset by default");
        vr.GovernanceController = GcHash; // setter == setGovernanceController, owner-gated
        Assert.AreEqual(GcHash, vr.GovernanceController, "GC wiring must persist");
    }

    [TestMethod]
    public void SetGovernanceController_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        var vr = Deploy(engine, owner: Stranger);
        Assert.ThrowsExactly<TestException>(() => vr.GovernanceController = GcHash,
            "SetGovernanceController is owner-gated");
    }

    [TestMethod]
    public void LockGovernance_RequiresGcWired_OwnerOnly_OneWay_DisablesInstantPath()
    {
        var engine = new TestEngine(true);
        var vr = Deploy(engine); // owner == engine.Sender

        Assert.IsFalse(vr.IsGovernanceLocked!.Value, "not locked at deploy");

        // Locking before wiring the GC would brick verifier registration entirely -> rejected.
        Assert.ThrowsExactly<TestException>(() => vr.LockGovernance(),
            "must refuse to lock before GovernanceController is wired");
        Assert.IsFalse(vr.IsGovernanceLocked!.Value, "failed lock must not change state");

        // Wire GC, then lock.
        vr.GovernanceController = GcHash;
        vr.LockGovernance();
        Assert.IsTrue(vr.IsGovernanceLocked!.Value, "governance must be locked");

        // One-way + idempotent: re-locking is a no-op (does not fault).
        vr.LockGovernance();
        Assert.IsTrue(vr.IsGovernanceLocked!.Value, "re-lock stays locked");

        // The instant owner path is now permanently disabled — even the legitimate owner is blocked.
        Assert.ThrowsExactly<TestException>(() => vr.RegisterVerifier(ProofZk, VerifierA),
            "instant owner RegisterVerifier must revert once governance is locked");

        Assert.ThrowsExactly<TestException>(() => vr.GovernanceController = VerifierB,
            "the owner must not be able to replace the trusted GovernanceController after locking");
        Assert.AreEqual(GcHash, vr.GovernanceController,
            "a rejected controller replacement must preserve the exact pre-lock controller");
    }

    [TestMethod]
    public void LockGovernance_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        var vr = Deploy(engine, owner: Stranger);
        // Wiring GC is itself owner-gated, so a stranger can't even reach the lock — assert the lock faults.
        Assert.ThrowsExactly<TestException>(() => vr.LockGovernance(),
            "LockGovernance is owner-gated");
    }

    [TestMethod]
    public void RegisterVerifierViaProposal_RequiresGcWired()
    {
        var engine = new TestEngine(true);
        var vr = Deploy(engine); // GC not wired

        Assert.ThrowsExactly<TestException>(() => vr.RegisterVerifierViaProposal(ProofZk, VerifierA, 1),
            "proposal path needs the GovernanceController wired first");
    }

    [TestMethod]
    public void RegisterVerifierViaProposal_NotApproved_Faults()
    {
        var engine = new TestEngine(true);
        var vr = Deploy(engine);

        // GC says the proposal is NOT approved/timelocked.
        engine.FromHash<Mock_VerifierRegistry_GovernanceController>(GcHash, m =>
        {
            m.Setup(c => c.IsApprovedAndTimelocked(It.IsAny<BigInteger?>())).Returns(false);
            m.Setup(c => c.MatchesProposalPayload(It.IsAny<BigInteger?>(), It.IsAny<byte[]?>())).Returns(true);
        }, checkExistence: false);
        vr.GovernanceController = GcHash;

        Assert.ThrowsExactly<TestException>(() => vr.RegisterVerifierViaProposal(ProofZk, VerifierA, 1),
            "an un-approved / un-timelocked proposal must not register a verifier");
        Assert.AreEqual(UInt160.Zero, vr.GetVerifier(ProofZk), "rejected proposal must not set state");
    }

    [TestMethod]
    public void RegisterVerifierViaProposal_PayloadMismatch_Faults()
    {
        var engine = new TestEngine(true);
        var vr = Deploy(engine);

        // Approved + timelocked, but the proposal payload does NOT bind to (proofType, verifier).
        // This pins the "council voted on different bytes" / blank-check protection.
        engine.FromHash<Mock_VerifierRegistry_GovernanceController>(GcHash, m =>
        {
            m.Setup(c => c.IsApprovedAndTimelocked(It.IsAny<BigInteger?>())).Returns(true);
            m.Setup(c => c.MatchesProposalPayload(It.IsAny<BigInteger?>(), It.IsAny<byte[]?>())).Returns(false);
        }, checkExistence: false);
        vr.GovernanceController = GcHash;

        Assert.ThrowsExactly<TestException>(() => vr.RegisterVerifierViaProposal(ProofZk, VerifierA, 1),
            "a proposal whose payload does not match the action args must be rejected");
        Assert.AreEqual(UInt160.Zero, vr.GetVerifier(ProofZk));
    }

    [TestMethod]
    public void RegisterVerifierViaProposal_Approved_Bound_Registers_ThenReplayProtected()
    {
        var engine = new TestEngine(true);
        var vr = Deploy(engine);

        // Approved + timelocked AND payload binds -> the council path may register.
        engine.FromHash<Mock_VerifierRegistry_GovernanceController>(GcHash, m =>
        {
            m.Setup(c => c.IsApprovedAndTimelocked(It.IsAny<BigInteger?>())).Returns(true);
            m.Setup(c => c.MatchesProposalPayload(It.IsAny<BigInteger?>(), It.IsAny<byte[]?>())).Returns(true);
        }, checkExistence: false);
        vr.GovernanceController = GcHash;

        vr.RegisterVerifierViaProposal(ProofZk, VerifierA, 42);
        Assert.AreEqual(VerifierA, vr.GetVerifier(ProofZk), "approved+bound proposal must register the verifier");

        // Replay protection: the SAME proposalId can never be applied twice, even though the GC
        // mock would still approve it. This prevents re-applying one council vote repeatedly.
        Assert.ThrowsExactly<TestException>(() => vr.RegisterVerifierViaProposal(ProofZk, VerifierB, 42),
            "a consumed proposalId must not be applied again");
        Assert.AreEqual(VerifierA, vr.GetVerifier(ProofZk), "replayed proposal must not overwrite state");

        // A fresh proposalId is independent and still applicable.
        vr.RegisterVerifierViaProposal(ProofZk, VerifierB, 43);
        Assert.AreEqual(VerifierB, vr.GetVerifier(ProofZk), "a new proposalId may register");
    }

    [TestMethod]
    public void VerifyCommitment_RejectsTooSmallCommitment()
    {
        var engine = new TestEngine(true);
        var vr = Deploy(engine);
        // Length must be strictly greater than ProofTypeOffset (316); 316 bytes is too small.
        Assert.ThrowsExactly<TestException>(() => vr.VerifyCommitment(new byte[ProofTypeOffset]),
            "a commitment not longer than the proofType offset must be rejected");
    }

    [TestMethod]
    public void VerifyCommitment_NoVerifierForProofType_Faults()
    {
        var engine = new TestEngine(true);
        var vr = Deploy(engine);
        // proofType 3 has no registered verifier -> dispatch must fault, not silently succeed.
        Assert.ThrowsExactly<TestException>(() => vr.VerifyCommitment(Commitment(ProofZk)),
            "dispatch to an unregistered proof type must fault");
    }

    [TestMethod]
    public void VerifyCommitment_DispatchesToRegisteredVerifier_ReturnsItsResult()
    {
        var engine = new TestEngine(true);
        var vr = Deploy(engine);

        // VerifierA returns true, VerifierB returns false — VerifyCommitment must faithfully relay.
        engine.FromHash<Mock_VerifierRegistry_Verifier>(VerifierA, m =>
            m.Setup(c => c.Verify(It.IsAny<byte[]?>())).Returns(true), checkExistence: false);
        engine.FromHash<Mock_VerifierRegistry_Verifier>(VerifierB, m =>
            m.Setup(c => c.Verify(It.IsAny<byte[]?>())).Returns(false), checkExistence: false);

        vr.RegisterVerifier(ProofZk, VerifierA);
        Assert.IsTrue(vr.VerifyCommitment(Commitment(ProofZk))!.Value,
            "must relay the registered verifier's true result");

        // Re-point proofType 3 to the false-returning verifier.
        vr.RegisterVerifier(ProofZk, VerifierB);
        Assert.IsFalse(vr.VerifyCommitment(Commitment(ProofZk))!.Value,
            "must relay the registered verifier's false result");
    }

    [TestMethod]
    public void BuildRegisterVerifierAction_CanonicalEncoding()
    {
        var engine = new TestEngine(true);
        var vr = Deploy(engine);

        var action = vr.BuildRegisterVerifierAction(ProofZk, VerifierA)!;
        // Layout: "neo4-gov:registerVerifier"(25) ‖ proofType(1) ‖ verifier(20) = 46 bytes.
        Assert.AreEqual(46, action.Length, "canonical action must be 46 bytes");

        var tag = System.Text.Encoding.ASCII.GetBytes("neo4-gov:registerVerifier");
        Assert.AreEqual(25, tag.Length);
        for (var i = 0; i < tag.Length; i++)
            Assert.AreEqual(tag[i], action[i], $"tag byte {i} mismatch");

        Assert.AreEqual(ProofZk, action[25], "proofType byte must follow the tag");

        var vk = VerifierA.GetSpan();
        for (var i = 0; i < 20; i++)
            Assert.AreEqual(vk[i], action[26 + i], $"verifier byte {i} mismatch");

        // Different args produce different bytes (binding is meaningful).
        var other = vr.BuildRegisterVerifierAction(1, VerifierB)!;
        CollectionAssert.AreNotEqual(action, other, "distinct (proofType, verifier) must encode differently");
    }
}
