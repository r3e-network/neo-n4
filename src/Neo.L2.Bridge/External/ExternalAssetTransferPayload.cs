using System.Buffers.Binary;
using System.Numerics;

namespace Neo.L2.Bridge.External;

/// <summary>
/// Canonical encoding of an asset-transfer payload that rides inside an
/// <c>ExternalCrossChainMessage</c> with
/// <c>ExternalMessageType.AssetTransfer</c> (or <c>AssetAndCall</c>'s prefix).
/// </summary>
/// <remarks>
/// See <c>doc.md</c> §11.3.3–§11.3.4.
/// Layout (foreign asset in network order; numeric fields little-endian):
/// <code>
/// [20B foreignAsset]   (opaque network-order foreign asset identifier)
/// [4B  amountLength]
/// [amountLength B amount]   (unsigned BigInteger little-endian)
/// </code>
/// </remarks>
public sealed record ExternalAssetTransferPayload
{
    /// <summary>Canonical uint256 amount width shared with foreign-chain routers and
    /// <c>NeoHub.ExternalBridgeEscrow</c>.</summary>
    public const int MaxAmountBytes = 32;

    /// <summary>
    /// Foreign-side asset identifier copied as opaque network-order bytes.
    /// </summary>
    public required ExternalAssetId ForeignAsset { get; init; }

    /// <summary>Amount being transferred, smallest-unit unsigned BigInteger.</summary>
    public required BigInteger Amount { get; init; }

    /// <summary>Encode to canonical bytes for embedding in
    /// <c>ExternalCrossChainMessage.Payload</c>.</summary>
    public byte[] Encode()
    {
        ArgumentNullException.ThrowIfNull(ForeignAsset);
        if (Amount.Sign <= 0)
            throw new InvalidOperationException("Amount must be positive");

        // Emit minimal unsigned little-endian bytes so the wire format matches
        // the documented "unsigned BigInteger little-endian" layout, DepositPayload
        // (DepositPayload.cs:30), and the on-chain minimal-unsigned-LE leaf encoding.
        // Amount is asserted positive above, so isUnsigned is always valid; a
        // value with the top bit set gets NO extra 0x00 sign byte (which signed
        // ToByteArray would add), keeping the encoding byte-for-byte interoperable
        // with foreign-chain / payout-adapter counterparties.
        var amountBytes = Amount.ToByteArray(isUnsigned: true, isBigEndian: false);
        if (amountBytes.Length > MaxAmountBytes)
            throw new InvalidOperationException(
                $"Amount byte length {amountBytes.Length} exceeds MaxAmountBytes {MaxAmountBytes}");

        var size = 20 + 4 + amountBytes.Length;
        var buffer = new byte[size];
        ForeignAsset.CopyTo(buffer.AsSpan(0, ExternalAssetId.Length));
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

        ExternalAssetId foreignAsset;
        try
        {
            foreignAsset = new ExternalAssetId(bytes.Slice(0, ExternalAssetId.Length));
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException(ex.Message, nameof(bytes), ex);
        }
        var amountLen = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(20, 4));
        if (amountLen <= 0 || amountLen > MaxAmountBytes)
            throw new ArgumentException(
                $"amountLength {amountLen} out of bounds [1, {MaxAmountBytes}]", nameof(bytes));
        if (bytes.Length != 24 + amountLen)
            throw new ArgumentException(
                $"payload length {bytes.Length} != 24 + amountLen {amountLen}", nameof(bytes));
        if (bytes[^1] == 0)
            throw new ArgumentException(
                "amount must use minimal unsigned little-endian encoding", nameof(bytes));

        var amount = new BigInteger(bytes.Slice(24, amountLen), isUnsigned: true, isBigEndian: false);

        return new ExternalAssetTransferPayload
        {
            ForeignAsset = foreignAsset,
            Amount = amount,
        };
    }
}
