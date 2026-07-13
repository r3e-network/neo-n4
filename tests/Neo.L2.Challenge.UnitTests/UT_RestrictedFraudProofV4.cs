using System.Buffers.Binary;
using Neo.Cryptography;
using Neo.L2.Batch;
using Neo.L2.State;

namespace Neo.L2.Challenge.UnitTests;

/// <summary>
/// Canonical payload and executable-verifier tests for restricted fraud-proof v4.
/// </summary>
/// <remarks>See doc.md §15 and §17.</remarks>
[TestClass]
public class UT_RestrictedFraudProofV4
{
    private const uint ChainId = 1001;
    private const ulong BatchNumber = 42;
    private static readonly UInt160 SettlementManager = Address('5');
    private static readonly UInt160 OptimisticChallenge = Address('6');
    private static readonly UInt160 FraudVerifier = Address('7');
    private static readonly UInt160 Sender = Address('a');
    private static readonly UInt256 ReplayDomain = RestrictedFraudProofV4.CreateReplayDomain(
        0x4F454E,
        SettlementManager,
        OptimisticChallenge,
        FraudVerifier);

    private sealed record Fixture(
        L2BatchCommitment Commitment,
        byte[] CanonicalHeader,
        RestrictedFraudProofV4 Payload,
        byte[] Transaction,
        MerkleProof TransactionProof,
        StorageProof StateProof,
        UInt256 ExpectedPostStateRoot);

    [TestMethod]
    public void Encode_Decode_RoundTripsCanonicalV4()
    {
        var fixture = BuildFixture(committedFraud: true);

        var bytes = fixture.Payload.Encode();
        var decoded = RestrictedFraudProofV4.Decode(bytes);

        Assert.AreEqual(RestrictedFraudProofV4.Version, bytes[0]);
        Assert.AreEqual(fixture.Payload, decoded);
        CollectionAssert.AreEqual(bytes, decoded.Encode());
    }

    [TestMethod]
    public void Verify_IncorrectCommittedTransition_ReturnsFraud()
    {
        var fixture = BuildFixture(committedFraud: true);

        var verdict = Verify(fixture.Payload, fixture.CanonicalHeader);

        Assert.AreEqual(RestrictedFraudProofV4Verdict.Fraud, verdict);
        Assert.AreNotEqual(fixture.ExpectedPostStateRoot, fixture.Commitment.PostStateRoot);
    }

    [TestMethod]
    public void Verify_CorrectCommittedTransition_ReturnsNoFraud()
    {
        var fixture = BuildFixture(committedFraud: false);

        var verdict = Verify(fixture.Payload, fixture.CanonicalHeader);

        Assert.AreEqual(RestrictedFraudProofV4Verdict.NoFraud, verdict);
        Assert.AreEqual(fixture.ExpectedPostStateRoot, fixture.Commitment.PostStateRoot);
    }

    [TestMethod]
    public void Verify_CommittedHeaderOrRootSubstitution_ReturnsInvalid()
    {
        var fixture = BuildFixture(committedFraud: true);
        var substitutedHeader = fixture.CanonicalHeader.ToArray();
        substitutedHeader[60] ^= 0x80;

        Assert.AreEqual(
            RestrictedFraudProofV4Verdict.Invalid,
            Verify(fixture.Payload, substitutedHeader));

        var substitutedTranscript = fixture.Payload with
        {
            Transcript = fixture.Payload.Transcript with
            {
                CommittedPostStateRoot = UInt256.Zero,
            },
        };
        Assert.AreEqual(
            RestrictedFraudProofV4Verdict.Invalid,
            Verify(substitutedTranscript, fixture.CanonicalHeader));
    }

    [TestMethod]
    public void Create_PostWitnessNotBoundToCommittedPostRoot_Rejects()
    {
        var fixture = BuildFixture(committedFraud: true);
        var substitutedCommitment = fixture.Commitment with
        {
            PostStateRoot = new UInt256(Crypto.Hash256("unwitnessed-committed-post-state"u8)),
        };

        Assert.ThrowsExactly<ArgumentException>(() => RestrictedFraudProofV4.Create(
            substitutedCommitment,
            SettlementManager,
            FraudVerifier,
            ReplayDomain,
            fixture.Transaction,
            fixture.TransactionProof,
            fixture.StateProof));
    }

    [TestMethod]
    public void Verify_ChainBatchTxAndBisectionTampering_ReturnsInvalid()
    {
        var fixture = BuildFixture(committedFraud: true);
        var mutations = new (int Offset, int Width)[]
        {
            (RestrictedFraudProofV4.ChainIdOffset, 4),
            (RestrictedFraudProofV4.BatchNumberOffset, 8),
            (RestrictedFraudProofV4.DisputedTxIndexOffset, 4),
            (RestrictedFraudProofV4.TransactionCountOffset, 4),
            (RestrictedFraudProofV4.LowerBoundOffset, 4),
            (RestrictedFraudProofV4.UpperBoundOffset, 4),
        };

        foreach (var mutation in mutations)
        {
            var tampered = fixture.Payload.Encode();
            tampered[mutation.Offset + mutation.Width - 1] ^= 0x01;
            AssertInvalid(tampered, fixture.CanonicalHeader);
        }
    }

    [TestMethod]
    public void Verify_ReplaySemanticClaimTranscriptAndWitnessTampering_ReturnsInvalid()
    {
        var fixture = BuildFixture(committedFraud: true);
        var offsets = new[]
        {
            RestrictedFraudProofV4.ReplayDomainOffset,
            RestrictedFraudProofV4.ExecutorSemanticIdOffset,
            RestrictedFraudProofV4.ClaimIdOffset,
            RestrictedFraudProofV4.TranscriptHashOffset,
            RestrictedFraudProofV4.WitnessHashOffset,
            RestrictedFraudProofV4.CommittedHeaderHashOffset,
            RestrictedFraudProofV4.FixedHeaderSize,
        };

        foreach (var offset in offsets)
        {
            var tampered = fixture.Payload.Encode();
            tampered[offset] ^= 0x01;
            AssertInvalid(tampered, fixture.CanonicalHeader);
        }

        Assert.AreEqual(
            RestrictedFraudProofV4Verdict.Invalid,
            RestrictedExecutionFraudVerifierV4.Verify(
                fixture.Payload,
                fixture.CanonicalHeader,
                Address('8'),
                FraudVerifier));
        Assert.AreEqual(
            RestrictedFraudProofV4Verdict.Invalid,
            RestrictedExecutionFraudVerifierV4.Verify(
                fixture.Payload,
                fixture.CanonicalHeader,
                SettlementManager,
                Address('8')));
    }

    [TestMethod]
    public void Verify_KeyValueAndMerklePathTampering_ReturnsInvalid()
    {
        var fixture = BuildFixture(committedFraud: true);
        var wrongValue = fixture.StateProof.PostValue.ToArray();
        wrongValue[0] ^= 0x01;
        var wrongSibling = new UInt256(Crypto.Hash256("wrong-sibling"u8));

        var witnessMutations = new[]
        {
            fixture.Payload with
            {
                StateProof = fixture.StateProof with { PostValue = wrongValue },
            },
            fixture.Payload with
            {
                StateProof = fixture.StateProof with
                {
                    PostSiblings = new[] { wrongSibling },
                },
            },
            fixture.Payload with
            {
                StateProof = fixture.StateProof with { LeafIndex = 2 },
            },
            fixture.Payload with
            {
                TransactionProof = fixture.TransactionProof with { LeafIndex = 1 },
            },
        };

        foreach (var payload in witnessMutations)
            Assert.AreEqual(
                RestrictedFraudProofV4Verdict.Invalid,
                Verify(payload, fixture.CanonicalHeader));
    }

    [TestMethod]
    public void Create_UnsupportedOpcodeOrMultiTransactionProof_FailsClosed()
    {
        var fixture = BuildFixture(committedFraud: true);
        var unsupportedTransaction = fixture.Transaction.ToArray();
        unsupportedTransaction[0] = 0x02;

        Assert.ThrowsExactly<InvalidDataException>(() => RestrictedFraudProofV4.Create(
            fixture.Commitment,
            SettlementManager,
            FraudVerifier,
            ReplayDomain,
            unsupportedTransaction,
            fixture.TransactionProof,
            fixture.StateProof));

        var secondTransaction = new byte[] { 0xFF };
        var transactionTree = new Neo.L2.State.MerkleTree(new[]
        {
            new UInt256(Crypto.Hash256(fixture.Transaction)),
            new UInt256(Crypto.Hash256(secondTransaction)),
        });
        var multiTransactionCommitment = fixture.Commitment with { TxRoot = transactionTree.Root };

        Assert.ThrowsExactly<ArgumentException>(() => RestrictedFraudProofV4.Create(
            multiTransactionCommitment,
            SettlementManager,
            FraudVerifier,
            ReplayDomain,
            fixture.Transaction,
            transactionTree.GetProof(0),
            fixture.StateProof));
    }

    [TestMethod]
    public void Decode_LegacyOrTrailingPayload_FailsClosed()
    {
        var fixture = BuildFixture(committedFraud: true);
        var legacy = fixture.Payload.Encode();
        legacy[0] = 3;
        Assert.ThrowsExactly<InvalidDataException>(() => RestrictedFraudProofV4.Decode(legacy));

        var trailing = fixture.Payload.Encode().Concat(new byte[] { 0x00 }).ToArray();
        Assert.ThrowsExactly<ArgumentException>(() => RestrictedFraudProofV4.Decode(trailing));
    }

    private static Fixture BuildFixture(bool committedFraud)
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

        var siblingLeaf = KeyedStateMerkleTree.HashLeaf(new byte[] { 0xFF }, new byte[] { 0x44 });
        var preLeaf = KeyedStateMerkleTree.HashLeaf(stateKey, preValue);
        var expectedPostLeaf = KeyedStateMerkleTree.HashLeaf(stateKey, expectedPostValue);
        var committedPostLeaf = KeyedStateMerkleTree.HashLeaf(stateKey, committedPostValue);
        var preStateRoot = Neo.L2.State.MerkleTree.ComputeRoot(new[] { preLeaf, siblingLeaf });
        var expectedPostStateRoot = Neo.L2.State.MerkleTree.ComputeRoot(new[] { expectedPostLeaf, siblingLeaf });
        var committedPostStateRoot = Neo.L2.State.MerkleTree.ComputeRoot(new[] { committedPostLeaf, siblingLeaf });

        var transactionHash = new UInt256(Crypto.Hash256(transaction));
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
            PreSiblings = new[] { siblingLeaf },
            PostSiblings = new[] { siblingLeaf },
        };
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
        var payload = RestrictedFraudProofV4.Create(
            commitment,
            SettlementManager,
            FraudVerifier,
            ReplayDomain,
            transaction,
            transactionProof,
            stateProof);
        var canonicalHeader = BatchSerializer.Encode(commitment)
            .AsSpan(0, BatchSerializer.CommitmentFixedSize)
            .ToArray();

        return new Fixture(
            commitment,
            canonicalHeader,
            payload,
            transaction,
            transactionProof,
            stateProof,
            expectedPostStateRoot);
    }

    private static RestrictedFraudProofV4Verdict Verify(
        RestrictedFraudProofV4 payload,
        byte[] canonicalHeader) =>
        RestrictedExecutionFraudVerifierV4.Verify(
            payload,
            canonicalHeader,
            SettlementManager,
            FraudVerifier);

    private static void AssertInvalid(byte[] payloadBytes, byte[] canonicalHeader)
    {
        var payload = RestrictedFraudProofV4.Decode(payloadBytes);
        Assert.AreEqual(RestrictedFraudProofV4Verdict.Invalid, Verify(payload, canonicalHeader));
    }

    private static UInt160 Address(char value) =>
        UInt160.Parse("0x" + new string(value, 40));
}
