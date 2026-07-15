using Neo.L2.Executor.Effects;
using Neo.L2.Executor.State;
using Neo.L2.Persistence;

namespace Neo.L2.Executor.UnitTests;

/// <summary>Tests for the shared read-through state transaction seam.</summary>
[TestClass]
public class UT_ExecutionStateTransaction
{
    [TestMethod]
    public void Constructor_RejectsNullBackingStore()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new ExecutionStateTransaction(null!));
    }

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
        Assert.AreEqual(CanonicalStorageOperation.Add, changes[0].Operation);
        CollectionAssert.AreEqual(new byte[] { 0x20 }, changes[1].OldValue!.Value.ToArray());
        CollectionAssert.AreEqual(new byte[] { 0x21 }, changes[1].NewValue!.Value.ToArray());
        Assert.AreEqual(CanonicalStorageOperation.Update, changes[1].Operation);
    }

    [TestMethod]
    public void Changes_CollapseNetNoOpAndClassifyDelete()
    {
        using var store = new InMemoryKeyValueStore();
        store.Put([0x01], [0x10]);
        store.Put([0x02], [0x20]);
        using var transaction = new ExecutionStateTransaction(store);
        transaction.Put([0x01], [0x11]);
        transaction.Put([0x01], [0x10]);
        transaction.Delete([0x02]);

        var changes = transaction.GetChanges();

        Assert.AreEqual(1, changes.Count);
        CollectionAssert.AreEqual(new byte[] { 0x02 }, changes[0].Key.ToArray());
        Assert.AreEqual(CanonicalStorageOperation.Delete, changes[0].Operation);
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

    [TestMethod]
    public void Overlay_AtomicOperationsEnumerationAndCountReflectNetState()
    {
        using var store = new InMemoryKeyValueStore();
        store.Put([0x01, 0x01], [0x10]);
        store.Put([0x02, 0x01], [0x20]);
        using var transaction = new ExecutionStateTransaction(store);

        Assert.AreEqual(2, transaction.Count);
        Assert.IsTrue(transaction.Contains([0x01, 0x01]));
        Assert.IsFalse(transaction.TryPut([0x01, 0x01], [0x11]));
        Assert.IsTrue(transaction.TryPut([0x01, 0x02], [0x12]));
        transaction.Put([0x01, 0x02], [0x13]);
        Assert.IsFalse(transaction.CompareExchange([0x01, 0x03], [], [0x14]));
        Assert.IsFalse(transaction.CompareExchange([0x01, 0x01], [0x99], [0x14]));
        Assert.IsTrue(transaction.CompareExchange([0x01, 0x01], [0x10], [0x14]));
        Assert.IsFalse(transaction.Delete([0x01, 0x03]));
        Assert.IsTrue(transaction.Delete([0x01, 0x02]));
        transaction.Put([0x02, 0x01], [0x21]);
        Assert.IsTrue(transaction.Delete([0x02, 0x01]));

        var rows = transaction.EnumeratePrefix([0x01]).ToArray();

        Assert.AreEqual(1, transaction.Count);
        Assert.AreEqual(1, rows.Length);
        CollectionAssert.AreEqual(new byte[] { 0x01, 0x01 }, rows[0].Key);
        CollectionAssert.AreEqual(new byte[] { 0x14 }, rows[0].Value);
        rows[0].Key[0] = 0xFF;
        rows[0].Value[0] = 0xFF;
        CollectionAssert.AreEqual(new byte[] { 0x14 }, transaction.Get([0x01, 0x01]));
    }

    [TestMethod]
    public void Operations_RejectEmptyKeysAndInvalidLifecycle()
    {
        using var store = new InMemoryKeyValueStore();
        using (var transaction = new ExecutionStateTransaction(store))
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => transaction.Put([], [0x01]));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => transaction.TryPut([], [0x01]));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                () => transaction.CompareExchange([], [], [0x01]));
            Assert.IsFalse(transaction.Delete([]));
            transaction.Put([0x01], [0x10]);
            transaction.Commit();
            Assert.IsTrue(transaction.IsCommitted);
            transaction.Rollback();
            Assert.ThrowsExactly<InvalidOperationException>(() => transaction.Put([0x02], [0x20]));
            CollectionAssert.AreEqual(new byte[] { 0x10 }, transaction.Get([0x01]));
        }

        using var rolledBack = new ExecutionStateTransaction(store);
        rolledBack.Put([0x02], [0x20]);
        rolledBack.Rollback();
        rolledBack.Rollback();
        Assert.ThrowsExactly<InvalidOperationException>(() => rolledBack.Get([0x01]));
        Assert.ThrowsExactly<InvalidOperationException>(() => rolledBack.Contains([0x01]));
        Assert.ThrowsExactly<InvalidOperationException>(
            () => rolledBack.EnumeratePrefix([]).ToArray());
        Assert.ThrowsExactly<InvalidOperationException>(() => rolledBack.GetChanges());
    }

    [TestMethod]
    public void Commit_FailureRestoresEveryAppliedBeforeImage()
    {
        using var store = new FaultInjectingStore(3);
        store.Seed([0x02], [0x20]);
        store.Seed([0x03], [0x30]);
        using var transaction = new ExecutionStateTransaction(store);
        transaction.Put([0x01], [0x10]);
        transaction.Put([0x02], [0x21]);
        transaction.Delete([0x03]);

        Assert.ThrowsExactly<InvalidOperationException>(() => transaction.Commit());

        Assert.IsNull(store.Get([0x01]));
        CollectionAssert.AreEqual(new byte[] { 0x20 }, store.Get([0x02]));
        CollectionAssert.AreEqual(new byte[] { 0x30 }, store.Get([0x03]));
    }

    [TestMethod]
    public void Commit_AndCompensationFailurePreserveBothErrors()
    {
        using var store = new FaultInjectingStore(2, 3);
        store.Seed([0x02], [0x20]);
        using var transaction = new ExecutionStateTransaction(store);
        transaction.Put([0x01], [0x10]);
        transaction.Put([0x02], [0x21]);

        var error = Assert.ThrowsExactly<AggregateException>(() => transaction.Commit());

        Assert.AreEqual(2, error.InnerExceptions.Count);
        Assert.IsTrue(store.Contains([0x01]));
        CollectionAssert.AreEqual(new byte[] { 0x20 }, store.Get([0x02]));
    }

    private sealed class FaultInjectingStore(params int[] failingMutations) : IL2KeyValueStore
    {
        private readonly InMemoryKeyValueStore _inner = new();
        private readonly HashSet<int> _failingMutations = failingMutations.ToHashSet();
        private int _mutationCount;

        public long Count => _inner.Count;

        public void Seed(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value) => _inner.Put(key, value);

        public void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
        {
            FailIfConfigured();
            _inner.Put(key, value);
        }

        public bool TryPut(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value) =>
            _inner.TryPut(key, value);

        public bool CompareExchange(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> expectedValue,
            ReadOnlySpan<byte> newValue) =>
            _inner.CompareExchange(key, expectedValue, newValue);

        public byte[]? Get(ReadOnlySpan<byte> key) => _inner.Get(key);

        public bool Delete(ReadOnlySpan<byte> key)
        {
            FailIfConfigured();
            return _inner.Delete(key);
        }

        public bool Contains(ReadOnlySpan<byte> key) => _inner.Contains(key);

        public IEnumerable<(byte[] Key, byte[] Value)> EnumeratePrefix(ReadOnlySpan<byte> prefix) =>
            _inner.EnumeratePrefix(prefix);

        public void Dispose() => _inner.Dispose();

        private void FailIfConfigured()
        {
            _mutationCount++;
            if (_failingMutations.Contains(_mutationCount))
                throw new InvalidOperationException($"injected mutation failure {_mutationCount}");
        }
    }
}
