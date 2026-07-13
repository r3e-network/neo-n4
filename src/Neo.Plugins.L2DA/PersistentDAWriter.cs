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
/// Local deployments wire one of:
/// <list type="bullet">
///   <item><description><c>new PersistentDAWriter(new RocksDbKeyValueStore("/var/lib/neo-l2/da"))</c> — durable, multi-GB, snappy-compressed.</description></item>
///   <item><description><c>new PersistentDAWriter(new InMemoryKeyValueStore())</c> — same surface but no disk; equivalent to <see cref="InMemoryDAWriter"/>, useful when callers want a single KV-backed code path across all stores.</description></item>
/// </list>
/// See doc.md §7.4 and §12. This writer always reports <see cref="DAMode.Local"/>. A
/// local RocksDB directory provides durability only and must never be used as evidence
/// for NeoFS, L1, External, or DAC.
/// </remarks>
public sealed class PersistentDAWriter : IDAWriter, IDisposable
{
    private readonly IL2KeyValueStore _store;
    private readonly bool _ownsStore;
    private bool _disposed;

    /// <inheritdoc />
    public DAMode Mode => DAMode.Local;

    /// <inheritdoc />
    public DAReceiptKind ReceiptKind => DAReceiptKind.LocalPersistence;

    /// <summary>
    /// Construct with a KV store. Caller owns the store unless
    /// <paramref name="ownsStore"/> is true.
    /// </summary>
    public PersistentDAWriter(
        IL2KeyValueStore store,
        DAMode mode = DAMode.Local,
        bool ownsStore = false)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (mode != DAMode.Local)
            throw new ArgumentException(
                $"PersistentDAWriter is local durability and cannot advertise public DAMode {mode}",
                nameof(mode));
        _store = store;
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
            Pointer = DAReceiptFormats.CommitmentPointer(commitment),
            Evidence = DAReceiptFormats.LocalEvidence.ToArray(),
            Kind = ReceiptKind,
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
        return IsAvailableCoreAsync(receipt, cancellationToken);
    }

    /// <summary>Create a distinct reader over the same local key-value store.</summary>
    public IDAReader CreateReader()
    {
        ThrowIfDisposed();
        return new Reader(_store);
    }

    private async ValueTask<bool> IsAvailableCoreAsync(
        DAReceipt receipt,
        CancellationToken cancellationToken)
        => await CreateReader().ReadAsync(receipt, cancellationToken).ConfigureAwait(false) is not null;

    private sealed class Reader(IL2KeyValueStore store) : IDAReader
    {
        public DAMode Mode => DAMode.Local;

        public DAReceiptKind ReceiptKind => DAReceiptKind.LocalPersistence;

        public ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
            DAReceipt receipt,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(receipt);
            ArgumentNullException.ThrowIfNull(receipt.Commitment);

            var payload = store.Get(receipt.Commitment.GetSpan());
            if (payload is null
                || !DAReceiptFormats.IsContentAddressedPayload(
                    receipt,
                    Mode,
                    ReceiptKind,
                    DAReceiptFormats.LocalEvidence,
                    payload))
            {
                return new ValueTask<ReadOnlyMemory<byte>?>((ReadOnlyMemory<byte>?)null);
            }

            return new ValueTask<ReadOnlyMemory<byte>?>(payload.ToArray());
        }
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
