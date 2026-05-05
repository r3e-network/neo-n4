namespace Neo.L2.Persistence;

/// <summary>
/// Minimal byte-keyed, byte-valued store abstraction shared by L2 components that need
/// persistence (RPC stores, DA blobs, forced-inclusion queues, etc.). Implementations
/// must be safe for concurrent reads + writes; explicit transaction support is out of
/// scope (each Put/Delete is its own atomic write).
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
