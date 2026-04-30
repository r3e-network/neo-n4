namespace Neo.L2.Challenge.UnitTests;

[TestClass]
public class UT_Challenge
{
    private static UInt256 H(char c)
    {
        var bytes = new byte[32];
        for (var i = 0; i < 32; i++) bytes[i] = (byte)c;
        return new UInt256(bytes);
    }

    private static BatchExecutionRequest MkRequest(uint chainId, ulong batchNumber, UInt256 preStateRoot) => new()
    {
        ChainId = chainId,
        BatchNumber = batchNumber,
        PreStateRoot = preStateRoot,
        Transactions = new ReadOnlyMemory<byte>[] { new byte[] { 0x01 } },
        L1MessagesConsumed = Array.Empty<CrossChainMessage>(),
        BlockContext = new BatchBlockContext
        {
            L1FinalizedHeight = 1,
            FirstBlockTimestamp = 1_700_000_000_000,
            LastBlockTimestamp = 1_700_000_005_000,
            SequencerCommitteeHash = UInt256.Zero,
            Network = 0x4F454E,
        },
    };

    private static L2BatchCommitment MkCommitment(uint chainId, ulong batchNumber, UInt256 preStateRoot, UInt256 claimedPost) => new()
    {
        ChainId = chainId,
        BatchNumber = batchNumber,
        FirstBlock = 100,
        LastBlock = 200,
        PreStateRoot = preStateRoot,
        PostStateRoot = claimedPost,
        TxRoot = UInt256.Zero,
        ReceiptRoot = UInt256.Zero,
        WithdrawalRoot = UInt256.Zero,
        L2ToL1MessageRoot = UInt256.Zero,
        L2ToL2MessageRoot = UInt256.Zero,
        DACommitment = UInt256.Zero,
        PublicInputHash = UInt256.Zero,
        ProofType = ProofType.Optimistic,
        Proof = ReadOnlyMemory<byte>.Empty,
    };

    private sealed class FixedReplayer : IFraudProofGenerator
    {
        public required UInt256 ReplayedRoot { get; init; }
        public ValueTask<UInt256> ReplayAsync(BatchExecutionRequest request, CancellationToken cancellationToken = default)
            => new ValueTask<UInt256>(ReplayedRoot);
    }

    [TestMethod]
    public void Payload_RoundTrips()
    {
        var p = new FraudProofPayload
        {
            PreStateRoot = H('a'),
            ClaimedPostStateRoot = H('b'),
            ReplayedPostStateRoot = H('c'),
            DisputedTxIndex = 7,
        };
        var bytes = p.Encode();
        Assert.AreEqual(FraudProofPayload.Size, bytes.Length);

        var decoded = FraudProofPayload.Decode(bytes);
        Assert.AreEqual(p.PreStateRoot, decoded.PreStateRoot);
        Assert.AreEqual(p.ClaimedPostStateRoot, decoded.ClaimedPostStateRoot);
        Assert.AreEqual(p.ReplayedPostStateRoot, decoded.ReplayedPostStateRoot);
        Assert.AreEqual(p.DisputedTxIndex, decoded.DisputedTxIndex);
    }

    [TestMethod]
    public void Payload_RejectsWrongSize()
    {
        Assert.ThrowsExactly<ArgumentException>(() => FraudProofPayload.Decode(new byte[42]));
    }

    [TestMethod]
    public void Payload_RejectsWrongVersion()
    {
        var bytes = new byte[FraudProofPayload.Size];
        bytes[0] = 99;
        Assert.ThrowsExactly<InvalidDataException>(() => FraudProofPayload.Decode(bytes));
    }

    [TestMethod]
    public async Task Inspect_NullWhenReplayMatches()
    {
        var pre = H('p');
        var post = H('q');
        var replayer = new FixedReplayer { ReplayedRoot = post };
        var orchestrator = new ChallengeOrchestrator(replayer);

        var commitment = MkCommitment(1001, 1, pre, post);
        var inputs = MkRequest(1001, 1, pre);

        var result = await orchestrator.InspectAsync(commitment, inputs);
        Assert.IsNull(result, "no fraud proof when replayed root matches claim");
    }

    [TestMethod]
    public async Task Inspect_EmitsProofWhenReplayDisagrees()
    {
        var pre = H('p');
        var sequencerClaim = H('q');
        var truth = H('r');
        var replayer = new FixedReplayer { ReplayedRoot = truth };
        var orchestrator = new ChallengeOrchestrator(replayer);

        var commitment = MkCommitment(1001, 1, pre, sequencerClaim);
        var inputs = MkRequest(1001, 1, pre);

        var result = await orchestrator.InspectAsync(commitment, inputs);
        Assert.IsNotNull(result);
        Assert.AreEqual(pre, result!.PreStateRoot);
        Assert.AreEqual(sequencerClaim, result.ClaimedPostStateRoot);
        Assert.AreEqual(truth, result.ReplayedPostStateRoot);
    }

    [TestMethod]
    public async Task Inspect_RejectsMismatchedChainId()
    {
        var orchestrator = new ChallengeOrchestrator(new FixedReplayer { ReplayedRoot = UInt256.Zero });
        var commitment = MkCommitment(1001, 1, H('a'), H('b'));
        var inputs = MkRequest(2002, 1, H('a'));
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () => await orchestrator.InspectAsync(commitment, inputs));
    }

    [TestMethod]
    public async Task Inspect_RejectsMismatchedBatchNumber()
    {
        var orchestrator = new ChallengeOrchestrator(new FixedReplayer { ReplayedRoot = UInt256.Zero });
        var commitment = MkCommitment(1001, 1, H('a'), H('b'));
        var inputs = MkRequest(1001, 99, H('a'));
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () => await orchestrator.InspectAsync(commitment, inputs));
    }

    [TestMethod]
    public async Task Inspect_RejectsMismatchedPreStateRoot()
    {
        var orchestrator = new ChallengeOrchestrator(new FixedReplayer { ReplayedRoot = UInt256.Zero });
        var commitment = MkCommitment(1001, 1, H('a'), H('b'));
        var inputs = MkRequest(1001, 1, H('z'));
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () => await orchestrator.InspectAsync(commitment, inputs));
    }
}
