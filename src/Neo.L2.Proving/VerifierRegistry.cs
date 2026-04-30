namespace Neo.L2.Proving;

/// <summary>
/// In-process dispatcher that routes a <see cref="L2BatchCommitment"/> to the right
/// <see cref="IL2ProofVerifier"/> by <see cref="ProofType"/>. Mirrors the behavior of
/// <c>NeoHub.VerifierRegistry</c> on L1.
/// </summary>
/// <remarks>
/// Used by the L2 settlement plugin to validate batches locally before submitting them, and
/// by tests that want to exercise the same dispatch logic without spinning up an L1 node.
/// </remarks>
public sealed class VerifierRegistry
{
    private readonly Dictionary<ProofType, IL2ProofVerifier> _verifiers = new();

    /// <summary>Register a verifier. Replaces any prior registration for the same kind.</summary>
    public void Register(IL2ProofVerifier verifier)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        _verifiers[verifier.Kind] = verifier;
    }

    /// <summary>True if a verifier is registered for the given kind.</summary>
    public bool IsRegistered(ProofType kind) => _verifiers.ContainsKey(kind);

    /// <summary>Number of registered verifiers.</summary>
    public int Count => _verifiers.Count;

    /// <summary>Dispatch and verify <paramref name="commitment"/> using the registered verifier.</summary>
    public ValueTask<ProofVerificationResult> VerifyAsync(
        L2BatchCommitment commitment,
        PublicInputs publicInputs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commitment);
        ArgumentNullException.ThrowIfNull(publicInputs);

        if (commitment.ProofType == ProofType.None)
            return new ValueTask<ProofVerificationResult>(
                ProofVerificationResult.Fail("ProofType.None requires no verification but is not allowed for finalization"));

        if (!_verifiers.TryGetValue(commitment.ProofType, out var verifier))
            return new ValueTask<ProofVerificationResult>(
                ProofVerificationResult.Fail($"no verifier registered for {commitment.ProofType}"));

        if (commitment.ChainId != publicInputs.ChainId
            || commitment.BatchNumber != publicInputs.BatchNumber
            || !commitment.PreStateRoot.Equals(publicInputs.PreStateRoot)
            || !commitment.PostStateRoot.Equals(publicInputs.PostStateRoot)
            || !commitment.TxRoot.Equals(publicInputs.TxRoot)
            || !commitment.ReceiptRoot.Equals(publicInputs.ReceiptRoot)
            || !commitment.WithdrawalRoot.Equals(publicInputs.WithdrawalRoot)
            || !commitment.L2ToL1MessageRoot.Equals(publicInputs.L2ToL1MessageRoot)
            || !commitment.L2ToL2MessageRoot.Equals(publicInputs.L2ToL2MessageRoot)
            || !commitment.DACommitment.Equals(publicInputs.DACommitment))
        {
            return new ValueTask<ProofVerificationResult>(
                ProofVerificationResult.Fail("commitment ↔ public inputs disagree on at least one field"));
        }

        return verifier.VerifyAsync(publicInputs, commitment.Proof, cancellationToken);
    }
}
