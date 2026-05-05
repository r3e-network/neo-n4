using Neo.Cryptography.ECC;
using Neo.L2.Persistence;

namespace Neo.L2.Sequencer.UnitTests;

/// <summary>
/// Tests for the IL2KeyValueStore-backed committee membership in
/// <see cref="InMemorySequencerCommitteeProvider"/>. Production wires
/// <see cref="RocksDbKeyValueStore"/> here so registered sequencers + their exit
/// windows survive node restarts. Without persistence, a node bounce mid-exit
/// could lose the ExitsAtUnixSeconds deadline.
/// </summary>
[TestClass]
public class UT_Sequencer_Persistence
{
    private static (ECPoint pub, byte[] priv) GenKey(byte seed)
    {
        var priv = new byte[32]; priv[0] = seed; priv[31] = 1;
        var pub = ECCurve.Secp256r1.G * priv;
        return (pub, priv);
    }

    private static UInt160 Addr(byte seed)
    {
        var bytes = new byte[20]; bytes[0] = seed;
        return new UInt160(bytes);
    }

    [TestMethod]
    public void Constructor_RejectsNullStore()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new InMemorySequencerCommitteeProvider(1001, (IL2KeyValueStore)null!));
    }

    [TestMethod]
    public async Task RocksDb_Backed_RegisteredMembersSurviveReopen()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-l2-seq-rocks-" + Guid.NewGuid().ToString("N"));
        var k1 = GenKey(1);
        var k2 = GenKey(2);
        try
        {
            using (var rocks = new RocksDbKeyValueStore(dir))
            using (var p = new InMemorySequencerCommitteeProvider(1001, rocks))
            {
                p.Register(k1.pub, Addr(0xA1));
                p.Register(k2.pub, Addr(0xA2));
            }

            using (var rocks = new RocksDbKeyValueStore(dir))
            using (var p = new InMemorySequencerCommitteeProvider(1001, rocks))
            {
                Assert.IsTrue(await p.IsRegisteredAsync(k1.pub));
                Assert.IsTrue(await p.IsRegisteredAsync(k2.pub));
                var committee = await p.GetActiveCommitteeAsync();
                Assert.AreEqual(2, committee.Count);
            }
        }
        finally
        {
            if (Directory.Exists(dir)) try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public async Task RocksDb_Backed_ExitWindowSurvivesReopen()
    {
        // The most safety-critical case: a sequencer in the middle of their exit
        // window. If the deadline (ExitsAtUnixSeconds) is lost on restart, Finalize
        // semantics break.
        var dir = Path.Combine(Path.GetTempPath(), "neo-l2-seq-exit-" + Guid.NewGuid().ToString("N"));
        var k = GenKey(3);
        try
        {
            using (var rocks = new RocksDbKeyValueStore(dir))
            using (var p = new InMemorySequencerCommitteeProvider(1001, rocks))
            {
                p.Register(k.pub, Addr(0xA1));
                p.BeginExit(k.pub, exitsAtUnixSeconds: 1_700_000_000);
            }

            using (var rocks = new RocksDbKeyValueStore(dir))
            using (var p = new InMemorySequencerCommitteeProvider(1001, rocks))
            {
                // Exit window must be remembered: Finalize before window expires throws.
                Assert.ThrowsExactly<InvalidOperationException>(
                    () => p.Finalize(k.pub, nowUnixSeconds: 1_699_999_999));
                // After window expires, finalizes successfully — proves the
                // ExitsAtUnixSeconds value carried across the restart.
                p.Finalize(k.pub, nowUnixSeconds: 1_700_000_000);
                Assert.IsFalse(await p.IsRegisteredAsync(k.pub));
            }
        }
        finally
        {
            if (Directory.Exists(dir)) try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public async Task RocksDb_Backed_FinalizeRemovesAcrossReopen()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-l2-seq-fin-" + Guid.NewGuid().ToString("N"));
        var k = GenKey(4);
        try
        {
            using (var rocks = new RocksDbKeyValueStore(dir))
            using (var p = new InMemorySequencerCommitteeProvider(1001, rocks))
            {
                p.Register(k.pub, Addr(0xA1));
                p.BeginExit(k.pub, exitsAtUnixSeconds: 100);
                p.Finalize(k.pub, nowUnixSeconds: 200);
            }

            using (var rocks = new RocksDbKeyValueStore(dir))
            using (var p = new InMemorySequencerCommitteeProvider(1001, rocks))
            {
                Assert.IsFalse(await p.IsRegisteredAsync(k.pub),
                    "finalized member must NOT come back on reopen");
                var c = await p.GetActiveCommitteeAsync();
                Assert.AreEqual(0, c.Count);
            }
        }
        finally
        {
            if (Directory.Exists(dir)) try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public async Task DefaultCtor_StillWorks_BackwardCompat()
    {
        var p = new InMemorySequencerCommitteeProvider(1001);
        var k = GenKey(5);
        p.Register(k.pub, Addr(0xA1));
        Assert.IsTrue(await p.IsRegisteredAsync(k.pub));
    }

    [TestMethod]
    public void Dispose_OwnsStore_DoesNotThrowOnDoubleDispose()
    {
        var p = new InMemorySequencerCommitteeProvider(1001);
        p.Dispose();
        p.Dispose();
    }
}
