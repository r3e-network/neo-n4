namespace Neo.L2;

/// <summary>
/// Per-L2 configuration as registered in <c>NeoHub.ChainRegistry</c>.
/// </summary>
/// <remarks>
/// See doc.md §3.2 (ChainRegistry).
/// </remarks>
public sealed record L2ChainConfig
{
    /// <summary>Globally unique chain identifier within the Neo Elastic Network.</summary>
    public required uint ChainId { get; init; }

    /// <summary>L1 contract account that controls operator-level chain parameters.</summary>
    public required UInt160 OperatorManager { get; init; }

    /// <summary>L1 verifier contract for this chain's batches.</summary>
    public required UInt160 Verifier { get; init; }

    /// <summary>L1 bridge adapter contract for this chain's deposits / withdrawals.</summary>
    public required UInt160 BridgeAdapter { get; init; }

    /// <summary>L1 message adapter contract for this chain's L1↔L2 / L2↔L2 messages.</summary>
    public required UInt160 MessageAdapter { get; init; }

    /// <summary>Public security label exposed to users and bridges.</summary>
    public required SecurityLevel SecurityLevel { get; init; }

    /// <summary>Data availability mode this chain is using.</summary>
    public required DAMode DAMode { get; init; }

    /// <summary>True if this chain participates in Neo Gateway proof aggregation.</summary>
    public required bool GatewayEnabled { get; init; }

    /// <summary>True if users can trigger an unconditional escape hatch withdrawal.</summary>
    public required bool PermissionlessExit { get; init; }

    /// <summary>True if the chain is currently active (not paused by NeoHub governance).</summary>
    public required bool Active { get; init; }
}
