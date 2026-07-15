using Neo.L2.Persistence;
using Neo.L2.Telemetry;

namespace Neo.Plugins.L2Gateway.UnitTests;

/// <summary>Restart, poison, idempotency, and operator-recovery tests for the Gateway outbox.</summary>
[TestClass]
public class UT_GatewayOutbox
{
    private static readonly UInt160 MessageRouter = UInt160.Parse("0x" + new string('a', 40));
    private static readonly UInt256 ReplayDomain = H(0xD1);
    private static readonly UInt256 VerificationKey = H(0xA1);
    private static readonly byte[] TestTerminalProof =
        Enumerable.Repeat((byte)0x5A, Sp1GatewayProofProver.Groth16ProofSize).ToArray();

    private static UInt256 H(byte value) => new(Enumerable.Repeat(value, 32).ToArray());

    private static L2BatchCommitment Batch(
        uint chainId,
        ulong batchNumber,
        byte proof = 0x31) => new()
        {
            ChainId = chainId,
            BatchNumber = batchNumber,
            FirstBlock = batchNumber,
            LastBlock = batchNumber,
            PreStateRoot = H(0x01),
            PostStateRoot = H(0x02),
            TxRoot = H(0x03),
            ReceiptRoot = H(0x04),
            WithdrawalRoot = H(0x05),
            L2ToL1MessageRoot = H(0x06),
            L2ToL2MessageRoot = H((byte)(chainId & 0xFF)),
            DACommitment = H(0x08),
            PublicInputHash = H(0x09),
            ProofType = ProofType.Zk,
            Proof = new byte[] { proof },
        };

    private sealed class Prover : IGatewayProofProver
    {
        public byte ProofSystem => 1;
        public byte AggregationBackendId => MerklePathRoundProver.ConstBackendId;
        public int Calls { get; private set; }

        public ValueTask<ReadOnlyMemory<byte>> ProveAsync(
            GatewayProofBinding binding,
            AggregatedCommitment commitment,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            ReadOnlyMemory<byte> proof = TestTerminalProof.ToArray();
            return ValueTask.FromResult(proof);
        }
    }

    private sealed class Publisher : IProofBoundGlobalRootPublisher
    {
        public bool Fail { get; set; }
        public int Calls { get; private set; }
        public ReadOnlyMemory<byte> LastProof { get; private set; }

        public ValueTask<UInt256> PublishGlobalRootAsync(
            GatewayProofBinding binding,
            AggregatedCommitment commitment,
            ReadOnlyMemory<byte> aggregatedProof,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            LastProof = aggregatedProof.ToArray();
            if (Fail) throw new TimeoutException("injected uncertain L1 submission");
            return ValueTask.FromResult(H(0xF1));
        }
    }

    private static L2GatewayPlugin Create(
        PersistentGatewayOutbox outbox,
        Prover prover,
        Publisher publisher,
        int maxRetries = 3)
    {
        var plugin = new L2GatewayPlugin();
        plugin.UseAggregator(new BinaryTreeAggregator(new MerklePathRoundProver()));
        plugin.UsePersistentOutbox(outbox);
        plugin.ConfigureGlobalRootPublication(
            prover,
            publisher,
            MessageRouter,
            ReplayDomain,
            VerificationKey,
            maxRetries);
        return plugin;
    }

    [TestMethod]
    public void Enqueue_IsKeyedIdempotentAndRejectsConflictingReuse()
    {
        using var outbox = new PersistentGatewayOutbox(
            new InMemoryKeyValueStore(),
            ownsStore: true);
        var original = Batch(1001, 7);

        Assert.IsTrue(outbox.Enqueue(original));
        Assert.IsFalse(outbox.Enqueue(original));
        Assert.ThrowsExactly<InvalidOperationException>(() => outbox.Enqueue(
            original with { Proof = new byte[] { 0x32 } }));

        var recovery = outbox.Recover();
        Assert.AreEqual(1, recovery.Sealed.Count);
        Assert.AreEqual(original, recovery.Sealed[0]);
    }

    [TestMethod]
    public void Restart_RehydratesEverySealedBatchInCanonicalOrder()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "neo-n4-gateway-outbox-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var firstStore = new RocksDbKeyValueStore(directory))
            using (var firstOutbox = new PersistentGatewayOutbox(firstStore))
            using (var first = new L2GatewayPlugin())
            {
                first.UseAggregator(new BinaryTreeAggregator(new MerklePathRoundProver()));
                first.UsePersistentOutbox(firstOutbox);
                first.ReceiveBatch(Batch(2002, 2));
                first.ReceiveBatch(Batch(1001, 1));
            }

            using var secondStore = new RocksDbKeyValueStore(directory);
            using var secondOutbox = new PersistentGatewayOutbox(secondStore);
            using var second = new L2GatewayPlugin();
            second.UseAggregator(new BinaryTreeAggregator(new MerklePathRoundProver()));
            second.UsePersistentOutbox(secondOutbox);

            Assert.AreEqual(2, second.Aggregator.PendingCount);
            Assert.AreEqual(2, second.OutboxStatus.QueueDepth);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task Restart_ResumesSubmittedAttemptWithoutReproving()
    {
        using var store = new InMemoryKeyValueStore();
        using var firstOutbox = new PersistentGatewayOutbox(store);
        var firstProver = new Prover();
        var firstPublisher = new Publisher { Fail = true };
        byte[] persistedProof;
        using (var first = Create(firstOutbox, firstProver, firstPublisher))
        {
            first.ReceiveBatch(Batch(1001, 1));
            await Assert.ThrowsExactlyAsync<TimeoutException>(
                async () => await first.PublishAggregateAsync(77));
            persistedProof = firstPublisher.LastProof.ToArray();
            Assert.AreEqual(GatewayOutboxState.Submitted, first.OutboxStatus.PublicationState);
            Assert.AreEqual(1, first.OutboxStatus.RetryCount);
        }

        using var secondOutbox = new PersistentGatewayOutbox(store);
        var secondProver = new Prover();
        var secondPublisher = new Publisher();
        using var second = Create(secondOutbox, secondProver, secondPublisher);

        Assert.IsTrue(second.HasPendingPublication);
        Assert.AreEqual(77UL, second.PendingPublicationEpoch);
        var transactionHash = await second.PublishAggregateAsync(77);

        Assert.AreEqual(H(0xF1), transactionHash);
        Assert.AreEqual(0, secondProver.Calls, "persisted successful proof must be reused");
        CollectionAssert.AreEqual(persistedProof, secondPublisher.LastProof.ToArray());
        Assert.IsFalse(second.HasPendingPublication);
        Assert.AreEqual(0, second.OutboxStatus.QueueDepth);
    }

    [TestMethod]
    public async Task RepeatedFailures_PoisonAndRequireExplicitOperatorRecovery()
    {
        using var outbox = new PersistentGatewayOutbox(
            new InMemoryKeyValueStore(),
            ownsStore: true);
        var prover = new Prover();
        var publisher = new Publisher { Fail = true };
        var metrics = new InMemoryMetrics();
        using var plugin = Create(outbox, prover, publisher, maxRetries: 3);
        plugin.WithMetrics(metrics);
        plugin.ReceiveBatch(Batch(1001, 1));

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await Assert.ThrowsExactlyAsync<TimeoutException>(
                async () => await plugin.PublishAggregateAsync(88));
        }

        Assert.AreEqual(GatewayOutboxState.Poisoned, plugin.OutboxStatus.PublicationState);
        Assert.AreEqual(3, plugin.OutboxStatus.RetryCount);
        Assert.AreEqual(3, publisher.Calls);
        Assert.AreEqual(3, metrics.GetCounter("l2.gateway.outbox.retries"));
        Assert.AreEqual(1, metrics.GetGauge("l2.gateway.outbox.poisoned"));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await plugin.PublishAggregateAsync(88));
        Assert.AreEqual(3, publisher.Calls, "poisoned work must not retry automatically");

        publisher.Fail = false;
        plugin.RecoverPoisonedPublication();
        Assert.AreEqual(GatewayOutboxState.Proved, plugin.OutboxStatus.PublicationState);
        Assert.AreEqual(0, plugin.OutboxStatus.RetryCount);
        await plugin.PublishAggregateAsync(88);

        Assert.IsFalse(plugin.HasPendingPublication);
        Assert.AreEqual(0, plugin.OutboxStatus.QueueDepth);
        Assert.AreEqual(0, metrics.GetGauge("l2.gateway.outbox.poisoned"));
        Assert.AreEqual(1, metrics.GetHistogram("l2.gateway.outbox.confirmation_lag_ms").Count);
    }
}
