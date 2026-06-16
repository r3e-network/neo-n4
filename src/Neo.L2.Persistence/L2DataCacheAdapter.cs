using System;
using System.Collections.Generic;
using System.Linq;
using Neo.L2;
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
        // The L2 KV stores' EnumeratePrefix iterates in ascending key order and is itself
        // lazy (the RocksDB backend yields straight off a live iterator; InMemory yields
        // off a single locked snapshot). Neo VM's interop almost always seeks forward;
        // backward seeks come from native-contract enumerators that snapshot then iterate.
        //
        // Forward: stream the ascending enumeration and yield entries with key >= keyOrPrefix
        //   without materializing the store first — for the production RocksDB backend this
        //   pulls and clones one entry at a time instead of cloning the whole store per seek.
        //   (Seeking the underlying iterator straight to keyOrPrefix would also skip the
        //   leading keys < keyOrPrefix, but that requires a seek-aware method on
        //   IL2KeyValueStore; this streaming form already removes the O(N)-memory blowup.)
        // Backward: a forward-only enumeration can't be reversed without buffering, so the
        //   descending path materializes the matching (key <= keyOrPrefix) entries before
        //   reversing. A reverse-iterator method on IL2KeyValueStore would remove this; that
        //   interface change is out of scope here.
        IEnumerable<(byte[] Key, byte[] Value)> filtered;
        if (direction == SeekDirection.Forward)
        {
            filtered = _store.EnumeratePrefix(ReadOnlySpan<byte>.Empty)
                .Where(kv => LexicographicByteArrayComparer.Instance.Compare(kv.Key, keyOrPrefix) >= 0);
        }
        else
        {
            filtered = _store.EnumeratePrefix(ReadOnlySpan<byte>.Empty)
                .Where(kv => LexicographicByteArrayComparer.Instance.Compare(kv.Key, keyOrPrefix) <= 0)
                .Reverse();
        }

        foreach (var (k, v) in filtered)
        {
            // Construct StorageKey via the implicit byte[] cast — Neo's
            // StorageKey internal ctor that parses [4B Id LE][KeyBytes] is
            // exposed only as an implicit conversion. StorageItem has a
            // public byte[] ctor.
            yield return ((StorageKey)k, new StorageItem(v));
        }
    }
}
