using Neo.L2.ForcedInclusion;
using Neo.L2.Sequencer;

namespace Neo.L2.Censorship;

/// <summary>
/// Resolves a censorship report to a sequencer only when finalized dBFT evidence supports
/// that attribution.
/// </summary>
/// <remarks>
/// See doc.md §15.4 and §17. This optional operator-evidence adapter may propose an attribution only
/// from finalized block/view consensus data. Returning <see langword="null"/> leaves the report
/// unattributed. Permissionless on-chain reports always use the zero address; governance reviews
/// independent evidence before naming a sequencer in the separately authorized slash action.
/// </remarks>
public interface ICensorshipAttributionProvider
{
    /// <summary>Resolve the sequencer responsible for one overdue forced-inclusion entry.</summary>
    ValueTask<CommitteeMember?> ResolveResponsibleSequencerAsync(
        uint chainId,
        ForcedInclusionEntry entry,
        uint observedAtUnixSeconds,
        CancellationToken cancellationToken = default);
}
