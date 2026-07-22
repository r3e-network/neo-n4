using Neo.L2.Batch;
using Neo.L2.Executor.ProofWitness;

namespace Neo.L2.SoftSeal.TestSupport;

/// <summary>
/// Shared SoftSeal batch executor: fixed post-state root, no real L2 execution.
/// Used by Multisig/Optimistic unit + E2E SoftSeal paths (not a ZK semantic).
/// </summary>
/// <remarks>
/// L1 settle broadcast remains operator-funded. Single definition avoids three
/// private clones with divergent PostStateRoot fills (0x22/0x33/0x44).
/// </remarks>
public sealed class SoftPassThroughExecutor : IProofWitnessBatchExecutor
{
    /// <summary>Canonical SoftSeal post-state root (32× 0x22).</summary>
    public static UInt256 PostStateRoot { get; } =
        new(Enumerable.Repeat((byte)0x22, 32).ToArray());

    /// <inheritdoc />
    public ValueTask<BatchExecutionResult> ApplyBatchAsync(
        BatchExecutionRequest request,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(BuildResult());

    /// <inheritdoc />
    public ValueTask<ProofWitnessExecutionResult> ApplyBatchWithWitnessAsync(
        SealedBatch batch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        return ValueTask.FromResult(new ProofWitnessExecutionResult
        {
            ExecutionResult = BuildResult(),
            ExecutionSemanticId = ExecutionSemanticIds.ReferenceNoOpV1,
            WitnessAuthenticated = false,
            StateWitness = ReadOnlyMemory<byte>.Empty,
            Effects = new byte[] { 0x01 },
        });
    }

    private static BatchExecutionResult BuildResult() => new()
    {
        PostStateRoot = PostStateRoot,
        ReceiptRoot = UInt256.Zero,
        WithdrawalRoot = UInt256.Zero,
        L2ToL1MessageRoot = UInt256.Zero,
        L2ToL2MessageRoot = UInt256.Zero,
        TxRoot = UInt256.Zero,
        GasConsumed = 0,
    };
}
