namespace Neo.L2.Telemetry.UnitTests;

/// <summary>
/// Tests for <see cref="MetricsRequestHandler"/> — the pure HTTP handler underlying
/// <see cref="MetricsHttpServer"/>. Verifies routing (200 vs. 404) and the response body
/// format.
/// </summary>
[TestClass]
public class UT_MetricsRequestHandler
{
    [TestMethod]
    public void Handle_MetricsPath_Returns200_WithPrometheusContentType()
    {
        var metrics = new InMemoryMetrics();
        metrics.IncrementCounter(MetricNames.BatchesSealed, 3);
        var handler = new MetricsRequestHandler(metrics);

        var response = handler.Handle("/metrics");

        Assert.AreEqual(200, response.StatusCode);
        Assert.AreEqual(PrometheusExporter.ContentType, response.ContentType);
        StringAssert.Contains(response.Body, "l2_batch_sealed_total 3");
    }

    [TestMethod]
    public void Handle_MetricsPath_WithTrailingSlash_StillRoutes()
    {
        var handler = new MetricsRequestHandler(new InMemoryMetrics());

        var response = handler.Handle("/metrics/");

        Assert.AreEqual(200, response.StatusCode);
    }

    [TestMethod]
    public void Handle_MetricsPath_WithQueryString_StillRoutes()
    {
        var handler = new MetricsRequestHandler(new InMemoryMetrics());

        var response = handler.Handle("/metrics?format=text");

        Assert.AreEqual(200, response.StatusCode);
    }

    [TestMethod]
    public void Handle_OtherPath_Returns404()
    {
        var handler = new MetricsRequestHandler(new InMemoryMetrics());

        Assert.AreEqual(404, handler.Handle("/").StatusCode);
        Assert.AreEqual(404, handler.Handle("/healthz").StatusCode);
        Assert.AreEqual(404, handler.Handle("/metrics/foo").StatusCode);
        Assert.AreEqual(404, handler.Handle("/api/metrics").StatusCode);
    }

    [TestMethod]
    public void Handle_PullsFreshSnapshot_OnEveryCall()
    {
        var metrics = new InMemoryMetrics();
        var handler = new MetricsRequestHandler(metrics);

        var first = handler.Handle("/metrics");
        StringAssert.DoesNotMatch(first.Body, new System.Text.RegularExpressions.Regex("l2_batch_sealed_total"));

        metrics.IncrementCounter(MetricNames.BatchesSealed, 7);
        var second = handler.Handle("/metrics");
        StringAssert.Contains(second.Body, "l2_batch_sealed_total 7");
    }

    [TestMethod]
    public void Handle_Rejects_Null()
    {
        var handler = new MetricsRequestHandler(new InMemoryMetrics());
        Assert.ThrowsExactly<ArgumentNullException>(() => handler.Handle(null!));
    }

    [TestMethod]
    public void Constructor_Rejects_NullSource()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => _ = new MetricsRequestHandler(null!));
    }

    [TestMethod]
    public void InMemoryMetrics_IsAlsoIMetricsSource()
    {
        IMetricsSource source = new InMemoryMetrics();
        var snap = source.Snapshot();
        Assert.IsNotNull(snap);
        Assert.AreEqual(0, snap.TotalEntries);
    }
}
