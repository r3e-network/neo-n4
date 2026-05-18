using Neo.L2.Executor.Receipts;

namespace Neo.L2.Executor;

/// <summary>
/// Pluggable per-transaction executor. The reference implementation
/// (<see cref="ReferenceTransactionExecutor"/>) is a no-op stand-in that simply records the
/// transaction hash and produces a synthetic empty receipt. Production Neo N4 L2 deployments
/// wire this to the NeoVM2/RISC-V executor; legacy NeoVM compatibility can still use
/// <c>Neo.SmartContract.ApplicationEngine</c>.
/// </summary>
/// <remarks>
/// The split lives here so the batch-level Merkle / sealing logic can be exhaustively tested
/// without depending on a running Neo node. See SPEC.md for the determinism contract that any
/// implementation must satisfy.
/// </remarks>
public interface ITransactionExecutor
{
    /// <summary>
    /// Execute a single transaction against the current state, return its receipt.
    /// Implementations MUST be deterministic per the SPEC.md contract.
    /// </summary>
    /// <param name="serializedTx">Canonical Neo-serialized transaction bytes.</param>
    /// <param name="batchContext">Batch-level context (block height, timestamp, network…).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<TransactionExecutionResult> ExecuteAsync(
        ReadOnlyMemory<byte> serializedTx,
        BatchBlockContext batchContext,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Output of a single transaction. Both successful and failed transactions return one of
/// these; failures get <see cref="Receipt.Success"/>=false and bubble back to the batch root.
/// </summary>
public sealed record TransactionExecutionResult
{
    /// <summary>The receipt produced.</summary>
    public required Receipt Receipt { get; init; }

    /// <summary>The canonical transaction hash (matches Neo's <c>Transaction.Hash</c>).</summary>
    public required UInt256 TxHash { get; init; }

    /// <summary>Withdrawals emitted during this transaction (added to the batch withdrawal tree).</summary>
    public required IReadOnlyList<WithdrawalRequest> Withdrawals { get; init; }

    /// <summary>L2 → L1 / L2 messages emitted during this transaction.</summary>
    public required IReadOnlyList<CrossChainMessage> Messages { get; init; }
}
