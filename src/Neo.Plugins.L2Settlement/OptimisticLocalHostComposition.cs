using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.Bridge;
using Neo.L2.Executor.ProofWitness;
using Neo.L2.ForcedInclusion;
using Neo.L2.Persistence;
using Neo.L2.Proving;
using Neo.L2.Proving.Optimistic;
using Neo.L2.Settlement.Rpc;
using Neo.L2.Telemetry;
using Neo.Plugins.L2Rpc;
using Neo.Wallets;

namespace Neo.Plugins.L2;

/// <summary>
/// Optimistic/local-DA host composition root: chain-directory plugins + durable layout +
/// <see cref="L2SettlementPlugin.WireProductionFromLayout"/> + bridge deposit source +
/// metrics + L2 RPC proof store.
/// </summary>
/// <remarks>
/// See doc.md §7.5 / §14.1 / §14.2. Opens Optimistic settlement without Neo.CLI. Sequencer
/// key and L1 bond references remain host-supplied (bond posting is a funded gate). Executor
/// and settlement signer are host-supplied. Shared operator surface lives on
/// <see cref="LocalHostCompositionBase"/>; this type only owns Optimistic <c>Open</c> + local
/// <see cref="PersistentDAWriter"/>. Dispose the composition (settlement first) before
/// reopening the same RocksDB paths.
/// </remarks>
public sealed class OptimisticLocalHostComposition : LocalHostCompositionBase
{
    private OptimisticLocalHostComposition(
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
    /// Open Optimistic host composition from a chain directory after
    /// <c>init-l2 --from-deploy-report</c> (and Optimistic ProofType in settlement/prover config).
    /// </summary>
    public static OptimisticLocalHostComposition Open(
        string chainDirectory,
        IProofWitnessBatchExecutor executor,
        KeyPair sequencerKey,
        UInt160 bondContract,
        UInt256 bondTxHash,
        INeoTransactionSigner signer,
        HttpClient? rpcHttpClient = null,
        Func<ulong>? submittedAtUnixMs = null,
        bool startMetricsHttp = false,
        int? metricsPortOverride = null,
        Func<bool>? metricsReadinessCheck = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainDirectory);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(sequencerKey);
        ArgumentNullException.ThrowIfNull(bondContract);
        ArgumentNullException.ThrowIfNull(bondTxHash);
        ArgumentNullException.ThrowIfNull(signer);

        var root = Path.GetFullPath(chainDirectory);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(
                $"Chain directory not found: {root}. Run neo-stack init-l2 first.");

        var settlementSettings = L2SettlementSettings.FromChainDirectory(root);
        if ((ProofType)settlementSettings.ProofType != ProofType.Optimistic)
        {
            throw new InvalidOperationException(
                $"OptimisticLocalHostComposition requires settlement ProofType.Optimistic; "
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
            rpcStore = InMemoryL2RpcStore.OpenFromChainDirectory(root);
            prover = L2ProverPlugin.CreateOptimisticWiredFromChainDirectory(
                root,
                sequencerKey,
                bondContract,
                bondTxHash,
                submittedAtUnixMs);
            metrics = L2MetricsPlugin.CreateFromChainDirectory(root);
            batch.WithMetrics(metrics.Metrics);
            settlement.WithMetrics(metrics.Metrics);
            IDAWriter instrumentedDa = new MetricsEmittingDAWriter(daWriter, metrics.Metrics);

            var forced = settlement.WireProductionFromLayout(
                root,
                layout,
                batch,
                executor,
                instrumentedDa,
                prover.Prover
                    ?? throw new InvalidOperationException("Optimistic prover Wire did not install IL2Prover"),
                signer,
                rpcHttpClient: rpcHttpClient);

            bridge = L2BridgePlugin.CreateFromChainDirectory(root);
            bridge.WithMetrics(metrics.Metrics);
            var deposits = settlement.ProductionDepositSource;
            if (deposits is not null)
                bridge.WithDepositSource(deposits);

            var host = new OptimisticLocalHostComposition(
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
