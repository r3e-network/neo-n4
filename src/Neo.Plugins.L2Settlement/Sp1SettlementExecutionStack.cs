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
}
