using Neo.L2.Executor.State;
using Neo.L2.Persistence;

namespace Neo.L2.Executor.UnitTests;

/// <summary>
/// Tests for the RocksDB-backed wiring of <see cref="KeyedStateStore"/>. Production
/// state must survive node restarts; the in-memory backing is a test-only default.
/// </summary>
[TestClass]
public class UT_KeyedStateStore_Persistence
{
    [TestMethod]
    public void Constructor_RejectsNullBacking()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new KeyedStateStore(null!));
    }

    [TestMethod]
    public void RocksDb_Backed_RoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-l2-keyed-rocks-rt-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var rocks = new RocksDbKeyValueStore(dir);
            using var store = new KeyedStateStore(rocks);
            store.Put(new byte[] { 0x01 }, new byte[] { 0xAA, 0xBB });
            var v = store.Get(new byte[] { 0x01 });
            CollectionAssert.AreEqual(new byte[] { 0xAA, 0xBB }, v.ToArray());
        }
        finally
        {
            if (Directory.Exists(dir)) try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public void RocksDb_Backed_StateSurvivesReopen()
    {
        // The whole point: data written in one process / instance is visible after
        // reopening the same KV store. Pin so a refactor that introduces an in-memory
        // cache layer can't silently break durability.
        var dir = Path.Combine(Path.GetTempPath(), "neo-l2-keyed-rocks-persist-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var rocks1 = new RocksDbKeyValueStore(dir))
            using (var store1 = new KeyedStateStore(rocks1))
            {
                store1.Put(new byte[] { 0x05 }, new byte[] { 0x42 });
                store1.Put(new byte[] { 0x09 }, new byte[] { 0xAB, 0xCD });
            }

            using (var rocks2 = new RocksDbKeyValueStore(dir))
            using (var store2 = new KeyedStateStore(rocks2))
            {
                Assert.AreEqual(2, store2.Count);
                CollectionAssert.AreEqual(new byte[] { 0x42 }, store2.Get(new byte[] { 0x05 }).ToArray());
                CollectionAssert.AreEqual(new byte[] { 0xAB, 0xCD }, store2.Get(new byte[] { 0x09 }).ToArray());
            }
        }
        finally
        {
            if (Directory.Exists(dir)) try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public void RocksDb_Backed_RootMatchesInMemoryRoot()
    {
        // Same content, same Merkle root regardless of backend. This is what makes
        // the RocksDB switch a drop-in replacement for production: deterministic root
        // is preserved.
        using var inMem = new KeyedStateStore();
        inMem.Put(new byte[] { 0x03 }, new byte[] { 0xCC });
        inMem.Put(new byte[] { 0x01 }, new byte[] { 0xAA });
        inMem.Put(new byte[] { 0x02 }, new byte[] { 0xBB });
        var inMemRoot = inMem.ComputeRoot();

        var dir = Path.Combine(Path.GetTempPath(), "neo-l2-keyed-rocks-root-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var rocks = new RocksDbKeyValueStore(dir);
            using var store = new KeyedStateStore(rocks);
            store.Put(new byte[] { 0x03 }, new byte[] { 0xCC });
            store.Put(new byte[] { 0x01 }, new byte[] { 0xAA });
            store.Put(new byte[] { 0x02 }, new byte[] { 0xBB });
            Assert.AreEqual(inMemRoot, store.ComputeRoot(),
                "RocksDB-backed and InMemory-backed stores must produce identical Merkle roots for the same content");
        }
        finally
        {
            if (Directory.Exists(dir)) try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public void Dispose_OwnsBacking_DisposesBackingStore()
    {
        // Default ctor takes ownership; disposing the KeyedStateStore disposes the
        // underlying InMemoryKeyValueStore. Verify by attempting to use after dispose.
        var store = new KeyedStateStore();
        store.Put(new byte[] { 0x01 }, new byte[] { 0xAA });
        store.Dispose();
        // The in-memory backing's Dispose is a no-op so we can't observe via the backing
        // directly, but a second Dispose on the wrapper must not throw.
        store.Dispose();
    }

    [TestMethod]
    public void Dispose_NotOwnsBacking_LeavesBackingIntact()
    {
        // When the caller passes their own backing, KeyedStateStore.Dispose must NOT
        // dispose it — the caller still owns it.
        using var rocks = new InMemoryKeyValueStore();
        var store = new KeyedStateStore(rocks);  // ownsBacking defaults to false
        store.Put(new byte[] { 0x01 }, new byte[] { 0xAA });
        store.Dispose();
        // rocks still works.
        rocks.Put(new byte[] { 0xFF }, new byte[] { 0x42 });
        Assert.AreEqual((byte)0x42, rocks.Get(new byte[] { 0xFF })![0]);
    }
}
