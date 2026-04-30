namespace Neo.L2.Telemetry;

/// <summary>
/// Operator-facing description for every canonical metric the L2 stack emits. Read by
/// <see cref="PrometheusExporter"/> to populate <c># HELP</c> lines so a Prometheus
/// dashboard tooltip says something useful rather than "L2 telemetry counter".
/// </summary>
/// <remarks>
/// Single source of truth — keep one entry here per <see cref="MetricNames"/> constant.
/// Descriptions should be a single sentence, not end with a period (Prometheus convention).
/// </remarks>
public static class MetricCatalog
{
    /// <summary>Description for <paramref name="metricName"/>, or a generic fallback if unknown.</summary>
    public static string GetHelp(string metricName)
    {
        ArgumentNullException.ThrowIfNull(metricName);
        return Descriptions.TryGetValue(metricName, out var help) ? help : "L2 telemetry metric";
    }

    /// <summary>Whether <paramref name="metricName"/> has an explicit catalog entry.</summary>
    public static bool IsKnown(string metricName)
    {
        ArgumentNullException.ThrowIfNull(metricName);
        return Descriptions.ContainsKey(metricName);
    }

    /// <summary>All known metric names + their descriptions.</summary>
    public static IReadOnlyDictionary<string, string> Descriptions { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Batch
        [MetricNames.BatchesSealed]        = "Number of L2 batches sealed by the local sequencer",
        [MetricNames.BatchSealLatencyMs]   = "Wall-clock milliseconds spent sealing each batch",
        [MetricNames.BatchTxCount]         = "Transactions in the most recently sealed batch",

        // Settlement
        [MetricNames.BatchesSubmitted]     = "Batches submitted to NeoHub successfully",
        [MetricNames.SubmitFailures]       = "Batch submissions that threw and were re-queued",
        [MetricNames.SubmitLatencyMs]      = "Round-trip wall-clock milliseconds for SubmitBatch",

        // Proving
        [MetricNames.ProofsGenerated]      = "Proofs generated for sealed batches, tagged by proof kind",
        [MetricNames.ProveLatencyMs]       = "Wall-clock milliseconds spent generating each proof, tagged by proof kind",
        [MetricNames.ProofsRejected]       = "Proofs the local verifier rejected before submission",

        // Bridge / DA / messaging
        [MetricNames.DepositsProcessed]    = "Deposits processed from the L1 bridge inbox",
        [MetricNames.WithdrawalsStaged]    = "Withdrawals staged into the L2 outbox",
        [MetricNames.MessagesEmitted]      = "Cross-chain messages emitted from this L2",
        [MetricNames.DAPublished]          = "DA payloads published successfully, tagged by DA mode",
        [MetricNames.DAPublishLatencyMs]   = "Wall-clock milliseconds for each DA publish, tagged by DA mode",
        [MetricNames.DAPublishFailures]    = "DA publishes that threw, tagged by DA mode",

        // Forced inclusion / censorship / challenge
        [MetricNames.ForcedInclusionObserved] = "Forced-inclusion entries observed by this node",
        [MetricNames.CensorshipReports]    = "Censorship reports the detector emitted",
        [MetricNames.FraudProofsEmitted]   = "Fraud proofs the orchestrator emitted",
        [MetricNames.BisectionRounds]      = "Bisection rounds taken to settle each fraud dispute",

        // Audit
        [MetricNames.AuditsRun]            = "Times the chain auditor ran",
        [MetricNames.AuditFailures]        = "Audit findings that failed the audit",
    };
}
