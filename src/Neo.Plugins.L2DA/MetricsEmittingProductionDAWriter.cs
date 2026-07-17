using Neo.L2;
using Neo.L2.Telemetry;

namespace Neo.Plugins.L2;

/// <summary>
/// Production-marker wrapper: emits the same DA publish metrics as
/// <see cref="MetricsEmittingDAWriter"/> while remaining an
/// <see cref="IProductionDAWriter"/> for Validity/ZK settlement profiles.
/// </summary>
/// <remarks>
/// See doc.md §7.4 / §12. Use when the host already holds a reviewed L1/NeoFS
/// production backend and still wants mode-tagged <c>l2.da.*</c> telemetry.
/// </remarks>
public sealed class MetricsEmittingProductionDAWriter : IProductionDAWriter
{
    private readonly MetricsEmittingDAWriter _decorated;

    /// <summary>Wrap a production DA writer with metric emission.</summary>
    public MetricsEmittingProductionDAWriter(IProductionDAWriter inner, IL2Metrics metrics)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(metrics);
        _decorated = new MetricsEmittingDAWriter(inner, metrics);
    }

    /// <summary>The metrics decorator (and its <see cref="MetricsEmittingDAWriter.Inner"/> production writer).</summary>
    public MetricsEmittingDAWriter Decorated => _decorated;

    /// <inheritdoc />
    public DAMode Mode => _decorated.Mode;

    /// <inheritdoc />
    public DAReceiptKind ReceiptKind => _decorated.ReceiptKind;

    /// <inheritdoc />
    public ValueTask<DAReceipt> PublishAsync(
        DAPublishRequest request,
        CancellationToken cancellationToken = default)
        => _decorated.PublishAsync(request, cancellationToken);

    /// <inheritdoc />
    public ValueTask<bool> IsAvailableAsync(
        DAReceipt receipt,
        CancellationToken cancellationToken = default)
        => _decorated.IsAvailableAsync(receipt, cancellationToken);
}
