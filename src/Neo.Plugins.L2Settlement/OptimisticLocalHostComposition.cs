using Neo.L2;
using Neo.L2.Executor.ProofWitness;
using Neo.L2.ForcedInclusion;
using Neo.L2.Proving;
using Neo.L2.Proving.Optimistic;
using Neo.L2.Settlement.Rpc;
using Neo.Wallets;

namespace Neo.Plugins.L2;

/// <summary>
/// Optimistic/local-DA host composition root: chain-directory plugins + durable layout +
/// <see cref="L2SettlementPlugin.WireProductionFromLayout"/>.
/// </summary>
/// <remarks>
/// See doc.md §7.5 / §14.2. Opens Optimistic settlement without Neo.CLI. Sequencer key and
/// L1 bond references remain host-supplied (bond posting is a funded gate). Executor and
/// settlement signer are host-supplied. Dispose the composition (settlement first) before
/// reopening the same RocksDB paths.
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
        RpcForcedInclusionSource forcedInclusion)
    {
        ChainDirectory = chainDirectory;
        Batch = batch;
        Settlement = settlement;
        Layout = layout;
        Prover = prover;
        DaWriter = daWriter;
        ForcedInclusion = forcedInclusion;
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
    /// Open Optimistic host composition from a chain directory after
    /// <c>init-l2 --from-deploy-report</c> (and Optimistic ProofType in settlement/prover config).
    /// </summary>
    /// <param name="chainDirectory">Chain root with plugin configs + durable store dirs.</param>
    /// <param name="executor">Batch executor (sample / custom).</param>
    /// <param name="sequencerKey">Sequencer secp256r1 key for optimistic claims.</param>
    /// <param name="bondContract">Non-zero L1 bond contract hash.</param>
    /// <param name="bondTxHash">Non-zero L1 bond transaction hash.</param>
    /// <param name="signer">L1 settlement transaction signer.</param>
    /// <param name="rpcHttpClient">Optional HTTP client for L1 JSON-RPC (tests inject mocks).</param>
    /// <param name="submittedAtUnixMs">Optional wall-clock for challenge windows.</param>
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
        try
        {
            batch = L2BatchPlugin.CreateFromChainDirectory(root);
            settlement = L2SettlementPlugin.CreateFromChainDirectory(root);
            layout = L2SettlementStoreLayout.Open(root);
            daWriter = PersistentDAWriter.OpenLocalFromChainDirectory(root);
            prover = L2ProverPlugin.CreateOptimisticWiredFromChainDirectory(
                root,
                sequencerKey,
                bondContract,
                bondTxHash,
                submittedAtUnixMs);
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

            return new OptimisticLocalHostComposition(
                root,
                batch,
                settlement,
                layout,
                prover,
                daWriter,
                forced);
        }
        catch
        {
            settlement?.Dispose();
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
        Settlement.Dispose();
        Prover.Dispose();
        Batch.Dispose();
        Layout.Dispose();
        DaWriter.Dispose();
        GC.SuppressFinalize(this);
    }
}
