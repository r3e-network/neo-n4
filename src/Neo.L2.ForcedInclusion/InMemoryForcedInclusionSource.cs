using System.Buffers.Binary;
using Neo.L2.Persistence;
using Neo.L2.Telemetry;

namespace Neo.L2.ForcedInclusion;

/// <summary>
/// <see cref="IForcedInclusionSource"/> with an in-memory pending queue and a pluggable
/// <see cref="IL2KeyValueStore"/> backing for the consumed-nonce set. Production wires
/// <see cref="RocksDbKeyValueStore"/> here so replay protection survives node restarts —
/// without it, a node that restarted mid-batch could re-include a forced tx that was
/// already consumed, breaking the L1's at-most-once contract.
/// </summary>
/// <remarks>
/// Pending queue stays in-memory (re-fetchable from L1 on startup). Consumed-nonce set
/// is durability-critical: an L2 that "forgets" a consumed nonce after restart would
/// either replay the tx (incorrect — double-execution on L2) or refuse to include a
/// new tx with that nonce (incorrect — a sequencer-replay-after-restart denial).
/// </remarks>
public sealed class InMemoryForcedInclusionSource : IForcedInclusionSource, IDisposable
{
    private readonly Lock _gate = new();
    private readonly SortedDictionary<ulong, ForcedInclusionEntry> _pending = new();
    private readonly IL2KeyValueStore _consumed;
    private readonly bool _ownsConsumed;
    private readonly IL2Metrics _metrics;
    private bool _disposed;

    /// <inheritdoc />
    public uint ChainId { get; }

    /// <summary>Construct with an in-memory consumed-nonce store. Suitable for tests + devnets.</summary>
    public InMemoryForcedInclusionSource(uint chainId, IL2Metrics? metrics = null)
        : this(chainId, new InMemoryKeyValueStore(), ownsConsumed: true, metrics) { }

    /// <summary>
    /// Construct with a caller-supplied IL2KeyValueStore for the consumed-nonce set —
    /// production wires <see cref="RocksDbKeyValueStore"/> here.
    /// </summary>
    public InMemoryForcedInclusionSource(uint chainId, IL2KeyValueStore consumed, bool ownsConsumed = false, IL2Metrics? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(consumed);
        ChainId = chainId;
        _consumed = consumed;
        _ownsConsumed = ownsConsumed;
        _metrics = metrics ?? NoOpMetrics.Instance;
    }

    private static byte[] NonceKey(ulong nonce)
    {
        var key = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(key, nonce);
        // Defensive against an empty-key rejection: nonce 0 would yield 8 zero bytes,
        // which IL2KeyValueStore.Put accepts (it's only zero-LENGTH keys that are
        // rejected). Pin the assumption here so a refactor that changes the key
        // encoding has to reckon with it.
        return key;
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
            if (_consumed.Contains(NonceKey(entry.Nonce)))
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
            // Persist the consumed-nonce marker. Value byte is irrelevant — presence is
            // the signal. Replay protection across restarts depends on this Put landing.
            _consumed.Put(NonceKey(nonce), new byte[] { 0x01 });
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

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsConsumed) _consumed.Dispose();
    }
}
