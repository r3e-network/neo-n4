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

    /// <summary>Counter: withdrawals staged.</summary>
    public const string WithdrawalsStaged = "l2.bridge.withdrawals";

    /// <summary>Counter: cross-chain messages emitted.</summary>
    public const string MessagesEmitted = "l2.messaging.emitted";

    /// <summary>Counter: DA payloads published, tagged by <c>mode</c>.</summary>
    public const string DAPublished = "l2.da.published";

    // ---- Forced inclusion / censorship / challenge ----

    /// <summary>Counter: forced-inclusion entries observed by the local node.</summary>
    public const string ForcedInclusionObserved = "l2.forced_inclusion.observed";

    /// <summary>Counter: censorship reports emitted by the detector.</summary>
    public const string CensorshipReports = "l2.censorship.reports";

    /// <summary>Counter: fraud proofs the orchestrator emitted.</summary>
    public const string FraudProofsEmitted = "l2.challenge.fraud_proofs";

    /// <summary>Histogram: number of bisection rounds to settle a fraud dispute.</summary>
    public const string BisectionRounds = "l2.challenge.bisection_rounds";

    // ---- Audit ----

    /// <summary>Counter: audits run.</summary>
    public const string AuditsRun = "l2.audit.runs";

    /// <summary>Counter: failed audit findings.</summary>
    public const string AuditFailures = "l2.audit.failures";
}
