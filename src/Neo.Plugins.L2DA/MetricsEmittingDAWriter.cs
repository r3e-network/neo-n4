using Neo.L2;
using Neo.L2.Telemetry;

namespace Neo.Plugins.L2;

/// <summary>
/// Decorates an <see cref="IDAWriter"/> with metric emission on publish. Counts
/// successful publishes, latency, and failures, all tagged by <c>mode</c> so a single
/// dashboard can compare DA layers. Pass-through for <see cref="IsAvailableAsync"/>.
/// </summary>
/// <remarks>
/// Composition pattern: any new <c>IDAWriter</c> automatically participates in the
/// metric contract by being wrapped in this decorator at <see cref="L2DAPlugin"/>
/// configure time.
/// </remarks>
public sealed class MetricsEmittingDAWriter : IDAWriter
{
    private readonly IDAWriter _inner;
    private readonly IL2Metrics _metrics;
    private readonly (string Key, string Value) _modeTag;

    /// <summary>Wrap <paramref name="inner"/> so its publishes flow through <paramref name="metrics"/>.</summary>
    public MetricsEmittingDAWriter(IDAWriter inner, IL2Metrics metrics)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(metrics);
        _inner = inner;
        _metrics = metrics;
        _modeTag = ("mode", inner.Mode.ToString());
    }

    /// <summary>The wrapped writer (escape hatch for tests / inspection).</summary>
    public IDAWriter Inner => _inner;

    /// <inheritdoc />
    public DAMode Mode => _inner.Mode;

    /// <inheritdoc />
    public async ValueTask<DAReceipt> PublishAsync(DAPublishRequest request, CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        DAReceipt receipt;
        try
        {
            receipt = await _inner.PublishAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            sw.Stop();
            _metrics.SafeIncrementCounter(MetricNames.DAPublishFailures, 1, _modeTag);
            throw;
        }
        // Defensive: a buggy IDAWriter that returns null would propagate as a NRE deep
        // in the caller (e.g. consumer dereferencing receipt.Commitment). Surface as a
        // clear contract violation instead. Same iter-171/172 callee-contract pattern.
        // Bumps the failure metric so operators still see something in the dashboard.
        if (receipt is null)
        {
            sw.Stop();
            _metrics.SafeIncrementCounter(MetricNames.DAPublishFailures, 1, _modeTag);
            throw new InvalidOperationException(
                $"IDAWriter.PublishAsync returned null (mode={_modeTag.Value})");
        }
        sw.Stop();
        // Success metrics outside the try: a metric throw here would otherwise be caught
        // as a "publish failure" — a worst-case false alarm where the DA blob WAS
        // published (downstream relayers will pick it up) but our metrics + the caller
        // both say it failed, prompting a re-publish that double-pays the DA layer.
        _metrics.SafeIncrementCounter(MetricNames.DAPublished, 1, _modeTag);
        _metrics.SafeRecordHistogram(MetricNames.DAPublishLatencyMs, sw.Elapsed.TotalMilliseconds, _modeTag);
        return receipt;
    }

    /// <inheritdoc />
    public ValueTask<bool> IsAvailableAsync(DAReceipt receipt, CancellationToken cancellationToken = default)
        => _inner.IsAvailableAsync(receipt, cancellationToken);
}
