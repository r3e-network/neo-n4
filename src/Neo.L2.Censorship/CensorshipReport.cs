using Neo.Cryptography.ECC;

namespace Neo.L2.Censorship;

/// <summary>
/// One slashable-censorship event detected by <see cref="CensorshipDetector"/>. Carries the
/// metadata an operator needs to submit a <c>NeoHub.ForcedInclusion.ReportCensorship</c> call
/// and (transitively) trigger <c>NeoHub.SequencerBond.Slash</c>.
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

    /// <summary>Sequencer pubkey held responsible (typically the active one when the deadline passed).</summary>
    public required ECPoint ResponsibleSequencer { get; init; }

    /// <summary>L1 address tied to the responsible sequencer (used for the slash payout claim).</summary>
    public required UInt160 ResponsibleSequencerAddress { get; init; }

    /// <summary>Amount to slash (caller computes per chain policy; e.g. proportional to delay).</summary>
    public required System.Numerics.BigInteger SlashAmount { get; init; }
}
