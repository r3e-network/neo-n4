using System.Buffers.Binary;
using System.ComponentModel;
using System.Numerics;
using System.Security.Cryptography;
using Moq;
using Neo;
using Neo.L2.TestInfra;
using Neo.SmartContract.Testing;
using Neo.SmartContract.Testing.Exceptions;

namespace NeoHub.Contracts.VmTests;

/// <summary>Minimal NEP-17 surface so the bridge's asset transfers can be mocked.</summary>
public abstract class MockNep17(SmartContractInitialize initialize) : SmartContract(initialize)
{
    [DisplayName("transfer")]
    public abstract bool? Transfer(UInt160? from, UInt160? to, BigInteger? amount, object? data);
}

/// <summary>
/// VM-level tests for NeoHub.SharedBridge — the canonical asset escrow. The bridge is deployed
/// against a <em>real</em> SettlementManager (only the proof and token back ends are mocked:
/// VerifierRegistry, DARegistry, DAValidator, TokenRegistry, the NEP-17 asset), so the withdrawal
/// path executes the deployed Merkle fold on hand-rolled real proofs instead of a
/// <c>VerifyWithdrawalLeafWithProof → true</c> stub. These tests pin two guarantees:
/// the C1 per-chain escrow accounting — a chain's withdrawals can never exceed its own deposits —
/// and the V5 position binding: a valid proof presented at a relabelled leaf index must not pay out.
/// </summary>
[TestClass]
public class UT_SharedBridge_Vm
{
    private static readonly UInt160 AssetHash = UInt160.Parse("0x" + new string('a', 40));
    private static readonly UInt160 L2Asset = UInt160.Parse("0x" + new string('b', 40));
    private static readonly UInt160 TrHash = UInt160.Parse("0x" + new string('6', 40));
    private const uint ChainA = 1001;
    private const uint ChainB = 2002;

    // Commitment header offsets — the same layout UT_CanonicalEncodingParity_Vm pins against the
    // golden vectors; rebuilt here so these tests can settle their own per-chain batches.
    private const int OffChainId = 0, OffBatch = 4, OffFirstBlock = 12, OffLastBlock = 20;
    private const int OffPreState = 28, OffPostState = 60, OffTxRoot = 92, OffReceiptRoot = 124;
    private const int OffWithdrawal = 156, OffL2ToL1 = 188, OffL2ToL2 = 220, OffDaCommitment = 252;
    private const int OffPublicInputHash = 284, OffProofType = 316;
    private const int CommitmentSize = 321;

    private static byte[] Hash256(byte[] x) => SHA256.HashData(SHA256.HashData(x));

    /// <summary>Mirror SharedBridge.ComputeWithdrawalLeafHash: chainId(4 LE) ‖ emittingContract ‖
    /// l2Sender ‖ l1Recipient ‖ l2Asset ‖ amountLen(4 LE) ‖ amount(minimal unsigned LE) ‖ nonce(8 LE),
    /// then double-SHA256.</summary>
    private static byte[] LeafBytes(uint chainId, UInt160 emitting, UInt160 l2Sender, UInt160 recipient,
        UInt160 l2Asset, BigInteger amount, ulong nonce)
    {
        var amt = amount.ToByteArray(isUnsigned: true, isBigEndian: false);
        var buf = new byte[4 + 20 + 20 + 20 + 20 + 4 + amt.Length + 8];
        var pos = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(pos, 4), chainId); pos += 4;
        emitting.GetSpan().CopyTo(buf.AsSpan(pos, 20)); pos += 20;
        l2Sender.GetSpan().CopyTo(buf.AsSpan(pos, 20)); pos += 20;
        recipient.GetSpan().CopyTo(buf.AsSpan(pos, 20)); pos += 20;
        l2Asset.GetSpan().CopyTo(buf.AsSpan(pos, 20)); pos += 20;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(pos, 4), (uint)amt.Length); pos += 4;
        amt.CopyTo(buf.AsSpan(pos, amt.Length)); pos += amt.Length;
        BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(pos, 8), nonce); pos += 8;
        return Hash256(buf);
    }

    private sealed record BridgeStack(
        TestEngine Engine,
        NeoHubSharedBridge Bridge,
        NeoHubSettlementManager Settlement,
        NeoHubChainRegistry Registry);

    /// <summary>
    /// Real ChainRegistry + real SettlementManager + real SharedBridge in one engine; only the
    /// proof and token back ends are mocks.
    /// </summary>
    private static BridgeStack Deploy()
    {
        var engine = new TestEngine(true);
        engine.Fee = 100_000_000_000L;
        var owner = engine.Sender;
        var registry = engine.Deploy<NeoHubChainRegistry>(
            NeoHubChainRegistry.Nef, NeoHubChainRegistry.Manifest, owner);

        var vrHash = UInt160.Parse("0x" + new string('2', 40));
        var drHash = UInt160.Parse("0x" + new string('3', 40));
        var dvHash = UInt160.Parse("0x" + new string('4', 40));

        engine.FromHash<NeoHubVerifierRegistry>(vrHash,
            m => m.Setup(c => c.VerifyCommitment(It.IsAny<byte[]?>())).Returns(true), checkExistence: false);
        UInt256? recordedDaCommitment = null;
        byte? recordedDaMode = null;
        engine.FromHash<NeoHubDARegistry>(drHash, m =>
        {
            m.Setup(c => c.Record(
                    It.IsAny<BigInteger?>(), It.IsAny<BigInteger?>(),
                    It.IsAny<UInt256?>(), It.IsAny<BigInteger?>()))
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
                    It.IsAny<BigInteger?>(), It.IsAny<BigInteger?>(),
                    It.IsAny<UInt256?>(), It.IsAny<BigInteger?>()))
                .Returns(true),
            checkExistence: false);

        var settlement = engine.Deploy<NeoHubSettlementManager>(
            NeoHubSettlementManager.Nef, NeoHubSettlementManager.Manifest,
            new object[] { owner, registry.Hash, vrHash });
        settlement.DARegistry = drHash;
        settlement.DAValidator = dvHash;

        engine.FromHash<NeoHubTokenRegistry>(TrHash, m =>
        {
            m.Setup(c => c.GetL2Asset(It.IsAny<UInt160?>(), It.IsAny<BigInteger?>())).Returns(L2Asset);
            m.Setup(c => c.IsActive(It.IsAny<UInt160?>(), It.IsAny<BigInteger?>())).Returns(true);
        }, checkExistence: false);
        engine.FromHash<MockNep17>(AssetHash, m =>
            m.Setup(c => c.Transfer(It.IsAny<UInt160?>(), It.IsAny<UInt160?>(), It.IsAny<BigInteger?>(), It.IsAny<object?>())).Returns(true),
            checkExistence: false);

        var bridge = engine.Deploy<NeoHubSharedBridge>(
            NeoHubSharedBridge.Nef, NeoHubSharedBridge.Manifest,
            new object[] { owner, settlement.Hash, TrHash });
        return new BridgeStack(engine, bridge, settlement, registry);
    }

    private static byte[] ConfigForChain(uint chainId)
    {
        var config = CanonicalEncodingVectors.ChainConfig();
        // The contract asserts the chain id embedded in the buffer equals its argument, so a second
        // chain needs that id rewritten; every other byte of the layout stays golden.
        BinaryPrimitives.WriteUInt32LittleEndian(config.AsSpan(0, 4), chainId);
        return config;
    }

    /// <summary>
    /// Build the 321-byte commitment header for batch 1 of <paramref name="chainId"/> whose
    /// withdrawalRoot is <paramref name="withdrawalRoot"/>, recomputing publicInputHash over the
    /// 348-byte preimage exactly as <c>ComputePublicInputHash</c> reads it: header bytes 0..27, the
    /// seven roots the header carries, then the two SubmitBatch arguments and the header's
    /// daCommitment.
    /// </summary>
    private static byte[] BuildCommitmentHeader(uint chainId, byte[] withdrawalRoot)
    {
        var c = new byte[CommitmentSize];
        BinaryPrimitives.WriteUInt32LittleEndian(c.AsSpan(OffChainId, 4), chainId);
        BinaryPrimitives.WriteUInt64LittleEndian(c.AsSpan(OffBatch, 8), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(c.AsSpan(OffFirstBlock, 8), 2);
        BinaryPrimitives.WriteUInt64LittleEndian(c.AsSpan(OffLastBlock, 8), 3);
        CanonicalEncodingVectors.Fill(0x10).CopyTo(c.AsSpan(OffPreState, 32));
        CanonicalEncodingVectors.Fill(0xA1).CopyTo(c.AsSpan(OffPostState, 32));
        CanonicalEncodingVectors.Fill(0x03).CopyTo(c.AsSpan(OffTxRoot, 32));
        CanonicalEncodingVectors.Fill(0x04).CopyTo(c.AsSpan(OffReceiptRoot, 32));
        withdrawalRoot.CopyTo(c.AsSpan(OffWithdrawal, 32));
        CanonicalEncodingVectors.Fill(0x06).CopyTo(c.AsSpan(OffL2ToL1, 32));
        CanonicalEncodingVectors.Fill(0x07).CopyTo(c.AsSpan(OffL2ToL2, 32));
        CanonicalEncodingVectors.Fill(0x09).CopyTo(c.AsSpan(OffDaCommitment, 32));

        var p = new byte[348];
        c.AsSpan(0, 28).CopyTo(p);
        var pos = 28;
        int[] rootOffsets = [OffPreState, OffPostState, OffTxRoot, OffReceiptRoot, OffWithdrawal, OffL2ToL1, OffL2ToL2];
        foreach (var off in rootOffsets)
        {
            c.AsSpan(off, 32).CopyTo(p.AsSpan(pos, 32));
            pos += 32;
        }
        CanonicalEncodingVectors.Fill(0xB1).CopyTo(p.AsSpan(pos, 32)); pos += 32; // l1MessageHash
        c.AsSpan(OffDaCommitment, 32).CopyTo(p.AsSpan(pos, 32)); pos += 32;
        CanonicalEncodingVectors.Fill(0xC2).CopyTo(p.AsSpan(pos, 32)); pos += 32; // blockContextHash
        Assert.AreEqual(348, pos, "public-inputs builder wrote the wrong number of bytes");
        Hash256(p).CopyTo(c.AsSpan(OffPublicInputHash, 32));

        c[OffProofType] = CanonicalEncodingVectors.ProofType;
        BinaryPrimitives.WriteUInt32LittleEndian(c.AsSpan(OffProofType + 1, 4), 0); // proof length = 0
        return c;
    }

    private static byte[][] SortLeaves(byte[][] leaves)
    {
        var sorted = (byte[][])leaves.Clone();
        Array.Sort(sorted, static (a, b) =>
        {
            for (var i = 0; i < 32; i++)
                if (a[i] != b[i]) return a[i].CompareTo(b[i]);
            return 0;
        });
        return sorted;
    }

    /// <summary>
    /// Merkle root over the 32-byte leaf hashes, <c>Hash256(left ‖ right)</c> with the trailing odd
    /// leaf paired with itself at every level — the convention the on-chain fold encodes.
    /// </summary>
    private static byte[] TreeRoot(byte[][] leaves)
    {
        var level = new List<byte[]>(SortLeaves(leaves));
        while (level.Count > 1)
        {
            if (level.Count % 2 == 1) level.Add(level[^1]);
            var next = new List<byte[]>();
            for (var i = 0; i < level.Count; i += 2)
                next.Add(Hash256(level[i].Concat(level[i + 1]).ToArray()));
            level = next;
        }
        return level[0];
    }

    /// <summary>Sibling hashes for <paramref name="leafIndex"/>, leaf level first, mirroring the
    /// on-chain fold's index-bit left/right convention.</summary>
    private static List<object> ProofSiblings(byte[][] leaves, int leafIndex)
    {
        var level = new List<byte[]>(SortLeaves(leaves));
        var idx = leafIndex;
        var siblings = new List<object>();
        while (level.Count > 1)
        {
            if (level.Count % 2 == 1) level.Add(level[^1]);
            siblings.Add(level[idx ^ 1]);
            var next = new List<byte[]>();
            for (var i = 0; i < level.Count; i += 2)
                next.Add(Hash256(level[i].Concat(level[i + 1]).ToArray()));
            level = next;
            idx >>= 1;
        }
        return siblings;
    }

    private static int IndexOf(byte[][] sorted, byte[] leaf)
    {
        for (var i = 0; i < sorted.Length; i++)
            if (sorted[i].AsSpan().SequenceEqual(leaf)) return i;
        throw new InvalidOperationException("leaf not found in sorted order");
    }

    /// <summary>Register <paramref name="chainId"/>, settle batch 1 carrying the tree of
    /// <paramref name="leaves"/> as its withdrawalRoot, and finalize it.</summary>
    private static void SettleWithdrawalBatch(BridgeStack stack, uint chainId, byte[][] leaves)
    {
        stack.Registry.RegisterChain(
            chainId, ConfigForChain(chainId), new UInt256(CanonicalEncodingVectors.Fill(0x10)));
        stack.Settlement.SubmitBatch(
            BuildCommitmentHeader(chainId, TreeRoot(leaves)),
            CanonicalEncodingVectors.Fill(0xB1),
            CanonicalEncodingVectors.Fill(0xC2));
        stack.Settlement.FinalizeBatch(chainId, 1);
    }

    [TestMethod]
    public void Deposit_CreditsPerChainEscrowLedger()
    {
        var stack = Deploy();
        var sb = stack.Bridge;
        var recipient = UInt160.Parse("0x" + new string('c', 40));

        Assert.AreEqual((BigInteger)0, sb.GetLockedBalance(ChainA, AssetHash));
        sb.Deposit(AssetHash, 1000, ChainA, recipient);
        Assert.AreEqual((BigInteger)1000, sb.GetLockedBalance(ChainA, AssetHash), "deposit must credit chain A's escrow");
        sb.Deposit(AssetHash, 500, ChainA, recipient);
        Assert.AreEqual((BigInteger)1500, sb.GetLockedBalance(ChainA, AssetHash), "second deposit accumulates");
        // Chain B got nothing.
        Assert.AreEqual((BigInteger)0, sb.GetLockedBalance(ChainB, AssetHash));
    }

    [TestMethod]
    public void Withdrawal_CannotDrainAnotherChainsEscrow_C1Isolation()
    {
        var stack = Deploy();
        var sb = stack.Bridge;
        var emitting = UInt160.Parse("0x" + new string('d', 40));
        var l2Sender = UInt160.Parse("0x" + new string('e', 40));
        var recipient = UInt160.Parse("0x" + new string('c', 40));

        // Fund ONLY chain A.
        sb.Deposit(AssetHash, 1000, ChainA, recipient);

        // Chain A settles batch 1 whose withdrawal tree is exactly the 600 withdrawal. Single-leaf
        // tree: the leaf is itself the root and the empty sibling list is the whole proof.
        var leafABytes = LeafBytes(ChainA, emitting, l2Sender, recipient, L2Asset, 600, nonce: 1);
        var leafA = new UInt256(leafABytes);
        SettleWithdrawalBatch(stack, ChainA, [leafABytes]);
        Assert.IsTrue(stack.Settlement.VerifyWithdrawalLeafWithProof(ChainA, 1, leafA, new List<object>(), 0)!,
            "control: leaf A must verify against its own settled batch");

        // A legitimate withdrawal from chain A succeeds and debits A's escrow.
        sb.FinalizeWithdrawalWithProof(ChainA, 1, leafA, new List<object>(), 0,
            emitting, l2Sender, L2Asset, 1, AssetHash, recipient, 600);
        Assert.AreEqual((BigInteger)400, sb.GetLockedBalance(ChainA, AssetHash), "withdrawal debits chain A's escrow");

        // Chain B settles its own batch containing the 500 withdrawal but deposited nothing — the
        // committed tree is operator-chosen data, not a statement about L1 escrow.
        var leafBBytes = LeafBytes(ChainB, emitting, l2Sender, recipient, L2Asset, 500, nonce: 1);
        var leafB = new UInt256(leafBBytes);
        SettleWithdrawalBatch(stack, ChainB, [leafBBytes]);
        Assert.IsTrue(stack.Settlement.VerifyWithdrawalLeafWithProof(ChainB, 1, leafB, new List<object>(), 0)!,
            "leaf B's proof verifies — the failure below is the escrow cap, not the proof");

        // A withdrawal for chain B (which has zero escrow) MUST fail at the per-chain cap — it cannot
        // draw from chain A's deposits. This is the core C1 isolation guarantee.
        Assert.ThrowsExactly<TestException>(() =>
            sb.FinalizeWithdrawalWithProof(ChainB, 1, leafB, new List<object>(), 0,
                emitting, l2Sender, L2Asset, 1, AssetHash, recipient, 500),
            "chain B has no escrow — must not be able to drain chain A's funds");
        // Chain A's balance is untouched by the failed cross-chain attempt.
        Assert.AreEqual((BigInteger)400, sb.GetLockedBalance(ChainA, AssetHash));
    }

    [TestMethod]
    public void Withdrawal_ExceedingChainsOwnEscrow_Fails()
    {
        var stack = Deploy();
        var sb = stack.Bridge;
        var emitting = UInt160.Parse("0x" + new string('d', 40));
        var l2Sender = UInt160.Parse("0x" + new string('e', 40));
        var recipient = UInt160.Parse("0x" + new string('c', 40));

        sb.Deposit(AssetHash, 100, ChainA, recipient);
        var leafBytes = LeafBytes(ChainA, emitting, l2Sender, recipient, L2Asset, 101, nonce: 1);
        var leaf = new UInt256(leafBytes);
        SettleWithdrawalBatch(stack, ChainA, [leafBytes]);
        Assert.IsTrue(stack.Settlement.VerifyWithdrawalLeafWithProof(ChainA, 1, leaf, new List<object>(), 0)!,
            "the proof verifies — the failure below is the escrow cap, not the proof");

        Assert.ThrowsExactly<TestException>(() =>
            sb.FinalizeWithdrawalWithProof(ChainA, 1, leaf, new List<object>(), 0,
                emitting, l2Sender, L2Asset, 1, AssetHash, recipient, 101),
            "withdrawing more than the chain's own escrow must fail");
    }

    [TestMethod]
    public void Withdrawal_RelabelledLeafIndex_Fails()
    {
        var stack = Deploy();
        var sb = stack.Bridge;
        var emitting = UInt160.Parse("0x" + new string('d', 40));
        var l2Sender = UInt160.Parse("0x" + new string('e', 40));
        var recipient = UInt160.Parse("0x" + new string('c', 40));

        sb.Deposit(AssetHash, 1000, ChainA, recipient);

        // Two-withdrawal tree (depth 1): 600 at nonce 1, 200 at nonce 2.
        var leaf600 = LeafBytes(ChainA, emitting, l2Sender, recipient, L2Asset, 600, nonce: 1);
        var leaf200 = LeafBytes(ChainA, emitting, l2Sender, recipient, L2Asset, 200, nonce: 2);
        var leaves = new[] { leaf600, leaf200 };
        SettleWithdrawalBatch(stack, ChainA, leaves);
        var sorted = SortLeaves(leaves);
        for (var i = 0; i < sorted.Length; i++)
        {
            Assert.IsTrue(stack.Settlement.VerifyWithdrawalLeafWithProof(
                    ChainA, 1, new UInt256(sorted[i]), ProofSiblings(leaves, i), (ulong)i)!,
                $"leaf {i} must verify at its true position");
        }

        var idx600 = IndexOf(sorted, leaf600);
        sb.FinalizeWithdrawalWithProof(ChainA, 1, new UInt256(leaf600),
            ProofSiblings(leaves, idx600), (ulong)idx600,
            emitting, l2Sender, L2Asset, 1, AssetHash, recipient, 600);
        Assert.AreEqual((BigInteger)400, sb.GetLockedBalance(ChainA, AssetHash), "withdrawal debits chain A's escrow");

        // V5 position binding: the depth-1 proof for leaf 200 still folds to the batch's withdrawal
        // root when the index is relabelled to idx200 + 2 — the fold consumes only the low bit — but
        // that index resolves to no leaf of the committed tree. The verifier must reject it; before
        // the terminator this call paid out.
        var idx200 = IndexOf(sorted, leaf200);
        Assert.ThrowsExactly<TestException>(() =>
            sb.FinalizeWithdrawalWithProof(ChainA, 1, new UInt256(leaf200),
                ProofSiblings(leaves, idx200), (ulong)idx200 + 2,
                emitting, l2Sender, L2Asset, 2, AssetHash, recipient, 200),
            "a proof relabelled past the tree's depth must not pay out");

        // The relabelled attempt must not have consumed leaf 200 or touched the escrow; the same
        // proof at its true position still pays out.
        Assert.AreEqual((BigInteger)400, sb.GetLockedBalance(ChainA, AssetHash));
        sb.FinalizeWithdrawalWithProof(ChainA, 1, new UInt256(leaf200),
            ProofSiblings(leaves, idx200), (ulong)idx200,
            emitting, l2Sender, L2Asset, 2, AssetHash, recipient, 200);
        Assert.AreEqual((BigInteger)200, sb.GetLockedBalance(ChainA, AssetHash), "legitimate withdrawal still pays");
    }
}
