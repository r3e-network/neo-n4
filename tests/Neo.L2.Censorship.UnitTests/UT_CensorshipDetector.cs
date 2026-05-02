using Neo.Cryptography.ECC;
using Neo.L2.ForcedInclusion;
using Neo.L2.Sequencer;

namespace Neo.L2.Censorship.UnitTests;

[TestClass]
public class UT_CensorshipDetector
{
    private static ECPoint K(byte seed)
    {
        var priv = new byte[32];
        for (var i = 0; i < 32; i++) priv[i] = (byte)(seed + i);
        return ECCurve.Secp256r1.G * priv;
    }

    private static UInt160 A(byte b)
    {
        var bytes = new byte[20];
        for (var i = 0; i < 20; i++) bytes[i] = b;
        return new UInt160(bytes);
    }

    private static ForcedInclusionEntry MkEntry(ulong nonce, uint deadline) => new()
    {
        Nonce = nonce,
        Sender = A(0xAA),
        TxHash = UInt256.Parse("0x" + new string('1', 64)),
        SerializedTx = new byte[] { (byte)nonce },
        DeadlineUnixSeconds = deadline,
    };

    [TestMethod]
    public async Task NoOverdue_ReturnsEmpty()
    {
        var src = new InMemoryForcedInclusionSource(1001);
        src.Enqueue(MkEntry(1, 1_700_010_000));

        var committee = new InMemorySequencerCommitteeProvider(1001);
        committee.Register(K(1), A(0x10));

        var clock = new FakeClock { NowUnixSeconds = 1_700_005_000 }; // before deadline
        var detector = new CensorshipDetector(src, committee, clock);

        var reports = await detector.DetectOverdueAsync();
        Assert.AreEqual(0, reports.Count);
    }

    [TestMethod]
    public async Task SingleOverdue_NamesResponsibleSequencer()
    {
        var src = new InMemoryForcedInclusionSource(1001);
        src.Enqueue(MkEntry(1, 1_700_005_000));

        var committee = new InMemorySequencerCommitteeProvider(1001);
        committee.Register(K(1), A(0x10));

        var clock = new FakeClock { NowUnixSeconds = 1_700_006_000 }; // 1000s past deadline
        var detector = new CensorshipDetector(src, committee, clock);

        var reports = await detector.DetectOverdueAsync();
        Assert.AreEqual(1, reports.Count);
        Assert.AreEqual(1UL, reports[0].ForcedInclusionNonce);
        Assert.AreEqual(1001U, reports[0].ChainId);
        Assert.AreEqual(K(1), reports[0].ResponsibleSequencer);
        Assert.AreEqual(A(0x10), reports[0].ResponsibleSequencerAddress);
    }

    [TestMethod]
    public async Task MultipleOverdue_AllReportSameResponsibleSequencer()
    {
        var src = new InMemoryForcedInclusionSource(1001);
        for (ulong i = 1; i <= 3; i++) src.Enqueue(MkEntry(i, 1_700_000_000));

        var committee = new InMemorySequencerCommitteeProvider(1001);
        committee.Register(K(1), A(0x10));
        committee.Register(K(2), A(0x20));

        var clock = new FakeClock { NowUnixSeconds = 1_700_010_000 };
        var detector = new CensorshipDetector(src, committee, clock);

        var reports = await detector.DetectOverdueAsync();
        Assert.AreEqual(3, reports.Count);
        // All 3 reports should name the same lowest-pubkey sequencer.
        var responsible = reports[0].ResponsibleSequencer;
        Assert.IsTrue(reports.All(r => r.ResponsibleSequencer.Equals(responsible)));
    }

    [TestMethod]
    public async Task NoCommittee_StillEmitsReports()
    {
        var src = new InMemoryForcedInclusionSource(1001);
        src.Enqueue(MkEntry(1, 1_700_005_000));

        var emptyCommittee = new InMemorySequencerCommitteeProvider(1001);

        var clock = new FakeClock { NowUnixSeconds = 1_700_006_000 };
        var detector = new CensorshipDetector(src, emptyCommittee, clock);

        var reports = await detector.DetectOverdueAsync();
        Assert.AreEqual(1, reports.Count);
        // No active committee → ResponsibleSequencer is the curve's identity sentinel.
        Assert.AreEqual(UInt160.Zero, reports[0].ResponsibleSequencerAddress);
    }

    [TestMethod]
    public async Task DefaultSlashAmount_PropagatesToReport()
    {
        var src = new InMemoryForcedInclusionSource(1001);
        src.Enqueue(MkEntry(1, 1_700_000_000));

        var committee = new InMemorySequencerCommitteeProvider(1001);
        committee.Register(K(1), A(0x10));

        var clock = new FakeClock { NowUnixSeconds = 1_700_010_000 };
        var detector = new CensorshipDetector(src, committee, clock);

        var reports = await detector.DetectOverdueAsync();
        Assert.AreEqual(new System.Numerics.BigInteger(1_000_000), reports[0].SlashAmount);
    }

    [TestMethod]
    public void Constructor_RejectsNegativeBaseSlashAmount()
    {
        // Regression: previously any BigInteger was accepted. A negative slash amount on
        // L1 would effectively reward the offending sequencer — clearly nonsensical.
        // Now: rejected at construction so the misconfig surfaces here, not when a slash
        // transaction reverts on L1.
        var src = new InMemoryForcedInclusionSource(1001);
        var committee = new InMemorySequencerCommitteeProvider(1001);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new CensorshipDetector(src, committee, baseSlashAmount: new System.Numerics.BigInteger(-1)));
    }

    [TestMethod]
    public void Constructor_AcceptsZeroBaseSlashAmount()
    {
        // Zero is allowed for warning-only modes — operator wants detection without
        // enforcement. The boundary is just non-negative.
        var src = new InMemoryForcedInclusionSource(1001);
        var committee = new InMemorySequencerCommitteeProvider(1001);
        var detector = new CensorshipDetector(src, committee, baseSlashAmount: System.Numerics.BigInteger.Zero);
        Assert.AreEqual(System.Numerics.BigInteger.Zero, detector.BaseSlashAmount);
    }

    [TestMethod]
    public async Task CustomSlashAmount_PropagatesToReport()
    {
        var src = new InMemoryForcedInclusionSource(1001);
        src.Enqueue(MkEntry(1, 1_700_000_000));

        var committee = new InMemorySequencerCommitteeProvider(1001);
        committee.Register(K(1), A(0x10));

        var clock = new FakeClock { NowUnixSeconds = 1_700_010_000 };
        var custom = new System.Numerics.BigInteger(50_000_000);
        var detector = new CensorshipDetector(src, committee, clock, baseSlashAmount: custom);

        var reports = await detector.DetectOverdueAsync();
        Assert.AreEqual(custom, reports[0].SlashAmount);
    }

    [TestMethod]
    public async Task PartiallyOverdue_OnlyOverdueReported()
    {
        var src = new InMemoryForcedInclusionSource(1001);
        src.Enqueue(MkEntry(1, 1_700_000_000));   // overdue at now=1_700_010_000
        src.Enqueue(MkEntry(2, 1_700_020_000));   // future

        var committee = new InMemorySequencerCommitteeProvider(1001);
        committee.Register(K(1), A(0x10));

        var clock = new FakeClock { NowUnixSeconds = 1_700_010_000 };
        var detector = new CensorshipDetector(src, committee, clock);

        var reports = await detector.DetectOverdueAsync();
        Assert.AreEqual(1, reports.Count);
        Assert.AreEqual(1UL, reports[0].ForcedInclusionNonce);
    }
}
