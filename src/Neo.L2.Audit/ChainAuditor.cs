using Neo.L2.Telemetry;

namespace Neo.L2.Audit;

/// <summary>
/// Orchestrates a chain audit. Runs every registered <see cref="IAuditCheck"/> over the
/// supplied batch sequence and produces a single <see cref="AuditReport"/>.
/// </summary>
public sealed class ChainAuditor
{
    private readonly List<IAuditCheck> _checks = new();
    private readonly IL2Metrics _metrics;

    /// <summary>Construct, optionally wired to a metrics sink.</summary>
    public ChainAuditor(IL2Metrics? metrics = null)
    {
        _metrics = metrics ?? NoOpMetrics.Instance;
    }

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
            // Surface the failure both in the report AND in the metrics so an operator
            // who watches dashboards sees the audit count tick + failure tick. Without
            // these the empty-input case appears nowhere in counters — the run is
            // invisible to monitoring even though we returned a failed report.
            _metrics.IncrementCounter(MetricNames.AuditsRun);
            _metrics.IncrementCounter(MetricNames.AuditFailures, 1);
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
            // Strictly ascending: duplicate batch numbers are a precondition violation
            // (a chain can't carry two distinct commitments at the same height). Without
            // the equality clause, duplicates would get buried as a "continuity violation"
            // by ContinuityCheck downstream, masking the real issue.
            if (batches[i].BatchNumber <= batches[i - 1].BatchNumber)
                throw new ArgumentException(
                    $"batches must be sorted strictly ascending by batchNumber " +
                    $"(batch {i}: {batches[i].BatchNumber}, prior: {batches[i - 1].BatchNumber})");
        }

        var allFindings = new List<AuditFinding>();
        if (_checks.Count == 0)
        {
            // No checks registered → audit proves nothing; surface as a failure rather
            // than silently passing.
            allFindings.Add(new AuditFinding
            {
                Check = "input", Passed = false, BatchNumber = 0,
                Detail = "no audit checks registered — call Register() before Audit",
            });
        }
        else
        {
            foreach (var check in _checks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IReadOnlyList<AuditFinding> findings;
                try
                {
                    findings = await check.RunAsync(batches, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Caller cancelled — bubble out so the auditor honors the token
                    // contract instead of swallowing into a finding.
                    throw;
                }
                catch (Exception ex)
                {
                    // A buggy custom check that throws shouldn't abort the entire audit.
                    // Convert to a failure finding so the remaining checks still run and
                    // the operator sees both the broken check AND the rest of the report.
                    findings = new[]
                    {
                        new AuditFinding
                        {
                            Check = check.Name,
                            Passed = false,
                            BatchNumber = 0,
                            Detail = $"check threw {ex.GetType().Name}: {ex.Message}",
                        },
                    };
                }
                allFindings.AddRange(findings);
            }
        }

        var failureCount = allFindings.Count(f => !f.Passed);
        _metrics.IncrementCounter(MetricNames.AuditsRun);
        if (failureCount > 0)
            _metrics.IncrementCounter(MetricNames.AuditFailures, failureCount);

        return new AuditReport
        {
            ChainId = chainId,
            FirstBatch = batches[0].BatchNumber,
            LastBatch = batches[^1].BatchNumber,
            Findings = allFindings,
        };
    }
}
