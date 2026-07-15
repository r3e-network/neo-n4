using Neo.L2.Executor.Receipts;
using Neo.L2.Messaging;
using Neo.L2.Persistence;
using Neo.L2.State;

namespace Neo.L2.Executor;

/// <summary>
/// Reference batch executor: applies L1 messages, then transactions, then computes the four
/// per-batch Merkle roots plus the post-state root resolved via the injected
/// <see cref="IPostStateRootOracle"/>. Implements the execution-order and root-computation
/// portions of the proving spec (<see cref="IL2BatchExecutor"/>).
/// </summary>
/// <remarks>
/// See SPEC.md for the full determinism contract. The class is production-quality.
/// <para>
/// <b>Pre-state validation is the caller's responsibility.</b> SPEC.md §"Error handling"
/// makes an invalid <see cref="BatchExecutionRequest.PreStateRoot"/> a fatal protocol error
/// ("the calling batcher MUST NOT seal a batch"). This executor does not re-derive the
/// store's current state root and compare it against <c>request.PreStateRoot</c>: the
/// <see cref="IPostStateRootOracle"/> abstraction it is given only resolves the post-batch
/// root and (for the test <see cref="DerivedPostStateRootOracle"/>) does not read live state
/// at all. A batcher that feeds this executor MUST verify that the store it is executing
/// against actually corresponds to <c>request.PreStateRoot</c> before sealing — e.g. by
/// computing the state-store oracle's root before applying the batch and rejecting on
/// mismatch.
/// </para>
/// <para>
/// The behavior of <see cref="BatchExecutionResult.PostStateRoot"/> is determined entirely by
/// the injected oracle:
/// <list type="bullet">
///   <item><description><see cref="State.KeyedStateRootOracle"/> — production in-process
///     Neo-classic Merkle over the live <see cref="State.KeyedStateStore"/>. Used by the
///     devnet, the Phase 0–2 integration tests, and the custom-executor end-to-end test.</description></item>
///   <item><description><c>Neo.L2.Executor.MerkleStatePostStateRootOracle</c> — production
///     state-root oracle over an <see cref="IL2KeyValueStore"/>, used by
///     <c>UT_E2E_RealVM_FullStack</c> and the production batch flow.</description></item>
///   <item><description><see cref="DerivedPostStateRootOracle"/> — test fixture below
///     (deterministic XOR of pre-state ⊕ receipt-root ⊕ block-context-hash). For tests
///     that exercise the executor's plumbing without standing up a state store. NOT for
///     production use; the production oracles above are the canonical implementations.</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class ReferenceBatchExecutor : IL2BatchExecutor
{
    private readonly ITransactionExecutor _txExecutor;
    private readonly TransactionEffectsProfile _effectsProfile;
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
        _effectsProfile = txExecutor.EffectsProfile;
        if (_effectsProfile is not TransactionEffectsProfile.ExecutorDeclared
            and not TransactionEffectsProfile.CanonicalNativeV1)
            throw new ArgumentOutOfRangeException(
                nameof(txExecutor),
                _effectsProfile,
                "ITransactionExecutor returned an unsupported effects profile");
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
        if (request.L1MessagesConsumed.Count > 0)
        {
            if (_l1Processor is null)
                throw new InvalidOperationException(
                    "A canonical IL1MessageProcessor is required for a non-empty L1 inbox");
            for (var index = 0; index < request.L1MessagesConsumed.Count; index++)
            {
                if (request.L1MessagesConsumed[index] is null)
                    throw new ArgumentException(
                        $"L1MessagesConsumed[{index}] is null",
                        nameof(request));
            }
            await _l1Processor.ApplyBatchAsync(
                request.ChainId,
                request.L1MessagesConsumed,
                cancellationToken).ConfigureAwait(false);
        }

        // 2. Apply transactions in order, collecting receipts + side effects.
        var receipts = new List<Receipt>(request.Transactions.Count);
        var txHashes = new List<UInt256>(request.Transactions.Count);
        var withdrawalTree = new WithdrawalTree();
        var outbox = new L2Outbox();
        long totalGas = 0;

        foreach (var serializedTx in request.Transactions)
        {
            var result = await _txExecutor.ExecuteAsync(serializedTx, request.BlockContext, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("ITransactionExecutor.ExecuteAsync returned null");
            // Defensive: even with `required` on the record fields, individual fields can
            // be null. Receipt would NRE on the .Hash() call below; TxHash would NRE in
            // MerkleTree.ComputeRoot. Surface as a clear contract violation. Same
            // iter-171/172 callee-contract pattern.
            ArgumentNullException.ThrowIfNull(result.Receipt);
            ArgumentNullException.ThrowIfNull(result.TxHash);
            receipts.Add(result.Receipt);
            txHashes.Add(result.TxHash);
            totalGas += result.Receipt.GasConsumed;

            if (_effectsProfile == TransactionEffectsProfile.CanonicalNativeV1)
                ValidateCanonicalNativeEffects(result);

            // Per L2 semantics, a failed transaction reverts all its state changes —
            // including emitted withdrawals and L2-side cross-chain messages. The
            // ITransactionExecutor contract should already filter these on the executor
            // side, but enforce it at the batch level too as defense in depth: a buggy
            // executor that leaks effects from a failed tx would silently produce a
            // withdrawal-tree / outbox commitment that doesn't match the (correct)
            // ReceiptRoot, surfacing only at L1 settlement when the inclusion proof
            // for the leaked withdrawal is checked against the user's actual state.
            if (!result.Receipt.Success) continue;

            if (_effectsProfile == TransactionEffectsProfile.CanonicalNativeV1)
            {
                var canonical = CanonicalNativeEffectsAdapter.Derive(request.ChainId, result.Effects);
                foreach (var withdrawal in canonical.Withdrawals) withdrawalTree.Add(withdrawal);
                foreach (var message in canonical.Messages) outbox.Add(message);
                continue;
            }

            AddExecutorDeclaredEffects(result, withdrawalTree, outbox);
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

    private static void ValidateCanonicalNativeEffects(TransactionExecutionResult result)
    {
        if (result.Effects is null)
            throw new InvalidOperationException(
                $"ITransactionExecutor returned null canonical effects for tx {result.TxHash}");
        if (result.Effects.StorageChanges is null || result.Effects.Events is null)
            throw new InvalidOperationException(
                $"ITransactionExecutor returned incomplete canonical effects for tx {result.TxHash}");

        var storageHash = Effects.CanonicalEffectsHasher.HashStorage(result.Effects.StorageChanges);
        var eventsHash = Effects.CanonicalEffectsHasher.HashEvents(result.Effects.Events);
        if (!Equals(result.Effects.StorageHash, storageHash)
            || !Equals(result.Effects.EventsHash, eventsHash)
            || !Equals(result.Receipt.StorageDeltaHash, result.Effects.StorageHash)
            || !Equals(result.Receipt.EventsHash, result.Effects.EventsHash))
            throw new InvalidOperationException(
                $"ITransactionExecutor canonical effects do not match receipt {result.TxHash}");
    }

    private static void AddExecutorDeclaredEffects(
        TransactionExecutionResult result,
        WithdrawalTree withdrawalTree,
        L2Outbox outbox)
    {
        var withdrawals = result.Withdrawals
            ?? throw new InvalidOperationException(
                $"ITransactionExecutor returned null result.Withdrawals for tx {result.TxHash}");
        var messages = result.Messages
            ?? throw new InvalidOperationException(
                $"ITransactionExecutor returned null result.Messages for tx {result.TxHash}");

        for (var index = 0; index < withdrawals.Count; index++)
        {
            var withdrawal = withdrawals[index]
                ?? throw new InvalidOperationException(
                    $"ITransactionExecutor returned null result.Withdrawals[{index}] for tx {result.TxHash}");
            withdrawalTree.Add(withdrawal);
        }
        for (var index = 0; index < messages.Count; index++)
        {
            var message = messages[index]
                ?? throw new InvalidOperationException(
                    $"ITransactionExecutor returned null result.Messages[{index}] for tx {result.TxHash}");
            outbox.Add(message);
        }
    }
}

/// <summary>
/// Pluggable interface for resolving the post-batch state root. Production implementations
/// (<see cref="State.KeyedStateRootOracle"/> in-process, <c>MerkleStatePostStateRootOracle</c>
/// over an <see cref="IL2KeyValueStore"/>) walk the actual state tree after
/// all batch writes have been applied. <see cref="DerivedPostStateRootOracle"/> below is a
/// test-only XOR fixture.
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

/// <summary>
/// Test fixture: derives postStateRoot deterministically from
/// <c>preStateRoot ⊕ receiptRoot ⊕ blockContextHash</c>. Useful for
/// <see cref="ReferenceBatchExecutor"/> plumbing tests that don't need to stand up a real
/// <see cref="State.KeyedStateStore"/> / <see cref="IL2KeyValueStore"/>.
/// Production deployments inject <see cref="State.KeyedStateRootOracle"/> (in-process) or
/// <c>MerkleStatePostStateRootOracle</c> (state-store-backed) — both produce real Merkle
/// commitments over the live state and match the on-chain
/// <c>SettlementManager.VerifyStateLeafWithProof</c> reconstructor byte-for-byte.
/// </summary>
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
        // Defense-in-depth: UInt256 is a reference type; null would NRE inside GetSpan()
        // with no link to the caller. Same iter-156 hashing-primitive pattern applied to
        // StateRootCalculator. (HashBlockContext also null-guards blockContext, but
        // blockContext == null surfaces as a clearer message here.)
        ArgumentNullException.ThrowIfNull(preStateRoot);
        ArgumentNullException.ThrowIfNull(receiptRoot);
        ArgumentNullException.ThrowIfNull(blockContext);
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
/// to <c>L2MessageContract</c> + <c>L2BridgeContract</c>; test projects provide a no-op
/// fake when the L1-side mechanics aren't under test.
/// </summary>
public interface IL1MessageProcessor
{
    /// <summary>Atomically apply the ordered L1 → L2 inbox for one batch.</summary>
    ValueTask ApplyBatchAsync(
        uint chainId,
        IReadOnlyList<CrossChainMessage> messages,
        CancellationToken cancellationToken = default);
}
