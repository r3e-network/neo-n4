using Neo.Cryptography.ECC;
using Neo.L2.ForcedInclusion;
using Neo.L2.Sequencer;

namespace Neo.L2.Censorship.UnitTests;

[TestClass]
public class UT_Censorship_Metrics
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

    [TestMethod]
    public async Task DetectOverdue_EmitsCensorshipReportsCounter()
    {
        var src = new InMemoryForcedInclusionSource(1001);
        for (ulong i = 1; i <= 3; i++)
            src.Enqueue(new ForcedInclusionEntry
            {
                Nonce = i,
                Sender = A(0xAA),
                TxHash = UInt256.Zero,
                SerializedTx = new byte[] { (byte)i },
                DeadlineUnixSeconds = 1_000_000,
            });

        var committee = new InMemorySequencerCommitteeProvider(1001);
        committee.Register(K(1), A(0x10));

        var clock = new FakeClock { NowUnixSeconds = 1_001_000 }; // overdue
        var metrics = new InMemoryMetrics();
        var detector = new CensorshipDetector(src, committee, clock, metrics: metrics);

        var reports = await detector.DetectOverdueAsync();
        Assert.AreEqual(3, reports.Count);
        Assert.AreEqual(3, metrics.GetCounter(MetricNames.CensorshipReports));
    }

    [TestMethod]
    public async Task DetectOverdue_NoReports_DoesNotEmit()
    {
        var src = new InMemoryForcedInclusionSource(1001);
        src.Enqueue(new ForcedInclusionEntry
        {
            Nonce = 1,
            Sender = A(0xAA),
            TxHash = UInt256.Zero,
            SerializedTx = new byte[] { 1 },
            DeadlineUnixSeconds = 1_000_000,
        });

        var committee = new InMemorySequencerCommitteeProvider(1001);
        committee.Register(K(1), A(0x10));

        var clock = new FakeClock { NowUnixSeconds = 999_999 }; // before deadline
        var metrics = new InMemoryMetrics();
        var detector = new CensorshipDetector(src, committee, clock, metrics: metrics);

        var reports = await detector.DetectOverdueAsync();
        Assert.AreEqual(0, reports.Count);
        Assert.AreEqual(0, metrics.GetCounter(MetricNames.CensorshipReports));
    }
}
