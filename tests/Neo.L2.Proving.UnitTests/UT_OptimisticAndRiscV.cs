using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.L2.Batch;
using Neo.L2.Proving.Optimistic;
using Neo.L2.Proving.RiscVZk;

namespace Neo.L2.Proving.UnitTests;

[TestClass]
public class UT_OptimisticAndRiscV
{
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
    public async Task Optimistic_VerifierAcceptsValidSequencerSig()
    {
        var priv = new byte[32];
        for (var i = 0; i < 32; i++) priv[i] = (byte)(i + 1);
        var pub = ECCurve.Secp256r1.G * priv;

        var inputs = SamplePublicInputs();
        var canonical = BatchSerializer.EncodePublicInputs(inputs);
        var sig = Crypto.Sign(canonical, priv);

        var payload = new OptimisticProofPayload
        {
            BondContract = UInt160.Parse("0x" + new string('b', 40)),
            BondTxHash = UInt256.Parse("0x" + new string('c', 64)),
            SubmittedAt = 1_700_000_000_000,
            SequencerSignature = sig,
        };

        var verifier = new OptimisticVerifier(pub);
        var result = await verifier.VerifyAsync(inputs, payload.Encode());
        Assert.IsTrue(result.Valid, result.FailureReason);
    }

    [TestMethod]
    public async Task Optimistic_VerifierRejectsBadSignature()
    {
        var priv = new byte[32]; priv[0] = 1;
        var realPub = ECCurve.Secp256r1.G * priv;
        var fakePriv = new byte[32]; fakePriv[0] = 2;

        var inputs = SamplePublicInputs();
        var canonical = BatchSerializer.EncodePublicInputs(inputs);
        var sig = Crypto.Sign(canonical, fakePriv); // signed with the wrong key

        var payload = new OptimisticProofPayload
        {
            BondContract = UInt160.Zero,
            BondTxHash = UInt256.Zero,
            SubmittedAt = 0,
            SequencerSignature = sig,
        };

        var result = await new OptimisticVerifier(realPub).VerifyAsync(inputs, payload.Encode());
        Assert.IsFalse(result.Valid);
    }

    [TestMethod]
    public async Task RiscV_MockProverVerifierRoundTrip()
    {
        var vkId = UInt256.Parse("0x" + new string('f', 64));
        var prover = new MockRiscVProver(vkId);
        var verifier = new MockRiscVVerifier(vkId);

        var inputs = SamplePublicInputs();
        var result = await prover.ProveAsync(new ProofRequest
        {
            PublicInputs = inputs,
            Witness = ReadOnlyMemory<byte>.Empty,
            Kind = ProofType.Zk,
        });

        Assert.AreEqual(ProofType.Zk, result.Kind);
        var verify = await verifier.VerifyAsync(inputs, result.Proof);
        Assert.IsTrue(verify.Valid, verify.FailureReason);
    }

    [TestMethod]
    public async Task RiscV_MockVerifierRejectsWrongVk()
    {
        var goodVk = UInt256.Parse("0x" + new string('a', 64));
        var badVk = UInt256.Parse("0x" + new string('b', 64));
        var prover = new MockRiscVProver(goodVk);
        var verifier = new MockRiscVVerifier(badVk);

        var inputs = SamplePublicInputs();
        var result = await prover.ProveAsync(new ProofRequest { PublicInputs = inputs, Witness = ReadOnlyMemory<byte>.Empty, Kind = ProofType.Zk });
        var verify = await verifier.VerifyAsync(inputs, result.Proof);
        Assert.IsFalse(verify.Valid);
    }

    [TestMethod]
    public async Task Registry_DispatchesByKind()
    {
        var vkId = UInt256.Parse("0x" + new string('f', 64));
        var registry = new VerifierRegistry();
        registry.Register(new MockRiscVVerifier(vkId));
        Assert.AreEqual(1, registry.Count);
        Assert.IsTrue(registry.IsRegistered(ProofType.Zk));

        var prover = new MockRiscVProver(vkId);
        var inputs = SamplePublicInputs();
        var proof = await prover.ProveAsync(new ProofRequest { PublicInputs = inputs, Witness = ReadOnlyMemory<byte>.Empty, Kind = ProofType.Zk });

        var commitment = new L2BatchCommitment
        {
            ChainId = inputs.ChainId,
            BatchNumber = inputs.BatchNumber,
            FirstBlock = 100,
            LastBlock = 200,
            PreStateRoot = inputs.PreStateRoot,
            PostStateRoot = inputs.PostStateRoot,
            TxRoot = inputs.TxRoot,
            ReceiptRoot = inputs.ReceiptRoot,
            WithdrawalRoot = inputs.WithdrawalRoot,
            L2ToL1MessageRoot = inputs.L2ToL1MessageRoot,
            L2ToL2MessageRoot = inputs.L2ToL2MessageRoot,
            DACommitment = inputs.DACommitment,
            PublicInputHash = proof.PublicInputHash,
            ProofType = ProofType.Zk,
            Proof = proof.Proof,
        };

        var verify = await registry.VerifyAsync(commitment, inputs);
        Assert.IsTrue(verify.Valid, verify.FailureReason);
    }

    [TestMethod]
    public async Task Registry_FailsWhenCommitmentDisagreesWithInputs()
    {
        var vkId = UInt256.Parse("0x" + new string('f', 64));
        var registry = new VerifierRegistry();
        registry.Register(new MockRiscVVerifier(vkId));

        var prover = new MockRiscVProver(vkId);
        var inputs = SamplePublicInputs();
        var proof = await prover.ProveAsync(new ProofRequest { PublicInputs = inputs, Witness = ReadOnlyMemory<byte>.Empty, Kind = ProofType.Zk });

        var commitment = new L2BatchCommitment
        {
            ChainId = inputs.ChainId,
            BatchNumber = 999,           // mismatch
            FirstBlock = 100, LastBlock = 200,
            PreStateRoot = inputs.PreStateRoot,
            PostStateRoot = inputs.PostStateRoot,
            TxRoot = inputs.TxRoot, ReceiptRoot = inputs.ReceiptRoot,
            WithdrawalRoot = inputs.WithdrawalRoot,
            L2ToL1MessageRoot = inputs.L2ToL1MessageRoot,
            L2ToL2MessageRoot = inputs.L2ToL2MessageRoot,
            DACommitment = inputs.DACommitment,
            PublicInputHash = proof.PublicInputHash,
            ProofType = ProofType.Zk,
            Proof = proof.Proof,
        };

        var verify = await registry.VerifyAsync(commitment, inputs);
        Assert.IsFalse(verify.Valid);
    }

    [TestMethod]
    public void OptimisticProofPayload_ByteLayout_MatchesDocumentedOffsets()
    {
        // Pins the layout claimed in OptimisticProofPayload's XML docs.
        var bond = UInt160.Parse("0x" + new string('a', 40));
        var bondTx = UInt256.Parse("0x" + new string('b', 64));
        var sig = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE };

        var payload = new OptimisticProofPayload
        {
            BondContract = bond,
            BondTxHash = bondTx,
            SubmittedAt = 0x1122334455667788,
            SequencerSignature = sig,
        };
        var bytes = payload.Encode();

        Assert.AreEqual(65 + sig.Length, bytes.Length);
        Assert.AreEqual(OptimisticProofPayload.Version, bytes[0]);
        CollectionAssert.AreEqual(bond.GetSpan().ToArray(), bytes[1..21]);
        CollectionAssert.AreEqual(bondTx.GetSpan().ToArray(), bytes[21..53]);
        Assert.AreEqual(0x1122334455667788UL, System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(53, 8)));
        Assert.AreEqual(sig.Length, System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(61, 4)));
        CollectionAssert.AreEqual(sig, bytes[65..]);
    }

    [TestMethod]
    public void RiscVProofPayload_ByteLayout_MatchesDocumentedOffsets()
    {
        // Pins the layout claimed in RiscVProofPayload's XML docs.
        var vk = UInt256.Parse("0x" + new string('c', 64));
        var proofBytes = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55 };

        var payload = new RiscVProofPayload
        {
            ProofSystem = ProofSystem.Sp1,
            ProofBytes = proofBytes,
            VerificationKeyId = vk,
        };
        var bytes = payload.Encode();

        Assert.AreEqual(38 + proofBytes.Length, bytes.Length);
        Assert.AreEqual(RiscVProofPayload.Version, bytes[0]);
        Assert.AreEqual((byte)ProofSystem.Sp1, bytes[1]);
        CollectionAssert.AreEqual(vk.GetSpan().ToArray(), bytes[2..34]);
        Assert.AreEqual(proofBytes.Length, System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(34, 4)));
        CollectionAssert.AreEqual(proofBytes, bytes[38..]);
    }
}
