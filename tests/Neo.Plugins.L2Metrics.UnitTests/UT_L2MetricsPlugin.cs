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
    public void Plugin_NameAndDescription_AreNonEmpty()
    {
        // Surfaced in plugin host startup logs; pin so a refactor doesn't accidentally
        // empty either. Same convention as UT_L2BridgePlugin / UT_L2GatewayPlugin /
        // UT_L2ProverPlugin.
        using var plugin = new L2MetricsPlugin();
        Assert.IsFalse(string.IsNullOrWhiteSpace(plugin.Name));
        Assert.IsFalse(string.IsNullOrWhiteSpace(plugin.Description));
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
    public async Task Start_WithoutReadinessCheck_ReadyzFailsClosed()
    {
        using var plugin = new L2MetricsPlugin();
        plugin.Start(portOverride: 0);

        using var client = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            Timeout = TimeSpan.FromSeconds(5),
        };
        var response = await client.GetAsync($"http://127.0.0.1:{plugin.BoundPort}/readyz");

        Assert.AreEqual(503, (int)response.StatusCode);
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
    public void CreateFromChainDirectory_LoadsPortAndBindAddress()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-n4-metrics-cfd-" + Guid.NewGuid().ToString("N"));
        var configDir = Path.Combine(dir, "Plugins", "Neo.Plugins.L2Metrics");
        Directory.CreateDirectory(configDir);
        try
        {
            File.WriteAllText(Path.Combine(configDir, "config.json"), """
                {
                  "PluginConfiguration": {
                    "Enabled": true,
                    "BindAddress": "127.0.0.1",
                    "Port": 19191,
                    "MaxConcurrentConnections": 16
                  }
                }
                """);
            using var plugin = L2MetricsPlugin.CreateFromChainDirectory(dir);
            Assert.AreEqual(19191, plugin.Settings.Port);
            Assert.AreEqual("127.0.0.1", plugin.Settings.BindAddress);
            Assert.AreEqual(16, plugin.Settings.MaxConcurrentConnections);
            Assert.AreEqual(16, plugin.MaxConcurrentConnections);
            Assert.IsTrue(plugin.Settings.Enabled);
            Assert.IsTrue(plugin.IsEnabled);
            Assert.AreEqual(19191, plugin.ConfiguredPort);
            Assert.AreEqual("127.0.0.1", plugin.BindAddress);
            Assert.AreEqual(0, plugin.BoundPort);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void CreateFromChainDirectory_MissingConfig_FailsClosed()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-n4-metrics-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.ThrowsExactly<FileNotFoundException>(
                () => L2MetricsPlugin.CreateFromChainDirectory(dir));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void WithReadinessCheck_RejectsNull()
    {
        using var plugin = new L2MetricsPlugin();
        Assert.ThrowsExactly<ArgumentNullException>(() => plugin.WithReadinessCheck(null!));
    }

    [TestMethod]
    public void WithHealthProbe_RejectsNull()
    {
        using var plugin = new L2MetricsPlugin();
        Assert.ThrowsExactly<ArgumentNullException>(() => plugin.WithHealthProbe(null!));
    }

    [TestMethod]
    public void HasReadinessCheck_FalseUntilInstalled()
    {
        using var plugin = new L2MetricsPlugin();
        Assert.IsFalse(plugin.HasReadinessCheck);
        plugin.WithReadinessCheck(static () => true);
        Assert.IsTrue(plugin.HasReadinessCheck);
    }

    [TestMethod]
    public void HasHealthProbe_FalseUntilInstalled()
    {
        using var plugin = new L2MetricsPlugin();
        Assert.IsFalse(plugin.HasHealthProbe);
        plugin.WithHealthProbe(static () => "{}");
        Assert.IsTrue(plugin.HasHealthProbe);
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
    public async Task HealthProbe_ServesJsonWhenWired()
    {
        using var plugin = new L2MetricsPlugin();
        plugin.WithHealthProbe(static () => """{"isLocalHostHealthy":true}""");
        plugin.Start(portOverride: 0);

        using var client = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            Timeout = TimeSpan.FromSeconds(5),
        };
        var resp = await client.GetAsync($"http://127.0.0.1:{plugin.BoundPort}/healthprobe");
        Assert.AreEqual(200, (int)resp.StatusCode);
        Assert.IsTrue(resp.Content.Headers.ContentType?.MediaType is "application/json");
        var body = await resp.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "isLocalHostHealthy");
    }

    [TestMethod]
    public async Task HealthProbe_Unwired_FailsClosedWith503()
    {
        using var plugin = new L2MetricsPlugin();
        plugin.Start(portOverride: 0);

        using var client = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            Timeout = TimeSpan.FromSeconds(5),
        };
        var resp = await client.GetAsync($"http://127.0.0.1:{plugin.BoundPort}/healthprobe");
        Assert.AreEqual(503, (int)resp.StatusCode);
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
    public void Settings_ValidatePort_RejectsOutOfRange()
    {
        // Regression: previously a typo like Port: 90909 would propagate to IPEndPoint
        // at Start() and surface an opaque "must be between 0..65535" deep in the stack.
        // Now: parse-time rejection with a clear error pointing at the config key.
        var ex = Assert.ThrowsExactly<System.IO.InvalidDataException>(() =>
            L2MetricsSettings.ValidatePort(90909));
        StringAssert.Contains(ex.Message, "Port 90909");
    }

    [TestMethod]
    public void Settings_ValidatePort_RejectsNegative()
    {
        var ex = Assert.ThrowsExactly<System.IO.InvalidDataException>(() =>
            L2MetricsSettings.ValidatePort(-1));
        StringAssert.Contains(ex.Message, "Port -1");
    }

    [TestMethod]
    public void Settings_ValidatePort_AcceptsBoundaryValues()
    {
        // 0 (any free port) and 65535 (max) are the inclusive bounds.
        Assert.AreEqual(0, L2MetricsSettings.ValidatePort(0));
        Assert.AreEqual(65535, L2MetricsSettings.ValidatePort(65535));
        Assert.AreEqual(9090, L2MetricsSettings.ValidatePort(9090));
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
