namespace Neo.L2.Telemetry.Tests;

[TestClass]
public class UT_TokenBucketRateLimiter
{
    [TestMethod]
    public void StartsFull()
    {
        var rl = new TokenBucketRateLimiter(refillRatePerSecond: 10, burstCapacity: 20);
        Assert.IsTrue(rl.TryConsume(20)); // can consume full burst
        Assert.IsFalse(rl.TryConsume(1)); // exhausted
    }

    [TestMethod]
    public void SingleTokenConsumption()
    {
        var rl = new TokenBucketRateLimiter(refillRatePerSecond: 100, burstCapacity: 200);
        Assert.IsTrue(rl.TryConsume());
        Assert.IsTrue(rl.TryConsume());
    }

    [TestMethod]
    public void BurstCapacityEnforced()
    {
        var rl = new TokenBucketRateLimiter(refillRatePerSecond: 100, burstCapacity: 5);
        // can consume up to burst capacity
        Assert.IsTrue(rl.TryConsume(5));
        // 6th token not available
        Assert.IsFalse(rl.TryConsume(1));
    }

    [TestMethod]
    public void Construct_RejectsNegativeRate()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new TokenBucketRateLimiter(refillRatePerSecond: 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new TokenBucketRateLimiter(refillRatePerSecond: -1));
    }

    [TestMethod]
    public void Construct_RejectsZeroBurst()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new TokenBucketRateLimiter(refillRatePerSecond: 10, burstCapacity: 0));
    }

    [TestMethod]
    public void AvailableTokens_IsDiagnostic()
    {
        var rl = new TokenBucketRateLimiter(refillRatePerSecond: 100, burstCapacity: 200);
        var before = rl.AvailableTokens;
        Assert.IsTrue(before > 0);
        rl.TryConsume(1);
        Assert.IsTrue(rl.AvailableTokens < before);
    }

    [TestMethod]
    public void TryConsume_RejectsNegativeCount()
    {
        var rl = new TokenBucketRateLimiter();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => rl.TryConsume(0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => rl.TryConsume(-1));
    }
}
