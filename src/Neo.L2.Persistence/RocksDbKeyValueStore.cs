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
/// serializes conditional writes so <see cref="IL2KeyValueStore.TryPut"/>,
/// <see cref="IL2KeyValueStore.CompareExchange"/>, and
/// <see cref="IAtomicL2KeyValueStore.CompareExchangeBatch"/> remain atomic relative to
/// ordinary Put/Delete calls on the shared instance.
/// </para>
/// <para>
/// Tunables: the default options enable Snappy compression and create-if-missing.
/// Ordinary Put/Delete writes use RocksDB's default <c>WriteOptions</c> — WAL-backed but
/// asynchronously flushed. Conditional writes are recovery commit points and therefore use
/// <c>WriteOptions.SetSync(true)</c>, forcing the WAL to stable storage before success returns.
/// Operators can still tune compression and compaction through the alternate constructor's
/// <see cref="DbOptions"/>.
/// </para>
/// </remarks>
public sealed class RocksDbKeyValueStore : IAtomicL2KeyValueStore, IDurableL2KeyValueStore
{
    private readonly RocksDb _db;
    private readonly Lock _writeGate = new();
    private readonly WriteOptions _durableWriteOptions = new WriteOptions().SetSync(true);
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
        var keyArr = key.ToArray();
        var valueArr = value.ToArray();
        lock (_writeGate)
        {
            ThrowIfDisposed();
            _db.Put(keyArr, valueArr);
        }
    }

    /// <inheritdoc />
    public bool TryPut(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        if (key.Length == 0)
            throw new ArgumentOutOfRangeException(nameof(key), "key must be non-empty");
        ThrowIfDisposed();
        var keyArr = key.ToArray();
        var valueArr = value.ToArray();
        lock (_writeGate)
        {
            ThrowIfDisposed();
            if (_db.Get(keyArr) is not null) return false;
            _db.Put(keyArr, valueArr, writeOptions: _durableWriteOptions);
            return true;
        }
    }

    /// <inheritdoc />
    public bool CompareExchange(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> expectedValue,
        ReadOnlySpan<byte> newValue)
    {
        if (key.Length == 0)
            throw new ArgumentOutOfRangeException(nameof(key), "key must be non-empty");
        ThrowIfDisposed();
        var keyArr = key.ToArray();
        var newValueArr = newValue.ToArray();
        lock (_writeGate)
        {
            ThrowIfDisposed();
            var current = _db.Get(keyArr);
            if (current is null || !current.AsSpan().SequenceEqual(expectedValue))
                return false;
            _db.Put(keyArr, newValueArr, writeOptions: _durableWriteOptions);
            return true;
        }
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
        lock (_writeGate)
        {
            ThrowIfDisposed();
            var existed = _db.Get(keyArr) is not null;
            _db.Remove(keyArr);
            return existed;
        }
    }

    /// <inheritdoc />
    public bool Contains(ReadOnlySpan<byte> key)
    {
        ThrowIfDisposed();
        return _db.Get(key.ToArray()) is not null;
    }

    /// <inheritdoc />
    public bool CompareExchangeBatch(
        IEnumerable<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte>? ExpectedValue)> conditions,
        IEnumerable<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte>? Value)> mutations)
    {
        ThrowIfDisposed();
        var expected = AtomicBatchValidator.Materialize(conditions, nameof(conditions));
        var replacement = AtomicBatchValidator.Materialize(mutations, nameof(mutations));
        lock (_writeGate)
        {
            ThrowIfDisposed();
            foreach (var condition in expected)
            {
                var current = _db.Get(condition.Key);
                if (condition.Value is null)
                {
                    if (current is not null) return false;
                }
                else if (current is null
                    || !current.AsSpan().SequenceEqual(condition.Value))
                {
                    return false;
                }
            }

            using var batch = new WriteBatch();
            foreach (var mutation in replacement)
            {
                if (mutation.Value is null)
                    batch.Delete(mutation.Key);
                else
                    batch.Put(mutation.Key, mutation.Value);
            }
            _db.Write(batch, _durableWriteOptions);
            return true;
        }
    }

    /// <inheritdoc />
    public void ReplaceAll(
        IEnumerable<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value)> entries)
    {
        ThrowIfDisposed();
        var replacement = AtomicReplacementValidator.Materialize(entries);
        lock (_writeGate)
        {
            ThrowIfDisposed();
            using var batch = new WriteBatch();
            using var iterator = _db.NewIterator();
            iterator.SeekToFirst();
            while (iterator.Valid())
            {
                batch.Delete(iterator.Key());
                iterator.Next();
            }
            foreach (var pair in replacement)
                batch.Put(pair.Key, pair.Value);
            _db.Write(batch, _durableWriteOptions);
        }
    }

    /// <inheritdoc />
    public bool CompareExchangeAll(
        IEnumerable<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value)> expectedEntries,
        IEnumerable<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value)> replacementEntries)
    {
        ThrowIfDisposed();
        var expected = AtomicReplacementValidator.Materialize(expectedEntries);
        var replacement = AtomicReplacementValidator.Materialize(replacementEntries);
        lock (_writeGate)
        {
            ThrowIfDisposed();
            if (!CurrentSnapshotEquals(expected)) return false;
            using var batch = new WriteBatch();
            foreach (var pair in expected)
                batch.Delete(pair.Key);
            foreach (var pair in replacement)
                batch.Put(pair.Key, pair.Value);
            _db.Write(batch, _durableWriteOptions);
            return true;
        }
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

    private bool CurrentSnapshotEquals(SortedDictionary<byte[], byte[]> expected)
    {
        using var iterator = _db.NewIterator();
        iterator.SeekToFirst();
        foreach (var pair in expected)
        {
            if (!iterator.Valid()
                || !iterator.Key().AsSpan().SequenceEqual(pair.Key)
                || !iterator.Value().AsSpan().SequenceEqual(pair.Value))
                return false;
            iterator.Next();
        }
        return !iterator.Valid();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RocksDbKeyValueStore));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_writeGate)
        {
            if (_disposed) return;
            _disposed = true;
            _db.Dispose();
        }
    }
}
