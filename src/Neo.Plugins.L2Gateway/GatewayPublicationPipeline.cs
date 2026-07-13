using System.Runtime.ExceptionServices;

namespace Neo.Plugins.L2Gateway;

/// <summary>Creates the terminal proof over a fully-bound Gateway publication statement.</summary>
/// <remarks>
/// See doc.md §4 (Neo Gateway). This prover runs after the target router, replay domain, and epoch
/// are known. Its output must verify against <see cref="GatewayProofBindingSerializer.ComputeHash"/>.
/// </remarks>
public interface IGatewayProofProver
{
    /// <summary>Terminal proof-system discriminator accepted by the on-chain verifier.</summary>
    byte ProofSystem { get; }

    /// <summary>Aggregation backend this proving circuit validates.</summary>
    byte AggregationBackendId { get; }

    /// <summary>Create a non-empty terminal proof for the exact binding and aggregate.</summary>
    ValueTask<ReadOnlyMemory<byte>> ProveAsync(
        GatewayProofBinding binding,
        AggregatedCommitment commitment,
        CancellationToken cancellationToken = default);
}

/// <summary>Delegate-backed test or custom integration adapter for a Gateway proving service.</summary>
/// <remarks>
/// See doc.md §4 (Neo Gateway). This adapter cannot authenticate daemon manifests, proof lengths,
/// public values, or verification keys and is therefore not a production SP1 implementation. Use
/// <see cref="Sp1GatewayProofProver"/> for the fail-closed Gateway SP1 file-queue protocol.
/// </remarks>
public sealed class DelegatingGatewayProofProver : IGatewayProofProver
{
    /// <summary>External proof-service call.</summary>
    public delegate ValueTask<ReadOnlyMemory<byte>> ProofFactory(
        GatewayProofBinding binding,
        AggregatedCommitment commitment,
        CancellationToken cancellationToken);

    private readonly ProofFactory _proofFactory;

    /// <inheritdoc />
    public byte ProofSystem { get; }

    /// <inheritdoc />
    public byte AggregationBackendId { get; }

    /// <summary>Construct a validated proof-service adapter.</summary>
    public DelegatingGatewayProofProver(
        byte proofSystem,
        byte aggregationBackendId,
        ProofFactory proofFactory)
    {
        if (proofSystem is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(proofSystem), "proofSystem must be 1..4");
        if (!GatewayProofBindingSerializer.IsProductionAggregationBackend(aggregationBackendId))
            throw new ArgumentException(
                "pass-through/reserved aggregation backend is not publishable",
                nameof(aggregationBackendId));
        ArgumentNullException.ThrowIfNull(proofFactory);
        ProofSystem = proofSystem;
        AggregationBackendId = aggregationBackendId;
        _proofFactory = proofFactory;
    }

    /// <inheritdoc />
    public ValueTask<ReadOnlyMemory<byte>> ProveAsync(
        GatewayProofBinding binding,
        AggregatedCommitment commitment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(commitment);
        cancellationToken.ThrowIfCancellationRequested();
        if (binding.ProofSystem != ProofSystem)
            throw new ArgumentException("binding proof system does not match prover", nameof(binding));
        if (binding.AggregationBackendId != AggregationBackendId
            || commitment.BackendId != AggregationBackendId)
        {
            throw new ArgumentException("binding aggregate backend does not match prover", nameof(binding));
        }
        var expected = GatewayProofBindingSerializer.Create(
            binding.MessageRouter,
            binding.ReplayDomain,
            binding.BatchEpoch,
            commitment,
            ProofSystem,
            binding.VerificationKeyId);
        if (!GatewayProofBindingSerializer.ComputeHash(expected)
            .Equals(GatewayProofBindingSerializer.ComputeHash(binding)))
        {
            throw new ArgumentException("binding does not match aggregate", nameof(binding));
        }
        return _proofFactory(binding, commitment, cancellationToken);
    }
}

/// <summary>Publishes a fully-bound Gateway statement and terminal proof through SettlementManager.</summary>
/// <remarks>
/// See doc.md §4 (Neo Gateway). Implementations own L1 signing and transport and must forward
/// every binding field and the proof unchanged.
/// </remarks>
public interface IProofBoundGlobalRootPublisher
{
    /// <summary>Sign, submit, reconcile, and confirm a proof-gated publication.</summary>
    ValueTask<UInt256> PublishGlobalRootAsync(
        GatewayProofBinding binding,
        AggregatedCommitment commitment,
        ReadOnlyMemory<byte> aggregatedProof,
        CancellationToken cancellationToken = default);
}

/// <summary>Observed on-chain state for one Gateway epoch.</summary>
public sealed record GatewayRootPublicationObservation
{
    /// <summary>Published global root.</summary>
    public required UInt256 GlobalMessageRoot { get; init; }

    /// <summary>Stored canonical proof-input hash.</summary>
    public required UInt256 ProofInputHash { get; init; }

}

/// <summary>Exact transaction payload forwarded to the wallet-backed submitter.</summary>
public sealed record GatewayRootPublishRequest
{
    /// <summary>Canonical publication statement.</summary>
    public required GatewayProofBinding Binding { get; init; }

    /// <summary>Hash256 of the canonical statement.</summary>
    public required UInt256 ProofInputHash { get; init; }

    /// <summary>
    /// Canonical packed <c>chainId:uint32 LE || batchNumber:uint64 LE</c> references validated by
    /// SettlementManager against its current finalized records.
    /// </summary>
    public required ReadOnlyMemory<byte> ConstituentReferences { get; init; }

    /// <summary>Terminal proof bytes.</summary>
    public required ReadOnlyMemory<byte> AggregatedProof { get; init; }
}

/// <summary>Raised when an epoch is already bound to a different global-root statement.</summary>
public sealed class GatewayPublicationConflictException(string message) : InvalidOperationException(message);

/// <summary>
/// Wallet/transport-neutral production publisher with preflight and post-failure reconciliation.
/// </summary>
/// <remarks>
/// See doc.md §4 (Neo Gateway). The submit delegate must sign, submit, and wait for L1 acceptance.
/// Exact already-published state is a benign idempotent success; conflicting state fails closed.
/// A timeout after L1 acceptance is reconciled by querying MessageRouter before the error escapes.
/// </remarks>
public sealed class ReconciledGlobalRootPublisher : IProofBoundGlobalRootPublisher
{
    /// <summary>Read <c>getGlobalRoot</c> and <c>getGlobalRootProofInputHash</c> for an epoch.</summary>
    public delegate ValueTask<GatewayRootPublicationObservation?> QueryPublicationAsync(
        UInt160 messageRouter,
        ulong batchEpoch,
        CancellationToken cancellationToken);

    /// <summary>Sign, submit, and confirm the exact nine-argument publish invocation.</summary>
    public delegate ValueTask<UInt256> SubmitAndConfirmAsync(
        GatewayRootPublishRequest request,
        CancellationToken cancellationToken);

    private const int MaxAggregatedProofBytes = 1 * 1024 * 1024;
    private readonly QueryPublicationAsync _queryPublication;
    private readonly SubmitAndConfirmAsync _submitAndConfirm;

    /// <summary>Construct a reconciled publisher.</summary>
    public ReconciledGlobalRootPublisher(
        QueryPublicationAsync queryPublication,
        SubmitAndConfirmAsync submitAndConfirm)
    {
        ArgumentNullException.ThrowIfNull(queryPublication);
        ArgumentNullException.ThrowIfNull(submitAndConfirm);
        _queryPublication = queryPublication;
        _submitAndConfirm = submitAndConfirm;
    }

    /// <inheritdoc />
    public async ValueTask<UInt256> PublishGlobalRootAsync(
        GatewayProofBinding binding,
        AggregatedCommitment commitment,
        ReadOnlyMemory<byte> aggregatedProof,
        CancellationToken cancellationToken = default)
    {
        GatewayProofBindingSerializer.Validate(binding);
        ArgumentNullException.ThrowIfNull(commitment);
        var expectedBinding = GatewayProofBindingSerializer.Create(
            binding.MessageRouter,
            binding.ReplayDomain,
            binding.BatchEpoch,
            commitment,
            binding.ProofSystem,
            binding.VerificationKeyId);
        if (!GatewayProofBindingSerializer.ComputeHash(expectedBinding)
            .Equals(GatewayProofBindingSerializer.ComputeHash(binding)))
        {
            throw new ArgumentException("binding does not match aggregate", nameof(binding));
        }
        if (aggregatedProof.IsEmpty)
            throw new ArgumentException("aggregated proof must be non-empty", nameof(aggregatedProof));
        if (aggregatedProof.Length > MaxAggregatedProofBytes)
            throw new ArgumentException("aggregated proof exceeds 1 MiB", nameof(aggregatedProof));

        var proofInputHash = GatewayProofBindingSerializer.ComputeHash(binding);
        var observed = await _queryPublication(
            binding.MessageRouter,
            binding.BatchEpoch,
            cancellationToken).ConfigureAwait(false);
        if (IsExactOrThrow(binding, proofInputHash, observed)) return UInt256.Zero;

        var request = new GatewayRootPublishRequest
        {
            Binding = binding,
            ProofInputHash = proofInputHash,
            ConstituentReferences = GatewayFinalityReferenceSerializer.Encode(commitment.Constituents),
            AggregatedProof = aggregatedProof.ToArray(),
        };

        try
        {
            var transactionHash = await _submitAndConfirm(request, cancellationToken).ConfigureAwait(false);
            if (transactionHash is null)
                throw new InvalidOperationException("submitter returned a null transaction hash");
            observed = await _queryPublication(
                binding.MessageRouter,
                binding.BatchEpoch,
                cancellationToken).ConfigureAwait(false);
            if (!IsExactOrThrow(binding, proofInputHash, observed))
                throw new InvalidOperationException("publisher returned before the global root was confirmed on L1");
            return transactionHash;
        }
        catch (Exception submitError)
        {
            try
            {
                observed = await _queryPublication(
                    binding.MessageRouter,
                    binding.BatchEpoch,
                    CancellationToken.None).ConfigureAwait(false);
                if (IsExactOrThrow(binding, proofInputHash, observed)) return UInt256.Zero;
            }
            catch (GatewayPublicationConflictException)
            {
                throw;
            }
            catch
            {
            }

            ExceptionDispatchInfo.Capture(submitError).Throw();
            throw;
        }
    }

    private static bool IsExactOrThrow(
        GatewayProofBinding binding,
        UInt256 proofInputHash,
        GatewayRootPublicationObservation? observed)
    {
        if (observed is null) return false;
        ArgumentNullException.ThrowIfNull(observed.GlobalMessageRoot);
        ArgumentNullException.ThrowIfNull(observed.ProofInputHash);
        if (observed.GlobalMessageRoot.Equals(binding.GlobalMessageRoot)
            && observed.ProofInputHash.Equals(proofInputHash))
        {
            return true;
        }
        throw new GatewayPublicationConflictException(
            $"epoch {binding.BatchEpoch} is already bound to a different Gateway statement");
    }
}
