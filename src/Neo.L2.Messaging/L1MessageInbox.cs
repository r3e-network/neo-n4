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
    /// Messages are drained in deterministic order (source chain ID, then nonce ascending)
    /// so that two L2 nodes reconstructing the same batch from the same L1 messages produce
    /// the same <c>L1MessageHash</c> regardless of arrival timing.
    /// </remarks>
    public IReadOnlyList<CrossChainMessage> Dequeue(int max)
    {
        if (max < 0) throw new ArgumentOutOfRangeException(nameof(max));
        if (max == 0) return Array.Empty<CrossChainMessage>();
        lock (_gate)
        {
            var n = Math.Min(max, _pending.Count);
            if (n == 0) return Array.Empty<CrossChainMessage>();

            // Drain into temporary array, sort deterministically, then mark consumed.
            // This guarantees that the L1MessageHash computed over the batch's L1
            // messages is identical across all L2 nodes that see the same set of
            // messages, even if they arrived in different orders due to network
            // propagation variance.
            var tmp = new CrossChainMessage[n];
            for (var i = 0; i < n; i++)
                tmp[i] = _pending.Dequeue();
            Array.Sort(tmp, (a, b) =>
            {
                var chainCompare = a.SourceChainId.CompareTo(b.SourceChainId);
                return chainCompare != 0 ? chainCompare : a.Nonce.CompareTo(b.Nonce);
            });

            var result = new CrossChainMessage[n];
            for (var i = 0; i < n; i++)
            {
                result[i] = tmp[i];
                var key = (result[i].SourceChainId, result[i].Nonce);
                _pendingKeys.Remove(key);
                _consumed.Add(key);
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
