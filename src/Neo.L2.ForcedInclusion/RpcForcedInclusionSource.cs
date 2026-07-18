using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Neo.Cryptography;
using Neo.L2.Persistence;
using Neo.L2.Settlement.Rpc;

namespace Neo.L2.ForcedInclusion;

/// <summary>
/// Production <see cref="IForcedInclusionSource"/> backed by a real Neo L1 RPC
/// endpoint. Discovers finalized enqueue events through an optional durable scanner,
/// polls <c>NeoHub.ForcedInclusion</c>'s <c>[Safe]</c> read methods for each tracked
/// nonce, and returns unconsumed entries deadline-ordered to the L2 batcher.
/// </summary>
/// <remarks>
/// <para>
/// Production wiring supplies <see cref="RpcForcedInclusionEventScanner"/> so finalized
/// <c>ForcedTxEnqueued</c> notifications and their cursor survive restarts. The
/// <c>knownNonces</c> seed and <see cref="RegisterNonce"/> remain available
/// for migrations and controlled recovery.
/// </para>
/// <para><see cref="ConfirmConsumedAsync"/> verifies the L1 consumed bit and never treats local
/// memory as authoritative. The canonical settlement finalizer submits the write first.</para>
/// </remarks>
public sealed class RpcForcedInclusionSource : IForcedInclusionSource, IDisposable
{
    private readonly JsonRpcClient _rpc;
    private readonly UInt160 _registryHash;
    private readonly TimeSpan _cacheTtl;
    private readonly bool _ownsRpc;
    private readonly RpcForcedInclusionEventScanner? _eventScanner;
    private readonly ConcurrentDictionary<ulong, byte> _knownNonces = new();
    private readonly Lock _cacheGate = new();
    private readonly SemaphoreSlim _fetchConcurrency = new(8, 8); // Cap concurrent L1 RPC calls
    private List<ForcedInclusionEntry>? _cachedDrain;
    private DateTime _cacheUntilUtc = DateTime.MinValue;
    private int _disposedFlag;

    /// <inheritdoc />
    public uint ChainId { get; }

    /// <summary>
    /// Construct.
    /// </summary>
    /// <param name="rpc">L1 JSON-RPC client.</param>
    /// <param name="registryHash">Deployed <c>NeoHub.ForcedInclusion</c> hash.</param>
    /// <param name="chainId">L2 chain id this source serves.</param>
    /// <param name="knownNonces">Optional migration/recovery seed; production discovery comes from <paramref name="eventScanner"/>.</param>
    /// <param name="cacheTtl">Drain-cache TTL (default 5s — short enough for live state, long enough that a batcher per-block call doesn't fan out per-known-nonce every time).</param>
    /// <param name="ownsRpc">If true, <see cref="Dispose"/> disposes the rpc client.</param>
    /// <param name="eventScanner">Optional durable finalized-event scanner; production supplies one.</param>
    public RpcForcedInclusionSource(
        JsonRpcClient rpc,
        UInt160 registryHash,
        uint chainId,
        IEnumerable<ulong> knownNonces,
        TimeSpan? cacheTtl = null,
        bool ownsRpc = false,
        RpcForcedInclusionEventScanner? eventScanner = null)
    {
        ArgumentNullException.ThrowIfNull(rpc);
        ArgumentNullException.ThrowIfNull(registryHash);
        ArgumentNullException.ThrowIfNull(knownNonces);
        _rpc = rpc;
        _registryHash = registryHash;
        ChainId = chainId;
        _cacheTtl = cacheTtl ?? TimeSpan.FromSeconds(5);
        _ownsRpc = ownsRpc;
        _eventScanner = eventScanner;
        foreach (var nonce in knownNonces) RegisterNonce(nonce);
        if (_eventScanner is not null)
        {
            foreach (var nonce in _eventScanner.LoadTrackedNonces())
                RegisterDiscoveredNonce(nonce);
        }
    }

    /// <summary>
    /// Open a production forced-inclusion source whose durable event store lives under
    /// <see cref="NeoHubDeployReport.RelativeForcedInclusionEventStoreDir"/> in a chain
    /// working directory. Owns the RocksDB store (via the event scanner) and optionally the RPC client.
    /// </summary>
    public static RpcForcedInclusionSource OpenFromChainDirectory(
        string chainDirectory,
        JsonRpcClient rpc,
        UInt160 forcedInclusionHash,
        uint chainId,
        uint startHeight,
        uint finalityDepth = 1,
        int maximumBlocksPerScan = 256,
        bool ownsRpc = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainDirectory);
        ArgumentNullException.ThrowIfNull(rpc);
        ArgumentNullException.ThrowIfNull(forcedInclusionHash);
        if (startHeight == 0)
            throw new ArgumentOutOfRangeException(
                nameof(startHeight), "ForcedInclusion deployment height must be non-zero");

        var root = System.IO.Path.GetFullPath(chainDirectory);
        if (!System.IO.Directory.Exists(root))
            throw new System.IO.DirectoryNotFoundException(
                $"Chain directory not found: {root}. Run neo-stack init-l2 first.");

        NeoHubDeployReport.EnsureSettlementStoreDirectories(root);
        var absolute = System.IO.Path.Combine(
            root, NeoHubDeployReport.RelativeForcedInclusionEventStoreDir);
        System.IO.Directory.CreateDirectory(absolute);
        var store = new RocksDbKeyValueStore(absolute);
        var scanner = new RpcForcedInclusionEventScanner(
            rpc,
            forcedInclusionHash,
            chainId,
            store,
            startHeight,
            finalityDepth,
            maximumBlocksPerScan,
            ownsStore: true);
        return new RpcForcedInclusionSource(
            rpc,
            forcedInclusionHash,
            chainId,
            Array.Empty<ulong>(),
            ownsRpc: ownsRpc,
            eventScanner: scanner);
    }

    /// <summary>
    /// Count of forced-inclusion nonces known to this source (scanner + operator seed).
    /// Offline ops surface without an L1 drain; durable settlement tracking remains separate.
    /// </summary>
    public int KnownNonceCount
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposedFlag) != 0, this);
            return _knownNonces.Count;
        }
    }

    /// <summary>
    /// Add a newly-discovered forced-tx nonce. Operators wire this to an L1
    /// <c>OnForcedTxEnqueued</c> event subscription so the next
    /// <see cref="DrainAsync"/> picks it up.
    /// </summary>
    public bool RegisterNonce(ulong nonce)
    {
        _eventScanner?.TrackNonce(nonce);
        return RegisterDiscoveredNonce(nonce);
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

        if (_eventScanner is not null)
            await _eventScanner.ScanAsync(
                    nonce => RegisterDiscoveredNonce(nonce), cancellationToken)
                .ConfigureAwait(false);

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

        // L1 consumption is filtered during FetchPendingAsync. Drain order: deadline asc.
        return drained
            .OrderBy(e => e.DeadlineUnixSeconds)
            .ThenBy(e => e.Nonce)
            .Take(max)
            .ToArray();
    }

    /// <inheritdoc />
    public async ValueTask ConfirmConsumedAsync(
        ulong nonce,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var consumed = await RpcContractReader.InvokeReadAsync(
            _rpc,
            _registryHash,
            "isConsumed",
            new object[] { ChainId, nonce },
            cancellationToken).ConfigureAwait(false);
        if (!RpcContractReader.ParseBoolean(consumed))
            throw new InvalidOperationException(
                $"forced-inclusion nonce {nonce} is not confirmed consumed on L1");
        _eventScanner?.ForgetNonce(nonce);
        _knownNonces.TryRemove(nonce, out _);
        InvalidateCache();
    }

    /// <inheritdoc />
    public async ValueTask<bool> HasOverdueEntryAsync(uint nowUnixSeconds, CancellationToken cancellationToken = default)
    {
        var pending = await DrainAsync(int.MaxValue, cancellationToken).ConfigureAwait(false);
        // Overdue iff nowUnixSeconds >= DeadlineUnixSeconds (i.e. deadline <= now), matching
        // InMemoryForcedInclusionSource.HasOverdueEntryAsync, CensorshipDetector's continue-only-
        // when-now-<-deadline loop, and the on-chain ReportCensorship boundary. Using strict <
        // here would let the RPC path skip the detect pass at the exact deadline second that the
        // in-memory and on-chain paths already act on.
        return pending.Any(e => e.DeadlineUnixSeconds <= nowUnixSeconds);
    }

    /// <summary>
    /// Soft overdue check against the last successful drain cache only — no L1 scan or RPC.
    /// Returns false when the cache is empty or expired. For offline LocalHost operator status;
    /// use <see cref="HasOverdueEntryAsync"/> when a live L1 poll is intended.
    /// </summary>
    public bool HasOverdueCachedEntry(uint nowUnixSeconds)
    {
        lock (_cacheGate)
        {
            if (_cachedDrain is null)
                return false;
            // Zero TTL means Drain always re-fetches, but keep the last snapshot for soft
            // status until InvalidateCache. Positive TTL respects the drain cache window.
            if (_cacheTtl > TimeSpan.Zero && DateTime.UtcNow >= _cacheUntilUtc)
                return false;
            return _cachedDrain.Any(e => e.DeadlineUnixSeconds <= nowUnixSeconds);
        }
    }

    private async Task<List<ForcedInclusionEntry>> FetchPendingAsync(CancellationToken ct)
    {
        var snapshot = _knownNonces.Keys.ToArray();
        // Cap concurrent RPC calls to prevent L1 rate-limit exhaustion.
        // Each nonce requires 2 RPC calls (getEntry + isConsumed); the semaphore
        // ensures at most 8 nonces are fetched concurrently, regardless of queue size.
        var fetchTasks = snapshot
            .Select(n => FetchEntryThrottledAsync(n, ct))
            .ToArray();
        var entries = await Task.WhenAll(fetchTasks).ConfigureAwait(false);
        return entries.Where(e => e is not null).Cast<ForcedInclusionEntry>().ToList();
    }

    private async Task<ForcedInclusionEntry?> FetchEntryThrottledAsync(ulong nonce, CancellationToken ct)
    {
        await _fetchConcurrency.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await FetchEntryAsync(nonce, ct).ConfigureAwait(false);
        }
        finally
        {
            _fetchConcurrency.Release();
        }
    }

    private async Task<ForcedInclusionEntry?> FetchEntryAsync(ulong nonce, CancellationToken ct)
    {
        // Two parallel reads: GetEntry fetches the encoded payload; IsConsumed drops entries
        // already confirmed by permissionless consumption after settlement finality.
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
        var encodedTxHash = new UInt256(Crypto.Hash256(tx));
        if (!encodedTxHash.Equals(txHash))
            throw new InvalidDataException(
                $"forced-tx entry nonce {nonce} txHash does not match Hash256(encodedTx)");
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
        if (Interlocked.Exchange(ref _disposedFlag, 1) != 0) return;
        _eventScanner?.Dispose();
        _fetchConcurrency.Dispose();
        if (_ownsRpc) _rpc.Dispose();
    }

    private bool RegisterDiscoveredNonce(ulong nonce)
    {
        var added = _knownNonces.TryAdd(nonce, 1);
        if (added) InvalidateCache();
        return added;
    }
}
