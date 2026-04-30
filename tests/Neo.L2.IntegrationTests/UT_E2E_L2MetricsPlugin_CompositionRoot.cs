using System.Net.Http;
using Neo.Json;
using Neo.L2.Batch;
using Neo.L2.Bridge;
using Neo.L2.Telemetry;
using Neo.Plugins.L2;
using Neo.Plugins.L2Rpc;

namespace Neo.L2.IntegrationTests;

/// <summary>
/// End-to-end test for the <see cref="L2MetricsPlugin"/> composition root: wires every
/// instrumented component (BatchSealer, MetricsEmittingDAWriter, DepositProcessor,
/// WithdrawalProcessor, BinaryTreeAggregator, L2RpcMethods) to one shared sink hosted by
/// the metrics plugin, scrapes <c>/metrics</c> through the plugin's HTTP server, and
/// asserts every component's metric family appears in the exposition.
/// </summary>
[TestClass]
public class UT_E2E_L2MetricsPlugin_CompositionRoot
{
    [TestMethod]
    public async Task CompositionRoot_AllPluginsShareSink_AndScrapeReturnsEverything()
    {
        // 1. Composition root.
        using var metricsPlugin = new L2MetricsPlugin();

        // 2. Each instrumented component pulls metricsPlugin.Metrics.
        var sealer = new BatchSealer(
            new L2BatchSettings { ChainId = 1001, MaxBlocksPerBatch = 1, MaxTransactionsPerBatch = 100, MaxBatchAgeMillis = int.MaxValue, Enabled = true },
            metricsPlugin.Metrics,
            () => 0L);

        var daWriter = new MetricsEmittingDAWriter(new InMemoryDAWriter(), metricsPlugin.Metrics);

        var registry = new AssetRegistry();
        registry.Register(new AssetMapping
        {
            L1Asset = UInt160.Parse("0x" + new string('1', 40)),
            L2ChainId = 1001,
            L2Asset = UInt160.Parse("0x" + new string('2', 40)),
            AssetType = AssetType.Gas, MintBurn = true, LockMint = true, Active = true,
        });
        var deposits = new DepositProcessor(1001, registry, metricsPlugin.Metrics);
        var withdrawals = new WithdrawalProcessor(1001, registry, metricsPlugin.Metrics);

        var aggregator = new Plugins.L2Gateway.BinaryTreeAggregator(metrics: metricsPlugin.Metrics);

        var store = new InMemoryL2RpcStore(1001, SecurityLevel.Optimistic);
        var rpc = new L2RpcMethods(store, metricsPlugin.Metrics);

        // 3. Drive activity across every component.
        for (var i = 0; i < 3; i++)
        {
            var sealed_ = sealer.OnBlockCommit((uint)(10 + i), 1000UL, 11, new[] { new byte[] { (byte)i } });
            Assert.IsNotNull(sealed_);
            await daWriter.PublishAsync(new DAPublishRequest { ChainId = 1001, BatchNumber = sealed_!.BatchNumber, Payload = new byte[] { (byte)i } });
            aggregator.Submit(sealed_);
        }
        aggregator.Aggregate();

        deposits.Process(BuildDeposit(1));
        withdrawals.Stage(BuildWithdrawal(1));

        rpc.GetL2StateRoot(new JArray { 1001 });
        rpc.GetSecurityLevel(new JArray { 1001 });

        // 4. Bind the HTTP server on a free port. Plugin's Start uses settings; the default
        //    is port 9090 which may collide. We test by reflection-free path: try Start
        //    and skip on collision (matches UT_L2MetricsPlugin's pattern).
        try { metricsPlugin.Start(); }
        catch (System.Net.Sockets.SocketException) { Assert.Inconclusive("port 9090 in use"); return; }

        // 5. Scrape /metrics through the plugin and assert every component's family appears.
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
        var resp = await client.GetAsync($"http://127.0.0.1:{metricsPlugin.BoundPort}/metrics");
        var body = await resp.Content.ReadAsStringAsync();

        Assert.AreEqual(200, (int)resp.StatusCode);

        // Every instrumented component must show up:
        StringAssert.Contains(body, "l2_batch_sealed_total 3", "BatchSealer");
        StringAssert.Contains(body, "l2_da_published_total{mode=\"External\"} 3", "MetricsEmittingDAWriter");
        StringAssert.Contains(body, "l2_bridge_deposits_total 1", "DepositProcessor");
        StringAssert.Contains(body, "l2_bridge_withdrawals_total 1", "WithdrawalProcessor");
        StringAssert.Contains(body, "l2_gateway_aggregations_total 1", "BinaryTreeAggregator");
        StringAssert.Contains(body, "l2_rpc_calls_total{method=\"getl2stateroot\"} 1", "L2RpcMethods.GetL2StateRoot");
        StringAssert.Contains(body, "l2_rpc_calls_total{method=\"getsecuritylevel\"} 1", "L2RpcMethods.GetSecurityLevel");
    }

    private static CrossChainMessage BuildDeposit(ulong nonce)
    {
        var payload = new DepositPayload
        {
            L1Asset = UInt160.Parse("0x" + new string('1', 40)),
            L2Recipient = UInt160.Parse("0x" + new string('a', 40)),
            Amount = 1_000_000,
        };
        return new CrossChainMessage
        {
            SourceChainId = 0,
            TargetChainId = 1001,
            Nonce = nonce,
            Sender = UInt160.Zero,
            Receiver = UInt160.Zero,
            MessageType = MessageType.Deposit,
            Payload = payload.Encode(),
            MessageHash = UInt256.Zero,
        };
    }

    private static WithdrawalRequest BuildWithdrawal(ulong nonce) => new()
    {
        EmittingContract = UInt160.Zero,
        L2Sender = UInt160.Parse("0x" + new string('a', 40)),
        L1Recipient = UInt160.Parse("0x" + new string('b', 40)),
        L2Asset = UInt160.Parse("0x" + new string('2', 40)),
        Amount = 100,
        Nonce = nonce,
    };
}
