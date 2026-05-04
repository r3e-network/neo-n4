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
            Leaf = null!, LeafIndex = 0,
            Siblings = new[] { UInt256.Zero }, PathBitmap = 0,
        };
        Assert.ThrowsExactly<ArgumentNullException>(() => MerkleProofSerializer.Encode(bad));
    }

    [TestMethod]
    public void Encode_RejectsNullSiblings()
    {
        // Pin MerkleProofSerializer.cs:37.
        var bad = new MerkleProof
        {
            Leaf = UInt256.Zero, LeafIndex = 0,
            Siblings = null!, PathBitmap = 0,
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
}
