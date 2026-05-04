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
        var resp = await client.GetAsync($"http://127.0.0.1:{server.Endpoint.Port}/random");
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
    public async Task Server_ReadyzWhenNotReady_Returns503_WithCorrectReasonPhrase()
    {
        // Regression: previously StatusText didn't include 503 — the readiness-probe
        // failure path returned "HTTP/1.0 503 OK", confusing strict HTTP parsers
        // (status code says error, reason phrase says OK). Now: "503 Service Unavailable".
        var handler = new MetricsRequestHandler(new InMemoryMetrics(), readinessCheck: () => false);

        using var server = new MetricsHttpServer(IPAddress.Loopback, port: 0, handler);
        server.Start();

        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
        var resp = await client.GetAsync($"http://127.0.0.1:{server.Endpoint.Port}/readyz");
        Assert.AreEqual(503, (int)resp.StatusCode);
        Assert.AreEqual("Service Unavailable", resp.ReasonPhrase);
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
    public void Constructor_SecondaryOverload_RejectsNullAddress()
    {
        // The (IPAddress, int port, handler) ctor delegates null-address handling
        // to IPEndPoint via MakeValidatedEndpoint (see comment at MetricsHttpServer.cs:53).
        // Pin the contract so a refactor that replaced IPEndPoint with a different type
        // — or moved the port check before address handling such that PortValidator
        // succeeded but a downstream NRE was the next failure — wouldn't silently
        // change the surface from ArgumentNullException to NRE.
        var handler = new MetricsRequestHandler(new InMemoryMetrics());
        var ex = Assert.ThrowsExactly<ArgumentNullException>(
            () => new MetricsHttpServer((IPAddress)null!, 0, handler));
        Assert.AreEqual("address", ex.ParamName);
    }

    [TestMethod]
    public void Endpoint_BoundToActualPort_WhenConstructedWithPortZero()
    {
        var handler = new MetricsRequestHandler(new InMemoryMetrics());
        using var server = new MetricsHttpServer(IPAddress.Loopback, port: 0, handler);
        Assert.IsTrue(server.Endpoint.Port > 0, "should resolve to a real port");
    }

    [TestMethod]
    public void Server_IsRunning_FollowsLifecycle()
    {
        var handler = new MetricsRequestHandler(new InMemoryMetrics());
        var server = new MetricsHttpServer(IPAddress.Loopback, port: 0, handler);
        Assert.IsFalse(server.IsRunning, "before Start");

        server.Start();
        Assert.IsTrue(server.IsRunning, "after Start");

        server.Dispose();
        Assert.IsFalse(server.IsRunning, "after Dispose");
    }

    [TestMethod]
    public void Server_DisposeWithoutStart_DoesNotThrow()
    {
        // Regression — Dispose must handle the case where Start was never called.
        // (Constructor binds the TcpListener so the port is already taken; we want to
        // make sure we can release it cleanly even if Start was skipped.)
        var handler = new MetricsRequestHandler(new InMemoryMetrics());
        var server = new MetricsHttpServer(IPAddress.Loopback, port: 0, handler);
        server.Dispose(); // no-throw

        // Double-Dispose should also be safe.
        server.Dispose();
    }

    [TestMethod]
    public async Task Server_StartTwiceIsIdempotent()
    {
        var handler = new MetricsRequestHandler(new InMemoryMetrics());
        using var server = new MetricsHttpServer(IPAddress.Loopback, port: 0, handler);
        server.Start();
        server.Start(); // no-op

        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
        var resp = await client.GetAsync($"http://127.0.0.1:{server.Endpoint.Port}/metrics");
        Assert.AreEqual(200, (int)resp.StatusCode);
    }

    [TestMethod]
    public async Task Server_RemainsResponsive_AfterSlowClient_GetsCutOff()
    {
        // Open a TCP connection but never send a request line. The server should drop
        // the connection after its 5-second deadline (we don't wait for the timeout in
        // this test — we just confirm the server still serves a fresh request afterwards).
        var handler = new MetricsRequestHandler(new InMemoryMetrics());
        using var server = new MetricsHttpServer(IPAddress.Loopback, port: 0, handler);
        server.Start();

        using var slow = new System.Net.Sockets.TcpClient();
        await slow.ConnectAsync(IPAddress.Loopback, server.Endpoint.Port);
        // ...silence...

        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
        var resp = await client.GetAsync($"http://127.0.0.1:{server.Endpoint.Port}/metrics");
        Assert.AreEqual(200, (int)resp.StatusCode, "server stays responsive while a slow client is mid-handshake");
    }

    [TestMethod]
    public void Constructor_RejectsOutOfRangePort()
    {
        // Regression for iter 208: previously the (IPAddress, int port, ...) overload
        // delegated port validation to IPEndPoint, which throws a generic "port"
        // ArgumentOutOfRangeException. Now goes through PortValidator.Validate, which
        // surfaces "MetricsHttpServer port {x} out of range".
        var handler = new MetricsRequestHandler(new InMemoryMetrics());
        var ex = Assert.ThrowsExactly<System.IO.InvalidDataException>(
            () => new MetricsHttpServer(IPAddress.Loopback, -1, handler));
        StringAssert.Contains(ex.Message, "MetricsHttpServer port");
        Assert.ThrowsExactly<System.IO.InvalidDataException>(
            () => new MetricsHttpServer(IPAddress.Loopback, 65536, handler));
        // Boundary: 0 (any free) and 65535 must succeed.
        using var s1 = new MetricsHttpServer(IPAddress.Loopback, 0, handler);
        // Skip 65535 — may collide with an in-use port on the test host.
    }
}
