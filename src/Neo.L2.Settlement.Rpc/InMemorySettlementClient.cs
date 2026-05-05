using System.Collections.Concurrent;
using Neo.Cryptography;
using Neo.L2.Batch;

namespace Neo.L2.Settlement.Rpc;

/// <summary>
/// In-process <see cref="ISettlementClient"/> implementation. Stores submitted batches
/// in a concurrent dictionary, computes a deterministic L1-tx-hash from the canonical
/// commitment bytes, and supports an explicit lifecycle-advance call so tests and
/// devnets can drive batches through Pending → Challengeable → Finalized without an
/// actual L1.
/// </summary>
/// <remarks>
/// Suitable for: unit tests, in-process integration tests, and the
/// <c>neo-l2-devnet</c> demo runner. NOT for production — production wires
/// <see cref="RpcSettlementClient"/> against a real L1 node.
/// <para>
/// Idempotency: the spec requires SubmitBatchAsync to be idempotent on
/// <c>(ChainId, BatchNumber)</c>. This implementation honors that — re-submitting
/// the same (chainId, batchNumber) with the same commitment returns the cached tx hash;
/// re-submitting with a *different* commitment for the same key throws
/// <see cref="InvalidOperationException"/> because that's an attempted batch-number
/// reuse and indicates a sequencer bug.
/// </para>
/// </remarks>
public sealed class InMemorySettlementClient : ISettlementClient
{
    private readonly ConcurrentDictionary<(uint ChainId, ulong BatchNumber), Entry> _batches = new();
    private readonly ConcurrentDictionary<uint, UInt256> _canonicalRoots = new();

    private sealed record Entry(L2BatchCommitment Commitment, UInt256 TxHash, BatchStatus Status);

    /// <summary>Number of batches currently tracked across all chains.</summary>
    public int BatchCount => _batches.Count;

    /// <inheritdoc />
    public ValueTask<UInt256> SubmitBatchAsync(
        L2BatchCommitment commitment,
        PublicInputs publicInputs,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(commitment);
        ArgumentNullException.ThrowIfNull(publicInputs);

        // Deterministic L1-tx hash: Hash256 of the canonical commitment bytes. A real
        // L1 would return the actual on-chain tx hash; we substitute a value derived
        // from the same input so test consumers can compute it independently.
        var bytes = BatchSerializer.Encode(commitment);
        var txHash = new UInt256(Crypto.Hash256(bytes));

        var key = (commitment.ChainId, commitment.BatchNumber);
        var added = _batches.TryAdd(key, new Entry(commitment, txHash, BatchStatus.Pending));
        if (!added)
        {
            // Idempotency: same key + same commitment → same tx hash. Different commitment
            // for the same key is an error (batch-number reuse with different content).
            var existing = _batches[key];
            if (!existing.TxHash.Equals(txHash))
                throw new InvalidOperationException(
                    $"batch ({commitment.ChainId}, {commitment.BatchNumber}) already submitted with a different commitment — sequencer must not reuse batch numbers");
            return new ValueTask<UInt256>(existing.TxHash);
        }
        return new ValueTask<UInt256>(txHash);
    }

    /// <inheritdoc />
    public ValueTask<UInt256> GetCanonicalStateRootAsync(uint chainId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<UInt256>(_canonicalRoots.TryGetValue(chainId, out var root) ? root : UInt256.Zero);
    }

    /// <inheritdoc />
    public ValueTask<BatchStatus> GetBatchStatusAsync(uint chainId, ulong batchNumber, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<BatchStatus>(
            _batches.TryGetValue((chainId, batchNumber), out var e) ? e.Status : BatchStatus.Unknown);
    }

    /// <summary>
    /// Test/devnet helper: advance the recorded status of a previously-submitted batch.
    /// Production NeoHub does this automatically per challenge window + verifier
    /// contracts; the in-memory client requires explicit driving so tests can pin
    /// each lifecycle transition.
    /// </summary>
    /// <remarks>
    /// Allowed transitions (matches NeoHub semantics):
    /// <list type="bullet">
    ///   <item><description>Pending → Challengeable (after submission, before challenge window opens)</description></item>
    ///   <item><description>Pending → Finalized (validity proofs that don't go through optimistic challenge)</description></item>
    ///   <item><description>Challengeable → Finalized (challenge window expired without fraud proof)</description></item>
    ///   <item><description>Pending or Challengeable → Reverted (fraud proof landed or governance reverted)</description></item>
    /// </list>
    /// Any other transition (e.g., Finalized → Pending, Reverted → Finalized) throws
    /// <see cref="InvalidOperationException"/> — pinning the lifecycle invariants
    /// matches what NeoHub.SettlementManager enforces on-chain.
    /// </remarks>
    public void AdvanceStatus(uint chainId, ulong batchNumber, BatchStatus next)
    {
        var key = (chainId, batchNumber);
        if (!_batches.TryGetValue(key, out var entry))
            throw new InvalidOperationException(
                $"batch ({chainId}, {batchNumber}) is not recorded — call SubmitBatchAsync first");
        if (!IsValidTransition(entry.Status, next))
            throw new InvalidOperationException(
                $"illegal transition {entry.Status} → {next} for batch ({chainId}, {batchNumber})");

        _batches[key] = entry with { Status = next };
        // When a batch finalizes, monotonically bump the per-chain canonical state root.
        // Same iter-203 monotonicity rule as InMemoryL2RpcStore.Finalize: never regress
        // to an older root if a newer one was already finalized.
        if (next == BatchStatus.Finalized)
        {
            _canonicalRoots.AddOrUpdate(
                chainId,
                _ => entry.Commitment.PostStateRoot,
                (_, prev) =>
                {
                    // Track the highest batch number that has finalized. Without this
                    // an out-of-order Finalize call would silently regress to an older
                    // post-state root.
                    var prevBatchNumber = FindBatchNumberFor(chainId, prev);
                    return prevBatchNumber is null || batchNumber > prevBatchNumber.Value
                        ? entry.Commitment.PostStateRoot
                        : prev;
                });
        }
    }

    private ulong? FindBatchNumberFor(uint chainId, UInt256 root)
    {
        ulong? result = null;
        foreach (var ((cid, bn), e) in _batches)
        {
            if (cid == chainId && e.Status == BatchStatus.Finalized && e.Commitment.PostStateRoot.Equals(root))
            {
                if (result is null || bn > result.Value) result = bn;
            }
        }
        return result;
    }

    private static bool IsValidTransition(BatchStatus from, BatchStatus to) => (from, to) switch
    {
        (BatchStatus.Pending, BatchStatus.Challengeable) => true,
        (BatchStatus.Pending, BatchStatus.Finalized) => true,
        (BatchStatus.Pending, BatchStatus.Reverted) => true,
        (BatchStatus.Challengeable, BatchStatus.Finalized) => true,
        (BatchStatus.Challengeable, BatchStatus.Reverted) => true,
        _ => false,
    };
}
