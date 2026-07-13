namespace Neo.L2;

/// <summary>
/// Pluggable writer for one Data Availability destination. Public writers must return
/// mode-specific, non-empty locator and evidence metadata and be paired with an independent
/// <see cref="IDAReader"/> in production.
/// </summary>
/// <remarks>
/// See doc.md §7.4 and §12.
/// </remarks>
public interface IDAWriter
{
    /// <summary>The DA layer this writer publishes to.</summary>
    DAMode Mode { get; }

    /// <summary>The receipt evidence format emitted by this writer.</summary>
    DAReceiptKind ReceiptKind => DAReceiptKind.Unspecified;

    /// <summary>
    /// Publish <paramref name="request"/> to the DA layer and return a receipt whose
    /// <see cref="DAReceipt.Commitment"/> goes into <see cref="L2BatchCommitment.DACommitment"/>.
    /// </summary>
    ValueTask<DAReceipt> PublishAsync(
        DAPublishRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirm that data previously published via <paramref name="receipt"/> is still retrievable.
    /// </summary>
    /// <returns><c>true</c> when the layer reports the data is available.</returns>
    ValueTask<bool> IsAvailableAsync(
        DAReceipt receipt,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Explicit opt-in contract for writers suitable for a production DA profile. Implementing
/// this interface asserts that publication reaches the public backend named by
/// <see cref="IDAWriter.Mode"/> rather than an in-process or node-local simulation.
/// </summary>
/// <remarks>See doc.md §7.4, §12, and §17.</remarks>
public interface IProductionDAWriter : IDAWriter
{
}
