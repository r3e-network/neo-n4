using System;
using System.Linq;
using Neo.L2.Persistence;
using Neo.Persistence;
using Neo.SmartContract;

namespace Neo.L2.Persistence.UnitTests;

/// <summary>
/// Tests for <see cref="L2DataCacheAdapter"/> — the bridge that lets Neo's
/// <see cref="DataCache"/> + <see cref="ApplicationEngine"/> run against an
/// L2 chain's <see cref="IL2KeyValueStore"/> without any special-case wiring.
/// </summary>
[TestClass]
public class UT_L2DataCacheAdapter
{
    private static StorageKey K(int id, params byte[] key) => new() { Id = id, Key = key };
    private static StorageItem V(params byte[] value) => new(value);

    [TestMethod]
    public void Add_Then_TryGet_ReturnsExactValue()
    {
        var store = new InMemoryKeyValueStore();
        var cache = new L2DataCacheAdapter(store);

        cache.Add(K(1, 0x01, 0x02), V(0xAA, 0xBB));
        cache.Commit(); // flush dirty entries to the underlying store
        var got = cache.TryGet(K(1, 0x01, 0x02));
        Assert.IsNotNull(got);
        CollectionAssert.AreEqual(new byte[] { 0xAA, 0xBB }, got!.Value.ToArray());
    }

    [TestMethod]
    public void Add_NotCommitted_VisibleInSameCache()
    {
        // Pin Neo's "uncommitted-but-visible-in-this-cache" semantics — a contract
        // that reads back a key it just wrote in the same execution must see the
        // new value, not the underlying store's pre-write state.
        var store = new InMemoryKeyValueStore();
        var cache = new L2DataCacheAdapter(store);

        cache.Add(K(1, 0x01), V(0xAB));
        var got = cache.TryGet(K(1, 0x01));
        Assert.IsNotNull(got);
        CollectionAssert.AreEqual(new byte[] { 0xAB }, got!.Value.ToArray());
    }

    [TestMethod]
    public void Delete_Persisted_RoundTripsThroughStore()
    {
        var store = new InMemoryKeyValueStore();
        var cache1 = new L2DataCacheAdapter(store);
        cache1.Add(K(1, 0x01), V(0xCD));
        cache1.Commit();

        var cache2 = new L2DataCacheAdapter(store);
        cache2.Delete(K(1, 0x01));
        cache2.Commit();

        var cache3 = new L2DataCacheAdapter(store);
        Assert.IsNull(cache3.TryGet(K(1, 0x01)));
        Assert.IsFalse(cache3.Contains(K(1, 0x01)));
    }

    [TestMethod]
    public void Update_Persisted_OverwritesExisting()
    {
        var store = new InMemoryKeyValueStore();
        var cache1 = new L2DataCacheAdapter(store);
        cache1.Add(K(1, 0x01), V(0x10));
        cache1.Commit();

        var cache2 = new L2DataCacheAdapter(store);
        var changed = cache2.GetAndChange(K(1, 0x01))
            ?? throw new InvalidOperationException("GetAndChange returned null for existing key");
        changed.Value = new byte[] { 0x20, 0x30 };
        cache2.Commit();

        var cache3 = new L2DataCacheAdapter(store);
        var got = cache3.TryGet(K(1, 0x01));
        Assert.IsNotNull(got);
        CollectionAssert.AreEqual(new byte[] { 0x20, 0x30 }, got!.Value.ToArray());
    }

    [TestMethod]
    public void Seek_ForwardFromKey_ReturnsSortedSubset()
    {
        var store = new InMemoryKeyValueStore();
        var cache = new L2DataCacheAdapter(store);
        cache.Add(K(1, 0x01), V(0x01));
        cache.Add(K(1, 0x02), V(0x02));
        cache.Add(K(1, 0x03), V(0x03));
        cache.Commit();

        var fresh = new L2DataCacheAdapter(store);
        var seen = fresh.Seek(K(1, 0x02).ToArray(), SeekDirection.Forward).ToArray();
        Assert.AreEqual(2, seen.Length);
        Assert.AreEqual(K(1, 0x02), seen[0].Key);
        Assert.AreEqual(K(1, 0x03), seen[1].Key);
    }

    [TestMethod]
    public void Seek_BackwardFromKey_ReturnsSortedSubsetDescending()
    {
        var store = new InMemoryKeyValueStore();
        var cache = new L2DataCacheAdapter(store);
        cache.Add(K(1, 0x01), V(0x01));
        cache.Add(K(1, 0x02), V(0x02));
        cache.Add(K(1, 0x03), V(0x03));
        cache.Commit();

        var fresh = new L2DataCacheAdapter(store);
        var seen = fresh.Seek(K(1, 0x02).ToArray(), SeekDirection.Backward).ToArray();
        Assert.AreEqual(2, seen.Length);
        Assert.AreEqual(K(1, 0x02), seen[0].Key);
        Assert.AreEqual(K(1, 0x01), seen[1].Key);
    }

    [TestMethod]
    public void StorageKeyByteLayout_RoundTripsThroughStore()
    {
        // Pin the wire-format invariant: StorageKey.ToArray() bytes are stored
        // verbatim, and the implicit byte[]→StorageKey cast parses them back to
        // the same Id + Key. A drift here would silently re-route a contract's
        // storage.
        var store = new InMemoryKeyValueStore();
        var cache = new L2DataCacheAdapter(store);
        var original = K(42, 0xCA, 0xFE, 0xBA, 0xBE);
        cache.Add(original, V(0xDD));
        cache.Commit();

        // Read back via Seek to exercise the SeekInternal byte→StorageKey path.
        var fresh = new L2DataCacheAdapter(store);
        var seen = fresh.Seek(original.ToArray(), SeekDirection.Forward).First();
        Assert.AreEqual(42, seen.Key.Id);
        CollectionAssert.AreEqual(new byte[] { 0xCA, 0xFE, 0xBA, 0xBE }, seen.Key.Key.ToArray());
    }

    [TestMethod]
    public void NullStore_RejectedAtConstruction()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new L2DataCacheAdapter(null!));
    }
}
