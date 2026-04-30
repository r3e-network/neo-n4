namespace Neo.L2.Audit;

/// <summary>
/// Verifies that the chain's batch sequence has the right shape:
/// <list type="bullet">
///   <item><description>batchNumbers are strictly increasing by 1.</description></item>
///   <item><description>preStateRoot of batch N+1 equals postStateRoot of batch N.</description></item>
///   <item><description>firstBlock of batch N+1 is greater than lastBlock of batch N.</description></item>
/// </list>
/// </summary>
public sealed class ContinuityCheck : IAuditCheck
{
    /// <inheritdoc />
    public string Name => "continuity";

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<AuditFinding>> RunAsync(
        IReadOnlyList<L2BatchCommitment> batches,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(batches);

        var findings = new List<AuditFinding>();
        for (var i = 1; i < batches.Count; i++)
        {
            var prev = batches[i - 1];
            var cur = batches[i];

            if (cur.BatchNumber != prev.BatchNumber + 1)
            {
                findings.Add(new AuditFinding
                {
                    Check = Name,
                    Passed = false,
                    BatchNumber = cur.BatchNumber,
                    Detail = $"batch number {cur.BatchNumber} does not follow {prev.BatchNumber}",
                });
                continue;
            }

            if (!cur.PreStateRoot.Equals(prev.PostStateRoot))
            {
                findings.Add(new AuditFinding
                {
                    Check = Name,
                    Passed = false,
                    BatchNumber = cur.BatchNumber,
                    Detail = $"preStateRoot {Truncate(cur.PreStateRoot)} != prior postStateRoot {Truncate(prev.PostStateRoot)}",
                });
                continue;
            }

            if (cur.FirstBlock <= prev.LastBlock)
            {
                findings.Add(new AuditFinding
                {
                    Check = Name,
                    Passed = false,
                    BatchNumber = cur.BatchNumber,
                    Detail = $"firstBlock {cur.FirstBlock} <= prior lastBlock {prev.LastBlock}",
                });
            }
        }

        if (findings.Count == 0)
        {
            findings.Add(new AuditFinding
            {
                Check = Name,
                Passed = true,
                BatchNumber = 0,
                Detail = $"{batches.Count} batches with continuous state roots and block ranges",
            });
        }
        return new ValueTask<IReadOnlyList<AuditFinding>>(findings);
    }

    private static string Truncate(UInt256 root)
    {
        var s = root.ToString();
        return s.Length <= 18 ? s : s[..10] + "…" + s[^6..];
    }
}
