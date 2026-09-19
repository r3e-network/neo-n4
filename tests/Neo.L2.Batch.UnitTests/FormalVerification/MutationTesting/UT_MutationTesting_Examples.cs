using System;
using System.Buffers.Binary;
using System.Linq;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo;
using Neo.Cryptography;
using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.State;

namespace Neo.L2.Batch.FormalVerification.MutationTesting;

/// <summary>
/// Mutation-sensitivity demonstrations against real production code paths.
/// Each test reconstructs a representative mutant LOCALLY (the bug the mutant would
/// introduce) and then asserts the production property that kills it. These are
/// hand-written sensitivity checks, NOT an automated mutation run: no mutation-score
/// percentage is claimed anywhere in this file. Measured mutation results require an
/// actual mutation runner (e.g. Stryker.NET) executing against src/Neo.L2.Batch and
/// belong in a separate evidence artifact when introduced.
/// </summary>
[TestClass]
public class UT_MutationTesting_Examples
{
    #region Mutation A: Operator Flips (== → !=)

    /// <summary>
    /// A mutant that flips equality to inequality in commitment comparison is killed by
    /// the round-trip property: decoded content must equal the original. Exercise the
    /// real <see cref="L2BatchCommitment"/> equality through the production encoder.
    /// </summary>
    [TestMethod]
    public void MutationA_ComparisonFlip_KilledByRoundTripEquality()
    {
        var original = MkCommitment(seed: 1, proofSize: 0);

        var decoded = BatchSerializer.Decode(BatchSerializer.Encode(original));

        // The mutant (compare with !=) reports "equal" only for unequal inputs; the
        // round-trip equality assertion fails for it on every input.
        decoded.Equals(original).Should().BeTrue(
            "round-trip decode must be content-equal to the original commitment");
        decoded.GetHashCode().Should().Be(original.GetHashCode(),
            "equal commitments must hash equal (hash contract)");
    }

    #endregion

    #region Mutation C: Boundary Violations (>= → >)

    /// <summary>
    /// A mutant that tightens <c>value &gt;= 0</c> to <c>value &gt; 0</c> is killed by
    /// feeding the boundary value zero through the production decoder path: zero-length
    /// proofs are legal, and the serializer must round-trip them.
    /// </summary>
    [TestMethod]
    public void MutationC_OffByOne_KilledByZeroLengthProofRoundTrip()
    {
        var boundary = MkCommitment(seed: 2, proofSize: 0);

        var decoded = BatchSerializer.Decode(BatchSerializer.Encode(boundary));

        decoded.Proof.Length.Should().Be(0,
            "zero-length proof is the >= boundary; a >-mutant rejects or corrupts it");
        decoded.ProofType.Should().Be(boundary.ProofType);
    }

    #endregion

    #region Mutation D: Logic Negation

    /// <summary>
    /// A mutant that inverts the ProofType range check (accepting undefined discriminants)
    /// is killed by the decoder's rejection property: bytes outside [0..Zk] must throw,
    /// not silently round-trip.
    /// </summary>
    [TestMethod]
    public void MutationD_NegatedRangeCheck_KilledByUnknownProofTypeRejection()
    {
        var valid = MkCommitment(seed: 3, proofSize: 16);
        var encoded = BatchSerializer.Encode(valid);

        // Mutated decoder (check inverted) accepts unknown discriminants; the real one
        // throws InvalidDataException. Corrupt the ProofType byte past ProofType.Zk.
        var maxDefined = (byte)ProofType.Zk;
        Assert.ThrowsExactly<System.IO.InvalidDataException>(() =>
            BatchSerializer.Decode(CorruptProofTypeByte(encoded, (byte)(maxDefined + 1))),
            "ProofType byte past Zk must be rejected");
        BatchSerializer.Decode(CorruptProofTypeByte(encoded, maxDefined)).ProofType
            .Should().Be(ProofType.Zk, "the defined boundary byte must still decode");
    }

    private static byte[] CorruptProofTypeByte(byte[] encoded, byte proofType)
    {
        // Layout: 4 + 8 + 8 + 8 scalar fields, then 9×32 root fields → ProofType at 120.
        const int proofTypeOffset = 4 + 8 + 8 + 8 + 9 * 32;
        var corrupted = (byte[])encoded.Clone();
        corrupted[proofTypeOffset] = proofType;
        return corrupted;
    }

    #endregion

    #region Mutation E: Critical Path Corruption

    /// <summary>
    /// A mutant that changes public-input hashing (e.g. skips Hash256, or +/− corruption
    /// in the 352-byte layout) is killed by cross-checking the production hash against
    /// an independently computed double-SHA256 over the canonical encoding.
    /// </summary>
    [TestMethod]
    public void MutationE_HashOrLayoutCorruption_KilledByIndependentHashCrossCheck()
    {
        var inputs = MkPublicInputs(seed: 4);

        var produced = StateRootCalculator.HashPublicInputs(inputs);

        // Independent recomputation from the pinned 352-byte wire layout — shares no
        // code with HashPublicInputs, so a layout/hash mutant diverges from this value.
        var encoded = BatchSerializer.EncodePublicInputs(inputs);
        encoded.Length.Should().Be(352, "public-inputs wire domain is pinned at 352 bytes");
        var expected = new UInt256(Crypto.Hash256(encoded));

        produced.Should().Be(expected,
            "HashPublicInputs must equal double-SHA256 over the canonical 352-byte encoding");

        // The hash-bypass mutant (return raw bytes as the "hash") fails this: a raw
        // encoding differs from its digest with overwhelming probability.
        new UInt256(encoded.AsSpan(0, 32)).Should().NotBe(produced,
            "raw encoding bytes are not the digest — a hash-bypass mutant is detectable");
    }

    /// <summary>
    /// A mutant that corrupts arithmetic in field offsets (e.g. swapping ForcedInclusionCount
    /// with a root field) is killed by field-boundary assertions on the canonical encoding:
    /// the last 4 bytes are the forced-inclusion count, little-endian.
    /// </summary>
    [TestMethod]
    public void MutationE_OffsetArithmeticCorruption_KilledByFieldBoundaryAssertions()
    {
        var inputs = MkPublicInputs(seed: 5, forcedInclusionCount: 0x01020304);

        var encoded = BatchSerializer.EncodePublicInputs(inputs);

        // The tail-4-byte check pins the layout end; an off-by-4 offset mutant writes
        // the count somewhere else and fails here.
        BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(348, 4)).Should().Be(0x01020304,
            "ForcedInclusionCount must sit at offset 348..351 little-endian");
        // And a scalar-offset mutant that shifts ChainId/BatchNumber breaks the head too.
        BinaryPrimitives.ReadUInt32LittleEndian(encoded).Should().Be(inputs.ChainId);
        BinaryPrimitives.ReadUInt64LittleEndian(encoded.AsSpan(4, 8)).Should().Be(inputs.BatchNumber);
    }

    #endregion

    #region Fixtures

    private static L2BatchCommitment MkCommitment(int seed, int proofSize)
    {
        var proof = new byte[proofSize];
        new Random(seed).NextBytes(proof);
        return new L2BatchCommitment
        {
            ChainId = (uint)seed + 1,
            BatchNumber = 100UL + (uint)seed,
            FirstBlock = 1000UL + (uint)seed,
            LastBlock = 1099UL + (uint)seed,
            PreStateRoot = MkUInt256(seed, 0x10),
            PostStateRoot = MkUInt256(seed, 0x20),
            TxRoot = MkUInt256(seed, 0x30),
            ReceiptRoot = MkUInt256(seed, 0x40),
            WithdrawalRoot = MkUInt256(seed, 0x50),
            L2ToL1MessageRoot = MkUInt256(seed, 0x60),
            L2ToL2MessageRoot = MkUInt256(seed, 0x70),
            DACommitment = MkUInt256(seed, 0x80),
            PublicInputHash = MkUInt256(seed, 0x90),
            ProofType = ProofType.Optimistic,
            Proof = proof,
        };
    }

    private static PublicInputs MkPublicInputs(int seed, uint forcedInclusionCount = 7)
    {
        return new PublicInputs
        {
            ChainId = (uint)seed + 1,
            BatchNumber = 42UL + (uint)seed,
            FirstBlock = 1UL + (uint)seed,
            LastBlock = 50UL + (uint)seed,
            PreStateRoot = MkUInt256(seed, 0x11),
            PostStateRoot = MkUInt256(seed, 0x22),
            TxRoot = MkUInt256(seed, 0x33),
            ReceiptRoot = MkUInt256(seed, 0x44),
            WithdrawalRoot = MkUInt256(seed, 0x55),
            L2ToL1MessageRoot = MkUInt256(seed, 0x66),
            L2ToL2MessageRoot = MkUInt256(seed, 0x77),
            L1MessageHash = MkUInt256(seed, 0x88),
            DACommitment = MkUInt256(seed, 0x99),
            BlockContextHash = MkUInt256(seed, 0xAA),
            ForcedInclusionCount = forcedInclusionCount,
        };
    }

    private static UInt256 MkUInt256(int seed, byte tag)
    {
        var bytes = new byte[32];
        bytes[0] = tag;
        bytes[1] = (byte)seed;
        bytes[31] = (byte)(seed * 7 + tag);
        return new UInt256(bytes);
    }

    #endregion
}
