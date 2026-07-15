using System.Buffers.Binary;
using System.ComponentModel;
using System.Numerics;
using Moq;
using Neo;
using Neo.Cryptography;
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
public abstract class MockMessageRouter_ZkVerifier(SmartContractInitialize initialize) : SmartContract(initialize)
{
    [DisplayName("verifyZkProof")]
    public abstract bool? VerifyZkProof(BigInteger? proofSystem, byte[]? verificationKeyId, byte[]? publicInputHash, byte[]? proofBytes);
}

/// <summary>GovernanceController surface used by MessageRouter proposal execution.</summary>
public abstract class MockMessageRouter_GovernanceController(SmartContractInitialize initialize) : SmartContract(initialize)
{
    [DisplayName("isApprovedAndTimelocked")]
    public abstract bool? IsApprovedAndTimelocked(BigInteger? proposalId);

    [DisplayName("matchesProposalPayload")]
    public abstract bool? MatchesProposalPayload(BigInteger? proposalId, byte[]? expectedPayload);
}

/// <summary>Canonical finalized-message-root surface exposed by SettlementManager.</summary>
public abstract class MockMessageRouter_SettlementManager(SmartContractInitialize initialize) : SmartContract(initialize)
{
    [DisplayName("getL2ToL1MessageRoot")]
    public abstract UInt256? GetL2ToL1MessageRoot(BigInteger? chainId, BigInteger? batchNumber);
}

/// <summary>
/// VM-level tests for NeoHub.MessageRouter — the cross-chain messaging hub. Executes the enqueue /
/// consume / publish-root / filter paths in a real NeoVM and pins the security-critical invariants:
/// L1→L2 nonces are per-chain monotonic, message consumption is replay-protected and settlement-
/// manager-gated, global-root and message-root publication is settlement-manager-gated and
/// publish-once-per-epoch, and the owner-gated L1 tx filter actually gates enqueue (accept/reject)
/// when configured.
/// </summary>
[TestClass]
public class UT_MessageRouter_Vm
{
    private const uint ChainA = 1001;
    private const uint ChainB = 2002;
    private static readonly UInt160 FilterHash = UInt160.Parse("0x" + new string('f', 40));
    private static readonly UInt160 OtherSm = UInt160.Parse("0x" + new string('3', 40));
    private static readonly UInt160 SettlementManagerHash = UInt160.Parse("0x" + new string('4', 40));
    private static readonly UInt160 Receiver = UInt160.Parse("0x" + new string('c', 40));
    private static readonly UInt160 ZkVerifierHash = UInt160.Parse("0x" + new string('9', 40));
    private static readonly UInt160 GovernanceControllerHash = UInt160.Parse("0x" + new string('8', 40));
    private static readonly UInt256 MsgHash = UInt256.Parse("0x" + new string('1', 64));
    private static readonly UInt256 GatewayVerificationKey = FilledHash(0xA1);
    private static readonly UInt256 GatewayReplayDomain = FilledHash(0xD1);
    private const byte GatewayAggregationBackend = 0x02;
    private const byte GatewayProofSystem = 0x01;

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

    private static UInt256 FilledHash(byte value) => new(Enumerable.Repeat(value, 32).ToArray());

    private static UInt256 PairRoot(UInt256 left, UInt256 right)
    {
        var input = new byte[64];
        left.GetSpan().CopyTo(input);
        right.GetSpan().CopyTo(input.AsSpan(32));
        return new UInt256(Crypto.Hash256(input));
    }

    private static void WireFinalizedL2ToL1Root(
        TestEngine engine,
        UInt256 root,
        uint sourceChainId = ChainA,
        ulong batchNumber = 5)
    {
        engine.FromHash<MockMessageRouter_SettlementManager>(SettlementManagerHash, mock =>
        {
            mock.Setup(contract => contract.GetL2ToL1MessageRoot(
                    It.IsAny<BigInteger?>(), It.IsAny<BigInteger?>()))
                .Returns(UInt256.Zero);
            mock.Setup(contract => contract.GetL2ToL1MessageRoot(
                    (BigInteger)sourceChainId, (BigInteger)batchNumber))
                .Returns(root);
        }, checkExistence: false);
    }

    private static void ConfigureAndLockGateway(TestEngine engine, NeoHubMessageRouter router)
    {
        router.SetGlobalRootVerifier(
            ZkVerifierHash,
            GatewayProofSystem,
            GatewayAggregationBackend,
            GatewayVerificationKey,
            GatewayReplayDomain);
        router.GovernanceController = GovernanceControllerHash;
        router.LockGlobalRootGovernance();
        Assert.IsTrue(router.IsGlobalRootGovernanceLocked);
    }

    private static UInt256 ComputeGatewayProofInputHash(
        UInt160 messageRouter,
        ulong batchEpoch,
        UInt256 globalRoot,
        UInt256 constituentRoot,
        uint constituentCount,
        byte aggregationBackend,
        byte proofSystem,
        UInt256 verificationKey,
        UInt256 replayDomain)
    {
        var bytes = new byte[170];
        "NEO4GWR2"u8.CopyTo(bytes);
        messageRouter.GetSpan().CopyTo(bytes.AsSpan(8, 20));
        replayDomain.GetSpan().CopyTo(bytes.AsSpan(28, 32));
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(60, 8), batchEpoch);
        globalRoot.GetSpan().CopyTo(bytes.AsSpan(68, 32));
        constituentRoot.GetSpan().CopyTo(bytes.AsSpan(100, 32));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(132, 4), constituentCount);
        bytes[136] = aggregationBackend;
        bytes[137] = proofSystem;
        verificationKey.GetSpan().CopyTo(bytes.AsSpan(138, 32));
        return new UInt256(Crypto.Hash256(bytes));
    }

    private static void WireVerifier(
        TestEngine engine,
        UInt256 expectedInputHash,
        byte[] expectedProof)
    {
        var expectedVerificationKey = GatewayVerificationKey.GetSpan().ToArray();
        var expectedInput = expectedInputHash.GetSpan().ToArray();
        engine.FromHash<MockMessageRouter_ZkVerifier>(ZkVerifierHash, mock =>
        {
            mock.Setup(contract => contract.VerifyZkProof(
                    It.IsAny<BigInteger?>(),
                    It.IsAny<byte[]?>(),
                    It.IsAny<byte[]?>(),
                    It.IsAny<byte[]?>()))
                .Returns(false);
            mock.Setup(contract => contract.VerifyZkProof(
                    (BigInteger)GatewayProofSystem,
                    It.Is<byte[]?>(value => value != null && value.SequenceEqual(expectedVerificationKey)),
                    It.Is<byte[]?>(value => value != null && value.SequenceEqual(expectedInput)),
                    It.Is<byte[]?>(value => value != null && value.SequenceEqual(expectedProof))))
                .Returns(true);
        }, checkExistence: false);
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
    public void ConsumeL2ToL1_ValidFinalizedProof_ConsumeOnce_ReplayProtected()
    {
        var engine = new TestEngine(true);
        var sibling = FilledHash(0x22);
        WireFinalizedL2ToL1Root(engine, PairRoot(MsgHash, sibling));
        var mr = Deploy(engine, settlementManager: SettlementManagerHash);
        IList<object> proof = new object[] { sibling.GetSpan().ToArray() };

        Assert.IsFalse(mr.IsConsumed(MsgHash));
        mr.ConsumeL2ToL1(ChainA, 5, MsgHash, proof, 0);
        Assert.IsTrue(mr.IsConsumed(MsgHash));
        Assert.ThrowsExactly<TestException>(() => mr.ConsumeL2ToL1(ChainA, 5, MsgHash, proof, 0),
            "double consume must fault");
    }

    [TestMethod]
    public void ConsumeL2ToL1_MissingFinalizedRootOrInvalidProof_FaultsWithoutConsuming()
    {
        var engine = new TestEngine(true);
        WireFinalizedL2ToL1Root(engine, UInt256.Zero);
        var mr = Deploy(engine, settlementManager: SettlementManagerHash);

        Assert.ThrowsExactly<TestException>(() =>
            mr.ConsumeL2ToL1(ChainA, 5, MsgHash, Array.Empty<object>(), 0));
        Assert.IsFalse(mr.IsConsumed(MsgHash), "unfinalized root must not consume the message");

        var secondEngine = new TestEngine(true);
        WireFinalizedL2ToL1Root(secondEngine, PairRoot(MsgHash, FilledHash(0x22)));
        var secondRouter = Deploy(secondEngine, settlementManager: SettlementManagerHash);
        IList<object> wrongProof = new object[] { FilledHash(0x23).GetSpan().ToArray() };

        Assert.ThrowsExactly<TestException>(() =>
            secondRouter.ConsumeL2ToL1(ChainA, 5, MsgHash, wrongProof, 0));
        Assert.IsFalse(secondRouter.IsConsumed(MsgHash), "invalid proof must not set state");
    }

    [TestMethod]
    public void ConsumeL2ToL1_RejectsNonCanonicalIndexMalformedSiblingAndLegacyBypass()
    {
        var engine = new TestEngine(true);
        var sibling = FilledHash(0x22);
        WireFinalizedL2ToL1Root(engine, PairRoot(MsgHash, sibling));
        var mr = Deploy(engine, settlementManager: SettlementManagerHash);

        Assert.ThrowsExactly<TestException>(() => mr.ConsumeL2ToL1(
            ChainA, 5, MsgHash, new object[] { sibling.GetSpan().ToArray() }, 2));
        Assert.ThrowsExactly<TestException>(() => mr.ConsumeL2ToL1(
            ChainA, 5, MsgHash, new object[] { new byte[31] }, 0));
        Assert.IsFalse(mr.IsConsumed(MsgHash));

        Assert.IsFalse(NeoHubMessageRouter.Manifest.Abi.Methods.Any(method => method.Name == "markConsumed"),
            "the witness-only arbitrary-hash bypass must not remain in the ABI");
    }

    [TestMethod]
    public void PublishGlobalRoot_WitnessCannotReplaceLockedProofProfile()
    {
        var engine = new TestEngine(true);
        var mr = Deploy(engine);

        Assert.ThrowsExactly<TestException>(() => mr.PublishGlobalRoot(
            7,
            FilledHash(0x51),
            FilledHash(0x61),
            2,
            GatewayAggregationBackend,
            GatewayProofSystem,
            GatewayVerificationKey,
            GatewayReplayDomain,
            new byte[] { 0x01 }));
        Assert.AreEqual(UInt256.Zero, mr.GetGlobalRoot(7));
    }

    [TestMethod]
    public void PublishGlobalRoot_NonSettlementManager_Faults()
    {
        var engine = new TestEngine(true);
        var mr = Deploy(engine, settlementManager: OtherSm);

        Assert.ThrowsExactly<TestException>(() => mr.PublishGlobalRoot(
            7,
            FilledHash(0x51),
            FilledHash(0x61),
            2,
            GatewayAggregationBackend,
            GatewayProofSystem,
            GatewayVerificationKey,
            GatewayReplayDomain,
            new byte[] { 0x01 }));
    }

    [TestMethod]
    public void GlobalRootGovernance_LockDisablesOwnerBypass()
    {
        var engine = new TestEngine(true);
        var mr = Deploy(engine);

        Assert.ThrowsExactly<TestException>(() => mr.LockGlobalRootGovernance());
        Assert.ThrowsExactly<TestException>(() => mr.SetGlobalRootVerifier(
            ZkVerifierHash,
            GatewayProofSystem,
            0xFE,
            GatewayVerificationKey,
            GatewayReplayDomain));
        Assert.ThrowsExactly<TestException>(() => mr.SetGlobalRootVerifier(
            UInt160.Zero,
            GatewayProofSystem,
            GatewayAggregationBackend,
            GatewayVerificationKey,
            GatewayReplayDomain));

        ConfigureAndLockGateway(engine, mr);
        Assert.AreEqual(ZkVerifierHash, mr.GlobalRootVerifier);
        Assert.AreEqual((BigInteger)GatewayProofSystem, mr.GlobalRootProofSystem);
        Assert.AreEqual((BigInteger)GatewayAggregationBackend, mr.GlobalRootAggregationBackend);
        Assert.AreEqual(GatewayVerificationKey, mr.GlobalRootVerificationKeyId);
        Assert.AreEqual(GatewayReplayDomain, mr.GlobalRootReplayDomain);
        Assert.AreEqual(GovernanceControllerHash, mr.GovernanceController);

        Assert.ThrowsExactly<TestException>(() => mr.SetGlobalRootVerifier(
            UInt160.Parse("0x" + new string('7', 40)),
            2,
            3,
            FilledHash(0xA2),
            FilledHash(0xD2)));
        Assert.ThrowsExactly<TestException>(() => mr.GovernanceController =
            UInt160.Parse("0x" + new string('6', 40)));
        Assert.AreEqual(ZkVerifierHash, mr.GlobalRootVerifier);
        Assert.AreEqual(GovernanceControllerHash, mr.GovernanceController);
    }

    [TestMethod]
    public void GlobalRootGovernance_ProposalRotationIsPayloadBoundAndReplayProtected()
    {
        var engine = new TestEngine(true);
        var mr = Deploy(engine);
        ConfigureAndLockGateway(engine, mr);
        var nextVerifier = UInt160.Parse("0x" + new string('7', 40));
        var nextVerificationKey = FilledHash(0xA2);
        var nextReplayDomain = FilledHash(0xD2);
        var expectedAction = mr.BuildSetGlobalRootVerifierAction(
            nextVerifier,
            2,
            3,
            nextVerificationKey,
            nextReplayDomain)!;
        const int actionFieldsAfterTag = 20 + 20 + 1 + 1 + 32 + 32;
        var actionTagLength = expectedAction.Length - actionFieldsAfterTag;
        CollectionAssert.AreEqual(
            mr.Hash.GetSpan().ToArray(),
            expectedAction.AsSpan(actionTagLength, 20).ToArray(),
            "governance action must be bound to this MessageRouter deployment");
        engine.FromHash<MockMessageRouter_GovernanceController>(GovernanceControllerHash, mock =>
        {
            mock.Setup(controller => controller.IsApprovedAndTimelocked((BigInteger)11)).Returns(true);
            mock.Setup(controller => controller.MatchesProposalPayload(
                    (BigInteger)11,
                    It.Is<byte[]?>(payload => payload != null && payload.SequenceEqual(expectedAction))))
                .Returns(true);
        }, checkExistence: false);

        mr.SetGlobalRootVerifierViaProposal(
            nextVerifier,
            2,
            3,
            nextVerificationKey,
            nextReplayDomain,
            11);

        Assert.AreEqual(nextVerifier, mr.GlobalRootVerifier);
        Assert.AreEqual((BigInteger)2, mr.GlobalRootProofSystem);
        Assert.AreEqual((BigInteger)3, mr.GlobalRootAggregationBackend);
        Assert.AreEqual(nextVerificationKey, mr.GlobalRootVerificationKeyId);
        Assert.AreEqual(nextReplayDomain, mr.GlobalRootReplayDomain);
        Assert.ThrowsExactly<TestException>(() => mr.SetGlobalRootVerifierViaProposal(
            nextVerifier,
            2,
            3,
            nextVerificationKey,
            nextReplayDomain,
            11));
    }

    [TestMethod]
    public void PublishGlobalRoot_BindsEpochRootConstituentsBackendDomainAndProof()
    {
        var engine = new TestEngine(true);
        var mr = Deploy(engine);
        ConfigureAndLockGateway(engine, mr);
        const ulong epoch = 77;
        const uint constituentCount = 2;
        var globalRoot = FilledHash(0x51);
        var constituentRoot = FilledHash(0x61);
        var proof = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE };
        var expectedInputHash = ComputeGatewayProofInputHash(
            mr.Hash,
            epoch,
            globalRoot,
            constituentRoot,
            constituentCount,
            GatewayAggregationBackend,
            GatewayProofSystem,
            GatewayVerificationKey,
            GatewayReplayDomain);
        WireVerifier(engine, expectedInputHash, proof);

        Assert.AreEqual(expectedInputHash, mr.BuildGlobalRootProofInputHash(
            epoch,
            globalRoot,
            constituentRoot,
            constituentCount,
            GatewayAggregationBackend,
            GatewayProofSystem,
            GatewayVerificationKey,
            GatewayReplayDomain));

        Assert.ThrowsExactly<TestException>(() => mr.PublishGlobalRoot(
            epoch + 1, globalRoot, constituentRoot, constituentCount,
            GatewayAggregationBackend, GatewayProofSystem, GatewayVerificationKey,
            GatewayReplayDomain, proof));
        Assert.ThrowsExactly<TestException>(() => mr.PublishGlobalRoot(
            epoch, FilledHash(0x52), constituentRoot, constituentCount,
            GatewayAggregationBackend, GatewayProofSystem, GatewayVerificationKey,
            GatewayReplayDomain, proof));
        Assert.ThrowsExactly<TestException>(() => mr.PublishGlobalRoot(
            epoch, globalRoot, FilledHash(0x62), constituentCount,
            GatewayAggregationBackend, GatewayProofSystem, GatewayVerificationKey,
            GatewayReplayDomain, proof));
        Assert.ThrowsExactly<TestException>(() => mr.PublishGlobalRoot(
            epoch, globalRoot, constituentRoot, constituentCount + 1,
            GatewayAggregationBackend, GatewayProofSystem, GatewayVerificationKey,
            GatewayReplayDomain, proof));
        Assert.ThrowsExactly<TestException>(() => mr.PublishGlobalRoot(
            epoch, globalRoot, constituentRoot, constituentCount,
            3, GatewayProofSystem, GatewayVerificationKey, GatewayReplayDomain, proof));
        Assert.ThrowsExactly<TestException>(() => mr.PublishGlobalRoot(
            epoch, globalRoot, constituentRoot, constituentCount,
            GatewayAggregationBackend, 2, GatewayVerificationKey, GatewayReplayDomain, proof));
        Assert.ThrowsExactly<TestException>(() => mr.PublishGlobalRoot(
            epoch, globalRoot, constituentRoot, constituentCount,
            GatewayAggregationBackend, GatewayProofSystem, FilledHash(0xA2),
            GatewayReplayDomain, proof));
        Assert.ThrowsExactly<TestException>(() => mr.PublishGlobalRoot(
            epoch, globalRoot, constituentRoot, constituentCount,
            GatewayAggregationBackend, GatewayProofSystem, GatewayVerificationKey,
            FilledHash(0xD2), proof));
        Assert.ThrowsExactly<TestException>(() => mr.PublishGlobalRoot(
            epoch, globalRoot, constituentRoot, constituentCount,
            GatewayAggregationBackend, GatewayProofSystem, GatewayVerificationKey,
            GatewayReplayDomain, new byte[] { 0xCA, 0xFE, 0xBA, 0xBF }));
        Assert.ThrowsExactly<TestException>(() => mr.PublishGlobalRoot(
            epoch, globalRoot, constituentRoot, constituentCount,
            GatewayAggregationBackend, GatewayProofSystem, GatewayVerificationKey,
            GatewayReplayDomain, Array.Empty<byte>()));
        Assert.AreEqual(UInt256.Zero, mr.GetGlobalRoot(epoch));

        Assert.IsTrue(mr.PublishGlobalRoot(
            epoch,
            globalRoot,
            constituentRoot,
            constituentCount,
            GatewayAggregationBackend,
            GatewayProofSystem,
            GatewayVerificationKey,
            GatewayReplayDomain,
            proof));
        Assert.AreEqual(globalRoot, mr.GetGlobalRoot(epoch));
        Assert.AreEqual(expectedInputHash, mr.GetGlobalRootProofInputHash(epoch));

        Assert.IsFalse(mr.PublishGlobalRoot(
            epoch,
            globalRoot,
            constituentRoot,
            constituentCount,
            GatewayAggregationBackend,
            GatewayProofSystem,
            GatewayVerificationKey,
            GatewayReplayDomain,
            Array.Empty<byte>()));
        Assert.ThrowsExactly<TestException>(() => mr.PublishGlobalRoot(
            epoch,
            FilledHash(0x53),
            constituentRoot,
            constituentCount,
            GatewayAggregationBackend,
            GatewayProofSystem,
            GatewayVerificationKey,
            GatewayReplayDomain,
            proof));
    }

    [TestMethod]
    public void PublishGlobalRoot_AllowsProvenCanonicalZeroMessageRoot()
    {
        var engine = new TestEngine(true);
        var mr = Deploy(engine);
        ConfigureAndLockGateway(engine, mr);
        const ulong epoch = 78;
        var constituentRoot = FilledHash(0x61);
        var proof = new byte[] { 0xCA, 0xFE };
        var expectedInputHash = ComputeGatewayProofInputHash(
            mr.Hash,
            epoch,
            UInt256.Zero,
            constituentRoot,
            1,
            GatewayAggregationBackend,
            GatewayProofSystem,
            GatewayVerificationKey,
            GatewayReplayDomain);
        WireVerifier(engine, expectedInputHash, proof);

        Assert.IsTrue(mr.PublishGlobalRoot(
            epoch,
            UInt256.Zero,
            constituentRoot,
            1,
            GatewayAggregationBackend,
            GatewayProofSystem,
            GatewayVerificationKey,
            GatewayReplayDomain,
            proof));
        Assert.AreEqual(UInt256.Zero, mr.GetGlobalRoot(epoch));
        Assert.AreEqual(expectedInputHash, mr.GetGlobalRootProofInputHash(epoch));
        Assert.IsFalse(mr.PublishGlobalRoot(
            epoch,
            UInt256.Zero,
            constituentRoot,
            1,
            GatewayAggregationBackend,
            GatewayProofSystem,
            GatewayVerificationKey,
            GatewayReplayDomain,
            Array.Empty<byte>()));
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
