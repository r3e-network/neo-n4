namespace Neo.L2;

/// <summary>
/// Pluggable writer for the configured Data Availability layer. Each implementation handles a
/// single <see cref="DAMode"/> (L1, NeoFS, External, DAC).
/// </summary>
/// <remarks>
/// See doc.md §7.4 and §12.
/// </remarks>
public interface IDAWriter
{
    /// <summary>The DA layer this writer publishes to.</summary>
    DAMode Mode { get; }

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
