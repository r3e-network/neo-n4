using Neo.L2;
using Neo.L2.Bridge;
using Neo.L2.Executor.ProofWitness;
using Neo.L2.ForcedInclusion;
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
/// and settlement signer are host-supplied. WireProduction leaves MessageRouter finalized-proof
/// ownership unset so <see cref="InMemoryL2RpcStore"/> can own <c>data/rpc/proofs</c>.
/// Dispose the composition (settlement first) before reopening the same RocksDB paths.
/// </remarks>
public sealed class OptimisticLocalHostComposition : IDisposable
{
    private bool _disposed;

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

    /// <summary>Optimistic <see cref="OptimisticProver"/> host.</summary>
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
        Func<ulong>? submittedAtUnixMs = null)
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

            var forced = settlement.WireProductionFromLayout(
                root,
                layout,
                batch,
                executor,
                daWriter,
                prover.Prover
                    ?? throw new InvalidOperationException("Optimistic prover Wire did not install IL2Prover"),
                signer,
                rpcHttpClient: rpcHttpClient);

            bridge = L2BridgePlugin.CreateFromChainDirectory(root);
            bridge.WithMetrics(metrics.Metrics);
            var deposits = settlement.ProductionComposition?.OwnedDepositSource;
            if (deposits is not null)
                bridge.WithDepositSource(deposits);

            return new OptimisticLocalHostComposition(
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
