using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.Bridge;
using Neo.L2.Executor.ProofWitness;
using Neo.L2.ForcedInclusion;
using Neo.L2.Persistence;
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
/// factories instead. Shared operator surface lives on <see cref="LocalHostCompositionBase"/>;
/// this type only owns Multisig <c>Open</c> + local <see cref="PersistentDAWriter"/>.
/// Dispose the composition (settlement first) before reopening the same RocksDB paths.
/// </remarks>
public sealed class MultisigLocalHostComposition : LocalHostCompositionBase
{
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
        DaWriter = daWriter;
    }

    /// <summary>Local persistent DA writer under <c>data/settlement/da</c>.</summary>
    public PersistentDAWriter DaWriter { get; }

    /// <inheritdoc />
    public override bool SupportsLocalDaReader => true;

    /// <inheritdoc />
    public override IDAReader CreateLocalDaReader() => DaWriter.CreateReader();

    /// <inheritdoc />
    private protected override void DisposeOwnedDaWriter() => DaWriter.Dispose();

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
    /// this is null, defaults to <see cref="LocalHostCompositionBase.IsOfflinePassportComplete"/> via
    /// <see cref="LocalHostCompositionBase.StartMetricsHttp"/>.
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

            var host = new MultisigLocalHostComposition(
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
            // Install readiness after composition exists so the default can use offline passport.
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
            daWriter?.Dispose();
            rpcStore?.Dispose();
            throw;
        }
    }
}
