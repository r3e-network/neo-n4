using Neo.L2.Executor.State;

namespace Sample.CounterChainExecutor.UnitTests;

/// <summary>
/// Tests for <see cref="KeyedStateStoreAdapter"/> — the production-ready bridge between
/// <c>ICounterChainState</c> (executor-side) and <c>KeyedStateStore</c> (framework-side).
/// </summary>
[TestClass]
public class UT_KeyedStateStoreAdapter
{
    [TestMethod]
    public void Adapter_PutThenGet_RoundTrips()
    {
        var store = new KeyedStateStore();
        var adapter = new KeyedStateStoreAdapter(store);
        var key = new byte[] { 1, 2, 3 };
        var value = new byte[] { 0xAA, 0xBB };

        adapter.Put(key, value);
        Assert.IsTrue(adapter.TryGet(key, out var read));
        CollectionAssert.AreEqual(value, read);
    }

    [TestMethod]
    public void Adapter_TryGetMissingKey_ReturnsFalse()
    {
        var adapter = new KeyedStateStoreAdapter(new KeyedStateStore());
        var missing = new byte[] { 9, 9, 9 };
        Assert.IsFalse(adapter.TryGet(missing, out var v));
        Assert.AreEqual(0, v.Length);
    }

    [TestMethod]
    public void Adapter_WritesParticipateInComputeRoot()
    {
        // The whole point of the adapter: writes flow into the SAME store the post-state
        // root oracle hashes. Pin that ComputeRoot reflects the executor's writes.
        var store = new KeyedStateStore();
        var adapter = new KeyedStateStoreAdapter(store);

        var emptyRoot = store.ComputeRoot();
        Assert.AreEqual(UInt256.Zero, emptyRoot);

        adapter.Put(new byte[] { 0x42 }, new byte[] { 0xFF });
        var nonEmptyRoot = store.ComputeRoot();
        Assert.AreNotEqual(UInt256.Zero, nonEmptyRoot);

        // Same write through KeyedStateStore directly should produce the same root.
        var store2 = new KeyedStateStore();
        store2.Put(new byte[] { 0x42 }, new byte[] { 0xFF });
        Assert.AreEqual(store2.ComputeRoot(), nonEmptyRoot,
            "adapter writes must produce the same root as direct KeyedStateStore writes");
    }
}
