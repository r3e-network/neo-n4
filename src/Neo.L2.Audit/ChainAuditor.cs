namespace Neo.L2.Audit;

/// <summary>
/// Orchestrates a chain audit. Runs every registered <see cref="IAuditCheck"/> over the
/// supplied batch sequence and produces a single <see cref="AuditReport"/>.
/// </summary>
public sealed class ChainAuditor
{
    private readonly List<IAuditCheck> _checks = new();

    /// <summary>Add a check. Order matters — checks are run sequentially.</summary>
    public ChainAuditor Register(IAuditCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        _checks.Add(check);
        return this;
    }

    /// <summary>Number of registered checks.</summary>
    public int CheckCount => _checks.Count;

    /// <summary>
    /// Run all checks against <paramref name="batches"/>. The batches must be sorted by
    /// <see cref="L2BatchCommitment.BatchNumber"/> ascending and all have the same chainId.
    /// </summary>
    public async ValueTask<AuditReport> AuditAsync(
        IReadOnlyList<L2BatchCommitment> batches,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batches);
        if (batches.Count == 0)
        {
            return new AuditReport
            {
                ChainId = 0,
                FirstBatch = 0,
                LastBatch = 0,
                Findings = new[]
                {
                    new AuditFinding
                    {
                        Check = "input", Passed = false, BatchNumber = 0,
                        Detail = "no batches supplied",
                    },
                },
            };
        }

        var chainId = batches[0].ChainId;
        for (var i = 1; i < batches.Count; i++)
        {
            if (batches[i].ChainId != chainId)
                throw new ArgumentException($"batch {i} has chainId {batches[i].ChainId}, expected {chainId}");
            if (batches[i].BatchNumber < batches[i - 1].BatchNumber)
                throw new ArgumentException($"batches must be sorted by batchNumber ascending");
        }

        var allFindings = new List<AuditFinding>();
        foreach (var check in _checks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var findings = await check.RunAsync(batches, cancellationToken).ConfigureAwait(false);
            allFindings.AddRange(findings);
        }

        return new AuditReport
        {
            ChainId = chainId,
            FirstBatch = batches[0].BatchNumber,
            LastBatch = batches[^1].BatchNumber,
            Findings = allFindings,
        };
    }
}
