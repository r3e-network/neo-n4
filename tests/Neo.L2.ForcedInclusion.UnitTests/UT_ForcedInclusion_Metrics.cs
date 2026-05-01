namespace Neo.L2.ForcedInclusion.UnitTests;

[TestClass]
public class UT_ForcedInclusion_Metrics
{
    private static ForcedInclusionEntry Mk(ulong nonce) => new()
    {
        Nonce = nonce,
        Sender = UInt160.Zero,
        TxHash = UInt256.Zero,
        SerializedTx = new byte[] { (byte)nonce },
        DeadlineUnixSeconds = 100,
    };

    [TestMethod]
    public void Enqueue_IncrementsForcedInclusionObserved()
    {
        var metrics = new InMemoryMetrics();
        var src = new InMemoryForcedInclusionSource(1001, metrics);

        src.Enqueue(Mk(1));
        src.Enqueue(Mk(2));

        Assert.AreEqual(2, metrics.GetCounter(MetricNames.ForcedInclusionObserved));
    }

    [TestMethod]
    public void DefaultsToNoOp_WhenMetricsNull()
    {
        var src = new InMemoryForcedInclusionSource(1001);
        src.Enqueue(Mk(1));
        // no-throw
    }
}
