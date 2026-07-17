using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.Bridge;
using Neo.L2.ForcedInclusion;
using Neo.L2.Messaging;
using Neo.L2.Persistence;
using Neo.L2.Proving.RiscVZk;
using Neo.L2.Settlement.Rpc;
using Neo.L2.Telemetry;
using Neo.Plugins.L2Rpc;

namespace Neo.Plugins.L2;

/// <summary>
/// Zk host composition root: chain-directory plugins + durable L2 state +
/// <see cref="Sp1SettlementExecutionStack"/> +
/// <see cref="L2SettlementPlugin.WireProductionFromLayout"/> + bridge deposit source +
/// metrics + L2 RPC proof store.
/// </summary>
/// <remarks>
/// See doc.md §7.3 / §7.5 / §8 / §14.1 / §14.2. Opens Zk settlement without Neo.CLI after
/// <c>init-l2 --from-deploy-report</c> and <c>bootstrap-genesis</c>. Host-supplied:
/// reviewed <c>neo-zkvm-executor</c> path + SHA-256 pin, governance-registered verification key,
/// production <see cref="IDAWriter"/> (L1 / NeoFS — funded credentials), and L1 settlement signer.
/// The out-of-process <c>prove-batch</c> daemon that drains <c>prover/inbox</c> remains a funded
/// operator process. WireProduction leaves MessageRouter finalized-proof ownership unset so
/// <see cref="InMemoryL2RpcStore"/> can own <c>data/rpc/proofs</c>. Dispose the composition
/// (settlement first) before reopening RocksDB paths.
/// </remarks>
public sealed class ZkLocalHostComposition : IDisposable
{
    private bool _disposed;

    private ZkLocalHostComposition(
        string chainDirectory,
        L2BatchPlugin batch,
        L2SettlementPlugin settlement,
        L2SettlementStoreLayout layout,
        RocksDbKeyValueStore state,
        Sp1SettlementExecutionStack stack,
        L2ProverPlugin prover,
        IProductionDAWriter daWriter,
        RpcForcedInclusionSource forcedInclusion,
        L2BridgePlugin bridge,
        L2MetricsPlugin metrics,
        InMemoryL2RpcStore rpcStore)
    {
        ChainDirectory = chainDirectory;
        Batch = batch;
        Settlement = settlement;
        Layout = layout;
        State = state;
        Stack = stack;
        Prover = prover;
        DaWriter = daWriter;
        ForcedInclusion = forcedInclusion;
        Bridge = bridge;
        Metrics = metrics;
        RpcStore = rpcStore;
    }

    /// <summary>Absolute chain working directory.</summary>
    public string ChainDirectory { get; }

    /// <summary>Batch plugin with L1 inbox + sealed-batch sink wired.</summary>
    public L2BatchPlugin Batch { get; }

    /// <summary>Settlement plugin with production WireProduction stack.</summary>
    public L2SettlementPlugin Settlement { get; }

    /// <summary>Durable settlement RocksDB layout (dispose after Settlement).</summary>
    public L2SettlementStoreLayout Layout { get; }

    /// <summary>
    /// Durable L2 state RocksDB at <see cref="Sp1SettlementExecutionStack.RelativeStateDir"/>
    /// (owned by this composition).
    /// </summary>
    public RocksDbKeyValueStore State { get; }

    /// <summary>Bound SP1 executor, file-queue prover, and ZK pipeline profile.</summary>
    public Sp1SettlementExecutionStack Stack { get; }

    /// <summary>Zk <see cref="Sp1BatchProofProver"/> host (same instance as <see cref="Stack"/>).</summary>
    public L2ProverPlugin Prover { get; }

    /// <summary>Host-supplied production DA writer (not disposed by this composition).</summary>
    public IProductionDAWriter DaWriter { get; }

    /// <summary>Forced-inclusion source installed on the batch plugin.</summary>
    public RpcForcedInclusionSource ForcedInclusion { get; }

    /// <summary>
    /// Bridge plugin with the same SharedBridge deposit source as the batcher L1 inbox
    /// when production deposit wiring is active.
    /// </summary>
    public L2BridgePlugin Bridge { get; }

    /// <summary>Shared metrics sink host (wired onto batch + settlement + bridge).</summary>
    public L2MetricsPlugin Metrics { get; }

    /// <summary>
    /// Durable L2 RPC store under <c>data/rpc/proofs</c> (register with
    /// <c>NeoSystem.AddService</c> before <c>L2RpcPlugin</c> methods).
    /// </summary>
    public InMemoryL2RpcStore RpcStore { get; }

    /// <summary>Recover and process durable pending settlement artifacts.</summary>
    public Task ReconcileAsync(CancellationToken cancellationToken = default)
        => Settlement.ReconcileAsync(cancellationToken);

    /// <summary>Process at most one durable pending artifact (best-effort).</summary>
    public Task SubmitNextAsync(CancellationToken cancellationToken = default)
        => Settlement.SubmitNextAsync(cancellationToken);

    /// <summary>Count durable artifacts not yet finalized on L1.</summary>
    public ValueTask<int> GetPendingCountAsync(CancellationToken cancellationToken = default)
        => Settlement.GetPendingCountAsync(cancellationToken);

    /// <summary>
    /// Read durable pending / retry / poison state
    /// (<see cref="L2SettlementPlugin.GetRecoveryStatusAsync"/>).
    /// </summary>
    public ValueTask<SettlementRecoveryStatus> GetRecoveryStatusAsync(
        CancellationToken cancellationToken = default)
        => Settlement.GetRecoveryStatusAsync(cancellationToken);

    /// <summary>
    /// Reset the exact poisoned batch after operator remediation
    /// (<see cref="L2SettlementPlugin.RecoverPoisonedBatchAsync"/>).
    /// </summary>
    public Task RecoverPoisonedBatchAsync(
        ulong batchNumber,
        UInt256 artifactContentHash,
        CancellationToken cancellationToken = default)
        => Settlement.RecoverPoisonedBatchAsync(batchNumber, artifactContentHash, cancellationToken);

    /// <summary>
    /// Forced-inclusion nonces reserved in durable settlement state
    /// (<see cref="L2SettlementPlugin.GetTrackedForcedInclusionNoncesAsync"/>).
    /// </summary>
    public ValueTask<IReadOnlyCollection<ulong>> GetTrackedForcedInclusionNoncesAsync(
        uint chainId,
        CancellationToken cancellationToken = default)
        => Settlement.GetTrackedForcedInclusionNoncesAsync(chainId, cancellationToken);

    /// <summary>
    /// Latest sealed-batch checkpoint when present
    /// (<see cref="L2SettlementPlugin.GetLatestCheckpointAsync"/>).
    /// </summary>
    public ValueTask<SealedBatchCheckpoint?> GetLatestCheckpointAsync(
        CancellationToken cancellationToken = default)
        => Settlement.GetLatestCheckpointAsync(cancellationToken);

    /// <summary>
    /// Pipeline initial state root (genesis / last finalized)
    /// (<see cref="L2SettlementPlugin.GetInitialStateRootAsync"/>).
    /// </summary>
    public ValueTask<UInt256> GetInitialStateRootAsync(
        CancellationToken cancellationToken = default)
        => Settlement.GetInitialStateRootAsync(cancellationToken);

    /// <summary>
    /// Persist a sealed batch through the durable production settlement path
    /// (<see cref="L2SettlementPlugin.PersistAsync"/>).
    /// </summary>
    public ValueTask<UInt256> PersistAsync(
        SealedBatch batch,
        CancellationToken cancellationToken = default)
        => Settlement.PersistAsync(batch, cancellationToken);

    /// <summary>
    /// Backfill a sealed batch through the same durable path as
    /// <see cref="PersistAsync"/> (<see cref="L2SettlementPlugin.EnqueueAsync"/>).
    /// </summary>
    public ValueTask<UInt256> EnqueueAsync(
        SealedBatch batch,
        CancellationToken cancellationToken = default)
        => Settlement.EnqueueAsync(batch, cancellationToken);

    /// <summary>
    /// True after WireProduction installed the production composition
    /// (<see cref="L2SettlementPlugin.IsProductionWired"/>).
    /// </summary>
    public bool IsProductionWired => Settlement.IsProductionWired;

    /// <summary>
    /// Production SharedBridge deposit source after WireProduction (same instance as
    /// <see cref="L2BatchPlugin.DepositSource"/> / <see cref="L2BridgePlugin.DepositSource"/>).
    /// </summary>
    public RpcSharedBridgeDepositSource? DepositSource => Settlement.ProductionDepositSource;

    /// <summary>
    /// Production MessageRouter after WireProduction (same instance as
    /// <see cref="L2BatchPlugin.MessageRouter"/>).
    /// </summary>
    public RpcMessageRouter? MessageRouter => Settlement.ProductionMessageRouter;

    /// <summary>
    /// Production forced-inclusion finalizer after WireProduction
    /// (<see cref="L2SettlementPlugin.ProductionForcedInclusionFinalizer"/>).
    /// </summary>
    public RpcForcedInclusionFinalizationClient? ForcedInclusionFinalizer
        => Settlement.ProductionForcedInclusionFinalizer;

    /// <summary>
    /// Production settlement client after WireProduction
    /// (<see cref="L2SettlementPlugin.ProductionSettlementClient"/>).
    /// </summary>
    public RpcSettlementClient? SettlementClient => Settlement.ProductionSettlementClient;

    /// <summary>
    /// Production L1 transaction sender after WireProduction
    /// (<see cref="L2SettlementPlugin.ProductionTransactionSender"/>).
    /// </summary>
    public RpcTransactionSender? TransactionSender => Settlement.ProductionTransactionSender;

    /// <summary>
    /// Bound metrics HTTP port after <see cref="StartMetricsHttp"/> (0 when not listening).
    /// </summary>
    public int MetricsBoundPort => Metrics.BoundPort;

    /// <summary>True when the metrics HTTP server is listening.</summary>
    public bool IsMetricsHttpListening => Metrics.BoundPort > 0;

    /// <summary>
    /// Start (or re-enter) the metrics HTTP server. Idempotent.
    /// When <paramref name="readinessCheck"/> is null, uses
    /// <c>() =&gt; Batch.HasSealedBatchSink</c>.
    /// </summary>
    public void StartMetricsHttp(int? portOverride = null, Func<bool>? readinessCheck = null)
    {
        Metrics.WithReadinessCheck(readinessCheck ?? (() => Batch.HasSealedBatchSink));
        Metrics.Start(portOverride);
    }

    /// <summary>
    /// Create an <see cref="L2RpcPlugin"/> pre-wired with this host's metrics sink.
    /// Caller must <c>NeoSystem.AddService(RpcStore)</c> and register the plugin with Neo.CLI;
    /// this composition does not own the returned plugin.
    /// </summary>
    public L2RpcPlugin CreateRpcPlugin()
    {
        var plugin = new L2RpcPlugin();
        plugin.WithMetrics(Metrics.Metrics);
        return plugin;
    }

    /// <summary>
    /// Open Zk host composition from a chain directory after
    /// <c>init-l2 --from-deploy-report</c>, <c>bootstrap-genesis</c>, and Zk ProofType config.
    /// </summary>
    /// <param name="chainDirectory">Chain root with plugin configs + durable store dirs.</param>
    /// <param name="executorPath">Reviewed host-native <c>neo-zkvm-executor</c> path.</param>
    /// <param name="executorSha256">Pinned SHA-256 of that binary (funded release pin).</param>
    /// <param name="verificationKeyId">Governance-registered SP1 verification key id.</param>
    /// <param name="daWriter">
    /// Production DA writer (<see cref="IProductionDAWriter"/> — L1 / NeoFS). Validity public DA
    /// credentials remain funded; tests inject stubs that implement the production marker.
    /// </param>
    /// <param name="signer">L1 settlement transaction signer.</param>
    /// <param name="rpcHttpClient">Optional HTTP client for L1 JSON-RPC (tests inject mocks).</param>
    /// <param name="executionTimeout">Optional native executor timeout.</param>
    /// <param name="proofTimeout">Optional SP1 proof result wait timeout.</param>
    /// <param name="proofPollInterval">Optional SP1 queue poll interval.</param>
    /// <param name="startMetricsHttp">
    /// When true, starts the metrics HTTP server after wiring (<see cref="L2MetricsPlugin.Start"/>).
    /// </param>
    /// <param name="metricsPortOverride">Optional port override (use 0 for an ephemeral test port).</param>
    /// <param name="metricsReadinessCheck">
    /// Optional <c>/readyz</c> predicate. When <paramref name="startMetricsHttp"/> is true and
    /// this is null, defaults to <c>() =&gt; batch.HasSealedBatchSink</c>.
    /// </param>
    public static ZkLocalHostComposition Open(
        string chainDirectory,
        string executorPath,
        ReadOnlyMemory<byte> executorSha256,
        UInt256 verificationKeyId,
        IProductionDAWriter daWriter,
        INeoTransactionSigner signer,
        HttpClient? rpcHttpClient = null,
        TimeSpan? executionTimeout = null,
        TimeSpan? proofTimeout = null,
        TimeSpan? proofPollInterval = null,
        bool startMetricsHttp = false,
        int? metricsPortOverride = null,
        Func<bool>? metricsReadinessCheck = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(executorPath);
        ArgumentNullException.ThrowIfNull(verificationKeyId);
        ArgumentNullException.ThrowIfNull(daWriter);
        ArgumentNullException.ThrowIfNull(signer);

        var root = Path.GetFullPath(chainDirectory);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(
                $"Chain directory not found: {root}. Run neo-stack init-l2 first.");

        var settlementSettings = L2SettlementSettings.FromChainDirectory(root);
        if ((ProofType)settlementSettings.ProofType != ProofType.Zk)
        {
            throw new InvalidOperationException(
                $"ZkLocalHostComposition requires settlement ProofType.Zk; "
                + $"configured {(ProofType)settlementSettings.ProofType}");
        }

        L2BatchPlugin? batch = null;
        L2SettlementPlugin? settlement = null;
        L2SettlementStoreLayout? layout = null;
        RocksDbKeyValueStore? state = null;
        L2ProverPlugin? prover = null;
        L2BridgePlugin? bridge = null;
        L2MetricsPlugin? metrics = null;
        InMemoryL2RpcStore? rpcStore = null;
        try
        {
            batch = L2BatchPlugin.CreateFromChainDirectory(root);
            settlement = L2SettlementPlugin.CreateFromChainDirectory(root);
            layout = L2SettlementStoreLayout.Open(root);
            state = Sp1SettlementExecutionStack.OpenStateFromChainDirectory(root);
            rpcStore = InMemoryL2RpcStore.OpenFromChainDirectory(root);
            var stack = Sp1SettlementExecutionStack.CreateFromChainDirectory(
                root,
                state,
                executorPath,
                executorSha256,
                verificationKeyId,
                executionTimeout,
                proofTimeout,
                proofPollInterval);

            // Bind the same Sp1BatchProofProver instance the stack owns (one queue identity).
            prover = L2ProverPlugin.CreateFromChainDirectory(root);
            if (prover.Kind != ProofType.Zk)
            {
                throw new InvalidOperationException(
                    $"ZkLocalHostComposition requires prover ProofType.Zk; "
                    + $"configured {prover.Kind}");
            }
            prover.Wire(zkProver: stack.Prover);

            metrics = L2MetricsPlugin.CreateFromChainDirectory(root);
            batch.WithMetrics(metrics.Metrics);
            settlement.WithMetrics(metrics.Metrics);
            // Keep IProductionDAWriter for RequireProductionDA; still emit l2.da.* metrics.
            IProductionDAWriter instrumentedDa =
                new MetricsEmittingProductionDAWriter(daWriter, metrics.Metrics);

            var forced = settlement.WireProductionFromLayout(
                root,
                layout,
                batch,
                stack.Executor,
                instrumentedDa,
                prover.Prover
                    ?? throw new InvalidOperationException("Zk prover Wire did not install IL2Prover"),
                signer,
                profile: stack.Profile,
                rpcHttpClient: rpcHttpClient);

            bridge = L2BridgePlugin.CreateFromChainDirectory(root);
            bridge.WithMetrics(metrics.Metrics);
            var deposits = settlement.ProductionDepositSource;
            if (deposits is not null)
                bridge.WithDepositSource(deposits);

            if (startMetricsHttp)
            {
                metrics.WithReadinessCheck(
                    metricsReadinessCheck ?? (() => batch.HasSealedBatchSink));
                metrics.Start(metricsPortOverride);
            }
            else if (metricsReadinessCheck is not null)
            {
                metrics.WithReadinessCheck(metricsReadinessCheck);
            }

            return new ZkLocalHostComposition(
                root,
                batch,
                settlement,
                layout,
                state,
                stack,
                prover,
                daWriter,
                forced,
                bridge,
                metrics,
                rpcStore);
        }
        catch
        {
            settlement?.Dispose();
            bridge?.Dispose();
            metrics?.Dispose();
            prover?.Dispose();
            batch?.Dispose();
            layout?.Dispose();
            state?.Dispose();
            rpcStore?.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Settlement owns production RPC scanners/clients that hold layout store refs.
        Settlement.Dispose();
        Bridge.Dispose();
        Metrics.Dispose();
        Prover.Dispose();
        Batch.Dispose();
        Layout.Dispose();
        State.Dispose();
        RpcStore.Dispose();
        GC.SuppressFinalize(this);
    }
}
