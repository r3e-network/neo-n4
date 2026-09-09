using Neo.Extensions.VM;
using Neo.L2;
using Neo.L2.Settlement.Rpc;
using Neo.Plugins.L2Gateway;
using Neo.SmartContract;
using Neo.VM;

namespace Neo.L2.Gateway.Rpc;

/// <summary>Production JSON-RPC publisher for atomic Gateway finality through RollupHub.</summary>
/// <remarks>
/// See doc.md §4 (Neo Gateway). Read-only preflight and post-confirmation use
/// <c>invokefunction</c>. The caller-supplied wallet delegate receives every current
/// RollupHub <c>publishGatewayGlobalRoot</c> argument unchanged. The hub validates
/// the exact finalized constituents, verifies via ZkVerifier, advances non-revertible
/// watermarks, then calls SharedBridge <c>publishMessageRoots</c> atomically.
/// Exact already-published state is idempotent; conflicting or unconfirmed state fails closed.
/// Host composition: <see cref="OpenFromChainDirectory(string,SignAndSendAsync)"/> reads L1 RPC +
/// RollupHub / SharedBridge hashes from settlement plugin config and/or
/// <c>l1.deployed.json</c> (SettlementManager / MessageRouter remain accepted aliases);
/// <see cref="OpenFromChainDirectory(string,INeoTransactionSigner,RpcTransactionSenderOptions?)"/>
/// also builds the canonical <c>publishGatewayGlobalRoot</c> script via
/// <see cref="RpcTransactionSender"/>.
/// Proof-input domain binder is RollupHub (see <see cref="GatewayProofBinding.MessageRouter"/>).
/// </remarks>
public sealed class ProofBoundRpcGlobalRootPublisher : IProofBoundGlobalRootPublisher, IDisposable
{
    /// <summary>Sign, submit, and wait for the complete ten-argument contract invocation.</summary>
    /// <param name="rollupHubHash">Configured NeoHub.RollupHub contract.</param>
    /// <param name="batchEpoch">Gateway aggregation epoch.</param>
    /// <param name="constituentReferences">Packed canonical L2 batch references.</param>
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
        UInt160 rollupHubHash,
        ulong batchEpoch,
        ReadOnlyMemory<byte> constituentReferences,
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
    private readonly UInt160 _rollupHubHash;
    private readonly UInt160 _sharedBridgeHash;
    private readonly SignAndSendAsync _signAndSend;
    private readonly ReconciledGlobalRootPublisher _reconciled;
    private readonly bool _ownsRpc;
    private bool _disposed;

    /// <summary>Construct a complete proof-bound RPC publisher.</summary>
    /// <param name="rpc">L1 Neo JSON-RPC client.</param>
    /// <param name="rollupHubHash">Configured non-zero RollupHub contract.</param>
    /// <param name="sharedBridgeHash">Configured non-zero SharedBridge contract.</param>
    /// <param name="signAndSend">Wallet/HSM transaction builder and confirmation delegate.</param>
    /// <param name="ownsRpc">Whether disposing this publisher also disposes <paramref name="rpc"/>.</param>
    public ProofBoundRpcGlobalRootPublisher(
        JsonRpcClient rpc,
        UInt160 rollupHubHash,
        UInt160 sharedBridgeHash,
        SignAndSendAsync signAndSend,
        bool ownsRpc = false)
    {
        ArgumentNullException.ThrowIfNull(rpc);
        ArgumentNullException.ThrowIfNull(rollupHubHash);
        ArgumentNullException.ThrowIfNull(sharedBridgeHash);
        ArgumentNullException.ThrowIfNull(signAndSend);
        if (rollupHubHash.Equals(UInt160.Zero))
            throw new ArgumentException("rollupHubHash must be non-zero", nameof(rollupHubHash));
        if (sharedBridgeHash.Equals(UInt160.Zero))
            throw new ArgumentException("sharedBridgeHash must be non-zero", nameof(sharedBridgeHash));

        _rpc = rpc;
        _rollupHubHash = rollupHubHash;
        _sharedBridgeHash = sharedBridgeHash;
        _signAndSend = signAndSend;
        _ownsRpc = ownsRpc;
        _reconciled = new ReconciledGlobalRootPublisher(QueryPublicationAsync, SubmitAsync);
    }

    /// <summary>RollupHub contract hash bound at construction (non-zero).</summary>
    public UInt160 RollupHubHash => _rollupHubHash;

    /// <summary>Legacy alias for <see cref="RollupHubHash"/>.</summary>
    public UInt160 SettlementManagerHash => _rollupHubHash;

    /// <summary>SharedBridge contract hash bound at construction (non-zero).</summary>
    public UInt160 SharedBridgeHash => _sharedBridgeHash;

    /// <summary>Deprecated alias for <see cref="SharedBridgeHash"/>.</summary>
    public UInt160 MessageRouterHash => _sharedBridgeHash;

    /// <summary>
    /// Open a publisher from a chain working directory: L1 RPC endpoint and NeoHub hashes from
    /// settlement plugin config and/or <c>l1.deployed.json</c>. Caller supplies
    /// <paramref name="signAndSend"/> (HSM or <see cref="CreateSignAndSend"/>).
    /// </summary>
    public static ProofBoundRpcGlobalRootPublisher OpenFromChainDirectory(
        string chainDirectory,
        SignAndSendAsync signAndSend)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainDirectory);
        ArgumentNullException.ThrowIfNull(signAndSend);
        var endpoints = L1DeployedEndpoints.FromChainDirectory(chainDirectory);
        var rpc = new JsonRpcClient(endpoints.RpcEndpoint.AbsoluteUri);
        return new ProofBoundRpcGlobalRootPublisher(
            rpc,
            endpoints.RollupHub,
            endpoints.SharedBridge,
            signAndSend,
            ownsRpc: true);
    }

    /// <summary>
    /// Open a publisher that signs with <paramref name="signer"/> via
    /// <see cref="RpcTransactionSender"/> and the canonical
    /// <c>RollupHub.publishGatewayGlobalRoot</c> script.
    /// </summary>
    /// <remarks>
    /// Local/testnet: <c>LocalKeyTransactionSigner.FromEnvironmentVariable()</c>.
    /// Production: HSM/KMS <see cref="INeoTransactionSigner"/>. Expected network comes from
    /// settlement config / <c>l1.deployed.json</c> unless overridden in
    /// <paramref name="options"/>.
    /// </remarks>
    public static ProofBoundRpcGlobalRootPublisher OpenFromChainDirectory(
        string chainDirectory,
        INeoTransactionSigner signer,
        RpcTransactionSenderOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainDirectory);
        ArgumentNullException.ThrowIfNull(signer);
        var endpoints = L1DeployedEndpoints.FromChainDirectory(chainDirectory);
        var network = options?.ExpectedNetwork is > 0
            ? options.ExpectedNetwork
            : endpoints.ExpectedNetwork
                ?? throw new InvalidDataException(
                    "ExpectedNetwork is required (settlement plugin config or l1.deployed.json network)");
        var senderOptions = (options ?? new RpcTransactionSenderOptions { ExpectedNetwork = network })
            with
        { ExpectedNetwork = network };
        var rpc = new JsonRpcClient(endpoints.RpcEndpoint.AbsoluteUri);
        var sender = new RpcTransactionSender(rpc, signer, senderOptions);
        var signAndSend = CreateSignAndSend(sender);
        return new ProofBoundRpcGlobalRootPublisher(
            rpc,
            endpoints.RollupHub,
            endpoints.SharedBridge,
            signAndSend,
            ownsRpc: true);
    }

    /// <summary>
    /// Build a <see cref="SignAndSendAsync"/> that emits
    /// <c>RollupHub.publishGatewayGlobalRoot</c> and confirms HALT via
    /// <paramref name="transactionSender"/>.
    /// </summary>
    public static SignAndSendAsync CreateSignAndSend(RpcTransactionSender transactionSender)
    {
        ArgumentNullException.ThrowIfNull(transactionSender);
        return async (
            rollupHubHash,
            batchEpoch,
            constituentReferences,
            globalRoot,
            constituentCommitmentsRoot,
            constituentCount,
            aggregationBackendId,
            proofSystem,
            verificationKeyId,
            replayDomain,
            aggregatedProof,
            cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(rollupHubHash);
            ArgumentNullException.ThrowIfNull(globalRoot);
            ArgumentNullException.ThrowIfNull(constituentCommitmentsRoot);
            ArgumentNullException.ThrowIfNull(verificationKeyId);
            ArgumentNullException.ThrowIfNull(replayDomain);
            if (rollupHubHash.Equals(UInt160.Zero))
                throw new ArgumentException("rollupHubHash must be non-zero", nameof(rollupHubHash));
            if (constituentCount == 0)
                throw new ArgumentOutOfRangeException(nameof(constituentCount), "constituentCount must be positive");
            if (constituentReferences.Length != checked((int)constituentCount * 12))
                throw new ArgumentException(
                    "constituentReferences length must be constituentCount * 12",
                    nameof(constituentReferences));

            using var scriptBuilder = new ScriptBuilder();
            scriptBuilder.EmitDynamicCall(
                rollupHubHash,
                "publishGatewayGlobalRoot",
                CallFlags.All,
                batchEpoch,
                constituentReferences.ToArray(),
                globalRoot,
                constituentCommitmentsRoot,
                constituentCount,
                aggregationBackendId,
                proofSystem,
                verificationKeyId,
                replayDomain,
                aggregatedProof.ToArray());
            var receipt = await transactionSender.SendInvocationAsync(
                scriptBuilder.ToArray(), cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "transaction sender returned a null Gateway publication receipt");
            if (!string.Equals(receipt.VmState, "HALT", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Gateway publication completed with VM state {receipt.VmState}");
            if (receipt.TransactionHash is null || receipt.TransactionHash.Equals(UInt256.Zero))
                throw new InvalidOperationException(
                    "Gateway publication returned a zero transaction hash");
            return receipt.TransactionHash;
        };
    }

    /// <inheritdoc />
    public ValueTask<UInt256> PublishGlobalRootAsync(
        GatewayProofBinding binding,
        AggregatedCommitment commitment,
        ReadOnlyMemory<byte> aggregatedProof,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GatewayProofBindingSerializer.Validate(binding);
        // GatewayProofBinding.MessageRouter is the proof-domain binder (RollupHub executing hash).
        if (!binding.MessageRouter.Equals(_rollupHubHash))
            throw new ArgumentException("binding targets a different RollupHub domain", nameof(binding));
        if (binding.ProofSystem == Sp1GatewayProofProver.Sp1ProofSystem
            && aggregatedProof.Length != Sp1GatewayProofProver.Groth16ProofSize)
        {
            throw new ArgumentException(
                $"SP1 Gateway proof must be {Sp1GatewayProofProver.Groth16ProofSize} bytes",
                nameof(aggregatedProof));
        }
        return _reconciled.PublishGlobalRootAsync(
            binding,
            commitment,
            aggregatedProof,
            cancellationToken);
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
        UInt160 domainBinder,
        ulong batchEpoch,
        CancellationToken cancellationToken)
    {
        if (!domainBinder.Equals(_rollupHubHash))
            throw new InvalidOperationException("publication query targets a different RollupHub");
        var rootToken = await RpcContractReader.InvokeReadAsync(
            _rpc,
            _rollupHubHash,
            "getGlobalRoot",
            new object[] { batchEpoch },
            cancellationToken).ConfigureAwait(false);
        var inputToken = await RpcContractReader.InvokeReadAsync(
            _rpc,
            _rollupHubHash,
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
        if (!binding.MessageRouter.Equals(_rollupHubHash))
            throw new InvalidOperationException("publication request targets a different RollupHub domain");
        return await _signAndSend(
            _rollupHubHash,
            binding.BatchEpoch,
            request.ConstituentReferences,
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
