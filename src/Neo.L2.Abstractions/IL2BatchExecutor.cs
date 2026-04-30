namespace Neo.L2;

/// <summary>
/// Deterministic batch executor — the function the L2 chain runs to seal a batch and the
/// function the prover proves correct. Implementations MUST be a pure function of their inputs:
/// no clocks, no random, no network, no log writes.
/// </summary>
/// <remarks>
/// See doc.md §8.1–§8.2.
/// <para>
/// This is the only deterministic surface that the verifier checks against. Anything outside
/// this contract (P2P, RPC, mempool, plugins, logging, the on-disk DB layout) is excluded by
/// design and must not influence the outputs.
/// </para>
/// </remarks>
public interface IL2BatchExecutor
{
    /// <summary>
    /// Apply <paramref name="request"/> on top of <see cref="BatchExecutionRequest.PreStateRoot"/>
    /// and return the resulting roots and gas usage.
    /// </summary>
    /// <param name="request">All inputs required for deterministic execution.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Roots and gas usage produced by executing the batch.</returns>
    ValueTask<BatchExecutionResult> ApplyBatchAsync(
        BatchExecutionRequest request,
        CancellationToken cancellationToken = default);
}
