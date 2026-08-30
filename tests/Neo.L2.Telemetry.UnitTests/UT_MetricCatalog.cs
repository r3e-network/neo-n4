using System.Text.RegularExpressions;
using Neo.L2.TestInfra;

namespace Neo.L2.Telemetry.UnitTests;

/// <summary>
/// Tests for <see cref="MetricCatalog"/> — operator-facing description lookup
/// for every canonical metric.
/// </summary>
[TestClass]
public class UT_MetricCatalog
{
    [TestMethod]
    public void GetHelp_ForEveryCanonicalMetric_ReturnsNonGenericText()
    {
        // Reflect over MetricNames public string constants. Every one must have a catalog entry.
        var nameType = typeof(MetricNames);
        var fields = nameType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string));

        var missing = new List<string>();
        foreach (var f in fields)
        {
            var name = (string)f.GetRawConstantValue()!;
            if (!MetricCatalog.IsKnown(name)) missing.Add(name);
        }

        Assert.AreEqual(0, missing.Count,
            $"MetricCatalog is missing entries for: {string.Join(", ", missing)}. Add them to MetricCatalog.Descriptions.");
    }

    [TestMethod]
    public void Catalog_HasNo_OrphanEntries()
    {
        // Reverse direction: every catalog entry must reference a real MetricNames constant.
        // Catches orphan descriptions that survive a metric rename or removal.
        var nameType = typeof(MetricNames);
        var declared = nameType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet();

        var orphans = MetricCatalog.Descriptions.Keys.Where(k => !declared.Contains(k)).ToList();

        Assert.AreEqual(0, orphans.Count,
            $"MetricCatalog has descriptions for non-existent MetricNames: {string.Join(", ", orphans)}. Either restore the MetricNames constant or remove the catalog entry.");
    }

    [TestMethod]
    public void GetHelp_UnknownName_ReturnsGenericFallback()
    {
        Assert.AreEqual("L2 telemetry metric", MetricCatalog.GetHelp("not.a.real.metric"));
    }

    [TestMethod]
    public void GetHelp_KnownName_ReturnsExpectedDescription()
    {
        StringAssert.Contains(MetricCatalog.GetHelp(MetricNames.BatchesSealed), "sealed");
        StringAssert.Contains(MetricCatalog.GetHelp(MetricNames.DAPublished), "DA");
        StringAssert.Contains(MetricCatalog.GetHelp(MetricNames.AuditFailures), "audit");
    }

    [TestMethod]
    public void GetHelp_Rejects_Null()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => MetricCatalog.GetHelp(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => MetricCatalog.IsKnown(null!));
    }

    [TestMethod]
    public void Descriptions_DoNotEndWithPeriod_PerPrometheusConvention()
    {
        var withPeriod = MetricCatalog.Descriptions
            .Where(kv => kv.Value.EndsWith('.'))
            .Select(kv => kv.Key)
            .ToList();

        Assert.AreEqual(0, withPeriod.Count,
            $"These descriptions end with a period: {string.Join(", ", withPeriod)}");
    }

    [TestMethod]
    public void Descriptions_AreNotBlank()
    {
        // An empty/whitespace description in the catalog would silently produce a
        // useless Prometheus HELP line ("# HELP foo_total " with nothing after).
        var blanks = MetricCatalog.Descriptions
            .Where(kv => string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => kv.Key)
            .ToList();

        Assert.AreEqual(0, blanks.Count,
            $"These descriptions are blank: {string.Join(", ", blanks)}");
    }

    [TestMethod]
    public void PrometheusExporter_UsesCatalogHelp_NotGenericString()
    {
        var m = new InMemoryMetrics();
        m.IncrementCounter(MetricNames.BatchesSealed, 1);

        var output = PrometheusExporter.Format(m.Snapshot());

        // Real description, not the generic placeholder.
        StringAssert.Contains(output, "# HELP l2_batch_sealed_total Number of L2 batches sealed by the local sequencer");
    }

    /// <remarks>
    /// The two reflection guards above walk <see cref="MetricNames"/> constants, so they are
    /// structurally unable to see a call site that skips the registry and passes a literal — which is
    /// how <c>l2.batch.on_block_committed_error</c> shipped undocumented on the L2Batch crash path.
    /// See docs/audit/subsystem-verification-audit-2026-08-30.md §5 V6.
    /// </remarks>
    [TestMethod]
    public void EmissionSites_UseMetricNamesConstants_NotRawLiterals()
    {
        var literalFirstArgument = new Regex(
            @"(Safe)?(IncrementCounter|SetGauge|RecordSummary|Observe)\s*\(\s*[@$]*""",
            RegexOptions.CultureInvariant);

        var offenders = new List<string>();
        foreach (var folder in new[] { "src", "tools" })
        {
            foreach (var file in Directory.EnumerateFiles(
                Path.Combine(RepoRoot.Directory, folder), "*.cs", SearchOption.AllDirectories))
            {
                if (IsBuildOutput(file)) continue;

                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    if (lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                    if (!literalFirstArgument.IsMatch(lines[i])) continue;
                    offenders.Add($"{Path.GetRelativePath(RepoRoot.Directory, file)}:{i + 1}");
                }
            }
        }

        Assert.AreEqual(0, offenders.Count,
            "Metric emitted with a string literal bypasses MetricNames and its catalog guard — " +
            $"declare a constant instead: {string.Join(", ", offenders)}");
    }

    [TestMethod]
    public void PrometheusExporter_BatchErrorCounter_RendersTheSameSeriesAsTheLiteral()
    {
        var m = new InMemoryMetrics();
        m.IncrementCounter(MetricNames.BatchOnBlockCommittedError, 1);

        var output = PrometheusExporter.Format(m.Snapshot());

        StringAssert.Contains(output,
            "# HELP l2_batch_on_block_committed_error_total " +
            "OnBlockCommitted handler runs in L2Batch that threw an exception");
        StringAssert.Contains(output, "l2_batch_on_block_committed_error_total 1");
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
}
