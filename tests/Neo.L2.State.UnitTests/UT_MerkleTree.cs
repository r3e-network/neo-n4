namespace Neo.L2.State.UnitTests;

[TestClass]
public class UT_MerkleTree
{
    private static UInt256 H(int i)
    {
        Span<byte> b = stackalloc byte[32];
        BitConverter.TryWriteBytes(b, i);
        return new UInt256(b);
    }

    [TestMethod]
    public void Empty_RootIsZero()
    {
        var tree = new MerkleTree(Array.Empty<UInt256>());
        Assert.AreEqual(UInt256.Zero, tree.Root);
        Assert.AreEqual(0, tree.LeafCount);
    }

    [TestMethod]
    public void Single_RootIsLeaf()
    {
        var leaf = H(42);
        var tree = new MerkleTree(new[] { leaf });
        Assert.AreEqual(leaf, tree.Root);
        Assert.AreEqual(0, tree.Depth);
    }

    [TestMethod]
    public void RootMatches_NeoMerkleTree()
    {
        var leaves = Enumerable.Range(1, 7).Select(H).ToArray();
        var ours = MerkleTree.ComputeRoot(leaves);
        var theirs = Cryptography.MerkleTree.ComputeRoot(leaves);
        Assert.AreEqual(theirs, ours);
    }

    [TestMethod]
    public void RootMatches_NeoMerkleTree_PowerOfTwo()
    {
        var leaves = Enumerable.Range(1, 8).Select(H).ToArray();
        Assert.AreEqual(
            Cryptography.MerkleTree.ComputeRoot(leaves),
            MerkleTree.ComputeRoot(leaves));
    }

    [TestMethod]
    public void Proof_VerifiesEveryLeaf()
    {
        var leaves = Enumerable.Range(0, 11).Select(H).ToArray();
        var tree = new MerkleTree(leaves);
        for (var i = 0; i < leaves.Length; i++)
        {
            var proof = tree.GetProof(i);
            Assert.AreEqual(leaves[i], proof.Leaf);
            Assert.AreEqual(i, proof.LeafIndex);
            Assert.IsTrue(MerkleTree.Verify(proof, tree.Root), $"proof failed for index {i}");
        }
    }

    [TestMethod]
    public void Proof_FailsAgainstWrongRoot()
    {
        var leaves = Enumerable.Range(0, 5).Select(H).ToArray();
        var tree = new MerkleTree(leaves);
        var proof = tree.GetProof(2);
        Assert.IsFalse(MerkleTree.Verify(proof, H(999)));
    }

    [TestMethod]
    public void Proof_FailsAfterTampering()
    {
        var leaves = Enumerable.Range(0, 5).Select(H).ToArray();
        var tree = new MerkleTree(leaves);
        var proof = tree.GetProof(2);
        var tampered = proof with { Leaf = H(999) };
        Assert.IsFalse(MerkleTree.Verify(tampered, tree.Root));
    }

    [TestMethod]
    public void Verify_RejectsRelabelledLeafIndex()
    {
        // C2: LeafIndex and PathBitmap travel as independent serialized fields. Without binding
        // the bitmap to the index, a genuine proof for leaf i verifies equally at any relabelled
        // index — inclusion bound to the leaf hash, not to a unique position.
        var leaves = Enumerable.Range(0, 8).Select(H).ToArray();
        var tree = new MerkleTree(leaves);
        var proof = tree.GetProof(3);
        Assert.IsTrue(MerkleTree.Verify(proof, tree.Root));
        foreach (var j in new[] { 0, 1, 2, 4, 5, 6, 7 })
        {
            Assert.IsFalse(MerkleTree.Verify(proof with { LeafIndex = j }, tree.Root),
                $"leaf 3's proof must not verify at index {j}");
        }
    }

    [TestMethod]
    public void Verify_RejectsIndexBeyondTheTreeDepth()
    {
        // C2, terminator half: index i + 2^depth walks the same fold directions (only the low
        // `depth` bits are consumed) and implies the same bitmap — but it names no leaf of the
        // tree. Same acceptance set as the on-chain folds' `index != 0` terminator.
        var leaves = Enumerable.Range(0, 5).Select(H).ToArray();
        var tree = new MerkleTree(leaves); // depth 3
        var proof = tree.GetProof(2);
        Assert.IsTrue(MerkleTree.Verify(proof, tree.Root));
        Assert.IsFalse(MerkleTree.Verify(proof with { LeafIndex = 2 + 8 }, tree.Root));
        Assert.IsFalse(MerkleTree.Verify(proof with { LeafIndex = 2 + 16 }, tree.Root));

        // Depth-0 corner: a single-leaf tree's empty-sibling proof binds the index to zero.
        var single = new MerkleTree(new[] { H(1) });
        var singleProof = single.GetProof(0);
        Assert.IsTrue(MerkleTree.Verify(singleProof, single.Root));
        Assert.IsFalse(MerkleTree.Verify(singleProof with { LeafIndex = 1 }, single.Root));
    }

    [TestMethod]
    public void Verify_RejectsInconsistentBitmap()
    {
        // C2: a bitmap that disagrees with the index's own bits is self-contradictory even when
        // the fold would still land on the root — the two position statements must match each
        // other, not just the root.
        var leaves = Enumerable.Range(0, 8).Select(H).ToArray();
        var tree = new MerkleTree(leaves);
        var proof = tree.GetProof(5); // 101b — bitmap has bits 0 and 2 set
        Assert.IsTrue(MerkleTree.Verify(proof, tree.Root));
        foreach (var flip in new[] { 0, 1, 2 })
        {
            Assert.IsFalse(
                MerkleTree.Verify(proof with { PathBitmap = proof.PathBitmap ^ (1UL << flip) }, tree.Root),
                $"flipping bitmap bit {flip} must be rejected");
        }
    }

    [TestMethod]
    public void Verify_AcceptsEveryPositionOfAnOddTree()
    {
        // Roundtrip control for the binding: every legitimate proof of a 5-leaf tree (odd count,
        // self-paired trailing leaf) still verifies, so the accept set shrank only for relabelled
        // or self-contradictory proofs.
        var leaves = Enumerable.Range(0, 5).Select(H).ToArray();
        var tree = new MerkleTree(leaves);
        for (var i = 0; i < leaves.Length; i++)
            Assert.IsTrue(MerkleTree.Verify(tree.GetProof(i), tree.Root), $"index {i}");
    }

    [TestMethod]
    public void Verify_RejectsNullLeaf()
    {
        // Regression for iter 168: Leaf is UInt256 (reference type) — `required` only
        // forces it to be set, not non-null. Without the iter-168 null-guard, a null
        // Leaf would NRE inside CombineHash's GetSpan() with no link to the bad caller.
        var bad = new MerkleProof
        {
            Leaf = null!,
            LeafIndex = 0,
            Siblings = new[] { UInt256.Zero },
            PathBitmap = 0,
        };
        Assert.ThrowsExactly<ArgumentNullException>(() => MerkleTree.Verify(bad, UInt256.Zero));
    }

    [TestMethod]
    public void Constructor_RejectsNullLeafEntry()
    {
        // Regression for iter 179: null UInt256 leaf entry would NRE deep in the tree
        // construction loop's CombineHash. Same iter-158/168 pattern; surface bad index.
        var leaves = new UInt256?[] { UInt256.Zero, null, UInt256.Zero };
        var ex = Assert.ThrowsExactly<ArgumentException>(() => new MerkleTree(leaves!));
        StringAssert.Contains(ex.Message, "[1]");
    }

    [TestMethod]
    public void ComputeRoot_RejectsNullLeafEntry()
    {
        // Same iter-179 guard on the static-method path.
        var leaves = new UInt256?[] { null, UInt256.Zero };
        var ex = Assert.ThrowsExactly<ArgumentException>(() => MerkleTree.ComputeRoot(leaves!));
        StringAssert.Contains(ex.Message, "[0]");
    }

    [TestMethod]
    public void Verify_RejectsNullSiblingEntry()
    {
        // Regression for iter 168: Siblings[i] is a reference type; even with the
        // collection itself non-null, individual entries can still be null. The
        // exception names the bad index so it's actionable.
        var bad = new MerkleProof
        {
            Leaf = UInt256.Zero,
            LeafIndex = 0,
            Siblings = new UInt256?[] { UInt256.Zero, null, UInt256.Zero }!,
            PathBitmap = 0,
        };
        var ex = Assert.ThrowsExactly<ArgumentException>(() => MerkleTree.Verify(bad, UInt256.Zero));
        StringAssert.Contains(ex.Message, "[1]");
    }

    [TestMethod]
    public void ComputeRoot_RejectsNullLeavesArray()
    {
        // Pin MerkleTree.cs:32. Param-level guard distinct from the per-entry pin
        // (ComputeRoot_RejectsNullLeafEntry, iter 179). Without it ComputeRoot would
        // NRE on `leaves.Count` access.
        Assert.ThrowsExactly<ArgumentNullException>(
            () => MerkleTree.ComputeRoot((IReadOnlyList<UInt256>)null!));
    }

    [TestMethod]
    public void Verify_RejectsNullProof()
    {
        // Pin MerkleTree.cs:131. Companion to Verify_RejectsNullLeaf (per-member, iter 168).
        Assert.ThrowsExactly<ArgumentNullException>(
            () => MerkleTree.Verify(null!, UInt256.Zero));
    }

    [TestMethod]
    public void GetProof_RejectsOutOfRangeIndex()
    {
        // Pin MerkleTree.cs:98-99. Without it the access at `_levels[..][idx]` in the
        // loop would throw IndexOutOfRange with no link to the bad index input.
        var tree = new MerkleTree(new[] { UInt256.Zero, UInt256.Zero });
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => tree.GetProof(-1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => tree.GetProof(2));
    }

    [TestMethod]
    public void Verify_RejectsNullSiblings()
    {
        // Pin MerkleTree.cs:137. Companion to Verify_RejectsNullLeaf (Leaf member, iter 168)
        // and the per-entry pin already in this file. Without it Verify NREs on
        // proof.Siblings.Count.
        var bad = new MerkleProof
        {
            Leaf = UInt256.Zero,
            LeafIndex = 0,
            Siblings = null!,
            PathBitmap = 0,
        };
        Assert.ThrowsExactly<ArgumentNullException>(
            () => MerkleTree.Verify(bad, UInt256.Zero));
    }
}
