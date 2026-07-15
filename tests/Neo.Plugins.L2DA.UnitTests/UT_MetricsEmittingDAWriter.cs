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
        var inner = new InMemoryDAWriter(); // Mode = Local
        var metrics = new InMemoryMetrics();
        var decorated = new MetricsEmittingDAWriter(inner, metrics);

        var receipt = await decorated.PublishAsync(BuildRequest(batch: 1, payload: new byte[] { 1, 2, 3 }));

        Assert.AreEqual(DAMode.Local, receipt.Layer);
        Assert.AreEqual(1, metrics.GetCounter(MetricNames.DAPublished, ("mode", "Local")));
        Assert.AreEqual(1, metrics.GetHistogram(MetricNames.DAPublishLatencyMs, ("mode", "Local")).Count);
        Assert.AreEqual(0, metrics.GetCounter(MetricNames.DAPublishFailures, ("mode", "Local")));
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

        Assert.AreEqual(5, metrics.GetCounter(MetricNames.DAPublished, ("mode", "Local")));
        Assert.AreEqual(5, metrics.GetHistogram(MetricNames.DAPublishLatencyMs, ("mode", "Local")).Count);
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
        Assert.AreEqual(inner.ReceiptKind, decorated.ReceiptKind);
        Assert.AreSame(inner, decorated.Inner);
    }

    [TestMethod]
    public async Task Inner_PreservedAcross_UnwrapRewrap()
    {
        // Regression: L2DAPlugin.WithMetrics unwraps the existing decorator and re-wraps with
        // the new metrics. The inner InMemoryDAWriter must keep its state — otherwise a
        // mid-flight metrics sink swap would lose previously-published content.
        var inner = new InMemoryDAWriter();
        var decorated1 = new MetricsEmittingDAWriter(inner, new InMemoryMetrics());

        var receipt = await decorated1.PublishAsync(new DAPublishRequest
        {
            ChainId = 1001,
            BatchNumber = 1,
            Payload = new byte[] { 1, 2, 3 },
        });

        // Simulate a metrics-sink rewire: unwrap, rewrap with a different sink.
        var unwrapped = decorated1.Inner;
        var decorated2 = new MetricsEmittingDAWriter(unwrapped, new InMemoryMetrics());

        // The inner store must still know about the original publish.
        Assert.IsTrue(await decorated2.IsAvailableAsync(receipt));
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

    [TestMethod]
    public async Task Publish_BuggyInnerReturnsNull_SurfacesContractViolation()
    {
        // Regression for iter 173: a buggy IDAWriter returning null DAReceipt would
        // propagate to callers as a NRE on `receipt.Commitment` access. Now surfaced
        // as a clear InvalidOperationException naming the contract method, and the
        // failure metric is still bumped so dashboards see something.
        var metrics = new InMemoryMetrics();
        var inner = new NullReturningWriter();
        var decorated = new MetricsEmittingDAWriter(inner, metrics);
        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await decorated.PublishAsync(BuildRequest(1, new byte[] { 0xAA })));
        StringAssert.Contains(ex.Message, "PublishAsync");
        Assert.AreEqual(1, metrics.GetCounter(MetricNames.DAPublishFailures, ("mode", "External")),
            "failure must be counted even on contract violation");
        Assert.AreEqual(0, metrics.GetCounter(MetricNames.DAPublished, ("mode", "External")),
            "success metric must NOT fire on null return");
    }

    private sealed class NullReturningWriter : IDAWriter
    {
        public DAMode Mode => DAMode.External;
        public ValueTask<DAReceipt> PublishAsync(DAPublishRequest request, CancellationToken cancellationToken = default)
            => new ValueTask<DAReceipt>((DAReceipt)null!);
        public ValueTask<bool> IsAvailableAsync(DAReceipt receipt, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(false);
    }

    [TestMethod]
    public async Task Publish_MalformedReceipt_FailsBeforeSuccessMetrics()
    {
        var metrics = new InMemoryMetrics();
        var decorated = new MetricsEmittingDAWriter(new MalformedWriter(), metrics);

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await decorated.PublishAsync(BuildRequest(1, new byte[] { 0xAA })));
        StringAssert.Contains(ex.Message, "malformed or mislabeled");
        Assert.AreEqual(1, metrics.GetCounter(MetricNames.DAPublishFailures, ("mode", "NeoFS")));
        Assert.AreEqual(0, metrics.GetCounter(MetricNames.DAPublished, ("mode", "NeoFS")));
    }

    private sealed class MalformedWriter : IDAWriter
    {
        public DAMode Mode => DAMode.NeoFS;

        public DAReceiptKind ReceiptKind => DAReceiptKind.NeoFSObject;

        public ValueTask<DAReceipt> PublishAsync(
            DAPublishRequest request,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new DAReceipt
            {
                Commitment = UInt256.Zero,
                Pointer = ReadOnlyMemory<byte>.Empty,
                Layer = DAMode.NeoFS,
            });

        public ValueTask<bool> IsAvailableAsync(
            DAReceipt receipt,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(false);
    }
}
