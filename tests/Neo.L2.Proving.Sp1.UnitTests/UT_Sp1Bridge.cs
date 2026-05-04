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
    public void Bridge_IsAvailable_ResultIsCached()
    {
        // Reset → first call computes (and caches false in dev). Second call returns
        // the cache without re-attempting the P/Invoke. ResetAvailableCache forces the
        // next call to recompute.
        Sp1Bridge.ResetAvailableCache();
        var first = Sp1Bridge.IsAvailable;
        var second = Sp1Bridge.IsAvailable;
        Assert.AreEqual(first, second);
        Sp1Bridge.ResetAvailableCache();
        var afterReset = Sp1Bridge.IsAvailable;
        Assert.AreEqual(first, afterReset, "post-reset re-compute must yield the same answer in dev");
    }

    [TestMethod]
    public void Bridge_MaxProofBytes_IsFiniteAndUnderInt32Max()
    {
        // Pin the defensive cap so a future "remove the size check, native bridge is
        // trusted" refactor can't silently drop it. Without the bound, a misbehaving
        // FFI return that declares >2GB would wrap the (int) cast in Prove() and feed
        // a wrapped length into Marshal.Copy — heap-overflow shape.
        Assert.IsTrue(Sp1Bridge.MaxProofBytes > 0);
        Assert.IsTrue(Sp1Bridge.MaxProofBytes < int.MaxValue,
            "cap must be < int.MaxValue so the (int) cast in Prove can't truncate");
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
    public void Sp1Prover_Constructor_RejectsNullVerificationKeyId()
    {
        // Regression for iter 198: Sp1RiscVProver has its OWN ArgumentNullException
        // guard at the top of its ctor (Sp1RiscVProver.cs:33), separate from the
        // MockRiscVProver fallback that the same ctor constructs as a backup. The
        // iter-215 pin on MockRiscVProver doesn't exercise this path — the Sp1* guard
        // fires first. Pin it directly so a refactor that drops the Sp1* guard would
        // not silently fall through to the Mock* one with confusing error attribution.
        Assert.ThrowsExactly<ArgumentNullException>(() => new Sp1RiscVProver(null!));
    }

    [TestMethod]
    public void Sp1Verifier_Constructor_RejectsNullExpectedVkId()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new Sp1RiscVVerifier(null!));
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
