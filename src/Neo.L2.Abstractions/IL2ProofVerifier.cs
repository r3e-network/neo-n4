namespace Neo.L2;

/// <summary>
/// Pluggable verifier registered in <c>NeoHub.VerifierRegistry</c>. One instance handles a single
/// <see cref="ProofType"/>; <c>VerifierRegistry</c> dispatches by kind.
/// </summary>
/// <remarks>
/// See doc.md §3.2 (VerifierRegistry) and §7.5 (ProverAdapter stages).
/// </remarks>
public interface IL2ProofVerifier
{
    /// <summary>The proof kind this verifier handles.</summary>
    ProofType Kind { get; }

    /// <summary>
    /// Verify <paramref name="proof"/> against <paramref name="publicInputs"/>. Returns
    /// <see cref="ProofVerificationResult.Ok"/> on success or a failure with a reason.
    /// </summary>
    /// <param name="publicInputs">The public-input bundle that was committed to.</param>
    /// <param name="proof">Proof bytes whose interpretation matches <see cref="Kind"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<ProofVerificationResult> VerifyAsync(
        PublicInputs publicInputs,
        ReadOnlyMemory<byte> proof,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Pluggable prover for the L2 sequencer side. Different stages register different implementations.
/// </summary>
/// <remarks>
/// See doc.md §7.5.
/// </remarks>
public interface IL2Prover
{
    /// <summary>The proof kind this prover produces.</summary>
    ProofType Kind { get; }

    /// <summary>
    /// Generate a proof for <paramref name="request"/>. Long-running implementations should
    /// honor <paramref name="cancellationToken"/>.
    /// </summary>
    ValueTask<ProofResult> ProveAsync(
        ProofRequest request,
        CancellationToken cancellationToken = default);
}
