namespace Neo.L2.Challenge.UnitTests;

[TestClass]
public class UT_BisectionGame
{
    private static UInt256 Root(int seed)
    {
        var bytes = new byte[32];
        bytes[0] = (byte)seed;
        bytes[1] = (byte)(seed >> 8);
        return new UInt256(bytes);
    }

    /// <summary>
    /// Build a sequencer checkpoint array where the sequencer "lied" starting at
    /// <paramref name="firstWrongIndex"/>. From there onward, every checkpoint differs from
    /// the challenger's. The challenger's array is the ground truth.
    /// </summary>
    private static (UInt256[] challenger, UInt256[] sequencer) BuildScenario(int txCount, int firstWrongIndex)
    {
        var challenger = new UInt256[txCount + 1];
        var sequencer = new UInt256[txCount + 1];
        for (var i = 0; i <= txCount; i++)
        {
            challenger[i] = Root(i);                            // ground truth
            sequencer[i] = i < firstWrongIndex ? Root(i) : Root(1000 + i); // diverges from firstWrongIndex
        }
        return (challenger, sequencer);
    }

    [TestMethod]
    public void MaxRoundsFor_ExpectedValues()
    {
        Assert.AreEqual(0, BisectionGame.MaxRoundsFor(1));
        Assert.AreEqual(1, BisectionGame.MaxRoundsFor(2));
        Assert.AreEqual(2, BisectionGame.MaxRoundsFor(3));
        Assert.AreEqual(2, BisectionGame.MaxRoundsFor(4));
        Assert.AreEqual(3, BisectionGame.MaxRoundsFor(8));
        Assert.AreEqual(4, BisectionGame.MaxRoundsFor(15));
        Assert.AreEqual(4, BisectionGame.MaxRoundsFor(16));
        Assert.AreEqual(10, BisectionGame.MaxRoundsFor(1024));
    }

    [TestMethod]
    public void Constructor_RejectsMismatchedLengths()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new BisectionGame(new[] { Root(0) }, new[] { Root(0), Root(1) }));
    }

    [TestMethod]
    public void Constructor_RejectsTooShort()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new BisectionGame(new[] { Root(0) }, new[] { Root(0) }));
    }

    [TestMethod]
    public void Constructor_RejectsDisagreementAtPreState()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new BisectionGame(
            new[] { Root(0), Root(1) },
            new[] { Root(99), Root(2) }));
    }

    [TestMethod]
    public void Constructor_RejectsAgreementAtPostState()
    {
        // No actual dispute → game should refuse to start.
        Assert.ThrowsExactly<ArgumentException>(() => new BisectionGame(
            new[] { Root(0), Root(1) },
            new[] { Root(0), Root(1) }));
    }

    [TestMethod]
    public void TwoTxs_FirstRoundSettles()
    {
        var (c, s) = BuildScenario(2, firstWrongIndex: 1);
        var game = new BisectionGame(c, s);
        // [0..2]: agree at 0, disagree at 2; midpoint = 1.
        var advanced = game.RunRound();
        // After one round, [lo, hi] is either [0,1] or [1,2], both width 1 → settles.
        Assert.IsFalse(advanced);
        Assert.IsTrue(game.IsSettled);
        Assert.AreEqual(0, game.DisputedIndex);
    }

    [TestMethod]
    public void EightTxs_DisputeAt5_ConvergesInLogN()
    {
        var (c, s) = BuildScenario(8, firstWrongIndex: 5);
        var game = new BisectionGame(c, s);
        var disputed = game.RunToSettlement();
        Assert.AreEqual(4, disputed); // first index where they disagree is 4 (after applying tx[4])
        Assert.IsTrue(game.Rounds <= BisectionGame.MaxRoundsFor(8));
    }

    [TestMethod]
    public void DisputeAtFirstTx_SettlesAtIndex0()
    {
        var (c, s) = BuildScenario(8, firstWrongIndex: 1);
        var game = new BisectionGame(c, s);
        game.RunToSettlement();
        Assert.AreEqual(0, game.DisputedIndex);
    }

    [TestMethod]
    public void DisputeAtLastTx_SettlesAtIndexN_minus_1()
    {
        var (c, s) = BuildScenario(8, firstWrongIndex: 8);
        var game = new BisectionGame(c, s);
        game.RunToSettlement();
        Assert.AreEqual(7, game.DisputedIndex); // tx index 7 is the wrong one
    }

    [TestMethod]
    public void OneThousandTxs_BoundedRounds()
    {
        var (c, s) = BuildScenario(1000, firstWrongIndex: 423);
        var game = new BisectionGame(c, s);
        game.RunToSettlement();
        // 1000 txs → ceil(log2(1000)) = 10 rounds max.
        Assert.IsTrue(game.Rounds <= 10, $"used {game.Rounds} rounds, max 10");
        Assert.AreEqual(422, game.DisputedIndex);
    }

    [TestMethod]
    public void DisputedIndex_ThrowsWhenUnsettled()
    {
        var (c, s) = BuildScenario(8, firstWrongIndex: 5);
        var game = new BisectionGame(c, s);
        // Don't run any rounds yet (game ≠ settled because TxCount > 1).
        Assert.IsFalse(game.IsSettled);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = game.DisputedIndex);
    }
}
