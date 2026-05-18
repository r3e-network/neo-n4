using Neo.Cryptography;
using Neo.L2;
using Neo.L2.Persistence;

namespace Neo.Plugins.L2;

/// <summary>
/// Durable <see cref="IDAWriter"/> backed by an <see cref="IL2KeyValueStore"/>. Hashes
/// the payload to produce the commitment (matching <see cref="InMemoryDAWriter"/>) but
/// persists the bytes to the underlying KV store so the data survives node restarts.
/// </summary>
/// <remarks>
/// Production deployments wire one of:
/// <list type="bullet">
///   <item><description><c>new PersistentDAWriter(new RocksDbKeyValueStore("/var/lib/neo-l2/da"))</c> — durable, multi-GB, snappy-compressed.</description></item>
///   <item><description><c>new PersistentDAWriter(new InMemoryKeyValueStore())</c> — same surface but no disk; equivalent to <see cref="InMemoryDAWriter"/>, useful when callers want a single KV-backed code path across all stores.</description></item>
/// </list>
/// Wire via <c>L2DAPlugin.WithWriter(new PersistentDAWriter(...))</c> before the host
/// fires Configure. Mode is configurable to align with whichever DAMode the operator
/// declared in <c>ChainRegistry</c> — the writer itself is layer-agnostic.
/// </remarks>
public sealed class PersistentDAWriter : IDAWriter, IDisposable
{
    private readonly IL2KeyValueStore _store;
    private readonly bool _ownsStore;
    private bool _disposed;

    /// <inheritdoc />
    public DAMode Mode { get; }

    /// <summary>
    /// Construct with an explicit <see cref="DAMode"/> + KV store. Caller owns the KV
    /// store unless <paramref name="ownsStore"/> is true (in which case Dispose flows
    /// through to the store).
    /// </summary>
    public PersistentDAWriter(IL2KeyValueStore store, DAMode mode = DAMode.NeoFS, bool ownsStore = false)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        Mode = mode;
        _ownsStore = ownsStore;
    }

    /// <inheritdoc />
    public ValueTask<DAReceipt> PublishAsync(DAPublishRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();

        var commitment = new UInt256(Crypto.Hash256(request.Payload.Span));
        // Defensive copy on the way in (the IL2KeyValueStore contract requires it but
        // making it explicit at this layer matches the iter-167 pattern used by all
        // other DA writers).
        _store.Put(commitment.GetSpan(), request.Payload.ToArray());

        return new ValueTask<DAReceipt>(new DAReceipt
        {
            Commitment = commitment,
            Pointer = ReadOnlyMemory<byte>.Empty,
            Layer = Mode,
        });
    }

    /// <inheritdoc />
    public ValueTask<bool> IsAvailableAsync(DAReceipt receipt, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(receipt.Commitment);
        ThrowIfDisposed();
        return new ValueTask<bool>(_store.Contains(receipt.Commitment.GetSpan()));
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PersistentDAWriter));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsStore) _store.Dispose();
    }
}
