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
    /// L2 chain id from the wired bridge / forced-inclusion inbox
    /// (<see cref="L2BridgePlugin.ChainId"/>).
    /// </summary>
    public uint ChainId => Bridge.ChainId;

    /// <summary>Configured proof type of the wired prover host.</summary>
    public ProofType ProofType => Prover.Kind;

    /// <summary>Production DA mode of the host-supplied DA writer.</summary>
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
            BridgeAssetCount = Bridge.Registry.Count,
            MetricsEntryCount = CaptureMetricsSnapshot().TotalEntries,
            MessageOutboxL2ToL1Count = MessageOutbox?.L2ToL1Count ?? 0,
            MessageOutboxL2ToL2Count = MessageOutbox?.L2ToL2Count ?? 0,
            StagedWithdrawalCount = Bridge.WithdrawalProcessor.StagedCount,
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
    /// Register a forced-inclusion nonce for recovery without scanning L1
    /// (<see cref="RpcForcedInclusionSource.RegisterNonce"/>).
    /// </summary>
    public bool RegisterForcedInclusionNonce(ulong nonce)
        => ForcedInclusion.RegisterNonce(nonce);

    /// <summary>
    /// Drop the in-memory forced-inclusion cache
    /// (<see cref="RpcForcedInclusionSource.InvalidateCache"/>).
    /// </summary>
    public void InvalidateForcedInclusionCache()
        => ForcedInclusion.InvalidateCache();

    /// <summary>
    /// Publish batch payload to the host DA writer
    /// (<see cref="IDAWriter.PublishAsync"/>). Local DA is fully offline; public DA
    /// backends remain funded credential gates.
    /// </summary>
    public ValueTask<DAReceipt> PublishDaAsync(
        DAPublishRequest request,
        CancellationToken cancellationToken = default)
        => DaWriter.PublishAsync(request, cancellationToken);

    /// <summary>
    /// Confirm a prior DA receipt is still retrievable
    /// (<see cref="IDAWriter.IsAvailableAsync"/>).
    /// </summary>
    public ValueTask<bool> IsDaAvailableAsync(
        DAReceipt receipt,
        CancellationToken cancellationToken = default)
        => DaWriter.IsAvailableAsync(receipt, cancellationToken);

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

    /// <summary>
    /// Capture an in-process metrics snapshot without HTTP
    /// (<see cref="IMetricsSource.Snapshot"/>).
    /// </summary>
    public MetricsSnapshot CaptureMetricsSnapshot()
    {
        if (Metrics.Metrics is IMetricsSource source)
            return source.Snapshot();
        return MetricsSnapshot.Empty;
    }

    /// <summary>
    /// Render Prometheus exposition text from the current metrics sink
    /// (<see cref="PrometheusExporter.Format"/>).
    /// </summary>
    public string ExportPrometheusMetrics()
        => PrometheusExporter.Format(CaptureMetricsSnapshot());

    /// <summary>
    /// Bridge plugin asset registry (L1↔L2 mappings for deposit minting)
    /// (<see cref="L2BridgePlugin.Registry"/>).
    /// </summary>
    public AssetRegistry BridgeAssetRegistry => Bridge.Registry;

    /// <summary>
    /// Register a bridge-side asset mapping
    /// (<see cref="AssetRegistry.Register"/>).
    /// </summary>
    public void RegisterBridgeAsset(AssetMapping mapping)
        => Bridge.Registry.Register(mapping);

    /// <summary>
    /// Snapshot all bridge-side asset mappings
    /// (<see cref="AssetRegistry.Snapshot"/>).
    /// </summary>
    public IReadOnlyList<AssetMapping> SnapshotBridgeAssets()
        => Bridge.Registry.Snapshot();

    /// <summary>Count of bridge-side asset mappings.</summary>
    public int BridgeAssetCount => Bridge.Registry.Count;


    /// <summary>
    /// Bridge deposit processor (mint instruction path)
    /// (<see cref="L2BridgePlugin.DepositProcessor"/>).
    /// </summary>
    public DepositProcessor DepositProcessor => Bridge.DepositProcessor;

    /// <summary>
    /// Bridge withdrawal processor (staging + seal tree)
    /// (<see cref="L2BridgePlugin.WithdrawalProcessor"/>).
    /// </summary>
    public WithdrawalProcessor WithdrawalProcessor => Bridge.WithdrawalProcessor;

    /// <summary>
    /// Validate a SharedBridge deposit message and produce a mint instruction
    /// (<see cref="DepositProcessor.Process"/>).
    /// </summary>
    public MintInstruction ProcessDeposit(CrossChainMessage message)
        => Bridge.DepositProcessor.Process(message);

    /// <summary>
    /// True when the deposit processor has already consumed
    /// (<paramref name="sourceChainId"/>, <paramref name="nonce"/>).
    /// </summary>
    public bool HasConsumedDeposit(uint sourceChainId, ulong nonce)
        => Bridge.DepositProcessor.HasConsumed(sourceChainId, nonce);

    /// <summary>
    /// Stage a withdrawal into the current batch tree
    /// (<see cref="WithdrawalProcessor.Stage"/>).
    /// </summary>
    public UInt256 StageWithdrawal(WithdrawalRequest request)
        => Bridge.WithdrawalProcessor.Stage(request);

    /// <summary>Number of withdrawals staged in the current open tree.</summary>
    public int StagedWithdrawalCount => Bridge.WithdrawalProcessor.StagedCount;

    /// <summary>
    /// Seal the staged withdrawal tree and start a fresh one
    /// (<see cref="WithdrawalProcessor.SealBatch"/>).
    /// </summary>
    public (UInt256 Root, Neo.L2.State.WithdrawalTree Tree) SealWithdrawalBatch()
        => Bridge.WithdrawalProcessor.SealBatch();

    /// <summary>
    /// Wired batch prover instance, or null when the prover plugin has not installed one.
    /// </summary>
    public IL2Prover? BatchProver => Prover.Prover;

    /// <summary>
    /// Generate a proof via the wired host prover
    /// (<see cref="IL2Prover.ProveAsync"/>). Multisig is fully offline; Zk may require
    /// funded executor/daemon resources.
    /// </summary>
    public ValueTask<ProofResult> ProveAsync(
        ProofRequest request,
        CancellationToken cancellationToken = default)
    {
        var prover = Prover.Prover
            ?? throw new InvalidOperationException("batch prover is not wired on this LocalHost");
        return prover.ProveAsync(request, cancellationToken);
    }

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
