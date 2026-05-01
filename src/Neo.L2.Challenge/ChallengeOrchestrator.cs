using Neo.L2.Telemetry;

namespace Neo.L2.Challenge;

/// <summary>
/// Off-chain orchestrator. Given a batch the L2 sequencer just submitted to L1, replay it
/// locally via <see cref="IFraudProofGenerator"/>; if the replayed post-state root disagrees
/// with the batch's claimed one, emit a <see cref="FraudProofPayload"/> the operator can
/// submit to <c>NeoHub.OptimisticChallenge.Challenge</c>.
/// </summary>
public sealed class ChallengeOrchestrator
{
    private readonly IFraudProofGenerator _replayer;
    private readonly IL2Metrics _metrics;

    /// <summary>Construct with the replayer.</summary>
    public ChallengeOrchestrator(IFraudProofGenerator replayer, IL2Metrics? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(replayer);
        _replayer = replayer;
        _metrics = metrics ?? NoOpMetrics.Instance;
    }

    /// <summary>
    /// Inspect a submitted batch + the inputs that produced it. If the replay disagrees,
    /// returns a fraud proof; if it agrees, returns null.
    /// </summary>
    /// <param name="claimedCommitment">The batch the sequencer published on L1.</param>
    /// <param name="inputs">The inputs the challenger reconstructed (txs + L1 messages + ctx).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask<FraudProofPayload?> InspectAsync(
        L2BatchCommitment claimedCommitment,
        BatchExecutionRequest inputs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claimedCommitment);
        ArgumentNullException.ThrowIfNull(inputs);
        if (claimedCommitment.ChainId != inputs.ChainId)
            throw new ArgumentException("commitment.ChainId != inputs.ChainId");
        if (claimedCommitment.BatchNumber != inputs.BatchNumber)
            throw new ArgumentException("commitment.BatchNumber != inputs.BatchNumber");
        if (!claimedCommitment.PreStateRoot.Equals(inputs.PreStateRoot))
            throw new ArgumentException("commitment.PreStateRoot != inputs.PreStateRoot");

        var replayedRoot = await _replayer.ReplayAsync(inputs, cancellationToken).ConfigureAwait(false);
        if (replayedRoot.Equals(claimedCommitment.PostStateRoot))
        {
            // Sequencer's claim matches challenger's replay → no fraud.
            return null;
        }

        var payload = new FraudProofPayload
        {
            PreStateRoot = claimedCommitment.PreStateRoot,
            ClaimedPostStateRoot = claimedCommitment.PostStateRoot,
            ReplayedPostStateRoot = replayedRoot,
            DisputedTxIndex = 0, // MVP: no per-tx narrowing yet.
        };
        _metrics.IncrementCounter(MetricNames.FraudProofsEmitted);
        return payload;
    }

    /// <summary>
    /// Like <see cref="InspectAsync"/>, but the caller already has per-tx checkpoint
    /// sequences for both parties. Runs <see cref="BisectionGame"/> internally and emits a
    /// fraud proof whose <see cref="FraudProofPayload.DisputedTxIndex"/> is the single
    /// narrowed index the verifier needs to re-execute.
    /// </summary>
    /// <param name="claimedCommitment">The batch the sequencer published.</param>
    /// <param name="inputs">Inputs the challenger reconstructed.</param>
    /// <param name="challengerCheckpoints">Length N+1: state root after applying tx[0..i].</param>
    /// <param name="sequencerCheckpoints">Same shape, from the sequencer's view.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Fraud-proof payload with narrowed <c>DisputedTxIndex</c>, or null when no fraud.</returns>
    public ValueTask<FraudProofPayload?> InspectWithBisectionAsync(
        L2BatchCommitment claimedCommitment,
        BatchExecutionRequest inputs,
        UInt256[] challengerCheckpoints,
        UInt256[] sequencerCheckpoints,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claimedCommitment);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(challengerCheckpoints);
        ArgumentNullException.ThrowIfNull(sequencerCheckpoints);
        if (claimedCommitment.ChainId != inputs.ChainId)
            throw new ArgumentException("commitment.ChainId != inputs.ChainId");
        if (claimedCommitment.BatchNumber != inputs.BatchNumber)
            throw new ArgumentException("commitment.BatchNumber != inputs.BatchNumber");
        if (!claimedCommitment.PreStateRoot.Equals(inputs.PreStateRoot))
            throw new ArgumentException("commitment.PreStateRoot != inputs.PreStateRoot");

        // No fraud if checkpoints agree at the end.
        if (challengerCheckpoints[^1].Equals(sequencerCheckpoints[^1]))
            return ValueTask.FromResult<FraudProofPayload?>(null);

        var game = new BisectionGame(challengerCheckpoints, sequencerCheckpoints, _metrics);
        var disputedIndex = game.RunToSettlement();

        var payload = new FraudProofPayload
        {
            PreStateRoot = claimedCommitment.PreStateRoot,
            ClaimedPostStateRoot = claimedCommitment.PostStateRoot,
            ReplayedPostStateRoot = challengerCheckpoints[^1],
            DisputedTxIndex = (uint)disputedIndex,
        };
        _metrics.IncrementCounter(MetricNames.FraudProofsEmitted);
        return ValueTask.FromResult<FraudProofPayload?>(payload);
    }
}
