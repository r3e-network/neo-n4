using Neo.L2;

namespace Neo.Plugins.L2Gateway;

/// <summary>
/// Phase 5 (doc.md §4) — collects per-chain L2 batch commitments and produces an
/// aggregated commitment that NeoHub.SettlementManager accepts in one shot.
/// </summary>
/// <remarks>
/// The reference scaffold here is a pass-through that re-emits each commitment unchanged.
/// Production deploys plug in a recursive-proof backend (SP1 aggregation, Halo2 accumulation,
/// or a custom STARK aggregation) and a global message-root tree.
/// </remarks>
public interface IGatewayAggregator
{
    /// <summary>Receive a per-chain commitment ready for aggregation.</summary>
    void Submit(L2BatchCommitment commitment);

    /// <summary>How many commitments are currently pending aggregation.</summary>
    int PendingCount { get; }

    /// <summary>
    /// Produce an aggregated commitment over all currently-pending entries and clear the
    /// queue. Returns <c>null</c> when nothing is pending.
    /// </summary>
    AggregatedCommitment? Aggregate();
}

/// <summary>
/// Output of <see cref="IGatewayAggregator.Aggregate"/>. Carries the constituent batch
/// numbers (per chain), the global message root that ties their L2-to-L2 messages together,
/// and the aggregated proof bytes.
/// </summary>
public sealed record AggregatedCommitment
{
    /// <summary>One <see cref="L2BatchCommitment"/> per source chain that contributed.</summary>
    public required IReadOnlyList<L2BatchCommitment> Constituents { get; init; }

    /// <summary>Merkle root of all L2-to-L2 messages from the constituent batches.</summary>
    public required UInt256 GlobalMessageRoot { get; init; }

    /// <summary>Aggregated proof bytes (interpretation depends on backend).</summary>
    public required ReadOnlyMemory<byte> AggregatedProof { get; init; }

    /// <summary>Identifier of the aggregation backend (matches <c>ProofSystem</c> in RiscVZk).</summary>
    public required byte BackendId { get; init; }

    /// <inheritdoc />
    public bool Equals(AggregatedCommitment? other)
    {
        return other is not null
            && BackendId == other.BackendId
            && GlobalMessageRoot.Equals(other.GlobalMessageRoot)
            && AggregatedProof.Span.SequenceEqual(other.AggregatedProof.Span);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(BackendId);
        hash.Add(GlobalMessageRoot);
        hash.AddBytes(AggregatedProof.Span);
        return hash.ToHashCode();
    }
}
