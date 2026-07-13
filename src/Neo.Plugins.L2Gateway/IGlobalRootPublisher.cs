using Neo.L2;

namespace Neo.Plugins.L2Gateway;

/// <summary>
/// Legacy pre-proof-binding publisher retained only for source compatibility and dev/test callers.
/// </summary>
/// <remarks>
/// See doc.md §4 (Neo Gateway). This interface cannot carry the required router, replay domain,
/// constituent root/count, proof-system, and backend binding and is therefore never accepted by
/// <see cref="L2GatewayPlugin.ConfigureGlobalRootPublication"/>. Production uses
/// <see cref="IProofBoundGlobalRootPublisher"/> and <see cref="ReconciledGlobalRootPublisher"/>.
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
/// Legacy no-op publisher for devnets and compatibility tests. It cannot enter production mode.
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
