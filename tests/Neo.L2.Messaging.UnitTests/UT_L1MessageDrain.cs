namespace Neo.L2.Messaging.UnitTests;

[TestClass]
public class UT_L1MessageDrain
{
    private static CrossChainMessage Msg(uint source, ulong nonce, byte tag = 1) => new()
    {
        SourceChainId = source,
        TargetChainId = 1001,
        Nonce = nonce,
        Sender = UInt160.Parse("0x" + new string('1', 40)),
        Receiver = UInt160.Parse("0x" + new string('2', 40)),
        MessageType = MessageType.Deposit,
        Payload = new byte[] { tag },
        MessageHash = UInt256.Zero,
    };

    [TestMethod]
    public void Combine_MergesSortsAndCaps()
    {
        var drain = L1MessageDrain.Combine(
            _ => new[] { Msg(0, 3), Msg(0, 1) },
            _ => new[] { Msg(0, 2) });

        var result = drain(2);
        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(1UL, result[0].Nonce);
        Assert.AreEqual(2UL, result[1].Nonce);
    }

    [TestMethod]
    public void Combine_RejectsNullDrainResult()
    {
        var drain = L1MessageDrain.Combine(_ => null!);
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => drain(1));
        StringAssert.Contains(ex.Message, "returned null");
    }

    [TestMethod]
    public void Combine_RejectsDuplicateSourceNonceAcrossDrains()
    {
        var drain = L1MessageDrain.Combine(
            _ => new[] { Msg(0, 7, tag: 1) },
            _ => new[] { Msg(0, 7, tag: 2) });
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => drain(10));
        StringAssert.Contains(ex.Message, "duplicate L1 message key");
    }

    [TestMethod]
    public void Combine_RejectsEmptyAndNullEntries()
    {
        Assert.ThrowsExactly<ArgumentException>(() => L1MessageDrain.Combine());
        Assert.ThrowsExactly<ArgumentNullException>(() => L1MessageDrain.Combine(null!));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => L1MessageDrain.Combine(max => Array.Empty<CrossChainMessage>(), null!));
    }
}
