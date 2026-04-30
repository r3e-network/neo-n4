using Neo.Cryptography;
using Neo.L2.Executor.Receipts;

namespace Neo.L2.Executor;

/// <summary>
/// Test / scaffolding executor: hashes the transaction bytes and returns an empty-effect
/// receipt. Replace with the real <c>ApplicationEngine</c>-backed executor for production.
/// </summary>
/// <remarks>
/// Because this executor produces no withdrawals or messages on its own, the
/// <see cref="ReferenceBatchExecutor"/> still needs an <see cref="IBatchEffectsCollector"/> to
/// supply test-defined effects when exercising the batch-sealing pipeline.
/// </remarks>
public sealed class ReferenceTransactionExecutor : ITransactionExecutor
{
    private readonly IBatchEffectsCollector? _effects;

    /// <summary>Construct optionally with an effects collector that supplies side effects.</summary>
    public ReferenceTransactionExecutor(IBatchEffectsCollector? effects = null)
    {
        _effects = effects;
    }

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
        var effects = _effects?.GetEffects(txHash) ?? BatchEffects.Empty;
        return new ValueTask<TransactionExecutionResult>(new TransactionExecutionResult
        {
            Receipt = receipt,
            TxHash = txHash,
            Withdrawals = effects.Withdrawals,
            Messages = effects.Messages,
        });
    }
}

/// <summary>
/// Test hook: lets a unit test or devnet driver inject deterministic per-transaction effects
/// (withdrawals + messages) into the reference executor without involving a real VM.
/// </summary>
public interface IBatchEffectsCollector
{
    /// <summary>Look up the deterministic effects for a transaction by its hash.</summary>
    BatchEffects GetEffects(UInt256 txHash);
}

/// <summary>Per-transaction effects.</summary>
public sealed record BatchEffects
{
    /// <summary>Empty effects.</summary>
    public static BatchEffects Empty { get; } = new()
    {
        Withdrawals = Array.Empty<WithdrawalRequest>(),
        Messages = Array.Empty<CrossChainMessage>(),
    };

    /// <summary>Withdrawals emitted by the transaction.</summary>
    public required IReadOnlyList<WithdrawalRequest> Withdrawals { get; init; }

    /// <summary>Cross-chain messages emitted by the transaction.</summary>
    public required IReadOnlyList<CrossChainMessage> Messages { get; init; }
}
