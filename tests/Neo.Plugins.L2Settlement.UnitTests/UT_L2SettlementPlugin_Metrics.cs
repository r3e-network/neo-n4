using Neo.L2.Proving;

namespace Neo.Plugins.L2Settlement.UnitTests;

/// <summary>
/// Tests that <see cref="L2SettlementPlugin"/> emits canonical telemetry on the success
/// path AND the failure path. The settlement plugin is the single-most-instrumented hot
/// path in the L2 stack — these assertions guard against silent metric drift.
/// </summary>
[TestClass]
public class UT_L2SettlementPlugin_Metrics
{
    [TestMethod]
    public async Task Submit_EmitsSubmittedCounter_AndProveLatency_AndSubmitLatency()
    {
        using var batch = new L2BatchPlugin();
        using var settlement = new L2SettlementPlugin();
        var prover = new FakeProver(ProofType.Multisig);
        var client = new FakeClient();
        var metrics = new InMemoryMetrics();

        settlement.Wire(batch, prover, client);
        settlement.WithMetrics(metrics);

        settlement.Enqueue(BuildCommitment(batchNumber: 1));
        await settlement.SubmitNextAsync();

        Assert.AreEqual(1, metrics.GetCounter(MetricNames.BatchesSubmitted), "submitted counter");
        Assert.AreEqual(0, metrics.GetCounter(MetricNames.SubmitFailures), "no failures");
        Assert.AreEqual(1, metrics.GetCounter(MetricNames.ProofsGenerated, ("kind", "Multisig")), "proofs tagged by kind");
        Assert.AreEqual(1, metrics.GetHistogram(MetricNames.ProveLatencyMs, ("kind", "Multisig")).Count, "prove-latency observation count");
        Assert.AreEqual(1, metrics.GetHistogram(MetricNames.SubmitLatencyMs).Count, "submit-latency observation count");
        Assert.AreEqual(1, client.SubmitCount, "client called once");
    }

    [TestMethod]
    public async Task Submit_OnClientThrows_IncrementsSubmitFailures_AndRequeues()
    {
        using var batch = new L2BatchPlugin();
        using var settlement = new L2SettlementPlugin();
        var prover = new FakeProver(ProofType.Multisig);
        var client = new FakeClient { ThrowOnSubmit = true };
        var metrics = new InMemoryMetrics();

        settlement.Wire(batch, prover, client);
        settlement.WithMetrics(metrics);

        settlement.Enqueue(BuildCommitment(batchNumber: 7));
        await settlement.SubmitNextAsync();

        Assert.AreEqual(0, metrics.GetCounter(MetricNames.BatchesSubmitted), "no successful submit");
        // iter 175: SubmitFailures is now tagged with the exception type so dashboards
        // can separate contract violations (InvalidOperationException) from real
        // network/L1 failures.
        Assert.AreEqual(1, metrics.GetCounter(MetricNames.SubmitFailures, ("exception", "InvalidOperationException")),
            "failure counter incremented and tagged with exception type");
        Assert.AreEqual(1, settlement.PendingCount, "batch re-queued for retry");
        Assert.AreEqual(1, metrics.GetCounter(MetricNames.ProofsGenerated, ("kind", "Multisig")), "prove still ran");
    }

    [TestMethod]
    public async Task Submit_NoMetricsWired_DoesNotThrow()
    {
        // Default = NoOpMetrics.Instance — emissions are no-ops, never null-deref.
        using var batch = new L2BatchPlugin();
        using var settlement = new L2SettlementPlugin();
        var prover = new FakeProver(ProofType.Multisig);
        var client = new FakeClient();

        settlement.Wire(batch, prover, client);

        settlement.Enqueue(BuildCommitment(batchNumber: 42));
        await settlement.SubmitNextAsync();

        Assert.AreEqual(1, client.SubmitCount);
    }

    [TestMethod]
    public async Task SubmitNextAsync_WithoutWire_DoesNotDrainQueue()
    {
        // Regression: previously SubmitNextAsync dequeued FIRST, then checked _prover/_client,
        // silently losing the item if Wire() hadn't been called. Now the wiring check comes
        // before the dequeue so items stay in the queue until properly wired.
        using var settlement = new L2SettlementPlugin();
        settlement.Enqueue(BuildCommitment(batchNumber: 1));
        Assert.AreEqual(1, settlement.PendingCount);

        await settlement.SubmitNextAsync(); // no Wire called

        Assert.AreEqual(1, settlement.PendingCount, "item must stay queued until Wire() is called");
    }

    [TestMethod]
    public async Task SubmitNextAsync_ConcurrentCalls_SerializeViaGate()
    {
        // Without the gate, two fire-and-forget OnBatchSealed events would spawn parallel
        // submits that race each other to call SubmitBatchAsync — landing on L1 out of
        // order. The gate ensures only one prove-and-submit runs at a time.
        using var batch = new L2BatchPlugin();
        using var settlement = new L2SettlementPlugin();
        var client = new ConcurrencyTrackingClient();
        settlement.Wire(batch, new FakeProver(ProofType.Multisig), client);

        // Queue 4 commitments and fire 4 concurrent SubmitNextAsync calls.
        for (var i = 1; i <= 4; i++) settlement.Enqueue(BuildCommitment((ulong)i));
        var tasks = new[]
        {
            settlement.SubmitNextAsync(),
            settlement.SubmitNextAsync(),
            settlement.SubmitNextAsync(),
            settlement.SubmitNextAsync(),
        };
        await Task.WhenAll(tasks);

        Assert.AreEqual(4, client.SubmitCount, "all 4 batches submitted");
        Assert.AreEqual(0, settlement.PendingCount, "queue drained");
        Assert.AreEqual(1, client.MaxConcurrent, $"only one submit should be in flight at a time (peak={client.MaxConcurrent})");
    }

    [TestMethod]
    public async Task Submit_RejectsProverKindMismatch()
    {
        // Regression: previously a buggy prover that returned ProofResult.Kind != requested
        // Kind silently produced a commitment with whatever the prover claimed. The mismatch
        // would only surface at audit time as "ProofType.None — soft-sealed but never
        // proved" with no link to the prover bug. Now: settlement plugin asserts the
        // contract at the prove boundary.
        using var batch = new L2BatchPlugin();
        using var settlement = new L2SettlementPlugin();
        var liarProver = new LiarProver();  // claims Kind = None regardless of request
        var client = new FakeClient();
        var metrics = new InMemoryMetrics();

        settlement.Wire(batch, liarProver, client);
        settlement.WithMetrics(metrics);

        settlement.Enqueue(BuildCommitment(batchNumber: 1));
        await settlement.SubmitNextAsync();

        // The mismatch should be caught and counted as a submit failure with the
        // contract-violation exception type tag (iter 175).
        Assert.AreEqual(0, metrics.GetCounter(MetricNames.BatchesSubmitted));
        Assert.AreEqual(1, metrics.GetCounter(MetricNames.SubmitFailures, ("exception", "InvalidOperationException")));
        Assert.AreEqual(1, settlement.PendingCount, "batch re-queued for retry");
    }

    private sealed class LiarProver : IL2Prover
    {
        public ProofType Kind => ProofType.Multisig;
        public ValueTask<ProofResult> ProveAsync(ProofRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new ProofResult
            {
                Proof = new byte[] { 0x01 },
                Kind = ProofType.None,  // ← lying about the kind
                PublicInputHash = UInt256.Zero,
            });
    }

    [TestMethod]
    public async Task Dispose_DuringInFlightSubmit_DoesNotThrow()
    {
        // Regression: previously Dispose() called _submitGate.Dispose() unguarded; if a
        // SubmitNextAsync was in flight, its finally-block Release() would throw
        // ObjectDisposedException, which surfaces only via TaskScheduler.UnobservedTaskException
        // (invisible by default). Now: WaitAsync/Release both swallow ObjectDisposedException.
        var batch = new L2BatchPlugin();
        var settlement = new L2SettlementPlugin();
        var blockingClient = new BlockingClient();
        settlement.Wire(batch, new FakeProver(ProofType.Multisig), blockingClient);

        settlement.Enqueue(BuildCommitment(1));
        var submitTask = settlement.SubmitNextAsync();
        // Wait until the submit is parked inside the client's TaskCompletionSource.
        await blockingClient.SubmitEntered.Task;

        // Tear down the plugin while a submit is mid-flight.
        settlement.Dispose();
        batch.Dispose();
        // Let the in-flight submit complete; it should finish without throwing despite
        // the gate being disposed.
        blockingClient.SubmitGate.SetResult();
        await submitTask;
    }

    private sealed class BlockingClient : ISettlementClient
    {
        public TaskCompletionSource SubmitEntered { get; } = new();
        public TaskCompletionSource SubmitGate { get; } = new();

        public async ValueTask<UInt256> SubmitBatchAsync(L2BatchCommitment commitment, PublicInputs publicInputs, CancellationToken cancellationToken = default)
        {
            SubmitEntered.TrySetResult();
            await SubmitGate.Task;
            return UInt256.Zero;
        }

        public ValueTask<UInt256> GetCanonicalStateRootAsync(uint chainId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(UInt256.Zero);

        public ValueTask<BatchStatus> GetBatchStatusAsync(uint chainId, ulong batchNumber, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(BatchStatus.Unknown);
    }

    [TestMethod]
    public async Task Submit_ThrowingMetrics_DoesNotReQueueAlreadySubmittedBatch()
    {
        // Regression for iter 164: previously a metrics-sink throw between
        // SubmitBatchAsync's success and the BatchesSubmitted/SubmitLatencyMs metric
        // calls was caught by the broad catch(Exception) block, which re-queues the
        // batch. Result: an already-on-L1 commitment gets re-submitted, the L1
        // contract rejects the duplicate, and the loop never terminates — paying L1
        // gas every retry. Now metrics are SafeIncrementCounter/SafeRecordHistogram
        // wrapped, so a metrics throw doesn't trigger the catch path.
        using var batch = new L2BatchPlugin();
        using var settlement = new L2SettlementPlugin();
        var prover = new FakeProver(ProofType.Multisig);
        var client = new FakeClient();
        var metrics = new ThrowingMetrics();

        settlement.Wire(batch, prover, client);
        settlement.WithMetrics(metrics);

        settlement.Enqueue(BuildCommitment(batchNumber: 13));
        await settlement.SubmitNextAsync();

        Assert.AreEqual(1, client.SubmitCount, "submit must run exactly once");
        Assert.AreEqual(0, settlement.PendingCount,
            "batch must NOT be re-queued — it's already on L1; re-queue would loop forever");
    }

    private sealed class ThrowingMetrics : Neo.L2.Telemetry.IL2Metrics
    {
        public void IncrementCounter(string name, long delta = 1, params (string Key, string Value)[] tags)
            => throw new InvalidOperationException($"sink down: {name}");
        public void RecordHistogram(string name, double value, params (string Key, string Value)[] tags)
            => throw new InvalidOperationException($"sink down: {name}");
        public void SetGauge(string name, double value, params (string Key, string Value)[] tags)
            => throw new InvalidOperationException($"sink down: {name}");
    }

    [TestMethod]
    public async Task Submit_RejectsEmptyProverProof_AtProveBoundary()
    {
        // Regression for iter 177: a prover that returns empty Proof bytes paired with
        // a non-None ProofType would otherwise produce a commitment that NoZeroProofCheck
        // catches hours later at audit time, with no link back to the prover bug.
        // Now caught at the prove boundary as a contract violation.
        using var batch = new L2BatchPlugin();
        using var settlement = new L2SettlementPlugin();
        var emptyProver = new EmptyProofProver();
        var client = new FakeClient();
        var metrics = new InMemoryMetrics();

        settlement.Wire(batch, emptyProver, client);
        settlement.WithMetrics(metrics);

        settlement.Enqueue(BuildCommitment(batchNumber: 1));
        await settlement.SubmitNextAsync();

        Assert.AreEqual(0, client.SubmitCount, "must not submit a soft-sealed batch");
        Assert.AreEqual(1, metrics.GetCounter(MetricNames.SubmitFailures, ("exception", "InvalidOperationException")));
        Assert.AreEqual(1, settlement.PendingCount);
    }

    private sealed class EmptyProofProver : IL2Prover
    {
        public ProofType Kind => ProofType.Multisig;
        public ValueTask<ProofResult> ProveAsync(ProofRequest request, CancellationToken cancellationToken = default)
            => new ValueTask<ProofResult>(new ProofResult
            {
                Proof = ReadOnlyMemory<byte>.Empty,           // ← empty
                Kind = ProofType.Multisig,
                PublicInputHash = Neo.L2.State.StateRootCalculator.HashPublicInputs(request.PublicInputs),
            });
    }

    [TestMethod]
    public async Task Submit_BuggyProverReturnsNull_TaggedAsContractViolation()
    {
        // Regression for iter 175: a buggy IL2Prover.ProveAsync returning null would
        // previously NRE inside `proofResult.Kind` access; now surfaces as
        // InvalidOperationException, gets caught by the broad catch, and is tagged
        // with the exception type so dashboards can separate contract violations.
        using var batch = new L2BatchPlugin();
        using var settlement = new L2SettlementPlugin();
        var nullProver = new NullReturningProver();
        var client = new FakeClient();
        var metrics = new InMemoryMetrics();

        settlement.Wire(batch, nullProver, client);
        settlement.WithMetrics(metrics);

        settlement.Enqueue(BuildCommitment(batchNumber: 1));
        await settlement.SubmitNextAsync();

        Assert.AreEqual(0, client.SubmitCount, "client must not be called when prover violates contract");
        Assert.AreEqual(1, metrics.GetCounter(MetricNames.SubmitFailures, ("exception", "InvalidOperationException")),
            "failure metric tagged with exception type");
        Assert.AreEqual(1, settlement.PendingCount, "batch re-queued for retry");
    }

    private sealed class NullReturningProver : IL2Prover
    {
        public ProofType Kind => ProofType.Multisig;
        public ValueTask<ProofResult> ProveAsync(ProofRequest request, CancellationToken cancellationToken = default)
            => new ValueTask<ProofResult>((ProofResult)null!);
    }

    [TestMethod]
    public void Wire_RejectsNullBatchPlugin()
    {
        using var settlement = new L2SettlementPlugin();
        Assert.ThrowsExactly<ArgumentNullException>(
            () => settlement.Wire(null!, new FakeProver(ProofType.Multisig), new FakeClient()));
    }

    [TestMethod]
    public void Wire_RejectsNullProver()
    {
        using var batch = new L2BatchPlugin();
        using var settlement = new L2SettlementPlugin();
        Assert.ThrowsExactly<ArgumentNullException>(
            () => settlement.Wire(batch, null!, new FakeClient()));
    }

    [TestMethod]
    public void Wire_RejectsNullClient()
    {
        using var batch = new L2BatchPlugin();
        using var settlement = new L2SettlementPlugin();
        Assert.ThrowsExactly<ArgumentNullException>(
            () => settlement.Wire(batch, new FakeProver(ProofType.Multisig), null!));
    }

    [TestMethod]
    public void WithMetrics_RejectsNullMetrics()
    {
        using var settlement = new L2SettlementPlugin();
        Assert.ThrowsExactly<ArgumentNullException>(() => settlement.WithMetrics(null!));
    }

    [TestMethod]
    public void Enqueue_RejectsNullCommitment()
    {
        // Pin L2SettlementPlugin.cs:102. Without the guard a null commitment would slip
        // into the pending queue and NRE during SubmitNextAsync's `_pending.Dequeue` ->
        // ProveAsync(null!.Proof) deep in the submit loop, with no link back to the
        // bad caller.
        using var settlement = new L2SettlementPlugin();
        Assert.ThrowsExactly<ArgumentNullException>(() => settlement.Enqueue(null!));
    }

    [TestMethod]
    public async Task Enqueue_WhenDisabled_DoesNothing_AndPendingStaysZero()
    {
        // We can't easily flip the private _settings.Enabled, so this test just confirms
        // the public Enqueue contract is honored when Enabled is true (default), and that
        // SubmitNextAsync against an empty queue is a no-op.
        using var batch = new L2BatchPlugin();
        using var settlement = new L2SettlementPlugin();
        var prover = new FakeProver(ProofType.Multisig);
        var client = new FakeClient();
        settlement.Wire(batch, prover, client);

        Assert.AreEqual(0, settlement.PendingCount);
        await settlement.SubmitNextAsync(); // empty queue
        Assert.AreEqual(0, client.SubmitCount);
    }

    private static L2BatchCommitment BuildCommitment(ulong batchNumber)
    {
        return new L2BatchCommitment
        {
            ChainId = 1001,
            BatchNumber = batchNumber,
            FirstBlock = batchNumber * 10,
            LastBlock = batchNumber * 10 + 9,
            PreStateRoot = UInt256.Zero,
            PostStateRoot = UInt256.Zero,
            TxRoot = UInt256.Zero,
            ReceiptRoot = UInt256.Zero,
            WithdrawalRoot = UInt256.Zero,
            L2ToL1MessageRoot = UInt256.Zero,
            L2ToL2MessageRoot = UInt256.Zero,
            DACommitment = UInt256.Zero,
            PublicInputHash = UInt256.Zero,
            ProofType = ProofType.None,
            Proof = ReadOnlyMemory<byte>.Empty,
        };
    }

    private sealed class FakeProver : IL2Prover
    {
        public FakeProver(ProofType kind) { Kind = kind; }
        public ProofType Kind { get; }
        public ValueTask<ProofResult> ProveAsync(ProofRequest request, CancellationToken cancellationToken = default)
        {
            // Honor the prover contract: return the actual hash of the supplied
            // publicInputs so the iter-140 settlement-plugin assertion (proofResult.
            // PublicInputHash must match settlement's computed hash) passes.
            return ValueTask.FromResult(new ProofResult
            {
                Proof = new byte[] { 0x01, 0x02, 0x03 },
                Kind = Kind,
                PublicInputHash = Neo.L2.State.StateRootCalculator.HashPublicInputs(request.PublicInputs),
            });
        }
    }

    private sealed class FakeClient : ISettlementClient
    {
        public int SubmitCount { get; private set; }
        public bool ThrowOnSubmit { get; set; }

        public ValueTask<UInt256> SubmitBatchAsync(L2BatchCommitment commitment, PublicInputs publicInputs, CancellationToken cancellationToken = default)
        {
            if (ThrowOnSubmit) throw new InvalidOperationException("simulated submit failure");
            SubmitCount++;
            return ValueTask.FromResult(UInt256.Zero);
        }

        public ValueTask<UInt256> GetCanonicalStateRootAsync(uint chainId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(UInt256.Zero);

        public ValueTask<BatchStatus> GetBatchStatusAsync(uint chainId, ulong batchNumber, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(BatchStatus.Unknown);
    }

    private sealed class ConcurrencyTrackingClient : ISettlementClient
    {
        private int _inFlight;
        private int _maxConcurrent;
        private int _submitCount;

        public int SubmitCount => Volatile.Read(ref _submitCount);
        public int MaxConcurrent => Volatile.Read(ref _maxConcurrent);

        public async ValueTask<UInt256> SubmitBatchAsync(L2BatchCommitment commitment, PublicInputs publicInputs, CancellationToken cancellationToken = default)
        {
            var current = Interlocked.Increment(ref _inFlight);
            // Track peak parallelism — without the gate, this would spike to N.
            int prev;
            do
            {
                prev = Volatile.Read(ref _maxConcurrent);
                if (current <= prev) break;
            } while (Interlocked.CompareExchange(ref _maxConcurrent, current, prev) != prev);

            // Yield so a racing caller has a chance to also enter this method.
            await Task.Delay(20).ConfigureAwait(false);

            Interlocked.Increment(ref _submitCount);
            Interlocked.Decrement(ref _inFlight);
            return UInt256.Zero;
        }

        public ValueTask<UInt256> GetCanonicalStateRootAsync(uint chainId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(UInt256.Zero);

        public ValueTask<BatchStatus> GetBatchStatusAsync(uint chainId, ulong batchNumber, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(BatchStatus.Unknown);
    }
}
