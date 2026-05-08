using System;
using System.Collections.Generic;
using System.Linq;
using Neo;
using Neo.L2.State;

namespace Neo.L2.State.UnitTests;

[TestClass]
public class UT_KeyedStateMerkleTree
{
    private static (byte[] Key, byte[] Value)[] Pairs(int count)
    {
        var p = new (byte[], byte[])[count];
        for (var i = 0; i < count; i++)
        {
            p[i] = (new byte[] { (byte)(0x10 + i) }, new byte[] { (byte)(0xA0 + i) });
        }
        return p;
    }

    [TestMethod]
    public void EmptySet_RootIsZero()
    {
        Assert.AreEqual(UInt256.Zero, KeyedStateMerkleTree.ComputeRoot(Array.Empty<(byte[], byte[])>()));
    }

    [TestMethod]
    public void SingleLeaf_RootIsLeafHash()
    {
        var pairs = Pairs(1);
        var root = KeyedStateMerkleTree.ComputeRoot(pairs);
        var leaf = KeyedStateMerkleTree.HashLeaf(pairs[0].Key, pairs[0].Value);
        Assert.AreEqual(leaf, root);
    }

    [TestMethod]
    public void Determinism_SameInputDifferentOrder_SameRoot()
    {
        // Pin: insertion order doesn't affect the root — sorted-key canonicalization.
        var pairs = Pairs(8);
        var rev = pairs.Reverse().ToArray();
        Assert.AreEqual(KeyedStateMerkleTree.ComputeRoot(pairs), KeyedStateMerkleTree.ComputeRoot(rev));
    }

    [TestMethod]
    public void DifferentValues_DifferentRoot()
    {
        var pairs1 = Pairs(4);
        var pairs2 = Pairs(4);
        // Mutate one value
        pairs2[2] = (pairs2[2].Key, new byte[] { 0xFF });
        Assert.AreNotEqual(KeyedStateMerkleTree.ComputeRoot(pairs1), KeyedStateMerkleTree.ComputeRoot(pairs2));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(7)]
    [DataRow(8)]
    [DataRow(15)]
    [DataRow(16)]
    public void EveryLeaf_InclusionProofVerifies(int count)
    {
        var pairs = Pairs(count);
        var root = KeyedStateMerkleTree.ComputeRoot(pairs);
        var sorted = pairs.OrderBy(p => p.Key, BCmp.Instance).ToArray();
        for (var i = 0; i < count; i++)
        {
            var siblings = KeyedStateMerkleTree.Prove(pairs, i);
            Assert.IsTrue(
                KeyedStateMerkleTree.Verify(root, sorted[i].Key, sorted[i].Value, i, count, siblings),
                $"leaf {i} of {count} must verify");
        }
    }

    [TestMethod]
    public void Verify_RejectsAlteredValue()
    {
        var pairs = Pairs(4);
        var root = KeyedStateMerkleTree.ComputeRoot(pairs);
        var siblings = KeyedStateMerkleTree.Prove(pairs, 1);
        Assert.IsFalse(
            KeyedStateMerkleTree.Verify(root, pairs[1].Key, new byte[] { 0xFF }, 1, 4, siblings),
            "altered value must fail inclusion verification");
    }

    [TestMethod]
    public void Verify_RejectsWrongIndex()
    {
        var pairs = Pairs(4);
        var root = KeyedStateMerkleTree.ComputeRoot(pairs);
        var sorted = pairs.OrderBy(p => p.Key, BCmp.Instance).ToArray();
        var siblings = KeyedStateMerkleTree.Prove(pairs, 2);
        Assert.IsTrue(KeyedStateMerkleTree.Verify(root, sorted[2].Key, sorted[2].Value, 2, 4, siblings));
        // Same leaf claimed at index 0 must fail.
        Assert.IsFalse(KeyedStateMerkleTree.Verify(root, sorted[2].Key, sorted[2].Value, 0, 4, siblings));
    }

    [TestMethod]
    public void Prove_OutOfRange_Throws()
    {
        var pairs = Pairs(4);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => KeyedStateMerkleTree.Prove(pairs, -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => KeyedStateMerkleTree.Prove(pairs, 4));
    }

    [TestMethod]
    public void HashLeaf_Deterministic_LengthPrefixedAvoidsAmbiguity()
    {
        // Pin: HashLeaf("ab", "c") != HashLeaf("a", "bc"). Without length prefixing,
        // the two would hash the same bytes.
        var ab_c = KeyedStateMerkleTree.HashLeaf(new byte[] { 0x61, 0x62 }, new byte[] { 0x63 });
        var a_bc = KeyedStateMerkleTree.HashLeaf(new byte[] { 0x61 }, new byte[] { 0x62, 0x63 });
        Assert.AreNotEqual(ab_c, a_bc);
    }

    private sealed class BCmp : IComparer<byte[]>
    {
        public static readonly BCmp Instance = new();
        public int Compare(byte[]? x, byte[]? y)
        {
            if (x is null || y is null) return (x is null ? 0 : 1) - (y is null ? 0 : 1);
            var min = Math.Min(x.Length, y.Length);
            for (var i = 0; i < min; i++) { var d = x[i] - y[i]; if (d != 0) return d; }
            return x.Length - y.Length;
        }
    }
}
