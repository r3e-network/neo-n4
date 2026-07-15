namespace Neo.L2.Persistence;

/// <summary>
/// Minimal byte-keyed, byte-valued store abstraction shared by L2 components that need
/// persistence (RPC stores, DA blobs, forced-inclusion queues, etc.). Implementations
/// must be safe for concurrent reads + writes. Individual operations are atomic;
/// backends that can commit guarded multi-key or complete-snapshot replacements expose
/// <see cref="IAtomicL2KeyValueStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// Default backend is <see cref="InMemoryKeyValueStore"/> (suitable for tests + devnets).
/// Production deployments wire <see cref="RocksDbKeyValueStore"/> for durable, multi-GB
/// state that survives node restarts.
/// </para>
/// <para>
/// Why a new abstraction rather than reusing Neo's <c>IStore</c>: Neo's IStore is
/// optimized for L1 ledger semantics (snapshots, write batches, MPT-friendly sort).
/// The L2 stores here just need a flat byte-keyed dictionary with prefix iteration —
/// matching that subset keeps callers free of Neo's Persistence dependencies.
/// </para>
/// </remarks>
public interface IL2KeyValueStore : IDisposable
{
    /// <summary>Insert or replace <paramref name="value"/> at <paramref name="key"/>.</summary>
    void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value);

    /// <summary>
    /// Atomically insert <paramref name="value"/> only when <paramref name="key"/> is absent.
    /// Returns <c>true</c> when inserted and <c>false</c> when a value already exists.
    /// </summary>
    /// <remarks>
    /// Durable backends must synchronously persist a successful insertion before returning;
    /// this operation is used as a recovery commit point.
    /// </remarks>
    bool TryPut(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value);

    /// <summary>
    /// Atomically replace <paramref name="expectedValue"/> with <paramref name="newValue"/>.
    /// Returns <c>false</c> when the key is absent or its current bytes differ.
    /// </summary>
    /// <remarks>
    /// Durable backends must synchronously persist a successful replacement before returning.
    /// </remarks>
    bool CompareExchange(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> expectedValue,
        ReadOnlySpan<byte> newValue);

    /// <summary>Read the value at <paramref name="key"/>, or <c>null</c> if absent.</summary>
    byte[]? Get(ReadOnlySpan<byte> key);

    /// <summary>Delete the entry at <paramref name="key"/>; returns <c>true</c> if it existed.</summary>
    bool Delete(ReadOnlySpan<byte> key);

    /// <summary>True if an entry exists at <paramref name="key"/>.</summary>
    bool Contains(ReadOnlySpan<byte> key);

    /// <summary>Enumerate all entries whose key starts with <paramref name="prefix"/>, in key order.</summary>
    /// <remarks>
    /// Yielded byte arrays are defensive copies — callers may freely mutate them
    /// without corrupting the store. Same iter-167 / iter-176 pattern as
    /// <c>KeyedStateStore.EnumerateSorted</c>.
    /// </remarks>
    IEnumerable<(byte[] Key, byte[] Value)> EnumeratePrefix(ReadOnlySpan<byte> prefix);

    /// <summary>Total number of keys in the store. Production RocksDB callers should treat this as O(N) — use sparingly.</summary>
    long Count { get; }
}

/// <summary>
/// Persistence capability for key-value stores whose committed records survive process restart.
/// </summary>
/// <remarks>
/// See doc.md §7.3, §7.5, §8, and §15.4. Recovery commit-point operations must not report
/// success until their write is durable according to the backend contract.
/// </remarks>
public interface IDurableL2KeyValueStore : IL2KeyValueStore
{
}

/// <summary>
/// Durable store capability for replacing a complete authenticated L2 state snapshot as
/// one atomic commit.
/// </summary>
/// <remarks>
/// See doc.md §7.3 and §8. Implementations validate and defensively copy the complete
/// replacement before mutating the store. An empty replacement clears the store.
/// </remarks>
public interface IAtomicL2KeyValueStore : IL2KeyValueStore
{
    /// <summary>
    /// Atomically apply a key-local mutation batch only when every supplied key condition
    /// still matches.
    /// </summary>
    /// <param name="conditions">
    /// Expected key states. A null expected value requires the key to be absent; a non-null
    /// value requires byte-for-byte equality.
    /// </param>
    /// <param name="mutations">
    /// Mutations to apply after all conditions match. A null value deletes the key; a non-null
    /// value inserts or replaces it.
    /// </param>
    /// <returns><c>true</c> when the batch committed; <c>false</c> on any condition mismatch.</returns>
    /// <remarks>
    /// See doc.md §7.3, §7.5, and §15.1. This is the bounded-key transaction primitive used
    /// when a hot-path record must not race a chain-level recovery marker.
    /// </remarks>
    bool CompareExchangeBatch(
        IEnumerable<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte>? ExpectedValue)> conditions,
        IEnumerable<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte>? Value)> mutations);

    /// <summary>Atomically replace every key/value pair in the store.</summary>
    void ReplaceAll(
        IEnumerable<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value)> entries);

    /// <summary>
    /// Atomically replace every key/value pair only when the complete current snapshot equals
    /// <paramref name="expectedEntries"/>.
    /// </summary>
    /// <returns><c>true</c> when replaced; <c>false</c> when the current snapshot differs.</returns>
    bool CompareExchangeAll(
        IEnumerable<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value)> expectedEntries,
        IEnumerable<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value)> replacementEntries);
}

internal static class AtomicBatchValidator
{
    public static SortedDictionary<byte[], byte[]?> Materialize(
        IEnumerable<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte>? Value)> entries,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(entries, parameterName);
        var materialized = new SortedDictionary<byte[], byte[]?>(
            LexicographicByteArrayComparer.Instance);
        foreach (var (keyMemory, valueMemory) in entries)
        {
            if (keyMemory.IsEmpty)
                throw new ArgumentOutOfRangeException(
                    parameterName, "atomic batch keys must be non-empty");
            if (!materialized.TryAdd(
                    keyMemory.ToArray(),
                    valueMemory?.ToArray()))
            {
                throw new InvalidDataException(
                    "atomic batch contains a duplicate key");
            }
        }
        return materialized;
    }
}

internal static class AtomicReplacementValidator
{
    public static SortedDictionary<byte[], byte[]> Materialize(
        IEnumerable<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value)> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var replacement = new SortedDictionary<byte[], byte[]>(
            LexicographicByteArrayComparer.Instance);
        foreach (var (keyMemory, valueMemory) in entries)
        {
            if (keyMemory.IsEmpty)
                throw new ArgumentOutOfRangeException(
                    nameof(entries), "replacement keys must be non-empty");
            if (!replacement.TryAdd(keyMemory.ToArray(), valueMemory.ToArray()))
                throw new InvalidDataException(
                    "replacement snapshot contains a duplicate key");
        }
        return replacement;
    }

    public static bool ContentEquals(
        IEnumerable<KeyValuePair<byte[], byte[]>> current,
        IEnumerable<KeyValuePair<byte[], byte[]>> expected)
    {
        using var currentEnumerator = current.GetEnumerator();
        using var expectedEnumerator = expected.GetEnumerator();
        while (true)
        {
            var hasCurrent = currentEnumerator.MoveNext();
            var hasExpected = expectedEnumerator.MoveNext();
            if (hasCurrent != hasExpected) return false;
            if (!hasCurrent) return true;
            if (!currentEnumerator.Current.Key.AsSpan().SequenceEqual(
                    expectedEnumerator.Current.Key)
                || !currentEnumerator.Current.Value.AsSpan().SequenceEqual(
                    expectedEnumerator.Current.Value))
                return false;
        }
    }
}
