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
        // v1 with wrong length: ArgumentException ("v1 payload must be exactly 101 bytes").
        var badV1 = new byte[42];
        badV1[0] = FraudProofPayload.Version;  // = 1
        Assert.ThrowsExactly<ArgumentException>(() => FraudProofPayload.Decode(badV1));

        // v2 with truncated header: ArgumentException ("v2 payload must be ≥ 105 bytes").
        var truncatedV2 = new byte[42];
        truncatedV2[0] = FraudProofPayload.Version2;  // = 2
        Assert.ThrowsExactly<ArgumentException>(() => FraudProofPayload.Decode(truncatedV2));
    }

    [TestMethod]
    public void Payload_RejectsUnknownVersion()
    {
        // Unknown version byte → InvalidDataException, regardless of length.
        var unknown = new byte[101];
        unknown[0] = 99;
        Assert.ThrowsExactly<InvalidDataException>(() => FraudProofPayload.Decode(unknown));
    }

    [TestMethod]
    public void Payload_RejectsEmpty()
    {
        // Zero-length buffer can't even read the version byte → ArgumentException.
        Assert.ThrowsExactly<ArgumentException>(() => FraudProofPayload.Decode(ReadOnlySpan<byte>.Empty));
    }

    [TestMethod]
    public void Payload_V2_RoundTrips()
    {
        // v2 carries disputed-tx witness bytes — encode produces 105 + N, decode
        // recovers the witness exactly.
        var tx = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE };
        var p = new FraudProofPayload
        {
            PreStateRoot = H('a'),
            ClaimedPostStateRoot = H('b'),
            ReplayedPostStateRoot = H('c'),
            DisputedTxIndex = 7,
            DisputedTxBytes = tx,
        };
        var bytes = p.Encode();
        Assert.AreEqual(FraudProofPayload.V2HeaderSize + tx.Length, bytes.Length,
            "v2 with 5-byte tx → 105 + 5 = 110 bytes");
        Assert.AreEqual(FraudProofPayload.Version2, bytes[0]);

        var decoded = FraudProofPayload.Decode(bytes);
        Assert.AreEqual(p.PreStateRoot, decoded.PreStateRoot);
        Assert.AreEqual(p.ClaimedPostStateRoot, decoded.ClaimedPostStateRoot);
        Assert.AreEqual(p.ReplayedPostStateRoot, decoded.ReplayedPostStateRoot);
        Assert.AreEqual(p.DisputedTxIndex, decoded.DisputedTxIndex);
        CollectionAssert.AreEqual(tx, decoded.DisputedTxBytes.ToArray());
        Assert.AreEqual(p, decoded, "FraudProofPayload Equals must compare witness bytes");
    }

    [TestMethod]
    public void Payload_V1_BackwardsCompat_EmptyWitnessOnDecode()
    {
        // v1-encoded bytes (no witness in the source) decode into a record with empty
        // DisputedTxBytes. Pin so a future change can't accidentally make v1 bytes look
        // like they had a witness.
        var p = new FraudProofPayload
        {
            PreStateRoot = H('a'),
            ClaimedPostStateRoot = H('b'),
            ReplayedPostStateRoot = H('c'),
            DisputedTxIndex = 5,
            // DisputedTxBytes deliberately omitted — defaults to ReadOnlyMemory<byte>.Empty.
        };
        var bytes = p.Encode();
        Assert.AreEqual(FraudProofPayload.Size, bytes.Length, "no witness → v1 size (101)");
        Assert.AreEqual(FraudProofPayload.Version, bytes[0]);

        var decoded = FraudProofPayload.Decode(bytes);
        Assert.IsTrue(decoded.DisputedTxBytes.IsEmpty,
            "v1 decode must produce empty witness, not garbage");
        Assert.AreEqual(p, decoded);
    }

    [TestMethod]
    public void Payload_V2_RejectsCapsViolation()
    {
        // Encoding > 64KB of witness bytes is refused — without the cap, a malicious
        // challenger could submit arbitrarily large payloads.
        var huge = new byte[FraudProofPayload.MaxDisputedTxBytes + 1];
        var p = new FraudProofPayload
        {
            PreStateRoot = H('a'),
            ClaimedPostStateRoot = H('b'),
            ReplayedPostStateRoot = H('c'),
            DisputedTxIndex = 0,
            DisputedTxBytes = huge,
        };
        Assert.ThrowsExactly<InvalidOperationException>(() => p.Encode());
    }

    [TestMethod]
    public void Payload_V2_DecodeRejectsTruncatedTrailer()
    {
        // v2 with declared len = 10 but only 5 bytes of witness data → reject.
        // Without strict length enforcement, a fraudulent payload could include a
        // partial witness and be silently accepted with whatever was at the bounds.
        var p = new FraudProofPayload
        {
            PreStateRoot = H('a'),
            ClaimedPostStateRoot = H('b'),
            ReplayedPostStateRoot = H('c'),
            DisputedTxIndex = 0,
            DisputedTxBytes = new byte[] { 1, 2, 3, 4, 5 },
        };
        var bytes = p.Encode();
        // Declared len is 5 (= bytes[101..104] LE). Trim the actual payload to 105+3.
        var truncated = bytes.AsSpan(0, FraudProofPayload.V2HeaderSize + 3).ToArray();
        Assert.ThrowsExactly<ArgumentException>(() => FraudProofPayload.Decode(truncated));
    }

    [TestMethod]
    public void Payload_V2_DecodeRejectsExtraTrailingBytes()
    {
        // v2 with extra bytes after the declared witness → reject. Pin strict length.
        var p = new FraudProofPayload
        {
            PreStateRoot = H('a'),
            ClaimedPostStateRoot = H('b'),
            ReplayedPostStateRoot = H('c'),
            DisputedTxIndex = 0,
            DisputedTxBytes = new byte[] { 1, 2, 3 },
        };
        var bytes = p.Encode();
        var withExtra = new byte[bytes.Length + 5];
        bytes.CopyTo(withExtra, 0);
        Assert.ThrowsExactly<ArgumentException>(() => FraudProofPayload.Decode(withExtra));
    }

    [TestMethod]
    public void Payload_V2_DecodeRejectsOversizedDeclaredLen()
    {
        // Construct a v2 buffer with declaredLen > MaxDisputedTxBytes; reject before
        // attempting to allocate / read.
        var bytes = new byte[FraudProofPayload.V2HeaderSize + 10];
        bytes[0] = FraudProofPayload.Version2;
        // Write a length that would claim > 64KB.
        var oversized = (uint)FraudProofPayload.MaxDisputedTxBytes + 1;
        bytes[101] = (byte)oversized;
        bytes[102] = (byte)(oversized >> 8);
        bytes[103] = (byte)(oversized >> 16);
        bytes[104] = (byte)(oversized >> 24);
        Assert.ThrowsExactly<ArgumentException>(() => FraudProofPayload.Decode(bytes));
    }

    [TestMethod]
    public void Payload_EncodedSize_VariesByVersion()
    {
        var noWitness = new FraudProofPayload
        {
            PreStateRoot = H('a'),
            ClaimedPostStateRoot = H('b'),
            ReplayedPostStateRoot = H('c'),
            DisputedTxIndex = 0,
        };
        Assert.AreEqual(FraudProofPayload.Size, noWitness.EncodedSize);

        var withWitness = noWitness with { DisputedTxBytes = new byte[10] };
        Assert.AreEqual(FraudProofPayload.V2HeaderSize + 10, withWitness.EncodedSize);
    }

    [TestMethod]
    public void Payload_Size_Is_101_Bytes_OnChainContractConstant()
    {
        // The on-chain NeoHub.GovernanceFraudVerifier hardcodes
        // FraudProofPayloadSize = 101 (= 1 version + 32 + 32 + 32 + 4 disputed-tx-index).
        // If this off-chain Size value drifts, the on-chain decoder rejects every payload
        // with "bad length" — a silent break of the optimistic-challenge fraud-proof game
        // that surfaces only when an operator submits a real fraud proof. Pin the value
        // explicitly so a refactor that changes the layout has to also update the
        // on-chain constant in lockstep.
        Assert.AreEqual(101, FraudProofPayload.Size);
    }

    [TestMethod]
    public void Payload_Version_Is_1_OnChainContractConstant()
    {
        // Same parity contract as Payload_Size_Is_101_Bytes_OnChainContractConstant.
        // The on-chain verifier hardcodes SupportedVersion = 1; bumping the off-chain
        // version without bumping the on-chain SupportedVersion would have every fraud
        // proof rejected "bad version".
        Assert.AreEqual((byte)1, FraudProofPayload.Version);
    }

    [TestMethod]
    public void Payload_RejectsWrongVersion()
    {
        var bytes = new byte[FraudProofPayload.Size];
        bytes[0] = 99;
        Assert.ThrowsExactly<InvalidDataException>(() => FraudProofPayload.Decode(bytes));
    }

    [TestMethod]
    public void Payload_ByteLayout_MatchesDocumentedOffsets()
    {
        // Pins the byte layout claimed in FraudProofPayload's XML docs. If anyone reorders
        // fields in Encode, this fails — the layout is part of the off-chain ↔ on-chain
        // contract.
        var pre = H('p'); var claimed = H('c'); var replayed = H('r');
        var p = new FraudProofPayload
        {
            PreStateRoot = pre,
            ClaimedPostStateRoot = claimed,
            ReplayedPostStateRoot = replayed,
            DisputedTxIndex = 0x12345678,
        };
        var bytes = p.Encode();

        Assert.AreEqual(101, bytes.Length, "documented total size");
        Assert.AreEqual(FraudProofPayload.Version, bytes[0], "byte 0 = version");
        CollectionAssert.AreEqual(pre.GetSpan().ToArray(), bytes[1..33], "bytes 1..33 = preStateRoot");
        CollectionAssert.AreEqual(claimed.GetSpan().ToArray(), bytes[33..65], "bytes 33..65 = claimedPostStateRoot");
        CollectionAssert.AreEqual(replayed.GetSpan().ToArray(), bytes[65..97], "bytes 65..97 = replayedPostStateRoot");
        Assert.AreEqual(
            0x12345678u,
            System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(97, 4)),
            "bytes 97..101 = disputedTxIndex (LE uint32)");
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

    [TestMethod]
    public async Task Inspect_RejectsBuggyReplayerReturningNull()
    {
        // Regression for iter 171: a buggy IFraudProofGenerator that returns null UInt256
        // would NRE inside replayedRoot.Equals() with no link to the replayer's contract
        // violation. Now surfaced as InvalidOperationException with the contract name.
        var orchestrator = new ChallengeOrchestrator(new NullReturningReplayer());
        var commitment = MkCommitment(1001, 1, H('a'), H('b'));
        var inputs = MkRequest(1001, 1, H('a'));
        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await orchestrator.InspectAsync(commitment, inputs));
        StringAssert.Contains(ex.Message, "ReplayAsync");
    }

    [TestMethod]
    public void FraudProofPayload_Encode_RejectsNullPreStateRoot()
    {
        // Pin FraudProofPayload.cs:50. UInt256 reference-typed; same iter-154+
        // hashing-primitive defense pattern as the L2BatchCommitment Encode pins.
        var bad = new FraudProofPayload
        {
            PreStateRoot = null!, ClaimedPostStateRoot = UInt256.Zero,
            ReplayedPostStateRoot = UInt256.Zero, DisputedTxIndex = 0,
        };
        Assert.ThrowsExactly<ArgumentNullException>(() => bad.Encode());
    }

    [TestMethod]
    public void FraudProofPayload_Encode_RejectsNullClaimedPostStateRoot()
    {
        var bad = new FraudProofPayload
        {
            PreStateRoot = UInt256.Zero, ClaimedPostStateRoot = null!,
            ReplayedPostStateRoot = UInt256.Zero, DisputedTxIndex = 0,
        };
        Assert.ThrowsExactly<ArgumentNullException>(() => bad.Encode());
    }

    [TestMethod]
    public void FraudProofPayload_Encode_RejectsNullReplayedPostStateRoot()
    {
        var bad = new FraudProofPayload
        {
            PreStateRoot = UInt256.Zero, ClaimedPostStateRoot = UInt256.Zero,
            ReplayedPostStateRoot = null!, DisputedTxIndex = 0,
        };
        Assert.ThrowsExactly<ArgumentNullException>(() => bad.Encode());
    }

    [TestMethod]
    public void ChallengeOrchestrator_Constructor_RejectsNullReplayer()
        => Assert.ThrowsExactly<ArgumentNullException>(() => new ChallengeOrchestrator(null!));

    [TestMethod]
    public async Task Inspect_RejectsNullCommitment()
    {
        // Pin ChallengeOrchestrator.cs:36.
        var orchestrator = new ChallengeOrchestrator(new FixedReplayer { ReplayedRoot = UInt256.Zero });
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await orchestrator.InspectAsync(null!, MkRequest(1001, 1, H('a'))));
    }

    [TestMethod]
    public async Task Inspect_RejectsNullInputs()
    {
        // Pin ChallengeOrchestrator.cs:37.
        var orchestrator = new ChallengeOrchestrator(new FixedReplayer { ReplayedRoot = UInt256.Zero });
        var commitment = MkCommitment(1001, 1, H('a'), H('b'));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await orchestrator.InspectAsync(commitment, null!));
    }

    [TestMethod]
    public async Task Inspect_RejectsNullPostStateRootInCommitment()
    {
        // Pin ChallengeOrchestrator.cs:42. PostStateRoot is referenced in the fraud-proof
        // construction below the early null-checks; without the guard it NREs deeper.
        var orchestrator = new ChallengeOrchestrator(new FixedReplayer { ReplayedRoot = UInt256.Zero });
        var commitment = MkCommitment(1001, 1, H('a'), null!);
        var inputs = MkRequest(1001, 1, H('a'));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await orchestrator.InspectAsync(commitment, inputs));
    }

    [TestMethod]
    public async Task Inspect_RejectsNullPreStateRootInInputs()
    {
        // Pin ChallengeOrchestrator.cs:43.
        var orchestrator = new ChallengeOrchestrator(new FixedReplayer { ReplayedRoot = UInt256.Zero });
        var commitment = MkCommitment(1001, 1, H('a'), H('b'));
        var inputs = MkRequest(1001, 1, null!);
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await orchestrator.InspectAsync(commitment, inputs));
    }

    [TestMethod]
    public async Task Inspect_RejectsNullPreStateRootInCommitment()
    {
        // Regression for iter 171: a null UInt256 PreStateRoot would NRE inside
        // PreStateRoot.Equals(...). Same iter-156 hashing-primitive defense pattern.
        var orchestrator = new ChallengeOrchestrator(new FixedReplayer { ReplayedRoot = UInt256.Zero });
        var commitment = MkCommitment(1001, 1, null!, H('b'));
        var inputs = MkRequest(1001, 1, H('a'));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await orchestrator.InspectAsync(commitment, inputs));
    }

    private sealed class NullReturningReplayer : IFraudProofGenerator
    {
        public ValueTask<UInt256> ReplayAsync(BatchExecutionRequest request, CancellationToken cancellationToken = default)
            => new ValueTask<UInt256>((UInt256)null!);
    }
}
