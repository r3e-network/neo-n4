using Neo.L2.Batch;

namespace Neo.L2.Persistence;

/// <summary>Durable L1 submission state for a proof manifest.</summary>
/// <remarks>See doc.md §7.5 and §15.1. Broadcasting is not settlement observation.</remarks>
public enum ProofSubmissionState : byte
{
    /// <summary>The proof is durable but no submission transaction is durably known.</summary>
    ProofReady = 0,

    /// <summary>A non-zero submission transaction hash is durably persisted.</summary>
    Submitted = 1,

    /// <summary>The batch is visible through the settlement contract lifecycle.</summary>
    SettlementObserved = 2,
}

/// <summary>Durable recovery state for one canonical settlement artifact.</summary>
/// <remarks>See doc.md §7.5, §15.1, and §17. Values are persisted; do not renumber.</remarks>
public enum SettlementRecoveryState : byte
{
    /// <summary>The artifact remains eligible for another bounded reconciliation attempt.</summary>
    Retrying = 1,

    /// <summary>Automatic retries are exhausted and explicit operator recovery is required.</summary>
    Poisoned = 2,
}

/// <summary>Durable failure and quarantine checkpoint for one settlement artifact.</summary>
/// <remarks>See doc.md §7.5, §15.1, and §17.</remarks>
public sealed record SettlementRecoveryCheckpoint
{
    /// <summary>L2 chain identifier of the blocked artifact.</summary>
    public required uint ChainId { get; init; }

    /// <summary>Canonical batch number of the blocked artifact.</summary>
    public required ulong BatchNumber { get; init; }

    /// <summary>Content hash of the immutable witness artifact.</summary>
    public required UInt256 ArtifactContentHash { get; init; }

    /// <summary>Current retry or poison state.</summary>
    public required SettlementRecoveryState State { get; init; }

    /// <summary>Consecutive failed reconciliation attempts since the last operator reset.</summary>
    public required int RetryCount { get; init; }

    /// <summary>UTC Unix milliseconds of the first failure in the current retry sequence.</summary>
    public required long FirstFailureAtUnixMilliseconds { get; init; }

    /// <summary>UTC Unix milliseconds of the most recent failure.</summary>
    public required long LastFailureAtUnixMilliseconds { get; init; }

    /// <summary>Bounded operator-visible error from the most recent failure.</summary>
    public string? LastError { get; init; }
}

/// <summary>Durable cross-store recovery intent for a reverted settlement tail.</summary>
/// <remarks>
/// See doc.md §7.3, §7.5, §15.1, and §17. The witness store atomically quarantines the
/// reverted artifact and every descendant before this checkpoint becomes visible. Execution
/// state must be restored to <see cref="TargetStateRoot"/> before the checkpoint is cleared.
/// </remarks>
public sealed record SettlementRollbackCheckpoint
{
    /// <summary>L2 chain identifier.</summary>
    public required uint ChainId { get; init; }

    /// <summary>First quarantined batch, whose pre-state becomes canonical again.</summary>
    public required ulong FirstBatchNumber { get; init; }

    /// <summary>Last quarantined speculative descendant.</summary>
    public required ulong LastBatchNumber { get; init; }

    /// <summary>Content hash of the first reverted artifact retained in quarantine.</summary>
    public required UInt256 RevertedArtifactContentHash { get; init; }

    /// <summary>Execution root expected before rollback, from the final quarantined descendant.</summary>
    public required UInt256 ExpectedCurrentStateRoot { get; init; }

    /// <summary>Authenticated pre-state root restored from the first reverted artifact.</summary>
    public required UInt256 TargetStateRoot { get; init; }
}

/// <summary>Durable proof-generation state associated with one witness artifact.</summary>
/// <remarks>
/// See doc.md §7.5 and §8. The manifest is written only after its referenced witness artifact
/// has been committed and validated.
/// </remarks>
public sealed record ProofResultManifest
{
    /// <summary>Wire-format version.</summary>
    public const ushort Version = 3;

    /// <summary>Settlement proof type produced for the artifact.</summary>
    public required ProofType ProofType { get; init; }

    /// <summary>L2 chain identifier of the committed artifact.</summary>
    public required uint ChainId { get; init; }

    /// <summary>Batch sequence number of the committed artifact.</summary>
    public required ulong BatchNumber { get; init; }

    /// <summary>Content hash of the immutable witness artifact.</summary>
    public required UInt256 ArtifactContentHash { get; init; }

    /// <summary>Hash256 of the artifact's canonical 332-byte public inputs.</summary>
    public required UInt256 PublicInputHash { get; init; }

    /// <summary>Verification-key identifier used to generate the proof.</summary>
    public required UInt256 VerificationKeyId { get; init; }

    /// <summary>Proof backend that generated the proof.</summary>
    public required WitnessProofSystem ProofSystem { get; init; }

    /// <summary>Exact execution semantic proved.</summary>
    public required UInt256 ExecutionSemanticId { get; init; }

    /// <summary>Terminal proof bytes.</summary>
    public required ReadOnlyMemory<byte> Proof { get; init; }

    /// <summary>Public values emitted by the zkVM, when the backend exposes them separately.</summary>
    public required ReadOnlyMemory<byte> PublicValues { get; init; }

    /// <summary>Durable proof, transaction, and settlement-observation state.</summary>
    public required ProofSubmissionState SubmissionState { get; init; }

    /// <summary>True only after a non-zero submission transaction hash is persisted.</summary>
    public bool Submitted => SubmissionState != ProofSubmissionState.ProofReady
        && L1TransactionHash is not null;

    /// <summary>True only after reconciliation observes the batch through settlement state.</summary>
    public bool SettlementObserved => SubmissionState == ProofSubmissionState.SettlementObserved;

    /// <summary>True only after L1 reports the batch as canonical and finalized.</summary>
    public required bool SettlementFinalized { get; init; }

    /// <summary>
    /// True only after final settlement and confirmed L1 consumption of every forced nonce.
    /// </summary>
    public bool ForcedInclusionFinalized { get; init; }

    /// <summary>L1 submission transaction, or <c>null</c> until submitted.</summary>
    public UInt256? L1TransactionHash { get; init; }

    /// <inheritdoc />
    public bool Equals(ProofResultManifest? other)
        => other is not null
            && ChainId == other.ChainId
            && BatchNumber == other.BatchNumber
            && ArtifactContentHash.Equals(other.ArtifactContentHash)
            && PublicInputHash.Equals(other.PublicInputHash)
            && ProofType == other.ProofType
            && VerificationKeyId.Equals(other.VerificationKeyId)
            && ProofSystem == other.ProofSystem
            && ExecutionSemanticId.Equals(other.ExecutionSemanticId)
            && Proof.Span.SequenceEqual(other.Proof.Span)
            && PublicValues.Span.SequenceEqual(other.PublicValues.Span)
            && SubmissionState == other.SubmissionState
            && SettlementFinalized == other.SettlementFinalized
            && ForcedInclusionFinalized == other.ForcedInclusionFinalized
            && Equals(L1TransactionHash, other.L1TransactionHash);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ChainId);
        hash.Add(BatchNumber);
        hash.Add(ArtifactContentHash);
        hash.Add(PublicInputHash);
        hash.Add(ProofType);
        hash.Add(VerificationKeyId);
        hash.Add(ProofSystem);
        hash.Add(ExecutionSemanticId);
        hash.AddBytes(Proof.Span);
        hash.AddBytes(PublicValues.Span);
        hash.Add(SubmissionState);
        hash.Add(SettlementFinalized);
        hash.Add(ForcedInclusionFinalized);
        hash.Add(L1TransactionHash);
        return hash.ToHashCode();
    }
}

/// <summary>Persistence boundary for committed proof witnesses and proof results.</summary>
/// <remarks>
/// See doc.md §7.5 and §8. Implementations must make artifact identity immutable: the same
/// chain/batch/content hash is idempotent, while any conflicting content fails closed.
/// </remarks>
public interface IProofWitnessStore : IDisposable
{
    /// <summary>
    /// Whether committed witness, proof, submission, and recovery records survive process restart.
    /// </summary>
    /// <remarks>
    /// Custom implementations default to fail-closed non-durable classification until they
    /// explicitly attest this production capability.
    /// </remarks>
    bool IsDurable => false;

    /// <summary>Atomically commit an immutable witness artifact.</summary>
    ValueTask CommitAsync(
        ProofWitnessArtifactV1 artifact,
        CancellationToken cancellationToken = default);

    /// <summary>Get a committed artifact by chain and batch number.</summary>
    ValueTask<ProofWitnessArtifactV1?> GetAsync(
        uint chainId,
        ulong batchNumber,
        CancellationToken cancellationToken = default);

    /// <summary>Enumerate committed artifacts for a chain in ascending batch-number order.</summary>
    IAsyncEnumerable<ProofWitnessArtifactV1> EnumerateCommittedAsync(
        uint chainId,
        CancellationToken cancellationToken = default);

    /// <summary>Atomically store an immutable proof result for a committed artifact.</summary>
    ValueTask PutProofAsync(
        ProofResultManifest manifest,
        CancellationToken cancellationToken = default);

    /// <summary>Get the proof result associated with an artifact content hash.</summary>
    ValueTask<ProofResultManifest?> GetProofAsync(
        UInt256 artifactContentHash,
        CancellationToken cancellationToken = default);

    /// <summary>Atomically bind a proof result to its L1 submission transaction.</summary>
    ValueTask MarkSubmittedAsync(
        UInt256 artifactContentHash,
        UInt256 l1TransactionHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replace a persisted submission transaction only after the settlement client explicitly
    /// reports the expected transaction dropped or reverted.
    /// </summary>
    ValueTask ReplaceSubmittedTransactionAsync(
        UInt256 artifactContentHash,
        UInt256 expectedTransactionHash,
        UInt256 replacementTransactionHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark settlement as observed when restart reconciliation finds the batch on L1 but the
    /// original transaction hash was not durably recorded before the prior process stopped.
    /// </summary>
    ValueTask MarkSubmissionObservedAsync(
        UInt256 artifactContentHash,
        CancellationToken cancellationToken = default);

    /// <summary>Mark settlement as finalized after L1 reports <see cref="BatchStatus.Finalized"/>.</summary>
    ValueTask MarkSettlementFinalizedAsync(
        UInt256 artifactContentHash,
        CancellationToken cancellationToken = default);

    /// <summary>Mark confirmed forced-inclusion consumption for a finalized artifact.</summary>
    ValueTask MarkForcedInclusionFinalizedAsync(
        UInt256 artifactContentHash,
        CancellationToken cancellationToken = default);

    /// <summary>Read the durable retry or poison checkpoint for an artifact.</summary>
    ValueTask<SettlementRecoveryCheckpoint?> GetSettlementRecoveryAsync(
        UInt256 artifactContentHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically record one failed reconciliation attempt and poison the artifact at the bound.
    /// </summary>
    ValueTask<SettlementRecoveryCheckpoint> RecordSettlementFailureAsync(
        uint chainId,
        ulong batchNumber,
        UInt256 artifactContentHash,
        string error,
        int maxAutomaticRetries,
        long failedAtUnixMilliseconds,
        CancellationToken cancellationToken = default);

    /// <summary>Reset an explicitly poisoned artifact after operator remediation.</summary>
    ValueTask ResetSettlementRecoveryAsync(
        uint chainId,
        ulong batchNumber,
        UInt256 artifactContentHash,
        CancellationToken cancellationToken = default);

    /// <summary>Clear resolved retry metadata without changing the canonical artifact.</summary>
    ValueTask ClearSettlementRecoveryAsync(
        uint chainId,
        ulong batchNumber,
        UInt256 artifactContentHash,
        CancellationToken cancellationToken = default);

    /// <summary>Atomically quarantine a reverted batch and every committed descendant.</summary>
    ValueTask<SettlementRollbackCheckpoint> QuarantineRevertedTailAsync(
        uint chainId,
        ulong firstBatchNumber,
        UInt256 revertedArtifactContentHash,
        CancellationToken cancellationToken = default);

    /// <summary>Read an unfinished execution-state rollback checkpoint for a chain.</summary>
    ValueTask<SettlementRollbackCheckpoint?> GetSettlementRollbackAsync(
        uint chainId,
        CancellationToken cancellationToken = default);

    /// <summary>Read a quarantined artifact by immutable content hash.</summary>
    ValueTask<ProofWitnessArtifactV1?> GetQuarantinedArtifactAsync(
        UInt256 artifactContentHash,
        CancellationToken cancellationToken = default);

    /// <summary>Clear the exact rollback checkpoint after execution state is restored.</summary>
    ValueTask CompleteSettlementRollbackAsync(
        SettlementRollbackCheckpoint checkpoint,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Return every forced nonce durably tracked by committed artifacts, including confirmed
    /// consumption, so a stale L1 read cache cannot reinsert it.
    /// </summary>
    ValueTask<IReadOnlyCollection<ulong>> GetTrackedForcedInclusionNoncesAsync(
        uint chainId,
        CancellationToken cancellationToken = default);
}
