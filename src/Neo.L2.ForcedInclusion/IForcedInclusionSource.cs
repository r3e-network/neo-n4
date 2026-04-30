namespace Neo.L2.ForcedInclusion;

/// <summary>
/// L2-side view of the L1 forced-inclusion queue defined in <c>NeoHub.ForcedInclusion</c>.
/// The L2 batcher polls this before sealing each batch and prepends any pending entries to
/// the transaction list. See doc.md §15.4.
/// </summary>
public interface IForcedInclusionSource
{
    /// <summary>L2 chain identifier this source watches.</summary>
    uint ChainId { get; }

    /// <summary>
    /// Fetch the entries that the sequencer must include in the next batch (deadline-ordered,
    /// oldest first). Implementations should cap the returned count to avoid unbounded batches.
    /// </summary>
    ValueTask<IReadOnlyList<ForcedInclusionEntry>> DrainAsync(int max, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark <paramref name="nonce"/> as consumed so future <see cref="DrainAsync"/> calls
    /// don't return it. Called by the batcher once the entry has been added to a sealed batch.
    /// </summary>
    ValueTask MarkConsumedAsync(ulong nonce, CancellationToken cancellationToken = default);

    /// <summary>
    /// True if any unconsumed entry has its deadline past <paramref name="nowUnixSeconds"/>.
    /// The batcher uses this to decide whether to halt finalization for censorship reasons.
    /// </summary>
    ValueTask<bool> HasOverdueEntryAsync(uint nowUnixSeconds, CancellationToken cancellationToken = default);
}

/// <summary>
/// One entry on the L1 forced-inclusion queue. Mirrors the canonical encoding in
/// <c>NeoHub.ForcedInclusion.EncodeEntry</c>: 20B sender + 32B txHash + 4B txLen + tx + 4B deadline.
/// </summary>
public sealed record ForcedInclusionEntry
{
    /// <summary>Per-chain monotonic nonce.</summary>
    public required ulong Nonce { get; init; }

    /// <summary>L1 sender that posted the forced tx.</summary>
    public required UInt160 Sender { get; init; }

    /// <summary>Hash of the encoded transaction (matches the L2's tx hashing).</summary>
    public required UInt256 TxHash { get; init; }

    /// <summary>Canonical Neo-serialized transaction bytes the L2 must execute.</summary>
    public required ReadOnlyMemory<byte> SerializedTx { get; init; }

    /// <summary>Unix timestamp (seconds) by which the L2 must include this entry.</summary>
    public required uint DeadlineUnixSeconds { get; init; }
}
