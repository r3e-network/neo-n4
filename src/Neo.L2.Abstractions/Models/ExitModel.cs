namespace Neo.L2;

/// <summary>
/// Exit model published in <see cref="L2ChainConfig"/> as part of the spec §16.2 security
/// label set. Tells users how they can leave the L2 if the sequencer turns malicious.
/// </summary>
/// <remarks>
/// See doc.md §16.2 (Security Labels — "Exit: permissionless / delayed / operator-assisted").
/// Pinned bytes 0..2 so a future add doesn't shift the existing wire format.
/// </remarks>
public enum ExitModel : byte
{
    /// <summary>User can exit at any time via <c>EmergencyManager.EscapeHatchExit*</c> with
    /// no operator cooperation — the strongest guarantee. Requires DA strong enough that
    /// the exit proof can be reconstructed off-chain.</summary>
    Permissionless = 0,

    /// <summary>User exit is permissionless but subject to a fixed challenge window
    /// (typical for optimistic rollups: 7 days, etc.).</summary>
    Delayed = 1,

    /// <summary>User exit requires the operator to co-sign or pre-stage exit batches.
    /// Acceptable only for low-trust use cases (validium / DAC chains where the operator
    /// is the DA committee).</summary>
    OperatorAssisted = 2,
}
