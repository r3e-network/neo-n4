using Microsoft.Extensions.Configuration;
using Neo.L2;

namespace Neo.Plugins.L2Gateway;

/// <summary>
/// Phase 5 plugin: receives sealed batches from one or more L2 chains via
/// <see cref="ReceiveBatch"/>, aggregates them through the registered
/// <see cref="IGatewayAggregator"/>, and publishes the aggregated commitment's global message
/// root to <c>NeoHub.MessageRouter.PublishGlobalRoot</c> via the registered
/// <see cref="IGlobalRootPublisher"/>.
/// </summary>
/// <remarks>
/// <para>The default <see cref="IGlobalRootPublisher"/> is <see cref="NoOpGlobalRootPublisher"/>
/// (devnet/test — records the call, performs no L1 tx). Production deploys call
/// <see cref="UseGlobalRootPublisher"/> with an RPC-backed publisher whose wallet signs the L1
/// <c>PublishGlobalRoot</c> invocation; once <c>Groth16Verifier</c> is wired on MessageRouter that
/// publish must clear the on-chain proof gate.</para>
/// <para>The operator-configured <see cref="GlobalRootVerificationKeyId"/> selects which
/// governance-registered Groth16 VK the proof is verified against on L1. It is forwarded
/// verbatim to the publisher; the plugin does not interpret it.</para>
/// </remarks>
public sealed class L2GatewayPlugin : Plugin
{
    private volatile IGatewayAggregator _aggregator = new PassThroughAggregator();
    private volatile IGlobalRootPublisher _globalRootPublisher = new NoOpGlobalRootPublisher();
    private ReadOnlyMemory<byte> _globalRootVerificationKeyId = ReadOnlyMemory<byte>.Empty;
    private bool _enabled = true;

    /// <inheritdoc />
    public override string Name => "L2GatewayPlugin";

    /// <inheritdoc />
    public override string Description => "Aggregates per-chain L2 batch commitments into a single Gateway commitment.";

    /// <summary>Replace the active aggregator. This is a configuration-time method;
    /// calling it while <see cref="ReceiveBatch"/> or <see cref="PullAggregate"/> is
    /// in flight on another thread is safe (the write is a single reference swap) but
    /// the old aggregator may receive the in-flight batch.</summary>
    public void UseAggregator(IGatewayAggregator aggregator)
    {
        ArgumentNullException.ThrowIfNull(aggregator);
        _aggregator = aggregator;
    }

    /// <summary>The currently active aggregator.</summary>
    public IGatewayAggregator Aggregator => _aggregator;

    /// <summary>
    /// Replace the global-root publisher (default <see cref="NoOpGlobalRootPublisher"/>). Inject an
    /// RPC-backed publisher for production to sign + submit the L1 <c>PublishGlobalRoot</c>
    /// transaction. Safe to call concurrently with <see cref="PublishAggregateAsync"/> (reference
    /// swap); the in-flight publish may still go through the old publisher.
    /// </summary>
    public void UseGlobalRootPublisher(IGlobalRootPublisher publisher)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        _globalRootPublisher = publisher;
    }

    /// <summary>The currently active global-root publisher.</summary>
    public IGlobalRootPublisher GlobalRootPublisher => _globalRootPublisher;

    /// <summary>
    /// The 32-byte governance-registered Groth16 VK id under which aggregated proofs are verified
    /// on L1. Set via <see cref="SetGlobalRootVerificationKeyId"/> (operator config). Empty until
    /// configured; a production publish with a proof-gated MessageRouter requires this to be set.
    /// </summary>
    public ReadOnlyMemory<byte> GlobalRootVerificationKeyId => _globalRootVerificationKeyId;

    /// <summary>
    /// Set the governance-registered VK id forwarded to the publisher on each
    /// <see cref="PublishAggregateAsync"/>. Must be exactly 32 bytes. Configuration-time method;
    /// the write is a copy so the caller's buffer can be reused.
    /// </summary>
    public void SetGlobalRootVerificationKeyId(ReadOnlyMemory<byte> verificationKeyId)
    {
        if (verificationKeyId.Length != 32)
            throw new ArgumentException("verificationKeyId must be 32 bytes", nameof(verificationKeyId));
        // Defensive copy so a later caller mutation can't retroactively change what we forward.
        var copy = new byte[32];
        verificationKeyId.Span.CopyTo(copy);
        _globalRootVerificationKeyId = copy;
    }

    /// <summary>Forward a per-chain sealed batch into the aggregator.</summary>
    public void ReceiveBatch(L2BatchCommitment commitment)
    {
        // Surface null at the API boundary instead of relying on the aggregator's own
        // guard — same iter-148/183 boundary pattern. A null commitment would otherwise
        // produce a generic ArgumentNullException with no link to the L2GatewayPlugin
        // call site.
        ArgumentNullException.ThrowIfNull(commitment);
        if (!_enabled) return;
        _aggregator.Submit(commitment);
    }

    /// <summary>Produce the next aggregated commitment (or <c>null</c> if nothing is pending).</summary>
    public AggregatedCommitment? PullAggregate() => _aggregator.Aggregate();

    /// <summary>
    /// Drain the aggregator and publish the resulting global message root + aggregated proof to
    /// L1 via the registered <see cref="IGlobalRootPublisher"/>. Returns the L1 transaction hash
    /// (or <see cref="UInt256.Zero"/> for a benign already-published retry / no-op publisher).
    /// Returns <see cref="UInt256.Zero"/> when nothing was pending (no publish attempted).
    /// </summary>
    /// <remarks>
    /// <para>This is the call that connects the off-chain aggregation pipeline to the on-chain
    /// proof-gated global-root publish path: <c>BinaryTreeAggregator.Aggregate</c> →
    /// <see cref="PublishAggregateAsync"/> → <c>MessageRouter.PublishGlobalRoot</c> →
    /// (when verifier wired) <c>Groth16Verifier.verifyZkProof</c>.</para>
    /// <para>The operator supplies the <paramref name="batchEpoch"/> (epoch numbering is
    /// operator-defined; on-chain replay protection keys off it).</para>
    /// </remarks>
    public async ValueTask<UInt256> PublishAggregateAsync(ulong batchEpoch, CancellationToken cancellationToken = default)
    {
        if (!_enabled) return UInt256.Zero;
        var commitment = _aggregator.Aggregate();
        if (commitment is null) return UInt256.Zero;  // nothing pending — no publish

        if (_globalRootVerificationKeyId.Length != 32)
            throw new InvalidOperationException(
                "GlobalRootVerificationKeyId not configured — call SetGlobalRootVerificationKeyId " +
                "with the 32-byte governance-registered VK id before publishing.");

        return await _globalRootPublisher.PublishGlobalRootAsync(
            batchEpoch, commitment, _globalRootVerificationKeyId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override void Configure()
    {
        var section = GetConfiguration();
        _enabled = section.GetValue("Enabled", true);
    }
}

