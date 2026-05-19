using RocksDbSharp;

namespace Neo.L2.Persistence;

/// <summary>
/// RocksDB-backed <see cref="IL2KeyValueStore"/>. Production default for durable L2-side
/// persistence (RPC stores, DA blobs, forced-inclusion queues). Survives node restarts;
/// supports multi-GB state.
/// </summary>
/// <remarks>
/// <para>
/// Construction opens (or creates if absent) a column-family-free RocksDB database at
/// the configured path. The library handles concurrent reads + writes; this wrapper
/// keeps the surface flat (no per-call locks) so multi-threaded L2 components can
/// share a single instance.
/// </para>
/// <para>
/// Tunables: the default options enable Snappy compression and create-if-missing.
/// Writes use RocksDB's default <c>WriteOptions</c> — WAL-backed but asynchronously
/// flushed (no per-write <c>fsync</c>). This matches the standard L2-sequencer
/// durability story: in-flight writes recoverable from WAL on restart; true finality
/// comes from commit to L1. Operators who need per-write <c>fsync</c> (or different
/// compression / compaction tradeoffs) supply their own <see cref="DbOptions"/> via
/// the alternate constructor — wrap a <c>WriteOptions { Sync = true }</c> at the
/// call site if synchronous WAL flushes are required.
/// </para>
/// </remarks>
public sealed class RocksDbKeyValueStore : IL2KeyValueStore
{
    private readonly RocksDb _db;
    private bool _disposed;

    /// <summary>Path on disk where the RocksDB database lives.</summary>
    public string DataDirectory { get; }

    /// <summary>Open (or create) a RocksDB store at <paramref name="dataDirectory"/> with default options.</summary>
    public RocksDbKeyValueStore(string dataDirectory)
        : this(dataDirectory, DefaultOptions())
    {
    }

    /// <summary>Open with caller-supplied <see cref="DbOptions"/> (lets operators tune compaction, compression, etc.).</summary>
    public RocksDbKeyValueStore(string dataDirectory, DbOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(options);
        DataDirectory = dataDirectory;
        try
        {
            _db = RocksDb.Open(options, dataDirectory);
        }
        catch (RocksDbException ex) when (LooksLikeLockHeld(ex.Message))
        {
            // RocksDB holds a single-writer LOCK file in dataDirectory; any second
            // open returns an opaque "IO error: While lock file ..." message.
            // Translate to a clearer operator-facing error so misconfigured deployments
            // (two daemons sharing one --data-dir) are debuggable from the log line.
            throw new InvalidOperationException(
                $"RocksDB data directory '{dataDirectory}' is already in use by another " +
                "process. Stop the other instance, or point this one at a different " +
                "--data-dir / DataDirectory.", ex);
        }
    }

    private static bool LooksLikeLockHeld(string message)
        => message.Contains("lock", StringComparison.OrdinalIgnoreCase)
            && (message.Contains("LOCK", StringComparison.Ordinal)
                || message.Contains("Resource temporarily unavailable", StringComparison.Ordinal)
                || message.Contains("already held", StringComparison.OrdinalIgnoreCase));

    private static DbOptions DefaultOptions() => new DbOptions()
        .SetCreateIfMissing(true)
        .SetCompression(Compression.Snappy);

    /// <inheritdoc />
    public long Count
    {
        get
        {
            // RocksDB doesn't track total key count cheaply; iterate. This is documented as
            // O(N) in IL2KeyValueStore — callers should not invoke on the hot path.
            ThrowIfDisposed();
            long count = 0;
            using var iter = _db.NewIterator();
            iter.SeekToFirst();
            while (iter.Valid()) { count++; iter.Next(); }
            return count;
        }
    }

    /// <inheritdoc />
    public void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        if (key.Length == 0)
            throw new ArgumentOutOfRangeException(nameof(key), "key must be non-empty");
        ThrowIfDisposed();
        _db.Put(key.ToArray(), value.ToArray());
    }

    /// <inheritdoc />
    public byte[]? Get(ReadOnlySpan<byte> key)
    {
        ThrowIfDisposed();
        return _db.Get(key.ToArray());
    }

    /// <inheritdoc />
    public bool Delete(ReadOnlySpan<byte> key)
    {
        ThrowIfDisposed();
        var keyArr = key.ToArray();
        // RocksDB's Delete is idempotent and returns no "did this key exist" hint, so
        // we have to probe first to honor the IL2KeyValueStore contract that returns
        // true iff the key existed before the call.
        var existed = _db.Get(keyArr) is not null;
        _db.Remove(keyArr);
        return existed;
    }

    /// <inheritdoc />
    public bool Contains(ReadOnlySpan<byte> key)
    {
        ThrowIfDisposed();
        return _db.Get(key.ToArray()) is not null;
    }

    /// <inheritdoc />
    public IEnumerable<(byte[] Key, byte[] Value)> EnumeratePrefix(ReadOnlySpan<byte> prefix)
    {
        ThrowIfDisposed();
        var prefixArr = prefix.ToArray();
        return EnumerateInternal(prefixArr);
    }

    private IEnumerable<(byte[] Key, byte[] Value)> EnumerateInternal(byte[] prefix)
    {
        using var iter = _db.NewIterator();
        if (prefix.Length == 0) iter.SeekToFirst();
        else iter.Seek(prefix);
        while (iter.Valid())
        {
            var key = iter.Key();
            if (prefix.Length > 0 && !StartsWith(key, prefix)) yield break;
            // RocksDB returns fresh byte[]s already — no need to clone, but the
            // IL2KeyValueStore contract mandates defensive copies and we honor it.
            yield return ((byte[])key.Clone(), (byte[])iter.Value().Clone());
            iter.Next();
        }
    }

    private static bool StartsWith(byte[] key, byte[] prefix)
    {
        if (prefix.Length == 0) return true;
        if (key.Length < prefix.Length) return false;
        for (var i = 0; i < prefix.Length; i++)
            if (key[i] != prefix[i]) return false;
        return true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RocksDbKeyValueStore));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _db.Dispose();
    }
}
