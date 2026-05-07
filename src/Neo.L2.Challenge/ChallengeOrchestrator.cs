using Neo.L2.Telemetry;

namespace Neo.L2.Challenge;

/// <summary>
/// Off-chain orchestrator. Given a batch the L2 sequencer just submitted to L1, replay it
/// locally via <see cref="IFraudProofGenerator"/>; if the replayed post-state root disagrees
/// with the batch's claimed one, emit a <see cref="FraudProofPayload"/> the operator can
/// submit to <c>NeoHub.OptimisticChallenge.Challenge</c>.
/// </summary>
/// <remarks>
/// Two entry points:
/// <list type="bullet">
///   <item><see cref="InspectAsync"/> — caller supplies inputs only; orchestrator replays
///     the whole batch and reports a batch-level discrepancy (no per-tx narrowing).
///     Use when bisection inputs aren't available (naive replay-only checkers, audit
///     pipelines that just want a yes/no answer).</item>
///   <item><see cref="InspectWithBisectionAsync"/> — caller supplies per-tx checkpoint
///     sequences for both parties; orchestrator runs <see cref="BisectionGame"/> and
///     reports the single narrowed disputed-tx index. Use this in production whenever
///     the L1 fraud verifier needs to know which transaction to re-execute.</item>
/// </list>
/// </remarks>
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
        // Defense-in-depth: UInt256 fields are reference types, `required` doesn't prevent
        // null. PreStateRoot.Equals(...) NREs if PreStateRoot itself is null. PostStateRoot
        // is dereferenced below for the fraud-proof construction. Same iter-156 pattern.
        ArgumentNullException.ThrowIfNull(claimedCommitment.PreStateRoot);
        ArgumentNullException.ThrowIfNull(claimedCommitment.PostStateRoot);
        ArgumentNullException.ThrowIfNull(inputs.PreStateRoot);
        if (claimedCommitment.ChainId != inputs.ChainId)
            throw new ArgumentException("commitment.ChainId != inputs.ChainId");
        if (claimedCommitment.BatchNumber != inputs.BatchNumber)
            throw new ArgumentException("commitment.BatchNumber != inputs.BatchNumber");
        if (!claimedCommitment.PreStateRoot.Equals(inputs.PreStateRoot))
            throw new ArgumentException("commitment.PreStateRoot != inputs.PreStateRoot");

        var replayedRoot = await _replayer.ReplayAsync(inputs, cancellationToken).ConfigureAwait(false);
        // Defensive: a buggy IFraudProofGenerator that returns null would NRE on
        // .Equals() below with no link to the replayer's contract violation. Surface
        // it as an InvalidOperationException so a custom replayer's bug is obvious.
        if (replayedRoot is null)
            throw new InvalidOperationException("IFraudProofGenerator.ReplayAsync returned null");
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
            // Batch-level entry: caller hasn't supplied per-tx checkpoints, so we can't
            // narrow which transaction caused the divergence. Sentinel value 0 is fine
            // for fraud verifiers that re-execute the entire batch; verifiers that need
            // per-tx narrowing must use InspectWithBisectionAsync (which runs BisectionGame
            // and supplies the real index).
            DisputedTxIndex = 0,
        };
        _metrics.SafeIncrementCounter(MetricNames.FraudProofsEmitted);
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
        // Same iter-171 defensive null-guards as InspectAsync. UInt256 fields are
        // reference type and `required` doesn't prevent null.
        ArgumentNullException.ThrowIfNull(claimedCommitment.PreStateRoot);
        ArgumentNullException.ThrowIfNull(claimedCommitment.PostStateRoot);
        ArgumentNullException.ThrowIfNull(inputs.PreStateRoot);
        if (claimedCommitment.ChainId != inputs.ChainId)
            throw new ArgumentException("commitment.ChainId != inputs.ChainId");
        if (claimedCommitment.BatchNumber != inputs.BatchNumber)
            throw new ArgumentException("commitment.BatchNumber != inputs.BatchNumber");
        if (!claimedCommitment.PreStateRoot.Equals(inputs.PreStateRoot))
            throw new ArgumentException("commitment.PreStateRoot != inputs.PreStateRoot");
        // Validate checkpoint shapes BEFORE the [^1] access below — otherwise an empty
        // array crashes with IndexOutOfRangeException, and mismatched lengths silently
        // compare incompatible last elements (could wrongly return "no fraud" when the
        // arrays happen to coincide at their last index).
        if (challengerCheckpoints.Length != sequencerCheckpoints.Length)
            throw new ArgumentException(
                $"checkpoint arrays must have the same length (challenger={challengerCheckpoints.Length}, sequencer={sequencerCheckpoints.Length})");
        if (challengerCheckpoints.Length < 2)
            throw new ArgumentException("checkpoint arrays must have length ≥ 2 (preState + at least one postState)");

        // Per-entry null guards: BisectionGame combines these via .Equals(); a null
        // entry would NRE deep in the bisection loop with no link to the bad index.
        // Same iter-179 MerkleTree.ComputeRoot per-entry pattern applied here.
        for (var i = 0; i < challengerCheckpoints.Length; i++)
        {
            if (challengerCheckpoints[i] is null)
                throw new ArgumentException(
                    $"challengerCheckpoints[{i}] is null", nameof(challengerCheckpoints));
            if (sequencerCheckpoints[i] is null)
                throw new ArgumentException(
                    $"sequencerCheckpoints[{i}] is null", nameof(sequencerCheckpoints));
        }

        // No fraud if checkpoints agree at the end.
        if (challengerCheckpoints[^1].Equals(sequencerCheckpoints[^1]))
            return ValueTask.FromResult<FraudProofPayload?>(null);

        var game = new BisectionGame(challengerCheckpoints, sequencerCheckpoints, _metrics);
        var disputedIndex = game.RunToSettlement();

        // Embed the disputed-tx bytes as a v2 witness when they fit under the
        // FraudProofPayload cap. A re-execution-capable verifier consumes this
        // directly; a structural verifier (NeoHub.GovernanceFraudVerifier) accepts
        // v2 the same way it accepts v1 — the witness is metadata, not part of
        // the structural decision. If the disputed tx is somehow oversized
        // (chain shipped a >64KB tx) we silently fall back to v1; the dispute
        // can still be filed and arbitrated structurally, and operators with
        // re-execution verifiers needing the witness can fetch it via their
        // own side-channel (e.g. a state-proof RPC against a recent snapshot).
        var disputedTxBytes = ReadOnlyMemory<byte>.Empty;
        if (disputedIndex >= 0 && disputedIndex < inputs.Transactions.Count)
        {
            var candidate = inputs.Transactions[disputedIndex];
            if (candidate.Length > 0 && candidate.Length <= FraudProofPayload.MaxDisputedTxBytes)
                disputedTxBytes = candidate;
        }

        var payload = new FraudProofPayload
        {
            PreStateRoot = claimedCommitment.PreStateRoot,
            ClaimedPostStateRoot = claimedCommitment.PostStateRoot,
            ReplayedPostStateRoot = challengerCheckpoints[^1],
            DisputedTxIndex = (uint)disputedIndex,
            DisputedTxBytes = disputedTxBytes,
        };
        _metrics.SafeIncrementCounter(MetricNames.FraudProofsEmitted);
        return ValueTask.FromResult<FraudProofPayload?>(payload);
    }
}
