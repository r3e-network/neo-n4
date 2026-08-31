namespace Neo.L2;

/// <summary>
/// Optional settlement capability that drives permissionless challenge-window expiry so
/// batches do not stay Challengeable until an external actor finalizes them.
/// </summary>
/// <remarks>
/// See doc.md §7.5 and §17. On-chain, <c>OptimisticChallenge.FinalizeIfPastWindow</c> is
/// permissionless and is the only path from Challengeable → Finalized: the window deadline
/// is recorded at SubmitBatch time, expiry is asserted against the L1 clock, and an
/// accepted fraud marker keeps the call shut. Implementations own the time source used for
/// the expiry decision; the contract re-asserts expiry on-chain, so a skewed local clock at
/// worst sends one doomed invocation or delays finalization by the skew.
/// </remarks>
public interface ISettlementWindowFinalizer
{
    /// <summary>
    /// Report whether the challenge window for a batch is recorded and already expired on
    /// L1. Returns false when no window is open — batches that are unknown, pending,
    /// finalized, or fraud-reverted have no window key on-chain.
    /// </summary>
    ValueTask<bool> IsWindowExpiredAsync(
        uint chainId,
        ulong batchNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Submit the permissionless <c>FinalizeIfPastWindow</c> invocation for a batch whose
    /// window has expired. Must be idempotent: once finalized (or fraud-reverted) the window
    /// key is consumed on-chain, so a retry racing a concurrent finalizer observes no window
    /// and completes without error.
    /// </summary>
    ValueTask FinalizeIfPastWindowAsync(
        uint chainId,
        ulong batchNumber,
        CancellationToken cancellationToken = default);
}
