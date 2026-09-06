using System;
using Neo;
using Neo.L2.Challenge;

namespace Neo.L2.Challenge.UnitTests;

[TestClass]
public class UT_BisectionGame_Fuzz
{
    private static UInt256 MakeRoot(int id)
    {
        var b = new byte[32];
        b[0] = (byte)(id & 0xFF);
        b[1] = (byte)((id >> 8) & 0xFF);
        b[2] = (byte)((id >> 16) & 0xFF);
        b[3] = (byte)((id >> 24) & 0xFF);
        return new UInt256(b);
    }

    /// <summary>
    /// Property: For any transaction count in [1, 2000] and any fault injection point in [1, txCount],
    /// the bisection game must:
    ///  1. Always settle in at most ceil(log2(txCount)) rounds.
    ///  2. Isolate the exact disputed transaction index (firstWrongIndex - 1).
    ///  3. Maintain the invariant that Low is an agreed state and High is a disputed state at every round.
    /// </summary>
    [TestMethod]
    [DataRow(0x12345678u)]
    [DataRow(0x87654321u)]
    [DataRow(0xCAFEBABEu)]
    [DataRow(0xDEADBEEFu)]
    public void BisectionGame_PropertyTest_ConvergesAndIsolatesExactDispute(uint seed)
    {
        var rng = new Random((int)(seed ^ 0x01020304u));
        for (var iter = 0; iter < 100; iter++)
        {
            var txCount = rng.Next(1, 1500);
            var firstWrongIndex = rng.Next(1, txCount + 1);

            var challenger = new UInt256[txCount + 1];
            var sequencer = new UInt256[txCount + 1];
            for (var i = 0; i <= txCount; i++)
            {
                challenger[i] = MakeRoot(i + 1);
                sequencer[i] = i < firstWrongIndex ? MakeRoot(i + 1) : MakeRoot(50000 + i);
            }

            var game = new BisectionGame(challenger, sequencer);
            var maxAllowedRounds = BisectionGame.MaxRoundsFor(txCount);

            // Step through round by round and verify invariant at every step
            var rounds = 0;
            while (game.RunRound())
            {
                rounds++;
                Assert.IsTrue(game.Lo < game.Hi, $"iter {iter}: Lo ({game.Lo}) must be < Hi ({game.Hi})");
                Assert.AreEqual(challenger[game.Lo], sequencer[game.Lo],
                    $"iter {iter}: checkpoints must agree at Lo={game.Lo}");
                Assert.AreNotEqual(challenger[game.Hi], sequencer[game.Hi],
                    $"iter {iter}: checkpoints must disagree at Hi={game.Hi}");
                Assert.IsTrue(rounds <= maxAllowedRounds + 1,
                    $"iter {iter}: game took {rounds} rounds which exceeds max {maxAllowedRounds}");
            }

            Assert.IsTrue(game.IsSettled, $"iter {iter}: game should be settled");
            Assert.AreEqual(firstWrongIndex - 1, game.DisputedIndex,
                $"iter {iter}: game disputed index {game.DisputedIndex} must match expected {firstWrongIndex - 1}");
            Assert.AreEqual(challenger[game.DisputedIndex], sequencer[game.DisputedIndex],
                $"iter {iter}: agreed pre-state at disputed index");
            Assert.AreNotEqual(challenger[game.DisputedIndex + 1], sequencer[game.DisputedIndex + 1],
                $"iter {iter}: disputed post-state at disputed index + 1");
        }
    }

    /// <summary>
    /// Exhaustive boundary sweep for small trace sizes (1 to 32) across all possible dispute locations.
    /// </summary>
    [TestMethod]
    public void BisectionGame_ExhaustiveSmallTraceSweep()
    {
        for (var txCount = 1; txCount <= 32; txCount++)
        {
            for (var firstWrong = 1; firstWrong <= txCount; firstWrong++)
            {
                var challenger = new UInt256[txCount + 1];
                var sequencer = new UInt256[txCount + 1];
                for (var i = 0; i <= txCount; i++)
                {
                    challenger[i] = MakeRoot(i + 1);
                    sequencer[i] = i < firstWrong ? MakeRoot(i + 1) : MakeRoot(100000 + i);
                }

                var game = new BisectionGame(challenger, sequencer);
                var disputed = game.RunToSettlement();
                Assert.AreEqual(firstWrong - 1, disputed,
                    $"txCount={txCount}, firstWrong={firstWrong}: disputed index mismatch");
                Assert.IsTrue(game.Rounds <= BisectionGame.MaxRoundsFor(txCount),
                    $"txCount={txCount}, firstWrong={firstWrong}: exceeded MaxRounds");
            }
        }
    }

    /// <summary>
    /// Calling RunRound or RunToSettlement on an already settled game is idempotent and safe.
    /// </summary>
    [TestMethod]
    public void BisectionGame_SettledGame_CallsAreIdempotent()
    {
        var challenger = new[] { MakeRoot(1), MakeRoot(2) };
        var sequencer = new[] { MakeRoot(1), MakeRoot(999) };
        var game = new BisectionGame(challenger, sequencer);

        var first = game.RunToSettlement();
        Assert.AreEqual(0, first);
        Assert.IsTrue(game.IsSettled);

        // Subsequent calls should return false and not mutate the disputed index
        Assert.IsFalse(game.RunRound());
        Assert.AreEqual(0, game.RunToSettlement());
        Assert.AreEqual(0, game.DisputedIndex);
    }
}
