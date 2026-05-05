using Neo.L2;
using Neo.L2.Persistence;

namespace Neo.Plugins.L2Rpc.UnitTests;

/// <summary>
/// Tests for the IL2KeyValueStore-backed proof storage in
/// <see cref="InMemoryL2RpcStore"/>. Production wires <see cref="RocksDbKeyValueStore"/>
/// here so finalized inclusion proofs survive node restarts and remain queryable
/// indefinitely via getl2withdrawalproof + getl2messageproof RPC.
/// </summary>
[TestClass]
public class UT_L2RpcStore_Persistence
{
    [TestMethod]
    public void Constructor_RejectsNullProofStore()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new InMemoryL2RpcStore(1001, SecurityLevel.Optimistic, null!));
    }

    [TestMethod]
    public void RocksDb_Backed_WithdrawalProofSurvivesReopen()
    {
        // The whole point: a withdrawal proof stored in node A must be queryable from
        // node B that opens the same RocksDB on disk. Without persistence, RPC clients
        // querying for the proof after a restart would get null and have to wait for
        // re-finalization — breaks the IL2RpcStore contract.
        var dir = Path.Combine(Path.GetTempPath(), "neo-l2-rpc-rocks-w-" + Guid.NewGuid().ToString("N"));
        var leaf = UInt256.Parse("0x" + new string('a', 64));
        var proof = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        try
        {
            using (var rocks = new RocksDbKeyValueStore(dir))
            using (var store = new InMemoryL2RpcStore(1001, SecurityLevel.Optimistic, rocks))
            {
                store.RecordWithdrawalProof(leaf, proof);
            }
            using (var rocks = new RocksDbKeyValueStore(dir))
            using (var store = new InMemoryL2RpcStore(1001, SecurityLevel.Optimistic, rocks))
            {
                var got = store.GetWithdrawalProof(leaf);
                Assert.IsNotNull(got);
                CollectionAssert.AreEqual(proof, got.Value.ToArray());
            }
        }
        finally
        {
            if (Directory.Exists(dir)) try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public void RocksDb_Backed_MessageProofSurvivesReopen()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-l2-rpc-rocks-m-" + Guid.NewGuid().ToString("N"));
        var msg = UInt256.Parse("0x" + new string('b', 64));
        var proof = new byte[] { 0x01, 0x02, 0x03 };
        try
        {
            using (var rocks = new RocksDbKeyValueStore(dir))
            using (var store = new InMemoryL2RpcStore(1001, SecurityLevel.Optimistic, rocks))
            {
                store.RecordMessageProof(msg, proof);
            }
            using (var rocks = new RocksDbKeyValueStore(dir))
            using (var store = new InMemoryL2RpcStore(1001, SecurityLevel.Optimistic, rocks))
            {
                var got = store.GetMessageProof(msg);
                Assert.IsNotNull(got);
                CollectionAssert.AreEqual(proof, got.Value.ToArray());
            }
        }
        finally
        {
            if (Directory.Exists(dir)) try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public void WithdrawalAndMessageProofs_DoNotCollide()
    {
        // Same UInt256 hash used as both a withdrawal leaf and a message hash must store
        // independently. The internal prefix bytes (0x01 vs 0x02) prevent collisions —
        // pin so a refactor that drops the prefix surfaces here.
        using var rocks = new InMemoryKeyValueStore();
        using var store = new InMemoryL2RpcStore(1001, SecurityLevel.Optimistic, rocks);

        var hash = UInt256.Parse("0x" + new string('5', 64));
        var withdrawalProof = new byte[] { 0x10 };
        var messageProof = new byte[] { 0x20 };

        store.RecordWithdrawalProof(hash, withdrawalProof);
        store.RecordMessageProof(hash, messageProof);

        var w = store.GetWithdrawalProof(hash);
        var m = store.GetMessageProof(hash);
        Assert.IsNotNull(w);
        Assert.IsNotNull(m);
        CollectionAssert.AreEqual(withdrawalProof, w.Value.ToArray());
        CollectionAssert.AreEqual(messageProof, m.Value.ToArray());
    }

    [TestMethod]
    public void Dispose_OwnsProofStore_FlowsThroughToBacking()
    {
        // Default ctor owns the proof store; disposing the L2RpcStore disposes it.
        var store = new InMemoryL2RpcStore(1001, SecurityLevel.Optimistic);
        store.RecordWithdrawalProof(
            UInt256.Parse("0x" + new string('1', 64)),
            new byte[] { 0x01 });
        store.Dispose();
        store.Dispose();  // double-dispose is safe
    }

    [TestMethod]
    public void Dispose_NotOwnsProofStore_LeavesBackingIntact()
    {
        using var rocks = new InMemoryKeyValueStore();
        var store = new InMemoryL2RpcStore(1001, SecurityLevel.Optimistic, rocks);  // ownsProofs=false
        store.Dispose();

        // rocks still works.
        rocks.Put(new byte[] { 0xFF }, new byte[] { 0x42 });
        Assert.AreEqual((byte)0x42, rocks.Get(new byte[] { 0xFF })![0]);
    }

    [TestMethod]
    public void DefaultCtor_StillWorks_BackwardCompat()
    {
        // Backward compat: the ctor signature without a KV store still works.
        var store = new InMemoryL2RpcStore(1001, SecurityLevel.Optimistic);
        var leaf = UInt256.Parse("0x" + new string('c', 64));
        store.RecordWithdrawalProof(leaf, new byte[] { 0x01 });
        Assert.IsNotNull(store.GetWithdrawalProof(leaf));
    }
}
