using Neo.L2;
using Neo.L2.Bridge;
using Neo.L2.Executor.ProofWitness;
using Neo.L2.ForcedInclusion;
using Neo.L2.Proving;
using Neo.L2.Proving.Attestation;
using Neo.L2.Settlement.Rpc;
using Neo.L2.Telemetry;

namespace Neo.Plugins.L2;

/// <summary>
/// Multisig/local-DA host composition root: chain-directory plugins + durable layout +
/// <see cref="L2SettlementPlugin.WireProductionFromLayout"/> + bridge deposit source + metrics.
/// </summary>
/// <remarks>
/// See doc.md §7.5 / §14.2. Opens Multisig settlement for TrainingWheels-style operators
/// without Neo.CLI. Executor and signer remain host-supplied (sample/custom executor;
/// <see cref="LocalKeyTransactionSigner"/> or HSM). Public DA / Zk / funded L1 publication
/// are out of scope — use <c>Sp1SettlementExecutionStack</c> and Gateway factories instead.
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
        L2MetricsPlugin metrics)
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

    /// <summary>Shared metrics sink host (wired onto batch + settlement).</summary>
    public L2MetricsPlugin Metrics { get; }

    /// <summary>
    /// Open Multisig host composition from a chain directory after
    /// <c>init-l2 --from-deploy-report</c> (and Multisig ProofType in settlement/prover config).
    /// </summary>
    /// <param name="chainDirectory">Chain root with plugin configs + durable store dirs.</param>
    /// <param name="executor">Batch executor (sample / custom; not SP1-native).</param>
    /// <param name="signers">Committee <see cref="ISignerSet"/> for attestation proofs.</param>
    /// <param name="signer">L1 settlement transaction signer.</param>
    /// <param name="rpcHttpClient">Optional HTTP client for L1 JSON-RPC (tests inject mocks).</param>
    public static MultisigLocalHostComposition Open(
        string chainDirectory,
        IProofWitnessBatchExecutor executor,
        ISignerSet signers,
        INeoTransactionSigner signer,
        HttpClient? rpcHttpClient = null)
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
        try
        {
            batch = L2BatchPlugin.CreateFromChainDirectory(root);
            settlement = L2SettlementPlugin.CreateFromChainDirectory(root);
            layout = L2SettlementStoreLayout.Open(root);
            daWriter = PersistentDAWriter.OpenLocalFromChainDirectory(root);
            prover = L2ProverPlugin.CreateMultisigWiredFromChainDirectory(root, signers);
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
                    ?? throw new InvalidOperationException("Multisig prover Wire did not install IL2Prover"),
                signer,
                rpcHttpClient: rpcHttpClient);

            bridge = L2BridgePlugin.CreateFromChainDirectory(root);
            bridge.WithMetrics(metrics.Metrics);
            var deposits = settlement.ProductionComposition?.OwnedDepositSource;
            if (deposits is not null)
                bridge.WithDepositSource(deposits);

            return new MultisigLocalHostComposition(
                root,
                batch,
                settlement,
                layout,
                prover,
                daWriter,
                forced,
                bridge,
                metrics);
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
        GC.SuppressFinalize(this);
    }
}
