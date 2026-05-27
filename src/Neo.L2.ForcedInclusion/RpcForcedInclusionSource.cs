using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Neo.L2.Settlement.Rpc;

namespace Neo.L2.ForcedInclusion;

/// <summary>
/// Production <see cref="IForcedInclusionSource"/> backed by a real Neo L1 RPC
/// endpoint. Polls <c>NeoHub.ForcedInclusion</c>'s <c>[Safe]</c> read methods
/// to fetch each known nonce's entry + consumption state, then returns
/// unconsumed entries deadline-ordered to the L2 batcher.
/// </summary>
/// <remarks>
/// <para>
/// Same operator-discovery model as <c>RpcSequencerCommitteeProvider</c>: the
/// contract doesn't expose nonce-range enumeration via <c>[Safe]</c> reads, so
/// the operator wires an L1 event-watcher subscribing to <c>OnForcedTxEnqueued</c>
/// and forwards each nonce via <see cref="RegisterNonce"/>. A genesis seed of
/// "all nonces 1..currentMax" can be loaded once at boot via the
/// constructor's <c>knownNonces</c> argument.
/// </para>
/// <para>
/// <see cref="MarkConsumedAsync"/> is local-only bookkeeping: once the L2 batcher
/// has sealed a forced tx into a batch, this source remembers "don't return that
/// nonce from <see cref="DrainAsync"/> again." The L1 contract's matching
/// <c>MarkConsumed</c> method is called by SettlementManager after a withdrawal
/// finalizes — that's a different mechanism.
/// </para>
/// </remarks>
public sealed class RpcForcedInclusionSource : IForcedInclusionSource, IDisposable
{
    private readonly JsonRpcClient _rpc;
    private readonly UInt160 _registryHash;
    private readonly TimeSpan _cacheTtl;
    private readonly bool _ownsRpc;
    private readonly ConcurrentDictionary<ulong, byte> _knownNonces = new();
    private readonly ConcurrentDictionary<ulong, byte> _locallyConsumed = new();
    private readonly Lock _cacheGate = new();
    private List<ForcedInclusionEntry>? _cachedDrain;
    private DateTime _cacheUntilUtc = DateTime.MinValue;
    private bool _disposed;

    /// <inheritdoc />
    public uint ChainId { get; }

    /// <summary>
    /// Construct.
    /// </summary>
    /// <param name="rpc">L1 JSON-RPC client.</param>
    /// <param name="registryHash">Deployed <c>NeoHub.ForcedInclusion</c> hash.</param>
    /// <param name="chainId">L2 chain id this source serves.</param>
    /// <param name="knownNonces">Bootstrap nonce set (operator's event-watcher seeds future nonces via <see cref="RegisterNonce"/>).</param>
    /// <param name="cacheTtl">Drain-cache TTL (default 5s — short enough for live state, long enough that a batcher per-block call doesn't fan out per-known-nonce every time).</param>
    /// <param name="ownsRpc">If true, <see cref="Dispose"/> disposes the rpc client.</param>
    public RpcForcedInclusionSource(
        JsonRpcClient rpc,
        UInt160 registryHash,
        uint chainId,
        IEnumerable<ulong> knownNonces,
        TimeSpan? cacheTtl = null,
        bool ownsRpc = false)
    {
        ArgumentNullException.ThrowIfNull(rpc);
        ArgumentNullException.ThrowIfNull(registryHash);
        ArgumentNullException.ThrowIfNull(knownNonces);
        _rpc = rpc;
        _registryHash = registryHash;
        ChainId = chainId;
        _cacheTtl = cacheTtl ?? TimeSpan.FromSeconds(5);
        _ownsRpc = ownsRpc;
        foreach (var n in knownNonces) _knownNonces[n] = 1;
    }

    /// <summary>
    /// Add a newly-discovered forced-tx nonce. Operators wire this to an L1
    /// <c>OnForcedTxEnqueued</c> event subscription so the next
    /// <see cref="DrainAsync"/> picks it up.
    /// </summary>
    public bool RegisterNonce(ulong nonce)
    {
        var added = _knownNonces.TryAdd(nonce, 1);
        if (added) InvalidateCache();
        return added;
    }

    /// <summary>Force a fresh L1 fanout on the next <see cref="DrainAsync"/>.</summary>
    public void InvalidateCache()
    {
        lock (_cacheGate)
        {
            _cachedDrain = null;
            _cacheUntilUtc = DateTime.MinValue;
        }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ForcedInclusionEntry>> DrainAsync(int max, CancellationToken cancellationToken = default)
    {
        if (max < 0) throw new ArgumentOutOfRangeException(nameof(max), "max must be non-negative");
        cancellationToken.ThrowIfCancellationRequested();
        if (max == 0) return Array.Empty<ForcedInclusionEntry>();

        List<ForcedInclusionEntry>? drained;
        lock (_cacheGate)
        {
            drained = _cachedDrain is not null && DateTime.UtcNow < _cacheUntilUtc
                ? new List<ForcedInclusionEntry>(_cachedDrain)
                : null;
        }
        if (drained is null)
        {
            drained = await FetchPendingAsync(cancellationToken).ConfigureAwait(false);
            lock (_cacheGate)
            {
                _cachedDrain = drained;
                _cacheUntilUtc = DateTime.UtcNow + _cacheTtl;
            }
        }

        // Filter out locally-consumed nonces + cap at `max`. Drain order: deadline asc.
        return drained
            .Where(e => !_locallyConsumed.ContainsKey(e.Nonce))
            .OrderBy(e => e.DeadlineUnixSeconds)
            .ThenBy(e => e.Nonce)
            .Take(max)
            .ToArray();
    }

    /// <inheritdoc />
    public ValueTask MarkConsumedAsync(ulong nonce, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _locallyConsumed[nonce] = 1;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask<bool> HasOverdueEntryAsync(uint nowUnixSeconds, CancellationToken cancellationToken = default)
    {
        var pending = await DrainAsync(int.MaxValue, cancellationToken).ConfigureAwait(false);
        // Use < (not <=) so a deadline at exactly nowUnixSeconds is "due now," not overdue —
        // the L2 batcher gets one block to include it before censorship is reported. Mirrors
        // the in-memory variant.
        return pending.Any(e => e.DeadlineUnixSeconds < nowUnixSeconds);
    }

    private async Task<List<ForcedInclusionEntry>> FetchPendingAsync(CancellationToken ct)
    {
        var snapshot = _knownNonces.Keys.ToArray();
        var fetchTasks = snapshot
            .Where(n => !_locallyConsumed.ContainsKey(n))
            .Select(n => FetchEntryAsync(n, ct))
            .ToArray();
        var entries = await Task.WhenAll(fetchTasks).ConfigureAwait(false);
        return entries.Where(e => e is not null).Cast<ForcedInclusionEntry>().ToList();
    }

    private async Task<ForcedInclusionEntry?> FetchEntryAsync(ulong nonce, CancellationToken ct)
    {
        // Two parallel reads: GetEntry to fetch the encoded payload, IsConsumed to drop
        // entries L1 has already finalized via SettlementManager.
        var entryTask = RpcContractReader.InvokeReadAsync(_rpc, _registryHash, "getEntry", new object[] { ChainId, nonce }, ct).AsTask();
        var consumedTask = RpcContractReader.InvokeReadAsync(_rpc, _registryHash, "isConsumed", new object[] { ChainId, nonce }, ct).AsTask();
        await Task.WhenAll(entryTask, consumedTask).ConfigureAwait(false);

        if (RpcContractReader.ParseBoolean(consumedTask.Result)) return null;
        var entryBytes = RpcContractReader.ParseByteArray(entryTask.Result);
        if (entryBytes.Length == 0) return null; // L1 returns empty bytes when nonce not stored
        return DecodeEntry(nonce, entryBytes);
    }

    /// <summary>
    /// Decode the canonical contract encoding (matches <c>ForcedInclusionContract.EncodeEntry</c>):
    /// <c>[20B sender][32B txHash][4B txLen LE][txBytes][4B deadline LE]</c>.
    /// </summary>
    public static ForcedInclusionEntry DecodeEntry(ulong nonce, ReadOnlySpan<byte> bytes)
    {
        const int FixedHeader = 20 + 32 + 4;
        const int FixedTrailer = 4;
        if (bytes.Length < FixedHeader + FixedTrailer)
            throw new InvalidDataException($"forced-tx entry too short ({bytes.Length} < {FixedHeader + FixedTrailer})");
        var sender = new UInt160(bytes.Slice(0, 20));
        var txHash = new UInt256(bytes.Slice(20, 32));
        var txLen = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(52, 4));
        if (txLen < 0)
            throw new InvalidDataException($"forced-tx entry txLen {txLen} negative");
        // Cap txLen at 1 MiB to prevent OOM from malicious L1 responses.
        // Real transactions are typically < 100 KiB; the generous cap handles
        // edge cases while preventing a crafted 2^31-1 allocation.
        if (txLen > 1024 * 1024)
            throw new InvalidDataException($"forced-tx entry txLen {txLen} exceeds 1 MiB cap");
        if (bytes.Length != FixedHeader + txLen + FixedTrailer)
            throw new InvalidDataException(
                $"forced-tx entry size {bytes.Length} != expected {FixedHeader + txLen + FixedTrailer} for txLen={txLen}");
        var tx = bytes.Slice(56, txLen).ToArray();
        var deadline = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(56 + txLen, 4));
        return new ForcedInclusionEntry
        {
            Nonce = nonce,
            Sender = sender,
            TxHash = txHash,
            SerializedTx = tx,
            DeadlineUnixSeconds = deadline,
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsRpc) _rpc.Dispose();
    }
}
