using System.ComponentModel;
using System.Numerics;
using Moq;
using Neo;
using Neo.SmartContract.Testing;
using Neo.SmartContract.Testing.Exceptions;

namespace NeoHub.Contracts.VmTests;

/// <summary>Canonical finalized transaction-root surface exposed by SettlementManager.</summary>
public abstract class MockForcedInclusion_SettlementManager(SmartContractInitialize initialize) : SmartContract(initialize)
{
    [DisplayName("getFinalizedTxRoot")]
    public abstract UInt256? GetFinalizedTxRoot(BigInteger? chainId, BigInteger? batchNumber);
}

/// <summary>
/// VM-level tests for NeoHub.ForcedInclusion — the censorship/liveness contract. Executes the
/// enqueue / consume / report / slash paths in a real NeoVM (ChainRegistry.pauseChain and
/// SequencerBond.slash mocked) and pins the round-1/round-3 security changes: ReportCensorship is
/// permissionless unattributed report+pause, slashing is the separate owner-gated
/// SlashReportedCensorship, it is at-most-once, and a belated finalized-proof Consume does NOT
/// immunize a governance-attributed sequencer.
/// </summary>
[TestClass]
public class UT_ForcedInclusion_Vm
{
    private const uint ChainId = 1001;
    private const uint Deadline = 3600;
    private static readonly UInt160 CrHash = UInt160.Parse("0x" + new string('7', 40));
    private static readonly UInt160 SbHash = UInt160.Parse("0x" + new string('8', 40));
    private static readonly UInt160 GasHash =
        UInt160.Parse("0xd2a4cff31913016155e38e474a2c06d08be276cf");
    private static readonly UInt160 NonNativeGasHash = UInt160.Parse("0x" + new string('6', 40));
    private static readonly UInt160 Sequencer = UInt160.Parse("0x" + new string('9', 40));
    private static readonly UInt160 FeeRecipient = UInt160.Parse("0x" + new string('a', 40));
    private static readonly UInt160 SettlementManagerHash = UInt160.Parse("0x" + new string('5', 40));

    private static void WireMocks(TestEngine engine)
    {
        engine.FromHash<NeoHubChainRegistry>(CrHash,
            m => m.Setup(c => c.PauseChain(It.IsAny<BigInteger?>())), checkExistence: false);
        engine.FromHash<NeoHubSequencerBond>(SbHash,
            m => m.Setup(c => c.Slash(It.IsAny<BigInteger?>(), It.IsAny<UInt160?>(), It.IsAny<BigInteger?>(), It.IsAny<UInt160?>())),
            checkExistence: false);
    }

    /// <summary>Deploy FI. owner==settlementManager==engine.Sender so the owner/SM witness checks
    /// pass; ChainRegistry + SequencerBond + slash amount wired to mocks.</summary>
    private static NeoHubForcedInclusion Deploy(
        TestEngine engine,
        UInt160? owner = null,
        bool wireEnforcement = true,
        UInt160? settlementManager = null)
    {
        WireMocks(engine);
        var o = owner ?? engine.Sender;
        var fi = engine.Deploy<NeoHubForcedInclusion>(
            NeoHubForcedInclusion.Nef, NeoHubForcedInclusion.Manifest,
            new object[] { o, settlementManager ?? engine.Sender, (BigInteger)Deadline });
        if (owner is null && wireEnforcement)
        {
            fi.ChainRegistry = CrHash;
            fi.SequencerBond = SbHash;
            fi.CensorshipSlashAmount = 100;
        }
        return fi;
    }

    private static readonly byte[] ForcedTransaction = [0xAB, 0xCD];
    private static UInt256 ForcedTransactionHash => new(Neo.Cryptography.Crypto.Hash256(ForcedTransaction));

    private static ulong Enqueue(NeoHubForcedInclusion fi) =>
        (ulong)fi.EnqueueForcedTransaction(ChainId, ForcedTransaction, ForcedTransactionHash)!;

    private static UInt256 PairRoot(UInt256 left, UInt256 right)
    {
        var input = new byte[64];
        left.GetSpan().CopyTo(input);
        right.GetSpan().CopyTo(input.AsSpan(32));
        return new UInt256(Neo.Cryptography.Crypto.Hash256(input));
    }

    private static void WireFinalizedTxRoot(TestEngine engine, UInt256 root, ulong batchNumber = 5)
    {
        engine.FromHash<MockForcedInclusion_SettlementManager>(SettlementManagerHash, mock =>
        {
            mock.Setup(contract => contract.GetFinalizedTxRoot(
                    It.IsAny<BigInteger?>(), It.IsAny<BigInteger?>()))
                .Returns(UInt256.Zero);
            mock.Setup(contract => contract.GetFinalizedTxRoot(
                    (BigInteger)ChainId, (BigInteger)batchNumber))
                .Returns(root);
        }, checkExistence: false);
    }

    [TestMethod]
    public void IsProductionReady_RequiresSpamControlPauseAndSlashingConfiguration()
    {
        var engine = new TestEngine(true);
        var fi = Deploy(engine, wireEnforcement: false);

        Assert.IsFalse(fi.IsProductionReady);
        fi.ChainRegistry = CrHash;
        Assert.IsFalse(fi.IsProductionReady);
        fi.SequencerBond = SbHash;
        Assert.IsFalse(fi.IsProductionReady);
        fi.CensorshipSlashAmount = 100;
        Assert.IsFalse(fi.IsProductionReady);
        fi.GasToken = GasHash;
        Assert.IsFalse(fi.IsProductionReady);
        fi.FeeRecipient = engine.Sender;
        Assert.IsFalse(fi.IsProductionReady);
        fi.Fee = 100_000;

        Assert.IsTrue(fi.IsProductionReady);
    }

    [TestMethod]
    public void GasToken_RejectsNonNativeContractAtDeployAndUpdate()
    {
        var engine = new TestEngine(true);
        Assert.ThrowsExactly<TestException>(() => engine.Deploy<NeoHubForcedInclusion>(
            NeoHubForcedInclusion.Nef,
            NeoHubForcedInclusion.Manifest,
            new object[]
            {
                engine.Sender,
                engine.Sender,
                (BigInteger)Deadline,
                NonNativeGasHash,
            }));

        var fi = Deploy(engine);
        Assert.ThrowsExactly<TestException>(() => fi.GasToken = NonNativeGasHash);
        Assert.AreEqual(UInt160.Zero, fi.GasToken);
    }

    [TestMethod]
    public void Enqueue_StoresEntry_IncrementsNonce()
    {
        var engine = new TestEngine(true);
        var fi = Deploy(engine);
        var n1 = Enqueue(fi);
        var n2 = Enqueue(fi);
        Assert.AreEqual(1UL, n1);
        Assert.AreEqual(2UL, n2);
        Assert.IsTrue(fi.GetEntry(ChainId, 1)!.Length > 0, "entry must be stored");
        Assert.IsFalse(fi.IsConsumed(ChainId, 1));
    }

    [TestMethod]
    public void Enqueue_WithProductionFee_ChargesWitnessedTransactionSenderInNativeGas()
    {
        var engine = new TestEngine(true);
        var fi = Deploy(engine);
        const long fee = 100_000;
        fi.GasToken = GasHash;
        fi.FeeRecipient = FeeRecipient;
        fi.Fee = fee;
        var senderBalanceBefore = (BigInteger)engine.Native.GAS.BalanceOf(engine.Sender)!;
        var recipientBalanceBefore = (BigInteger)engine.Native.GAS.BalanceOf(FeeRecipient)!;

        var nonce = Enqueue(fi);

        Assert.AreEqual(1UL, nonce);
        Assert.AreEqual(senderBalanceBefore - fee, engine.Native.GAS.BalanceOf(engine.Sender));
        Assert.AreEqual(recipientBalanceBefore + fee,
            engine.Native.GAS.BalanceOf(FeeRecipient));
        CollectionAssert.AreEqual(
            engine.Sender.GetSpan().ToArray(),
            fi.GetEntry(ChainId, nonce)![..UInt160.Length],
            "the authenticated transaction sender must be committed as the forced-tx submitter");
    }

    [TestMethod]
    public void Enqueue_FailedNativeGasTransfer_RollsBackNonceAndEntry()
    {
        var engine = new TestEngine(true);
        var fi = Deploy(engine);
        fi.GasToken = GasHash;
        fi.FeeRecipient = FeeRecipient;
        var senderBalance = (BigInteger)engine.Native.GAS.BalanceOf(engine.Sender)!;
        var recipientBalance = (BigInteger)engine.Native.GAS.BalanceOf(FeeRecipient)!;
        fi.Fee = senderBalance + 1;

        Assert.ThrowsExactly<TestException>(() => Enqueue(fi));
        Assert.AreEqual(0, fi.GetEntry(ChainId, 1)!.Length,
            "a failed fee transfer must not leave an orphaned entry");
        Assert.AreEqual(senderBalance, engine.Native.GAS.BalanceOf(engine.Sender));
        Assert.AreEqual(recipientBalance, engine.Native.GAS.BalanceOf(FeeRecipient));

        fi.Fee = 1;
        Assert.AreEqual(1UL, Enqueue(fi),
            "the failed invocation must not consume the first nonce");
    }

    [TestMethod]
    public void Consume_ValidFinalizedTransactionProof_SetsConsumed_AndIsAtMostOnce()
    {
        var engine = new TestEngine(true);
        var sibling = new UInt256(Enumerable.Repeat((byte)0x22, 32).ToArray());
        WireFinalizedTxRoot(engine, PairRoot(ForcedTransactionHash, sibling));
        var fi = Deploy(engine, settlementManager: SettlementManagerHash);
        Enqueue(fi);
        IList<object> proof = new object[] { sibling.GetSpan().ToArray() };

        fi.Consume(ChainId, 5, 1, proof, 0);
        Assert.IsTrue(fi.IsConsumed(ChainId, 1));
        Assert.ThrowsExactly<TestException>(() => fi.Consume(ChainId, 5, 1, proof, 0),
            "double consume must fault");
    }

    [TestMethod]
    public void Consume_UnfinalizedWrongOrMalformedProof_FaultsWithoutConsuming()
    {
        var engine = new TestEngine(true);
        WireFinalizedTxRoot(engine, UInt256.Zero);
        var fi = Deploy(engine, settlementManager: SettlementManagerHash);
        Enqueue(fi);
        Assert.ThrowsExactly<TestException>(() => fi.Consume(ChainId, 5, 1, Array.Empty<object>(), 0));
        Assert.IsFalse(fi.IsConsumed(ChainId, 1));

        var secondEngine = new TestEngine(true);
        var sibling = new UInt256(Enumerable.Repeat((byte)0x22, 32).ToArray());
        WireFinalizedTxRoot(secondEngine, PairRoot(ForcedTransactionHash, sibling));
        var second = Deploy(secondEngine, settlementManager: SettlementManagerHash);
        Enqueue(second);
        Assert.ThrowsExactly<TestException>(() => second.Consume(
            ChainId, 5, 1, new object[] { new byte[32] }, 0));
        Assert.ThrowsExactly<TestException>(() => second.Consume(
            ChainId, 5, 1, new object[] { new byte[31] }, 0));
        Assert.ThrowsExactly<TestException>(() => second.Consume(
            ChainId, 5, 1, new object[] { sibling.GetSpan().ToArray() }, 2));
        Assert.IsFalse(second.IsConsumed(ChainId, 1));
        Assert.IsFalse(NeoHubForcedInclusion.Manifest.Abi.Methods.Any(method => method.Name == "markConsumed"),
            "the witness-only arbitrary-nonce bypass must not remain in the ABI");
    }

    [TestMethod]
    public void Enqueue_RejectsCallerSuppliedHashThatDoesNotMatchTransaction()
    {
        var engine = new TestEngine(true);
        var fi = Deploy(engine);
        Assert.ThrowsExactly<TestException>(() => fi.EnqueueForcedTransaction(
            ChainId, ForcedTransaction, UInt256.Zero));
        Assert.AreEqual(0, fi.GetEntry(ChainId, 1)!.Length);
    }

    [TestMethod]
    public void ReportCensorship_FalseBeforeDeadline_TrueAfter_AtMostOnce()
    {
        var engine = new TestEngine(true);
        var fi = Deploy(engine);
        Enqueue(fi);

        Assert.IsFalse(fi.ReportCensorship(ChainId, 1, UInt160.Zero), "before the deadline, no report");
        Assert.IsFalse(fi.IsCensorshipReported(ChainId, 1));

        engine.PersistingBlock.Advance(TimeSpan.FromSeconds(Deadline + 1));
        Assert.IsTrue(fi.ReportCensorship(ChainId, 1, UInt160.Zero), "overdue entry must report");
        Assert.IsTrue(fi.IsCensorshipReported(ChainId, 1));
        Assert.ThrowsExactly<TestException>(() => fi.ReportCensorship(ChainId, 1, UInt160.Zero), "double report must fault");
    }

    [TestMethod]
    public void ReportCensorship_UnknownAttributionStillRecordsAndPauses()
    {
        var engine = new TestEngine(true);
        var fi = Deploy(engine);
        Enqueue(fi);
        engine.PersistingBlock.Advance(TimeSpan.FromSeconds(Deadline + 1));

        UInt160? reportedSequencer = null;
        fi.OnSequencerCensorshipReported += (_, _, sequencer) => reportedSequencer = sequencer;

        Assert.IsTrue(fi.ReportCensorship(ChainId, 1, UInt160.Zero));
        Assert.IsTrue(fi.IsCensorshipReported(ChainId, 1));
        Assert.IsFalse(fi.IsCensorshipSlashed(ChainId, 1));
        Assert.AreEqual(UInt160.Zero, reportedSequencer,
            "permissionless reports must emit unattributed evidence");
    }

    [TestMethod]
    public void ReportCensorship_CallerSuppliedAttribution_FaultsWithoutRecording()
    {
        var engine = new TestEngine(true);
        var fi = Deploy(engine);
        Enqueue(fi);
        engine.PersistingBlock.Advance(TimeSpan.FromSeconds(Deadline + 1));

        Assert.ThrowsExactly<TestException>(() => fi.ReportCensorship(ChainId, 1, Sequencer),
            "a permissionless reporter must not be able to frame a sequencer");
        Assert.IsFalse(fi.IsCensorshipReported(ChainId, 1));
    }

    [TestMethod]
    public void SlashReportedCensorship_RequiresReport_ThenSlashes_AtMostOnce()
    {
        var engine = new TestEngine(true);
        var fi = Deploy(engine);
        Enqueue(fi);

        // No report yet -> slashing is rejected (round-3: slash is gated on an existing report).
        Assert.ThrowsExactly<TestException>(() => fi.SlashReportedCensorship(ChainId, 1, Sequencer), "no report -> no slash");

        engine.PersistingBlock.Advance(TimeSpan.FromSeconds(Deadline + 1));
        fi.ReportCensorship(ChainId, 1, UInt160.Zero);

        fi.SlashReportedCensorship(ChainId, 1, Sequencer);
        Assert.IsTrue(fi.IsCensorshipSlashed(ChainId, 1));
        Assert.ThrowsExactly<TestException>(() => fi.SlashReportedCensorship(ChainId, 1, Sequencer), "double slash must fault");
    }

    [TestMethod]
    public void SlashReportedCensorship_StillSlashesAfterLateInclusion_Round3Fix()
    {
        // Round-3 [8] fix: a belated Consume after a censorship report must NOT immunize the
        // sequencer — SlashReportedCensorship is intentionally NOT gated on !IsConsumed.
        var engine = new TestEngine(true);
        WireFinalizedTxRoot(engine, ForcedTransactionHash);
        var fi = Deploy(engine, settlementManager: SettlementManagerHash);
        Enqueue(fi);
        engine.PersistingBlock.Advance(TimeSpan.FromSeconds(Deadline + 1));
        fi.ReportCensorship(ChainId, 1, UInt160.Zero);

        fi.Consume(ChainId, 5, 1, Array.Empty<object>(), 0); // late inclusion AFTER the report
        Assert.IsTrue(fi.IsConsumed(ChainId, 1));

        fi.SlashReportedCensorship(ChainId, 1, Sequencer); // must still succeed
        Assert.IsTrue(fi.IsCensorshipSlashed(ChainId, 1), "late inclusion must not block the slash");
    }

    [TestMethod]
    public void SlashReportedCensorship_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        // owner is a different account than the test signer -> the owner gate must reject.
        var fi = Deploy(engine, owner: UInt160.Parse("0x" + new string('1', 40)));
        Assert.ThrowsExactly<TestException>(() => fi.SlashReportedCensorship(ChainId, 1, Sequencer),
            "SlashReportedCensorship is owner-gated");
    }

    [TestMethod]
    public void SlashReportedCensorship_NonOwnerCannotUseValidReport()
    {
        var engine = new TestEngine(true);
        var fi = Deploy(
            engine,
            owner: UInt160.Parse("0x" + new string('1', 40)),
            wireEnforcement: false);
        Enqueue(fi);
        engine.PersistingBlock.Advance(TimeSpan.FromSeconds(Deadline + 1));
        Assert.IsTrue(fi.ReportCensorship(ChainId, 1, UInt160.Zero));

        Assert.ThrowsExactly<TestException>(
            () => fi.SlashReportedCensorship(ChainId, 1, Sequencer),
            "a valid permissionless report must not bypass governance attribution");
        Assert.IsFalse(fi.IsCensorshipSlashed(ChainId, 1));
    }
}
