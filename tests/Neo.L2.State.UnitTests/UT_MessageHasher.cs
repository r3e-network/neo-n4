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
            ChainId = 1U,
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
        //   [4B chainId LE]
        //   [20B emittingContract][20B l2Sender][20B l1Recipient][20B l2Asset]
        //   [4B amountLen][amountLen B unsigned-LE-amount][8B nonce]
        //   → Hash256
        // The L1 SharedBridge verifies withdrawal-leaf inclusion against this hash.
        // The leading chainId is a domain-separator so an inclusion proof from one
        // L2's withdrawal root can't replay against another L2.
        var amount = new BigInteger(0x1122334455667788UL);
        var amountBytes = amount.ToByteArray(isUnsigned: true, isBigEndian: false);
        var wd = new WithdrawalRequest
        {
            ChainId = 1U,
            EmittingContract = A(),
            L2Sender = B(),
            L1Recipient = C(),
            L2Asset = D(),
            Amount = amount,
            Nonce = 0xCAFEBABEDEADBEEFUL,
        };

        var size = 4 + 20 + 20 + 20 + 20 + 4 + amountBytes.Length + 8;
        var buf = new byte[size];
        var span = buf.AsSpan();
        var pos = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(pos, 4), wd.ChainId); pos += 4;
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
            SourceChainId = 1001,
            TargetChainId = 2002,
            Nonce = 1,
            Sender = sender,
            Receiver = receiver,
            MessageType = MessageType.Call,
            Payload = new byte[0],
            MessageHash = UInt256.Zero,
        });
        var h2 = MessageHasher.HashMessage(new CrossChainMessage
        {
            SourceChainId = 2002,
            TargetChainId = 1001,
            Nonce = 1,
            Sender = sender,
            Receiver = receiver,
            MessageType = MessageType.Call,
            Payload = new byte[0],
            MessageHash = UInt256.Zero,
        });
        Assert.AreNotEqual(h1, h2);
    }

    [TestMethod]
    public void HashWithdrawal_DifferentChainId_ProducesDifferentHash()
    {
        // Regression for the H1 fix: chainId is part of the leaf-hash preimage as
        // a 4-byte LE domain-separator. Two withdrawals with IDENTICAL emittingContract,
        // l2Sender, l1Recipient, l2Asset, amount, and nonce MUST hash to different
        // leaves when their chainId differs — otherwise an inclusion proof from one
        // L2's withdrawal root could be replayed against another L2 with a coincidentally
        // matching tuple. Operational defense (per-chain Merkle root + chainId-scoped
        // consumed key) remains as defense-in-depth, but the hash domain itself is now
        // chain-separated so this scenario can never reach those gates.
        var wd1099 = new WithdrawalRequest
        {
            ChainId = 1099U,
            EmittingContract = A(),
            L2Sender = B(),
            L1Recipient = C(),
            L2Asset = D(),
            Amount = 1_000_000UL,
            Nonce = 7,
        };
        var wd2099 = wd1099 with { ChainId = 2099U };
        Assert.AreNotEqual(MessageHasher.HashWithdrawal(wd1099), MessageHasher.HashWithdrawal(wd2099),
            "chainId must domain-separate the withdrawal-leaf hash (H1)");
        // Sanity: chainId is the ONLY change between the two; everything else equal.
        Assert.AreEqual(wd1099.EmittingContract, wd2099.EmittingContract);
        Assert.AreEqual(wd1099.Nonce, wd2099.Nonce);
        Assert.AreEqual(wd1099.Amount, wd2099.Amount);
    }

    [TestMethod]
    public void HashWithdrawal_SameInputs_Deterministic()
    {
        // Companion to the chainId-separation test: same WithdrawalRequest hashed
        // twice must produce the same leaf. Without this, the H1 fix's chainId-LE
        // encoding (or any future preimage refactor) could non-deterministically
        // permute fields and silently break inclusion proofs for *all* withdrawals.
        var wd = new WithdrawalRequest
        {
            ChainId = 1099U,
            EmittingContract = A(),
            L2Sender = B(),
            L1Recipient = C(),
            L2Asset = D(),
            Amount = 1_000_000UL,
            Nonce = 42,
        };
        Assert.AreEqual(MessageHasher.HashWithdrawal(wd), MessageHasher.HashWithdrawal(wd));
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
            ChainId = 1U,
            EmittingContract = A(),
            L2Sender = B(),
            L1Recipient = C(),
            L2Asset = D(),
            Amount = huge,
            Nonce = 1,
        };
        Assert.ThrowsExactly<ArgumentException>(() => MessageHasher.HashWithdrawal(bad));
    }

    [TestMethod]
    public void HashWithdrawal_AcceptsExactly64ByteAmount()
    {
        // Boundary partner of RejectsOversizedAmount: cap is `> 64`, so exactly 64
        // bytes must hash without error. 2^504 serializes to exactly 64 unsigned-LE
        // bytes (bit 504 = byte 63 bit 0).
        var atMax = BigInteger.One << 504;
        var w = new WithdrawalRequest
        {
            ChainId = 1U,
            EmittingContract = A(),
            L2Sender = B(),
            L1Recipient = C(),
            L2Asset = D(),
            Amount = atMax,
            Nonce = 1,
        };
        var hash = MessageHasher.HashWithdrawal(w);
        Assert.AreNotEqual(UInt256.Zero, hash);
    }

    [TestMethod]
    public void HashWithdrawal_GoldenVector_CrossImplementationParity()
    {
        // Frozen cross-implementation reference. The off-chain MessageHasher.HashWithdrawal
        // must produce the same leaf hash as the on-chain
        // NeoHub.SharedBridge.ComputeWithdrawalLeafHash, which a unit test cannot invoke
        // (it is private NeoVM contract code). CanonicalBufferLayout pins the off-chain
        // encoder against a buffer assembled from the *documented* layout — but that mirror
        // buffer lives in this test file, so a refactor touching both the encoder and the
        // mirror in lockstep would stay green. This literal was computed OUT OF BAND (not by
        // the C# encoder) for the canonical input below; it breaks on any drift in either,
        // and is the value a contract reviewer re-derives against ComputeWithdrawalLeafHash.
        //
        // Canonical input — symmetric UInt160 bytes so GetSpan() byte-order is irrelevant:
        //   chainId=1, emitting=0xAA*20, l2Sender=0xBB*20, l1Recipient=0xCC*20,
        //   l2Asset=0xDD*20, amount=1000 (0xE803 unsigned-LE), nonce=1
        //   buf = 01000000 | AA*20 | BB*20 | CC*20 | DD*20 | 02000000 | E803 | 0100000000000000
        //   leaf = Sha256(Sha256(buf))
        var wd = Wd(emitting: A(), l2sender: B(), l1recipient: C(), l2asset: D());
        var golden = UInt256.Parse("0xe16deec3f35f8c64d2f2f11ed10a498374855a499da9eea4ad33bbb134c4ad76");
        Assert.AreEqual(golden, MessageHasher.HashWithdrawal(wd),
            "withdrawal-leaf hash drifted from the frozen on-chain parity vector");
    }

    [TestMethod]
    public void DecodeMessage_RoundTripsCanonicalPreimage()
    {
        var original = Msg(sender: A(), receiver: B()) with
        {
            SourceChainId = 1001,
            TargetChainId = 2002,
            Nonce = ulong.MaxValue,
            MessageType = MessageType.Call,
            Payload = new byte[] { 0xAA, 0xBB, 0xCC },
        };

        var decoded = MessageHasher.DecodeMessage(MessageHasher.EncodeMessage(original));

        Assert.AreEqual(original.SourceChainId, decoded.SourceChainId);
        Assert.AreEqual(original.TargetChainId, decoded.TargetChainId);
        Assert.AreEqual(original.Nonce, decoded.Nonce);
        Assert.AreEqual(original.Sender, decoded.Sender);
        Assert.AreEqual(original.Receiver, decoded.Receiver);
        Assert.AreEqual(original.MessageType, decoded.MessageType);
        CollectionAssert.AreEqual(original.Payload.ToArray(), decoded.Payload.ToArray());
        Assert.AreEqual(MessageHasher.HashMessage(original), decoded.MessageHash);
    }

    [TestMethod]
    public void DecodeMessage_RejectsTruncatedUnknownTypeAndInvalidLengths()
    {
        Assert.ThrowsExactly<InvalidDataException>(
            () => MessageHasher.DecodeMessage(new byte[60]));

        var encoded = MessageHasher.EncodeMessage(Msg(sender: A(), receiver: B()));
        encoded[56] = byte.MaxValue;
        Assert.ThrowsExactly<InvalidDataException>(() => MessageHasher.DecodeMessage(encoded));

        encoded = MessageHasher.EncodeMessage(Msg(sender: A(), receiver: B()));
        BinaryPrimitives.WriteInt32LittleEndian(encoded.AsSpan(57, 4), -1);
        Assert.ThrowsExactly<InvalidDataException>(() => MessageHasher.DecodeMessage(encoded));

        encoded = MessageHasher.EncodeMessage(Msg(sender: A(), receiver: B()));
        BinaryPrimitives.WriteInt32LittleEndian(
            encoded.AsSpan(57, 4),
            MessageHasher.MaxMessagePayloadBytes + 1);
        Assert.ThrowsExactly<InvalidDataException>(() => MessageHasher.DecodeMessage(encoded));

        encoded = MessageHasher.EncodeMessage(Msg(sender: A(), receiver: B()));
        BinaryPrimitives.WriteInt32LittleEndian(encoded.AsSpan(57, 4), 1);
        Assert.ThrowsExactly<InvalidDataException>(() => MessageHasher.DecodeMessage(encoded));
    }

    [TestMethod]
    public void DecodeWithdrawal_RoundTripsCanonicalPreimage()
    {
        var original = Wd(emitting: A(), l2sender: B(), l1recipient: C(), l2asset: D()) with
        {
            ChainId = uint.MaxValue,
            Amount = BigInteger.One << 504,
            Nonce = ulong.MaxValue,
        };

        var decoded = MessageHasher.DecodeWithdrawal(MessageHasher.EncodeWithdrawal(original));

        Assert.AreEqual(original, decoded);
        Assert.AreEqual(MessageHasher.HashWithdrawal(original), MessageHasher.HashWithdrawal(decoded));
    }

    [TestMethod]
    public void DecodeWithdrawal_RejectsTruncatedAndInvalidLengths()
    {
        Assert.ThrowsExactly<InvalidDataException>(
            () => MessageHasher.DecodeWithdrawal(new byte[95]));

        var encoded = MessageHasher.EncodeWithdrawal(
            Wd(emitting: A(), l2sender: B(), l1recipient: C(), l2asset: D()));
        BinaryPrimitives.WriteInt32LittleEndian(encoded.AsSpan(84, 4), -1);
        Assert.ThrowsExactly<InvalidDataException>(() => MessageHasher.DecodeWithdrawal(encoded));

        encoded = MessageHasher.EncodeWithdrawal(
            Wd(emitting: A(), l2sender: B(), l1recipient: C(), l2asset: D()));
        BinaryPrimitives.WriteInt32LittleEndian(encoded.AsSpan(84, 4), 65);
        Assert.ThrowsExactly<InvalidDataException>(() => MessageHasher.DecodeWithdrawal(encoded));

        encoded = MessageHasher.EncodeWithdrawal(
            Wd(emitting: A(), l2sender: B(), l1recipient: C(), l2asset: D()));
        BinaryPrimitives.WriteInt32LittleEndian(encoded.AsSpan(84, 4), 1);
        Assert.ThrowsExactly<InvalidDataException>(() => MessageHasher.DecodeWithdrawal(encoded));
    }
}
