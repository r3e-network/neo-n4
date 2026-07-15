using System.Collections.Concurrent;
using Neo.L2.Persistence;
using Neo.L2.Settlement.Rpc;

namespace Neo.L2.Bridge;

/// <summary>
/// Production <see cref="ISharedBridgeDepositSource"/>: durable event discovery plus
/// <c>GetDeposit</c> materialization into canonical L1→L2 deposit messages.
/// </summary>
/// <remarks>
/// Wire into <c>L2BatchPlugin</c> via <c>WireL1MessageInbox</c> / <c>WithDepositSource</c> so
/// SharedBridge deposits enter the batcher L1 inbox with reserve → durable-seal confirm
/// lifecycle. Operators must call <see cref="ScanAsync"/> on a poll loop before sealing
/// batches that should include newly finalized deposits.
/// </remarks>
public sealed class RpcSharedBridgeDepositSource : ISharedBridgeDepositSource, IDisposable
{
    private readonly JsonRpcClient _rpc;
    private readonly UInt160 _sharedBridgeHash;
    private readonly UInt160 _l2BridgeHash;
    private readonly RpcSharedBridgeDepositScanner _scanner;
    private readonly ConcurrentDictionary<ulong, CrossChainMessage> _ready = new();
    private readonly ConcurrentDictionary<ulong, CrossChainMessage> _reserved = new();
    private readonly ConcurrentDictionary<ulong, byte> _consumed = new();
    private readonly Lock _gate = new();
    private readonly IL2KeyValueStore _store;
    private readonly bool _ownsRpc;
    private readonly bool _ownsStore;
    private int _disposed;

    /// <summary>Construct a deposit source for one L2 chain.</summary>
    public RpcSharedBridgeDepositSource(
        JsonRpcClient rpc,
        UInt160 sharedBridgeHash,
        uint chainId,
        UInt160 l2BridgeHash,
        IL2KeyValueStore store,
        uint startHeight,
        uint finalityDepth = 1,
        int maximumBlocksPerScan = 256,
        bool ownsRpc = false,
        bool ownsStore = false)
    {
        ArgumentNullException.ThrowIfNull(rpc);
        ArgumentNullException.ThrowIfNull(sharedBridgeHash);
        ArgumentNullException.ThrowIfNull(l2BridgeHash);
        ArgumentNullException.ThrowIfNull(store);
        if (sharedBridgeHash.Equals(UInt160.Zero))
            throw new ArgumentException("SharedBridge hash must be non-zero", nameof(sharedBridgeHash));
        if (l2BridgeHash.Equals(UInt160.Zero))
            throw new ArgumentException("L2 bridge hash must be non-zero", nameof(l2BridgeHash));
        if (chainId == 0)
            throw new ArgumentOutOfRangeException(nameof(chainId), "chain id must be non-zero");

        _rpc = rpc;
        _sharedBridgeHash = sharedBridgeHash;
        _l2BridgeHash = l2BridgeHash;
        _store = store;
        _ownsRpc = ownsRpc;
        _ownsStore = ownsStore;
        ChainId = chainId;
        _scanner = new RpcSharedBridgeDepositScanner(
            rpc,
            sharedBridgeHash,
            chainId,
            store,
            startHeight,
            finalityDepth,
            maximumBlocksPerScan);
    }

    /// <inheritdoc />
    public uint ChainId { get; }

    /// <inheritdoc />
    public async ValueTask<int> ScanAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var discovered = new List<ulong>();
        var scanned = await _scanner.ScanAsync(discovered.Add, cancellationToken).ConfigureAwait(false);

        foreach (var nonce in _scanner.LoadTrackedNonces())
        {
            if (!_ready.ContainsKey(nonce)
                && !_reserved.ContainsKey(nonce)
                && !_consumed.ContainsKey(nonce))
                discovered.Add(nonce);
        }

        foreach (var nonce in discovered.Distinct().OrderBy(n => n))
            await MaterializeAsync(nonce, cancellationToken).ConfigureAwait(false);
        return scanned;
    }

    /// <inheritdoc />
    public IReadOnlyList<CrossChainMessage> Peek(int maxMessages)
    {
        ThrowIfDisposed();
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
        ThrowIfDisposed();
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
                var nonce = selected[i].Key;
                var message = selected[i].Value;
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
        ThrowIfDisposed();
        lock (_gate)
        {
            _ready.TryRemove(nonce, out _);
            _reserved.TryRemove(nonce, out _);
            _consumed[nonce] = 1;
        }
        _scanner.ForgetNonce(nonce);
    }

    /// <inheritdoc />
    public void ReleaseReservations(IEnumerable<ulong> nonces)
    {
        ArgumentNullException.ThrowIfNull(nonces);
        ThrowIfDisposed();
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

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _scanner.Dispose();
        if (_ownsRpc) _rpc.Dispose();
        if (_ownsStore) _store.Dispose();
    }

    private async ValueTask MaterializeAsync(ulong nonce, CancellationToken cancellationToken)
    {
        if (_consumed.ContainsKey(nonce)
            || _ready.ContainsKey(nonce)
            || _reserved.ContainsKey(nonce))
            return;

        var raw = await RpcContractReader.InvokeReadAsync(
            _rpc,
            _sharedBridgeHash,
            "getDeposit",
            new object[] { ChainId, nonce },
            cancellationToken).ConfigureAwait(false);
        var bytes = RpcContractReader.ParseByteArray(raw);
        if (bytes.Length == 0)
            throw new InvalidDataException(
                $"SharedBridge getDeposit({ChainId},{nonce}) returned empty bytes after DepositEnqueued");

        var record = SharedBridgeDepositRecord.Decode(bytes);
        if (record.Nonce != nonce)
            throw new InvalidDataException(
                $"SharedBridge getDeposit({ChainId},{nonce}) returned record nonce {record.Nonce}");

        var message = record.ToCrossChainMessage(ChainId, _l2BridgeHash);
        lock (_gate)
        {
            if (_consumed.ContainsKey(nonce) || _reserved.ContainsKey(nonce))
                return;
            _ready[nonce] = message;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }
}
