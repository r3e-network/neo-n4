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
    public void Put_RejectsEmptyKey()
    {
        // Pin KeyedStateStore.cs:32's ArgumentOutOfRangeException.ThrowIfZero(key.Length).
        // Empty keys would otherwise hash via HashEntry to a leaf identifiable only by
        // value — every empty-key entry would distinguish only by HashEntry(0,...).
        // Surface the bad input at Put rather than letting it pollute the Merkle root.
        var s = new KeyedStateStore();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => s.Put(ReadOnlySpan<byte>.Empty, new byte[] { 0xAA }));
    }

    [TestMethod]
    public void Put_DefensiveCopy_CallerMutationAfterPutDoesNotCorruptStore()
    {
        // Symmetric write-side pin to EnumerateSorted_DefensiveCopy: KeyedStateStore.cs:33
        // calls ToArray() on both key and value, so a caller that mutates their original
        // backing buffer after Put returns must not corrupt stored state. Same iter-167
        // pattern as InMemoryL2RpcStore.RecordWithdrawalProof / InMemoryMessageRouter.
        var s = new KeyedStateStore();
        var keyBuf = new byte[] { 0x05 };
        var valBuf = new byte[] { 0x42 };
        s.Put(keyBuf, valBuf);

        keyBuf[0] = 0xFF;
        valBuf[0] = 0xFF;

        var stored = s.Get(new byte[] { 0x05 }).ToArray();
        Assert.AreEqual(0x42, stored[0], "stored value must not reflect post-Put mutation");
        Assert.IsTrue(s.Contains(new byte[] { 0x05 }), "stored key must not reflect post-Put mutation");
        Assert.IsFalse(s.Contains(new byte[] { 0xFF }), "post-mutation key must not appear in store");
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
    public void EnumerateSorted_DefensiveCopy_CallerCannotCorruptStore()
    {
        // Regression for iter 176: previously EnumerateSorted yielded the raw byte[]
        // references stored in the SortedDictionary, so a debug consumer that mutated
        // the returned bytes would silently corrupt the store's keys/values. Now
        // each yielded entry is a fresh clone — caller mutations are isolated.
        var s = new KeyedStateStore();
        s.Put(new byte[] { 0x05 }, new byte[] { 0x42 });

        foreach (var (key, value) in s.EnumerateSorted())
        {
            // Mutate the yielded buffers — must not leak back into the store.
            key[0] = 0xFF;
            value[0] = 0xFF;
        }

        var roundtrip = s.Get(new byte[] { 0x05 }).ToArray();
        Assert.AreEqual(0x42, roundtrip[0], "stored value must survive caller mutations");
        Assert.IsTrue(s.Contains(new byte[] { 0x05 }), "stored key must survive caller mutations");
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
