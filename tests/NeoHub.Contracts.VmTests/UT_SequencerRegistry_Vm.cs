using System;
using System.ComponentModel;
using System.Numerics;
using Moq;
using Neo;
using Neo.Cryptography.ECC;
using Neo.SmartContract; // Contract.CreateSignatureRedeemScript, Helper.ToScriptHash
using Neo.SmartContract.Testing;
using Neo.SmartContract.Testing.Exceptions;

namespace NeoHub.Contracts.VmTests;

/// <summary>Minimal SequencerBond surface so the registry's min-bond eligibility gate can be mocked.
/// The registry calls <c>Contract.Call(bondContract, "hasMinBond", ReadOnly, {chainId, sequencerAddress})</c>
/// during <c>register</c>; this exposes exactly that method so we can drive accept/reject behaviour.</summary>
public abstract class Mock_SequencerRegistry_Bond(SmartContractInitialize initialize) : SmartContract(initialize)
{
    [DisplayName("hasMinBond")]
    public abstract bool? HasMinBond(BigInteger? chainId, UInt160? sequencer);
}

/// <summary>
/// VM-level tests for NeoHub.SequencerRegistry — the per-chain dBFT sequencer pubkey registry.
/// These execute the compiled NEF in a real NeoVM and pin the security-critical invariants:
///   - Governance owner-gating on every mutator (setOwner / setMaxCommitteeSize / setExitWindowSeconds),
///     positive AND negative CheckWitness paths, plus their input-validation faults.
///   - register binds the consensus key to the bond/payout address (BOTH witnesses required), enforces
///     the cross-contract min-bond gate, rejects the reserved chainId 0, the zero address, duplicate
///     registration, and the max-committee-size cap.
///   - The active-count accounting is conserved: register increments, finalize (after the exit window)
///     decrements, and the cap is honoured.
///   - unregister is sequencer-witness-gated, only Active→Exiting, replay-protected against double-exit;
///     finalize enforces the exit-window timelock (cannot remove before Runtime.Time >= exitsAt) and
///     only acts on Exiting entries.
///
/// Sequencer consensus keys are real secp256r1 public keys; their witness is supplied by
/// <c>engine.SetTransactionSigners(pubkey)</c>. Because the VM derives the sig-contract script hash that
/// CheckWitness(UInt160) compares against from that same pubkey, deploying the sequencer's payout address
/// as <c>ScriptHashOf(pubkey)</c> lets a single ECPoint signer satisfy BOTH CheckWitness(sequencerKey)
/// and CheckWitness(sequencerAddress) the way a real co-signed registration would.
/// </summary>
[TestClass]
public class UT_SequencerRegistry_Vm
{
    private const uint ChainA = 1001;
    private const uint ChainB = 2002;

    // Real secp256r1 public keys (drawn from the testing protocol's standby committee).
    private static readonly ECPoint Seq1 =
        ECPoint.Parse("03b209fd4f53a7170ea4444e0cb0a6bb6a53c2bd016926989cf85f9b0fba17a70c", ECCurve.Secp256r1);
    private static readonly ECPoint Seq2 =
        ECPoint.Parse("02df48f60e8f3e01c48ff40b9b7f1310d7a8b2a193188befe1c2e3df740e895093", ECCurve.Secp256r1);
    private static readonly ECPoint Seq3 =
        ECPoint.Parse("03b8d9d5771d8f513aa0869b9cc8d50986403b78c6da36890638c3d46a5adce04a", ECCurve.Secp256r1);

    private static readonly UInt160 Stranger = UInt160.Parse("0x" + new string('c', 40));

    // Bond contracts: one that grants the min-bond, one that denies it. Distinct hashes because a hash
    // cannot be mocked twice — and the registry binds a single bond contract per deployment.
    private static readonly UInt160 BondOk = UInt160.Parse("0x" + new string('a', 40));
    private static readonly UInt160 BondDeny = UInt160.Parse("0x" + new string('b', 40));

    /// <summary>Address tied to a sequencer pubkey == the single-sig script hash the VM derives from it,
    /// so that <c>SetTransactionSigners(pubkey)</c> satisfies CheckWitness for BOTH the key and address.</summary>
    private static UInt160 AddrOf(ECPoint pubkey) =>
        Contract.CreateSignatureRedeemScript(pubkey).ToScriptHash();

    /// <summary>Deploy the registry. owner defaults to engine.Sender so the owner witness passes; pass a
    /// non-Sender <paramref name="owner"/> to exercise the negative owner-auth paths. The bond contract
    /// defaults to a mock that grants the min-bond; pass <paramref name="bond"/> = BondDeny for the
    /// insufficient-bond path.</summary>
    private static NeoHubSequencerRegistry Deploy(TestEngine engine, UInt160? owner = null, UInt160? bond = null)
    {
        var o = owner ?? engine.Sender;
        var bondHash = bond ?? BondOk;

        engine.FromHash<Mock_SequencerRegistry_Bond>(BondOk, m =>
            m.Setup(c => c.HasMinBond(It.IsAny<BigInteger?>(), It.IsAny<UInt160?>())).Returns(true),
            checkExistence: false);
        engine.FromHash<Mock_SequencerRegistry_Bond>(BondDeny, m =>
            m.Setup(c => c.HasMinBond(It.IsAny<BigInteger?>(), It.IsAny<UInt160?>())).Returns(false),
            checkExistence: false);

        return engine.Deploy<NeoHubSequencerRegistry>(
            NeoHubSequencerRegistry.Nef, NeoHubSequencerRegistry.Manifest, new object[] { o, bondHash });
    }

    /// <summary>Witness the sequencer key (and, via its derived script hash, its payout address) and
    /// register it on <paramref name="chainId"/>, restoring the owner signer afterwards.</summary>
    private static void RegisterAs(TestEngine engine, NeoHubSequencerRegistry reg, uint chainId, ECPoint key, UInt160 owner)
    {
        engine.SetTransactionSigners(key);
        reg.Register(chainId, key, AddrOf(key));
        engine.SetTransactionSigners(owner);
    }

    // ───────────────────────── deploy validation ─────────────────────────

    [TestMethod]
    public void Deploy_RejectsZeroOwnerAndZeroBond()
    {
        var e1 = new TestEngine(true);
        Assert.ThrowsExactly<TestException>(() => e1.Deploy<NeoHubSequencerRegistry>(
            NeoHubSequencerRegistry.Nef, NeoHubSequencerRegistry.Manifest,
            new object[] { UInt160.Zero, BondOk }),
            "zero owner must be rejected");

        var e2 = new TestEngine(true);
        Assert.ThrowsExactly<TestException>(() => e2.Deploy<NeoHubSequencerRegistry>(
            NeoHubSequencerRegistry.Nef, NeoHubSequencerRegistry.Manifest,
            new object[] { e2.Sender, UInt160.Zero }),
            "zero bond contract must be rejected — an unbonded committee 'looks deployed but broken'");
    }

    [TestMethod]
    public void Deploy_InitializesDefaults()
    {
        var engine = new TestEngine(true);
        var reg = Deploy(engine);

        Assert.AreEqual(engine.Sender, reg.Owner!, "owner is the deployer");
        Assert.AreEqual((BigInteger)21, reg.MaxCommitteeSize!, "default max committee size");
        Assert.AreEqual((BigInteger)86400, reg.ExitWindowSeconds!, "default exit window (24h)");
        Assert.AreEqual((BigInteger)0, reg.GetActiveCount(ChainA)!, "no sequencers registered yet");
    }

    // ───────────────────────── ownership (owner-gated) ─────────────────────────

    [TestMethod]
    public void SetOwner_OwnerOnly_RejectsZero_TransfersAndRevokesOldOwner()
    {
        var engine = new TestEngine(true);
        var reg = Deploy(engine); // owner == Sender

        Assert.ThrowsExactly<TestException>(() => reg.Owner = UInt160.Zero, "zero new owner rejected");

        reg.Owner = Stranger;
        Assert.AreEqual(Stranger, reg.Owner!, "ownership transferred");

        // The old owner (still the witnessed Sender) can no longer act as governance.
        Assert.ThrowsExactly<TestException>(() => reg.MaxCommitteeSize = (BigInteger)10,
            "former owner must lose authority after transfer");
    }

    [TestMethod]
    public void SetOwner_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        var reg = Deploy(engine, owner: Stranger); // owner != witnessed Sender
        Assert.ThrowsExactly<TestException>(() => reg.Owner = engine.Sender, "SetOwner is owner-gated");
    }

    [TestMethod]
    public void SetMaxCommitteeSize_OwnerGated_BoundsChecked()
    {
        var engine = new TestEngine(true);
        var reg = Deploy(engine);

        Assert.ThrowsExactly<TestException>(() => reg.MaxCommitteeSize = (BigInteger)0, "size 0 rejected");
        Assert.ThrowsExactly<TestException>(() => reg.MaxCommitteeSize = (BigInteger)65, "size > 64 rejected");

        reg.MaxCommitteeSize = (BigInteger)7;
        Assert.AreEqual((BigInteger)7, reg.MaxCommitteeSize!, "in-bounds size accepted");
    }

    [TestMethod]
    public void SetMaxCommitteeSize_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        var reg = Deploy(engine, owner: Stranger);
        Assert.ThrowsExactly<TestException>(() => reg.MaxCommitteeSize = (BigInteger)7,
            "SetMaxCommitteeSize is owner-gated");
    }

    [TestMethod]
    public void SetExitWindowSeconds_OwnerGated_BoundsChecked()
    {
        var engine = new TestEngine(true);
        var reg = Deploy(engine);

        Assert.ThrowsExactly<TestException>(() => reg.ExitWindowSeconds = (BigInteger)59, "< 60s rejected");
        Assert.ThrowsExactly<TestException>(() => reg.ExitWindowSeconds = (BigInteger)(7 * 86400 + 1), "> 7d rejected");

        reg.ExitWindowSeconds = (BigInteger)3600;
        Assert.AreEqual((BigInteger)3600, reg.ExitWindowSeconds!, "in-bounds window accepted");
    }

    [TestMethod]
    public void SetExitWindowSeconds_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        var reg = Deploy(engine, owner: Stranger);
        Assert.ThrowsExactly<TestException>(() => reg.ExitWindowSeconds = (BigInteger)3600,
            "SetExitWindowSeconds is owner-gated");
    }

    // ───────────────────────── register: witness + bond + validation ─────────────────────────

    [TestMethod]
    public void Register_HappyPath_SetsActiveAndAddress_IncrementsCount()
    {
        var engine = new TestEngine(true);
        var reg = Deploy(engine);

        Assert.IsFalse(reg.IsRegistered(ChainA, Seq1)!.Value);

        engine.SetTransactionSigners(Seq1);
        reg.Register(ChainA, Seq1, AddrOf(Seq1));

        Assert.IsTrue(reg.IsRegistered(ChainA, Seq1)!.Value, "sequencer is registered");
        Assert.AreEqual((BigInteger)1, reg.GetStatus(ChainA, Seq1)!, "status Active (1)");
        Assert.AreEqual(AddrOf(Seq1), reg.GetSequencerAddress(ChainA, Seq1)!, "payout address recorded");
        Assert.AreEqual((BigInteger)1, reg.GetActiveCount(ChainA)!, "active count incremented");
        // A different chain has its own independent count.
        Assert.AreEqual((BigInteger)0, reg.GetActiveCount(ChainB)!, "register is per-chain");
    }

    [TestMethod]
    public void Register_RejectsReservedChainZero()
    {
        var engine = new TestEngine(true);
        var reg = Deploy(engine);

        engine.SetTransactionSigners(Seq1);
        Assert.ThrowsExactly<TestException>(() => reg.Register(0, Seq1, AddrOf(Seq1)),
            "chainId 0 is the reserved L1 sentinel");
    }

    [TestMethod]
    public void Register_RejectsZeroSequencerAddress()
    {
        var engine = new TestEngine(true);
        var reg = Deploy(engine);

        engine.SetTransactionSigners(Seq1);
        Assert.ThrowsExactly<TestException>(() => reg.Register(ChainA, Seq1, UInt160.Zero),
            "zero sequencer address rejected");
    }

    [TestMethod]
    public void Register_WithoutSequencerKeyWitness_Faults()
    {
        var engine = new TestEngine(true);
        var reg = Deploy(engine); // default signer is the validators address, not Seq1's account

        Assert.ThrowsExactly<TestException>(() => reg.Register(ChainA, Seq1, AddrOf(Seq1)),
            "register requires a witness for the sequencer key");
        Assert.IsFalse(reg.IsRegistered(ChainA, Seq1)!.Value, "rejected register must not set state");
    }

    [TestMethod]
    public void Register_KeyWitnessButAddressNotCoSigned_Faults_PreventsBondTheft()
    {
        var engine = new TestEngine(true);
        var reg = Deploy(engine);

        // Witness Seq1's key, but claim someone else's bonded address (Seq2's). Without the
        // address co-signature an operator could attach their key to a victim's bonded address and
        // get that victim slashed for the operator's misbehaviour.
        engine.SetTransactionSigners(Seq1);
        Assert.ThrowsExactly<TestException>(() => reg.Register(ChainA, Seq1, AddrOf(Seq2)),
            "register requires a witness for the sequencer address too (key/address binding)");
        Assert.IsFalse(reg.IsRegistered(ChainA, Seq1)!.Value);
    }

    [TestMethod]
    public void Register_InsufficientBond_Faults()
    {
        var engine = new TestEngine(true);
        var reg = Deploy(engine, bond: BondDeny); // bond contract denies the min-bond gate

        engine.SetTransactionSigners(Seq1);
        Assert.ThrowsExactly<TestException>(() => reg.Register(ChainA, Seq1, AddrOf(Seq1)),
            "an unbonded sequencer must be rejected by the cross-contract min-bond gate");
        Assert.IsFalse(reg.IsRegistered(ChainA, Seq1)!.Value, "rejected register must not set state");
    }

    [TestMethod]
    public void Register_DuplicateRegistration_Faults()
    {
        var engine = new TestEngine(true);
        var reg = Deploy(engine);

        engine.SetTransactionSigners(Seq1);
        reg.Register(ChainA, Seq1, AddrOf(Seq1));
        Assert.ThrowsExactly<TestException>(() => reg.Register(ChainA, Seq1, AddrOf(Seq1)),
            "a sequencer cannot register twice on the same chain");
        Assert.AreEqual((BigInteger)1, reg.GetActiveCount(ChainA)!, "duplicate attempt must not double-count");
    }

    [TestMethod]
    public void Register_HonoursMaxCommitteeSizeCap()
    {
        var engine = new TestEngine(true);
        var owner = engine.Sender;
        var reg = Deploy(engine);

        // Shrink the cap to 1 so the second registration overflows it.
        reg.MaxCommitteeSize = (BigInteger)1;

        RegisterAs(engine, reg, ChainA, Seq1, owner);
        Assert.AreEqual((BigInteger)1, reg.GetActiveCount(ChainA)!);

        engine.SetTransactionSigners(Seq2);
        Assert.ThrowsExactly<TestException>(() => reg.Register(ChainA, Seq2, AddrOf(Seq2)),
            "registration beyond the committee cap must fault");
        Assert.AreEqual((BigInteger)1, reg.GetActiveCount(ChainA)!, "failed overflow must not change the count");
    }

    // ───────────────────────── unregister: witness + replay + status ─────────────────────────

    [TestMethod]
    public void Unregister_RequiresSequencerWitness()
    {
        var engine = new TestEngine(true);
        var owner = engine.Sender;
        var reg = Deploy(engine);
        RegisterAs(engine, reg, ChainA, Seq1, owner);

        // A different account (the owner/default signer) cannot exit Seq1.
        Assert.ThrowsExactly<TestException>(() => reg.Unregister(ChainA, Seq1),
            "unregister is gated on the sequencer's own witness");
        Assert.AreEqual((BigInteger)1, reg.GetStatus(ChainA, Seq1)!, "still Active after rejected unregister");
    }

    [TestMethod]
    public void Unregister_UnknownSequencer_Faults()
    {
        var engine = new TestEngine(true);
        var reg = Deploy(engine);

        engine.SetTransactionSigners(Seq1);
        Assert.ThrowsExactly<TestException>(() => reg.Unregister(ChainA, Seq1),
            "cannot unregister a sequencer that was never registered");
    }

    [TestMethod]
    public void Unregister_MovesActiveToExiting_ReplayProtected()
    {
        var engine = new TestEngine(true);
        var owner = engine.Sender;
        var reg = Deploy(engine);
        RegisterAs(engine, reg, ChainA, Seq1, owner);

        engine.SetTransactionSigners(Seq1);
        var exitsAt = reg.Unregister(ChainA, Seq1)!.Value;
        Assert.IsTrue(exitsAt > (BigInteger)0, "exit timestamp returned");
        Assert.AreEqual((BigInteger)2, reg.GetStatus(ChainA, Seq1)!, "status Exiting (2)");
        // Count is unchanged: the sequencer is still in the registry (and signing) until finalize.
        Assert.AreEqual((BigInteger)1, reg.GetActiveCount(ChainA)!, "exiting sequencer still counts until finalize");

        // Second unregister must fault — only an Active entry may begin exiting.
        Assert.ThrowsExactly<TestException>(() => reg.Unregister(ChainA, Seq1),
            "an already-exiting sequencer cannot re-initiate exit (replay)");
    }

    // ───────────────────────── finalize: timelock + status + accounting ─────────────────────────

    [TestMethod]
    public void Finalize_BeforeExitWindow_Faults()
    {
        var engine = new TestEngine(true);
        var owner = engine.Sender;
        var reg = Deploy(engine);
        RegisterAs(engine, reg, ChainA, Seq1, owner);

        engine.SetTransactionSigners(Seq1);
        reg.Unregister(ChainA, Seq1);
        engine.SetTransactionSigners(owner);

        // The 24h default window has not elapsed: finalize must fault and leave the entry in place.
        Assert.ThrowsExactly<TestException>(() => reg.Finalize(ChainA, Seq1),
            "finalize before the exit window elapses must fault");
        Assert.IsTrue(reg.IsRegistered(ChainA, Seq1)!.Value, "still registered before the window closes");
        Assert.AreEqual((BigInteger)1, reg.GetActiveCount(ChainA)!, "count unchanged before finalize");
    }

    [TestMethod]
    public void Finalize_NonExitingEntry_Faults()
    {
        var engine = new TestEngine(true);
        var owner = engine.Sender;
        var reg = Deploy(engine);
        RegisterAs(engine, reg, ChainA, Seq1, owner);

        // Active (not Exiting) entry cannot be finalized.
        Assert.ThrowsExactly<TestException>(() => reg.Finalize(ChainA, Seq1),
            "an Active (non-Exiting) sequencer cannot be finalized");
    }

    [TestMethod]
    public void Finalize_UnknownSequencer_Faults()
    {
        var engine = new TestEngine(true);
        var reg = Deploy(engine);
        Assert.ThrowsExactly<TestException>(() => reg.Finalize(ChainA, Seq1),
            "finalize on a non-registered sequencer must fault");
    }

    [TestMethod]
    public void Finalize_AfterExitWindow_RemovesAndDecrementsCount()
    {
        var engine = new TestEngine(true);
        var owner = engine.Sender;
        var reg = Deploy(engine);

        // Use a short exit window to keep the time advance bounded.
        reg.ExitWindowSeconds = (BigInteger)60;

        RegisterAs(engine, reg, ChainA, Seq1, owner);
        RegisterAs(engine, reg, ChainA, Seq2, owner);
        Assert.AreEqual((BigInteger)2, reg.GetActiveCount(ChainA)!);

        engine.SetTransactionSigners(Seq1);
        reg.Unregister(ChainA, Seq1);
        engine.SetTransactionSigners(owner);

        // Advance past the 60s exit window; finalize may then be called by anyone (no witness gate).
        engine.PersistingBlock.Advance(TimeSpan.FromSeconds(61));
        reg.Finalize(ChainA, Seq1);

        Assert.IsFalse(reg.IsRegistered(ChainA, Seq1)!.Value, "removed from the registry");
        Assert.AreEqual((BigInteger)1, reg.GetActiveCount(ChainA)!, "active count decremented to the remaining sequencer");
        // Seq2 is untouched and still Active.
        Assert.AreEqual((BigInteger)1, reg.GetStatus(ChainA, Seq2)!, "the other sequencer remains Active");

        // Replay protection: the entry is gone, so a second finalize faults.
        Assert.ThrowsExactly<TestException>(() => reg.Finalize(ChainA, Seq1),
            "a removed sequencer cannot be finalized again");
    }

    [TestMethod]
    public void RegisterAfterFinalize_FreesTheSlot_AndCountStaysConsistent()
    {
        var engine = new TestEngine(true);
        var owner = engine.Sender;
        var reg = Deploy(engine);
        reg.ExitWindowSeconds = (BigInteger)60;

        // Full register → exit → finalize cycle, then re-register the same key: count returns to a
        // consistent 1 with no double-counting and no negative balance.
        RegisterAs(engine, reg, ChainA, Seq1, owner);
        engine.SetTransactionSigners(Seq1);
        reg.Unregister(ChainA, Seq1);
        engine.SetTransactionSigners(owner);
        engine.PersistingBlock.Advance(TimeSpan.FromSeconds(61));
        reg.Finalize(ChainA, Seq1);
        Assert.AreEqual((BigInteger)0, reg.GetActiveCount(ChainA)!, "count back to zero after finalize");

        RegisterAs(engine, reg, ChainA, Seq1, owner);
        Assert.IsTrue(reg.IsRegistered(ChainA, Seq1)!.Value, "the freed slot can be re-registered");
        Assert.AreEqual((BigInteger)1, reg.GetActiveCount(ChainA)!, "count consistent after re-register");
        Assert.AreEqual((BigInteger)1, reg.GetStatus(ChainA, Seq1)!, "re-registered as Active");
    }
}
