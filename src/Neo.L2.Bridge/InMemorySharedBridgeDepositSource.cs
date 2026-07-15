using System.Collections.Concurrent;

namespace Neo.L2.Bridge;

/// <summary>
/// In-process <see cref="ISharedBridgeDepositSource"/> for tests and the local devnet.
/// Operators inject pre-built <see cref="SharedBridgeDepositRecord"/> values the same way
/// production materializes them from <c>GetDeposit</c>.
/// </summary>
public sealed class InMemorySharedBridgeDepositSource : ISharedBridgeDepositSource
{
    private readonly UInt160 _l2BridgeHash;
    private readonly ConcurrentDictionary<ulong, CrossChainMessage> _ready = new();
    private readonly ConcurrentDictionary<ulong, CrossChainMessage> _reserved = new();
    private readonly ConcurrentDictionary<ulong, byte> _consumed = new();
    private readonly Lock _gate = new();

    /// <summary>Construct for one L2 chain.</summary>
    public InMemorySharedBridgeDepositSource(uint chainId, UInt160 l2BridgeHash)
    {
        ArgumentNullException.ThrowIfNull(l2BridgeHash);
        if (chainId == 0)
            throw new ArgumentOutOfRangeException(nameof(chainId), "chain id 0 is reserved for L1");
        if (l2BridgeHash.Equals(UInt160.Zero))
            throw new ArgumentException("L2 bridge hash must be non-zero", nameof(l2BridgeHash));
        ChainId = chainId;
        _l2BridgeHash = l2BridgeHash;
    }

    /// <inheritdoc />
    public uint ChainId { get; }

    /// <summary>
    /// Enqueue a deposit as if SharedBridge had just finalized it. Throws on nonce replay
    /// against ready, reserved, or consumed sets.
    /// </summary>
    public CrossChainMessage Enqueue(SharedBridgeDepositRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Nonce == 0)
            throw new ArgumentException("deposit nonce must be non-zero", nameof(record));

        var message = record.ToCrossChainMessage(ChainId, _l2BridgeHash);
        lock (_gate)
        {
            if (_consumed.ContainsKey(record.Nonce)
                || _ready.ContainsKey(record.Nonce)
                || _reserved.ContainsKey(record.Nonce))
                throw new InvalidOperationException(
                    $"deposit nonce {record.Nonce} is already tracked for chain {ChainId}");
            _ready[record.Nonce] = message;
        }
        return message;
    }

    /// <inheritdoc />
    public ValueTask<int> ScanAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(0);
    }

    /// <inheritdoc />
    public IReadOnlyList<CrossChainMessage> Peek(int maxMessages)
    {
        if (maxMessages < 0)
            throw new ArgumentOutOfRangeException(nameof(maxMessages));
        if (maxMessages == 0)
            return Array.Empty<CrossChainMessage>();

        lock (_gate)
        {
            return _ready
                .OrderBy(pair => pair.Key)
                .Take(maxMessages)
                .Select(pair => pair.Value)
                .ToArray();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<CrossChainMessage> Drain(int maxMessages)
    {
        if (maxMessages < 0)
            throw new ArgumentOutOfRangeException(nameof(maxMessages));
        if (maxMessages == 0)
            return Array.Empty<CrossChainMessage>();

        lock (_gate)
        {
            var selected = _ready
                .OrderBy(pair => pair.Key)
                .Take(maxMessages)
                .ToArray();
            var result = new CrossChainMessage[selected.Length];
            for (var i = 0; i < selected.Length; i++)
            {
                var (nonce, message) = (selected[i].Key, selected[i].Value);
                if (!_ready.TryRemove(nonce, out _))
                    throw new InvalidOperationException(
                        $"deposit nonce {nonce} disappeared during drain");
                _reserved[nonce] = message;
                result[i] = message;
            }
            return result;
        }
    }

    /// <inheritdoc />
    public void ConfirmConsumed(ulong nonce)
    {
        lock (_gate)
        {
            _ready.TryRemove(nonce, out _);
            _reserved.TryRemove(nonce, out _);
            _consumed[nonce] = 1;
        }
    }

    /// <inheritdoc />
    public void ReleaseReservations(IEnumerable<ulong> nonces)
    {
        ArgumentNullException.ThrowIfNull(nonces);
        lock (_gate)
        {
            foreach (var nonce in nonces)
            {
                if (_consumed.ContainsKey(nonce))
                    continue;
                if (_reserved.TryRemove(nonce, out var message))
                    _ready[nonce] = message;
            }
        }
    }
}
