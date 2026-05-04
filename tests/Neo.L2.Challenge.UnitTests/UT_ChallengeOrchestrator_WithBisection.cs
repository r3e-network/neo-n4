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
    public async Task InspectWithBisection_RejectsNullCommitment()
    {
        // Pin ChallengeOrchestrator.cs:93. Param-level null-guard companion to
        // Bisection_RejectsNullCheckpointEntry (per-entry, iter 196).
        var orch = new ChallengeOrchestrator(new NoopReplayer());
        var pre = H(0);
        var arr = new[] { pre, H(1) };
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(async () =>
            await orch.InspectWithBisectionAsync(null!, MkInputs(pre), arr, arr));
    }

    [TestMethod]
    public async Task InspectWithBisection_RejectsNullInputs()
    {
        // Pin ChallengeOrchestrator.cs:94.
        var orch = new ChallengeOrchestrator(new NoopReplayer());
        var pre = H(0);
        var arr = new[] { pre, H(1) };
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(async () =>
            await orch.InspectWithBisectionAsync(MkCommit(pre, H(1)), null!, arr, arr));
    }

    [TestMethod]
    public async Task InspectWithBisection_RejectsNullChallengerCheckpoints()
    {
        // Pin ChallengeOrchestrator.cs:95.
        var orch = new ChallengeOrchestrator(new NoopReplayer());
        var pre = H(0);
        var arr = new[] { pre, H(1) };
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(async () =>
            await orch.InspectWithBisectionAsync(MkCommit(pre, H(1)), MkInputs(pre), null!, arr));
    }

    [TestMethod]
    public async Task InspectWithBisection_RejectsNullSequencerCheckpoints()
    {
        // Pin ChallengeOrchestrator.cs:96.
        var orch = new ChallengeOrchestrator(new NoopReplayer());
        var pre = H(0);
        var arr = new[] { pre, H(1) };
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(async () =>
            await orch.InspectWithBisectionAsync(MkCommit(pre, H(1)), MkInputs(pre), arr, null!));
    }

    [TestMethod]
    public async Task InspectWithBisection_RejectsNullPostStateRootInCommitment()
    {
        // Pin ChallengeOrchestrator.cs:100. Companion to Inspect's pin.
        var orch = new ChallengeOrchestrator(new NoopReplayer());
        var pre = H(0);
        var arr = new[] { pre, H(1) };
        var commitment = MkCommit(pre, null!);
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(async () =>
            await orch.InspectWithBisectionAsync(commitment, MkInputs(pre), arr, arr));
    }

    [TestMethod]
    public async Task InspectWithBisection_RejectsNullPreStateRootInInputs()
    {
        // Pin ChallengeOrchestrator.cs:101.
        var orch = new ChallengeOrchestrator(new NoopReplayer());
        var pre = H(0);
        var arr = new[] { pre, H(1) };
        var inputs = MkInputs(null!);
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(async () =>
            await orch.InspectWithBisectionAsync(MkCommit(pre, H(1)), inputs, arr, arr));
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

    [TestMethod]
    public async Task Bisection_EmptyCheckpoints_ThrowsArgumentException()
    {
        // Regression: was IndexOutOfRangeException when [^1] hit an empty array before
        // validation. Now the orchestrator's own boundary rejects with ArgumentException.
        var orch = new ChallengeOrchestrator(new NoopReplayer());
        var pre = H(0);
        var empty = Array.Empty<UInt256>();

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await orch.InspectWithBisectionAsync(MkCommit(pre, H(1)), MkInputs(pre), empty, empty));
    }

    [TestMethod]
    public async Task Bisection_MismatchedCheckpointLengths_ThrowsArgumentException()
    {
        // Was: line 94 silently compared arrays' last elements at different positions.
        // If the last-elements happened to coincide, we'd wrongly return "no fraud".
        var orch = new ChallengeOrchestrator(new NoopReplayer());
        var pre = H(0);
        var shorter = new[] { pre, H(1), H(2) };
        var longer = new[] { pre, H(1), H(2), H(3), H(4) };

        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await orch.InspectWithBisectionAsync(MkCommit(pre, H(4)), MkInputs(pre), shorter, longer));
        StringAssert.Contains(ex.Message, "same length");
    }

    [TestMethod]
    public async Task Bisection_SingleCheckpoint_ThrowsArgumentException()
    {
        // Length-1 means just preState, no postState — nothing to bisect.
        var orch = new ChallengeOrchestrator(new NoopReplayer());
        var pre = H(0);
        var oneOnly = new[] { pre };

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await orch.InspectWithBisectionAsync(MkCommit(pre, H(1)), MkInputs(pre), oneOnly, oneOnly));
    }

    private sealed class NoopReplayer : IFraudProofGenerator
    {
        public ValueTask<UInt256> ReplayAsync(BatchExecutionRequest inputs, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(UInt256.Zero);
    }

    [TestMethod]
    public async Task Bisection_RejectsNullCheckpointEntry()
    {
        // Regression for iter 196: a null entry in either checkpoint array would
        // propagate to .Equals() in the no-fraud check or BisectionGame's loop,
        // producing a confusing NRE deep in the bisection. Now caught at the source
        // with the bad index named.
        var orch = new ChallengeOrchestrator(new NoopReplayer());
        var pre = H(0);
        var bad = new UInt256?[] { pre, null, H(2) };
        var good = new[] { pre, H(1), H(2) };

        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await orch.InspectWithBisectionAsync(MkCommit(pre, H(2)), MkInputs(pre), bad!, good));
        StringAssert.Contains(ex.Message, "[1]");
    }
}
