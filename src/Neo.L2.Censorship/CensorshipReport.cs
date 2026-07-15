using Neo.Cryptography.ECC;

namespace Neo.L2.Censorship;

/// <summary>
/// One overdue forced-inclusion event detected by <see cref="CensorshipDetector"/>. Carries the
/// metadata needed for a permissionless <c>NeoHub.ForcedInclusion.ReportCensorship</c> pause and,
/// only when finalized dBFT evidence identifies the responsible member, a separate
/// governance-authorized <c>SlashReportedCensorship</c> action.
/// </summary>
public sealed record CensorshipReport
{
    /// <summary>L2 chain identifier the report applies to.</summary>
    public required uint ChainId { get; init; }

    /// <summary>Forced-inclusion entry's per-chain nonce that went past its deadline.</summary>
    public required ulong ForcedInclusionNonce { get; init; }

    /// <summary>Hash of the forced transaction that was not included.</summary>
    public required UInt256 OverdueTxHash { get; init; }

    /// <summary>Unix timestamp at which the entry's deadline expired.</summary>
    public required uint DeadlineUnixSeconds { get; init; }

    /// <summary>
    /// Sequencer pubkey supported by finalized attribution evidence, or the curve identity when
    /// attribution is unavailable.
    /// </summary>
    public required ECPoint ResponsibleSequencer { get; init; }

    /// <summary>
    /// L1 address tied to the evidenced sequencer, or <see cref="UInt160.Zero"/> when governance
    /// must resolve attribution before slashing.
    /// </summary>
    public required UInt160 ResponsibleSequencerAddress { get; init; }

    /// <summary>
    /// Chain-policy slash amount proposed for an evidence-attributed report. This value is not an
    /// authorization; the on-chain governance action applies the configured contract policy.
    /// </summary>
    public required System.Numerics.BigInteger SlashAmount { get; init; }
}
