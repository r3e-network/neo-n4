using Neo.L2.Executor.State;

namespace Neo.L2.Executor.UnitTests;

[TestClass]
public class UT_KeyedStateStore
{
    [TestMethod]
    public void Empty_RootIsZero()
    {
        var s = new KeyedStateStore();
        Assert.AreEqual(UInt256.Zero, s.ComputeRoot());
        Assert.AreEqual(0, s.Count);
    }

    [TestMethod]
    public void PutGetReadsBack()
    {
        var s = new KeyedStateStore();
        s.Put(new byte[] { 0x01 }, new byte[] { 0xAA, 0xBB });
        var v = s.Get(new byte[] { 0x01 });
        CollectionAssert.AreEqual(new byte[] { 0xAA, 0xBB }, v.ToArray());
    }

    [TestMethod]
    public void PutDuplicateOverwrites()
    {
        var s = new KeyedStateStore();
        s.Put(new byte[] { 0x01 }, new byte[] { 0xAA });
        s.Put(new byte[] { 0x01 }, new byte[] { 0xBB });
        CollectionAssert.AreEqual(new byte[] { 0xBB }, s.Get(new byte[] { 0x01 }).ToArray());
        Assert.AreEqual(1, s.Count);
    }

    [TestMethod]
    public void Delete_RemovesEntry()
    {
        var s = new KeyedStateStore();
        s.Put(new byte[] { 0x01 }, new byte[] { 0xAA });
        Assert.IsTrue(s.Delete(new byte[] { 0x01 }));
        Assert.AreEqual(0, s.Count);
        Assert.IsFalse(s.Delete(new byte[] { 0x01 })); // already gone
    }

    [TestMethod]
    public void Root_ChangesOnEveryWrite()
    {
        var s = new KeyedStateStore();
        s.Put(new byte[] { 0x01 }, new byte[] { 0xAA });
        var r1 = s.ComputeRoot();
        s.Put(new byte[] { 0x02 }, new byte[] { 0xBB });
        var r2 = s.ComputeRoot();
        s.Put(new byte[] { 0x01 }, new byte[] { 0xCC });
        var r3 = s.ComputeRoot();

        Assert.AreNotEqual(r1, r2);
        Assert.AreNotEqual(r2, r3);
        Assert.AreNotEqual(r1, r3);
    }

    [TestMethod]
    public void Root_IsInsertOrderIndependent()
    {
        var a = new KeyedStateStore();
        a.Put(new byte[] { 0x03 }, new byte[] { 0xCC });
        a.Put(new byte[] { 0x01 }, new byte[] { 0xAA });
        a.Put(new byte[] { 0x02 }, new byte[] { 0xBB });

        var b = new KeyedStateStore();
        b.Put(new byte[] { 0x01 }, new byte[] { 0xAA });
        b.Put(new byte[] { 0x02 }, new byte[] { 0xBB });
        b.Put(new byte[] { 0x03 }, new byte[] { 0xCC });

        Assert.AreEqual(a.ComputeRoot(), b.ComputeRoot());
    }

    [TestMethod]
    public void EnumerateSorted_ReturnsLexOrder()
    {
        var s = new KeyedStateStore();
        s.Put(new byte[] { 0x03 }, new byte[] { 0xCC });
        s.Put(new byte[] { 0x01 }, new byte[] { 0xAA });
        s.Put(new byte[] { 0x02 }, new byte[] { 0xBB });

        var keys = s.EnumerateSorted().Select(e => e.Key[0]).ToArray();
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, keys);
    }

    [TestMethod]
    public async Task Oracle_ReturnsStoreRoot()
    {
        var store = new KeyedStateStore();
        store.Put(new byte[] { 0x01 }, new byte[] { 0xAA });
        var oracle = new KeyedStateRootOracle(store);

        var root = await oracle.ResolveAsync(
            UInt256.Zero, UInt256.Zero,
            new BatchBlockContext
            {
                L1FinalizedHeight = 1,
                FirstBlockTimestamp = 0,
                LastBlockTimestamp = 0,
                SequencerCommitteeHash = UInt256.Zero,
                Network = 0,
            });

        Assert.AreEqual(store.ComputeRoot(), root);
    }
}
