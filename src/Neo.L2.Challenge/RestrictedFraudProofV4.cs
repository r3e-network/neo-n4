using System.Buffers.Binary;
using Neo.Cryptography;
using Neo.L2.Batch;
using Neo.L2.State;

namespace Neo.L2.Challenge;

/// <summary>
/// Canonical final-step transcript bound into a restricted-execution v4 fraud claim.
/// </summary>
/// <remarks>
/// See doc.md §15 and §17. The currently supported trustless profile is deliberately limited to
/// one transaction and one existing counter-state key because the batch commitment does not yet
/// contain a transaction count or an intermediate execution-trace root.
/// </remarks>
public sealed record RestrictedBisectionTranscript
{
    /// <summary>Deployment-specific replay domain.</summary>
    public required UInt256 ReplayDomain { get; init; }

    /// <summary>Identifier of the executor semantics the final step uses.</summary>
    public required UInt256 ExecutorSemanticId { get; init; }

    /// <summary>Hash256 of the canonical 321-byte committed batch header.</summary>
    public required UInt256 CommittedHeaderHash { get; init; }

    /// <summary>Disputed L2 chain.</summary>
    public required uint ChainId { get; init; }

    /// <summary>Disputed batch.</summary>
    public required ulong BatchNumber { get; init; }

    /// <summary>Final disputed transaction index.</summary>
    public required uint DisputedTxIndex { get; init; }

    /// <summary>Transaction count asserted by the settled transcript.</summary>
    public required uint TransactionCount { get; init; }

    /// <summary>Inclusive lower checkpoint bound.</summary>
    public required uint LowerBound { get; init; }

    /// <summary>Exclusive upper checkpoint bound.</summary>
    public required uint UpperBound { get; init; }

    /// <summary>Committed state root before the supported single step.</summary>
    public required UInt256 PreStateRoot { get; init; }

    /// <summary>Sequencer-committed state root after the supported single step.</summary>
    public required UInt256 CommittedPostStateRoot { get; init; }

    /// <summary>State root obtained by executing the supported single step.</summary>
    public required UInt256 ExpectedPostStateRoot { get; init; }

    /// <summary>Committed transaction Merkle root.</summary>
    public required UInt256 TxRoot { get; init; }

    internal byte[] EncodeForHash()
    {
        ArgumentNullException.ThrowIfNull(ReplayDomain);
        ArgumentNullException.ThrowIfNull(ExecutorSemanticId);
        ArgumentNullException.ThrowIfNull(CommittedHeaderHash);
        ArgumentNullException.ThrowIfNull(PreStateRoot);
        ArgumentNullException.ThrowIfNull(CommittedPostStateRoot);
        ArgumentNullException.ThrowIfNull(ExpectedPostStateRoot);
        ArgumentNullException.ThrowIfNull(TxRoot);

        var bytes = new byte[32 * 7 + 4 * 5 + 8];
        var span = bytes.AsSpan();
        var position = 0;
        WriteUInt256(span, ref position, ReplayDomain);
        WriteUInt256(span, ref position, ExecutorSemanticId);
        WriteUInt256(span, ref position, CommittedHeaderHash);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(position, 4), ChainId); position += 4;
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(position, 8), BatchNumber); position += 8;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(position, 4), DisputedTxIndex); position += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(position, 4), TransactionCount); position += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(position, 4), LowerBound); position += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(position, 4), UpperBound); position += 4;
        WriteUInt256(span, ref position, PreStateRoot);
        WriteUInt256(span, ref position, CommittedPostStateRoot);
        WriteUInt256(span, ref position, ExpectedPostStateRoot);
        WriteUInt256(span, ref position, TxRoot);
        return bytes;
    }

    private static void WriteUInt256(Span<byte> destination, ref int position, UInt256 value)
    {
        value.GetSpan().CopyTo(destination.Slice(position, 32));
        position += 32;
    }
}

/// <summary>Result of evaluating a restricted-execution v4 claim.</summary>
/// <remarks>See doc.md §15 and §17.</remarks>
public enum RestrictedFraudProofV4Verdict : byte
{
    /// <summary>The payload or one of its canonical bindings is invalid.</summary>
    Invalid = 0,

    /// <summary>The committed transition matches the supported executor semantics.</summary>
    NoFraud = 1,

    /// <summary>The committed transition differs from the supported executor semantics.</summary>
    Fraud = 2,
}

/// <summary>
/// Canonical v4 trustless restricted-execution fraud-proof payload.
/// </summary>
/// <remarks>
/// See doc.md §15 and §17. All integers are little-endian. The transaction proof is encoded with
/// <see cref="MerkleProofSerializer"/> and the state witness reuses <see cref="StorageProof"/>.
/// Versions 1-3 remain structural/governance formats and are intentionally not represented here.
/// </remarks>
public sealed record RestrictedFraudProofV4
{
    /// <summary>Wire-format version.</summary>
    public const byte Version = 4;

    /// <summary>Fixed header size through the disputed transaction length prefix.</summary>
    public const int FixedHeaderSize = 353;

    /// <summary>Maximum disputed transaction size.</summary>
    public const int MaxDisputedTxBytes = FraudProofPayload.MaxDisputedTxBytes;

    /// <summary>Offset of the replay-domain hash.</summary>
    public const int ReplayDomainOffset = 1;

    /// <summary>Offset of the executor semantic id.</summary>
    public const int ExecutorSemanticIdOffset = 33;

    /// <summary>Offset of the replay-protected claim id.</summary>
    public const int ClaimIdOffset = 65;

    /// <summary>Offset of the settled-bisection transcript hash.</summary>
    public const int TranscriptHashOffset = 97;

    /// <summary>Offset of the complete witness hash.</summary>
    public const int WitnessHashOffset = 129;

    /// <summary>Offset of the committed canonical-header hash.</summary>
    public const int CommittedHeaderHashOffset = 161;

    /// <summary>Offset of chainId.</summary>
    public const int ChainIdOffset = 193;

    /// <summary>Offset of batchNumber.</summary>
    public const int BatchNumberOffset = 197;

    /// <summary>Offset of disputedTxIndex.</summary>
    public const int DisputedTxIndexOffset = 205;

    /// <summary>Offset of transcript transactionCount.</summary>
    public const int TransactionCountOffset = 209;

    /// <summary>Offset of transcript lowerBound.</summary>
    public const int LowerBoundOffset = 213;

    /// <summary>Offset of transcript upperBound.</summary>
    public const int UpperBoundOffset = 217;

    /// <summary>Offset of the committed pre-state root.</summary>
    public const int PreStateRootOffset = 221;

    /// <summary>Offset of the committed post-state root.</summary>
    public const int CommittedPostStateRootOffset = 253;

    /// <summary>Offset of the verifier-derived expected post-state root.</summary>
    public const int ExpectedPostStateRootOffset = 285;

    /// <summary>Offset of the committed transaction root.</summary>
    public const int TxRootOffset = 317;

    /// <summary>Offset of the disputed transaction length prefix.</summary>
    public const int TxLengthOffset = 349;

    /// <summary>Bound settled-bisection transcript.</summary>
    public required RestrictedBisectionTranscript Transcript { get; init; }

    /// <summary>Hash that identifies this exact deployment, batch, transcript, and witness.</summary>
    public required UInt256 ClaimId { get; init; }

    /// <summary>Hash of the canonical final-step transcript.</summary>
    public required UInt256 TranscriptHash { get; init; }

    /// <summary>Hash of transaction bytes, transaction proof, and state witness.</summary>
    public required UInt256 WitnessHash { get; init; }

    /// <summary>Exact transaction bytes committed by <see cref="RestrictedBisectionTranscript.TxRoot"/>.</summary>
    public required ReadOnlyMemory<byte> DisputedTxBytes { get; init; }

    /// <summary>Canonical transaction inclusion proof.</summary>
    public required MerkleProof TransactionProof { get; init; }

    /// <summary>Existing-key pre/post state witness for the supported transition.</summary>
    public required StorageProof StateProof { get; init; }

    /// <summary>Semantic id of the supported Counter Increment existing-key transition.</summary>
    public static UInt256 CounterIncrementExecutorSemanticId =>
        new(Crypto.Hash256("neo4-executor:counter-increment-existing-key:v1"u8));

    /// <summary>Create a deployment-specific replay domain.</summary>
    public static UInt256 CreateReplayDomain(
        uint network,
        UInt160 settlementManager,
        UInt160 optimisticChallenge,
        UInt160 fraudVerifier)
    {
        ArgumentNullException.ThrowIfNull(settlementManager);
        ArgumentNullException.ThrowIfNull(optimisticChallenge);
        ArgumentNullException.ThrowIfNull(fraudVerifier);
        var tag = "neo4-fraud-replay-domain:v1"u8;
        var bytes = new byte[tag.Length + 4 + 20 * 3];
        tag.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(tag.Length, 4), network);
        settlementManager.GetSpan().CopyTo(bytes.AsSpan(tag.Length + 4, 20));
        optimisticChallenge.GetSpan().CopyTo(bytes.AsSpan(tag.Length + 24, 20));
        fraudVerifier.GetSpan().CopyTo(bytes.AsSpan(tag.Length + 44, 20));
        return new UInt256(Crypto.Hash256(bytes));
    }

    /// <summary>
    /// Build a canonical v4 claim for the currently supported single-transaction Counter profile.
    /// </summary>
    public static RestrictedFraudProofV4 Create(
        L2BatchCommitment commitment,
        UInt160 settlementManager,
        UInt160 fraudVerifier,
        UInt256 replayDomain,
        ReadOnlyMemory<byte> disputedTxBytes,
        MerkleProof transactionProof,
        StorageProof stateProof)
    {
        ArgumentNullException.ThrowIfNull(commitment);
        ArgumentNullException.ThrowIfNull(settlementManager);
        ArgumentNullException.ThrowIfNull(fraudVerifier);
        ArgumentNullException.ThrowIfNull(replayDomain);
        ArgumentNullException.ThrowIfNull(transactionProof);
        ArgumentNullException.ThrowIfNull(stateProof);
        if (commitment.ProofType != ProofType.Optimistic)
            throw new ArgumentException("v4 requires an optimistic commitment", nameof(commitment));

        var commitmentBytes = BatchSerializer.Encode(commitment);
        var canonicalHeader = commitmentBytes.AsSpan(0, BatchSerializer.CommitmentFixedSize).ToArray();
        var expectedPostRoot = RestrictedExecutionFraudVerifierV4.ComputeExpectedPostRoot(
            disputedTxBytes.Span,
            stateProof,
            out var derivedPreStateRoot,
            out var derivedCommittedPostStateRoot);
        if (!derivedPreStateRoot.Equals(commitment.PreStateRoot))
            throw new ArgumentException("state witness does not reconstruct commitment.PreStateRoot", nameof(stateProof));
        if (!derivedCommittedPostStateRoot.Equals(commitment.PostStateRoot))
            throw new ArgumentException("state witness does not reconstruct commitment.PostStateRoot", nameof(stateProof));

        var transcript = new RestrictedBisectionTranscript
        {
            ReplayDomain = replayDomain,
            ExecutorSemanticId = CounterIncrementExecutorSemanticId,
            CommittedHeaderHash = new UInt256(Crypto.Hash256(canonicalHeader)),
            ChainId = commitment.ChainId,
            BatchNumber = commitment.BatchNumber,
            DisputedTxIndex = 0,
            TransactionCount = 1,
            LowerBound = 0,
            UpperBound = 1,
            PreStateRoot = commitment.PreStateRoot,
            CommittedPostStateRoot = commitment.PostStateRoot,
            ExpectedPostStateRoot = expectedPostRoot,
            TxRoot = commitment.TxRoot,
        };
        var transcriptHash = ComputeTranscriptHash(transcript);
        var witnessHash = ComputeWitnessHash(disputedTxBytes.Span, transactionProof, stateProof);
        var claimId = ComputeClaimId(
            settlementManager,
            fraudVerifier,
            transcript,
            transcriptHash,
            witnessHash);

        var payload = new RestrictedFraudProofV4
        {
            Transcript = transcript,
            ClaimId = claimId,
            TranscriptHash = transcriptHash,
            WitnessHash = witnessHash,
            DisputedTxBytes = disputedTxBytes.ToArray(),
            TransactionProof = transactionProof,
            StateProof = stateProof,
        };
        if (RestrictedExecutionFraudVerifierV4.Verify(payload, canonicalHeader, settlementManager, fraudVerifier)
            == RestrictedFraudProofV4Verdict.Invalid)
            throw new ArgumentException("v4 witness is not canonical for the supplied commitment");
        return payload;
    }

    /// <summary>Encode to canonical v4 bytes.</summary>
    public byte[] Encode()
    {
        ValidateRequiredFields();
        var transactionProofBytes = MerkleProofSerializer.Encode(TransactionProof);
        var stateProofBytes = EncodeStateProof(StateProof);
        var size = checked(FixedHeaderSize + DisputedTxBytes.Length + 4 + transactionProofBytes.Length + 4 + stateProofBytes.Length);
        var bytes = new byte[size];
        var span = bytes.AsSpan();
        span[0] = Version;
        Transcript.ReplayDomain.GetSpan().CopyTo(span.Slice(ReplayDomainOffset, 32));
        Transcript.ExecutorSemanticId.GetSpan().CopyTo(span.Slice(ExecutorSemanticIdOffset, 32));
        ClaimId.GetSpan().CopyTo(span.Slice(ClaimIdOffset, 32));
        TranscriptHash.GetSpan().CopyTo(span.Slice(TranscriptHashOffset, 32));
        WitnessHash.GetSpan().CopyTo(span.Slice(WitnessHashOffset, 32));
        Transcript.CommittedHeaderHash.GetSpan().CopyTo(span.Slice(CommittedHeaderHashOffset, 32));
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(ChainIdOffset, 4), Transcript.ChainId);
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(BatchNumberOffset, 8), Transcript.BatchNumber);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(DisputedTxIndexOffset, 4), Transcript.DisputedTxIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(TransactionCountOffset, 4), Transcript.TransactionCount);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(LowerBoundOffset, 4), Transcript.LowerBound);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(UpperBoundOffset, 4), Transcript.UpperBound);
        Transcript.PreStateRoot.GetSpan().CopyTo(span.Slice(PreStateRootOffset, 32));
        Transcript.CommittedPostStateRoot.GetSpan().CopyTo(span.Slice(CommittedPostStateRootOffset, 32));
        Transcript.ExpectedPostStateRoot.GetSpan().CopyTo(span.Slice(ExpectedPostStateRootOffset, 32));
        Transcript.TxRoot.GetSpan().CopyTo(span.Slice(TxRootOffset, 32));
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(TxLengthOffset, 4), (uint)DisputedTxBytes.Length);

        var position = FixedHeaderSize;
        DisputedTxBytes.Span.CopyTo(span.Slice(position, DisputedTxBytes.Length));
        position += DisputedTxBytes.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(position, 4), (uint)transactionProofBytes.Length);
        position += 4;
        transactionProofBytes.CopyTo(span.Slice(position, transactionProofBytes.Length));
        position += transactionProofBytes.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(position, 4), (uint)stateProofBytes.Length);
        position += 4;
        stateProofBytes.CopyTo(span.Slice(position, stateProofBytes.Length));
        return bytes;
    }

    /// <summary>Decode strict canonical v4 bytes.</summary>
    public static RestrictedFraudProofV4 Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < FixedHeaderSize)
            throw new ArgumentException($"v4 payload needs at least {FixedHeaderSize} bytes", nameof(bytes));
        if (bytes[0] != Version)
            throw new InvalidDataException($"unsupported restricted fraud-proof version {bytes[0]}");

        var txLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(TxLengthOffset, 4));
        if (txLength > MaxDisputedTxBytes)
            throw new ArgumentException($"v4 disputed tx length {txLength} exceeds {MaxDisputedTxBytes}", nameof(bytes));
        var position = checked(FixedHeaderSize + (int)txLength);
        if (bytes.Length < position + 4)
            throw new ArgumentException("v4 payload truncates transaction proof length", nameof(bytes));
        var transactionProofLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(position, 4));
        position += 4;
        if (transactionProofLength > MerkleProofSerializer.HeaderSize + 32 * MerkleProofSerializer.MaxDepth)
            throw new ArgumentException("v4 transaction proof exceeds canonical depth cap", nameof(bytes));
        if (bytes.Length < position + transactionProofLength + 4)
            throw new ArgumentException("v4 payload truncates transaction proof", nameof(bytes));
        var transactionProof = MerkleProofSerializer.Decode(bytes.Slice(position, (int)transactionProofLength));
        position += (int)transactionProofLength;

        var stateProofLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(position, 4));
        position += 4;
        if (bytes.Length != position + stateProofLength)
            throw new ArgumentException("v4 state proof length or trailing bytes mismatch", nameof(bytes));
        var (stateProof, consumed) = StorageProof.Decode(bytes.Slice(position, (int)stateProofLength));
        if (consumed != stateProofLength)
            throw new ArgumentException("v4 state proof is not canonical", nameof(bytes));

        var txBytes = bytes.Slice(FixedHeaderSize, (int)txLength).ToArray();
        return new RestrictedFraudProofV4
        {
            Transcript = new RestrictedBisectionTranscript
            {
                ReplayDomain = new UInt256(bytes.Slice(ReplayDomainOffset, 32)),
                ExecutorSemanticId = new UInt256(bytes.Slice(ExecutorSemanticIdOffset, 32)),
                CommittedHeaderHash = new UInt256(bytes.Slice(CommittedHeaderHashOffset, 32)),
                ChainId = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(ChainIdOffset, 4)),
                BatchNumber = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(BatchNumberOffset, 8)),
                DisputedTxIndex = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(DisputedTxIndexOffset, 4)),
                TransactionCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(TransactionCountOffset, 4)),
                LowerBound = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(LowerBoundOffset, 4)),
                UpperBound = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(UpperBoundOffset, 4)),
                PreStateRoot = new UInt256(bytes.Slice(PreStateRootOffset, 32)),
                CommittedPostStateRoot = new UInt256(bytes.Slice(CommittedPostStateRootOffset, 32)),
                ExpectedPostStateRoot = new UInt256(bytes.Slice(ExpectedPostStateRootOffset, 32)),
                TxRoot = new UInt256(bytes.Slice(TxRootOffset, 32)),
            },
            ClaimId = new UInt256(bytes.Slice(ClaimIdOffset, 32)),
            TranscriptHash = new UInt256(bytes.Slice(TranscriptHashOffset, 32)),
            WitnessHash = new UInt256(bytes.Slice(WitnessHashOffset, 32)),
            DisputedTxBytes = txBytes,
            TransactionProof = transactionProof,
            StateProof = stateProof,
        };
    }

    internal static UInt256 ComputeTranscriptHash(RestrictedBisectionTranscript transcript)
    {
        var tag = "neo4-fraud-bisection-transcript:v1"u8;
        var body = transcript.EncodeForHash();
        var bytes = new byte[tag.Length + body.Length];
        tag.CopyTo(bytes);
        body.CopyTo(bytes, tag.Length);
        return new UInt256(Crypto.Hash256(bytes));
    }

    internal static UInt256 ComputeWitnessHash(
        ReadOnlySpan<byte> disputedTxBytes,
        MerkleProof transactionProof,
        StorageProof stateProof)
    {
        var tag = "neo4-fraud-witness:v1"u8;
        var transactionProofBytes = MerkleProofSerializer.Encode(transactionProof);
        var stateProofBytes = EncodeStateProof(stateProof);
        var bytes = new byte[tag.Length + 4 + disputedTxBytes.Length + 4 + transactionProofBytes.Length + 4 + stateProofBytes.Length];
        var span = bytes.AsSpan();
        tag.CopyTo(span);
        var position = tag.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(position, 4), (uint)disputedTxBytes.Length); position += 4;
        disputedTxBytes.CopyTo(span.Slice(position, disputedTxBytes.Length)); position += disputedTxBytes.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(position, 4), (uint)transactionProofBytes.Length); position += 4;
        transactionProofBytes.CopyTo(span.Slice(position, transactionProofBytes.Length)); position += transactionProofBytes.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(position, 4), (uint)stateProofBytes.Length); position += 4;
        stateProofBytes.CopyTo(span.Slice(position, stateProofBytes.Length));
        return new UInt256(Crypto.Hash256(bytes));
    }

    internal static UInt256 ComputeClaimId(
        UInt160 settlementManager,
        UInt160 fraudVerifier,
        RestrictedBisectionTranscript transcript,
        UInt256 transcriptHash,
        UInt256 witnessHash)
    {
        var tag = "neo4-fraud-claim:v4"u8;
        var bytes = new byte[tag.Length + 20 + 20 + 32 * 5 + 4 + 8 + 4];
        var span = bytes.AsSpan();
        tag.CopyTo(span);
        var position = tag.Length;
        settlementManager.GetSpan().CopyTo(span.Slice(position, 20)); position += 20;
        fraudVerifier.GetSpan().CopyTo(span.Slice(position, 20)); position += 20;
        transcript.ReplayDomain.GetSpan().CopyTo(span.Slice(position, 32)); position += 32;
        transcript.ExecutorSemanticId.GetSpan().CopyTo(span.Slice(position, 32)); position += 32;
        transcript.CommittedHeaderHash.GetSpan().CopyTo(span.Slice(position, 32)); position += 32;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(position, 4), transcript.ChainId); position += 4;
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(position, 8), transcript.BatchNumber); position += 8;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(position, 4), transcript.DisputedTxIndex); position += 4;
        transcriptHash.GetSpan().CopyTo(span.Slice(position, 32)); position += 32;
        witnessHash.GetSpan().CopyTo(span.Slice(position, 32));
        return new UInt256(Crypto.Hash256(bytes));
    }

    private void ValidateRequiredFields()
    {
        ArgumentNullException.ThrowIfNull(Transcript);
        ArgumentNullException.ThrowIfNull(ClaimId);
        ArgumentNullException.ThrowIfNull(TranscriptHash);
        ArgumentNullException.ThrowIfNull(WitnessHash);
        ArgumentNullException.ThrowIfNull(TransactionProof);
        ArgumentNullException.ThrowIfNull(StateProof);
        if (DisputedTxBytes.Length > MaxDisputedTxBytes)
            throw new InvalidOperationException($"DisputedTxBytes exceeds {MaxDisputedTxBytes}");
    }

    private static byte[] EncodeStateProof(StorageProof proof)
    {
        var bytes = new byte[proof.EncodedSize];
        var written = proof.Encode(bytes);
        if (written != bytes.Length)
            throw new InvalidOperationException("StorageProof encoded length mismatch");
        return bytes;
    }

    /// <inheritdoc />
    public bool Equals(RestrictedFraudProofV4? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Equals(Transcript, other.Transcript)
            && Equals(ClaimId, other.ClaimId)
            && Equals(TranscriptHash, other.TranscriptHash)
            && Equals(WitnessHash, other.WitnessHash)
            && DisputedTxBytes.Span.SequenceEqual(other.DisputedTxBytes.Span)
            && MerkleProofSerializer.Encode(TransactionProof)
                .AsSpan()
                .SequenceEqual(MerkleProofSerializer.Encode(other.TransactionProof))
            && Equals(StateProof, other.StateProof);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Transcript);
        hash.Add(ClaimId);
        hash.Add(TranscriptHash);
        hash.Add(WitnessHash);
        hash.Add(DisputedTxBytes.Length);
        hash.AddBytes(MerkleProofSerializer.Encode(TransactionProof));
        hash.Add(StateProof);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Off-chain reference implementation of the trustless restricted-execution v4 verifier.
/// </summary>
/// <remarks>
/// See doc.md §15 and §17. This mirrors the on-chain
/// <c>NeoHub.RestrictedExecutionFraudVerifier</c> decision boundary.
/// </remarks>
public static class RestrictedExecutionFraudVerifierV4
{
    private const int HeaderChainIdOffset = 0;
    private const int HeaderBatchNumberOffset = 4;
    private const int HeaderPreStateRootOffset = 28;
    private const int HeaderPostStateRootOffset = 60;
    private const int HeaderTxRootOffset = 92;
    private const int HeaderProofTypeOffset = 316;
    private const byte OptimisticProofType = 2;
    private const byte IncrementCounterOpcode = 1;

    /// <summary>Evaluate a v4 payload against a canonical SettlementManager batch header.</summary>
    public static RestrictedFraudProofV4Verdict Verify(
        RestrictedFraudProofV4 payload,
        ReadOnlySpan<byte> committedHeader,
        UInt160 settlementManager,
        UInt160 fraudVerifier)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(payload);
            ArgumentNullException.ThrowIfNull(settlementManager);
            ArgumentNullException.ThrowIfNull(fraudVerifier);
            if (committedHeader.Length != BatchSerializer.CommitmentFixedSize)
                return RestrictedFraudProofV4Verdict.Invalid;

            var transcript = payload.Transcript;
            if (transcript.ChainId == 0
                || transcript.ReplayDomain.Equals(UInt256.Zero)
                || transcript.DisputedTxIndex != 0
                || transcript.TransactionCount != 1
                || transcript.LowerBound != 0
                || transcript.UpperBound != 1
                || !transcript.ExecutorSemanticId.Equals(RestrictedFraudProofV4.CounterIncrementExecutorSemanticId))
                return RestrictedFraudProofV4Verdict.Invalid;

            if (BinaryPrimitives.ReadUInt32LittleEndian(committedHeader.Slice(HeaderChainIdOffset, 4)) != transcript.ChainId
                || BinaryPrimitives.ReadUInt64LittleEndian(committedHeader.Slice(HeaderBatchNumberOffset, 8)) != transcript.BatchNumber
                || committedHeader[HeaderProofTypeOffset] != OptimisticProofType)
                return RestrictedFraudProofV4Verdict.Invalid;

            var headerHash = new UInt256(Crypto.Hash256(committedHeader));
            if (!headerHash.Equals(transcript.CommittedHeaderHash)
                || !committedHeader.Slice(HeaderPreStateRootOffset, 32).SequenceEqual(transcript.PreStateRoot.GetSpan())
                || !committedHeader.Slice(HeaderPostStateRootOffset, 32).SequenceEqual(transcript.CommittedPostStateRoot.GetSpan())
                || !committedHeader.Slice(HeaderTxRootOffset, 32).SequenceEqual(transcript.TxRoot.GetSpan()))
                return RestrictedFraudProofV4Verdict.Invalid;

            if (!RestrictedFraudProofV4.ComputeTranscriptHash(transcript).Equals(payload.TranscriptHash)
                || !RestrictedFraudProofV4.ComputeWitnessHash(
                    payload.DisputedTxBytes.Span,
                    payload.TransactionProof,
                    payload.StateProof).Equals(payload.WitnessHash)
                || !RestrictedFraudProofV4.ComputeClaimId(
                    settlementManager,
                    fraudVerifier,
                    transcript,
                    payload.TranscriptHash,
                    payload.WitnessHash).Equals(payload.ClaimId))
                return RestrictedFraudProofV4Verdict.Invalid;

            var txHash = new UInt256(Crypto.Hash256(payload.DisputedTxBytes.Span));
            if (payload.TransactionProof.LeafIndex != 0
                || payload.TransactionProof.PathBitmap != 0
                || payload.TransactionProof.Siblings.Count != 0
                || !payload.TransactionProof.Leaf.Equals(txHash)
                || !payload.TransactionProof.Verify(transcript.TxRoot))
                return RestrictedFraudProofV4Verdict.Invalid;

            var expectedPostRoot = ComputeExpectedPostRoot(
                payload.DisputedTxBytes.Span,
                payload.StateProof,
                out var derivedPreStateRoot,
                out var derivedCommittedPostStateRoot);
            if (!derivedPreStateRoot.Equals(transcript.PreStateRoot)
                || !derivedCommittedPostStateRoot.Equals(transcript.CommittedPostStateRoot)
                || !expectedPostRoot.Equals(transcript.ExpectedPostStateRoot))
                return RestrictedFraudProofV4Verdict.Invalid;

            return expectedPostRoot.Equals(transcript.CommittedPostStateRoot)
                ? RestrictedFraudProofV4Verdict.NoFraud
                : RestrictedFraudProofV4Verdict.Fraud;
        }
        catch (Exception)
        {
            return RestrictedFraudProofV4Verdict.Invalid;
        }
    }

    internal static UInt256 ComputeExpectedPostRoot(
        ReadOnlySpan<byte> disputedTxBytes,
        StorageProof stateProof,
        out UInt256 derivedPreStateRoot,
        out UInt256 derivedCommittedPostStateRoot)
    {
        ArgumentNullException.ThrowIfNull(stateProof);
        if (disputedTxBytes.Length != 29 || disputedTxBytes[0] != IncrementCounterOpcode)
            throw new InvalidDataException("unsupported executor transition");
        if (stateProof.PreValue.Length != 8 || stateProof.PostValue.Length != 8)
            throw new InvalidDataException("counter state values must be 8 bytes");
        if (stateProof.PreSiblings.Count > StorageProof.MaxSiblingDepth
            || stateProof.PostSiblings.Count > StorageProof.MaxSiblingDepth
            || !IsCanonicalLeafIndex(stateProof.LeafIndex, stateProof.PreSiblings.Count)
            || !IsCanonicalLeafIndex(stateProof.LeafIndex, stateProof.PostSiblings.Count))
            throw new InvalidDataException("storage proof leaf index is outside its Merkle path");

        var expectedKey = new byte[8 + 20];
        "counter:"u8.CopyTo(expectedKey);
        disputedTxBytes.Slice(1, 20).CopyTo(expectedKey.AsSpan(8, 20));
        if (!stateProof.Key.Span.SequenceEqual(expectedKey))
            throw new InvalidDataException("state key does not match transaction sender");

        var previous = BinaryPrimitives.ReadUInt64LittleEndian(stateProof.PreValue.Span);
        var amount = BinaryPrimitives.ReadUInt64LittleEndian(disputedTxBytes.Slice(21, 8));
        var expectedValue = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(expectedValue, unchecked(previous + amount));

        derivedPreStateRoot = Fold(
            KeyedStateMerkleTree.HashLeaf(expectedKey, stateProof.PreValue.ToArray()),
            stateProof.PreSiblings,
            stateProof.LeafIndex);
        derivedCommittedPostStateRoot = Fold(
            KeyedStateMerkleTree.HashLeaf(expectedKey, stateProof.PostValue.ToArray()),
            stateProof.PostSiblings,
            stateProof.LeafIndex);
        var expectedPostStateRoot = Fold(
            KeyedStateMerkleTree.HashLeaf(expectedKey, expectedValue),
            stateProof.PreSiblings,
            stateProof.LeafIndex);
        return expectedPostStateRoot;
    }

    private static bool IsCanonicalLeafIndex(ulong leafIndex, int siblingCount) =>
        siblingCount == 64 || (leafIndex >> siblingCount) == 0;

    private static UInt256 Fold(UInt256 leaf, IReadOnlyList<UInt256> siblings, ulong leafIndex)
    {
        var current = leaf;
        var index = leafIndex;
        var bytes = new byte[64];
        foreach (var sibling in siblings)
        {
            ArgumentNullException.ThrowIfNull(sibling);
            if ((index & 1UL) == 0UL)
            {
                current.GetSpan().CopyTo(bytes.AsSpan());
                sibling.GetSpan().CopyTo(bytes.AsSpan(32));
            }
            else
            {
                sibling.GetSpan().CopyTo(bytes.AsSpan());
                current.GetSpan().CopyTo(bytes.AsSpan(32));
            }
            current = new UInt256(Crypto.Hash256(bytes));
            index >>= 1;
        }
        return current;
    }
}
