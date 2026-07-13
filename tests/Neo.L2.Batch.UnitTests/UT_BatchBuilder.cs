namespace Neo.L2.Batch.UnitTests;

[TestClass]
public class UT_BatchBuilder
{
    private static BatchBlockContext SampleContext() => new()
    {
        L1FinalizedHeight = 1234,
        FirstBlockTimestamp = 1_700_000_000_000,
        LastBlockTimestamp = 1_700_000_005_000,
        SequencerCommitteeHash = UInt256.Parse("0x" + new string('1', 64)),
        Network = 0x4F454E,
    };

    private static BatchExecutionResult SampleResult() => new()
    {
        PostStateRoot = UInt256.Parse("0x" + new string('a', 64)),
        ReceiptRoot = UInt256.Parse("0x" + new string('b', 64)),
        WithdrawalRoot = UInt256.Parse("0x" + new string('c', 64)),
        L2ToL1MessageRoot = UInt256.Parse("0x" + new string('d', 64)),
        L2ToL2MessageRoot = UInt256.Parse("0x" + new string('e', 64)),
        TxRoot = UInt256.Parse("0x" + new string('f', 64)),
        GasConsumed = 999_999,
    };

    [TestMethod]
    public void Builder_AccumulatesBlocksTransactionsAndMessages()
    {
        var b = new BatchBuilder(1001, 7, 100, UInt256.Zero);
        b.AddBlock(101).AddBlock(102).AddBlock(103);
        b.AddTransaction(new byte[] { 0x01, 0x02 });
        b.AddTransaction(new byte[] { 0x03 });

        Assert.AreEqual(100UL, b.Batch.FirstBlock);
        Assert.AreEqual(103UL, b.Batch.LastBlock);
        Assert.AreEqual(2, b.Batch.TransactionCount);
        Assert.AreEqual(1001U, b.Batch.ChainId);
    }

    [TestMethod]
    public void Builder_RejectsOutOfOrderBlocks()
    {
        var b = new BatchBuilder(1001, 1, 100, UInt256.Zero);
        b.AddBlock(101);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => b.AddBlock(99));
    }

    [TestMethod]
    public void Builder_RequiresBlockContextBeforeExecutionRequest()
    {
        var b = new BatchBuilder(1001, 1, 100, UInt256.Zero);
        Assert.ThrowsExactly<InvalidOperationException>(() => b.ToExecutionRequest());
    }

    [TestMethod]
    public void Builder_ProducesExecutionRequestThenCommitment()
    {
        var b = new BatchBuilder(1001, 1, 100, UInt256.Zero);
        b.AddBlock(100).AddBlock(101);
        b.AddTransaction(new byte[] { 0xAA });
        b.WithBlockContext(SampleContext());

        var req = b.ToExecutionRequest();
        Assert.AreEqual(1001U, req.ChainId);
        Assert.AreEqual(1UL, req.BatchNumber);
        Assert.AreEqual(1, req.Transactions.Count);

        var commitment = b.Seal(SampleResult(), UInt256.Zero, UInt256.Zero, ProofType.Multisig, new byte[] { 0x77 });

        Assert.AreEqual(1001U, commitment.ChainId);
        Assert.AreEqual(101UL, commitment.LastBlock);
        Assert.AreEqual(ProofType.Multisig, commitment.ProofType);
        Assert.IsTrue(b.Batch.IsSealed);
    }

    [TestMethod]
    public void SealArtifact_DeepCopiesTransactionsMessagesAndContext()
    {
        var transaction = new byte[] { 0xaa, 0xbb };
        var messagePayload = new byte[] { 0x11, 0x22 };
        var messageWithoutHash = new CrossChainMessage
        {
            SourceChainId = 0,
            TargetChainId = 1001,
            Nonce = 7,
            Sender = new UInt160(new byte[UInt160.Length]),
            Receiver = new UInt160(Enumerable.Repeat((byte)1, UInt160.Length).ToArray()),
            MessageType = MessageType.Deposit,
            Payload = messagePayload,
            MessageHash = UInt256.Zero,
        };
        var message = messageWithoutHash with
        {
            MessageHash = Neo.L2.State.MessageHasher.HashMessage(messageWithoutHash),
        };
        var builder = new BatchBuilder(1001, 1, 100, UInt256.Zero)
            .AddBlock(100)
            .AddTransaction(transaction)
            .ConsumeL1Message(message)
            .WithBlockContext(SampleContext());

        var sealedBatch = builder.SealArtifact();
        transaction[0] = 0xff;
        messagePayload[0] = 0xff;

        CollectionAssert.AreEqual(
            new byte[] { 0xaa, 0xbb }, sealedBatch.Transactions[0].ToArray());
        CollectionAssert.AreEqual(
            new byte[] { 0x11, 0x22 }, sealedBatch.L1Messages[0].Payload.ToArray());
        Assert.AreEqual(SampleContext(), sealedBatch.BlockContext);
        Assert.ThrowsExactly<InvalidOperationException>(
            () => builder.AddTransaction(new byte[] { 0x01 }));
    }

    [TestMethod]
    public void Builder_RejectsAddAfterSeal()
    {
        var b = new BatchBuilder(1001, 1, 100, UInt256.Zero);
        b.WithBlockContext(SampleContext());
        b.Seal(SampleResult(), UInt256.Zero, UInt256.Zero, ProofType.None, ReadOnlyMemory<byte>.Empty);
        Assert.ThrowsExactly<InvalidOperationException>(() => b.AddBlock(200));
    }

    [TestMethod]
    public void ToCommitment_RejectsNullExecutionResult()
    {
        // Regression for iter 166: previously a null executionResult would NRE on
        // the first property access (executionResult.PostStateRoot) — confusing for the
        // operator and only surfaced AFTER _batch.Seal() had committed. The early null
        // guard surfaces the bad input before sealing so a re-attempt can succeed.
        var b = new BatchBuilder(1001, 1, 100, UInt256.Zero);
        b.WithBlockContext(SampleContext());
        Assert.ThrowsExactly<ArgumentNullException>(
            () => b.Seal(null!, UInt256.Zero, UInt256.Zero, ProofType.None, ReadOnlyMemory<byte>.Empty));
        // Batch must NOT be sealed after the failed call — confirms the guard fires
        // BEFORE _batch.Seal().
        Assert.IsFalse(b.Batch.IsSealed, "guard must fire before Seal so a retry can succeed");
    }

    [TestMethod]
    public void ToCommitment_RejectsNullDaCommitment()
    {
        var b = new BatchBuilder(1001, 1, 100, UInt256.Zero);
        b.WithBlockContext(SampleContext());
        Assert.ThrowsExactly<ArgumentNullException>(
            () => b.Seal(SampleResult(), null!, UInt256.Zero, ProofType.None, ReadOnlyMemory<byte>.Empty));
        Assert.IsFalse(b.Batch.IsSealed);
    }

    [TestMethod]
    public void ToCommitment_RejectsNullPublicInputHash()
    {
        var b = new BatchBuilder(1001, 1, 100, UInt256.Zero);
        b.WithBlockContext(SampleContext());
        Assert.ThrowsExactly<ArgumentNullException>(
            () => b.Seal(SampleResult(), UInt256.Zero, null!, ProofType.None, ReadOnlyMemory<byte>.Empty));
        Assert.IsFalse(b.Batch.IsSealed);
    }

    [TestMethod]
    public void Constructor_RejectsNullPreStateRoot()
    {
        // Pin L2Batch.cs:59. UInt256 is reference-typed; `required` only forces "must be
        // set," not "non-null." A null preStateRoot would surface only at Seal time when
        // EncodePublicInputs runs its own ThrowIfNull, attributing the failure to the
        // sealer rather than the constructor. Catch at the source.
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new BatchBuilder(1001, 1, 100, null!));
    }

    [TestMethod]
    public void ConsumeL1Message_RejectsNull()
    {
        var b = new BatchBuilder(1001, 1, 100, UInt256.Zero);
        Assert.ThrowsExactly<ArgumentNullException>(() => b.ConsumeL1Message(null!));
    }

    [TestMethod]
    public void AddWithdrawal_RejectsNull()
    {
        var b = new BatchBuilder(1001, 1, 100, UInt256.Zero);
        Assert.ThrowsExactly<ArgumentNullException>(() => b.AddWithdrawal(null!));
    }

    [TestMethod]
    public void AddL2ToL1Message_RejectsNull()
    {
        var b = new BatchBuilder(1001, 1, 100, UInt256.Zero);
        Assert.ThrowsExactly<ArgumentNullException>(() => b.AddL2ToL1Message(null!));
    }

    [TestMethod]
    public void AddL2ToL2Message_RejectsNull()
    {
        var b = new BatchBuilder(1001, 1, 100, UInt256.Zero);
        Assert.ThrowsExactly<ArgumentNullException>(() => b.AddL2ToL2Message(null!));
    }
}
