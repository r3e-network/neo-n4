using Neo.L2.Batch;
using Neo.L2.State;

namespace Neo.L2.Proving.Attestation;

/// <summary>
/// Stage 0 prover — collects validator signatures over the canonical public-input encoding
/// and packages them as a <see cref="MultisigProofPayload"/>.
/// </summary>
/// <remarks>
/// See doc.md §7.5 (Stage 0 Attestation proof). This is a real, production-usable prover for
/// the early phases of a Neo L2 deployment, before optimistic challenge windows or ZK
/// validity proofs are wired up.
/// </remarks>
public sealed class AttestationProver : IL2Prover
{
    private readonly ISignerSet _signers;

    /// <inheritdoc />
    public ProofType Kind => ProofType.Multisig;

    /// <summary>Construct with a signer set.</summary>
    public AttestationProver(ISignerSet signers)
    {
        ArgumentNullException.ThrowIfNull(signers);
        _signers = signers;
    }

    /// <inheritdoc />
    public async ValueTask<ProofResult> ProveAsync(ProofRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Kind != ProofType.Multisig)
            throw new ArgumentException($"AttestationProver expects ProofType.Multisig, got {request.Kind}", nameof(request));

        var canonicalBytes = BatchSerializer.EncodePublicInputs(request.PublicInputs);
        var publicInputHash = StateRootCalculator.HashPublicInputs(request.PublicInputs);

        var signatures = await _signers.SignAsync(canonicalBytes, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("ISignerSet.SignAsync returned null");
        var payload = new MultisigProofPayload { Signatures = signatures };

        return new ProofResult
        {
            Proof = payload.Encode(),
            Kind = ProofType.Multisig,
            PublicInputHash = publicInputHash,
        };
    }
}
