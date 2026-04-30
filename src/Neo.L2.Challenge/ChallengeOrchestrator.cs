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

    /// <summary>Construct with the replayer.</summary>
    public ChallengeOrchestrator(IFraudProofGenerator replayer)
    {
        ArgumentNullException.ThrowIfNull(replayer);
        _replayer = replayer;
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

        return new FraudProofPayload
        {
            PreStateRoot = claimedCommitment.PreStateRoot,
            ClaimedPostStateRoot = claimedCommitment.PostStateRoot,
            ReplayedPostStateRoot = replayedRoot,
            DisputedTxIndex = 0, // MVP: no per-tx narrowing yet.
        };
    }
}
