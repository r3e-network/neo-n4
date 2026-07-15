using Neo.Cryptography;
using Neo.L2.Executor.Receipts;

namespace Neo.L2.Executor;

/// <summary>
/// Test / scaffolding executor: hashes the transaction bytes and returns an empty-effect
/// receipt. Production Neo N4 L2s use the NeoVM2/RISC-V executor; the
/// <c>ApplicationEngine</c>-backed executor is retained for legacy NeoVM compatibility.
/// </summary>
public sealed class ReferenceTransactionExecutor : ITransactionExecutor
{
    /// <inheritdoc />
    public ValueTask<TransactionExecutionResult> ExecuteAsync(
        ReadOnlyMemory<byte> serializedTx,
        BatchBlockContext batchContext,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var txHash = new UInt256(Crypto.Hash256(serializedTx.Span));
        var receipt = new Receipt
        {
            TxHash = txHash,
            Success = true,
            GasConsumed = 0,
            StorageDeltaHash = UInt256.Zero,
            EventsHash = UInt256.Zero,
        };
        return new ValueTask<TransactionExecutionResult>(new TransactionExecutionResult
        {
            Receipt = receipt,
            TxHash = txHash,
            Withdrawals = Array.Empty<WithdrawalRequest>(),
            Messages = Array.Empty<CrossChainMessage>(),
        });
    }
}
