using Neo.L2.State;

namespace Neo.L2.Audit;

/// <summary>
/// Verifies that each batch's stored <see cref="L2BatchCommitment.PublicInputHash"/>
/// matches independently resolved canonical public inputs.
/// </summary>
/// <remarks>
/// The commitment alone does not carry <see cref="PublicInputs.L1MessageHash"/> or
/// <see cref="PublicInputs.BlockContextHash"/>. A resolver backed by the canonical witness
/// store or replay path is therefore mandatory; zero-filling either field would silently
/// preserve the retired soft-commitment convention. Proof binding remains the responsibility
/// of <see cref="ProofValidityCheck"/>.
/// </remarks>
public sealed class PublicInputHashConsistencyCheck : IAuditCheck
{
    private readonly Func<L2BatchCommitment, PublicInputs> _resolver;

    /// <summary>Construct with an independent canonical public-inputs resolver.</summary>
    public PublicInputHashConsistencyCheck(Func<L2BatchCommitment, PublicInputs> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _resolver = resolver;
    }

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

            var inputs = _resolver(batch)
                ?? throw new InvalidOperationException(
                    $"publicInputsResolver returned null for batch {batch.BatchNumber}");
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

    private static string Truncate(UInt256 root) => AuditFormatting.Truncate(root);
}
