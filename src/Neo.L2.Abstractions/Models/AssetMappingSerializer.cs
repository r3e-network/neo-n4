using System.Buffers.Binary;

namespace Neo.L2;

/// <summary>Canonical 50-byte wire encoding consumed by NeoHub.TokenRegistry.</summary>
/// <remarks>See doc.md §11.2 (AssetMapping).</remarks>
public static class AssetMappingSerializer
{
    /// <summary>Fixed encoded mapping length.</summary>
    public const int EncodedSize = 50;

    /// <summary>Encodes a mapping using little-endian integers and raw Neo hash bytes.</summary>
    public static byte[] Encode(AssetMapping mapping)
    {
        Validate(mapping);
        var bytes = new byte[EncodedSize];
        mapping.L1Asset.GetSpan().CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20, sizeof(uint)), mapping.L2ChainId);
        mapping.L2Asset.GetSpan().CopyTo(bytes.AsSpan(24));
        bytes[44] = (byte)mapping.AssetType;
        bytes[45] = mapping.MintBurn ? (byte)1 : (byte)0;
        bytes[46] = mapping.LockMint ? (byte)1 : (byte)0;
        bytes[47] = mapping.L1Decimals;
        bytes[48] = mapping.L2Decimals;
        bytes[49] = mapping.Active ? (byte)1 : (byte)0;
        return bytes;
    }

    /// <summary>Decodes and validates the canonical 50-byte mapping encoding.</summary>
    public static AssetMapping Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != EncodedSize)
            throw new FormatException($"AssetMapping must be exactly {EncodedSize} bytes, got {bytes.Length}.");
        var mapping = new AssetMapping
        {
            L1Asset = new UInt160(bytes[..20]),
            L2ChainId = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(20, sizeof(uint))),
            L2Asset = new UInt160(bytes.Slice(24, UInt160.Length)),
            AssetType = (AssetType)bytes[44],
            MintBurn = DecodeBoolean(bytes[45], 45),
            LockMint = DecodeBoolean(bytes[46], 46),
            L1Decimals = bytes[47],
            L2Decimals = bytes[48],
            Active = DecodeBoolean(bytes[49], 49),
        };
        Validate(mapping);
        return mapping;
    }

    private static void Validate(AssetMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        if (mapping.L1Asset is null || mapping.L1Asset == UInt160.Zero)
            throw new ArgumentException("L1 asset must not be zero.", nameof(mapping));
        ChainIdValidator.ValidateL2(mapping.L2ChainId, nameof(mapping));
        if (mapping.L2Asset is null || mapping.L2Asset == UInt160.Zero)
            throw new ArgumentException("L2 asset must not be zero.", nameof(mapping));
        if (!Enum.IsDefined(mapping.AssetType))
            throw new ArgumentOutOfRangeException(nameof(mapping), mapping.AssetType, "Unknown asset type.");
        AssetAmount.ValidateDecimals(mapping.L1Decimals, nameof(mapping));
        AssetAmount.ValidateDecimals(mapping.L2Decimals, nameof(mapping));
    }

    private static bool DecodeBoolean(byte value, int offset)
    {
        return value switch
        {
            0 => false,
            1 => true,
            _ => throw new FormatException($"AssetMapping boolean at offset {offset} must be 0 or 1."),
        };
    }
}
