using Neo.L2.Batch;
using Neo.L2.Proving.RiscVZk;
using Neo.L2.State;

namespace Neo.L2.Proving.Sp1;

/// <summary>
/// Production-bound RISC-V prover that calls into <see cref="Sp1Bridge"/>. Falls back to
/// <see cref="MockRiscVProver"/> when the native bridge is not loadable, so the
/// <c>L2SettlementPlugin</c> wiring stays valid in dev environments.
/// </summary>
/// <remarks>
/// See doc.md §7.5 (Stage 2) and SPEC.md (deterministic batch executor). The bridge's
/// concrete proof format is wrapped in <see cref="RiscVProofPayload"/>.
/// </remarks>
public sealed class Sp1RiscVProver : RiscVProverBase
{
    private readonly UInt256 _verificationKeyId;
    private readonly MockRiscVProver _fallback;

    /// <inheritdoc />
    public override ProofSystem ProofSystem => ProofSystem.Sp1;

    /// <summary>True if the native bridge is loadable. False forces the mock fallback path.</summary>
    public bool BridgeAvailable => Sp1Bridge.IsAvailable;

    /// <summary>Construct with the verification-key id the matching verifier expects.</summary>
    public Sp1RiscVProver(UInt256 verificationKeyId)
    {
        // UInt256 is a reference type. Surface a null vk at the ctor instead of letting
        // it propagate to the iter-159 RiscVProofPayload.Encode null-guard later, which
        // would name "VerificationKeyId" but not the producer (this prover).
        ArgumentNullException.ThrowIfNull(verificationKeyId);
        _verificationKeyId = verificationKeyId;
        _fallback = new MockRiscVProver(verificationKeyId);
    }

    /// <inheritdoc />
    public override async ValueTask<ProofResult> ProveAsync(ProofRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Kind != ProofType.Zk)
            throw new ArgumentException($"Expected ProofType.Zk, got {request.Kind}", nameof(request));

        if (!BridgeAvailable)
            return await _fallback.ProveAsync(request, cancellationToken).ConfigureAwait(false);

        // Build the canonical input bytes the bridge expects: PublicInputs encoding +
        // length-prefixed witness bytes. The bridge's bincode v0.1 ABI accepts whatever
        // wrapper neo-zkvm's ProofInput uses; here we treat it as opaque and hand off.
        // Use checked arithmetic so a pathological witness size near int.MaxValue
        // surfaces as an OverflowException naming the sum, not a confusing
        // OverflowException from `new byte[wrappedNeg]` deep in the allocator.
        var publicInputBytes = BatchSerializer.EncodePublicInputs(request.PublicInputs);
        var combinedSize = checked(4 + publicInputBytes.Length + 4 + request.Witness.Length);
        var combined = new byte[combinedSize];
        var span = combined.AsSpan();
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(span.Slice(0, 4), publicInputBytes.Length);
        publicInputBytes.AsSpan().CopyTo(span.Slice(4));
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
            span.Slice(4 + publicInputBytes.Length, 4), request.Witness.Length);
        request.Witness.Span.CopyTo(span.Slice(8 + publicInputBytes.Length));

        var (status, proofBytes) = Sp1Bridge.Prove(combined);
        if (status == Sp1BridgeStatus.NotImplemented)
        {
            // Bridge missing or compiled without the real prover (dev/test) — fall back
            // to the mock so dev flows still work end-to-end.
            return await _fallback.ProveAsync(request, cancellationToken).ConfigureAwait(false);
        }
        if (status != Sp1BridgeStatus.Ok || proofBytes is null)
        {
            // Bridge IS available and ran, but rejected the input or failed proving.
            // Falling back to mock here would silently substitute a trivially-valid proof
            // for a real failure — the downstream verifier would then reject the mock,
            // surfacing only as a confusing "verify rejected" message hours later.
            // Surface the bridge's status directly so the operator can diagnose.
            throw new InvalidOperationException(
                $"SP1 bridge {status} for proof generation — verify input shape, witness, or bridge state");
        }

        var publicInputHash = StateRootCalculator.HashPublicInputs(request.PublicInputs);
        var payload = new RiscVProofPayload
        {
            ProofSystem = ProofSystem.Sp1,
            ProofBytes = proofBytes,
            VerificationKeyId = _verificationKeyId,
        };
        return new ProofResult
        {
            Proof = payload.Encode(),
            Kind = ProofType.Zk,
            PublicInputHash = publicInputHash,
        };
    }
}

/// <summary>Companion verifier for <see cref="Sp1RiscVProver"/>. Same fallback semantics.</summary>
public sealed class Sp1RiscVVerifier : IL2ProofVerifier
{
    private readonly UInt256 _expectedVkId;
    private readonly MockRiscVVerifier _fallback;

    /// <inheritdoc />
    public ProofType Kind => ProofType.Zk;

    /// <summary>Construct expecting a specific verification-key id.</summary>
    public Sp1RiscVVerifier(UInt256 expectedVkId)
    {
        ArgumentNullException.ThrowIfNull(expectedVkId);
        _expectedVkId = expectedVkId;
        _fallback = new MockRiscVVerifier(expectedVkId);
    }

    /// <inheritdoc />
    public ValueTask<ProofVerificationResult> VerifyAsync(
        PublicInputs publicInputs,
        ReadOnlyMemory<byte> proof,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        RiscVProofPayload payload;
        try
        {
            payload = RiscVProofPayload.Decode(proof.Span);
        }
        catch (Exception ex)
        {
            return new ValueTask<ProofVerificationResult>(ProofVerificationResult.Fail($"decode: {ex.Message}"));
        }

        if (!payload.VerificationKeyId.Equals(_expectedVkId))
            return new ValueTask<ProofVerificationResult>(ProofVerificationResult.Fail("vk mismatch"));

        if (!Sp1Bridge.IsAvailable)
            return _fallback.VerifyAsync(publicInputs, proof, cancellationToken);

        var status = Sp1Bridge.Verify(payload.ProofBytes.Span);
        return status switch
        {
            Sp1BridgeStatus.Ok => new ValueTask<ProofVerificationResult>(ProofVerificationResult.Ok),
            Sp1BridgeStatus.NotImplemented => _fallback.VerifyAsync(publicInputs, proof, cancellationToken),
            _ => new ValueTask<ProofVerificationResult>(ProofVerificationResult.Fail($"sp1 verify rejected: {status}")),
        };
    }
}
