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
}
