using Neo.L2.Executor.Effects;
using Neo.L2.Executor.State;
using Neo.L2.Persistence;

namespace Neo.L2.Executor.UnitTests;

/// <summary>Tests for the shared read-through state transaction seam.</summary>
[TestClass]
public class UT_ExecutionStateTransaction
{
    [TestMethod]
    public void Overlay_ReadsBackingAndKeepsWritesIsolatedUntilCommit()
    {
        using var store = new InMemoryKeyValueStore();
        store.Put([0x01], [0x10]);
        using var transaction = new ExecutionStateTransaction(store);

        CollectionAssert.AreEqual(new byte[] { 0x10 }, transaction.Get([0x01]));
        transaction.Put([0x01], [0x11]);
        transaction.Put([0x02], [0x20]);

        CollectionAssert.AreEqual(new byte[] { 0x10 }, store.Get([0x01]));
        Assert.IsNull(store.Get([0x02]));
        transaction.Commit();
        CollectionAssert.AreEqual(new byte[] { 0x11 }, store.Get([0x01]));
        CollectionAssert.AreEqual(new byte[] { 0x20 }, store.Get([0x02]));
    }

    [TestMethod]
    public void Rollback_DiscardsPutAndDelete()
    {
        using var store = new InMemoryKeyValueStore();
        store.Put([0x01], [0x10]);
        using var transaction = new ExecutionStateTransaction(store);
        transaction.Delete([0x01]);
        transaction.Put([0x02], [0x20]);

        transaction.Rollback();

        CollectionAssert.AreEqual(new byte[] { 0x10 }, store.Get([0x01]));
        Assert.IsNull(store.Get([0x02]));
    }

    [TestMethod]
    public void Changes_AreSortedAndBindBeforeAndAfterImages()
    {
        using var store = new InMemoryKeyValueStore();
        store.Put([0x02], [0x20]);
        using var transaction = new ExecutionStateTransaction(store);
        transaction.Put([0x02], [0x21]);
        transaction.Put([0x01], [0x10]);

        var changes = transaction.GetChanges();

        Assert.AreEqual(2, changes.Count);
        CollectionAssert.AreEqual(new byte[] { 0x01 }, changes[0].Key.ToArray());
        Assert.IsFalse(changes[0].OldValue.HasValue);
        CollectionAssert.AreEqual(new byte[] { 0x10 }, changes[0].NewValue!.Value.ToArray());
        CollectionAssert.AreEqual(new byte[] { 0x20 }, changes[1].OldValue!.Value.ToArray());
        CollectionAssert.AreEqual(new byte[] { 0x21 }, changes[1].NewValue!.Value.ToArray());
        Assert.AreEqual(CanonicalStorageOperation.Put, changes[1].Operation);
    }

    [TestMethod]
    public void Commit_RejectsConcurrentBeforeImageChange()
    {
        using var store = new InMemoryKeyValueStore();
        store.Put([0x01], [0x10]);
        using var transaction = new ExecutionStateTransaction(store);
        transaction.Put([0x01], [0x11]);
        store.Put([0x01], [0x12]);

        Assert.ThrowsExactly<InvalidOperationException>(() => transaction.Commit());
        CollectionAssert.AreEqual(new byte[] { 0x12 }, store.Get([0x01]));
    }
}
