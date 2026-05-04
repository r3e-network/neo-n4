using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.L2.Proving.Attestation;

namespace Neo.L2.Proving.UnitTests;

[TestClass]
public class UT_Attestation
{
    private static (ECPoint pub, byte[] priv) GenKey(byte seed)
    {
        var priv = new byte[32];
        for (var i = 0; i < 32; i++) priv[i] = (byte)(seed + i);
        var pub = ECCurve.Secp256r1.G * priv;
        return (pub, priv);
    }

    private static PublicInputs SamplePublicInputs() => new()
    {
        ChainId = 1001,
        BatchNumber = 7,
        PreStateRoot = UInt256.Parse("0x" + new string('1', 64)),
        PostStateRoot = UInt256.Parse("0x" + new string('2', 64)),
        TxRoot = UInt256.Parse("0x" + new string('3', 64)),
        ReceiptRoot = UInt256.Parse("0x" + new string('4', 64)),
        WithdrawalRoot = UInt256.Parse("0x" + new string('5', 64)),
        L2ToL1MessageRoot = UInt256.Parse("0x" + new string('6', 64)),
        L2ToL2MessageRoot = UInt256.Parse("0x" + new string('7', 64)),
        L1MessageHash = UInt256.Parse("0x" + new string('8', 64)),
        DACommitment = UInt256.Parse("0x" + new string('9', 64)),
        BlockContextHash = UInt256.Parse("0x" + new string('a', 64)),
    };

    [TestMethod]
    public async Task FullCycle_ProveThenVerify()
    {
        var keys = Enumerable.Range(1, 4).Select(i => GenKey((byte)i)).ToList();
        var signers = new InMemorySignerSet(keys);
        var prover = new AttestationProver(signers);
        var verifier = new AttestationVerifier(keys.Select(k => k.pub), threshold: 3);

        var inputs = SamplePublicInputs();
        var result = await prover.ProveAsync(new ProofRequest
        {
            PublicInputs = inputs,
            Witness = ReadOnlyMemory<byte>.Empty,
            Kind = ProofType.Multisig,
        });

        Assert.AreEqual(ProofType.Multisig, result.Kind);
        var verify = await verifier.VerifyAsync(inputs, result.Proof);
        Assert.IsTrue(verify.Valid, verify.FailureReason);
    }

    [TestMethod]
    public async Task Verify_FailsWhenSignerOutsideSet()
    {
        var insiders = Enumerable.Range(1, 3).Select(i => GenKey((byte)i)).ToList();
        var outsider = GenKey(99);

        var signers = new InMemorySignerSet(insiders.Concat(new[] { outsider }));
        var prover = new AttestationProver(signers);
        var verifier = new AttestationVerifier(insiders.Select(k => k.pub), threshold: 3);

        var inputs = SamplePublicInputs();
        var result = await prover.ProveAsync(new ProofRequest { PublicInputs = inputs, Witness = ReadOnlyMemory<byte>.Empty, Kind = ProofType.Multisig });

        var verify = await verifier.VerifyAsync(inputs, result.Proof);
        Assert.IsFalse(verify.Valid);
        StringAssert.Contains(verify.FailureReason ?? "", "unknown signer");
    }

    [TestMethod]
    public async Task Verify_FailsBelowThreshold()
    {
        var keys = Enumerable.Range(1, 5).Select(i => GenKey((byte)i)).ToList();
        // Sign with only 2 of the 5 validators.
        var signers = new InMemorySignerSet(keys.Take(2));
        var prover = new AttestationProver(signers);
        var verifier = new AttestationVerifier(keys.Select(k => k.pub), threshold: 4);

        var inputs = SamplePublicInputs();
        var result = await prover.ProveAsync(new ProofRequest { PublicInputs = inputs, Witness = ReadOnlyMemory<byte>.Empty, Kind = ProofType.Multisig });

        var verify = await verifier.VerifyAsync(inputs, result.Proof);
        Assert.IsFalse(verify.Valid);
        StringAssert.Contains(verify.FailureReason ?? "", "< threshold");
    }

    [TestMethod]
    public async Task Verify_FailsOnDuplicateSigner()
    {
        // Regression: previously the verifier sequence was validator-set → length →
        // sig-verify → dedup. A malicious prover could submit MaxSigners=256 copies of
        // one valid signature and force 256 redundant ECDSA verifications before the
        // duplicate check fired. Now: dedup runs BEFORE sig-verify, capping cost at
        // first occurrence per key.
        var keys = Enumerable.Range(1, 4).Select(i => GenKey((byte)i)).ToList();
        var signers = new InMemorySignerSet(keys);
        var prover = new AttestationProver(signers);
        var verifier = new AttestationVerifier(keys.Select(k => k.pub), threshold: 3);

        var inputs = SamplePublicInputs();
        var honest = await prover.ProveAsync(new ProofRequest { PublicInputs = inputs, Witness = ReadOnlyMemory<byte>.Empty, Kind = ProofType.Multisig });
        var honestPayload = MultisigProofPayload.Decode(honest.Proof.Span);

        // Build a payload that repeats the first signer twice.
        var repeated = new MultisigProofPayload
        {
            Signatures = new[] { honestPayload.Signatures[0], honestPayload.Signatures[0], honestPayload.Signatures[1], honestPayload.Signatures[2] },
        };
        var verify = await verifier.VerifyAsync(inputs, repeated.Encode());
        Assert.IsFalse(verify.Valid);
        StringAssert.Contains(verify.FailureReason ?? "", "duplicate signer");
    }

    [TestMethod]
    public async Task Verify_FailsWhenInputsTampered()
    {
        var keys = Enumerable.Range(1, 4).Select(i => GenKey((byte)i)).ToList();
        var signers = new InMemorySignerSet(keys);
        var prover = new AttestationProver(signers);
        var verifier = new AttestationVerifier(keys.Select(k => k.pub), threshold: 3);

        var inputs = SamplePublicInputs();
        var result = await prover.ProveAsync(new ProofRequest { PublicInputs = inputs, Witness = ReadOnlyMemory<byte>.Empty, Kind = ProofType.Multisig });

        var tampered = inputs with { BatchNumber = 999 };
        var verify = await verifier.VerifyAsync(tampered, result.Proof);
        Assert.IsFalse(verify.Valid);
    }

    [TestMethod]
    public void MultisigPayload_RoundTrips()
    {
        var k = GenKey(1);
        var sig = Crypto.Sign(new byte[] { 1, 2, 3 }, k.priv);
        var payload = new MultisigProofPayload
        {
            Signatures = new[] { new SignerSignature { PublicKey = k.pub, Signature = sig } },
        };
        var bytes = payload.Encode();
        var decoded = MultisigProofPayload.Decode(bytes);
        Assert.AreEqual(1, decoded.Signatures.Count);
        Assert.AreEqual(k.pub, decoded.Signatures[0].PublicKey);
    }

    [TestMethod]
    public void MultisigPayload_Decode_RejectsOversizedSignerCount()
    {
        // Craft a header claiming MaxSigners+1; decoder must reject before allocating.
        var oversized = MultisigProofPayload.MaxSigners + 1;
        var totalBytes = 3 + oversized * (33 + 64);
        var bigBuf = new byte[totalBytes];
        bigBuf[0] = MultisigProofPayload.Version;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bigBuf.AsSpan(1, 2), (ushort)oversized);

        Assert.ThrowsExactly<InvalidDataException>(() => MultisigProofPayload.Decode(bigBuf));
    }

    [TestMethod]
    public void MultisigPayload_Encode_RejectsBadSignatureLength()
    {
        // Regression for iter 159: previously Encode used Span.CopyTo(span.Slice(pos, 64))
        // which silently zero-pads when the source is < 64 bytes (only throws for source >
        // destination), producing a structurally-valid but semantically-wrong encoding —
        // the signature won't verify on the receiving side. Now caught at Encode time.
        var k = GenKey(1);
        var truncated = new byte[63];                          // off-by-one short
        var oversized = new byte[65];                          // off-by-one long
        var truncPayload = new MultisigProofPayload
        {
            Signatures = new[] { new SignerSignature { PublicKey = k.pub, Signature = truncated } },
        };
        var oversizePayload = new MultisigProofPayload
        {
            Signatures = new[] { new SignerSignature { PublicKey = k.pub, Signature = oversized } },
        };
        Assert.ThrowsExactly<ArgumentException>(() => truncPayload.Encode());
        Assert.ThrowsExactly<ArgumentException>(() => oversizePayload.Encode());
    }

    [TestMethod]
    public void MultisigPayload_Encode_RejectsNullSignaturesCollection()
    {
        // The required `Signatures` member can still hold null — `required` only enforces
        // "must be set," not "non-null." Without MultisigProofPayload.cs:31's
        // ArgumentNullException.ThrowIfNull(Signatures), Encode would NRE inside the
        // `Signatures.Count` access with no link back to the bad input.
        var bad = new MultisigProofPayload { Signatures = null! };
        Assert.ThrowsExactly<ArgumentNullException>(() => bad.Encode());
    }

    [TestMethod]
    public void MultisigPayload_Encode_RejectsNullPublicKey()
    {
        // Companion to RejectsNullSignerEntry. PublicKey is also `required` but reference-
        // typed (ECPoint); a SignerSignature with PublicKey = null reaches Encode and
        // would NRE on PublicKey.GetSpan() without MultisigProofPayload.cs:47's guard.
        var k = GenKey(1);
        var sig = Crypto.Sign(new byte[] { 1 }, k.priv);
        var payload = new MultisigProofPayload
        {
            Signatures = new[]
            {
                new SignerSignature { PublicKey = null!, Signature = sig },
            },
        };
        Assert.ThrowsExactly<ArgumentNullException>(() => payload.Encode());
    }

    [TestMethod]
    public void MultisigPayload_Encode_RejectsNullSignerEntry()
    {
        // Regression for iter 159: Signatures[i] is a reference type; even with `required`
        // forcing the collection to be set, individual entries can still be null. Without
        // the guard, Encode would NRE inside s.PublicKey.GetSpan() with no link to the
        // bad index. Now caught with the bad index in the exception message.
        var k = GenKey(1);
        var sig = Crypto.Sign(new byte[] { 1 }, k.priv);
        var payload = new MultisigProofPayload
        {
            Signatures = new SignerSignature?[]
            {
                new() { PublicKey = k.pub, Signature = sig },
                null,
                new() { PublicKey = k.pub, Signature = sig },
            }!,
        };
        var ex = Assert.ThrowsExactly<ArgumentException>(() => payload.Encode());
        StringAssert.Contains(ex.Message, "[1]");
    }

    [TestMethod]
    public void MultisigPayload_Encode_RejectsOversizedSignerCount()
    {
        // Encode/Decode symmetry — Decode rejects > MaxSigners (covered above by
        // RejectsOversizedSignerCount). Without the matching Encode-side check, a
        // producer could create bytes that round-trip Decode would refuse, hiding
        // the bug at the next consumer.
        var k = GenKey(1);
        var sig = Crypto.Sign(new byte[] { 1 }, k.priv);
        var sigs = new SignerSignature[MultisigProofPayload.MaxSigners + 1];
        for (var i = 0; i < sigs.Length; i++)
            sigs[i] = new SignerSignature { PublicKey = k.pub, Signature = sig };

        var payload = new MultisigProofPayload { Signatures = sigs };
        Assert.ThrowsExactly<InvalidOperationException>(() => payload.Encode());
    }

    [TestMethod]
    public void MultisigPayload_AcceptsExactlyMaxSigners()
    {
        // Boundary case: exactly MaxSigners=256 must succeed. Pairs with the reject test.
        var k = GenKey(1);
        var sig = Crypto.Sign(new byte[] { 1 }, k.priv);
        var sigs = new SignerSignature[MultisigProofPayload.MaxSigners];
        for (var i = 0; i < sigs.Length; i++)
            sigs[i] = new SignerSignature { PublicKey = k.pub, Signature = sig };

        var payload = new MultisigProofPayload { Signatures = sigs };
        var bytes = payload.Encode();
        var decoded = MultisigProofPayload.Decode(bytes);

        Assert.AreEqual(MultisigProofPayload.MaxSigners, decoded.Signatures.Count);
    }

    [TestMethod]
    public async Task AttestationProver_BuggySignerSetReturnsNull_SurfacesContractViolation()
    {
        // Regression for iter 174: a buggy ISignerSet that returns null from SignAsync
        // would propagate as a confusing NRE inside MultisigProofPayload.Encode's
        // null-guard (which would name "Signatures" but not the actual root cause).
        // Now surfaces as InvalidOperationException at the prover boundary.
        var prover = new AttestationProver(new NullReturningSigners());
        var inputs = SamplePublicInputs();
        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await prover.ProveAsync(new ProofRequest
            {
                PublicInputs = inputs,
                Witness = ReadOnlyMemory<byte>.Empty,
                Kind = ProofType.Multisig,
            }));
        StringAssert.Contains(ex.Message, "SignAsync");
    }

    private sealed class NullReturningSigners : ISignerSet
    {
        public IReadOnlyList<ECPoint> ValidatorKeys { get; } = Array.Empty<ECPoint>();
        public ValueTask<IReadOnlyList<SignerSignature>> SignAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default)
            => new ValueTask<IReadOnlyList<SignerSignature>>((IReadOnlyList<SignerSignature>)null!);
    }

    [TestMethod]
    public void AttestationProver_Constructor_RejectsNullSigners()
    {
        // Pin AttestationProver.cs:25.
        Assert.ThrowsExactly<ArgumentNullException>(() => new AttestationProver(null!));
    }

    [TestMethod]
    public async Task AttestationProver_ProveAsync_RejectsNullRequest()
    {
        // Pin AttestationProver.cs:32.
        var prover = new AttestationProver(new NullReturningSigners());
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await prover.ProveAsync(null!));
    }

    [TestMethod]
    public void AttestationVerifier_Constructor_RejectsNullValidators()
    {
        // Pin AttestationVerifier.cs:31.
        Assert.ThrowsExactly<ArgumentNullException>(() => new AttestationVerifier(null!, threshold: 1));
    }
}
