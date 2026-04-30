namespace Neo.L2.Challenge;

/// <summary>
/// Replays a batch deterministically and reports the post-state root the replay produced.
/// </summary>
/// <remarks>
/// Production wires this to <see cref="Neo.L2.Executor.ReferenceBatchExecutor"/> against the
/// challenger's local state-store snapshot at <see cref="BatchExecutionRequest.PreStateRoot"/>.
/// Tests can inject a fixed-output implementation to drive the orchestrator deterministically.
/// </remarks>
public interface IFraudProofGenerator
{
    /// <summary>
    /// Replay <paramref name="request"/> and return the post-state root produced by the
    /// challenger's executor. If this disagrees with the batch's claimed post-state root,
    /// <see cref="ChallengeOrchestrator"/> emits a fraud proof.
    /// </summary>
    ValueTask<UInt256> ReplayAsync(BatchExecutionRequest request, CancellationToken cancellationToken = default);
}
