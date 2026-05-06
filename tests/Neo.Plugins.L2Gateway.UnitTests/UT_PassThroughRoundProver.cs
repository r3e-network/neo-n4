using System;
using System.Buffers.Binary;
using Neo.Cryptography;

namespace Neo.Plugins.L2Gateway.UnitTests;

/// <summary>
/// Round-prover-level pinning of <see cref="PassThroughRoundProver"/>. The
/// <c>BinaryTreeAggregator</c> tests cover end-to-end aggregation; these target the
/// individual <c>Combine</c> contract so a future refactor of the round prover that
/// preserves the aggregator's behavior on small inputs but breaks the byte layout
/// (which the L1 / SP1-Compress consumer depends on) still fails the build.
/// </summary>
[TestClass]
public class UT_PassThroughRoundProver
{
    private static UInt256 H(char c) => UInt256.Parse("0x" + new string(c, 64));

    private static RoundResult R(UInt256 root, byte[] proof) =>
        new() { MessageRootContribution = root, ProofBytes = proof };

    [TestMethod]
    public void BackendId_IsConstantFE()
    {
        // Pinned: the backend id is part of every AggregatedCommitment and any L1 / SP1
        // consumer pattern-matches on it. A refactor that picks a different default id
        // silently re-routes downstream dispatch.
        Assert.AreEqual((byte)0xFE, PassThroughRoundProver.ConstBackendId);
        Assert.AreEqual(PassThroughRoundProver.ConstBackendId, new PassThroughRoundProver().BackendId);
    }

    [TestMethod]
    public void Combine_RightNull_ReturnsLeftUnchanged()
    {
        // Merkle odd-leaf rule pinned at the round-prover level — at the aggregator level
        // it's covered indirectly by OddCardinality_PromotesTrailingLeafUnchanged, but a
        // round prover that wraps the lone child instead of returning it unchanged would
        // break the equivalence with even-cardinality trees.
        var p = new PassThroughRoundProver();
        var left = R(H('a'), new byte[] { 0x01, 0x02, 0x03 });
        var combined = p.Combine(left, null);
        Assert.AreSame(left, combined);
    }

    [TestMethod]
    public void Combine_MessageRoot_IsHash256OfConcatenatedRoots()
    {
        // Same convention as Neo's MerkleTree (Hash256(left || right)) — switching to
        // SHA256-once or a different concat order would silently desync from
        // BinaryTreeAggregator's expectations.
        var p = new PassThroughRoundProver();
        var leftRoot = H('a');
        var rightRoot = H('b');
        var combined = p.Combine(R(leftRoot, [0x01]), R(rightRoot, [0x02]));

        Span<byte> buf = stackalloc byte[64];
        leftRoot.GetSpan().CopyTo(buf);
        rightRoot.GetSpan().CopyTo(buf[32..]);
        var expected = new UInt256(Crypto.Hash256(buf));
        Assert.AreEqual(expected, combined.MessageRootContribution);
    }

    [TestMethod]
    public void Combine_ProofBytes_LayoutIsLeftLenLeftRightLenRight()
    {
        // [4B leftLen LE][leftBytes][4B rightLen LE][rightBytes]. Any wire-format
        // consumer (L1 verifier, SP1 Compress wrapper) parses these prefixes; reordering
        // or switching to big-endian breaks them silently.
        var p = new PassThroughRoundProver();
        var leftProof = new byte[] { 0x10, 0x11, 0x12 };
        var rightProof = new byte[] { 0xA0, 0xA1 };
        var combined = p.Combine(R(H('a'), leftProof), R(H('b'), rightProof));

        var bytes = combined.ProofBytes.ToArray();
        Assert.AreEqual(4 + 3 + 4 + 2, bytes.Length);
        Assert.AreEqual(3, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0, 4)));
        CollectionAssert.AreEqual(leftProof, bytes[4..7]);
        Assert.AreEqual(2, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(7, 4)));
        CollectionAssert.AreEqual(rightProof, bytes[11..13]);
    }

    [TestMethod]
    public void Combine_BothEmptyProofBytes_ProducesEightZeroLengthPrefixBytes()
    {
        // Edge case: a tree of two empty-proof children should still produce a valid
        // length-prefixed envelope (two zero prefixes). Without this, downstream parsers
        // that read the length-prefix-then-body pattern would over-read past the buffer.
        var p = new PassThroughRoundProver();
        var combined = p.Combine(
            R(H('a'), Array.Empty<byte>()),
            R(H('b'), Array.Empty<byte>()));

        var bytes = combined.ProofBytes.ToArray();
        Assert.AreEqual(8, bytes.Length);
        Assert.AreEqual(0, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0, 4)));
        Assert.AreEqual(0, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4, 4)));
    }

    [TestMethod]
    public void Combine_IsAsymmetric_OrderMatters()
    {
        // Combine(A, B) ≠ Combine(B, A) for distinct A, B — pins that the round prover
        // is order-sensitive (so the aggregator's left/right indexing matters and an
        // accidental swap surfaces as a different root, not silent correctness).
        var p = new PassThroughRoundProver();
        var a = R(H('a'), [0x01]);
        var b = R(H('b'), [0x02]);
        var ab = p.Combine(a, b);
        var ba = p.Combine(b, a);
        Assert.AreNotEqual(ab.MessageRootContribution, ba.MessageRootContribution);
        CollectionAssert.AreNotEqual(ab.ProofBytes.ToArray(), ba.ProofBytes.ToArray());
    }

    [TestMethod]
    public void Combine_RejectsNullLeft()
    {
        // Defense-in-depth: a future caller that passes a null left (e.g. mis-promotion of
        // a None'd RoundResult) must surface as ArgumentNullException, not NRE deep
        // inside Hash256.
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new PassThroughRoundProver().Combine(null!, R(H('b'), [0x01])));
    }
}
