namespace Neo.L2.Audit;

/// <summary>
/// Verifies that the chain's batch sequence has the right shape:
/// <list type="bullet">
///   <item><description>batchNumbers are strictly increasing by 1.</description></item>
///   <item><description>preStateRoot of batch N+1 equals postStateRoot of batch N.</description></item>
///   <item><description>firstBlock of batch N+1 is greater than lastBlock of batch N.</description></item>
/// </list>
/// Note: chainId continuity is already enforced upstream by <see cref="ChainAuditor"/>'s
/// precondition check (raises <c>ArgumentException</c>), so this check assumes a single-chain
/// list and does not re-verify it.
/// <para>
/// Scope: this is a <em>relative</em> continuity check over the supplied list — it verifies the
/// N-1 links between consecutive batches only. It does NOT anchor the first batch: the first
/// batch's <c>PreStateRoot</c> is not compared against genesis or any prior canonical root, and
/// its <c>BatchNumber</c> is not required to be 1. A single-batch list therefore performs zero
/// comparisons and passes vacuously, and a forked sub-range with internally-consistent roots
/// passes. When auditing a sub-range that does not start at batch 1, the caller is responsible
/// for separately proving the first batch follows the chain's real prior batch.
/// </para>
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
            // Be precise about what was actually checked: the inter-batch links (N-1 of them).
            // A single-batch (or empty) list verifies zero links and the first batch is never
            // anchored against a prior root — say so rather than implying every batch was
            // independently validated.
            var links = batches.Count > 0 ? batches.Count - 1 : 0;
            var detail = links == 0
                ? $"{batches.Count} batch(es); no inter-batch links to verify (first batch not anchored)"
                : $"{batches.Count} batches: {links} inter-batch links have continuous state roots and block ranges (first batch not anchored)";
            findings.Add(new AuditFinding
            {
                Check = Name,
                Passed = true,
                BatchNumber = 0,
                Detail = detail,
            });
        }
        return new ValueTask<IReadOnlyList<AuditFinding>>(findings);
    }

    private static string Truncate(UInt256 root) => AuditFormatting.Truncate(root);
}
