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
                var index = _tree.Add(request);
                _byNonce[key] = index;
                var leaf = MessageHasher.HashWithdrawal(request);
                _metrics.IncrementCounter(MetricNames.WithdrawalsStaged);
                return leaf;
            }
        }
        catch
        {
            _metrics.IncrementCounter(MetricNames.WithdrawalsRejected);
            throw;
        }
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
            _tree = new WithdrawalTree();
            _byNonce.Clear();
            return (sealed_.Root, sealed_);
        }
    }
}
