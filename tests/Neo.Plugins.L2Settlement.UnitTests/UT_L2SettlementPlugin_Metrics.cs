using System.Collections.Concurrent;
using Neo.Cryptography;
using Neo.L2.Batch;
using Neo.L2.Executor.ProofWitness;
using Neo.L2.ForcedInclusion;
using Neo.L2.Persistence;
using Neo.L2.State;

namespace Neo.Plugins.L2Settlement.UnitTests;

[TestClass]
public class UT_L2SettlementPlugin_Metrics
{
    private const uint ChainId = 1001;

    [TestMethod]
    public async Task DurableSubmit_Success_EmitsProofSubmitAndLatencyMetrics()
    {
        using var backend = new InMemoryKeyValueStore();
        using var store = new KeyValueProofWitnessStore(backend);
        using var batch = new L2BatchPlugin();
        using var settlement = new L2SettlementPlugin();
        var metrics = new InMemoryMetrics();
        var client = new MetricsSettlementClient();
        settlement.WithMetrics(metrics);
        Wire(settlement, batch, store, client);

        await settlement.EnqueueAsync(BuildBatch(1));
        await WaitUntilAsync(() => client.SubmitCount == 1);
        await settlement.SubmitNextAsync();

        Assert.AreEqual(1, metrics.GetCounter(
            MetricNames.ProofsGenerated, ("kind", "Multisig")));
        Assert.AreEqual(1, metrics.GetCounter(MetricNames.BatchesSubmitted));
        Assert.AreEqual(1, metrics.GetHistogram(
            MetricNames.ProveLatencyMs, ("kind", "Multisig")).Count);
        Assert.AreEqual(1, metrics.GetHistogram(MetricNames.SubmitLatencyMs).Count);
        Assert.AreEqual(0, await settlement.GetPendingCountAsync());
    }

    [TestMethod]
    public async Task DurableSubmit_Failure_EmitsFailureAndRetainsArtifact()
    {
        using var backend = new InMemoryKeyValueStore();
        using var store = new KeyValueProofWitnessStore(backend);
        using var batch = new L2BatchPlugin();
        using var settlement = new L2SettlementPlugin();
        var metrics = new InMemoryMetrics();
        var client = new MetricsSettlementClient { ThrowOnSubmit = true };
        settlement.WithMetrics(metrics);
        Wire(settlement, batch, store, client);

        await settlement.EnqueueAsync(BuildBatch(2));
        await WaitUntilAsync(() => metrics.GetCounter(
            MetricNames.SubmitFailures,
            ("exception", "InvalidOperationException")) == 1);

        Assert.AreEqual(0, metrics.GetCounter(MetricNames.BatchesSubmitted));
        Assert.AreEqual(1, await settlement.GetPendingCountAsync());
        var artifacts = new List<ProofWitnessArtifactV1>();
        await foreach (var artifact in store.EnumerateCommittedAsync(ChainId))
            artifacts.Add(artifact);
        Assert.AreEqual(1, artifacts.Count);
        Assert.IsNotNull(await store.GetProofAsync(artifacts[0].ContentHash));
    }

    [TestMethod]
    public async Task DurableSubmit_ConcurrentReconcile_SerializesSubmission()
    {
        using var backend = new InMemoryKeyValueStore();
        using var store = new KeyValueProofWitnessStore(backend);
        using var batch = new L2BatchPlugin();
        using var settlement = new L2SettlementPlugin();
        var client = new MetricsSettlementClient { SubmitDelay = TimeSpan.FromMilliseconds(20) };
        Wire(settlement, batch, store, client);

        for (ulong batchNumber = 1; batchNumber <= 4; batchNumber++)
            await settlement.EnqueueAsync(BuildBatch(batchNumber));
        await WaitUntilAsync(() => client.SubmitCount == 4);
        await Task.WhenAll(
            settlement.SubmitNextAsync(),
            settlement.SubmitNextAsync(),
            settlement.SubmitNextAsync(),
            settlement.SubmitNextAsync());

        Assert.AreEqual(4, client.SubmitCount);
        Assert.AreEqual(1, client.MaxConcurrent);
        Assert.AreEqual(0, await settlement.GetPendingCountAsync());
    }

    [TestMethod]
    public async Task DurableSubmit_ThrowingMetrics_DoesNotDuplicateOrLoseState()
    {
        using var backend = new InMemoryKeyValueStore();
        using var store = new KeyValueProofWitnessStore(backend);
        using var batch = new L2BatchPlugin();
        using var settlement = new L2SettlementPlugin();
        var client = new MetricsSettlementClient();
        settlement.WithMetrics(new ThrowingMetrics());
        Wire(settlement, batch, store, client);

        await settlement.EnqueueAsync(BuildBatch(9));
        await WaitUntilAsync(() => client.SubmitCount == 1);
        await settlement.SubmitNextAsync();
        await settlement.SubmitNextAsync();

        Assert.AreEqual(1, client.SubmitCount);
        Assert.AreEqual(0, await settlement.GetPendingCountAsync());
    }

    [TestMethod]
    public void Wire_ForcedSourceWithoutFinalizer_FailsClosed()
    {
        using var backend = new InMemoryKeyValueStore();
        using var store = new KeyValueProofWitnessStore(backend);
        using var batch = new L2BatchPlugin();
        using var settlement = new L2SettlementPlugin();
        using var forcedSource = new InMemoryForcedInclusionSource(ChainId);

        Assert.ThrowsExactly<InvalidOperationException>(() => settlement.Wire(
            batch,
            new MetricsExecutor(),
            new MetricsDaWriter(),
            store,
            new MetricsProver(),
            new MetricsSettlementClient(),
            ProofWitnessPipelineProfile.Legacy(ChainId, ProofType.Multisig),
            forcedInclusionSource: forcedSource));
    }

    private static void Wire(
        L2SettlementPlugin settlement,
        L2BatchPlugin batch,
        IProofWitnessStore store,
        ISettlementClient client)
        => settlement.Wire(
            batch,
            new MetricsExecutor(),
            new MetricsDaWriter(),
            store,
            new MetricsProver(),
            client,
            ProofWitnessPipelineProfile.Legacy(ChainId, ProofType.Multisig));

    private static SealedBatch BuildBatch(ulong batchNumber)
        => new(
            ChainId,
            batchNumber,
            firstBlock: batchNumber * 10,
            lastBlock: batchNumber * 10,
            preStateRoot: H(0x11),
            transactions: new ReadOnlyMemory<byte>[]
            {
                new byte[] { 0x01, checked((byte)batchNumber) },
            },
            l1Messages: Array.Empty<CrossChainMessage>(),
            blockContext: new BatchBlockContext
            {
                L1FinalizedHeight = 1,
                FirstBlockTimestamp = 1,
                LastBlockTimestamp = 1,
                SequencerCommitteeHash = H(0x12),
                Network = 860833102,
            });

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!predicate())
        {
            if (DateTime.UtcNow >= deadline)
                Assert.Fail("timed out waiting for background reconciliation");
            await Task.Delay(10);
        }
    }

    private static UInt256 H(byte value)
    {
        var bytes = new byte[UInt256.Length];
        bytes[0] = value;
        return new UInt256(bytes);
    }

    private sealed class MetricsExecutor : IProofWitnessBatchExecutor
    {
        public ValueTask<BatchExecutionResult> ApplyBatchAsync(
            BatchExecutionRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<ProofWitnessExecutionResult> ApplyBatchWithWitnessAsync(
            SealedBatch batch,
            CancellationToken cancellationToken = default)
        {
            var txHashes = batch.Transactions
                .Select(transaction => new UInt256(Crypto.Hash256(transaction.Span)))
                .ToArray();
            return ValueTask.FromResult(new ProofWitnessExecutionResult
            {
                ExecutionResult = new BatchExecutionResult
                {
                    PostStateRoot = H(0x21),
                    TxRoot = StateRootCalculator.ComputeTxRoot(txHashes),
                    ReceiptRoot = H(0x22),
                    WithdrawalRoot = UInt256.Zero,
                    L2ToL1MessageRoot = UInt256.Zero,
                    L2ToL2MessageRoot = UInt256.Zero,
                    GasConsumed = 0,
                },
                ExecutionSemanticId = ExecutionSemanticIds.ReferenceNoOpV1,
                WitnessAuthenticated = false,
                StateWitness = ReadOnlyMemory<byte>.Empty,
                Effects = new byte[] { 0x4e, 0x45, 0x4f, 0x34, 0x45, 0x46, 0x58, 0x31 },
            });
        }
    }

    private sealed class MetricsDaWriter : IDAWriter
    {
        public DAMode Mode => DAMode.External;
        public DAReceiptKind ReceiptKind => DAReceiptKind.ExternalPublication;

        public ValueTask<DAReceipt> PublishAsync(
            DAPublishRequest request,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new DAReceipt
            {
                Layer = Mode,
                Kind = ReceiptKind,
                Commitment = ExecutionPayloadSerializer.ComputeCommitment(request.Payload.Span),
                Pointer = new byte[] { 0x01 },
                Evidence = new byte[] { 0x02 },
            });

        public ValueTask<bool> IsAvailableAsync(
            DAReceipt receipt,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(true);
    }

    private sealed class MetricsProver : IL2Prover
    {
        public ProofType Kind => ProofType.Multisig;

        public ValueTask<ProofResult> ProveAsync(
            ProofRequest request,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new ProofResult
            {
                Kind = Kind,
                Proof = new byte[] { 0x31 },
                PublicInputHash = StateRootCalculator.HashPublicInputs(request.PublicInputs),
            });
    }

    private sealed class MetricsSettlementClient : ISettlementClient
    {
        private readonly ConcurrentDictionary<(uint ChainId, ulong BatchNumber), BatchStatus>
            _statuses = new();
        private int _inFlight;
        private int _maxConcurrent;
        private int _submitCount;

        public bool ThrowOnSubmit { get; init; }
        public TimeSpan SubmitDelay { get; init; }
        public int SubmitCount => Volatile.Read(ref _submitCount);
        public int MaxConcurrent => Volatile.Read(ref _maxConcurrent);

        public async ValueTask<UInt256> SubmitBatchAsync(
            L2BatchCommitment commitment,
            PublicInputs publicInputs,
            CancellationToken cancellationToken = default)
        {
            var inFlight = Interlocked.Increment(ref _inFlight);
            UpdateMaximum(inFlight);
            try
            {
                Interlocked.Increment(ref _submitCount);
                if (SubmitDelay > TimeSpan.Zero)
                    await Task.Delay(SubmitDelay, cancellationToken);
                if (ThrowOnSubmit)
                    throw new InvalidOperationException("simulated settlement failure");
                _statuses[(commitment.ChainId, commitment.BatchNumber)] = BatchStatus.Pending;
                return H(checked((byte)(0x80 + commitment.BatchNumber)));
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }

        public ValueTask<UInt256> GetCanonicalStateRootAsync(
            uint chainId,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(UInt256.Zero);

        public ValueTask<BatchStatus> GetBatchStatusAsync(
            uint chainId,
            ulong batchNumber,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(
                _statuses.GetValueOrDefault((chainId, batchNumber), BatchStatus.Unknown));

        private void UpdateMaximum(int value)
        {
            while (true)
            {
                var current = Volatile.Read(ref _maxConcurrent);
                if (value <= current) return;
                if (Interlocked.CompareExchange(ref _maxConcurrent, value, current) == current)
                    return;
            }
        }
    }

    private sealed class ThrowingMetrics : IL2Metrics
    {
        public void IncrementCounter(
            string name,
            long delta = 1,
            params ReadOnlySpan<(string Key, string Value)> tags)
            => throw new InvalidOperationException("metrics sink unavailable");

        public void RecordHistogram(
            string name,
            double value,
            params ReadOnlySpan<(string Key, string Value)> tags)
            => throw new InvalidOperationException("metrics sink unavailable");

        public void SetGauge(
            string name,
            double value,
            params ReadOnlySpan<(string Key, string Value)> tags)
            => throw new InvalidOperationException("metrics sink unavailable");
    }
}
