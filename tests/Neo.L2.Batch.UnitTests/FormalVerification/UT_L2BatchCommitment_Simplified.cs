using System;
using System.Buffers.Binary;
using System.Linq;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo;
using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.State;

namespace Neo.L2.Batch.FormalVerification;

/// <summary>
/// Deterministic sampled regression tests for the canonical
/// <see cref="L2BatchCommitment"/> byte format produced by the production
/// <see cref="BatchSerializer.Encode"/>/<see cref="BatchSerializer.Decode"/> pair.
/// This is NOT a formal proof: it is a fixed, reproducible test corpus
/// (seeded RNG cases plus hand-computed boundary and golden-byte cases)
/// that pins the wire layout defined in doc.md §7.2 / §8.1 and enforced by
/// <c>BatchSerializer.CommitmentFixedSize</c> (321-byte fixed portion).
/// </summary>
[TestClass]
public class UT_L2BatchCommitment_Simplified
{
    // Canonical field offsets of the commitment encoding, derived independently
    // from the spec layout: 4 (ChainId) + 3×8 (BatchNumber/FirstBlock/LastBlock)
    // + 9×32 (roots) + 1 (ProofType) + 4 (proof length prefix) = 321.
    private const int OffsetChainId = 0;
    private const int OffsetBatchNumber = 4;
    private const int OffsetFirstBlock = 12;
    private const int OffsetLastBlock = 20;
    private const int OffsetRoots = 28;
    private const int OffsetProofType = OffsetRoots + 9 * 32; // 316
    private const int OffsetProofLength = OffsetProofType + 1; // 317
    private const int OffsetProofBytes = OffsetProofLength + 4; // 321
    private const int CommitmentFixedSizeExpected = 321;

    private static readonly string[] RootFieldNames =
    {
        "PreStateRoot", "PostStateRoot", "TxRoot", "ReceiptRoot", "WithdrawalRoot",
        "L2ToL1MessageRoot", "L2ToL2MessageRoot", "DACommitment", "PublicInputHash",
    };

    /// <summary>Build a commitment with all 9 roots filled with distinct, varied bytes.</summary>
    private static L2BatchCommitment MakeCommitment(
        uint chainId, ulong batchNumber, ulong firstBlock, ulong lastBlock,
        ProofType proofType, byte[] proof, int rootSeed)
    {
        var roots = new UInt256[9];
        for (var i = 0; i < 9; i++)
        {
            var bytes = new byte[32];
            for (var j = 0; j < 32; j++)
                bytes[j] = (byte)(rootSeed + i * 32 + j * 7);
            roots[i] = new UInt256(bytes);
        }

        return new L2BatchCommitment
        {
            ChainId = chainId,
            BatchNumber = batchNumber,
            FirstBlock = firstBlock,
            LastBlock = lastBlock,
            PreStateRoot = roots[0],
            PostStateRoot = roots[1],
            TxRoot = roots[2],
            ReceiptRoot = roots[3],
            WithdrawalRoot = roots[4],
            L2ToL1MessageRoot = roots[5],
            L2ToL2MessageRoot = roots[6],
            DACommitment = roots[7],
            PublicInputHash = roots[8],
            ProofType = proofType,
            Proof = proof,
        };
    }

    private static byte[] MakeProof(int length, int seed)
    {
        var proof = new byte[length];
        for (var i = 0; i < length; i++)
            proof[i] = (byte)(seed + i * 13);
        return proof;
    }

    /// <summary>Assert every field of the decoded commitment against the original.</summary>
    private static void AssertAllFieldsPreserved(L2BatchCommitment original, L2BatchCommitment decoded, string because)
    {
        decoded.ChainId.Should().Be(original.ChainId, because);
        decoded.BatchNumber.Should().Be(original.BatchNumber, because);
        decoded.FirstBlock.Should().Be(original.FirstBlock, because);
        decoded.LastBlock.Should().Be(original.LastBlock, because);
        decoded.PreStateRoot.Should().Be(original.PreStateRoot, because);
        decoded.PostStateRoot.Should().Be(original.PostStateRoot, because);
        decoded.TxRoot.Should().Be(original.TxRoot, because);
        decoded.ReceiptRoot.Should().Be(original.ReceiptRoot, because);
        decoded.WithdrawalRoot.Should().Be(original.WithdrawalRoot, because);
        decoded.L2ToL1MessageRoot.Should().Be(original.L2ToL1MessageRoot, because);
        decoded.L2ToL2MessageRoot.Should().Be(original.L2ToL2MessageRoot, because);
        decoded.DACommitment.Should().Be(original.DACommitment, because);
        decoded.PublicInputHash.Should().Be(original.PublicInputHash, because);
        decoded.ProofType.Should().Be(original.ProofType, because);
        decoded.Proof.ToArray().Should().Equal(original.Proof.ToArray(), because);
        decoded.Should().Be(original, because);
    }

    #region Round-trip regression over the production Encode/Decode pair

    /// <summary>
    /// Deterministic sampled round-trip: 64 seeded cases with all 9 roots varied,
    /// non-empty proof bytes, and first &lt;= last. Asserts every field independently
    /// (including the proof bytes), not just record equality.
    /// </summary>
    [TestMethod]
    public void BatchSerializer_RoundTrip_PreservesAllFields_IncludingProofBytes()
    {
        const int caseCount = 64;
        for (var caseIndex = 0; caseIndex < caseCount; caseIndex++)
        {
            var rng = new Random(1_000 + caseIndex);
            var chainId = (uint)rng.Next(1, int.MaxValue);
            var batchNumber = (ulong)rng.Next(0, 1 << 30);
            var firstBlock = batchNumber * 100UL + (ulong)rng.Next(0, 100);
            var lastBlock = firstBlock + (ulong)rng.Next(0, 200);
            var proof = MakeProof(rng.Next(1, 512), caseIndex);
            var proofType = (ProofType)(caseIndex % 3); // Multisig / Optimistic / Zk
            var commitment = MakeCommitment(
                chainId, batchNumber, firstBlock, lastBlock, proofType, proof, rootSeed: caseIndex + 1);

            var encoded = BatchSerializer.Encode(commitment);
            encoded.Length.Should().Be(CommitmentFixedSizeExpected + proof.Length,
                $"encoded size = 321 fixed + {proof.Length} proof bytes at case #{caseIndex}");

            var decoded = BatchSerializer.Decode(encoded);
            AssertAllFieldsPreserved(commitment, decoded, $"full field preservation at case #{caseIndex}");
        }
    }

    /// <summary>
    /// Full bit-width and boundary sweep on the integer fields: 0, 1, the high-bit
    /// value 0x80000000 / 0x8000000000000000, and uint/ulong.MaxValue, each with a
    /// distinct proof payload and all 9 roots varied. first &lt;= last holds in every case.
    /// </summary>
    [TestMethod]
    public void BatchSerializer_RoundTrip_IntegerBoundaries_FullBitWidths()
    {
        var uintBoundaries = new[] { 0u, 1u, 0x8000_0000u, uint.MaxValue };
        var ulongBoundaries = new ulong[] { 0ul, 1ul, 0x8000_0000_0000_0000ul, ulong.MaxValue };

        var caseIndex = 0;
        foreach (var chainId in uintBoundaries)
        foreach (var batchNumber in ulongBoundaries)
        foreach (var firstBlock in ulongBoundaries)
        foreach (var lastBlock in new[] { firstBlock, ulong.MaxValue }) // first <= last always
        {
            // Skip duplicate (firstBlock, lastBlock) combos: lastBlock == ulong.MaxValue
            // already covers lastBlock == firstBlock when firstBlock is ulong.MaxValue.
            if (firstBlock == ulong.MaxValue && lastBlock == ulong.MaxValue && batchNumber != ulongBoundaries[0])
                continue;

            var proof = MakeProof(1 + (caseIndex % 17), caseIndex);
            var commitment = MakeCommitment(
                chainId, batchNumber, firstBlock, lastBlock, ProofType.Zk, proof, rootSeed: 100 + caseIndex);

            var decoded = BatchSerializer.Decode(BatchSerializer.Encode(commitment));
            AssertAllFieldsPreserved(commitment, decoded, $"boundary case #{caseIndex}");
            caseIndex++;
        }
        caseIndex.Should().BeGreaterThan(0, "the boundary sweep must execute at least one case");
    }

    #endregion

    #region Wire layout: independent byte-shift and golden-vector checks at production offsets

    /// <summary>
    /// Inspects the PRODUCTION encoded bytes at fixed offsets using independent
    /// byte shifts derived from hand-chosen values — not via BinaryPrimitives on a
    /// locally written buffer. Verifies little-endian placement of ChainId,
    /// BatchNumber, FirstBlock, LastBlock, the proof length prefix, and the exact
    /// 32-byte payload placement of all 9 roots.
    /// </summary>
    [TestMethod]
    public void BatchSerializer_EncodedBytes_FieldOffsets_AreLittleEndian_AtCanonicalPositions()
    {
        var roots = new UInt256[9];
        for (var i = 0; i < 9; i++)
        {
            var bytes = new byte[32];
            bytes[0] = (byte)(0xA0 + i);           // marker low byte per root slot
            bytes[31] = (byte)(0x50 + i);          // marker high byte per root slot
            bytes[15] = 0xEE;                      // mid-byte marker (high-word shift checks)
            roots[i] = new UInt256(bytes);
        }

        const uint chainId = 0xAABBCCDDu;
        const ulong batchNumber = 0x0102030405060708ul;
        const ulong firstBlock = 0x1122334455667788ul;
        const ulong lastBlock = 0x99AABBCCDDEEFF00ul;
        var proof = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x42 };
        var commitment = new L2BatchCommitment
        {
            ChainId = chainId,
            BatchNumber = batchNumber,
            FirstBlock = firstBlock,
            LastBlock = lastBlock,
            PreStateRoot = roots[0],
            PostStateRoot = roots[1],
            TxRoot = roots[2],
            ReceiptRoot = roots[3],
            WithdrawalRoot = roots[4],
            L2ToL1MessageRoot = roots[5],
            L2ToL2MessageRoot = roots[6],
            DACommitment = roots[7],
            PublicInputHash = roots[8],
            ProofType = ProofType.Optimistic,
            Proof = proof,
        };

        var encoded = BatchSerializer.Encode(commitment);

        // ChainId @ 0: LE => lowest byte (0xDD) first, highest (0xAA) at offset 3.
        encoded[OffsetChainId + 0].Should().Be(0xDD, "ChainId byte 0 is the least significant byte");
        encoded[OffsetChainId + 1].Should().Be(0xCC, "ChainId byte 1");
        encoded[OffsetChainId + 2].Should().Be(0xBB, "ChainId byte 2");
        encoded[OffsetChainId + 3].Should().Be(0xAA, "ChainId byte 3 is the most significant byte");

        // BatchNumber @ 4: LE byte shifts across the full 8 bytes.
        for (var i = 0; i < 8; i++)
            encoded[OffsetBatchNumber + i].Should().Be((byte)(batchNumber >> (8 * i)),
                $"BatchNumber byte {i} must equal value >> (8*{i}) (little-endian shift)");
        // Same independent shift rule for FirstBlock and LastBlock.
        for (var i = 0; i < 8; i++)
        {
            encoded[OffsetFirstBlock + i].Should().Be((byte)(firstBlock >> (8 * i)),
                $"FirstBlock byte {i} little-endian shift");
            encoded[OffsetLastBlock + i].Should().Be((byte)(lastBlock >> (8 * i)),
                $"LastBlock byte {i} little-endian shift");
        }

        // All 9 roots at 32-byte slots starting at offset 28: full payload equality
        // against the original byte arrays, slot-by-slot, field-by-field.
        var expectedRootBytes = new[]
        {
            commitment.PreStateRoot, commitment.PostStateRoot, commitment.TxRoot,
            commitment.ReceiptRoot, commitment.WithdrawalRoot, commitment.L2ToL1MessageRoot,
            commitment.L2ToL2MessageRoot, commitment.DACommitment, commitment.PublicInputHash,
        };
        for (var slot = 0; slot < 9; slot++)
        {
            var expected = expectedRootBytes[slot].GetSpan().ToArray();
            for (var j = 0; j < 32; j++)
                encoded[OffsetRoots + slot * 32 + j].Should().Be(expected[j],
                    $"{RootFieldNames[slot]} byte {j} at absolute offset {OffsetRoots + slot * 32 + j}");
        }

        // ProofType byte and proof length prefix (int32 LE) after the roots.
        encoded[OffsetProofType].Should().Be((byte)ProofType.Optimistic, "ProofType discriminator byte");
        for (var i = 0; i < 4; i++)
            encoded[OffsetProofLength + i].Should().Be((byte)((uint)proof.Length >> (8 * i)),
                $"proof length prefix byte {i} little-endian shift");

        // Proof bytes appended verbatim after the fixed portion.
        for (var i = 0; i < proof.Length; i++)
            encoded[OffsetProofBytes + i].Should().Be(proof[i], $"proof byte {i}");

        // No trailing bytes beyond proof.
        encoded.Length.Should().Be(OffsetProofBytes + proof.Length, "no trailing bytes after the proof");
    }

    /// <summary>
    /// Hand-computed golden vector: a commitment with a single-byte proof encoded to
    /// an exact 322-byte array written out byte-by-byte in the test (independent of the
    /// production writer). Production output must match it exactly.
    /// </summary>
    [TestMethod]
    public void BatchSerializer_Encode_MatchesHandComputedGoldenVector()
    {
        const uint chainId = 1u;
        const ulong batchNumber = 7ul;
        const ulong firstBlock = 100ul;
        const ulong lastBlock = 100ul; // first == last is valid
        var preStateRootBytes = new byte[32];
        preStateRootBytes[0] = 0x01;
        var postStateRootBytes = new byte[32];
        postStateRootBytes[31] = 0x02;
        var proof = new byte[] { 0xFF };

        var commitment = new L2BatchCommitment
        {
            ChainId = chainId,
            BatchNumber = batchNumber,
            FirstBlock = firstBlock,
            LastBlock = lastBlock,
            PreStateRoot = new UInt256(preStateRootBytes),
            PostStateRoot = new UInt256(postStateRootBytes),
            TxRoot = UInt256.Zero,
            ReceiptRoot = UInt256.Zero,
            WithdrawalRoot = UInt256.Zero,
            L2ToL1MessageRoot = UInt256.Zero,
            L2ToL2MessageRoot = UInt256.Zero,
            DACommitment = UInt256.Zero,
            PublicInputHash = UInt256.Zero,
            ProofType = ProofType.Multisig,
            Proof = proof,
        };

        // Golden bytes assembled independently: LE integers via explicit byte math,
        // roots as raw 32-byte arrays, then type/length/proof.
        var golden = new byte[322];
        golden[0] = 0x01; golden[1] = 0x00; golden[2] = 0x00; golden[3] = 0x00; // ChainId = 1
        golden[4] = 0x07;                                                       // BatchNumber = 7 (rest zero)
        golden[12] = 0x64;                                                      // FirstBlock = 100 (rest zero)
        golden[20] = 0x64;                                                      // LastBlock = 100 (rest zero)
        preStateRootBytes.CopyTo(golden, 28);                                   // PreStateRoot @ 28
        postStateRootBytes.CopyTo(golden, 60);                                  // PostStateRoot @ 60
        // TxRoot..PublicInputHash @ 92..315 stay all-zero (UInt256.Zero payload).
        golden[316] = 0x01;                                                     // ProofType.Multisig
        golden[317] = 0x01; golden[318] = 0x00; golden[319] = 0x00; golden[320] = 0x00; // proof length 1
        golden[321] = 0xFF;                                                     // proof byte

        var encoded = BatchSerializer.Encode(commitment);
        encoded.Length.Should().Be(golden.Length, "golden vector length must match production output");
        encoded.Should().Equal(golden, "production encoding must match the hand-computed golden vector");
    }

    /// <summary>
    /// The fixed-size constant and the empty-proof encoding must agree with the
    /// independently computed 321-byte layout constant.
    /// </summary>
    [TestMethod]
    public void BatchSerializer_CommitmentFixedSize_MatchesIndependentLayoutComputation()
    {
        var commitment = MakeCommitment(1, 1, 1, 1, ProofType.Multisig, Array.Empty<byte>(), rootSeed: 1);
        var encoded = BatchSerializer.Encode(commitment);

        const int independentLayout = 4 + 3 * 8 + 9 * 32 + 1 + 4;
        BatchSerializer.CommitmentFixedSize.Should().Be(independentLayout);
        BatchSerializer.CommitmentFixedSize.Should().Be(CommitmentFixedSizeExpected);
        encoded.Length.Should().Be(independentLayout, "empty proof yields exactly the fixed portion");
    }

    #endregion

    #region Decode validation surface (sampled, deterministic)

    /// <summary>
    /// Decode must reject an inverted block range: the canonical decoder enforces
    /// first &lt;= last (doc.md §7.2). Tampering only the LastBlock bytes keeps every
    /// other field intact, so this isolates the range validation.
    /// </summary>
    [TestMethod]
    public void BatchSerializer_Decode_RejectsLastBlockBelowFirstBlock()
    {
        var commitment = MakeCommitment(5, 10, 200, 300, ProofType.Multisig, new byte[] { 1, 2, 3 }, rootSeed: 9);
        var encoded = BatchSerializer.Encode(commitment);

        // Overwrite LastBlock (@20, 8 bytes LE) with 199 (< FirstBlock 200).
        var tampered = (byte[])encoded.Clone();
        for (var i = 0; i < 8; i++)
            tampered[OffsetLastBlock + i] = (byte)((199ul >> (8 * i)) & 0xFF);

        var act = () => BatchSerializer.Decode(tampered);
        act.Should().ThrowExactly<InvalidDataException>();
    }

    /// <summary>
    /// Decode must reject an unknown ProofType discriminator byte while accepting
    /// every defined value 0..Zk (deterministic sampled sweep over the enum range).
    /// </summary>
    [TestMethod]
    public void BatchSerializer_Decode_ProofTypeByteRange()
    {
        var maxProofType = (byte)ProofType.Zk;
        for (var b = 0; b <= maxProofType; b++)
        {
            var commitment = MakeCommitment(1, 1, 1, 2, (ProofType)b, new byte[] { 0xAA }, rootSeed: b);
            var decoded = BatchSerializer.Decode(BatchSerializer.Encode(commitment));
            decoded.ProofType.Should().Be((ProofType)b, $"defined ProofType byte {b} must decode");
        }

        var baseCommitment = MakeCommitment(1, 1, 1, 2, ProofType.Multisig, new byte[] { 0xAA }, rootSeed: 1);
        var tampered = BatchSerializer.Encode(baseCommitment);
        tampered[OffsetProofType] = (byte)(maxProofType + 1);
        var act = () => BatchSerializer.Decode(tampered);
        act.Should().ThrowExactly<InvalidDataException>($"ProofType byte {maxProofType + 1} is undefined");
    }

    /// <summary>
    /// Decode must reject truncated buffers and trailing bytes after the proof
    /// (strict length match), sampled deterministically.
    /// </summary>
    [TestMethod]
    public void BatchSerializer_Decode_RejectsTruncationAndTrailingBytes()
    {
        var commitment = MakeCommitment(2, 3, 4, 5, ProofType.Zk, new byte[] { 0x11, 0x22 }, rootSeed: 3);
        var encoded = BatchSerializer.Encode(commitment);

        var act = () => BatchSerializer.Decode(encoded.AsSpan(0, CommitmentFixedSizeExpected - 1).ToArray());
        act.Should().ThrowExactly<ArgumentException>("buffer smaller than the fixed portion is rejected");

        var trailing = new byte[encoded.Length + 1];
        encoded.CopyTo(trailing, 0);
        var actTrailing = () => BatchSerializer.Decode(trailing);
        actTrailing.Should().ThrowExactly<InvalidDataException>("trailing bytes after the proof are rejected");
    }

    #endregion
}
