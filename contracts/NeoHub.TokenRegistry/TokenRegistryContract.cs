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
[ContractAuthor("R3E Network", "dev@r3e.network")]
[ContractDescription("Canonical L1 ↔ L2 asset mapping registry for Neo Elastic Network.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.TokenRegistry")]
[ContractPermission(Permission.Any, Method.Any)]
public class TokenRegistryContract : SmartContract
{
    private const byte PrefixMapping = 0x01;   // 0x01 + l1Asset(20B) + l2ChainId(4B) → encoded mapping
    private const byte KeyOwner = 0xFF;
    private const byte AssetTypeGas = 0;
    private const byte AssetTypeNeo = 1;
    private const byte AssetTypePlatformUsdt = 5;
    private const byte AssetTypePlatformUsdc = 6;
    private const byte AssetTypePlatformBtc = 7;

    /// <summary>
    /// Encoded mapping: 20B l1Asset + 4B chainId + 20B l2Asset + 1B assetType +
    /// 1B mintBurn + 1B lockMint + 1B l1Decimals + 1B l2Decimals + 1B active = 50B.
    /// </summary>
    public const int MappingSize = 20 + 4 + 20 + 6;

    /// <summary>Emitted when a new mapping is registered or replaced.</summary>
    [DisplayName("MappingRegistered")]
    public static event Action<UInt160, uint, UInt160> OnMappingRegistered = default!;

    /// <summary>Emitted when a mapping's active flag is toggled.</summary>
    [DisplayName("MappingActiveChanged")]
    public static event Action<UInt160, uint, bool> OnMappingActiveChanged = default!;

    /// <summary>Emitted when ownership is transferred.</summary>
    [DisplayName("OwnerChanged")]
    public static event Action<UInt160, UInt160> OnOwnerChanged = default!;

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

    /// <summary>Transfer governance ownership. Owner only.</summary>
    public static void SetOwner(UInt160 newOwner)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(newOwner.IsValid && !newOwner.IsZero, "invalid new owner");
        var oldOwner = GetOwner();
        Storage.Put(new byte[] { KeyOwner }, newOwner);
        OnOwnerChanged(oldOwner, newOwner);
    }

    /// <summary>Register a new L1 ↔ L2 asset mapping. Owner only.</summary>
    public static void RegisterMapping(byte[] mappingBytes)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(mappingBytes.Length == MappingSize, "mapping size mismatch");

        var l1Asset = ReadUInt160(mappingBytes, 0);
        var chainId = ReadUInt32(mappingBytes, 20);
        var l2Asset = ReadUInt160(mappingBytes, 24);
        var assetType = mappingBytes[44];
        var l1Decimals = mappingBytes[47];
        var l2Decimals = mappingBytes[48];

        // Surface corrupt deployment data here. A zero asset on either side would
        // silently route every bridge transfer of that asset to a dead-letter; a
        // chainId=0 mapping is the L1 sentinel which no L2 watches.
        ExecutionEngine.Assert(l1Asset.IsValid && !l1Asset.IsZero, "invalid L1 asset");
        ExecutionEngine.Assert(l2Asset.IsValid && !l2Asset.IsZero, "invalid L2 asset");
        ExecutionEngine.Assert(chainId > 0, "chainId 0 is reserved for L1");
        ExecutionEngine.Assert(l1Decimals <= 18 && l2Decimals <= 18, "invalid decimals");
        if (assetType == AssetTypeNeo)
        {
            ExecutionEngine.Assert(l1Decimals == 0, "L1 NEO decimals must be 0");
            ExecutionEngine.Assert(l2Decimals == 8, "L2 NEO decimals must be 8");
        }
        if (assetType == AssetTypeGas)
        {
            ExecutionEngine.Assert(l1Decimals == 8 && l2Decimals == 8, "GAS decimals must be 8");
        }
        if (assetType == AssetTypePlatformUsdt)
        {
            ExecutionEngine.Assert(l1Decimals == 6 && l2Decimals == 6, "USDT decimals must be 6");
        }
        if (assetType == AssetTypePlatformUsdc)
        {
            ExecutionEngine.Assert(l1Decimals == 6 && l2Decimals == 6, "USDC decimals must be 6");
        }
        if (assetType == AssetTypePlatformBtc)
        {
            ExecutionEngine.Assert(l1Decimals == 8 && l2Decimals == 8, "BTC decimals must be 8");
        }

        // Ensure new mappings are active by default (last byte = 1). The incoming mappingBytes is a
        // NeoVM ByteString (immutable) — index-assigning it (mappingBytes[49] = 1) compiles to
        // SETITEM and FAULTs at runtime ("Invalid type for SETITEM: ByteString"). Copy into a fresh
        // mutable buffer and set the active byte there. (Without the active default, callers who
        // don't set the flag would get an inactive mapping that silently blocks bridge transfers.)
        var stored = new byte[MappingSize];
        for (var i = 0; i < MappingSize; i++) stored[i] = mappingBytes[i];
        stored[MappingSize - 1] = 1;

        Storage.Put(MappingKey(l1Asset, chainId), stored);
        OnMappingRegistered(l1Asset, chainId, l2Asset);
    }

    /// <summary>
    /// Activate or deactivate an existing mapping. Owner only. Deactivating a mapping makes
    /// <see cref="IsActive"/> return false, which the SharedBridge deposit/withdrawal paths use to
    /// gate transfers — letting governance freeze a specific asset/chain pair (e.g. a deprecated
    /// or compromised token) without deleting the mapping or its decimals metadata.
    /// </summary>
    public static void SetActive(UInt160 l1Asset, uint chainId, bool active)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        var raw = Storage.Get(MappingKey(l1Asset, chainId));
        ExecutionEngine.Assert(raw != null, "mapping not found");
        // raw is a NeoVM ByteString (immutable); index-assigning it FAULTs at runtime. Copy into a
        // fresh mutable buffer, flip the active byte there, then store.
        var src = (byte[])raw!;
        var bytes = new byte[MappingSize];
        for (var i = 0; i < MappingSize; i++) bytes[i] = src[i];
        bytes[MappingSize - 1] = (byte)(active ? 1 : 0);
        Storage.Put(MappingKey(l1Asset, chainId), bytes);
        OnMappingActiveChanged(l1Asset, chainId, active);
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

    /// <summary>L1 decimals recorded for a mapping, or 0 if the mapping is missing.</summary>
    [Safe]
    public static byte GetL1Decimals(UInt160 l1Asset, uint chainId)
    {
        var raw = Storage.Get(MappingKey(l1Asset, chainId));
        if (raw == null) return 0;
        var bytes = (byte[])raw;
        return bytes[47];
    }

    /// <summary>L2 decimals recorded for a mapping, or 0 if the mapping is missing.</summary>
    [Safe]
    public static byte GetL2Decimals(UInt160 l1Asset, uint chainId)
    {
        var raw = Storage.Get(MappingKey(l1Asset, chainId));
        if (raw == null) return 0;
        var bytes = (byte[])raw;
        return bytes[48];
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
