using System.ComponentModel;
using System.Numerics;
using Moq;
using Neo;
using Neo.SmartContract.Testing;
using Neo.SmartContract.Testing.Exceptions;

namespace NeoHub.Contracts.VmTests;

/// <summary>
/// VM-level tests for NeoHub.ExternalBridgeBond — the per-(externalChainId, committee member)
/// slashable bond escrow. These execute the deploy/deposit/slash/withdraw/governance paths in a
/// real NeoVM with the NEP-17 bond asset replaced by a mock, and pin the security-critical
/// invariants:
///   * deploy validates owner / bondAsset (non-zero) and seeds DefaultMinBond,
///   * Deposit enforces the 0xE0_xx_xx_xx foreign-namespace chain-id prefix (cross-namespace
///     guard so a Neo-L2 chain id can never be bonded here), validates member/amount, and
///     conserves accounting (credit-before-transfer, FAULT-revert on transfer failure),
///   * the NEP-17 hook rejects unsolicited direct transfers (only Deposit-initiated transfers
///     credit), so stray tokens cannot be booked as bond out-of-band,
///   * Slash is gated to registered slashers OR the owner-witness and can never debit more than
///     the posted bond,
///   * Withdraw is owner-gated and can never debit more than the posted balance,
///   * governance setters (SetOwner / SetMinBond / RegisterSlasher / RevokeSlasher) are owner-gated.
///
/// The bond asset is mocked with the shared <see cref="MockNep17"/> (defined in UT_SharedBridge_Vm.cs).
/// Slasher authorization keys off <c>Runtime.CallingScriptHash</c>; we spoof it via
/// <c>engine.OnGetCallingScriptHash</c> to exercise the authorized-slasher path without deploying a
/// real slasher contract.
/// </summary>
[TestClass]
public class UT_ExternalBridgeBond_Vm
{
    private static readonly UInt160 AssetHash = UInt160.Parse("0x" + new string('a', 40));
    private static readonly UInt160 Member = UInt160.Parse("0x" + new string('b', 40));
    private static readonly UInt160 SlasherHash = UInt160.Parse("0x" + new string('c', 40));
    private static readonly UInt160 Recipient = UInt160.Parse("0x" + new string('d', 40));
    private static readonly UInt160 Stranger = UInt160.Parse("0x" + new string('e', 40));

    // External-chain ids MUST carry the 0xE0_xx_xx_xx foreign-namespace prefix.
    private const uint ExtChainA = 0xE0000001U;
    private const uint ExtChainB = 0xE0000002U;
    // A Neo L2 chain id (no foreign-namespace prefix) — must be rejected by Deposit.
    private const uint NeoChain = 1001U;

    /// <summary>Default min bond baked in at deploy (DefaultMinBond = 10 GAS at 8 decimals).</summary>
    private const long DefaultMinBond = 10_000_000_000L;

    /// <summary>
    /// Deploy the bond contract. owner defaults to engine.Sender so owner-gated witness checks pass;
    /// pass an explicit <paramref name="owner"/> to exercise the negative authorization paths. The
    /// bond asset is wired to a mock NEP-17 whose transfer returns <paramref name="transferOk"/>.
    /// </summary>
    private static NeoHubExternalBridgeBond Deploy(TestEngine engine, UInt160? owner = null, bool transferOk = true)
    {
        var o = owner ?? engine.Sender;
        engine.FromHash<MockNep17>(AssetHash, m =>
            m.Setup(c => c.Transfer(It.IsAny<UInt160?>(), It.IsAny<UInt160?>(), It.IsAny<BigInteger?>(), It.IsAny<object?>()))
                .Returns(transferOk),
            checkExistence: false);

        return engine.Deploy<NeoHubExternalBridgeBond>(
            NeoHubExternalBridgeBond.Nef, NeoHubExternalBridgeBond.Manifest,
            new object[] { o, AssetHash });
    }

    // ---------------------------------------------------------------------------------------------
    // Deploy-time input validation
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void Deploy_WiresOwnerAssetAndDefaultMinBond()
    {
        var engine = new TestEngine(true);
        var bond = Deploy(engine);

        Assert.AreEqual(engine.Sender, bond.Owner, "owner must be the deploy arg");
        Assert.AreEqual(AssetHash, bond.BondAsset, "bond asset must be the deploy arg");
        Assert.AreEqual((BigInteger)DefaultMinBond, bond.MinBond, "min bond defaults to DefaultMinBond");
        // No slasher is authorized by default — the owner must register the fraud verifier.
        Assert.IsFalse(bond.IsSlasher(SlasherHash)!.Value, "no slasher authorized at deploy");
        Assert.IsFalse(bond.IsSlasher(Stranger)!.Value, "unrelated account is not a slasher");
    }

    [TestMethod]
    public void Deploy_RejectsZeroOwner()
    {
        var engine = new TestEngine(true);
        engine.FromHash<MockNep17>(AssetHash, m =>
            m.Setup(c => c.Transfer(It.IsAny<UInt160?>(), It.IsAny<UInt160?>(), It.IsAny<BigInteger?>(), It.IsAny<object?>()))
                .Returns(true), checkExistence: false);

        Assert.ThrowsExactly<TestException>(() => engine.Deploy<NeoHubExternalBridgeBond>(
            NeoHubExternalBridgeBond.Nef, NeoHubExternalBridgeBond.Manifest,
            new object[] { UInt160.Zero, AssetHash }),
            "a zero owner must be rejected at deploy");
    }

    [TestMethod]
    public void Deploy_RejectsZeroBondAsset()
    {
        var engine = new TestEngine(true);
        Assert.ThrowsExactly<TestException>(() => engine.Deploy<NeoHubExternalBridgeBond>(
            NeoHubExternalBridgeBond.Nef, NeoHubExternalBridgeBond.Manifest,
            new object[] { engine.Sender, UInt160.Zero }),
            "a zero bond asset must be rejected at deploy");
    }

    // ---------------------------------------------------------------------------------------------
    // Deposit: foreign-namespace guard + accounting
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void Deposit_CreditsBondLedger_PerChainAndAccumulates()
    {
        var engine = new TestEngine(true);
        var bond = Deploy(engine);

        Assert.AreEqual((BigInteger)0, bond.GetBalance(ExtChainA, Member));
        bond.Deposit(ExtChainA, Member, 1000);
        Assert.AreEqual((BigInteger)1000, bond.GetBalance(ExtChainA, Member), "deposit credits the bond ledger");
        bond.Deposit(ExtChainA, Member, 500);
        Assert.AreEqual((BigInteger)1500, bond.GetBalance(ExtChainA, Member), "second deposit accumulates");
        // A different external chain is an independent ledger row — deposits do not leak across chains.
        Assert.AreEqual((BigInteger)0, bond.GetBalance(ExtChainB, Member), "other chain got nothing");
    }

    [TestMethod]
    public void Deposit_RejectsNonForeignNamespaceChainId()
    {
        // The foreign-namespace prefix guard: a Neo-L2 chain id (lacking the 0xE0_xx_xx_xx prefix)
        // must be rejected so committee bonds can never be indexed under an L2 chain id namespace.
        var engine = new TestEngine(true);
        var bond = Deploy(engine);

        Assert.ThrowsExactly<TestException>(() => bond.Deposit(NeoChain, Member, 100),
            "a chain id without the 0xE0 foreign-namespace prefix must be rejected");
        Assert.ThrowsExactly<TestException>(() => bond.Deposit(0, Member, 100),
            "chain id 0 lacks the foreign-namespace prefix and must be rejected");
        Assert.AreEqual((BigInteger)0, bond.GetBalance(NeoChain, Member), "rejected deposit credits nothing");
    }

    [TestMethod]
    public void Deposit_RejectsNonPositiveAmount_ZeroMember()
    {
        var engine = new TestEngine(true);
        var bond = Deploy(engine);

        Assert.ThrowsExactly<TestException>(() => bond.Deposit(ExtChainA, Member, 0),
            "zero deposit amount must be rejected");
        Assert.ThrowsExactly<TestException>(() => bond.Deposit(ExtChainA, Member, -1),
            "negative deposit amount must be rejected");
        Assert.ThrowsExactly<TestException>(() => bond.Deposit(ExtChainA, UInt160.Zero, 100),
            "zero member must be rejected");
        Assert.AreEqual((BigInteger)0, bond.GetBalance(ExtChainA, Member), "none of the rejected calls credited");
    }

    [TestMethod]
    public void Deposit_FailedAssetTransfer_FaultsAndRevertsAccounting()
    {
        // Bond asset transfer returns false (e.g. paused/frozen). Deposit credits the ledger BEFORE
        // the transfer, so the contract MUST FAULT on the false return and the NeoVM must revert the
        // credit — otherwise a member could inflate its bond without actually paying.
        var engine = new TestEngine(true);
        var bond = Deploy(engine, transferOk: false);

        Assert.ThrowsExactly<TestException>(() => bond.Deposit(ExtChainA, Member, 1000),
            "a failed asset transfer must FAULT the deposit");
        Assert.AreEqual((BigInteger)0, bond.GetBalance(ExtChainA, Member),
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
        var bond = Deploy(engine);

        Assert.ThrowsExactly<TestException>(() => bond.OnNEP17Payment(Member, 100, null),
            "direct (non-Deposit) transfer must be rejected by the NEP-17 hook");
    }

    [TestMethod]
    public void OnNEP17Payment_RejectsNonPositiveAmount()
    {
        var engine = new TestEngine(true);
        var bond = Deploy(engine);

        // The contract checks `amount > 0` FIRST (before the pending-transfer marker check), so a
        // zero-amount callback must abort on the amount guard specifically — NOT on the pending-
        // transfer guard. Capturing the abort reason makes this test fail if the amount>0 guard is
        // ever removed (in which case it would instead reach, and abort on, the pending-transfer guard).
        var ex = Assert.ThrowsExactly<TestException>(() => bond.OnNEP17Payment(Member, 0, null),
            "zero-amount NEP-17 callback must be rejected");
        StringAssert.Contains(ex.Message, "amount must be positive");
    }

    // ---------------------------------------------------------------------------------------------
    // hasMinBond accounting view
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void HasMinBond_TracksBalanceVersusThreshold()
    {
        var engine = new TestEngine(true);
        var bond = Deploy(engine);

        Assert.IsFalse(bond.HasMinBond(ExtChainA, Member)!.Value, "no bond -> below threshold");
        bond.Deposit(ExtChainA, Member, DefaultMinBond - 1);
        Assert.IsFalse(bond.HasMinBond(ExtChainA, Member)!.Value, "just under threshold");
        bond.Deposit(ExtChainA, Member, 1);
        Assert.IsTrue(bond.HasMinBond(ExtChainA, Member)!.Value, "exactly at threshold qualifies");
    }

    // ---------------------------------------------------------------------------------------------
    // Slash: slasher-OR-owner-gated + accounting conservation
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void Slash_ByRegisteredSlasher_DebitsBondAndPaysRecipient()
    {
        var engine = new TestEngine(true);
        var bond = Deploy(engine);
        bond.RegisterSlasher(SlasherHash);
        bond.Deposit(ExtChainA, Member, 1000);

        // Spoof CallingScriptHash so the bond contract sees a registered slasher as the caller.
        engine.OnGetCallingScriptHash = (current, expected) => SlasherHash;
        try
        {
            bond.Slash(ExtChainA, Member, 400, Recipient);
        }
        finally
        {
            engine.OnGetCallingScriptHash = null;
        }

        Assert.AreEqual((BigInteger)600, bond.GetBalance(ExtChainA, Member),
            "slash debits exactly the slashed amount");
    }

    [TestMethod]
    public void Slash_ByOwnerWitness_DevnetPath_DebitsBond()
    {
        // The owner-witness is also authorized to slash (devnet path before the fraud verifier is
        // wired). engine.Sender is auto-witnessed and is the owner, so the owner branch applies.
        var engine = new TestEngine(true);
        var bond = Deploy(engine); // owner == engine.Sender
        bond.Deposit(ExtChainA, Member, 1000);

        // The caller is the test harness (not a registered slasher); slash must pass on the
        // owner-witness branch alone.
        bond.Slash(ExtChainA, Member, 300, Recipient);
        Assert.AreEqual((BigInteger)700, bond.GetBalance(ExtChainA, Member),
            "owner-witness slash debits the bond on the devnet path");
    }

    [TestMethod]
    public void Slash_ByNonSlasherNonOwner_Faults_AndLeavesBondIntact()
    {
        var engine = new TestEngine(true);
        // Owner is a different account (Stranger) so the owner-witness branch cannot save the caller.
        var bond = Deploy(engine, owner: Stranger);
        bond.Deposit(ExtChainA, Member, 1000);

        // CallingScriptHash spoofed to an account that is NEITHER a registered slasher NOR the owner.
        // (Spoofing it to the owner would make CheckWitness(owner) pass via the contract-witness rule
        // — hash == CallingScriptHash — and the slash would wrongly succeed.)
        var nonOwnerCaller = UInt160.Parse("0x" + new string('f', 40));
        engine.OnGetCallingScriptHash = (current, expected) => nonOwnerCaller;
        try
        {
            Assert.ThrowsExactly<TestException>(() => bond.Slash(ExtChainA, Member, 400, Recipient),
                "only a registered slasher or the owner may slash");
        }
        finally
        {
            engine.OnGetCallingScriptHash = null;
        }

        Assert.AreEqual((BigInteger)1000, bond.GetBalance(ExtChainA, Member),
            "rejected slash must not touch the bond");
    }

    [TestMethod]
    public void Slash_MoreThanBond_Faults_NoNegativeBalance()
    {
        var engine = new TestEngine(true);
        var bond = Deploy(engine);
        bond.RegisterSlasher(SlasherHash);
        bond.Deposit(ExtChainA, Member, 1000);

        engine.OnGetCallingScriptHash = (current, expected) => SlasherHash;
        try
        {
            Assert.ThrowsExactly<TestException>(() => bond.Slash(ExtChainA, Member, 1001, Recipient),
                "cannot slash more than the posted bond");
        }
        finally
        {
            engine.OnGetCallingScriptHash = null;
        }

        Assert.AreEqual((BigInteger)1000, bond.GetBalance(ExtChainA, Member),
            "over-slash must not drive the balance negative");
    }

    [TestMethod]
    public void Slash_RejectsNonPositiveAmount_AndZeroRecipient()
    {
        var engine = new TestEngine(true);
        var bond = Deploy(engine);
        bond.RegisterSlasher(SlasherHash);
        bond.Deposit(ExtChainA, Member, 1000);

        engine.OnGetCallingScriptHash = (current, expected) => SlasherHash;
        try
        {
            Assert.ThrowsExactly<TestException>(() => bond.Slash(ExtChainA, Member, 0, Recipient),
                "zero slash amount must be rejected");
            Assert.ThrowsExactly<TestException>(() => bond.Slash(ExtChainA, Member, 100, UInt160.Zero),
                "zero recipient must be rejected");
        }
        finally
        {
            engine.OnGetCallingScriptHash = null;
        }

        Assert.AreEqual((BigInteger)1000, bond.GetBalance(ExtChainA, Member),
            "rejected slash must leave the bond intact");
    }

    [TestMethod]
    public void Slash_FailedPayout_FaultsAndRevertsDebit()
    {
        // Recipient is non-zero so a payout transfer is attempted; the asset transfer returns false,
        // which MUST FAULT so the bond decrement is reverted (no silent loss of accounting).
        // A single conditional mock distinguishes the two transfer directions by destination:
        //   * deposit pulls tokens IN  (to == the bond contract) -> succeeds, seeds the bond,
        //   * slash pays tokens OUT     (to == Recipient)         -> fails, must FAULT.
        // (The same hash can only be mocked once per test, so we cannot flip it mid-test.)
        var engine = new TestEngine(true);
        engine.FromHash<MockNep17>(AssetHash, m =>
        {
            m.Setup(c => c.Transfer(It.IsAny<UInt160?>(), It.Is<UInt160?>(to => to == Recipient),
                It.IsAny<BigInteger?>(), It.IsAny<object?>())).Returns(false);
            m.Setup(c => c.Transfer(It.IsAny<UInt160?>(), It.Is<UInt160?>(to => to != Recipient),
                It.IsAny<BigInteger?>(), It.IsAny<object?>())).Returns(true);
        }, checkExistence: false);
        var bond = engine.Deploy<NeoHubExternalBridgeBond>(
            NeoHubExternalBridgeBond.Nef, NeoHubExternalBridgeBond.Manifest,
            new object[] { engine.Sender, AssetHash });

        bond.RegisterSlasher(SlasherHash);
        bond.Deposit(ExtChainA, Member, 1000);
        Assert.AreEqual((BigInteger)1000, bond.GetBalance(ExtChainA, Member));

        engine.OnGetCallingScriptHash = (current, expected) => SlasherHash;
        try
        {
            Assert.ThrowsExactly<TestException>(() => bond.Slash(ExtChainA, Member, 400, Recipient),
                "a failed slash payout must FAULT");
        }
        finally
        {
            engine.OnGetCallingScriptHash = null;
        }

        Assert.AreEqual((BigInteger)1000, bond.GetBalance(ExtChainA, Member),
            "FAULT must revert the bond debit when payout fails");
    }

    // ---------------------------------------------------------------------------------------------
    // Withdraw: owner-gated + accounting conservation
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void Withdraw_ByOwner_DebitsBalance()
    {
        var engine = new TestEngine(true);
        var bond = Deploy(engine); // owner == engine.Sender (auto-witnessed)
        bond.Deposit(ExtChainA, Member, 1000);

        bond.Withdraw(ExtChainA, Member, 600);
        Assert.AreEqual((BigInteger)400, bond.GetBalance(ExtChainA, Member),
            "owner withdrawal debits exactly the requested amount");
    }

    [TestMethod]
    public void Withdraw_ByNonOwner_Faults()
    {
        var engine = new TestEngine(true);
        // Owner is a different account than the test signer -> the owner witness gate must reject.
        var bond = Deploy(engine, owner: Stranger);
        // Seed a balance with a working deposit (deposit is not owner-gated).
        bond.Deposit(ExtChainA, Member, 1000);

        Assert.ThrowsExactly<TestException>(() => bond.Withdraw(ExtChainA, Member, 100),
            "Withdraw is owner-gated");
        Assert.AreEqual((BigInteger)1000, bond.GetBalance(ExtChainA, Member),
            "rejected withdrawal must not debit");
    }

    [TestMethod]
    public void Withdraw_MoreThanBalance_Faults_NoNegativeBalance()
    {
        var engine = new TestEngine(true);
        var bond = Deploy(engine);
        bond.Deposit(ExtChainA, Member, 1000);

        Assert.ThrowsExactly<TestException>(() => bond.Withdraw(ExtChainA, Member, 1001),
            "cannot withdraw more than the posted balance");
        Assert.AreEqual((BigInteger)1000, bond.GetBalance(ExtChainA, Member),
            "over-withdrawal must not drive the balance negative");
    }

    [TestMethod]
    public void Withdraw_RejectsNonPositiveAmount()
    {
        var engine = new TestEngine(true);
        var bond = Deploy(engine);
        bond.Deposit(ExtChainA, Member, 1000);

        Assert.ThrowsExactly<TestException>(() => bond.Withdraw(ExtChainA, Member, 0),
            "zero withdrawal amount must be rejected");
    }

    [TestMethod]
    public void Withdraw_FailedTransfer_FaultsAndRevertsDebit()
    {
        // Conditional mock: deposit (to == bond contract) succeeds to seed the bond; the withdrawal
        // payout (to == Member) fails, which MUST FAULT and revert the balance debit.
        var engine = new TestEngine(true);
        engine.FromHash<MockNep17>(AssetHash, m =>
        {
            m.Setup(c => c.Transfer(It.IsAny<UInt160?>(), It.Is<UInt160?>(to => to == Member),
                It.IsAny<BigInteger?>(), It.IsAny<object?>())).Returns(false);
            m.Setup(c => c.Transfer(It.IsAny<UInt160?>(), It.Is<UInt160?>(to => to != Member),
                It.IsAny<BigInteger?>(), It.IsAny<object?>())).Returns(true);
        }, checkExistence: false);
        var bond = engine.Deploy<NeoHubExternalBridgeBond>(
            NeoHubExternalBridgeBond.Nef, NeoHubExternalBridgeBond.Manifest,
            new object[] { engine.Sender, AssetHash });

        bond.Deposit(ExtChainA, Member, 1000);
        Assert.AreEqual((BigInteger)1000, bond.GetBalance(ExtChainA, Member));

        Assert.ThrowsExactly<TestException>(() => bond.Withdraw(ExtChainA, Member, 600),
            "a failed withdrawal transfer must FAULT");
        Assert.AreEqual((BigInteger)1000, bond.GetBalance(ExtChainA, Member),
            "FAULT must revert the balance debit when transfer fails");
    }

    // ---------------------------------------------------------------------------------------------
    // Governance: owner-gated setters
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void SetMinBond_OwnerOnly_UpdatesThreshold()
    {
        var engine = new TestEngine(true);
        var bond = Deploy(engine);

        bond.MinBond = 5_000_000_000;
        Assert.AreEqual((BigInteger)5_000_000_000, bond.MinBond, "owner can change the min bond");
        Assert.ThrowsExactly<TestException>(() => bond.MinBond = 0,
            "non-positive min bond must be rejected");
    }

    [TestMethod]
    public void SetMinBond_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        var bond = Deploy(engine, owner: Stranger);

        Assert.ThrowsExactly<TestException>(() => bond.MinBond = 5_000_000_000,
            "SetMinBond is owner-gated");
        Assert.AreEqual((BigInteger)DefaultMinBond, bond.MinBond, "rejected set must not change state");
    }

    [TestMethod]
    public void SetOwner_OwnerOnly_TransfersOwnership_RejectsZero()
    {
        var engine = new TestEngine(true);
        var bond = Deploy(engine);

        Assert.ThrowsExactly<TestException>(() => bond.Owner = UInt160.Zero,
            "zero new owner must be rejected");
        bond.Owner = Stranger;
        Assert.AreEqual(Stranger, bond.Owner, "ownership transfers to the new owner");
        // The old owner (engine.Sender) can no longer act — the witness now belongs to Stranger.
        Assert.ThrowsExactly<TestException>(() => bond.MinBond = 7,
            "old owner loses authority after transfer");
    }

    [TestMethod]
    public void SetOwner_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        var bond = Deploy(engine, owner: Stranger);

        Assert.ThrowsExactly<TestException>(() => bond.Owner = engine.Sender,
            "SetOwner is owner-gated");
    }

    [TestMethod]
    public void RegisterAndRevokeSlasher_OwnerOnly_RoundTrips()
    {
        var engine = new TestEngine(true);
        var bond = Deploy(engine);
        var newSlasher = UInt160.Parse("0x" + new string('7', 40));

        Assert.IsFalse(bond.IsSlasher(newSlasher)!.Value);
        bond.RegisterSlasher(newSlasher);
        Assert.IsTrue(bond.IsSlasher(newSlasher)!.Value, "registered slasher is authorized");
        bond.RevokeSlasher(newSlasher);
        Assert.IsFalse(bond.IsSlasher(newSlasher)!.Value, "revoked slasher is de-authorized");
    }

    [TestMethod]
    public void RegisterSlasher_NonOwner_Faults_AndRejectsZero()
    {
        var engine = new TestEngine(true);
        var ownerBond = Deploy(engine);
        // Zero slasher rejected even for the owner.
        Assert.ThrowsExactly<TestException>(() => ownerBond.RegisterSlasher(UInt160.Zero),
            "zero slasher must be rejected");

        var engine2 = new TestEngine(true);
        var bond = Deploy(engine2, owner: Stranger);
        var newSlasher = UInt160.Parse("0x" + new string('7', 40));
        Assert.ThrowsExactly<TestException>(() => bond.RegisterSlasher(newSlasher),
            "RegisterSlasher is owner-gated");
        Assert.IsFalse(bond.IsSlasher(newSlasher)!.Value, "rejected register must not authorize");
    }

    [TestMethod]
    public void RevokeSlasher_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        var bond = Deploy(engine); // owner == engine.Sender
        bond.RegisterSlasher(SlasherHash);
        Assert.IsTrue(bond.IsSlasher(SlasherHash)!.Value);

        var engine2 = new TestEngine(true);
        var bond2 = Deploy(engine2, owner: Stranger);
        Assert.ThrowsExactly<TestException>(() => bond2.RevokeSlasher(SlasherHash),
            "RevokeSlasher is owner-gated");
    }
}
