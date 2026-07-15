using System.Numerics;

namespace Neo.L2.Bridge.UnitTests;

[TestClass]
public class UT_InMemorySharedBridgeDepositSource
{
    private static readonly UInt160 L2Bridge = UInt160.Parse("0x" + new string('d', 40));
    private static readonly UInt160 Asset = UInt160.Parse("0x" + new string('a', 40));
    private static readonly UInt160 Alice = UInt160.Parse("0x" + new string('b', 40));

    [TestMethod]
    public void Enqueue_Drain_Confirm_Lifecycle()
    {
        var source = new InMemorySharedBridgeDepositSource(1099, L2Bridge);
        var record = Record(3);

        var message = source.Enqueue(record);
        Assert.AreEqual(3UL, message.Nonce);
        Assert.AreEqual(L2Bridge, message.Receiver);
        Assert.AreEqual(1, source.Peek(10).Count);

        var drained = source.Drain(10);
        Assert.AreEqual(1, drained.Count);
        Assert.AreEqual(0, source.Peek(10).Count, "drain must reserve so peek is empty");
        Assert.AreEqual(0, source.Drain(10).Count, "reserved deposits must not re-drain");

        Assert.ThrowsExactly<InvalidOperationException>(() => source.Enqueue(record));

        source.ConfirmConsumed(3);
        Assert.AreEqual(0, source.Peek(10).Count);
        Assert.ThrowsExactly<InvalidOperationException>(() => source.Enqueue(record));
    }

    [TestMethod]
    public void ReleaseReservations_ReturnsToReady()
    {
        var source = new InMemorySharedBridgeDepositSource(1099, L2Bridge);
        source.Enqueue(Record(5));
        source.Enqueue(Record(2));
        source.Enqueue(Record(9));

        var drained = source.Drain(2);
        Assert.AreEqual(2, drained.Count);
        Assert.AreEqual(2UL, drained[0].Nonce);
        Assert.AreEqual(5UL, drained[1].Nonce);

        source.ReleaseReservations(new[] { 2UL, 5UL });
        var again = source.Drain(10);
        Assert.AreEqual(3, again.Count);
        Assert.AreEqual(2UL, again[0].Nonce);
        Assert.AreEqual(5UL, again[1].Nonce);
        Assert.AreEqual(9UL, again[2].Nonce);
    }

    [TestMethod]
    public void ConfirmConsumed_IsIdempotent()
    {
        var source = new InMemorySharedBridgeDepositSource(1099, L2Bridge);
        source.Enqueue(Record(1));
        source.Drain(1);
        source.ConfirmConsumed(1);
        source.ConfirmConsumed(1);
        Assert.AreEqual(0, source.Peek(10).Count);
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
