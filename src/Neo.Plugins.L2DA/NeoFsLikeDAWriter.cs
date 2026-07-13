using System.Buffers.Binary;
using System.Collections.Concurrent;
using Neo.Cryptography;
using Neo.L2;

namespace Neo.Plugins.L2;

/// <summary>
/// Content-addressed in-process DA writer that mimics NeoFS object semantics: per-chain
/// namespace, object id = Hash256(payload), retrievable by id. It is a deterministic test
/// backend only; it does not contact NeoFS or survive process restarts.
/// </summary>
/// <remarks>
/// See doc.md §12.2. This is an explicit development-only semantic simulation; it is not
/// a NeoFS SDK client and cannot be selected by the production DA profile. The receipt's
/// <see cref="DAReceipt.Kind"/> records that limitation.
/// </remarks>
public sealed class NeoFsLikeDAWriter : IDAWriter
{
    private readonly ConcurrentDictionary<(uint chainId, UInt256 id), byte[]> _store = new();

    /// <inheritdoc />
    public DAMode Mode => DAMode.NeoFS;

    /// <inheritdoc />
    public DAReceiptKind ReceiptKind => DAReceiptKind.SemanticSimulation;

    /// <summary>Number of objects retained across all chains (test inspection helper).</summary>
    public int ObjectCount => _store.Count;

    /// <inheritdoc />
    public ValueTask<DAReceipt> PublishAsync(DAPublishRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        var objectId = new UInt256(Crypto.Hash256(request.Payload.Span));
        // Idempotent: same payload → same id; multiple writes are no-ops.
        _store[(request.ChainId, objectId)] = request.Payload.ToArray();

        // The simulation pointer encodes (4B chainId LE) + 32B objectId. A production
        // NeoFS client must emit the actual container/object locator instead.
        var pointer = new byte[4 + 32];
        BinaryPrimitives.WriteUInt32LittleEndian(pointer.AsSpan(0, 4), request.ChainId);
        objectId.GetSpan().CopyTo(pointer.AsSpan(4));

        return new ValueTask<DAReceipt>(new DAReceipt
        {
            Commitment = objectId,
            Pointer = pointer,
            Evidence = DAReceiptFormats.NeoFsSemanticEvidence.ToArray(),
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

        return IsAvailableCoreAsync(receipt, cancellationToken);
    }

    /// <summary>Create a distinct reader over the same semantic NeoFS store.</summary>
    public IDAReader CreateReader() => new Reader(_store);

    private async ValueTask<bool> IsAvailableCoreAsync(
        DAReceipt receipt,
        CancellationToken cancellationToken)
        => await CreateReader().ReadAsync(receipt, cancellationToken).ConfigureAwait(false) is not null;

    /// <summary>Retrieve a previously published payload by chain + object id (test/debug helper).</summary>
    /// <remarks>
    /// Defensive copy: the store retains the original <c>byte[]</c> reference; without
    /// the per-call clone, a debug consumer that mutated the returned bytes would
    /// silently corrupt the store. Same iter-176 pattern as
    /// <c>KeyedStateStore.EnumerateSorted</c>.
    /// </remarks>
    public ReadOnlyMemory<byte>? TryGet(uint chainId, UInt256 objectId)
    {
        ArgumentNullException.ThrowIfNull(objectId);
        return _store.TryGetValue((chainId, objectId), out var bytes)
            ? (ReadOnlyMemory<byte>?)(byte[])bytes.Clone()
            : null;
    }

    /// <summary>Drop everything (test-only convenience).</summary>
    public void Clear() => _store.Clear();

    private sealed class Reader(
        ConcurrentDictionary<(uint chainId, UInt256 id), byte[]> store) : IDAReader
    {
        public DAMode Mode => DAMode.NeoFS;

        public DAReceiptKind ReceiptKind => DAReceiptKind.SemanticSimulation;

        public ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
            DAReceipt receipt,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(receipt);
            ArgumentNullException.ThrowIfNull(receipt.Commitment);

            if (!receipt.HasRequiredMetadata(Mode, ReceiptKind)
                || receipt.Pointer.Length != 36
                || !receipt.Pointer.Span[4..].SequenceEqual(receipt.Commitment.GetSpan())
                || !receipt.Evidence.Span.SequenceEqual(DAReceiptFormats.NeoFsSemanticEvidence))
            {
                return new ValueTask<ReadOnlyMemory<byte>?>((ReadOnlyMemory<byte>?)null);
            }

            var chainId = BinaryPrimitives.ReadUInt32LittleEndian(receipt.Pointer.Span[..4]);
            if (!store.TryGetValue((chainId, receipt.Commitment), out var payload)
                || !Crypto.Hash256(payload).AsSpan().SequenceEqual(receipt.Commitment.GetSpan()))
            {
                return new ValueTask<ReadOnlyMemory<byte>?>((ReadOnlyMemory<byte>?)null);
            }

            return new ValueTask<ReadOnlyMemory<byte>?>((byte[])payload.Clone());
        }
    }
}
