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
    private readonly ConcurrentDictionary<ulong, byte> _consumed = new();

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
    /// against ready or consumed sets.
    /// </summary>
    public CrossChainMessage Enqueue(SharedBridgeDepositRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Nonce == 0)
            throw new ArgumentException("deposit nonce must be non-zero", nameof(record));
        if (_consumed.ContainsKey(record.Nonce) || _ready.ContainsKey(record.Nonce))
            throw new InvalidOperationException(
                $"deposit nonce {record.Nonce} is already tracked for chain {ChainId}");

        var message = record.ToCrossChainMessage(ChainId, _l2BridgeHash);
        if (!_ready.TryAdd(record.Nonce, message))
            throw new InvalidOperationException(
                $"deposit nonce {record.Nonce} raced into the ready set");
        return message;
    }

    /// <inheritdoc />
    public ValueTask<int> ScanAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Local source has no remote cursor — records are injected via Enqueue.
        return ValueTask.FromResult(0);
    }

    /// <inheritdoc />
    public IReadOnlyList<CrossChainMessage> Peek(int maxMessages)
    {
        if (maxMessages < 0)
            throw new ArgumentOutOfRangeException(nameof(maxMessages));
        if (maxMessages == 0)
            return Array.Empty<CrossChainMessage>();

        return _ready
            .Where(pair => !_consumed.ContainsKey(pair.Key))
            .OrderBy(pair => pair.Key)
            .Take(maxMessages)
            .Select(pair => pair.Value)
            .ToArray();
    }

    /// <inheritdoc />
    public void ConfirmConsumed(ulong nonce)
    {
        _consumed[nonce] = 1;
        _ready.TryRemove(nonce, out _);
    }

    /// <summary>Synchronous drain adapter for <c>BatchSealer</c>.</summary>
    public IReadOnlyList<CrossChainMessage> Drain(int maxMessages) => Peek(maxMessages);
}
