namespace Neo.L2;

/// <summary>
/// Data Availability mode advertised by an L2 chain. Recorded in <c>NeoHub.DARegistry</c>.
/// </summary>
/// <remarks>
/// See doc.md §7.4 and §12. Values 0 through 3 are public security labels. The
/// <see cref="Local"/> sentinel is reserved for operator-local durability and tests; it
/// must never be registered as a chain's public DA mode.
/// </remarks>
public enum DAMode : byte
{
    /// <summary>Compressed batch data published to Neo N3 / Neo 4 L1. Highest cost, highest security.</summary>
    L1 = 0,

    /// <summary>Batch data stored in NeoFS; NeoHub records the object commitment.</summary>
    NeoFS = 1,

    /// <summary>Generic external DA layer (e.g. Celestia, EigenDA).</summary>
    External = 2,

    /// <summary>Data Availability Committee — fixed set of signers attest to availability. Lowest cost, highest risk.</summary>
    DAC = 3,

    /// <summary>
    /// Process-local or node-local storage. This is durability, not public data
    /// availability, and is rejected by canonical ChainRegistry encoders.
    /// </summary>
    Local = byte.MaxValue,
}

/// <summary>Classification helpers for public and local DA modes.</summary>
/// <remarks>See doc.md §7.4 and §12.</remarks>
public static class DAModeExtensions
{
    /// <summary>Return whether a mode may be advertised through ChainRegistry.</summary>
    public static bool IsPublic(this DAMode mode)
        => mode is DAMode.L1 or DAMode.NeoFS or DAMode.External or DAMode.DAC;
}
