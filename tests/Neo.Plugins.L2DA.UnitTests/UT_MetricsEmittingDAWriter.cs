namespace Neo.Plugins.L2DA.UnitTests;

/// <summary>
/// Tests for <see cref="MetricsEmittingDAWriter"/> — ensures the decorator emits
/// <c>l2.da.published</c> / <c>l2.da.publish_latency_ms</c> on success and
/// <c>l2.da.publish_failures</c> on exception, all tagged by mode.
/// </summary>
[TestClass]
public class UT_MetricsEmittingDAWriter
{
    [TestMethod]
    public async Task Publish_Success_IncrementsPublishedAndLatency_TaggedByMode()
    {
        var inner = new InMemoryDAWriter(); // Mode = External
        var metrics = new InMemoryMetrics();
        var decorated = new MetricsEmittingDAWriter(inner, metrics);

        var receipt = await decorated.PublishAsync(BuildRequest(batch: 1, payload: new byte[] { 1, 2, 3 }));

        Assert.AreEqual(DAMode.External, receipt.Layer);
        Assert.AreEqual(1, metrics.GetCounter(MetricNames.DAPublished, ("mode", "External")));
        Assert.AreEqual(1, metrics.GetHistogram(MetricNames.DAPublishLatencyMs, ("mode", "External")).Count);
        Assert.AreEqual(0, metrics.GetCounter(MetricNames.DAPublishFailures, ("mode", "External")));
    }

    [TestMethod]
    public async Task Publish_OnInnerThrows_IncrementsFailures_AndPropagates()
    {
        var inner = new ThrowingWriter(DAMode.NeoFS);
        var metrics = new InMemoryMetrics();
        var decorated = new MetricsEmittingDAWriter(inner, metrics);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await decorated.PublishAsync(BuildRequest(batch: 1, payload: new byte[] { 0xFF })));

        Assert.AreEqual(0, metrics.GetCounter(MetricNames.DAPublished, ("mode", "NeoFS")));
        Assert.AreEqual(1, metrics.GetCounter(MetricNames.DAPublishFailures, ("mode", "NeoFS")));
    }

    [TestMethod]
    public async Task Publish_Multiple_AccumulatesByMode()
    {
        var inner = new InMemoryDAWriter();
        var metrics = new InMemoryMetrics();
        var decorated = new MetricsEmittingDAWriter(inner, metrics);

        for (var i = 0; i < 5; i++)
            await decorated.PublishAsync(BuildRequest(batch: (ulong)i, payload: new byte[] { (byte)i }));

        Assert.AreEqual(5, metrics.GetCounter(MetricNames.DAPublished, ("mode", "External")));
        Assert.AreEqual(5, metrics.GetHistogram(MetricNames.DAPublishLatencyMs, ("mode", "External")).Count);
    }

    [TestMethod]
    public async Task IsAvailableAsync_PassesThrough_ToInner()
    {
        var inner = new InMemoryDAWriter();
        var metrics = new InMemoryMetrics();
        var decorated = new MetricsEmittingDAWriter(inner, metrics);

        var receipt = await decorated.PublishAsync(BuildRequest(batch: 1, payload: new byte[] { 7 }));
        var available = await decorated.IsAvailableAsync(receipt);

        Assert.IsTrue(available, "IsAvailableAsync should pass through to inner");
    }

    [TestMethod]
    public void Constructor_Rejects_NullArguments()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => _ = new MetricsEmittingDAWriter(null!, new InMemoryMetrics()));
        Assert.ThrowsExactly<ArgumentNullException>(() => _ = new MetricsEmittingDAWriter(new InMemoryDAWriter(), null!));
    }

    [TestMethod]
    public void Mode_Mirrors_Inner()
    {
        var inner = new InMemoryDAWriter();
        var decorated = new MetricsEmittingDAWriter(inner, new InMemoryMetrics());
        Assert.AreEqual(inner.Mode, decorated.Mode);
        Assert.AreSame(inner, decorated.Inner);
    }

    private static DAPublishRequest BuildRequest(ulong batch, byte[] payload) => new()
    {
        ChainId = 1001,
        BatchNumber = batch,
        Payload = payload,
    };

    private sealed class ThrowingWriter : IDAWriter
    {
        public ThrowingWriter(DAMode mode) { Mode = mode; }
        public DAMode Mode { get; }
        public ValueTask<DAReceipt> PublishAsync(DAPublishRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("simulated DA failure");
        public ValueTask<bool> IsAvailableAsync(DAReceipt receipt, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(false);
    }
}
