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

    /// <summary>Overall result: true iff every check in <see cref="Findings"/> passed.</summary>
    public bool Passed => Findings.All(f => f.Passed);

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
