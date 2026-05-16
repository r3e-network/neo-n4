using System.Linq;
using Neo.L2.Executor.State;

namespace Neo.L2.State.UnitTests;

/// <summary>
/// Cross-pin test: <see cref="KeyedStateMerkleTree.ComputeRoot"/> (the production
/// state oracle's tree composer) MUST agree with the canonical
/// <c>MerkleTree.ComputeRoot</c> of <c>KeyedStateStore.HashEntry</c> leaves —
/// the algorithm pinned by <c>UT_OnChainMerkleVerifyParity</c> against the
/// on-chain <c>SettlementManager.VerifyStateLeafWithProof</c> verifier.
/// </summary>
/// <remarks>
/// Without this pin, an off-chain divergence between the production oracle
/// (<c>MerkleStatePostStateRootOracle</c>) and the on-chain reconstructor would
/// produce committed state roots that fail escape-hatch verification for
/// odd-cardinality state trees > 2 leaves. The two conventions coincidentally
/// agree for power-of-2 leaf counts, so a regression that re-introduced
/// promote-unchanged here would still pass 2/4/8-leaf integration tests.
/// </remarks>
[TestClass]
public class UT_KeyedStateMerkleTree_NeoClassicParity
{
    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(7)]
    [DataRow(8)]
    [DataRow(9)]
    [DataRow(15)]
    [DataRow(16)]
    public void KeyedStateMerkle_AgreesWith_MerkleTree_OverHashEntry_Leaves(int count)
    {
        var pairs = new (byte[] Key, byte[] Value)[count];
        for (var i = 0; i < count; i++)
            pairs[i] = (new byte[] { (byte)(0x10 + i) }, new byte[] { (byte)(0xA0 + i) });

        var rootViaKeyedStateMerkle = KeyedStateMerkleTree.ComputeRoot(pairs);

        // Parity-test pattern: hash each pair via KeyedStateStore.HashEntry, then
        // feed the resulting UInt256 leaves through the canonical MerkleTree.
        var leaves = pairs
            .Select(p => KeyedStateStore.HashEntry(p.Key, p.Value))
            .ToArray();
        var rootViaMerkleTree = MerkleTree.ComputeRoot(leaves);

        Assert.AreEqual(rootViaMerkleTree, rootViaKeyedStateMerkle,
            $"{count}-leaf tree: KeyedStateMerkleTree must produce the same root " +
            "as MerkleTree.ComputeRoot over HashEntry leaves — this is what the " +
            "on-chain SettlementManager.VerifyStateLeafWithProof reconstructor expects");
    }

    [TestMethod]
    public void HashLeaf_AgreesWith_KeyedStateStore_HashEntry()
    {
        // Pin: both leaf-hash entry points must be byte-identical for any (key, value).
        // KeyedStateMerkleTree.HashLeaf and KeyedStateStore.HashEntry are documented as
        // canonical; a drift between them would silently break the production oracle's
        // pairing with the on-chain reconstructor even if the tree composition matches.
        var key = new byte[] { 0x01, 0x02, 0x03 };
        var value = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
        var hashLeaf = KeyedStateMerkleTree.HashLeaf(key, value);
        var hashEntry = KeyedStateStore.HashEntry(key, value);
        Assert.AreEqual(hashEntry, hashLeaf);
    }
}
