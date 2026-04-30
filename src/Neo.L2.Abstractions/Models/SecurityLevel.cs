namespace Neo.L2;

/// <summary>
/// Public security label that <see cref="L2ChainConfig"/> exposes via <c>NeoHub.ChainRegistry</c>.
/// Users and bridges read this to make trust decisions.
/// </summary>
/// <remarks>
/// See doc.md §3.2 and §12. Higher numbers mean stronger guarantees but not necessarily lower cost.
/// </remarks>
public enum SecurityLevel : byte
{
    /// <summary>No L1 verification. Pure sidechain trust.</summary>
    Sidechain = 0,

    /// <summary>Sidechain whose batches commit to L1 but no fraud or validity proof is checked.</summary>
    Settled = 1,

    /// <summary>Optimistic rollup — batches enter pending and may be challenged within a window.</summary>
    Optimistic = 2,

    /// <summary>ZK validity rollup — every state transition is proven and verified on L1.</summary>
    Validity = 3,

    /// <summary>ZK validity proof on L1, but data availability is off-chain (DAC, NeoFS, external DA).</summary>
    Validium = 4,
}
