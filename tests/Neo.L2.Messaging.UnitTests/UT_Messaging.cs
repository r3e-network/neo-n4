namespace Neo.L2.Messaging.UnitTests;

[TestClass]
public class UT_Messaging
{
    private static CrossChainMessage Build(uint target, ulong nonce, MessageType kind = MessageType.Call)
        => MessageBuilder.Build(
            sourceChainId: 1001,
            targetChainId: target,
            nonce: nonce,
            sender: UInt160.Parse("0x" + new string('a', 40)),
            receiver: UInt160.Parse("0x" + new string('b', 40)),
            messageType: kind,
            payload: new byte[] { 0xAA, 0xBB });

    [TestMethod]
    public void MessageBuilder_FillsHash()
    {
        var m = Build(0, 1);
        Assert.AreNotEqual(UInt256.Zero, m.MessageHash);

        var same = Build(0, 1);
        Assert.AreEqual(m.MessageHash, same.MessageHash);

        var diff = Build(0, 2);
        Assert.AreNotEqual(m.MessageHash, diff.MessageHash);
    }

    [TestMethod]
    public void Inbox_DequeueRespectsFifo()
    {
        var inbox = new L1MessageInbox();
        for (ulong i = 1; i <= 3; i++) inbox.Enqueue(Build(0, i));
        Assert.AreEqual(3, inbox.PendingCount);

        var taken = inbox.Dequeue(2);
        Assert.AreEqual(2, taken.Count);
        Assert.AreEqual(1UL, taken[0].Nonce);
        Assert.AreEqual(2UL, taken[1].Nonce);
        Assert.AreEqual(1, inbox.PendingCount);
        Assert.AreEqual(2, inbox.ConsumedCount);
    }

    [TestMethod]
    public void Inbox_RejectsReplay()
    {
        var inbox = new L1MessageInbox();
        inbox.Enqueue(Build(0, 1));
        inbox.Dequeue(1);
        Assert.ThrowsExactly<InvalidOperationException>(() => inbox.Enqueue(Build(0, 1)));
    }

    [TestMethod]
    public void Inbox_RejectsDuplicatePending()
    {
        var inbox = new L1MessageInbox();
        inbox.Enqueue(Build(0, 1));
        Assert.ThrowsExactly<InvalidOperationException>(() => inbox.Enqueue(Build(0, 1)));
    }

    [TestMethod]
    public void Outbox_SplitsByDestination()
    {
        var outbox = new L2Outbox();
        outbox.Add(Build(0, 1));   // L2 → L1
        outbox.Add(Build(0, 2));   // L2 → L1
        outbox.Add(Build(2002, 1)); // L2 → L2
        Assert.AreEqual(2, outbox.L2ToL1Count);
        Assert.AreEqual(1, outbox.L2ToL2Count);
        Assert.AreNotEqual(UInt256.Zero, outbox.L2ToL1Root);
        Assert.AreNotEqual(UInt256.Zero, outbox.L2ToL2Root);
    }

    [TestMethod]
    public async Task Router_RoundTrips()
    {
        var router = new InMemoryMessageRouter();
        router.Inbox.Enqueue(Build(0, 1));
        router.Inbox.Enqueue(Build(0, 2));

        var taken = await router.DequeueL1MessagesAsync(1001, 10);
        Assert.AreEqual(2, taken.Count);

        await router.EnqueueOutboundAsync(new[] { Build(0, 1), Build(2002, 5) });
        Assert.AreEqual(1, router.Outbox.L2ToL1Count);
        Assert.AreEqual(1, router.Outbox.L2ToL2Count);
    }

    [TestMethod]
    public async Task Router_GetMessageProof_NullWhenNotFinalized()
    {
        var router = new InMemoryMessageRouter();
        var p = await router.GetMessageProofAsync(UInt256.Zero);
        Assert.IsNull(p);
    }
}
