using Neo.Cryptography;
using Neo.L2.Batch;
using Neo.L2.Challenge;
using Neo.L2.Executor;
using Neo.L2.Executor.Receipts;
using Neo.L2.Executor.State;
using Neo.L2.State;

namespace Neo.L2.IntegrationTests;

/// <summary>
/// Phase-3 stitch: a sequencer lies about the post-state root for an 8-tx batch; the
/// challenger replays, detects the mismatch via <see cref="ChallengeOrchestrator"/>, and
/// runs <see cref="BisectionGame"/> to narrow to the single disputed tx.
/// </summary>
[TestClass]
public class UT_Mvp_Phase3_OptimisticChallenge
{
    private const uint LocalChainId = 1001;
    private const int TxCount = 8;

    private static BatchBlockContext Ctx() => new()
    {
        L1FinalizedHeight = 1000,
        FirstBlockTimestamp = 1_700_000_000_000,
        LastBlockTimestamp = 1_700_000_005_000,
        SequencerCommitteeHash = UInt256.Zero,
        Network = 0x4F454E,
    };

    /// <summary>
    /// Honest replayer: walks the txs and builds a real KeyedStateStore. Each tx writes a
    /// distinct key so the per-tx state roots actually evolve.
    /// </summary>
    private sealed class HonestReplayer : IFraudProofGenerator
    {
        public ValueTask<UInt256> ReplayAsync(BatchExecutionRequest request, CancellationToken cancellationToken = default)
        {
            var store = new KeyedStateStore();
            for (var i = 0; i < request.Transactions.Count; i++)
            {
                var tx = request.Transactions[i].Span;
                var key = new byte[] { 0x10, (byte)i };
                store.Put(key, tx.ToArray());
            }
            return new ValueTask<UInt256>(store.ComputeRoot());
        }
    }

    /// <summary>
    /// Walk each tx through a fresh KeyedStateStore and snapshot per-tx state roots.
    /// Returns the (N+1)-element checkpoint array.
    /// </summary>
    private static UInt256[] BuildHonestCheckpoints(IReadOnlyList<ReadOnlyMemory<byte>> txs)
    {
        var store = new KeyedStateStore();
        var checkpoints = new UInt256[txs.Count + 1];
        checkpoints[0] = store.ComputeRoot();
        for (var i = 0; i < txs.Count; i++)
        {
            var key = new byte[] { 0x10, (byte)i };
            store.Put(key, txs[i].Span.ToArray());
            checkpoints[i + 1] = store.ComputeRoot();
        }
        return checkpoints;
    }

    /// <summary>
    /// A "lying" sequencer that diverges from the honest checkpoint at <paramref name="liesFromIndex"/>:
    /// every checkpoint at or after that index is replaced with garbage.
    /// </summary>
    private static UInt256[] BuildLyingCheckpoints(UInt256[] honest, int liesFromIndex)
    {
        var lying = (UInt256[])honest.Clone();
        for (var i = liesFromIndex; i <= honest.Length - 1; i++)
        {
            // Garbage: hash the index alone (deterministic but disagrees with honest).
            var bytes = new byte[32];
            bytes[0] = (byte)(0xAA + i);
            bytes[1] = 0xFF;
            lying[i] = new UInt256(Crypto.Hash256(bytes));
        }
        return lying;
    }

    [TestMethod]
    public async Task SequencerLies_ChallengerCatchesAndNarrowsToSingleTx()
    {
        // Build 8 transactions with distinct payloads.
        var txs = new ReadOnlyMemory<byte>[TxCount];
        for (var i = 0; i < TxCount; i++) txs[i] = new byte[] { (byte)(i + 0x80), 0xCC };

        var honestCheckpoints = BuildHonestCheckpoints(txs);
        var liesFromIndex = 5;
        var lyingCheckpoints = BuildLyingCheckpoints(honestCheckpoints, liesFromIndex);

        var preStateRoot = honestCheckpoints[0];
        var challengerComputedPostRoot = honestCheckpoints[^1];      // truth
        var sequencerClaimedPostRoot = lyingCheckpoints[^1];          // sequencer's claim

        Assert.AreNotEqual(challengerComputedPostRoot, sequencerClaimedPostRoot,
            "test setup error: honest and lying checkpoints must diverge at post-state");

        // ---- Step 1: ChallengeOrchestrator detects the mismatch via fraud-proof generator ----
        var commitment = new L2BatchCommitment
        {
            ChainId = LocalChainId, BatchNumber = 1,
            FirstBlock = 100, LastBlock = 200,
            PreStateRoot = preStateRoot,
            PostStateRoot = sequencerClaimedPostRoot,
            TxRoot = UInt256.Zero, ReceiptRoot = UInt256.Zero, WithdrawalRoot = UInt256.Zero,
            L2ToL1MessageRoot = UInt256.Zero, L2ToL2MessageRoot = UInt256.Zero,
            DACommitment = UInt256.Zero, PublicInputHash = UInt256.Zero,
            ProofType = ProofType.Optimistic,
            Proof = ReadOnlyMemory<byte>.Empty,
        };
        var inputs = new BatchExecutionRequest
        {
            ChainId = LocalChainId, BatchNumber = 1,
            PreStateRoot = preStateRoot,
            Transactions = txs,
            L1MessagesConsumed = Array.Empty<CrossChainMessage>(),
            BlockContext = Ctx(),
        };

        var orchestrator = new ChallengeOrchestrator(new HonestReplayer());
        var fraudProof = await orchestrator.InspectAsync(commitment, inputs);

        Assert.IsNotNull(fraudProof, "challenger should detect divergence");
        Assert.AreEqual(preStateRoot, fraudProof!.PreStateRoot);
        Assert.AreEqual(sequencerClaimedPostRoot, fraudProof.ClaimedPostStateRoot);
        Assert.AreEqual(challengerComputedPostRoot, fraudProof.ReplayedPostStateRoot);

        // ---- Step 2: BisectionGame narrows to the single disputed tx ----
        var game = new BisectionGame(honestCheckpoints, lyingCheckpoints);
        var disputedIndex = game.RunToSettlement();

        // Lies start at checkpoint index `liesFromIndex` (= 5), which means the transaction
        // applied AT step 5 (i.e. the 5th tx, 0-based index 4) is the offender.
        Assert.AreEqual(liesFromIndex - 1, disputedIndex,
            $"bisection should converge on tx index {liesFromIndex - 1}");

        Assert.IsTrue(game.Rounds <= BisectionGame.MaxRoundsFor(TxCount),
            $"used {game.Rounds} rounds, max allowed {BisectionGame.MaxRoundsFor(TxCount)}");
    }

    [TestMethod]
    public async Task HonestSequencer_NoFraudProof_NoBisection()
    {
        var txs = new ReadOnlyMemory<byte>[TxCount];
        for (var i = 0; i < TxCount; i++) txs[i] = new byte[] { (byte)(i + 0x80), 0xCC };

        var honestCheckpoints = BuildHonestCheckpoints(txs);
        var preStateRoot = honestCheckpoints[0];
        var honestPostRoot = honestCheckpoints[^1];

        var commitment = new L2BatchCommitment
        {
            ChainId = LocalChainId, BatchNumber = 1,
            FirstBlock = 100, LastBlock = 200,
            PreStateRoot = preStateRoot,
            PostStateRoot = honestPostRoot,
            TxRoot = UInt256.Zero, ReceiptRoot = UInt256.Zero, WithdrawalRoot = UInt256.Zero,
            L2ToL1MessageRoot = UInt256.Zero, L2ToL2MessageRoot = UInt256.Zero,
            DACommitment = UInt256.Zero, PublicInputHash = UInt256.Zero,
            ProofType = ProofType.Optimistic,
            Proof = ReadOnlyMemory<byte>.Empty,
        };
        var inputs = new BatchExecutionRequest
        {
            ChainId = LocalChainId, BatchNumber = 1,
            PreStateRoot = preStateRoot,
            Transactions = txs,
            L1MessagesConsumed = Array.Empty<CrossChainMessage>(),
            BlockContext = Ctx(),
        };

        var orchestrator = new ChallengeOrchestrator(new HonestReplayer());
        var fraudProof = await orchestrator.InspectAsync(commitment, inputs);

        Assert.IsNull(fraudProof, "honest sequencer → no fraud proof emitted");
    }

    [TestMethod]
    public async Task FraudProofPayload_RoundTripsAfterDetection()
    {
        var txs = new ReadOnlyMemory<byte>[TxCount];
        for (var i = 0; i < TxCount; i++) txs[i] = new byte[] { (byte)(i + 0x80), 0xCC };

        var honest = BuildHonestCheckpoints(txs);
        var lying = BuildLyingCheckpoints(honest, liesFromIndex: 3);

        var commitment = new L2BatchCommitment
        {
            ChainId = LocalChainId, BatchNumber = 1,
            FirstBlock = 100, LastBlock = 200,
            PreStateRoot = honest[0],
            PostStateRoot = lying[^1],
            TxRoot = UInt256.Zero, ReceiptRoot = UInt256.Zero, WithdrawalRoot = UInt256.Zero,
            L2ToL1MessageRoot = UInt256.Zero, L2ToL2MessageRoot = UInt256.Zero,
            DACommitment = UInt256.Zero, PublicInputHash = UInt256.Zero,
            ProofType = ProofType.Optimistic,
            Proof = ReadOnlyMemory<byte>.Empty,
        };
        var inputs = new BatchExecutionRequest
        {
            ChainId = LocalChainId, BatchNumber = 1,
            PreStateRoot = honest[0],
            Transactions = txs,
            L1MessagesConsumed = Array.Empty<CrossChainMessage>(),
            BlockContext = Ctx(),
        };

        var fraudProof = await new ChallengeOrchestrator(new HonestReplayer()).InspectAsync(commitment, inputs);
        Assert.IsNotNull(fraudProof);

        var bytes = fraudProof!.Encode();
        Assert.AreEqual(FraudProofPayload.Size, bytes.Length);

        var decoded = FraudProofPayload.Decode(bytes);
        Assert.AreEqual(fraudProof.PreStateRoot, decoded.PreStateRoot);
        Assert.AreEqual(fraudProof.ClaimedPostStateRoot, decoded.ClaimedPostStateRoot);
        Assert.AreEqual(fraudProof.ReplayedPostStateRoot, decoded.ReplayedPostStateRoot);
    }
}
