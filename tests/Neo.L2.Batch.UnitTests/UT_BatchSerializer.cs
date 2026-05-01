namespace Neo.L2.Batch.UnitTests;

[TestClass]
public class UT_BatchSerializer
{
    private static UInt256 H(char c) => UInt256.Parse("0x" + new string(c, 64));

    private static L2BatchCommitment Sample(byte[]? proof = null) => new()
    {
        ChainId = 0xCAFEBABE,
        BatchNumber = 0xDEAD_BEEF_F00D_BABEUL,
        FirstBlock = 100,
        LastBlock = 200,
        PreStateRoot = H('1'),
        PostStateRoot = H('2'),
        TxRoot = H('3'),
        ReceiptRoot = H('4'),
        WithdrawalRoot = H('5'),
        L2ToL1MessageRoot = H('6'),
        L2ToL2MessageRoot = H('7'),
        DACommitment = H('8'),
        PublicInputHash = H('9'),
        ProofType = ProofType.Zk,
        Proof = proof ?? new byte[] { 0xAA, 0xBB, 0xCC },
    };

    [TestMethod]
    public void Commitment_RoundTrips()
    {
        var original = Sample();
        var bytes = BatchSerializer.Encode(original);
        var decoded = BatchSerializer.Decode(bytes);
        Assert.AreEqual(original, decoded);
    }

    [TestMethod]
    public void Commitment_RoundTrips_EmptyProof()
    {
        var original = Sample(Array.Empty<byte>());
        var bytes = BatchSerializer.Encode(original);
        var decoded = BatchSerializer.Decode(bytes);
        Assert.AreEqual(original, decoded);
    }

    [TestMethod]
    public void Commitment_DeterministicOutput()
    {
        var a = BatchSerializer.Encode(Sample());
        var b = BatchSerializer.Encode(Sample());
        CollectionAssert.AreEqual(a, b);
    }

    [TestMethod]
    public void Commitment_FixedSizeMatchesConstant()
    {
        var bytes = BatchSerializer.Encode(Sample(Array.Empty<byte>()));
        Assert.AreEqual(BatchSerializer.CommitmentFixedSize, bytes.Length);
    }

    [TestMethod]
    public void Commitment_RejectsOversizedProof()
    {
        var huge = new byte[2 * 1024 * 1024];
        var c = Sample(huge);
        Assert.ThrowsExactly<ArgumentException>(() => BatchSerializer.Encode(c));
    }

    [TestMethod]
    public void Commitment_DetectsTruncation()
    {
        var bytes = BatchSerializer.Encode(Sample());
        Assert.ThrowsExactly<InvalidDataException>(() => BatchSerializer.Decode(bytes.AsSpan(0, bytes.Length - 1)));
    }

    [TestMethod]
    public void PublicInputs_RoundTrips()
    {
        var original = new PublicInputs
        {
            ChainId = 1001,
            BatchNumber = 42,
            PreStateRoot = H('a'),
            PostStateRoot = H('b'),
            TxRoot = H('c'),
            ReceiptRoot = H('d'),
            WithdrawalRoot = H('e'),
            L2ToL1MessageRoot = H('f'),
            L2ToL2MessageRoot = H('1'),
            L1MessageHash = H('2'),
            DACommitment = H('3'),
            BlockContextHash = H('4'),
        };
        var bytes = BatchSerializer.EncodePublicInputs(original);
        Assert.AreEqual(BatchSerializer.PublicInputsSize, bytes.Length);

        var decoded = BatchSerializer.DecodePublicInputs(bytes);
        Assert.AreEqual(original, decoded);
    }

    [TestMethod]
    public void PublicInputs_RejectsWrongSize()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            BatchSerializer.DecodePublicInputs(new byte[BatchSerializer.PublicInputsSize - 1]));
    }

    [TestMethod]
    public void Commitment_ByteLayout_MatchesDocumentedOffsets()
    {
        // Pins the layout claimed in BatchSerializer's XML docs. NeoHub.SettlementManager
        // depends on these offsets; a future encoder reorder must fail this test.
        var c = Sample(new byte[] { 0xAA, 0xBB, 0xCC });
        var bytes = BatchSerializer.Encode(c);

        Assert.AreEqual(BatchSerializer.CommitmentFixedSize + c.Proof.Length, bytes.Length);

        Assert.AreEqual(0xCAFEBABEu, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4)));
        Assert.AreEqual(0xDEAD_BEEF_F00D_BABEUL, System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(4, 8)));
        Assert.AreEqual(100UL, System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(12, 8)));
        Assert.AreEqual(200UL, System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(20, 8)));

        CollectionAssert.AreEqual(c.PreStateRoot.GetSpan().ToArray(), bytes[28..60]);
        CollectionAssert.AreEqual(c.PostStateRoot.GetSpan().ToArray(), bytes[60..92]);
        CollectionAssert.AreEqual(c.TxRoot.GetSpan().ToArray(), bytes[92..124]);
        CollectionAssert.AreEqual(c.ReceiptRoot.GetSpan().ToArray(), bytes[124..156]);
        CollectionAssert.AreEqual(c.WithdrawalRoot.GetSpan().ToArray(), bytes[156..188]);
        CollectionAssert.AreEqual(c.L2ToL1MessageRoot.GetSpan().ToArray(), bytes[188..220]);
        CollectionAssert.AreEqual(c.L2ToL2MessageRoot.GetSpan().ToArray(), bytes[220..252]);
        CollectionAssert.AreEqual(c.DACommitment.GetSpan().ToArray(), bytes[252..284]);
        CollectionAssert.AreEqual(c.PublicInputHash.GetSpan().ToArray(), bytes[284..316]);

        Assert.AreEqual((byte)ProofType.Zk, bytes[316]);
        Assert.AreEqual(c.Proof.Length, System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(317, 4)));
        CollectionAssert.AreEqual(c.Proof.ToArray(), bytes[321..]);
    }

    [TestMethod]
    public void Commitment_Encode_RejectsOversizedProof()
    {
        // Proof exceeding 1 MiB throws — defensive limit matching NeoHub.
        var oversized = new byte[1024 * 1024 + 1];
        var c = Sample(oversized);
        Assert.ThrowsExactly<ArgumentException>(() => BatchSerializer.Encode(c));
    }

    [TestMethod]
    public void Commitment_Encode_AcceptsExactlyMaxProof()
    {
        // Boundary case: exactly 1 MiB must succeed. Off-by-one in the limit check would
        // either reject this (too strict) or accept 1 MiB+1 (too loose) — pin both directions.
        var atLimit = new byte[1024 * 1024];
        var c = Sample(atLimit);
        var bytes = BatchSerializer.Encode(c);
        var decoded = BatchSerializer.Decode(bytes);
        Assert.AreEqual(atLimit.Length, decoded.Proof.Length);
    }

    [TestMethod]
    public void Commitment_Decode_RejectsHeaderClaimingOversizedProof()
    {
        // Craft a header that claims proof length > ProofMaxBytes; decoder must reject before
        // attempting to allocate the array.
        var c = Sample(new byte[] { 0xAA });
        var bytes = BatchSerializer.Encode(c);
        // Overwrite the proof-length prefix at offset 317 with a huge value.
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(317, 4), 1024 * 1024 + 1);
        Assert.ThrowsExactly<InvalidDataException>(() => BatchSerializer.Decode(bytes));
    }

    [TestMethod]
    public void PublicInputs_ByteLayout_MatchesDocumentedOffsets()
    {
        var inputs = new PublicInputs
        {
            ChainId = 0xCAFEBABE,
            BatchNumber = 0xDEAD_BEEFUL,
            PreStateRoot = H('a'),
            PostStateRoot = H('b'),
            TxRoot = H('c'),
            ReceiptRoot = H('d'),
            WithdrawalRoot = H('e'),
            L2ToL1MessageRoot = H('f'),
            L2ToL2MessageRoot = H('1'),
            L1MessageHash = H('2'),
            DACommitment = H('3'),
            BlockContextHash = H('4'),
        };
        var bytes = BatchSerializer.EncodePublicInputs(inputs);

        Assert.AreEqual(332, bytes.Length);
        Assert.AreEqual(0xCAFEBABEu, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4)));
        Assert.AreEqual(0xDEAD_BEEFUL, System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(4, 8)));
        CollectionAssert.AreEqual(inputs.PreStateRoot.GetSpan().ToArray(), bytes[12..44]);
        CollectionAssert.AreEqual(inputs.BlockContextHash.GetSpan().ToArray(), bytes[300..332]);
    }
}
