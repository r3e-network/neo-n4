namespace Neo.L2;

/// <summary>
/// Discriminator for cross-chain messages routed through Neo Connect.
/// </summary>
/// <remarks>
/// See doc.md §3.2 (MessageRouter) and §10 (Neo Connect).
/// </remarks>
public enum MessageType : byte
{
    /// <summary>Asset deposit (typically L1 → L2).</summary>
    Deposit = 0,

    /// <summary>Asset withdrawal (typically L2 → L1).</summary>
    Withdraw = 1,

    /// <summary>Generic contract call payload.</summary>
    Call = 2,

    /// <summary>Event notification (no expected response).</summary>
    Event = 3,

    /// <summary>Governance / configuration update.</summary>
    Governance = 4,
}
