namespace Neo.Plugins.L2Gateway.UnitTests;

/// <summary>Security and lifecycle tests for the production Gateway publication state machine.</summary>
[TestClass]
public class UT_L2GatewayPlugin
{
    private static readonly UInt160 MessageRouter = UInt160.Parse("0x" + new string('a', 40));
    private static readonly UInt256 ReplayDomain = FilledHash(0xD1);
    private static readonly UInt256 VerificationKeyId = FilledHash(0xA1);
    private static readonly byte[] TestTerminalProof =
        Enumerable.Repeat((byte)0x5A, Sp1GatewayProofProver.Groth16ProofSize).ToArray();

    private static UInt256 FilledHash(byte value) => new(Enumerable.Repeat(value, 32).ToArray());

    private static L2BatchCommitment SampleCommitment(
        uint chainId = 1001,
        ulong batchNumber = 1,
        byte proofByte = 0x31) => new()
        {
            ChainId = chainId,
            BatchNumber = batchNumber,
            FirstBlock = batchNumber * 10,
            LastBlock = batchNumber * 10 + 9,
            PreStateRoot = FilledHash(0x01),
            PostStateRoot = FilledHash(0x02),
            TxRoot = FilledHash(0x03),
            ReceiptRoot = FilledHash(0x04),
            WithdrawalRoot = FilledHash(0x05),
            L2ToL1MessageRoot = FilledHash(0x06),
            L2ToL2MessageRoot = FilledHash((byte)(chainId & 0xFF)),
            DACommitment = FilledHash(0x08),
            PublicInputHash = FilledHash(0x09),
            ProofType = ProofType.Multisig,
            Proof = new byte[] { proofByte },
        };

    private sealed class RecordingProofProver : IGatewayProofProver
    {
        public byte ProofSystem { get; init; } = 1;
        public byte AggregationBackendId { get; init; } = MerklePathRoundProver.ConstBackendId;
        public int CallCount { get; private set; }
        public bool FailNext { get; set; }
        public GatewayProofBinding? LastBinding { get; private set; }

        public ValueTask<ReadOnlyMemory<byte>> ProveAsync(
            GatewayProofBinding binding,
            AggregatedCommitment commitment,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            LastBinding = binding;
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("injected prover failure");
            }
            ReadOnlyMemory<byte> proof = TestTerminalProof.ToArray();
            return ValueTask.FromResult(proof);
        }
    }

    private sealed class RecordingPublisher : IProofBoundGlobalRootPublisher
    {
        public int CallCount { get; private set; }
        public bool FailNext { get; set; }
        public GatewayProofBinding? LastBinding { get; private set; }
        public AggregatedCommitment? LastCommitment { get; private set; }
        public ReadOnlyMemory<byte> LastProof { get; private set; }
        public UInt256 TransactionHash { get; } = UInt256.Parse("0x" + new string('f', 64));

        public ValueTask<UInt256> PublishGlobalRootAsync(
            GatewayProofBinding binding,
            AggregatedCommitment commitment,
            ReadOnlyMemory<byte> aggregatedProof,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            LastBinding = binding;
            LastCommitment = commitment;
            LastProof = aggregatedProof.ToArray();
            if (FailNext)
            {
                FailNext = false;
                throw new TimeoutException("injected publisher timeout");
            }
            return ValueTask.FromResult(TransactionHash);
        }
    }

    private static (L2GatewayPlugin Plugin, RecordingProofProver Prover, RecordingPublisher Publisher)
        CreateProductionPlugin()
    {
        var plugin = new L2GatewayPlugin();
        plugin.UseAggregator(new BinaryTreeAggregator(new MerklePathRoundProver()));
        plugin.UsePersistentOutbox(new PersistentGatewayOutbox(
            new Neo.L2.Persistence.InMemoryKeyValueStore(),
            ownsStore: true), ownsOutbox: true);
        var prover = new RecordingProofProver();
        var publisher = new RecordingPublisher();
        plugin.ConfigureGlobalRootPublication(
            prover,
            publisher,
            MessageRouter,
            ReplayDomain,
            VerificationKeyId);
        return (plugin, prover, publisher);
    }

    [TestMethod]
    public void DefaultsRemainDevOnly_AndProductionRejectsPassThrough()
    {
        using var plugin = new L2GatewayPlugin();
        Assert.IsInstanceOfType(plugin.Aggregator, typeof(PassThroughAggregator));
        Assert.IsInstanceOfType(plugin.GlobalRootPublisher, typeof(NoOpGlobalRootPublisher));
        var prover = new RecordingProofProver
        {
            AggregationBackendId = PassThroughAggregator.BackendId,
        };
        Assert.ThrowsExactly<ArgumentException>(() => plugin.ConfigureGlobalRootPublication(
            prover,
            new RecordingPublisher(),
            MessageRouter,
            ReplayDomain,
            VerificationKeyId));

        plugin.UseAggregator(new BinaryTreeAggregator());
        Assert.ThrowsExactly<ArgumentException>(() => plugin.ConfigureGlobalRootPublication(
            new RecordingProofProver
            {
                AggregationBackendId = PassThroughRoundProver.ConstBackendId,
            },
            new RecordingPublisher(),
            MessageRouter,
            ReplayDomain,
            VerificationKeyId));
    }

    [TestMethod]
    public async Task PublishAggregateAsync_UnconfiguredFailsBeforeDraining()
    {
        using var plugin = new L2GatewayPlugin();
        plugin.ReceiveBatch(SampleCommitment());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await plugin.PublishAggregateAsync(7));

        Assert.AreEqual(1, plugin.Aggregator.PendingCount);
        Assert.IsFalse(plugin.HasPendingPublication);
    }

    [TestMethod]
    public void ProductionConfiguration_RequiresPersistentOutbox()
    {
        using var plugin = new L2GatewayPlugin();
        plugin.UseAggregator(new BinaryTreeAggregator(new MerklePathRoundProver()));

        Assert.ThrowsExactly<InvalidOperationException>(() => plugin.ConfigureGlobalRootPublication(
            new RecordingProofProver(),
            new RecordingPublisher(),
            MessageRouter,
            ReplayDomain,
            VerificationKeyId));
    }

    [TestMethod]
    public async Task PublishAggregateAsync_BindsAllFields_AndClearsOnlyAfterSuccess()
    {
        var setup = CreateProductionPlugin();
        using var plugin = setup.Plugin;
        plugin.ReceiveBatch(SampleCommitment(chainId: 2002));
        plugin.ReceiveBatch(SampleCommitment(chainId: 1001));

        var transactionHash = await plugin.PublishAggregateAsync(77);

        Assert.AreEqual(setup.Publisher.TransactionHash, transactionHash);
        Assert.AreEqual(1, setup.Prover.CallCount);
        Assert.AreEqual(1, setup.Publisher.CallCount);
        Assert.IsFalse(plugin.HasPendingPublication);
        Assert.AreEqual(0, plugin.Aggregator.PendingCount);
        var binding = setup.Publisher.LastBinding!;
        Assert.AreEqual(77UL, binding.BatchEpoch);
        Assert.AreEqual(MessageRouter, binding.MessageRouter);
        Assert.AreEqual(ReplayDomain, binding.ReplayDomain);
        Assert.AreEqual(VerificationKeyId, binding.VerificationKeyId);
        Assert.AreEqual(2U, binding.ConstituentCount);
        Assert.AreEqual(MerklePathRoundProver.ConstBackendId, binding.AggregationBackendId);
        Assert.AreEqual((byte)1, binding.ProofSystem);
        Assert.IsTrue(setup.Publisher.LastProof.Span.SequenceEqual(TestTerminalProof));
    }

    [TestMethod]
    public async Task ProverFailure_RetainsExactAttempt_ForSameEpochRetry()
    {
        var setup = CreateProductionPlugin();
        using var plugin = setup.Plugin;
        setup.Prover.FailNext = true;
        plugin.ReceiveBatch(SampleCommitment());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await plugin.PublishAggregateAsync(81));
        Assert.IsTrue(plugin.HasPendingPublication);
        Assert.AreEqual(81UL, plugin.PendingPublicationEpoch);
        Assert.AreEqual(0, plugin.Aggregator.PendingCount);
        Assert.AreEqual(0, setup.Publisher.CallCount);

        var transactionHash = await plugin.PublishAggregateAsync(81);
        Assert.AreEqual(setup.Publisher.TransactionHash, transactionHash);
        Assert.AreEqual(2, setup.Prover.CallCount);
        Assert.AreEqual(1, setup.Publisher.CallCount);
        Assert.IsFalse(plugin.HasPendingPublication);
    }

    [TestMethod]
    public async Task PublisherFailure_RetriesSameProof_AndBlocksNewEpoch()
    {
        var setup = CreateProductionPlugin();
        using var plugin = setup.Plugin;
        setup.Publisher.FailNext = true;
        plugin.ReceiveBatch(SampleCommitment());

        await Assert.ThrowsExactlyAsync<TimeoutException>(
            async () => await plugin.PublishAggregateAsync(91));
        var firstProof = setup.Publisher.LastProof.ToArray();
        Assert.IsTrue(plugin.HasPendingPublication);
        Assert.AreEqual(1, setup.Prover.CallCount);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await plugin.PublishAggregateAsync(92));
        Assert.AreEqual(1, setup.Publisher.CallCount);

        var transactionHash = await plugin.PublishAggregateAsync(91);
        Assert.AreEqual(setup.Publisher.TransactionHash, transactionHash);
        Assert.AreEqual(1, setup.Prover.CallCount, "successful proof bytes must be reused on retry");
        Assert.AreEqual(2, setup.Publisher.CallCount);
        CollectionAssert.AreEqual(firstProof, setup.Publisher.LastProof.ToArray());
        Assert.IsFalse(plugin.HasPendingPublication);
    }

    [TestMethod]
    public void DirectDrainAndReconfiguration_AreBlockedWithProductionWork()
    {
        var setup = CreateProductionPlugin();
        using var plugin = setup.Plugin;
        plugin.ReceiveBatch(SampleCommitment());

        Assert.ThrowsExactly<InvalidOperationException>(() => plugin.PullAggregate());
        Assert.ThrowsExactly<InvalidOperationException>(() => plugin.UseAggregator(
            new BinaryTreeAggregator(new MerklePathRoundProver())));
    }

    [TestMethod]
    public void LegacySeams_ValidateAndDefensivelyCopy()
    {
        using var plugin = new L2GatewayPlugin();
        Assert.ThrowsExactly<ArgumentNullException>(() => plugin.UseAggregator(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => plugin.ReceiveBatch(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => plugin.UseGlobalRootPublisher(null!));
        Assert.ThrowsExactly<ArgumentException>(() => plugin.SetGlobalRootVerificationKeyId(new byte[31]));

        var mutable = Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray();
        plugin.SetGlobalRootVerificationKeyId(mutable);
        mutable[0] = 0xFF;
        Assert.AreEqual(0x01, plugin.GlobalRootVerificationKeyId.Span[0]);
        Assert.IsFalse(string.IsNullOrWhiteSpace(plugin.Name));
        Assert.IsFalse(string.IsNullOrWhiteSpace(plugin.Description));
    }

    [TestMethod]
    public void CreateFromChainDirectory_LoadsSettingsAndAttachesOutbox()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-n4-gw-cfd-" + Guid.NewGuid().ToString("N"));
        var configDir = Path.Combine(dir, "Plugins", "Neo.Plugins.L2Gateway");
        Directory.CreateDirectory(configDir);
        try
        {
            File.WriteAllText(Path.Combine(configDir, "config.json"), """
                {
                  "PluginConfiguration": {
                    "Enabled": true,
                    "MaxAutomaticRetries": 7
                  }
                }
                """);
            using var plugin = L2GatewayPlugin.CreateFromChainDirectory(dir);
            Assert.IsTrue(plugin.Settings.Enabled);
            Assert.AreEqual(7, plugin.Settings.MaxAutomaticRetries);
            Assert.IsTrue(plugin.HasPersistentOutbox);
            Assert.IsTrue(Directory.Exists(Path.Combine(
                dir, NeoHubDeployReport.RelativeGatewayOutboxStoreDir)));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void CreateFromChainDirectory_MissingConfig_FailsClosed()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-n4-gw-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.ThrowsExactly<FileNotFoundException>(
                () => L2GatewayPlugin.CreateFromChainDirectory(dir));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void OpenFromChainDirectory_OutboxRoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-n4-gw-outbox-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using (var outbox = PersistentGatewayOutbox.OpenFromChainDirectory(dir))
            {
                var recovery = outbox.Recover();
                Assert.AreEqual(0, recovery.Sealed.Count);
                Assert.IsNull(recovery.Publication);
            }
            Assert.IsTrue(Directory.Exists(Path.Combine(
                dir, NeoHubDeployReport.RelativeGatewayOutboxStoreDir)));
            // Reopen same path after dispose (RocksDB lock released).
            using var reopened = PersistentGatewayOutbox.OpenFromChainDirectory(dir);
            Assert.IsNull(reopened.Recover().Publication);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
