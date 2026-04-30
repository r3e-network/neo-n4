namespace Neo.L2.Audit;

/// <summary>
/// One independent auditable invariant. <see cref="ChainAuditor"/> runs every registered
/// check and aggregates their findings into an <see cref="AuditReport"/>.
/// </summary>
public interface IAuditCheck
{
    /// <summary>Stable identifier (e.g. <c>"continuity"</c>, <c>"proof"</c>).</summary>
    string Name { get; }

    /// <summary>Run the check and return zero or more findings.</summary>
    ValueTask<IReadOnlyList<AuditFinding>> RunAsync(
        IReadOnlyList<L2BatchCommitment> batches,
        CancellationToken cancellationToken = default);
}
