using System;
using System.Collections.Generic;
using System.Numerics;
using Neo;
using Neo.Cryptography.ECC;
using Neo.SmartContract.Testing;
using Neo.SmartContract.Testing.Exceptions;

namespace NeoHub.Contracts.VmTests;

/// <summary>
/// VM-level tests for NeoHub.GovernanceController — the L1 council / timelock / admission-policy hub.
/// These execute the compiled NEF in a real NeoVM and pin the security-critical governance invariants:
///   - Council membership + per-member one-vote approval (M-of-N), replay-protected.
///   - Owner-only mutators (admission mode, verifier/bridge allowlists, immutable flags, ownership,
///     upgrade windows, marker) reject non-owner witnesses (CheckWitness negative path).
///   - SetAdmissionMode may only TIGHTEN instantly; loosening must go through the council-approved,
///     timelocked, payload-bound, replay-protected SetAdmissionModeViaProposal path.
///   - The *ViaProposal execution gate enforces approval + timelock + payload binding + per-proposal
///     replay protection, and the owner CancelProposal veto permanently disables an approved proposal.
///   - Immutable flags are write-only (set-once, never cleared) — the ZKsync PermanentRestriction idiom.
///
/// Council members are real secp256r1 public keys; their witness is supplied by
/// <c>engine.SetTransactionSigners(member)</c> (which derives the signature-contract script hash the
/// VM's CheckWitness compares against). The deployer (engine.Sender, the default validators address)
/// is the governance owner; owner-gated calls run under the default signer set.
/// </summary>
[TestClass]
public class UT_GovernanceController_Vm
{
    // Two real secp256r1 public keys (drawn from the testing protocol's standby committee) used as
    // council members. CheckWitness(member) passes only when the member is set as a tx signer.
    private static readonly ECPoint Member1 =
        ECPoint.Parse("03b209fd4f53a7170ea4444e0cb0a6bb6a53c2bd016926989cf85f9b0fba17a70c", ECCurve.Secp256r1);
    private static readonly ECPoint Member2 =
        ECPoint.Parse("02df48f60e8f3e01c48ff40b9b7f1310d7a8b2a193188befe1c2e3df740e895093", ECCurve.Secp256r1);
    // A valid public key that is NOT a council member (used for the negative membership path).
    private static readonly ECPoint NonMember =
        ECPoint.Parse("03b8d9d5771d8f513aa0869b9cc8d50986403b78c6da36890638c3d46a5adce04a", ECCurve.Secp256r1);
    private static readonly ECPoint NewMember1 = PointFromScalar(101);
    private static readonly ECPoint NewMember2 = PointFromScalar(102);

    private static readonly UInt160 Verifier = UInt160.Parse("0x" + new string('a', 40));
    private static readonly UInt160 Bridge = UInt160.Parse("0x" + new string('b', 40));
    private static readonly UInt160 Stranger = UInt160.Parse("0x" + new string('c', 40));

    private const uint Timelock = 100; // seconds
    private const byte ExpiredProposalStage = 5;

    /// <summary>Deploy with owner = engine.Sender (the default validators address), a 2-member council,
    /// threshold 2, and a 100s timelock. Pass a non-Sender <paramref name="owner"/> to exercise the
    /// negative owner-auth paths.</summary>
    private static NeoHubGovernanceController Deploy(TestEngine engine, UInt160? owner = null,
        uint threshold = 2, uint timelock = Timelock, ECPoint[]? members = null)
    {
        var o = owner ?? engine.Sender;
        var council = members ?? new ECPoint[] { Member1, Member2 };
        return engine.Deploy<NeoHubGovernanceController>(
            NeoHubGovernanceController.Nef, NeoHubGovernanceController.Manifest,
            new object[] { o, council, (BigInteger)threshold, (BigInteger)timelock });
    }

    // ───────────────────────── deploy validation ─────────────────────────

    [TestMethod]
    public void Deploy_RejectsBadArgs()
    {
        // zero owner
        var e1 = new TestEngine(true);
        Assert.ThrowsExactly<TestException>(() => e1.Deploy<NeoHubGovernanceController>(
            NeoHubGovernanceController.Nef, NeoHubGovernanceController.Manifest,
            new object[] { UInt160.Zero, new ECPoint[] { Member1 }, (BigInteger)1, (BigInteger)Timelock }),
            "zero owner must be rejected");

        // empty council
        var e2 = new TestEngine(true);
        Assert.ThrowsExactly<TestException>(() => e2.Deploy<NeoHubGovernanceController>(
            NeoHubGovernanceController.Nef, NeoHubGovernanceController.Manifest,
            new object[] { e2.Sender, new ECPoint[0], (BigInteger)1, (BigInteger)Timelock }),
            "empty council must be rejected");

        // threshold zero
        var e3 = new TestEngine(true);
        Assert.ThrowsExactly<TestException>(() => e3.Deploy<NeoHubGovernanceController>(
            NeoHubGovernanceController.Nef, NeoHubGovernanceController.Manifest,
            new object[] { e3.Sender, new ECPoint[] { Member1 }, (BigInteger)0, (BigInteger)Timelock }),
            "zero threshold must be rejected");

        // threshold exceeds committee size
        var e4 = new TestEngine(true);
        Assert.ThrowsExactly<TestException>(() => e4.Deploy<NeoHubGovernanceController>(
            NeoHubGovernanceController.Nef, NeoHubGovernanceController.Manifest,
            new object[] { e4.Sender, new ECPoint[] { Member1 }, (BigInteger)2, (BigInteger)Timelock }),
            "threshold larger than council must be rejected");

        // zero timelock (instant execution defeats the timelock) must be rejected
        var e5 = new TestEngine(true);
        Assert.ThrowsExactly<TestException>(() => e5.Deploy<NeoHubGovernanceController>(
            NeoHubGovernanceController.Nef, NeoHubGovernanceController.Manifest,
            new object[] { e5.Sender, new ECPoint[] { Member1 }, (BigInteger)1, (BigInteger)0 }),
            "zero timelock must be rejected");

        var e6 = new TestEngine(true);
        Assert.ThrowsExactly<TestException>(() => Deploy(e6, threshold: 1,
            members: new ECPoint[] { Member1, Member1 }), "duplicate council members must be rejected");

        var e7 = new TestEngine(true);
        Assert.ThrowsExactly<TestException>(() => Deploy(e7, threshold: 1,
            members: CreateDistinctMembers(65)), "council size must be bounded without count truncation");
    }

    [TestMethod]
    public void Deploy_InitializesCouncilState()
    {
        var engine = new TestEngine(true);
        var g = Deploy(engine);

        Assert.AreEqual(engine.Sender, g.Owner!, "owner is the deployer");
        Assert.AreEqual((BigInteger)2, g.CouncilCount!);
        Assert.AreEqual((BigInteger)2, g.Threshold!);
        Assert.AreEqual((BigInteger)1, g.CouncilEpoch!);
        Assert.AreEqual((BigInteger)Timelock, g.TimelockSeconds!);
        Assert.AreEqual((BigInteger)0, g.AdmissionMode!, "starts permissioned");
        Assert.IsTrue(g.IsCouncilMember(Member1)!.Value);
        Assert.IsTrue(g.IsCouncilMember(Member2)!.Value);
        Assert.IsFalse(g.IsCouncilMember(NonMember)!.Value);
    }

    // ───────────────────────── ownership ─────────────────────────

    [TestMethod]
    public void SetOwner_OwnerOnly_RejectsZero_TransfersOnSuccess()
    {
        var engine = new TestEngine(true);
        var g = Deploy(engine); // owner == Sender

        Assert.ThrowsExactly<TestException>(() => g.Owner = UInt160.Zero, "zero new owner rejected");

        var newOwner = Stranger;
        g.Owner = newOwner;
        Assert.AreEqual(newOwner, g.Owner!, "ownership transferred");

        // Old owner (still the witnessed Sender) can no longer act — the witness no longer matches.
        Assert.ThrowsExactly<TestException>(() => g.ApproveVerifier(Verifier),
            "former owner must lose authority after transfer");
    }

    [TestMethod]
    public void SetOwner_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        // Owner is a different account than the witnessed Sender -> the owner gate must reject.
        var g = Deploy(engine, owner: Stranger);
        Assert.ThrowsExactly<TestException>(() => g.Owner = engine.Sender, "SetOwner is owner-gated");
    }

    // ───────────────────────── verifier / bridge allowlists (owner-gated) ─────────────────────────

    [TestMethod]
    public void ApproveRevokeVerifier_OwnerGated()
    {
        var engine = new TestEngine(true);
        var g = Deploy(engine);

        Assert.IsFalse(g.IsApprovedVerifier(Verifier)!.Value);
        Assert.ThrowsExactly<TestException>(() => g.ApproveVerifier(UInt160.Zero), "zero verifier rejected");

        g.ApproveVerifier(Verifier);
        Assert.IsTrue(g.IsApprovedVerifier(Verifier)!.Value);
        g.RevokeVerifier(Verifier);
        Assert.IsFalse(g.IsApprovedVerifier(Verifier)!.Value, "revoked verifier no longer approved");
    }

    [TestMethod]
    public void ApproveVerifier_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        var g = Deploy(engine, owner: Stranger);
        Assert.ThrowsExactly<TestException>(() => g.ApproveVerifier(Verifier), "ApproveVerifier is owner-gated");
        Assert.IsFalse(g.IsApprovedVerifier(Verifier)!.Value, "rejected approval must not set state");
    }

    [TestMethod]
    public void ApproveRevokeBridgeAdapter_OwnerGated()
    {
        var engine = new TestEngine(true);
        var g = Deploy(engine);

        Assert.ThrowsExactly<TestException>(() => g.ApproveBridgeAdapter(UInt160.Zero), "zero bridge rejected");
        g.ApproveBridgeAdapter(Bridge);
        Assert.IsTrue(g.IsApprovedBridgeAdapter(Bridge)!.Value);
        g.RevokeBridgeAdapter(Bridge);
        Assert.IsFalse(g.IsApprovedBridgeAdapter(Bridge)!.Value);
    }

    [TestMethod]
    public void ApproveBridgeAdapter_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        var g = Deploy(engine, owner: Stranger);
        Assert.ThrowsExactly<TestException>(() => g.ApproveBridgeAdapter(Bridge),
            "ApproveBridgeAdapter is owner-gated");
    }

    // ───────────────────────── admission mode ─────────────────────────

    [TestMethod]
    public void SetAdmissionMode_OnlyTightens_RejectsInvalidAndLoosen()
    {
        var engine = new TestEngine(true);
        var g = Deploy(engine); // starts at mode 0 (permissioned)

        Assert.ThrowsExactly<TestException>(() => g.AdmissionMode = (BigInteger)3, "mode > 2 invalid");

        // Already at the most restrictive (0); setting 0 is allowed (mode <= current).
        g.AdmissionMode = (BigInteger)0;
        Assert.AreEqual((BigInteger)0, g.AdmissionMode!);

        // Loosening (0 -> 2) via the instant owner path must be rejected — must go through proposal.
        Assert.ThrowsExactly<TestException>(() => g.AdmissionMode = (BigInteger)2,
            "instant SetAdmissionMode may only tighten; loosening must use the proposal path");
        Assert.AreEqual((BigInteger)0, g.AdmissionMode!, "rejected loosening must not change state");
    }

    [TestMethod]
    public void SetAdmissionMode_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        var g = Deploy(engine, owner: Stranger);
        Assert.ThrowsExactly<TestException>(() => g.AdmissionMode = (BigInteger)0,
            "SetAdmissionMode is owner-gated");
    }

    // ───────────────────────── council proposal lifecycle ─────────────────────────

    [TestMethod]
    public void CreateProposal_RequiresCouncilMemberWitness_AndNonEmptyPayload()
    {
        var engine = new TestEngine(true);
        var g = Deploy(engine);
        var payload = g.BuildSetAdmissionModeAction((BigInteger)2)!;

        // Non-member signer cannot create a proposal even if witnessed.
        engine.SetTransactionSigners(NonMember);
        Assert.ThrowsExactly<TestException>(() => g.CreateProposal(NonMember, payload),
            "non-council member cannot create a proposal");

        // Member but NOT witnessed (signer is a different member) -> CheckWitness(Member1) fails.
        engine.SetTransactionSigners(Member2);
        Assert.ThrowsExactly<TestException>(() => g.CreateProposal(Member1, payload),
            "CreateProposal requires the signer's own witness");

        // Witnessed member, empty payload -> rejected.
        engine.SetTransactionSigners(Member1);
        Assert.ThrowsExactly<TestException>(() => g.CreateProposal(Member1, new byte[0]),
            "empty proposal payload rejected");

        // Witnessed member, valid payload -> id 1.
        Assert.AreEqual((BigInteger)1, g.CreateProposal(Member1, payload)!, "first proposal id is 1");
        Assert.IsTrue(g.GetProposal((BigInteger)1)!.Length > 0, "payload stored");
        Assert.AreEqual((BigInteger)1, g.GetProposalEpoch((BigInteger)1)!, "proposal binds creation epoch");
    }

    [TestMethod]
    public void Approve_OneVotePerMember_ReplayProtected_ReachesThreshold()
    {
        var engine = new TestEngine(true);
        var g = Deploy(engine);
        var payload = g.BuildSetAdmissionModeAction((BigInteger)1)!;

        engine.SetTransactionSigners(Member1);
        var id = g.CreateProposal(Member1, payload)!.Value;

        // Approving an unknown proposal faults.
        Assert.ThrowsExactly<TestException>(() => g.Approve((BigInteger)999, Member1), "unknown proposal");

        // Member1 approves once.
        Assert.AreEqual((BigInteger)1, g.Approve(id, Member1)!);
        Assert.AreEqual((BigInteger)1, g.GetApprovalCount(id)!);

        // Double-vote by the same member faults.
        Assert.ThrowsExactly<TestException>(() => g.Approve(id, Member1), "one vote per member");

        // A non-member cannot approve.
        engine.SetTransactionSigners(NonMember);
        Assert.ThrowsExactly<TestException>(() => g.Approve(id, NonMember), "non-member cannot approve");

        // Member without their own witness cannot approve.
        engine.SetTransactionSigners(Member1);
        Assert.ThrowsExactly<TestException>(() => g.Approve(id, Member2),
            "Approve requires the approving member's witness");

        // Member2 approves -> threshold (2) reached -> approvedAt recorded.
        engine.SetTransactionSigners(Member2);
        Assert.AreEqual((BigInteger)2, g.Approve(id, Member2)!, "threshold reached");
        Assert.AreEqual((BigInteger)2, g.GetApprovalCount(id)!);
        Assert.IsTrue(g.GetApprovedAt(id)! > (BigInteger)0, "approvedAt set on first threshold crossing");
    }

    [TestMethod]
    public void IsApprovedAndTimelocked_FalseBeforeTimelock_TrueAfter()
    {
        var engine = new TestEngine(true);
        var owner = engine.Sender;
        var g = Deploy(engine);
        var id = ApproveToThreshold(engine, g, g.BuildSetAdmissionModeAction((BigInteger)2)!, owner);

        // Threshold reached but timelock not elapsed -> not yet executable.
        Assert.IsFalse(g.IsApprovedAndTimelocked(id)!.Value, "timelock not elapsed yet");

        engine.PersistingBlock.Advance(TimeSpan.FromSeconds(Timelock + 1));
        Assert.IsTrue(g.IsApprovedAndTimelocked(id)!.Value, "timelock elapsed -> executable");
    }

    // ───────────── SetAdmissionModeViaProposal: the only loosening path ─────────────

    [TestMethod]
    public void SetAdmissionModeViaProposal_LoosensOnlyAfterApprovalTimelockAndBinding()
    {
        var engine = new TestEngine(true);
        var owner = engine.Sender;
        var g = Deploy(engine);
        var action = g.BuildSetAdmissionModeAction((BigInteger)2)!; // target = permissionless
        var id = ApproveToThreshold(engine, g, action, owner);

        // Before timelock elapses, execution must fault even though the payload binds.
        Assert.ThrowsExactly<TestException>(() => g.SetAdmissionModeViaProposal((BigInteger)2, id),
            "cannot execute before timelock elapses");

        engine.PersistingBlock.Advance(TimeSpan.FromSeconds(Timelock + 1));

        // Payload binding: the council voted on mode 2; executing with mode 1 must be rejected.
        Assert.ThrowsExactly<TestException>(() => g.SetAdmissionModeViaProposal((BigInteger)1, id),
            "payload binding: executed mode must equal the voted mode");

        // Correct mode after timelock -> admission loosened to 2.
        g.SetAdmissionModeViaProposal((BigInteger)2, id);
        Assert.AreEqual((BigInteger)2, g.AdmissionMode!, "admission loosened via proposal");

        // Replay protection: the same proposal cannot be consumed twice.
        Assert.ThrowsExactly<TestException>(() => g.SetAdmissionModeViaProposal((BigInteger)2, id),
            "proposal already consumed");
    }

    [TestMethod]
    public void SetAdmissionModeViaProposal_VetoedProposal_CannotExecute()
    {
        var engine = new TestEngine(true);
        var owner = engine.Sender;
        var g = Deploy(engine);
        var action = g.BuildSetAdmissionModeAction((BigInteger)2)!;
        var id = ApproveToThreshold(engine, g, action, owner);

        // Owner vetoes the approved proposal (default signers == owner).
        g.CancelProposal(id);
        Assert.IsTrue(g.IsVetoed(id)!.Value);
        Assert.IsFalse(g.IsApprovedAndTimelocked(id)!.Value, "vetoed proposal is never timelock-satisfied");

        engine.PersistingBlock.Advance(TimeSpan.FromSeconds(Timelock + 1));
        Assert.ThrowsExactly<TestException>(() => g.SetAdmissionModeViaProposal((BigInteger)2, id),
            "a vetoed proposal must never execute");
        Assert.AreEqual((BigInteger)0, g.AdmissionMode!, "admission stays permissioned after veto");
    }

    [TestMethod]
    public void CancelProposal_OwnerOnly_PermanentAndIdempotent()
    {
        var engine = new TestEngine(true);
        var owner = engine.Sender;
        var g = Deploy(engine);
        var id = ApproveToThreshold(engine, g, g.BuildSetAdmissionModeAction((BigInteger)1)!, owner);

        // Non-owner cannot veto.
        engine.SetTransactionSigners(Member1);
        Assert.ThrowsExactly<TestException>(() => g.CancelProposal(id), "CancelProposal is owner-gated");
        Assert.IsFalse(g.IsVetoed(id)!.Value);

        // Unknown proposal cannot be vetoed.
        engine.SetTransactionSigners(owner);
        Assert.ThrowsExactly<TestException>(() => g.CancelProposal((BigInteger)999), "unknown proposal");

        // Owner vetoes; re-vetoing is a no-op (no fault).
        g.CancelProposal(id);
        Assert.IsTrue(g.IsVetoed(id)!.Value);
        g.CancelProposal(id);
        Assert.IsTrue(g.IsVetoed(id)!.Value, "re-veto is idempotent");
    }

    // ───────────────────────── immutable flags (set-once) ─────────────────────────

    [TestMethod]
    public void SetImmutableFlag_OwnerGated_WriteOnceNeverCleared()
    {
        var engine = new TestEngine(true);
        var owner = engine.Sender;
        var g = Deploy(engine);
        const byte flagId = 7;

        Assert.IsFalse(g.IsImmutable((BigInteger)flagId)!.Value);

        // Non-owner cannot set.
        engine.SetTransactionSigners(Member1);
        Assert.ThrowsExactly<TestException>(() => g.SetImmutableFlag((BigInteger)flagId),
            "SetImmutableFlag is owner-gated");
        Assert.IsFalse(g.IsImmutable((BigInteger)flagId)!.Value);

        // Owner sets; setting twice is an idempotent no-op (still set, no fault, no clear path).
        engine.SetTransactionSigners(owner);
        g.SetImmutableFlag((BigInteger)flagId);
        Assert.IsTrue(g.IsImmutable((BigInteger)flagId)!.Value);
        g.SetImmutableFlag((BigInteger)flagId);
        Assert.IsTrue(g.IsImmutable((BigInteger)flagId)!.Value, "flag stays set — there is no clear path");
    }

    [TestMethod]
    public void SetImmutableFlagViaProposal_BindsPayload_ReplayProtected()
    {
        var engine = new TestEngine(true);
        var owner = engine.Sender;
        var g = Deploy(engine);
        const byte flagId = 9;
        var action = g.BuildSetImmutableFlagAction((BigInteger)flagId)!;
        var id = ApproveToThreshold(engine, g, action, owner);

        // Cannot execute before timelock.
        Assert.ThrowsExactly<TestException>(() => g.SetImmutableFlagViaProposal((BigInteger)flagId, id),
            "cannot execute before timelock");

        engine.PersistingBlock.Advance(TimeSpan.FromSeconds(Timelock + 1));

        // Payload binding: voted on flagId 9; executing with flagId 8 must be rejected.
        Assert.ThrowsExactly<TestException>(() => g.SetImmutableFlagViaProposal((BigInteger)8, id),
            "payload binding: executed flagId must equal the voted flagId");

        g.SetImmutableFlagViaProposal((BigInteger)flagId, id);
        Assert.IsTrue(g.IsImmutable((BigInteger)flagId)!.Value, "flag set via proposal");

        // Replay protection.
        Assert.ThrowsExactly<TestException>(() => g.SetImmutableFlagViaProposal((BigInteger)flagId, id),
            "proposal already consumed");
    }

    [TestMethod]
    public void ActionTags_AreDomainSeparated()
    {
        var engine = new TestEngine(true);
        var g = Deploy(engine);

        var admission = g.BuildSetAdmissionModeAction((BigInteger)2)!;
        var immutable = g.BuildSetImmutableFlagAction((BigInteger)2)!;
        var rotation = g.BuildRotateCouncilAction(new ECPoint[] { NewMember1, NewMember2 }, (BigInteger)2)!;

        // Same trailing arg byte but different method-id prefix -> different bytes. This is what
        // prevents an approved "set admission mode" proposal from being replayed as "set immutable flag".
        CollectionAssert.AreNotEqual(admission, immutable,
            "distinct *ViaProposal actions must encode to distinct payloads");
        CollectionAssert.AreNotEqual(admission, rotation);
        CollectionAssert.AreNotEqual(immutable, rotation);
    }

    // ───────────────────────── council rotation ─────────────────────────

    [TestMethod]
    public void BuildRotateCouncilAction_EncodesEpochThresholdCountAndOrderedMembers()
    {
        var engine = new TestEngine(true);
        var governance = Deploy(engine);
        var action = governance.BuildRotateCouncilAction(
            new ECPoint[] { NewMember1, NewMember2 }, (BigInteger)2)!;

        Assert.AreEqual(107, action.Length);
        CollectionAssert.AreEqual(System.Text.Encoding.ASCII.GetBytes("neo4-gov:rotateCouncil:v1"),
            action[..25]);
        CollectionAssert.AreEqual(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }, action[25..33]);
        CollectionAssert.AreEqual(new byte[] { 2, 0, 0, 0 }, action[33..37]);
        CollectionAssert.AreEqual(new byte[] { 2, 0, 0, 0 }, action[37..41]);
        CollectionAssert.AreEqual(NewMember1.EncodePoint(true), action[41..74]);
        CollectionAssert.AreEqual(NewMember2.EncodePoint(true), action[74..107]);

        var reversed = governance.BuildRotateCouncilAction(
            new ECPoint[] { NewMember2, NewMember1 }, (BigInteger)2)!;
        CollectionAssert.AreNotEqual(action, reversed, "the complete ordered member list is bound");
    }

    [TestMethod]
    public void BuildRotateCouncilAction_RejectsDuplicateThresholdAndBoundViolations()
    {
        var engine = new TestEngine(true);
        var governance = Deploy(engine);

        Assert.ThrowsExactly<TestException>(() => governance.BuildRotateCouncilAction(
            new ECPoint[] { NewMember1, NewMember1 }, (BigInteger)1), "duplicate new members rejected");
        Assert.ThrowsExactly<TestException>(() => governance.BuildRotateCouncilAction(
            new ECPoint[] { NewMember1 }, (BigInteger)0), "zero threshold rejected");
        Assert.ThrowsExactly<TestException>(() => governance.BuildRotateCouncilAction(
            new ECPoint[] { NewMember1 }, (BigInteger)2), "threshold above member count rejected");
        Assert.ThrowsExactly<TestException>(() => governance.BuildRotateCouncilAction(
            CreateDistinctMembers(65), (BigInteger)1), "more than 64 members rejected");
    }

    [TestMethod]
    public void RotateCouncil_RejectsIncompleteDuplicateAndInvalidSets()
    {
        var engine = new TestEngine(true);
        var governance = Deploy(engine);
        var newMembers = new ECPoint[] { NewMember1, NewMember2 };
        var proposalId = ApproveToThreshold(engine, governance,
            governance.BuildRotateCouncilAction(newMembers, (BigInteger)2)!, engine.Sender);
        engine.PersistingBlock.Advance(TimeSpan.FromSeconds(Timelock + 1));

        Assert.ThrowsExactly<TestException>(() => governance.RotateCouncil(
            new ECPoint[] { Member1 }, newMembers, (BigInteger)2, proposalId),
            "partial old snapshot rejected");
        Assert.ThrowsExactly<TestException>(() => governance.RotateCouncil(
            new ECPoint[] { Member1, Member1 }, newMembers, (BigInteger)2, proposalId),
            "duplicate old snapshot rejected");
        Assert.ThrowsExactly<TestException>(() => governance.RotateCouncil(
            new ECPoint[] { Member1, NonMember }, newMembers, (BigInteger)2, proposalId),
            "old snapshot must equal the current set");
        Assert.ThrowsExactly<TestException>(() => governance.RotateCouncil(
            new ECPoint[] { Member1, Member2 }, new ECPoint[] { NewMember1, NewMember1 },
            (BigInteger)1, proposalId), "duplicate new members rejected");
        Assert.ThrowsExactly<TestException>(() => governance.RotateCouncil(
            new ECPoint[] { Member1, Member2 }, newMembers, (BigInteger)0, proposalId),
            "zero threshold rejected");
        Assert.ThrowsExactly<TestException>(() => governance.RotateCouncil(
            new ECPoint[] { Member1, Member2 }, newMembers, (BigInteger)3, proposalId),
            "threshold above new council size rejected");
        Assert.ThrowsExactly<TestException>(() => governance.RotateCouncil(
            new ECPoint[] { Member1, Member2 }, CreateDistinctMembers(65),
            (BigInteger)1, proposalId), "new council maximum enforced");

        Assert.AreEqual((BigInteger)1, governance.CouncilEpoch!);
        Assert.IsTrue(governance.IsCouncilMember(Member1)!.Value);
        Assert.IsFalse(governance.IsCouncilMember(NewMember1)!.Value);
    }

    [TestMethod]
    public void RotateCouncil_EnforcesTimelockPayloadBindingAndReplayProtection()
    {
        var engine = new TestEngine(true);
        var governance = Deploy(engine);
        var newMembers = new ECPoint[] { NewMember1, NewMember2 };
        var proposalId = ApproveToThreshold(engine, governance,
            governance.BuildRotateCouncilAction(newMembers, (BigInteger)2)!, engine.Sender);

        Assert.ThrowsExactly<TestException>(() => governance.RotateCouncil(
            new ECPoint[] { Member1, Member2 }, newMembers, (BigInteger)2, proposalId),
            "rotation cannot execute before timelock");

        engine.PersistingBlock.Advance(TimeSpan.FromSeconds(Timelock + 1));
        Assert.ThrowsExactly<TestException>(() => governance.RotateCouncil(
            new ECPoint[] { Member1, Member2 }, new ECPoint[] { NewMember2, NewMember1 },
            (BigInteger)2, proposalId), "ordered payload mismatch rejected");
        Assert.ThrowsExactly<TestException>(() => governance.RotateCouncil(
            new ECPoint[] { Member1, Member2 }, newMembers, (BigInteger)1, proposalId),
            "threshold payload mismatch rejected");

        governance.RotateCouncil(
            new ECPoint[] { Member1, Member2 }, newMembers, (BigInteger)2, proposalId);

        Assert.AreEqual((BigInteger)2, governance.CouncilEpoch!);
        Assert.AreEqual((BigInteger)2, governance.CouncilCount!);
        Assert.AreEqual((BigInteger)2, governance.Threshold!);
        Assert.IsFalse(governance.IsCouncilMember(Member1)!.Value);
        Assert.IsFalse(governance.IsCouncilMember(Member2)!.Value);
        Assert.IsTrue(governance.IsCouncilMember(NewMember1)!.Value);
        Assert.IsTrue(governance.IsCouncilMember(NewMember2)!.Value);

        Assert.ThrowsExactly<TestException>(() => governance.RotateCouncil(
            new ECPoint[] { NewMember1, NewMember2 }, newMembers, (BigInteger)2, proposalId),
            "the same proposal id cannot execute twice");
    }

    [TestMethod]
    public void RotateCouncil_InvalidatesAllOldEpochProposals_AndNewCouncilTakesControl()
    {
        var engine = new TestEngine(true);
        var governance = Deploy(engine);
        var oldAdmissionProposal = ApproveToThreshold(engine, governance,
            governance.BuildSetAdmissionModeAction((BigInteger)2)!, engine.Sender);
        var newMembers = new ECPoint[] { NewMember1, NewMember2 };
        var rotationProposal = ApproveToThreshold(engine, governance,
            governance.BuildRotateCouncilAction(newMembers, (BigInteger)2)!, engine.Sender);
        engine.PersistingBlock.Advance(TimeSpan.FromSeconds(Timelock + 1));

        governance.RotateCouncil(
            new ECPoint[] { Member1, Member2 }, newMembers, (BigInteger)2, rotationProposal);

        Assert.IsFalse(governance.IsApprovedAndTimelocked(oldAdmissionProposal)!.Value,
            "old approvals cannot authorize actions in a new epoch");
        Assert.AreEqual((BigInteger)ExpiredProposalStage, governance.GetProposalStage(oldAdmissionProposal)!);
        Assert.ThrowsExactly<TestException>(() => governance.SetAdmissionModeViaProposal(
            (BigInteger)2, oldAdmissionProposal), "old approved proposal cannot execute after rotation");

        engine.SetTransactionSigners(Member1);
        Assert.ThrowsExactly<TestException>(() => governance.CreateProposal(Member1,
            governance.BuildSetImmutableFlagAction((BigInteger)11)!), "removed member loses authority immediately");

        engine.SetTransactionSigners(NewMember1);
        var newProposal = governance.CreateProposal(NewMember1,
            governance.BuildSetImmutableFlagAction((BigInteger)11))!.Value;
        Assert.AreEqual((BigInteger)2, governance.GetProposalEpoch(newProposal)!);
        Assert.AreEqual((BigInteger)1, governance.Approve(newProposal, NewMember1)!);
        engine.SetTransactionSigners(NewMember2);
        Assert.AreEqual((BigInteger)2, governance.Approve(newProposal, NewMember2)!);
    }

    [TestMethod]
    public void RotateCouncil_TwoOfThreeRecoversAfterOneSignerLost()
    {
        var engine = new TestEngine(true);
        var oldMembers = new ECPoint[] { Member1, Member2, NonMember };
        var governance = Deploy(engine, threshold: 2, members: oldMembers);
        var newMembers = new ECPoint[] { NewMember1, NewMember2 };

        var rotationProposal = ApproveToThreshold(engine, governance,
            governance.BuildRotateCouncilAction(newMembers, (BigInteger)2)!, engine.Sender);
        engine.PersistingBlock.Advance(TimeSpan.FromSeconds(Timelock + 1));
        governance.RotateCouncil(oldMembers, newMembers, (BigInteger)2, rotationProposal);

        Assert.AreEqual((BigInteger)2, governance.CouncilEpoch!);
        Assert.AreEqual((BigInteger)2, governance.CouncilCount!);
        Assert.AreEqual((BigInteger)2, governance.Threshold!);
        Assert.IsFalse(governance.IsCouncilMember(Member1)!.Value);
        Assert.IsFalse(governance.IsCouncilMember(Member2)!.Value);
        Assert.IsFalse(governance.IsCouncilMember(NonMember)!.Value,
            "the unavailable member is removed without participating in recovery");

        engine.SetTransactionSigners(NonMember);
        Assert.ThrowsExactly<TestException>(() => governance.CreateProposal(NonMember,
            governance.BuildSetImmutableFlagAction((BigInteger)13)!),
            "the removed unavailable signer cannot regain authority");

        engine.SetTransactionSigners(NewMember1);
        var newProposal = governance.CreateProposal(NewMember1,
            governance.BuildSetImmutableFlagAction((BigInteger)13))!.Value;
        Assert.AreEqual((BigInteger)1, governance.Approve(newProposal, NewMember1)!);
        engine.SetTransactionSigners(NewMember2);
        Assert.AreEqual((BigInteger)2, governance.Approve(newProposal, NewMember2)!);
    }

    [TestMethod]
    public void Approve_CurrentMemberCannotApproveProposalFromPreviousEpoch()
    {
        var engine = new TestEngine(true);
        var governance = Deploy(engine);

        engine.SetTransactionSigners(Member1);
        var oldProposal = governance.CreateProposal(Member1,
            governance.BuildSetImmutableFlagAction((BigInteger)12))!.Value;
        governance.Approve(oldProposal, Member1);

        var newMembers = new ECPoint[] { Member2, NewMember1 };
        var rotationProposal = ApproveToThreshold(engine, governance,
            governance.BuildRotateCouncilAction(newMembers, (BigInteger)2)!, engine.Sender);
        engine.PersistingBlock.Advance(TimeSpan.FromSeconds(Timelock + 1));
        governance.RotateCouncil(
            new ECPoint[] { Member1, Member2 }, newMembers, (BigInteger)2, rotationProposal);

        engine.SetTransactionSigners(Member2);
        Assert.IsTrue(governance.IsCouncilMember(Member2)!.Value, "member remains in the new council");
        Assert.ThrowsExactly<TestException>(() => governance.Approve(oldProposal, Member2),
            "membership in the new council cannot revive an old-epoch proposal");
    }

    // ───────────────────────── upgrade windows (owner-gated) ─────────────────────────

    [TestMethod]
    public void SetUpgradeWindows_OwnerGated_RejectsZero()
    {
        var engine = new TestEngine(true);
        var g = Deploy(engine);

        Assert.ThrowsExactly<TestException>(() => g.SetUpgradeWindows((BigInteger)0, (BigInteger)1, (BigInteger)1),
            "zero notice rejected");
        Assert.ThrowsExactly<TestException>(() => g.SetUpgradeWindows((BigInteger)1, (BigInteger)0, (BigInteger)1),
            "zero execution window rejected");
        Assert.ThrowsExactly<TestException>(() => g.SetUpgradeWindows((BigInteger)1, (BigInteger)1, (BigInteger)0),
            "zero cooldown rejected");

        g.SetUpgradeWindows((BigInteger)10, (BigInteger)20, (BigInteger)30);
        Assert.AreEqual((BigInteger)10, g.UpgradeNoticeSeconds!);
        Assert.AreEqual((BigInteger)20, g.UpgradeExecutionWindowSeconds!);
        Assert.AreEqual((BigInteger)30, g.UpgradeCooldownSeconds!);

        // Non-owner cannot change windows.
        engine.SetTransactionSigners(Member1);
        Assert.ThrowsExactly<TestException>(() => g.SetUpgradeWindows((BigInteger)1, (BigInteger)1, (BigInteger)1),
            "SetUpgradeWindows is owner-gated");
    }

    // ───────────────────────── helpers ─────────────────────────

    /// <summary>Create a proposal with <paramref name="action"/> as payload and approve it to threshold (2),
    /// then restore the owner signer (<paramref name="owner"/>) so the caller continues under the owner
    /// witness. <paramref name="owner"/> is the deploy-time engine.Sender. Returns the id.</summary>
    private static ulong ApproveToThreshold(TestEngine engine, NeoHubGovernanceController g, byte[] action, UInt160 owner)
    {
        engine.SetTransactionSigners(Member1);
        var id = g.CreateProposal(Member1, action)!.Value;
        g.Approve((BigInteger)id, Member1);
        engine.SetTransactionSigners(Member2);
        g.Approve((BigInteger)id, Member2);
        engine.SetTransactionSigners(owner);
        return (ulong)id;
    }

    private static ECPoint[] CreateDistinctMembers(int count)
    {
        var members = new ECPoint[count];
        for (var index = 0; index < count; index++) members[index] = PointFromScalar(index + 1000);
        return members;
    }

    private static ECPoint PointFromScalar(int scalar)
    {
        var scalarBytes = new BigInteger(scalar).ToByteArray(isUnsigned: true, isBigEndian: true);
        var paddedScalar = new byte[32];
        Buffer.BlockCopy(scalarBytes, 0, paddedScalar, paddedScalar.Length - scalarBytes.Length, scalarBytes.Length);
        var point = ECCurve.Secp256r1.G * paddedScalar;
        return ECPoint.DecodePoint(point.EncodePoint(true), ECCurve.Secp256r1);
    }
}
