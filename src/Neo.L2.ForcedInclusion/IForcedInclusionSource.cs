namespace Neo.L2.ForcedInclusion;

/// <summary>
/// L2-side view of the L1 forced-inclusion queue defined in <c>NeoHub.ForcedInclusion</c>.
/// See doc.md §15.4. Two consumers:
/// <list type="bullet">
///   <item><description><see cref="DrainAsync"/> feeds the batcher's forced-tx prepend:
///   <c>BatchSealer</c> takes a <c>forcedDrain</c> callback and prepends drained entries to the
///   start of each batch so a sequencer cannot silently exclude them. (The async source is adapted
///   to that synchronous callback by the orchestration / plugin wiring.)</description></item>
///   <item><description><see cref="HasOverdueEntryAsync"/> drives <c>CensorshipDetector</c>, which
///   produces a <c>CensorshipReport</c> that an operator submits to
///   <c>NeoHub.ForcedInclusion.ReportCensorship</c> — the permissionless on-chain pause. Slashing
///   is a separate governance action and requires finalized dBFT attribution evidence, so an
///   overdue entry cannot be used to frame an arbitrary committee member.</description></item>
/// </list>
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
    /// Confirm <paramref name="nonce"/> as consumed after L1 settlement finality and a successful
    /// permissionless <c>ForcedInclusion.consume</c> read-back. The batcher must never call this while a
    /// batch is merely open, sealed, proved, submitted, or pending finality.
    /// </summary>
    ValueTask ConfirmConsumedAsync(ulong nonce, CancellationToken cancellationToken = default);

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

    /// <inheritdoc />
    public bool Equals(ForcedInclusionEntry? other)
    {
        return other is not null
            && Nonce == other.Nonce
            && Sender.Equals(other.Sender)
            && TxHash.Equals(other.TxHash)
            && DeadlineUnixSeconds == other.DeadlineUnixSeconds
            && SerializedTx.Span.SequenceEqual(other.SerializedTx.Span);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Nonce);
        hash.Add(Sender);
        hash.Add(TxHash);
        hash.Add(DeadlineUnixSeconds);
        hash.AddBytes(SerializedTx.Span);
        return hash.ToHashCode();
    }
}
