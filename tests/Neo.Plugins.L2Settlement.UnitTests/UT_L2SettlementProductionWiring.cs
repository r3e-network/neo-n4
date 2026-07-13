using Neo.L2.Batch;
using Neo.L2.Executor.ProofWitness;
using Neo.L2.ForcedInclusion;
using Neo.L2.Persistence;
using Neo.L2.Settlement.Rpc;
using Neo.Network.P2P.Payloads;

namespace Neo.Plugins.L2Settlement.UnitTests;

[TestClass]
public class UT_L2SettlementProductionWiring
{
    private const uint ChainId = 1001;

    [TestMethod]
    public void WireProduction_ConstructsCanonicalRpcStackAndForcedPair()
    {
        using var backend = new InMemoryKeyValueStore();
        using var store = new KeyValueProofWitnessStore(backend);
        using var batch = new L2BatchPlugin();
        using var settlement = new L2SettlementPlugin(ProductionSettings());
        var signer = new TrackingSigner(Account(0x33));

        var forcedSource = settlement.WireProduction(
            batch,
            new TestExecutor(),
            new TestDaWriter(),
            store,
            new TestProver(),
            ProofWitnessPipelineProfile.Legacy(ChainId, ProofType.Multisig),
            signer,
            new ulong[] { 3, 7 });

        var composition = settlement.ProductionComposition;
        Assert.IsNotNull(composition);
        Assert.AreSame(forcedSource, composition.ForcedInclusionSource);
        Assert.IsInstanceOfType<RpcTransactionSender>(composition.TransactionSender);
        Assert.IsInstanceOfType<RpcSettlementClient>(composition.SettlementClient);
        Assert.IsInstanceOfType<RpcForcedInclusionFinalizationClient>(
            composition.ForcedInclusionFinalizer);
        Assert.AreEqual(new Uri("http://127.0.0.1:10332/"), composition.Rpc.Endpoint);
        Assert.AreEqual(ChainId, composition.Configuration.ChainId);
        Assert.AreEqual(860833102u, composition.Configuration.ExpectedNetwork);
        Assert.AreEqual(ChainId, forcedSource.ChainId);
    }

    [TestMethod]
    public async Task Dispose_ProductionStackDisposesOwnedRpcButNotCallerSigner()
    {
        using var backend = new InMemoryKeyValueStore();
        using var store = new KeyValueProofWitnessStore(backend);
        using var batch = new L2BatchPlugin();
        var settlement = new L2SettlementPlugin(ProductionSettings());
        var signer = new TrackingSigner(Account(0x44));
        settlement.WireProduction(
            batch,
            new TestExecutor(),
            new TestDaWriter(),
            store,
            new TestProver(),
            ProofWitnessPipelineProfile.Legacy(ChainId, ProofType.Multisig),
            signer,
            Array.Empty<ulong>());
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
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            L2SettlementProductionComposition.Create(
                settings, null!, Array.Empty<ulong>()));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            L2SettlementProductionComposition.Create(
                settings, new TrackingSigner(UInt160.Zero), Array.Empty<ulong>()));
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
            ProofWitnessPipelineProfile.Legacy(ChainId, ProofType.Multisig),
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
            ProofWitnessPipelineProfile.Legacy(ChainId, ProofType.Multisig),
            forcedInclusionSource: source));
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

    private static UInt160 Account(byte value)
    {
        var bytes = new byte[UInt160.Length];
        bytes[0] = value;
        return new UInt160(bytes);
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
            => throw new NotSupportedException();

        public ValueTask<BatchStatus> GetBatchStatusAsync(
            uint chainId,
            ulong batchNumber,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void Dispose() => IsDisposed = true;
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
