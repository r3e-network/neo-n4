namespace Neo.Plugins.L2Gateway.UnitTests;

/// <summary>
/// Tests that <see cref="BinaryTreeAggregator"/> emits <c>l2.gateway.aggregations</c>,
/// <c>l2.gateway.batches_aggregated</c>, <c>l2.gateway.aggregation_rounds</c>, and
/// <c>l2.gateway.aggregation_latency_ms</c> on each successful aggregation.
/// </summary>
[TestClass]
public class UT_BinaryTreeAggregator_Metrics
{
    private static UInt256 H(char c)
    {
        var bytes = new byte[32];
        for (var i = 0; i < 32; i++) bytes[i] = (byte)c;
        return new UInt256(bytes);
    }

    private static L2BatchCommitment MkBatch(uint chainId, byte[] proof, UInt256 l2L2Root)
    {
        var z = UInt256.Zero;
        return new L2BatchCommitment
        {
            ChainId = chainId,
            BatchNumber = 1,
            FirstBlock = 100,
            LastBlock = 200,
            PreStateRoot = z,
            PostStateRoot = z,
            TxRoot = z,
            ReceiptRoot = z,
            WithdrawalRoot = z,
            L2ToL1MessageRoot = z,
            L2ToL2MessageRoot = l2L2Root,
            DACommitment = z,
            PublicInputHash = z,
            ProofType = ProofType.Multisig,
            Proof = proof,
        };
    }

    [TestMethod]
    public void Aggregate_FourBatches_RecordsTwoRounds_And_BatchCount()
    {
        var metrics = new InMemoryMetrics();
        var agg = new BinaryTreeAggregator(metrics: metrics);

        agg.Submit(MkBatch(1001, new byte[] { 0x01 }, H('a')));
        agg.Submit(MkBatch(1002, new byte[] { 0x02 }, H('b')));
        agg.Submit(MkBatch(1003, new byte[] { 0x03 }, H('c')));
        agg.Submit(MkBatch(1004, new byte[] { 0x04 }, H('d')));

        var result = agg.Aggregate();
        Assert.IsNotNull(result);

        Assert.AreEqual(1, metrics.GetCounter(MetricNames.GatewayAggregations));
        Assert.AreEqual(4, metrics.GetCounter(MetricNames.GatewayBatchesAggregated));
        var rounds = metrics.GetHistogram(MetricNames.GatewayAggregationRounds);
        Assert.AreEqual(1, rounds.Count);
        Assert.AreEqual(2.0, rounds[0], "log2(4) == 2 rounds");
        Assert.AreEqual(1, metrics.GetHistogram(MetricNames.GatewayAggregationLatencyMs).Count);
    }

    [TestMethod]
    public void Aggregate_SingleBatch_RecordsZeroRounds()
    {
        var metrics = new InMemoryMetrics();
        var agg = new BinaryTreeAggregator(metrics: metrics);

        agg.Submit(MkBatch(1001, new byte[] { 0x01 }, H('a')));
        agg.Aggregate();

        var rounds = metrics.GetHistogram(MetricNames.GatewayAggregationRounds);
        Assert.AreEqual(1, rounds.Count);
        Assert.AreEqual(0.0, rounds[0], "1 leaf needs 0 rounds");
    }

    [TestMethod]
    public void Aggregate_Empty_DoesNotEmit()
    {
        var metrics = new InMemoryMetrics();
        var agg = new BinaryTreeAggregator(metrics: metrics);

        Assert.IsNull(agg.Aggregate());

        Assert.AreEqual(0, metrics.GetCounter(MetricNames.GatewayAggregations));
        Assert.AreEqual(0, metrics.GetHistogram(MetricNames.GatewayAggregationRounds).Count);
    }

    [TestMethod]
    public void RepeatedAggregations_AccumulateBatchCount()
    {
        var metrics = new InMemoryMetrics();
        var agg = new BinaryTreeAggregator(metrics: metrics);

        agg.Submit(MkBatch(1001, new byte[] { 0x01 }, H('a')));
        agg.Submit(MkBatch(1002, new byte[] { 0x02 }, H('b')));
        agg.Aggregate(); // 2 batches → 1 round

        agg.Submit(MkBatch(1003, new byte[] { 0x03 }, H('c')));
        agg.Aggregate(); // 1 batch → 0 rounds

        Assert.AreEqual(2, metrics.GetCounter(MetricNames.GatewayAggregations));
        Assert.AreEqual(3, metrics.GetCounter(MetricNames.GatewayBatchesAggregated));
        var rounds = metrics.GetHistogram(MetricNames.GatewayAggregationRounds);
        Assert.AreEqual(2, rounds.Count);
    }

    [TestMethod]
    public void DefaultsToNoOp_WhenNoMetrics()
    {
        // Existing call sites without the metrics arg keep working.
        var agg = new BinaryTreeAggregator();
        agg.Submit(MkBatch(1001, new byte[] { 0x01 }, H('a')));
        var result = agg.Aggregate();
        Assert.IsNotNull(result);
    }

    [TestMethod]
    public void WithMetrics_PreservesPendingSubmissions()
    {
        // Symmetric with iter 70/71 fixes — the in-place metric swap must preserve the
        // aggregator's pending submission list (re-constructing would lose them).
        var initial = new InMemoryMetrics();
        var agg = new BinaryTreeAggregator(metrics: initial);
        agg.Submit(MkBatch(1001, new byte[] { 0x01 }, H('a')));
        agg.Submit(MkBatch(1002, new byte[] { 0x02 }, H('b')));

        var second = new InMemoryMetrics();
        agg.WithMetrics(second);
        Assert.AreEqual(2, agg.PendingCount, "pending list survives the rewire");

        var result = agg.Aggregate();
        Assert.IsNotNull(result);
        Assert.AreEqual(2, result!.Constituents.Count);
        Assert.AreEqual(1, second.GetCounter(MetricNames.GatewayAggregations), "post-rewire emit hits new sink");
        Assert.AreEqual(0, initial.GetCounter(MetricNames.GatewayAggregations), "pre-rewire never aggregated");
    }
}
