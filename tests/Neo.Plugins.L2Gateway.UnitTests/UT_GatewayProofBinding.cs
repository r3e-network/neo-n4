using System.Buffers.Binary;
using Neo.L2.Batch;

namespace Neo.Plugins.L2Gateway.UnitTests;

[TestClass]
public class UT_GatewayProofBinding
{
    private static UInt256 H(byte value) => new(Enumerable.Repeat(value, 32).ToArray());

    private static L2BatchCommitment Batch(uint chainId, ulong batchNumber, byte proof) => new()
    {
        ChainId = chainId,
        BatchNumber = batchNumber,
        FirstBlock = batchNumber,
        LastBlock = batchNumber,
        PreStateRoot = H(0x01),
        PostStateRoot = H(0x02),
        TxRoot = H(0x03),
        ReceiptRoot = H(0x04),
        WithdrawalRoot = H(0x05),
        L2ToL1MessageRoot = H(0x06),
        L2ToL2MessageRoot = H((byte)(chainId & 0xFF)),
        DACommitment = H(0x08),
        PublicInputHash = H(0x09),
        ProofType = ProofType.Zk,
        Proof = new byte[] { proof },
    };

    private static AggregatedCommitment Aggregate(params L2BatchCommitment[] constituents) => new()
    {
        Constituents = constituents,
        GlobalMessageRoot = H(0x51),
        ConstituentCommitmentsRoot =
            GatewayProofBindingSerializer.ComputeConstituentCommitmentsRoot(constituents),
        AggregatedProof = new byte[] { 0xCA, 0xFE },
        BackendId = MerklePathRoundProver.ConstBackendId,
    };

    [TestMethod]
    public void EncodeDecode_PinsCanonicalLayoutAndHash()
    {
        var commitment = Aggregate(Batch(1001, 1, 0x11), Batch(2002, 1, 0x22));
        var router = UInt160.Parse("0x" + new string('a', 40));
        var binding = GatewayProofBindingSerializer.Create(
            router,
            H(0xD1),
            77,
            commitment,
            1,
            H(0xA1));

        var encoded = GatewayProofBindingSerializer.Encode(binding);
        var decoded = GatewayProofBindingSerializer.Decode(encoded);

        Assert.AreEqual(GatewayProofBindingSerializer.EncodedSize, encoded.Length);
        CollectionAssert.AreEqual("NEO4GWR2"u8.ToArray(), encoded[..8]);
        Assert.AreEqual(77UL, BinaryPrimitives.ReadUInt64LittleEndian(encoded.AsSpan(60, 8)));
        Assert.AreEqual(2U, BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(132, 4)));
        Assert.AreEqual(MerklePathRoundProver.ConstBackendId, encoded[136]);
        Assert.AreEqual(1, encoded[137]);
        Assert.AreEqual(binding, decoded);
        Assert.AreEqual(
            GatewayProofBindingSerializer.ComputeHash(binding),
            GatewayProofBindingSerializer.ComputeHash(decoded));
    }

    [TestMethod]
    public void ConstituentRoot_BindsEveryCanonicalCommitmentByte()
    {
        var first = Batch(1001, 1, 0x11);
        var second = Batch(2002, 1, 0x22);
        var original = GatewayProofBindingSerializer.ComputeConstituentCommitmentsRoot(
            new[] { first, second });
        var tampered = second with { Proof = new byte[] { 0x23 } };
        var changed = GatewayProofBindingSerializer.ComputeConstituentCommitmentsRoot(
            new[] { first, tampered });

        Assert.AreNotEqual(original, changed);
        CollectionAssert.AreNotEqual(
            BatchSerializer.Encode(second),
            BatchSerializer.Encode(tampered));
    }

    [TestMethod]
    public void Create_RejectsReorderedDuplicateAndMutatedConstituents()
    {
        var first = Batch(1001, 1, 0x11);
        var second = Batch(2002, 1, 0x22);
        var router = UInt160.Parse("0x" + new string('a', 40));
        Assert.ThrowsExactly<ArgumentException>(() => GatewayProofBindingSerializer.Create(
            router,
            H(0xD1),
            1,
            Aggregate(second, first),
            1,
            H(0xA1)));
        Assert.ThrowsExactly<ArgumentException>(() => GatewayProofBindingSerializer.Create(
            router,
            H(0xD1),
            1,
            Aggregate(first, first),
            1,
            H(0xA1)));

        var aggregate = Aggregate(first, second);
        ((L2BatchCommitment[])aggregate.Constituents)[1] = second with { Proof = new byte[] { 0x23 } };
        Assert.ThrowsExactly<InvalidOperationException>(() => GatewayProofBindingSerializer.Create(
            router,
            H(0xD1),
            1,
            aggregate,
            1,
            H(0xA1)));
    }

    [TestMethod]
    public void Validate_RejectsPassThroughAndZeroDomains()
    {
        var commitment = Aggregate(Batch(1001, 1, 0x11));
        var router = UInt160.Parse("0x" + new string('a', 40));
        Assert.ThrowsExactly<ArgumentException>(() => GatewayProofBindingSerializer.Create(
            router,
            UInt256.Zero,
            1,
            commitment,
            1,
            H(0xA1)));
        Assert.IsFalse(GatewayProofBindingSerializer.IsProductionAggregationBackend(
            PassThroughRoundProver.ConstBackendId));
        Assert.IsFalse(GatewayProofBindingSerializer.IsProductionAggregationBackend(
            PassThroughAggregator.BackendId));
    }

    [TestMethod]
    public void EncodeDecode_AllowsCanonicalZeroGlobalMessageRoot()
    {
        var constituent = Batch(1001, 1, 0x11) with { L2ToL2MessageRoot = UInt256.Zero };
        var commitment = Aggregate(constituent) with { GlobalMessageRoot = UInt256.Zero };
        var binding = GatewayProofBindingSerializer.Create(
            UInt160.Parse("0x" + new string('a', 40)),
            H(0xD1),
            1,
            commitment,
            1,
            H(0xA1));

        var encoded = GatewayProofBindingSerializer.Encode(binding);
        var decoded = GatewayProofBindingSerializer.Decode(encoded);

        Assert.AreEqual(UInt256.Zero, decoded.GlobalMessageRoot);
        Assert.IsTrue(encoded.AsSpan(68, 32).SequenceEqual(new byte[32]));
    }
}
