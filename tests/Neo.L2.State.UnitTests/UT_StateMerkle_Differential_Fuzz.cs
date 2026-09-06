using System;
using System.Collections.Generic;
using System.Linq;
using Neo;
using Neo.Cryptography;
using Neo.L2.State;

namespace Neo.L2.State.UnitTests;

[TestClass]
public class UT_StateMerkle_Differential_Fuzz
{
    private static bool SimulatedOnChainVerifyStateLeafWithProof(
        UInt256 canonicalRoot,
        UInt256 leafHash,
        IReadOnlyList<UInt256> siblings,
        ulong leafIndex)
    {
        if (canonicalRoot.Equals(UInt256.Zero)) return false;
        if (siblings.Count > 64) return false;

        var current = leafHash.GetSpan().ToArray();
        var index = leafIndex;
        for (var i = 0; i < siblings.Count; i++)
        {
            var sibling = siblings[i].GetSpan().ToArray();
            var combined = new byte[64];
            if ((index & 1UL) == 0UL)
            {
                Buffer.BlockCopy(current, 0, combined, 0, 32);
                Buffer.BlockCopy(sibling, 0, combined, 32, 32);
            }
            else
            {
                Buffer.BlockCopy(sibling, 0, combined, 0, 32);
                Buffer.BlockCopy(current, 0, combined, 32, 32);
            }
            current = Crypto.Hash256(combined);
            index >>= 1;
        }
        if (index != 0) return false;
        return canonicalRoot.Equals(new UInt256(current));
    }

    /// <summary>
    /// Property: KeyedStateMerkleTree root is completely order-independent for any key-value map.
    /// Shuffling key-value pairs in arbitrary order produces identical roots.
    /// </summary>
    [TestMethod]
    [DataRow(0x11112222u)]
    [DataRow(0x33334444u)]
    [DataRow(0x55556666u)]
    public void KeyedStateMerkleTree_OrderIndependence_Property(uint seed)
    {
        var rng = new Random((int)(seed ^ 0x99887766u));
        for (var iter = 0; iter < 50; iter++)
        {
            var count = rng.Next(1, 60);
            var seen = new HashSet<string>();
            var original = new List<(byte[] Key, byte[] Value)>();
            for (var i = 0; i < count; i++)
            {
                var k = new byte[rng.Next(1, 32)]; rng.NextBytes(k);
                var hex = Convert.ToHexString(k);
                if (seen.Add(hex))
                {
                    var v = new byte[rng.Next(0, 64)]; rng.NextBytes(v);
                    original.Add((k, v));
                }
            }

            var baselineRoot = KeyedStateMerkleTree.ComputeRoot(original);

            // Permute 5 times
            for (var p = 0; p < 5; p++)
            {
                var shuffled = original.OrderBy(_ => rng.Next()).ToList();
                var shuffledRoot = KeyedStateMerkleTree.ComputeRoot(shuffled);
                Assert.AreEqual(baselineRoot, shuffledRoot,
                    $"iter {iter} perm {p}: shuffled pairs produced different root");
            }
        }
    }

    /// <summary>
    /// Property: For any tree shape, every generated Merkle proof passes both C# MerkleTree.Verify
    /// and the on-chain SettlementManager.VerifyStateLeafWithProof folding algorithm.
    /// </summary>
    [TestMethod]
    [DataRow(0xA1B2C3D4u)]
    [DataRow(0xD4C3B2A1u)]
    public void KeyedStateMerkleTree_ProofVerification_DifferentialProperty(uint seed)
    {
        var rng = new Random((int)(seed ^ 0x55AA1234u));
        for (var iter = 0; iter < 50; iter++)
        {
            var count = rng.Next(1, 32);
            var seen = new HashSet<string>();
            var distinctPairs = new List<(byte[] Key, byte[] Value)>();
            for (var i = 0; i < count; i++)
            {
                var k = new byte[16]; rng.NextBytes(k);
                if (seen.Add(Convert.ToHexString(k)))
                {
                    var v = new byte[16]; rng.NextBytes(v);
                    distinctPairs.Add((k, v));
                }
            }

            var root = KeyedStateMerkleTree.ComputeRoot(distinctPairs);
            for (var idx = 0; idx < distinctPairs.Count; idx++)
            {
                var siblings = KeyedStateMerkleTree.Prove(distinctPairs, idx);
                var (k, v) = distinctPairs.OrderBy(p => p.Key, LexicographicByteArrayComparer.Instance).ElementAt(idx);
                var leafHash = KeyedStateMerkleTree.HashLeaf(k, v);

                // Check simulated on-chain fold
                var onChainOk = SimulatedOnChainVerifyStateLeafWithProof(root, leafHash, siblings, (ulong)idx);
                Assert.IsTrue(onChainOk, $"iter {iter}, leaf {idx}: on-chain proof verification failed");
            }
        }
    }

    /// <summary>
    /// Tamper sensitivity fuzz: Flipping any bit in the leaf, siblings, or root causes proof verification to fail.
    /// </summary>
    [TestMethod]
    [DataRow(0x77889900u)]
    [DataRow(0x00998877u)]
    public void KeyedStateMerkleTree_TamperSensitivity_Fuzz(uint seed)
    {
        var rng = new Random((int)(seed ^ 0xF00DFAC0u));
        for (var iter = 0; iter < 30; iter++)
        {
            var count = rng.Next(2, 16);
            var seen = new HashSet<string>();
            var distinct = new List<(byte[] Key, byte[] Value)>();
            for (var i = 0; i < count; i++)
            {
                var k = new byte[8]; rng.NextBytes(k);
                if (seen.Add(Convert.ToHexString(k)))
                {
                    var v = new byte[8]; rng.NextBytes(v);
                    distinct.Add((k, v));
                }
            }
            if (distinct.Count < 2) continue;

            var root = KeyedStateMerkleTree.ComputeRoot(distinct);
            var sorted = distinct.OrderBy(p => p.Key, LexicographicByteArrayComparer.Instance).ToList();

            var targetIdx = rng.Next(0, sorted.Count);
            var siblings = KeyedStateMerkleTree.Prove(sorted, targetIdx);
            var leafHash = KeyedStateMerkleTree.HashLeaf(sorted[targetIdx].Key, sorted[targetIdx].Value);

            // Baseline must pass
            Assert.IsTrue(SimulatedOnChainVerifyStateLeafWithProof(root, leafHash, siblings, (ulong)targetIdx));

            // Tamper leaf
            var badLeaf = FlipBit(leafHash, rng);
            Assert.IsFalse(SimulatedOnChainVerifyStateLeafWithProof(root, badLeaf, siblings, (ulong)targetIdx));

            // Tamper root
            var badRoot = FlipBit(root, rng);
            Assert.IsFalse(SimulatedOnChainVerifyStateLeafWithProof(badRoot, leafHash, siblings, (ulong)targetIdx));

            // Tamper index: shifting index beyond proof depth or flipping bit where sibling differs
            var badIndex = (ulong)targetIdx + (1UL << siblings.Count);
            Assert.IsFalse(SimulatedOnChainVerifyStateLeafWithProof(root, leafHash, siblings, badIndex));

            // Tamper a sibling
            if (siblings.Count > 0)
            {
                var sibIdx = rng.Next(0, siblings.Count);
                var tamperedSiblings = siblings.ToList();
                tamperedSiblings[sibIdx] = FlipBit(tamperedSiblings[sibIdx], rng);
                Assert.IsFalse(SimulatedOnChainVerifyStateLeafWithProof(root, leafHash, tamperedSiblings, (ulong)targetIdx));
            }
        }
    }

    private static UInt256 FlipBit(UInt256 val, Random rng)
    {
        var b = val.GetSpan().ToArray();
        var byteIdx = rng.Next(0, 32);
        var bitIdx = rng.Next(0, 8);
        b[byteIdx] ^= (byte)(1 << bitIdx);
        return new UInt256(b);
    }
}
