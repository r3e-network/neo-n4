using System.Numerics;
using Neo.Cryptography.ECC;
using Neo.L2.Executor.State;
using Neo.L2.Persistence;
using Neo.L2.Sequencer;
using Neo.Plugins.L2;
using Neo.Plugins.L2Rpc;

namespace Neo.L2.IntegrationTests;

/// <summary>
/// End-to-end persistence integration test. Devnet's <c>--data-dir</c> flag wires four
/// independent <see cref="RocksDbKeyValueStore"/> instances under a shared root directory:
/// one each for <see cref="KeyedStateStore"/>, <see cref="InMemoryL2RpcStore"/>,
/// <see cref="InMemorySequencerCommitteeProvider"/>, and the DA writer. Per-component
/// reopen tests already pin each store individually; this test pins the *combined* story:
///   1. Four independently-RocksDB-backed components share one root directory
///   2. Each gets its own subdirectory and they don't trample each other
///   3. After dispose+reopen, all four rehydrate correctly with no cross-contamination
///
/// Without this test, a refactor that accidentally shared a directory (or omitted one
/// component from the persistence wiring) would slip through component-level tests but
/// break the operator-facing devnet flow on restart.
/// </summary>
[TestClass]
public class UT_E2E_Persistence_FullStack
{
    private static (ECPoint pub, byte[] priv) GenKey(byte seed)
    {
        var priv = new byte[32]; priv[0] = seed; priv[31] = 1;
        var pub = ECCurve.Secp256r1.G * priv;
        return (pub, priv);
    }

    [TestMethod]
    public async Task DataDirRoot_AllThreeComponents_RehydrateOnReopen()
    {
        var root = Path.Combine(Path.GetTempPath(), "neo-l2-fullstack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        const uint chainId = 1001;
        var assetL1 = new UInt160(new byte[] {
            0x11,0x11,0x11,0x11, 0x11,0x11,0x11,0x11, 0x11,0x11,
            0x11,0x11,0x11,0x11, 0x11,0x11,0x11,0x11, 0x11,0x11
        });
        var assetL2 = new UInt160(new byte[] {
            0x22,0x22,0x22,0x22, 0x22,0x22,0x22,0x22, 0x22,0x22,
            0x22,0x22,0x22,0x22, 0x22,0x22,0x22,0x22, 0x22,0x22
        });
        var alice = new UInt160(new byte[] {
            0x33,0x33,0x33,0x33, 0x33,0x33,0x33,0x33, 0x33,0x33,
            0x33,0x33,0x33,0x33, 0x33,0x33,0x33,0x33, 0x33,0x33
        });
        var aliceBalanceKey = new byte[1 + 20 + 20];
        aliceBalanceKey[0] = 0x01; // balance prefix in KeyedStateStore
        assetL2.GetSpan().CopyTo(aliceBalanceKey.AsSpan(1));
        alice.GetSpan().CopyTo(aliceBalanceKey.AsSpan(1 + 20));

        var seqKey = GenKey(0x42);
        var seqL1Addr = new UInt160(new byte[] {
            0x55,0x55,0x55,0x55, 0x55,0x55,0x55,0x55, 0x55,0x55,
            0x55,0x55,0x55,0x55, 0x55,0x55,0x55,0x55, 0x55,0x55
        });
        var withdrawalLeaf = new UInt256(new byte[] {
            0xAA,0xAA,0xAA,0xAA, 0xAA,0xAA,0xAA,0xAA, 0xAA,0xAA,0xAA,0xAA, 0xAA,0xAA,0xAA,0xAA,
            0xAA,0xAA,0xAA,0xAA, 0xAA,0xAA,0xAA,0xAA, 0xAA,0xAA,0xAA,0xAA, 0xAA,0xAA,0xAA,0xAA
        });
        var proofBytes = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var daPayload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE };
        DAReceipt daReceipt = default!;

        try
        {
            // First boot: write data through each component.
            using (var stateRocks = new RocksDbKeyValueStore(Path.Combine(root, "state")))
            using (var stateStore = new KeyedStateStore(stateRocks, ownsBacking: true))
            using (var rpcRocks = new RocksDbKeyValueStore(Path.Combine(root, "rpc-proofs")))
            using (var rpcStore = new InMemoryL2RpcStore(chainId, SecurityLevel.Optimistic, rpcRocks, ownsProofs: true))
            using (var seqRocks = new RocksDbKeyValueStore(Path.Combine(root, "sequencer")))
            using (var seqProvider = new InMemorySequencerCommitteeProvider(chainId, seqRocks, ownsStore: true))
            using (var daWriter = (PersistentDAWriter)L2DAPlugin.BuildDefaultWriter(
                DAMode.Local, Path.Combine(root, "da")))
            {
                // (1) State: alice has 100 wei in assetL2.
                stateStore.Put(aliceBalanceKey, new BigInteger(100).ToByteArray());

                // (2) RPC: a withdrawal proof recorded.
                rpcStore.RecordWithdrawalProof(withdrawalLeaf, proofBytes);
                rpcStore.RegisterAsset(assetL1, assetL2);

                // (3) Sequencer: register a committee member.
                seqProvider.Register(seqKey.pub, seqL1Addr);

                // (4) DA: publish a payload — receipt commitment is content-addressed.
                daReceipt = await daWriter.PublishAsync(new DAPublishRequest
                {
                    ChainId = chainId,
                    BatchNumber = 1,
                    Payload = daPayload,
                });
            }

            // Second boot: rehydrate against the same directories. Each component must
            // see exactly what it wrote, and nothing it didn't.
            using (var stateRocks = new RocksDbKeyValueStore(Path.Combine(root, "state")))
            using (var stateStore = new KeyedStateStore(stateRocks, ownsBacking: true))
            using (var rpcRocks = new RocksDbKeyValueStore(Path.Combine(root, "rpc-proofs")))
            using (var rpcStore = new InMemoryL2RpcStore(chainId, SecurityLevel.Optimistic, rpcRocks, ownsProofs: true))
            using (var seqRocks = new RocksDbKeyValueStore(Path.Combine(root, "sequencer")))
            using (var seqProvider = new InMemorySequencerCommitteeProvider(chainId, seqRocks, ownsStore: true))
            {
                // (1) State.
                Assert.IsTrue(stateStore.Contains(aliceBalanceKey),
                    "alice's balance key must rehydrate from RocksDB");
                var aliceBytes = stateStore.Get(aliceBalanceKey).ToArray();
                Assert.AreEqual(new BigInteger(100), new BigInteger(aliceBytes));
                Assert.AreEqual(1, stateStore.Count);

                // (2) RPC. Asset registry is in-memory (rebuildable from L1) so it does NOT
                // survive — but withdrawal proofs are RocksDB-backed.
                var proof = rpcStore.GetWithdrawalProof(withdrawalLeaf);
                Assert.IsNotNull(proof, "withdrawal proof must rehydrate from RocksDB");
                CollectionAssert.AreEqual(proofBytes, proof!.Value.ToArray());

                // (3) Sequencer.
                Assert.IsTrue(await seqProvider.IsRegisteredAsync(seqKey.pub),
                    "sequencer must rehydrate from RocksDB");
                var committee = await seqProvider.GetActiveCommitteeAsync();
                Assert.AreEqual(1, committee.Count);
                Assert.AreEqual(seqL1Addr, committee[0].L1Address);

                // (4) DA: same content-addressed commitment must still resolve to
                // IsAvailable=true after restart. PersistentDAWriter is a property of
                // the plugin layer; the integration test pins that the explicitly local
                // RocksDB durability path survives the dispose boundary without claiming
                // a public DA security label.
                using var daWriter2 = (PersistentDAWriter)L2DAPlugin.BuildDefaultWriter(
                    DAMode.Local, Path.Combine(root, "da"));
                var available = await daWriter2.IsAvailableAsync(daReceipt);
                Assert.IsTrue(available, "DA payload must rehydrate from RocksDB");
            }
        }
        finally
        {
            if (Directory.Exists(root)) try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
