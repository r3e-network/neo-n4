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
}
