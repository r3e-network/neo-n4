using Neo.L2.State;

namespace Neo.L2.Audit;

/// <summary>
/// Verifies that each batch's stored <see cref="L2BatchCommitment.PublicInputHash"/>
/// matches the hash of public inputs reconstructed from the commitment's own fields.
/// Catches commitments where the PublicInputHash was set to a value derived from
/// different public inputs than the commitment claims — a tampered submission that
/// would otherwise verify against the wrong proof.
/// </summary>
/// <remarks>
/// MVP: assumes <c>L1MessageHash</c> and <c>BlockContextHash</c> are zero (matching the
/// current Phase 0-3 settlement plugin's <c>BuildPublicInputs</c>). When future phases
/// fill those in, this check needs an augmenting resolver — the same shape as
/// <see cref="ProofValidityCheck"/>'s <c>publicInputsResolver</c>.
/// </remarks>
public sealed class PublicInputHashConsistencyCheck : IAuditCheck
{
    /// <inheritdoc />
    public string Name => "public_input_hash";

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

            var inputs = new PublicInputs
            {
                ChainId = batch.ChainId,
                BatchNumber = batch.BatchNumber,
                PreStateRoot = batch.PreStateRoot,
                PostStateRoot = batch.PostStateRoot,
                TxRoot = batch.TxRoot,
                ReceiptRoot = batch.ReceiptRoot,
                WithdrawalRoot = batch.WithdrawalRoot,
                L2ToL1MessageRoot = batch.L2ToL1MessageRoot,
                L2ToL2MessageRoot = batch.L2ToL2MessageRoot,
                L1MessageHash = UInt256.Zero,
                DACommitment = batch.DACommitment,
                BlockContextHash = UInt256.Zero,
            };
            var expected = StateRootCalculator.HashPublicInputs(inputs);

            if (!batch.PublicInputHash.Equals(expected))
            {
                findings.Add(new AuditFinding
                {
                    Check = Name,
                    Passed = false,
                    BatchNumber = batch.BatchNumber,
                    Detail = $"batch {batch.BatchNumber} PublicInputHash mismatch: stored {Truncate(batch.PublicInputHash)}, expected {Truncate(expected)}",
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
                Detail = $"all {batches.Count} batches have consistent PublicInputHash",
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
