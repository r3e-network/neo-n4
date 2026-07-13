using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.Executor.ProofWitness;
using Neo.L2.ForcedInclusion;
using Neo.L2.Persistence;
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
    private IL2Metrics _metrics = NoOpMetrics.Instance;

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
        IForcedInclusionSource? forcedInclusionSource = null)
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
            batchPlugin.WithForcedInclusionSource(forcedInclusionSource);
        }

        _pipeline = new CanonicalSettlementPipeline(
            executor,
            daWriter,
            store,
            prover,
            client,
            profile,
            _metrics,
            forcedInclusionFinalizer);
        _batchPlugin = batchPlugin;
        batchPlugin.WithSealedBatchSink(this);
        if (_settings.Enabled) _ = ReconcileSafelyAsync();
    }

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

    /// <summary>Count durable artifacts not yet observed on L1.</summary>
    public ValueTask<int> GetPendingCountAsync(
        CancellationToken cancellationToken = default)
    {
        var pipeline = _pipeline
            ?? throw new InvalidOperationException("settlement pipeline is not wired");
        return pipeline.GetPendingCountAsync(cancellationToken);
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
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _batchPlugin?.RemoveSealedBatchSink(this);
            _pipeline?.Dispose();
            _pipeline = null;
            _batchPlugin = null;
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
