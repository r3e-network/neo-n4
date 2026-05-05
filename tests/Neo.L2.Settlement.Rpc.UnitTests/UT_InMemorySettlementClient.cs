using Neo.L2.Batch;

namespace Neo.L2.Settlement.Rpc.UnitTests;

/// <summary>
/// Tests for <see cref="InMemorySettlementClient"/> — the in-process
/// <see cref="ISettlementClient"/> implementation used by tests + devnet runners.
/// Pins the lifecycle-state machine, idempotency rules, and monotonic canonical-
/// state-root behavior.
/// </summary>
[TestClass]
public class UT_InMemorySettlementClient
{
    private static L2BatchCommitment Mk(uint chainId, ulong batchNumber, byte postRootSeed = 0x01) => new()
    {
        ChainId = chainId,
        BatchNumber = batchNumber,
        FirstBlock = batchNumber * 10,
        LastBlock = batchNumber * 10 + 9,
        PreStateRoot = UInt256.Zero,
        PostStateRoot = MakeUInt256(postRootSeed),
        TxRoot = UInt256.Zero,
        ReceiptRoot = UInt256.Zero,
        WithdrawalRoot = UInt256.Zero,
        L2ToL1MessageRoot = UInt256.Zero,
        L2ToL2MessageRoot = UInt256.Zero,
        DACommitment = UInt256.Zero,
        PublicInputHash = UInt256.Zero,
        ProofType = ProofType.None,
        Proof = ReadOnlyMemory<byte>.Empty,
    };

    private static UInt256 MakeUInt256(byte seed)
    {
        var bytes = new byte[32];
        bytes[0] = seed;
        return new UInt256(bytes);
    }

    private static PublicInputs SamplePublicInputs() => new()
    {
        ChainId = 1001, BatchNumber = 1,
        PreStateRoot = UInt256.Zero, PostStateRoot = UInt256.Zero,
        TxRoot = UInt256.Zero, ReceiptRoot = UInt256.Zero,
        WithdrawalRoot = UInt256.Zero, L2ToL1MessageRoot = UInt256.Zero,
        L2ToL2MessageRoot = UInt256.Zero, L1MessageHash = UInt256.Zero,
        DACommitment = UInt256.Zero, BlockContextHash = UInt256.Zero,
    };

    [TestMethod]
    public async Task Submit_RecordsBatchAsPending()
    {
        var client = new InMemorySettlementClient();
        var tx = await client.SubmitBatchAsync(Mk(1001, 1), SamplePublicInputs());
        Assert.AreNotEqual(UInt256.Zero, tx);
        Assert.AreEqual(BatchStatus.Pending, await client.GetBatchStatusAsync(1001, 1));
        Assert.AreEqual(1, client.BatchCount);
    }

    [TestMethod]
    public async Task Submit_TxHashIsDeterministic()
    {
        // Same commitment → same tx hash. Pinned because tests rely on this for
        // round-trip checks; if the hash were random a test couldn't predict it.
        var client = new InMemorySettlementClient();
        var c = Mk(1001, 1);
        var tx1 = await client.SubmitBatchAsync(c, SamplePublicInputs());
        var tx2 = await client.SubmitBatchAsync(c, SamplePublicInputs());
        Assert.AreEqual(tx1, tx2, "idempotent re-submission must return the same tx hash");
        Assert.AreEqual(1, client.BatchCount, "idempotent re-submission must not double-count");
    }

    [TestMethod]
    public async Task Submit_RejectsBatchNumberReuseWithDifferentCommitment()
    {
        var client = new InMemorySettlementClient();
        await client.SubmitBatchAsync(Mk(1001, 1, postRootSeed: 0x01), SamplePublicInputs());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await client.SubmitBatchAsync(Mk(1001, 1, postRootSeed: 0x02), SamplePublicInputs()));
    }

    [TestMethod]
    public async Task GetBatchStatus_UnknownReturnsUnknown()
    {
        var client = new InMemorySettlementClient();
        Assert.AreEqual(BatchStatus.Unknown, await client.GetBatchStatusAsync(1001, 99));
    }

    [TestMethod]
    public async Task GetCanonicalStateRoot_DefaultsToZero()
    {
        var client = new InMemorySettlementClient();
        Assert.AreEqual(UInt256.Zero, await client.GetCanonicalStateRootAsync(1001));
    }

    [TestMethod]
    public async Task AdvanceStatus_PendingToChallengeable()
    {
        var client = new InMemorySettlementClient();
        await client.SubmitBatchAsync(Mk(1001, 1), SamplePublicInputs());
        client.AdvanceStatus(1001, 1, BatchStatus.Challengeable);
        Assert.AreEqual(BatchStatus.Challengeable, await client.GetBatchStatusAsync(1001, 1));
    }

    [TestMethod]
    public async Task AdvanceStatus_PendingToFinalized_BumpsCanonicalRoot()
    {
        var client = new InMemorySettlementClient();
        await client.SubmitBatchAsync(Mk(1001, 1, postRootSeed: 0x05), SamplePublicInputs());
        client.AdvanceStatus(1001, 1, BatchStatus.Finalized);
        Assert.AreEqual(MakeUInt256(0x05), await client.GetCanonicalStateRootAsync(1001));
    }

    [TestMethod]
    public async Task AdvanceStatus_ChallengeableToFinalized()
    {
        var client = new InMemorySettlementClient();
        await client.SubmitBatchAsync(Mk(1001, 1, postRootSeed: 0x07), SamplePublicInputs());
        client.AdvanceStatus(1001, 1, BatchStatus.Challengeable);
        client.AdvanceStatus(1001, 1, BatchStatus.Finalized);
        Assert.AreEqual(BatchStatus.Finalized, await client.GetBatchStatusAsync(1001, 1));
        Assert.AreEqual(MakeUInt256(0x07), await client.GetCanonicalStateRootAsync(1001));
    }

    [TestMethod]
    public async Task AdvanceStatus_PendingToReverted()
    {
        var client = new InMemorySettlementClient();
        await client.SubmitBatchAsync(Mk(1001, 1), SamplePublicInputs());
        client.AdvanceStatus(1001, 1, BatchStatus.Reverted);
        Assert.AreEqual(BatchStatus.Reverted, await client.GetBatchStatusAsync(1001, 1));
        // Reverted does NOT update canonical root.
        Assert.AreEqual(UInt256.Zero, await client.GetCanonicalStateRootAsync(1001));
    }

    [TestMethod]
    public async Task AdvanceStatus_ChallengeableToReverted()
    {
        var client = new InMemorySettlementClient();
        await client.SubmitBatchAsync(Mk(1001, 1), SamplePublicInputs());
        client.AdvanceStatus(1001, 1, BatchStatus.Challengeable);
        client.AdvanceStatus(1001, 1, BatchStatus.Reverted);
        Assert.AreEqual(BatchStatus.Reverted, await client.GetBatchStatusAsync(1001, 1));
    }

    [TestMethod]
    public async Task AdvanceStatus_RejectsFinalizedToPending()
    {
        // Pin the lifecycle invariant: once finalized, can't go back.
        var client = new InMemorySettlementClient();
        await client.SubmitBatchAsync(Mk(1001, 1), SamplePublicInputs());
        client.AdvanceStatus(1001, 1, BatchStatus.Finalized);
        Assert.ThrowsExactly<InvalidOperationException>(
            () => client.AdvanceStatus(1001, 1, BatchStatus.Pending));
    }

    [TestMethod]
    public async Task AdvanceStatus_RejectsRevertedToFinalized()
    {
        var client = new InMemorySettlementClient();
        await client.SubmitBatchAsync(Mk(1001, 1), SamplePublicInputs());
        client.AdvanceStatus(1001, 1, BatchStatus.Reverted);
        Assert.ThrowsExactly<InvalidOperationException>(
            () => client.AdvanceStatus(1001, 1, BatchStatus.Finalized));
    }

    [TestMethod]
    public void AdvanceStatus_RejectsUnknownBatch()
    {
        var client = new InMemorySettlementClient();
        Assert.ThrowsExactly<InvalidOperationException>(
            () => client.AdvanceStatus(1001, 99, BatchStatus.Finalized));
    }

    [TestMethod]
    public async Task AdvanceStatus_OutOfOrderFinalize_DoesNotRegressCanonicalRoot()
    {
        // Pin iter-203 monotonicity rule. Finalizing batch 5 then finalizing batch 3
        // must NOT revert the canonical root to batch 3's value.
        var client = new InMemorySettlementClient();
        await client.SubmitBatchAsync(Mk(1001, 3, postRootSeed: 0x03), SamplePublicInputs());
        await client.SubmitBatchAsync(Mk(1001, 5, postRootSeed: 0x05), SamplePublicInputs());

        client.AdvanceStatus(1001, 5, BatchStatus.Finalized);
        Assert.AreEqual(MakeUInt256(0x05), await client.GetCanonicalStateRootAsync(1001));

        client.AdvanceStatus(1001, 3, BatchStatus.Finalized);
        Assert.AreEqual(MakeUInt256(0x05), await client.GetCanonicalStateRootAsync(1001),
            "out-of-order Finalize must not regress the canonical state root to a lower batch");
    }

    [TestMethod]
    public async Task SubmitBatchAsync_RejectsNullCommitment()
    {
        var client = new InMemorySettlementClient();
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await client.SubmitBatchAsync(null!, SamplePublicInputs()));
    }

    [TestMethod]
    public async Task SubmitBatchAsync_RejectsNullPublicInputs()
    {
        var client = new InMemorySettlementClient();
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await client.SubmitBatchAsync(Mk(1001, 1), null!));
    }

    [TestMethod]
    public async Task PerChainState_Independent()
    {
        // Submitting on chain 1001 must not affect chain 2002.
        var client = new InMemorySettlementClient();
        await client.SubmitBatchAsync(Mk(1001, 1, postRootSeed: 0x11), SamplePublicInputs());
        client.AdvanceStatus(1001, 1, BatchStatus.Finalized);

        Assert.AreEqual(MakeUInt256(0x11), await client.GetCanonicalStateRootAsync(1001));
        Assert.AreEqual(UInt256.Zero, await client.GetCanonicalStateRootAsync(2002),
            "other chains must not pick up state from chain 1001");
    }
}
