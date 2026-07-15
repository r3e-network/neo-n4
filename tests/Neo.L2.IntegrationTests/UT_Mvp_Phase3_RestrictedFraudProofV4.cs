using System.Buffers.Binary;
using Neo.Cryptography;
using Neo.L2.Batch;
using Neo.L2.Challenge;
using Neo.L2.State;

namespace Neo.L2.IntegrationTests;

/// <summary>
/// Phase-3 integration pin for SettlementManager-bound restricted fraud-proof v4.
/// </summary>
[TestClass]
public class UT_Mvp_Phase3_RestrictedFraudProofV4
{
    private const uint ChainId = 1001;
    private const ulong BatchNumber = 42;
    private static readonly UInt160 SettlementManager = Address('5');
    private static readonly UInt160 OptimisticChallenge = Address('6');
    private static readonly UInt160 FraudVerifier = Address('7');
    private static readonly UInt160 Sender = Address('a');

    [TestMethod]
    [DataRow(true, RestrictedFraudProofV4Verdict.Fraud)]
    [DataRow(false, RestrictedFraudProofV4Verdict.NoFraud)]
    public void RestrictedV4_BindsCommittedSingleStepAndReturnsSemanticVerdict(
        bool committedFraud,
        RestrictedFraudProofV4Verdict expectedVerdict)
    {
        var transaction = new byte[29];
        transaction[0] = 0x01;
        Sender.GetSpan().CopyTo(transaction.AsSpan(1, 20));
        BinaryPrimitives.WriteUInt64LittleEndian(transaction.AsSpan(21, 8), 9);

        var stateKey = new byte[28];
        "counter:"u8.CopyTo(stateKey);
        Sender.GetSpan().CopyTo(stateKey.AsSpan(8, 20));
        var preValue = new byte[8];
        var expectedPostValue = new byte[8];
        var committedPostValue = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(preValue, 11);
        BinaryPrimitives.WriteUInt64LittleEndian(expectedPostValue, 20);
        BinaryPrimitives.WriteUInt64LittleEndian(committedPostValue, committedFraud ? 21UL : 20UL);
        var preStateRoot = KeyedStateMerkleTree.HashLeaf(stateKey, preValue);
        var expectedPostStateRoot = KeyedStateMerkleTree.HashLeaf(stateKey, expectedPostValue);
        var committedPostStateRoot = KeyedStateMerkleTree.HashLeaf(stateKey, committedPostValue);
        var transactionHash = new UInt256(Crypto.Hash256(transaction));

        var commitment = new L2BatchCommitment
        {
            ChainId = ChainId,
            BatchNumber = BatchNumber,
            FirstBlock = 100,
            LastBlock = 100,
            PreStateRoot = preStateRoot,
            PostStateRoot = committedPostStateRoot,
            TxRoot = transactionHash,
            ReceiptRoot = UInt256.Zero,
            WithdrawalRoot = UInt256.Zero,
            L2ToL1MessageRoot = UInt256.Zero,
            L2ToL2MessageRoot = UInt256.Zero,
            DACommitment = UInt256.Zero,
            PublicInputHash = UInt256.Zero,
            ProofType = ProofType.Optimistic,
            Proof = ReadOnlyMemory<byte>.Empty,
        };
        var transactionProof = new MerkleProof
        {
            Leaf = transactionHash,
            LeafIndex = 0,
            PathBitmap = 0,
            Siblings = Array.Empty<UInt256>(),
        };
        var stateProof = new StorageProof
        {
            Key = stateKey,
            PreValue = preValue,
            PostValue = committedPostValue,
            LeafIndex = 0,
            PreSiblings = Array.Empty<UInt256>(),
            PostSiblings = Array.Empty<UInt256>(),
        };
        var replayDomain = RestrictedFraudProofV4.CreateReplayDomain(
            0x4F454E,
            SettlementManager,
            OptimisticChallenge,
            FraudVerifier);
        var payload = RestrictedFraudProofV4.Create(
            commitment,
            SettlementManager,
            FraudVerifier,
            replayDomain,
            transaction,
            transactionProof,
            stateProof);
        var canonicalHeader = BatchSerializer.Encode(commitment)
            .AsSpan(0, BatchSerializer.CommitmentFixedSize)
            .ToArray();

        Assert.AreEqual(
            expectedVerdict,
            RestrictedExecutionFraudVerifierV4.Verify(
                RestrictedFraudProofV4.Decode(payload.Encode()),
                canonicalHeader,
                SettlementManager,
                FraudVerifier));
    }

    private static UInt160 Address(char value) =>
        UInt160.Parse("0x" + new string(value, 40));
}
