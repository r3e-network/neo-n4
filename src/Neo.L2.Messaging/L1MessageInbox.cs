namespace Neo.L2.Messaging;

/// <summary>
/// In-memory L1 → L2 message inbox. Production deployments back this with on-disk state
/// synced from <c>NeoHub.MessageRouter</c>; the in-memory variant lives here for tests and
/// the devnet boot path.
/// </summary>
/// <remarks>
/// Per doc.md §15.1, the L2 batch must include each L1 message at most once. This class
/// enforces consume-once via per-(sourceChain, nonce) tracking.
/// </remarks>
public sealed class L1MessageInbox
{
    private readonly Lock _gate = new();
    private readonly Queue<CrossChainMessage> _pending = new();
    // Mirror of _pending's (sourceChain, nonce) pairs so the duplicate-pending check in
    // Enqueue is O(1) instead of O(n) — for an inbox with thousands of pending messages
    // the prior list-scan is observable.
    private readonly HashSet<(uint, ulong)> _pendingKeys = new();
    private readonly HashSet<(uint, ulong)> _consumed = new();

    /// <summary>Number of unconsumed messages currently buffered.</summary>
    public int PendingCount
    {
        get { lock (_gate) return _pending.Count; }
    }

    /// <summary>Number of messages that have ever been consumed.</summary>
    public int ConsumedCount
    {
        get { lock (_gate) return _consumed.Count; }
    }

    /// <summary>Append a fresh inbound message. Throws if the same (source, nonce) already arrived.</summary>
    public void Enqueue(CrossChainMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        lock (_gate)
        {
            var key = (message.SourceChainId, message.Nonce);
            if (_consumed.Contains(key))
                throw new InvalidOperationException($"Message ({key.SourceChainId},{key.Nonce}) was already consumed");
            if (!_pendingKeys.Add(key))
                throw new InvalidOperationException($"Message ({key.SourceChainId},{key.Nonce}) is already pending");
            _pending.Enqueue(message);
        }
    }

    /// <summary>Drain up to <paramref name="max"/> messages and mark them consumed.</summary>
    /// <remarks>
    /// The lowest-ordered <paramref name="max"/> messages (by source chain ID, then nonce
    /// ascending) are selected across the <em>entire</em> pending set, not just the FIFO front.
    /// This guarantees that two L2 nodes reconstructing the same batch from the same set of
    /// pending L1 messages select the same subset and produce the same <c>L1MessageHash</c>,
    /// regardless of arrival timing — even when <paramref name="max"/> is smaller than the
    /// pending count (in which case sorting only the FIFO-popped front would let nodes with
    /// different arrival orders pick different subsets).
    /// </remarks>
    public IReadOnlyList<CrossChainMessage> Dequeue(int max)
    {
        if (max < 0) throw new ArgumentOutOfRangeException(nameof(max));
        if (max == 0) return Array.Empty<CrossChainMessage>();
        lock (_gate)
        {
            var pendingCount = _pending.Count;
            var n = Math.Min(max, pendingCount);
            if (n == 0) return Array.Empty<CrossChainMessage>();

            // Snapshot the pending set in FIFO order, then order a copy deterministically and take
            // the lowest n. Selecting over the whole set (not just the FIFO front) is what makes the
            // chosen subset — and therefore the L1MessageHash — identical across nodes that received
            // the same messages in different orders due to network propagation variance.
            var fifo = _pending.ToArray();
            var sorted = (CrossChainMessage[])fifo.Clone();
            Array.Sort(sorted, static (a, b) =>
            {
                var chainCompare = a.SourceChainId.CompareTo(b.SourceChainId);
                return chainCompare != 0 ? chainCompare : a.Nonce.CompareTo(b.Nonce);
            });

            var result = new CrossChainMessage[n];
            var selected = new HashSet<(uint, ulong)>();
            for (var i = 0; i < n; i++)
            {
                result[i] = sorted[i];
                var key = (result[i].SourceChainId, result[i].Nonce);
                selected.Add(key);
                _pendingKeys.Remove(key);
                _consumed.Add(key);
            }

            // Rebuild the queue with the non-selected messages, preserving their original FIFO order.
            _pending.Clear();
            if (n < pendingCount)
            {
                foreach (var msg in fifo)
                {
                    if (!selected.Contains((msg.SourceChainId, msg.Nonce)))
                        _pending.Enqueue(msg);
                }
            }
            return result;
        }
    }

    /// <summary>True if a (source, nonce) pair has been consumed by some prior call to <see cref="Dequeue"/>.</summary>
    public bool HasConsumed(uint sourceChainId, ulong nonce)
    {
        lock (_gate)
            return _consumed.Contains((sourceChainId, nonce));
    }
}
