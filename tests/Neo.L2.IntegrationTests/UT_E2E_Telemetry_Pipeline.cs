using System.Net;
using System.Net.Http;
using Neo.L2.Batch;
using Neo.L2.Bridge;
using Neo.L2.Telemetry;
using Neo.Plugins.L2;

namespace Neo.L2.IntegrationTests;

/// <summary>
/// End-to-end smoke test for the telemetry pipeline:
///   <list type="number">
///     <item><description>A single shared <see cref="InMemoryMetrics"/> sink</description></item>
///     <item><description>Drive batch sealing through <see cref="BatchSealer"/> (emits batch counters/histograms)</description></item>
///     <item><description>Drive DA writes through <see cref="MetricsEmittingDAWriter"/> (emits da counters/histograms)</description></item>
///     <item><description>Synthesize bridge + proving counters that production plugins emit</description></item>
///     <item><description>Stand up a <see cref="MetricsHttpServer"/> on a free local port</description></item>
///     <item><description>Scrape <c>/metrics</c> over real HTTP and assert on the resulting Prometheus exposition</description></item>
///   </list>
/// Lock the metric contract end-to-end — if any wiring loosens, this test catches it.
/// </summary>
[TestClass]
public class UT_E2E_Telemetry_Pipeline
{
    [TestMethod]
    public async Task TelemetryPipeline_FromPluginsToScrape_ProducesPrometheusOutput()
    {
        var metrics = new InMemoryMetrics();
        var settings = new L2BatchSettings
        {
            ChainId = 1001,
            MaxBlocksPerBatch = 1,
            MaxTransactionsPerBatch = 100,
            MaxBatchAgeMillis = int.MaxValue,
            Enabled = true,
        };

        var sealer = new BatchSealer(settings, metrics, () => 0L);
        var daWriter = new MetricsEmittingDAWriter(new InMemoryDAWriter(), metrics);

        // Drive 4 batches end-to-end.
        for (var i = 0; i < 4; i++)
        {
            var sealed_ = sealer.OnBlockCommit((uint)(10 + i), 1000UL + (ulong)i, network: 11, RawTxs(2));
            Assert.IsNotNull(sealed_);

            // Each sealed batch publishes its payload to DA.
            var receipt = await daWriter.PublishAsync(new DAPublishRequest
            {
                ChainId = settings.ChainId,
                BatchNumber = sealed_!.BatchNumber,
                Payload = new byte[] { (byte)i, 0xCA, 0xFE },
            });
            Assert.AreEqual(DAMode.External, receipt.Layer);

            // Synthesize the proving + settlement counters that L2SettlementPlugin emits.
            metrics.IncrementCounter(MetricNames.ProofsGenerated, 1, ("kind", "Multisig"));
            metrics.RecordHistogram(MetricNames.ProveLatencyMs, 1.5 + i, ("kind", "Multisig"));
            metrics.IncrementCounter(MetricNames.BatchesSubmitted);

            // Synthesize bridge activity.
            metrics.IncrementCounter(MetricNames.DepositsProcessed);
            metrics.IncrementCounter(MetricNames.WithdrawalsStaged);
        }

        // Stand up the HTTP server and scrape.
        var handler = new MetricsRequestHandler(metrics);
        using var server = new MetricsHttpServer(IPAddress.Loopback, port: 0, handler);
        server.Start();

        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
        var resp = await client.GetAsync($"http://127.0.0.1:{server.Endpoint.Port}/metrics");
        var body = await resp.Content.ReadAsStringAsync();

        Assert.AreEqual(200, (int)resp.StatusCode, "scrape status");

        // Batch metrics
        StringAssert.Contains(body, "l2_batch_sealed_total 4");
        StringAssert.Contains(body, "# TYPE l2_batch_seal_latency_ms summary");
        StringAssert.Contains(body, "l2_batch_seal_latency_ms_count 4");

        // DA metrics (mode-tagged)
        StringAssert.Contains(body, "l2_da_published_total{mode=\"External\"} 4");
        StringAssert.Contains(body, "l2_da_publish_latency_ms_count{mode=\"External\"} 4");

        // Proving (kind-tagged)
        StringAssert.Contains(body, "l2_proving_generated_total{kind=\"Multisig\"} 4");
        StringAssert.Contains(body, "l2_proving_latency_ms_count{kind=\"Multisig\"} 4");

        // Settlement
        StringAssert.Contains(body, "l2_settlement_submitted_total 4");

        // Bridge
        StringAssert.Contains(body, "l2_bridge_deposits_total 4");
        StringAssert.Contains(body, "l2_bridge_withdrawals_total 4");

        // Content-type sanity
        StringAssert.Contains(resp.Content.Headers.ContentType?.ToString() ?? "", "text/plain");
    }

    [TestMethod]
    public async Task TelemetryPipeline_FailedDAWrite_BumpsFailureCounter_NotSuccessCounter()
    {
        var metrics = new InMemoryMetrics();
        var daWriter = new MetricsEmittingDAWriter(new ThrowingDA(DAMode.NeoFS), metrics);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await daWriter.PublishAsync(new DAPublishRequest
            {
                ChainId = 1001,
                BatchNumber = 1,
                Payload = new byte[] { 0x01 },
            }));

        var handler = new MetricsRequestHandler(metrics);
        using var server = new MetricsHttpServer(IPAddress.Loopback, port: 0, handler);
        server.Start();

        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
        var resp = await client.GetAsync($"http://127.0.0.1:{server.Endpoint.Port}/metrics");
        var body = await resp.Content.ReadAsStringAsync();

        StringAssert.Contains(body, "l2_da_publish_failures_total{mode=\"NeoFS\"} 1");
        // Success counter must NOT be present (no entry => no line)
        StringAssert.DoesNotMatch(body, new System.Text.RegularExpressions.Regex(@"l2_da_published_total\{mode=""NeoFS""\}"));
    }

    private static IEnumerable<byte[]> RawTxs(int n)
    {
        for (var i = 0; i < n; i++)
            yield return new byte[] { (byte)i, 0xDE, 0xAD };
    }

    private sealed class ThrowingDA : IDAWriter
    {
        public ThrowingDA(DAMode mode) { Mode = mode; }
        public DAMode Mode { get; }
        public ValueTask<DAReceipt> PublishAsync(DAPublishRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("simulated DA failure");
        public ValueTask<bool> IsAvailableAsync(DAReceipt receipt, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(false);
    }
}
