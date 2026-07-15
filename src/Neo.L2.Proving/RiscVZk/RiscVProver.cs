using Neo.L2.Batch;

namespace Neo.L2.Proving.RiscVZk;

/// <summary>
/// Stage 2 (ZK) prover — generates a validity proof of <c>ApplyBatch</c>. This class is a
/// thin shell whose concrete implementations live in dedicated backends:
/// </summary>
/// <list type="bullet">
///   <item><description><see cref="Sp1BatchProofProver"/> — production client for the isolated SP1 daemon.</description></item>
///   <item><description><c>Neo.L2.Proving.RiscVZk.MockRiscVProver</c> — used in tests; produces a deterministic placeholder proof.</description></item>
/// </list>
/// <remarks>
/// See doc.md §7.5 (Stage 2) and §8.
/// </remarks>
public abstract class RiscVProverBase : IZkExecutionProver
{
    /// <inheritdoc />
    public ProofType Kind => ProofType.Zk;

    /// <summary>The proof system this prover targets.</summary>
    public abstract ProofSystem ProofSystem { get; }

    /// <inheritdoc />
    public WitnessProofSystem WitnessProofSystem =>
        (WitnessProofSystem)(byte)ProofSystem;

    /// <inheritdoc />
    public abstract UInt256 VerificationKeyId { get; }

    /// <inheritdoc />
    public abstract UInt256 ExecutionSemanticId { get; }

    /// <inheritdoc />
    public abstract bool ProducesCryptographicProof { get; }

    /// <inheritdoc />
    public abstract ValueTask<ProofResult> ProveAsync(
        ProofRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Test-only RISC-V prover that returns a deterministic, opaque "proof" without invoking any
/// real zkVM. Useful for plumbing tests; verification by <see cref="MockRiscVVerifier"/> just
/// checks the format is correct.
/// </summary>
public sealed class MockRiscVProver : RiscVProverBase
{
    private readonly UInt256 _vkId;

    /// <inheritdoc />
    public override ProofSystem ProofSystem => ProofSystem.Sp1;

    /// <inheritdoc />
    public override UInt256 VerificationKeyId => _vkId;

    /// <inheritdoc />
    public override UInt256 ExecutionSemanticId => ExecutionSemanticIds.Sp1LegacyNeoN3GuestV1;

    /// <inheritdoc />
    public override bool ProducesCryptographicProof => false;

    /// <summary>Construct with the verification-key identifier the matching verifier expects.</summary>
    public MockRiscVProver(UInt256 verificationKeyId)
    {
        ArgumentNullException.ThrowIfNull(verificationKeyId);
        _vkId = verificationKeyId;
    }

    /// <inheritdoc />
    public override ValueTask<ProofResult> ProveAsync(ProofRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Kind != ProofType.Zk)
            throw new ArgumentException($"Expected ProofType.Zk, got {request.Kind}", nameof(request));

        var publicInputHash = State.StateRootCalculator.HashPublicInputs(request.PublicInputs);

        // Mock proof: simply <vkId>‖<publicInputHash> so the verifier can confirm shape.
        var proofBytes = new byte[64];
        _vkId.GetSpan().CopyTo(proofBytes.AsSpan(0, 32));
        publicInputHash.GetSpan().CopyTo(proofBytes.AsSpan(32, 32));

        var payload = new RiscVProofPayload
        {
            ProofSystem = ProofSystem,
            ProofBytes = proofBytes,
            VerificationKeyId = _vkId,
        };

        return new ValueTask<ProofResult>(new ProofResult
        {
            Proof = payload.Encode(),
            Kind = ProofType.Zk,
            PublicInputHash = publicInputHash,
        });
    }
}

/// <summary>
/// Test-only verifier paired with <see cref="MockRiscVProver"/>. NOT a real ZK verifier.
/// </summary>
public sealed class MockRiscVVerifier : IL2ProofVerifier
{
    private readonly UInt256 _expectedVkId;

    /// <inheritdoc />
    public ProofType Kind => ProofType.Zk;

    /// <summary>Construct expecting a specific verification-key id.</summary>
    public MockRiscVVerifier(UInt256 expectedVkId)
    {
        ArgumentNullException.ThrowIfNull(expectedVkId);
        _expectedVkId = expectedVkId;
    }

    /// <inheritdoc />
    public ValueTask<ProofVerificationResult> VerifyAsync(
        PublicInputs publicInputs,
        ReadOnlyMemory<byte> proof,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(publicInputs);

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

        if (payload.ProofBytes.Length != 64)
            return new ValueTask<ProofVerificationResult>(ProofVerificationResult.Fail($"mock proof length {payload.ProofBytes.Length} != 64"));

        var inputHash = State.StateRootCalculator.HashPublicInputs(publicInputs);
        var vkSlice = payload.ProofBytes.Slice(0, 32);
        var hashSlice = payload.ProofBytes.Slice(32, 32);
        if (!vkSlice.Span.SequenceEqual(_expectedVkId.GetSpan()))
            return new ValueTask<ProofVerificationResult>(ProofVerificationResult.Fail("mock vk slice mismatch"));
        if (!hashSlice.Span.SequenceEqual(inputHash.GetSpan()))
            return new ValueTask<ProofVerificationResult>(ProofVerificationResult.Fail("mock public-input hash mismatch"));

        return new ValueTask<ProofVerificationResult>(ProofVerificationResult.Ok);
    }
}
