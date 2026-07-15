using System.Buffers.Binary;
using Neo.Cryptography.ECC;
using Neo.L2.Persistence;
using Neo.L2.Telemetry;

namespace Neo.L2.Sequencer;

/// <summary>
/// <see cref="ISequencerCommitteeProvider"/> with an in-memory cache and an optional
/// <see cref="IL2KeyValueStore"/> backing for the committee membership. Production
/// wires <see cref="RocksDbKeyValueStore"/> here so registered sequencers + their
/// exit windows survive node restarts — without persistence, a node bounce mid-exit
/// would lose the ExitsAtUnixSeconds deadline and either re-admit a sequencer that
/// was supposed to be in cooldown or refuse to finalize an exit that already passed
/// its window.
/// </summary>
/// <remarks>
/// Implementation: in-memory <c>Dictionary&lt;ECPoint, CommitteeMember&gt;</c> as the
/// hot path (fast O(1) ContainsKey + Values enumeration) plus shadow writes to the
/// IL2KeyValueStore. On construction, members are loaded from the KV store back
/// into the dict so a restart picks up where the previous instance left off.
/// </remarks>
public sealed class InMemorySequencerCommitteeProvider : ISequencerCommitteeProvider, IDisposable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<ECPoint, CommitteeMember> _members = new();
    private readonly IL2KeyValueStore _store;
    private readonly bool _ownsStore;
    private readonly IL2Metrics _metrics;
    private int _maxSize;
    private bool _disposed;

    /// <inheritdoc />
    public uint ChainId { get; }

    /// <summary>Construct against a chain id and committee size, with an in-memory backing.</summary>
    public InMemorySequencerCommitteeProvider(uint chainId, int maxCommitteeSize = 21, IL2Metrics? metrics = null)
        : this(chainId, new InMemoryKeyValueStore(), ownsStore: true, maxCommitteeSize, metrics) { }

    /// <summary>
    /// Construct with a caller-supplied <see cref="IL2KeyValueStore"/> for the committee
    /// membership — production wires <see cref="RocksDbKeyValueStore"/> here.
    /// </summary>
    public InMemorySequencerCommitteeProvider(
        uint chainId,
        IL2KeyValueStore store,
        bool ownsStore = false,
        int maxCommitteeSize = 21,
        IL2Metrics? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        // Symmetric validation with SetMaxCommitteeSize: range [1..64]. Without this, a
        // ctor-time `0` or `-1` accepts every Register call as "full" silently, and
        // `>64` exceeds dBFT 2.0's practical committee bound. Surface the misconfig at
        // construction time, not at first Register.
        if (maxCommitteeSize < 1 || maxCommitteeSize > 64)
            throw new ArgumentOutOfRangeException(nameof(maxCommitteeSize),
                $"maxCommitteeSize {maxCommitteeSize} must be in [1, 64]");
        ChainId = chainId;
        _store = store;
        _ownsStore = ownsStore;
        _maxSize = maxCommitteeSize;
        _metrics = metrics ?? NoOpMetrics.Instance;

        // Hydrate the in-memory cache from the KV store. A restart picks up exactly
        // where the previous process left off — registered members keep their
        // (Status, ExitsAtUnixSeconds) state.
        foreach (var (key, value) in _store.EnumeratePrefix(ReadOnlySpan<byte>.Empty))
        {
            var pub = ECPoint.DecodePoint(key, ECCurve.Secp256r1);
            var member = DecodeMember(pub, value);
            _members[pub] = member;
        }
    }

    /// <summary>Add a member with status=Active. Throws if already present or committee is full.</summary>
    public void Register(ECPoint pubKey, UInt160 l1Address)
    {
        ArgumentNullException.ThrowIfNull(pubKey);
        // l1Address is the governance slash target only after an independent finalized-dBFT
        // attribution provider identifies this member. Null would make that evidence impossible
        // to bind to the L1 bond record.
        ArgumentNullException.ThrowIfNull(l1Address);
        int newSize;
        var member = new CommitteeMember
        {
            PublicKey = pubKey,
            L1Address = l1Address,
            Status = 1, // Active
            ExitsAtUnixSeconds = 0,
        };
        lock (_gate)
        {
            if (_members.ContainsKey(pubKey))
                throw new InvalidOperationException($"already registered: {pubKey}");
            if (_members.Count >= _maxSize)
                throw new InvalidOperationException($"committee full ({_maxSize})");
            _members[pubKey] = member;
            _store.Put(EncodeKey(pubKey), EncodeValue(member));
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
            var updated = member with { Status = 2, ExitsAtUnixSeconds = exitsAtUnixSeconds };
            _members[pubKey] = updated;
            _store.Put(EncodeKey(pubKey), EncodeValue(updated));
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
            _store.Delete(EncodeKey(pubKey));
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

    /// <summary>
    /// Snapshot the current committee. Note on exit semantics: a member that has called
    /// <see cref="BeginExit"/> (Status=Exiting) is still returned here until <see cref="Finalize"/>
    /// removes it — this method takes no clock input and therefore cannot apply time-based
    /// exclusion of members whose <c>ExitsAtUnixSeconds</c> has elapsed. Callers that require
    /// exited members to disappear must invoke <see cref="Finalize"/> promptly once the exit
    /// window passes; the snapshot reflects committee membership, not exit-window expiry.
    /// </summary>
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

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsStore) _store.Dispose();
    }

    // -- Encoding helpers --
    // Key: 33-byte compressed secp256r1 pubkey.
    // Value: 20-B L1Address || 1-B Status || 4-B little-endian ExitsAtUnixSeconds = 25 bytes.

    private static byte[] EncodeKey(ECPoint pubKey) => pubKey.EncodePoint(true);

    private static byte[] EncodeValue(CommitteeMember m)
    {
        var bytes = new byte[25];
        m.L1Address.GetSpan().CopyTo(bytes.AsSpan(0, 20));
        bytes[20] = m.Status;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(21, 4), m.ExitsAtUnixSeconds);
        return bytes;
    }

    private static CommitteeMember DecodeMember(ECPoint pubKey, ReadOnlySpan<byte> value)
    {
        if (value.Length != 25)
            throw new InvalidOperationException(
                $"corrupt CommitteeMember encoding: expected 25 bytes, got {value.Length}");
        var l1 = new UInt160(value.Slice(0, 20));
        var status = value[20];
        var exits = BinaryPrimitives.ReadUInt32LittleEndian(value.Slice(21, 4));
        return new CommitteeMember
        {
            PublicKey = pubKey,
            L1Address = l1,
            Status = status,
            ExitsAtUnixSeconds = exits,
        };
    }
}
