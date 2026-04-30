using System;
using System.ComponentModel;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace NeoHub.TokenRegistry;

/// <summary>
/// Canonical L1↔L2 asset mapping. SharedBridge uses this to know what L2 representation a
/// freshly deposited L1 asset should mint. See doc.md §11.2 (AssetMapping).
/// </summary>
[DisplayName("NeoHub.TokenRegistry")]
[ContractAuthor("Neo Project", "dev@neo.org")]
[ContractDescription("Canonical L1 ↔ L2 asset mapping registry for Neo Elastic Network.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/neo-project/neo4/tree/master/contracts/NeoHub.TokenRegistry")]
[ContractPermission(Permission.Any, Method.Any)]
public class TokenRegistryContract : SmartContract
{
    private const byte PrefixMapping = 0x01;   // 0x01 + l1Asset(20B) + l2ChainId(4B) → encoded mapping
    private const byte KeyOwner = 0xFF;

    /// <summary>
    /// Encoded mapping: 20B l1Asset + 4B chainId + 20B l2Asset + 1B assetType +
    /// 1B mintBurn + 1B lockMint + 1B active = 48B.
    /// </summary>
    public const int MappingSize = 20 + 4 + 20 + 4;

    /// <summary>Emitted when a new mapping is registered or replaced.</summary>
    [DisplayName("MappingRegistered")]
    public static event Action<UInt160, uint, UInt160> OnMappingRegistered = default!;

    /// <summary>Set the initial owner.</summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var owner = (UInt160)data;
        ExecutionEngine.Assert(owner.IsValid && !owner.IsZero, "invalid owner");
        Storage.Put(new byte[] { KeyOwner }, owner);
    }

    /// <summary>Governance owner.</summary>
    [Safe]
    public static UInt160 GetOwner()
    {
        var raw = Storage.Get(new byte[] { KeyOwner });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Register a new L1 ↔ L2 asset mapping. Owner only.</summary>
    public static void RegisterMapping(byte[] mappingBytes)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(mappingBytes.Length == MappingSize, "mapping size mismatch");

        var l1Asset = ReadUInt160(mappingBytes, 0);
        var chainId = ReadUInt32(mappingBytes, 20);
        var l2Asset = ReadUInt160(mappingBytes, 24);

        Storage.Put(MappingKey(l1Asset, chainId), mappingBytes);
        OnMappingRegistered(l1Asset, chainId, l2Asset);
    }

    /// <summary>Read the mapping for a given (l1Asset, chainId) pair.</summary>
    [Safe]
    public static byte[] GetMapping(UInt160 l1Asset, uint chainId)
    {
        var raw = Storage.Get(MappingKey(l1Asset, chainId));
        return raw == null ? new byte[0] : (byte[])raw;
    }

    /// <summary>Convenience: extract just the L2 asset hash from a mapping.</summary>
    [Safe]
    public static UInt160 GetL2Asset(UInt160 l1Asset, uint chainId)
    {
        var raw = Storage.Get(MappingKey(l1Asset, chainId));
        if (raw == null) return UInt160.Zero;
        var bytes = (byte[])raw;
        return ReadUInt160(bytes, 24);
    }

    /// <summary>True if the mapping's active flag is set (last byte == 1).</summary>
    [Safe]
    public static bool IsActive(UInt160 l1Asset, uint chainId)
    {
        var raw = Storage.Get(MappingKey(l1Asset, chainId));
        if (raw == null) return false;
        var bytes = (byte[])raw;
        return bytes[MappingSize - 1] == 1;
    }

    private static byte[] MappingKey(UInt160 l1Asset, uint chainId)
    {
        var k = new byte[1 + 20 + 4];
        k[0] = PrefixMapping;
        var b = (byte[])l1Asset;
        for (var i = 0; i < 20; i++) k[1 + i] = b[i];
        k[21] = (byte)chainId; k[22] = (byte)(chainId >> 8); k[23] = (byte)(chainId >> 16); k[24] = (byte)(chainId >> 24);
        return k;
    }

    private static UInt160 ReadUInt160(byte[] data, int offset)
    {
        var slice = new byte[20];
        for (var i = 0; i < 20; i++) slice[i] = data[offset + i];
        return (UInt160)slice;
    }

    private static uint ReadUInt32(byte[] data, int offset)
    {
        return (uint)data[offset]
            | ((uint)data[offset + 1] << 8)
            | ((uint)data[offset + 2] << 16)
            | ((uint)data[offset + 3] << 24);
    }
}
