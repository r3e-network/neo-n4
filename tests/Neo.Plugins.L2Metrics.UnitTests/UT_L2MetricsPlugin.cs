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
    public async Task Start_WithDefaultSettings_BindsHttpServer_AndScrapeWorks()
    {
        using var plugin = new L2MetricsPlugin();

        // Default port is 9090 which may collide. Reach into settings via a small detour:
        // construct a fresh plugin and immediately swap settings via reflection — actually
        // the plugin's Configure path is the only mutation point. The cleaner test is to
        // just observe Start succeeds OR throws on collision; we use port=0 by placing it
        // in a config file is overkill, so we use the default and detect collision.
        try
        {
            plugin.Start();
        }
        catch (System.Net.Sockets.SocketException)
        {
            Assert.Inconclusive("port 9090 in use on this machine; skip");
            return;
        }

        Assert.AreNotEqual(0, plugin.BoundPort, "server should bind a real port after Start");

        // Emit a metric, scrape, confirm.
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
        try { plugin.Start(); } catch (System.Net.Sockets.SocketException) { Assert.Inconclusive(); return; }
        var first = plugin.BoundPort;
        plugin.Start(); // no-op
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
        try { plugin.Start(); } catch (System.Net.Sockets.SocketException) { Assert.Inconclusive(); return; }

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
}
