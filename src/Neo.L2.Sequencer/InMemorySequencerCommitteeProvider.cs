using Neo.Cryptography.ECC;

namespace Neo.L2.Sequencer;

/// <summary>
/// In-memory <see cref="ISequencerCommitteeProvider"/> for tests + devnet. Production wires
/// the L1-RPC-backed implementation that polls <c>NeoHub.SequencerRegistry</c>.
/// </summary>
public sealed class InMemorySequencerCommitteeProvider : ISequencerCommitteeProvider
{
    private readonly Lock _gate = new();
    private readonly Dictionary<ECPoint, CommitteeMember> _members = new();
    private int _maxSize;

    /// <inheritdoc />
    public uint ChainId { get; }

    /// <summary>Construct against a chain id and committee size.</summary>
    public InMemorySequencerCommitteeProvider(uint chainId, int maxCommitteeSize = 21)
    {
        ChainId = chainId;
        _maxSize = maxCommitteeSize;
    }

    /// <summary>Add a member with status=Active. Throws if already present or committee is full.</summary>
    public void Register(ECPoint pubKey, UInt160 l1Address)
    {
        ArgumentNullException.ThrowIfNull(pubKey);
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
        }
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
    }

    /// <summary>Permanently remove an exiting member after their window has elapsed.</summary>
    public void Finalize(ECPoint pubKey, uint nowUnixSeconds)
    {
        ArgumentNullException.ThrowIfNull(pubKey);
        lock (_gate)
        {
            if (!_members.TryGetValue(pubKey, out var member))
                throw new InvalidOperationException($"not registered: {pubKey}");
            if (member.Status != 2)
                throw new InvalidOperationException("not exiting");
            if (nowUnixSeconds < member.ExitsAtUnixSeconds)
                throw new InvalidOperationException("exit window still open");
            _members.Remove(pubKey);
        }
    }

    /// <summary>Update the configured max committee size.</summary>
    public void SetMaxCommitteeSize(int max)
    {
        if (max < 1 || max > 64) throw new ArgumentOutOfRangeException(nameof(max));
        lock (_gate) _maxSize = max;
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
