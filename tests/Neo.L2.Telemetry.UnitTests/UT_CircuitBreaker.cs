namespace Neo.L2.Telemetry.Tests;

[TestClass]
public class UT_CircuitBreaker
{
    [TestMethod]
    public void StartsClosed()
    {
        var cb = new CircuitBreaker("test", failureThreshold: 3);
        Assert.AreEqual(CircuitState.Closed, cb.State);
        Assert.AreEqual(0, cb.FailureCount);
    }

    [TestMethod]
    public void TryEnter_AllowsWhenClosed()
    {
        var cb = new CircuitBreaker("test", failureThreshold: 3);
        Assert.IsTrue(cb.TryEnter());
    }

    [TestMethod]
    public void RecordFailure_IncrementsCount()
    {
        var cb = new CircuitBreaker("test", failureThreshold: 3);
        cb.RecordFailure();
        Assert.AreEqual(1, cb.FailureCount);
        Assert.AreEqual(CircuitState.Closed, cb.State);
    }

    [TestMethod]
    public void OpensAfterThresholdFailures()
    {
        var cb = new CircuitBreaker("test", failureThreshold: 2);
        cb.RecordFailure();
        cb.RecordFailure();
        Assert.AreEqual(CircuitState.Open, cb.State);
        Assert.IsFalse(cb.TryEnter());
    }

    [TestMethod]
    public void RecordSuccess_ResetsCircuit()
    {
        var cb = new CircuitBreaker("test", failureThreshold: 2);
        cb.RecordFailure();
        cb.RecordSuccess();
        Assert.AreEqual(0, cb.FailureCount);
        Assert.AreEqual(CircuitState.Closed, cb.State);
    }

    [TestMethod]
    public void TryEnter_FailsFastWhenOpen()
    {
        var cb = new CircuitBreaker("test", failureThreshold: 1, openTimeout: TimeSpan.FromHours(1));
        cb.RecordFailure();
        Assert.IsFalse(cb.TryEnter());
        Assert.IsFalse(cb.TryEnter()); // still open
    }

    [TestMethod]
    public void Construct_RejectsZeroThreshold()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new CircuitBreaker("test", failureThreshold: 0));
    }

    [TestMethod]
    public void Construct_RejectsNullName()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new CircuitBreaker(null!));
    }

    [TestMethod]
    public void HalfOpen_AllowsExactlyOneProbeUntilOutcome()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var cb = new CircuitBreaker(
            "test",
            failureThreshold: 1,
            openTimeout: TimeSpan.FromSeconds(10),
            timeProvider: clock);

        cb.RecordFailure();
        Assert.AreEqual(CircuitState.Open, cb.State);
        Assert.IsFalse(cb.TryEnter());

        clock.Advance(TimeSpan.FromSeconds(11));
        Assert.AreEqual(CircuitState.HalfOpen, cb.State);
        Assert.IsTrue(cb.TryEnter(), "first half-open probe must be allowed");
        Assert.IsFalse(cb.TryEnter(), "second concurrent probe must be rejected");

        cb.RecordFailure();
        Assert.AreEqual(CircuitState.Open, cb.State);
        Assert.IsFalse(cb.TryEnter());

        clock.Advance(TimeSpan.FromSeconds(11));
        Assert.IsTrue(cb.TryEnter());
        cb.RecordSuccess();
        Assert.AreEqual(CircuitState.Closed, cb.State);
        Assert.IsTrue(cb.TryEnter());
        Assert.IsTrue(cb.TryEnter());
    }

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _utcNow = start;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow += delta;
    }
}
