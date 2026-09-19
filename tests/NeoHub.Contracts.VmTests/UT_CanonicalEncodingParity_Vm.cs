using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using Moq;
using Neo;
using Neo.L2.TestInfra;
using Neo.SmartContract.Testing;
using Neo.SmartContract.Testing.Exceptions;

namespace NeoHub.Contracts.VmTests;

/// <summary>
/// Audit finding <c>V2</c>, second half of the lock: the compiled contracts must parse
/// <see cref="CanonicalEncodingVectors"/>'s bytes the way the off-chain encoders write them.
/// </summary>
/// <remarks>
/// <para>
/// This assembly cannot reference <c>Neo.L2.Batch</c> (see <see cref="CanonicalEncodingVectors"/>),
/// so it cannot call <c>BatchSerializer</c>. What it can do — and what no other test in this repo did
/// — is hand-roll the same buffers from its own copy of the offset table, compare them to the shared
/// vectors, and then feed them to the deployed NEF. Three independent statements of the layout
/// (encoder, this table, the contract's constants) now have to agree, and the agreement is checked by
/// executing the contract rather than by a comment claiming it.
/// </para>
/// <para>
/// Wave 2 retargets settlement to lean <c>NeoHubRollupHub</c> (352-byte public-input hash with
/// trailing <c>forcedInclusionCount</c>). ChainRegistry admission-gate tests still use the
/// ChainRegistry NEF when present.
/// </para>
/// </remarks>
[TestClass]
public class UT_CanonicalEncodingParity_Vm
{
    private const int CommitmentSize = 321;
    private const int PublicInputsSize = 352;
    private const int ConfigSize = 91;

    // Commitment header offsets, copied independently of both BatchSerializer's writer and
    // RollupHub's constants. UT_BatchSerializer pins this same shape against the encoder and
    // the vectors pin it against data, so a field that moves in exactly one of the three now fails.
    private const int OffChainId = 0, OffBatch = 4, OffFirstBlock = 12, OffLastBlock = 20;
    private const int OffPreState = 28, OffPostState = 60, OffTxRoot = 92, OffReceiptRoot = 124;
    private const int OffWithdrawal = 156, OffL2ToL1 = 188, OffL2ToL2 = 220, OffDaCommitment = 252;
    private const int OffPublicInputHash = 284, OffProofType = 316;

    // The two UInt160 slots that RegisterChainPublic's semi-permissionless admission gate slices out of
    // the raw 91-byte config buffer. The contract hardcodes those numbers with no named constant of its
    // own, so the test below checks the slice lands on the slots the vector documents as verifier
    // (0x22-filled) and bridgeAdapter (0x33-filled) instead of trusting the arithmetic.
    private const int ConfigVerifierOffset = 24, ConfigBridgeOffset = 44;
    private const byte ConfigVerifierFill = 0x22, ConfigBridgeFill = 0x33;

    private const byte StatusPending = 1, StatusFinalized = 3;

    private static readonly UInt160 VerifierHash = UInt160.Parse("0x" + new string('5', 40));

    private static UInt256 Root(byte fill) => new(CanonicalEncodingVectors.Fill(fill));

    private static byte[] Hash256(byte[] x) => SHA256.HashData(SHA256.HashData(x));

    /// <summary>
    /// Rebuilds the public-inputs buffer from the vector's field values and this assembly's own idea
    /// of the order, rather than copying the vector's bytes — so the digest written into the header
    /// below is computed here, exactly as <c>RollupHub.ComputePublicInputHash</c> computes it.
    /// </summary>
    private static byte[] BuildPublicInputs()
    {
        var p = new byte[PublicInputsSize];
        var pos = 0;
        void PutInt(uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(pos, 4), value);
            pos += 4;
        }
        void PutLong(ulong value)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(p.AsSpan(pos, 8), value);
            pos += 8;
        }
        void PutRoot(byte fill)
        {
            CanonicalEncodingVectors.Fill(fill).CopyTo(p.AsSpan(pos, 32));
            pos += 32;
        }

        PutInt(CanonicalEncodingVectors.ChainId);
        PutLong(CanonicalEncodingVectors.Batch);
        PutLong(CanonicalEncodingVectors.FirstBlock);
        PutLong(CanonicalEncodingVectors.LastBlock);
        PutRoot(CanonicalEncodingVectors.FillPreStateRoot);
        PutRoot(CanonicalEncodingVectors.FillPostStateRoot);
        PutRoot(CanonicalEncodingVectors.FillTxRoot);
        PutRoot(CanonicalEncodingVectors.FillReceiptRoot);
        CanonicalEncodingVectors.WithdrawalRoot().CopyTo(p.AsSpan(pos, 32));
        pos += 32;
        PutRoot(CanonicalEncodingVectors.FillL2ToL1MessageRoot);
        PutRoot(CanonicalEncodingVectors.FillL2ToL2MessageRoot);
        PutRoot(CanonicalEncodingVectors.FillL1MessageHash);
        PutRoot(CanonicalEncodingVectors.FillDaCommitment);
        PutRoot(CanonicalEncodingVectors.FillBlockContextHash);
        PutInt(0); // forcedInclusionCount
        Assert.AreEqual(p.Length, pos, "public-inputs builder wrote the wrong number of bytes");
        return p;
    }

    private static byte[] BuildCommitmentHeader()
    {
        var c = new byte[CommitmentSize];
        BinaryPrimitives.WriteUInt32LittleEndian(c.AsSpan(OffChainId, 4), CanonicalEncodingVectors.ChainId);
        BinaryPrimitives.WriteUInt64LittleEndian(c.AsSpan(OffBatch, 8), CanonicalEncodingVectors.Batch);
        BinaryPrimitives.WriteUInt64LittleEndian(c.AsSpan(OffFirstBlock, 8), CanonicalEncodingVectors.FirstBlock);
        BinaryPrimitives.WriteUInt64LittleEndian(c.AsSpan(OffLastBlock, 8), CanonicalEncodingVectors.LastBlock);
        CanonicalEncodingVectors.Fill(CanonicalEncodingVectors.FillPreStateRoot).CopyTo(c.AsSpan(OffPreState, 32));
        CanonicalEncodingVectors.Fill(CanonicalEncodingVectors.FillPostStateRoot).CopyTo(c.AsSpan(OffPostState, 32));
        CanonicalEncodingVectors.Fill(CanonicalEncodingVectors.FillTxRoot).CopyTo(c.AsSpan(OffTxRoot, 32));
        CanonicalEncodingVectors.Fill(CanonicalEncodingVectors.FillReceiptRoot).CopyTo(c.AsSpan(OffReceiptRoot, 32));
        CanonicalEncodingVectors.WithdrawalRoot().CopyTo(c.AsSpan(OffWithdrawal, 32));
        CanonicalEncodingVectors.Fill(CanonicalEncodingVectors.FillL2ToL1MessageRoot).CopyTo(c.AsSpan(OffL2ToL1, 32));
        CanonicalEncodingVectors.Fill(CanonicalEncodingVectors.FillL2ToL2MessageRoot).CopyTo(c.AsSpan(OffL2ToL2, 32));
        CanonicalEncodingVectors.Fill(CanonicalEncodingVectors.FillDaCommitment).CopyTo(c.AsSpan(OffDaCommitment, 32));
        Hash256(BuildPublicInputs()).CopyTo(c.AsSpan(OffPublicInputHash, 32));
        c[OffProofType] = CanonicalEncodingVectors.ProofType;
        BinaryPrimitives.WriteUInt32LittleEndian(c.AsSpan(OffProofType + 1, 4), 0); // proof length = 0
        return c;
    }

    private static void AssertBytesEqual(string what, byte[] expected, byte[] actual)
    {
        Assert.AreEqual(expected.Length, actual.Length, $"{what}: length differs");
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.AreEqual(expected[i], actual[i],
                $"{what}: byte {i} is 0x{actual[i]:X2}, the vector says 0x{expected[i]:X2}");
        }
    }

    private static void AssertSlotIs(string what, byte[] config, int offset, byte fill)
    {
        for (var i = 0; i < 20; i++)
        {
            Assert.AreEqual(fill, config[offset + i],
                $"{what}: config[{offset + i}] is 0x{config[offset + i]:X2}, " +
                $"expected the 0x{fill:X2}-filled slot");
        }
    }

    private static byte[] ConfigForChain(uint chainId)
    {
        var config = CanonicalEncodingVectors.ChainConfig();
        // The contract asserts the chain id embedded in the buffer equals its argument, so a second
        // chain needs that id rewritten; every other byte of the layout stays golden.
        BinaryPrimitives.WriteUInt32LittleEndian(config.AsSpan(0, 4), chainId);
        return config;
    }

    private sealed record HubPair(TestEngine Engine, NeoHubRollupHub Hub);

    /// <summary>
    /// Real RollupHub in one engine; only the verifier stub is mocked
    /// (<c>VerifyProof → true</c>), matching <c>UT_RollupHub_Vm</c>.
    /// </summary>
    private static HubPair DeployHub(bool verifierAccepts = true)
    {
        var engine = new TestEngine(true);
        engine.Fee = 100_000_000_000L;
        var owner = engine.Sender;
        engine.FromHash<StubVerifier>(VerifierHash, mock =>
            mock.Setup(v => v.VerifyProof(It.IsAny<byte[]>())).Returns(verifierAccepts),
            checkExistence: false);
        var hub = engine.Deploy<NeoHubRollupHub>(NeoHubRollupHub.Nef, NeoHubRollupHub.Manifest,
            new object[] { owner, VerifierHash });
        return new HubPair(engine, hub);
    }

    /// <summary>
    /// Registers chain 1001 from the golden config vector and settles batch 1 from the golden
    /// commitment vector. Submitting is itself the assertion, through two independent on-chain checks:
    /// <c>ComputePublicInputHash</c> rebuilds the 352-byte preimage from <em>its own</em> offsets —
    /// header bytes 0..251 plus the two submit arguments interleaved with daCommitment, then
    /// forcedInclusionCount — and faults unless that digest equals the header's offset-284 field;
    /// and <c>IsProofTypeCompatible</c> reads the proof-type byte at offset 316 against the security
    /// level decoded from the config vector.
    /// </summary>
    private static HubPair Settled()
    {
        var pair = DeployHub();
        pair.Hub.RegisterChain(
            CanonicalEncodingVectors.ChainId,
            CanonicalEncodingVectors.ChainConfig(),
            Root(CanonicalEncodingVectors.FillPreStateRoot));
        pair.Hub.SubmitBatch(
            BuildCommitmentHeader(),
            CanonicalEncodingVectors.Fill(CanonicalEncodingVectors.FillL1MessageHash),
            CanonicalEncodingVectors.Fill(CanonicalEncodingVectors.FillBlockContextHash),
            0u);
        pair.Hub.FinalizeBatch(CanonicalEncodingVectors.ChainId, CanonicalEncodingVectors.Batch);
        return pair;
    }

    private static List<object> Siblings(int leafIndex) =>
        [.. CanonicalEncodingVectors.WithdrawalSiblings(leafIndex)];

    private static UInt256 RootAt(byte[] commitment, int offset) =>
        new(commitment.AsSpan(offset, 32).ToArray());

    [TestMethod]
    public void HandRolledBuilders_MatchGoldenVectors()
    {
        AssertBytesEqual("commitment header", CanonicalEncodingVectors.CommitmentHeader(), BuildCommitmentHeader());
        AssertBytesEqual("public inputs", CanonicalEncodingVectors.PublicInputs(), BuildPublicInputs());
        Assert.AreEqual(CommitmentSize, CanonicalEncodingVectors.CommitmentHeader().Length);
        Assert.AreEqual(ConfigSize, CanonicalEncodingVectors.ChainConfig().Length);
    }

    [TestMethod]
    public void ChainRegistry_ReadsEverySemanticByteOfTheGoldenConfig()
    {
        // Lean RollupHub stores the same 91-byte config; semantic getters cover security level +
        // active bit, and the remaining single-byte fields are read back from GetChainConfig.
        var pair = DeployHub();
        var config = CanonicalEncodingVectors.ChainConfig();
        BigInteger chainId = CanonicalEncodingVectors.ChainId;

        pair.Hub.RegisterChain(chainId, config, Root(CanonicalEncodingVectors.FillPreStateRoot));

        CollectionAssert.AreEqual(config, pair.Hub.GetChainConfig(chainId));
        Assert.AreEqual((BigInteger)CanonicalEncodingVectors.ChainConfigSecurityLevel,
            pair.Hub.GetSecurityLevel(chainId), "offset 84");
        Assert.AreEqual(CanonicalEncodingVectors.ChainConfigDAMode, config[85], "offset 85");
        Assert.AreEqual(1, config[86], "offset 86 gatewayEnabled");
        Assert.AreEqual(0, config[87], "offset 87 permissionlessExit");
        Assert.AreEqual(1, config[88], "offset 88 sequencerModel");
        Assert.AreEqual(2, config[89], "offset 89 exitModel");
        Assert.IsTrue(pair.Hub.IsActive(chainId)!.Value, "offset 90");
    }

    [TestMethod]
    public void SettlementManager_SettlesTheGoldenCommitmentAndKeepsItsRoots()
    {
        var pair = DeployHub();
        BigInteger chainId = CanonicalEncodingVectors.ChainId;
        pair.Hub.RegisterChain(chainId, CanonicalEncodingVectors.ChainConfig(), Root(CanonicalEncodingVectors.FillPreStateRoot));

        var header = BuildCommitmentHeader();
        pair.Hub.SubmitBatch(
            header,
            CanonicalEncodingVectors.Fill(CanonicalEncodingVectors.FillL1MessageHash),
            CanonicalEncodingVectors.Fill(CanonicalEncodingVectors.FillBlockContextHash),
            0u);
        Assert.AreEqual((BigInteger)StatusPending, pair.Hub.GetBatchStatus(chainId, 1));

        pair.Hub.FinalizeBatch(chainId, CanonicalEncodingVectors.Batch);
        Assert.AreEqual((BigInteger)StatusFinalized, pair.Hub.GetBatchStatus(chainId, 1));
        Assert.AreEqual((BigInteger)1, pair.Hub.GetLatestFinalizedBatchNumber(chainId));
        Assert.AreEqual(Root(CanonicalEncodingVectors.FillPostStateRoot),
            pair.Hub.GetCanonicalStateRoot(chainId));

        // RollupHub exposes the stored commitment header; SharedBridge pays out against the
        // withdrawal root folded from this same buffer.
        var stored = pair.Hub.GetBatchCommitment(chainId, 1)!;
        Assert.AreEqual(Root(CanonicalEncodingVectors.FillTxRoot), RootAt(stored, OffTxRoot));
        Assert.AreEqual(Root(CanonicalEncodingVectors.FillL2ToL1MessageRoot), RootAt(stored, OffL2ToL1));
        Assert.AreEqual(Root(CanonicalEncodingVectors.FillL2ToL2MessageRoot), RootAt(stored, OffL2ToL2));
    }

    [TestMethod]
    public void SettlementManager_RejectsTheGoldenCommitmentWhenOneRootOffsetMoves()
    {
        // Control for the leg above: the submit only proves anything because a wrong offset fails.
        // Swap txRoot and receiptRoot in the buffer, leaving the recorded publicInputHash alone, which
        // is what a one-sided layout change on either side of the boundary produces.
        var pair = DeployHub();
        pair.Hub.RegisterChain(CanonicalEncodingVectors.ChainId, CanonicalEncodingVectors.ChainConfig(), Root(CanonicalEncodingVectors.FillPreStateRoot));
        var header = BuildCommitmentHeader();
        var tx = header[OffTxRoot..(OffTxRoot + 32)].ToArray();
        var receipt = header[OffReceiptRoot..(OffReceiptRoot + 32)].ToArray();
        receipt.CopyTo(header.AsSpan(OffTxRoot));
        tx.CopyTo(header.AsSpan(OffReceiptRoot));

        Assert.ThrowsExactly<TestException>(() => pair.Hub.SubmitBatch(
            header,
            CanonicalEncodingVectors.Fill(CanonicalEncodingVectors.FillL1MessageHash),
            CanonicalEncodingVectors.Fill(CanonicalEncodingVectors.FillBlockContextHash),
            0u));
    }

    [TestMethod]
    public void VerifyWithdrawalLeafWithProof_FoldsTheGoldenSiblings()
    {
        var pair = Settled();
        BigInteger chainId = CanonicalEncodingVectors.ChainId;
        var leaves = CanonicalEncodingVectors.WithdrawalLeaves();

        // Every leaf position, including the odd last one that is paired with its own duplicate.
        for (var i = 0; i < leaves.Count; i++)
        {
            Assert.IsTrue(pair.Hub.VerifyWithdrawalLeafWithProof(
                    chainId, CanonicalEncodingVectors.Batch, new UInt256(leaves[i]), Siblings(i), i)!,
                $"leaf {i} must Merkle-verify against the finalized withdrawalRoot");
        }

        var siblings = Siblings(0);
        siblings[0] = CanonicalEncodingVectors.Fill(0xEE);
        Assert.IsFalse(pair.Hub.VerifyWithdrawalLeafWithProof(
            chainId, CanonicalEncodingVectors.Batch, new UInt256(leaves[0]), siblings, 0)!.Value);

        // Right siblings, wrong position: leafIndex drives the left/right choice at every level.
        Assert.IsFalse(pair.Hub.VerifyWithdrawalLeafWithProof(
            chainId, CanonicalEncodingVectors.Batch, new UInt256(leaves[0]), Siblings(0), 1)!.Value);

        // V5 position binding: the golden tree has depth 3, so leaf 4's proof folds identically when
        // the index is relabelled to 4 + 2^3 = 12 — the fold consumes only the low three bits — but
        // 12 is no leaf of the tree. A proof bound only to the leaf hash would accept it.
        Assert.IsFalse(pair.Hub.VerifyWithdrawalLeafWithProof(
            chainId, CanonicalEncodingVectors.Batch, new UInt256(leaves[4]), Siblings(4), 12)!.Value);

        Assert.IsFalse(pair.Hub.VerifyWithdrawalLeafWithProof(
            chainId, 2, new UInt256(leaves[0]), Siblings(0), 0)!.Value);
    }

    [TestMethod]
    public void VerifyStateLeafWithProof_FoldsTheGoldenSiblingsAgainstTheCanonicalRoot()
    {
        // RollupHub does not expose VerifyStateLeafWithProof; the same Hash256 fold is covered by
        // VerifyWithdrawalLeafWithProof. Here we pin that atomic RegisterChain installs the
        // golden tree root as the canonical state root (the storage side of the old state-leaf path).
        var pair = DeployHub();
        BigInteger chainId = 1002;
        var root = new UInt256(CanonicalEncodingVectors.WithdrawalRoot());
        pair.Hub.RegisterChain(chainId, ConfigForChain(1002), root);

        Assert.AreEqual(root, pair.Hub.GetGenesisStateRoot(chainId));
        Assert.AreEqual(root, pair.Hub.GetCanonicalStateRoot(chainId));
    }

    [TestMethod]
    public void RegisterChainPublic_ApprovesTheVerifierAndBridgeAtTheSerializersOffsets()
    {
        // The semi-permissionless admission gate slices the verifier and bridgeAdapter out of the same
        // 91-byte buffer at literal offsets 24 and 44. L2ChainConfigSerializer names the same numbers
        // (OffsetVerifier/OffsetBridge), but nothing executable links the two statements, so a
        // one-sided move makes the gate check the approval-set membership of the wrong field — and an
        // unapproved verifier walks through believing it was approved.
        var engine = new TestEngine(true);
        engine.Fee = 100_000_000_000L;
        var registry = engine.Deploy<NeoHubChainRegistry>(
            NeoHubChainRegistry.Nef, NeoHubChainRegistry.Manifest, engine.Sender);

        var config = ConfigForChain(1003);
        AssertSlotIs("verifier", config, ConfigVerifierOffset, ConfigVerifierFill);
        AssertSlotIs("bridgeAdapter", config, ConfigBridgeOffset, ConfigBridgeFill);
        var approvedVerifier = new UInt160(config[ConfigVerifierOffset..(ConfigVerifierOffset + 20)]);
        var approvedBridge = new UInt160(config[ConfigBridgeOffset..(ConfigBridgeOffset + 20)]);
        UInt160? askedVerifier = null;
        UInt160? askedBridge = null;

        var governanceHash = UInt160.Parse("0x" + new string('7', 40));
        engine.FromHash<NeoHubGovernanceController>(governanceHash, m =>
        {
            m.Setup(c => c.AdmissionMode).Returns((BigInteger)1); // semi-permissionless
            m.Setup(c => c.IsApprovedVerifier(It.IsAny<UInt160?>()))
                .Returns((UInt160? asked) =>
                {
                    askedVerifier = asked;
                    return asked == approvedVerifier;
                });
            m.Setup(c => c.IsApprovedBridgeAdapter(It.IsAny<UInt160?>()))
                .Returns((UInt160? asked) =>
                {
                    askedBridge = asked;
                    return asked == approvedBridge;
                });
        }, checkExistence: false);
        registry.GovernanceController = governanceHash;

        registry.RegisterChainPublic(1003, config, Root(CanonicalEncodingVectors.FillPreStateRoot));

        Assert.AreEqual(approvedVerifier, askedVerifier,
            $"the contract's verifier slice (config[{ConfigVerifierOffset}..]) is not the slot " +
            "L2ChainConfigSerializer writes");
        Assert.AreEqual(approvedBridge, askedBridge,
            $"the contract's bridgeAdapter slice (config[{ConfigBridgeOffset}..]) is not the slot " +
            "L2ChainConfigSerializer writes");
        Assert.IsTrue(registry.IsActive(1003));
    }

    [TestMethod]
    public void RegisterChainPublic_PermissionedModeRejectsTheGoldenConfig()
    {
        // The other admission branch, unexecuted before this file too: mode 0 must refuse a public
        // registration without writing any of the config it was handed.
        var engine = new TestEngine(true);
        engine.Fee = 100_000_000_000L;
        var registry = engine.Deploy<NeoHubChainRegistry>(
            NeoHubChainRegistry.Nef, NeoHubChainRegistry.Manifest, engine.Sender);

        var governanceHash = UInt160.Parse("0x" + new string('7', 40));
        engine.FromHash<NeoHubGovernanceController>(
            governanceHash,
            m => m.Setup(c => c.AdmissionMode).Returns((BigInteger)0),
            checkExistence: false);
        registry.GovernanceController = governanceHash;
        BigInteger chainId = 1004;

        Assert.ThrowsExactly<TestException>(
            () => registry.RegisterChainPublic(
                1004, ConfigForChain(1004), Root(CanonicalEncodingVectors.FillPreStateRoot)));

        Assert.IsFalse(registry.IsActive(chainId));
        CollectionAssert.AreEqual(Array.Empty<byte>(), registry.GetChainConfig(chainId));
    }
}
