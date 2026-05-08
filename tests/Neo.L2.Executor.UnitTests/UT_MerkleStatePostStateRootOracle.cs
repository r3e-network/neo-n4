using System.Threading.Tasks;
using Neo;
using Neo.L2;
using Neo.L2.Executor;
using Neo.L2.Persistence;
using Neo.L2.State;

namespace Neo.L2.Executor.UnitTests;

[TestClass]
public class UT_MerkleStatePostStateRootOracle
{
    private static readonly BatchBlockContext Ctx = new()
    {
        L1FinalizedHeight = 1,
        FirstBlockTimestamp = 1_700_000_000_000UL,
        LastBlockTimestamp = 1_700_000_005_000UL,
        SequencerCommitteeHash = UInt256.Zero,
        Network = 0x4F454E,
    };

    [TestMethod]
    public async Task EmptyStore_ReturnsZeroRoot()
    {
        using var store = new InMemoryKeyValueStore();
        var oracle = new MerkleStatePostStateRootOracle(store);
        var root = await oracle.ResolveAsync(UInt256.Zero, UInt256.Zero, Ctx);
        Assert.AreEqual(UInt256.Zero, root);
    }

    [TestMethod]
    public async Task RootChanges_AfterStateMutation()
    {
        using var store = new InMemoryKeyValueStore();
        var oracle = new MerkleStatePostStateRootOracle(store);

        var emptyRoot = await oracle.ResolveAsync(UInt256.Zero, UInt256.Zero, Ctx);
        store.Put(new byte[] { 0x01 }, new byte[] { 0xAA });
        var afterPut = await oracle.ResolveAsync(UInt256.Zero, UInt256.Zero, Ctx);
        store.Put(new byte[] { 0x01 }, new byte[] { 0xBB });
        var afterUpdate = await oracle.ResolveAsync(UInt256.Zero, UInt256.Zero, Ctx);

        Assert.AreNotEqual(emptyRoot, afterPut, "any state mutation must change the root");
        Assert.AreNotEqual(afterPut, afterUpdate, "value update must change the root");
    }

    [TestMethod]
    public async Task SameStateInDifferentInsertionOrder_SameRoot()
    {
        // Determinism: insertion order doesn't affect the root.
        using var s1 = new InMemoryKeyValueStore();
        using var s2 = new InMemoryKeyValueStore();
        var o1 = new MerkleStatePostStateRootOracle(s1);
        var o2 = new MerkleStatePostStateRootOracle(s2);

        s1.Put(new byte[] { 0x01 }, new byte[] { 0xAA });
        s1.Put(new byte[] { 0x02 }, new byte[] { 0xBB });
        s1.Put(new byte[] { 0x03 }, new byte[] { 0xCC });

        s2.Put(new byte[] { 0x03 }, new byte[] { 0xCC });
        s2.Put(new byte[] { 0x01 }, new byte[] { 0xAA });
        s2.Put(new byte[] { 0x02 }, new byte[] { 0xBB });

        var r1 = await o1.ResolveAsync(UInt256.Zero, UInt256.Zero, Ctx);
        var r2 = await o2.ResolveAsync(UInt256.Zero, UInt256.Zero, Ctx);
        Assert.AreEqual(r1, r2);
    }

    [TestMethod]
    public void Prove_ReturnsValidInclusionProof_VerifiableAgainstRoot()
    {
        using var store = new InMemoryKeyValueStore();
        store.Put(new byte[] { 0x01 }, new byte[] { 0xAA });
        store.Put(new byte[] { 0x02 }, new byte[] { 0xBB });
        store.Put(new byte[] { 0x03 }, new byte[] { 0xCC });
        store.Put(new byte[] { 0x04 }, new byte[] { 0xDD });

        var oracle = new MerkleStatePostStateRootOracle(store);
        var root = oracle.ResolveAsync(UInt256.Zero, UInt256.Zero, Ctx).Result;
        var siblings = oracle.Prove(new byte[] { 0x02 });

        Assert.IsNotNull(siblings);
        Assert.IsTrue(KeyedStateMerkleTree.Verify(
            root, new byte[] { 0x02 }, new byte[] { 0xBB }, leafIndex: 1, totalLeaves: 4, siblings!));
    }

    [TestMethod]
    public void Prove_MissingKey_ReturnsNull()
    {
        using var store = new InMemoryKeyValueStore();
        store.Put(new byte[] { 0x01 }, new byte[] { 0xAA });
        var oracle = new MerkleStatePostStateRootOracle(store);
        Assert.IsNull(oracle.Prove(new byte[] { 0xFF }));
    }
}
