using Neo.L2;
using Neo.L2.Telemetry;

namespace Neo.Plugins.L2Gateway;

/// <summary>
/// Tiered <see cref="IGatewayAggregator"/> that performs log(N) rounds of pairwise combination
/// over the pending batches. Each round calls the configured <see cref="IRoundProver"/> to fold
/// two child commitments into one parent. The shape is the same whether the round prover is a
/// pass-through hash (default) or a real recursive-ZK backend (production).
/// </summary>
/// <remarks>
/// See doc.md §4 (Neo Gateway).
/// <para>
/// Compared to <see cref="PassThroughAggregator"/>, which produces a flat O(N) proof, this
/// aggregator produces an O(log N)-deep recursive structure that's the right shape for
/// SP1 Compress / Halo2 accumulators / Risc0 STARK folding.
/// </para>
/// <para>
/// The <see cref="AggregatedCommitment.AggregatedProof"/> bytes carry whatever attestation the
/// configured <see cref="IRoundProver"/> emits (e.g. committee signatures for
/// <see cref="MultisigRoundProver"/>, Merkle-path data for <see cref="MerklePathRoundProver"/>).
/// Producing those bytes does not by itself make the published global root trust-minimized: the
/// current on-chain publish path authorizes on the settlement-manager witness and does not
/// verify the aggregated proof. A consumer that wants the attestation to be load-bearing must
/// verify it (via the round prover's verify primitive) before committing the root.
/// </para>
/// </remarks>
public sealed class BinaryTreeAggregator : IGatewayAggregator
{
    private readonly Lock _gate = new();
    private readonly List<L2BatchCommitment> _pending = new();
    private readonly IRoundProver _roundProver;
    private IL2Metrics _metrics;

    /// <summary>The active round prover.</summary>
    public IRoundProver RoundProver => _roundProver;

    /// <summary>Construct with a round prover (default = pass-through).</summary>
    public BinaryTreeAggregator(IRoundProver? roundProver = null, IL2Metrics? metrics = null)
    {
        _roundProver = roundProver ?? new PassThroughRoundProver();
        _metrics = metrics ?? NoOpMetrics.Instance;
    }

    /// <summary>Swap the metrics sink in-place. Preserves pending submissions, unlike re-constructing.</summary>
    public void WithMetrics(IL2Metrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        _metrics = metrics;
    }

    /// <inheritdoc />
    public int PendingCount
    {
        get { lock (_gate) return _pending.Count; }
    }

    /// <inheritdoc />
    public void Submit(L2BatchCommitment commitment)
    {
        ArgumentNullException.ThrowIfNull(commitment);
        lock (_gate) _pending.Add(commitment);
    }

    /// <inheritdoc />
    public AggregatedCommitment? Aggregate()
    {
        L2BatchCommitment[] snapshot;
        lock (_gate)
        {
            if (_pending.Count == 0) return null;
            snapshot = _pending.ToArray();
            _pending.Clear();
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Seed the leaves: each batch contributes its L2->L2 message root + its own proof bytes.
        var current = new RoundResult[snapshot.Length];
        for (var i = 0; i < snapshot.Length; i++)
        {
            current[i] = new RoundResult
            {
                MessageRootContribution = snapshot[i].L2ToL2MessageRoot,
                ProofBytes = snapshot[i].Proof,
            };
        }

        // Pairwise reduce — log(N) rounds. Odd cardinality promotes the trailing leaf unchanged
        // (matches Neo's Merkle convention but without leaf duplication so the proof bytes don't
        // grow on odd levels).
        var rounds = 0;
        while (current.Length > 1)
        {
            var next = new RoundResult[(current.Length + 1) / 2];
            for (var i = 0; i < next.Length; i++)
            {
                var left = current[i * 2];
                var right = (i * 2 + 1 < current.Length) ? current[i * 2 + 1] : null;
                next[i] = _roundProver.Combine(left, right)
                    ?? throw new InvalidOperationException(
                        $"IRoundProver.Combine returned null at round {rounds}, slot {i}");
            }
            current = next;
            rounds++;
        }

        sw.Stop();
        // Safe* wrappers: the aggregation result is computed and must be returned even
        // if the metrics sink throws.
        _metrics.SafeIncrementCounter(MetricNames.GatewayAggregations);
        _metrics.SafeIncrementCounter(MetricNames.GatewayBatchesAggregated, snapshot.Length);
        _metrics.SafeRecordHistogram(MetricNames.GatewayAggregationRounds, rounds);
        _metrics.SafeRecordHistogram(MetricNames.GatewayAggregationLatencyMs, sw.Elapsed.TotalMilliseconds);

        var root = current[0];
        return new AggregatedCommitment
        {
            Constituents = snapshot,
            GlobalMessageRoot = root.MessageRootContribution,
            AggregatedProof = root.ProofBytes,
            BackendId = _roundProver.BackendId,
        };
    }

    /// <summary>Compute the depth (number of rounds) for <paramref name="leafCount"/> leaves. 0 ≤ depth ≤ 31.</summary>
    public static int RoundsFor(int leafCount)
    {
        if (leafCount <= 1) return 0;
        var depth = 0;
        var n = leafCount;
        while (n > 1)
        {
            n = (n + 1) / 2;
            depth++;
        }
        return depth;
    }
}
