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
    public async Task SingleOverdue_WithoutAttributionLeavesIdentityUnknown()
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
        Assert.AreEqual(ECCurve.Secp256r1.Infinity, reports[0].ResponsibleSequencer);
        Assert.AreEqual(UInt160.Zero, reports[0].ResponsibleSequencerAddress);
    }

    [TestMethod]
    public async Task ExplicitAttribution_NamesResponsibleSequencer()
    {
        var src = new InMemoryForcedInclusionSource(1001);
        src.Enqueue(MkEntry(1, 1_700_005_000));

        var committee = new InMemorySequencerCommitteeProvider(1001);
        committee.Register(K(1), A(0x10));
        var responsible = (await committee.GetActiveCommitteeAsync()).Single();

        var clock = new FakeClock { NowUnixSeconds = 1_700_006_000 };
        var detector = new CensorshipDetector(
            src,
            committee,
            clock,
            attributionProvider: new FixedAttributionProvider(responsible));

        var reports = await detector.DetectOverdueAsync();
        Assert.AreEqual(1, reports.Count);
        Assert.AreEqual(responsible.PublicKey, reports[0].ResponsibleSequencer);
        Assert.AreEqual(responsible.L1Address, reports[0].ResponsibleSequencerAddress);
    }

    [TestMethod]
    public async Task MultipleOverdue_WithoutAttributionRemainUnknown()
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
        Assert.IsTrue(reports.All(r => r.ResponsibleSequencer.Equals(ECCurve.Secp256r1.Infinity)));
        Assert.IsTrue(reports.All(r => r.ResponsibleSequencerAddress == UInt160.Zero));
    }

    [TestMethod]
    public async Task AttributionOutsideCommittee_FailsClosed()
    {
        var src = new InMemoryForcedInclusionSource(1001);
        src.Enqueue(MkEntry(1, 1_700_005_000));

        var committee = new InMemorySequencerCommitteeProvider(1001);
        committee.Register(K(1), A(0x10));
        var outsider = new CommitteeMember
        {
            PublicKey = K(2),
            L1Address = A(0x20),
            Status = 1,
            ExitsAtUnixSeconds = 0,
        };
        var detector = new CensorshipDetector(
            src,
            committee,
            new FakeClock { NowUnixSeconds = 1_700_006_000 },
            attributionProvider: new FixedAttributionProvider(outsider));

        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await detector.DetectOverdueAsync());
        StringAssert.Contains(error.Message, "outside the active committee");
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
    public void Constructor_RejectsNullSource()
    {
        // Pin CensorshipDetector.cs:40. The forced-inclusion source provides every entry
        // the detector inspects; null would NRE at the first InspectAsync.
        var committee = new InMemorySequencerCommitteeProvider(1001);
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new CensorshipDetector(null!, committee));
    }

    [TestMethod]
    public void Constructor_RejectsNullCommittee()
    {
        // Pin CensorshipDetector.cs:41. The committee provides ResponsibleSequencerAddress
        // for every report; null would NRE at the first deadline-passed entry.
        var src = new InMemoryForcedInclusionSource(1001);
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new CensorshipDetector(src, null!));
    }

    [TestMethod]
    public void Constructor_RejectsCrossChainCommittee()
    {
        var src = new InMemoryForcedInclusionSource(1001);
        var committee = new InMemorySequencerCommitteeProvider(1002);

        Assert.ThrowsExactly<ArgumentException>(() => new CensorshipDetector(src, committee));
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
    public async Task DetectOverdueAsync_SurfacesNullReturnFromSourceAsContractViolation()
    {
        // Regression for iter 172: a buggy IForcedInclusionSource that returns null from
        // DrainAsync would NRE inside the foreach, with no link to the source's contract
        // violation. Now surfaces as InvalidOperationException naming DrainAsync.
        var src = new BuggySource { ReturnsNullDrain = true };
        var committee = new InMemorySequencerCommitteeProvider(1001);
        var clock = new FakeClock { NowUnixSeconds = 1_700_010_000 };
        var detector = new CensorshipDetector(src, committee, clock);
        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await detector.DetectOverdueAsync());
        StringAssert.Contains(ex.Message, "DrainAsync");
    }

    [TestMethod]
    public async Task DetectOverdueAsync_SurfacesNullReturnFromCommitteeAsContractViolation()
    {
        // Same iter-172 pattern: a buggy ISequencerCommitteeProvider returning null from
        // GetActiveCommitteeAsync would NRE on .Count.
        var src = new InMemoryForcedInclusionSource(1001);
        src.Enqueue(MkEntry(1, 1_700_000_000));
        var committee = new BuggyCommittee();
        var clock = new FakeClock { NowUnixSeconds = 1_700_010_000 };
        var detector = new CensorshipDetector(src, committee, clock);
        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await detector.DetectOverdueAsync());
        StringAssert.Contains(ex.Message, "GetActiveCommitteeAsync");
    }

    private sealed class BuggySource : IForcedInclusionSource
    {
        public uint ChainId => 1001;
        public bool ReturnsNullDrain { get; init; }
        public ValueTask<bool> HasOverdueEntryAsync(uint nowUnixSeconds, CancellationToken cancellationToken = default)
            => new ValueTask<bool>(true);
        public ValueTask<IReadOnlyList<ForcedInclusionEntry>> DrainAsync(int max, CancellationToken cancellationToken = default)
            => new ValueTask<IReadOnlyList<ForcedInclusionEntry>>(ReturnsNullDrain ? null! : Array.Empty<ForcedInclusionEntry>());
        public ValueTask ConfirmConsumedAsync(ulong nonce, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }

    private sealed class BuggyCommittee : ISequencerCommitteeProvider
    {
        public uint ChainId => 1001;
        public ValueTask<IReadOnlyList<CommitteeMember>> GetActiveCommitteeAsync(CancellationToken cancellationToken = default)
            => new ValueTask<IReadOnlyList<CommitteeMember>>((IReadOnlyList<CommitteeMember>)null!);
        public ValueTask<bool> IsRegisteredAsync(ECPoint publicKey, CancellationToken cancellationToken = default)
            => new ValueTask<bool>(false);
        public ValueTask<int> GetMaxCommitteeSizeAsync(CancellationToken cancellationToken = default)
            => new ValueTask<int>(7);
    }

    private sealed class FixedAttributionProvider(CommitteeMember member) : ICensorshipAttributionProvider
    {
        public ValueTask<CommitteeMember?> ResolveResponsibleSequencerAsync(
            uint chainId,
            ForcedInclusionEntry entry,
            uint observedAtUnixSeconds,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<CommitteeMember?>(member);
        }
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
