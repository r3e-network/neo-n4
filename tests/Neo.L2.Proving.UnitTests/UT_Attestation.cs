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
}
