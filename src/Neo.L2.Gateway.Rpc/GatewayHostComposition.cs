using Neo.L2;
using Neo.L2.Proving.Attestation;
using Neo.L2.Settlement.Rpc;
using Neo.L2.Telemetry;
using Neo.Plugins.L2Gateway;

namespace Neo.L2.Gateway.Rpc;

/// <summary>
/// Gateway host composition root: durable aggregator/outbox + proof-bound publisher +
/// production publication profile from a chain working directory.
/// </summary>
/// <remarks>
/// See doc.md §4 / §14.2. Lives in Gateway.Rpc so <see cref="ProofBoundRpcGlobalRootPublisher"/>
/// can bind without circular plugin dependencies. Terminal proving circuit and replay domain /
/// verification key remain host-supplied; funded L1 confirmation is operator-owned.
/// Optional <see cref="IL2Metrics"/> wires outbox/aggregator telemetry (pair with
/// Multisig/Optimistic/Zk LocalHost metrics). Dispose the composition (gateway then publisher)
/// before reopening durable outbox paths.
/// </remarks>
public sealed class GatewayHostComposition : IDisposable
{
    private bool _disposed;

    private GatewayHostComposition(
        string chainDirectory,
        L2GatewayPlugin gateway,
        ProofBoundRpcGlobalRootPublisher publisher,
        IGatewayProofProver proofProver,
        bool ownsProofProver)
    {
        ChainDirectory = chainDirectory;
        Gateway = gateway;
        Publisher = publisher;
        ProofProver = proofProver;
        OwnsProofProver = ownsProofProver;
    }

    /// <summary>Absolute chain working directory.</summary>
    public string ChainDirectory { get; }

    /// <summary>Gateway plugin with durable outbox and production publication profile.</summary>
    public L2GatewayPlugin Gateway { get; }

    /// <summary>Proof-bound SettlementManager publisher (owns L1 RPC when opened from chain dir).</summary>
    public ProofBoundRpcGlobalRootPublisher Publisher { get; }

    /// <summary>Terminal Gateway proof prover installed on the publication profile.</summary>
    public IGatewayProofProver ProofProver { get; }

    /// <summary>True when this composition created and owns <see cref="ProofProver"/> disposal.</summary>
    public bool OwnsProofProver { get; }

    /// <summary>
    /// Active aggregator installed on the gateway plugin
    /// (<see cref="L2GatewayPlugin.Aggregator"/>).
    /// </summary>
    public IGatewayAggregator Aggregator => Gateway.Aggregator;

    /// <summary>
    /// True when an unconfirmed publication remains retryable or poisoned
    /// (<see cref="L2GatewayPlugin.HasPendingPublication"/>).
    /// </summary>
    public bool HasPendingPublication => Gateway.HasPendingPublication;

    /// <summary>
    /// Pending publication epoch, or null when none awaits confirmation
    /// (<see cref="L2GatewayPlugin.PendingPublicationEpoch"/>).
    /// </summary>
    public ulong? PendingPublicationEpoch => Gateway.PendingPublicationEpoch;

    /// <summary>
    /// Durable outbox / confirmation-lag status
    /// (<see cref="L2GatewayPlugin.OutboxStatus"/>).
    /// </summary>
    public GatewayOutboxStatus OutboxStatus => Gateway.OutboxStatus;

    /// <summary>
    /// Forward a finalized per-chain batch into the durable aggregator/outbox
    /// (<see cref="L2GatewayPlugin.ReceiveBatch"/>).
    /// </summary>
    public void ReceiveBatch(L2BatchCommitment commitment) => Gateway.ReceiveBatch(commitment);

    /// <summary>
    /// Aggregate, prove, publish, and confirm the next Gateway epoch
    /// (<see cref="L2GatewayPlugin.PublishAggregateAsync"/>). Funded L1 confirmation remains
    /// operator-owned.
    /// </summary>
    public ValueTask<UInt256> PublishAggregateAsync(
        ulong batchEpoch,
        CancellationToken cancellationToken = default)
        => Gateway.PublishAggregateAsync(batchEpoch, cancellationToken);

    /// <summary>
    /// Reset a poisoned publication for operator recovery
    /// (<see cref="L2GatewayPlugin.RecoverPoisonedPublication"/>).
    /// </summary>
    public void RecoverPoisonedPublication() => Gateway.RecoverPoisonedPublication();

    /// <summary>
    /// Open Merkle-path Gateway composition: durable Merkle aggregator/outbox + publisher
    /// + <see cref="L2GatewayPlugin.ConfigureGlobalRootPublicationFromChainDirectory"/>.
    /// </summary>
    /// <param name="chainDirectory">Chain root after init-l2 / deploy-report materialization.</param>
    /// <param name="proofProver">Terminal proving circuit matching Merkle backend 0xC1.</param>
    /// <param name="signer">L1 transaction signer for publishGatewayGlobalRoot.</param>
    /// <param name="replayDomain">Non-zero application/network replay domain.</param>
    /// <param name="verificationKeyId">Non-zero verification key id bound on L1.</param>
    /// <param name="options">Optional RPC sender options (network defaults from deployed layout).</param>
    /// <param name="metrics">Optional metrics sink (e.g. Multisig LocalHost <c>Metrics.Metrics</c>).</param>
    public static GatewayHostComposition OpenMerkle(
        string chainDirectory,
        IGatewayProofProver proofProver,
        INeoTransactionSigner signer,
        UInt256 replayDomain,
        UInt256 verificationKeyId,
        RpcTransactionSenderOptions? options = null,
        IL2Metrics? metrics = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainDirectory);
        ArgumentNullException.ThrowIfNull(proofProver);
        ArgumentNullException.ThrowIfNull(signer);
        ArgumentNullException.ThrowIfNull(replayDomain);
        ArgumentNullException.ThrowIfNull(verificationKeyId);
        if (proofProver.AggregationBackendId != MerklePathRoundProver.ConstBackendId)
        {
            throw new ArgumentException(
                $"OpenMerkle requires MerklePathRoundProver backend "
                + $"{MerklePathRoundProver.ConstBackendId}, got {proofProver.AggregationBackendId}",
                nameof(proofProver));
        }

        return Open(
            chainDirectory,
            L2GatewayPlugin.CreateMerkleDurableFromChainDirectory(chainDirectory),
            proofProver,
            ownsProofProver: false,
            signer,
            replayDomain,
            verificationKeyId,
            options,
            metrics);
    }

    /// <summary>
    /// Open Multisig-round Gateway composition: durable Multisig aggregator/outbox +
    /// publisher + publication profile. Terminal <paramref name="proofProver"/> must
    /// match Multisig backend 0xC0.
    /// </summary>
    /// <param name="chainDirectory">Chain root after init-l2 / deploy-report materialization.</param>
    /// <param name="signers">Committee <see cref="ISignerSet"/> for round attestations.</param>
    /// <param name="threshold">Minimum signatures per aggregation round.</param>
    /// <param name="proofProver">Terminal proving circuit matching Multisig backend 0xC0.</param>
    /// <param name="signer">L1 transaction signer for publishGatewayGlobalRoot.</param>
    /// <param name="replayDomain">Non-zero application/network replay domain.</param>
    /// <param name="verificationKeyId">Non-zero verification key id bound on L1.</param>
    /// <param name="options">Optional RPC sender options.</param>
    /// <param name="metrics">Optional metrics sink for outbox/aggregator telemetry.</param>
    public static GatewayHostComposition OpenMultisig(
        string chainDirectory,
        ISignerSet signers,
        int threshold,
        IGatewayProofProver proofProver,
        INeoTransactionSigner signer,
        UInt256 replayDomain,
        UInt256 verificationKeyId,
        RpcTransactionSenderOptions? options = null,
        IL2Metrics? metrics = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainDirectory);
        ArgumentNullException.ThrowIfNull(signers);
        ArgumentNullException.ThrowIfNull(proofProver);
        ArgumentNullException.ThrowIfNull(signer);
        ArgumentNullException.ThrowIfNull(replayDomain);
        ArgumentNullException.ThrowIfNull(verificationKeyId);
        if (proofProver.AggregationBackendId != MultisigRoundProver.ConstBackendId)
        {
            throw new ArgumentException(
                $"OpenMultisig requires MultisigRoundProver backend "
                + $"{MultisigRoundProver.ConstBackendId}, got {proofProver.AggregationBackendId}",
                nameof(proofProver));
        }

        return Open(
            chainDirectory,
            L2GatewayPlugin.CreateMultisigDurableFromChainDirectory(
                chainDirectory, signers, threshold),
            proofProver,
            ownsProofProver: false,
            signer,
            replayDomain,
            verificationKeyId,
            options,
            metrics);
    }

    /// <summary>
    /// Open SP1 recursive Gateway composition: durable SP1 aggregator/outbox +
    /// <see cref="Sp1GatewayProofProver.OpenFromChainDirectory"/> + publisher +
    /// publication profile.
    /// </summary>
    /// <param name="chainDirectory">Chain root after init-l2 / deploy-report materialization.</param>
    /// <param name="gatewayVerificationKey">Locked Gateway guest program verification key.</param>
    /// <param name="signer">L1 transaction signer for publishGatewayGlobalRoot.</param>
    /// <param name="replayDomain">Non-zero application/network replay domain.</param>
    /// <param name="verificationKeyId">
    /// Non-zero L1-bound verification key id (often the same raw key as
    /// <paramref name="gatewayVerificationKey"/>).
    /// </param>
    /// <param name="options">Optional RPC sender options.</param>
    /// <param name="resultTimeout">Optional SP1 result wait timeout.</param>
    /// <param name="pollInterval">Optional SP1 result poll interval.</param>
    /// <param name="metrics">Optional metrics sink for outbox/aggregator telemetry.</param>
    public static GatewayHostComposition OpenSp1(
        string chainDirectory,
        UInt256 gatewayVerificationKey,
        INeoTransactionSigner signer,
        UInt256 replayDomain,
        UInt256 verificationKeyId,
        RpcTransactionSenderOptions? options = null,
        TimeSpan? resultTimeout = null,
        TimeSpan? pollInterval = null,
        IL2Metrics? metrics = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainDirectory);
        ArgumentNullException.ThrowIfNull(gatewayVerificationKey);
        ArgumentNullException.ThrowIfNull(signer);
        ArgumentNullException.ThrowIfNull(replayDomain);
        ArgumentNullException.ThrowIfNull(verificationKeyId);

        var proofProver = Sp1GatewayProofProver.OpenFromChainDirectory(
            chainDirectory,
            gatewayVerificationKey,
            resultTimeout,
            pollInterval);
        return Open(
            chainDirectory,
            L2GatewayPlugin.CreateSp1DurableFromChainDirectory(chainDirectory),
            proofProver,
            ownsProofProver: true,
            signer,
            replayDomain,
            verificationKeyId,
            options,
            metrics);
    }

    private static GatewayHostComposition Open(
        string chainDirectory,
        L2GatewayPlugin gateway,
        IGatewayProofProver proofProver,
        bool ownsProofProver,
        INeoTransactionSigner signer,
        UInt256 replayDomain,
        UInt256 verificationKeyId,
        RpcTransactionSenderOptions? options,
        IL2Metrics? metrics)
    {
        ProofBoundRpcGlobalRootPublisher? publisher = null;
        try
        {
            if (metrics is not null)
                gateway.WithMetrics(metrics);

            publisher = ProofBoundRpcGlobalRootPublisher.OpenFromChainDirectory(
                chainDirectory,
                signer,
                options);
            gateway.ConfigureGlobalRootPublicationFromChainDirectory(
                chainDirectory,
                proofProver,
                publisher,
                replayDomain,
                verificationKeyId);
            return new GatewayHostComposition(
                Path.GetFullPath(chainDirectory),
                gateway,
                publisher,
                proofProver,
                ownsProofProver);
        }
        catch
        {
            gateway.Dispose();
            publisher?.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Gateway.Dispose();
        Publisher.Dispose();
        if (OwnsProofProver && ProofProver is IDisposable disposableProver)
            disposableProver.Dispose();
        GC.SuppressFinalize(this);
    }
}
