using Neo.L2;

namespace Neo.Plugins.L2Gateway;

/// <summary>
/// Publishes a Phase-5 Neo Gateway aggregated commitment's global message root to
/// <c>NeoHub.MessageRouter.PublishGlobalRoot</c> on L1. This is the off-chain counterpart to the
/// on-chain proof-gated publish path: after the <see cref="BinaryTreeAggregator"/> (or any other
/// <see cref="IGatewayAggregator"/>) produces an <see cref="AggregatedCommitment"/>, the gateway
/// plugin calls this to anchor the global root on L1 — and once a <c>Groth16Verifier</c> is wired
/// on MessageRouter, the publish must clear that proof gate.
/// </summary>
/// <remarks>
/// <para>Framework provides the seam; the operator supplies the L1-wallet-backed implementation
/// (the same division of labor as <c>ISettlementClient</c>, <c>IDAWriter</c>,
/// <c>IForcedInclusionSource</c>). A reference <see cref="NoOpGlobalRootPublisher"/> ships for
/// devnets and tests; production deploys inject an RPC-backed publisher that signs + submits the
/// L1 transaction via <c>L2GatewayPlugin.UseGlobalRootPublisher</c>.</para>
/// <para>The verification key id passed to the publish call is the governance-registered Groth16
/// verifying key under which the <c>AggregatedProof</c> was produced. It is passed through
/// verbatim — the publisher does not interpret it.</para>
/// <para>Idempotency on the epoch is enforced on-chain (publish-once-per-epoch in MessageRouter),
/// so a retried publish for an already-published epoch faults at the contract level rather than
/// double-committing. Implementations SHOULD surface that fault as a benign already-published
/// result, not a hard failure.</para>
/// </remarks>
public interface IGlobalRootPublisher
{
    /// <summary>
    /// Publish <paramref name="commitment"/>'s global message root + aggregated proof to L1.
    /// </summary>
    /// <param name="batchEpoch">Operator-defined epoch number (typically a unix-second window).
    /// On-chain publish-once-per-epoch replay protection keys off this.</param>
    /// <param name="commitment">The aggregated commitment carrying
    /// <see cref="AggregatedCommitment.GlobalMessageRoot"/>,
    /// <see cref="AggregatedCommitment.AggregatedProof"/>, and
    /// <see cref="AggregatedCommitment.BackendId"/>.</param>
    /// <param name="verificationKeyId">32-byte governance-registered VK id matching the proof
    /// backend. Forwarded to <c>MessageRouter.PublishGlobalRoot</c> unchanged.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>The L1 transaction hash that carried the publish, or <see cref="UInt256.Zero"/>
    /// if the epoch was already published (benign retry).</returns>
    ValueTask<UInt256> PublishGlobalRootAsync(
        ulong batchEpoch,
        AggregatedCommitment commitment,
        ReadOnlyMemory<byte> verificationKeyId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default no-op publisher for devnets + tests. Records the last publish attempt so tests can
/// assert the gateway plugin actually invoked it, but performs no L1 transaction. Production
/// deploys replace this via <c>L2GatewayPlugin.UseGlobalRootPublisher</c>.
/// </summary>
public sealed class NoOpGlobalRootPublisher : IGlobalRootPublisher
{
    /// <summary>Number of times <see cref="PublishGlobalRootAsync"/> was called.</summary>
    public int CallCount { get; private set; }

    /// <summary>The last epoch passed to <see cref="PublishGlobalRootAsync"/>, or <c>null</c>.</summary>
    public ulong? LastEpoch { get; private set; }

    /// <summary>The last global root passed to <see cref="PublishGlobalRootAsync"/>, or <c>null</c>.</summary>
    public UInt256? LastGlobalRoot { get; private set; }

    /// <summary>The last backend id passed to <see cref="PublishGlobalRootAsync"/>, or <c>null</c>.</summary>
    public byte? LastBackendId { get; private set; }

    /// <inheritdoc />
    public ValueTask<UInt256> PublishGlobalRootAsync(
        ulong batchEpoch,
        AggregatedCommitment commitment,
        ReadOnlyMemory<byte> verificationKeyId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commitment);
        if (verificationKeyId.Length != 32)
            throw new ArgumentException("verificationKeyId must be 32 bytes", nameof(verificationKeyId));
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        LastEpoch = batchEpoch;
        LastGlobalRoot = commitment.GlobalMessageRoot;
        LastBackendId = commitment.BackendId;
        return ValueTask.FromResult(UInt256.Zero);  // no L1 tx in no-op mode
    }
}
