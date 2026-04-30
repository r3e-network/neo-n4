using Neo.L2.Proving;

namespace Neo.L2.Audit;

/// <summary>
/// Re-runs each batch's proof through the configured <see cref="VerifierRegistry"/>. Catches
/// the case where a stored batch carries a proof that no longer verifies (verifier upgrade
/// regression, corrupted on-disk state, malicious submission that slipped past).
/// </summary>
public sealed class ProofValidityCheck : IAuditCheck
{
    private readonly VerifierRegistry _registry;
    private readonly Func<L2BatchCommitment, PublicInputs> _publicInputsResolver;

    /// <summary>Construct.</summary>
    public ProofValidityCheck(VerifierRegistry registry, Func<L2BatchCommitment, PublicInputs> publicInputsResolver)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(publicInputsResolver);
        _registry = registry;
        _publicInputsResolver = publicInputsResolver;
    }

    /// <inheritdoc />
    public string Name => "proof";

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AuditFinding>> RunAsync(
        IReadOnlyList<L2BatchCommitment> batches,
        CancellationToken cancellationToken = default)
    {
        var findings = new List<AuditFinding>();
        foreach (var batch in batches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var publicInputs = _publicInputsResolver(batch);
            var result = await _registry.VerifyAsync(batch, publicInputs, cancellationToken).ConfigureAwait(false);
            findings.Add(new AuditFinding
            {
                Check = Name,
                Passed = result.Valid,
                BatchNumber = batch.BatchNumber,
                Detail = result.Valid
                    ? $"{batch.ProofType} proof verified ({batch.Proof.Length} bytes)"
                    : result.FailureReason ?? "verification failed",
            });
        }
        return findings;
    }
}
