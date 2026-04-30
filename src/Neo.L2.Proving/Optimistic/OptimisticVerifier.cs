using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.L2.Batch;

namespace Neo.L2.Proving.Optimistic;

/// <summary>
/// Stage 1 verifier — checks that the optimistic proof payload is well-formed and signed by
/// the registered sequencer key. It does NOT enforce the challenge window itself; that is the
/// job of <c>NeoHub.SettlementManager</c>, which keeps the batch in
/// <see cref="BatchStatus.Challengeable"/> until the window expires.
/// </summary>
/// <remarks>
/// See doc.md §7.5 (Stage 1) and §15.4 (forced inclusion).
/// </remarks>
public sealed class OptimisticVerifier : IL2ProofVerifier
{
    private readonly ECPoint _sequencerKey;

    /// <inheritdoc />
    public ProofType Kind => ProofType.Optimistic;

    /// <summary>Construct with the registered sequencer key.</summary>
    public OptimisticVerifier(ECPoint sequencerKey)
    {
        ArgumentNullException.ThrowIfNull(sequencerKey);
        _sequencerKey = sequencerKey;
    }

    /// <inheritdoc />
    public ValueTask<ProofVerificationResult> VerifyAsync(
        PublicInputs publicInputs,
        ReadOnlyMemory<byte> proof,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(publicInputs);

        OptimisticProofPayload payload;
        try
        {
            payload = OptimisticProofPayload.Decode(proof.Span);
        }
        catch (Exception ex)
        {
            return new ValueTask<ProofVerificationResult>(ProofVerificationResult.Fail($"decode: {ex.Message}"));
        }

        if (payload.SequencerSignature.Length != 64)
            return new ValueTask<ProofVerificationResult>(
                ProofVerificationResult.Fail($"sequencer signature length {payload.SequencerSignature.Length} != 64"));

        var canonicalBytes = BatchSerializer.EncodePublicInputs(publicInputs);
        if (!Crypto.VerifySignature(canonicalBytes, payload.SequencerSignature.Span, _sequencerKey))
            return new ValueTask<ProofVerificationResult>(
                ProofVerificationResult.Fail("sequencer signature verification failed"));

        return new ValueTask<ProofVerificationResult>(ProofVerificationResult.Ok);
    }
}
