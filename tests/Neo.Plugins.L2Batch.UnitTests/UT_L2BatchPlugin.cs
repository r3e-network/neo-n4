using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.Telemetry;
using Neo.Plugins.L2;

namespace Neo.Plugins.L2Batch.UnitTests;

[TestClass]
public class UT_L2BatchPlugin
{
    private static SealedBatch SampleBatch() => new(
        1001,
        1,
        0,
        0,
        UInt256.Zero,
        Array.Empty<ReadOnlyMemory<byte>>(),
        Array.Empty<CrossChainMessage>(),
        new BatchBlockContext
        {
            L1FinalizedHeight = 1,
            FirstBlockTimestamp = 1,
            LastBlockTimestamp = 1,
            SequencerCommitteeHash = UInt256.Zero,
            Network = 1,
        });

    [TestMethod]
    public void DispatchSealed_OneSubscriberThrows_OthersStillFire()
    {
        // Regression for iter 170: a buggy OnBatchSealed subscriber would previously
        // surface its exception to Neo's Blockchain.Committed via standard .NET event
        // semantics (first-throw aborts further dispatch). Now isolated so each
        // subscriber's failure is contained.
        var fired = new bool[3];
        EventHandler<SealedBatch>? handler = null;
        handler += (_, _) => fired[0] = true;
        handler += (_, _) => throw new InvalidOperationException("buggy subscriber");
        handler += (_, _) => fired[2] = true;

        var metrics = new InMemoryMetrics();
        L2BatchPlugin.DispatchSealed(this, handler, SampleBatch(), metrics);

        Assert.IsTrue(fired[0], "subscriber 0 must fire");
        Assert.IsTrue(fired[2], "subscriber 2 must fire even after subscriber 1 threw");
        Assert.AreEqual(1, metrics.GetCounter(MetricNames.BatchSealedSubscriberFailures),
            "subscriber failures must be counted");
    }

    [TestMethod]
    public void DispatchSealed_NoSubscribers_DoesNotThrow()
    {
        var metrics = new InMemoryMetrics();
        L2BatchPlugin.DispatchSealed(this, handler: null, SampleBatch(), metrics);
        Assert.AreEqual(0, metrics.GetCounter(MetricNames.BatchSealedSubscriberFailures));
    }

    [TestMethod]
    public void DispatchSealed_MultipleThrowsAllCounted()
    {
        EventHandler<SealedBatch>? handler = null;
        for (var i = 0; i < 4; i++)
            handler += (_, _) => throw new InvalidOperationException("nope");

        var metrics = new InMemoryMetrics();
        L2BatchPlugin.DispatchSealed(this, handler, SampleBatch(), metrics);
        Assert.AreEqual(4, metrics.GetCounter(MetricNames.BatchSealedSubscriberFailures));
    }

    [TestMethod]
    public void WithMetrics_RejectsNullMetrics()
    {
        // Pin L2BatchPlugin.cs:40. Symmetric to other plugin WithMetrics pins.
        using var plugin = new L2BatchPlugin();
        Assert.ThrowsExactly<ArgumentNullException>(() => plugin.WithMetrics(null!));
    }

    [TestMethod]
    public void Plugin_NameAndDescription_AreNonEmpty()
    {
        // Surfaced in plugin host startup logs; pin so a refactor doesn't accidentally
        // empty either. Same convention as UT_L2BridgePlugin / UT_L2GatewayPlugin /
        // UT_L2ProverPlugin.
        using var plugin = new L2BatchPlugin();
        Assert.IsFalse(string.IsNullOrWhiteSpace(plugin.Name));
        Assert.IsFalse(string.IsNullOrWhiteSpace(plugin.Description));
    }

    [TestMethod]
    public void ProcessCommittedBlock_SinkFailureRetriesPendingBeforeLaterBlock()
    {
        using var plugin = new L2BatchPlugin(OneBlockSettings());
        var sink = new DurableSink { FailBeforePersistOnce = true };
        plugin.WithSealedBatchSink(sink, 1001);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => plugin.ProcessCommittedBlock(1, 1000, 11, NoTxs()));
        CollectionAssert.AreEqual(new ulong[] { 1 }, sink.AttemptedBatchNumbers.ToArray());
        Assert.AreEqual(0, sink.PersistedBatches.Count);

        plugin.ProcessCommittedBlock(2, 1100, 11, NoTxs());

        CollectionAssert.AreEqual(
            new ulong[] { 1, 1, 2 }, sink.AttemptedBatchNumbers.ToArray());
        Assert.AreEqual(2, sink.PersistedBatches.Count);
        Assert.AreEqual(1UL, sink.PersistedBatches[0].LastBlock);
        Assert.AreEqual(2UL, sink.PersistedBatches[1].FirstBlock);
        Assert.AreEqual(
            DurableSink.RootFor(1), sink.PersistedBatches[1].PreStateRoot);
    }

    [TestMethod]
    public void ProcessCommittedBlock_RetryOfTriggerBlockPersistsPendingWithoutDuplicateBatch()
    {
        using var plugin = new L2BatchPlugin(OneBlockSettings());
        var sink = new DurableSink { FailBeforePersistOnce = true };
        plugin.WithSealedBatchSink(sink, 1001);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => plugin.ProcessCommittedBlock(1, 1000, 11, NoTxs()));
        plugin.ProcessCommittedBlock(1, 1000, 11, NoTxs());

        Assert.AreEqual(1, sink.PersistedBatches.Count);
        Assert.AreEqual(1UL, sink.Checkpoint!.BatchNumber);
        Assert.AreEqual(1UL, sink.Checkpoint.LastBlock);
    }

    [TestMethod]
    public void Wire_CrashAfterPersistBeforeAck_RestoresNumberBlockAndStateContinuity()
    {
        var sink = new DurableSink { FailAfterPersistOnce = true };
        using (var first = new L2BatchPlugin(OneBlockSettings()))
        {
            first.WithSealedBatchSink(sink, 1001);
            Assert.ThrowsExactly<InvalidOperationException>(
                () => first.ProcessCommittedBlock(1, 1000, 11, NoTxs()));
        }
        Assert.AreEqual(1UL, sink.Checkpoint!.BatchNumber);
        Assert.AreEqual(1UL, sink.Checkpoint.LastBlock);

        using var restarted = new L2BatchPlugin(OneBlockSettings());
        restarted.WithSealedBatchSink(sink, 1001);
        restarted.ProcessCommittedBlock(2, 1100, 11, NoTxs());

        Assert.AreEqual(2, sink.CheckpointReadCount);
        Assert.AreEqual(2, sink.PersistedBatches.Count);
        var recoveredBatch = sink.PersistedBatches[1];
        Assert.AreEqual(2UL, recoveredBatch.BatchNumber);
        Assert.AreEqual(2UL, recoveredBatch.FirstBlock);
        Assert.AreEqual(DurableSink.RootFor(1), recoveredBatch.PreStateRoot);
    }

    [TestMethod]
    public void Wire_RestoredCheckpointRejectsDuplicateAndDowntimeGapFailClosed()
    {
        var checkpointRoot = DurableSink.RootFor(3);
        var sink = new DurableSink
        {
            Checkpoint = new SealedBatchCheckpoint(3, 30, checkpointRoot),
        };
        using var plugin = new L2BatchPlugin(OneBlockSettings());
        plugin.WithSealedBatchSink(sink, 1001);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => plugin.ProcessCommittedBlock(30, 1000, 11, NoTxs()));
        Assert.ThrowsExactly<InvalidOperationException>(
            () => plugin.ProcessCommittedBlock(32, 1200, 11, NoTxs()));
        Assert.AreEqual(0, sink.AttemptedBatchNumbers.Count);

        plugin.ProcessCommittedBlock(31, 1100, 11, NoTxs());
        Assert.AreEqual(4UL, sink.PersistedBatches.Single().BatchNumber);
        Assert.AreEqual(checkpointRoot, sink.PersistedBatches.Single().PreStateRoot);
    }

    [TestMethod]
    public void Wire_EmptyCheckpointRequiresFirstNonGenesisBlock()
    {
        var sink = new DurableSink();
        using var plugin = new L2BatchPlugin(OneBlockSettings());
        plugin.WithSealedBatchSink(sink, 1001);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => plugin.ProcessCommittedBlock(2, 1200, 11, NoTxs()));
        Assert.AreEqual(0, sink.AttemptedBatchNumbers.Count);

        plugin.ProcessCommittedBlock(1, 1100, 11, NoTxs());
        Assert.AreEqual(1UL, sink.Checkpoint!.BatchNumber);
        Assert.AreEqual(1UL, sink.Checkpoint.LastBlock);
    }

    private static L2BatchSettings OneBlockSettings() => new()
    {
        ChainId = 1001,
        MaxBlocksPerBatch = 1,
        MaxTransactionsPerBatch = 100,
        MaxBatchAgeMillis = int.MaxValue,
        Enabled = true,
    };

    private static IEnumerable<byte[]> NoTxs() => Array.Empty<byte[]>();

    private sealed class DurableSink : ISealedBatchSink
    {
        public bool FailBeforePersistOnce { get; init; }
        public bool FailAfterPersistOnce { get; init; }
        public SealedBatchCheckpoint? Checkpoint { get; set; }
        public int CheckpointReadCount { get; private set; }
        public List<ulong> AttemptedBatchNumbers { get; } = new();
        public List<SealedBatch> PersistedBatches { get; } = new();

        private bool _failedBefore;
        private bool _failedAfter;

        public ValueTask<UInt256> PersistAsync(
            SealedBatch batch,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AttemptedBatchNumbers.Add(batch.BatchNumber);
            if (FailBeforePersistOnce && !_failedBefore)
            {
                _failedBefore = true;
                throw new InvalidOperationException("sink unavailable before persistence");
            }

            if (Checkpoint?.BatchNumber == batch.BatchNumber)
            {
                if (Checkpoint.LastBlock != batch.LastBlock)
                    throw new InvalidOperationException("idempotent batch retry changed block range");
                return ValueTask.FromResult(Checkpoint.PostStateRoot);
            }
            var expectedBatch = Checkpoint is null ? 1UL : Checkpoint.BatchNumber + 1;
            if (batch.BatchNumber != expectedBatch)
                throw new InvalidOperationException("sink observed a batch-number gap");
            if (Checkpoint is not null)
            {
                if (batch.FirstBlock != Checkpoint.LastBlock + 1)
                    throw new InvalidOperationException("sink observed a block gap");
                if (!batch.PreStateRoot.Equals(Checkpoint.PostStateRoot))
                    throw new InvalidOperationException("sink observed a state-root gap");
            }

            var postStateRoot = RootFor(batch.BatchNumber);
            PersistedBatches.Add(batch);
            Checkpoint = new SealedBatchCheckpoint(
                batch.BatchNumber, batch.LastBlock, postStateRoot);
            if (FailAfterPersistOnce && !_failedAfter)
            {
                _failedAfter = true;
                throw new InvalidOperationException("process stopped after persistence");
            }
            return ValueTask.FromResult(postStateRoot);
        }

        public ValueTask<SealedBatchCheckpoint?> GetLatestCheckpointAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CheckpointReadCount++;
            return ValueTask.FromResult(Checkpoint);
        }

        public ValueTask<IReadOnlyCollection<ulong>> GetTrackedForcedInclusionNoncesAsync(
            uint chainId,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyCollection<ulong>>(Array.Empty<ulong>());

        public static UInt256 RootFor(ulong batchNumber)
        {
            var bytes = new byte[UInt256.Length];
            bytes[0] = checked((byte)(0x60 + batchNumber));
            return new UInt256(bytes);
        }
    }
}
