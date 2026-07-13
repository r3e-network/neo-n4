using Neo.L2.Settlement.Rpc;
using Neo.Plugins.L2Gateway;

namespace Neo.L2.Gateway.Rpc;

/// <summary>Production JSON-RPC publisher for the complete proof-bound MessageRouter ABI.</summary>
/// <remarks>
/// See doc.md §4 (Neo Gateway). Read-only preflight and post-confirmation use
/// <c>invokefunction</c>. The caller-supplied wallet delegate receives every current
/// <c>publishGlobalRoot</c> argument unchanged. Exact already-published state is idempotent;
/// conflicting or unconfirmed state fails closed.
/// </remarks>
public sealed class ProofBoundRpcGlobalRootPublisher : IProofBoundGlobalRootPublisher, IDisposable
{
    /// <summary>Sign, submit, and wait for the complete nine-argument contract invocation.</summary>
    /// <param name="messageRouterHash">Configured NeoHub.MessageRouter contract.</param>
    /// <param name="batchEpoch">Gateway aggregation epoch.</param>
    /// <param name="globalRoot">Aggregated L2-to-L2 message root.</param>
    /// <param name="constituentCommitmentsRoot">Root of canonical constituent commitments.</param>
    /// <param name="constituentCount">Number of canonical constituents.</param>
    /// <param name="aggregationBackendId">Locked Gateway aggregation backend.</param>
    /// <param name="proofSystem">Locked terminal proof-system discriminator.</param>
    /// <param name="verificationKeyId">Locked Gateway guest program verification key.</param>
    /// <param name="replayDomain">Locked application/network replay domain.</param>
    /// <param name="aggregatedProof">Terminal proof bytes.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Confirmed transaction hash.</returns>
    public delegate ValueTask<UInt256> SignAndSendAsync(
        UInt160 messageRouterHash,
        ulong batchEpoch,
        UInt256 globalRoot,
        UInt256 constituentCommitmentsRoot,
        uint constituentCount,
        byte aggregationBackendId,
        byte proofSystem,
        UInt256 verificationKeyId,
        UInt256 replayDomain,
        ReadOnlyMemory<byte> aggregatedProof,
        CancellationToken cancellationToken);

    private readonly JsonRpcClient _rpc;
    private readonly UInt160 _messageRouterHash;
    private readonly SignAndSendAsync _signAndSend;
    private readonly ReconciledGlobalRootPublisher _reconciled;
    private readonly bool _ownsRpc;
    private bool _disposed;

    /// <summary>Construct a complete proof-bound RPC publisher.</summary>
    /// <param name="rpc">L1 Neo JSON-RPC client.</param>
    /// <param name="messageRouterHash">Configured non-zero MessageRouter contract.</param>
    /// <param name="signAndSend">Wallet/HSM transaction builder and confirmation delegate.</param>
    /// <param name="ownsRpc">Whether disposing this publisher also disposes <paramref name="rpc"/>.</param>
    public ProofBoundRpcGlobalRootPublisher(
        JsonRpcClient rpc,
        UInt160 messageRouterHash,
        SignAndSendAsync signAndSend,
        bool ownsRpc = false)
    {
        ArgumentNullException.ThrowIfNull(rpc);
        ArgumentNullException.ThrowIfNull(messageRouterHash);
        ArgumentNullException.ThrowIfNull(signAndSend);
        if (messageRouterHash.Equals(UInt160.Zero))
            throw new ArgumentException("messageRouterHash must be non-zero", nameof(messageRouterHash));

        _rpc = rpc;
        _messageRouterHash = messageRouterHash;
        _signAndSend = signAndSend;
        _ownsRpc = ownsRpc;
        _reconciled = new ReconciledGlobalRootPublisher(QueryPublicationAsync, SubmitAsync);
    }

    /// <inheritdoc />
    public ValueTask<UInt256> PublishGlobalRootAsync(
        GatewayProofBinding binding,
        ReadOnlyMemory<byte> aggregatedProof,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GatewayProofBindingSerializer.Validate(binding);
        if (!binding.MessageRouter.Equals(_messageRouterHash))
            throw new ArgumentException("binding targets a different MessageRouter", nameof(binding));
        if (binding.ProofSystem == Sp1GatewayProofProver.Sp1ProofSystem
            && aggregatedProof.Length != Sp1GatewayProofProver.Groth16ProofSize)
        {
            throw new ArgumentException(
                $"SP1 Gateway proof must be {Sp1GatewayProofProver.Groth16ProofSize} bytes",
                nameof(aggregatedProof));
        }
        return _reconciled.PublishGlobalRootAsync(binding, aggregatedProof, cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsRpc) _rpc.Dispose();
        GC.SuppressFinalize(this);
    }

    private async ValueTask<GatewayRootPublicationObservation?> QueryPublicationAsync(
        UInt160 messageRouter,
        ulong batchEpoch,
        CancellationToken cancellationToken)
    {
        if (!messageRouter.Equals(_messageRouterHash))
            throw new InvalidOperationException("publication query targets a different MessageRouter");
        var rootToken = await RpcContractReader.InvokeReadAsync(
            _rpc,
            _messageRouterHash,
            "getGlobalRoot",
            new object[] { batchEpoch },
            cancellationToken).ConfigureAwait(false);
        var inputToken = await RpcContractReader.InvokeReadAsync(
            _rpc,
            _messageRouterHash,
            "getGlobalRootProofInputHash",
            new object[] { batchEpoch },
            cancellationToken).ConfigureAwait(false);
        var globalRoot = RpcContractReader.ParseUInt256(rootToken);
        var proofInputHash = RpcContractReader.ParseUInt256(inputToken);
        if (globalRoot.Equals(UInt256.Zero) && proofInputHash.Equals(UInt256.Zero)) return null;
        return new GatewayRootPublicationObservation
        {
            GlobalMessageRoot = globalRoot,
            ProofInputHash = proofInputHash,
        };
    }

    private async ValueTask<UInt256> SubmitAsync(
        GatewayRootPublishRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var binding = request.Binding;
        if (!binding.MessageRouter.Equals(_messageRouterHash))
            throw new InvalidOperationException("publication request targets a different MessageRouter");
        return await _signAndSend(
            _messageRouterHash,
            binding.BatchEpoch,
            binding.GlobalMessageRoot,
            binding.ConstituentCommitmentsRoot,
            binding.ConstituentCount,
            binding.AggregationBackendId,
            binding.ProofSystem,
            binding.VerificationKeyId,
            binding.ReplayDomain,
            request.AggregatedProof,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("wallet submitter returned a null transaction hash");
    }
}
