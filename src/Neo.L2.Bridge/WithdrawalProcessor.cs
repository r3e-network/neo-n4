using Neo.L2.State;
using Neo.L2.Telemetry;

namespace Neo.L2.Bridge;

/// <summary>
/// Records withdrawals emitted on L2 and stages them into the per-batch
/// <see cref="WithdrawalTree"/>. Per doc.md §15.3, the tree's root is what users prove against
/// to pull canonical funds back from <c>NeoHub.SharedBridge</c>.
/// </summary>
public sealed class WithdrawalProcessor
{
    private readonly AssetRegistry _registry;
    private IL2Metrics _metrics;
    private readonly Lock _gate = new();
    private readonly Dictionary<(UInt160, ulong), int> _byNonce = new();
    // Cross-batch nonce dedup. Without this, after SealBatch clears _byNonce, a user
    // could re-stage the same (sender, nonce) in the next batch — the L2 accepts it,
    // and the duplicate is only caught hours later at L1 settlement. The
    // (chain, sender, nonce) tuple is monotonic per-sender per the WithdrawalRequest
    // contract, so we enforce uniqueness across the chain's lifetime.
    // Bounded at MaxConsumedAcrossBatches; when exceeded, evict oldest half entries
    // to prevent unbounded memory growth. L1 settlement provides final replay protection.
    private readonly HashSet<(UInt160, ulong)> _consumedAcrossBatches = new();
    private readonly Queue<(UInt160, ulong)> _consumedOrder = new();
    /// <summary>Maximum number of cross-batch consumed nonces tracked in memory.</summary>
    private const int MaxConsumedAcrossBatches = 1_000_000;
    private WithdrawalTree _tree = new();

    /// <summary>Identifier of the L2 chain this processor runs on.</summary>
    public uint LocalChainId { get; }

    /// <summary>Number of withdrawals staged in the current batch.</summary>
    public int StagedCount
    {
        get { lock (_gate) return _tree.Count; }
    }

    /// <summary>Construct.</summary>
    public WithdrawalProcessor(uint localChainId, AssetRegistry registry, IL2Metrics? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        LocalChainId = localChainId;
        _registry = registry;
        _metrics = metrics ?? NoOpMetrics.Instance;
    }

    /// <summary>Swap the metrics sink in-place. Preserves nonce-dedup + tree state, unlike re-constructing.</summary>
    public void WithMetrics(IL2Metrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        _metrics = metrics;
    }

    /// <summary>
    /// Validate and append a withdrawal. Returns the leaf hash that was inserted into the tree.
    /// </summary>
    public UInt256 Stage(WithdrawalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Defensive: WithdrawalRequest's UInt160 fields are reference types — `required`
        // forces them to be set but doesn't prevent null. A null EmittingContract /
        // L2Sender / L1Recipient / L2Asset would surface only deep in
        // MessageHasher.HashWithdrawal's GetSpan() with a NullReferenceException and no
        // link back to the bad field. Catch them at the API boundary.
        ArgumentNullException.ThrowIfNull(request.EmittingContract);
        ArgumentNullException.ThrowIfNull(request.L2Sender);
        ArgumentNullException.ThrowIfNull(request.L1Recipient);
        ArgumentNullException.ThrowIfNull(request.L2Asset);
        UInt256 leaf;
        try
        {
            if (!_registry.TryGetByL2(request.L2Asset, out var mapping) || mapping is null)
                throw new InvalidOperationException($"Unknown L2 asset {request.L2Asset}");
            if (!mapping.Active)
                throw new InvalidOperationException($"Asset {request.L2Asset} is inactive");
            if (request.Amount <= 0)
                throw new ArgumentException("Withdrawal amount must be positive", nameof(request));

            lock (_gate)
            {
                var key = (request.L2Sender, request.Nonce);
                if (_byNonce.ContainsKey(key))
                    throw new InvalidOperationException(
                        $"Withdrawal nonce {request.Nonce} already used by sender {request.L2Sender}");
                if (_consumedAcrossBatches.Contains(key))
                    throw new InvalidOperationException(
                        $"Withdrawal nonce {request.Nonce} already used by sender {request.L2Sender} in a prior batch");
                var index = _tree.Add(request);
                _byNonce[key] = index;
                leaf = MessageHasher.HashWithdrawal(request);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Metric calls are isolated so a defective sink can't double-count or
            // mask the original business exception. Same swallow-and-rethrow as the
            // success path below.
            try { _metrics.IncrementCounter(MetricNames.WithdrawalsRejected); } catch { }
            throw;
        }
        // Success counter outside the lock + outside the try: a defective metrics sink
        // would otherwise leave the staged tree mutation committed but throw to the
        // caller, who would then think the operation failed. The catch above would also
        // fire (double-counting). Now metrics failure is invisible to the caller.
        try { _metrics.IncrementCounter(MetricNames.WithdrawalsStaged); } catch { }
        return leaf;
    }

    /// <summary>
    /// Seal the current batch and return the withdrawal Merkle root + tree (so the caller can
    /// generate per-user proofs later). After sealing, a fresh tree is started.
    /// </summary>
    public (UInt256 Root, WithdrawalTree Tree) SealBatch()
    {
        lock (_gate)
        {
            var sealed_ = _tree;
            // Promote the about-to-seal batch's nonces into the cross-batch consumed
            // set BEFORE clearing _byNonce. After SealBatch returns, the next call to
            // Stage on a duplicate (sender, nonce) sees it in _consumedAcrossBatches
            // and rejects it.
            foreach (var key in _byNonce.Keys)
            {
                _consumedAcrossBatches.Add(key);
                _consumedOrder.Enqueue(key);
            }
            // Evict oldest entries if over capacity. The L1 settlement contract
            // provides definitive replay protection for finalized withdrawals,
            // so evicting here is safe — it only affects pre-settlement L2-level
            // deduplication as a defense-in-depth measure.
            while (_consumedAcrossBatches.Count > MaxConsumedAcrossBatches)
            {
                // Evict the oldest half of entries
                var toEvict = _consumedAcrossBatches.Count / 2;
                for (var i = 0; i < toEvict && _consumedOrder.Count > 0; i++)
                {
                    var old = _consumedOrder.Dequeue();
                    _consumedAcrossBatches.Remove(old);
                }
            }
            _tree = new WithdrawalTree();
            _byNonce.Clear();
            return (sealed_.Root, sealed_);
        }
    }
}
