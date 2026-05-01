namespace Neo.L2.Challenge.UnitTests;

[TestClass]
public class UT_Challenge_Metrics
{
    private static UInt256 H(byte b)
    {
        var bytes = new byte[32];
        bytes[0] = b;
        return new UInt256(bytes);
    }

    [TestMethod]
    public void BisectionGame_AtSettle_RecordsRoundsHistogram()
    {
        // 4-tx game, disagreement at index 2 → log2(4) = 2 rounds.
        var pre = H(0);
        var c = new[] { pre, H(1), H(2), H(3), H(99) };  // challenger
        var s = new[] { pre, H(1), H(2), H(3), H(50) };  // sequencer disagrees only at last index
        var metrics = new InMemoryMetrics();

        var game = new BisectionGame(c, s, metrics);
        game.RunToSettlement();

        var rounds = metrics.GetHistogram(MetricNames.BisectionRounds);
        Assert.AreEqual(1, rounds.Count, "one settle = one observation");
        Assert.IsTrue(rounds[0] >= 0 && rounds[0] <= 4, $"rounds within sane range, got {rounds[0]}");
    }

    [TestMethod]
    public void BisectionGame_DefaultsToNoOp_WhenNoMetrics()
    {
        var pre = H(0);
        var c = new[] { pre, H(99) };
        var s = new[] { pre, H(50) };

        var game = new BisectionGame(c, s); // no metrics
        game.RunToSettlement();
        Assert.IsTrue(game.IsSettled);
    }
}
