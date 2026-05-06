using System.Buffers.Binary;
using Neo.Cryptography;

namespace Neo.L2.State.UnitTests;

/// <summary>
/// Pins parity between the off-chain Merkle proof generator (used to build user
/// withdrawal / state-leaf proofs) and the on-chain verifier algorithm in
/// <c>NeoHub.SettlementManager.VerifyWithdrawalLeafWithProof</c> /
/// <c>VerifyStateLeafWithProof</c>. Replicates the on-chain folding logic in C# so
/// any future divergence (sibling-order swap, leafIndex bit interpretation, hash
/// composition) surfaces here rather than at the L1 contract boundary, where it would
/// silently reject every user's withdrawal until manually re-derived.
///
/// The on-chain algorithm:
/// <code>
/// for each sibling at level i:
///   if (leafIndex bit i == 0): combined = current || sibling     // current on left
///   else:                       combined = sibling || current    // current on right
///   current = Sha256(Sha256(combined))                            // Hash256
///   leafIndex >>= 1
/// return current  // == root if proof is valid
/// </code>
/// </summary>
[TestClass]
public class UT_OnChainMerkleVerifyParity
{
    /// <summary>Replica of <c>SettlementManager.VerifyWithdrawalLeafWithProof</c>'s root re-derivation.</summary>
    private static UInt256 OnChainReconstructRoot(UInt256 leafHash, IReadOnlyList<UInt256> siblings, ulong leafIndex)
    {
        var current = leafHash.GetSpan().ToArray();  // 32 bytes
        var index = leafIndex;
        for (var i = 0; i < siblings.Count; i++)
        {
            var sibling = siblings[i].GetSpan().ToArray();
            var combined = new byte[64];
            if ((index & 1UL) == 0UL)
            {
                Array.Copy(current, 0, combined, 0, 32);
                Array.Copy(sibling, 0, combined, 32, 32);
            }
            else
            {
                Array.Copy(sibling, 0, combined, 0, 32);
                Array.Copy(current, 0, combined, 32, 32);
            }
            current = Crypto.Hash256(combined);  // Sha256(Sha256(x))
            index >>= 1;
        }
        return new UInt256(current);
    }

    private static UInt256 H(byte b)
    {
        var bytes = new byte[32];
        bytes[0] = b;
        return new UInt256(bytes);
    }

    [TestMethod]
    public void OnChain_ReconstructsRoot_From_OffChain_Proof_FourLeafTree()
    {
        // 4-leaf tree: indexes 0..3, depth = 2. Pick a middle leaf (idx=1) so both
        // levels exercise the right-side path (leafIndex bit 0 = 1 → leaf right;
        // bit 1 = 0 → parent left).
        var leaves = new[] { H(0x01), H(0x02), H(0x03), H(0x04) };
        var tree = new MerkleTree(leaves);

        for (var i = 0; i < leaves.Length; i++)
        {
            var proof = tree.GetProof(i);
            var reDerived = OnChainReconstructRoot(proof.Leaf, proof.Siblings, (ulong)proof.LeafIndex);
            Assert.AreEqual(tree.Root, reDerived, $"on-chain re-derivation must match for leaf {i}");
        }
    }

    [TestMethod]
    public void OnChain_ReconstructsRoot_From_OffChain_Proof_OddCardinality()
    {
        // Odd-cardinality tree (5 leaves) exercises the duplication / promotion edge case:
        // last leaf at level 0 has a "self" sibling; tree depth = 3.
        var leaves = new[] { H(0xA0), H(0xB0), H(0xC0), H(0xD0), H(0xE0) };
        var tree = new MerkleTree(leaves);

        for (var i = 0; i < leaves.Length; i++)
        {
            var proof = tree.GetProof(i);
            var reDerived = OnChainReconstructRoot(proof.Leaf, proof.Siblings, (ulong)proof.LeafIndex);
            Assert.AreEqual(tree.Root, reDerived, $"on-chain re-derivation must match for leaf {i} in odd-card tree");
        }
    }

    [TestMethod]
    public void OnChain_ReconstructsRoot_From_OffChain_Proof_SevenLeafTree()
    {
        // 7-leaf tree: every level is odd → exercises odd-cardinality promotion at
        // every internal level. Same exhaustive pin as
        // UT_MerkleProofSerializer.Encode_AllLeafPositions_RoundTrip but at the
        // on-chain-replica layer.
        var leaves = new UInt256[7];
        for (var i = 0; i < 7; i++) leaves[i] = H((byte)(0x10 + i));
        var tree = new MerkleTree(leaves);

        for (var i = 0; i < leaves.Length; i++)
        {
            var proof = tree.GetProof(i);
            var reDerived = OnChainReconstructRoot(proof.Leaf, proof.Siblings, (ulong)proof.LeafIndex);
            Assert.AreEqual(tree.Root, reDerived, $"on-chain re-derivation must match for leaf {i} in 7-leaf tree");
        }
    }

    [TestMethod]
    public void OnChain_RejectsTamperedSibling()
    {
        // Sentinel: if the on-chain re-derivation produces a different root from a
        // tampered proof, the L1 verifier (which compares against storage) will reject
        // — same property in C# space.
        var leaves = new[] { H(0x01), H(0x02), H(0x03), H(0x04) };
        var tree = new MerkleTree(leaves);
        var proof = tree.GetProof(2);

        // Mutate one sibling.
        var tampered = proof.Siblings.ToList();
        tampered[0] = H(0xFF);

        var reDerived = OnChainReconstructRoot(proof.Leaf, tampered, (ulong)proof.LeafIndex);
        Assert.AreNotEqual(tree.Root, reDerived, "tampered sibling must not Merkle-verify");
    }

    [TestMethod]
    public void OnChain_StateTree_ReconstructsRoot_FromKeyedStateEntry()
    {
        // End-to-end pin for EmergencyManager.EscapeHatchExitWithProof: a user's
        // (key, value) state entry hashed via KeyedStateStore.HashEntry and proved
        // against the canonical state root re-derives correctly using the on-chain
        // algorithm.
        var entries = new (byte[] Key, byte[] Value)[]
        {
            (new byte[] { 0x01, 0xAA }, new byte[] { 100 }),
            (new byte[] { 0x02, 0xBB }, new byte[] { 200 }),
            (new byte[] { 0x03, 0xCC }, new byte[] { 0x33, 0x44 }),
            (new byte[] { 0x04, 0xDD }, new byte[] { 0x55 }),
        };
        var leaves = new UInt256[entries.Length];
        for (var i = 0; i < entries.Length; i++)
            leaves[i] = Neo.L2.Executor.State.KeyedStateStore.HashEntry(entries[i].Key, entries[i].Value);
        var tree = new MerkleTree(leaves);

        // Pick the middle entry — verifies the proof+algorithm work for non-trivial position.
        var targetIndex = 2;
        var proof = tree.GetProof(targetIndex);
        var reDerived = OnChainReconstructRoot(proof.Leaf, proof.Siblings, (ulong)proof.LeafIndex);
        Assert.AreEqual(tree.Root, reDerived, "state-tree proof must Merkle-verify on-chain");
    }
}
