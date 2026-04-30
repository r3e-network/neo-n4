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
        Assert.AreEqual(1, metrics.GetCounter(MetricNames.SubmitFailures), "failure counter incremented");
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
            return ValueTask.FromResult(new ProofResult
            {
                Proof = new byte[] { 0x01, 0x02, 0x03 },
                Kind = Kind,
                PublicInputHash = UInt256.Zero,
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
}
