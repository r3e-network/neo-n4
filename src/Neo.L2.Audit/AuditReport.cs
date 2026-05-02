namespace Neo.L2.Audit;

/// <summary>
/// Outcome of running a chain audit. Aggregates per-check findings + an overall pass/fail.
/// </summary>
public sealed record AuditReport
{
    /// <summary>L2 chain identifier the audit covered.</summary>
    public required uint ChainId { get; init; }

    /// <summary>Inclusive batch number range that was audited.</summary>
    public required ulong FirstBatch { get; init; }

    /// <summary>Inclusive batch number range that was audited.</summary>
    public required ulong LastBatch { get; init; }

    /// <summary>
    /// Overall result: true iff <see cref="Findings"/> is non-empty AND every entry passed.
    /// </summary>
    /// <remarks>
    /// The non-empty guard prevents <c>Findings.All(...)</c>'s vacuous-true on an empty list
    /// — a caller who constructs an <see cref="AuditReport"/> directly (bypassing
    /// <see cref="ChainAuditor"/>) with no findings would otherwise silently look "passed".
    /// <see cref="ChainAuditor"/> already guarantees at least one finding via its
    /// no-checks-registered guard, so this is purely defense for hand-built reports.
    /// </remarks>
    public bool Passed => Findings.Count > 0 && Findings.All(f => f.Passed);

    /// <summary>Per-check findings, in order they were run.</summary>
    public required IReadOnlyList<AuditFinding> Findings { get; init; }

    /// <summary>Render a short, human-readable summary.</summary>
    public string Summarize()
    {
        var head = $"Audit for chain {ChainId}, batches {FirstBatch}..{LastBatch}: {(Passed ? "✅ PASSED" : "❌ FAILED")}";
        var body = string.Join("\n", Findings.Select(f => $"  [{(f.Passed ? "ok" : "FAIL")}] {f.Check}: {f.Detail}"));
        return $"{head}\n{body}";
    }
}

/// <summary>One pass/fail entry produced by a single audit check.</summary>
public sealed record AuditFinding
{
    /// <summary>Identifier of the check that produced this finding.</summary>
    public required string Check { get; init; }

    /// <summary>True if the check passed.</summary>
    public required bool Passed { get; init; }

    /// <summary>Per-batch number this finding applies to (or 0 for chain-wide checks).</summary>
    public required ulong BatchNumber { get; init; }

    /// <summary>One-line human-readable detail.</summary>
    public required string Detail { get; init; }
}
