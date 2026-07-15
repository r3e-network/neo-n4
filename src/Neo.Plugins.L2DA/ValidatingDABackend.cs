using Neo.Cryptography;
using Neo.L2;

namespace Neo.Plugins.L2;

internal sealed class ValidatingDAWriter(IDAWriter inner) : IDAWriter
{
    internal IDAWriter Inner => inner;

    public DAMode Mode => inner.Mode;

    public DAReceiptKind ReceiptKind => inner.ReceiptKind;

    public async ValueTask<DAReceipt> PublishAsync(
        DAPublishRequest request,
        CancellationToken cancellationToken = default)
    {
        var receipt = await inner.PublishAsync(request, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Production DA writer {inner.GetType().Name} returned a null receipt");
        if (!receipt.HasRequiredMetadata(Mode, ReceiptKind))
            throw new InvalidOperationException(
                $"Production DA writer {inner.GetType().Name} returned malformed or mislabeled {Mode}/{ReceiptKind} receipt metadata");
        if (!Crypto.Hash256(request.Payload.Span).AsSpan().SequenceEqual(receipt.Commitment.GetSpan()))
            throw new InvalidOperationException(
                $"Production DA writer {inner.GetType().Name} returned a commitment that does not bind the published payload");
        return receipt;
    }

    public ValueTask<bool> IsAvailableAsync(
        DAReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return receipt.HasRequiredMetadata(Mode, ReceiptKind)
            ? inner.IsAvailableAsync(receipt, cancellationToken)
            : ValueTask.FromResult(false);
    }
}

internal sealed class ValidatingDAReader(IDAReader inner) : IDAReader
{
    internal IDAReader Inner => inner;

    public DAMode Mode => inner.Mode;

    public DAReceiptKind ReceiptKind => inner.ReceiptKind;

    public async ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
        DAReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (!receipt.HasRequiredMetadata(Mode, ReceiptKind)) return null;

        var payload = await inner.ReadAsync(receipt, cancellationToken).ConfigureAwait(false);
        if (payload is null) return null;
        if (!Crypto.Hash256(payload.Value.Span).AsSpan().SequenceEqual(receipt.Commitment.GetSpan()))
            return null;
        return payload.Value.ToArray();
    }
}
