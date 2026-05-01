namespace Neo.L2.Telemetry;

/// <summary>
/// Canonical metric names emitted across the L2 stack. Concentrating them here prevents
/// accidental drift between plugins.
/// </summary>
public static class MetricNames
{
    // ---- Batch / sealing ----

    /// <summary>Counter: number of L2 batches sealed by the local sequencer.</summary>
    public const string BatchesSealed = "l2.batch.sealed";

    /// <summary>Histogram: time (ms) from first block in a batch to seal.</summary>
    public const string BatchSealLatencyMs = "l2.batch.seal_latency_ms";

    /// <summary>Gauge: number of transactions in the most recently sealed batch.</summary>
    public const string BatchTxCount = "l2.batch.tx_count";

    // ---- Settlement ----

    /// <summary>Counter: batches submitted to NeoHub successfully.</summary>
    public const string BatchesSubmitted = "l2.settlement.submitted";

    /// <summary>Counter: batches whose submission failed.</summary>
    public const string SubmitFailures = "l2.settlement.submit_failures";

    /// <summary>Histogram: round-trip ms for a SubmitBatch call.</summary>
    public const string SubmitLatencyMs = "l2.settlement.submit_latency_ms";

    // ---- Proving ----

    /// <summary>Counter: proofs generated, tagged by <c>kind</c>.</summary>
    public const string ProofsGenerated = "l2.proving.generated";

    /// <summary>Histogram: proving wall-clock ms, tagged by <c>kind</c>.</summary>
    public const string ProveLatencyMs = "l2.proving.latency_ms";

    /// <summary>Counter: proofs the local verifier rejected.</summary>
    public const string ProofsRejected = "l2.proving.rejected";

    // ---- Bridge / DA / messaging ----

    /// <summary>Counter: deposits processed (incoming L1 messages).</summary>
    public const string DepositsProcessed = "l2.bridge.deposits";

    /// <summary>Counter: deposits rejected by validation (replay, unknown asset, inactive mapping).</summary>
    public const string DepositsRejected = "l2.bridge.deposits_rejected";

    /// <summary>Counter: withdrawals staged.</summary>
    public const string WithdrawalsStaged = "l2.bridge.withdrawals";

    /// <summary>Counter: withdrawals rejected by validation (unknown asset, duplicate nonce, non-positive amount).</summary>
    public const string WithdrawalsRejected = "l2.bridge.withdrawals_rejected";

    /// <summary>Counter: cross-chain messages emitted.</summary>
    public const string MessagesEmitted = "l2.messaging.emitted";

    /// <summary>Counter: DA payloads published, tagged by <c>mode</c>.</summary>
    public const string DAPublished = "l2.da.published";

    /// <summary>Histogram: DA publish wall-clock ms, tagged by <c>mode</c>.</summary>
    public const string DAPublishLatencyMs = "l2.da.publish_latency_ms";

    /// <summary>Counter: DA publishes that threw, tagged by <c>mode</c>.</summary>
    public const string DAPublishFailures = "l2.da.publish_failures";

    // ---- Sequencer registry ----

    /// <summary>Counter: sequencers registered to the local committee.</summary>
    public const string SequencersRegistered = "l2.sequencer.registered";

    /// <summary>Counter: sequencer exit windows started.</summary>
    public const string SequencerExitsStarted = "l2.sequencer.exits_started";

    /// <summary>Counter: sequencer exits that finalized (removed from committee).</summary>
    public const string SequencerExitsFinalized = "l2.sequencer.exits_finalized";

    /// <summary>Gauge: current committee size (active + exiting members).</summary>
    public const string SequencerCommitteeSize = "l2.sequencer.committee_size";

    // ---- Forced inclusion / censorship / challenge ----

    /// <summary>Counter: forced-inclusion entries observed by the local node.</summary>
    public const string ForcedInclusionObserved = "l2.forced_inclusion.observed";

    /// <summary>Counter: censorship reports emitted by the detector.</summary>
    public const string CensorshipReports = "l2.censorship.reports";

    /// <summary>Counter: fraud proofs the orchestrator emitted.</summary>
    public const string FraudProofsEmitted = "l2.challenge.fraud_proofs";

    /// <summary>Histogram: number of bisection rounds to settle a fraud dispute.</summary>
    public const string BisectionRounds = "l2.challenge.bisection_rounds";

    // ---- Gateway (Phase 5 aggregation) ----

    /// <summary>Counter: aggregations performed by the local gateway.</summary>
    public const string GatewayAggregations = "l2.gateway.aggregations";

    /// <summary>Histogram: rounds (tree depth) per aggregation.</summary>
    public const string GatewayAggregationRounds = "l2.gateway.aggregation_rounds";

    /// <summary>Histogram: aggregation wall-clock ms.</summary>
    public const string GatewayAggregationLatencyMs = "l2.gateway.aggregation_latency_ms";

    /// <summary>Counter: total constituent batches folded into an aggregation (incremented by N each time).</summary>
    public const string GatewayBatchesAggregated = "l2.gateway.batches_aggregated";

    // ---- RPC ----

    /// <summary>Counter: L2 RPC method calls, tagged by <c>method</c>.</summary>
    public const string RpcCalls = "l2.rpc.calls";

    /// <summary>Histogram: L2 RPC method wall-clock ms, tagged by <c>method</c>.</summary>
    public const string RpcLatencyMs = "l2.rpc.latency_ms";

    /// <summary>Counter: L2 RPC method calls that threw, tagged by <c>method</c>.</summary>
    public const string RpcFailures = "l2.rpc.failures";

    // ---- Audit ----

    /// <summary>Counter: audits run.</summary>
    public const string AuditsRun = "l2.audit.runs";

    /// <summary>Counter: failed audit findings.</summary>
    public const string AuditFailures = "l2.audit.failures";
}
