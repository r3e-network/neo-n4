using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.Bridge;
using Neo.L2.Executor.ProofWitness;
using Neo.L2.ForcedInclusion;
using Neo.L2.Messaging;
using Neo.L2.Proving;
using Neo.L2.Proving.Attestation;
using Neo.L2.Settlement.Rpc;
using Neo.L2.Telemetry;
using Neo.Plugins.L2Rpc;

namespace Neo.Plugins.L2;

/// <summary>
/// Multisig/local-DA host composition root: chain-directory plugins + durable layout +
/// <see cref="L2SettlementPlugin.WireProductionFromLayout"/> + bridge deposit source +
/// metrics + L2 RPC proof store.
/// </summary>
/// <remarks>
/// See doc.md §7.5 / §14.1 / §14.2. Opens Multisig settlement for TrainingWheels-style
/// operators without Neo.CLI. Executor and signer remain host-supplied (sample/custom
/// executor; <see cref="LocalKeyTransactionSigner"/> or HSM). Public DA / Zk / funded L1
/// publication are out of scope — use <c>Sp1SettlementExecutionStack</c> and Gateway
/// factories instead. WireProduction leaves MessageRouter finalized-proof ownership unset
/// so <see cref="InMemoryL2RpcStore"/> can own <c>data/rpc/proofs</c> for query durability.
/// Dispose the composition (settlement first) before reopening the same RocksDB paths.
/// </remarks>
public sealed class MultisigLocalHostComposition : IDisposable
{
    private bool _disposed;

    private MultisigLocalHostComposition(
        string chainDirectory,
        L2BatchPlugin batch,
        L2SettlementPlugin settlement,
        L2SettlementStoreLayout layout,
        L2ProverPlugin prover,
        PersistentDAWriter daWriter,
        RpcForcedInclusionSource forcedInclusion,
        L2BridgePlugin bridge,
        L2MetricsPlugin metrics,
        InMemoryL2RpcStore rpcStore)
    {
        ChainDirectory = chainDirectory;
        Batch = batch;
        Settlement = settlement;
        Layout = layout;
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

    /// <summary>Multisig <see cref="AttestationProver"/> host.</summary>
    public L2ProverPlugin Prover { get; }

    /// <summary>Local persistent DA writer under <c>data/settlement/da</c>.</summary>
    public PersistentDAWriter DaWriter { get; }

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
    /// L2 chain id from the wired bridge / forced-inclusion inbox
    /// (<see cref="L2BridgePlugin.ChainId"/>).
    /// </summary>
    public uint ChainId => Bridge.ChainId;

    /// <summary>Configured proof type of the wired prover host.</summary>
    public ProofType ProofType => Prover.Kind;

    /// <summary>Local DA mode of the wired persistent DA writer.</summary>
    public DAMode DaMode => DaWriter.Mode;

    /// <summary>
    /// True when WireProduction installed the sealed-batch sink on the batcher
    /// (<see cref="L2BatchPlugin.HasSealedBatchSink"/>).
    /// </summary>
    public bool HasSealedBatchSink => Batch.HasSealedBatchSink;

    /// <summary>
    /// Operator readiness without Neo.CLI: production WireProduction + sealed-batch sink.
    /// Matches the default LocalHost <c>/readyz</c> predicate.
    /// </summary>
    public bool IsOperatorReady => IsProductionWired && HasSealedBatchSink;

    /// <summary>
    /// Non-mutating view of ready SharedBridge deposits
    /// (<see cref="L2BridgePlugin.PeekSharedBridgeDeposits"/>).
    /// </summary>
    public IReadOnlyList<CrossChainMessage> PeekSharedBridgeDeposits(int maxMessages)
        => Bridge.PeekSharedBridgeDeposits(maxMessages);

    /// <summary>
    /// Aggregate local operator readiness (durable settlement + deposit peek + metrics)
    /// without Neo.CLI or funded L1 traffic beyond stores already opened by WireProduction.
    /// </summary>
    /// <param name="depositPeekLimit">Max deposits counted via non-mutating peek (default 64).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask<LocalHostOperatorStatus> GetOperatorStatusAsync(
        int depositPeekLimit = 64,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(depositPeekLimit);
        var pending = await GetPendingCountAsync(cancellationToken).ConfigureAwait(false);
        var recovery = await GetRecoveryStatusAsync(cancellationToken).ConfigureAwait(false);
        var tracked = await GetTrackedForcedInclusionNoncesAsync(ChainId, cancellationToken)
            .ConfigureAwait(false);
        var readyDeposits = PeekSharedBridgeDeposits(depositPeekLimit);
        return new LocalHostOperatorStatus
        {
            ChainId = ChainId,
            ProofType = ProofType,
            DaMode = DaMode,
            SecurityLevel = RpcStore.SecurityLevel,
            GatewayEnabled = RpcStore.GatewayEnabled,
            Sequencer = RpcStore.Sequencer,
            Exit = RpcStore.Exit,
            IsProductionWired = IsProductionWired,
            HasSealedBatchSink = HasSealedBatchSink,
            IsOperatorReady = IsOperatorReady,
            HasDepositSource = DepositSource is not null,
            HasMessageRouter = MessageRouter is not null,
            IsMetricsHttpListening = IsMetricsHttpListening,
            MetricsBoundPort = MetricsBoundPort,
            PendingSettlementCount = pending,
            ReadyDepositCount = readyDeposits.Count,
            LatestRpcStateRoot = GetLatestRpcStateRoot(),
            Recovery = recovery,
            TrackedForcedInclusionNonceCount = tracked.Count,
        };
    }

    /// <summary>
    /// Latest finalized L2 state root from the host RPC store
    /// (<see cref="InMemoryL2RpcStore.GetLatestStateRoot"/>).
    /// </summary>
    public UInt256 GetLatestRpcStateRoot() => RpcStore.GetLatestStateRoot();

    /// <summary>
    /// Record a sealed batch into the host RPC store for L2 RPC without Neo.CLI
    /// (<see cref="InMemoryL2RpcStore.AddBatch"/>).
    /// </summary>
    public void AddRpcBatch(L2BatchCommitment commitment, BatchStatus status)
        => RpcStore.AddBatch(commitment, status);

    /// <summary>
    /// Mark a batch finalized in the host RPC store
    /// (<see cref="InMemoryL2RpcStore.Finalize"/>).
    /// </summary>
    public void FinalizeRpcBatch(ulong batchNumber) => RpcStore.Finalize(batchNumber);

    /// <summary>
    /// Record an L1 deposit status into the host RPC store
    /// (<see cref="InMemoryL2RpcStore.RecordDeposit"/>).
    /// </summary>
    public void RecordRpcDeposit(DepositStatus status) => RpcStore.RecordDeposit(status);

    /// <summary>
    /// Query L1 deposit status from the host RPC store
    /// (<see cref="InMemoryL2RpcStore.GetL1DepositStatus"/>).
    /// </summary>
    public DepositStatus? GetRpcL1DepositStatus(uint sourceChainId, ulong nonce)
        => RpcStore.GetL1DepositStatus(sourceChainId, nonce);

    /// <summary>
    /// Query a batch commitment from the host RPC store
    /// (<see cref="InMemoryL2RpcStore.GetBatch"/>).
    /// </summary>
    public L2BatchCommitment? GetRpcBatch(ulong batchNumber) => RpcStore.GetBatch(batchNumber);

    /// <summary>
    /// Query batch status from the host RPC store
    /// (<see cref="InMemoryL2RpcStore.GetBatchStatus"/>).
    /// </summary>
    public BatchStatus GetRpcBatchStatus(ulong batchNumber) => RpcStore.GetBatchStatus(batchNumber);

    /// <summary>
    /// Register an L1↔L2 asset mapping in the host RPC store
    /// (<see cref="InMemoryL2RpcStore.RegisterAsset"/>).
    /// </summary>
    public void RegisterRpcAsset(UInt160 l1Asset, UInt160 l2Asset)
        => RpcStore.RegisterAsset(l1Asset, l2Asset);

    /// <summary>
    /// Resolve L1 asset for an L2 token from the host RPC store
    /// (<see cref="InMemoryL2RpcStore.GetCanonicalAsset"/>).
    /// </summary>
    public UInt160? GetRpcCanonicalAsset(UInt160 l2Asset) => RpcStore.GetCanonicalAsset(l2Asset);

    /// <summary>
    /// Resolve L2 token for an L1 asset from the host RPC store
    /// (<see cref="InMemoryL2RpcStore.GetBridgedAsset"/>).
    /// </summary>
    public UInt160? GetRpcBridgedAsset(UInt160 l1Asset) => RpcStore.GetBridgedAsset(l1Asset);

    /// <summary>
    /// Record a withdrawal inclusion proof in the host RPC store
    /// (<see cref="InMemoryL2RpcStore.RecordWithdrawalProof"/>).
    /// </summary>
    public void RecordRpcWithdrawalProof(UInt256 leafHash, byte[] proofBytes)
        => RpcStore.RecordWithdrawalProof(leafHash, proofBytes);

    /// <summary>
    /// Record a message inclusion proof in the host RPC store
    /// (<see cref="InMemoryL2RpcStore.RecordMessageProof"/>).
    /// </summary>
    public void RecordRpcMessageProof(UInt256 messageHash, byte[] proofBytes)
        => RpcStore.RecordMessageProof(messageHash, proofBytes);

    /// <summary>
    /// Query a withdrawal inclusion proof from the host RPC store
    /// (<see cref="InMemoryL2RpcStore.GetWithdrawalProof"/>).
    /// </summary>
    public ReadOnlyMemory<byte>? GetRpcWithdrawalProof(UInt256 leafHash)
        => RpcStore.GetWithdrawalProof(leafHash);

    /// <summary>
    /// Query a message inclusion proof from the host RPC store
    /// (<see cref="InMemoryL2RpcStore.GetMessageProof"/>).
    /// </summary>
    public ReadOnlyMemory<byte>? GetRpcMessageProof(UInt256 messageHash)
        => RpcStore.GetMessageProof(messageHash);

    /// <summary>
    /// Local MessageRouter outbox when WireProduction installed a router; null otherwise
    /// (<see cref="RpcMessageRouter.Outbox"/>).
    /// </summary>
    public L2Outbox? MessageOutbox => MessageRouter?.Outbox;

    /// <summary>
    /// Enqueue outbound cross-chain messages into the wired MessageRouter outbox
    /// (<see cref="RpcMessageRouter.EnqueueOutboundAsync"/>).
    /// </summary>
    public ValueTask EnqueueOutboundMessagesAsync(
        IReadOnlyList<CrossChainMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var router = MessageRouter
            ?? throw new InvalidOperationException("MessageRouter is not wired on this LocalHost");
        return router.EnqueueOutboundAsync(messages, cancellationToken);
    }

    /// <summary>
    /// Record a finalized message proof on the wired MessageRouter
    /// (<see cref="RpcMessageRouter.RecordFinalizedProof"/>).
    /// </summary>
    public void RecordMessageRouterFinalizedProof(UInt256 messageHash, ReadOnlyMemory<byte> proofBytes)
    {
        var router = MessageRouter
            ?? throw new InvalidOperationException("MessageRouter is not wired on this LocalHost");
        router.RecordFinalizedProof(messageHash, proofBytes);
    }

    /// <summary>
    /// Query a finalized message proof from the wired MessageRouter store
    /// (<see cref="RpcMessageRouter.GetMessageProofAsync"/>). Returns null when unwired.
    /// </summary>
    public ValueTask<ReadOnlyMemory<byte>?> GetMessageRouterProofAsync(
        UInt256 messageHash,
        CancellationToken cancellationToken = default)
    {
        if (MessageRouter is null)
            return ValueTask.FromResult<ReadOnlyMemory<byte>?>(null);
        return MessageRouter.GetMessageProofAsync(messageHash, cancellationToken);
    }

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
    /// Open Multisig host composition from a chain directory after
    /// <c>init-l2 --from-deploy-report</c> (and Multisig ProofType in settlement/prover config).
    /// </summary>
    /// <param name="chainDirectory">Chain root with plugin configs + durable store dirs.</param>
    /// <param name="executor">Batch executor (sample / custom; not SP1-native).</param>
    /// <param name="signers">Committee <see cref="ISignerSet"/> for attestation proofs.</param>
    /// <param name="signer">L1 settlement transaction signer.</param>
    /// <param name="rpcHttpClient">Optional HTTP client for L1 JSON-RPC (tests inject mocks).</param>
    /// <param name="startMetricsHttp">
    /// When true, starts the metrics HTTP server after wiring (<see cref="L2MetricsPlugin.Start"/>).
    /// </param>
    /// <param name="metricsPortOverride">Optional port override (use 0 for an ephemeral test port).</param>
    /// <param name="metricsReadinessCheck">
    /// Optional <c>/readyz</c> predicate. When <paramref name="startMetricsHttp"/> is true and
    /// this is null, defaults to <c>() =&gt; batch.HasSealedBatchSink</c>.
    /// </param>
    public static MultisigLocalHostComposition Open(
        string chainDirectory,
        IProofWitnessBatchExecutor executor,
        ISignerSet signers,
        INeoTransactionSigner signer,
        HttpClient? rpcHttpClient = null,
        bool startMetricsHttp = false,
        int? metricsPortOverride = null,
        Func<bool>? metricsReadinessCheck = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainDirectory);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(signers);
        ArgumentNullException.ThrowIfNull(signer);

        var root = Path.GetFullPath(chainDirectory);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(
                $"Chain directory not found: {root}. Run neo-stack init-l2 first.");

        var settlementSettings = L2SettlementSettings.FromChainDirectory(root);
        if ((ProofType)settlementSettings.ProofType != ProofType.Multisig)
        {
            throw new InvalidOperationException(
                $"MultisigLocalHostComposition requires settlement ProofType.Multisig; "
                + $"configured {(ProofType)settlementSettings.ProofType}");
        }

        L2BatchPlugin? batch = null;
        L2SettlementPlugin? settlement = null;
        L2SettlementStoreLayout? layout = null;
        L2ProverPlugin? prover = null;
        PersistentDAWriter? daWriter = null;
        L2BridgePlugin? bridge = null;
        L2MetricsPlugin? metrics = null;
        InMemoryL2RpcStore? rpcStore = null;
        try
        {
            batch = L2BatchPlugin.CreateFromChainDirectory(root);
            settlement = L2SettlementPlugin.CreateFromChainDirectory(root);
            layout = L2SettlementStoreLayout.Open(root);
            daWriter = PersistentDAWriter.OpenLocalFromChainDirectory(root);
            // Own data/rpc/proofs before WireProduction; MessageRouter leaves finalized null.
            rpcStore = InMemoryL2RpcStore.OpenFromChainDirectory(root);
            prover = L2ProverPlugin.CreateMultisigWiredFromChainDirectory(root, signers);
            metrics = L2MetricsPlugin.CreateFromChainDirectory(root);
            batch.WithMetrics(metrics.Metrics);
            settlement.WithMetrics(metrics.Metrics);
            // Local DA publishes through the same l2.da.* counters as L2DAPlugin.WithMetrics.
            IDAWriter instrumentedDa = new MetricsEmittingDAWriter(daWriter, metrics.Metrics);

            var forced = settlement.WireProductionFromLayout(
                root,
                layout,
                batch,
                executor,
                instrumentedDa,
                prover.Prover
                    ?? throw new InvalidOperationException("Multisig prover Wire did not install IL2Prover"),
                signer,
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

            return new MultisigLocalHostComposition(
                root,
                batch,
                settlement,
                layout,
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
            daWriter?.Dispose();
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
        DaWriter.Dispose();
        RpcStore.Dispose();
        GC.SuppressFinalize(this);
    }
}
