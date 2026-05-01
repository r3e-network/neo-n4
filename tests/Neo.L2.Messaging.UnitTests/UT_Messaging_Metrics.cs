namespace Neo.L2.Messaging.UnitTests;

[TestClass]
public class UT_Messaging_Metrics
{
    private static CrossChainMessage Mk(uint targetChain, ulong nonce) => new()
    {
        SourceChainId = 1001,
        TargetChainId = targetChain,
        Nonce = nonce,
        Sender = UInt160.Zero,
        Receiver = UInt160.Zero,
        MessageType = MessageType.Call,
        Payload = new byte[] { (byte)nonce },
        MessageHash = UInt256.Zero,
    };

    [TestMethod]
    public void Outbox_Add_IncrementsMessagesEmitted_AcrossDestinations()
    {
        var metrics = new InMemoryMetrics();
        var outbox = new L2Outbox(metrics);

        outbox.Add(Mk(0, 1));     // L2 → L1
        outbox.Add(Mk(0, 2));     // L2 → L1
        outbox.Add(Mk(1002, 3));  // L2 → L2

        Assert.AreEqual(3, metrics.GetCounter(MetricNames.MessagesEmitted));
        Assert.AreEqual(2, outbox.L2ToL1Count);
        Assert.AreEqual(1, outbox.L2ToL2Count);
    }

    [TestMethod]
    public void Outbox_DefaultsToNoOp_WhenNoMetrics()
    {
        var outbox = new L2Outbox();
        outbox.Add(Mk(0, 1));
        // no-throw
    }
}
