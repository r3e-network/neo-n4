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
///     cannot drain a bond), the permissionless-OR-owner-co-sign gate, the challenger witness, a
///     non-empty proof, the open+unexpired window, and is replay-protected (already-accepted).
///   * A successful Challenge records the accepted-fraud marker, which both blocks a second challenge
///     and blocks FinalizeIfPastWindow (a challenged batch can never be finalized).
///   * FinalizeIfPastWindow only runs once the deadline has elapsed and only on an open window.
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
            "approved-only verifier is NOT permissionless (still needs owner co-sign)");

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

    // ---- Challenge: the CRITICAL allowlist + co-sign gates -----------------------------------

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
    public void Challenge_ApprovedButNotPermissionless_RequiresOwnerCoSign()
    {
        // An approved-but-not-permissionless verifier needs owner/governance co-sign at Challenge
        // time. We set up the approved-only state under the owner (== Sender), then transfer
        // ownership away so the co-sign witness is absent for the subsequent challenge.
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
        oc.Owner = Hash('1');               // governance is now a different account

        // engine.Sender witnesses itself (so the challenger witness is fine), but it is no longer the
        // owner; an approved-only verifier still requires the owner co-sign -> fault.
        Assert.ThrowsExactly<TestException>(() =>
            oc.Challenge(ChainId, BatchNum, engine.Sender, Proof, verifier),
            "approved-only verifier requires owner/governance co-sign");
    }

    [TestMethod]
    public void Challenge_OwnerCoSign_WithApprovedVerifier_Accepts()
    {
        // Owner == signer: an approved-only verifier is usable because the owner co-sign witness
        // is present. This pins the positive co-sign path.
        var engine = new TestEngine(true);
        var smHash = Hash('5');
        var sbHash = Hash('8');
        var verifier = Hash('a');
        var oc = Deploy(engine, smHash, sbHash); // owner == Sender

        WireVerifier(engine, verifier, verdict: true);
        WireSm(engine, smHash);
        WireBond(engine, sbHash, 1000);
        oc.RegisterFraudVerifier(verifier); // approved-only
        oc.OpenWindow(ChainId, BatchNum, Sequencer);

        // Challenger must witness itself; deploy used Sender as owner, but the challenger here is a
        // distinct principal -> witness for the challenger is absent -> fault.
        Assert.ThrowsExactly<TestException>(() =>
            oc.Challenge(ChainId, BatchNum, Challenger, Proof, verifier),
            "challenger without its own witness must fault");

        // Use engine.Sender as the challenger so its witness is present (and owner co-sign too).
        oc.Challenge(ChainId, BatchNum, engine.Sender, Proof, verifier);

        // Accepted-fraud marker is now set: a challenged batch can never be finalized.
        engine.PersistingBlock.Advance(TimeSpan.FromSeconds(DefaultWindow + 10));
        Assert.ThrowsExactly<TestException>(() => oc.FinalizeIfPastWindow(ChainId, BatchNum),
            "a challenged batch must not be finalizable");
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
    public void Challenge_V4ProfileMismatchAndLegacyV3_RequireGovernanceCoSign()
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
        WireVerifier(engine, verifier, verdict: true);
        WireSm(engine, smHash);
        WireBond(engine, sbHash, 1, (amount, beneficiary) => slashes.Add((amount, beneficiary)));
        oc.RegisterFraudVerifier(verifier);
        oc.OpenWindow(ChainId, BatchNum, Sequencer);

        oc.Challenge(ChainId, BatchNum, engine.Sender, Proof, verifier);

        Assert.HasCount(1, slashes);
        Assert.AreEqual(BigInteger.One, slashes[0].Amount);
        Assert.AreEqual(UInt160.Zero, slashes[0].Beneficiary);
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

        WireVerifier(engine, verifier, verdict: false); // verifier says "no fraud"
        WireSm(engine, smHash);
        WireBond(engine, sbHash, 1000);
        oc.RegisterFraudVerifier(verifier);
        oc.OpenWindow(ChainId, BatchNum, Sequencer);

        Assert.ThrowsExactly<TestException>(() =>
            oc.Challenge(ChainId, BatchNum, engine.Sender, Proof, verifier),
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

        WireVerifier(engine, verifier, verdict: true);
        oc.RegisterFraudVerifier(verifier);
        // No OpenWindow call -> there is no window to challenge.

        Assert.ThrowsExactly<TestException>(() =>
            oc.Challenge(ChainId, BatchNum, engine.Sender, Proof, verifier),
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

        WireVerifier(engine, verifier, verdict: true);
        WireSm(engine, smHash);
        WireBond(engine, sbHash, 1000);
        oc.RegisterFraudVerifier(verifier);
        oc.OpenWindow(ChainId, BatchNum, Sequencer);

        // Advance past the deadline -> the window is closed -> challenge must fault.
        engine.PersistingBlock.Advance(TimeSpan.FromSeconds(DefaultWindow + 1));
        Assert.ThrowsExactly<TestException>(() =>
            oc.Challenge(ChainId, BatchNum, engine.Sender, Proof, verifier),
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

        WireVerifier(engine, verifier, verdict: true);
        WireSm(engine, smHash);
        WireBond(engine, sbHash, 0); // sequencer has no bond
        oc.RegisterFraudVerifier(verifier);
        oc.OpenWindow(ChainId, BatchNum, Sequencer);

        Assert.ThrowsExactly<TestException>(() =>
            oc.Challenge(ChainId, BatchNum, engine.Sender, Proof, verifier),
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
    }
}
