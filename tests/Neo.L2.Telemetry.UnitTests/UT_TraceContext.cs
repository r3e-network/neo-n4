namespace Neo.L2.Telemetry.Tests;

[TestClass]
public class UT_TraceContext
{
    [TestMethod]
    public void CorrelationId_ReturnsNonEmpty()
    {
        var id = TraceContext.CorrelationId;
        Assert.IsNotNull(id);
        Assert.IsTrue(id.Length > 0);
    }

    [TestMethod]
    public void SetCorrelationId_RoundTrips()
    {
        var expected = "abc123def456";
        TraceContext.SetCorrelationId(expected);
        Assert.AreEqual(expected, TraceContext.CorrelationId);
    }

    [TestMethod]
    public void NewCorrelationId_GeneratesNewId()
    {
        var id1 = TraceContext.NewCorrelationId();
        var id2 = TraceContext.NewCorrelationId();
        Assert.IsNotNull(id1);
        Assert.IsNotNull(id2);
        Assert.AreNotEqual(id1, id2);
    }

    [TestMethod]
    public void Clear_ResetsToNewId()
    {
        TraceContext.SetCorrelationId("fixed-id");
        TraceContext.Clear();
        var id = TraceContext.CorrelationId;
        Assert.AreNotEqual("fixed-id", id);
    }

    [TestMethod]
    public void SetCorrelationId_NullUsesGeneratedId()
    {
        TraceContext.SetCorrelationId("previous");
        TraceContext.SetCorrelationId(null!);
        var id = TraceContext.CorrelationId;
        Assert.IsNotNull(id);
    }

    [TestMethod]
    public async Task RunWithTraceAsync_PreservesContext()
    {
        TraceContext.SetCorrelationId("outer");
        await TraceContext.RunWithTraceAsync(async () =>
        {
            var inner = TraceContext.CorrelationId;
            Assert.IsNotNull(inner);
            Assert.AreNotEqual("outer", inner);
            await Task.CompletedTask;
        });
        Assert.AreEqual("outer", TraceContext.CorrelationId);
    }

    [TestMethod]
    public async Task RunWithTraceAsync_AcceptsCustomId()
    {
        await TraceContext.RunWithTraceAsync(async () =>
        {
            Assert.AreEqual("custom-id", TraceContext.CorrelationId);
            await Task.CompletedTask;
        }, correlationId: "custom-id");
    }
}
