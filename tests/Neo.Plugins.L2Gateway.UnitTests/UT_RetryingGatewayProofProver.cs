namespace Neo.Plugins.L2Gateway.UnitTests;

/// <summary>
/// Tests for <see cref="RetryingGatewayProofProver"/> — only <see cref="TimeoutException"/>
/// is retried with backoff; fail-closed errors propagate immediately, and the decorator is a
/// transparent pass-through otherwise.
/// </summary>
[TestClass]
public class UT_RetryingGatewayProofProver
{
    private static readonly TimeSpan FastBackoff = TimeSpan.FromMilliseconds(1);

    [TestMethod]
    public async Task ProveAsync_InnerSucceeds_ReturnsProof_NoRetry()
    {
        var inner = new StubGatewayProofProver();
        var retrying = new RetryingGatewayProofProver(inner, initialBackoff: FastBackoff);

        var proof = await retrying.ProveAsync(null!, null!);

        Assert.AreEqual(1, inner.Calls);
        Assert.AreSequenceEqual(new byte[] { 1 }, proof.ToArray());
    }

    [TestMethod]
    public async Task ProveAsync_TimeoutThenSuccess_RetriesAndReturns()
    {
        var inner = new StubGatewayProofProver { TimeoutBeforeSuccess = 2 };
        var retrying = new RetryingGatewayProofProver(inner, maxAttempts: 5, initialBackoff: FastBackoff);

        var proof = await retrying.ProveAsync(null!, null!);

        Assert.AreEqual(3, inner.Calls, "two timeouts then success -> three calls");
        Assert.AreSequenceEqual(new byte[] { 3 }, proof.ToArray());
    }

    [TestMethod]
    public async Task ProveAsync_RepeatedTimeouts_ExhaustsAttempts_ThenThrows()
    {
        var inner = new StubGatewayProofProver { TimeoutBeforeSuccess = 99 };
        var retrying = new RetryingGatewayProofProver(inner, maxAttempts: 4, initialBackoff: FastBackoff);

        await Assert.ThrowsExactlyAsync<TimeoutException>(
            async () => await retrying.ProveAsync(null!, null!));

        Assert.AreEqual(4, inner.Calls, "all maxAttempts exhausted");
    }

    [TestMethod]
    public async Task ProveAsync_FailClosedError_DoesNotRetry()
    {
        var inner = new StubGatewayProofProver { ThrowOnCall = new InvalidDataException("artifact mismatch") };
        var retrying = new RetryingGatewayProofProver(inner, maxAttempts: 5, initialBackoff: FastBackoff);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            async () => await retrying.ProveAsync(null!, null!));

        Assert.AreEqual(1, inner.Calls, "fail-closed errors must not be retried");
    }

    [TestMethod]
    public async Task ProveAsync_PreCanceledToken_PropagatesWithoutAnyAttempt()
    {
        var inner = new StubGatewayProofProver();
        var retrying = new RetryingGatewayProofProver(inner, initialBackoff: FastBackoff);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            async () => await retrying.ProveAsync(null!, null!, cts.Token));

        Assert.AreEqual(0, inner.Calls, "cancellation must be honored before the first attempt");
    }

    [TestMethod]
    public void ProofSystem_And_BackendId_PassThrough()
    {
        var inner = new StubGatewayProofProver();
        var retrying = new RetryingGatewayProofProver(inner);
        Assert.AreEqual(inner.ProofSystem, retrying.ProofSystem);
        Assert.AreEqual(inner.AggregationBackendId, retrying.AggregationBackendId);
    }

    [TestMethod]
    public void Constructor_ValidatesArguments()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => _ = new RetryingGatewayProofProver(null!));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = new RetryingGatewayProofProver(new StubGatewayProofProver(), maxAttempts: 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = new RetryingGatewayProofProver(new StubGatewayProofProver(), initialBackoff: TimeSpan.Zero));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = new RetryingGatewayProofProver(new StubGatewayProofProver(), backoffMultiplier: 0.9));
    }

    [TestMethod]
    public void Constructor_RejectsNonFiniteMultipliersAndExcessiveDelay()
    {
        foreach (var multiplier in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                _ = new RetryingGatewayProofProver(new StubGatewayProofProver(), backoffMultiplier: multiplier));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = new RetryingGatewayProofProver(new StubGatewayProofProver(), initialBackoff: TimeSpan.MaxValue));
    }

    [TestMethod]
    public async Task ProveAsync_CancellationDuringBackoff_DoesNotRetry()
    {
        using var cts = new CancellationTokenSource();
        var inner = new StubGatewayProofProver { TimeoutBeforeSuccess = 99, BeforeTimeout = cts.Cancel };
        var retrying = new RetryingGatewayProofProver(inner, initialBackoff: FastBackoff);
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(
            async () => await retrying.ProveAsync(null!, null!, cts.Token));
        Assert.AreEqual(1, inner.Calls);
    }

    private sealed class StubGatewayProofProver : IGatewayProofProver
    {
        public byte ProofSystem => GatewaySp1FileQueueProtocol.Sp1ProofSystem;
        public byte AggregationBackendId => GatewaySp1FileQueueProtocol.RecursiveAggregationBackendId;
        public int Calls { get; private set; }
        public int TimeoutBeforeSuccess { get; set; }
        public Exception? ThrowOnCall { get; set; }
        public Action? BeforeTimeout { get; set; }

        public ValueTask<ReadOnlyMemory<byte>> ProveAsync(
            GatewayProofBinding binding,
            AggregatedCommitment commitment,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            if (BeforeTimeout is not null) BeforeTimeout();
            if (ThrowOnCall is not null)
                return ValueTask.FromException<ReadOnlyMemory<byte>>(ThrowOnCall);
            if (Calls <= TimeoutBeforeSuccess)
                return ValueTask.FromException<ReadOnlyMemory<byte>>(new TimeoutException("simulated daemon timeout"));
            return ValueTask.FromResult<ReadOnlyMemory<byte>>(new byte[] { (byte)Calls });
        }
    }
}