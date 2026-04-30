using Neo.L2.Executor.Receipts;
using Neo.L2.Messaging;
using Neo.L2.State;

namespace Neo.L2.Executor;

/// <summary>
/// Reference batch executor: applies L1 messages, then transactions, then computes the four
/// per-batch Merkle roots plus a placeholder post-state root. Implements
/// <see cref="IL2BatchExecutor"/> exactly as the proving spec mandates.
/// </summary>
/// <remarks>
/// See SPEC.md for the full determinism contract. The post-state root produced here is
/// derived from the pre-state root XORed with a hash of the receipt root — adequate for
/// scaffolding tests but NOT for production. Production deployments inject a real MPT-backed
/// state-root oracle.
/// </remarks>
public sealed class ReferenceBatchExecutor : IL2BatchExecutor
{
    private readonly ITransactionExecutor _txExecutor;
    private readonly IL1MessageProcessor? _l1Processor;
    private readonly IPostStateRootOracle _postStateRootOracle;

    /// <summary>Construct with required components.</summary>
    public ReferenceBatchExecutor(
        ITransactionExecutor txExecutor,
        IPostStateRootOracle postStateRootOracle,
        IL1MessageProcessor? l1Processor = null)
    {
        ArgumentNullException.ThrowIfNull(txExecutor);
        ArgumentNullException.ThrowIfNull(postStateRootOracle);
        _txExecutor = txExecutor;
        _postStateRootOracle = postStateRootOracle;
        _l1Processor = l1Processor;
    }

    /// <inheritdoc />
    public async ValueTask<BatchExecutionResult> ApplyBatchAsync(
        BatchExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1. Apply L1 messages first (per SPEC.md §"Execution order" #1).
        if (_l1Processor is not null)
        {
            foreach (var msg in request.L1MessagesConsumed)
                await _l1Processor.ApplyAsync(msg, cancellationToken).ConfigureAwait(false);
        }

        // 2. Apply transactions in order, collecting receipts + side effects.
        var receipts = new List<Receipt>(request.Transactions.Count);
        var txHashes = new List<UInt256>(request.Transactions.Count);
        var withdrawalTree = new WithdrawalTree();
        var outbox = new L2Outbox();
        long totalGas = 0;

        foreach (var serializedTx in request.Transactions)
        {
            var result = await _txExecutor.ExecuteAsync(serializedTx, request.BlockContext, cancellationToken).ConfigureAwait(false);
            receipts.Add(result.Receipt);
            txHashes.Add(result.TxHash);
            totalGas += result.Receipt.GasConsumed;

            foreach (var w in result.Withdrawals) withdrawalTree.Add(w);
            foreach (var m in result.Messages) outbox.Add(m);
        }

        // 3. Compute the four batch-level roots.
        var txRoot = MerkleTree.ComputeRoot(txHashes);
        var receiptRoot = MerkleTree.ComputeRoot(receipts.Select(r => r.Hash()).ToArray());

        // 4. Resolve post-state root via the oracle.
        var postStateRoot = await _postStateRootOracle
            .ResolveAsync(request.PreStateRoot, receiptRoot, request.BlockContext, cancellationToken)
            .ConfigureAwait(false);

        return new BatchExecutionResult
        {
            PostStateRoot = postStateRoot,
            ReceiptRoot = receiptRoot,
            WithdrawalRoot = withdrawalTree.Root,
            L2ToL1MessageRoot = outbox.L2ToL1Root,
            L2ToL2MessageRoot = outbox.L2ToL2Root,
            TxRoot = txRoot,
            GasConsumed = totalGas,
        };
    }
}

/// <summary>
/// Pluggable interface for resolving the post-batch state root. The real implementation walks
/// the MPT after all writes have been applied; the stub implementation in
/// <see cref="DerivedPostStateRootOracle"/> is for tests.
/// </summary>
public interface IPostStateRootOracle
{
    /// <summary>Compute the post-state root after applying the batch.</summary>
    ValueTask<UInt256> ResolveAsync(
        UInt256 preStateRoot,
        UInt256 receiptRoot,
        BatchBlockContext blockContext,
        CancellationToken cancellationToken = default);
}

/// <summary>Test oracle: derives postStateRoot deterministically from preStateRoot ⊕ receiptRoot ⊕ blockContextHash.</summary>
public sealed class DerivedPostStateRootOracle : IPostStateRootOracle
{
    /// <inheritdoc />
    public ValueTask<UInt256> ResolveAsync(
        UInt256 preStateRoot,
        UInt256 receiptRoot,
        BatchBlockContext blockContext,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ctxHash = StateRootCalculator.HashBlockContext(blockContext);
        Span<byte> buf = stackalloc byte[32];
        var pre = preStateRoot.GetSpan();
        var rec = receiptRoot.GetSpan();
        var ctx = ctxHash.GetSpan();
        for (var i = 0; i < 32; i++)
            buf[i] = (byte)(pre[i] ^ rec[i] ^ ctx[i]);
        return new ValueTask<UInt256>(new UInt256(buf));
    }
}

/// <summary>
/// Pluggable interface for applying inbound L1 messages on the L2 side. Production wires this
/// to <c>L2MessageContract</c> + <c>L2BridgeContract</c>; tests provide a stub.
/// </summary>
public interface IL1MessageProcessor
{
    /// <summary>Apply a single L1 → L2 message.</summary>
    ValueTask ApplyAsync(CrossChainMessage message, CancellationToken cancellationToken = default);
}
