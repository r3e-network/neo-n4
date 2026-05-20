using System;
using System.Collections.Generic;
using System.Linq;
using Neo.Persistence;
using Neo.SmartContract;

namespace Neo.L2.Persistence;

/// <summary>
/// Adapter that exposes an <see cref="IL2KeyValueStore"/> as Neo's abstract
/// <see cref="DataCache"/>. Lets <see cref="ApplicationEngine"/> + the rest of
/// Neo's VM run against an L2 chain's KV store with no special-case wiring.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="StorageKey"/>'s wire bytes (<c>[4B Id LE][KeyBytes]</c>) are used
/// verbatim as the L2 KV store's key. <see cref="StorageItem"/>'s value bytes
/// are stored as the L2 KV store's value. Both are serialized via Neo's
/// canonical encoders (<c>StorageKey.ToArray()</c>, <c>StorageItem.Value.ToArray()</c>),
/// so the on-disk layout is byte-compatible with anything a Neo node would
/// produce — caller can swap an L2 KV store for a Neo `IStore` once that
/// migration becomes desirable.
/// </para>
/// <para>
/// <see cref="DataCache"/>'s base class handles the read-through cache + the
/// dirty-tracking machinery; this adapter only implements the 7 leaf-level
/// store methods.
/// </para>
/// </remarks>
public sealed class L2DataCacheAdapter : DataCache
{
    private readonly IL2KeyValueStore _store;

    /// <summary>Diagnostic: count of AddInternal calls.</summary>
    public int AddCount { get; private set; }
    /// <summary>Diagnostic: count of UpdateInternal calls.</summary>
    public int UpdateCount { get; private set; }
    /// <summary>Diagnostic: count of DeleteInternal calls.</summary>
    public int DeleteCount { get; private set; }

    /// <summary>Construct against the underlying L2 KV store. The cache is mutable by default.</summary>
    public L2DataCacheAdapter(IL2KeyValueStore store, bool readOnly = false) : base(readOnly)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    protected override void AddInternal(StorageKey key, StorageItem value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        AddCount++;
        // Neo's DataCache base contract: AddInternal is called only when the
        // key is not already present (the base class guards). We honor that
        // with a Put — same byte-layout semantics as the production KV store.
        _store.Put(key.ToArray(), value.Value.Span);
    }

    /// <inheritdoc />
    protected override void DeleteInternal(StorageKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        DeleteCount++;
        _store.Delete(key.ToArray());
    }

    /// <inheritdoc />
    protected override bool ContainsInternal(StorageKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _store.Contains(key.ToArray());
    }

    /// <inheritdoc />
    protected override StorageItem GetInternal(StorageKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var bytes = _store.Get(key.ToArray())
            ?? throw new KeyNotFoundException($"key {key} not found");
        return new StorageItem(bytes);
    }

    /// <inheritdoc />
    protected override StorageItem? TryGetInternal(StorageKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var bytes = _store.Get(key.ToArray());
        return bytes is null ? null : new StorageItem(bytes);
    }

    /// <inheritdoc />
    protected override void UpdateInternal(StorageKey key, StorageItem value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        UpdateCount++;
        // Update vs Add: base class guarantees the key existed at the time
        // Update was called. A Put is correct either way (insert-or-replace).
        _store.Put(key.ToArray(), value.Value.Span);
    }

    /// <inheritdoc />
    protected override IEnumerable<(StorageKey Key, StorageItem Value)> SeekInternal(byte[] keyOrPrefix, SeekDirection direction)
    {
        ArgumentNullException.ThrowIfNull(keyOrPrefix);
        // The L2 KV stores' EnumeratePrefix iterates in ascending key order.
        // Forward seek: start at keyOrPrefix and walk ascending.
        // Backward seek: enumerate the full prefix-less store (or the
        //   matching prefix), filter to keys <= keyOrPrefix, return descending.
        // Neo VM's interop almost always seeks forward; backward seeks come
        // from native-contract enumerators that snapshot then iterate.
        // EnumeratePrefix already delivers in lex order per the IL2KeyValueStore
        // contract (both InMemory and RocksDB backends honor it); no OrderBy needed.
        var snapshot = _store.EnumeratePrefix(ReadOnlySpan<byte>.Empty).ToList();

        IEnumerable<(byte[] Key, byte[] Value)> filtered = direction == SeekDirection.Forward
            ? snapshot.Where(kv => ByteArrayComparer.Instance.Compare(kv.Key, keyOrPrefix) >= 0)
            : snapshot.Where(kv => ByteArrayComparer.Instance.Compare(kv.Key, keyOrPrefix) <= 0)
                      .Reverse();

        foreach (var (k, v) in filtered)
        {
            // Construct StorageKey via the implicit byte[] cast — Neo's
            // StorageKey internal ctor that parses [4B Id LE][KeyBytes] is
            // exposed only as an implicit conversion. StorageItem has a
            // public byte[] ctor.
            yield return ((StorageKey)k, new StorageItem(v));
        }
    }

    private sealed class ByteArrayComparer : IComparer<byte[]>
    {
        public static readonly ByteArrayComparer Instance = new();
        public int Compare(byte[]? x, byte[]? y)
        {
            if (x is null || y is null) return (x is null ? 0 : 1) - (y is null ? 0 : 1);
            return x.AsSpan().SequenceCompareTo(y);
        }
    }
}
