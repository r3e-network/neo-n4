namespace Neo.Plugins.L2Gateway;

/// <summary>
/// One layer of aggregation. Given two child commitments (proof bytes + global-root contribution),
/// the round prover produces a single combined commitment for the next layer.
/// </summary>
/// <remarks>
/// Default impl is <see cref="PassThroughRoundProver"/> which just hashes the two inputs together.
/// Production swaps in a real recursive ZK backend: SP1 Compress, Halo2 accumulators, Risc0 STARK
/// folding, etc. The interface intentionally exposes only opaque byte slices so the C# side does
/// not depend on any particular proof system.
/// </remarks>
public interface IRoundProver
{
    /// <summary>Identifier of this prover (logged + emitted in <see cref="AggregatedCommitment"/>).</summary>
    byte BackendId { get; }

    /// <summary>
    /// Combine two child round-results into one parent. Either child may be null if this round
    /// has odd cardinality (the lone child is promoted unchanged when <paramref name="right"/> is null).
    /// </summary>
    RoundResult Combine(RoundResult left, RoundResult? right);
}

/// <summary>
/// One node in the aggregation tree. Carries the L2-to-L2 message-root contribution + the
/// proof bytes for that subtree.
/// </summary>
public sealed record RoundResult
{
    /// <summary>Hash that contributes to <see cref="AggregatedCommitment.GlobalMessageRoot"/>.</summary>
    public required UInt256 MessageRootContribution { get; init; }

    /// <summary>Opaque proof bytes for this subtree. Format depends on backend.</summary>
    public required ReadOnlyMemory<byte> ProofBytes { get; init; }
}
