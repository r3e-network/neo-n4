using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.Bridge;
using Neo.L2.ForcedInclusion;
using Neo.L2.Persistence;
using Neo.L2.Proving;
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
/// operator process. Shared operator surface lives on <see cref="LocalHostCompositionBase"/>;
/// this type only owns Zk <c>Open</c>, <see cref="Sp1SettlementExecutionStack"/>, durable state,
/// and host-supplied production DA. Dispose the composition (settlement first) before reopening
/// RocksDB paths.
/// </remarks>
public sealed class ZkLocalHostComposition : LocalHostCompositionBase
{
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
        : base(
            chainDirectory,
            batch,
            settlement,
            layout,
            prover,
            daWriter,
            forcedInclusion,
            bridge,
            metrics,
            rpcStore)
    {
        State = state;
        Stack = stack;
        DaWriter = daWriter;
    }

    /// <summary>
    /// Durable L2 state RocksDB at <see cref="Sp1SettlementExecutionStack.RelativeStateDir"/>
    /// (owned by this composition).
    /// </summary>
    public RocksDbKeyValueStore State { get; }

    /// <summary>Bound SP1 executor, file-queue prover, and ZK pipeline profile.</summary>
    public Sp1SettlementExecutionStack Stack { get; }

    /// <summary>Host-supplied production DA writer (not disposed by this composition).</summary>
    public IProductionDAWriter DaWriter { get; }

    /// <inheritdoc />
    public override bool SupportsLocalDaReader => false;

    /// <inheritdoc />
    private protected override void DisposeModeResources() => State.Dispose();

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
    /// this is null, defaults to <see cref="LocalHostCompositionBase.IsOfflinePassportComplete"/> via
    /// <see cref="LocalHostCompositionBase.StartMetricsHttp"/>.
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

            var host = new ZkLocalHostComposition(
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
            if (startMetricsHttp)
                host.StartMetricsHttp(metricsPortOverride, metricsReadinessCheck);
            else if (metricsReadinessCheck is not null)
                host.Metrics.WithReadinessCheck(metricsReadinessCheck);
            return host;
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
}
