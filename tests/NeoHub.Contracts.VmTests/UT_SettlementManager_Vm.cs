using System.Buffers.Binary;
using System.ComponentModel;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Moq;
using Neo;
using Neo.SmartContract.Testing;
using Neo.SmartContract.Testing.Exceptions;

namespace NeoHub.Contracts.VmTests;

/// <summary>MessageRouter publication surface used by SettlementManager Gateway VM tests.</summary>
public abstract class MockSettlementManager_MessageRouter(SmartContractInitialize initialize)
    : SmartContract(initialize)
{
    [DisplayName("publishGlobalRoot")]
    public abstract bool? PublishGlobalRoot(
        BigInteger? batchEpoch,
        UInt256? globalRoot,
        UInt256? constituentCommitmentsRoot,
        BigInteger? constituentCount,
        BigInteger? aggregationBackendId,
        BigInteger? proofSystem,
        UInt256? verificationKeyId,
        UInt256? replayDomain,
        byte[]? aggregatedProof);
}

/// <summary>Governance proposal surface used by SettlementManager governance-lock VM tests.</summary>
public abstract class MockSettlementManager_GovernanceController(SmartContractInitialize initialize)
    : SmartContract(initialize)
{
    [DisplayName("isApprovedAndTimelocked")]
    public abstract bool? IsApprovedAndTimelocked(BigInteger? proposalId);

    [DisplayName("matchesProposalPayload")]
    public abstract bool? MatchesProposalPayload(BigInteger? proposalId, byte[]? payload);
}

/// <summary>
/// VM-level tests for NeoHub.SettlementManager — the security-critical settlement path. These
/// execute the compiled NEF in a real NeoVM with the cross-contract dependencies (ChainRegistry,
/// VerifierRegistry, DARegistry, DAValidator) replaced by registered mocks, so the full
/// SubmitBatch → FinalizeBatch → RevertBatch sequencing logic is exercised, including:
///   - C2: the on-chain publicInputHash binding (a commitment whose recorded roots don't match the
///         reconstructed hash is rejected).
///   - C3: preStateRoot continuity against the canonical head.
///   - C4 + round-2 fix: RevertBatch rewind + FinalizeBatch's sequence/continuity re-check, which
///         together stop an orphaned descendant of a reverted head from finalizing onto the rewound
///         root.
/// </summary>
[TestClass]
public class UT_SettlementManager_Vm
{
    private const uint ChainId = 1001;
    private static byte[] GenesisState => R(0x10);
    private static UInt256 GenesisStateRoot => new(GenesisState);

    // Commitment header offsets (see Neo.L2.Batch.BatchSerializer).
    private const int OffChainId = 0, OffBatch = 4, OffFirstBlock = 12, OffLastBlock = 20;
    private const int OffPreState = 28, OffPostState = 60, OffTxRoot = 92, OffReceiptRoot = 124;
    private const int OffWithdrawal = 156, OffL2ToL1 = 188, OffL2ToL2 = 220, OffDaCommitment = 252;
    private const int OffPublicInputHash = 284, OffProofType = 316, OffProofLen = 317, ProofBytesOffset = 321;
    private const int OptimisticProofLength = 85, OptimisticSequencerOffsetInProof = 61;

    private static byte[] R(byte fill) { var b = new byte[32]; for (var i = 0; i < 32; i++) b[i] = fill; return b; }

    /// <summary>Hash256 = SHA256(SHA256(x)).</summary>
    private static byte[] Hash256(byte[] x) => SHA256.HashData(SHA256.HashData(x));

    /// <summary>
    /// Build a valid commitment whose publicInputHash@284 equals the contract's reconstruction, so
    /// the C2 binding passes. l1MessageHash/blockContextHash are zero (and passed to SubmitBatch).
    /// </summary>
    private static (byte[] commitment, byte[] l1msg, byte[] blkctx) BuildCommitment(
        ulong batch,
        byte[] preState,
        byte[] postState,
        byte[] withdrawalRoot,
        byte proofType = 3,
        uint chainId = ChainId,
        byte[]? l2ToL2MessageRoot = null)
    {
        var proofLength = proofType == 2 ? OptimisticProofLength : 0;
        var c = new byte[ProofBytesOffset + proofLength];
        BinaryPrimitives.WriteUInt32LittleEndian(c.AsSpan(OffChainId, 4), chainId);
        BinaryPrimitives.WriteUInt64LittleEndian(c.AsSpan(OffBatch, 8), batch);
        BinaryPrimitives.WriteUInt64LittleEndian(c.AsSpan(OffFirstBlock, 8), batch);
        BinaryPrimitives.WriteUInt64LittleEndian(c.AsSpan(OffLastBlock, 8), batch);
        preState.CopyTo(c.AsSpan(OffPreState, 32));
        postState.CopyTo(c.AsSpan(OffPostState, 32));
        R(0x03).CopyTo(c.AsSpan(OffTxRoot, 32));
        R(0x04).CopyTo(c.AsSpan(OffReceiptRoot, 32));
        withdrawalRoot.CopyTo(c.AsSpan(OffWithdrawal, 32));
        R(0x06).CopyTo(c.AsSpan(OffL2ToL1, 32));
        (l2ToL2MessageRoot ?? R(0x07)).CopyTo(c.AsSpan(OffL2ToL2, 32));
        R(0x09).CopyTo(c.AsSpan(OffDaCommitment, 32)); // must be non-zero (RecordDataAvailability)
        c[OffProofType] = proofType;
        BinaryPrimitives.WriteUInt32LittleEndian(c.AsSpan(OffProofLen, 4), (uint)proofLength);
        if (proofType == 2)
        {
            c[ProofBytesOffset] = 2;
            for (var index = 0; index < 20; index++)
                c[ProofBytesOffset + OptimisticSequencerOffsetInProof + index] = (byte)(index + 1);
        }

        var l1msg = new byte[32];
        var blkctx = new byte[32];

        // Reconstruct publicInputHash exactly as SettlementManager.ComputePublicInputHash does.
        var pre = new byte[4 + 8 + 10 * 32];
        var pos = 0;
        Array.Copy(c, 0, pre, pos, 12); pos += 12; // chainId + batchNumber
        void Put(byte[] src, int off) { Array.Copy(src, off, pre, pos, 32); pos += 32; }
        Put(c, OffPreState); Put(c, OffPostState); Put(c, OffTxRoot); Put(c, OffReceiptRoot);
        Put(c, OffWithdrawal); Put(c, OffL2ToL1); Put(c, OffL2ToL2);
        Array.Copy(l1msg, 0, pre, pos, 32); pos += 32;
        Put(c, OffDaCommitment);
        Array.Copy(blkctx, 0, pre, pos, 32); pos += 32;
        Hash256(pre).CopyTo(c.AsSpan(OffPublicInputHash, 32));
        return (c, l1msg, blkctx);
    }

    private static NeoHubSettlementManager Deploy(
        TestEngine engine,
        byte securityLevel = 0,
        byte daMode = 0,
        Func<byte>? securityLevelProvider = null,
        Func<byte>? daModeProvider = null,
        Func<byte, bool>? daValidation = null,
        Func<uint, UInt256>? genesisStateRootProvider = null)
    {
        engine.Fee = 100_000_000_000L;
        var owner = engine.Sender;
        var crHash = UInt160.Parse("0x" + new string('1', 40));
        var vrHash = UInt160.Parse("0x" + new string('2', 40));
        var drHash = UInt160.Parse("0x" + new string('3', 40));
        var dvHash = UInt160.Parse("0x" + new string('4', 40));
        var ocHash = UInt160.Parse("0x" + new string('5', 40));

        engine.FromHash<NeoHubChainRegistry>(crHash, m =>
        {
            m.Setup(c => c.IsActive(It.IsAny<BigInteger?>())).Returns(true);
            m.Setup(c => c.GetSecurityLevel(It.IsAny<BigInteger?>()))
                .Returns(() => (BigInteger)(securityLevelProvider?.Invoke() ?? securityLevel));
            m.Setup(c => c.GetDAMode(It.IsAny<BigInteger?>()))
                .Returns(() => (BigInteger)(daModeProvider?.Invoke() ?? daMode));
            m.Setup(c => c.GetGatewayEnabled(It.IsAny<BigInteger?>())).Returns(true);
            m.Setup(c => c.GetGenesisStateRoot(It.IsAny<BigInteger?>()))
                .Returns((BigInteger? requestedChainId) =>
                    genesisStateRootProvider?.Invoke((uint)requestedChainId!.Value)
                    ?? GenesisStateRoot);
        }, checkExistence: false);
        engine.FromHash<NeoHubVerifierRegistry>(vrHash,
            m => m.Setup(c => c.VerifyCommitment(It.IsAny<byte[]?>())).Returns(true), checkExistence: false);
        UInt256? recordedDaCommitment = null;
        byte? recordedDaMode = null;
        engine.FromHash<NeoHubDARegistry>(drHash, m =>
        {
            m.Setup(c => c.Record(
                    It.IsAny<BigInteger?>(),
                    It.IsAny<BigInteger?>(),
                    It.IsAny<UInt256?>(),
                    It.IsAny<BigInteger?>()))
                .Callback((BigInteger? _, BigInteger? _, UInt256? commitment, BigInteger? mode) =>
                {
                    recordedDaCommitment = commitment;
                    recordedDaMode = (byte)mode!.Value;
                });
            m.Setup(c => c.GetCommitment(It.IsAny<BigInteger?>(), It.IsAny<BigInteger?>()))
                .Returns(() => recordedDaCommitment);
            m.Setup(c => c.GetMode(It.IsAny<BigInteger?>(), It.IsAny<BigInteger?>()))
                .Returns(() => (BigInteger)(recordedDaMode ?? 0));
        }, checkExistence: false);
        engine.FromHash<NeoHubDAValidator>(dvHash,
            m => m.Setup(c => c.Validate(
                    It.IsAny<BigInteger?>(),
                    It.IsAny<BigInteger?>(),
                    It.IsAny<UInt256?>(),
                    It.IsAny<BigInteger?>()))
                .Returns((BigInteger? _, BigInteger? _, UInt256? _, BigInteger? mode) =>
                    daValidation?.Invoke((byte)mode!.Value) ?? true),
            checkExistence: false);
        engine.FromHash<NeoHubOptimisticChallenge>(ocHash,
            m => m.Setup(c => c.OpenWindow(
                It.IsAny<BigInteger?>(),
                It.IsAny<BigInteger?>(),
                It.IsAny<UInt160?>())).Returns((BigInteger)0),
            checkExistence: false);

        var sm = engine.Deploy<NeoHubSettlementManager>(
            NeoHubSettlementManager.Nef, NeoHubSettlementManager.Manifest, new object[] { owner, crHash, vrHash });
        sm.DARegistry = drHash;
        sm.DAValidator = dvHash;
        sm.OptimisticChallenge = ocHash;
        return sm;
    }

    private static void WireGatewayRouter(
        TestEngine engine,
        NeoHubSettlementManager settlementManager,
        UInt160 routerHash)
    {
        engine.FromHash<MockSettlementManager_MessageRouter>(routerHash, mock =>
            mock.Setup(router => router.PublishGlobalRoot(
                    It.IsAny<BigInteger?>(),
                    It.IsAny<UInt256?>(),
                    It.IsAny<UInt256?>(),
                    It.IsAny<BigInteger?>(),
                    It.IsAny<BigInteger?>(),
                    It.IsAny<BigInteger?>(),
                    It.IsAny<UInt256?>(),
                    It.IsAny<UInt256?>(),
                    It.IsAny<byte[]?>()))
                .Returns(true),
            checkExistence: false);
        settlementManager.MessageRouter = routerHash;
    }

    private static UInt160 WireGovernanceController(
        TestEngine engine,
        NeoHubSettlementManager settlementManager,
        Func<ulong, bool> isApprovedAndTimelocked,
        Func<ulong, byte[], bool> matchesProposalPayload)
    {
        var governanceHash = UInt160.Parse("0x" + new string('7', 40));
        engine.FromHash<MockSettlementManager_GovernanceController>(governanceHash, mock =>
        {
            mock.Setup(governance => governance.IsApprovedAndTimelocked(It.IsAny<BigInteger?>()))
                .Returns((BigInteger? proposalId) =>
                    isApprovedAndTimelocked((ulong)proposalId!.Value));
            mock.Setup(governance => governance.MatchesProposalPayload(
                    It.IsAny<BigInteger?>(), It.IsAny<byte[]?>()))
                .Returns((BigInteger? proposalId, byte[]? payload) =>
                    matchesProposalPayload((ulong)proposalId!.Value, payload!));
        }, checkExistence: false);
        settlementManager.GovernanceController = governanceHash;
        return governanceHash;
    }

    private static byte[] PackGatewayReferences(
        IReadOnlyList<(uint ChainId, ulong BatchNumber)> references)
    {
        var result = new byte[references.Count * 12];
        for (var index = 0; index < references.Count; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                result.AsSpan(index * 12, 4),
                references[index].ChainId);
            BinaryPrimitives.WriteUInt64LittleEndian(
                result.AsSpan(index * 12 + 4, 8),
                references[index].BatchNumber);
        }
        return result;
    }

    private static byte[] ComputeMerkleRoot(IReadOnlyList<byte[]> leaves, bool duplicateOdd)
    {
        Assert.IsGreaterThan(0, leaves.Count);
        var level = leaves.Select(leaf => leaf.ToArray()).ToList();
        while (level.Count > 1)
        {
            var next = new List<byte[]>((level.Count + 1) / 2);
            for (var index = 0; index < level.Count; index += 2)
            {
                if (index + 1 >= level.Count)
                {
                    next.Add(duplicateOdd
                        ? HashGatewayPair(level[index], level[index])
                        : level[index]);
                }
                else
                {
                    next.Add(HashGatewayPair(level[index], level[index + 1]));
                }
            }
            level = next;
        }
        return level[0];
    }

    private static byte[] HashGatewayPair(byte[] left, byte[] right)
    {
        var pair = new byte[64];
        left.CopyTo(pair, 0);
        right.CopyTo(pair, 32);
        return Hash256(pair);
    }

    private static (
        byte[] References,
        UInt256 ConstituentRoot,
        UInt256 GlobalRoot,
        IReadOnlyList<byte[]> Commitments) FinalizeGatewayConstituents(
            NeoHubSettlementManager settlementManager,
            int count)
    {
        var references = new List<(uint ChainId, ulong BatchNumber)>(count);
        var commitments = new List<byte[]>(count);
        var messageRoots = new List<byte[]>(count);
        for (var index = 0; index < count; index++)
        {
            var chainId = ChainId + (uint)index;
            var messageRoot = R((byte)(0x40 + index));
            var (commitment, l1MessageHash, blockContextHash) = BuildCommitment(
                batch: 1,
                preState: GenesisState,
                postState: R((byte)(0x80 + index)),
                withdrawalRoot: R((byte)(0x50 + index)),
                proofType: 3,
                chainId: chainId,
                l2ToL2MessageRoot: messageRoot);
            settlementManager.SubmitBatch(commitment, l1MessageHash, blockContextHash);
            settlementManager.FinalizeBatch(chainId, 1);
            references.Add((chainId, 1));
            commitments.Add(commitment);
            messageRoots.Add(messageRoot);
        }

        return (
            PackGatewayReferences(references),
            new UInt256(ComputeMerkleRoot(commitments.Select(Hash256).ToArray(), duplicateOdd: true)),
            new UInt256(ComputeMerkleRoot(messageRoots, duplicateOdd: false)),
            commitments);
    }

    [TestMethod]
    public void SubmitBatch_ValidiumWithZkAndNeoFsDa_IsAccepted()
    {
        var engine = new TestEngine(true);
        var sm = Deploy(engine, securityLevel: 4, daMode: 1);
        var (commitment, l1MessageHash, blockContextHash) = BuildCommitment(
            batch: 1,
            preState: GenesisState,
            postState: R(0xA1),
            withdrawalRoot: R(0x51),
            proofType: 3);

        sm.SubmitBatch(commitment, l1MessageHash, blockContextHash);

        Assert.AreEqual((BigInteger)1, sm.GetBatchStatus(ChainId, 1));
    }

    [TestMethod]
    public void SubmitBatch_FirstBatchRequiresRegisteredGenesisStateRoot()
    {
        var engine = new TestEngine(true);
        var sm = Deploy(engine);
        Assert.AreEqual(GenesisStateRoot, sm.GetCanonicalStateRoot(ChainId));
        var (wrong, wrongL1MessageHash, wrongBlockContextHash) = BuildCommitment(
            batch: 1,
            preState: R(0x99),
            postState: R(0xA1),
            withdrawalRoot: R(0x51));

        Assert.ThrowsExactly<TestException>(
            () => sm.SubmitBatch(wrong, wrongL1MessageHash, wrongBlockContextHash),
            "a verifier-approved transition from an attacker-selected first state must fail");
        Assert.AreEqual((BigInteger)0, sm.GetBatchStatus(ChainId, 1));

        var (correct, l1MessageHash, blockContextHash) = BuildCommitment(
            batch: 1,
            preState: GenesisState,
            postState: R(0xA1),
            withdrawalRoot: R(0x51));
        sm.SubmitBatch(correct, l1MessageHash, blockContextHash);
        Assert.AreEqual((BigInteger)1, sm.GetBatchStatus(ChainId, 1));
    }

    [TestMethod]
    public void SubmitBatch_MissingRegisteredGenesisStateRoot_FailsClosed()
    {
        var engine = new TestEngine(true);
        var sm = Deploy(
            engine,
            genesisStateRootProvider: _ => UInt256.Zero);
        var (commitment, l1MessageHash, blockContextHash) = BuildCommitment(
            batch: 1,
            preState: GenesisState,
            postState: R(0xA1),
            withdrawalRoot: R(0x51));

        Assert.ThrowsExactly<TestException>(
            () => sm.SubmitBatch(commitment, l1MessageHash, blockContextHash));
        Assert.AreEqual((BigInteger)0, sm.GetBatchStatus(ChainId, 1));
    }

    [TestMethod]
    [DataRow(0, 0, false)]
    [DataRow(0, 1, true)]
    [DataRow(0, 2, true)]
    [DataRow(0, 3, true)]
    [DataRow(1, 0, false)]
    [DataRow(1, 1, true)]
    [DataRow(1, 2, true)]
    [DataRow(1, 3, true)]
    [DataRow(2, 0, false)]
    [DataRow(2, 1, false)]
    [DataRow(2, 2, true)]
    [DataRow(2, 3, true)]
    [DataRow(3, 0, false)]
    [DataRow(3, 1, false)]
    [DataRow(3, 2, false)]
    [DataRow(3, 3, true)]
    [DataRow(4, 0, false)]
    [DataRow(4, 1, false)]
    [DataRow(4, 2, false)]
    [DataRow(4, 3, true)]
    public void SubmitBatch_EnforcesExplicitProofSecurityCompatibility(
        int securityLevel,
        int proofType,
        bool expectedCompatible)
    {
        var engine = new TestEngine(true);
        var daMode = securityLevel == 4 ? (byte)1 : (byte)0;
        var sm = Deploy(engine, (byte)securityLevel, daMode);
        var (commitment, l1MessageHash, blockContextHash) = BuildCommitment(
            batch: 1,
            preState: GenesisState,
            postState: R(0xA1),
            withdrawalRoot: R(0x51),
            proofType: (byte)proofType);

        if (!expectedCompatible)
        {
            Assert.ThrowsExactly<TestException>(
                () => sm.SubmitBatch(commitment, l1MessageHash, blockContextHash));
            return;
        }

        sm.SubmitBatch(commitment, l1MessageHash, blockContextHash);

        var expectedStatus = proofType == 2 ? (BigInteger)2 : (BigInteger)1;
        Assert.AreEqual(expectedStatus, sm.GetBatchStatus(ChainId, 1));
    }

    [TestMethod]
    [DataRow(0, 0, true)]
    [DataRow(0, 1, true)]
    [DataRow(0, 2, true)]
    [DataRow(0, 3, true)]
    [DataRow(1, 0, true)]
    [DataRow(1, 1, true)]
    [DataRow(1, 2, true)]
    [DataRow(1, 3, true)]
    [DataRow(2, 0, true)]
    [DataRow(2, 1, true)]
    [DataRow(2, 2, true)]
    [DataRow(2, 3, true)]
    [DataRow(3, 0, true)]
    [DataRow(3, 1, false)]
    [DataRow(3, 2, false)]
    [DataRow(3, 3, false)]
    [DataRow(4, 0, false)]
    [DataRow(4, 1, true)]
    [DataRow(4, 2, true)]
    [DataRow(4, 3, true)]
    public void SubmitBatch_RejectsLegacyOrMaliciousSecurityAndDaMismatch(
        int securityLevel,
        int daMode,
        bool expectedCompatible)
    {
        var engine = new TestEngine(true);
        var proofType = securityLevel switch
        {
            0 or 1 => (byte)1,
            2 => (byte)2,
            _ => (byte)3,
        };
        var sm = Deploy(engine, (byte)securityLevel, (byte)daMode);
        var (commitment, l1MessageHash, blockContextHash) = BuildCommitment(
            batch: 1,
            preState: GenesisState,
            postState: R(0xA1),
            withdrawalRoot: R(0x51),
            proofType: proofType);

        if (!expectedCompatible)
        {
            Assert.ThrowsExactly<TestException>(
                () => sm.SubmitBatch(commitment, l1MessageHash, blockContextHash));
            return;
        }

        sm.SubmitBatch(commitment, l1MessageHash, blockContextHash);
    }

    [TestMethod]
    public void SubmitBatch_RejectsUnknownSecurityOrProofType()
    {
        var unknownSecurityEngine = new TestEngine(true);
        var unknownSecurityManager = Deploy(unknownSecurityEngine, securityLevel: 99, daMode: 0);
        var (validProof, l1MessageHash, blockContextHash) = BuildCommitment(
            batch: 1,
            preState: GenesisState,
            postState: R(0xA1),
            withdrawalRoot: R(0x51),
            proofType: 3);
        Assert.ThrowsExactly<TestException>(
            () => unknownSecurityManager.SubmitBatch(validProof, l1MessageHash, blockContextHash));

        var unknownProofEngine = new TestEngine(true);
        var unknownProofManager = Deploy(unknownProofEngine, securityLevel: 0, daMode: 0);
        var (unknownProof, secondL1MessageHash, secondBlockContextHash) = BuildCommitment(
            batch: 1,
            preState: GenesisState,
            postState: R(0xA1),
            withdrawalRoot: R(0x51),
            proofType: 99);
        Assert.ThrowsExactly<TestException>(
            () => unknownProofManager.SubmitBatch(unknownProof, secondL1MessageHash, secondBlockContextHash));
    }

    [TestMethod]
    public void FinalizeBatch_RevalidatesProofAndDaAfterChainSecurityConfigurationChanges()
    {
        byte currentSecurityLevel = 0;
        byte currentDaMode = 0;
        var proofUpgradeEngine = new TestEngine(true);
        var proofUpgradeManager = Deploy(
            proofUpgradeEngine,
            securityLevelProvider: () => currentSecurityLevel,
            daModeProvider: () => currentDaMode);
        var (multisigCommitment, l1MessageHash, blockContextHash) = BuildCommitment(
            batch: 1,
            preState: GenesisState,
            postState: R(0xA1),
            withdrawalRoot: R(0x51),
            proofType: 1);
        proofUpgradeManager.SubmitBatch(multisigCommitment, l1MessageHash, blockContextHash);

        currentSecurityLevel = 3;
        Assert.ThrowsExactly<TestException>(() => proofUpgradeManager.FinalizeBatch(ChainId, 1),
            "a pending multisig batch must not finalize after the chain upgrades to Validity");
        Assert.AreEqual((BigInteger)1, proofUpgradeManager.GetBatchStatus(ChainId, 1));

        currentSecurityLevel = 3;
        currentDaMode = 0;
        var daChangeEngine = new TestEngine(true);
        var daChangeManager = Deploy(
            daChangeEngine,
            securityLevelProvider: () => currentSecurityLevel,
            daModeProvider: () => currentDaMode);
        var (zkCommitment, secondL1MessageHash, secondBlockContextHash) = BuildCommitment(
            batch: 1,
            preState: GenesisState,
            postState: R(0xA1),
            withdrawalRoot: R(0x51),
            proofType: 3);
        daChangeManager.SubmitBatch(zkCommitment, secondL1MessageHash, secondBlockContextHash);

        currentDaMode = 1;
        Assert.ThrowsExactly<TestException>(() => daChangeManager.FinalizeBatch(ChainId, 1),
            "a pending Validity batch must not finalize after its DA mode becomes off-chain");
        Assert.AreEqual((BigInteger)1, daChangeManager.GetBatchStatus(ChainId, 1));
    }

    [TestMethod]
    public void FinalizeBatch_ValidatesTheDaModeRecordedForTheBatch()
    {
        byte currentDaMode = 1;
        var engine = new TestEngine(true);
        var sm = Deploy(
            engine,
            securityLevel: 0,
            daModeProvider: () => currentDaMode,
            daValidation: mode => mode == 1);
        var (commitment, l1MessageHash, blockContextHash) = BuildCommitment(
            batch: 1,
            preState: GenesisState,
            postState: R(0xA1),
            withdrawalRoot: R(0x51),
            proofType: 1);
        sm.SubmitBatch(commitment, l1MessageHash, blockContextHash);

        currentDaMode = 2;
        sm.FinalizeBatch(ChainId, 1);

        Assert.AreEqual((BigInteger)3, sm.GetBatchStatus(ChainId, 1));
    }

    [TestMethod]
    public void SubmitBatch_FinalizeBatch_RecordsCanonicalRoot()
    {
        var engine = new TestEngine(true);
        var sm = Deploy(engine);
        var post1 = R(0xA1);

        var (c1, l1, blk) = BuildCommitment(batch: 1, preState: GenesisState, postState: post1, withdrawalRoot: R(0x51));
        sm.SubmitBatch(c1, l1, blk);
        Assert.AreEqual((BigInteger)1, sm.GetBatchStatus(ChainId, 1), "batch 1 should be Pending(1)");
        Assert.AreEqual(UInt256.Zero, sm.GetL2ToL1MessageRoot(ChainId, 1),
            "pending message roots must not be consumable");
        Assert.AreEqual(UInt256.Zero, sm.GetL2ToL2MessageRoot(ChainId, 1),
            "pending message roots must not be consumable");
        Assert.AreEqual(UInt256.Zero, sm.GetFinalizedTxRoot(ChainId, 1),
            "pending transaction roots must not authorize forced consumption");

        sm.FinalizeBatch(ChainId, 1);
        Assert.AreEqual((BigInteger)3, sm.GetBatchStatus(ChainId, 1), "batch 1 should be Finalized(3)");
        Assert.AreEqual(new UInt256(post1), sm.GetCanonicalStateRoot(ChainId), "canonical root must be batch 1 postState");
        Assert.AreEqual((BigInteger)1, sm.GetLatestFinalizedBatch(ChainId));
        Assert.AreEqual(new UInt256(R(0x06)), sm.GetL2ToL1MessageRoot(ChainId, 1));
        Assert.AreEqual(new UInt256(R(0x07)), sm.GetL2ToL2MessageRoot(ChainId, 1));
        Assert.AreEqual(new UInt256(R(0x03)), sm.GetFinalizedTxRoot(ChainId, 1));
    }

    [TestMethod]
    public void GetChallengeableBatchHeader_ReturnsCanonicalStoredHeader_OnlyWhileChallengeable()
    {
        var engine = new TestEngine(true);
        var sm = Deploy(engine, securityLevel: 0);
        var (commitment, l1MessageHash, blockContextHash) = BuildCommitment(
            batch: 1,
            preState: GenesisState,
            postState: R(0xA1),
            withdrawalRoot: R(0x51),
            proofType: 2);

        Assert.ThrowsExactly<TestException>(() => sm.GetChallengeableBatchHeader(ChainId, 1));
        sm.SubmitBatch(commitment, l1MessageHash, blockContextHash);

        Assert.AreEqual((BigInteger)2, sm.GetBatchStatus(ChainId, 1));
        CollectionAssert.AreEqual(
            commitment.AsSpan(0, ProofBytesOffset).ToArray(),
            sm.GetChallengeableBatchHeader(ChainId, 1)!);

        sm.RevertBatch(ChainId, 1);
        Assert.ThrowsExactly<TestException>(() => sm.GetChallengeableBatchHeader(ChainId, 1));
    }

    [TestMethod]
    public void SubmitBatch_RejectsTamperedPostStateRoot_C2Binding()
    {
        var engine = new TestEngine(true);
        var sm = Deploy(engine);
        var (c1, l1, blk) = BuildCommitment(batch: 1, preState: GenesisState, postState: R(0xA1), withdrawalRoot: R(0x51));
        // Tamper the recorded postStateRoot AFTER the publicInputHash was computed → binding breaks.
        R(0xEE).CopyTo(c1.AsSpan(OffPostState, 32));
        Assert.ThrowsExactly<TestException>(() => sm.SubmitBatch(c1, l1, blk),
            "C2: a postStateRoot not bound to the publicInputHash must be rejected");
    }

    [TestMethod]
    public void RevertedHead_OrphanDescendant_CannotFinalize_Round2Fix()
    {
        var engine = new TestEngine(true);
        var sm = Deploy(engine);
        var post1 = R(0xA1);

        // Batch 1: authenticated genesis → finalize.
        var (c1, l1, blk) = BuildCommitment(batch: 1, preState: GenesisState, postState: post1, withdrawalRoot: R(0x51));
        sm.SubmitBatch(c1, l1, blk);
        sm.FinalizeBatch(ChainId, 1);

        // Batch 2: chains onto batch 1 (preState == canonical == post1). Submitted but not finalized.
        var (c2, l1b, blkb) = BuildCommitment(batch: 2, preState: post1, postState: R(0xA2), withdrawalRoot: R(0x52));
        sm.SubmitBatch(c2, l1b, blkb);
        Assert.AreEqual((BigInteger)1, sm.GetBatchStatus(ChainId, 2), "batch 2 Pending");

        // Owner reverts the finalized head (batch 1) → rewinds latest to 0 and restores genesis.
        sm.RevertBatch(ChainId, 1);
        Assert.AreEqual((BigInteger)0, sm.GetLatestFinalizedBatch(ChainId), "latest rewound to 0");
        Assert.AreEqual(GenesisStateRoot, sm.GetCanonicalStateRoot(ChainId));

        // Round-2 fix: FinalizeBatch(2) must now FAULT — batch 2 is no longer next-in-sequence and
        // would otherwise finalize onto the rewound root (making its withdrawalRoot claimable).
        Assert.ThrowsExactly<TestException>(() => sm.FinalizeBatch(ChainId, 2),
            "orphaned descendant of a reverted head must not finalize");
    }

    [TestMethod]
    public void LockGovernance_RequiresCompleteWiringAndFreezesOwnerMutationAndRollback()
    {
        var engine = new TestEngine(true);
        var settlementManager = Deploy(engine);
        var (commitment, l1MessageHash, blockContextHash) = BuildCommitment(
            batch: 1,
            preState: GenesisState,
            postState: R(0xA1),
            withdrawalRoot: R(0x51));
        settlementManager.SubmitBatch(commitment, l1MessageHash, blockContextHash);

        Assert.ThrowsExactly<TestException>(() => settlementManager.LockGovernance(),
            "locking without a GovernanceController must fail closed");
        var governanceHash = WireGovernanceController(
            engine,
            settlementManager,
            _ => true,
            (_, _) => true);
        Assert.ThrowsExactly<TestException>(() => settlementManager.LockGovernance(),
            "locking without the canonical MessageRouter must fail closed");

        settlementManager.MessageRouter = UInt160.Parse("0x" + new string('6', 40));
        settlementManager.LockGovernance();
        settlementManager.LockGovernance();

        Assert.IsTrue(settlementManager.IsGovernanceLocked);
        Assert.AreEqual(governanceHash, settlementManager.GovernanceController);
        Assert.ThrowsExactly<TestException>(() => settlementManager.Owner = governanceHash);
        Assert.ThrowsExactly<TestException>(() => settlementManager.DARegistry = governanceHash);
        Assert.ThrowsExactly<TestException>(() => settlementManager.DAValidator = governanceHash);
        Assert.ThrowsExactly<TestException>(() => settlementManager.OptimisticChallenge = governanceHash);
        Assert.ThrowsExactly<TestException>(() => settlementManager.MessageRouter = governanceHash);
        Assert.ThrowsExactly<TestException>(() => settlementManager.GovernanceController = engine.Sender);
        Assert.ThrowsExactly<TestException>(() => settlementManager.RevertBatch(ChainId, 1),
            "the bootstrap owner must lose direct rollback authority after the production lock");
        Assert.AreEqual((BigInteger)1, settlementManager.GetBatchStatus(ChainId, 1));
    }

    [TestMethod]
    public void BuildRevertBatchAction_UsesDomainSeparatedCanonicalLittleEndianEncoding()
    {
        var engine = new TestEngine(true);
        var settlementManager = Deploy(engine);
        const uint chainId = 0x04030201;
        const ulong batchNumber = 0x0C0B0A0908070605;

        var expected = Encoding.ASCII.GetBytes("neo4-gov:revertBatch")
            .Concat(settlementManager.Hash.GetSpan().ToArray())
            .Concat(new byte[]
            {
                0x01, 0x02, 0x03, 0x04,
                0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C,
            })
            .ToArray();

        CollectionAssert.AreEqual(
            expected,
            settlementManager.BuildRevertBatchAction(chainId, batchNumber)!);
        Assert.AreEqual(52, expected.Length,
            "the governance action must bind tag, exact SettlementManager, chain, and batch");
    }

    [TestMethod]
    public void BuildRevertBatchAction_BindsExecutingContractAgainstCrossDeploymentReplay()
    {
        var engine = new TestEngine(true);
        var firstSettlementManager = Deploy(engine);
        engine.SetTransactionSigners(
            UInt160.Parse("0x1112131415161718191a1b1c1d1e1f2021222324"));
        var secondSettlementManager = engine.Deploy<NeoHubSettlementManager>(
            NeoHubSettlementManager.Nef,
            NeoHubSettlementManager.Manifest,
            new object[]
            {
                engine.Sender,
                UInt160.Parse("0x" + new string('1', 40)),
                UInt160.Parse("0x" + new string('2', 40)),
            });

        var firstAction = firstSettlementManager.BuildRevertBatchAction(ChainId, 7)!;
        var secondAction = secondSettlementManager.BuildRevertBatchAction(ChainId, 7)!;
        var tagLength = Encoding.ASCII.GetByteCount("neo4-gov:revertBatch");

        Assert.AreNotEqual(firstSettlementManager.Hash, secondSettlementManager.Hash,
            "the regression setup must deploy two distinct SettlementManager contracts");
        Assert.IsFalse(firstAction.SequenceEqual(secondAction),
            "one approved action must not be replayable against another SettlementManager deployment");
        CollectionAssert.AreEqual(
            firstSettlementManager.Hash.GetSpan().ToArray(),
            firstAction.AsSpan(tagLength, UInt160.Length).ToArray());
        CollectionAssert.AreEqual(
            secondSettlementManager.Hash.GetSpan().ToArray(),
            secondAction.AsSpan(tagLength, UInt160.Length).ToArray());
    }

    [TestMethod]
    public void RevertBatchViaProposal_RequiresExactApprovedTimelockedUnconsumedPayload()
    {
        var engine = new TestEngine(true);
        var settlementManager = Deploy(engine);
        var (commitment, l1MessageHash, blockContextHash) = BuildCommitment(
            batch: 1,
            preState: GenesisState,
            postState: R(0xA1),
            withdrawalRoot: R(0x51));
        settlementManager.SubmitBatch(commitment, l1MessageHash, blockContextHash);
        settlementManager.FinalizeBatch(ChainId, 1);

        const ulong proposalId = 42;
        var payloadMatches = false;
        var expectedAction = settlementManager.BuildRevertBatchAction(ChainId, 1)!;
        WireGovernanceController(
            engine,
            settlementManager,
            candidate => candidate == proposalId,
            (candidate, payload) => candidate == proposalId
                && payloadMatches
                && payload.SequenceEqual(expectedAction));
        settlementManager.MessageRouter = UInt160.Parse("0x" + new string('6', 40));
        settlementManager.LockGovernance();

        Assert.ThrowsExactly<TestException>(() => settlementManager.RevertBatch(ChainId, 1));
        Assert.ThrowsExactly<TestException>(() =>
            settlementManager.RevertBatchViaProposal(ChainId, 1, proposalId - 1),
            "an unapproved proposal must not authorize rollback");
        Assert.ThrowsExactly<TestException>(() =>
            settlementManager.RevertBatchViaProposal(ChainId, 1, proposalId),
            "an approved proposal for different payload bytes must not authorize rollback");

        payloadMatches = true;
        settlementManager.RevertBatchViaProposal(ChainId, 1, proposalId);

        Assert.AreEqual((BigInteger)4, settlementManager.GetBatchStatus(ChainId, 1));
        Assert.AreEqual((BigInteger)0, settlementManager.GetLatestFinalizedBatch(ChainId));
        Assert.AreEqual(GenesisStateRoot, settlementManager.GetCanonicalStateRoot(ChainId));
        Assert.ThrowsExactly<TestException>(() =>
            settlementManager.RevertBatchViaProposal(ChainId, 1, proposalId),
            "a governance rollback proposal must be consumable at most once");
    }

    [TestMethod]
    public void LockedGovernance_PreservesOptimisticChallengeRollbackForChallengeableBatch()
    {
        var engine = new TestEngine(true);
        var settlementManager = Deploy(engine, securityLevel: 2);
        var (commitment, l1MessageHash, blockContextHash) = BuildCommitment(
            batch: 1,
            preState: GenesisState,
            postState: R(0xA1),
            withdrawalRoot: R(0x51),
            proofType: 2);
        settlementManager.SubmitBatch(commitment, l1MessageHash, blockContextHash);

        settlementManager.OptimisticChallenge = engine.Sender;
        settlementManager.MessageRouter = UInt160.Parse("0x" + new string('6', 40));
        WireGovernanceController(engine, settlementManager, _ => false, (_, _) => false);
        settlementManager.LockGovernance();

        settlementManager.RevertBatch(ChainId, 1);
        Assert.AreEqual((BigInteger)4, settlementManager.GetBatchStatus(ChainId, 1));
    }

    [TestMethod]
    public void PublishGatewayGlobalRoot_RejectsPendingRevertedAndUnknownReferences()
    {
        var engine = new TestEngine(true);
        var settlementManager = Deploy(engine);
        var routerHash = UInt160.Parse("0x" + new string('6', 40));
        WireGatewayRouter(engine, settlementManager, routerHash);
        var messageRoot = R(0x47);
        var (commitment, l1MessageHash, blockContextHash) = BuildCommitment(
            batch: 1,
            preState: GenesisState,
            postState: R(0xA1),
            withdrawalRoot: R(0x51),
            l2ToL2MessageRoot: messageRoot);
        settlementManager.SubmitBatch(commitment, l1MessageHash, blockContextHash);
        var references = PackGatewayReferences(new[] { (ChainId, 1UL) });
        var commitmentRoot = new UInt256(Hash256(commitment));
        var globalRoot = new UInt256(messageRoot);

        Assert.ThrowsExactly<TestException>(() => settlementManager.PublishGatewayGlobalRoot(
            1, references, globalRoot, commitmentRoot, 1, 2, 1, new UInt256(R(0xA1)),
            new UInt256(R(0xD1)), new byte[] { 0xCA }));

        settlementManager.RevertBatch(ChainId, 1);
        Assert.ThrowsExactly<TestException>(() => settlementManager.PublishGatewayGlobalRoot(
            1, references, globalRoot, commitmentRoot, 1, 2, 1, new UInt256(R(0xA1)),
            new UInt256(R(0xD1)), new byte[] { 0xCA }));

        var unknown = PackGatewayReferences(new[] { (ChainId + 1, 1UL) });
        Assert.ThrowsExactly<TestException>(() => settlementManager.PublishGatewayGlobalRoot(
            1, unknown, globalRoot, commitmentRoot, 1, 2, 1, new UInt256(R(0xA1)),
            new UInt256(R(0xD1)), new byte[] { 0xCA }));
    }

    [TestMethod]
    public void PublishGatewayGlobalRoot_RejectsUnorderedDuplicateAndTamperedSets()
    {
        var engine = new TestEngine(true);
        var settlementManager = Deploy(engine);
        WireGatewayRouter(
            engine,
            settlementManager,
            UInt160.Parse("0x" + new string('6', 40)));
        var fixture = FinalizeGatewayConstituents(settlementManager, 3);
        var unordered = fixture.References.ToArray();
        var first = unordered.AsSpan(0, 12).ToArray();
        unordered.AsSpan(12, 12).CopyTo(unordered.AsSpan(0, 12));
        first.CopyTo(unordered.AsSpan(12, 12));
        var duplicate = fixture.References.ToArray();
        duplicate.AsSpan(0, 12).CopyTo(duplicate.AsSpan(12, 12));

        Assert.ThrowsExactly<TestException>(() => settlementManager.PublishGatewayGlobalRoot(
            7, unordered, fixture.GlobalRoot, fixture.ConstituentRoot, 3, 2, 1,
            new UInt256(R(0xA1)), new UInt256(R(0xD1)), new byte[] { 0xCA }));
        Assert.ThrowsExactly<TestException>(() => settlementManager.PublishGatewayGlobalRoot(
            7, duplicate, fixture.GlobalRoot, fixture.ConstituentRoot, 3, 2, 1,
            new UInt256(R(0xA1)), new UInt256(R(0xD1)), new byte[] { 0xCA }));
        Assert.ThrowsExactly<TestException>(() => settlementManager.PublishGatewayGlobalRoot(
            7, fixture.References, new UInt256(R(0xEE)), fixture.ConstituentRoot, 3, 2, 1,
            new UInt256(R(0xA1)), new UInt256(R(0xD1)), new byte[] { 0xCA }));
        Assert.ThrowsExactly<TestException>(() => settlementManager.PublishGatewayGlobalRoot(
            7, fixture.References, fixture.GlobalRoot, new UInt256(R(0xEF)), 3, 2, 1,
            new UInt256(R(0xA1)), new UInt256(R(0xD1)), new byte[] { 0xCA }));
    }

    [TestMethod]
    [DataRow(3)]
    [DataRow(5)]
    public void PublishGatewayGlobalRoot_RebuildsPinnedOddLeafPolicies(int constituentCount)
    {
        var engine = new TestEngine(true);
        var settlementManager = Deploy(engine);
        WireGatewayRouter(
            engine,
            settlementManager,
            UInt160.Parse("0x" + new string('6', 40)));
        var fixture = FinalizeGatewayConstituents(settlementManager, constituentCount);

        Assert.IsTrue(settlementManager.PublishGatewayGlobalRoot(
            9,
            fixture.References,
            fixture.GlobalRoot,
            fixture.ConstituentRoot,
            constituentCount,
            2,
            1,
            new UInt256(R(0xA1)),
            new UInt256(R(0xD1)),
            new byte[] { 0xCA }));
        for (var index = 0; index < constituentCount; index++)
            Assert.AreEqual((BigInteger)1, settlementManager.GetGatewayFinalizedThrough(ChainId + index));
    }

    [TestMethod]
    public void PublishGatewayGlobalRoot_AllowsCanonicalZeroMessageRoot()
    {
        var engine = new TestEngine(true);
        var settlementManager = Deploy(engine);
        WireGatewayRouter(
            engine,
            settlementManager,
            UInt160.Parse("0x" + new string('6', 40)));
        var (commitment, l1MessageHash, blockContextHash) = BuildCommitment(
            batch: 1,
            preState: GenesisState,
            postState: R(0xA1),
            withdrawalRoot: R(0x51),
            l2ToL2MessageRoot: R(0x00));
        settlementManager.SubmitBatch(commitment, l1MessageHash, blockContextHash);
        settlementManager.FinalizeBatch(ChainId, 1);
        var references = PackGatewayReferences(new[] { (ChainId, 1UL) });

        Assert.IsTrue(settlementManager.PublishGatewayGlobalRoot(
            10,
            references,
            UInt256.Zero,
            new UInt256(Hash256(commitment)),
            1,
            2,
            1,
            new UInt256(R(0xA1)),
            new UInt256(R(0xD1)),
            new byte[] { 0xCA }));
        Assert.AreEqual((BigInteger)1, settlementManager.GetGatewayFinalizedThrough(ChainId));
    }

    [TestMethod]
    public void PublishGatewayGlobalRoot_RouterFaultRollsBackEveryWatermark()
    {
        var engine = new TestEngine(true);
        var settlementManager = Deploy(engine);
        var router = engine.Deploy<NeoHubMessageRouter>(
            NeoHubMessageRouter.Nef,
            NeoHubMessageRouter.Manifest,
            new object[] { engine.Sender, settlementManager.Hash });
        settlementManager.MessageRouter = router.Hash;
        var fixture = FinalizeGatewayConstituents(settlementManager, 2);

        Assert.ThrowsExactly<TestException>(() => settlementManager.PublishGatewayGlobalRoot(
            11, fixture.References, fixture.GlobalRoot, fixture.ConstituentRoot, 2, 2, 1,
            new UInt256(R(0xA1)), new UInt256(R(0xD1)), new byte[] { 0xCA }));

        Assert.AreEqual((BigInteger)0, settlementManager.GetGatewayFinalizedThrough(ChainId));
        Assert.AreEqual((BigInteger)0, settlementManager.GetGatewayFinalizedThrough(ChainId + 1));
        Assert.AreEqual(UInt256.Zero, router.GetGlobalRoot(11));
    }

    [TestMethod]
    public void PublishGatewayGlobalRoot_RealRouterWitnessLocksPublishedHistoryOnly()
    {
        var engine = new TestEngine(true);
        var settlementManager = Deploy(engine);
        var router = engine.Deploy<NeoHubMessageRouter>(
            NeoHubMessageRouter.Nef,
            NeoHubMessageRouter.Manifest,
            new object[] { engine.Sender, settlementManager.Hash });
        settlementManager.MessageRouter = router.Hash;
        var fixture = FinalizeGatewayConstituents(settlementManager, 1);
        var verifierHash = UInt160.Parse("0x" + new string('9', 40));
        var governanceHash = UInt160.Parse("0x" + new string('8', 40));
        var verificationKey = new UInt256(R(0xA1));
        var replayDomain = new UInt256(R(0xD1));
        var proof = new byte[] { 0xCA, 0xFE };
        engine.FromHash<MockMessageRouter_ZkVerifier>(verifierHash, mock =>
            mock.Setup(verifier => verifier.VerifyZkProof(
                    It.IsAny<BigInteger?>(),
                    It.IsAny<byte[]?>(),
                    It.IsAny<byte[]?>(),
                    It.IsAny<byte[]?>()))
                .Returns(true),
            checkExistence: false);
        router.SetGlobalRootVerifier(verifierHash, 1, 2, verificationKey, replayDomain);
        router.GovernanceController = governanceHash;
        router.LockGlobalRootGovernance();

        Assert.ThrowsExactly<TestException>(() => router.PublishGlobalRoot(
            13, fixture.GlobalRoot, fixture.ConstituentRoot, 1, 2, 1,
            verificationKey, replayDomain, proof));
        Assert.IsTrue(settlementManager.PublishGatewayGlobalRoot(
            13, fixture.References, fixture.GlobalRoot, fixture.ConstituentRoot, 1, 2, 1,
            verificationKey, replayDomain, proof));

        Assert.AreEqual(fixture.GlobalRoot, router.GetGlobalRoot(13));
        Assert.AreEqual((BigInteger)1, settlementManager.GetGatewayFinalizedThrough(ChainId));
        Assert.ThrowsExactly<TestException>(() => settlementManager.RevertBatch(ChainId, 1));

        var (second, l1MessageHash, blockContextHash) = BuildCommitment(
            batch: 2,
            preState: R(0x80),
            postState: R(0x81),
            withdrawalRoot: R(0x52),
            l2ToL2MessageRoot: R(0x48));
        settlementManager.SubmitBatch(second, l1MessageHash, blockContextHash);
        settlementManager.FinalizeBatch(ChainId, 2);
        settlementManager.RevertBatch(ChainId, 2);
        Assert.AreEqual((BigInteger)1, settlementManager.GetLatestFinalizedBatch(ChainId));
        Assert.AreEqual((BigInteger)4, settlementManager.GetBatchStatus(ChainId, 2));
    }

    [TestMethod]
    public void PublishGatewayGlobalRoot_RejectsMoreThan4096Constituents()
    {
        var engine = new TestEngine(true);
        var settlementManager = Deploy(engine);
        Assert.ThrowsExactly<TestException>(() => settlementManager.PublishGatewayGlobalRoot(
            1,
            new byte[4097 * 12],
            new UInt256(R(0x51)),
            new UInt256(R(0x61)),
            4097,
            2,
            1,
            new UInt256(R(0xA1)),
            new UInt256(R(0xD1)),
            new byte[] { 0xCA }));
    }
}
