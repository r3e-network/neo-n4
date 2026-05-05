namespace Neo.L2.Audit;

/// <summary>
/// Verifies each batch's intra-batch block range invariants:
/// <list type="bullet">
///   <item><description><c>FirstBlock &lt;= LastBlock</c> — a range-inverted batch (e.g. first=10, last=5)
///     is structurally meaningless and indicates a buggy sequencer.</description></item>
///   <item><description><c>BatchNumber &gt;= 1</c> — batch numbers start at 1 (0 is reserved for genesis).</description></item>
/// </list>
/// </summary>
/// <remarks>
/// Cheap to run — no cross-batch comparisons or external lookups. Complements
/// <see cref="ContinuityCheck"/> which validates inter-batch ordering but assumes
/// each batch is internally well-formed.
/// </remarks>
public sealed class BatchRangeCheck : IAuditCheck
{
    /// <inheritdoc />
    public string Name => "batch_range";

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<AuditFinding>> RunAsync(
        IReadOnlyList<L2BatchCommitment> batches,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(batches);

        var findings = new List<AuditFinding>();
        var failed = 0;

        foreach (var batch in batches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (batch.FirstBlock > batch.LastBlock)
            {
                findings.Add(new AuditFinding
                {
                    Check = Name,
                    Passed = false,
                    BatchNumber = batch.BatchNumber,
                    Detail = $"batch {batch.BatchNumber} has inverted range: firstBlock {batch.FirstBlock} > lastBlock {batch.LastBlock}",
                });
                failed++;
                continue;
            }

            if (batch.BatchNumber == 0)
            {
                findings.Add(new AuditFinding
                {
                    Check = Name,
                    Passed = false,
                    BatchNumber = 0,
                    Detail = "batchNumber 0 is reserved for genesis — operational batches start at 1",
                });
                failed++;
            }
        }

        if (failed == 0)
        {
            findings.Add(new AuditFinding
            {
                Check = Name,
                Passed = true,
                BatchNumber = 0,
                Detail = $"all {batches.Count} batches have valid intra-batch ranges (firstBlock <= lastBlock, batchNumber >= 1)",
            });
        }
        return new ValueTask<IReadOnlyList<AuditFinding>>(findings);
    }
}
