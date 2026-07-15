using System.Buffers.Binary;

namespace Neo.L2.Abstractions.UnitTests;

[TestClass]
public class UT_AssetMappingSerializer
{
    [TestMethod]
    public void Encode_UsesCanonicalTokenRegistryLayout()
    {
        var mapping = Mapping();

        var bytes = AssetMappingSerializer.Encode(mapping);

        Assert.AreEqual(50, bytes.Length);
        CollectionAssert.AreEqual(mapping.L1Asset.GetSpan().ToArray(), bytes[..20]);
        Assert.AreEqual(mapping.L2ChainId, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(20, 4)));
        CollectionAssert.AreEqual(mapping.L2Asset.GetSpan().ToArray(), bytes[24..44]);
        CollectionAssert.AreEqual(new byte[] { 5, 1, 1, 6, 6, 1 }, bytes[44..]);
    }

    [TestMethod]
    public void Decode_RoundTripsCanonicalMapping()
    {
        var mapping = Mapping();

        var decoded = AssetMappingSerializer.Decode(AssetMappingSerializer.Encode(mapping));

        Assert.AreEqual(mapping, decoded);
    }

    [TestMethod]
    public void Decode_RejectsNonCanonicalBoolean()
    {
        var bytes = AssetMappingSerializer.Encode(Mapping());
        bytes[45] = 2;

        Assert.ThrowsExactly<FormatException>(() => AssetMappingSerializer.Decode(bytes));
    }

    [TestMethod]
    public void Encode_RejectsZeroAssetsAndReservedChain()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            AssetMappingSerializer.Encode(Mapping() with { L1Asset = UInt160.Zero }));
        Assert.ThrowsExactly<ArgumentException>(() =>
            AssetMappingSerializer.Encode(Mapping() with { L2Asset = UInt160.Zero }));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            AssetMappingSerializer.Encode(Mapping() with { L2ChainId = 0 }));
    }

    private static AssetMapping Mapping()
    {
        return new AssetMapping
        {
            L1Asset = UInt160.Parse("0x" + new string('1', 40)),
            L2ChainId = 1001,
            L2Asset = UInt160.Parse("0x" + new string('2', 40)),
            AssetType = AssetType.PlatformUsdt,
            MintBurn = true,
            LockMint = true,
            L1Decimals = 6,
            L2Decimals = 6,
            Active = true,
        };
    }
}
