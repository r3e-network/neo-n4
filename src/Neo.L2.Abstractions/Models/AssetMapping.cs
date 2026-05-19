using System.Numerics;
using Neo.SmartContract.Native;

namespace Neo.L2;

/// <summary>
/// Records the canonical L1 ↔ L2 representation of an asset in <c>NeoHub.TokenRegistry</c>.
/// </summary>
/// <remarks>
/// See doc.md §11.2.
/// </remarks>
public sealed record AssetMapping
{
    /// <summary>L1 contract hash for the canonical asset.</summary>
    public required UInt160 L1Asset { get; init; }

    /// <summary>L2 chain identifier the mapping applies to.</summary>
    public required uint L2ChainId { get; init; }

    /// <summary>L2 contract hash that represents the bridged asset.</summary>
    public required UInt160 L2Asset { get; init; }

    /// <summary>Decimals used by the L1 asset's smallest unit.</summary>
    public required byte L1Decimals { get; init; }

    /// <summary>Decimals used by the L2 representation's smallest unit.</summary>
    public required byte L2Decimals { get; init; }

    /// <summary>Asset category (GAS / NEO / NEP-17 / stablecoin / platform catalog / RWA / …).</summary>
    public required AssetType AssetType { get; init; }

    /// <summary>If true, L2 supply is mint/burn against the bridge.</summary>
    public required bool MintBurn { get; init; }

    /// <summary>If true, L1 supply is locked while L2 mints a representation.</summary>
    public required bool LockMint { get; init; }

    /// <summary>If false, the mapping is registered but cannot be used (governance pause).</summary>
    public required bool Active { get; init; }

    /// <summary>Convert a canonical L1 amount into this mapping's L2 smallest-unit amount.</summary>
    public BigInteger ToL2Amount(BigInteger l1Amount) => AssetAmount.Scale(l1Amount, L1Decimals, L2Decimals);

    /// <summary>Convert an L2 smallest-unit amount into this mapping's canonical L1 amount.</summary>
    public BigInteger ToL1Amount(BigInteger l2Amount) => AssetAmount.Scale(l2Amount, L2Decimals, L1Decimals);
}

/// <summary>
/// Asset categories recognized by <see cref="AssetMapping"/>. New categories are appended.
/// </summary>
public enum AssetType : byte
{
    /// <summary>Canonical GAS bridged from Neo L1.</summary>
    Gas = 0,

    /// <summary>Bridged NEO governance token.</summary>
    Neo = 1,

    /// <summary>Generic NEP-17 fungible token.</summary>
    Nep17 = 2,

    /// <summary>Pegged stablecoin (USDT, USDC, FIAT-backed).</summary>
    Stablecoin = 3,

    /// <summary>Real-world-asset representation (bond, equity, etc.).</summary>
    Rwa = 4,

    /// <summary>Platform-wide canonical USDT mapping.</summary>
    PlatformUsdt = 5,

    /// <summary>Platform-wide canonical USDC mapping.</summary>
    PlatformUsdc = 6,

    /// <summary>Platform-wide canonical BTC mapping.</summary>
    PlatformBtc = 7,
}

/// <summary>Shared amount-scaling rules for assets whose L1 and L2 decimals differ.</summary>
public static class AssetAmount
{
    /// <summary>Maximum decimal precision accepted by N4 token metadata.</summary>
    public const byte MaxDecimals = 18;

    /// <summary>Validate a decimal precision byte.</summary>
    public static void ValidateDecimals(byte decimals, string paramName)
    {
        if (decimals > MaxDecimals)
            throw new ArgumentOutOfRangeException(paramName, decimals, $"decimals must be between 0 and {MaxDecimals}.");
    }

    /// <summary>
    /// Scale an amount from one decimal domain to another. Down-scaling must be exact,
    /// otherwise the bridge would silently round value away.
    /// </summary>
    public static BigInteger Scale(BigInteger amount, byte fromDecimals, byte toDecimals)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        ValidateDecimals(fromDecimals, nameof(fromDecimals));
        ValidateDecimals(toDecimals, nameof(toDecimals));
        if (fromDecimals == toDecimals) return amount;

        var diff = Math.Abs(toDecimals - fromDecimals);
        var factor = BigInteger.Pow(10, diff);
        if (toDecimals > fromDecimals) return amount * factor;

        var quotient = BigInteger.DivRem(amount, factor, out var remainder);
        if (remainder != BigInteger.Zero)
            throw new InvalidOperationException(
                $"amount {amount} cannot be represented exactly with {toDecimals} decimals.");
        return quotient;
    }
}

/// <summary>
/// Platform-wide built-in token policy for N4 L2 chains.
/// L1 asset hashes are supplied by the caller because mainnet, testnet, and local
/// L1 forks can differ; L2 asset identifiers are native to the r3e N4 core fork.
/// </summary>
public static class PlatformAssets
{
    /// <summary>Canonical L2 mapped NEO token name.</summary>
    public const string L2NeoName = "NEO";

    /// <summary>Canonical L2 mapped NEO token symbol.</summary>
    public const string L2NeoSymbol = "NEO";

    /// <summary>Neo L1 NEO remains indivisible.</summary>
    public const byte L1NeoDecimals = 0;

    /// <summary>N4 L2 native NEO uses 8 decimals across all L2 chains.</summary>
    public const byte L2NeoDecimals = 8;

    /// <summary>Neo L1 GAS uses 8 decimals.</summary>
    public const byte L1GasDecimals = 8;

    /// <summary>N4 L2 native GAS uses 8 decimals across all L2 chains.</summary>
    public const byte L2GasDecimals = 8;

    /// <summary>Canonical L2 mapped USDT token name.</summary>
    public const string L2UsdtName = "USDT";

    /// <summary>Canonical L2 mapped USDT token symbol.</summary>
    public const string L2UsdtSymbol = "USDT";

    /// <summary>Canonical L1 USDT uses 6 decimals.</summary>
    public const byte L1UsdtDecimals = 6;

    /// <summary>N4 L2 platform USDT uses 6 decimals across all L2 chains.</summary>
    public const byte L2UsdtDecimals = 6;

    /// <summary>Canonical L2 mapped USDC token name.</summary>
    public const string L2UsdcName = "USDC";

    /// <summary>Canonical L2 mapped USDC token symbol.</summary>
    public const string L2UsdcSymbol = "USDC";

    /// <summary>Canonical L1 USDC uses 6 decimals.</summary>
    public const byte L1UsdcDecimals = 6;

    /// <summary>N4 L2 platform USDC uses 6 decimals across all L2 chains.</summary>
    public const byte L2UsdcDecimals = 6;

    /// <summary>Canonical L2 mapped BTC token name.</summary>
    public const string L2BtcName = "BTC";

    /// <summary>Canonical L2 mapped BTC token symbol.</summary>
    public const string L2BtcSymbol = "BTC";

    /// <summary>Canonical BTC representations use 8 decimals.</summary>
    public const byte L1BtcDecimals = 8;

    /// <summary>N4 L2 platform BTC uses 8 decimals across all L2 chains.</summary>
    public const byte L2BtcDecimals = 8;

    /// <summary>Native-bridge-managed N4 L2 NEO asset id from the L2 core fork.</summary>
    public static UInt160 L2NeoAsset => NativeContract.BridgedNep17.L2NeoTokenId;

    /// <summary>Native N4 L2 GAS asset id from the L2 core fork.</summary>
    public static UInt160 L2GasAsset => NativeContract.Governance.GasTokenId;

    /// <summary>Native-bridge-managed N4 L2 USDT asset id from the L2 core fork.</summary>
    public static UInt160 L2UsdtAsset => NativeContract.BridgedNep17.L2UsdtTokenId;

    /// <summary>Native-bridge-managed N4 L2 USDC asset id from the L2 core fork.</summary>
    public static UInt160 L2UsdcAsset => NativeContract.BridgedNep17.L2UsdcTokenId;

    /// <summary>Native-bridge-managed N4 L2 BTC asset id from the L2 core fork.</summary>
    public static UInt160 L2BtcAsset => NativeContract.BridgedNep17.L2BtcTokenId;

    /// <summary>Create the canonical NEO mapping for a given L1 asset hash and L2 chain.</summary>
    public static AssetMapping CreateNeoMapping(UInt160 l1Asset, uint l2ChainId, bool active = true) => new()
    {
        L1Asset = l1Asset,
        L2ChainId = l2ChainId,
        L2Asset = L2NeoAsset,
        L1Decimals = L1NeoDecimals,
        L2Decimals = L2NeoDecimals,
        AssetType = AssetType.Neo,
        MintBurn = true,
        LockMint = true,
        Active = active,
    };

    /// <summary>Create the canonical GAS mapping for a given L1 asset hash and L2 chain.</summary>
    public static AssetMapping CreateGasMapping(UInt160 l1Asset, uint l2ChainId, bool active = true) => new()
    {
        L1Asset = l1Asset,
        L2ChainId = l2ChainId,
        L2Asset = L2GasAsset,
        L1Decimals = L1GasDecimals,
        L2Decimals = L2GasDecimals,
        AssetType = AssetType.Gas,
        MintBurn = true,
        LockMint = true,
        Active = active,
    };

    /// <summary>Create the canonical USDT mapping for a given L1 asset hash and L2 chain.</summary>
    public static AssetMapping CreateUsdtMapping(UInt160 l1Asset, uint l2ChainId, bool active = true) =>
        CreateFixedDecimalsMapping(l1Asset, l2ChainId, L2UsdtAsset, L1UsdtDecimals, L2UsdtDecimals, AssetType.PlatformUsdt, active);

    /// <summary>Create the canonical USDC mapping for a given L1 asset hash and L2 chain.</summary>
    public static AssetMapping CreateUsdcMapping(UInt160 l1Asset, uint l2ChainId, bool active = true) =>
        CreateFixedDecimalsMapping(l1Asset, l2ChainId, L2UsdcAsset, L1UsdcDecimals, L2UsdcDecimals, AssetType.PlatformUsdc, active);

    /// <summary>Create the canonical BTC mapping for a given L1 asset hash and L2 chain.</summary>
    public static AssetMapping CreateBtcMapping(UInt160 l1Asset, uint l2ChainId, bool active = true) =>
        CreateFixedDecimalsMapping(l1Asset, l2ChainId, L2BtcAsset, L1BtcDecimals, L2BtcDecimals, AssetType.PlatformBtc, active);

    private static AssetMapping CreateFixedDecimalsMapping(
        UInt160 l1Asset,
        uint l2ChainId,
        UInt160 l2Asset,
        byte l1Decimals,
        byte l2Decimals,
        AssetType assetType,
        bool active) => new()
        {
            L1Asset = l1Asset,
            L2ChainId = l2ChainId,
            L2Asset = l2Asset,
            L1Decimals = l1Decimals,
            L2Decimals = l2Decimals,
            AssetType = assetType,
            MintBurn = true,
            LockMint = true,
            Active = active,
        };
}
