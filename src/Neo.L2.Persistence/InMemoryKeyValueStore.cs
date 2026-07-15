using System.Collections.Concurrent;
using Neo.L2;

namespace Neo.L2.Persistence;

/// <summary>
/// In-process <see cref="IL2KeyValueStore"/> backed by a sorted dictionary keyed on
/// lexicographic byte comparison. Suitable for tests, dev-nets, and single-node demos.
/// Production wires <see cref="RocksDbKeyValueStore"/> for durable persistence.
/// </summary>
/// <remarks>
/// Concurrent access uses a single lock — adequate for the moderate write rate L2
/// stores see in tests + dev-nets. Production traffic patterns are served better by
/// RocksDB's per-column-family locking, which is why this class is not the production
/// default.
/// </remarks>
public sealed class InMemoryKeyValueStore : IAtomicL2KeyValueStore
{
    private readonly Lock _gate = new();
    private readonly SortedDictionary<byte[], byte[]> _data = new(LexicographicByteArrayComparer.Instance);

    /// <inheritdoc />
    public long Count
    {
        get { lock (_gate) return _data.Count; }
    }

    /// <inheritdoc />
    public void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        if (key.Length == 0)
            throw new ArgumentOutOfRangeException(nameof(key), "key must be non-empty");
        var keyArr = key.ToArray();
        var valArr = value.ToArray();
        lock (_gate) _data[keyArr] = valArr;
    }

    /// <inheritdoc />
    public bool TryPut(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        if (key.Length == 0)
            throw new ArgumentOutOfRangeException(nameof(key), "key must be non-empty");
        var keyArr = key.ToArray();
        var valueArr = value.ToArray();
        lock (_gate)
        {
            if (_data.ContainsKey(keyArr)) return false;
            _data.Add(keyArr, valueArr);
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
        var keyArr = key.ToArray();
        lock (_gate)
        {
            if (!_data.TryGetValue(keyArr, out var current)
                || !current.AsSpan().SequenceEqual(expectedValue))
                return false;
            _data[keyArr] = newValue.ToArray();
            return true;
        }
    }

    /// <inheritdoc />
    public byte[]? Get(ReadOnlySpan<byte> key)
    {
        var keyArr = key.ToArray();
        lock (_gate)
        {
            return _data.TryGetValue(keyArr, out var value) ? (byte[])value.Clone() : null;
        }
    }

    /// <inheritdoc />
    public bool Delete(ReadOnlySpan<byte> key)
    {
        var keyArr = key.ToArray();
        lock (_gate) return _data.Remove(keyArr);
    }

    /// <inheritdoc />
    public bool Contains(ReadOnlySpan<byte> key)
    {
        var keyArr = key.ToArray();
        lock (_gate) return _data.ContainsKey(keyArr);
    }

    /// <inheritdoc />
    public bool CompareExchangeBatch(
        IEnumerable<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte>? ExpectedValue)> conditions,
        IEnumerable<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte>? Value)> mutations)
    {
        var expected = AtomicBatchValidator.Materialize(conditions, nameof(conditions));
        var replacement = AtomicBatchValidator.Materialize(mutations, nameof(mutations));
        lock (_gate)
        {
            foreach (var condition in expected)
            {
                var exists = _data.TryGetValue(condition.Key, out var current);
                if (condition.Value is null)
                {
                    if (exists) return false;
                }
                else if (!exists || !current!.AsSpan().SequenceEqual(condition.Value))
                {
                    return false;
                }
            }
            foreach (var mutation in replacement)
            {
                if (mutation.Value is null)
                    _data.Remove(mutation.Key);
                else
                    _data[mutation.Key] = mutation.Value;
            }
            return true;
        }
    }

    /// <inheritdoc />
    public void ReplaceAll(
        IEnumerable<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value)> entries)
    {
        var replacement = AtomicReplacementValidator.Materialize(entries);
        lock (_gate)
        {
            _data.Clear();
            foreach (var pair in replacement)
                _data.Add(pair.Key, pair.Value);
        }
    }

    /// <inheritdoc />
    public bool CompareExchangeAll(
        IEnumerable<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value)> expectedEntries,
        IEnumerable<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value)> replacementEntries)
    {
        var expected = AtomicReplacementValidator.Materialize(expectedEntries);
        var replacement = AtomicReplacementValidator.Materialize(replacementEntries);
        lock (_gate)
        {
            if (!AtomicReplacementValidator.ContentEquals(_data, expected)) return false;
            _data.Clear();
            foreach (var pair in replacement)
                _data.Add(pair.Key, pair.Value);
            return true;
        }
    }

    /// <inheritdoc />
    public IEnumerable<(byte[] Key, byte[] Value)> EnumeratePrefix(ReadOnlySpan<byte> prefix)
        // Materialize the span on the heap before delegating to the iterator — a
        // ReadOnlySpan<byte> can't cross a yield boundary.
        => EnumerateSnapshot(prefix.ToArray());

    private IEnumerable<(byte[] Key, byte[] Value)> EnumerateSnapshot(byte[] prefix)
    {
        // Snapshot under the lock to avoid holding it across the yield boundary; defensive
        // copies on every yielded entry so caller mutations can't reach the store.
        List<KeyValuePair<byte[], byte[]>> snapshot;
        lock (_gate) snapshot = _data.Where(kv => StartsWith(kv.Key, prefix)).ToList();
        foreach (var kv in snapshot)
            yield return ((byte[])kv.Key.Clone(), (byte[])kv.Value.Clone());
    }

    /// <inheritdoc />
    public void Dispose() { /* no-op for the in-memory backend */ }

    private static bool StartsWith(byte[] key, byte[] prefix)
        => key.AsSpan().StartsWith(prefix);
}
