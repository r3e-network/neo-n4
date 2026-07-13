namespace Neo.Plugins.L2Gateway.UnitTests;

/// <summary>Canonical ABI tests for SettlementManager Gateway finality references.</summary>
[TestClass]
public sealed class UT_GatewayFinalityReferenceSerializer
{
    [TestMethod]
    public void Encode_WritesStrictLittleEndianLayout()
    {
        var constituents = new[]
        {
            Commitment(0x01020304, 0x0102030405060708),
            Commitment(0x0A0B0C0D, 0x1112131415161718),
        };

        var encoded = GatewayFinalityReferenceSerializer.Encode(constituents);

        CollectionAssert.AreEqual(
            new byte[]
            {
                0x04, 0x03, 0x02, 0x01,
                0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01,
                0x0D, 0x0C, 0x0B, 0x0A,
                0x18, 0x17, 0x16, 0x15, 0x14, 0x13, 0x12, 0x11,
            },
            encoded);
        CollectionAssert.AreEqual(
            new[]
            {
                new GatewayFinalityReference(0x01020304, 0x0102030405060708),
                new GatewayFinalityReference(0x0A0B0C0D, 0x1112131415161718),
            },
            GatewayFinalityReferenceSerializer.Decode(encoded).ToArray());
    }

    [TestMethod]
    public void Encode_RejectsReservedUnorderedAndDuplicateReferences()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => GatewayFinalityReferenceSerializer.Encode(new[] { Commitment(0, 1) }));
        Assert.ThrowsExactly<ArgumentException>(() => GatewayFinalityReferenceSerializer.Encode(
            new[] { Commitment(2, 1), Commitment(1, 1) }));
        Assert.ThrowsExactly<ArgumentException>(() => GatewayFinalityReferenceSerializer.Encode(
            new[] { Commitment(1, 1), Commitment(1, 1) }));
    }

    [TestMethod]
    public void Decode_RejectsMalformedOrNonCanonicalPayloads()
    {
        Assert.ThrowsExactly<InvalidDataException>(
            () => GatewayFinalityReferenceSerializer.Decode(ReadOnlySpan<byte>.Empty));
        Assert.ThrowsExactly<InvalidDataException>(
            () => GatewayFinalityReferenceSerializer.Decode(new byte[11]));
        Assert.ThrowsExactly<InvalidDataException>(
            () => GatewayFinalityReferenceSerializer.Decode(new byte[12]));

        var duplicate = GatewayFinalityReferenceSerializer.Encode(
            new[] { Commitment(1, 1), Commitment(1, 2) });
        duplicate[16] = 1;
        duplicate[17] = 0;
        duplicate[18] = 0;
        duplicate[19] = 0;
        Assert.ThrowsExactly<InvalidDataException>(
            () => GatewayFinalityReferenceSerializer.Decode(duplicate));
    }

    [TestMethod]
    public void Encode_Accepts4096AndRejects4097Constituents()
    {
        var maximum = Enumerable.Range(1, GatewayFinalityReferenceSerializer.MaxConstituents)
            .Select(batchNumber => Commitment(1, (ulong)batchNumber))
            .ToArray();

        Assert.AreEqual(
            GatewayFinalityReferenceSerializer.MaxConstituents
                * GatewayFinalityReferenceSerializer.EntrySize,
            GatewayFinalityReferenceSerializer.Encode(maximum).Length);

        var excessive = maximum.Append(Commitment(1, 4097)).ToArray();
        Assert.ThrowsExactly<ArgumentException>(
            () => GatewayFinalityReferenceSerializer.Encode(excessive));
        Assert.ThrowsExactly<InvalidDataException>(
            () => GatewayFinalityReferenceSerializer.Decode(
                new byte[(GatewayFinalityReferenceSerializer.MaxConstituents + 1)
                    * GatewayFinalityReferenceSerializer.EntrySize]));
    }

    private static L2BatchCommitment Commitment(uint chainId, ulong batchNumber) => new()
    {
        ChainId = chainId,
        BatchNumber = batchNumber,
        FirstBlock = batchNumber,
        LastBlock = batchNumber,
        PreStateRoot = Hash(0x01),
        PostStateRoot = Hash(0x02),
        TxRoot = Hash(0x03),
        ReceiptRoot = Hash(0x04),
        WithdrawalRoot = Hash(0x05),
        L2ToL1MessageRoot = Hash(0x06),
        L2ToL2MessageRoot = Hash(0x07),
        DACommitment = Hash(0x08),
        PublicInputHash = Hash(0x09),
        ProofType = ProofType.Zk,
        Proof = new byte[] { 0x10 },
    };

    private static UInt256 Hash(byte value) => new(Enumerable.Repeat(value, 32).ToArray());
}
