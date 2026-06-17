using System.ComponentModel;
using System.Numerics;
using Moq;
using Neo;
using Neo.SmartContract.Testing;
using Neo.SmartContract.Testing.Exceptions;

namespace NeoHub.Contracts.VmTests;

/// <summary>
/// VM-level tests for NeoHub.SequencerBond — the per-(chainId, sequencer) slashable bond escrow.
/// These execute the deploy/deposit/slash/withdraw/governance paths in a real NeoVM with the
/// NEP-17 bond asset replaced by a mock, and pin the security-critical invariants:
///   * deploy validates owner / bondAsset / non-empty slasher list,
///   * Deposit accounting conserves (credit-before-transfer, FAULT-revert on transfer failure),
///   * the NEP-17 hook rejects unsolicited direct transfers (only Deposit-initiated transfers credit),
///   * Slash is gated to registered slashers and can never debit more than the posted bond,
///   * Withdraw is owner-gated and can never debit more than the posted balance,
///   * governance setters (SetOwner / SetMinBond / RegisterSlasher / RevokeSlasher) are owner-gated.
///
/// The bond asset is mocked with the shared <see cref="MockNep17"/> (defined in UT_SharedBridge_Vm.cs).
/// Slasher authorization keys off <c>Runtime.CallingScriptHash</c>; we spoof it via
/// <c>engine.OnGetCallingScriptHash</c> to exercise the authorized-slasher path without deploying a
/// real slasher contract.
/// </summary>
[TestClass]
public class UT_SequencerBond_Vm
{
    private static readonly UInt160 AssetHash = UInt160.Parse("0x" + new string('a', 40));
    private static readonly UInt160 Sequencer = UInt160.Parse("0x" + new string('b', 40));
    private static readonly UInt160 SlasherHash = UInt160.Parse("0x" + new string('c', 40));
    private static readonly UInt160 Recipient = UInt160.Parse("0x" + new string('d', 40));
    private static readonly UInt160 Stranger = UInt160.Parse("0x" + new string('e', 40));

    private const uint ChainA = 1001;
    private const uint ChainB = 2002;

    /// <summary>Default min bond baked in at deploy (DefaultMinBond = 1.0 GAS at 8 decimals).</summary>
    private const long DefaultMinBond = 1_000_000L;

    /// <summary>
    /// Deploy the bond contract. owner defaults to engine.Sender so owner-gated witness checks pass;
    /// pass an explicit <paramref name="owner"/> to exercise the negative authorization paths. The
    /// initial slasher list contains <see cref="SlasherHash"/>. The bond asset is wired to a
    /// mock NEP-17 whose transfer returns <paramref name="transferOk"/>.
    /// </summary>
    private static NeoHubSequencerBond Deploy(TestEngine engine, UInt160? owner = null, bool transferOk = true)
    {
        var o = owner ?? engine.Sender;
        engine.FromHash<MockNep17>(AssetHash, m =>
            m.Setup(c => c.Transfer(It.IsAny<UInt160?>(), It.IsAny<UInt160?>(), It.IsAny<BigInteger?>(), It.IsAny<object?>()))
                .Returns(transferOk),
            checkExistence: false);

        return engine.Deploy<NeoHubSequencerBond>(
            NeoHubSequencerBond.Nef, NeoHubSequencerBond.Manifest,
            new object[] { o, AssetHash, new object[] { SlasherHash } });
    }

    // ---------------------------------------------------------------------------------------------
    // Deploy-time input validation
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void Deploy_WiresOwnerAssetAndDefaultMinBond()
    {
        var engine = new TestEngine(true);
        var sb = Deploy(engine);

        Assert.AreEqual(engine.Sender, sb.Owner, "owner must be the deploy arg");
        Assert.AreEqual(AssetHash, sb.BondAsset, "bond asset must be the deploy arg");
        Assert.AreEqual((BigInteger)DefaultMinBond, sb.MinBond, "min bond defaults to DefaultMinBond");
        Assert.IsTrue(sb.IsSlasher(SlasherHash)!.Value, "initial slasher must be authorized");
        Assert.IsFalse(sb.IsSlasher(Stranger)!.Value, "unrelated account is not a slasher");
    }

    [TestMethod]
    public void Deploy_RejectsZeroOwner()
    {
        var engine = new TestEngine(true);
        engine.FromHash<MockNep17>(AssetHash, m =>
            m.Setup(c => c.Transfer(It.IsAny<UInt160?>(), It.IsAny<UInt160?>(), It.IsAny<BigInteger?>(), It.IsAny<object?>()))
                .Returns(true), checkExistence: false);

        Assert.ThrowsExactly<TestException>(() => engine.Deploy<NeoHubSequencerBond>(
            NeoHubSequencerBond.Nef, NeoHubSequencerBond.Manifest,
            new object[] { UInt160.Zero, AssetHash, new object[] { SlasherHash } }),
            "a zero owner must be rejected at deploy");
    }

    [TestMethod]
    public void Deploy_RejectsZeroBondAsset()
    {
        var engine = new TestEngine(true);
        Assert.ThrowsExactly<TestException>(() => engine.Deploy<NeoHubSequencerBond>(
            NeoHubSequencerBond.Nef, NeoHubSequencerBond.Manifest,
            new object[] { engine.Sender, UInt160.Zero, new object[] { SlasherHash } }),
            "a zero bond asset must be rejected at deploy");
    }

    [TestMethod]
    public void Deploy_RejectsEmptySlasherList()
    {
        var engine = new TestEngine(true);
        Assert.ThrowsExactly<TestException>(() => engine.Deploy<NeoHubSequencerBond>(
            NeoHubSequencerBond.Nef, NeoHubSequencerBond.Manifest,
            new object[] { engine.Sender, AssetHash, new object[] { } }),
            "an empty slasher list breaks economic security and must be rejected");
    }

    [TestMethod]
    public void Deploy_RejectsZeroSlasherEntry()
    {
        var engine = new TestEngine(true);
        Assert.ThrowsExactly<TestException>(() => engine.Deploy<NeoHubSequencerBond>(
            NeoHubSequencerBond.Nef, NeoHubSequencerBond.Manifest,
            new object[] { engine.Sender, AssetHash, new object[] { UInt160.Zero } }),
            "a zero slasher entry must be rejected at deploy");
    }

    // ---------------------------------------------------------------------------------------------
    // Deposit accounting
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void Deposit_CreditsBondLedger_PerChainAndAccumulates()
    {
        var engine = new TestEngine(true);
        var sb = Deploy(engine);

        Assert.AreEqual((BigInteger)0, sb.GetBalance(ChainA, Sequencer));
        sb.Deposit(ChainA, Sequencer, 1000);
        Assert.AreEqual((BigInteger)1000, sb.GetBalance(ChainA, Sequencer), "deposit credits the bond ledger");
        sb.Deposit(ChainA, Sequencer, 500);
        Assert.AreEqual((BigInteger)1500, sb.GetBalance(ChainA, Sequencer), "second deposit accumulates");
        // A different chain is an independent ledger row — deposits do not leak across chains.
        Assert.AreEqual((BigInteger)0, sb.GetBalance(ChainB, Sequencer), "other chain got nothing");
    }

    [TestMethod]
    public void Deposit_RejectsReservedChainZero_NonPositiveAmount_ZeroSequencer()
    {
        var engine = new TestEngine(true);
        var sb = Deploy(engine);

        Assert.ThrowsExactly<TestException>(() => sb.Deposit(0, Sequencer, 100),
            "chainId 0 is the reserved L1 sentinel");
        Assert.ThrowsExactly<TestException>(() => sb.Deposit(ChainA, Sequencer, 0),
            "zero deposit amount must be rejected");
        Assert.ThrowsExactly<TestException>(() => sb.Deposit(ChainA, Sequencer, -1),
            "negative deposit amount must be rejected");
        Assert.ThrowsExactly<TestException>(() => sb.Deposit(ChainA, UInt160.Zero, 100),
            "zero sequencer must be rejected");
        // None of the rejected calls credited anything.
        Assert.AreEqual((BigInteger)0, sb.GetBalance(ChainA, Sequencer));
    }

    [TestMethod]
    public void Deposit_FailedAssetTransfer_FaultsAndRevertsAccounting()
    {
        // Bond asset transfer returns false (e.g. paused/frozen). Deposit credits the ledger BEFORE
        // the transfer, so the contract MUST FAULT on the false return and the NeoVM must revert the
        // credit — otherwise a sequencer could inflate its bond without actually paying.
        var engine = new TestEngine(true);
        var sb = Deploy(engine, transferOk: false);

        Assert.ThrowsExactly<TestException>(() => sb.Deposit(ChainA, Sequencer, 1000),
            "a failed asset transfer must FAULT the deposit");
        Assert.AreEqual((BigInteger)0, sb.GetBalance(ChainA, Sequencer),
            "FAULT must revert the pre-transfer credit — no bond inflation");
    }

    // ---------------------------------------------------------------------------------------------
    // NEP-17 hook: reject unsolicited direct transfers
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void OnNEP17Payment_DirectTransfer_WithoutPendingDeposit_Rejected()
    {
        // A raw NEP-17 transfer that was NOT initiated by Deposit has no pending-transfer marker and
        // must be rejected, so stray tokens cannot be credited as bond out-of-band.
        var engine = new TestEngine(true);
        var sb = Deploy(engine);

        Assert.ThrowsExactly<TestException>(() => sb.OnNEP17Payment(Sequencer, 100, null),
            "direct (non-Deposit) transfer must be rejected by the NEP-17 hook");
    }

    [TestMethod]
    public void OnNEP17Payment_RejectsNonPositiveAmount()
    {
        var engine = new TestEngine(true);
        var sb = Deploy(engine);

        Assert.ThrowsExactly<TestException>(() => sb.OnNEP17Payment(Sequencer, 0, null),
            "zero-amount NEP-17 callback must be rejected");
    }

    // ---------------------------------------------------------------------------------------------
    // hasMinBond accounting view
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void HasMinBond_TracksBalanceVersusThreshold()
    {
        var engine = new TestEngine(true);
        var sb = Deploy(engine);

        Assert.IsFalse(sb.HasMinBond(ChainA, Sequencer)!.Value, "no bond -> below threshold");
        sb.Deposit(ChainA, Sequencer, DefaultMinBond - 1);
        Assert.IsFalse(sb.HasMinBond(ChainA, Sequencer)!.Value, "just under threshold");
        sb.Deposit(ChainA, Sequencer, 1);
        Assert.IsTrue(sb.HasMinBond(ChainA, Sequencer)!.Value, "exactly at threshold qualifies");
    }

    // ---------------------------------------------------------------------------------------------
    // Slash: slasher-gated + accounting conservation
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void Slash_ByRegisteredSlasher_DebitsBondAndPaysRecipient()
    {
        var engine = new TestEngine(true);
        var sb = Deploy(engine);
        sb.Deposit(ChainA, Sequencer, 1000);

        // Spoof CallingScriptHash so the bond contract sees a registered slasher as the caller.
        engine.OnGetCallingScriptHash = (current, expected) => SlasherHash;
        try
        {
            sb.Slash(ChainA, Sequencer, 400, Recipient);
        }
        finally
        {
            engine.OnGetCallingScriptHash = null;
        }

        Assert.AreEqual((BigInteger)600, sb.GetBalance(ChainA, Sequencer),
            "slash debits exactly the slashed amount");
    }

    [TestMethod]
    public void Slash_ByNonSlasher_Faults_AndLeavesBondIntact()
    {
        var engine = new TestEngine(true);
        var sb = Deploy(engine);
        sb.Deposit(ChainA, Sequencer, 1000);

        // CallingScriptHash spoofed to a NON-registered account -> slash gate must reject.
        engine.OnGetCallingScriptHash = (current, expected) => Stranger;
        try
        {
            Assert.ThrowsExactly<TestException>(() => sb.Slash(ChainA, Sequencer, 400, Recipient),
                "only a registered slasher may slash");
        }
        finally
        {
            engine.OnGetCallingScriptHash = null;
        }

        Assert.AreEqual((BigInteger)1000, sb.GetBalance(ChainA, Sequencer),
            "rejected slash must not touch the bond");
    }

    [TestMethod]
    public void Slash_MoreThanBond_Faults_NoNegativeBalance()
    {
        var engine = new TestEngine(true);
        var sb = Deploy(engine);
        sb.Deposit(ChainA, Sequencer, 1000);

        engine.OnGetCallingScriptHash = (current, expected) => SlasherHash;
        try
        {
            Assert.ThrowsExactly<TestException>(() => sb.Slash(ChainA, Sequencer, 1001, Recipient),
                "cannot slash more than the posted bond");
        }
        finally
        {
            engine.OnGetCallingScriptHash = null;
        }

        Assert.AreEqual((BigInteger)1000, sb.GetBalance(ChainA, Sequencer),
            "over-slash must not drive the balance negative");
    }

    [TestMethod]
    public void Slash_RejectsNonPositiveAmount()
    {
        var engine = new TestEngine(true);
        var sb = Deploy(engine);
        sb.Deposit(ChainA, Sequencer, 1000);

        engine.OnGetCallingScriptHash = (current, expected) => SlasherHash;
        try
        {
            Assert.ThrowsExactly<TestException>(() => sb.Slash(ChainA, Sequencer, 0, Recipient),
                "zero slash amount must be rejected");
        }
        finally
        {
            engine.OnGetCallingScriptHash = null;
        }
    }

    [TestMethod]
    public void Slash_FailedPayout_FaultsAndRevertsDebit()
    {
        // Recipient is non-zero so a payout transfer is attempted; the asset transfer returns false,
        // which MUST FAULT so the bond decrement is reverted (no silent loss of accounting).
        // A single conditional mock distinguishes the two transfer directions by destination:
        //   * deposit pulls tokens IN  (to == the bond contract)        -> succeeds, seeds the bond,
        //   * slash pays tokens OUT     (to == Recipient)               -> fails, must FAULT.
        // (The same hash can only be mocked once per test, so we cannot flip it mid-test.)
        var engine = new TestEngine(true);
        engine.FromHash<MockNep17>(AssetHash, m =>
        {
            m.Setup(c => c.Transfer(It.IsAny<UInt160?>(), It.Is<UInt160?>(to => to == Recipient),
                It.IsAny<BigInteger?>(), It.IsAny<object?>())).Returns(false);
            m.Setup(c => c.Transfer(It.IsAny<UInt160?>(), It.Is<UInt160?>(to => to != Recipient),
                It.IsAny<BigInteger?>(), It.IsAny<object?>())).Returns(true);
        }, checkExistence: false);
        var sb = engine.Deploy<NeoHubSequencerBond>(
            NeoHubSequencerBond.Nef, NeoHubSequencerBond.Manifest,
            new object[] { engine.Sender, AssetHash, new object[] { SlasherHash } });

        sb.Deposit(ChainA, Sequencer, 1000);
        Assert.AreEqual((BigInteger)1000, sb.GetBalance(ChainA, Sequencer));

        engine.OnGetCallingScriptHash = (current, expected) => SlasherHash;
        try
        {
            Assert.ThrowsExactly<TestException>(() => sb.Slash(ChainA, Sequencer, 400, Recipient),
                "a failed slash payout must FAULT");
        }
        finally
        {
            engine.OnGetCallingScriptHash = null;
        }

        Assert.AreEqual((BigInteger)1000, sb.GetBalance(ChainA, Sequencer),
            "FAULT must revert the bond debit when payout fails");
    }

    // ---------------------------------------------------------------------------------------------
    // Withdraw: owner-gated + accounting conservation
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void Withdraw_ByOwner_DebitsBalance()
    {
        var engine = new TestEngine(true);
        var sb = Deploy(engine); // owner == engine.Sender (auto-witnessed)
        sb.Deposit(ChainA, Sequencer, 1000);

        sb.Withdraw(ChainA, Sequencer, 600);
        Assert.AreEqual((BigInteger)400, sb.GetBalance(ChainA, Sequencer),
            "owner withdrawal debits exactly the requested amount");
    }

    [TestMethod]
    public void Withdraw_ByNonOwner_Faults()
    {
        var engine = new TestEngine(true);
        // Owner is a different account than the test signer -> the owner witness gate must reject.
        var sb = Deploy(engine, owner: Stranger);
        // Seed a balance with a working deposit (deposit is not owner-gated).
        sb.Deposit(ChainA, Sequencer, 1000);

        Assert.ThrowsExactly<TestException>(() => sb.Withdraw(ChainA, Sequencer, 100),
            "Withdraw is owner-gated");
        Assert.AreEqual((BigInteger)1000, sb.GetBalance(ChainA, Sequencer),
            "rejected withdrawal must not debit");
    }

    [TestMethod]
    public void Withdraw_MoreThanBalance_Faults_NoNegativeBalance()
    {
        var engine = new TestEngine(true);
        var sb = Deploy(engine);
        sb.Deposit(ChainA, Sequencer, 1000);

        Assert.ThrowsExactly<TestException>(() => sb.Withdraw(ChainA, Sequencer, 1001),
            "cannot withdraw more than the posted balance");
        Assert.AreEqual((BigInteger)1000, sb.GetBalance(ChainA, Sequencer),
            "over-withdrawal must not drive the balance negative");
    }

    [TestMethod]
    public void Withdraw_RejectsNonPositiveAmount()
    {
        var engine = new TestEngine(true);
        var sb = Deploy(engine);
        sb.Deposit(ChainA, Sequencer, 1000);

        Assert.ThrowsExactly<TestException>(() => sb.Withdraw(ChainA, Sequencer, 0),
            "zero withdrawal amount must be rejected");
    }

    [TestMethod]
    public void Withdraw_FailedTransfer_FaultsAndRevertsDebit()
    {
        // Conditional mock: deposit (to == bond contract) succeeds to seed the bond; the withdrawal
        // payout (to == Sequencer) fails, which MUST FAULT and revert the balance debit.
        var engine = new TestEngine(true);
        engine.FromHash<MockNep17>(AssetHash, m =>
        {
            m.Setup(c => c.Transfer(It.IsAny<UInt160?>(), It.Is<UInt160?>(to => to == Sequencer),
                It.IsAny<BigInteger?>(), It.IsAny<object?>())).Returns(false);
            m.Setup(c => c.Transfer(It.IsAny<UInt160?>(), It.Is<UInt160?>(to => to != Sequencer),
                It.IsAny<BigInteger?>(), It.IsAny<object?>())).Returns(true);
        }, checkExistence: false);
        var sb = engine.Deploy<NeoHubSequencerBond>(
            NeoHubSequencerBond.Nef, NeoHubSequencerBond.Manifest,
            new object[] { engine.Sender, AssetHash, new object[] { SlasherHash } });

        sb.Deposit(ChainA, Sequencer, 1000);
        Assert.AreEqual((BigInteger)1000, sb.GetBalance(ChainA, Sequencer));

        Assert.ThrowsExactly<TestException>(() => sb.Withdraw(ChainA, Sequencer, 600),
            "a failed withdrawal transfer must FAULT");
        Assert.AreEqual((BigInteger)1000, sb.GetBalance(ChainA, Sequencer),
            "FAULT must revert the balance debit when transfer fails");
    }

    // ---------------------------------------------------------------------------------------------
    // Governance: owner-gated setters
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void SetMinBond_OwnerOnly_UpdatesThreshold()
    {
        var engine = new TestEngine(true);
        var sb = Deploy(engine);

        sb.MinBond = 5_000_000;
        Assert.AreEqual((BigInteger)5_000_000, sb.MinBond, "owner can raise the min bond");
        Assert.ThrowsExactly<TestException>(() => sb.MinBond = 0,
            "non-positive min bond must be rejected");
    }

    [TestMethod]
    public void SetMinBond_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        var sb = Deploy(engine, owner: Stranger);

        Assert.ThrowsExactly<TestException>(() => sb.MinBond = 5_000_000,
            "SetMinBond is owner-gated");
        Assert.AreEqual((BigInteger)DefaultMinBond, sb.MinBond, "rejected set must not change state");
    }

    [TestMethod]
    public void SetOwner_OwnerOnly_TransfersOwnership_RejectsZero()
    {
        var engine = new TestEngine(true);
        var sb = Deploy(engine);

        Assert.ThrowsExactly<TestException>(() => sb.Owner = UInt160.Zero,
            "zero new owner must be rejected");
        sb.Owner = Stranger;
        Assert.AreEqual(Stranger, sb.Owner, "ownership transfers to the new owner");
        // The old owner (engine.Sender) can no longer act — the witness now belongs to Stranger.
        Assert.ThrowsExactly<TestException>(() => sb.MinBond = 7,
            "old owner loses authority after transfer");
    }

    [TestMethod]
    public void SetOwner_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        var sb = Deploy(engine, owner: Stranger);

        Assert.ThrowsExactly<TestException>(() => sb.Owner = engine.Sender,
            "SetOwner is owner-gated");
    }

    [TestMethod]
    public void RegisterAndRevokeSlasher_OwnerOnly_RoundTrips()
    {
        var engine = new TestEngine(true);
        var sb = Deploy(engine);
        var newSlasher = UInt160.Parse("0x" + new string('7', 40));

        Assert.IsFalse(sb.IsSlasher(newSlasher)!.Value);
        sb.RegisterSlasher(newSlasher);
        Assert.IsTrue(sb.IsSlasher(newSlasher)!.Value, "registered slasher is authorized");
        sb.RevokeSlasher(newSlasher);
        Assert.IsFalse(sb.IsSlasher(newSlasher)!.Value, "revoked slasher is de-authorized");
    }

    [TestMethod]
    public void RegisterSlasher_NonOwner_Faults_AndRejectsZero()
    {
        var engine = new TestEngine(true);
        var ownerSb = Deploy(engine);
        // Zero slasher rejected even for the owner.
        Assert.ThrowsExactly<TestException>(() => ownerSb.RegisterSlasher(UInt160.Zero),
            "zero slasher must be rejected");

        var engine2 = new TestEngine(true);
        var sb = Deploy(engine2, owner: Stranger);
        var newSlasher = UInt160.Parse("0x" + new string('7', 40));
        Assert.ThrowsExactly<TestException>(() => sb.RegisterSlasher(newSlasher),
            "RegisterSlasher is owner-gated");
        Assert.IsFalse(sb.IsSlasher(newSlasher)!.Value, "rejected register must not authorize");
    }

    [TestMethod]
    public void RevokeSlasher_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        var sb = Deploy(engine, owner: Stranger);

        Assert.ThrowsExactly<TestException>(() => sb.RevokeSlasher(SlasherHash),
            "RevokeSlasher is owner-gated");
        Assert.IsTrue(sb.IsSlasher(SlasherHash)!.Value, "rejected revoke must leave the slasher authorized");
    }
}
