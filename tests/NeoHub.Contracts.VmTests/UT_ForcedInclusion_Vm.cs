using System.Numerics;
using Moq;
using Neo;
using Neo.SmartContract.Testing;
using Neo.SmartContract.Testing.Exceptions;

namespace NeoHub.Contracts.VmTests;

/// <summary>
/// VM-level tests for NeoHub.ForcedInclusion — the censorship/liveness contract. Executes the
/// enqueue / consume / report / slash paths in a real NeoVM (ChainRegistry.pauseChain and
/// SequencerBond.slash mocked) and pins the round-1/round-3 security changes: ReportCensorship is
/// permissionless report+pause, slashing is the separate owner-gated SlashReportedCensorship, it is
/// at-most-once, and a belated MarkConsumed does NOT immunize a reported sequencer.
/// </summary>
[TestClass]
public class UT_ForcedInclusion_Vm
{
    private const uint ChainId = 1001;
    private const uint Deadline = 3600;
    private static readonly UInt160 CrHash = UInt160.Parse("0x" + new string('7', 40));
    private static readonly UInt160 SbHash = UInt160.Parse("0x" + new string('8', 40));
    private static readonly UInt160 GasHash = UInt160.Parse("0x" + new string('6', 40));
    private static readonly UInt160 Sequencer = UInt160.Parse("0x" + new string('9', 40));

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
        bool wireEnforcement = true)
    {
        WireMocks(engine);
        var o = owner ?? engine.Sender;
        var fi = engine.Deploy<NeoHubForcedInclusion>(
            NeoHubForcedInclusion.Nef, NeoHubForcedInclusion.Manifest,
            new object[] { o, engine.Sender, (BigInteger)Deadline });
        if (owner is null && wireEnforcement)
        {
            fi.ChainRegistry = CrHash;
            fi.SequencerBond = SbHash;
            fi.CensorshipSlashAmount = 100;
        }
        return fi;
    }

    private static ulong Enqueue(NeoHubForcedInclusion fi) =>
        (ulong)fi.EnqueueForcedTransaction(ChainId, new byte[] { 0xAB, 0xCD }, UInt256.Zero)!;

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
    public void MarkConsumed_BySettlementManager_SetsConsumed_AndIsAtMostOnce()
    {
        var engine = new TestEngine(true);
        var fi = Deploy(engine); // settlementManager == engine.Sender
        Enqueue(fi);
        fi.MarkConsumed(ChainId, 1);
        Assert.IsTrue(fi.IsConsumed(ChainId, 1));
        Assert.ThrowsExactly<TestException>(() => fi.MarkConsumed(ChainId, 1), "double consume must fault");
    }

    [TestMethod]
    public void ReportCensorship_FalseBeforeDeadline_TrueAfter_AtMostOnce()
    {
        var engine = new TestEngine(true);
        var fi = Deploy(engine);
        Enqueue(fi);

        Assert.IsFalse(fi.ReportCensorship(ChainId, 1, Sequencer), "before the deadline, no report");
        Assert.IsFalse(fi.IsCensorshipReported(ChainId, 1));

        engine.PersistingBlock.Advance(TimeSpan.FromSeconds(Deadline + 1));
        Assert.IsTrue(fi.ReportCensorship(ChainId, 1, Sequencer), "overdue entry must report");
        Assert.IsTrue(fi.IsCensorshipReported(ChainId, 1));
        Assert.ThrowsExactly<TestException>(() => fi.ReportCensorship(ChainId, 1, Sequencer), "double report must fault");
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
        fi.ReportCensorship(ChainId, 1, Sequencer);

        fi.SlashReportedCensorship(ChainId, 1, Sequencer);
        Assert.IsTrue(fi.IsCensorshipSlashed(ChainId, 1));
        Assert.ThrowsExactly<TestException>(() => fi.SlashReportedCensorship(ChainId, 1, Sequencer), "double slash must fault");
    }

    [TestMethod]
    public void SlashReportedCensorship_StillSlashesAfterLateInclusion_Round3Fix()
    {
        // Round-3 [8] fix: a belated MarkConsumed after a censorship report must NOT immunize the
        // sequencer — SlashReportedCensorship is intentionally NOT gated on !IsConsumed.
        var engine = new TestEngine(true);
        var fi = Deploy(engine);
        Enqueue(fi);
        engine.PersistingBlock.Advance(TimeSpan.FromSeconds(Deadline + 1));
        fi.ReportCensorship(ChainId, 1, Sequencer);

        fi.MarkConsumed(ChainId, 1); // late inclusion AFTER the report
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
}
