namespace Neo.L2;

/// <summary>
/// Data Availability mode advertised by an L2 chain. Recorded in <c>NeoHub.DARegistry</c>.
/// </summary>
/// <remarks>
/// See doc.md §7.4 and §12. Cost decreases and risk increases as the number rises.
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
}
