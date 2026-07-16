using Neo.L2.Batch;
using Neo.L2.Executor.ProofWitness;
using Neo.L2.ForcedInclusion;
using Neo.L2.Messaging;
using Neo.L2.Persistence;
using Neo.L2.Settlement.Rpc;
using Neo.Network.P2P.Payloads;
using System.Net;
using System.Text;

namespace Neo.Plugins.L2Settlement.UnitTests;

[TestClass]
public class UT_L2SettlementProductionWiring
{
    private const uint ChainId = 1001;

    [TestMethod]
    public void WireProduction_ConstructsCanonicalRpcStackAndForcedPair()
    {
        using var backend = new TemporaryRocksDb();
        using var forcedEvents = new TemporaryRocksDb();
        using var store = new KeyValueProofWitnessStore(backend.Store);
        using var batch = new L2BatchPlugin();
        using var settlement = new L2SettlementPlugin(ProductionSettings());
        var signer = new TrackingSigner(Account(0x33));
        using var http = CanonicalRootHttpClient();

        var forcedSource = settlement.WireProduction(
            batch,
            new TestExecutor(),
            new TestDaWriter(),
            store,
            new TestProver(),
            ProofWitnessPipelineProfile.Legacy(ChainId, ProofType.Multisig, Root(0x11)),
            signer,
            forcedEvents.Store,
            forcedInclusionDeploymentHeight: 123,
            knownForcedInclusionNonces: new ulong[] { 3, 7 },
            rpcHttpClient: http);

        var composition = settlement.ProductionComposition;
        Assert.IsNotNull(composition);
        Assert.AreSame(forcedSource, composition.ForcedInclusionSource);
        Assert.IsInstanceOfType<RpcTransactionSender>(composition.TransactionSender);
        Assert.IsInstanceOfType<RpcSettlementClient>(composition.SettlementClient);
        Assert.IsInstanceOfType<RpcForcedInclusionFinalizationClient>(
            composition.ForcedInclusionFinalizer);
        Assert.IsInstanceOfType<RpcForcedInclusionEventScanner>(
            composition.ForcedInclusionEventScanner);
        Assert.AreEqual(new Uri("http://127.0.0.1:10332/"), composition.Rpc.Endpoint);
        Assert.AreEqual(ChainId, composition.Configuration.ChainId);
        Assert.AreEqual(860833102u, composition.Configuration.ExpectedNetwork);
        Assert.AreEqual(ChainId, forcedSource.ChainId);
    }

    [TestMethod]
    public void WireProduction_RejectsVolatileWitnessStore()
    {
        using var witnessBackend = new InMemoryKeyValueStore();
        using var forcedEvents = new TemporaryRocksDb();
        using var store = new KeyValueProofWitnessStore(witnessBackend);
        using var batch = new L2BatchPlugin();
        using var settlement = new L2SettlementPlugin(ProductionSettings());

        Assert.ThrowsExactly<InvalidOperationException>(() => settlement.WireProduction(
            batch,
            new TestExecutor(),
            new TestDaWriter(),
            store,
            new TestProver(),
            ProofWitnessPipelineProfile.Legacy(ChainId, ProofType.Multisig, Root(0x11)),
            new TrackingSigner(Account(0x34)),
            forcedEvents.Store,
            forcedInclusionDeploymentHeight: 123));
    }

    [TestMethod]
    public void WireProduction_RejectsVolatileForcedInclusionEventStore()
    {
        using var witnessBackend = new TemporaryRocksDb();
        using var forcedEvents = new InMemoryKeyValueStore();
        using var store = new KeyValueProofWitnessStore(witnessBackend.Store);
        using var batch = new L2BatchPlugin();
        using var settlement = new L2SettlementPlugin(ProductionSettings());

        Assert.ThrowsExactly<InvalidOperationException>(() => settlement.WireProduction(
            batch,
            new TestExecutor(),
            new TestDaWriter(),
            store,
            new TestProver(),
            ProofWitnessPipelineProfile.Legacy(ChainId, ProofType.Multisig, Root(0x11)),
            new TrackingSigner(Account(0x35)),
            forcedEvents,
            forcedInclusionDeploymentHeight: 123));
    }

    [TestMethod]
    public async Task Dispose_ProductionStackDisposesOwnedRpcButNotCallerSigner()
    {
        using var backend = new TemporaryRocksDb();
        using var forcedEvents = new TemporaryRocksDb();
        using var store = new KeyValueProofWitnessStore(backend.Store);
        using var batch = new L2BatchPlugin();
        var settlement = new L2SettlementPlugin(ProductionSettings());
        var signer = new TrackingSigner(Account(0x44));
        using var http = CanonicalRootHttpClient();
        settlement.WireProduction(
            batch,
            new TestExecutor(),
            new TestDaWriter(),
            store,
            new TestProver(),
            ProofWitnessPipelineProfile.Legacy(ChainId, ProofType.Multisig, Root(0x11)),
            signer,
            forcedEvents.Store,
            forcedInclusionDeploymentHeight: 123,
            rpcHttpClient: http);
        var composition = settlement.ProductionComposition;
        Assert.IsNotNull(composition);

        settlement.Dispose();
        settlement.Dispose();

        Assert.IsTrue(composition.IsDisposed);
        Assert.IsFalse(signer.IsDisposed, "signer custody remains caller-owned");
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(async () =>
            await composition.SettlementClient.GetBatchStatusAsync(ChainId, 1));
    }

    [TestMethod]
    public void WireProduction_RejectsNullOrZeroAccountSigner()
    {
        var settings = ProductionSettings();
        using var forcedEvents = new InMemoryKeyValueStore();
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            L2SettlementProductionComposition.Create(
                settings, null!, forcedEvents, 123));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            L2SettlementProductionComposition.Create(
                settings, new TrackingSigner(UInt160.Zero), forcedEvents, 123));
    }

    [TestMethod]
    public void Dispose_DiWireLeavesCallerOwnedClientAndSourceAlive()
    {
        using var backend = new InMemoryKeyValueStore();
        using var store = new KeyValueProofWitnessStore(backend);
        using var batch = new L2BatchPlugin();
        var settlement = new L2SettlementPlugin(new L2SettlementSettings
        {
            ChainId = ChainId,
            ProofType = (byte)ProofType.Multisig,
            Enabled = false,
        });
        var client = new TrackingSettlementClient();
        var source = new TrackingForcedInclusionSource();

        settlement.Wire(
            batch,
            new TestExecutor(),
            new TestDaWriter(),
            store,
            new TestProver(),
            client,
            ProofWitnessPipelineProfile.Legacy(ChainId, ProofType.Multisig, Root(0x11)),
            new NoOpForcedInclusionFinalizer(),
            source);

        settlement.Dispose();

        Assert.IsFalse(client.IsDisposed, "DI settlement client remains caller-owned");
        Assert.IsFalse(source.IsDisposed, "DI forced source remains caller-owned");
        client.Dispose();
        source.Dispose();
    }

    [TestMethod]
    public void Wire_ForcedSourceWithoutFinalizer_FailsClosed()
    {
        using var backend = new InMemoryKeyValueStore();
        using var store = new KeyValueProofWitnessStore(backend);
        using var batch = new L2BatchPlugin();
        using var settlement = new L2SettlementPlugin(new L2SettlementSettings
        {
            ChainId = ChainId,
            ProofType = (byte)ProofType.Multisig,
            Enabled = false,
        });
        using var source = new TrackingForcedInclusionSource();

        Assert.ThrowsExactly<InvalidOperationException>(() => settlement.Wire(
            batch,
            new TestExecutor(),
            new TestDaWriter(),
            store,
            new TestProver(),
            new TrackingSettlementClient(),
            ProofWitnessPipelineProfile.Legacy(ChainId, ProofType.Multisig, Root(0x11)),
            forcedInclusionSource: source));
    }

    [TestMethod]
    public void Wire_AttachesSharedBridgeDepositInboxToBatchPlugin()
    {
        // Production composition must surface deposit inbox on the batcher, not only
        // forced-inclusion. Callers own the deposit source; settlement does not dispose it.
        using var backend = new InMemoryKeyValueStore();
        using var store = new KeyValueProofWitnessStore(backend);
        using var batch = new L2BatchPlugin();
        using var settlement = new L2SettlementPlugin(new L2SettlementSettings
        {
            ChainId = ChainId,
            ProofType = (byte)ProofType.Multisig,
            Enabled = false,
        });
        var deposits = new InMemorySharedBridgeDepositSource(
            ChainId, UInt160.Parse("0x" + new string('e', 40)));
        var committee = Root(0xCC);

        settlement.Wire(
            batch,
            new TestExecutor(),
            new TestDaWriter(),
            store,
            new TestProver(),
            new TrackingSettlementClient(),
            ProofWitnessPipelineProfile.Legacy(ChainId, ProofType.Multisig, Root(0x11)),
            depositSource: deposits,
            l1FinalizedHeight: static () => 99,
            sequencerCommitteeHash: () => committee);

        Assert.AreSame(deposits, batch.DepositSource);
    }

    [TestMethod]
    public void Wire_DepositInboxWithoutBlockContextProviders_FailsClosed()
    {
        using var backend = new InMemoryKeyValueStore();
        using var store = new KeyValueProofWitnessStore(backend);
        using var batch = new L2BatchPlugin();
        using var settlement = new L2SettlementPlugin(new L2SettlementSettings
        {
            ChainId = ChainId,
            ProofType = (byte)ProofType.Multisig,
            Enabled = false,
        });
        var deposits = new InMemorySharedBridgeDepositSource(
            ChainId, UInt160.Parse("0x" + new string('e', 40)));

        Assert.ThrowsExactly<InvalidOperationException>(() => settlement.Wire(
            batch,
            new TestExecutor(),
            new TestDaWriter(),
            store,
            new TestProver(),
            new TrackingSettlementClient(),
            ProofWitnessPipelineProfile.Legacy(ChainId, ProofType.Multisig, Root(0x11)),
            depositSource: deposits));
    }

    [TestMethod]
    public void Wire_DepositChainMismatch_FailsClosed()
    {
        using var backend = new InMemoryKeyValueStore();
        using var store = new KeyValueProofWitnessStore(backend);
        using var batch = new L2BatchPlugin();
        using var settlement = new L2SettlementPlugin(new L2SettlementSettings
        {
            ChainId = ChainId,
            ProofType = (byte)ProofType.Multisig,
            Enabled = false,
        });
        var deposits = new InMemorySharedBridgeDepositSource(
            2002, UInt160.Parse("0x" + new string('e', 40)));

        Assert.ThrowsExactly<InvalidOperationException>(() => settlement.Wire(
            batch,
            new TestExecutor(),
            new TestDaWriter(),
            store,
            new TestProver(),
            new TrackingSettlementClient(),
            ProofWitnessPipelineProfile.Legacy(ChainId, ProofType.Multisig, Root(0x11)),
            depositSource: deposits,
            l1FinalizedHeight: static () => 1,
            sequencerCommitteeHash: static () => Root(0x1)));
    }

    [TestMethod]
    public void WireProduction_ConstructsOwnedDepositSourceWhenSharedBridgeConfigured()
    {
        using var backend = new TemporaryRocksDb();
        using var forcedEvents = new TemporaryRocksDb();
        using var depositEvents = new TemporaryRocksDb();
        using var store = new KeyValueProofWitnessStore(backend.Store);
        using var batch = new L2BatchPlugin();
        using var settlement = new L2SettlementPlugin(ProductionSettingsWithSharedBridge());
        var signer = new TrackingSigner(Account(0x55));
        using var http = CanonicalRootHttpClient();

        settlement.WireProduction(
            batch,
            new TestExecutor(),
            new TestDaWriter(),
            store,
            new TestProver(),
            ProofWitnessPipelineProfile.Legacy(ChainId, ProofType.Multisig, Root(0x11)),
            signer,
            forcedEvents.Store,
            forcedInclusionDeploymentHeight: 123,
            rpcHttpClient: http,
            l1FinalizedHeight: static () => 10,
            sequencerCommitteeHash: static () => Root(0xCC),
            sharedBridgeDepositEventStore: depositEvents.Store,
            sharedBridgeDeploymentHeight: 50);

        var composition = settlement.ProductionComposition;
        Assert.IsNotNull(composition);
        Assert.IsNotNull(composition.OwnedDepositSource);
        Assert.AreSame(composition.OwnedDepositSource, batch.DepositSource);
        Assert.AreEqual(ChainId, composition.OwnedDepositSource!.ChainId);
    }

    [TestMethod]
    public void WireProduction_SharedBridgeWithoutDepositStore_FailsClosed()
    {
        using var backend = new TemporaryRocksDb();
        using var forcedEvents = new TemporaryRocksDb();
        using var store = new KeyValueProofWitnessStore(backend.Store);
        using var batch = new L2BatchPlugin();
        using var settlement = new L2SettlementPlugin(ProductionSettingsWithSharedBridge());

        Assert.ThrowsExactly<InvalidOperationException>(() => settlement.WireProduction(
            batch,
            new TestExecutor(),
            new TestDaWriter(),
            store,
            new TestProver(),
            ProofWitnessPipelineProfile.Legacy(ChainId, ProofType.Multisig, Root(0x11)),
            new TrackingSigner(Account(0x56)),
            forcedEvents.Store,
            forcedInclusionDeploymentHeight: 123,
            l1FinalizedHeight: static () => 10,
            sequencerCommitteeHash: static () => Root(0xCC)));
    }

    [TestMethod]
    public void WireProduction_SharedBridgeVolatileDepositStore_FailsClosed()
    {
        using var backend = new TemporaryRocksDb();
        using var forcedEvents = new TemporaryRocksDb();
        using var depositEvents = new InMemoryKeyValueStore();
        using var store = new KeyValueProofWitnessStore(backend.Store);
        using var batch = new L2BatchPlugin();
        using var settlement = new L2SettlementPlugin(ProductionSettingsWithSharedBridge());

        Assert.ThrowsExactly<InvalidOperationException>(() => settlement.WireProduction(
            batch,
            new TestExecutor(),
            new TestDaWriter(),
            store,
            new TestProver(),
            ProofWitnessPipelineProfile.Legacy(ChainId, ProofType.Multisig, Root(0x11)),
            new TrackingSigner(Account(0x57)),
            forcedEvents.Store,
            forcedInclusionDeploymentHeight: 123,
            l1FinalizedHeight: static () => 10,
            sequencerCommitteeHash: static () => Root(0xCC),
            sharedBridgeDepositEventStore: depositEvents,
            sharedBridgeDeploymentHeight: 50));
    }

    [TestMethod]
    public void WireProduction_SharedBridgeZeroDeploymentHeight_FailsClosed()
    {
        using var backend = new TemporaryRocksDb();
        using var forcedEvents = new TemporaryRocksDb();
        using var depositEvents = new TemporaryRocksDb();
        using var store = new KeyValueProofWitnessStore(backend.Store);
        using var batch = new L2BatchPlugin();
        using var settlement = new L2SettlementPlugin(ProductionSettingsWithSharedBridge());

        Assert.ThrowsExactly<InvalidOperationException>(() => settlement.WireProduction(
            batch,
            new TestExecutor(),
            new TestDaWriter(),
            store,
            new TestProver(),
            ProofWitnessPipelineProfile.Legacy(ChainId, ProofType.Multisig, Root(0x11)),
            new TrackingSigner(Account(0x58)),
            forcedEvents.Store,
            forcedInclusionDeploymentHeight: 123,
            l1FinalizedHeight: static () => 10,
            sequencerCommitteeHash: static () => Root(0xCC),
            sharedBridgeDepositEventStore: depositEvents.Store,
            sharedBridgeDeploymentHeight: 0));
    }

    [TestMethod]
    public void WireProduction_SharedBridgeWithoutBlockContext_FailsClosed()
    {
        using var backend = new TemporaryRocksDb();
        using var forcedEvents = new TemporaryRocksDb();
        using var depositEvents = new TemporaryRocksDb();
        using var store = new KeyValueProofWitnessStore(backend.Store);
        using var batch = new L2BatchPlugin();
        using var settlement = new L2SettlementPlugin(ProductionSettingsWithSharedBridge());
        using var http = CanonicalRootHttpClient();

        Assert.ThrowsExactly<InvalidOperationException>(() => settlement.WireProduction(
            batch,
            new TestExecutor(),
            new TestDaWriter(),
            store,
            new TestProver(),
            ProofWitnessPipelineProfile.Legacy(ChainId, ProofType.Multisig, Root(0x11)),
            new TrackingSigner(Account(0x59)),
            forcedEvents.Store,
            forcedInclusionDeploymentHeight: 123,
            rpcHttpClient: http,
            sharedBridgeDepositEventStore: depositEvents.Store,
            sharedBridgeDeploymentHeight: 50));
    }

    [TestMethod]
    public void WireProduction_ExplicitDepositSource_SkipsOwnedConstruction()
    {
        using var backend = new TemporaryRocksDb();
        using var forcedEvents = new TemporaryRocksDb();
        using var store = new KeyValueProofWitnessStore(backend.Store);
        using var batch = new L2BatchPlugin();
        using var settlement = new L2SettlementPlugin(ProductionSettingsWithSharedBridge());
        using var http = CanonicalRootHttpClient();
        var explicitDeposits = new InMemorySharedBridgeDepositSource(
            ChainId, UInt160.Parse("0x" + new string('e', 40)));

        settlement.WireProduction(
            batch,
            new TestExecutor(),
            new TestDaWriter(),
            store,
            new TestProver(),
            ProofWitnessPipelineProfile.Legacy(ChainId, ProofType.Multisig, Root(0x11)),
            new TrackingSigner(Account(0x5A)),
            forcedEvents.Store,
            forcedInclusionDeploymentHeight: 123,
            rpcHttpClient: http,
            depositSource: explicitDeposits,
            l1FinalizedHeight: static () => 10,
            sequencerCommitteeHash: static () => Root(0xCC));

        var composition = settlement.ProductionComposition;
        Assert.IsNotNull(composition);
        Assert.IsNull(composition.OwnedDepositSource, "caller-owned source skips auto-construction");
        Assert.AreSame(explicitDeposits, batch.DepositSource);
    }

    [TestMethod]
    public void Dispose_ProductionStackDisposesOwnedDepositSource()
    {
        using var backend = new TemporaryRocksDb();
        using var forcedEvents = new TemporaryRocksDb();
        using var depositEvents = new TemporaryRocksDb();
        using var store = new KeyValueProofWitnessStore(backend.Store);
        using var batch = new L2BatchPlugin();
        var settlement = new L2SettlementPlugin(ProductionSettingsWithSharedBridge());
        using var http = CanonicalRootHttpClient();

        settlement.WireProduction(
            batch,
            new TestExecutor(),
            new TestDaWriter(),
            store,
            new TestProver(),
            ProofWitnessPipelineProfile.Legacy(ChainId, ProofType.Multisig, Root(0x11)),
            new TrackingSigner(Account(0x5B)),
            forcedEvents.Store,
            forcedInclusionDeploymentHeight: 123,
            rpcHttpClient: http,
            l1FinalizedHeight: static () => 10,
            sequencerCommitteeHash: static () => Root(0xCC),
            sharedBridgeDepositEventStore: depositEvents.Store,
            sharedBridgeDeploymentHeight: 50);

        var owned = settlement.ProductionComposition!.OwnedDepositSource;
        Assert.IsNotNull(owned);

        settlement.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => owned!.Peek(1));
    }

    private static L2SettlementSettings ProductionSettings() => new()
    {
        ChainId = ChainId,
        L1RpcEndpoint = "http://127.0.0.1:10332",
        ExpectedNetwork = 860833102,
        SettlementManagerHash = "0x" + new string('1', 40),
        ForcedInclusionHash = "0x" + new string('2', 40),
        ProofType = (byte)ProofType.Multisig,
        Enabled = false,
    };

    private static L2SettlementSettings ProductionSettingsWithSharedBridge() => new()
    {
        ChainId = ChainId,
        L1RpcEndpoint = "http://127.0.0.1:10332",
        ExpectedNetwork = 860833102,
        SettlementManagerHash = "0x" + new string('1', 40),
        ForcedInclusionHash = "0x" + new string('2', 40),
        SharedBridgeHash = "0x" + new string('3', 40),
        // L2BridgeHash empty → NativeContract.L2Bridge.Hash
        ProofType = (byte)ProofType.Multisig,
        Enabled = false,
    };

    private static L2SettlementSettings ProductionSettingsWithMessageRouter() => new()
    {
        ChainId = ChainId,
        L1RpcEndpoint = "http://127.0.0.1:10332",
        ExpectedNetwork = 860833102,
        SettlementManagerHash = "0x" + new string('1', 40),
        ForcedInclusionHash = "0x" + new string('2', 40),
        MessageRouterHash = "0x" + new string('4', 40),
        ProofType = (byte)ProofType.Multisig,
        Enabled = false,
    };

    [TestMethod]
    public void WireProduction_ConstructsOwnedMessageRouterWhenConfigured()
    {
        using var backend = new TemporaryRocksDb();
        using var forcedEvents = new TemporaryRocksDb();
        using var routerEvents = new TemporaryRocksDb();
        using var store = new KeyValueProofWitnessStore(backend.Store);
        using var batch = new L2BatchPlugin();
        using var settlement = new L2SettlementPlugin(ProductionSettingsWithMessageRouter());
        using var http = CanonicalRootHttpClient();

        settlement.WireProduction(
            batch,
            new TestExecutor(),
            new TestDaWriter(),
            store,
            new TestProver(),
            ProofWitnessPipelineProfile.Legacy(ChainId, ProofType.Multisig, Root(0x11)),
            new TrackingSigner(Account(0x5C)),
            forcedEvents.Store,
            forcedInclusionDeploymentHeight: 123,
            rpcHttpClient: http,
            l1FinalizedHeight: static () => 10,
            sequencerCommitteeHash: static () => Root(0xCC),
            messageRouterEventStore: routerEvents.Store,
            messageRouterDeploymentHeight: 40);

        var composition = settlement.ProductionComposition;
        Assert.IsNotNull(composition);
        Assert.IsNotNull(composition.OwnedMessageRouter);
    }

    [TestMethod]
    public void WireProduction_MessageRouterWithoutEventStore_FailsClosed()
    {
        using var backend = new TemporaryRocksDb();
        using var forcedEvents = new TemporaryRocksDb();
        using var store = new KeyValueProofWitnessStore(backend.Store);
        using var batch = new L2BatchPlugin();
        using var settlement = new L2SettlementPlugin(ProductionSettingsWithMessageRouter());

        Assert.ThrowsExactly<InvalidOperationException>(() => settlement.WireProduction(
            batch,
            new TestExecutor(),
            new TestDaWriter(),
            store,
            new TestProver(),
            ProofWitnessPipelineProfile.Legacy(ChainId, ProofType.Multisig, Root(0x11)),
            new TrackingSigner(Account(0x5D)),
            forcedEvents.Store,
            forcedInclusionDeploymentHeight: 123,
            l1FinalizedHeight: static () => 10,
            sequencerCommitteeHash: static () => Root(0xCC)));
    }

    [TestMethod]
    public void WireProduction_ExplicitMessageRouter_SkipsOwnedConstruction()
    {
        using var backend = new TemporaryRocksDb();
        using var forcedEvents = new TemporaryRocksDb();
        using var store = new KeyValueProofWitnessStore(backend.Store);
        using var batch = new L2BatchPlugin();
        using var settlement = new L2SettlementPlugin(ProductionSettingsWithMessageRouter());
        using var http = CanonicalRootHttpClient();
        using var explicitRouter = new InMemoryMessageRouter();

        settlement.WireProduction(
            batch,
            new TestExecutor(),
            new TestDaWriter(),
            store,
            new TestProver(),
            ProofWitnessPipelineProfile.Legacy(ChainId, ProofType.Multisig, Root(0x11)),
            new TrackingSigner(Account(0x5E)),
            forcedEvents.Store,
            forcedInclusionDeploymentHeight: 123,
            rpcHttpClient: http,
            messageRouter: explicitRouter,
            l1FinalizedHeight: static () => 10,
            sequencerCommitteeHash: static () => Root(0xCC));

        var composition = settlement.ProductionComposition;
        Assert.IsNotNull(composition);
        Assert.IsNull(composition.OwnedMessageRouter);
    }

    private static UInt160 Account(byte value)
    {
        var bytes = new byte[UInt160.Length];
        bytes[0] = value;
        return new UInt160(bytes);
    }

    private static UInt256 Root(byte value)
    {
        var bytes = new byte[UInt256.Length];
        bytes[0] = value;
        return new UInt256(bytes);
    }

    private sealed class TemporaryRocksDb : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(), "neo-n4-settlement-wiring-" + Guid.NewGuid().ToString("N"));

        public TemporaryRocksDb()
        {
            Store = new RocksDbKeyValueStore(_directory);
        }

        public RocksDbKeyValueStore Store { get; }

        public void Dispose()
        {
            Store.Dispose();
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class TestExecutor : IProofWitnessBatchExecutor
    {
        public ValueTask<BatchExecutionResult> ApplyBatchAsync(
            BatchExecutionRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<ProofWitnessExecutionResult> ApplyBatchWithWitnessAsync(
            SealedBatch batch,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class TestDaWriter : IDAWriter
    {
        public DAMode Mode => DAMode.External;
        public DAReceiptKind ReceiptKind => DAReceiptKind.ExternalPublication;

        public ValueTask<DAReceipt> PublishAsync(
            DAPublishRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<bool> IsAvailableAsync(
            DAReceipt receipt,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class TestProver : IL2Prover
    {
        public ProofType Kind => ProofType.Multisig;

        public ValueTask<ProofResult> ProveAsync(
            ProofRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class TrackingSigner(UInt160 account)
        : INeoTransactionSigner, IDisposable
    {
        public UInt160 Account { get; } = account;
        public WitnessScope Scope => WitnessScope.CalledByEntry;
        public bool IsDisposed { get; private set; }

        public Witness CreatePlaceholderWitness() => new()
        {
            InvocationScript = new byte[] { 0x01 },
            VerificationScript = new byte[] { 0x01 },
        };

        public ValueTask<Witness> SignAsync(
            Transaction transaction,
            uint network,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void Dispose() => IsDisposed = true;
    }

    private sealed class TrackingSettlementClient : ISettlementClient, IDisposable
    {
        public bool IsDisposed { get; private set; }

        public ValueTask<UInt256> SubmitBatchAsync(
            L2BatchCommitment commitment,
            PublicInputs publicInputs,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<UInt256> GetCanonicalStateRootAsync(
            uint chainId,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Root(0x11));

        public ValueTask<BatchStatus> GetBatchStatusAsync(
            uint chainId,
            ulong batchNumber,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void Dispose() => IsDisposed = true;
    }

    private static HttpClient CanonicalRootHttpClient()
        => new(new CanonicalRootHandler(Root(0x11)));

    private sealed class CanonicalRootHandler(UInt256 root) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rootBase64 = Convert.ToBase64String(root.GetSpan());
            var body = "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{" +
                "\"state\":\"HALT\",\"stack\":[{" +
                $"\"type\":\"ByteString\",\"value\":\"{rootBase64}\"" +
                "}]}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class TrackingForcedInclusionSource
        : IForcedInclusionSource, IDisposable
    {
        public uint ChainId => UT_L2SettlementProductionWiring.ChainId;
        public bool IsDisposed { get; private set; }

        public ValueTask<IReadOnlyList<ForcedInclusionEntry>> DrainAsync(
            int max,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<ForcedInclusionEntry>>(
                Array.Empty<ForcedInclusionEntry>());

        public ValueTask ConfirmConsumedAsync(
            ulong nonce,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask<bool> HasOverdueEntryAsync(
            uint nowUnixSeconds,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(false);

        public void Dispose() => IsDisposed = true;
    }

    private sealed class NoOpForcedInclusionFinalizer
        : IForcedInclusionFinalizationClient
    {
        public ValueTask ConsumeAndConfirmAsync(
            uint chainId,
            ulong batchNumber,
            IReadOnlyList<ForcedInclusionConsumptionProof> proofs,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }
}
