using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.L2.Audit;
using Neo.L2.Bridge;
using Neo.L2.State;

namespace Neo.L2.Batch.FormalVerification;

/// <summary>
/// Finite regressions for serialization, relative continuity, message binding, and Merkle proofs.
/// These do not prove executor determinism, nonce allocation, replay prevention, or withdrawal settlement.
/// </summary>
/// <remarks>See doc.md §7.3, §8, §10, and §11. No statistical confidence or whole-system proof is claimed.</remarks>
[TestClass]
public class UT_Phase3AdvancedProperties
{
    [TestMethod]
    public async Task ChainAuditor_SerializedSequence_ReportsBrokenStateLink()
    {
        var batches = new L2BatchCommitment[16];
        var previousRoot = UInt256.Zero;
        for (var index = 0; index < batches.Length; index++)
        {
            var batch = CreateCommitment(index + 1) with { PreStateRoot = previousRoot };
            batches[index] = BatchSerializer.Decode(BatchSerializer.Encode(batch));
            previousRoot = batch.PostStateRoot;
        }

        var auditor = new ChainAuditor().Register(new ContinuityCheck());
        var valid = await auditor.AuditAsync(batches);
        Assert.IsTrue(valid.Passed);
        Assert.AreEqual(1U, valid.ChainId);
        Assert.AreEqual(1UL, valid.FirstBatch);
        Assert.AreEqual(16UL, valid.LastBatch);

        foreach (var index in new[] { 1, 8, 15 })
        {
            var broken = (L2BatchCommitment[])batches.Clone();
            broken[index] = broken[index] with { PreStateRoot = ChangeHash(broken[index].PreStateRoot) };
            broken[index] = BatchSerializer.Decode(BatchSerializer.Encode(broken[index]));
            var report = await auditor.AuditAsync(broken);
            Assert.IsFalse(report.Passed);
            Assert.AreEqual(1, report.Findings.Count);
            Assert.AreEqual("continuity", report.Findings[0].Check);
            Assert.AreEqual(broken[index].BatchNumber, report.Findings[0].BatchNumber);
            StringAssert.Contains(report.Findings[0].Detail, "preStateRoot");
        }
    }

    [TestMethod]
    public void BatchSerializer_IndependentEqualInputs_ProduceOrderedIdenticalBytes()
    {
        for (var seed = 1; seed <= 50; seed++)
        {
            var first = CreateCommitment(seed);
            var second = CreateCommitment(seed);
            var encoded = BatchSerializer.Encode(first);
            Assert.AreNotSame(first, second);
            Assert.AreSequenceEqual(encoded, BatchSerializer.Encode(second));
            Assert.AreEqual(first, BatchSerializer.Decode(encoded));

            var changedProof = second.Proof.ToArray();
            changedProof[seed % changedProof.Length] ^= 0x80;
            var changed = BatchSerializer.Encode(second with { Proof = changedProof });
            Assert.IsFalse(encoded.AsSpan().SequenceEqual(changed));
            Assert.AreSequenceEqual(changedProof, BatchSerializer.Decode(changed).Proof.ToArray());
        }
    }

    [TestMethod]
    public void DepositMessage_NonceAndDestination_AreBoundInCanonicalMessage()
    {
        var record = CreateDeposit(1);
        var bridge = Address(4);
        var baseline = record.ToCrossChainMessage(1, bridge);
        foreach (var nonce in new[] { 0UL, 1UL, 255UL, 256UL, 1UL << 63, ulong.MaxValue })
        {
            var message = (record with { Nonce = nonce }).ToCrossChainMessage(1, bridge);
            var encoded = MessageHasher.EncodeMessage(message);
            for (var index = 0; index < 8; index++)
                Assert.AreEqual((byte)(nonce >> (8 * index)), encoded[8 + index]);
            var decoded = MessageHasher.DecodeMessage(encoded);
            Assert.AreEqual(nonce, decoded.Nonce);
            Assert.AreEqual(message.MessageHash, decoded.MessageHash);
            Assert.AreSequenceEqual(baseline.Payload.ToArray(), message.Payload.ToArray());
            if (nonce != record.Nonce)
                Assert.AreNotEqual(baseline.MessageHash, message.MessageHash);

            var otherChain = (record with { Nonce = nonce }).ToCrossChainMessage(2, bridge);
            Assert.AreNotEqual(message.MessageHash, otherChain.MessageHash);
            Assert.AreEqual(2U, MessageHasher.DecodeMessage(MessageHasher.EncodeMessage(otherChain)).TargetChainId);
        }
    }

    [TestMethod]
    public void DepositMessageTree_ProofsVerify_AndRejectChangedLeafRootOrSibling()
    {
        foreach (var count in new[] { 1, 2, 7, 8, 100 })
        {
            var leaves = Enumerable.Range(1, count)
                .Select(index => CreateDeposit(index).ToCrossChainMessage(1, Address(4)).MessageHash)
                .ToArray();
            var tree = new MerkleTree(leaves);
            Assert.AreEqual(count, tree.LeafCount);
            var expectedDepth = 0;
            for (var width = count; width > 1; width = (width + 1) / 2)
                expectedDepth++;
            Assert.AreEqual(expectedDepth, tree.Depth);
            Assert.AreEqual(Neo.Cryptography.MerkleTree.ComputeRoot(leaves), tree.Root);

            for (var index = 0; index < count; index++)
            {
                var proof = tree.GetProof(index);
                Assert.AreEqual(leaves[index], proof.Leaf);
                Assert.AreEqual(index, proof.LeafIndex);
                Assert.IsTrue(proof.Verify(tree.Root));
                Assert.IsFalse((proof with { Leaf = ChangeHash(proof.Leaf) }).Verify(tree.Root));
                Assert.IsFalse(proof.Verify(ChangeHash(tree.Root)));
                if (proof.Siblings.Count > 0)
                {
                    var siblings = proof.Siblings.ToArray();
                    siblings[0] = ChangeHash(siblings[0]);
                    Assert.IsFalse((proof with { Siblings = siblings }).Verify(tree.Root));
                }
            }
        }
    }

    private static L2BatchCommitment CreateCommitment(int seed)
    {
        var random = new Random(seed);
        UInt256 Root()
        {
            var bytes = new byte[32];
            random.NextBytes(bytes);
            return new UInt256(bytes);
        }
        var proof = new byte[seed + 1];
        random.NextBytes(proof);
        return new L2BatchCommitment
        {
            ChainId = 1,
            BatchNumber = (ulong)seed,
            FirstBlock = (ulong)seed * 100,
            LastBlock = (ulong)seed * 100 + 99,
            PreStateRoot = Root(),
            PostStateRoot = Root(),
            TxRoot = Root(),
            ReceiptRoot = Root(),
            WithdrawalRoot = Root(),
            L2ToL1MessageRoot = Root(),
            L2ToL2MessageRoot = Root(),
            DACommitment = Root(),
            PublicInputHash = Root(),
            ProofType = ProofType.Optimistic,
            Proof = proof,
        };
    }

    private static SharedBridgeDepositRecord CreateDeposit(int index) => new()
    {
        Asset = Address(1),
        Recipient = Address(2),
        Sender = Address(3),
        Nonce = (ulong)index,
        Amount = new BigInteger(index * 1000000),
    };

    private static UInt160 Address(byte value)
    {
        var bytes = new byte[20];
        bytes[0] = value;
        return new UInt160(bytes);
    }

    private static UInt256 ChangeHash(UInt256 hash)
    {
        var bytes = hash.GetSpan().ToArray();
        bytes[0] ^= 0x80;
        return new UInt256(bytes);
    }
}
