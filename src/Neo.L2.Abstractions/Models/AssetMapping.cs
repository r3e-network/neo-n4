namespace Neo.L2;

/// <summary>
/// Records the canonical L1 ↔ L2 representation of an asset in <c>NeoHub.TokenRegistry</c>.
/// </summary>
/// <remarks>
/// See doc.md §11.2.
/// </remarks>
public sealed record AssetMapping
{
    /// <summary>L1 contract hash for the asset (or <see cref="UInt160.Zero"/> for native GAS).</summary>
    public required UInt160 L1Asset { get; init; }

    /// <summary>L2 chain identifier the mapping applies to.</summary>
    public required uint L2ChainId { get; init; }

    /// <summary>L2 contract hash that represents the bridged asset.</summary>
    public required UInt160 L2Asset { get; init; }

    /// <summary>Asset category (GAS / NEO / NEP-17 / stablecoin / RWA / …).</summary>
    public required AssetType AssetType { get; init; }

    /// <summary>If true, L2 supply is mint/burn against the bridge.</summary>
    public required bool MintBurn { get; init; }

    /// <summary>If true, L1 supply is locked while L2 mints a representation.</summary>
    public required bool LockMint { get; init; }

    /// <summary>If false, the mapping is registered but cannot be used (governance pause).</summary>
    public required bool Active { get; init; }
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
}
