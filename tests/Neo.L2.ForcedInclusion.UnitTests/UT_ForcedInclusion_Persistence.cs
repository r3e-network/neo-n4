using Neo.L2.Persistence;

namespace Neo.L2.ForcedInclusion.UnitTests;

/// <summary>
/// Tests for the IL2KeyValueStore-backed consumed-nonce set in
/// <see cref="InMemoryForcedInclusionSource"/>. Production wires
/// <see cref="RocksDbKeyValueStore"/> here so replay protection survives node restarts.
/// </summary>
[TestClass]
public class UT_ForcedInclusion_Persistence
{
    private static ForcedInclusionEntry MkEntry(ulong nonce) => new()
    {
        Nonce = nonce,
        Sender = UInt160.Zero,
        TxHash = UInt256.Zero,
        SerializedTx = new byte[] { 0xAA, 0xBB },
        DeadlineUnixSeconds = 1_700_000_000,
    };

    [TestMethod]
    public void Constructor_RejectsNullConsumedStore()
    {
        // Explicit cast picks the (uint, IL2KeyValueStore, ...) overload — without it
        // the compiler resolves to the (uint, IL2Metrics?) overload where null metrics
        // is legal.
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new InMemoryForcedInclusionSource(1001, (IL2KeyValueStore)null!));
    }

    [TestMethod]
    public async Task RocksDb_Backed_ConsumedNonceSurvivesReopen()
    {
        // The whole point of persisting the consumed-nonce set: a node that restarts
        // mid-processing must NOT re-include a forced tx that was already consumed.
        // Without this, the L1's at-most-once contract for forced inclusion is broken.
        var dir = Path.Combine(Path.GetTempPath(), "neo-l2-fi-rocks-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var rocks = new RocksDbKeyValueStore(dir))
            using (var src = new InMemoryForcedInclusionSource(1001, rocks))
            {
                src.Enqueue(MkEntry(42));
                await src.MarkConsumedAsync(42);
            }

            using (var rocks = new RocksDbKeyValueStore(dir))
            using (var src = new InMemoryForcedInclusionSource(1001, rocks))
            {
                // Re-enqueueing the same nonce on the new instance must throw —
                // the consumed-nonce set carried over from the persistent store.
                Assert.ThrowsExactly<InvalidOperationException>(
                    () => src.Enqueue(MkEntry(42)));
            }
        }
        finally
        {
            if (Directory.Exists(dir)) try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public async Task RocksDb_Backed_FreshNonceStillAllowed()
    {
        // Companion to the survival test: nonces NOT consumed in the prior instance
        // must still be enqueueable in the new instance.
        var dir = Path.Combine(Path.GetTempPath(), "neo-l2-fi-rocks-fresh-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var rocks = new RocksDbKeyValueStore(dir))
            using (var src = new InMemoryForcedInclusionSource(1001, rocks))
            {
                src.Enqueue(MkEntry(7));
                await src.MarkConsumedAsync(7);
            }
            using (var rocks = new RocksDbKeyValueStore(dir))
            using (var src = new InMemoryForcedInclusionSource(1001, rocks))
            {
                // 7 is consumed (rejected) but 8 is fresh.
                src.Enqueue(MkEntry(8));
                Assert.AreEqual(1, src.PendingCount);
            }
        }
        finally
        {
            if (Directory.Exists(dir)) try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public void DefaultCtor_StillWorks_BackwardCompat()
    {
        // Default ctor with no IL2KeyValueStore — uses InMemoryKeyValueStore.
        var src = new InMemoryForcedInclusionSource(1001);
        src.Enqueue(MkEntry(1));
        Assert.AreEqual(1, src.PendingCount);
    }

    [TestMethod]
    public void Dispose_OwnsConsumed_DoesNotThrowOnDoubleDispose()
    {
        var src = new InMemoryForcedInclusionSource(1001);
        src.Dispose();
        src.Dispose();
    }

    [TestMethod]
    public async Task Dispose_NotOwnsConsumed_LeavesBackingIntact()
    {
        using var rocks = new InMemoryKeyValueStore();
        var src = new InMemoryForcedInclusionSource(1001, rocks);
        src.Enqueue(MkEntry(1));
        await src.MarkConsumedAsync(1);
        src.Dispose();

        // rocks still works.
        rocks.Put(new byte[] { 0xFF }, new byte[] { 0x42 });
        Assert.AreEqual((byte)0x42, rocks.Get(new byte[] { 0xFF })![0]);
    }
}
