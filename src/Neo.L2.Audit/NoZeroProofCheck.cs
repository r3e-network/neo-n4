namespace Neo.L2.Audit;

/// <summary>
/// Verifies that every audited batch carries a real proof — neither
/// <see cref="ProofType.None"/> nor empty proof bytes. A batch with no proof has been
/// soft-sealed by the local sequencer but never had a real attestation / optimistic /
/// ZK proof attached; if it slipped past <c>L2SettlementPlugin</c> in that state it
/// indicates stuck settlement, a misconfigured prover, or a manual override.
/// </summary>
/// <remarks>
/// This check looks at <see cref="L2BatchCommitment.ProofType"/> and
/// <see cref="L2BatchCommitment.Proof"/> in isolation; it does NOT re-verify the proof
/// (that's <see cref="ProofValidityCheck"/>'s job). Cheap and fast — useful for catching
/// the "soft-sealed but never proved" failure mode without paying full verification cost.
/// </remarks>
public sealed class NoZeroProofCheck : IAuditCheck
{
    /// <inheritdoc />
    public string Name => "no_zero_proof";

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

            if (batch.ProofType == ProofType.None)
            {
                findings.Add(new AuditFinding
                {
                    Check = Name,
                    Passed = false,
                    BatchNumber = batch.BatchNumber,
                    Detail = $"batch {batch.BatchNumber} has ProofType=None — soft-sealed but never proved",
                });
                failed++;
            }
            else if (batch.Proof.IsEmpty)
            {
                findings.Add(new AuditFinding
                {
                    Check = Name,
                    Passed = false,
                    BatchNumber = batch.BatchNumber,
                    Detail = $"batch {batch.BatchNumber} has ProofType={batch.ProofType} but Proof bytes are empty",
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
                Detail = $"all {batches.Count} batches carry a non-empty proof",
            });
        }
        return new ValueTask<IReadOnlyList<AuditFinding>>(findings);
    }
}
