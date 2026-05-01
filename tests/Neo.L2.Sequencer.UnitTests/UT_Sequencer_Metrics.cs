using Neo.Cryptography.ECC;

namespace Neo.L2.Sequencer.UnitTests;

[TestClass]
public class UT_Sequencer_Metrics
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
    public void Register_IncrementsCounterAndGauge()
    {
        var metrics = new InMemoryMetrics();
        var c = new InMemorySequencerCommitteeProvider(1001, 21, metrics);

        c.Register(K(1), A(0x10));
        c.Register(K(2), A(0x20));
        c.Register(K(3), A(0x30));

        Assert.AreEqual(3, metrics.GetCounter(MetricNames.SequencersRegistered));
        Assert.AreEqual(3.0, metrics.GetGauge(MetricNames.SequencerCommitteeSize));
    }

    [TestMethod]
    public void BeginExit_IncrementsExitsStarted()
    {
        var metrics = new InMemoryMetrics();
        var c = new InMemorySequencerCommitteeProvider(1001, 21, metrics);
        c.Register(K(1), A(0x10));

        c.BeginExit(K(1), 1_000_000);

        Assert.AreEqual(1, metrics.GetCounter(MetricNames.SequencerExitsStarted));
        Assert.AreEqual(1.0, metrics.GetGauge(MetricNames.SequencerCommitteeSize), "still in committee until Finalize");
    }

    [TestMethod]
    public void Finalize_IncrementsExitsFinalized_AndDecrementsGauge()
    {
        var metrics = new InMemoryMetrics();
        var c = new InMemorySequencerCommitteeProvider(1001, 21, metrics);
        c.Register(K(1), A(0x10));
        c.Register(K(2), A(0x20));
        c.BeginExit(K(1), 1_000_000);

        c.Finalize(K(1), 1_000_001);

        Assert.AreEqual(1, metrics.GetCounter(MetricNames.SequencerExitsFinalized));
        Assert.AreEqual(1.0, metrics.GetGauge(MetricNames.SequencerCommitteeSize));
    }

    [TestMethod]
    public void DefaultsToNoOp_WhenMetricsNull()
    {
        var c = new InMemorySequencerCommitteeProvider(1001);
        c.Register(K(1), A(0x10));
        // no-throw
    }
}
