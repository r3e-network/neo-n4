using System.Net.Http;

namespace Neo.Plugins.L2Metrics.UnitTests;

/// <summary>
/// Tests for <see cref="L2MetricsPlugin"/> — the production composition root that hosts
/// the shared <see cref="IL2Metrics"/> sink and the HTTP server. Verifies the plugin
/// successfully boots, exposes a sink other plugins can wire to, and stands up the
/// server when started.
/// </summary>
[TestClass]
public class UT_L2MetricsPlugin
{
    [TestMethod]
    public void Metrics_Property_IsAlwaysNonNull()
    {
        using var plugin = new L2MetricsPlugin();
        Assert.IsNotNull(plugin.Metrics);
    }

    [TestMethod]
    public void BoundPort_IsZero_BeforeStart()
    {
        using var plugin = new L2MetricsPlugin();
        Assert.AreEqual(0, plugin.BoundPort);
    }

    [TestMethod]
    public async Task Start_BindsHttpServer_AndScrapeWorks()
    {
        using var plugin = new L2MetricsPlugin();
        plugin.Start(portOverride: 0); // any free port

        Assert.AreNotEqual(0, plugin.BoundPort);

        plugin.Metrics.IncrementCounter(MetricNames.BatchesSealed, 7);

        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
        var resp = await client.GetAsync($"http://127.0.0.1:{plugin.BoundPort}/metrics");
        var body = await resp.Content.ReadAsStringAsync();

        Assert.AreEqual(200, (int)resp.StatusCode);
        StringAssert.Contains(body, "l2_batch_sealed_total 7");
    }

    [TestMethod]
    public void Start_TwiceIsIdempotent()
    {
        using var plugin = new L2MetricsPlugin();
        plugin.Start(portOverride: 0);
        var first = plugin.BoundPort;
        plugin.Start(portOverride: 0); // no-op
        Assert.AreEqual(first, plugin.BoundPort);
    }

    [TestMethod]
    public void WithReadinessCheck_RejectsNull()
    {
        using var plugin = new L2MetricsPlugin();
        Assert.ThrowsExactly<ArgumentNullException>(() => plugin.WithReadinessCheck(null!));
    }

    [TestMethod]
    public async Task ReadinessCheck_GatesReadyzResponse()
    {
        using var plugin = new L2MetricsPlugin();
        var ready = false;
        plugin.WithReadinessCheck(() => ready);
        plugin.Start(portOverride: 0);

        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
        Assert.AreEqual(503, (int)(await client.GetAsync($"http://127.0.0.1:{plugin.BoundPort}/readyz")).StatusCode);
        ready = true;
        Assert.AreEqual(200, (int)(await client.GetAsync($"http://127.0.0.1:{plugin.BoundPort}/readyz")).StatusCode);
    }

    [TestMethod]
    public void Settings_DefaultValues()
    {
        var s = new L2MetricsSettings();
        Assert.IsTrue(s.Enabled);
        Assert.AreEqual("127.0.0.1", s.BindAddress);
        Assert.AreEqual(9090, s.Port);
    }

    [TestMethod]
    public void ResolveBindAddress_NumericIPv4_Works()
    {
        var ip = L2MetricsPlugin.ResolveBindAddress("127.0.0.1");
        Assert.AreEqual(System.Net.IPAddress.Loopback, ip);
    }

    [TestMethod]
    public void ResolveBindAddress_NumericIPv6_Works()
    {
        var ip = L2MetricsPlugin.ResolveBindAddress("::1");
        Assert.AreEqual(System.Net.IPAddress.IPv6Loopback, ip);
    }

    [TestMethod]
    public void ResolveBindAddress_AnyAddress_Works()
    {
        var ip = L2MetricsPlugin.ResolveBindAddress("0.0.0.0");
        Assert.AreEqual(System.Net.IPAddress.Any, ip);
    }

    [TestMethod]
    public void ResolveBindAddress_LocalhostHostname_ResolvesToLoopback()
    {
        // Regression: this used to throw InvalidOperationException because
        // IPAddress.TryParse rejects hostnames. Now we fall through to DNS.
        var ip = L2MetricsPlugin.ResolveBindAddress("localhost");
        Assert.IsTrue(System.Net.IPAddress.IsLoopback(ip), $"got {ip}, expected a loopback address");
    }

    [TestMethod]
    public void ResolveBindAddress_BogusHostname_Throws()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            L2MetricsPlugin.ResolveBindAddress("does-not-exist.invalid"));
    }

    [TestMethod]
    public void ResolveBindAddress_EmptyString_Throws()
    {
        // Defensive: empty string would otherwise reach Dns.GetHostAddresses, which on
        // Linux returns ALL local interface addresses non-deterministically. Operator
        // gets a server bound to a random interface depending on machine config.
        Assert.ThrowsExactly<ArgumentException>(() =>
            L2MetricsPlugin.ResolveBindAddress(""));
    }

    [TestMethod]
    public void ResolveBindAddress_Null_Throws()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            L2MetricsPlugin.ResolveBindAddress(null!));
    }

    [TestMethod]
    public void Start_ConcurrentCalls_BindOnlyOnce()
    {
        // Without the start lock, two threads could both observe _server == null and
        // try to bind, racing on the port and leaking one of the two servers.
        using var plugin = new L2MetricsPlugin();
        var threadCount = 8;
        var threads = new System.Threading.Thread[threadCount];
        var barrier = new System.Threading.Barrier(threadCount);
        var firstBound = 0;

        for (var i = 0; i < threadCount; i++)
        {
            threads[i] = new System.Threading.Thread(() =>
            {
                barrier.SignalAndWait();
                plugin.Start(portOverride: 0);
            });
            threads[i].Start();
        }
        foreach (var t in threads) t.Join();

        firstBound = plugin.BoundPort;
        Assert.AreNotEqual(0, firstBound, "Start should have bound a real port");

        // After the storm, calling Start again is still a no-op — port stays the same.
        plugin.Start(portOverride: 0);
        Assert.AreEqual(firstBound, plugin.BoundPort);
    }
}
