using Neo.L2.Telemetry;

namespace Neo.L2.ForcedInclusion;

/// <summary>
/// In-memory <see cref="IForcedInclusionSource"/> for tests + devnet. Production wires the
/// L1-RPC-backed implementation (which polls <c>NeoHub.ForcedInclusion.GetEntry</c>).
/// </summary>
public sealed class InMemoryForcedInclusionSource : IForcedInclusionSource
{
    private readonly Lock _gate = new();
    private readonly SortedDictionary<ulong, ForcedInclusionEntry> _pending = new();
    private readonly HashSet<ulong> _consumed = new();
    private readonly IL2Metrics _metrics;

    /// <inheritdoc />
    public uint ChainId { get; }

    /// <summary>Construct against a chain id, optionally wired to a metrics sink.</summary>
    public InMemoryForcedInclusionSource(uint chainId, IL2Metrics? metrics = null)
    {
        ChainId = chainId;
        _metrics = metrics ?? NoOpMetrics.Instance;
    }

    /// <summary>Number of entries that have not yet been drained or consumed.</summary>
    public int PendingCount
    {
        get { lock (_gate) return _pending.Count; }
    }

    /// <summary>Add an entry to the queue (simulates an L1-side enqueue).</summary>
    public void Enqueue(ForcedInclusionEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate)
        {
            if (_consumed.Contains(entry.Nonce))
                throw new InvalidOperationException($"nonce {entry.Nonce} already consumed");
            if (!_pending.TryAdd(entry.Nonce, entry))
                throw new InvalidOperationException($"nonce {entry.Nonce} already pending");
        }
        // SafeIncrementCounter: the enqueue is committed before the metric fires; a
        // metric throw must not surface as a caller-visible "enqueue failed". See
        // iter-162/163 fix.
        _metrics.SafeIncrementCounter(MetricNames.ForcedInclusionObserved);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ForcedInclusionEntry>> DrainAsync(int max, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (max <= 0) return new ValueTask<IReadOnlyList<ForcedInclusionEntry>>(Array.Empty<ForcedInclusionEntry>());
        lock (_gate)
        {
            var n = Math.Min(max, _pending.Count);
            if (n == 0) return new ValueTask<IReadOnlyList<ForcedInclusionEntry>>(Array.Empty<ForcedInclusionEntry>());
            var result = new ForcedInclusionEntry[n];
            var i = 0;
            foreach (var (nonce, entry) in _pending)
            {
                if (i == n) break;
                result[i++] = entry;
            }
            return new ValueTask<IReadOnlyList<ForcedInclusionEntry>>(result);
        }
    }

    /// <inheritdoc />
    public ValueTask MarkConsumedAsync(ulong nonce, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_pending.Remove(nonce))
                throw new InvalidOperationException($"nonce {nonce} not pending");
            _consumed.Add(nonce);
        }
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<bool> HasOverdueEntryAsync(uint nowUnixSeconds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            foreach (var entry in _pending.Values)
            {
                if (nowUnixSeconds >= entry.DeadlineUnixSeconds) return new ValueTask<bool>(true);
            }
            return new ValueTask<bool>(false);
        }
    }
}
