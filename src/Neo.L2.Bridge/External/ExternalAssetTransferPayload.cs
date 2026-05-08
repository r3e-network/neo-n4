using System.Buffers.Binary;
using System.Numerics;

namespace Neo.L2.Bridge.External;

/// <summary>
/// Canonical encoding of an asset-transfer payload that rides inside an
/// <c>ExternalCrossChainMessage</c> with
/// <c>ExternalMessageType.AssetTransfer</c> (or <c>AssetAndCall</c>'s prefix).
/// </summary>
/// <remarks>
/// Layout (little-endian):
/// <code>
/// [20B foreignAsset]   (Eth/Tron contract address; for Solana, 20 high bytes
///                       of the SPL token mint account, packed via Truncate20)
/// [4B  amountLength]
/// [amountLength B amount]   (unsigned BigInteger little-endian)
/// </code>
/// </remarks>
public sealed record ExternalAssetTransferPayload
{
    /// <summary>Hard cap on amount byte length to prevent runaway encodings.
    /// 64 bytes covers a 512-bit amount — far more than any realistic supply.</summary>
    public const int MaxAmountBytes = 64;

    /// <summary>Foreign-side asset identifier (last 20B of the foreign address).</summary>
    public required UInt160 ForeignAsset { get; init; }

    /// <summary>Amount being transferred, smallest-unit unsigned BigInteger.</summary>
    public required BigInteger Amount { get; init; }

    /// <summary>Encode to canonical bytes for embedding in
    /// <c>ExternalCrossChainMessage.Payload</c>.</summary>
    public byte[] Encode()
    {
        ArgumentNullException.ThrowIfNull(ForeignAsset);
        if (Amount.Sign < 0)
            throw new InvalidOperationException("Amount must be non-negative");

        // BigInteger.ToByteArray returns LE, two's-complement. For unsigned
        // values that don't naturally need a sign byte, the result has no
        // padding. We emit the raw bytes; callers passing Sign=0 get a
        // single zero byte (matches DepositPayload).
        var amountBytes = Amount.ToByteArray();
        if (amountBytes.Length > MaxAmountBytes)
            throw new InvalidOperationException(
                $"Amount byte length {amountBytes.Length} exceeds MaxAmountBytes {MaxAmountBytes}");

        var size = 20 + 4 + amountBytes.Length;
        var buffer = new byte[size];
        ForeignAsset.GetSpan().CopyTo(buffer.AsSpan(0, 20));
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(20, 4), amountBytes.Length);
        amountBytes.AsSpan().CopyTo(buffer.AsSpan(24));
        return buffer;
    }

    /// <summary>Decode canonical bytes back into the record.</summary>
    public static ExternalAssetTransferPayload Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 24)
            throw new ArgumentException(
                $"payload length {bytes.Length} < 24 (header)", nameof(bytes));

        var foreignAsset = new UInt160(bytes.Slice(0, 20).ToArray());
        var amountLen = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(20, 4));
        if (amountLen < 0 || amountLen > MaxAmountBytes)
            throw new ArgumentException(
                $"amountLength {amountLen} out of bounds [0, {MaxAmountBytes}]", nameof(bytes));
        if (bytes.Length != 24 + amountLen)
            throw new ArgumentException(
                $"payload length {bytes.Length} != 24 + amountLen {amountLen}", nameof(bytes));

        var amount = new BigInteger(bytes.Slice(24, amountLen).ToArray());
        return new ExternalAssetTransferPayload
        {
            ForeignAsset = foreignAsset,
            Amount = amount,
        };
    }
}
