using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.Executor.ProofWitness;
using Neo.L2.ForcedInclusion;
using Neo.L2.Persistence;
using Neo.L2.Settlement.Rpc;
using Neo.L2.Telemetry;

namespace Neo.Plugins.L2;

/// <summary>
/// Hosts the canonical durable execution, DA, witness, proving, and settlement pipeline.
/// </summary>
/// <remarks>
/// See doc.md §7.2, §7.5, §8, and §15.1. Committed witness artifacts are the only queue of
/// record; this plugin never treats an in-memory <c>Queue&lt;L2BatchCommitment&gt;</c> as
/// production truth.
/// </remarks>
public sealed class L2SettlementPlugin : Plugin, ISealedBatchSink
{
    private L2SettlementSettings _settings = new();
    private L2BatchPlugin? _batchPlugin;
    private CanonicalSettlementPipeline? _pipeline;
    private L2SettlementProductionComposition? _productionComposition;
    private IL2Metrics _metrics = NoOpMetrics.Instance;

    /// <summary>Construct the plugin and load its operator configuration.</summary>
    public L2SettlementPlugin()
    {
    }

    internal L2SettlementPlugin(L2SettlementSettings settings) : this()
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    /// <inheritdoc />
    public override string Name => "L2SettlementPlugin";

    /// <inheritdoc />
    public override string Description =>
        "Persists canonical proof witnesses and reconciles durable L2 settlement.";

    /// <inheritdoc />
    protected override void Configure()
    {
        _settings = L2SettlementSettings.From(GetConfiguration());
    }

    /// <summary>
    /// Wire every required production seam and begin restart reconciliation from the witness store.
    /// </summary>
    public void Wire(
        L2BatchPlugin batchPlugin,
        IProofWitnessBatchExecutor executor,
        IDAWriter daWriter,
        IProofWitnessStore store,
        IL2Prover prover,
        ISettlementClient client,
        ProofWitnessPipelineProfile profile,
        IForcedInclusionFinalizationClient? forcedInclusionFinalizer = null,
        IForcedInclusionSource? forcedInclusionSource = null,
        int? maxAutomaticRetries = null)
    {
        ArgumentNullException.ThrowIfNull(batchPlugin);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(daWriter);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(prover);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(profile);
        if (_pipeline is not null)
            throw new InvalidOperationException("settlement pipeline is already wired");
        if (_settings.ChainId != 0
            && (_settings.ChainId != profile.ChainId
                || (ProofType)_settings.ProofType != profile.ProofType))
            throw new InvalidOperationException(
                "plugin settings differ from the canonical pipeline profile");
        if (forcedInclusionSource is not null)
        {
            if (forcedInclusionFinalizer is null)
                throw new InvalidOperationException(
                    "forced-inclusion source requires an L1 finalization client");
            if (forcedInclusionSource.ChainId != profile.ChainId)
                throw new InvalidOperationException(
                    "forced-inclusion source chain differs from the pipeline profile");
        }

        var pipeline = new CanonicalSettlementPipeline(
            executor,
            daWriter,
            store,
            prover,
            client,
            profile,
            _metrics,
            forcedInclusionFinalizer,
            maxAutomaticRetries ?? 3);
        _pipeline = pipeline;
        try
        {
            if (forcedInclusionSource is not null)
                batchPlugin.WithForcedInclusionSource(forcedInclusionSource);
            batchPlugin.WithSealedBatchSink(this, profile.ChainId);
        }
        catch
        {
            _pipeline = null;
            pipeline.Dispose();
            throw;
        }
        _batchPlugin = batchPlugin;
        if (_settings.Enabled) _ = ReconcileSafelyAsync();
    }

    /// <summary>
    /// Build and own the canonical production L1 RPC settlement stack, then wire the durable
    /// pipeline through the same dependency-injection path as <see cref="Wire"/>.
    /// </summary>
    /// <remarks>
    /// See doc.md §3.2, §7.5, §14.2, and §15.4. The caller retains ownership of
    /// <paramref name="signer"/> and all explicitly supplied execution, DA, store, prover, and
    /// batch-plugin dependencies, including <paramref name="forcedInclusionEventStore"/>. This
    /// plugin owns only the RPC client, transaction sender, settlement client, forced-inclusion
    /// scanner/finalizer, and forced-inclusion source it constructs.
    /// </remarks>
    /// <returns>
    /// The owned RPC forced-inclusion source. Its durable scanner discovers finalized L1 enqueue
    /// events automatically; <see cref="RpcForcedInclusionSource.RegisterNonce"/> remains a
    /// migration/recovery hook rather than the production discovery path.
    /// </returns>
    public RpcForcedInclusionSource WireProduction(
        L2BatchPlugin batchPlugin,
        IProofWitnessBatchExecutor executor,
        IDAWriter daWriter,
        IProofWitnessStore store,
        IL2Prover prover,
        ProofWitnessPipelineProfile profile,
        INeoTransactionSigner signer,
        IL2KeyValueStore forcedInclusionEventStore,
        uint forcedInclusionDeploymentHeight,
        uint forcedInclusionFinalityDepth = 1,
        int forcedInclusionMaximumBlocksPerScan = 256,
        IEnumerable<ulong>? knownForcedInclusionNonces = null,
        int? maxAutomaticRetries = null,
        HttpClient? rpcHttpClient = null)
    {
        ArgumentNullException.ThrowIfNull(batchPlugin);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(daWriter);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(prover);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(signer);
        ArgumentNullException.ThrowIfNull(forcedInclusionEventStore);
        if (!store.IsDurable)
            throw new InvalidOperationException(
                "production settlement requires a durable proof witness store");
        if (forcedInclusionEventStore is not IDurableL2KeyValueStore)
            throw new InvalidOperationException(
                "production settlement requires a durable forced-inclusion event store");
        if (_pipeline is not null || _productionComposition is not null)
            throw new InvalidOperationException("settlement pipeline is already wired");

        var composition = L2SettlementProductionComposition.Create(
            _settings,
            signer,
            forcedInclusionEventStore,
            forcedInclusionDeploymentHeight,
            forcedInclusionFinalityDepth,
            forcedInclusionMaximumBlocksPerScan,
            knownForcedInclusionNonces,
            rpcHttpClient);
        try
        {
            Wire(
                batchPlugin,
                executor,
                daWriter,
                store,
                prover,
                composition.SettlementClient,
                profile,
                composition.ForcedInclusionFinalizer,
                composition.ForcedInclusionSource,
                maxAutomaticRetries);
            _productionComposition = composition;
            return composition.ForcedInclusionSource;
        }
        catch
        {
            composition.Dispose();
            throw;
        }
    }

    internal L2SettlementProductionComposition? ProductionComposition =>
        _productionComposition;

    /// <summary>Wire a telemetry sink without changing durable pipeline state.</summary>
    public void WithMetrics(IL2Metrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        _metrics = metrics;
        _pipeline?.WithMetrics(metrics);
    }

    /// <inheritdoc />
    public async ValueTask<UInt256> PersistAsync(
        SealedBatch batch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (!_settings.Enabled)
            throw new InvalidOperationException("settlement plugin is disabled");
        var pipeline = _pipeline
            ?? throw new InvalidOperationException("settlement pipeline is not wired");
        var postStateRoot = await pipeline.PersistAsync(batch, cancellationToken)
            .ConfigureAwait(false);
        _ = ReconcileSafelyAsync();
        return postStateRoot;
    }

    /// <summary>Backfill a sealed batch through the same durable production path.</summary>
    public ValueTask<UInt256> EnqueueAsync(
        SealedBatch batch,
        CancellationToken cancellationToken = default)
        => PersistAsync(batch, cancellationToken);

    /// <summary>Process at most one durable pending artifact, best-effort.</summary>
    public Task SubmitNextAsync(CancellationToken cancellationToken = default)
        => ReconcileSafelyAsync(maximumBatches: 1, cancellationToken);

    /// <summary>Recover and process every durable pending artifact, surfacing failures.</summary>
    public Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        var pipeline = _pipeline
            ?? throw new InvalidOperationException("settlement pipeline is not wired");
        return pipeline.ReconcileAsync(cancellationToken: cancellationToken);
    }

    /// <summary>Count durable artifacts not yet finalized on L1.</summary>
    public ValueTask<int> GetPendingCountAsync(
        CancellationToken cancellationToken = default)
    {
        var pipeline = _pipeline
            ?? throw new InvalidOperationException("settlement pipeline is not wired");
        return pipeline.GetPendingCountAsync(cancellationToken);
    }

    /// <summary>Read durable pending, retry, and poison state for operator health surfaces.</summary>
    public ValueTask<SettlementRecoveryStatus> GetRecoveryStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var pipeline = _pipeline
            ?? throw new InvalidOperationException("settlement pipeline is not wired");
        return pipeline.GetRecoveryStatusAsync(cancellationToken);
    }

    /// <summary>Reset the exact poisoned batch after operator remediation.</summary>
    public Task RecoverPoisonedBatchAsync(
        ulong batchNumber,
        UInt256 artifactContentHash,
        CancellationToken cancellationToken = default)
    {
        var pipeline = _pipeline
            ?? throw new InvalidOperationException("settlement pipeline is not wired");
        return pipeline.RecoverPoisonedBatchAsync(
            batchNumber, artifactContentHash, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyCollection<ulong>> GetTrackedForcedInclusionNoncesAsync(
        uint chainId,
        CancellationToken cancellationToken = default)
    {
        var pipeline = _pipeline
            ?? throw new InvalidOperationException("settlement pipeline is not wired");
        if (_settings.ChainId != 0 && chainId != _settings.ChainId)
            throw new InvalidOperationException(
                "forced-inclusion reservation chain differs from settlement settings");
        return pipeline.GetTrackedForcedInclusionNoncesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<SealedBatchCheckpoint?> GetLatestCheckpointAsync(
        CancellationToken cancellationToken = default)
    {
        var pipeline = _pipeline
            ?? throw new InvalidOperationException("settlement pipeline is not wired");
        return pipeline.GetLatestCheckpointAsync(cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<UInt256> GetInitialStateRootAsync(
        CancellationToken cancellationToken = default)
    {
        var pipeline = _pipeline
            ?? throw new InvalidOperationException("settlement pipeline is not wired");
        return pipeline.GetInitialStateRootAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            var productionComposition = _productionComposition;
            _productionComposition = null;
            _batchPlugin?.RemoveSealedBatchSink(this);
            _pipeline?.Dispose();
            _pipeline = null;
            _batchPlugin = null;
            productionComposition?.Dispose();
        }
        base.Dispose(disposing);
    }

    private async Task ReconcileSafelyAsync(
        int maximumBatches = int.MaxValue,
        CancellationToken cancellationToken = default)
    {
        var pipeline = _pipeline;
        if (pipeline is null || !_settings.Enabled) return;
        try
        {
            await pipeline.ReconcileAsync(maximumBatches, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException
            and not StackOverflowException
            and not OperationCanceledException)
        {
            _metrics.SafeIncrementCounter(
                MetricNames.SubmitFailures,
                1,
                ("exception", exception.GetType().Name));
        }
    }
}
