using System.Net;
using System.Net.Http;

namespace Neo.L2.Telemetry.UnitTests;

/// <summary>
/// Integration test for <see cref="MetricsHttpServer"/> — actually binds a
/// <see cref="System.Net.Sockets.TcpListener"/> on a free localhost port, scrapes
/// <c>/metrics</c> over HTTP, and verifies the round-trip.
/// </summary>
[TestClass]
public class UT_MetricsHttpServer
{
    [TestMethod]
    public async Task Server_RespondsToMetricsScrape_OverRealHttp()
    {
        var metrics = new InMemoryMetrics();
        metrics.IncrementCounter(MetricNames.BatchesSealed, 11);
        var handler = new MetricsRequestHandler(metrics);

        using var server = new MetricsHttpServer(IPAddress.Loopback, port: 0, handler);
        server.Start();

        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
        var resp = await client.GetAsync($"http://127.0.0.1:{server.Endpoint.Port}/metrics");
        var body = await resp.Content.ReadAsStringAsync();

        Assert.AreEqual(200, (int)resp.StatusCode);
        StringAssert.Contains(body, "l2_batch_sealed_total 11");
        StringAssert.Contains(resp.Content.Headers.ContentType?.ToString() ?? "", "text/plain");
    }

    [TestMethod]
    public async Task Server_OtherPath_Returns404()
    {
        var handler = new MetricsRequestHandler(new InMemoryMetrics());

        using var server = new MetricsHttpServer(IPAddress.Loopback, port: 0, handler);
        server.Start();

        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
        var resp = await client.GetAsync($"http://127.0.0.1:{server.Endpoint.Port}/healthz");
        Assert.AreEqual(404, (int)resp.StatusCode);
    }

    [TestMethod]
    public async Task Server_HandlesMultipleSequentialRequests()
    {
        var metrics = new InMemoryMetrics();
        metrics.IncrementCounter(MetricNames.BatchesSealed, 1);
        var handler = new MetricsRequestHandler(metrics);

        using var server = new MetricsHttpServer(IPAddress.Loopback, port: 0, handler);
        server.Start();

        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
        for (var i = 0; i < 5; i++)
        {
            var resp = await client.GetAsync($"http://127.0.0.1:{server.Endpoint.Port}/metrics");
            Assert.AreEqual(200, (int)resp.StatusCode, $"request {i}");
        }
    }

    [TestMethod]
    public void Constructor_Rejects_NullArguments()
    {
        var handler = new MetricsRequestHandler(new InMemoryMetrics());
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            _ = new MetricsHttpServer((IPEndPoint)null!, handler));
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            _ = new MetricsHttpServer(IPAddress.Loopback, 0, null!));
    }

    [TestMethod]
    public void Endpoint_BoundToActualPort_WhenConstructedWithPortZero()
    {
        var handler = new MetricsRequestHandler(new InMemoryMetrics());
        using var server = new MetricsHttpServer(IPAddress.Loopback, port: 0, handler);
        Assert.IsTrue(server.Endpoint.Port > 0, "should resolve to a real port");
    }
}
