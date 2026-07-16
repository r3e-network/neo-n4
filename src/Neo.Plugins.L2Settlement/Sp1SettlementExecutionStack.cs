using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.Executor.ProofWitness;
using Neo.L2.Persistence;
using Neo.L2.Proving.RiscVZk;

namespace Neo.Plugins.L2;

/// <summary>Locked production SP1 execution and proving dependencies for one L2 chain.</summary>
/// <remarks>
/// See doc.md §7.3, §7.5, and §8. The factory binds one authenticated state root, native
/// execution binary digest, SP1 prover queue, verification key, semantic ID, and ZK profile.
/// </remarks>
public sealed record Sp1SettlementExecutionStack
{
    /// <summary>
    /// Canonical L2 state RocksDB path under a chain working directory
    /// (written by <c>neo-stack bootstrap-genesis</c>).
    /// </summary>
    public const string RelativeStateDir = "data/state";

    /// <summary>Scratch directory for host-native <c>neo-zkvm-executor</c> runs.</summary>
    public const string RelativeExecutorScratchDir = "prover/executor-scratch";

    /// <summary>
    /// File-queue directory watched by the out-of-process <c>prove-batch</c> daemon
    /// (matches <c>init-l2</c> <c>prover/inbox</c>).
    /// </summary>
    public const string RelativeProverQueueDir = "prover/inbox";

    /// <summary>Native executor that produces authenticated proof witness material.</summary>
    public required Sp1StatefulBatchExecutor Executor { get; init; }

    /// <summary>Out-of-process cryptographic SP1 proof client.</summary>
    public required Sp1BatchProofProver Prover { get; init; }

    /// <summary>Production ZK settlement profile locked to the same SP1 verification key.</summary>
    public required ProofWitnessPipelineProfile Profile { get; init; }

    /// <summary>Create and validate one complete production SP1 dependency stack.</summary>
    public static Sp1SettlementExecutionStack Create(
        uint chainId,
        IAtomicL2KeyValueStore state,
        UInt256 initialStateRoot,
        string executorPath,
        ReadOnlyMemory<byte> executorSha256,
        string executorScratchDirectory,
        string proverQueueDirectory,
        UInt256 verificationKeyId,
        TimeSpan? executionTimeout = null,
        TimeSpan? proofTimeout = null,
        TimeSpan? proofPollInterval = null)
    {
        ChainIdValidator.ValidateL2(chainId);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(initialStateRoot);
        ArgumentNullException.ThrowIfNull(verificationKeyId);
        if (initialStateRoot.Equals(UInt256.Zero))
            throw new ArgumentException(
                "SP1 initial state root must be non-zero", nameof(initialStateRoot));
        if (verificationKeyId.Equals(UInt256.Zero))
            throw new ArgumentException(
                "SP1 verification key must be non-zero", nameof(verificationKeyId));
        var source = new Sp1StateWitnessSource(state, initialStateRoot);
        var executor = new Sp1StatefulBatchExecutor(
            source,
            executorPath,
            executorSha256,
            executorScratchDirectory,
            executionTimeout);
        var prover = new Sp1BatchProofProver(
            proverQueueDirectory,
            verificationKeyId,
            proofTimeout,
            proofPollInterval);
        if (!prover.ExecutionSemanticId.Equals(ExecutionSemanticIds.Sp1StatefulNeoVmV1))
            throw new InvalidOperationException(
                "SP1 prover semantic differs from the canonical stateful executor");
        return new Sp1SettlementExecutionStack
        {
            Executor = executor,
            Prover = prover,
            Profile = ProofWitnessPipelineProfile.Zk(
                chainId,
                WitnessProofSystem.Sp1,
                verificationKeyId,
                initialStateRoot),
        };
    }

    /// <summary>
    /// Open durable L2 state RocksDB at <see cref="RelativeStateDir"/> after
    /// <c>neo-stack bootstrap-genesis</c>. Caller owns disposal.
    /// </summary>
    public static RocksDbKeyValueStore OpenStateFromChainDirectory(string chainDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainDirectory);
        var root = System.IO.Path.GetFullPath(chainDirectory);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(
                $"Chain directory not found: {root}. Run neo-stack init-l2 first.");

        var statePath = System.IO.Path.Combine(root, RelativeStateDir);
        if (!Directory.Exists(statePath))
            throw new DirectoryNotFoundException(
                $"L2 state store not found at {statePath}. Run neo-stack bootstrap-genesis first.");
        return new RocksDbKeyValueStore(statePath);
    }

    /// <summary>
    /// Bind SP1 executor/prover/profile from a chain working directory after
    /// <c>bootstrap-genesis</c> and <c>init-l2 --from-deploy-report</c>.
    /// </summary>
    /// <remarks>
    /// Loads <c>ChainId</c> from settlement plugin config (must be ProofType=Zk) and the
    /// genesis root from <c>genesis-manifest.json</c>. Ensures
    /// <see cref="RelativeExecutorScratchDir"/> and <see cref="RelativeProverQueueDir"/>
    /// exist under the chain root. The reviewed <c>neo-zkvm-executor</c> binary path/SHA-256
    /// and governance-registered verification key remain host-supplied (funded release pin).
    /// Prefer <see cref="OpenStateFromChainDirectory"/> for <paramref name="state"/> when
    /// the host does not already hold the RocksDB handle.
    /// </remarks>
    public static Sp1SettlementExecutionStack CreateFromChainDirectory(
        string chainDirectory,
        IAtomicL2KeyValueStore state,
        string executorPath,
        ReadOnlyMemory<byte> executorSha256,
        UInt256 verificationKeyId,
        TimeSpan? executionTimeout = null,
        TimeSpan? proofTimeout = null,
        TimeSpan? proofPollInterval = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainDirectory);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(executorPath);
        ArgumentNullException.ThrowIfNull(verificationKeyId);

        var root = System.IO.Path.GetFullPath(chainDirectory);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(
                $"Chain directory not found: {root}. Run neo-stack init-l2 first.");

        var settings = L2SettlementSettings.FromChainDirectory(root);
        if (settings.ChainId == 0)
            throw new InvalidDataException(
                "settlement plugin config ChainId is unset — run init-l2 --from-deploy-report first");
        var proofType = ProofTypeExtensions.Resolve(settings.ProofType);
        if (proofType != ProofType.Zk)
            throw new InvalidOperationException(
                "CreateFromChainDirectory requires ProofType=Zk settlement config — "
                + "use ProofWitnessPipelineProfile.LegacyFromChainDirectory for Multisig/Optimistic");

        var genesis = L2GenesisManifest.ReadInitialStateRootFromChainDirectory(root);
        var scratch = System.IO.Path.Combine(root, RelativeExecutorScratchDir);
        var queue = System.IO.Path.Combine(root, RelativeProverQueueDir);
        Directory.CreateDirectory(scratch);
        Directory.CreateDirectory(queue);

        return Create(
            settings.ChainId,
            state,
            genesis,
            executorPath,
            executorSha256,
            scratch,
            queue,
            verificationKeyId,
            executionTimeout,
            proofTimeout,
            proofPollInterval);
    }
}
