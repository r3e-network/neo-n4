using System.Collections.Concurrent;
using Neo.Cryptography;
using Neo.L2;

namespace Neo.Plugins.L2;

/// <summary>
/// Process-local <see cref="IDAWriter"/> for tests, devnets, and single-node demos.
/// Hashes the payload to produce the commitment; retains the bytes in a concurrent
/// dictionary so <see cref="IsAvailableAsync"/> can answer truthfully.
/// </summary>
/// <remarks>
/// See doc.md §7.4 and §12. This writer reports <see cref="DAMode.Local"/> and can never
/// satisfy a public DA security label.
/// </remarks>
public sealed class InMemoryDAWriter : IDAWriter
{
    private readonly ConcurrentDictionary<UInt256, byte[]> _store = new();

    /// <inheritdoc />
    public DAMode Mode => DAMode.Local;

    /// <inheritdoc />
    public DAReceiptKind ReceiptKind => DAReceiptKind.LocalPersistence;

    /// <inheritdoc />
    public ValueTask<DAReceipt> PublishAsync(DAPublishRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        var commitment = new UInt256(Crypto.Hash256(request.Payload.Span));
        _store[commitment] = request.Payload.ToArray();

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
        // Commitment is UInt256 (reference type); `required` doesn't prevent null.
        // Without this guard, ContainsKey(null) throws ArgumentNullException with a
        // generic "key" message. Same iter-148/183/184 pattern.
        ArgumentNullException.ThrowIfNull(receipt.Commitment);
        return IsAvailableCoreAsync(receipt, cancellationToken);
    }

    /// <summary>Create a distinct reader over the same development-only in-memory store.</summary>
    public IDAReader CreateReader() => new Reader(_store);

    private async ValueTask<bool> IsAvailableCoreAsync(
        DAReceipt receipt,
        CancellationToken cancellationToken)
        => await CreateReader().ReadAsync(receipt, cancellationToken).ConfigureAwait(false) is not null;

    private sealed class Reader(ConcurrentDictionary<UInt256, byte[]> store) : IDAReader
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

            if (!store.TryGetValue(receipt.Commitment, out var payload)
                || !DAReceiptFormats.IsContentAddressedPayload(
                    receipt,
                    Mode,
                    ReceiptKind,
                    DAReceiptFormats.LocalEvidence,
                    payload))
            {
                return new ValueTask<ReadOnlyMemory<byte>?>((ReadOnlyMemory<byte>?)null);
            }

            return new ValueTask<ReadOnlyMemory<byte>?>((byte[])payload.Clone());
        }
    }
}
