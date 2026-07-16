using Neo.L2;
using Neo.L2.Persistence;

namespace Neo.Plugins.L2;

/// <summary>
/// Opens the canonical WireProduction durable stores under a chain working directory
/// (<c>data/settlement/*</c> as created by <c>init-l2</c> / deploy-report materialization).
/// </summary>
/// <remarks>
/// See doc.md §7.5 / §14.2. Caller owns disposal: dispose this layout after the settlement
/// plugin (or after <see cref="L2SettlementPlugin.WireProduction"/>) has been disposed so
/// scanners release their store references first.
/// </remarks>
public sealed class L2SettlementStoreLayout : IDisposable
{
    private bool _disposed;

    private L2SettlementStoreLayout(
        string chainDirectory,
        RocksDbKeyValueStore proofWitnessBackend,
        KeyValueProofWitnessStore proofWitness,
        RocksDbKeyValueStore forcedInclusionEvents,
        RocksDbKeyValueStore sharedBridgeDeposits,
        RocksDbKeyValueStore messageRouterEvents)
    {
        ChainDirectory = chainDirectory;
        ProofWitnessBackend = proofWitnessBackend;
        ProofWitness = proofWitness;
        ForcedInclusionEvents = forcedInclusionEvents;
        SharedBridgeDeposits = sharedBridgeDeposits;
        MessageRouterEvents = messageRouterEvents;
    }

    /// <summary>Absolute chain working directory used for relative store paths.</summary>
    public string ChainDirectory { get; }

    /// <summary>RocksDB backend under <see cref="NeoHubDeployReport.RelativeProofWitnessStoreDir"/>.</summary>
    public RocksDbKeyValueStore ProofWitnessBackend { get; }

    /// <summary>Durable proof-witness store (owns its backend via layout dispose).</summary>
    public KeyValueProofWitnessStore ProofWitness { get; }

    /// <summary>Forced-inclusion event scanner store.</summary>
    public RocksDbKeyValueStore ForcedInclusionEvents { get; }

    /// <summary>SharedBridge deposit event scanner store.</summary>
    public RocksDbKeyValueStore SharedBridgeDeposits { get; }

    /// <summary>MessageRouter L1→L2 event scanner store.</summary>
    public RocksDbKeyValueStore MessageRouterEvents { get; }

    /// <summary>
    /// Ensure canonical directories exist and open RocksDB stores for WireProduction.
    /// </summary>
    /// <param name="chainDirectory">Chain root from <c>create-chain</c> / <c>init-l2</c>.</param>
    public static L2SettlementStoreLayout Open(string chainDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainDirectory);
        var root = Path.GetFullPath(chainDirectory);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(
                $"Chain directory not found: {root}. Run neo-stack init-l2 first.");

        NeoHubDeployReport.EnsureSettlementStoreDirectories(root);

        RocksDbKeyValueStore? proofBackend = null;
        RocksDbKeyValueStore? forced = null;
        RocksDbKeyValueStore? deposits = null;
        RocksDbKeyValueStore? messages = null;
        try
        {
            proofBackend = OpenStore(root, NeoHubDeployReport.RelativeProofWitnessStoreDir);
            forced = OpenStore(root, NeoHubDeployReport.RelativeForcedInclusionEventStoreDir);
            deposits = OpenStore(root, NeoHubDeployReport.RelativeSharedBridgeDepositEventStoreDir);
            messages = OpenStore(root, NeoHubDeployReport.RelativeMessageRouterEventStoreDir);
            // ownsStore: false — layout disposes the four RocksDB instances explicitly.
            var proofWitness = new KeyValueProofWitnessStore(proofBackend, ownsStore: false);
            return new L2SettlementStoreLayout(root, proofBackend, proofWitness, forced, deposits, messages);
        }
        catch
        {
            messages?.Dispose();
            deposits?.Dispose();
            forced?.Dispose();
            proofBackend?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Absolute path of a recommended store directory under this layout's chain root.
    /// </summary>
    public string ResolvePath(string relativeStoreDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeStoreDir);
        return Path.GetFullPath(Path.Combine(ChainDirectory, relativeStoreDir));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ProofWitness.Dispose();
        MessageRouterEvents.Dispose();
        SharedBridgeDeposits.Dispose();
        ForcedInclusionEvents.Dispose();
        ProofWitnessBackend.Dispose();
    }

    private static RocksDbKeyValueStore OpenStore(string chainRoot, string relative)
    {
        var absolute = Path.Combine(chainRoot, relative);
        Directory.CreateDirectory(absolute);
        return new RocksDbKeyValueStore(absolute);
    }
}
