using System.Buffers.Binary;
using System.Numerics;
using Neo.Cryptography;

namespace Neo.L2.State.UnitTests;

/// <summary>
/// Direct unit tests for <see cref="MessageHasher"/>. Existing coverage exercises the
/// upstream MessageBuilder.Build / WithdrawalProcessor.Stage boundary (UT_Messaging,
/// UT_Bridge), which catches null fields before they ever reach the hasher. But
/// MessageHasher.HashMessage / HashWithdrawal are static utilities that any caller can
/// invoke directly — the per-field null-guards at MessageHasher.cs:27-28 and 58-61 are
/// documented as defense-in-depth for that case and need direct pins so a refactor
/// can't silently drop them.
/// </summary>
[TestClass]
public class UT_MessageHasher
{
    private static UInt160 A() => UInt160.Parse("0x" + new string('a', 40));
    private static UInt160 B() => UInt160.Parse("0x" + new string('b', 40));
    private static UInt160 C() => UInt160.Parse("0x" + new string('c', 40));
    private static UInt160 D() => UInt160.Parse("0x" + new string('d', 40));

    private static CrossChainMessage Msg(UInt160? sender = null, UInt160? receiver = null) => new()
    {
        SourceChainId = 1001,
        TargetChainId = 0,
        Nonce = 1,
        Sender = sender!,
        Receiver = receiver!,
        MessageType = MessageType.Call,
        Payload = new byte[] { 0x01, 0x02 },
        MessageHash = UInt256.Zero,
    };

    private static WithdrawalRequest Wd(
        UInt160? emitting = null, UInt160? l2sender = null,
        UInt160? l1recipient = null, UInt160? l2asset = null) => new()
    {
        EmittingContract = emitting!,
        L2Sender = l2sender!,
        L1Recipient = l1recipient!,
        L2Asset = l2asset!,
        Amount = 1000,
        Nonce = 1,
    };

    [TestMethod]
    public void HashMessage_RejectsNullMessage()
        => Assert.ThrowsExactly<ArgumentNullException>(() => MessageHasher.HashMessage(null!));

    [TestMethod]
    public void HashMessage_RejectsNullSender()
    {
        // Pinning the in-method null-guard at MessageHasher.cs:27. MessageBuilder.Build
        // already catches this upstream, but a direct caller of HashMessage must hit the
        // guard here — without it, message.Sender.GetSpan() NREs with no link to which
        // field was null.
        var bad = Msg(sender: null, receiver: B());
        Assert.ThrowsExactly<ArgumentNullException>(() => MessageHasher.HashMessage(bad));
    }

    [TestMethod]
    public void HashMessage_RejectsNullReceiver()
    {
        var bad = Msg(sender: A(), receiver: null);
        Assert.ThrowsExactly<ArgumentNullException>(() => MessageHasher.HashMessage(bad));
    }

    [TestMethod]
    public void HashMessage_DeterministicForSameInput()
    {
        // Sanity for the happy path — same input → same hash, different input → different.
        var h1 = MessageHasher.HashMessage(Msg(sender: A(), receiver: B()));
        var h2 = MessageHasher.HashMessage(Msg(sender: A(), receiver: B()));
        Assert.AreEqual(h1, h2);
    }

    [TestMethod]
    public void HashWithdrawal_RejectsNullWithdrawal()
        => Assert.ThrowsExactly<ArgumentNullException>(() => MessageHasher.HashWithdrawal(null!));

    [TestMethod]
    public void HashWithdrawal_RejectsNullEmittingContract()
    {
        var bad = Wd(emitting: null, l2sender: B(), l1recipient: C(), l2asset: D());
        Assert.ThrowsExactly<ArgumentNullException>(() => MessageHasher.HashWithdrawal(bad));
    }

    [TestMethod]
    public void HashWithdrawal_RejectsNullL2Sender()
    {
        var bad = Wd(emitting: A(), l2sender: null, l1recipient: C(), l2asset: D());
        Assert.ThrowsExactly<ArgumentNullException>(() => MessageHasher.HashWithdrawal(bad));
    }

    [TestMethod]
    public void HashWithdrawal_RejectsNullL1Recipient()
    {
        var bad = Wd(emitting: A(), l2sender: B(), l1recipient: null, l2asset: D());
        Assert.ThrowsExactly<ArgumentNullException>(() => MessageHasher.HashWithdrawal(bad));
    }

    [TestMethod]
    public void HashWithdrawal_RejectsNullL2Asset()
    {
        var bad = Wd(emitting: A(), l2sender: B(), l1recipient: C(), l2asset: null);
        Assert.ThrowsExactly<ArgumentNullException>(() => MessageHasher.HashWithdrawal(bad));
    }

    [TestMethod]
    public void HashMessage_CanonicalBufferLayout_MatchesDocumented()
    {
        // Pins the canonical hash-input layout claimed in MessageHasher.cs:30-43:
        //   [4B sourceChainId][4B targetChainId][8B nonce][20B sender][20B receiver]
        //   [1B messageType][4B payloadLen][payloadLen B payload]
        //   → Hash256
        // If any L1 contract reads message bytes off the wire with a different field
        // order or endianness, the hashes desync. Independently assembles the buffer
        // here and asserts Hash256 of it equals MessageHasher.HashMessage's output.
        var msg = new CrossChainMessage
        {
            SourceChainId = 1001,
            TargetChainId = 2002,
            Nonce = 0xDEADBEEFCAFEBABE,
            Sender = A(),
            Receiver = B(),
            MessageType = MessageType.Call,
            Payload = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 },
            MessageHash = UInt256.Zero,
        };

        var size = 4 + 4 + 8 + 20 + 20 + 1 + 4 + msg.Payload.Length;
        var buf = new byte[size];
        var span = buf.AsSpan();
        var pos = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(pos, 4), msg.SourceChainId); pos += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(pos, 4), msg.TargetChainId); pos += 4;
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(pos, 8), msg.Nonce); pos += 8;
        msg.Sender.GetSpan().CopyTo(span.Slice(pos, 20)); pos += 20;
        msg.Receiver.GetSpan().CopyTo(span.Slice(pos, 20)); pos += 20;
        span[pos++] = (byte)msg.MessageType;
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(pos, 4), msg.Payload.Length); pos += 4;
        msg.Payload.Span.CopyTo(span.Slice(pos));

        var expected = new UInt256(Crypto.Hash256(buf));
        Assert.AreEqual(expected, MessageHasher.HashMessage(msg));
    }

    [TestMethod]
    public void HashWithdrawal_CanonicalBufferLayout_MatchesDocumented()
    {
        // Pins the canonical hash-input layout claimed in MessageHasher.cs:66-76:
        //   [20B emittingContract][20B l2Sender][20B l1Recipient][20B l2Asset]
        //   [4B amountLen][amountLen B unsigned-LE-amount][8B nonce]
        //   → Hash256
        // The L1 SharedBridge verifies withdrawal-leaf inclusion against this hash.
        var amount = new BigInteger(0x1122334455667788UL);
        var amountBytes = amount.ToByteArray(isUnsigned: true, isBigEndian: false);
        var wd = new WithdrawalRequest
        {
            EmittingContract = A(),
            L2Sender = B(),
            L1Recipient = C(),
            L2Asset = D(),
            Amount = amount,
            Nonce = 0xCAFEBABEDEADBEEFUL,
        };

        var size = 20 + 20 + 20 + 20 + 4 + amountBytes.Length + 8;
        var buf = new byte[size];
        var span = buf.AsSpan();
        var pos = 0;
        wd.EmittingContract.GetSpan().CopyTo(span.Slice(pos, 20)); pos += 20;
        wd.L2Sender.GetSpan().CopyTo(span.Slice(pos, 20)); pos += 20;
        wd.L1Recipient.GetSpan().CopyTo(span.Slice(pos, 20)); pos += 20;
        wd.L2Asset.GetSpan().CopyTo(span.Slice(pos, 20)); pos += 20;
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(pos, 4), amountBytes.Length); pos += 4;
        amountBytes.AsSpan().CopyTo(span.Slice(pos, amountBytes.Length)); pos += amountBytes.Length;
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(pos, 8), wd.Nonce);

        var expected = new UInt256(Crypto.Hash256(buf));
        Assert.AreEqual(expected, MessageHasher.HashWithdrawal(wd));
    }

    [TestMethod]
    public void HashMessage_FieldOrderChange_ProducesDifferentHash()
    {
        // Sentinel: swapping source/target chainId must produce a different hash. If
        // the encoder ever degrades to a commutative concat (or hashes a Set instead
        // of an ordered tuple), this fails.
        var sender = A();
        var receiver = B();
        var h1 = MessageHasher.HashMessage(new CrossChainMessage
        {
            SourceChainId = 1001, TargetChainId = 2002, Nonce = 1,
            Sender = sender, Receiver = receiver,
            MessageType = MessageType.Call, Payload = new byte[0],
            MessageHash = UInt256.Zero,
        });
        var h2 = MessageHasher.HashMessage(new CrossChainMessage
        {
            SourceChainId = 2002, TargetChainId = 1001, Nonce = 1,
            Sender = sender, Receiver = receiver,
            MessageType = MessageType.Call, Payload = new byte[0],
            MessageHash = UInt256.Zero,
        });
        Assert.AreNotEqual(h1, h2);
    }

    [TestMethod]
    public void HashWithdrawal_RejectsOversizedAmount()
    {
        // Pinning MessageHasher.cs:63-64 — amount serialization caps at 64 bytes
        // (already a > 256-bit number, well past any plausible token). Without this
        // cap the buffer alloc size becomes attacker-influenced.
        var huge = BigInteger.One << 600; // ~75 bytes when serialized
        var bad = new WithdrawalRequest
        {
            EmittingContract = A(),
            L2Sender = B(),
            L1Recipient = C(),
            L2Asset = D(),
            Amount = huge,
            Nonce = 1,
        };
        Assert.ThrowsExactly<ArgumentException>(() => MessageHasher.HashWithdrawal(bad));
    }
}
