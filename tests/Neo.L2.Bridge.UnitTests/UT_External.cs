using System.Numerics;
using Neo.L2.Bridge.External;
using Neo.L2.Messaging;

namespace Neo.L2.Bridge.UnitTests;

[TestClass]
public class UT_External_AssetTransferPayload
{
    [TestMethod]
    public void Encode_RoundTrips()
    {
        var p = new ExternalAssetTransferPayload
        {
            ForeignAsset = UInt160.Parse("0x" + new string('a', 40)),
            Amount = new BigInteger(1_000_000),
        };
        var bytes = p.Encode();
        var back = ExternalAssetTransferPayload.Decode(bytes);
        Assert.AreEqual(p.ForeignAsset, back.ForeignAsset);
        Assert.AreEqual(p.Amount, back.Amount);
    }

    [TestMethod]
    public void Encode_RejectsNonPositiveAmount()
    {
        foreach (var amount in new[] { new BigInteger(-1), BigInteger.Zero })
        {
            var payload = new ExternalAssetTransferPayload
            {
                ForeignAsset = UInt160.Parse("0x" + new string('a', 40)),
                Amount = amount,
            };
            Assert.ThrowsExactly<InvalidOperationException>(() => payload.Encode());
        }
    }

    [TestMethod]
    public void EncodeAndDecode_RejectZeroForeignAsset()
    {
        var payload = new ExternalAssetTransferPayload
        {
            ForeignAsset = UInt160.Zero,
            Amount = BigInteger.One,
        };
        Assert.ThrowsExactly<InvalidOperationException>(() => payload.Encode());

        var bytes = new byte[25];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20, 4), 1);
        bytes[24] = 1;
        Assert.ThrowsExactly<ArgumentException>(
            () => ExternalAssetTransferPayload.Decode(bytes));
    }

    [TestMethod]
    public void Encode_TopBitSetAmount_HasNoSignByte_AndRoundTrips()
    {
        // The cases the signed→unsigned switch actually changes: an amount whose top byte has the
        // high bit set must NOT gain a trailing 0x00 sign byte (which signed ToByteArray() added),
        // so the wire stays byte-for-byte interoperable with the foreign-chain router/payout adapter.
        var cases = new (BigInteger amount, int expectedLen)[]
        {
            (new BigInteger(0x80), 1),    // 128  → [0x80]
            (new BigInteger(0xFF), 1),    // 255  → [0xFF]
            (new BigInteger(0x8000), 2),  // 32768 → [0x00,0x80]
        };
        foreach (var (amount, expectedLen) in cases)
        {
            var p = new ExternalAssetTransferPayload
            {
                ForeignAsset = UInt160.Parse("0x" + new string('a', 40)),
                Amount = amount,
            };
            var bytes = p.Encode();
            var amountLen = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(20, 4));
            Assert.AreEqual(expectedLen, amountLen, $"amount {amount} must encode minimal-unsigned-LE with no sign byte");
            Assert.AreEqual(amount, ExternalAssetTransferPayload.Decode(bytes).Amount);
        }
    }

    [TestMethod]
    public void Decode_RejectsTrailingBytes()
    {
        var p = new ExternalAssetTransferPayload
        {
            ForeignAsset = UInt160.Parse("0x" + new string('a', 40)),
            Amount = new BigInteger(1_000_000),
        };
        var bytes = p.Encode();
        var padded = new byte[bytes.Length + 1];
        bytes.CopyTo(padded, 0);
        Assert.ThrowsExactly<ArgumentException>(() => ExternalAssetTransferPayload.Decode(padded));
    }

    [TestMethod]
    public void Decode_RejectsTruncated()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => ExternalAssetTransferPayload.Decode(new byte[10]));
    }

    [TestMethod]
    public void Decode_RejectsZeroNonMinimalAndWiderThanUint256Amounts()
    {
        static byte[] Payload(int amountLength, byte[] amount)
        {
            var bytes = new byte[24 + amount.Length];
            UInt160.Parse("0x" + new string('a', 40)).GetSpan().CopyTo(bytes);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(20, 4), amountLength);
            amount.CopyTo(bytes, 24);
            return bytes;
        }

        Assert.ThrowsExactly<ArgumentException>(
            () => ExternalAssetTransferPayload.Decode(Payload(0, [])));
        Assert.ThrowsExactly<ArgumentException>(
            () => ExternalAssetTransferPayload.Decode(Payload(1, [0])));
        Assert.ThrowsExactly<ArgumentException>(
            () => ExternalAssetTransferPayload.Decode(Payload(2, [1, 0])));
        Assert.ThrowsExactly<ArgumentException>(
            () => ExternalAssetTransferPayload.Decode(Payload(33, Enumerable.Repeat((byte)1, 33).ToArray())));
    }
}

[TestClass]
public class UT_External_MpcCommitteePayload
{
    private static MpcSignature MakeSig(byte fill, int keyLen) => new()
    {
        PublicKey = Enumerable.Repeat(fill, keyLen).ToArray(),
        Signature = Enumerable.Repeat((byte)(fill ^ 0xFF), 64).ToArray(),
    };

    [TestMethod]
    public void Encode_Secp256k1_RoundTrips()
    {
        var p = new MpcCommitteePayload
        {
            CurveTag = MpcCommitteePayload.CurveSecp256k1,
            Signatures = new[] { MakeSig(0x01, 33), MakeSig(0x02, 33), MakeSig(0x03, 33) },
        };
        var bytes = p.Encode();
        // Header (2B) + 3 × (33 + 64) = 2 + 291 = 293
        Assert.AreEqual(2 + 3 * (33 + 64), bytes.Length);
        var back = MpcCommitteePayload.Decode(bytes, MpcCommitteePayload.CurveSecp256k1);
        Assert.AreEqual(3, back.Signatures.Count);
        Assert.IsTrue(back.Signatures[0].PublicKey.Span.SequenceEqual(p.Signatures[0].PublicKey.Span));
        Assert.IsTrue(back.Signatures[2].Signature.Span.SequenceEqual(p.Signatures[2].Signature.Span));
    }

    [TestMethod]
    public void Encode_Ed25519_RoundTrips()
    {
        var p = new MpcCommitteePayload
        {
            CurveTag = MpcCommitteePayload.CurveEd25519,
            Signatures = new[] { MakeSig(0x10, 32), MakeSig(0x20, 32) },
        };
        var bytes = p.Encode();
        // Header (2B) + 2 × (32 + 64) = 2 + 192 = 194
        Assert.AreEqual(2 + 2 * (32 + 64), bytes.Length);
        var back = MpcCommitteePayload.Decode(bytes, MpcCommitteePayload.CurveEd25519);
        Assert.AreEqual(2, back.Signatures.Count);
    }

    [TestMethod]
    public void Encode_RejectsWrongPubkeyLength()
    {
        // 32B key with secp256k1 curve tag (which expects 33B) → reject.
        var p = new MpcCommitteePayload
        {
            CurveTag = MpcCommitteePayload.CurveSecp256k1,
            Signatures = new[] { MakeSig(0x01, 32) },
        };
        Assert.ThrowsExactly<ArgumentException>(() => p.Encode());
    }

    [TestMethod]
    public void Encode_RejectsWrongSignatureLength()
    {
        var p = new MpcCommitteePayload
        {
            CurveTag = MpcCommitteePayload.CurveSecp256k1,
            Signatures = new[] { new MpcSignature
            {
                PublicKey = Enumerable.Repeat((byte)0xAA, 33).ToArray(),
                Signature = new byte[63],   // not 64
            }},
        };
        Assert.ThrowsExactly<ArgumentException>(() => p.Encode());
    }

    [TestMethod]
    public void Decode_RejectsCurveLengthMismatch()
    {
        // Encode as secp256k1 (33B keys), decode as ed25519 (expects 32B keys)
        // — length mismatch.
        var p = new MpcCommitteePayload
        {
            CurveTag = MpcCommitteePayload.CurveSecp256k1,
            Signatures = new[] { MakeSig(0x01, 33) },
        };
        var bytes = p.Encode();
        Assert.ThrowsExactly<ArgumentException>(
            () => MpcCommitteePayload.Decode(bytes, MpcCommitteePayload.CurveEd25519));
    }

    [TestMethod]
    public void Decode_RejectsUnknownCurveTag()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => MpcCommitteePayload.Decode(new byte[10], curveTag: 99));
    }

    [TestMethod]
    public void Encode_RejectsAboveMaxSigners()
    {
        var sigs = new MpcSignature[MpcCommitteePayload.MaxSigners + 1];
        for (var i = 0; i < sigs.Length; i++) sigs[i] = MakeSig((byte)i, 33);
        var p = new MpcCommitteePayload
        {
            CurveTag = MpcCommitteePayload.CurveSecp256k1,
            Signatures = sigs,
        };
        Assert.ThrowsExactly<InvalidOperationException>(() => p.Encode());
    }
}

[TestClass]
public class UT_External_EndToEnd
{
    /// <summary>
    /// Full path: build an ExternalCrossChainMessage, hash it canonically,
    /// "sign" it with three stub committee members, encode the proof,
    /// decode the proof back. This is what the off-chain watcher daemon
    /// will do in production (replacing the stub signer with a real
    /// secp256k1/ed25519 signer over an HSM). Pins the bytes-on-the-wire
    /// contract end-to-end.
    /// </summary>
    [TestMethod]
    public void Watcher_Roundtrip_BuildSignEncodeDecode()
    {
        var transfer = new ExternalAssetTransferPayload
        {
            ForeignAsset = UInt160.Parse("0x" + new string('e', 40)),
            Amount = new BigInteger(1_000_000),
        }.Encode();

        var msg = ExternalMessageBuilder.Build(
            externalChainId: 0xE000_0001U,                          // Eth mainnet
            neoChainId: 1099u,
            nonce: 7,
            direction: ExternalBridgeDirection.ForeignToNeo,
            sender: UInt160.Parse("0x" + new string('1', 40)),
            recipient: UInt160.Parse("0x" + new string('a', 40)),
            deadlineUnixSeconds: 1_900_000_000UL,
            sourceTxRef: UInt256.Parse("0x" + new string('e', 64)),
            messageType: ExternalMessageType.AssetTransfer,
            payload: transfer);

        Assert.AreNotEqual(UInt256.Zero, msg.MessageHash);

        // Three "signatures" — in production these would be real secp256k1.
        // The on-chain MpcCommitteeVerifier will reject these as invalid
        // signatures, but the wire-format round-trip is what we're pinning here.
        var proof = new MpcCommitteePayload
        {
            CurveTag = MpcCommitteePayload.CurveSecp256k1,
            Signatures = new[]
            {
                new MpcSignature { PublicKey = Enumerable.Repeat((byte)0x01, 33).ToArray(), Signature = Enumerable.Repeat((byte)0xA1, 64).ToArray() },
                new MpcSignature { PublicKey = Enumerable.Repeat((byte)0x02, 33).ToArray(), Signature = Enumerable.Repeat((byte)0xA2, 64).ToArray() },
                new MpcSignature { PublicKey = Enumerable.Repeat((byte)0x03, 33).ToArray(), Signature = Enumerable.Repeat((byte)0xA3, 64).ToArray() },
            },
        };
        var proofBytes = proof.Encode();
        var proofBack = MpcCommitteePayload.Decode(proofBytes, MpcCommitteePayload.CurveSecp256k1);
        Assert.AreEqual(3, proofBack.Signatures.Count);
        Assert.IsTrue(proofBack.Signatures[1].PublicKey.Span.SequenceEqual(
            proof.Signatures[1].PublicKey.Span));

        // Decode the asset-transfer payload back too.
        var transferBack = ExternalAssetTransferPayload.Decode(msg.Payload.Span);
        Assert.AreEqual(new BigInteger(1_000_000), transferBack.Amount);
    }
}
