using System;
using System.ComponentModel;
using System.Numerics;
using Moq;
using Neo;
using Neo.SmartContract.Testing;
using Neo.SmartContract.Testing.Exceptions;

namespace NeoHub.Contracts.VmTests;

/// <summary>Minimal fraud-verifier surface so OptimisticChallenge's verifier hand-off can be mocked.
/// The contract trusts this call's boolean answer, which is exactly why the on-chain allowlist gate
/// (RegisterFraudVerifier / IsApprovedFraudVerifier) must protect it — these tests pin that gate.</summary>
public abstract class Mock_OptimisticChallenge_Verifier(SmartContractInitialize initialize) : SmartContract(initialize)
{
    [DisplayName("verifyFraud")]
    public abstract bool? VerifyFraud(BigInteger? chainId, BigInteger? batchNumber, byte[]? fraudProofBytes);

    [DisplayName("getSettlementManager")]
    public abstract UInt160? GetSettlementManager();

    [DisplayName("getExecutorSemanticId")]
    public abstract UInt256? GetExecutorSemanticId();

    [DisplayName("getReplayDomain")]
    public abstract UInt256? GetReplayDomain();
}

/// <summary>Minimal GovernanceController surface so the §16 council-veto proposal paths can be
/// mocked. The contract consults BOTH read-only checks on the wired hash:
/// <c>isApprovedAndTimelocked</c> (council multisig + timelock) and <c>matchesProposalPayload</c>
/// (binds the voted payload to the exact action args about to be applied).</summary>
public abstract class Mock_OptimisticChallenge_GovernanceController(SmartContractInitialize initialize) : SmartContract(initialize)
{
    [DisplayName("isApprovedAndTimelocked")]
    public abstract bool? IsApprovedAndTimelocked(BigInteger? proposalId);

    [DisplayName("matchesProposalPayload")]
    public abstract bool? MatchesProposalPayload(BigInteger? proposalId, byte[]? expectedAction);
}

/// <summary>
/// VM-level tests for NeoHub.OptimisticChallenge — the Phase-3 optimistic-rollup challenge window.
/// Executes the open-window / challenge / finalize paths in a real NeoVM (SettlementManager,
/// SequencerBond, and the fraud verifier mocked) and pins the security-critical invariants:
///   * _deploy rejects zero owner / settlement-manager / sequencer-bond wiring.
///   * OpenWindow is settlement-manager-witness-gated, validates chainId&gt;0 and a non-zero
///     sequencer, and is open-once (no re-arming an already-open window).
///   * The challenger-reward / window-seconds / ownership setters are owner-gated (positive AND
///     negative) and bounds-checked.
///   * Challenge enforces the CRITICAL fraud-verifier allowlist gate (an un-approved "yes-verifier"
///     cannot drain a bond), the exact permissionless executable-profile gate, the challenger witness, a
///     non-empty proof, the open+unexpired window, and is replay-protected (already-accepted).
///   * A successful Challenge records the accepted-fraud marker, which both blocks a second challenge
///     and blocks FinalizeIfPastWindow (a challenged batch can never be finalized).
///   * FinalizeIfPastWindow only runs once the deadline has elapsed and only on an open window.
///   * §16 production governance lock: owner-gated, one-way LockGovernance (refuses to lock before a
///     GovernanceController is wired) permanently disables the instant allowlist add / profile bind /
///     revoke paths and freezes the controller hash, while the council proposal twins
///     (RegisterFraudVerifierViaProposal and friends) stay open and stay replay-protected +
///     payload-bound.
/// </summary>
[TestClass]
public class UT_OptimisticChallenge_Vm
{
    private const uint ChainId = 1001;
    private const ulong BatchNum = 7;
    private static readonly UInt160 Sequencer = UInt160.Parse("0x" + new string('9', 40));
    private static readonly UInt160 Challenger = UInt160.Parse("0x" + new string('c', 40));
    private static readonly byte[] Proof = { 0x03, 0xAD, 0xBE, 0xEF };
    private static readonly UInt256 ExecutorSemanticId = RestrictedFraudProofV4TestData.ExecutorSemanticId;
    private static readonly UInt256 ReplayDomain = RestrictedFraudProofV4TestData.ReplayDomain;
    private static readonly UInt256 ClaimId = UInt256.Parse("0x" + new string('d', 64));

    // The default window from the contract (1h). OpenWindow uses GetWindowSeconds() for the deadline.
    private const uint DefaultWindow = 3600;

    // ---- helpers -----------------------------------------------------------------------------

    /// <summary>Deploy OptimisticChallenge. owner/settlementManager default to engine.Sender so the
    /// owner and settlement-manager witness checks pass. Pass an explicit <paramref name="owner"/> or
    /// <paramref name="settlementManager"/> to exercise the negative authorization paths. SM and bond
    /// are wired to the two mock hashes (smHash / sbHash) so the cross-contract calls resolve.</summary>
    private static NeoHubOptimisticChallenge Deploy(TestEngine engine, UInt160 smHash, UInt160 sbHash,
        UInt160? owner = null, UInt160? settlementManager = null, bool witnessSm = true)
    {
        var sender = engine.Sender;
        var o = owner ?? sender;
        var sm = settlementManager ?? smHash;
        var oc = engine.Deploy<NeoHubOptimisticChallenge>(
            NeoHubOptimisticChallenge.Nef, NeoHubOptimisticChallenge.Manifest,
            new object[] { o, sm, sbHash });
        // OpenWindow is gated on CheckWitness(settlementManager). The SM is a *contract* hash (also
        // wired as a mock for revertBatch/finalizeBatch), so to let the SM-witnessed paths run we add
        // it as a transaction signer alongside the deployer. witnessSm:false exercises the negative
        // "caller is not the settlement manager" path.
        if (witnessSm) engine.SetTransactionSigners(sender, sm);
        return oc;
    }

    private static UInt160 Hash(char c) => UInt160.Parse("0x" + new string(c, 40));

    /// <summary>Wire a SettlementManager mock (revertBatch + finalizeBatch are void no-ops).</summary>
    private static void WireSm(TestEngine engine, UInt160 smHash, Func<bool>? failRevert = null) =>
        engine.FromHash<NeoHubSettlementManager>(smHash, m =>
        {
            m.Setup(c => c.RevertBatch(It.IsAny<BigInteger?>(), It.IsAny<BigInteger?>()))
                .Callback(() =>
                {
                    if (failRevert?.Invoke() == true) throw new InvalidOperationException("revert failed");
                });
            m.Setup(c => c.FinalizeBatch(It.IsAny<BigInteger?>(), It.IsAny<BigInteger?>()));
        }, checkExistence: false);

    /// <summary>Wire a SequencerBond mock with a fixed balance and a no-op slash.</summary>
    private static void WireBond(
        TestEngine engine,
        UInt160 sbHash,
        BigInteger balance,
        Action<BigInteger, UInt160>? onSlash = null) =>
        engine.FromHash<NeoHubSequencerBond>(sbHash, m =>
        {
            m.Setup(c => c.GetBalance(It.IsAny<BigInteger?>(), It.IsAny<UInt160?>())).Returns(balance);
            m.Setup(c => c.Slash(
                    It.IsAny<BigInteger?>(),
                    It.IsAny<UInt160?>(),
                    It.IsAny<BigInteger?>(),
                    It.IsAny<UInt160?>()))
                .Callback((BigInteger? _, UInt160? _, BigInteger? amount, UInt160? beneficiary) =>
                    onSlash?.Invoke(amount!.Value, beneficiary!));
        }, checkExistence: false);

    /// <summary>Wire a fraud-verifier mock that returns <paramref name="verdict"/> from verifyFraud.</summary>
    private static void WireVerifier(
        TestEngine engine,
        UInt160 verifierHash,
        bool verdict,
        UInt160? settlementManager = null,
        UInt256? executorSemanticId = null,
        UInt256? replayDomain = null) =>
        engine.FromHash<Mock_OptimisticChallenge_Verifier>(verifierHash, m =>
        {
            m.Setup(c => c.VerifyFraud(It.IsAny<BigInteger?>(), It.IsAny<BigInteger?>(), It.IsAny<byte[]?>()))
                .Returns(verdict);
            m.Setup(c => c.GetSettlementManager()).Returns(settlementManager ?? Hash('5'));
            m.Setup(c => c.GetExecutorSemanticId()).Returns(executorSemanticId ?? ExecutorSemanticId);
            m.Setup(c => c.GetReplayDomain()).Returns(replayDomain ?? ReplayDomain);
        }, checkExistence: false);

    /// <summary>Wire a GovernanceController mock whose two read-only proposal checks answer
    /// <paramref name="approved"/> and <paramref name="payloadMatches"/> independently, so a test
    /// can pin the council-approval gate and the payload-binding gate separately.</summary>
    private static void WireGc(
        TestEngine engine, UInt160 gcHash, bool approved = true, bool payloadMatches = true) =>
        engine.FromHash<Mock_OptimisticChallenge_GovernanceController>(gcHash, m =>
        {
            m.Setup(c => c.IsApprovedAndTimelocked(It.IsAny<BigInteger?>())).Returns(approved);
            m.Setup(c => c.MatchesProposalPayload(It.IsAny<BigInteger?>(), It.IsAny<byte[]?>()))
                .Returns(payloadMatches);
        }, checkExistence: false);

    private static byte[] V4Proof(
        UInt256? replayDomain = null,
        UInt256? executorSemanticId = null,
        UInt256? claimId = null) =>
        RestrictedFraudProofV4TestData.BuildProfileProof(
            replayDomain ?? ReplayDomain,
            executorSemanticId ?? ExecutorSemanticId,
            claimId ?? ClaimId);

    // ---- deploy validation -------------------------------------------------------------------

    [TestMethod]
    public void Deploy_RejectsZeroPrincipals()
    {
        var engine = new TestEngine(true);
        var sm = Hash('5');
        var sb = Hash('8');

        // Zero owner / settlement-manager / sequencer-bond must each fault the _deploy guards: a
        // zero sequencerBond would silently fail to slash on a successful challenge.
        Assert.ThrowsExactly<TestException>(() => engine.Deploy<NeoHubOptimisticChallenge>(
            NeoHubOptimisticChallenge.Nef, NeoHubOptimisticChallenge.Manifest,
            new object[] { UInt160.Zero, sm, sb }), "zero owner must be rejected");
        Assert.ThrowsExactly<TestException>(() => engine.Deploy<NeoHubOptimisticChallenge>(
            NeoHubOptimisticChallenge.Nef, NeoHubOptimisticChallenge.Manifest,
            new object[] { engine.Sender, UInt160.Zero, sb }), "zero settlement manager must be rejected");
        Assert.ThrowsExactly<TestException>(() => engine.Deploy<NeoHubOptimisticChallenge>(
            NeoHubOptimisticChallenge.Nef, NeoHubOptimisticChallenge.Manifest,
            new object[] { engine.Sender, sm, UInt160.Zero }), "zero sequencer bond must be rejected");
    }

    [TestMethod]
    public void Deploy_SeedsDefaultWindowAndReward()
    {
        var engine = new TestEngine(true);
        var oc = Deploy(engine, Hash('5'), Hash('8'));
        Assert.AreEqual((BigInteger)DefaultWindow, oc.WindowSeconds!, "default window is 1h");
        Assert.AreEqual((BigInteger)5000, oc.ChallengerRewardBps!, "default reward is 50%");
        Assert.AreEqual(engine.Sender, oc.Owner, "owner is the deployer");
    }

    // ---- OpenWindow: auth + input validation + open-once -------------------------------------

    [TestMethod]
    public void OpenWindow_BySettlementManager_RecordsDeadline_AndIsOpenOnce()
    {
        var engine = new TestEngine(true);
        // settlementManager defaults to engine.Sender so the SM witness check passes.
        var oc = Deploy(engine, Hash('5'), Hash('8'));

        Assert.AreEqual((BigInteger)0, oc.GetDeadline(ChainId, BatchNum)!, "no window yet");
        var deadline = oc.OpenWindow(ChainId, BatchNum, Sequencer)!;
        Assert.AreEqual(deadline, oc.GetDeadline(ChainId, BatchNum)!, "deadline is recorded");
        Assert.IsTrue(oc.IsWindowOpen(ChainId, BatchNum, (uint)deadline)!.Value, "window open at the deadline");
        Assert.IsFalse(oc.IsWindowOpen(ChainId, BatchNum, (uint)deadline + 1)!.Value, "closed one second past");

        // Re-opening the same (chain, batch) must fault — a sequencer cannot reset its own window.
        Assert.ThrowsExactly<TestException>(() => oc.OpenWindow(ChainId, BatchNum, Sequencer),
            "window already open must fault");
    }

    [TestMethod]
    public void OpenWindow_NonSettlementManager_Faults()
    {
        var engine = new TestEngine(true);
        // settlementManager is a different account than the signer -> SM witness absent.
        var oc = Deploy(engine, Hash('5'), Hash('8'), settlementManager: Hash('3'), witnessSm: false);

        Assert.ThrowsExactly<TestException>(() => oc.OpenWindow(ChainId, BatchNum, Sequencer),
            "OpenWindow is settlement-manager-gated");
        Assert.AreEqual((BigInteger)0, oc.GetDeadline(ChainId, BatchNum)!, "rejected open must not set state");
    }

    [TestMethod]
    public void OpenWindow_RejectsChainZero_AndZeroSequencer()
    {
        var engine = new TestEngine(true);
        var oc = Deploy(engine, Hash('5'), Hash('8'));

        Assert.ThrowsExactly<TestException>(() => oc.OpenWindow(0, BatchNum, Sequencer),
            "chainId 0 is the reserved L1 sentinel");
        Assert.ThrowsExactly<TestException>(() => oc.OpenWindow(ChainId, BatchNum, UInt160.Zero),
            "zero sequencer cannot be slashed later -> rejected");
    }

    // ---- owner-gated configuration setters ---------------------------------------------------

    [TestMethod]
    public void Setters_OwnerGated_PositivePath_WithBounds()
    {
        var engine = new TestEngine(true);
        var oc = Deploy(engine, Hash('5'), Hash('8')); // owner == engine.Sender

        oc.WindowSeconds = 120;
        Assert.AreEqual((BigInteger)120, oc.WindowSeconds!);
        oc.ChallengerRewardBps = 6000;
        Assert.AreEqual((BigInteger)6000, oc.ChallengerRewardBps!);

        // Bounds: window in [60s, 7d]; bps in (0, 10000].
        Assert.ThrowsExactly<TestException>(() => oc.WindowSeconds = 59, "window below 60s rejected");
        Assert.ThrowsExactly<TestException>(() => oc.WindowSeconds = 7 * 86400 + 1, "window above 7d rejected");
        Assert.ThrowsExactly<TestException>(() => oc.ChallengerRewardBps = 0, "bps 0 rejected");
        Assert.ThrowsExactly<TestException>(() => oc.ChallengerRewardBps = 10001, "bps above 10000 rejected");
    }

    [TestMethod]
    public void Setters_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        // owner is a different account than the signer -> every owner gate must reject.
        var oc = Deploy(engine, Hash('5'), Hash('8'), owner: Hash('1'));

        Assert.ThrowsExactly<TestException>(() => oc.WindowSeconds = 120, "SetWindowSeconds is owner-gated");
        Assert.ThrowsExactly<TestException>(() => oc.ChallengerRewardBps = 6000, "SetChallengerRewardBps is owner-gated");
        Assert.ThrowsExactly<TestException>(() => oc.Owner = Hash('2'), "SetOwner is owner-gated");
        Assert.ThrowsExactly<TestException>(() => oc.RegisterFraudVerifier(Hash('a')), "RegisterFraudVerifier is owner-gated");
        Assert.ThrowsExactly<TestException>(() => oc.RegisterPermissionlessFraudVerifier(Hash('a')),
            "RegisterPermissionlessFraudVerifier is owner-gated");
        Assert.ThrowsExactly<TestException>(() => oc.RevokeFraudVerifier(Hash('a')), "RevokeFraudVerifier is owner-gated");
    }

    [TestMethod]
    public void SetOwner_TransfersGovernance_RejectsZero()
    {
        var engine = new TestEngine(true);
        var oc = Deploy(engine, Hash('5'), Hash('8'));

        Assert.ThrowsExactly<TestException>(() => oc.Owner = UInt160.Zero, "zero new owner rejected");
        var newOwner = Hash('2');
        oc.Owner = newOwner;
        Assert.AreEqual(newOwner, oc.Owner, "ownership transferred");
        // Old owner (the signer) can no longer drive owner-gated calls.
        Assert.ThrowsExactly<TestException>(() => oc.WindowSeconds = 120, "old owner loses authority after transfer");
    }

    // ---- fraud-verifier allowlist ------------------------------------------------------------

    [TestMethod]
    public void RegisterFraudVerifier_TogglesAllowlist_RejectsZero()
    {
        var engine = new TestEngine(true);
        var oc = Deploy(engine, Hash('5'), Hash('8'));
        var verifier = Hash('a');

        Assert.IsFalse(oc.IsApprovedFraudVerifier(verifier)!.Value, "not approved by default");
        Assert.IsFalse(oc.IsPermissionlessFraudVerifier(verifier)!.Value, "not permissionless by default");

        Assert.ThrowsExactly<TestException>(() => oc.RegisterFraudVerifier(UInt160.Zero), "zero verifier rejected");

        oc.RegisterFraudVerifier(verifier);
        Assert.IsTrue(oc.IsApprovedFraudVerifier(verifier)!.Value, "approved after register");
        Assert.IsFalse(oc.IsPermissionlessFraudVerifier(verifier)!.Value,
            "approved-only verifier is NOT permissionless and cannot revert a batch");

        oc.RevokeFraudVerifier(verifier);
        Assert.IsFalse(oc.IsApprovedFraudVerifier(verifier)!.Value, "revoked");
    }

    [TestMethod]
    public void RegisterPermissionlessFraudProfile_BindsChainSemanticAndReplayDomain()
    {
        var engine = new TestEngine(true);
        var smHash = Hash('5');
        var oc = Deploy(engine, smHash, Hash('8'));
        var verifier = Hash('a');
        var wrongSettlementVerifier = Hash('b');
        var wrongSemanticVerifier = Hash('d');
        var wrongReplayVerifier = Hash('e');
        WireVerifier(engine, verifier, verdict: true, settlementManager: smHash);
        WireVerifier(engine, wrongSettlementVerifier, verdict: true, settlementManager: Hash('c'));
        WireVerifier(
            engine,
            wrongSemanticVerifier,
            verdict: true,
            settlementManager: smHash,
            executorSemanticId: UInt256.Parse("0x" + new string('f', 64)));
        WireVerifier(
            engine,
            wrongReplayVerifier,
            verdict: true,
            settlementManager: smHash,
            replayDomain: UInt256.Parse("0x" + new string('e', 64)));

        Assert.ThrowsExactly<TestException>(() => oc.RegisterPermissionlessFraudVerifier(verifier));
        Assert.ThrowsExactly<TestException>(() => oc.RegisterPermissionlessFraudProfile(
            ChainId,
            wrongSettlementVerifier,
            ExecutorSemanticId,
            ReplayDomain));
        Assert.ThrowsExactly<TestException>(() => oc.RegisterPermissionlessFraudProfile(
            ChainId,
            wrongSemanticVerifier,
            ExecutorSemanticId,
            ReplayDomain));
        Assert.ThrowsExactly<TestException>(() => oc.RegisterPermissionlessFraudProfile(
            ChainId,
            wrongReplayVerifier,
            ExecutorSemanticId,
            ReplayDomain));
        oc.RegisterPermissionlessFraudProfile(ChainId, verifier, ExecutorSemanticId, ReplayDomain);
        Assert.IsTrue(oc.IsApprovedFraudVerifier(verifier)!.Value);
        Assert.IsFalse(oc.IsPermissionlessFraudVerifier(verifier)!.Value);
        Assert.IsTrue(oc.IsPermissionlessFraudProfile(
            ChainId,
            verifier,
            ExecutorSemanticId,
            ReplayDomain)!.Value);
        Assert.IsFalse(oc.IsPermissionlessFraudProfile(
            ChainId + 1,
            verifier,
            ExecutorSemanticId,
            ReplayDomain)!.Value);

        oc.RegisterFraudVerifier(verifier);
        Assert.IsFalse(oc.IsPermissionlessFraudProfile(
            ChainId,
            verifier,
            ExecutorSemanticId,
            ReplayDomain)!.Value);
        oc.RegisterPermissionlessFraudProfile(ChainId, verifier, ExecutorSemanticId, ReplayDomain);
        oc.RevokeFraudVerifier(verifier);
        Assert.IsFalse(oc.IsApprovedFraudVerifier(verifier)!.Value);
        Assert.IsFalse(oc.IsPermissionlessFraudProfile(
            ChainId,
            verifier,
            ExecutorSemanticId,
            ReplayDomain)!.Value);
    }

    // ---- Challenge: the CRITICAL allowlist + executable-profile gates ------------------------

    [TestMethod]
    public void Challenge_UnapprovedVerifier_Faults_PreventsBondDrain()
    {
        // CRITICAL: an attacker-deployed "yes-verifier" that is NOT on the allowlist must not be
        // usable — otherwise anyone could drain any sequencer's bond and revert any pending batch.
        var engine = new TestEngine(true);
        var smHash = Hash('5');
        var sbHash = Hash('8');
        var oc = Deploy(engine, smHash, sbHash);
        oc.OpenWindow(ChainId, BatchNum, Sequencer);

        var rogueVerifier = Hash('a');
        WireVerifier(engine, rogueVerifier, verdict: true); // it WOULD say "fraud!" if called...

        // ...but it was never approved, so Challenge must fault before ever calling it.
        Assert.ThrowsExactly<TestException>(() =>
            oc.Challenge(ChainId, BatchNum, Challenger, Proof, rogueVerifier),
            "un-approved fraud verifier must be rejected");
    }

    [TestMethod]
    public void Challenge_ApprovedButNotPermissionless_IsRejectedEvenWithOwnerWitness()
    {
        // An approved-but-not-profile-bound verifier can never revert a value-bearing batch.
        // The transaction signer is also the owner, proving governance cannot bypass the gate.
        var engine = new TestEngine(true);
        var smHash = Hash('5');
        var sbHash = Hash('8');
        var verifier = Hash('a');
        var oc = Deploy(engine, smHash, sbHash); // owner == Sender

        WireVerifier(engine, verifier, verdict: true);
        WireSm(engine, smHash);
        WireBond(engine, sbHash, 1000);
        oc.RegisterFraudVerifier(verifier); // approved-only (NOT permissionless)
        oc.OpenWindow(ChainId, BatchNum, Sequencer);

        Assert.ThrowsExactly<TestException>(() =>
            oc.Challenge(ChainId, BatchNum, engine.Sender, Proof, verifier),
            "approved-only verifier must fail closed even with an owner witness");
    }

    [TestMethod]
    public void Challenge_LegacyVerifier_OwnerCoSignCannotRevertOrSlash()
    {
        var engine = new TestEngine(true);
        var smHash = Hash('5');
        var sbHash = Hash('8');
        var verifier = Hash('a');
        var oc = Deploy(engine, smHash, sbHash); // owner == Sender

        WireVerifier(engine, verifier, verdict: true);
        WireSm(engine, smHash);
        var slashCount = 0;
        WireBond(engine, sbHash, 1000, (_, _) => slashCount++);
        oc.RegisterFraudVerifier(verifier); // approved-only
        oc.OpenWindow(ChainId, BatchNum, Sequencer);

        Assert.ThrowsExactly<TestException>(() =>
            oc.Challenge(ChainId, BatchNum, engine.Sender, Proof, verifier));
        Assert.AreEqual(0, slashCount);
        engine.PersistingBlock.Advance(TimeSpan.FromSeconds(DefaultWindow + 10));
        oc.FinalizeIfPastWindow(ChainId, BatchNum);
    }

    [TestMethod]
    public void Challenge_V4PermissionlessProfile_AcceptsAndBlocksGlobalClaimReplay()
    {
        // Permissionless verifier: no owner co-sign needed. Full happy path with replay protection.
        var engine = new TestEngine(true);
        var smHash = Hash('5');
        var sbHash = Hash('8');
        var verifier = Hash('a');
        var oc = Deploy(engine, smHash, sbHash);

        WireVerifier(engine, verifier, verdict: true, settlementManager: smHash);
        WireSm(engine, smHash);
        WireBond(engine, sbHash, 1000);
        oc.RegisterPermissionlessFraudProfile(ChainId, verifier, ExecutorSemanticId, ReplayDomain);
        oc.OpenWindow(ChainId, BatchNum, Sequencer);
        oc.OpenWindow(ChainId, BatchNum + 1, Sequencer);
        var proof = V4Proof();

        oc.Owner = Hash('1');
        oc.Challenge(ChainId, BatchNum, engine.Sender, proof, verifier);
        Assert.IsTrue(oc.IsClaimConsumed(ClaimId)!.Value);

        Assert.ThrowsExactly<TestException>(() =>
            oc.Challenge(ChainId, BatchNum + 1, engine.Sender, proof, verifier),
            "one claim id cannot be replayed against another batch");

        engine.PersistingBlock.Advance(TimeSpan.FromSeconds(DefaultWindow + 10));
        Assert.ThrowsExactly<TestException>(() => oc.FinalizeIfPastWindow(ChainId, BatchNum),
            "challenged batch cannot be finalized");
    }

    [TestMethod]
    public void Challenge_V4ProfileMismatchAndLegacyV3_FailClosed()
    {
        var engine = new TestEngine(true);
        var smHash = Hash('5');
        var sbHash = Hash('8');
        var verifier = Hash('a');
        var oc = Deploy(engine, smHash, sbHash);
        WireVerifier(engine, verifier, verdict: true, settlementManager: smHash);
        oc.RegisterPermissionlessFraudProfile(ChainId, verifier, ExecutorSemanticId, ReplayDomain);
        oc.OpenWindow(ChainId, BatchNum, Sequencer);
        oc.Owner = Hash('1');

        Assert.ThrowsExactly<TestException>(() =>
            oc.Challenge(ChainId, BatchNum, engine.Sender, Proof, verifier));
        Assert.ThrowsExactly<TestException>(() =>
            oc.Challenge(ChainId, BatchNum, engine.Sender, V4Proof(replayDomain: UInt256.Parse("0x" + new string('e', 64))), verifier));
        Assert.ThrowsExactly<TestException>(() =>
            oc.Challenge(ChainId, BatchNum, engine.Sender, V4Proof(executorSemanticId: UInt256.Parse("0x" + new string('f', 64))), verifier));
    }

    [TestMethod]
    public void Challenge_RevertFailure_RollsBackClaimAndAcceptedMarker()
    {
        var engine = new TestEngine(true);
        var smHash = Hash('5');
        var sbHash = Hash('8');
        var verifier = Hash('a');
        var failRevert = true;
        var oc = Deploy(engine, smHash, sbHash);
        WireVerifier(engine, verifier, verdict: true, settlementManager: smHash);
        WireSm(engine, smHash, () => failRevert);
        WireBond(engine, sbHash, 1000);
        oc.RegisterPermissionlessFraudProfile(ChainId, verifier, ExecutorSemanticId, ReplayDomain);
        oc.OpenWindow(ChainId, BatchNum, Sequencer);
        oc.Owner = Hash('1');

        Assert.ThrowsExactly<TestException>(() =>
            oc.Challenge(ChainId, BatchNum, engine.Sender, V4Proof(), verifier));
        Assert.IsFalse(oc.IsClaimConsumed(ClaimId)!.Value);

        failRevert = false;
        engine.PersistingBlock.Advance(TimeSpan.FromSeconds(DefaultWindow + 10));
        oc.FinalizeIfPastWindow(ChainId, BatchNum);
    }

    [TestMethod]
    public void Challenge_OneUnitBond_SkipsZeroRewardSlashAndBurnsRemainder()
    {
        var engine = new TestEngine(true);
        var smHash = Hash('5');
        var sbHash = Hash('8');
        var verifier = Hash('a');
        var slashes = new List<(BigInteger Amount, UInt160 Beneficiary)>();
        var oc = Deploy(engine, smHash, sbHash);
        WireSm(engine, smHash);
        WireBond(engine, sbHash, 1, (amount, beneficiary) => slashes.Add((amount, beneficiary)));
        WireVerifier(engine, verifier, verdict: true, settlementManager: smHash);
        oc.RegisterPermissionlessFraudProfile(ChainId, verifier, ExecutorSemanticId, ReplayDomain);
        oc.OpenWindow(ChainId, BatchNum, Sequencer);

        oc.Challenge(ChainId, BatchNum, engine.Sender, V4Proof(), verifier);

        Assert.HasCount(1, slashes);
        Assert.AreEqual(BigInteger.One, slashes[0].Amount);
        Assert.AreEqual(UInt160.Zero, slashes[0].Beneficiary);
    }

    // ---- accepted challenge must not wedge the chain (audit C4) -------------------------------

    [TestMethod]
    public void Challenge_AcceptedProof_ConsumesWindow_SoResubmitCanReArm()
    {
        // SettlementManager.SubmitBatch explicitly invites a corrected resubmit of a reverted slot,
        // which calls OpenWindow for the same (chainId, batchNumber). While the accepted challenge
        // left the window keys behind, that call faulted "window already open" and the chain could
        // never advance again.
        var engine = new TestEngine(true);
        var smHash = Hash('5');
        var sbHash = Hash('8');
        var verifier = Hash('a');
        var oc = Deploy(engine, smHash, sbHash);
        WireSm(engine, smHash);
        WireBond(engine, sbHash, 1000);
        WireVerifier(engine, verifier, verdict: true, settlementManager: smHash);
        oc.RegisterPermissionlessFraudProfile(ChainId, verifier, ExecutorSemanticId, ReplayDomain);
        var firstDeadline = oc.OpenWindow(ChainId, BatchNum, Sequencer)!;

        oc.Challenge(ChainId, BatchNum, engine.Sender, V4Proof(), verifier);

        Assert.AreEqual((BigInteger)0, oc.GetDeadline(ChainId, BatchNum)!,
            "an accepted challenge must consume the deadline");
        Assert.IsFalse(oc.IsWindowOpen(ChainId, BatchNum, (uint)firstDeadline)!.Value,
            "the stale window must not read as open at its own deadline");

        var reArmed = oc.OpenWindow(ChainId, BatchNum, Sequencer)!;
        Assert.IsTrue(reArmed.Value > 0, "the corrected resubmit must be able to open a fresh window");
    }

    [TestMethod]
    public void Challenge_AcceptedProof_ReArmedWindow_StillRejectsSecondChallenge()
    {
        // Clearing the window must not turn a proven-fraudulent batch into a re-challengeable one:
        // the accepted-fraud marker, not the window, is the durable rail. A distinct claimId skips the
        // earlier "claim already consumed" guard so this pins the batch-level guard.
        var engine = new TestEngine(true);
        var smHash = Hash('5');
        var sbHash = Hash('8');
        var verifier = Hash('a');
        var oc = Deploy(engine, smHash, sbHash);
        WireSm(engine, smHash);
        WireBond(engine, sbHash, 1000);
        WireVerifier(engine, verifier, verdict: true, settlementManager: smHash);
        oc.RegisterPermissionlessFraudProfile(ChainId, verifier, ExecutorSemanticId, ReplayDomain);
        oc.OpenWindow(ChainId, BatchNum, Sequencer);
        oc.Challenge(ChainId, BatchNum, engine.Sender, V4Proof(), verifier);
        oc.OpenWindow(ChainId, BatchNum, Sequencer);

        var ex = Assert.ThrowsExactly<TestException>(() =>
            oc.Challenge(ChainId, BatchNum, engine.Sender,
                V4Proof(claimId: UInt256.Parse("0x" + new string('b', 64))), verifier),
            "a re-armed window must not allow a second accepted challenge on the same batch");
        StringAssert.Contains(ex.Message, "already accepted");
    }

    [TestMethod]
    public void Challenge_AcceptedProof_ReArmedWindow_StillCannotFinalize()
    {
        // The alternative fix — letting OpenWindow overwrite an expired window — would also re-open
        // finalize for batches left un-finalized. This pins that the chosen fix did not do that: the
        // challenged batch stays un-finalizable across the fresh window.
        var engine = new TestEngine(true);
        var smHash = Hash('5');
        var sbHash = Hash('8');
        var verifier = Hash('a');
        var oc = Deploy(engine, smHash, sbHash);
        WireSm(engine, smHash);
        WireBond(engine, sbHash, 1000);
        WireVerifier(engine, verifier, verdict: true, settlementManager: smHash);
        oc.RegisterPermissionlessFraudProfile(ChainId, verifier, ExecutorSemanticId, ReplayDomain);
        oc.OpenWindow(ChainId, BatchNum, Sequencer);
        oc.Challenge(ChainId, BatchNum, engine.Sender, V4Proof(), verifier);
        oc.OpenWindow(ChainId, BatchNum, Sequencer);

        engine.PersistingBlock.Advance(TimeSpan.FromSeconds(DefaultWindow + 10));
        var ex = Assert.ThrowsExactly<TestException>(() => oc.FinalizeIfPastWindow(ChainId, BatchNum),
            "a challenged batch must never finalize, even behind a re-armed window");
        StringAssert.Contains(ex.Message, "batch was challenged");
    }

    [TestMethod]
    public void Challenge_RejectedProof_DoesNotAccept_NorBlockFinalize()
    {
        // If the verifier returns false, the challenge must fault and NOT set the accepted marker —
        // the batch should still be finalizable after the window. Pins that a failed fraud proof
        // does not poison an honest batch.
        var engine = new TestEngine(true);
        var smHash = Hash('5');
        var sbHash = Hash('8');
        var verifier = Hash('a');
        var oc = Deploy(engine, smHash, sbHash);

        WireVerifier(engine, verifier, verdict: false, settlementManager: smHash); // verifier says "no fraud"
        WireSm(engine, smHash);
        WireBond(engine, sbHash, 1000);
        oc.RegisterPermissionlessFraudProfile(ChainId, verifier, ExecutorSemanticId, ReplayDomain);
        oc.OpenWindow(ChainId, BatchNum, Sequencer);

        Assert.ThrowsExactly<TestException>(() =>
            oc.Challenge(ChainId, BatchNum, engine.Sender, V4Proof(), verifier),
            "a rejected fraud proof must fault");

        // The window is intact and unchallenged -> after expiry it finalizes cleanly.
        engine.PersistingBlock.Advance(TimeSpan.FromSeconds(DefaultWindow + 10));
        oc.FinalizeIfPastWindow(ChainId, BatchNum); // must not throw
    }

    [TestMethod]
    public void Challenge_RejectsEmptyProof_AndZeroChallenger_AndZeroVerifier()
    {
        var engine = new TestEngine(true);
        var smHash = Hash('5');
        var sbHash = Hash('8');
        var verifier = Hash('a');
        var oc = Deploy(engine, smHash, sbHash);

        WireVerifier(engine, verifier, verdict: true);
        oc.RegisterFraudVerifier(verifier);
        oc.OpenWindow(ChainId, BatchNum, Sequencer);

        // Empty proof: engine.Sender is witnessed, so CheckWitness passes and the empty-proof guard
        // (`fraudProofBytes.Length > 0`) is the decider. Assert the reason so a removed guard fails here.
        var exEmpty = Assert.ThrowsExactly<TestException>(() =>
            oc.Challenge(ChainId, BatchNum, engine.Sender, Array.Empty<byte>(), verifier),
            "empty fraud proof rejected");
        StringAssert.Contains(exEmpty.Message, "empty fraud proof");

        // Zero (unwitnessable) challenger: UInt160.Zero can never be a transaction signer, so the FIRST
        // guard `CheckWitness(challenger)` aborts before the later `!challenger.IsZero` validity guard is
        // reached. The challenger-witness gate IS the real protection here (an attacker cannot forge a
        // witness for address 0, nor for any victim), so pin the abort to the witness gate.
        var exZeroChallenger = Assert.ThrowsExactly<TestException>(() =>
            oc.Challenge(ChainId, BatchNum, UInt160.Zero, Proof, verifier),
            "zero (unwitnessable) challenger rejected by the challenger-witness gate");
        StringAssert.Contains(exZeroChallenger.Message, "witness");

        // Zero fraud verifier: Sender is witnessed and the proof is non-empty + challenger valid, so the
        // decider is the `!fraudVerifier.IsZero` validity guard. Assert its reason.
        var exZeroVerifier = Assert.ThrowsExactly<TestException>(() =>
            oc.Challenge(ChainId, BatchNum, engine.Sender, Proof, UInt160.Zero),
            "zero fraud verifier rejected");
        StringAssert.Contains(exZeroVerifier.Message, "invalid fraud verifier");
    }

    [TestMethod]
    public void Challenge_NoOpenWindow_Faults()
    {
        var engine = new TestEngine(true);
        var smHash = Hash('5');
        var sbHash = Hash('8');
        var verifier = Hash('a');
        var oc = Deploy(engine, smHash, sbHash);

        WireVerifier(engine, verifier, verdict: true, settlementManager: smHash);
        oc.RegisterPermissionlessFraudProfile(ChainId, verifier, ExecutorSemanticId, ReplayDomain);
        // No OpenWindow call -> there is no window to challenge.

        Assert.ThrowsExactly<TestException>(() =>
            oc.Challenge(ChainId, BatchNum, engine.Sender, V4Proof(), verifier),
            "challenge with no open window must fault");
    }

    [TestMethod]
    public void Challenge_AfterWindowClosed_Faults()
    {
        var engine = new TestEngine(true);
        var smHash = Hash('5');
        var sbHash = Hash('8');
        var verifier = Hash('a');
        var oc = Deploy(engine, smHash, sbHash);

        WireVerifier(engine, verifier, verdict: true, settlementManager: smHash);
        WireSm(engine, smHash);
        WireBond(engine, sbHash, 1000);
        oc.RegisterPermissionlessFraudProfile(ChainId, verifier, ExecutorSemanticId, ReplayDomain);
        oc.OpenWindow(ChainId, BatchNum, Sequencer);

        // Advance past the deadline -> the window is closed -> challenge must fault.
        engine.PersistingBlock.Advance(TimeSpan.FromSeconds(DefaultWindow + 1));
        Assert.ThrowsExactly<TestException>(() =>
            oc.Challenge(ChainId, BatchNum, engine.Sender, V4Proof(), verifier),
            "challenge after the window has closed must fault");
    }

    [TestMethod]
    public void Challenge_NoBondToSlash_Faults()
    {
        // Accounting guard: with a zero current bond there is nothing to slash. Challenge must fault
        // (the "no bond to slash" precondition) rather than pay a 0 reward / revert for free.
        var engine = new TestEngine(true);
        var smHash = Hash('5');
        var sbHash = Hash('8');
        var verifier = Hash('a');
        var oc = Deploy(engine, smHash, sbHash);

        WireVerifier(engine, verifier, verdict: true, settlementManager: smHash);
        WireSm(engine, smHash);
        WireBond(engine, sbHash, 0); // sequencer has no bond
        oc.RegisterPermissionlessFraudProfile(ChainId, verifier, ExecutorSemanticId, ReplayDomain);
        oc.OpenWindow(ChainId, BatchNum, Sequencer);

        Assert.ThrowsExactly<TestException>(() =>
            oc.Challenge(ChainId, BatchNum, engine.Sender, V4Proof(), verifier),
            "no bond to slash must fault");
    }

    // ---- FinalizeIfPastWindow ----------------------------------------------------------------

    [TestMethod]
    public void FinalizeIfPastWindow_RequiresPastDeadline_AndOpenWindow()
    {
        var engine = new TestEngine(true);
        var smHash = Hash('5');
        var sbHash = Hash('8');
        var oc = Deploy(engine, smHash, sbHash);
        WireSm(engine, smHash);

        // No window at all -> fault.
        Assert.ThrowsExactly<TestException>(() => oc.FinalizeIfPastWindow(ChainId, BatchNum),
            "no open window cannot be finalized");

        oc.OpenWindow(ChainId, BatchNum, Sequencer);
        // Still within the window -> finalize must fault.
        Assert.ThrowsExactly<TestException>(() => oc.FinalizeIfPastWindow(ChainId, BatchNum),
            "finalize before the deadline must fault");

        // Past the deadline and unchallenged -> finalize succeeds.
        engine.PersistingBlock.Advance(TimeSpan.FromSeconds(DefaultWindow + 1));
        oc.FinalizeIfPastWindow(ChainId, BatchNum); // must not throw
        Assert.AreEqual((BigInteger)0, oc.GetDeadline(ChainId, BatchNum),
            "finalized window must be consumed before the external finalize call");
        Assert.ThrowsExactly<TestException>(() => oc.FinalizeIfPastWindow(ChainId, BatchNum),
            "finalized window cannot be replayed");
    }

    // ---- production governance lock (§16) ----------------------------------------------------

    [TestMethod]
    public void SetGovernanceController_OwnerOnly_Persists()
    {
        var engine = new TestEngine(true);
        var oc = Deploy(engine, Hash('5'), Hash('8'));
        Assert.AreEqual(UInt160.Zero, oc.GovernanceController, "GC is unset at deploy");
        oc.GovernanceController = Hash('c');
        Assert.AreEqual(Hash('c'), oc.GovernanceController, "GC wiring must persist");
    }

    [TestMethod]
    public void SetGovernanceController_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        var oc = Deploy(engine, Hash('5'), Hash('8'), owner: Hash('1'));
        Assert.ThrowsExactly<TestException>(() => oc.GovernanceController = Hash('c'),
            "SetGovernanceController is owner-gated");
    }

    [TestMethod]
    public void LockGovernance_RequiresGcWired_OwnerOnly_OneWay_FreezeTheRest()
    {
        var engine = new TestEngine(true);
        var smHash = Hash('5');
        var oc = Deploy(engine, smHash, Hash('8'));
        Assert.IsFalse(oc.IsGovernanceLocked!.Value, "not locked at deploy");

        // Locking before wiring the GC would brick the allowlist forever -> rejected, no state change.
        Assert.ThrowsExactly<TestException>(() => oc.LockGovernance(),
            "must refuse to lock before GovernanceController is wired");
        Assert.IsFalse(oc.IsGovernanceLocked!.Value, "a failed lock must not change state");

        var gc = Hash('c');
        oc.GovernanceController = gc;
        oc.LockGovernance();
        Assert.IsTrue(oc.IsGovernanceLocked!.Value, "governance must be locked");

        // One-way + idempotent: re-locking is a no-op, not a fault.
        oc.LockGovernance();
        Assert.IsTrue(oc.IsGovernanceLocked!.Value, "re-lock stays locked");

        // Every instant owner path is now permanently closed — including for the legitimate owner.
        Assert.ThrowsExactly<TestException>(() => oc.RegisterFraudVerifier(Hash('a')),
            "instant allowlist add must revert once governance is locked");
        Assert.ThrowsExactly<TestException>(() => oc.RegisterPermissionlessFraudProfile(
            ChainId, Hash('a'), ExecutorSemanticId, ReplayDomain),
            "instant profile bind must revert once governance is locked");
        Assert.ThrowsExactly<TestException>(() => oc.RevokeFraudVerifier(Hash('a')),
            "instant revoke must revert once governance is locked — otherwise a locked owner " +
            "could still disable every fraud verifier");

        // The controller hash is frozen too: swapping it for an accept-all contract would make the
        // council-veto path a formality.
        Assert.ThrowsExactly<TestException>(() => oc.GovernanceController = Hash('a'),
            "the owner must not be able to replace the trusted GovernanceController after locking");
        Assert.AreEqual(gc, oc.GovernanceController,
            "a rejected controller replacement must preserve the exact pre-lock controller");
    }

    [TestMethod]
    public void LockGovernance_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        var oc = Deploy(engine, Hash('5'), Hash('8'), owner: Hash('1'));
        Assert.ThrowsExactly<TestException>(() => oc.LockGovernance(),
            "LockGovernance is owner-gated");
    }

    [TestMethod]
    public void LockedGovernance_StillChallenges_AndSlashes()
    {
        // The lock freezes WHO may be allowed to prove fraud — it must never freeze fraud proving.
        // A deployment that locked administration and then lost the challenge path would turn every
        // subsequent fraudulent batch into an unconditional finalization.
        var engine = new TestEngine(true);
        var smHash = Hash('5');
        var sbHash = Hash('8');
        var verifier = Hash('a');
        var oc = Deploy(engine, smHash, sbHash);
        WireVerifier(engine, verifier, verdict: true, settlementManager: smHash);
        WireSm(engine, smHash);
        WireBond(engine, sbHash, 1000);
        oc.RegisterPermissionlessFraudProfile(ChainId, verifier, ExecutorSemanticId, ReplayDomain);

        var gc = Hash('c');
        oc.GovernanceController = gc;
        oc.LockGovernance();

        oc.OpenWindow(ChainId, BatchNum, Sequencer);
        oc.Challenge(ChainId, BatchNum, engine.Sender, V4Proof(), verifier);
        Assert.IsTrue(oc.IsClaimConsumed(ClaimId)!.Value,
            "a locked deployment must still accept a valid fraud proof");
        Assert.ThrowsExactly<TestException>(() => oc.FinalizeIfPastWindow(ChainId, BatchNum),
            "the challenged batch must still be blocked from finalizing");
    }

    [TestMethod]
    public void RegisterFraudVerifierViaProposal_RequiresGcWired_Faults()
    {
        var engine = new TestEngine(true);
        var oc = Deploy(engine, Hash('5'), Hash('8'));
        Assert.ThrowsExactly<TestException>(() => oc.RegisterFraudVerifierViaProposal(Hash('a'), 1),
            "proposal path needs the GovernanceController wired first");
    }

    [TestMethod]
    public void RegisterFraudVerifierViaProposal_PayloadMismatch_Faults()
    {
        // Approved + timelocked, but the council voted on different bytes: blank-check defense.
        var engine = new TestEngine(true);
        var gc = Hash('c');
        WireGc(engine, gc, approved: true, payloadMatches: false);
        var oc = Deploy(engine, Hash('5'), Hash('8'));
        oc.GovernanceController = gc;

        Assert.ThrowsExactly<TestException>(() => oc.RegisterFraudVerifierViaProposal(Hash('a'), 1),
            "a proposal whose payload does not match the action args must be rejected");
        Assert.IsFalse(oc.IsApprovedFraudVerifier(Hash('a'))!.Value,
            "a rejected proposal must not touch the allowlist");
    }

    [TestMethod]
    public void RegisterFraudVerifierViaProposal_NotApproved_Faults()
    {
        // Payload binds, but the council has not approved + timelocked it.
        var engine = new TestEngine(true);
        var gc = Hash('c');
        WireGc(engine, gc, approved: false, payloadMatches: true);
        var oc = Deploy(engine, Hash('5'), Hash('8'));
        oc.GovernanceController = gc;

        Assert.ThrowsExactly<TestException>(() => oc.RegisterFraudVerifierViaProposal(Hash('a'), 1),
            "an un-approved / un-timelocked proposal must not register a verifier");
    }

    [TestMethod]
    public void RegisterFraudVerifierViaProposal_Registers_ReplayProtects_AndSurvivesLock()
    {
        var engine = new TestEngine(true);
        var gc = Hash('c');
        WireGc(engine, gc);
        var oc = Deploy(engine, Hash('5'), Hash('8'));
        oc.GovernanceController = gc;

        var verifier = Hash('a');
        oc.RegisterFraudVerifierViaProposal(verifier, 42);
        Assert.IsTrue(oc.IsApprovedFraudVerifier(verifier)!.Value,
            "an approved + bound proposal must extend the allowlist");

        // One proposal, one application — and the consumption is per-proposal, not per-action, so
        // the same vote cannot be re-spent on a different verifier.
        Assert.ThrowsExactly<TestException>(() => oc.RegisterFraudVerifierViaProposal(verifier, 42),
            "a consumed proposal cannot be replayed");
        Assert.ThrowsExactly<TestException>(() => oc.RevokeFraudVerifierViaProposal(verifier, 42),
            "a consumed proposal cannot be re-spent on a different action");

        // The lock closes the instant paths but must NOT strand upgrades: the council path still works.
        oc.LockGovernance();
        Assert.ThrowsExactly<TestException>(() => oc.RegisterFraudVerifier(Hash('b')),
            "instant path stays closed after locking");
        oc.RegisterFraudVerifierViaProposal(Hash('b'), 43);
        Assert.IsTrue(oc.IsApprovedFraudVerifier(Hash('b'))!.Value,
            "the council path must remain available once governance is locked");
        oc.RevokeFraudVerifierViaProposal(Hash('b'), 44);
        Assert.IsFalse(oc.IsApprovedFraudVerifier(Hash('b'))!.Value,
            "the council must also be able to revoke once governance is locked");
    }

    [TestMethod]
    public void RegisterPermissionlessFraudProfileViaProposal_BindsExactProfile_AndSurvivesLock()
    {
        var engine = new TestEngine(true);
        var smHash = Hash('5');
        var gc = Hash('c');
        var verifier = Hash('a');
        WireGc(engine, gc);
        WireVerifier(engine, verifier, verdict: true, settlementManager: smHash);
        var oc = Deploy(engine, smHash, Hash('8'));
        oc.GovernanceController = gc;

        oc.RegisterPermissionlessFraudProfileViaProposal(
            ChainId, verifier, ExecutorSemanticId, ReplayDomain, 7);
        Assert.IsTrue(oc.IsPermissionlessFraudProfile(
            ChainId, verifier, ExecutorSemanticId, ReplayDomain)!.Value,
            "an approved + bound profile proposal must authorize the exact tuple");
        Assert.IsFalse(oc.IsPermissionlessFraudProfile(
            ChainId + 1, verifier, ExecutorSemanticId, ReplayDomain)!.Value,
            "the proposal must not authorize a neighbouring chain");

        oc.LockGovernance();
        Assert.ThrowsExactly<TestException>(() => oc.RegisterPermissionlessFraudProfile(
            ChainId, verifier, ExecutorSemanticId, ReplayDomain),
            "instant profile bind must revert once governance is locked");
        Assert.ThrowsExactly<TestException>(() => oc.RegisterPermissionlessFraudProfileViaProposal(
            ChainId, verifier, ExecutorSemanticId, ReplayDomain, 7),
            "the profile proposal must be replay-protected like every other council action");
    }

    [TestMethod]
    public void BuildActions_UseCanonicalTagEncoding()
    {
        var engine = new TestEngine(true);
        var oc = Deploy(engine, Hash('5'), Hash('8'));
        var verifier = Hash('a');
        var verifierBytes = verifier.GetSpan().ToArray();

        // The tags are hand-built byte arrays on-chain, so the only thing that catches a mistyped
        // spelling is comparing them against the off-chain ASCII the council actually signs.
        var register = oc.BuildRegisterFraudVerifierAction(verifier)!;
        CollectionAssert.AreEqual(Concat(
            System.Text.Encoding.ASCII.GetBytes("neo4-gov:registerFraudVerifier"), verifierBytes), register,
            "registerFraudVerifier action must be tag||verifier (50B)");

        var revoke = oc.BuildRevokeFraudVerifierAction(verifier)!;
        CollectionAssert.AreEqual(Concat(
            System.Text.Encoding.ASCII.GetBytes("neo4-gov:revokeFraudVerifier"), verifierBytes), revoke,
            "revokeFraudVerifier action must be tag||verifier (48B)");

        var profile = oc.BuildRegisterPermissionlessFraudProfileAction(
            ChainId, verifier, ExecutorSemanticId, ReplayDomain)!;
        CollectionAssert.AreEqual(Concat(
            System.Text.Encoding.ASCII.GetBytes("neo4-gov:registerPermissionlessFraudProfile"),
            BitConverter.GetBytes(ChainId),
            verifierBytes, ExecutorSemanticId.GetSpan().ToArray(),
            ReplayDomain.GetSpan().ToArray()), profile,
            "fraud-profile action must bind chain + verifier + semantic id + replay domain, LE");

        // Distinct tags keep a vote on one action from being replayable as another.
        Assert.IsFalse(register.AsSpan().SequenceEqual(revoke), "register and revoke actions must differ");
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var total = 0;
        foreach (var part in parts) total += part.Length;
        var buf = new byte[total];
        var pos = 0;
        foreach (var part in parts)
        {
            Array.Copy(part, 0, buf, pos, part.Length);
            pos += part.Length;
        }
        return buf;
    }
}
