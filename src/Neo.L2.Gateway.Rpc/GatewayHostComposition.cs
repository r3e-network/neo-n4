using Neo.L2.Settlement.Rpc;
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
/// Dispose the composition (gateway then publisher) before reopening durable outbox paths.
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
    /// Open Merkle-path Gateway composition: durable Merkle aggregator/outbox + publisher
    /// + <see cref="L2GatewayPlugin.ConfigureGlobalRootPublicationFromChainDirectory"/>.
    /// </summary>
    /// <param name="chainDirectory">Chain root after init-l2 / deploy-report materialization.</param>
    /// <param name="proofProver">Terminal proving circuit matching Merkle backend 0xC1.</param>
    /// <param name="signer">L1 transaction signer for publishGatewayGlobalRoot.</param>
    /// <param name="replayDomain">Non-zero application/network replay domain.</param>
    /// <param name="verificationKeyId">Non-zero verification key id bound on L1.</param>
    /// <param name="options">Optional RPC sender options (network defaults from deployed layout).</param>
    public static GatewayHostComposition OpenMerkle(
        string chainDirectory,
        IGatewayProofProver proofProver,
        INeoTransactionSigner signer,
        UInt256 replayDomain,
        UInt256 verificationKeyId,
        RpcTransactionSenderOptions? options = null)
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
            options);
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
    public static GatewayHostComposition OpenSp1(
        string chainDirectory,
        UInt256 gatewayVerificationKey,
        INeoTransactionSigner signer,
        UInt256 replayDomain,
        UInt256 verificationKeyId,
        RpcTransactionSenderOptions? options = null,
        TimeSpan? resultTimeout = null,
        TimeSpan? pollInterval = null)
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
            options);
    }

    private static GatewayHostComposition Open(
        string chainDirectory,
        L2GatewayPlugin gateway,
        IGatewayProofProver proofProver,
        bool ownsProofProver,
        INeoTransactionSigner signer,
        UInt256 replayDomain,
        UInt256 verificationKeyId,
        RpcTransactionSenderOptions? options)
    {
        ProofBoundRpcGlobalRootPublisher? publisher = null;
        try
        {
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
