using System;
using System.Numerics;
using Neo;
using Neo.Cryptography.ECC;
using Neo.SmartContract.Testing;
using Neo.SmartContract.Testing.Exceptions;

namespace NeoHub.Contracts.VmTests;

/// <summary>
/// Adversarial tests for the P3-1 finding ("threshold re-check missing"):
/// <c>GovernanceController.IsApprovedAndTimelocked</c> only asserts <c>approvedAt != 0</c> and
/// never re-reads <c>GetThreshold()</c>. A proposal therefore keeps its "approved" state even
/// if the council threshold has since risen above the number of approvals it collected.
///
/// These tests pin WHY that is not exploitable, and — more importantly — they construct the
/// exact intermediate state a naive threshold re-check would have to catch:
///
///   * <see cref="StaleThresholdProposal_CannotExecute_AfterRotationRaisesThreshold"/> builds a
///     proposal whose approval count (2) is genuinely BELOW the post-rotation threshold (3),
///     while its <c>approvedAt</c> is still set, and proves the execution gate still rejects it.
///   * <see cref="PostRotationProposal_MustMeetRaisedThreshold"/> proves the RAISED threshold is
///     live for every proposal created in the new epoch (2 of 3 approvals is not enough).
///   * <see cref="StaleProposal_RelayToDownstreamContract_IsRejected"/> repeats the attack over
///     the real cross-contract relay path (GovernanceController → RollupHub) and proves the
///     downstream state is untouched.
///
/// The load-bearing invariant (verified below, not assumed) is:
///   <c>GetThreshold()</c> is a pure function of <c>GetCouncilEpoch()</c>. The threshold is
///   written in exactly two places — <c>_deploy</c> (line 195) and <c>RotateCouncil</c>
///   (line 395) — and <c>RotateCouncil</c> bumps the epoch (line 396) in the SAME invocation,
///   so the two are atomic. A proposal is bound to the epoch it was created in
///   (<c>PrefixProposalEpoch</c>, written once at creation, line 447), and
///   <c>IsApprovedAndTimelocked</c> requires that epoch to still be current (line 569).
///   Any proposal whose threshold was crossed under a different threshold is therefore dead.
///
/// Note the epoch conjunct — NOT its position in the function — is what enforces this. All the
/// guards in <c>IsApprovedAndTimelocked</c> are conjunctive "return false" checks, so reordering
/// them cannot change the outcome; what matters is that the epoch check EXISTS and that the
/// threshold/epoch mutation is atomic.
/// </summary>
[TestClass]
public class UT_GovernanceController_ThresholdRecheck_Vm
{
    // Three council members, so the threshold can be raised from 2-of-3 to 3-of-3.
    private static readonly ECPoint Member1 = PointFromScalar(201);
    private static readonly ECPoint Member2 = PointFromScalar(202);
    private static readonly ECPoint Member3 = PointFromScalar(203);

    private const uint Timelock = 100; // seconds

    private static NeoHubGovernanceController Deploy(TestEngine engine, uint threshold, params ECPoint[] members)
    {
        // GovernanceController is the largest NeoHub contract; its deployment exceeds the
        // TestEngine's default GAS budget. Mirrors UT_GovernanceController_Vm's helper.
        engine.Fee = 100_000_000_000L;
        return engine.Deploy<NeoHubGovernanceController>(
            NeoHubGovernanceController.Nef, NeoHubGovernanceController.Manifest,
            new object[] { engine.Sender, members, (BigInteger)threshold, (BigInteger)Timelock });
    }

    /// <summary>
    /// THE ATTACK. A proposal is approved under a 2-of-3 threshold, then the council rotates and
    /// raises the threshold to 3-of-3. The proposal's approval count (2) is now BELOW the live
    /// threshold (3) — exactly the "stale threshold" state a naive re-check would have to catch.
    /// Its <c>approvedAt</c> is still set, so <c>IsApprovedAndTimelocked</c> would return TRUE if
    /// it did not consult the epoch.
    /// </summary>
    [TestMethod]
    public void StaleThresholdProposal_CannotExecute_AfterRotationRaisesThreshold()
    {
        var engine = new TestEngine(true);
        var governance = Deploy(engine, threshold: 2, Member1, Member2, Member3);

        // --- Build the attack target: a proposal that reaches the CURRENT (2-of-3) threshold.
        var action = governance.BuildSetAdmissionModeAction((BigInteger)1)!;
        engine.SetTransactionSigners(Member1);
        var staleProposal = governance.CreateProposal(Member1, action)!.Value;
        governance.Approve((BigInteger)staleProposal, Member1);
        engine.SetTransactionSigners(Member2);
        governance.Approve((BigInteger)staleProposal, Member2);
        engine.SetTransactionSigners(engine.Sender);

        Assert.AreEqual((BigInteger)2, governance.GetApprovalCount((BigInteger)staleProposal)!);
        Assert.IsTrue(governance.GetApprovedAt((BigInteger)staleProposal)! > (BigInteger)0,
            "approvedAt is set — the proposal really did cross the live threshold");

        engine.PersistingBlock.Advance(TimeSpan.FromSeconds(Timelock + 1));

        // Positive control: BEFORE the rotation the proposal is genuinely executable. Without this
        // the negative assertions below would be vacuous.
        Assert.IsTrue(governance.IsApprovedAndTimelocked((BigInteger)staleProposal)!.Value,
            "pre-rotation: the proposal IS approved + timelocked (attack precondition holds)");

        // --- Rotate the council, raising the threshold from 2 to 3 while keeping the members.
        // The rotation action binds (epoch, threshold, count, ordered members), so the payload must
        // be built in the same epoch in which the rotation executes.
        var rotationAction = governance.BuildRotateCouncilAction(
            new ECPoint[] { Member1, Member2, Member3 }, (BigInteger)3)!;
        engine.SetTransactionSigners(Member1);
        var rotationProposal = governance.CreateProposal(Member1, rotationAction)!.Value;
        governance.Approve((BigInteger)rotationProposal, Member1);
        engine.SetTransactionSigners(Member2);
        governance.Approve((BigInteger)rotationProposal, Member2);
        engine.SetTransactionSigners(engine.Sender);
        engine.PersistingBlock.Advance(TimeSpan.FromSeconds(Timelock + 1));

        governance.RotateCouncil(
            new ECPoint[] { Member1, Member2, Member3 },
            new ECPoint[] { Member1, Member2, Member3 },
            (BigInteger)3, (BigInteger)rotationProposal);

        Assert.AreEqual((BigInteger)3, governance.Threshold!, "threshold raised to 3-of-3");
        Assert.AreEqual((BigInteger)2, governance.CouncilEpoch!, "epoch advanced 1 -> 2");

        // --- The stale-threshold state is genuinely reachable: the proposal is still "approved"
        // (approvedAt != 0) and its approval count is now BELOW the live threshold.
        Assert.IsTrue(governance.GetApprovedAt((BigInteger)staleProposal)! > (BigInteger)0,
            "approvedAt survives the rotation — the threshold re-check is genuinely absent");
        Assert.AreEqual((BigInteger)2, governance.GetApprovalCount((BigInteger)staleProposal)!);
        Assert.IsTrue(governance.GetApprovalCount((BigInteger)staleProposal)! < governance.Threshold!,
            "the stale proposal's 2 approvals no longer satisfy the live 3-of-3 threshold");

        // --- ...and the epoch conjunct is what stops it.
        Assert.AreEqual((BigInteger)1, governance.GetProposalEpoch((BigInteger)staleProposal)!,
            "the proposal stays bound to its creation epoch");
        Assert.IsFalse(governance.IsApprovedAndTimelocked((BigInteger)staleProposal)!.Value,
            "a proposal approved under a superseded threshold must NOT satisfy the execution gate");
        Assert.AreEqual((BigInteger)5, governance.GetProposalStage((BigInteger)staleProposal)!,
            "stage is Expired (5)");

        Assert.ThrowsExactly<TestException>(
            () => governance.SetAdmissionModeViaProposal((BigInteger)1, (BigInteger)staleProposal),
            "the stale-threshold proposal must not be executable");
        Assert.AreEqual((BigInteger)0, governance.AdmissionMode!,
            "admission mode unchanged — the stale proposal had no effect");
    }

    /// <summary>
    /// The raised threshold is LIVE for every proposal created in the new epoch: 2 of 3 approvals
    /// no longer records <c>approvedAt</c>. This closes the other direction of the finding — a
    /// threshold that is only enforced at approval time would let a 2-approval proposal through
    /// in the new epoch.
    /// </summary>
    [TestMethod]
    public void PostRotationProposal_MustMeetRaisedThreshold()
    {
        var engine = new TestEngine(true);
        var governance = Deploy(engine, threshold: 2, Member1, Member2, Member3);

        var rotationAction = governance.BuildRotateCouncilAction(
            new ECPoint[] { Member1, Member2, Member3 }, (BigInteger)3)!;
        engine.SetTransactionSigners(Member1);
        var rotationProposal = governance.CreateProposal(Member1, rotationAction)!.Value;
        governance.Approve((BigInteger)rotationProposal, Member1);
        engine.SetTransactionSigners(Member2);
        governance.Approve((BigInteger)rotationProposal, Member2);
        engine.SetTransactionSigners(engine.Sender);
        engine.PersistingBlock.Advance(TimeSpan.FromSeconds(Timelock + 1));
        governance.RotateCouncil(
            new ECPoint[] { Member1, Member2, Member3 },
            new ECPoint[] { Member1, Member2, Member3 },
            (BigInteger)3, (BigInteger)rotationProposal);

        // New epoch, new threshold of 3. A fresh proposal needs all three members.
        var action = governance.BuildSetAdmissionModeAction((BigInteger)1)!;
        engine.SetTransactionSigners(Member1);
        var proposal = governance.CreateProposal(Member1, action)!.Value;
        governance.Approve((BigInteger)proposal, Member1);
        engine.SetTransactionSigners(Member2);
        governance.Approve((BigInteger)proposal, Member2);
        engine.SetTransactionSigners(engine.Sender);

        Assert.AreEqual((BigInteger)2, governance.GetApprovalCount((BigInteger)proposal)!);
        Assert.AreEqual((BigInteger)0, governance.GetApprovedAt((BigInteger)proposal)!,
            "2 of 3 does NOT reach the raised threshold — approvedAt must stay unset");

        engine.PersistingBlock.Advance(TimeSpan.FromSeconds(Timelock + 1));
        Assert.IsFalse(governance.IsApprovedAndTimelocked((BigInteger)proposal)!.Value,
            "a 2-of-3 proposal cannot pass the gate while the live threshold is 3-of-3");
        Assert.ThrowsExactly<TestException>(
            () => governance.SetAdmissionModeViaProposal((BigInteger)1, (BigInteger)proposal),
            "under-approved proposal must not execute");

        // The third approval completes the quorum and the proposal becomes executable. The
        // timelock clock starts when the threshold is first crossed, i.e. at this third approval,
        // so the block must be advanced again from here.
        engine.SetTransactionSigners(Member3);
        governance.Approve((BigInteger)proposal, Member3);
        engine.SetTransactionSigners(engine.Sender);
        Assert.IsTrue(governance.GetApprovedAt((BigInteger)proposal)! > (BigInteger)0,
            "3 of 3 reaches the raised threshold");
        engine.PersistingBlock.Advance(TimeSpan.FromSeconds(Timelock + 1));
        Assert.IsTrue(governance.IsApprovedAndTimelocked((BigInteger)proposal)!.Value);
        governance.SetAdmissionModeViaProposal((BigInteger)1, (BigInteger)proposal);
        Assert.AreEqual((BigInteger)1, governance.AdmissionMode!);
    }

    /// <summary>
    /// The same attack over the REAL cross-contract relay path: GovernanceController
    /// (<c>RelayGovernanceAction</c>) → RollupHub (<c>executeGovernanceProposal</c>). The stale
    /// proposal must be rejected by the GovernanceController gate before the relay, and the
    /// downstream contract's state must be completely untouched.
    /// </summary>
    [TestMethod]
    public void StaleProposal_RelayToDownstreamContract_IsRejected()
    {
        var engine = new TestEngine(true);
        var governance = Deploy(engine, threshold: 2, Member1, Member2, Member3);

        // A RollupHub with a governance-locked, GC-wired setup and two registered chains.
        var verifierHash = UInt160.Parse("0x" + new string('5', 40));
        var sharedBridgeHash = UInt160.Parse("0x" + new string('6', 40));
        var hub = engine.Deploy<NeoHubRollupHub>(NeoHubRollupHub.Nef, NeoHubRollupHub.Manifest,
            new object[] { engine.Sender, verifierHash });
        const uint liveChainId = 1001;
        const uint staleChainId = 1002;
        hub.RegisterChain(liveChainId, ChainConfig(liveChainId), new UInt256(Fill32(0x11)));
        hub.RegisterChain(staleChainId, ChainConfig(staleChainId), new UInt256(Fill32(0x22)));
        hub.GovernanceController = governance.Hash;
        hub.SharedBridge = sharedBridgeHash;
        hub.LockGovernance();
        governance.RegisterGovernanceTarget(hub.Hash);

        Assert.IsTrue(hub.IsActive(liveChainId)!.Value, "precondition: chain starts active");

        // --- Positive control: a live proposal relays through to the downstream contract and
        // actually deactivates the chain. Without this the negative assertion below is vacuous.
        var liveAction = hub.BuildSetChainActiveAction((BigInteger)liveChainId, false)!;
        engine.SetTransactionSigners(Member1);
        var liveProposal = governance.CreateProposal(Member1, liveAction)!.Value;
        governance.Approve((BigInteger)liveProposal, Member1);
        engine.SetTransactionSigners(Member2);
        governance.Approve((BigInteger)liveProposal, Member2);
        engine.SetTransactionSigners(engine.Sender);
        engine.PersistingBlock.Advance(TimeSpan.FromSeconds(Timelock + 1));

        governance.RelayGovernanceAction((BigInteger)0, liveAction, (BigInteger)liveProposal);
        Assert.IsFalse(hub.IsActive(liveChainId)!.Value,
            "the relay path works end-to-end: the live proposal deactivated the downstream chain");

        // --- The attack, over the same relay path, after the threshold was raised.
        var staleAction = hub.BuildSetChainActiveAction((BigInteger)staleChainId, false)!;
        engine.SetTransactionSigners(Member1);
        var staleProposal = governance.CreateProposal(Member1, staleAction)!.Value;
        governance.Approve((BigInteger)staleProposal, Member1);
        engine.SetTransactionSigners(Member2);
        governance.Approve((BigInteger)staleProposal, Member2);
        engine.SetTransactionSigners(engine.Sender);
        engine.PersistingBlock.Advance(TimeSpan.FromSeconds(Timelock + 1));

        var rotationAction = governance.BuildRotateCouncilAction(
            new ECPoint[] { Member1, Member2, Member3 }, (BigInteger)3)!;
        engine.SetTransactionSigners(Member1);
        var rotationProposal = governance.CreateProposal(Member1, rotationAction)!.Value;
        governance.Approve((BigInteger)rotationProposal, Member1);
        engine.SetTransactionSigners(Member2);
        governance.Approve((BigInteger)rotationProposal, Member2);
        engine.SetTransactionSigners(engine.Sender);
        engine.PersistingBlock.Advance(TimeSpan.FromSeconds(Timelock + 1));
        governance.RotateCouncil(
            new ECPoint[] { Member1, Member2, Member3 },
            new ECPoint[] { Member1, Member2, Member3 },
            (BigInteger)3, (BigInteger)rotationProposal);

        Assert.AreEqual((BigInteger)3, governance.Threshold!);
        Assert.IsTrue(governance.GetApprovedAt((BigInteger)staleProposal)! > (BigInteger)0,
            "the stale proposal is still 'approved' — the threshold re-check really is absent");
        Assert.IsTrue(governance.GetApprovalCount((BigInteger)staleProposal)! < governance.Threshold!,
            "its 2 approvals are below the live 3-of-3 threshold");

        Assert.ThrowsExactly<TestException>(
            () => governance.RelayGovernanceAction((BigInteger)0, staleAction, (BigInteger)staleProposal),
            "the relay must reject the stale-threshold proposal");
        Assert.IsTrue(hub.IsActive(staleChainId)!.Value,
            "the downstream contract state is untouched by the rejected relay");
    }

    /// <summary>
    /// Minimal 91-byte RollupHub chain config. The embedded chainId (offset 0) must match the
    /// registration id, and <c>OffsetActive</c> (90) must be 1 for <c>IsActive</c> to report true.
    /// </summary>
    private static byte[] ChainConfig(uint chainId)
    {
        var config = new byte[91];
        config[0] = (byte)chainId;
        config[1] = (byte)(chainId >> 8);
        config[2] = (byte)(chainId >> 16);
        config[3] = (byte)(chainId >> 24);
        config[90] = 1; // OffsetActive
        return config;
    }

    /// <summary>32-byte genesis-root filler (non-zero — RollupHub rejects a zero genesis root).</summary>
    private static byte[] Fill32(byte fill)
    {
        var bytes = new byte[32];
        for (var i = 0; i < bytes.Length; i++) bytes[i] = fill;
        return bytes;
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
