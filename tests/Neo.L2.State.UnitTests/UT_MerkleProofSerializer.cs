namespace Neo.L2.State.UnitTests;

/// <summary>
/// Tests for <see cref="MerkleProofSerializer"/> — canonical Merkle proof wire format
/// consumed by L1 NeoHub.SharedBridge for withdrawal verification.
/// </summary>
[TestClass]
public class UT_MerkleProofSerializer
{
    private static UInt256 H(int i)
    {
        Span<byte> b = stackalloc byte[32];
        BitConverter.TryWriteBytes(b, i);
        return new UInt256(b);
    }

    [TestMethod]
    public void Encode_Then_Decode_RoundTrips_FourLeaves()
    {
        var leaves = new[] { H(1), H(2), H(3), H(4) };
        var tree = new MerkleTree(leaves);
        var proof = tree.GetProof(2);

        var bytes = MerkleProofSerializer.Encode(proof);
        var decoded = MerkleProofSerializer.Decode(bytes);

        Assert.AreEqual(proof.Leaf, decoded.Leaf);
        Assert.AreEqual(proof.LeafIndex, decoded.LeafIndex);
        Assert.AreEqual(proof.PathBitmap, decoded.PathBitmap);
        CollectionAssert.AreEqual(proof.Siblings.ToArray(), decoded.Siblings.ToArray());
        Assert.IsTrue(MerkleTree.Verify(decoded, tree.Root), "decoded proof verifies against same root");
    }

    [TestMethod]
    public void Encode_SingleLeafTree_RoundTrips()
    {
        // Depth-0 tree: no siblings. Encoded form is just the header.
        var tree = new MerkleTree(new[] { H(42) });
        var proof = tree.GetProof(0);
        Assert.AreEqual(0, proof.Siblings.Count);

        var bytes = MerkleProofSerializer.Encode(proof);
        Assert.AreEqual(MerkleProofSerializer.HeaderSize, bytes.Length);

        var decoded = MerkleProofSerializer.Decode(bytes);
        Assert.AreEqual(0, decoded.Siblings.Count);
        Assert.IsTrue(MerkleTree.Verify(decoded, tree.Root));
    }

    [TestMethod]
    public void Encode_LayoutMatchesSpec()
    {
        var tree = new MerkleTree(new[] { H(1), H(2) });
        var proof = tree.GetProof(0);

        var bytes = MerkleProofSerializer.Encode(proof);

        // 32 (leaf) + 4 (idx) + 8 (bitmap) + 4 (count) + 32 (1 sibling) = 80
        Assert.AreEqual(80, bytes.Length);
        // Bytes 32-35 are the leaf index (uint32 LE) = 0
        Assert.AreEqual(0u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(32, 4)));
        // Bytes 44-47 are the sibling count (uint32 LE) = 1
        Assert.AreEqual(1u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(44, 4)));
    }

    [TestMethod]
    public void Decode_TruncatedHeader_Throws()
    {
        var truncated = new byte[MerkleProofSerializer.HeaderSize - 1];
        Assert.ThrowsExactly<ArgumentException>(() => MerkleProofSerializer.Decode(truncated));
    }

    [TestMethod]
    public void Decode_TruncatedSiblings_Throws()
    {
        var leaves = new[] { H(1), H(2), H(3), H(4) };
        var tree = new MerkleTree(leaves);
        var proof = tree.GetProof(0);
        var bytes = MerkleProofSerializer.Encode(proof);

        // Drop the last 16 bytes — siblings claimed but not all present.
        var truncated = bytes[..^16];
        Assert.ThrowsExactly<ArgumentException>(() => MerkleProofSerializer.Decode(truncated));
    }

    [TestMethod]
    public void Decode_ExtraTrailingBytes_Throws()
    {
        var leaves = new[] { H(1), H(2) };
        var tree = new MerkleTree(leaves);
        var proof = tree.GetProof(0);
        var bytes = MerkleProofSerializer.Encode(proof);
        var extended = new byte[bytes.Length + 8];
        Array.Copy(bytes, extended, bytes.Length);

        Assert.ThrowsExactly<ArgumentException>(() => MerkleProofSerializer.Decode(extended));
    }

    [TestMethod]
    public void Encode_OversizedDepth_Throws()
    {
        var oversized = new MerkleProof
        {
            Leaf = H(1),
            LeafIndex = 0,
            Siblings = Enumerable.Range(0, MerkleProofSerializer.MaxDepth + 1).Select(H).ToList(),
            PathBitmap = 0,
        };
        Assert.ThrowsExactly<ArgumentException>(() => MerkleProofSerializer.Encode(oversized));
    }

    [TestMethod]
    public void Decode_AdvertisedSiblingCount_AboveMaxDepth_Throws()
    {
        // Craft a header that claims SiblingCount > MaxDepth.
        var bytes = new byte[MerkleProofSerializer.HeaderSize + 32 * (MerkleProofSerializer.MaxDepth + 1)];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(44, 4), (uint)(MerkleProofSerializer.MaxDepth + 1));
        Assert.ThrowsExactly<ArgumentException>(() => MerkleProofSerializer.Decode(bytes));
    }

    [TestMethod]
    public void Encode_AcceptsExactlyMaxDepth_AndRoundTrips()
    {
        // Boundary case: exactly MaxDepth siblings must succeed. Pairs with the reject-at-
        // MaxDepth+1 test above so an off-by-one in the limit check is caught either way.
        var proof = new MerkleProof
        {
            Leaf = H(1),
            LeafIndex = 0,
            Siblings = Enumerable.Range(0, MerkleProofSerializer.MaxDepth).Select(H).ToList(),
            PathBitmap = 0,
        };
        var bytes = MerkleProofSerializer.Encode(proof);
        var decoded = MerkleProofSerializer.Decode(bytes);
        Assert.AreEqual(MerkleProofSerializer.MaxDepth, decoded.Siblings.Count);
        Assert.AreEqual(proof.Leaf, decoded.Leaf);
    }

    [TestMethod]
    public void Encode_RejectsNullProof()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => MerkleProofSerializer.Encode(null!));
    }

    [TestMethod]
    public void Encode_RejectsNullLeaf()
    {
        // Pin MerkleProofSerializer.cs:36. Without it Encode NREs on
        // proof.Leaf.GetSpan(). Same iter-154+ defense pattern.
        var bad = new MerkleProof
        {
            Leaf = null!,
            LeafIndex = 0,
            Siblings = new[] { UInt256.Zero },
            PathBitmap = 0,
        };
        Assert.ThrowsExactly<ArgumentNullException>(() => MerkleProofSerializer.Encode(bad));
    }

    [TestMethod]
    public void Encode_RejectsNullSiblings()
    {
        // Pin MerkleProofSerializer.cs:37.
        var bad = new MerkleProof
        {
            Leaf = UInt256.Zero,
            LeafIndex = 0,
            Siblings = null!,
            PathBitmap = 0,
        };
        Assert.ThrowsExactly<ArgumentNullException>(() => MerkleProofSerializer.Encode(bad));
    }

    [TestMethod]
    public void MerkleProof_VerifyInstanceMethod_DelegatesToStatic()
    {
        var leaves = new[] { H(1), H(2), H(3), H(4) };
        var tree = new MerkleTree(leaves);
        var proof = tree.GetProof(2);

        Assert.IsTrue(proof.Verify(tree.Root));
        Assert.IsFalse(proof.Verify(UInt256.Zero), "wrong root → false");
    }

    [TestMethod]
    public void Decode_RejectsLeafIndexExceedingIntMax()
    {
        // Regression for iter 203: encoder writes leafIndex via `(uint)proof.LeafIndex`
        // after a `LeafIndex < 0` check, so honest output is in [0, int.MaxValue]. A
        // malicious or corrupt input could carry leafIndex > int.MaxValue; the (int)cast
        // in Decode would silently wrap to negative. Now rejected at decode time.
        var leaves = new[] { H(1), H(2), H(3), H(4) };
        var tree = new MerkleTree(leaves);
        var proof = tree.GetProof(0);
        var bytes = MerkleProofSerializer.Encode(proof);
        // Overwrite the leafIndex bytes (offset 32, 4 bytes) with 0xFFFFFFFF (uint.MaxValue,
        // > int.MaxValue).
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(32, 4), uint.MaxValue);

        var ex = Assert.ThrowsExactly<ArgumentException>(() => MerkleProofSerializer.Decode(bytes));
        StringAssert.Contains(ex.Message, "LeafIndex");
    }

    [TestMethod]
    public void Roundtrip_For_AllPositionsIn_SevenLeafTree()
    {
        var leaves = Enumerable.Range(1, 7).Select(H).ToList();
        var tree = new MerkleTree(leaves);

        for (var i = 0; i < leaves.Count; i++)
        {
            var proof = tree.GetProof(i);
            var bytes = MerkleProofSerializer.Encode(proof);
            var decoded = MerkleProofSerializer.Decode(bytes);
            Assert.IsTrue(MerkleTree.Verify(decoded, tree.Root), $"proof at index {i} verifies after round-trip");
        }
    }

    // === PROPERTY-BASED FUZZ TESTS FOR MERKLE PROOFS ===

    [TestMethod]
    public void Encode_Decode_RandomProofs_ProducesValidWireFormat()
    {
        var rng = new Random(unchecked((int)3735928559));
        
        for (int i = 0; i < 100; i++)
        {
            var proof = GenerateRandomMerkleProof(rng);
            var encoded = MerkleProofSerializer.Encode(proof);
            
            var decoded = MerkleProofSerializer.Decode(encoded);
            
            Assert.AreEqual(proof.Leaf, decoded.Leaf, $"Leaf mismatch at iteration {i}");
            Assert.AreEqual(proof.LeafIndex, decoded.LeafIndex, $"LeafIndex mismatch at iteration {i}");
            Assert.AreEqual(proof.PathBitmap, decoded.PathBitmap, $"PathBitmap mismatch at iteration {i}");
            Assert.AreEqual(proof.Siblings.Count, decoded.Siblings.Count, 
                $"Sibling count mismatch at iteration {i}");
            
            for (int j = 0; j < proof.Siblings.Count; j++)
            {
                Assert.AreEqual(
                    proof.Siblings[j], decoded.Siblings[j],
                    $"Sibling[{j}] mismatch at iteration {i}");
            }
        }
    }

    [TestMethod]
    public void Encode_Decode_VaryingDepth_AllValid()
    {
        var rng = new Random(unchecked((int)3405837918));
        
        // Test various depths from 0 to MaxDepth
        for (int depth = 0; depth <= MerkleProofSerializer.MaxDepth; depth += Math.Max(1, depth / 2))
        {
            for (int i = 0; i < 50; i++)
            {
                var proof = new MerkleProof
                {
                    Leaf = RandomUInt256(rng),
                    LeafIndex = rng.Next(0, 1000),
                    Siblings = Enumerable.Range(0, depth).Select(_ => RandomUInt256(rng)).ToList(),
                    PathBitmap = (ulong)rng.Next(0, int.MaxValue)
                };

                try
                {
                    var encoded = MerkleProofSerializer.Encode(proof);
                    var decoded = MerkleProofSerializer.Decode(encoded);
                    
                    Assert.AreEqual(proof.Siblings.Count, decoded.Siblings.Count,
                        $"Failed for depth {depth} at iteration {i}");
                }
                catch (ArgumentException)
                {
                    // Expected when depth > MaxDepth
                    if (depth > MerkleProofSerializer.MaxDepth)
                        continue;
                    throw;
                }
            }
        }
    }

    [TestMethod]
    public void Encode_ZeroDepth_SingleLeafRoundTrips()
    {
        var proof = new MerkleProof
        {
            Leaf = RandomUInt256(new Random(0x12345678)),
            LeafIndex = 0,
            Siblings = new List<UInt256>(),
            PathBitmap = 0
        };

        var encoded = MerkleProofSerializer.Encode(proof);
        Assert.AreEqual(MerkleProofSerializer.HeaderSize, encoded.Length);
        
        var decoded = MerkleProofSerializer.Decode(encoded);
        Assert.AreEqual(0, decoded.Siblings.Count);
        Assert.AreEqual(proof.Leaf, decoded.Leaf);
    }

    [TestMethod]
    public void Encode_MaxDepth_WireFormatValid()
    {
        var rng = new Random(255412632);
        
        var proof = new MerkleProof
        {
            Leaf = RandomUInt256(rng),
            LeafIndex = 42,
            Siblings = Enumerable.Range(0, MerkleProofSerializer.MaxDepth).Select(_ => RandomUInt256(rng)).ToList(),
            PathBitmap = (ulong)rng.Next(0, int.MaxValue)
        };

        var encoded = MerkleProofSerializer.Encode(proof);
        Assert.IsTrue(encoded.Length >= MerkleProofSerializer.HeaderSize);
        
        var decoded = MerkleProofSerializer.Decode(encoded);
        Assert.AreEqual(proof.Siblings.Count, decoded.Siblings.Count);
    }

    [TestMethod]
    public void Fuzz_MerkleProof_200Iterations_ComprehensiveCheck()
    {
        var rng = new Random(unchecked((int)2864434317));
        
        for (int i = 0; i < 200; i++)
        {
            var leaf = RandomUInt256(rng);
            var leafIndex = rng.Next(0, int.MaxValue);
            var siblingCount = rng.Next(0, MerkleProofSerializer.MaxDepth + 2);
            
            var siblings = new List<UInt256>();
            for (int j = 0; j < siblingCount; j++)
            {
                siblings.Add(RandomUInt256(rng));
            }
            
            var proof = new MerkleProof
            {
                Leaf = leaf,
                LeafIndex = leafIndex,
                Siblings = siblings,
                PathBitmap = (ulong)rng.Next(0, int.MaxValue)
            };

            try
            {
                var encoded = MerkleProofSerializer.Encode(proof);
                var decoded = MerkleProofSerializer.Decode(encoded);
                
                Assert.AreEqual(leaf, decoded.Leaf, $"Iteration {i}: Leaf mismatch");
                Assert.AreEqual(leafIndex, decoded.LeafIndex, $"Iteration {i}: LeafIndex mismatch");
                Assert.AreEqual(siblingCount, decoded.Siblings.Count, $"Iteration {i}: Sibling count mismatch");
                
                for (int j = 0; j < siblings.Count; j++)
                {
                    Assert.AreEqual(siblings[j], decoded.Siblings[j], 
                        $"Iteration {i}: Sibling[{j}] mismatch");
                }
            }
            catch (ArgumentException)
            {
                // Expected for invalid inputs
            }
        }
    }

    private static UInt256 RandomUInt256(Random rng)
    {
        var bytes = new byte[32];
        rng.NextBytes(bytes);
        return new UInt256(bytes);
    }

    private static MerkleProof GenerateRandomMerkleProof(Random rng)
    {
        var depth = rng.Next(0, MerkleProofSerializer.MaxDepth + 2);
        
        return new MerkleProof
        {
            Leaf = RandomUInt256(rng),
            LeafIndex = rng.Next(0, int.MaxValue),
            Siblings = Enumerable.Range(0, depth).Select(_ => RandomUInt256(rng)).ToList(),
            PathBitmap = (ulong)rng.Next(0, int.MaxValue)
        };
    }
}
