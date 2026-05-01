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
        Assert.AreEqual(404, handler.Handle("/random").StatusCode);
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

    [TestMethod]
    public void Healthz_AlwaysReturns200()
    {
        var handler = new MetricsRequestHandler(new InMemoryMetrics());

        var response = handler.Handle("/healthz");

        Assert.AreEqual(200, response.StatusCode);
        StringAssert.Contains(response.Body, "ok");
    }

    [TestMethod]
    public void Readyz_NoCheck_Returns200()
    {
        var handler = new MetricsRequestHandler(new InMemoryMetrics());

        var response = handler.Handle("/readyz");

        Assert.AreEqual(200, response.StatusCode);
        StringAssert.Contains(response.Body, "ready");
    }

    [TestMethod]
    public void Readyz_CheckReturnsTrue_Returns200()
    {
        var handler = new MetricsRequestHandler(new InMemoryMetrics(), readinessCheck: () => true);

        Assert.AreEqual(200, handler.Handle("/readyz").StatusCode);
    }

    [TestMethod]
    public void Readyz_CheckReturnsFalse_Returns503()
    {
        var handler = new MetricsRequestHandler(new InMemoryMetrics(), readinessCheck: () => false);

        var response = handler.Handle("/readyz");

        Assert.AreEqual(503, response.StatusCode);
        StringAssert.Contains(response.Body, "not ready");
    }

    [TestMethod]
    public void Readyz_PredicateCalled_OnEveryRequest()
    {
        var counter = 0;
        var handler = new MetricsRequestHandler(new InMemoryMetrics(), readinessCheck: () =>
        {
            counter++;
            return counter % 2 == 0; // false, true, false, true, ...
        });

        Assert.AreEqual(503, handler.Handle("/readyz").StatusCode); // counter=1, odd → false
        Assert.AreEqual(200, handler.Handle("/readyz").StatusCode); // counter=2, even → true
        Assert.AreEqual(503, handler.Handle("/readyz").StatusCode); // counter=3, odd → false
    }

    [TestMethod]
    public void Healthz_TolerantOf_TrailingSlashAndQuery()
    {
        var handler = new MetricsRequestHandler(new InMemoryMetrics());

        Assert.AreEqual(200, handler.Handle("/healthz/").StatusCode);
        Assert.AreEqual(200, handler.Handle("/healthz?probe=1").StatusCode);
    }

    [TestMethod]
    public void Readyz_PredicateThrows_Returns503()
    {
        // A buggy or missing-dependency readiness predicate should produce 503, not crash
        // the connection. Operators chase the underlying exception in their logs, not via HTTP.
        var handler = new MetricsRequestHandler(new InMemoryMetrics(),
            readinessCheck: () => throw new InvalidOperationException("dependency unavailable"));

        var response = handler.Handle("/readyz");

        Assert.AreEqual(503, response.StatusCode);
        StringAssert.Contains(response.Body, "predicate threw");
    }
}
