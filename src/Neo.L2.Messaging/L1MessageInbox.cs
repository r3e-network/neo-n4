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
            foreach (var existing in _pending)
            {
                if (existing.SourceChainId == message.SourceChainId && existing.Nonce == message.Nonce)
                    throw new InvalidOperationException($"Message ({key.SourceChainId},{key.Nonce}) is already pending");
            }
            _pending.Enqueue(message);
        }
    }

    /// <summary>Drain up to <paramref name="max"/> messages and mark them consumed.</summary>
    public IReadOnlyList<CrossChainMessage> Dequeue(int max)
    {
        if (max < 0) throw new ArgumentOutOfRangeException(nameof(max));
        if (max == 0) return Array.Empty<CrossChainMessage>();
        lock (_gate)
        {
            var n = Math.Min(max, _pending.Count);
            if (n == 0) return Array.Empty<CrossChainMessage>();
            var result = new CrossChainMessage[n];
            for (var i = 0; i < n; i++)
            {
                result[i] = _pending.Dequeue();
                _consumed.Add((result[i].SourceChainId, result[i].Nonce));
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
