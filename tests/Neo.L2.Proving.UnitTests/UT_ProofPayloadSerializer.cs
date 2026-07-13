using System.Buffers.Binary;

namespace Neo.L2.Proving.UnitTests;

/// <summary>Boundary coverage for the shared proof-payload codec primitives.</summary>
[TestClass]
public sealed class UT_ProofPayloadSerializer
{
    [TestMethod]
    public void Version_RoundTripsAndRejectsInvalidBuffers()
    {
        Span<byte> buffer = stackalloc byte[1];
        ProofPayloadSerializer.WriteVersion(buffer, 7);
        ProofPayloadSerializer.ReadVersion(buffer, 7, "test");

        Assert.AreEqual((byte)7, buffer[0]);
        Assert.ThrowsExactly<ArgumentException>(
            () => ProofPayloadSerializer.WriteVersion(Span<byte>.Empty, 1));
        Assert.ThrowsExactly<ArgumentException>(
            () => ProofPayloadSerializer.ReadVersion(ReadOnlySpan<byte>.Empty, 1, "test"));
        Assert.ThrowsExactly<InvalidDataException>(
            () => ProofPayloadSerializer.ReadVersion(new byte[] { 2 }, 1, "test"));
    }

    [TestMethod]
    public void ExactLength_AcceptsMatchAndRejectsMismatch()
    {
        ProofPayloadSerializer.ValidateExactLength(4, 4, "test");
        Assert.ThrowsExactly<InvalidDataException>(
            () => ProofPayloadSerializer.ValidateExactLength(3, 4, "test"));
    }

    [TestMethod]
    public void VarBytesLength_EnforcesBothBounds()
    {
        ProofPayloadSerializer.ValidateVarBytesLength(0, 4, "field");
        ProofPayloadSerializer.ValidateVarBytesLength(4, 4, "field");

        Assert.ThrowsExactly<InvalidDataException>(
            () => ProofPayloadSerializer.ValidateVarBytesLength(-1, 4, "field"));
        Assert.ThrowsExactly<InvalidDataException>(
            () => ProofPayloadSerializer.ValidateVarBytesLength(5, 4, "field"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => ProofPayloadSerializer.ValidateVarBytesLength(0, -1, "field"));
    }

    [TestMethod]
    public void LengthPrefixed_RoundTripsAtNonZeroOffset()
    {
        var buffer = new byte[12];
        var writePosition = 2;
        ProofPayloadSerializer.WriteLengthPrefixed(
            buffer,
            ref writePosition,
            new byte[] { 0xAA, 0xBB, 0xCC });

        Assert.AreEqual(9, writePosition);
        Assert.AreEqual(3, BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(2, 4)));

        var readPosition = 2;
        var decoded = ProofPayloadSerializer.ReadLengthPrefixed(
            buffer.AsSpan(0, writePosition),
            ref readPosition,
            3,
            "field");

        CollectionAssert.AreEqual(new byte[] { 0xAA, 0xBB, 0xCC }, decoded.ToArray());
        Assert.AreEqual(writePosition, readPosition);
    }

    [TestMethod]
    public void WriteLengthPrefixed_RejectsInvalidPositionAndCapacity()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
        {
            var position = -1;
            ProofPayloadSerializer.WriteLengthPrefixed(new byte[8], ref position, new byte[1]);
        });
        Assert.ThrowsExactly<ArgumentException>(() =>
        {
            var position = 0;
            ProofPayloadSerializer.WriteLengthPrefixed(new byte[4], ref position, new byte[1]);
        });
        Assert.ThrowsExactly<ArgumentException>(() =>
        {
            var position = 5;
            ProofPayloadSerializer.WriteLengthPrefixed(new byte[8], ref position, ReadOnlySpan<byte>.Empty);
        });
    }

    [TestMethod]
    public void ReadLengthPrefixed_RejectsTruncatedOrInvalidPayloads()
    {
        Assert.ThrowsExactly<InvalidDataException>(() =>
        {
            var position = 0;
            ProofPayloadSerializer.ReadLengthPrefixed(new byte[3], ref position, 4, "field");
        });
        Assert.ThrowsExactly<InvalidDataException>(() =>
        {
            var position = -1;
            ProofPayloadSerializer.ReadLengthPrefixed(new byte[8], ref position, 4, "field");
        });
        Assert.ThrowsExactly<InvalidDataException>(() =>
        {
            var position = 0;
            var buffer = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(buffer, -1);
            ProofPayloadSerializer.ReadLengthPrefixed(buffer, ref position, 4, "field");
        });
        Assert.ThrowsExactly<InvalidDataException>(() =>
        {
            var position = 0;
            var buffer = new byte[5];
            BinaryPrimitives.WriteInt32LittleEndian(buffer, 2);
            ProofPayloadSerializer.ReadLengthPrefixed(buffer, ref position, 4, "field");
        });
    }
}
