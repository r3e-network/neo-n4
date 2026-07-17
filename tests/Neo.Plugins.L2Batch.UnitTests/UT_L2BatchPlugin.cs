using Neo.Cryptography;
using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.ForcedInclusion;
using Neo.L2.Messaging;
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
    public void WithMetrics_AfterSinkWiringUsesNewMetricsSink()
    {
        using var plugin = new L2BatchPlugin(OneBlockSettings());
        var sink = new DurableSink();
        var metrics = new InMemoryMetrics();
        plugin.WithSealedBatchSink(sink, 1001);

        plugin.WithMetrics(metrics);
        plugin.ProcessCommittedBlock(1, 1000, 11, NoTxs());

        Assert.AreEqual(1, metrics.GetCounter(MetricNames.BatchesSealed));
    }

    [TestMethod]
    public void WithSealingInputs_RejectsNullAndBindsBlockContext()
    {
        using var plugin = new L2BatchPlugin(OneBlockSettings());
        Func<int, IReadOnlyList<CrossChainMessage>> drain = _ => Array.Empty<CrossChainMessage>();
        Func<uint> finalizedHeight = () => 77;
        var committeeHash = DurableSink.RootFor(9);
        Func<UInt256> committee = () => committeeHash;

        Assert.ThrowsExactly<ArgumentNullException>(
            () => plugin.WithSealingInputs(null!, finalizedHeight, committee));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => plugin.WithSealingInputs(drain, null!, committee));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => plugin.WithSealingInputs(drain, finalizedHeight, null!));

        plugin.WithSealingInputs(drain, finalizedHeight, committee);
        var sink = new DurableSink();
        plugin.WithSealedBatchSink(sink, 1001);
        plugin.ProcessCommittedBlock(1, 1000, 11, NoTxs());

        var context = sink.PersistedBatches.Single().BlockContext;
        Assert.AreEqual(77U, context.L1FinalizedHeight);
        Assert.AreEqual(committeeHash, context.SequencerCommitteeHash);
    }

    [TestMethod]
    public void WithSealingInputs_AfterSinkWiringFailsClosed()
    {
        using var plugin = new L2BatchPlugin(OneBlockSettings());
        plugin.WithSealedBatchSink(new DurableSink(), 1001);

        Assert.ThrowsExactly<InvalidOperationException>(() => plugin.WithSealingInputs(
            _ => Array.Empty<CrossChainMessage>(),
            () => 0,
            () => UInt256.Zero));
    }

    [TestMethod]
    public void WithForcedInclusionSource_RejectsNullWrongChainAndLateWiring()
    {
        using var plugin = new L2BatchPlugin(OneBlockSettings());

        Assert.ThrowsExactly<ArgumentNullException>(
            () => plugin.WithForcedInclusionSource(null!));
        Assert.ThrowsExactly<InvalidOperationException>(
            () => plugin.WithForcedInclusionSource(new FakeForcedInclusionSource(1002)));

        plugin.WithSealedBatchSink(new DurableSink(), 1001);
        Assert.ThrowsExactly<InvalidOperationException>(
            () => plugin.WithForcedInclusionSource(new FakeForcedInclusionSource(1001)));
    }

    [TestMethod]
    public void WithSealedBatchSink_RejectsInvalidArgumentsAndSettingsMismatch()
    {
        using var plugin = new L2BatchPlugin(OneBlockSettings());

        Assert.ThrowsExactly<ArgumentNullException>(
            () => plugin.WithSealedBatchSink(null!, 1001));
        Assert.ThrowsExactly<InvalidDataException>(
            () => plugin.WithSealedBatchSink(new DurableSink(), 0));
        Assert.ThrowsExactly<InvalidOperationException>(
            () => plugin.WithSealedBatchSink(new DurableSink(), 1002));
    }

    [TestMethod]
    public void WithSealedBatchSink_EnforcesOneImmutableSink()
    {
        using var plugin = new L2BatchPlugin(OneBlockSettings());
        var first = new DurableSink();
        var second = new DurableSink();
        plugin.WithSealedBatchSink(first, 1001);

        plugin.WithSealedBatchSink(first, 1001);
        Assert.AreEqual(1, first.CheckpointReadCount);
        Assert.ThrowsExactly<InvalidOperationException>(
            () => plugin.WithSealedBatchSink(second, 1001));

        plugin.RemoveSealedBatchSink(first);
        Assert.ThrowsExactly<InvalidOperationException>(
            () => plugin.WithSealedBatchSink(second, 1001));
    }

    [TestMethod]
    public void WithSealedBatchSink_UnboundSettingsAdoptChainAndRejectForcedSourceMismatch()
    {
        using (var unbound = new L2BatchPlugin())
        {
            unbound.WithSealedBatchSink(new DurableSink(), 1001);
            unbound.ProcessCommittedBlock(1, 1000, 11, NoTxs());
        }

        using var mismatched = new L2BatchPlugin();
        mismatched.WithForcedInclusionSource(new FakeForcedInclusionSource(1002));
        Assert.ThrowsExactly<InvalidOperationException>(
            () => mismatched.WithSealedBatchSink(new DurableSink(), 1001));
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
    public void TryRetryPendingSealedBatch_RetriesWithoutNewBlock()
    {
        using var plugin = new L2BatchPlugin(OneBlockSettings());
        var sink = new DurableSink { FailBeforePersistOnce = true };
        plugin.WithSealedBatchSink(sink, 1001);

        Assert.IsFalse(plugin.TryRetryPendingSealedBatch());
        Assert.ThrowsExactly<InvalidOperationException>(
            () => plugin.ProcessCommittedBlock(1, 1000, 11, NoTxs()));
        Assert.IsTrue(plugin.HasPendingSealedBatch);
        Assert.IsFalse(plugin.HasOpenBatch);

        Assert.IsTrue(plugin.TryRetryPendingSealedBatch());
        Assert.IsFalse(plugin.HasPendingSealedBatch);
        Assert.AreEqual(1, sink.PersistedBatches.Count);
        Assert.AreEqual(1UL, sink.Checkpoint!.BatchNumber);
        Assert.IsFalse(plugin.TryRetryPendingSealedBatch());
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
    public void ProcessCommittedBlock_OlderThanPendingBatchFailsAfterDurableRetry()
    {
        using var plugin = new L2BatchPlugin(TwoBlockSettings());
        var sink = new DurableSink { FailBeforePersistOnce = true };
        plugin.WithSealedBatchSink(sink, 1001);
        plugin.ProcessCommittedBlock(1, 1000, 11, NoTxs());
        Assert.ThrowsExactly<InvalidOperationException>(
            () => plugin.ProcessCommittedBlock(2, 1100, 11, NoTxs()));

        Assert.ThrowsExactly<InvalidOperationException>(
            () => plugin.ProcessCommittedBlock(1, 1000, 11, NoTxs()));

        Assert.AreEqual(1, sink.PersistedBatches.Count);
        Assert.AreEqual(2UL, sink.Checkpoint!.LastBlock);
    }

    [TestMethod]
    public void WireL1MessageInbox_Deposits_ConfirmAfterDurableSeal()
    {
        // Functional completeness: Drain reserves → seal persists → ConfirmConsumed.
        var deposits = new InMemorySharedBridgeDepositSource(1001, L2BridgeHash);
        deposits.Enqueue(DepositRecord(nonce: 7, amount: 1000));
        deposits.Enqueue(DepositRecord(nonce: 8, amount: 2000));

        var sink = new DurableSink();
        using var plugin = new L2BatchPlugin(OneBlockSettings());
        plugin.WireL1MessageInbox(
            chainId: 1001,
            l1FinalizedHeight: static () => 42,
            sequencerCommitteeHash: static () => CommitteeHash,
            deposits: deposits);
        plugin.WithSealedBatchSink(sink, 1001);

        plugin.ProcessCommittedBlock(1, 1000, 11, NoTxs());

        var batch = sink.PersistedBatches.Single();
        Assert.AreEqual(2, batch.L1Messages.Count);
        Assert.AreEqual(7UL, batch.L1Messages[0].Nonce);
        Assert.AreEqual(8UL, batch.L1Messages[1].Nonce);
        Assert.AreEqual(MessageType.Deposit, batch.L1Messages[0].MessageType);
        Assert.AreEqual(42U, batch.BlockContext.L1FinalizedHeight);
        Assert.AreEqual(0, deposits.Peek(10).Count, "confirmed deposits must leave ready set");
        Assert.AreEqual(0, deposits.Drain(10).Count, "confirmed deposits must not re-drain");
        Assert.AreSame(deposits, plugin.DepositSource);
    }

    [TestMethod]
    public void WireL1MessageInbox_Deposits_ReleaseOnPersistFailureAndRetrySucceeds()
    {
        // Persist failure must ReleaseReservations so the next seal re-includes deposits.
        var deposits = new InMemorySharedBridgeDepositSource(1001, L2BridgeHash);
        deposits.Enqueue(DepositRecord(nonce: 3, amount: 500));

        var sink = new DurableSink { FailBeforePersistOnce = true };
        using var plugin = new L2BatchPlugin(OneBlockSettings());
        plugin.WireL1MessageInbox(
            chainId: 1001,
            l1FinalizedHeight: static () => 1,
            sequencerCommitteeHash: static () => CommitteeHash,
            deposits: deposits);
        plugin.WithSealedBatchSink(sink, 1001);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => plugin.ProcessCommittedBlock(1, 1000, 11, NoTxs()));
        Assert.AreEqual(0, sink.PersistedBatches.Count);
        // After release, deposit is ready again (peek) even though not yet confirmed.
        Assert.AreEqual(1, deposits.Peek(10).Count);

        plugin.ProcessCommittedBlock(1, 1000, 11, NoTxs());
        Assert.AreEqual(1, sink.PersistedBatches.Count);
        Assert.AreEqual(3UL, sink.PersistedBatches[0].L1Messages.Single().Nonce);
        Assert.AreEqual(0, deposits.Peek(10).Count);
    }

    [TestMethod]
    public void WireL1MessageInbox_RejectsEmptySourcesAndChainMismatch()
    {
        using var plugin = new L2BatchPlugin(OneBlockSettings());
        Assert.ThrowsExactly<ArgumentException>(() =>
            plugin.WireL1MessageInbox(
                1001,
                static () => 1,
                static () => CommitteeHash,
                deposits: null,
                messageRouter: null));

        var wrongChain = new InMemorySharedBridgeDepositSource(2002, L2BridgeHash);
        Assert.ThrowsExactly<ArgumentException>(() =>
            plugin.WireL1MessageInbox(
                1001,
                static () => 1,
                static () => CommitteeHash,
                deposits: wrongChain));
    }

    [TestMethod]
    public void WireL1MessageInbox_Deposits_ScanAtSealDiscoversPending()
    {
        // Production RpcSharedBridgeDepositSource only materializes after ScanAsync.
        // Seal-path FromDeposits must Scan before Drain so L1-finalized deposits enter the batch
        // without a separate operator poll loop.
        var deposits = new ScanGatedDepositSource(1001, L2BridgeHash);
        deposits.Stage(DepositRecord(nonce: 11, amount: 777));
        Assert.AreEqual(0, deposits.Peek(10).Count);

        var sink = new DurableSink();
        using var plugin = new L2BatchPlugin(OneBlockSettings());
        plugin.WireL1MessageInbox(
            chainId: 1001,
            l1FinalizedHeight: static () => 5,
            sequencerCommitteeHash: static () => CommitteeHash,
            deposits: deposits);
        plugin.WithSealedBatchSink(sink, 1001);

        plugin.ProcessCommittedBlock(1, 1000, 11, NoTxs());

        Assert.IsTrue(deposits.ScanCount >= 1, "seal must scan deposits");
        var batch = sink.PersistedBatches.Single();
        Assert.AreEqual(1, batch.L1Messages.Count);
        Assert.AreEqual(11UL, batch.L1Messages[0].Nonce);
        Assert.AreEqual(0, deposits.Peek(10).Count, "confirmed after durable seal");
    }

    [TestMethod]
    public void WireL1MessageInbox_MessageRouter_SealsInboundMessages()
    {
        // MessageRouter traffic must enter the sealed batch L1 inbox via FromRouter.
        using var router = new InMemoryMessageRouter();
        router.Inbox.Enqueue(L1CallMessage(nonce: 3));
        router.Inbox.Enqueue(L1CallMessage(nonce: 4));

        var sink = new DurableSink();
        using var plugin = new L2BatchPlugin(OneBlockSettings());
        plugin.WireL1MessageInbox(
            chainId: 1001,
            l1FinalizedHeight: static () => 9,
            sequencerCommitteeHash: static () => CommitteeHash,
            messageRouter: router);
        plugin.WithSealedBatchSink(sink, 1001);

        plugin.ProcessCommittedBlock(1, 1000, 11, NoTxs());

        Assert.AreSame(router, plugin.MessageRouter);
        var batch = sink.PersistedBatches.Single();
        Assert.AreEqual(2, batch.L1Messages.Count);
        Assert.AreEqual(3UL, batch.L1Messages[0].Nonce);
        Assert.AreEqual(4UL, batch.L1Messages[1].Nonce);
        Assert.AreEqual(MessageType.Call, batch.L1Messages[0].MessageType);
        Assert.AreEqual(0, router.Inbox.PendingCount, "dequeue must consume inbox");
    }

    [TestMethod]
    public void WireL1MessageInbox_DepositsAndMessageRouter_MergeSorted()
    {
        // Combined inbox: SharedBridge deposits + MessageRouter call messages, sorted by nonce.
        // Nonces must not collide under sourceChainId=0 (Combine fail-closed).
        var deposits = new InMemorySharedBridgeDepositSource(1001, L2BridgeHash);
        deposits.Enqueue(DepositRecord(nonce: 2, amount: 100));
        deposits.Enqueue(DepositRecord(nonce: 5, amount: 200));
        using var router = new InMemoryMessageRouter();
        router.Inbox.Enqueue(L1CallMessage(nonce: 3));
        router.Inbox.Enqueue(L1CallMessage(nonce: 4));

        var sink = new DurableSink();
        using var plugin = new L2BatchPlugin(OneBlockSettings());
        plugin.WireL1MessageInbox(
            chainId: 1001,
            l1FinalizedHeight: static () => 1,
            sequencerCommitteeHash: static () => CommitteeHash,
            deposits: deposits,
            messageRouter: router);
        plugin.WithSealedBatchSink(sink, 1001);

        plugin.ProcessCommittedBlock(1, 1000, 11, NoTxs());

        var batch = sink.PersistedBatches.Single();
        CollectionAssert.AreEqual(
            new ulong[] { 2, 3, 4, 5 },
            batch.L1Messages.Select(m => m.Nonce).ToArray());
        Assert.AreEqual(MessageType.Deposit, batch.L1Messages[0].MessageType);
        Assert.AreEqual(MessageType.Call, batch.L1Messages[1].MessageType);
        Assert.AreSame(deposits, plugin.DepositSource);
        Assert.AreSame(router, plugin.MessageRouter);
        Assert.AreEqual(0, deposits.Peek(10).Count, "deposits confirmed after durable seal");
    }

    [TestMethod]
    public void WireL1MessageInbox_RejectsSecondDistinctMessageRouter()
    {
        using var first = new InMemoryMessageRouter();
        using var second = new InMemoryMessageRouter();
        using var plugin = new L2BatchPlugin(OneBlockSettings());
        plugin.WireL1MessageInbox(
            1001, static () => 1, static () => CommitteeHash, messageRouter: first);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            plugin.WireL1MessageInbox(
                1001, static () => 1, static () => CommitteeHash, messageRouter: second));
    }

    [TestMethod]
    public void ForcedInclusion_FiltersTrackedAndNullEntriesWithoutEarlyConsumption()
    {
        var source = new FakeForcedInclusionSource(1001)
        {
            Entries =
            [
                ForcedEntry(1, 0xA1),
                null!,
                ForcedEntry(2, 0xA2),
                ForcedEntry(3, 0xA3),
            ],
        };
        var sink = new DurableSink
        {
            TrackedForcedInclusionNonces = [1],
        };
        using var plugin = new L2BatchPlugin(OneBlockSettings());
        plugin.WithForcedInclusionSource(source);
        plugin.WithSealedBatchSink(sink, 1001);

        plugin.ProcessCommittedBlock(1, 1000, 11, NoTxs());

        var batch = sink.PersistedBatches.Single();
        CollectionAssert.AreEqual(
            new ulong[] { 2, 3 },
            batch.ForcedInclusions.Select(entry => entry.Nonce).ToArray());
        Assert.AreEqual(int.MaxValue, source.LastDrainMaximum);
        Assert.AreEqual(1001U, sink.LastTrackedForcedInclusionChainId);
        Assert.AreEqual(0, source.ConfirmConsumedCount);
    }

    [TestMethod]
    public void ForcedInclusion_NullDrainResultFailsClosed()
    {
        var source = new FakeForcedInclusionSource(1001) { Entries = null };
        var sink = new DurableSink();
        using var plugin = new L2BatchPlugin(OneBlockSettings());
        plugin.WithForcedInclusionSource(source);
        plugin.WithSealedBatchSink(sink, 1001);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => plugin.ProcessCommittedBlock(1, 1000, 11, NoTxs()));
        Assert.AreEqual(0, sink.PersistedBatches.Count);
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

    [TestMethod]
    public void Wire_EmptyCheckpointUsesAuthenticatedInitialStateRoot()
    {
        var initialStateRoot = DurableSink.RootFor(9);
        var sink = new DurableSink { InitialStateRoot = initialStateRoot };
        using var plugin = new L2BatchPlugin(OneBlockSettings());

        plugin.WithSealedBatchSink(sink, 1001);
        plugin.ProcessCommittedBlock(1, 1100, 11, NoTxs());

        Assert.AreEqual(initialStateRoot, sink.PersistedBatches.Single().PreStateRoot);
    }

    private static readonly UInt160 L2BridgeHash = UInt160.Parse("0x" + new string('e', 40));
    private static readonly UInt160 AssetHash = UInt160.Parse("0x" + new string('a', 40));
    private static readonly UInt160 AliceHash = UInt160.Parse("0x" + new string('b', 40));
    private static readonly UInt256 CommitteeHash = UInt256.Parse("0x" + new string('c', 64));

    private static SharedBridgeDepositRecord DepositRecord(ulong nonce, int amount) => new()
    {
        Asset = AssetHash,
        Recipient = AliceHash,
        Sender = AliceHash,
        Nonce = nonce,
        Amount = amount,
    };

    private static CrossChainMessage L1CallMessage(ulong nonce) => MessageBuilder.Build(
        sourceChainId: 0,
        targetChainId: 1001,
        nonce: nonce,
        sender: AliceHash,
        receiver: L2BridgeHash,
        messageType: MessageType.Call,
        payload: new byte[] { 0xCA, (byte)nonce });

    private static L2BatchSettings OneBlockSettings() => new()
    {
        ChainId = 1001,
        MaxBlocksPerBatch = 1,
        MaxTransactionsPerBatch = 100,
        MaxBatchAgeMillis = int.MaxValue,
        Enabled = true,
    };

    private static L2BatchSettings TwoBlockSettings() => new()
    {
        ChainId = 1001,
        MaxBlocksPerBatch = 2,
        MaxTransactionsPerBatch = 100,
        MaxBatchAgeMillis = int.MaxValue,
        Enabled = true,
    };

    private static IEnumerable<byte[]> NoTxs() => Array.Empty<byte[]>();

    private static ForcedInclusionEntry ForcedEntry(ulong nonce, byte value)
    {
        var transaction = new[] { value };
        return new ForcedInclusionEntry
        {
            Nonce = nonce,
            Sender = UInt160.Zero,
            TxHash = new UInt256(Crypto.Hash256(transaction)),
            SerializedTx = transaction,
            DeadlineUnixSeconds = 1000,
        };
    }

    private sealed class DurableSink : ISealedBatchSink
    {
        public bool FailBeforePersistOnce { get; init; }
        public bool FailAfterPersistOnce { get; init; }
        public SealedBatchCheckpoint? Checkpoint { get; set; }
        public int CheckpointReadCount { get; private set; }
        public List<ulong> AttemptedBatchNumbers { get; } = new();
        public List<SealedBatch> PersistedBatches { get; } = new();
        public IReadOnlyCollection<ulong> TrackedForcedInclusionNonces { get; init; } = [];
        public UInt256 InitialStateRoot { get; init; } = UInt256.Zero;
        public uint? LastTrackedForcedInclusionChainId { get; private set; }

        private bool _failedBefore;
        private bool _failedAfter;

        public ValueTask<UInt256> GetInitialStateRootAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(InitialStateRoot);
        }

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
            else if (!batch.PreStateRoot.Equals(InitialStateRoot))
            {
                throw new InvalidOperationException(
                    "sink observed a genesis state-root mismatch");
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
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastTrackedForcedInclusionChainId = chainId;
            return ValueTask.FromResult(TrackedForcedInclusionNonces);
        }

        public static UInt256 RootFor(ulong batchNumber)
        {
            var bytes = new byte[UInt256.Length];
            bytes[0] = checked((byte)(0x60 + batchNumber));
            return new UInt256(bytes);
        }
    }

    private sealed class FakeForcedInclusionSource(uint chainId) : IForcedInclusionSource
    {
        public uint ChainId { get; } = chainId;

        public IReadOnlyList<ForcedInclusionEntry>? Entries { get; init; } = [];

        public int? LastDrainMaximum { get; private set; }

        public int ConfirmConsumedCount { get; private set; }

        public ValueTask<IReadOnlyList<ForcedInclusionEntry>> DrainAsync(
            int max,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastDrainMaximum = max;
            return ValueTask.FromResult(Entries!);
        }

        public ValueTask ConfirmConsumedAsync(
            ulong nonce,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConfirmConsumedCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> HasOverdueEntryAsync(
            uint nowUnixSeconds,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(false);
        }
    }

    /// <summary>
    /// Staged deposits become ready only after <see cref="ScanAsync"/> — mirrors production
    /// event discovery without spinning an L1 RPC stub.
    /// </summary>
    private sealed class ScanGatedDepositSource : ISharedBridgeDepositSource
    {
        private readonly InMemorySharedBridgeDepositSource _inner;
        private readonly List<SharedBridgeDepositRecord> _staged = new();

        public ScanGatedDepositSource(uint chainId, UInt160 l2BridgeHash)
        {
            _inner = new InMemorySharedBridgeDepositSource(chainId, l2BridgeHash);
        }

        public uint ChainId => _inner.ChainId;
        public int ScanCount { get; private set; }

        public void Stage(SharedBridgeDepositRecord record) => _staged.Add(record);

        public ValueTask<int> ScanAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ScanCount++;
            foreach (var record in _staged)
                _inner.Enqueue(record);
            var count = _staged.Count;
            _staged.Clear();
            return ValueTask.FromResult(count);
        }

        public IReadOnlyList<CrossChainMessage> Peek(int maxMessages) => _inner.Peek(maxMessages);

        public IReadOnlyList<CrossChainMessage> Drain(int maxMessages) => _inner.Drain(maxMessages);

        public void ConfirmConsumed(ulong nonce) => _inner.ConfirmConsumed(nonce);

        public void ReleaseReservations(IEnumerable<ulong> nonces) =>
            _inner.ReleaseReservations(nonces);
    }
}
