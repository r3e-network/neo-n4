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
}
