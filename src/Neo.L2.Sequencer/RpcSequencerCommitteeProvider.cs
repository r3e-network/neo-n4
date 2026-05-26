using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Neo.Cryptography.ECC;
using Neo.L2.Settlement.Rpc;

namespace Neo.L2.Sequencer;

/// <summary>
/// Production <see cref="ISequencerCommitteeProvider"/> backed by a real Neo L1 RPC
/// endpoint. Polls <c>NeoHub.SequencerRegistry</c>'s <c>[Safe]</c> read methods to
/// discover the live status of each known sequencer key, with a configurable cache
/// TTL so per-block calls don't fan out to N RPCs every time consensus asks for the
/// committee.
/// </summary>
/// <remarks>
/// <para>
/// The <c>SequencerRegistry</c> contract does not expose key enumeration via RPC
/// (its storage iterator is only reachable via Neo's <c>findstates</c> RPC, which
/// not every node exposes). To stay portable, this provider takes its key set
/// from the operator: pass the genesis committee to the constructor, then call
/// <see cref="RegisterKnownKey"/> when an event-watcher (operator-supplied)
/// observes a <c>Register</c> event from L1. Removed sequencers are detected by
/// the L1 <c>getStatus</c> call returning the unregistered byte (0) — no operator
/// action needed for cleanup.
/// </para>
/// <para>
/// All status reads are issued in parallel via <see cref="Task.WhenAll(IEnumerable{Task})"/>;
/// for a 21-validator committee this means a single fanout per cache miss, not 21
/// sequential round trips.
/// </para>
/// </remarks>
public sealed class RpcSequencerCommitteeProvider : ISequencerCommitteeProvider, IDisposable
{
    private readonly JsonRpcClient _rpc;
    private readonly UInt160 _registryHash;
    private readonly TimeSpan _cacheTtl;
    private readonly bool _ownsRpc;
    private readonly ConcurrentDictionary<ECPoint, byte> _knownKeys = new();
    private readonly Lock _cacheGate = new();
    private CommitteeMember[]? _cachedCommittee;
    private DateTime _cacheUntilUtc = DateTime.MinValue;
    private int _cachedMaxSize = -1;
    private DateTime _cachedMaxSizeUntilUtc = DateTime.MinValue;
    private bool _disposed;

    /// <inheritdoc />
    public uint ChainId { get; }

    /// <summary>
    /// Construct against a real L1 JSON-RPC endpoint, the deployed
    /// <c>SequencerRegistry</c> contract hash, the chain id this provider serves,
    /// and an initial set of known sequencer public keys (genesis committee).
    /// </summary>
    /// <param name="rpc">L1 JSON-RPC client (caller-owned by default).</param>
    /// <param name="registryHash">Deployed <c>NeoHub.SequencerRegistry</c> hash.</param>
    /// <param name="chainId">L2 chain id this provider tracks.</param>
    /// <param name="genesisKeys">
    /// Initial bootstrap key set. Each key is queried for its current status via L1 RPC;
    /// keys reporting status=0 (unregistered) are excluded from the committee snapshot.
    /// </param>
    /// <param name="cacheTtl">
    /// How long a successful committee snapshot is reused before a fresh L1 fanout.
    /// Default 5 seconds — short enough to track live status changes, long enough to
    /// shield the L1 RPC from per-block fanout in dBFT.
    /// </param>
    /// <param name="ownsRpc">If true, <see cref="Dispose"/> disposes <paramref name="rpc"/>.</param>
    public RpcSequencerCommitteeProvider(
        JsonRpcClient rpc,
        UInt160 registryHash,
        uint chainId,
        IEnumerable<ECPoint> genesisKeys,
        TimeSpan? cacheTtl = null,
        bool ownsRpc = false)
    {
        ArgumentNullException.ThrowIfNull(rpc);
        ArgumentNullException.ThrowIfNull(registryHash);
        ArgumentNullException.ThrowIfNull(genesisKeys);
        _rpc = rpc;
        _registryHash = registryHash;
        ChainId = chainId;
        _cacheTtl = cacheTtl ?? TimeSpan.FromSeconds(5);
        _ownsRpc = ownsRpc;
        foreach (var key in genesisKeys)
        {
            ArgumentNullException.ThrowIfNull(key);
            _knownKeys[key] = 1;
        }
    }

    /// <summary>
    /// Add a newly-discovered sequencer pubkey to the known-keys set. Operators wire this
    /// to an L1 event subscription (<c>OnSequencerRegistered</c>) so future
    /// <see cref="GetActiveCommitteeAsync"/> calls include the new member.
    /// </summary>
    /// <returns><see langword="true"/> if the key was newly added; <see langword="false"/> if already known.</returns>
    public bool RegisterKnownKey(ECPoint key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var added = _knownKeys.TryAdd(key, 1);
        if (added) InvalidateCache();
        return added;
    }

    /// <summary>
    /// Force the next <see cref="GetActiveCommitteeAsync"/> call to skip the cache + re-fanout.
    /// Useful after a known event (governance vote, manual operator action) has changed
    /// committee state and the operator wants the new state immediately, not on the next
    /// TTL boundary.
    /// </summary>
    public void InvalidateCache()
    {
        lock (_cacheGate)
        {
            _cachedCommittee = null;
            _cacheUntilUtc = DateTime.MinValue;
        }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<CommitteeMember>> GetActiveCommitteeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_cacheGate)
        {
            if (_cachedCommittee is not null && DateTime.UtcNow < _cacheUntilUtc)
                return _cachedCommittee;
        }

        var snapshot = _knownKeys.Keys.ToArray();
        var statusTasks = snapshot.Select(k => FetchMemberAsync(k, cancellationToken)).ToArray();
        var members = await Task.WhenAll(statusTasks).ConfigureAwait(false);
        var live = members.Where(m => m is not null).Cast<CommitteeMember>().ToArray();

        lock (_cacheGate)
        {
            _cachedCommittee = live;
            _cacheUntilUtc = DateTime.UtcNow + _cacheTtl;
        }
        return live;
    }

    /// <inheritdoc />
    public async ValueTask<int> GetMaxCommitteeSizeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_cacheGate)
        {
            if (_cachedMaxSize >= 0 && DateTime.UtcNow < _cachedMaxSizeUntilUtc)
                return _cachedMaxSize;
        }
        var raw = await RpcContractReader.InvokeReadAsync(_rpc, _registryHash, "getMaxCommitteeSize", Array.Empty<object>(), cancellationToken).ConfigureAwait(false);
        var size = RpcContractReader.ParseInteger(raw);
        lock (_cacheGate)
        {
            _cachedMaxSize = size;
            _cachedMaxSizeUntilUtc = DateTime.UtcNow + _cacheTtl;
        }
        return size;
    }

    /// <inheritdoc />
    public async ValueTask<bool> IsRegisteredAsync(ECPoint sequencerKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sequencerKey);
        cancellationToken.ThrowIfCancellationRequested();
        // L1 contract is the source of truth — don't rely on the local known-keys set.
        // A key absent from known-keys but registered on L1 (operator missed an event)
        // would silently report unregistered if we shortcut here.
        var raw = await RpcContractReader.InvokeReadAsync(_rpc, _registryHash, "isRegistered",
            new object[] { ChainId, EncodePubKey(sequencerKey) }, cancellationToken).ConfigureAwait(false);
        return RpcContractReader.ParseBoolean(raw);
    }

    private async Task<CommitteeMember?> FetchMemberAsync(ECPoint pubKey, CancellationToken ct)
    {
        var pkBytes = EncodePubKey(pubKey);
        var statusRaw = await RpcContractReader.InvokeReadAsync(_rpc, _registryHash, "getStatus", new object[] { ChainId, pkBytes }, ct).ConfigureAwait(false);
        var status = (byte)RpcContractReader.ParseInteger(statusRaw);
        if (status == 0) return null; // not registered on L1 — drop from known set

        var addressRaw = await RpcContractReader.InvokeReadAsync(_rpc, _registryHash, "getSequencerAddress", new object[] { ChainId, pkBytes }, ct).ConfigureAwait(false);
        var address = RpcContractReader.ParseUInt160(addressRaw);

        // The contract stores exitsAtUnix in the entry value; not exposed via [Safe] reads
        // currently. For a member in Exiting status, callers consulting this provider for
        // dBFT eligibility just need to know they're STILL in the committee — exit-window
        // accounting is the operator's L1-event-watcher's responsibility (or a contract
        // change to expose getExitsAt). Pin status; leave ExitsAtUnixSeconds=0 for the
        // value the contract doesn't surface yet.
        return new CommitteeMember
        {
            PublicKey = pubKey,
            L1Address = address,
            Status = status,
            ExitsAtUnixSeconds = 0,
        };
    }

    private static byte[] EncodePubKey(ECPoint pub) => pub.EncodePoint(true);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsRpc) _rpc.Dispose();
    }
}
