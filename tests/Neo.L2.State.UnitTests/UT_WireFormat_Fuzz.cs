using System;
using System.Numerics;
using Neo;
using Neo.L2;
using Neo.L2.Bridge;
using Neo.L2.State;

namespace Neo.L2.State.UnitTests;

/// <summary>
/// Fuzz tests for the wire-format decoders. ZKsync's Foundry suite stresses every
/// L1↔L2 message encoder/decoder against random byte sequences; we adopt the same
/// principle here for neo4's canonical encoders. The contract being tested:
/// <em>every byte sequence that reaches a decoder either round-trips to an
/// equivalent record or is rejected with a typed exception — no decoder may panic,
/// crash, or silently mis-parse.</em>
/// </summary>
/// <remarks>
/// Determinism: seeded <see cref="System.Random"/> so a regression is reproducible.
/// Iteration count per seed is scaled to keep the full fuzz suite under a few
/// hundred milliseconds wall-clock — adjust upward when surfacing a bug.
/// </remarks>
[TestClass]
public class UT_WireFormat_Fuzz
{
    private const int IterationsPerSeed = 500;

    /// <summary>
    /// Fuzz <see cref="MerkleProofSerializer.Decode"/> with random byte sequences of
    /// random length. Decoder must either return a parsed proof OR throw
    /// <c>ArgumentException</c> / <c>InvalidDataException</c>. Any other exception
    /// (NullRef, IndexOutOfRange, OverflowException) signals a missing bounds check.
    /// </summary>
    [TestMethod]
    [DataRow(1u)]
    [DataRow(2u)]
    [DataRow(0xC0FFEEu)]
    [DataRow(0xF00DBABEu)]
    public void MerkleProofSerializer_Decode_NeverCrashes(uint seed)
    {
        var rng = new Random((int)(seed ^ 0xBABE_DECDu));
        for (var i = 0; i < IterationsPerSeed; i++)
        {
            var len = rng.Next(0, 4096);
            var buf = new byte[len];
            rng.NextBytes(buf);
            try
            {
                _ = MerkleProofSerializer.Decode(buf);
            }
            catch (ArgumentException) { /* expected — malformed bytes */ }
            catch (System.IO.InvalidDataException) { /* expected — malformed bytes */ }
            catch (Exception ex) when (
                ex is not OutOfMemoryException &&
                ex is not StackOverflowException &&
                ex is not OperationCanceledException)
            {
                Assert.Fail($"seed 0x{seed:X8} iter {i}: MerkleProofSerializer.Decode threw " +
                    $"{ex.GetType().Name} on random {len}-byte input — expected ArgumentException or InvalidDataException only");
            }
        }
    }

    /// <summary>
    /// Fuzz <see cref="DepositPayload.Decode"/>. Same contract as the Merkle decoder —
    /// either parses, or throws a typed exception, never crashes the host.
    /// </summary>
    [TestMethod]
    [DataRow(0x42u)]
    [DataRow(0x1337u)]
    [DataRow(0xDEADu)]
    [DataRow(0xBEEFu)]
    public void DepositPayload_Decode_NeverCrashes(uint seed)
    {
        var rng = new Random((int)(seed ^ 0xD15C0_AAAu));
        for (var i = 0; i < IterationsPerSeed; i++)
        {
            var len = rng.Next(0, 512);
            var buf = new byte[len];
            rng.NextBytes(buf);
            try
            {
                _ = DepositPayload.Decode(buf);
            }
            catch (ArgumentException) { /* expected */ }
            catch (System.IO.InvalidDataException) { /* expected */ }
            catch (Exception ex) when (
                ex is not OutOfMemoryException &&
                ex is not StackOverflowException &&
                ex is not OperationCanceledException)
            {
                Assert.Fail($"seed 0x{seed:X8} iter {i}: DepositPayload.Decode threw " +
                    $"{ex.GetType().Name} on random {len}-byte input");
            }
        }
    }

    /// <summary>
    /// Differential: for every well-formed <see cref="MerkleProof"/> produced via
    /// the canonical generator, encode then decode must produce an equivalent
    /// proof. Drift between encoder and decoder would surface here, even with
    /// fuzzed input shapes.
    /// </summary>
    [TestMethod]
    [DataRow(0x111u)]
    [DataRow(0x222u)]
    [DataRow(0x333u)]
    [DataRow(0x444u)]
    [DataRow(0x555u)]
    public void MerkleProofSerializer_RoundTrip_IsIdentity_AcrossFuzzedTreeShapes(uint seed)
    {
        var rng = new Random((int)(seed ^ 0x7777_2222u));
        for (var i = 0; i < 50; i++)
        {
            var leafCount = rng.Next(1, 17);
            var leaves = new UInt256[leafCount];
            for (var j = 0; j < leafCount; j++)
            {
                var b = new byte[32];
                rng.NextBytes(b);
                leaves[j] = new UInt256(b);
            }
            var tree = new MerkleTree(leaves);
            var leafIndex = rng.Next(0, leafCount);
            var proof = tree.GetProof(leafIndex);

            var encoded = MerkleProofSerializer.Encode(proof);
            var decoded = MerkleProofSerializer.Decode(encoded);

            Assert.AreEqual(proof.Leaf, decoded.Leaf,
                $"seed 0x{seed:X8} iter {i}: leaf mismatch at idx {leafIndex} of {leafCount}-leaf tree");
            Assert.AreEqual(proof.LeafIndex, decoded.LeafIndex,
                $"seed 0x{seed:X8} iter {i}: leafIndex mismatch");
            Assert.AreEqual(proof.Siblings.Count, decoded.Siblings.Count,
                $"seed 0x{seed:X8} iter {i}: sibling count mismatch");
            for (var s = 0; s < proof.Siblings.Count; s++)
            {
                Assert.AreEqual(proof.Siblings[s], decoded.Siblings[s],
                    $"seed 0x{seed:X8} iter {i}: sibling[{s}] mismatch");
            }
        }
    }

    /// <summary>
    /// Differential round-trip for <see cref="DepositPayload"/>: well-formed payloads
    /// must encode then decode to byte-identical records across fuzzed (l1Asset,
    /// l2Recipient, amount) tuples.
    /// </summary>
    [TestMethod]
    [DataRow(0xAAAA1111u)]
    [DataRow(0xBBBB2222u)]
    [DataRow(0xCCCC3333u)]
    [DataRow(0xDDDD4444u)]
    public void DepositPayload_RoundTrip_IsIdentity_AcrossFuzzedFields(uint seed)
    {
        var rng = new Random((int)(seed ^ 0xCAFE_FACEu));
        for (var i = 0; i < 100; i++)
        {
            var l1 = new byte[20]; rng.NextBytes(l1);
            var l2 = new byte[20]; rng.NextBytes(l2);
            if (l1[0] == 0) l1[0] = 1; // avoid Zero
            if (l2[0] == 0) l2[0] = 1;

            // Amount in [1, 2^256-1] expressed as random-length byte vector (1..32 bytes).
            var amountLen = rng.Next(1, 33);
            var amountBytes = new byte[amountLen];
            rng.NextBytes(amountBytes);
            if (amountBytes[amountLen - 1] == 0) amountBytes[amountLen - 1] = 1; // make magnitude non-zero
            var amount = new BigInteger(amountBytes, isUnsigned: true, isBigEndian: false);
            if (amount == BigInteger.Zero) amount = 1;

            var original = new DepositPayload
            {
                L1Asset = new UInt160(l1),
                L2Recipient = new UInt160(l2),
                Amount = amount,
            };
            var encoded = original.Encode();
            var decoded = DepositPayload.Decode(encoded);

            Assert.AreEqual(original.L1Asset, decoded.L1Asset, $"seed 0x{seed:X8} iter {i}: L1Asset mismatch");
            Assert.AreEqual(original.L2Recipient, decoded.L2Recipient, $"seed 0x{seed:X8} iter {i}: L2Recipient mismatch");
            Assert.AreEqual(original.Amount, decoded.Amount, $"seed 0x{seed:X8} iter {i}: Amount mismatch (orig={original.Amount}, decoded={decoded.Amount})");

            // Re-encode the decoded record — must be byte-identical to the first encoding.
            var reEncoded = decoded.Encode();
            CollectionAssert.AreEqual(encoded, reEncoded,
                $"seed 0x{seed:X8} iter {i}: re-encode is not byte-identical");
        }
    }

    /// <summary>
    /// Truncation fuzz: take any valid encoding, lop random suffix bytes, decoder must
    /// reject. Mirrors ZKsync's "incomplete L1→L2 message rejected" Foundry test.
    /// </summary>
    [TestMethod]
    [DataRow(0xABCDEFu)]
    [DataRow(0xFEDCBAu)]
    public void DepositPayload_Decode_RejectsTruncations(uint seed)
    {
        var rng = new Random((int)(seed ^ 0x7777_8888u));
        for (var i = 0; i < 50; i++)
        {
            var amountBytes = new byte[rng.Next(1, 33)];
            rng.NextBytes(amountBytes);
            if (amountBytes[^1] == 0) amountBytes[^1] = 1;
            var encoded = new DepositPayload
            {
                L1Asset = new UInt160(NonZeroBytes(rng, 20)),
                L2Recipient = new UInt160(NonZeroBytes(rng, 20)),
                Amount = new BigInteger(amountBytes, isUnsigned: true, isBigEndian: false),
            }.Encode();

            // Lop a random byte count off the tail (1 byte to encoded.Length-1).
            var lopLen = rng.Next(1, encoded.Length);
            var truncated = new byte[encoded.Length - lopLen];
            Array.Copy(encoded, truncated, truncated.Length);

            var caught = false;
            try { _ = DepositPayload.Decode(truncated); }
            catch (ArgumentException) { caught = true; }
            catch (System.IO.InvalidDataException) { caught = true; }
            Assert.IsTrue(caught,
                $"seed 0x{seed:X8} iter {i}: truncated ({lopLen}B off) payload accepted — decoder missing a length check");
        }
    }

    private static byte[] NonZeroBytes(Random rng, int len)
    {
        var b = new byte[len];
        rng.NextBytes(b);
        if (b[0] == 0) b[0] = 1;
        return b;
    }
}
