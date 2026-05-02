using System.Buffers.Binary;
using System.Numerics;

namespace Neo.L2.Bridge;

/// <summary>
/// Canonical encoding of an asset-deposit payload that rides inside a
/// <see cref="CrossChainMessage"/> with <see cref="MessageType.Deposit"/>.
/// </summary>
/// <remarks>
/// Format (little-endian):
/// <c>[20B l1Asset] [20B l2Recipient] [4B amountLength] [amountLength B amount(unsigned LE)]</c>.
/// </remarks>
public sealed record DepositPayload
{
    /// <summary>L1 asset hash that was locked / burned at the SharedBridge.</summary>
    public required UInt160 L1Asset { get; init; }

    /// <summary>L2 address that should receive the bridged representation.</summary>
    public required UInt160 L2Recipient { get; init; }

    /// <summary>Smallest-unit amount being deposited.</summary>
    public required BigInteger Amount { get; init; }

    /// <summary>Encode to canonical bytes for embedding in <see cref="CrossChainMessage.Payload"/>.</summary>
    public byte[] Encode()
    {
        var amountBytes = Amount.ToByteArray(isUnsigned: true, isBigEndian: false);
        if (amountBytes.Length > 64)
            throw new InvalidOperationException("Deposit amount exceeds 64 bytes");
        var size = 20 + 20 + 4 + amountBytes.Length;
        var buffer = new byte[size];
        var span = buffer.AsSpan();
        var pos = 0;
        L1Asset.GetSpan().CopyTo(span.Slice(pos, 20)); pos += 20;
        L2Recipient.GetSpan().CopyTo(span.Slice(pos, 20)); pos += 20;
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(pos, 4), amountBytes.Length); pos += 4;
        amountBytes.CopyTo(span.Slice(pos)); pos += amountBytes.Length;
        if (pos != size) throw new InvalidOperationException("DepositPayload encode length mismatch");
        return buffer;
    }

    /// <summary>Decode a canonical payload back to a <see cref="DepositPayload"/>.</summary>
    public static DepositPayload Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 44)
            throw new ArgumentException("DepositPayload too small", nameof(bytes));
        var pos = 0;
        var l1 = new UInt160(bytes.Slice(pos, 20)); pos += 20;
        var l2 = new UInt160(bytes.Slice(pos, 20)); pos += 20;
        var amountLen = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(pos, 4)); pos += 4;
        // Strict length match: trailing bytes after the amount would be silently ignored,
        // creating a malleability surface where an attacker appends padding the L1 hashes
        // (full bytes) but the L2 ignores. Same defensive pattern as OptimisticProofPayload.
        if (amountLen < 0 || amountLen > 64 || pos + amountLen != bytes.Length)
            throw new InvalidDataException(
                $"Invalid amount length {amountLen} (expected total {pos + amountLen}, have {bytes.Length})");
        var amount = new BigInteger(bytes.Slice(pos, amountLen), isUnsigned: true, isBigEndian: false);
        return new DepositPayload { L1Asset = l1, L2Recipient = l2, Amount = amount };
    }
}
