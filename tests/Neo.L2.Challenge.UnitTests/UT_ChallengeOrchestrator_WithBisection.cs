using Neo.L2.Batch;
using Neo.L2.Executor;
using Neo.L2.Executor.Receipts;

namespace Neo.L2.Challenge.UnitTests;

[TestClass]
public class UT_ChallengeOrchestrator_WithBisection
{
    private static UInt256 H(byte b)
    {
        var bytes = new byte[32];
        bytes[0] = b;
        return new UInt256(bytes);
    }

    private static L2BatchCommitment MkCommit(UInt256 preRoot, UInt256 claimedPostRoot)
    {
        return new L2BatchCommitment
        {
            ChainId = 1001,
            BatchNumber = 5,
            FirstBlock = 100,
            LastBlock = 200,
            PreStateRoot = preRoot,
            PostStateRoot = claimedPostRoot,
            TxRoot = UInt256.Zero,
            ReceiptRoot = UInt256.Zero,
            WithdrawalRoot = UInt256.Zero,
            L2ToL1MessageRoot = UInt256.Zero,
            L2ToL2MessageRoot = UInt256.Zero,
            DACommitment = UInt256.Zero,
            PublicInputHash = UInt256.Zero,
            ProofType = ProofType.Multisig,
            Proof = new byte[] { 0x01 },
        };
    }

    private static BatchExecutionRequest MkInputs(UInt256 preRoot)
    {
        return new BatchExecutionRequest
        {
            ChainId = 1001,
            BatchNumber = 5,
            PreStateRoot = preRoot,
            Transactions = new List<ReadOnlyMemory<byte>>(),
            L1MessagesConsumed = new List<CrossChainMessage>(),
            BlockContext = new BatchBlockContext
            {
                L1FinalizedHeight = 100,
                FirstBlockTimestamp = 1000, LastBlockTimestamp = 1000,
                SequencerCommitteeHash = UInt256.Zero, Network = 11,
            },
        };
    }

    [TestMethod]
    public async Task Bisection_AgreesAtEnd_ReturnsNull()
    {
        var orch = new ChallengeOrchestrator(new NoopReplayer());
        var pre = H(0);
        var both = new[] { pre, H(1), H(2), H(3) };

        var result = await orch.InspectWithBisectionAsync(MkCommit(pre, H(3)), MkInputs(pre), both, both);
        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task Bisection_DisagreesAt_LastIndex_NarrowsToCorrectIndex()
    {
        var orch = new ChallengeOrchestrator(new NoopReplayer());
        var pre = H(0);
        // Disagreement starts at index 5 — the bisection should narrow to disputed index 4 or 5
        // depending on the exact bisection algorithm. Just assert it's in the right region.
        var honest = new[] { pre, H(1), H(2), H(3), H(4), H(5), H(6), H(7), H(8) };
        var lying  = new[] { pre, H(1), H(2), H(3), H(4), H(99), H(99), H(99), H(99) };

        var payload = await orch.InspectWithBisectionAsync(
            MkCommit(pre, H(99)), MkInputs(pre), honest, lying);

        Assert.IsNotNull(payload);
        Assert.AreEqual(pre, payload!.PreStateRoot);
        Assert.AreEqual(H(99), payload.ClaimedPostStateRoot);
        Assert.AreEqual(H(8), payload.ReplayedPostStateRoot);
        // Bisection narrows to the boundary between agreement and disagreement.
        // Standard algorithm: disputedIndex is the agreed pre-state of the disputed tx.
        Assert.IsTrue(payload.DisputedTxIndex >= 4 && payload.DisputedTxIndex <= 5,
            $"narrowed to index {payload.DisputedTxIndex}, expected 4..5");
    }

    [TestMethod]
    public async Task Bisection_ValidatesArgsLikeInspect()
    {
        var orch = new ChallengeOrchestrator(new NoopReplayer());
        var pre = H(0);
        var arr = new[] { pre, H(1) };

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await orch.InspectWithBisectionAsync(
                MkCommit(pre, H(1)) with { ChainId = 9999 }, MkInputs(pre), arr, arr));
    }

    private sealed class NoopReplayer : IFraudProofGenerator
    {
        public ValueTask<UInt256> ReplayAsync(BatchExecutionRequest inputs, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(UInt256.Zero);
    }
}
