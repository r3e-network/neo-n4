using System.Numerics;

namespace Neo.L2.Bridge.UnitTests;

[TestClass]
public class UT_InMemorySharedBridgeDepositSource
{
    private static readonly UInt160 L2Bridge = UInt160.Parse("0x" + new string('d', 40));
    private static readonly UInt160 Asset = UInt160.Parse("0x" + new string('a', 40));
    private static readonly UInt160 Alice = UInt160.Parse("0x" + new string('b', 40));

    [TestMethod]
    public void Enqueue_Peek_Confirm_Lifecycle()
    {
        var source = new InMemorySharedBridgeDepositSource(1099, L2Bridge);
        var record = new SharedBridgeDepositRecord
        {
            Asset = Asset,
            Recipient = Alice,
            Sender = Alice,
            Nonce = 3,
            Amount = 500,
        };

        var message = source.Enqueue(record);
        Assert.AreEqual(3UL, message.Nonce);
        Assert.AreEqual(L2Bridge, message.Receiver);
        Assert.AreEqual(1, source.Peek(10).Count);

        Assert.ThrowsExactly<InvalidOperationException>(() => source.Enqueue(record));

        source.ConfirmConsumed(3);
        Assert.AreEqual(0, source.Peek(10).Count);
        Assert.ThrowsExactly<InvalidOperationException>(() => source.Enqueue(record));
    }

    [TestMethod]
    public void Drain_OrdersByNonce()
    {
        var source = new InMemorySharedBridgeDepositSource(1099, L2Bridge);
        source.Enqueue(Record(5));
        source.Enqueue(Record(2));
        source.Enqueue(Record(9));

        var drained = source.Drain(2);
        Assert.AreEqual(2, drained.Count);
        Assert.AreEqual(2UL, drained[0].Nonce);
        Assert.AreEqual(5UL, drained[1].Nonce);
    }

    private static SharedBridgeDepositRecord Record(ulong nonce) => new()
    {
        Asset = Asset,
        Recipient = Alice,
        Sender = Alice,
        Nonce = nonce,
        Amount = new BigInteger(nonce * 10),
    };
}
