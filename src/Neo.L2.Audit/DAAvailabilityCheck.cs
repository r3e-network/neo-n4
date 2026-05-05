namespace Neo.L2.Audit;

/// <summary>
/// Verifies that every audited batch's <see cref="L2BatchCommitment.DACommitment"/>
/// resolves to a payload still available on the configured DA layer. Catches the
/// "DA layer dropped/garbage-collected the payload" failure mode that would leave
/// L2 batches recoverable in commitment but unrecoverable in actual data — the worst
/// kind of silent data-availability regression.
/// </summary>
/// <remarks>
/// Cheap to run for in-memory / RocksDB DA writers; for real NeoFS / L1-DA backends,
/// each <c>IsAvailableAsync</c> call may incur an external round-trip. Operators who
/// run this on a hot path should sample (e.g. last-N batches) rather than auditing
/// the entire chain history every interval.
/// <para>
/// Skips batches whose <c>DACommitment</c> is <see cref="UInt256.Zero"/> — the legacy
/// "no DA published yet" sentinel. Those would be flagged by the upstream
/// <see cref="NoZeroProofCheck"/> only if they also lack a proof; a batch with a real
/// proof but no DA commitment is an artifact of older devnet runs and shouldn't
/// gate the audit pass.
/// </para>
/// </remarks>
public sealed class DAAvailabilityCheck : IAuditCheck
{
    private readonly IDAWriter _da;

    /// <summary>Wire the DA writer whose <c>IsAvailableAsync</c> will be queried.</summary>
    public DAAvailabilityCheck(IDAWriter da)
    {
        // Without this guard a null _da NREs deep inside RunAsync's IsAvailable call,
        // surfacing as an unrelated stack trace far from the wiring bug. Same iter-156
        // pattern as the other audit checks.
        ArgumentNullException.ThrowIfNull(da);
        _da = da;
    }

    /// <inheritdoc />
    public string Name => "da_availability";

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AuditFinding>> RunAsync(
        IReadOnlyList<L2BatchCommitment> batches,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(batches);

        var findings = new List<AuditFinding>();
        var failed = 0;
        var skipped = 0;

        foreach (var batch in batches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (batch.DACommitment == UInt256.Zero)
            {
                // Legacy "no DA" sentinel. Don't flag — a batch with this state is
                // older than the devnet's DA wiring change and shouldn't gate audits.
                skipped++;
                continue;
            }

            // Build a minimal receipt — IsAvailableAsync only consumes Commitment,
            // Pointer, and Layer. We use the DA writer's mode since the original
            // pointer isn't available in the commitment alone (this is content-
            // addressed, so commitment alone is enough for in-memory / RocksDB
            // writers; real L1-DA / NeoFS writers may need the original pointer
            // re-derived from another source).
            var receipt = new DAReceipt
            {
                Commitment = batch.DACommitment,
                Pointer = ReadOnlyMemory<byte>.Empty,
                Layer = _da.Mode,
            };
            var available = await _da.IsAvailableAsync(receipt, cancellationToken)
                .ConfigureAwait(false);
            if (!available)
            {
                findings.Add(new AuditFinding
                {
                    Check = Name,
                    Passed = false,
                    BatchNumber = batch.BatchNumber,
                    Detail = $"batch {batch.BatchNumber} DACommitment={batch.DACommitment} is no longer available on {_da.Mode}",
                });
                failed++;
            }
        }

        if (failed == 0)
        {
            var checkedCount = batches.Count - skipped;
            findings.Add(new AuditFinding
            {
                Check = Name,
                Passed = true,
                BatchNumber = 0,
                Detail = skipped == 0
                    ? $"all {checkedCount} batches' DA payloads remain available on {_da.Mode}"
                    : $"all {checkedCount} batches' DA payloads remain available on {_da.Mode} ({skipped} skipped — legacy zero commitment)",
            });
        }
        return findings;
    }
}
