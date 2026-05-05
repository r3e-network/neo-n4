using Neo.L2.Persistence;

namespace Neo.L2.Messaging.UnitTests;

/// <summary>
/// Tests for the IL2KeyValueStore-backed finalized-proof map in
/// <see cref="InMemoryMessageRouter"/>. Production wires
/// <see cref="RocksDbKeyValueStore"/> here so finalized message proofs survive node
/// restarts and remain queryable via GetMessageProofAsync.
/// </summary>
[TestClass]
public class UT_MessageRouter_Persistence
{
    [TestMethod]
    public void Constructor_RejectsNullFinalizedStore()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new InMemoryMessageRouter(null, null, null!));
    }

    [TestMethod]
    public async Task RocksDb_Backed_FinalizedProofSurvivesReopen()
    {
        // The whole point: finalize a message in router1, reopen the same RocksDB store
        // via router2, query the proof — must still be there.
        var dir = Path.Combine(Path.GetTempPath(), "neo-l2-router-rocks-" + Guid.NewGuid().ToString("N"));
        var msgHash = UInt256.Parse("0x" + new string('a', 64));
        var proofBytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        try
        {
            using (var rocks = new RocksDbKeyValueStore(dir))
            using (var router1 = new InMemoryMessageRouter(null, null, rocks))
            {
                router1.RecordFinalized(msgHash, proofBytes);
            }
            using (var rocks = new RocksDbKeyValueStore(dir))
            using (var router2 = new InMemoryMessageRouter(null, null, rocks))
            {
                var p = await router2.GetMessageProofAsync(msgHash);
                Assert.IsNotNull(p);
                CollectionAssert.AreEqual(proofBytes, p.Value.ToArray());
            }
        }
        finally
        {
            if (Directory.Exists(dir)) try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public async Task InMemory_KvStore_RoundTrips()
    {
        // The default ctor still works after the refactor — pin so a regression
        // doesn't break the no-config "just give me a router" usage.
        var router = new InMemoryMessageRouter();
        var msgHash = UInt256.Parse("0x" + new string('b', 64));
        router.RecordFinalized(msgHash, new byte[] { 0x01, 0x02 });

        var p = await router.GetMessageProofAsync(msgHash);
        Assert.IsNotNull(p);
        CollectionAssert.AreEqual(new byte[] { 0x01, 0x02 }, p.Value.ToArray());
    }

    [TestMethod]
    public async Task Dispose_OwnsBacking_DoesNotThrowOnDoubleDispose()
    {
        // Default ctor: router owns the backing. Disposing twice must not throw.
        var router = new InMemoryMessageRouter();
        router.RecordFinalized(UInt256.Zero, new byte[] { 0x01 });
        // Wait — can't call RecordFinalized with UInt256.Zero because that fails the null
        // check (UInt256.Zero IS null in some Neo versions? No — it's just zero bytes).
        // Actually it's fine, UInt256.Zero is not null reference.
        router.Dispose();
        router.Dispose();  // no-throw
    }

    [TestMethod]
    public async Task Dispose_NotOwnsBacking_LeavesBackingIntact()
    {
        using var rocks = new InMemoryKeyValueStore();
        var router = new InMemoryMessageRouter(null, null, rocks);
        var hash = UInt256.Parse("0x" + new string('1', 64));
        router.RecordFinalized(hash, new byte[] { 0x42 });
        router.Dispose();

        // rocks is still usable since router doesn't own it.
        var raw = rocks.Get(hash.GetSpan());
        Assert.IsNotNull(raw);
        Assert.AreEqual((byte)0x42, raw[0]);
    }
}
