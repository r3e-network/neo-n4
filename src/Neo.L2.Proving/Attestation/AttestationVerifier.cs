using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.L2.Batch;

namespace Neo.L2.Proving.Attestation;

/// <summary>
/// Stage 0 verifier — checks that ≥ <c>Threshold</c> distinct registered validators signed
/// the canonical public-input encoding.
/// </summary>
/// <remarks>
/// See doc.md §7.5 (Stage 0). This verifier is what NeoHub.VerifierRegistry dispatches to
/// when the L2's <see cref="L2ChainConfig"/> declares Multisig proofs.
/// </remarks>
public sealed class AttestationVerifier : IL2ProofVerifier
{
    private readonly HashSet<ECPoint> _validatorSet;

    /// <inheritdoc />
    public ProofType Kind => ProofType.Multisig;

    /// <summary>Minimum number of distinct validator signatures required.</summary>
    public int Threshold { get; }

    /// <summary>Number of validators in the registered set.</summary>
    public int ValidatorCount => _validatorSet.Count;

    /// <summary>Construct with a validator set and an M-of-N threshold.</summary>
    public AttestationVerifier(IEnumerable<ECPoint> validators, int threshold)
    {
        ArgumentNullException.ThrowIfNull(validators);
        _validatorSet = new HashSet<ECPoint>(validators);
        if (_validatorSet.Count == 0)
            throw new ArgumentException("Validator set must not be empty", nameof(validators));
        if (threshold <= 0 || threshold > _validatorSet.Count)
            throw new ArgumentOutOfRangeException(nameof(threshold),
                $"Threshold {threshold} must be in [1, {_validatorSet.Count}]");
        Threshold = threshold;
    }

    /// <inheritdoc />
    public ValueTask<ProofVerificationResult> VerifyAsync(
        PublicInputs publicInputs,
        ReadOnlyMemory<byte> proof,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(publicInputs);

        MultisigProofPayload payload;
        try
        {
            payload = MultisigProofPayload.Decode(proof.Span);
        }
        catch (Exception ex)
        {
            return new ValueTask<ProofVerificationResult>(ProofVerificationResult.Fail($"decode: {ex.Message}"));
        }

        var canonicalBytes = BatchSerializer.EncodePublicInputs(publicInputs);
        var seen = new HashSet<ECPoint>();
        foreach (var s in payload.Signatures)
        {
            if (!_validatorSet.Contains(s.PublicKey))
                return new ValueTask<ProofVerificationResult>(
                    ProofVerificationResult.Fail($"unknown signer {s.PublicKey}"));
            if (s.Signature.Length != 64)
                return new ValueTask<ProofVerificationResult>(
                    ProofVerificationResult.Fail($"signature length {s.Signature.Length} != 64"));
            if (!Crypto.VerifySignature(canonicalBytes, s.Signature.Span, s.PublicKey))
                return new ValueTask<ProofVerificationResult>(
                    ProofVerificationResult.Fail($"signature verification failed for {s.PublicKey}"));
            if (!seen.Add(s.PublicKey))
                return new ValueTask<ProofVerificationResult>(
                    ProofVerificationResult.Fail($"duplicate signer {s.PublicKey}"));
        }

        if (seen.Count < Threshold)
            return new ValueTask<ProofVerificationResult>(
                ProofVerificationResult.Fail($"{seen.Count} signatures < threshold {Threshold}"));

        return new ValueTask<ProofVerificationResult>(ProofVerificationResult.Ok);
    }
}
