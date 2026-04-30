using Neo.Json;

namespace Neo.Plugins.L2Rpc.UnitTests;

/// <summary>
/// Tests that <see cref="L2RpcMethods"/> emits per-method telemetry — counter, latency
/// histogram, and (on exception) failure counter — all tagged by RPC method name.
/// </summary>
[TestClass]
public class UT_L2RpcMethods_Metrics
{
    [TestMethod]
    public void EachMethod_EmitsCallsAndLatency_TaggedByMethod()
    {
        var store = new InMemoryL2RpcStore(1001, SecurityLevel.Optimistic);
        var metrics = new InMemoryMetrics();
        var methods = new L2RpcMethods(store, metrics);

        methods.GetL2StateRoot(new JArray { 1001 });
        methods.GetL2BatchStatus(new JArray { 1001, 1UL });
        methods.GetSecurityLevel(new JArray { 1001 });

        Assert.AreEqual(1, metrics.GetCounter(MetricNames.RpcCalls, ("method", "getl2stateroot")));
        Assert.AreEqual(1, metrics.GetCounter(MetricNames.RpcCalls, ("method", "getl2batchstatus")));
        Assert.AreEqual(1, metrics.GetCounter(MetricNames.RpcCalls, ("method", "getsecuritylevel")));

        Assert.AreEqual(1, metrics.GetHistogram(MetricNames.RpcLatencyMs, ("method", "getl2stateroot")).Count);
        Assert.AreEqual(1, metrics.GetHistogram(MetricNames.RpcLatencyMs, ("method", "getl2batchstatus")).Count);

        // No failures on the happy path
        Assert.AreEqual(0, metrics.GetCounter(MetricNames.RpcFailures, ("method", "getl2stateroot")));
    }

    [TestMethod]
    public void RepeatedCalls_AccumulatePerMethod()
    {
        var store = new InMemoryL2RpcStore(1001, SecurityLevel.Optimistic);
        var metrics = new InMemoryMetrics();
        var methods = new L2RpcMethods(store, metrics);

        for (var i = 0; i < 5; i++)
            methods.GetL2StateRoot(new JArray { 1001 });

        Assert.AreEqual(5, metrics.GetCounter(MetricNames.RpcCalls, ("method", "getl2stateroot")));
        Assert.AreEqual(5, metrics.GetHistogram(MetricNames.RpcLatencyMs, ("method", "getl2stateroot")).Count);
    }

    [TestMethod]
    public void ForeignChain_BumpsFailures_AndPropagates()
    {
        var store = new InMemoryL2RpcStore(1001, SecurityLevel.Optimistic);
        var metrics = new InMemoryMetrics();
        var methods = new L2RpcMethods(store, metrics);

        Assert.ThrowsExactly<ArgumentException>(() =>
            methods.GetL2StateRoot(new JArray { 9999 })); // wrong chainId

        Assert.AreEqual(0, metrics.GetCounter(MetricNames.RpcCalls, ("method", "getl2stateroot")), "no successful call");
        Assert.AreEqual(1, metrics.GetCounter(MetricNames.RpcFailures, ("method", "getl2stateroot")), "failure tagged");
    }

    [TestMethod]
    public void NoMetrics_DefaultsToNoOp()
    {
        // Old call sites without the metrics arg keep working.
        var store = new InMemoryL2RpcStore(1001, SecurityLevel.Optimistic);
        var methods = new L2RpcMethods(store); // no metrics
        var result = methods.GetL2StateRoot(new JArray { 1001 });
        Assert.IsNotNull(result);
    }
}
