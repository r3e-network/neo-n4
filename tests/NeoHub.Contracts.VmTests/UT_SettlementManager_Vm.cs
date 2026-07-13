using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using Moq;
using Neo;
using Neo.SmartContract.Testing;
using Neo.SmartContract.Testing.Exceptions;

namespace NeoHub.Contracts.VmTests;

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
        ulong batch, byte[] preState, byte[] postState, byte[] withdrawalRoot, byte proofType = 3)
    {
        var proofLength = proofType == 2 ? OptimisticProofLength : 0;
        var c = new byte[ProofBytesOffset + proofLength];
        BinaryPrimitives.WriteUInt32LittleEndian(c.AsSpan(OffChainId, 4), ChainId);
        BinaryPrimitives.WriteUInt64LittleEndian(c.AsSpan(OffBatch, 8), batch);
        BinaryPrimitives.WriteUInt64LittleEndian(c.AsSpan(OffFirstBlock, 8), batch);
        BinaryPrimitives.WriteUInt64LittleEndian(c.AsSpan(OffLastBlock, 8), batch);
        preState.CopyTo(c.AsSpan(OffPreState, 32));
        postState.CopyTo(c.AsSpan(OffPostState, 32));
        R(0x03).CopyTo(c.AsSpan(OffTxRoot, 32));
        R(0x04).CopyTo(c.AsSpan(OffReceiptRoot, 32));
        withdrawalRoot.CopyTo(c.AsSpan(OffWithdrawal, 32));
        R(0x06).CopyTo(c.AsSpan(OffL2ToL1, 32));
        R(0x07).CopyTo(c.AsSpan(OffL2ToL2, 32));
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
        Func<byte, bool>? daValidation = null)
    {
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

    [TestMethod]
    public void SubmitBatch_ValidiumWithZkAndNeoFsDa_IsAccepted()
    {
        var engine = new TestEngine(true);
        var sm = Deploy(engine, securityLevel: 4, daMode: 1);
        var (commitment, l1MessageHash, blockContextHash) = BuildCommitment(
            batch: 1,
            preState: R(0x00),
            postState: R(0xA1),
            withdrawalRoot: R(0x51),
            proofType: 3);

        sm.SubmitBatch(commitment, l1MessageHash, blockContextHash);

        Assert.AreEqual((BigInteger)1, sm.GetBatchStatus(ChainId, 1));
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
            preState: R(0x00),
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
            preState: R(0x00),
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
            preState: R(0x00),
            postState: R(0xA1),
            withdrawalRoot: R(0x51),
            proofType: 3);
        Assert.ThrowsExactly<TestException>(
            () => unknownSecurityManager.SubmitBatch(validProof, l1MessageHash, blockContextHash));

        var unknownProofEngine = new TestEngine(true);
        var unknownProofManager = Deploy(unknownProofEngine, securityLevel: 0, daMode: 0);
        var (unknownProof, secondL1MessageHash, secondBlockContextHash) = BuildCommitment(
            batch: 1,
            preState: R(0x00),
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
            preState: R(0x00),
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
            preState: R(0x00),
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
            preState: R(0x00),
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

        var (c1, l1, blk) = BuildCommitment(batch: 1, preState: R(0x00), postState: post1, withdrawalRoot: R(0x51));
        sm.SubmitBatch(c1, l1, blk);
        Assert.AreEqual((BigInteger)1, sm.GetBatchStatus(ChainId, 1), "batch 1 should be Pending(1)");
        Assert.AreEqual(UInt256.Zero, sm.GetL2ToL1MessageRoot(ChainId, 1),
            "pending message roots must not be consumable");
        Assert.AreEqual(UInt256.Zero, sm.GetL2ToL2MessageRoot(ChainId, 1),
            "pending message roots must not be consumable");

        sm.FinalizeBatch(ChainId, 1);
        Assert.AreEqual((BigInteger)3, sm.GetBatchStatus(ChainId, 1), "batch 1 should be Finalized(3)");
        Assert.AreEqual(new UInt256(post1), sm.GetCanonicalStateRoot(ChainId), "canonical root must be batch 1 postState");
        Assert.AreEqual((BigInteger)1, sm.GetLatestFinalizedBatch(ChainId));
        Assert.AreEqual(new UInt256(R(0x06)), sm.GetL2ToL1MessageRoot(ChainId, 1));
        Assert.AreEqual(new UInt256(R(0x07)), sm.GetL2ToL2MessageRoot(ChainId, 1));
    }

    [TestMethod]
    public void SubmitBatch_RejectsTamperedPostStateRoot_C2Binding()
    {
        var engine = new TestEngine(true);
        var sm = Deploy(engine);
        var (c1, l1, blk) = BuildCommitment(batch: 1, preState: R(0x00), postState: R(0xA1), withdrawalRoot: R(0x51));
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

        // Batch 1: genesis (preState 0) → finalize.
        var (c1, l1, blk) = BuildCommitment(batch: 1, preState: R(0x00), postState: post1, withdrawalRoot: R(0x51));
        sm.SubmitBatch(c1, l1, blk);
        sm.FinalizeBatch(ChainId, 1);

        // Batch 2: chains onto batch 1 (preState == canonical == post1). Submitted but not finalized.
        var (c2, l1b, blkb) = BuildCommitment(batch: 2, preState: post1, postState: R(0xA2), withdrawalRoot: R(0x52));
        sm.SubmitBatch(c2, l1b, blkb);
        Assert.AreEqual((BigInteger)1, sm.GetBatchStatus(ChainId, 2), "batch 2 Pending");

        // Owner reverts the finalized head (batch 1) → rewinds latest to 0, canonical cleared.
        sm.RevertBatch(ChainId, 1);
        Assert.AreEqual((BigInteger)0, sm.GetLatestFinalizedBatch(ChainId), "latest rewound to 0");

        // Round-2 fix: FinalizeBatch(2) must now FAULT — batch 2 is no longer next-in-sequence and
        // would otherwise finalize onto the rewound root (making its withdrawalRoot claimable).
        Assert.ThrowsExactly<TestException>(() => sm.FinalizeBatch(ChainId, 2),
            "orphaned descendant of a reverted head must not finalize");
    }
}
