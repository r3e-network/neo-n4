using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.Bridge;
using Neo.L2.Executor.ProofWitness;
using Neo.L2.ForcedInclusion;
using Neo.L2.Messaging;
using Neo.L2.Persistence;
using Neo.L2.Sequencer;
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

    /// <summary>
    /// Host composition factory: preload settings from a chain working directory
    /// (<c>init-l2</c> / <c>--from-deploy-report</c>) without the Neo plugin config loader.
    /// </summary>
    /// <remarks>
    /// Pair with <see cref="L2SettlementStoreLayout.Open"/> and
    /// <see cref="WireProductionFromLayout"/> for Multisig/Optimistic local hosts.
    /// Zk hosts still supply Sp1 executor/DA/prover + explicit profile.
    /// </remarks>
    public static L2SettlementPlugin CreateFromChainDirectory(string chainDirectory)
        => new(L2SettlementSettings.FromChainDirectory(chainDirectory));

    /// <summary>
    /// Host composition: build an owned <see cref="RpcSharedBridgeDepositSource"/> from
    /// settlement plugin config + durable store under
    /// <c>data/settlement/shared-bridge-deposits</c>.
    /// </summary>
    /// <remarks>
    /// Requires <c>SharedBridgeHash</c>, <c>SharedBridgeDeploymentHeight</c>,
    /// <c>L1RpcEndpoint</c>, and non-zero <c>ChainId</c> in settlement config (as written by
    /// deploy-report materialization). Empty <c>L2BridgeHash</c> resolves to native
    /// <c>L2Bridge</c>. Pass the result to <c>L2BridgePlugin.WithDepositSource</c> and/or
    /// <c>L2BatchPlugin.WireL1MessageInbox</c>. Caller owns disposal (source owns RPC + store).
    /// </remarks>
    public static RpcSharedBridgeDepositSource CreateDepositSourceFromChainDirectory(
        string chainDirectory)
    {
        var settings = L2SettlementSettings.FromChainDirectory(chainDirectory);
        var production = settings.ValidateProduction();
        if (production.SharedBridgeHash is null || production.L2BridgeHash is null)
            throw new InvalidOperationException(
                "CreateDepositSourceFromChainDirectory requires SharedBridgeHash "
                + "(and optional L2BridgeHash) in settlement plugin config");
        if (settings.SharedBridgeDeploymentHeight == 0)
            throw new InvalidOperationException(
                "CreateDepositSourceFromChainDirectory requires SharedBridgeDeploymentHeight "
                + "in settlement plugin config (deploy-report blockIndex)");

        var rpc = new JsonRpcClient(production.RpcEndpoint.AbsoluteUri);
        return RpcSharedBridgeDepositSource.OpenFromChainDirectory(
            chainDirectory,
            rpc,
            production.SharedBridgeHash,
            production.ChainId,
            production.L2BridgeHash,
            settings.SharedBridgeDeploymentHeight,
            settings.L1FinalityDepth,
            ownsRpc: true);
    }

    /// <summary>
    /// Host composition: owned <see cref="RpcForcedInclusionSource"/> from settlement config
    /// + durable store under <c>data/settlement/forced-inclusion-events</c>.
    /// </summary>
    public static RpcForcedInclusionSource CreateForcedInclusionSourceFromChainDirectory(
        string chainDirectory)
    {
        var settings = L2SettlementSettings.FromChainDirectory(chainDirectory);
        var production = settings.ValidateProduction();
        if (settings.ForcedInclusionDeploymentHeight == 0)
            throw new InvalidOperationException(
                "CreateForcedInclusionSourceFromChainDirectory requires ForcedInclusionDeploymentHeight "
                + "in settlement plugin config (deploy-report blockIndex)");

        var rpc = new JsonRpcClient(production.RpcEndpoint.AbsoluteUri);
        return RpcForcedInclusionSource.OpenFromChainDirectory(
            chainDirectory,
            rpc,
            production.ForcedInclusionHash,
            production.ChainId,
            settings.ForcedInclusionDeploymentHeight,
            settings.L1FinalityDepth,
            ownsRpc: true);
    }

    /// <summary>
    /// Host composition: owned <see cref="RpcMessageRouter"/> from settlement config + durable
    /// L1→L2 event store under <c>data/settlement/message-router-events</c> (and optional
    /// finalized proofs under <c>data/rpc/proofs</c>).
    /// </summary>
    public static RpcMessageRouter CreateMessageRouterFromChainDirectory(
        string chainDirectory,
        bool openFinalizedProofStore = true)
    {
        var settings = L2SettlementSettings.FromChainDirectory(chainDirectory);
        var production = settings.ValidateProduction();
        if (production.MessageRouterHash is null)
            throw new InvalidOperationException(
                "CreateMessageRouterFromChainDirectory requires MessageRouterHash "
                + "in settlement plugin config");
        if (settings.MessageRouterDeploymentHeight == 0)
            throw new InvalidOperationException(
                "CreateMessageRouterFromChainDirectory requires MessageRouterDeploymentHeight "
                + "in settlement plugin config (deploy-report blockIndex)");

        var rpc = new JsonRpcClient(production.RpcEndpoint.AbsoluteUri);
        return RpcMessageRouter.OpenFromChainDirectory(
            chainDirectory,
            rpc,
            production.MessageRouterHash,
            production.ChainId,
            settings.MessageRouterDeploymentHeight,
            settings.L1FinalityDepth,
            openFinalizedProofStore: openFinalizedProofStore,
            ownsRpc: true);
    }

    /// <summary>
    /// Host composition: open deposit + ForcedInclusion + MessageRouter with one L1 RPC
    /// client and wire them onto <paramref name="batchPlugin"/>.
    /// </summary>
    /// <remarks>
    /// Prefer this over three independent <c>Create*FromChainDirectory</c> calls.
    /// Caller must dispose the returned <see cref="L1InboxFromChainDirectory"/> after the
    /// batch plugin (and any bridge plugin that holds the deposit source) are disposed.
    /// </remarks>
    public static L1InboxFromChainDirectory WireL1InboxFromChainDirectory(
        string chainDirectory,
        L2BatchPlugin batchPlugin,
        bool openFinalizedProofStore = true)
    {
        ArgumentNullException.ThrowIfNull(batchPlugin);
        var inbox = L1InboxFromChainDirectory.Open(chainDirectory, openFinalizedProofStore);
        try
        {
            inbox.WireBatch(batchPlugin);
            return inbox;
        }
        catch
        {
            inbox.Dispose();
            throw;
        }
    }

    /// <summary>Loaded settlement settings (host composition / tests).</summary>
    internal L2SettlementSettings Settings => _settings;

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
    /// <remarks>
    /// SharedBridge deposits and MessageRouter traffic are optional but, when either is supplied,
    /// <paramref name="l1FinalizedHeight"/> and <paramref name="sequencerCommitteeHash"/> are
    /// required and are installed on the batcher via <see cref="L2BatchPlugin.WireL1MessageInbox"/>
    /// before the sealed-batch sink is attached. Deposit confirm/release remains owned by the batcher.
    /// </remarks>
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
        ISharedBridgeDepositSource? depositSource = null,
        IMessageRouter? messageRouter = null,
        Func<uint>? l1FinalizedHeight = null,
        Func<UInt256>? sequencerCommitteeHash = null,
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
        if (depositSource is not null && depositSource.ChainId != profile.ChainId)
            throw new InvalidOperationException(
                "deposit source chain differs from the pipeline profile");
        if ((depositSource is not null || messageRouter is not null)
            && (l1FinalizedHeight is null || sequencerCommitteeHash is null))
            throw new InvalidOperationException(
                "L1 message inbox wiring requires l1FinalizedHeight and sequencerCommitteeHash providers");

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
            // Order: L1 inbox first (drain composition), then forced inclusion, then durable sink
            // (creates the sealer that captures the drain).
            if (depositSource is not null || messageRouter is not null)
            {
                batchPlugin.WireL1MessageInbox(
                    profile.ChainId,
                    l1FinalizedHeight!,
                    sequencerCommitteeHash!,
                    deposits: depositSource,
                    messageRouter: messageRouter);
            }
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
    /// scanner/finalizer/source, and any owned SharedBridge deposit source / MessageRouter it
    /// constructs.
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
        uint forcedInclusionDeploymentHeight = 0,
        uint? forcedInclusionFinalityDepth = null,
        int forcedInclusionMaximumBlocksPerScan = 256,
        IEnumerable<ulong>? knownForcedInclusionNonces = null,
        int? maxAutomaticRetries = null,
        HttpClient? rpcHttpClient = null,
        ISharedBridgeDepositSource? depositSource = null,
        IMessageRouter? messageRouter = null,
        Func<uint>? l1FinalizedHeight = null,
        Func<UInt256>? sequencerCommitteeHash = null,
        IL2KeyValueStore? sharedBridgeDepositEventStore = null,
        uint sharedBridgeDeploymentHeight = 0,
        uint? sharedBridgeFinalityDepth = null,
        int sharedBridgeMaximumBlocksPerScan = 256,
        IL2KeyValueStore? messageRouterEventStore = null,
        uint messageRouterDeploymentHeight = 0,
        uint? messageRouterFinalityDepth = null,
        int messageRouterMaximumBlocksPerScan = 256,
        IEnumerable<ulong>? knownInboundMessageNonces = null,
        IL2KeyValueStore? messageRouterFinalizedProofStore = null)
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

        // Explicit WireProduction height args win; otherwise use plugin config (typically
        // materialized from neo-hub-deploy evidence via --from-deploy-report).
        var effectiveForcedHeight = ResolveDeploymentHeight(
            forcedInclusionDeploymentHeight,
            _settings.ForcedInclusionDeploymentHeight,
            "ForcedInclusionDeploymentHeight");
        var effectiveSharedBridgeHeight = ResolveDeploymentHeight(
            sharedBridgeDeploymentHeight,
            _settings.SharedBridgeDeploymentHeight,
            "SharedBridgeDeploymentHeight",
            required: false);
        var effectiveMessageRouterHeight = ResolveDeploymentHeight(
            messageRouterDeploymentHeight,
            _settings.MessageRouterDeploymentHeight,
            "MessageRouterDeploymentHeight",
            required: false);
        // Finality depth: explicit method args win; otherwise plugin L1FinalityDepth
        // so scanners and RpcL1FinalizedHeightSource share one config field.
        var effectiveForcedFinality = forcedInclusionFinalityDepth ?? _settings.L1FinalityDepth;
        var effectiveSharedBridgeFinality = sharedBridgeFinalityDepth ?? _settings.L1FinalityDepth;
        var effectiveMessageRouterFinality = messageRouterFinalityDepth ?? _settings.L1FinalityDepth;

        // When SharedBridgeHash / MessageRouterHash are configured and the caller does not
        // supply those sources, composition owns the RPC adapters. Explicit sources remain
        // caller-owned and skip auto-construction.
        var composition = L2SettlementProductionComposition.Create(
            _settings,
            signer,
            forcedInclusionEventStore,
            effectiveForcedHeight,
            effectiveForcedFinality,
            forcedInclusionMaximumBlocksPerScan,
            knownForcedInclusionNonces,
            rpcHttpClient,
            sharedBridgeDepositEventStore,
            effectiveSharedBridgeHeight,
            effectiveSharedBridgeFinality,
            sharedBridgeMaximumBlocksPerScan,
            constructDepositSource: depositSource is null,
            messageRouterEventStore,
            effectiveMessageRouterHeight,
            effectiveMessageRouterFinality,
            messageRouterMaximumBlocksPerScan,
            knownInboundMessageNonces,
            messageRouterFinalizedProofStore,
            constructMessageRouter: messageRouter is null);
        try
        {
            var effectiveDeposits = depositSource ?? composition.OwnedDepositSource;
            var effectiveRouter = messageRouter ?? composition.OwnedMessageRouter;
            // Default L1 tip lag from the same production RPC + L1FinalityDepth so hosts
            // do not re-type getblockcount math for inbox wiring. Committee hash still
            // needs operator-supplied keys (genesis set for RpcSequencerCommitteeProvider).
            var effectiveL1FinalizedHeight = l1FinalizedHeight;
            if ((effectiveDeposits is not null || effectiveRouter is not null)
                && effectiveL1FinalizedHeight is null)
            {
                effectiveL1FinalizedHeight = new RpcL1FinalizedHeightSource(
                    composition.Rpc,
                    effectiveForcedFinality).CreateSyncProvider();
            }
            if ((effectiveDeposits is not null || effectiveRouter is not null)
                && sequencerCommitteeHash is null)
                throw new InvalidOperationException(
                    "L1 message inbox wiring requires a sequencerCommitteeHash provider "
                    + "(SequencerCommitteeHasher.CreateSyncProvider over ISequencerCommitteeProvider)");

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
                effectiveDeposits,
                effectiveRouter,
                effectiveL1FinalizedHeight,
                sequencerCommitteeHash,
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

    /// <summary>
    /// Production composition root that binds durable stores from
    /// <see cref="L2SettlementStoreLayout"/> and the static sequencer committee hash from
    /// <c>chain.config.json</c> validators under <paramref name="chainDirectory"/>.
    /// </summary>
    /// <remarks>
    /// When <paramref name="profile"/> is omitted, uses
    /// <see cref="ProofWitnessPipelineProfile.LegacyFromChainDirectory"/> (Multisig/Optimistic).
    /// ZK hosts must pass a profile from <c>Sp1SettlementExecutionStack</c> (and matching
    /// settings ProofType). Executor, DA writer, prover, and signer remain host-supplied
    /// (Multisig local DA: <c>PersistentDAWriter.OpenLocalFromChainDirectory</c>).
    /// Layout ownership stays with the caller — dispose the layout after this plugin.
    /// </remarks>
    public RpcForcedInclusionSource WireProductionFromLayout(
        string chainDirectory,
        L2SettlementStoreLayout layout,
        L2BatchPlugin batchPlugin,
        IProofWitnessBatchExecutor executor,
        IDAWriter daWriter,
        IL2Prover prover,
        INeoTransactionSigner signer,
        ProofWitnessPipelineProfile? profile = null,
        Func<UInt256>? sequencerCommitteeHash = null,
        HttpClient? rpcHttpClient = null,
        ISharedBridgeDepositSource? depositSource = null,
        IMessageRouter? messageRouter = null,
        Func<uint>? l1FinalizedHeight = null,
        int? maxAutomaticRetries = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainDirectory);
        ArgumentNullException.ThrowIfNull(layout);
        var root = System.IO.Path.GetFullPath(chainDirectory);
        var layoutRoot = System.IO.Path.GetFullPath(layout.ChainDirectory);
        if (!string.Equals(root, layoutRoot, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"L2SettlementStoreLayout chain directory '{layoutRoot}' differs from '{root}'");

        profile ??= ProofWitnessPipelineProfile.LegacyFromChainDirectory(root);
        sequencerCommitteeHash ??= SequencerCommitteeConfig
            .CreateStaticHashProviderFromChainDirectory(root);

        return WireProduction(
            batchPlugin,
            executor,
            daWriter,
            layout.ProofWitness,
            prover,
            profile,
            signer,
            layout.ForcedInclusionEvents,
            rpcHttpClient: rpcHttpClient,
            depositSource: depositSource,
            messageRouter: messageRouter,
            l1FinalizedHeight: l1FinalizedHeight,
            sequencerCommitteeHash: sequencerCommitteeHash,
            sharedBridgeDepositEventStore: layout.SharedBridgeDeposits,
            messageRouterEventStore: layout.MessageRouterEvents,
            maxAutomaticRetries: maxAutomaticRetries);
    }

    internal L2SettlementProductionComposition? ProductionComposition =>
        _productionComposition;

    /// <summary>
    /// True after a successful <see cref="WireProduction"/> /
    /// <see cref="WireProductionFromLayout"/> that still owns the production composition.
    /// </summary>
    public bool IsProductionWired => _productionComposition is not null;

    /// <summary>
    /// Whether settlement submit/reconcile is enabled in plugin settings.
    /// </summary>
    public bool IsEnabled => _settings.Enabled;

    /// <summary>
    /// Configured L1 finality depth used by deposit / FI / MessageRouter scanners
    /// when WireProduction does not override per-scanner depths.
    /// </summary>
    public uint L1FinalityDepth => _settings.L1FinalityDepth;

    /// <summary>Configured ForcedInclusion contract deployment height (0 when unset).</summary>
    public uint ForcedInclusionDeploymentHeight => _settings.ForcedInclusionDeploymentHeight;

    /// <summary>Configured SharedBridge contract deployment height (0 when unset).</summary>
    public uint SharedBridgeDeploymentHeight => _settings.SharedBridgeDeploymentHeight;

    /// <summary>Configured MessageRouter contract deployment height (0 when unset).</summary>
    public uint MessageRouterDeploymentHeight => _settings.MessageRouterDeploymentHeight;

    /// <summary>
    /// Deposit source owned by the last successful <see cref="WireProduction"/> /
    /// <see cref="WireProductionFromLayout"/> when SharedBridge is configured; null when
    /// the caller supplied a deposit source or SharedBridge is not configured.
    /// </summary>
    public RpcSharedBridgeDepositSource? ProductionDepositSource =>
        _productionComposition?.OwnedDepositSource;

    /// <summary>
    /// Message router owned by the last successful WireProduction path when
    /// MessageRouterHash is configured; null when the caller supplied a router or it is unset.
    /// </summary>
    public RpcMessageRouter? ProductionMessageRouter =>
        _productionComposition?.OwnedMessageRouter;

    /// <summary>
    /// Forced-inclusion source owned by WireProduction (same instance returned by
    /// <see cref="WireProduction"/> / <see cref="WireProductionFromLayout"/>).
    /// </summary>
    public RpcForcedInclusionSource? ProductionForcedInclusionSource =>
        _productionComposition?.ForcedInclusionSource;

    /// <summary>
    /// Forced-inclusion L1 finalization client owned by WireProduction (consume/slash path).
    /// </summary>
    public RpcForcedInclusionFinalizationClient? ProductionForcedInclusionFinalizer =>
        _productionComposition?.ForcedInclusionFinalizer;

    /// <summary>
    /// Settlement client owned by WireProduction for L1 submit/finalization RPC calls.
    /// </summary>
    public RpcSettlementClient? ProductionSettlementClient =>
        _productionComposition?.SettlementClient;

    /// <summary>
    /// Transaction sender owned by WireProduction (network-pinned signing + broadcast).
    /// </summary>
    public RpcTransactionSender? ProductionTransactionSender =>
        _productionComposition?.TransactionSender;

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

    /// <summary>
    /// Prefer an explicit non-zero method argument; otherwise use the plugin config height.
    /// Forced-inclusion height is always required (scanner cursor start).
    /// </summary>
    private static uint ResolveDeploymentHeight(
        uint argumentHeight,
        uint settingsHeight,
        string settingsName,
        bool required = true)
    {
        var resolved = argumentHeight != 0 ? argumentHeight : settingsHeight;
        if (required && resolved == 0)
            throw new InvalidOperationException(
                $"{settingsName} must be a non-zero L1 deploy block "
                + "(pass WireProduction height argument or set plugin config from deploy report)");
        return resolved;
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
