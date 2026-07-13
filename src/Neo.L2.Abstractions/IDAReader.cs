namespace Neo.L2;

/// <summary>
/// Reads DA payloads from a receipt without relying on the publishing component's local cache.
/// Production composition roots pair an <see cref="IProductionDAWriter"/> with a distinct
/// <see cref="IProductionDAReader"/> instance.
/// </summary>
/// <remarks>See doc.md §7.4, §12, and §17.</remarks>
public interface IDAReader
{
    /// <summary>The public or local DA layer read by this component.</summary>
    DAMode Mode { get; }

    /// <summary>The receipt evidence format understood by this reader.</summary>
    DAReceiptKind ReceiptKind { get; }

    /// <summary>
    /// Retrieve and verify the payload addressed by <paramref name="receipt"/>. Returns
    /// <see langword="null"/> for unknown, malformed, mislabeled, or unverifiable receipts.
    /// </summary>
    ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
        DAReceipt receipt,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Explicit opt-in contract for an independently operated production DA reader.
/// </summary>
/// <remarks>See doc.md §7.4, §12, and §17.</remarks>
public interface IProductionDAReader : IDAReader
{
}
