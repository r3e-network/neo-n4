using System.Buffers.Binary;
using System.ComponentModel;
using System.Numerics;
using System.Security.Cryptography;
using Moq;
using Neo;
using Neo.SmartContract.Testing;
using Neo.SmartContract.Testing.Exceptions;

namespace NeoHub.Contracts.VmTests;

[TestClass]
public class UT_RollupHub_Vm
{
    private const uint ChainId = 1001;
    private static byte[] GenesisState => R(0x10);
    private static UInt256 GenesisStateRoot => new(GenesisState);

    private const int OffChainId = 0, OffBatch = 4, OffFirstBlock = 12, OffLastBlock = 20;
    private const int OffPreState = 28, OffPostState = 60, OffTxRoot = 92, OffReceiptRoot = 124;
    private const int OffWithdrawal = 156, OffL2ToL1 = 188, OffL2ToL2 = 220, OffDaCommitment = 252;
    private const int OffPublicInputHash = 284, OffProofType = 316, OffProofLen = 317, ProofBytesOffset = 321;

    /// <summary>Arbitrary address the stub verifier is mocked at; the hub only needs a
    /// non-zero registry target whose <c>verifyProof</c> answers.</summary>
    private static readonly UInt160 VerifierHash = UInt160.Parse("0x" + new string('5', 40));

    /// <summary>
    /// Hash256 of the shared canonical public-inputs preimage
    /// (tests/Shared/canonical_encoding_vectors.hex, exported to the Rust lane from
    /// canonical_encoding_parity.rs). The vector's l1MessageHash (b1×32) and daCommitment (09×32)
    /// are distinct, so any encoder that swaps their tail positions computes a different digest.
    /// </summary>
    private const string CanonicalPublicInputHashHex =
        "034f85e7b09682466547018fefe98ea82dd0582ba29ee7998807fe1ec023f0bc";

    /// <summary>Digest of the same shared bytes assembled in the pre-fix (swapped) tail order
    /// header ‖ daCommitment ‖ l1MessageHash ‖ blockContextHash — computed independently of both
    /// the contract and BatchSerializer, and pinned here so a regression to that layout fails a
    /// test instead of silently re-forking the protocol.</summary>
    private const string SwappedPublicInputHashHex =
        "8d68e62ccddbc753fad17dc3e61486b6c28765576456839a60453382e524bb9a";

    private static byte[] R(byte fill) { var b = new byte[32]; for (var i = 0; i < 32; i++) b[i] = fill; return b; }
    private static byte[] Hash256(byte[] x) => SHA256.HashData(SHA256.HashData(x));

    private static (TestEngine engine, NeoHubRollupHub hub, UInt160 owner) Deploy(bool verifierAccepts = true)
    {
        var engine = new TestEngine(true);
        engine.Fee = 100_000_000_000L;
        var owner = engine.Sender;
        engine.FromHash<StubVerifier>(VerifierHash, mock =>
            mock.Setup(v => v.VerifyProof(It.IsAny<byte[]>())).Returns(verifierAccepts),
            checkExistence: false);
        var hub = engine.Deploy<NeoHubRollupHub>(NeoHubRollupHub.Nef, NeoHubRollupHub.Manifest,
            new object[] { owner, VerifierHash });
        return (engine, hub, owner);
    }

    private static (byte[] commitment, byte[] l1msg, byte[] blkctx) BuildCommitment(
        ulong batch,
        byte[] preState,
        byte[] postState,
        byte proofType = 3,
        byte[]? proof = null)
    {
        proof ??= [0x01, 0x02, 0x03];
        var c = new byte[ProofBytesOffset + proof.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(c.AsSpan(OffChainId, 4), ChainId);
        BinaryPrimitives.WriteUInt64LittleEndian(c.AsSpan(OffBatch, 8), batch);
        BinaryPrimitives.WriteUInt64LittleEndian(c.AsSpan(OffFirstBlock, 8), (batch - 1) * 10 + 1);
        BinaryPrimitives.WriteUInt64LittleEndian(c.AsSpan(OffLastBlock, 8), batch * 10);
        preState.CopyTo(c.AsSpan(OffPreState, 32));
        postState.CopyTo(c.AsSpan(OffPostState, 32));
        R(0xAA).CopyTo(c.AsSpan(OffTxRoot, 32));
        R(0xBB).CopyTo(c.AsSpan(OffReceiptRoot, 32));
        R(0xCC).CopyTo(c.AsSpan(OffWithdrawal, 32));
        R(0xDD).CopyTo(c.AsSpan(OffL2ToL1, 32));
        R(0xEE).CopyTo(c.AsSpan(OffL2ToL2, 32));
        R(0xDA).CopyTo(c.AsSpan(OffDaCommitment, 32));
        c[OffProofType] = proofType;
        BinaryPrimitives.WriteInt32LittleEndian(c.AsSpan(OffProofLen, 4), proof.Length);
        proof.CopyTo(c.AsSpan(ProofBytesOffset, proof.Length));

        var l1msg = new byte[32];
        var blkctx = new byte[32];
        var pubInputHash = Hash256(BuildPublicInputsBuffer(c, l1msg, blkctx));
        pubInputHash.CopyTo(c.AsSpan(OffPublicInputHash, 32));

        return (c, l1msg, blkctx);
    }

    /// <summary>
    /// The canonical 352-byte public-inputs preimage (BatchSerializer.EncodePublicInputs layout):
    /// header fields up to l2ToL2MessageRoot, then l1MessageHash, then the commitment's
    /// daCommitment, then blockContextHash, then forcedInclusionCount (u32 LE).
    /// </summary>
    private static byte[] BuildPublicInputsBuffer(byte[] commitment, byte[] l1msg, byte[] blkctx, uint forcedInclusionCount = 0)
    {
        var buf = new byte[352];
        commitment.AsSpan(0, 252).CopyTo(buf.AsSpan(0, 252));
        l1msg.CopyTo(buf.AsSpan(252, 32));
        commitment.AsSpan(OffDaCommitment, 32).CopyTo(buf.AsSpan(284, 32));
        blkctx.CopyTo(buf.AsSpan(316, 32));
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(348, 4), forcedInclusionCount);
        return buf;
    }

    private static byte[] BuildChainConfig(uint chainId, byte securityLevel = 3, bool active = true)
    {
        var cfg = new byte[91];
        BinaryPrimitives.WriteUInt32LittleEndian(cfg.AsSpan(0, 4), chainId);
        // operatorManager, verifier, bridgeAdapter, messageAdapter (each 20 bytes)
        for (var i = 4; i < 84; i++) cfg[i] = 0x01;
        cfg[84] = securityLevel;
        cfg[85] = 0;             // L1 DA = 0
        cfg[86] = 1;             // gatewayEnabled
        cfg[87] = 1;             // permissionlessExit
        cfg[88] = 1;             // DbftCommittee
        cfg[89] = 0;             // Permissionless
        cfg[90] = (byte)(active ? 1 : 0);
        return cfg;
    }

    /// <summary>
    /// The exact commitment of tests/Shared/canonical_encoding_vectors.hex: chainId 1001, batch 1,
    /// blocks 2–3, proofType Multisig (1), empty proof, and the ten vector roots. Building it here
    /// from raw offsets keeps the VmTests bundle dependency-free; the digest it carries is the
    /// shared file's, not anything this test computes.
    /// </summary>
    private static byte[] BuildSharedVectorCommitment(byte[] publicInputHash)
    {
        var c = new byte[ProofBytesOffset];
        BinaryPrimitives.WriteUInt32LittleEndian(c.AsSpan(OffChainId, 4), 1001);
        BinaryPrimitives.WriteUInt64LittleEndian(c.AsSpan(OffBatch, 8), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(c.AsSpan(OffFirstBlock, 8), 2);
        BinaryPrimitives.WriteUInt64LittleEndian(c.AsSpan(OffLastBlock, 8), 3);
        R(0x10).CopyTo(c.AsSpan(OffPreState, 32));
        R(0xA1).CopyTo(c.AsSpan(OffPostState, 32));
        R(0x03).CopyTo(c.AsSpan(OffTxRoot, 32));
        R(0x04).CopyTo(c.AsSpan(OffReceiptRoot, 32));
        Hex("cd6983836343f9205e1d9c2c90fd891e827ee366762018cd13c31920bde7469e")
            .CopyTo(c.AsSpan(OffWithdrawal, 32));
        R(0x06).CopyTo(c.AsSpan(OffL2ToL1, 32));
        R(0x07).CopyTo(c.AsSpan(OffL2ToL2, 32));
        R(0x09).CopyTo(c.AsSpan(OffDaCommitment, 32));
        publicInputHash.CopyTo(c.AsSpan(OffPublicInputHash, 32));
        c[OffProofType] = 1; // Multisig — accepted for a Settled chain
        BinaryPrimitives.WriteInt32LittleEndian(c.AsSpan(OffProofLen, 4), 0);
        return c;
    }

    [TestMethod]
    public void Deploy_InitializesOwnerAndVerifier()
    {
        var (_, hub, owner) = Deploy();
        Assert.AreEqual(owner, hub.Owner);
        Assert.AreEqual(VerifierHash, hub.VerifierRegistry);
    }

    [TestMethod]
    public void Deploy_RejectsZeroVerifierRegistry()
    {
        // Fail closed: without a verifier every batch would finalize unproven.
        var engine = new TestEngine(true);
        engine.Fee = 100_000_000_000L;
        Assert.ThrowsExactly<TestException>(() =>
            engine.Deploy<NeoHubRollupHub>(NeoHubRollupHub.Nef, NeoHubRollupHub.Manifest,
                new object[] { engine.Sender, UInt160.Zero }));
    }

    [TestMethod]
    public void RegisterChain_And_GenesisStateRoot_Succeeds()
    {
        var (_, hub, _) = Deploy();
        var config = BuildChainConfig(ChainId);

        hub.RegisterChain(config);
        Assert.IsTrue(hub.IsChainActive(ChainId));
        Assert.AreEqual((BigInteger)3, hub.GetSecurityLevel(ChainId));

        hub.RegisterGenesisStateRoot(ChainId, GenesisStateRoot);
        Assert.AreEqual(GenesisStateRoot, hub.GetGenesisStateRoot(ChainId));
        Assert.AreEqual(GenesisStateRoot, hub.GetCanonicalStateRoot(ChainId));
    }

    [TestMethod]
    public void Pause_And_Resume_Chain()
    {
        var (_, hub, _) = Deploy();
        var config = BuildChainConfig(ChainId);
        hub.RegisterChain(config);

        Assert.IsTrue(hub.IsChainActive(ChainId));

        hub.PauseChain(ChainId);
        Assert.IsFalse(hub.IsChainActive(ChainId));

        hub.ResumeChain(ChainId);
        Assert.IsTrue(hub.IsChainActive(ChainId));
    }

    [TestMethod]
    public void ForcedInclusion_Enqueue_IncrementsPendingCount()
    {
        var (_, hub, _) = Deploy();
        hub.RegisterChain(BuildChainConfig(ChainId));

        Assert.AreEqual((BigInteger)0, hub.GetPendingForcedCount(ChainId));
        Assert.AreEqual((BigInteger)0, hub.GetNextForcedNonce(ChainId));

        var txHash = new UInt256(R(0x99));
        var nonce = hub.EnqueueForcedTransaction(ChainId, [0x01, 0x02], txHash);

        Assert.AreEqual((BigInteger)0, nonce);
        Assert.AreEqual((BigInteger)1, hub.GetPendingForcedCount(ChainId));
    }

    [TestMethod]
    public void SubmitAndFinalizeBatch_ConsumesForcedInclusionCount()
    {
        var (_, hub, _) = Deploy();
        hub.RegisterChain(BuildChainConfig(ChainId));
        hub.RegisterGenesisStateRoot(ChainId, GenesisStateRoot);

        hub.EnqueueForcedTransaction(ChainId, [0x01], new UInt256(R(0x91)));
        hub.EnqueueForcedTransaction(ChainId, [0x02], new UInt256(R(0x92)));
        Assert.AreEqual((BigInteger)2, hub.GetPendingForcedCount(ChainId));

        var postState = R(0x20);
        var (c, l1msg, blkctx) = BuildCommitment(1, GenesisState, postState, proofType: 3);
        // Count 0 preserves the existing public-input hash domain used by BuildCommitment.
        hub.SubmitAndFinalizeBatch(c, l1msg, blkctx, 0u);
        Assert.AreEqual((BigInteger)2, hub.GetPendingForcedCount(ChainId));

        // Underflow must fail closed when claiming more than pending.
        var (c2, l1msg2, blkctx2) = BuildCommitment(2, postState, R(0x30), proofType: 3);
        Assert.ThrowsExactly<TestException>(() =>
            hub.SubmitAndFinalizeBatch(c2, l1msg2, blkctx2, 3u));
    }

    [TestMethod]
    public void SubmitAndFinalizeBatch_AcceptsTheSharedCanonicalEncodingVector()
    {
        // Pins the compiled contract to the shared golden bytes: BatchSerializer feeds the same
        // preimage to UT_CanonicalEncodingParity, and canonical_encoding_parity.rs feeds it to
        // hash_public_inputs. A contract that rebuilds the preimage in the wrong tail order
        // computes SwappedPublicInputHashHex and rejects this commitment.
        var (engine, hub, _) = Deploy();
        hub.RegisterChain(BuildChainConfig(ChainId, securityLevel: 1)); // Settled accepts Multisig
        hub.RegisterGenesisStateRoot(ChainId, new UInt256(R(0x10)));

        var commitment = BuildSharedVectorCommitment(Hex(CanonicalPublicInputHashHex));
        hub.SubmitAndFinalizeBatch(commitment, R(0xB1), R(0xC2), 0u);

        Assert.AreEqual((BigInteger)3, hub.GetBatchStatus(ChainId, 1));
        Assert.AreEqual(new UInt256(R(0xA1)), hub.GetCanonicalStateRoot(ChainId));
    }

    [TestMethod]
    public void SubmitAndFinalizeBatch_RejectsTheSwappedPublicInputEncoding()
    {
        // The pre-fix contract copied the header straight through (daCommitment at 252) and
        // appended l1MessageHash at 284, so it accepted commitments carrying the swapped digest
        // and rejected canonical ones. Both directions must now fail closed.
        var (_, hub, _) = Deploy();
        hub.RegisterChain(BuildChainConfig(ChainId, securityLevel: 1));
        hub.RegisterGenesisStateRoot(ChainId, new UInt256(R(0x10)));

        var swapped = BuildSharedVectorCommitment(Hex(SwappedPublicInputHashHex));
        Assert.ThrowsExactly<TestException>(
            () => hub.SubmitAndFinalizeBatch(swapped, R(0xB1), R(0xC2), 0u));
    }

    [TestMethod]
    public void SubmitAndFinalizeBatch_RejectsBatchWhenVerifierRejects()
    {
        var (_, hub, _) = Deploy(verifierAccepts: false);
        hub.RegisterChain(BuildChainConfig(ChainId));
        hub.RegisterGenesisStateRoot(ChainId, GenesisStateRoot);

        var (c, l1msg, blkctx) = BuildCommitment(1, GenesisState, R(0x20), proofType: 3);

        Assert.ThrowsExactly<TestException>(() =>
            hub.SubmitAndFinalizeBatch(c, l1msg, blkctx, 0u));
        Assert.AreEqual((BigInteger)0, hub.GetLatestFinalizedBatchNumber(ChainId));
    }

    [TestMethod]
    public void SubmitAndFinalizeBatch_RejectsProofTypeBelowChainSecurityLevel()
    {
        var (_, hub, _) = Deploy();
        hub.RegisterChain(BuildChainConfig(ChainId, securityLevel: 3)); // Validity ⇒ ZK only
        hub.RegisterGenesisStateRoot(ChainId, GenesisStateRoot);

        var (c, l1msg, blkctx) = BuildCommitment(1, GenesisState, R(0x20), proofType: 1); // Multisig

        Assert.ThrowsExactly<TestException>(() =>
            hub.SubmitAndFinalizeBatch(c, l1msg, blkctx, 0u));
    }

    [TestMethod]
    public void SubmitBatch_RejectsOptimisticBatchWithoutChallengeWindow()
    {
        // The optimistic state machine (window, bonds, deadline finalization) is not wired in this
        // contract, so both settlement entries refuse optimistic batches instead of finalizing
        // them unprotected.
        var (_, hub, _) = Deploy();
        hub.RegisterChain(BuildChainConfig(ChainId, securityLevel: 2)); // Optimistic
        hub.RegisterGenesisStateRoot(ChainId, GenesisStateRoot);

        var (c, l1msg, blkctx) = BuildCommitment(1, GenesisState, R(0x20), proofType: 2);

        Assert.ThrowsExactly<TestException>(() => hub.SubmitBatch(c, l1msg, blkctx, 0u));
    }

    [TestMethod]
    public void SubmitAndFinalizeBatch_AtomicExecution_Success()
    {
        var (_, hub, _) = Deploy();
        hub.RegisterChain(BuildChainConfig(ChainId));
        hub.RegisterGenesisStateRoot(ChainId, GenesisStateRoot);

        var postState = R(0x20);
        var postStateRoot = new UInt256(postState);
        var (c, l1msg, blkctx) = BuildCommitment(1, GenesisState, postState, proofType: 3);

        // Submit and finalize atomically in 1 transaction!
        hub.SubmitAndFinalizeBatch(c, l1msg, blkctx, 0u);

        // Check finalized state
        Assert.AreEqual((BigInteger)3, hub.GetBatchStatus(ChainId, 1));
        Assert.AreEqual((BigInteger)1, hub.GetLatestFinalizedBatchNumber(ChainId));
        Assert.AreEqual(postStateRoot, hub.GetCanonicalStateRoot(ChainId));

        // Check DA availability recorded internally
        Assert.IsTrue(hub.IsBatchDAAvailable(ChainId, 1));
        Assert.AreEqual(new UInt256(R(0xDA)), hub.GetBatchDACommitment(ChainId, 1));
    }

    [TestMethod]
    public void SubmitAndFinalizeBatch_RejectsOptimisticBatch()
    {
        var (_, hub, _) = Deploy();
        hub.RegisterChain(BuildChainConfig(ChainId, securityLevel: 2));
        hub.RegisterGenesisStateRoot(ChainId, GenesisStateRoot);

        var (c, l1msg, blkctx) = BuildCommitment(1, GenesisState, R(0x20), proofType: 2); // ProofTypeOptimistic = 2

        Assert.ThrowsExactly<TestException>(() =>
            hub.SubmitAndFinalizeBatch(c, l1msg, blkctx, 0u));
    }

    [TestMethod]
    public void SubmitBatch_TwoStepFlow_Success()
    {
        var (_, hub, _) = Deploy();
        hub.RegisterChain(BuildChainConfig(ChainId));
        hub.RegisterGenesisStateRoot(ChainId, GenesisStateRoot);

        var postState = R(0x20);
        var postStateRoot = new UInt256(postState);
        var (c, l1msg, blkctx) = BuildCommitment(1, GenesisState, postState, proofType: 3);

        // Step 1: Submit batch
        hub.SubmitBatch(c, l1msg, blkctx, 0u);
        Assert.AreEqual((BigInteger)1, hub.GetBatchStatus(ChainId, 1)); // StatusPending
        Assert.AreEqual((BigInteger)0, hub.GetLatestFinalizedBatchNumber(ChainId));

        // Step 2: Finalize batch
        hub.FinalizeBatch(ChainId, 1);
        Assert.AreEqual((BigInteger)3, hub.GetBatchStatus(ChainId, 1)); // StatusFinalized
        Assert.AreEqual((BigInteger)1, hub.GetLatestFinalizedBatchNumber(ChainId));
        Assert.AreEqual(postStateRoot, hub.GetCanonicalStateRoot(ChainId));
    }

    [TestMethod]
    public void PublishGatewayGlobalRoot_HappyPath_AdvancesWatermarkAndStoresRoot()
    {
        var sharedBridgeHash = UInt160.Parse("0x" + new string('7', 40));
        var engine = new TestEngine(true);
        engine.Fee = 100_000_000_000L;
        var owner = engine.Sender;
        engine.FromHash<StubVerifier>(VerifierHash, mock =>
        {
            mock.Setup(v => v.VerifyProof(It.IsAny<byte[]>())).Returns(true);
            mock.Setup(v => v.VerifyZkProof(
                    It.IsAny<BigInteger?>(),
                    It.IsAny<byte[]?>(),
                    It.IsAny<byte[]?>(),
                    It.IsAny<byte[]?>()))
                .Returns(true);
        }, checkExistence: false);
        engine.FromHash<StubSharedBridge>(sharedBridgeHash, mock =>
            mock.Setup(b => b.PublishMessageRoots(
                    It.IsAny<BigInteger?>(),
                    It.IsAny<BigInteger?>(),
                    It.IsAny<UInt256?>(),
                    It.IsAny<UInt256?>())),
            checkExistence: false);

        var hub = engine.Deploy<NeoHubRollupHub>(NeoHubRollupHub.Nef, NeoHubRollupHub.Manifest,
            new object[] { owner, VerifierHash });
        hub.RegisterChain(BuildChainConfig(ChainId));
        hub.RegisterGenesisStateRoot(ChainId, GenesisStateRoot);
        hub.SharedBridge = sharedBridgeHash;

        var (c, l1msg, blkctx) = BuildCommitment(1, GenesisState, R(0x20), proofType: 3);
        hub.SubmitAndFinalizeBatch(c, l1msg, blkctx, 0u);
        Assert.AreEqual((BigInteger)0, hub.GetGatewayFinalizedThrough(ChainId));

        var references = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(references.AsSpan(0, 4), ChainId);
        BinaryPrimitives.WriteUInt64LittleEndian(references.AsSpan(4, 8), 1);
        var commitmentRoot = new UInt256(Hash256(c));
        var globalRoot = new UInt256(R(0xEE));
        var vk = new UInt256(R(0xA1));
        var replay = new UInt256(R(0xD1));

        Assert.IsTrue(hub.PublishGatewayGlobalRoot(
            42,
            references,
            globalRoot,
            commitmentRoot,
            1,
            2,
            1,
            vk,
            replay,
            [0xCA, 0xFE]));

        Assert.AreEqual((BigInteger)1, hub.GetGatewayFinalizedThrough(ChainId));
        Assert.AreEqual(globalRoot, hub.GetGlobalRoot(42));
        Assert.AreNotEqual(UInt256.Zero, hub.GetGlobalRootProofInputHash(42));
    }

    private static byte[] Hex(string value)
    {
        var bytes = new byte[value.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(value.Substring(i * 2, 2), 16);
        return bytes;
    }
}

/// <summary>Stand-in for the deployed verifier: answers <c>verifyProof</c> exactly as the test
/// wires it, so proof-acceptance and proof-rejection paths are exercised independently of any
/// concrete verifier build. Also answers Gateway <c>verifyZkProof</c>.</summary>
public abstract class StubVerifier(SmartContractInitialize initialize) : SmartContract(initialize)
{
    [DisplayName("verifyProof")]
    public abstract bool VerifyProof(byte[]? commitmentBytes);

    [DisplayName("verifyZkProof")]
    public abstract bool VerifyZkProof(
        BigInteger? proofSystem,
        byte[]? verificationKeyId,
        byte[]? publicInputHash,
        byte[]? proofBytes);
}

/// <summary>Minimal SharedBridge stand-in for Gateway <c>publishMessageRoots</c> fan-out.</summary>
public abstract class StubSharedBridge(SmartContractInitialize initialize) : SmartContract(initialize)
{
    [DisplayName("publishMessageRoots")]
    public abstract void PublishMessageRoots(
        BigInteger? chainId,
        BigInteger? batchNumber,
        UInt256? l2ToL1Root,
        UInt256? l2ToL2Root);
}
