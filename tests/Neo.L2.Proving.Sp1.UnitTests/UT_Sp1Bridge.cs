namespace Neo.L2.Proving.Sp1.UnitTests;

[TestClass]
public class UT_Sp1Bridge
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
    public void Bridge_IsAvailable_FalseWhenLibraryMissing()
    {
        // The bridge .so isn't installed in dev environments; expect false.
        Assert.IsFalse(Sp1Bridge.IsAvailable);
    }

    [TestMethod]
    public void Bridge_Prove_WithoutLibraryReturnsNotImplemented()
    {
        var (status, proof) = Sp1Bridge.Prove(new byte[] { 0x01 });
        Assert.AreEqual(Sp1BridgeStatus.NotImplemented, status);
        Assert.IsNull(proof);
    }

    [TestMethod]
    public void Bridge_Verify_WithoutLibraryReturnsNotImplemented()
    {
        var status = Sp1Bridge.Verify(new byte[] { 0x01 });
        Assert.AreEqual(Sp1BridgeStatus.NotImplemented, status);
    }

    [TestMethod]
    public async Task Sp1Prover_FallsBackToMockWhenBridgeUnavailable()
    {
        var vkId = UInt256.Parse("0x" + new string('f', 64));
        var prover = new Sp1RiscVProver(vkId);
        Assert.IsFalse(prover.BridgeAvailable);

        var result = await prover.ProveAsync(new ProofRequest
        {
            PublicInputs = SamplePublicInputs(),
            Witness = ReadOnlyMemory<byte>.Empty,
            Kind = ProofType.Zk,
        });
        Assert.AreEqual(ProofType.Zk, result.Kind);
        Assert.IsTrue(result.Proof.Length > 0);
    }

    [TestMethod]
    public async Task Sp1Verifier_AcceptsMockFallbackProof()
    {
        var vkId = UInt256.Parse("0x" + new string('f', 64));
        var prover = new Sp1RiscVProver(vkId);
        var verifier = new Sp1RiscVVerifier(vkId);

        var inputs = SamplePublicInputs();
        var proof = await prover.ProveAsync(new ProofRequest
        {
            PublicInputs = inputs,
            Witness = ReadOnlyMemory<byte>.Empty,
            Kind = ProofType.Zk,
        });

        var verify = await verifier.VerifyAsync(inputs, proof.Proof);
        Assert.IsTrue(verify.Valid, verify.FailureReason);
    }

    [TestMethod]
    public async Task Sp1Verifier_RejectsWrongVk()
    {
        var prover = new Sp1RiscVProver(UInt256.Parse("0x" + new string('a', 64)));
        var verifier = new Sp1RiscVVerifier(UInt256.Parse("0x" + new string('b', 64)));

        var inputs = SamplePublicInputs();
        var proof = await prover.ProveAsync(new ProofRequest { PublicInputs = inputs, Witness = ReadOnlyMemory<byte>.Empty, Kind = ProofType.Zk });
        var verify = await verifier.VerifyAsync(inputs, proof.Proof);

        Assert.IsFalse(verify.Valid);
    }
}
