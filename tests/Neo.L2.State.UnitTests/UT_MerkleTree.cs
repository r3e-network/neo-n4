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
}
