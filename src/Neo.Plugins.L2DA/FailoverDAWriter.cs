using Neo.Cryptography;
using Neo.L2;
using Neo.L2.Telemetry;

namespace Neo.Plugins.L2;

/// <summary>
/// Failover decorator over a set of <see cref="IDAWriter"/> endpoints that all share the
/// SAME <see cref="DAMode"/> and <see cref="DAReceiptKind"/> profile. Publishes to the first
/// endpoint and, on a transient <see cref="IOException"/> only, falls back to the next
/// endpoint in order. Closes the "production mode has no built-in fallback" gap in
/// <see cref="L2DAPlugin"/> (doc.md §12 / operator DR runbook) without ever downgrading a
/// production publish to a lower-assurance mode or receipt kind.
/// </summary>
/// <remarks>
/// Profile invariants (enforced by the constructor):
/// <list type="bullet">
/// <item>Every endpoint must report the same <see cref="IDAWriter.Mode"/> as the primary —
/// cross-mode failover is rejected so a NeoFS/L1 outage can never silently downgrade
/// production publication to <see cref="DAMode.Local"/>.</item>
/// <item>Every endpoint must report the same non-<see cref="DAReceiptKind.Unspecified"/>
/// <see cref="IDAWriter.ReceiptKind"/> so the receipts this decorator emits stay readable by
/// the reader paired with that kind.</item>
/// </list>
/// Failover policy:
/// <list type="bullet">
/// <item>Only transient <see cref="IOException"/> failures are retried on the next endpoint;
/// <see cref="InvalidDataException"/> and <see cref="TimeoutException"/> are never treated as
/// transient. Any other exception (including <see cref="OperationCanceledException"/>) exits
/// immediately — cancellation always propagates and is never masked by a fallback.</item>
/// <item>A successful publish is validated before being returned: the receipt must be
/// non-null, carry <see cref="DAReceipt.HasRequiredMetadata"/> for this profile, and bind the
/// published payload via the canonical double-SHA256 commitment
/// (<c>Crypto.Hash256(payload)</c>). A null or malformed receipt aborts the publish with
/// <see cref="InvalidOperationException"/> without trying another endpoint — a writer that
/// returns invalid evidence is misconfigured, and falling over would only duplicate the
/// invalidity.</item>
/// </list>
/// Metrics ownership: this decorator emits the per-tier <c>l2.da.published</c> /
/// <c>l2.da.publish_failures</c> counters itself, tagged with the shared profile mode.
/// Callers must NOT additionally wrap a <see cref="FailoverDAWriter"/> in
/// <see cref="MetricsEmittingDAWriter"/> — that would double-count every publish. The
/// constructor takes <see cref="IL2Metrics"/> precisely so the failover writer owns its own
/// metering; the per-endpoint writers handed in should be undecorated.
///
/// <see cref="IsAvailableAsync"/> answers <c>false</c> without probing when the receipt does
/// not match this profile (unknown layer or receipt kind); for a matching receipt it probes
/// every endpoint in order until one reports available, propagating cancellation and
/// continuing past transient <see cref="IOException"/> probe failures only.
///
/// This is the write-side failover seam only. Wiring a standby list into
/// <see cref="L2DAPlugin"/> config and provisioning the matching standalone
/// <see cref="IDAReader"/> path is a config/spec decision and is intentionally not done here.
/// </remarks>
public sealed class FailoverDAWriter : IDAWriter
{
    private readonly IReadOnlyList<IDAWriter> _tiers;
    private readonly IL2Metrics _metrics;
    private readonly (string Key, string Value) _modeTag;

    /// <summary>
    /// Wrap an ordered set of same-profile DA endpoints; the first is primary. All endpoints
    /// must share the primary's <see cref="DAMode"/> and a common non-
    /// <see cref="DAReceiptKind.Unspecified"/> <see cref="DAReceiptKind"/>.
    /// </summary>
    public FailoverDAWriter(IEnumerable<IDAWriter> tiers, IL2Metrics metrics)
    {
        ArgumentNullException.ThrowIfNull(tiers);
        ArgumentNullException.ThrowIfNull(metrics);
        var list = tiers.ToArray();
        if (list.Length == 0)
            throw new ArgumentException("at least one DA tier is required", nameof(tiers));
        for (var i = 0; i < list.Length; i++)
        {
            var tier = list[i] ?? throw new ArgumentException(
                $"tier[{i}] is null", nameof(tiers));
            if (tier.Mode != list[0].Mode)
                throw new ArgumentException(
                    $"mixed DA modes are not supported: tier[{i}] reports {tier.Mode} but the primary reports {list[0].Mode}; " +
                    "failover must never downgrade to a different DA profile", nameof(tiers));
            if (tier.ReceiptKind == DAReceiptKind.Unspecified)
                throw new ArgumentException(
                    $"tier[{i}] reports {nameof(DAReceiptKind.Unspecified)} receipt kind; " +
                    "failover endpoints must declare a concrete receipt evidence format", nameof(tiers));
            if (tier.ReceiptKind != list[0].ReceiptKind)
                throw new ArgumentException(
                    $"mixed receipt kinds are not supported: tier[{i}] reports {tier.ReceiptKind} but the primary reports {list[0].ReceiptKind}", nameof(tiers));
        }
        _tiers = list;
        _metrics = metrics;
        _modeTag = ("mode", list[0].Mode.ToString());
    }

    /// <summary>The shared profile mode (the only DA layer this failover set publishes to).</summary>
    public DAMode Mode => _tiers[0].Mode;

    /// <summary>The shared receipt evidence format emitted by every endpoint.</summary>
    public DAReceiptKind ReceiptKind => _tiers[0].ReceiptKind;

    /// <inheritdoc />
    public async ValueTask<DAReceipt> PublishAsync(DAPublishRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Exception? last = null;
        foreach (var tier in _tiers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var receipt = await tier.PublishAsync(request, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException(
                        $"DA tier {tier.GetType().Name} ({Mode}/{ReceiptKind}) returned a null receipt");
                if (!receipt.HasRequiredMetadata(Mode, ReceiptKind))
                    throw new InvalidOperationException(
                        $"DA tier {tier.GetType().Name} returned malformed or mislabeled {Mode}/{ReceiptKind} receipt metadata");
                if (!Crypto.Hash256(request.Payload.Span).AsSpan().SequenceEqual(receipt.Commitment.GetSpan()))
                    throw new InvalidOperationException(
                        $"DA tier {tier.GetType().Name} returned a commitment that does not bind the published payload");
                _metrics.SafeIncrementCounter(MetricNames.DAPublished, 1, _modeTag);
                return receipt;
            }
            catch (IOException ex) when (IsTransient(ex))
            {
                last = ex;
                _metrics.SafeIncrementCounter(MetricNames.DAPublishFailures, 1, _modeTag);
            }
        }
        throw new InvalidOperationException(
            $"all {_tiers.Count} DA tiers ({Mode}/{ReceiptKind}) failed to publish (last failure: {last!.GetType().Name})", last);
    }

    /// <inheritdoc />
    public async ValueTask<bool> IsAvailableAsync(DAReceipt receipt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        // A receipt from a different layer or evidence format was not produced by this
        // failover set; no endpoint can vouch for it, and probing would be meaningless.
        if (receipt.Layer != Mode || receipt.Kind != ReceiptKind)
            return false;
        foreach (var tier in _tiers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await tier.IsAvailableAsync(receipt, cancellationToken).ConfigureAwait(false))
                    return true;
            }
            catch (IOException ex) when (IsTransient(ex))
            {
                // Transient probe failure: this endpoint cannot answer right now; the next
                // one shares the same profile and may still be reachable.
            }
        }
        return false;
    }

    private static bool IsTransient(IOException ex)
        => (Exception)ex is not InvalidDataException;
}
