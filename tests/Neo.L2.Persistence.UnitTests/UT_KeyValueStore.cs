namespace Neo.L2.Persistence.UnitTests;

/// <summary>
/// Shared contract tests for <see cref="IL2KeyValueStore"/>. Run against both
/// <see cref="InMemoryKeyValueStore"/> and <see cref="RocksDbKeyValueStore"/> so any
/// future backend (e.g., LevelDB, SQLite) just needs to add a TestClass that inherits
/// from <see cref="KeyValueStoreContractTests{T}"/> and provides a Create() factory.
/// </summary>
[TestClass]
public class UT_InMemoryKeyValueStore : KeyValueStoreContractTests
{
    protected override IL2KeyValueStore Create() => new InMemoryKeyValueStore();
}

[TestClass]
public class UT_RocksDbKeyValueStore : KeyValueStoreContractTests
{
    private string? _tempDir;

    protected override IL2KeyValueStore Create()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "neo-l2-rocks-" + Guid.NewGuid().ToString("N"));
        return new RocksDbKeyValueStore(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (_tempDir is not null && Directory.Exists(_tempDir))
        {
            // RocksDB drops some files even after Dispose; best-effort cleanup so the
            // temp dir doesn't accumulate across test runs.
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public void Persistence_DataSurvivesReopen()
    {
        // The whole point of using RocksDB instead of in-memory: data survives the
        // process. Pin it. (Run only against the RocksDB backend; in-memory has no
        // disk so this test wouldn't apply.)
        var dir = Path.Combine(Path.GetTempPath(), "neo-l2-rocks-persist-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var s1 = new RocksDbKeyValueStore(dir))
            {
                s1.Put(new byte[] { 0x01 }, new byte[] { 0xAA, 0xBB });
            }
            // Reopen — same path, fresh handle.
            using (var s2 = new RocksDbKeyValueStore(dir))
            {
                var bytes = s2.Get(new byte[] { 0x01 });
                Assert.IsNotNull(bytes);
                CollectionAssert.AreEqual(new byte[] { 0xAA, 0xBB }, bytes);
            }
        }
        finally
        {
            if (Directory.Exists(dir)) try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public void Constructor_RejectsEmptyDataDirectory()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new RocksDbKeyValueStore(""));
    }

    [TestMethod]
    public void DataDirectory_ExposesConfiguredPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-l2-rocks-path-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var store = new RocksDbKeyValueStore(dir);
            Assert.AreEqual(dir, store.DataDirectory);
        }
        finally
        {
            if (Directory.Exists(dir)) try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}

/// <summary>Backend-independent contract tests, instantiated by per-backend TestClass.</summary>
public abstract class KeyValueStoreContractTests
{
    protected abstract IL2KeyValueStore Create();

    [TestMethod]
    public void Empty_CountIsZero()
    {
        using var s = Create();
        Assert.AreEqual(0L, s.Count);
    }

    [TestMethod]
    public void PutGet_RoundTrips()
    {
        using var s = Create();
        s.Put(new byte[] { 0x01 }, new byte[] { 0xAA, 0xBB });
        var got = s.Get(new byte[] { 0x01 });
        Assert.IsNotNull(got);
        CollectionAssert.AreEqual(new byte[] { 0xAA, 0xBB }, got);
    }

    [TestMethod]
    public void Get_AbsentReturnsNull()
    {
        using var s = Create();
        Assert.IsNull(s.Get(new byte[] { 0xFF }));
    }

    [TestMethod]
    public void Put_OverwritesExisting()
    {
        using var s = Create();
        s.Put(new byte[] { 0x01 }, new byte[] { 0xAA });
        s.Put(new byte[] { 0x01 }, new byte[] { 0xBB });
        CollectionAssert.AreEqual(new byte[] { 0xBB }, s.Get(new byte[] { 0x01 }));
        Assert.AreEqual(1L, s.Count);
    }

    [TestMethod]
    public void Delete_RemovesEntry()
    {
        using var s = Create();
        s.Put(new byte[] { 0x01 }, new byte[] { 0xAA });
        Assert.IsTrue(s.Delete(new byte[] { 0x01 }));
        Assert.IsFalse(s.Delete(new byte[] { 0x01 }), "second delete returns false (already gone)");
        Assert.IsNull(s.Get(new byte[] { 0x01 }));
        Assert.AreEqual(0L, s.Count);
    }

    [TestMethod]
    public void Contains_TracksPresence()
    {
        using var s = Create();
        Assert.IsFalse(s.Contains(new byte[] { 0x01 }));
        s.Put(new byte[] { 0x01 }, new byte[] { 0xAA });
        Assert.IsTrue(s.Contains(new byte[] { 0x01 }));
    }

    [TestMethod]
    public void Put_RejectsEmptyKey()
    {
        using var s = Create();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => s.Put(ReadOnlySpan<byte>.Empty, new byte[] { 0xAA }));
    }

    [TestMethod]
    public void EnumeratePrefix_FiltersByPrefix()
    {
        using var s = Create();
        s.Put(new byte[] { 0x10, 0x01 }, new byte[] { 0xA1 });
        s.Put(new byte[] { 0x10, 0x02 }, new byte[] { 0xA2 });
        s.Put(new byte[] { 0x20, 0x01 }, new byte[] { 0xB1 });

        var prefix10 = s.EnumeratePrefix(new byte[] { 0x10 }).Select(kv => kv.Value[0]).ToArray();
        Assert.AreEqual(2, prefix10.Length);
        CollectionAssert.AreEquivalent(new[] { (byte)0xA1, (byte)0xA2 }, prefix10);

        var prefix20 = s.EnumeratePrefix(new byte[] { 0x20 }).Select(kv => kv.Value[0]).ToArray();
        Assert.AreEqual(1, prefix20.Length);
        Assert.AreEqual((byte)0xB1, prefix20[0]);
    }

    [TestMethod]
    public void EnumeratePrefix_EmptyPrefixReturnsAll()
    {
        using var s = Create();
        s.Put(new byte[] { 0x01 }, new byte[] { 0xAA });
        s.Put(new byte[] { 0x02 }, new byte[] { 0xBB });
        var all = s.EnumeratePrefix(ReadOnlySpan<byte>.Empty).ToList();
        Assert.AreEqual(2, all.Count);
    }

    [TestMethod]
    public void EnumeratePrefix_DefensiveCopy_MutationDoesNotCorruptStore()
    {
        using var s = Create();
        s.Put(new byte[] { 0x05 }, new byte[] { 0x42 });
        foreach (var (key, value) in s.EnumeratePrefix(ReadOnlySpan<byte>.Empty))
        {
            key[0] = 0xFF;
            value[0] = 0xFF;
        }
        var roundtrip = s.Get(new byte[] { 0x05 });
        Assert.IsNotNull(roundtrip);
        Assert.AreEqual((byte)0x42, roundtrip[0], "stored bytes survive caller mutations");
    }

    [TestMethod]
    public void Get_DefensiveCopy_MutationDoesNotCorruptStore()
    {
        using var s = Create();
        s.Put(new byte[] { 0x05 }, new byte[] { 0x42 });
        var first = s.Get(new byte[] { 0x05 })!;
        first[0] = 0xFF;
        var second = s.Get(new byte[] { 0x05 })!;
        Assert.AreEqual((byte)0x42, second[0]);
    }

    [TestMethod]
    public void Count_ReflectsLifecycle()
    {
        using var s = Create();
        Assert.AreEqual(0L, s.Count);
        s.Put(new byte[] { 0x01 }, new byte[] { 0xAA });
        s.Put(new byte[] { 0x02 }, new byte[] { 0xBB });
        Assert.AreEqual(2L, s.Count);
        s.Delete(new byte[] { 0x01 });
        Assert.AreEqual(1L, s.Count);
    }
}
