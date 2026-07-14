using System.Numerics;
using Neo.L2.ForcedInclusion;
using Neo.L2.Sequencer;
using Neo.L2.Telemetry;

namespace Neo.L2.Censorship;

/// <summary>
/// Off-chain orchestrator: poll the forced-inclusion source for overdue entries and emit
/// <see cref="CensorshipReport"/> records that an operator can submit on L1. It attributes a
/// report only when an explicit finalized-consensus resolver supplies evidence.
/// </summary>
/// <remarks>
/// See doc.md §15.4 (forced inclusion) and §17 (sequencer-censorship mitigations).
/// <para>
/// Operates against pluggable interfaces — <see cref="IForcedInclusionSource"/> for the queue
/// and <see cref="ISequencerCommitteeProvider"/> for the committee — so it works with both the
/// in-memory test backends and the production L1-RPC-backed implementations. Sequencer
/// attribution is a separate capability because committee membership does not prove which dBFT
/// proposer was responsible for a particular deadline.
/// </para>
/// </remarks>
public sealed class CensorshipDetector
{
    private readonly IForcedInclusionSource _source;
    private readonly ISequencerCommitteeProvider _committee;
    private readonly IClock _clock;
    private readonly BigInteger _baseSlashAmount;
    private readonly IL2Metrics _metrics;
    private readonly ICensorshipAttributionProvider? _attributionProvider;

    /// <summary>Default slash amount — 1.0 GAS at 8 decimal places.</summary>
    public static readonly BigInteger DefaultSlashAmount = new(1_000_000);

    /// <summary>Amount to slash (default policy: fixed per overdue entry).</summary>
    public BigInteger BaseSlashAmount => _baseSlashAmount;

    /// <summary>Construct.</summary>
    public CensorshipDetector(
        IForcedInclusionSource source,
        ISequencerCommitteeProvider committee,
        IClock? clock = null,
        BigInteger? baseSlashAmount = null,
        IL2Metrics? metrics = null,
        ICensorshipAttributionProvider? attributionProvider = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(committee);
        _source = source;
        _committee = committee;
        if (source.ChainId != committee.ChainId)
            throw new ArgumentException(
                $"Forced-inclusion chain {source.ChainId} does not match committee chain {committee.ChainId}.",
                nameof(committee));
        _clock = clock ?? new SystemClock();
        _baseSlashAmount = baseSlashAmount ?? DefaultSlashAmount;
        // A negative slash amount on L1 would effectively reward the offending sequencer
        // — clearly nonsensical. Zero is allowed for warning-only modes (operator wants
        // detection without enforcement). Reject negative outright at construction so the
        // misconfig surfaces here, not at L1 settlement when a slash transaction reverts.
        if (_baseSlashAmount < BigInteger.Zero)
            throw new ArgumentOutOfRangeException(nameof(baseSlashAmount),
                $"BaseSlashAmount must be non-negative, got {_baseSlashAmount}");
        _metrics = metrics ?? NoOpMetrics.Instance;
        _attributionProvider = attributionProvider;
    }

    /// <summary>Maximum number of forced-inclusion entries scanned per detect pass.</summary>
    private const int MaxEntriesPerScan = 1000;

    /// <summary>
    /// Inspect the queue once and emit reports for every entry whose deadline has passed.
    /// The detector does NOT consume the queue (that's the batcher's job once the sequencer
    /// returns); reports are advisory until the operator submits them on L1.
    /// </summary>
    public async ValueTask<IReadOnlyList<CensorshipReport>> DetectOverdueAsync(CancellationToken cancellationToken = default)
    {
        var nowSeconds = _clock.NowUnixSeconds;
        var hasOverdue = await _source.HasOverdueEntryAsync(nowSeconds, cancellationToken).ConfigureAwait(false);
        if (!hasOverdue) return Array.Empty<CensorshipReport>();

        // Drain up to a capped number of candidates so an attacker who enqueues
        // thousands of entries can't cause unbounded memory allocation on every
        // scan tick. The batcher drains the rest in the next block.
        var entries = await _source.DrainAsync(MaxEntriesPerScan, cancellationToken).ConfigureAwait(false);
        // Defensive: a buggy IForcedInclusionSource that returns null would NRE in the
        // foreach below with no link to the source's contract violation. Same iter-171
        // pattern: surface the bad return value as InvalidOperationException with the
        // contract method name.
        if (entries is null)
            throw new InvalidOperationException("IForcedInclusionSource.DrainAsync returned null");
        var reports = new List<CensorshipReport>();

        var committee = await _committee.GetActiveCommitteeAsync(cancellationToken).ConfigureAwait(false);
        if (committee is null)
            throw new InvalidOperationException(
                "ISequencerCommitteeProvider.GetActiveCommitteeAsync returned null");
        foreach (var e in entries)
        {
            if (nowSeconds < e.DeadlineUnixSeconds) continue;
            var responsible = await ResolveResponsibleSequencerAsync(
                e,
                nowSeconds,
                committee,
                cancellationToken).ConfigureAwait(false);
            reports.Add(new CensorshipReport
            {
                ChainId = _source.ChainId,
                ForcedInclusionNonce = e.Nonce,
                OverdueTxHash = e.TxHash,
                DeadlineUnixSeconds = e.DeadlineUnixSeconds,
                ResponsibleSequencer = responsible?.PublicKey ?? Cryptography.ECC.ECCurve.Secp256r1.Infinity,
                ResponsibleSequencerAddress = responsible?.L1Address ?? UInt160.Zero,
                SlashAmount = _baseSlashAmount,
            });
        }
        if (reports.Count > 0)
            _metrics.SafeIncrementCounter(MetricNames.CensorshipReports, reports.Count);
        return reports;
    }

    private async ValueTask<CommitteeMember?> ResolveResponsibleSequencerAsync(
        ForcedInclusionEntry entry,
        uint observedAtUnixSeconds,
        IReadOnlyList<CommitteeMember> committee,
        CancellationToken cancellationToken)
    {
        if (_attributionProvider is null || committee.Count == 0) return null;

        var attributed = await _attributionProvider.ResolveResponsibleSequencerAsync(
            _source.ChainId,
            entry,
            observedAtUnixSeconds,
            cancellationToken).ConfigureAwait(false);
        if (attributed is null) return null;

        var canonical = committee.FirstOrDefault(member => member.PublicKey == attributed.PublicKey);
        if (canonical is null || canonical.L1Address != attributed.L1Address)
            throw new InvalidOperationException(
                "ICensorshipAttributionProvider returned a sequencer outside the active committee snapshot");
        return canonical;
    }
}

/// <summary>Pluggable clock so tests can advance time without sleeping.</summary>
public interface IClock
{
    /// <summary>Current Unix timestamp in seconds.</summary>
    uint NowUnixSeconds { get; }
}

/// <summary>Real wall clock.</summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public uint NowUnixSeconds => (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}

/// <summary>Test clock — caller sets <see cref="NowUnixSeconds"/> directly.</summary>
public sealed class FakeClock : IClock
{
    /// <inheritdoc />
    public uint NowUnixSeconds { get; set; }
}
