using Neo.Cryptography.ECC;
using Neo.L2.Telemetry;

namespace Neo.L2.Sequencer;

/// <summary>
/// In-memory <see cref="ISequencerCommitteeProvider"/> for tests + devnet. Production wires
/// the L1-RPC-backed implementation that polls <c>NeoHub.SequencerRegistry</c>.
/// </summary>
public sealed class InMemorySequencerCommitteeProvider : ISequencerCommitteeProvider
{
    private readonly Lock _gate = new();
    private readonly Dictionary<ECPoint, CommitteeMember> _members = new();
    private readonly IL2Metrics _metrics;
    private int _maxSize;

    /// <inheritdoc />
    public uint ChainId { get; }

    /// <summary>Construct against a chain id and committee size.</summary>
    public InMemorySequencerCommitteeProvider(uint chainId, int maxCommitteeSize = 21, IL2Metrics? metrics = null)
    {
        ChainId = chainId;
        _maxSize = maxCommitteeSize;
        _metrics = metrics ?? NoOpMetrics.Instance;
    }

    /// <summary>Add a member with status=Active. Throws if already present or committee is full.</summary>
    public void Register(ECPoint pubKey, UInt160 l1Address)
    {
        ArgumentNullException.ThrowIfNull(pubKey);
        // l1Address feeds into CensorshipReport.ResponsibleSequencerAddress and the slash
        // payout claim — null would silently propagate to L1 submission and either
        // misroute the slash or hard-revert the slash transaction.
        ArgumentNullException.ThrowIfNull(l1Address);
        int newSize;
        lock (_gate)
        {
            if (_members.ContainsKey(pubKey))
                throw new InvalidOperationException($"already registered: {pubKey}");
            if (_members.Count >= _maxSize)
                throw new InvalidOperationException($"committee full ({_maxSize})");
            _members[pubKey] = new CommitteeMember
            {
                PublicKey = pubKey,
                L1Address = l1Address,
                Status = 1, // Active
                ExitsAtUnixSeconds = 0,
            };
            newSize = _members.Count;
        }
        // Safe* wrappers: if the metrics sink throws, the registration is already
        // committed — bubbling that exception would make the caller assume failure
        // and try to re-register, which would then throw "already registered". See
        // iter-162/163 fix.
        _metrics.SafeIncrementCounter(MetricNames.SequencersRegistered);
        _metrics.SafeSetGauge(MetricNames.SequencerCommitteeSize, newSize);
    }

    /// <summary>Mark a member as exiting; their entry stays in the committee until <see cref="Finalize"/>.</summary>
    public void BeginExit(ECPoint pubKey, uint exitsAtUnixSeconds)
    {
        ArgumentNullException.ThrowIfNull(pubKey);
        lock (_gate)
        {
            if (!_members.TryGetValue(pubKey, out var member))
                throw new InvalidOperationException($"not registered: {pubKey}");
            if (member.Status != 1)
                throw new InvalidOperationException("already exiting");
            _members[pubKey] = member with { Status = 2, ExitsAtUnixSeconds = exitsAtUnixSeconds };
        }
        _metrics.SafeIncrementCounter(MetricNames.SequencerExitsStarted);
    }

    /// <summary>Permanently remove an exiting member after their window has elapsed.</summary>
    public void Finalize(ECPoint pubKey, uint nowUnixSeconds)
    {
        ArgumentNullException.ThrowIfNull(pubKey);
        int newSize;
        lock (_gate)
        {
            if (!_members.TryGetValue(pubKey, out var member))
                throw new InvalidOperationException($"not registered: {pubKey}");
            if (member.Status != 2)
                throw new InvalidOperationException("not exiting");
            if (nowUnixSeconds < member.ExitsAtUnixSeconds)
                throw new InvalidOperationException("exit window still open");
            _members.Remove(pubKey);
            newSize = _members.Count;
        }
        _metrics.SafeIncrementCounter(MetricNames.SequencerExitsFinalized);
        _metrics.SafeSetGauge(MetricNames.SequencerCommitteeSize, newSize);
    }

    /// <summary>Update the configured max committee size.</summary>
    /// <remarks>
    /// Rejects values smaller than the current member count. Without that check, an operator
    /// could shrink the cap below the existing committee, leaving a state where the count
    /// silently exceeds the cap until members exit organically — a misleading "almost-frozen"
    /// configuration that hides the misconfig. Callers who genuinely want to freeze new
    /// registrations should add a separate explicit API rather than abusing this setter.
    /// </remarks>
    public void SetMaxCommitteeSize(int max)
    {
        if (max < 1 || max > 64) throw new ArgumentOutOfRangeException(nameof(max));
        lock (_gate)
        {
            if (max < _members.Count)
                throw new InvalidOperationException(
                    $"max {max} < current committee count {_members.Count} — exit members before shrinking");
            _maxSize = max;
        }
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<CommitteeMember>> GetActiveCommitteeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return new ValueTask<IReadOnlyList<CommitteeMember>>(_members.Values.ToArray());
        }
    }

    /// <inheritdoc />
    public ValueTask<int> GetMaxCommitteeSizeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return new ValueTask<int>(_maxSize);
    }

    /// <inheritdoc />
    public ValueTask<bool> IsRegisteredAsync(ECPoint sequencerKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(sequencerKey);
        lock (_gate) return new ValueTask<bool>(_members.ContainsKey(sequencerKey));
    }
}
