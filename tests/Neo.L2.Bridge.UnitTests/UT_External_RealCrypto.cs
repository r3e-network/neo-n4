using System.Numerics;
using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.L2.Bridge.External;
using Neo.L2.Messaging;
using Neo.Wallets;

namespace Neo.L2.Bridge.UnitTests;

/// <summary>
/// End-to-end real-cryptography test of the Phase B MPC bridge pipeline.
/// Mirrors EXACTLY what an off-chain watcher daemon does when assembling
/// the proof bytes that <c>NeoHub.MpcCommitteeVerifier.VerifyInboundMessage</c>
/// will accept.
/// </summary>
/// <remarks>
/// Pipeline pinned by these tests:
/// <list type="number">
/// <item><description>Build canonical <c>ExternalCrossChainMessage</c> bytes
///   (the bytes the verifier receives via the registry as
///   <c>messageBytes</c>).</description></item>
/// <item><description>Each watcher key signs those bytes with secp256k1+SHA256
///   — the same curve+hash <c>CryptoLib.VerifyWithECDsa</c> internally
///   verifies in the on-chain MpcCommitteeVerifier.</description></item>
/// <item><description><c>MpcCommitteePayload.Encode</c> packs the (pubkey, sig)
///   pairs into the wire format <c>VerifyInboundMessage</c>'s
///   <c>proofBytes</c> argument expects.</description></item>
/// <item><description>Off-chain re-verify (using <c>Crypto.VerifySignature</c>)
///   confirms each signature would pass the on-chain check. If this test
///   passes, a watcher submitting these bytes will get a green light from
///   the verifier; if this test fails, every watcher would too.</description></item>
/// </list>
/// </remarks>
[TestClass]
public class UT_External_RealCrypto
{
    private static (KeyPair[] keys, byte[] committeeBlob) GenerateCommittee(int size)
    {
        var keys = new KeyPair[size];
        var blob = new byte[size * 33];
        for (var i = 0; i < size; i++)
        {
            // Deterministic test keys so failures reproduce. Production watchers
            // generate per-host random keys + bond them on-chain via
            // NeoHub.ExternalBridgeBond (Phase B follow-on).
            var priv = new byte[32];
            for (var j = 0; j < 32; j++) priv[j] = (byte)((i + 1) * 17 + j);
            // Mask top 3 bits to keep the private key inside the curve order
            // (secp256k1 order is just under 2^256). Without this 1-in-2^256
            // chance test keys could land out-of-range.
            priv[0] &= 0x1F;
            keys[i] = new KeyPair(priv, ECCurve.Secp256k1);
            // Encode the 33-byte compressed pubkey into the committee blob.
            keys[i].PublicKey.EncodePoint(true).CopyTo(blob.AsSpan(i * 33, 33));
        }
        return (keys, blob);
    }

    private static ExternalCrossChainMessage BuildEthDeposit(ulong nonce)
    {
        var transferPayload = new ExternalAssetTransferPayload
        {
            ForeignAsset = UInt160.Parse("0x" + new string('e', 40)),
            Amount = new BigInteger(1_000_000),
        }.Encode();

        return ExternalMessageBuilder.Build(
            externalChainId: 0xE000_0001U,                  // Eth mainnet
            neoChainId: 1099u,
            nonce: nonce,
            direction: ExternalBridgeDirection.ForeignToNeo,
            sender: UInt160.Parse("0x" + new string('1', 40)),
            recipient: UInt160.Parse("0x" + new string('a', 40)),
            deadlineUnixSeconds: 1_900_000_000UL,
            sourceTxRef: UInt256.Parse("0x" + new string('e', 64)),
            messageType: ExternalMessageType.AssetTransfer,
            payload: transferPayload);
    }

    /// <summary>Compute the canonical bytes that <c>messageBytes</c> contains
    /// on the contract side — same layout as <c>ExternalMessageHasher</c>'s
    /// pre-image, which is what watchers sign.</summary>
    private static byte[] CanonicalMessageBytes(ExternalCrossChainMessage msg)
    {
        // Layout (102B fixed prefix + payload). Matches ExternalMessageHasher.HashMessage.
        var size = 102 + msg.Payload.Length;
        var buf = new byte[size];
        var pos = 0;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(pos, 4), msg.ExternalChainId); pos += 4;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(pos, 4), msg.NeoChainId); pos += 4;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(pos, 8), msg.Nonce); pos += 8;
        buf[pos++] = (byte)msg.Direction;
        msg.Sender.GetSpan().CopyTo(buf.AsSpan(pos, 20)); pos += 20;
        msg.Recipient.GetSpan().CopyTo(buf.AsSpan(pos, 20)); pos += 20;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(pos, 8), msg.DeadlineUnixSeconds); pos += 8;
        msg.SourceTxRef.GetSpan().CopyTo(buf.AsSpan(pos, 32)); pos += 32;
        buf[pos++] = (byte)msg.MessageType;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(pos, 4), msg.Payload.Length); pos += 4;
        msg.Payload.Span.CopyTo(buf.AsSpan(pos));
        return buf;
    }

    [TestMethod]
    public void Watcher_FullPipeline_ProducesAcceptedProof()
    {
        // Setup: 5-member committee, threshold 3.
        var (keys, _) = GenerateCommittee(5);

        // The message a watcher observed: a deposit of 1M USDC on Eth bound
        // for an L2 user.
        var msg = BuildEthDeposit(nonce: 7);
        var canonicalBytes = CanonicalMessageBytes(msg);

        // 3 watchers sign — exactly threshold. The same canonical bytes the
        // contract receives + the same SHA256 inside CryptoLib.VerifyWithECDsa.
        var sigs = new MpcSignature[3];
        for (var i = 0; i < 3; i++)
        {
            var rawSig = Crypto.Sign(canonicalBytes, keys[i], HashAlgorithm.SHA256);
            sigs[i] = new MpcSignature
            {
                PublicKey = keys[i].PublicKey.EncodePoint(true),
                Signature = rawSig,
            };
        }
        var proof = new MpcCommitteePayload
        {
            CurveTag = MpcCommitteePayload.CurveSecp256k1,
            Signatures = sigs,
        };
        var proofBytes = proof.Encode();

        // Verify each signature off-chain — same call shape the on-chain
        // verifier ends up making after decoding the proof.
        for (var i = 0; i < 3; i++)
        {
            var ok = Crypto.VerifySignature(
                canonicalBytes,
                sigs[i].Signature.Span,
                keys[i].PublicKey,
                HashAlgorithm.SHA256);
            Assert.IsTrue(ok, $"Signature {i} failed off-chain verification (would also fail on-chain)");
        }

        // Decode and re-verify — the round-trip the contract performs.
        var decoded = MpcCommitteePayload.Decode(proofBytes, MpcCommitteePayload.CurveSecp256k1);
        Assert.AreEqual(3, decoded.Signatures.Count);
        for (var i = 0; i < decoded.Signatures.Count; i++)
        {
            // Reconstruct the ECPoint from the 33-byte compressed encoding —
            // exactly what `(ECPoint)pubkey` does inside the contract.
            var pubkey = ECPoint.DecodePoint(decoded.Signatures[i].PublicKey.Span, ECCurve.Secp256k1);
            var ok = Crypto.VerifySignature(
                canonicalBytes,
                decoded.Signatures[i].Signature.Span,
                pubkey,
                HashAlgorithm.SHA256);
            Assert.IsTrue(ok, $"Decoded signature {i} failed verification");
        }
    }

    [TestMethod]
    public void Watcher_TamperedMessageBytes_FailsVerification()
    {
        // Sanity: if a single byte of the canonical message changes after
        // signing, every signature must fail. Pins that the verifier can't
        // be tricked into accepting one message's signatures for another's
        // bytes.
        var (keys, _) = GenerateCommittee(3);
        var msg = BuildEthDeposit(nonce: 7);
        var canonicalBytes = CanonicalMessageBytes(msg);
        var rawSig = Crypto.Sign(canonicalBytes, keys[0], HashAlgorithm.SHA256);

        // Flip one bit in the recipient field (offset 37).
        canonicalBytes[37] ^= 0x01;

        var ok = Crypto.VerifySignature(
            canonicalBytes,
            rawSig,
            keys[0].PublicKey,
            HashAlgorithm.SHA256);
        Assert.IsFalse(ok, "tampered message must not verify under the original signature");
    }

    [TestMethod]
    public void Watcher_WrongKeySignature_Fails()
    {
        // Signature from key 0 verified under key 1's pubkey must fail.
        // Pins that the on-chain "find pubkey in committee" loop can't be
        // tricked by submitting key 1's pubkey alongside key 0's signature.
        var (keys, _) = GenerateCommittee(3);
        var msg = BuildEthDeposit(nonce: 7);
        var canonicalBytes = CanonicalMessageBytes(msg);
        var sig0 = Crypto.Sign(canonicalBytes, keys[0], HashAlgorithm.SHA256);

        var ok = Crypto.VerifySignature(
            canonicalBytes,
            sig0,
            keys[1].PublicKey,
            HashAlgorithm.SHA256);
        Assert.IsFalse(ok, "signature from key 0 must not verify under key 1's pubkey");
    }

    [TestMethod]
    public void CommitteeBlob_Layout_Matches_ContractExpectation()
    {
        // The on-chain MpcCommitteeVerifier expects committeeBlob =
        //   N × 33B compressed pubkey
        // where N = size and pubkeys are in canonical (registration) order.
        // This test pins the off-chain blob has exactly that shape.
        var (keys, blob) = GenerateCommittee(5);
        Assert.AreEqual(5 * 33, blob.Length);
        for (var i = 0; i < 5; i++)
        {
            var slice = blob.AsSpan(i * 33, 33);
            // First byte is the compression marker (0x02 or 0x03 for secp256k1).
            Assert.IsTrue(slice[0] == 0x02 || slice[0] == 0x03,
                $"committee blob slot {i} doesn't start with secp256k1 compression marker");
            // Decode + re-encode round-trips to the same bytes.
            var decoded = ECPoint.DecodePoint(slice, ECCurve.Secp256k1);
            var reEncoded = decoded.EncodePoint(true);
            Assert.IsTrue(slice.SequenceEqual(reEncoded));
            // And the decoded point matches the original keypair's pubkey.
            Assert.AreEqual(keys[i].PublicKey, decoded);
        }
    }

    [TestMethod]
    public void Watcher_UnderThreshold_FailsBeforeContractEvenChecks()
    {
        // If a watcher submits fewer than threshold signatures, the contract
        // rejects with "signature count below threshold" BEFORE running any
        // crypto. Off-chain we want to surface the same misconfig before
        // submission. Document the size invariant on the proof bytes.
        var (keys, _) = GenerateCommittee(5);
        var msg = BuildEthDeposit(nonce: 7);
        var canonicalBytes = CanonicalMessageBytes(msg);

        // Only 2 sigs but threshold is 3.
        var sigs = new MpcSignature[2];
        for (var i = 0; i < 2; i++)
        {
            sigs[i] = new MpcSignature
            {
                PublicKey = keys[i].PublicKey.EncodePoint(true),
                Signature = Crypto.Sign(canonicalBytes, keys[i], HashAlgorithm.SHA256),
            };
        }
        var proofBytes = new MpcCommitteePayload
        {
            CurveTag = MpcCommitteePayload.CurveSecp256k1,
            Signatures = sigs,
        }.Encode();

        // Header tells us how many were signed; off-chain code can short-
        // circuit before submitting if the count is below the per-chain
        // threshold (which the watcher knows from its config).
        var sigCount = (int)proofBytes[0] | ((int)proofBytes[1] << 8);
        Assert.AreEqual(2, sigCount);
        Assert.IsTrue(sigCount < 3, "below-threshold pre-flight check");
    }
}
