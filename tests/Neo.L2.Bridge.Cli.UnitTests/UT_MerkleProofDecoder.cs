using System;
using Neo;
using Neo.L2.Bridge.Cli.Commands;
using Neo.L2.State;

namespace Neo.L2.Bridge.Cli.UnitTests;

/// <summary>
/// End-to-end pin: a proof emitted by the canonical
/// <see cref="MerkleProofSerializer.Encode(MerkleProof)"/> round-trips
/// through <see cref="MerkleProofDecoder.Decode"/> back to the same
/// sibling list, in leaf-to-root order. Catches a wire-format drift between
/// the off-chain encoder and the bridge CLI's decoder.
/// </summary>
[TestClass]
public class UT_MerkleProofDecoder
{
    private static UInt256 H(byte seed)
    {
        var b = new byte[32];
        for (var i = 0; i < 32; i++) b[i] = (byte)(seed + i);
        return new UInt256(b);
    }

    [TestMethod]
    public void Decode_RoundTripsSerializerOutput_TwoSiblings()
    {
        var siblings = new[] { H(0x10), H(0x20) };
        var proof = new MerkleProof
        {
            Leaf = H(0x01),
            LeafIndex = 1,
            PathBitmap = 0,
            Siblings = siblings,
        };
        var bytes = MerkleProofSerializer.Encode(proof);
        var decoded = MerkleProofDecoder.Decode(bytes);

        Assert.AreEqual(siblings.Length, decoded.Count);
        for (var i = 0; i < siblings.Length; i++)
            Assert.AreEqual(siblings[i], decoded[i], $"sibling[{i}] must round-trip");
    }

    [TestMethod]
    public void Decode_HandlesEmptySiblings_ForSingleLeafTree()
    {
        // A single-leaf batch's withdrawal proof has zero siblings (the leaf IS the root).
        // The decoder must accept the canonical encoding (header only) and return empty.
        var proof = new MerkleProof
        {
            Leaf = H(0x42),
            LeafIndex = 0,
            PathBitmap = 0,
            Siblings = Array.Empty<UInt256>(),
        };
        var bytes = MerkleProofSerializer.Encode(proof);
        var decoded = MerkleProofDecoder.Decode(bytes);
        Assert.AreEqual(0, decoded.Count);
    }

    [TestMethod]
    public void Decode_RejectsTruncatedHeader()
    {
        var truncated = new byte[10]; // less than HeaderSize=48
        var ex = Assert.ThrowsExactly<InvalidDataException>(() => MerkleProofDecoder.Decode(truncated));
        StringAssert.Contains(ex.Message, "too short");
    }

    [TestMethod]
    public void Decode_RejectsSizeInconsistentWithSiblingCount()
    {
        // Encode a 2-sibling proof, then truncate the body — the size mismatch should
        // reject. Catches a network-corruption / partial-read condition without
        // silently consuming a partial sibling.
        var proof = new MerkleProof
        {
            Leaf = H(0x01),
            LeafIndex = 1,
            PathBitmap = 0,
            Siblings = new[] { H(0x10), H(0x20) },
        };
        var bytes = MerkleProofSerializer.Encode(proof);
        var truncated = new byte[bytes.Length - 4]; // chop a few bytes off the trailing sibling
        Array.Copy(bytes, truncated, truncated.Length);
        Assert.ThrowsExactly<InvalidDataException>(() => MerkleProofDecoder.Decode(truncated));
    }

    [TestMethod]
    public void Decode_PreservesLeafToRootOrder()
    {
        // Sibling order is part of the off-chain↔on-chain contract: SettlementManager's
        // VerifyWithdrawalLeafWithProof walks siblings[0] paired with the leaf, then
        // siblings[1] one level up, etc. A reversed decoder would silently yield
        // proof failures on multi-level trees. Pin the order via three distinct
        // siblings and the decoded indices.
        var s = new[] { H(0xAA), H(0xBB), H(0xCC) };
        var proof = new MerkleProof
        {
            Leaf = H(0x01),
            LeafIndex = 5,
            PathBitmap = 0,
            Siblings = s,
        };
        var bytes = MerkleProofSerializer.Encode(proof);
        var decoded = MerkleProofDecoder.Decode(bytes);
        Assert.AreEqual(s[0], decoded[0]);
        Assert.AreEqual(s[1], decoded[1]);
        Assert.AreEqual(s[2], decoded[2]);
    }
}
