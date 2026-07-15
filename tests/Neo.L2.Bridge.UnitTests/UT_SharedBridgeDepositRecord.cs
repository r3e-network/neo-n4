using System.Numerics;

namespace Neo.L2.Bridge.UnitTests;

[TestClass]
public class UT_SharedBridgeDepositRecord
{
    private static readonly UInt160 Asset = UInt160.Parse("0x" + new string('a', 40));
    private static readonly UInt160 Recipient = UInt160.Parse("0x" + new string('b', 40));
    private static readonly UInt160 Sender = UInt160.Parse("0x" + new string('c', 40));
    private static readonly UInt160 L2Bridge = UInt160.Parse("0x" + new string('d', 40));

    [TestMethod]
    public void EncodeDecode_RoundTrips_IncludingHighMsbAmount()
    {
        // Amount with high bit set would grow a sign byte under signed ToByteArray().
        var amount = BigInteger.Parse("128");
        var record = new SharedBridgeDepositRecord
        {
            Asset = Asset,
            Recipient = Recipient,
            Sender = Sender,
            Nonce = 42,
            Amount = amount,
        };

        var bytes = record.Encode();
        var decoded = SharedBridgeDepositRecord.Decode(bytes);
        Assert.AreEqual(Asset, decoded.Asset);
        Assert.AreEqual(Recipient, decoded.Recipient);
        Assert.AreEqual(Sender, decoded.Sender);
        Assert.AreEqual(42UL, decoded.Nonce);
        Assert.AreEqual(amount, decoded.Amount);
        CollectionAssert.AreEqual(bytes, decoded.Encode());
    }

    [TestMethod]
    public void ToDepositPayload_ProjectsCanonicalMintFields()
    {
        var record = new SharedBridgeDepositRecord
        {
            Asset = Asset,
            Recipient = Recipient,
            Sender = Sender,
            Nonce = 7,
            Amount = 1_000_000,
        };

        var payload = record.ToDepositPayload();
        Assert.AreEqual(Asset, payload.L1Asset);
        Assert.AreEqual(Recipient, payload.L2Recipient);
        Assert.AreEqual(new BigInteger(1_000_000), payload.Amount);
        CollectionAssert.AreEqual(payload.Encode(), DepositPayload.Decode(payload.Encode()).Encode());
    }

    [TestMethod]
    public void ToCrossChainMessage_BindsNativeBridgeReceiverAndCanonicalHash()
    {
        var record = new SharedBridgeDepositRecord
        {
            Asset = Asset,
            Recipient = Recipient,
            Sender = Sender,
            Nonce = 9,
            Amount = 55,
        };

        var message = record.ToCrossChainMessage(targetChainId: 1099, l2BridgeHash: L2Bridge);
        Assert.AreEqual(0u, message.SourceChainId);
        Assert.AreEqual(1099u, message.TargetChainId);
        Assert.AreEqual(9UL, message.Nonce);
        Assert.AreEqual(Sender, message.Sender);
        Assert.AreEqual(L2Bridge, message.Receiver);
        Assert.AreEqual(MessageType.Deposit, message.MessageType);
        CollectionAssert.AreEqual(record.ToDepositPayload().Encode(), message.Payload.ToArray());
        Assert.AreEqual(MessageHasher.HashMessage(message with { MessageHash = UInt256.Zero }), message.MessageHash);
    }

    [TestMethod]
    public void Decode_RejectsTrailingBytesAndZeroFields()
    {
        var record = new SharedBridgeDepositRecord
        {
            Asset = Asset,
            Recipient = Recipient,
            Sender = Sender,
            Nonce = 1,
            Amount = 1,
        };
        var bytes = record.Encode().Concat(new byte[] { 0xFF }).ToArray();
        Assert.ThrowsExactly<InvalidDataException>(() => SharedBridgeDepositRecord.Decode(bytes));

        Assert.ThrowsExactly<InvalidDataException>(() => SharedBridgeDepositRecord.Decode(
            new SharedBridgeDepositRecord
            {
                Asset = UInt160.Zero,
                Recipient = Recipient,
                Sender = Sender,
                Nonce = 1,
                Amount = 1,
            }.Encode()));
    }
}
