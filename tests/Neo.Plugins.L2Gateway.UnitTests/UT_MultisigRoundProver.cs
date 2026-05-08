using System;
using System.Linq;
using Neo;
using Neo.Cryptography.ECC;
using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.Proving.Attestation;
using Neo.Plugins.L2Gateway;

namespace Neo.Plugins.L2Gateway.UnitTests;

/// <summary>
/// Tests for the production-grade <see cref="MultisigRoundProver"/>: real Secp256r1
/// signatures, threshold enforcement, deterministic signer ordering, end-to-end
/// verifier round-trip.
/// </summary>
[TestClass]
public class UT_MultisigRoundProver
{
    private static (ECPoint pub, byte[] priv) GenKey(byte seed)
    {
        var priv = new byte[32];
        for (var i = 0; i < 32; i++) priv[i] = (byte)(seed + i);
        return (ECCurve.Secp256r1.G * priv, priv);
    }

    private static RoundResult Leaf(byte seed)
    {
        var bytes = new byte[32];
        for (var i = 0; i < 32; i++) bytes[i] = (byte)(seed + i);
        return new RoundResult
        {
            MessageRootContribution = new UInt256(bytes),
            ProofBytes = new byte[] { seed, (byte)(seed + 1) },
        };
    }

    [TestMethod]
    public void Combine_FullCommittee_ProducesVerifiableSignatures()
    {
        var keys = Enumerable.Range(1, 4).Select(i => GenKey((byte)i)).ToList();
        var signers = new InMemorySignerSet(keys);
        var prover = new MultisigRoundProver(signers, threshold: 3);

        var l = Leaf(0x10);
        var r = Leaf(0x20);
        var combined = prover.Combine(l, r);

        // Verifies via the prover's static checker.
        Assert.IsTrue(MultisigRoundProver.VerifyRound(combined, l, r, prover.ValidatorKeys, threshold: 3));

        // Decoded payload must contain all 4 sigs (full committee signed) — the
        // prover threshold (3) is the FLOOR, not the cap.
        var payload = MultisigProofPayload.Decode(combined.ProofBytes.Span);
        Assert.AreEqual(4, payload.Signatures.Count);
    }

    [TestMethod]
    public void VerifyRound_RejectsWhenThresholdRaisedAboveActualSignerCount()
    {
        var keys = Enumerable.Range(1, 3).Select(i => GenKey((byte)i)).ToList();
        var signers = new InMemorySignerSet(keys);
        var prover = new MultisigRoundProver(signers, threshold: 2);

        var l = Leaf(0x30);
        var r = Leaf(0x40);
        var combined = prover.Combine(l, r);

        // The proof carries 3 sigs (full committee). VerifyRound with threshold=2 passes;
        // verifying with threshold=4 (more than the committee size) fails because at
        // most 3 signatures exist.
        Assert.IsTrue(MultisigRoundProver.VerifyRound(combined, l, r, prover.ValidatorKeys, threshold: 2));
        Assert.IsFalse(MultisigRoundProver.VerifyRound(combined, l, r, prover.ValidatorKeys, threshold: 4));
    }

    [TestMethod]
    public void VerifyRound_RejectsTamperedRightChild()
    {
        // A verifier checking with the WRONG `right` (operator slipped a different
        // commitment) must fail — the canonical-message hash differs, so no valid
        // signature exists for the falsified pairing.
        var keys = Enumerable.Range(1, 3).Select(i => GenKey((byte)i)).ToList();
        var signers = new InMemorySignerSet(keys);
        var prover = new MultisigRoundProver(signers, threshold: 2);

        var l = Leaf(0x50);
        var realRight = Leaf(0x60);
        var combined = prover.Combine(l, realRight);

        var tamperedRight = Leaf(0x61); // off-by-one
        Assert.IsFalse(MultisigRoundProver.VerifyRound(combined, l, tamperedRight, prover.ValidatorKeys, threshold: 2));
    }

    [TestMethod]
    public void VerifyRound_RejectsSignaturesFromOutsideValidatorSet()
    {
        // Even if 3 legitimate-shape signatures verify against arbitrary keys,
        // VerifyRound only counts signatures whose public key is in the canonical
        // validator set. A prover swapping in outsider keys cannot pass the check.
        var insiders = Enumerable.Range(1, 4).Select(i => GenKey((byte)i)).ToList();
        var outsiders = Enumerable.Range(50, 4).Select(i => GenKey((byte)i)).ToList();
        var prover = new MultisigRoundProver(new InMemorySignerSet(outsiders), threshold: 3);

        var l = Leaf(0x70);
        var r = Leaf(0x80);
        var combined = prover.Combine(l, r);

        // Verify against the INSIDER set — outsiders' signatures should not count.
        var insiderKeys = insiders.Select(k => k.pub).ToList();
        Assert.IsFalse(MultisigRoundProver.VerifyRound(combined, l, r, insiderKeys, threshold: 1));
    }

    [TestMethod]
    public void Combine_OddTrailingChild_PromotedUnchanged()
    {
        // Mirrors PassThroughRoundProver's Merkle odd-leaf rule. A round with right=null
        // returns left verbatim — no signing, no proof-bytes wrap.
        var keys = Enumerable.Range(1, 3).Select(i => GenKey((byte)i)).ToList();
        var prover = new MultisigRoundProver(new InMemorySignerSet(keys), threshold: 2);
        var left = Leaf(0x90);
        var promoted = prover.Combine(left, null);
        Assert.AreSame(left, promoted, "right=null promotes left object identity");
    }

    [TestMethod]
    public void Combine_ProducesDeterministicSignerOrdering_AndIdenticalMessageRoots()
    {
        // ECDSA is non-deterministic (random k), so two Combine calls produce DIFFERENT
        // signature bytes for the same input. What MUST be deterministic is the signer
        // ordering inside the encoded payload (sorted by pubkey) and the combined
        // MessageRootContribution. Pin both so a refactor that reverses the sort or
        // changes the hash convention surfaces here.
        var keys = Enumerable.Range(1, 3).Select(i => GenKey((byte)i)).ToList();
        var pA = new MultisigRoundProver(new InMemorySignerSet(keys), threshold: 2);
        var pB = new MultisigRoundProver(new InMemorySignerSet(keys), threshold: 2);

        var l = Leaf(0xA0);
        var r = Leaf(0xB0);
        var a = pA.Combine(l, r);
        var b = pB.Combine(l, r);

        Assert.AreEqual(a.MessageRootContribution, b.MessageRootContribution,
            "combined Merkle root must be deterministic");

        var payloadA = MultisigProofPayload.Decode(a.ProofBytes.Span);
        var payloadB = MultisigProofPayload.Decode(b.ProofBytes.Span);
        Assert.AreEqual(payloadA.Signatures.Count, payloadB.Signatures.Count);
        // Pubkey order must match across the two payloads.
        for (var i = 0; i < payloadA.Signatures.Count; i++)
        {
            Assert.AreEqual(payloadA.Signatures[i].PublicKey, payloadB.Signatures[i].PublicKey,
                $"signer {i} pubkey must be in canonical (sorted) order across encodings");
        }
        // Pubkey list must be sorted (the canonical order).
        for (var i = 1; i < payloadA.Signatures.Count; i++)
        {
            Assert.IsTrue(
                payloadA.Signatures[i - 1].PublicKey.CompareTo(payloadA.Signatures[i].PublicKey) <= 0,
                "signers must be in ascending pubkey order");
        }
    }

    [TestMethod]
    public void Ctor_ThresholdAboveValidatorCount_Rejected()
    {
        var keys = Enumerable.Range(1, 2).Select(i => GenKey((byte)i)).ToList();
        var signers = new InMemorySignerSet(keys);
        Assert.ThrowsExactly<ArgumentException>(() => new MultisigRoundProver(signers, threshold: 3));
    }

    [TestMethod]
    public void Ctor_ThresholdZeroOrNegative_Rejected()
    {
        var keys = Enumerable.Range(1, 2).Select(i => GenKey((byte)i)).ToList();
        var signers = new InMemorySignerSet(keys);
        Assert.ThrowsExactly<ArgumentException>(() => new MultisigRoundProver(signers, threshold: 0));
        Assert.ThrowsExactly<ArgumentException>(() => new MultisigRoundProver(signers, threshold: -1));
    }

    [TestMethod]
    public void EndToEnd_AggregatorWithMultisigProver_ProducesSignedAggregate()
    {
        // Full pipeline: BinaryTreeAggregator + MultisigRoundProver + 4 leaves.
        // Each round signs; the aggregate's BackendId reflects the multisig prover.
        var keys = Enumerable.Range(1, 4).Select(i => GenKey((byte)i)).ToList();
        var prover = new MultisigRoundProver(new InMemorySignerSet(keys), threshold: 3);
        var aggregator = new BinaryTreeAggregator(prover);

        for (var i = 0; i < 4; i++)
            aggregator.Submit(MakeBatch((byte)(0xA0 + i)));

        var result = aggregator.Aggregate();
        Assert.IsNotNull(result);
        Assert.AreEqual(MultisigRoundProver.ConstBackendId, result.BackendId);
        Assert.AreEqual(4, result.Constituents.Count);
        Assert.IsTrue(result.AggregatedProof.Length > 0, "aggregated proof must be non-empty");
    }

    private static L2BatchCommitment MakeBatch(byte seed)
    {
        var rootBytes = new byte[32];
        for (var i = 0; i < 32; i++) rootBytes[i] = (byte)(seed + i);
        return new L2BatchCommitment
        {
            ChainId = 1099,
            BatchNumber = seed,
            FirstBlock = seed * 100UL,
            LastBlock = seed * 100UL + 99,
            PreStateRoot = UInt256.Zero,
            PostStateRoot = UInt256.Zero,
            TxRoot = UInt256.Zero,
            ReceiptRoot = UInt256.Zero,
            WithdrawalRoot = UInt256.Zero,
            L2ToL1MessageRoot = UInt256.Zero,
            L2ToL2MessageRoot = new UInt256(rootBytes),
            DACommitment = UInt256.Zero,
            PublicInputHash = UInt256.Zero,
            ProofType = ProofType.Multisig,
            Proof = new byte[] { seed },
        };
    }
}
