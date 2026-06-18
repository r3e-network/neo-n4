using System.ComponentModel;
using System.Numerics;
using Moq;
using Neo;
using Neo.SmartContract.Testing;
using Neo.SmartContract.Testing.Exceptions;

namespace NeoHub.Contracts.VmTests;

/// <summary>Minimal L1→L2 message filter surface so the router's filter hook can be mocked.</summary>
public abstract class MockL1TxFilter(SmartContractInitialize initialize) : SmartContract(initialize)
{
    [DisplayName("acceptL1ToL2")]
    public abstract bool? AcceptL1ToL2(BigInteger? targetChainId, UInt160? sender, UInt160? receiver,
        BigInteger? messageType, byte[]? payload);
}

/// <summary>Minimal Groth16Verifier surface so the router's proof-gate dispatch can be mocked
/// without a real bn254 engine. The router calls
/// <c>verifyZkProof(proofSystem, vkId, publicInputHash, proofBytes)</c> read-only.</summary>
public abstract class MockMessageRouter_Groth16Verifier(SmartContractInitialize initialize) : SmartContract(initialize)
{
    [DisplayName("verifyZkProof")]
    public abstract bool? VerifyZkProof(BigInteger? proofSystem, byte[]? verificationKeyId, byte[]? publicInputHash, byte[]? proofBytes);
}

/// <summary>
/// VM-level tests for NeoHub.MessageRouter — the cross-chain messaging hub. Executes the enqueue /
/// consume / publish-root / filter paths in a real NeoVM and pins the security-critical invariants:
/// L1→L2 nonces are per-chain monotonic, message consumption is replay-protected and settlement-
/// manager-gated, global-root and message-root publication is settlement-manager-gated (global root
/// additionally non-zero + publish-once-per-epoch), and the owner-gated L1 tx filter actually gates
/// enqueue (accept/reject) when configured.
/// </summary>
[TestClass]
public class UT_MessageRouter_Vm
{
    private const uint ChainA = 1001;
    private const uint ChainB = 2002;
    private static readonly UInt160 FilterHash = UInt160.Parse("0x" + new string('f', 40));
    private static readonly UInt160 OtherSm = UInt160.Parse("0x" + new string('3', 40));
    private static readonly UInt160 Receiver = UInt160.Parse("0x" + new string('c', 40));
    private static readonly UInt160 Groth16Hash = UInt160.Parse("0x" + new string('9', 40));
    private static readonly UInt256 MsgHash = UInt256.Parse("0x" + new string('1', 64));

    /// <summary>Deploy the router. owner/settlementManager default to engine.Sender so the owner and
    /// settlement-manager witness checks pass; pass an explicit <paramref name="settlementManager"/>
    /// (or <paramref name="owner"/>) to exercise the negative authorization paths.</summary>
    private static NeoHubMessageRouter Deploy(TestEngine engine, UInt160? owner = null, UInt160? settlementManager = null)
    {
        var o = owner ?? engine.Sender;
        var sm = settlementManager ?? engine.Sender;
        return engine.Deploy<NeoHubMessageRouter>(
            NeoHubMessageRouter.Nef, NeoHubMessageRouter.Manifest, new object[] { o, sm });
    }

    [TestMethod]
    public void EnqueueL1ToL2_IncrementsNoncePerChain_StoresMessage()
    {
        var engine = new TestEngine(true);
        var mr = Deploy(engine);

        Assert.AreEqual((BigInteger)1, mr.EnqueueL1ToL2(ChainA, Receiver, 1, new byte[] { 0xAB })!);
        Assert.AreEqual((BigInteger)2, mr.EnqueueL1ToL2(ChainA, Receiver, 1, new byte[] { 0xCD })!);
        // Per-chain nonce: a different chain starts its own sequence at 1.
        Assert.AreEqual((BigInteger)1, mr.EnqueueL1ToL2(ChainB, Receiver, 1, new byte[] { 0xEF })!);

        Assert.IsTrue(mr.GetL1ToL2(ChainA, 1)!.Length > 0, "enqueued message must be retrievable");
        Assert.IsTrue(mr.GetL1ToL2(ChainB, 1)!.Length > 0);
    }

    [TestMethod]
    public void EnqueueL1ToL2_RejectsZeroReceiver_AndReservedChainZero()
    {
        var engine = new TestEngine(true);
        var mr = Deploy(engine);

        Assert.ThrowsExactly<TestException>(() => mr.EnqueueL1ToL2(ChainA, UInt160.Zero, 1, new byte[] { 0x01 }),
            "zero receiver must be rejected");
        Assert.ThrowsExactly<TestException>(() => mr.EnqueueL1ToL2(0, Receiver, 1, new byte[] { 0x01 }),
            "targetChainId 0 is the reserved L1 sentinel");
    }

    [TestMethod]
    public void MarkConsumed_BySettlementManager_ConsumeOnce_ReplayProtected()
    {
        var engine = new TestEngine(true);
        var mr = Deploy(engine); // settlementManager == engine.Sender

        Assert.IsFalse(mr.IsConsumed(MsgHash));
        mr.MarkConsumed(ChainA, MsgHash);
        Assert.IsTrue(mr.IsConsumed(MsgHash));
        Assert.ThrowsExactly<TestException>(() => mr.MarkConsumed(ChainA, MsgHash), "double consume must fault");
    }

    [TestMethod]
    public void MarkConsumed_NonSettlementManager_Faults()
    {
        var engine = new TestEngine(true);
        var mr = Deploy(engine, settlementManager: OtherSm); // SM witness will not be present

        Assert.ThrowsExactly<TestException>(() => mr.MarkConsumed(ChainA, MsgHash),
            "MarkConsumed is settlement-manager-gated");
        Assert.IsFalse(mr.IsConsumed(MsgHash), "rejected consume must not set state");
    }

    [TestMethod]
    public void PublishGlobalRoot_NonZero_OncePerEpoch()
    {
        var engine = new TestEngine(true);
        var mr = Deploy(engine);
        var root = UInt256.Parse("0x" + new string('2', 64));
        // Devnet mode: no global-root verifier wired → witness-only path, proof args ignored
        // (pass empty/zero; the verifier pointer is zero so the proof-gate branch is skipped).
        var emptyProof = Array.Empty<byte>();
        var dummyVkId = new byte[32];

        Assert.AreEqual(UInt256.Zero, mr.GetGlobalRoot(7), "no root published yet");
        Assert.ThrowsExactly<TestException>(() => mr.PublishGlobalRoot(7, UInt256.Zero, dummyVkId, emptyProof),
            "zero global root rejected");

        mr.PublishGlobalRoot(7, root, dummyVkId, emptyProof);
        Assert.AreEqual(root, mr.GetGlobalRoot(7));
        Assert.ThrowsExactly<TestException>(() => mr.PublishGlobalRoot(7, root, dummyVkId, emptyProof),
            "publish-once-per-epoch");

        // A different epoch is independent and still publishable.
        var root8 = UInt256.Parse("0x" + new string('4', 64));
        mr.PublishGlobalRoot(8, root8, dummyVkId, emptyProof);
        Assert.AreEqual(root8, mr.GetGlobalRoot(8));
    }

    [TestMethod]
    public void PublishGlobalRoot_NonSettlementManager_Faults()
    {
        var engine = new TestEngine(true);
        var mr = Deploy(engine, settlementManager: OtherSm);
        var root = UInt256.Parse("0x" + new string('2', 64));

        Assert.ThrowsExactly<TestException>(() => mr.PublishGlobalRoot(7, root, new byte[32], Array.Empty<byte>()),
            "PublishGlobalRoot is settlement-manager-gated");
    }

    // ---------------------------------------------------------------------------------------------
    // Global-root proof gate (production). When a Groth16Verifier is wired, PublishGlobalRoot MUST
    // verify an aggregated proof before committing the root — the global root becomes the single
    // 32-byte public input the proof commits to. Mocked Groth16Verifier so the real bn254 math
    // (engine-gated) isn't required.
    // ---------------------------------------------------------------------------------------------

    private static byte[] SampleVkId() => new byte[32] {
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
        0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10,
        0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18,
        0x19, 0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f, 0x20,
    };

    private static void WireGroth16Mock(TestEngine engine, bool accepts) =>
        engine.FromHash<MockMessageRouter_Groth16Verifier>(Groth16Hash, m =>
            m.Setup(c => c.VerifyZkProof(
                It.IsAny<BigInteger?>(), It.IsAny<byte[]?>(),
                It.IsAny<byte[]?>(), It.IsAny<byte[]?>()))
             .Returns(accepts), checkExistence: false);

    [TestMethod]
    public void SetGlobalRootVerifier_OwnerOnly_WiresAndClears()
    {
        var engine = new TestEngine(true);
        var mr = Deploy(engine);

        // Default: no verifier wired (devnet mode).
        Assert.AreEqual(UInt160.Zero, mr.GlobalRootVerifier);

        // Owner wires the verifier with proofSystem=SP1(1).
        mr.SetGlobalRootVerifier(Groth16Hash, 1);
        Assert.AreEqual(Groth16Hash, mr.GlobalRootVerifier);
        Assert.AreEqual((BigInteger)1, mr.GlobalRootProofSystem);

        // Owner clears it back to devnet mode (zero hash).
        mr.SetGlobalRootVerifier(UInt160.Zero, 1);
        Assert.AreEqual(UInt160.Zero, mr.GlobalRootVerifier);

        // Negative: non-owner faults. Switch signer away from the owner.
        engine.SetTransactionSigners(OtherSm);
        Assert.ThrowsExactly<TestException>(() => mr.SetGlobalRootVerifier(Groth16Hash, 1),
            "non-owner cannot wire the global-root verifier");

        // Negative: bad proofSystem range.
        engine.SetTransactionSigners(engine.Sender);
        Assert.ThrowsExactly<TestException>(() => mr.SetGlobalRootVerifier(Groth16Hash, 0),
            "proofSystem=0 must be rejected");
        Assert.ThrowsExactly<TestException>(() => mr.SetGlobalRootVerifier(Groth16Hash, 5),
            "proofSystem=5 must be rejected");
    }

    [TestMethod]
    public void PublishGlobalRoot_ProofGateAccepts_WhenGroth16Accepts()
    {
        var engine = new TestEngine(true);
        var mr = Deploy(engine);
        mr.SetGlobalRootVerifier(Groth16Hash, 1);  // production mode
        WireGroth16Mock(engine, accepts: true);

        var root = UInt256.Parse("0x" + new string('7', 64));
        var proof = new byte[] { 0xCA, 0xFE };  // opaque to the router; the mock ignores content
        mr.PublishGlobalRoot(7, root, SampleVkId(), proof);
        Assert.AreEqual(root, mr.GetGlobalRoot(7), "verified root must be committed");
    }

    [TestMethod]
    public void PublishGlobalRoot_ProofGateRejects_WhenGroth16Rejects()
    {
        var engine = new TestEngine(true);
        var mr = Deploy(engine);
        mr.SetGlobalRootVerifier(Groth16Hash, 1);  // production mode
        WireGroth16Mock(engine, accepts: false);

        var root = UInt256.Parse("0x" + new string('7', 64));
        // Groth16 mock returns false → publish must fault ("aggregated proof rejected").
        Assert.ThrowsExactly<TestException>(() =>
            mr.PublishGlobalRoot(7, root, SampleVkId(), new byte[] { 0xCA, 0xFE }),
            "a Groth16 pairing failure must reject the global-root publish");
        Assert.AreEqual(UInt256.Zero, mr.GetGlobalRoot(7),
            "rejected publish must NOT commit the root");
    }

    [TestMethod]
    public void PublishGlobalRoot_ProofGateRejectsEmptyProof_WhenVerifierWired()
    {
        var engine = new TestEngine(true);
        var mr = Deploy(engine);
        mr.SetGlobalRootVerifier(Groth16Hash, 1);  // production mode → proof required
        // No mock wired; the contract faults on the empty-proof guard BEFORE reaching the call.
        var root = UInt256.Parse("0x" + new string('7', 64));
        Assert.ThrowsExactly<TestException>(() =>
            mr.PublishGlobalRoot(7, root, SampleVkId(), Array.Empty<byte>()),
            "empty aggregated proof must be rejected when a verifier is wired");
    }

    [TestMethod]
    public void PublishMessageRoots_BySettlementManager_StoresBothRoots()
    {
        var engine = new TestEngine(true);
        var mr = Deploy(engine);
        var l2ToL1 = UInt256.Parse("0x" + new string('a', 64));
        var l2ToL2 = UInt256.Parse("0x" + new string('b', 64));

        mr.PublishMessageRoots(ChainA, 5, l2ToL1, l2ToL2);
        Assert.AreEqual(l2ToL1, mr.GetL2ToL1Root(ChainA, 5));
        Assert.AreEqual(l2ToL2, mr.GetL2ToL2Root(ChainA, 5));
    }

    [TestMethod]
    public void PublishMessageRoots_NonSettlementManager_Faults()
    {
        var engine = new TestEngine(true);
        var mr = Deploy(engine, settlementManager: OtherSm);
        var l2ToL1 = UInt256.Parse("0x" + new string('a', 64));
        var l2ToL2 = UInt256.Parse("0x" + new string('b', 64));

        Assert.ThrowsExactly<TestException>(() => mr.PublishMessageRoots(ChainA, 5, l2ToL1, l2ToL2),
            "PublishMessageRoots is settlement-manager-gated");
    }

    [TestMethod]
    public void L1TxFilter_OwnerGated_SetGetClear()
    {
        var engine = new TestEngine(true);
        var mr = Deploy(engine, owner: engine.Sender);

        Assert.AreEqual(UInt160.Zero, mr.GetL1TxFilter(ChainA), "no filter by default");
        mr.SetL1TxFilter(ChainA, FilterHash);
        Assert.AreEqual(FilterHash, mr.GetL1TxFilter(ChainA));
        mr.ClearL1TxFilter(ChainA);
        Assert.AreEqual(UInt160.Zero, mr.GetL1TxFilter(ChainA), "cleared filter reads back as zero");
    }

    [TestMethod]
    public void L1TxFilter_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        // Owner is a different account than the test signer -> the owner gate must reject.
        var mr = Deploy(engine, owner: UInt160.Parse("0x" + new string('1', 40)));

        Assert.ThrowsExactly<TestException>(() => mr.SetL1TxFilter(ChainA, FilterHash),
            "SetL1TxFilter is owner-gated");
        Assert.ThrowsExactly<TestException>(() => mr.ClearL1TxFilter(ChainA),
            "ClearL1TxFilter is owner-gated");
    }

    [TestMethod]
    public void EnqueueL1ToL2_WhenFilterRejects_Faults_WhenAccepts_Succeeds()
    {
        var engine = new TestEngine(true);
        var mr = Deploy(engine);
        var rejectFilter = UInt160.Parse("0x" + new string('e', 40));
        var acceptFilter = UInt160.Parse("0x" + new string('d', 40));

        engine.FromHash<MockL1TxFilter>(rejectFilter, m =>
            m.Setup(c => c.AcceptL1ToL2(It.IsAny<BigInteger?>(), It.IsAny<UInt160?>(), It.IsAny<UInt160?>(),
                It.IsAny<BigInteger?>(), It.IsAny<byte[]?>())).Returns(false), checkExistence: false);
        engine.FromHash<MockL1TxFilter>(acceptFilter, m =>
            m.Setup(c => c.AcceptL1ToL2(It.IsAny<BigInteger?>(), It.IsAny<UInt160?>(), It.IsAny<UInt160?>(),
                It.IsAny<BigInteger?>(), It.IsAny<byte[]?>())).Returns(true), checkExistence: false);

        // Filter rejects -> enqueue must fault.
        mr.SetL1TxFilter(ChainA, rejectFilter);
        Assert.ThrowsExactly<TestException>(() => mr.EnqueueL1ToL2(ChainA, Receiver, 1, new byte[] { 0x01 }),
            "a filter that rejects must block the enqueue");

        // Re-point the chain's filter to an accepting one -> enqueue now succeeds.
        mr.SetL1TxFilter(ChainA, acceptFilter);
        Assert.AreEqual((BigInteger)1, mr.EnqueueL1ToL2(ChainA, Receiver, 1, new byte[] { 0x01 })!,
            "an accepting filter must let the enqueue through");
    }
}
