using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Neo.Cryptography;
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

/// <summary>Durable proof-generation state associated with one witness artifact.</summary>
/// <remarks>
/// See doc.md §7.5 and §8. The manifest is written only after its referenced witness artifact
/// has been committed and validated.
/// </remarks>
public sealed record ProofResultManifest
{
    /// <summary>Wire-format version.</summary>
    public const ushort Version = 2;

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

    /// <summary>
    /// Return every forced nonce durably tracked by committed artifacts, including confirmed
    /// consumption, so a stale L1 read cache cannot reinsert it.
    /// </summary>
    ValueTask<IReadOnlyCollection<ulong>> GetTrackedForcedInclusionNoncesAsync(
        uint chainId,
        CancellationToken cancellationToken = default);
}

/// <summary><see cref="IL2KeyValueStore"/>-backed proof witness store.</summary>
/// <remarks>
/// See doc.md §7.5 and §8. Artifact keys are exactly
/// <c>"PWIT" | chainId:u32 LE | batchNumber:u64 LE</c>. Each value is one complete canonical
/// artifact, so a RocksDB Put is the crash-recovery commit point.
/// </remarks>
public sealed class KeyValueProofWitnessStore : IProofWitnessStore
{
    private static ReadOnlySpan<byte> ArtifactPrefix => "PWIT"u8;
    private static ReadOnlySpan<byte> ProofPrefix => "PWRF"u8;
    private static ReadOnlySpan<byte> SettlementRecoveryPrefix => "PWRC"u8;

    private readonly IL2KeyValueStore _store;
    private readonly bool _ownsStore;
    private bool _disposed;

    /// <summary>Create a witness store over a shared key-value backend.</summary>
    public KeyValueProofWitnessStore(IL2KeyValueStore store, bool ownsStore = false)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _ownsStore = ownsStore;
    }

    /// <inheritdoc />
    public ValueTask CommitAsync(
        ProofWitnessArtifactV1 artifact,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(artifact);
        ThrowIfDisposed();
        var key = ArtifactKey(artifact.ChainId, artifact.BatchNumber);
        var encoded = ProofWitnessArtifactSerializer.Encode(artifact);

        for (var attempt = 0; attempt < 8; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_store.TryPut(key, encoded)) return ValueTask.CompletedTask;
            var currentBytes = _store.Get(key);
            if (currentBytes is null) continue;
            var current = ProofWitnessArtifactSerializer.Decode(currentBytes);
            if (current.ContentHash.Equals(artifact.ContentHash))
                return ValueTask.CompletedTask;
            throw new InvalidOperationException(
                $"Conflicting proof witness for chain {artifact.ChainId}, batch {artifact.BatchNumber}: " +
                $"stored {HashHex(current.ContentHash)}, attempted {HashHex(artifact.ContentHash)}");
        }

        throw new InvalidOperationException(
            "Proof witness key changed repeatedly during atomic commit");
    }

    /// <inheritdoc />
    public ValueTask<ProofWitnessArtifactV1?> GetAsync(
        uint chainId,
        ulong batchNumber,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        var bytes = _store.Get(ArtifactKey(chainId, batchNumber));
        if (bytes is null) return new ValueTask<ProofWitnessArtifactV1?>((ProofWitnessArtifactV1?)null);
        var artifact = ProofWitnessArtifactSerializer.Decode(bytes);
        if (artifact.ChainId != chainId || artifact.BatchNumber != batchNumber)
            throw new InvalidDataException(
                "Proof witness value identity does not match its storage key");
        return new ValueTask<ProofWitnessArtifactV1?>(artifact);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ProofWitnessArtifactV1> EnumerateCommittedAsync(
        uint chainId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var prefix = ArtifactChainPrefix(chainId);
        var artifacts = new List<ProofWitnessArtifactV1>();
        foreach (var (key, value) in _store.EnumeratePrefix(prefix))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (key.Length != ArtifactPrefix.Length + 4 + 8)
                throw new InvalidDataException("Malformed proof witness storage key length");
            var keyBatchNumber = BinaryPrimitives.ReadUInt64LittleEndian(key.AsSpan(8, 8));
            var artifact = ProofWitnessArtifactSerializer.Decode(value);
            if (artifact.ChainId != chainId || artifact.BatchNumber != keyBatchNumber)
                throw new InvalidDataException(
                    "Proof witness value identity does not match its storage key");
            artifacts.Add(artifact);
        }

        foreach (var artifact in artifacts.OrderBy(static artifact => artifact.BatchNumber))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return artifact;
            await Task.Yield();
        }
    }

    /// <inheritdoc />
    public ValueTask PutProofAsync(
        ProofResultManifest manifest,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(manifest);
        ThrowIfDisposed();
        var committedBytes = _store.Get(ArtifactKey(manifest.ChainId, manifest.BatchNumber))
            ?? throw new InvalidOperationException(
                $"Cannot persist proof for unknown chain {manifest.ChainId}, batch {manifest.BatchNumber}");
        var committedArtifact = ProofWitnessArtifactSerializer.Decode(committedBytes);
        ValidateManifestBinding(manifest, committedArtifact);

        var key = ProofKey(manifest.ArtifactContentHash);
        var encoded = ProofResultManifestSerializer.Encode(manifest);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_store.TryPut(key, encoded)) return ValueTask.CompletedTask;
            var currentBytes = _store.Get(key);
            if (currentBytes is null) continue;
            if (currentBytes.AsSpan().SequenceEqual(encoded)) return ValueTask.CompletedTask;
            throw new InvalidOperationException(
                $"Conflicting proof result for artifact {HashHex(manifest.ArtifactContentHash)}");
        }

        throw new InvalidOperationException("Proof result key changed repeatedly during atomic commit");
    }

    /// <inheritdoc />
    public ValueTask<ProofResultManifest?> GetProofAsync(
        UInt256 artifactContentHash,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(artifactContentHash);
        ThrowIfDisposed();
        var bytes = _store.Get(ProofKey(artifactContentHash));
        if (bytes is null) return new ValueTask<ProofResultManifest?>((ProofResultManifest?)null);
        var manifest = ProofResultManifestSerializer.Decode(bytes);
        if (!manifest.ArtifactContentHash.Equals(artifactContentHash))
            throw new InvalidDataException("Proof manifest identity does not match its storage key");
        return new ValueTask<ProofResultManifest?>(manifest);
    }

    /// <inheritdoc />
    public ValueTask MarkSubmittedAsync(
        UInt256 artifactContentHash,
        UInt256 l1TransactionHash,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(artifactContentHash);
        ArgumentNullException.ThrowIfNull(l1TransactionHash);
        if (l1TransactionHash.Equals(UInt256.Zero))
            throw new ArgumentException("L1 transaction hash must be non-zero", nameof(l1TransactionHash));
        ThrowIfDisposed();
        var key = ProofKey(artifactContentHash);

        for (var attempt = 0; attempt < 32; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentBytes = _store.Get(key)
                ?? throw new InvalidOperationException(
                    $"No proof result exists for artifact {HashHex(artifactContentHash)}");
            var current = ProofResultManifestSerializer.Decode(currentBytes);
            if (!current.ArtifactContentHash.Equals(artifactContentHash))
                throw new InvalidDataException("Proof manifest identity does not match its storage key");
            if (current.SubmissionState != ProofSubmissionState.ProofReady)
            {
                if (current.L1TransactionHash?.Equals(l1TransactionHash) == true)
                    return ValueTask.CompletedTask;
                throw new InvalidOperationException(
                    $"Artifact {HashHex(artifactContentHash)} is already bound to a different L1 transaction");
            }

            var updated = current with
            {
                SubmissionState = ProofSubmissionState.Submitted,
                L1TransactionHash = l1TransactionHash,
            };
            var updatedBytes = ProofResultManifestSerializer.Encode(updated);
            if (_store.CompareExchange(key, currentBytes, updatedBytes))
                return ValueTask.CompletedTask;
        }

        throw new InvalidOperationException(
            "Proof result changed repeatedly while marking it submitted");
    }

    /// <inheritdoc />
    public ValueTask ReplaceSubmittedTransactionAsync(
        UInt256 artifactContentHash,
        UInt256 expectedTransactionHash,
        UInt256 replacementTransactionHash,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(artifactContentHash);
        ArgumentNullException.ThrowIfNull(expectedTransactionHash);
        ArgumentNullException.ThrowIfNull(replacementTransactionHash);
        if (expectedTransactionHash.Equals(UInt256.Zero))
            throw new ArgumentException(
                "Expected L1 transaction hash must be non-zero", nameof(expectedTransactionHash));
        if (replacementTransactionHash.Equals(UInt256.Zero))
            throw new ArgumentException(
                "Replacement L1 transaction hash must be non-zero", nameof(replacementTransactionHash));
        ThrowIfDisposed();
        var key = ProofKey(artifactContentHash);

        for (var attempt = 0; attempt < 32; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentBytes = _store.Get(key)
                ?? throw new InvalidOperationException(
                    $"No proof result exists for artifact {HashHex(artifactContentHash)}");
            var current = ProofResultManifestSerializer.Decode(currentBytes);
            if (!current.ArtifactContentHash.Equals(artifactContentHash))
                throw new InvalidDataException(
                    "Proof manifest identity does not match its storage key");
            if (current.SettlementObserved)
                throw new InvalidOperationException(
                    "An observed settlement transaction cannot be replaced");
            if (current.SubmissionState != ProofSubmissionState.Submitted
                || current.L1TransactionHash?.Equals(expectedTransactionHash) != true)
                throw new InvalidOperationException(
                    "Persisted submission transaction differs from the expected replacement target");
            if (expectedTransactionHash.Equals(replacementTransactionHash))
                return ValueTask.CompletedTask;

            var updatedBytes = ProofResultManifestSerializer.Encode(current with
            {
                L1TransactionHash = replacementTransactionHash,
            });
            if (_store.CompareExchange(key, currentBytes, updatedBytes))
                return ValueTask.CompletedTask;
        }

        throw new InvalidOperationException(
            "Proof result changed repeatedly while replacing its submission transaction");
    }

    /// <inheritdoc />
    public ValueTask MarkSubmissionObservedAsync(
        UInt256 artifactContentHash,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(artifactContentHash);
        ThrowIfDisposed();
        var key = ProofKey(artifactContentHash);

        for (var attempt = 0; attempt < 32; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentBytes = _store.Get(key)
                ?? throw new InvalidOperationException(
                    $"No proof result exists for artifact {HashHex(artifactContentHash)}");
            var current = ProofResultManifestSerializer.Decode(currentBytes);
            if (!current.ArtifactContentHash.Equals(artifactContentHash))
                throw new InvalidDataException(
                    "Proof manifest identity does not match its storage key");
            if (current.SettlementObserved) return ValueTask.CompletedTask;
            var updatedBytes = ProofResultManifestSerializer.Encode(
                current with { SubmissionState = ProofSubmissionState.SettlementObserved });
            if (_store.CompareExchange(key, currentBytes, updatedBytes))
                return ValueTask.CompletedTask;
        }

        throw new InvalidOperationException(
            "Proof result changed repeatedly while reconciling submission");
    }

    /// <inheritdoc />
    public ValueTask MarkForcedInclusionFinalizedAsync(
        UInt256 artifactContentHash,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(artifactContentHash);
        ThrowIfDisposed();
        var key = ProofKey(artifactContentHash);

        for (var attempt = 0; attempt < 32; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentBytes = _store.Get(key)
                ?? throw new InvalidOperationException(
                    $"No proof result exists for artifact {HashHex(artifactContentHash)}");
            var current = ProofResultManifestSerializer.Decode(currentBytes);
            if (!current.ArtifactContentHash.Equals(artifactContentHash))
                throw new InvalidDataException(
                    "Proof manifest identity does not match its storage key");
            if (current.ForcedInclusionFinalized) return ValueTask.CompletedTask;
            if (!current.SettlementObserved)
                throw new InvalidOperationException(
                    "forced inclusion cannot finalize before settlement is observed");
            var artifactBytes = _store.Get(ArtifactKey(current.ChainId, current.BatchNumber))
                ?? throw new InvalidOperationException(
                    "proof manifest references a missing witness artifact");
            var artifact = ProofWitnessArtifactSerializer.Decode(artifactBytes);
            if (!artifact.ContentHash.Equals(artifactContentHash)
                || artifact.ExecutionPayload.ForcedInclusions.Count == 0)
                throw new InvalidOperationException(
                    "artifact has no matching forced-inclusion reservation to finalize");
            var updatedBytes = ProofResultManifestSerializer.Encode(
                current with { ForcedInclusionFinalized = true });
            if (_store.CompareExchange(key, currentBytes, updatedBytes))
                return ValueTask.CompletedTask;
        }

        throw new InvalidOperationException(
            "Proof result changed repeatedly while finalizing forced inclusion");
    }

    /// <inheritdoc />
    public ValueTask<SettlementRecoveryCheckpoint?> GetSettlementRecoveryAsync(
        UInt256 artifactContentHash,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(artifactContentHash);
        ThrowIfDisposed();
        var bytes = _store.Get(SettlementRecoveryKey(artifactContentHash));
        if (bytes is null)
            return new ValueTask<SettlementRecoveryCheckpoint?>((SettlementRecoveryCheckpoint?)null);
        var checkpoint = SettlementRecoveryCheckpointSerializer.Decode(bytes);
        if (!checkpoint.ArtifactContentHash.Equals(artifactContentHash))
            throw new InvalidDataException(
                "Settlement recovery identity does not match its storage key");
        return new ValueTask<SettlementRecoveryCheckpoint?>(checkpoint);
    }

    /// <inheritdoc />
    public ValueTask<SettlementRecoveryCheckpoint> RecordSettlementFailureAsync(
        uint chainId,
        ulong batchNumber,
        UInt256 artifactContentHash,
        string error,
        int maxAutomaticRetries,
        long failedAtUnixMilliseconds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(artifactContentHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        if (maxAutomaticRetries is < 1 or > 100)
            throw new ArgumentOutOfRangeException(
                nameof(maxAutomaticRetries), "maxAutomaticRetries must be in [1, 100]");
        if (failedAtUnixMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(failedAtUnixMilliseconds));
        ThrowIfDisposed();
        ValidateRecoveryArtifact(chainId, batchNumber, artifactContentHash);
        var key = SettlementRecoveryKey(artifactContentHash);

        for (var attempt = 0; attempt < 32; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentBytes = _store.Get(key);
            SettlementRecoveryCheckpoint updated;
            if (currentBytes is null)
            {
                updated = new SettlementRecoveryCheckpoint
                {
                    ChainId = chainId,
                    BatchNumber = batchNumber,
                    ArtifactContentHash = artifactContentHash,
                    State = maxAutomaticRetries == 1
                        ? SettlementRecoveryState.Poisoned
                        : SettlementRecoveryState.Retrying,
                    RetryCount = 1,
                    FirstFailureAtUnixMilliseconds = failedAtUnixMilliseconds,
                    LastFailureAtUnixMilliseconds = failedAtUnixMilliseconds,
                    LastError = error,
                };
                var inserted = SettlementRecoveryCheckpointSerializer.Encode(updated);
                if (_store.TryPut(key, inserted))
                    return ValueTask.FromResult(
                        SettlementRecoveryCheckpointSerializer.Decode(inserted));
                continue;
            }

            var current = SettlementRecoveryCheckpointSerializer.Decode(currentBytes);
            ValidateRecoveryBinding(current, chainId, batchNumber, artifactContentHash);
            if (current.State == SettlementRecoveryState.Poisoned)
                return ValueTask.FromResult(current);
            var retryCount = checked(current.RetryCount + 1);
            updated = current with
            {
                State = retryCount >= maxAutomaticRetries
                    ? SettlementRecoveryState.Poisoned
                    : SettlementRecoveryState.Retrying,
                RetryCount = retryCount,
                FirstFailureAtUnixMilliseconds = current.RetryCount == 0
                    ? failedAtUnixMilliseconds
                    : current.FirstFailureAtUnixMilliseconds,
                LastFailureAtUnixMilliseconds = failedAtUnixMilliseconds,
                LastError = error,
            };
            var updatedBytes = SettlementRecoveryCheckpointSerializer.Encode(updated);
            if (_store.CompareExchange(key, currentBytes, updatedBytes))
                return ValueTask.FromResult(
                    SettlementRecoveryCheckpointSerializer.Decode(updatedBytes));
        }

        throw new InvalidOperationException(
            "Settlement recovery changed repeatedly while recording a failure");
    }

    /// <inheritdoc />
    public ValueTask ResetSettlementRecoveryAsync(
        uint chainId,
        ulong batchNumber,
        UInt256 artifactContentHash,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(artifactContentHash);
        ThrowIfDisposed();
        ValidateRecoveryArtifact(chainId, batchNumber, artifactContentHash);
        var key = SettlementRecoveryKey(artifactContentHash);

        for (var attempt = 0; attempt < 32; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentBytes = _store.Get(key)
                ?? throw new InvalidOperationException(
                    "No settlement recovery checkpoint exists for the artifact");
            var current = SettlementRecoveryCheckpointSerializer.Decode(currentBytes);
            ValidateRecoveryBinding(current, chainId, batchNumber, artifactContentHash);
            if (current.State != SettlementRecoveryState.Poisoned)
                throw new InvalidOperationException(
                    "Settlement recovery checkpoint is not poisoned");
            var resetBytes = SettlementRecoveryCheckpointSerializer.Encode(current with
            {
                State = SettlementRecoveryState.Retrying,
                RetryCount = 0,
                FirstFailureAtUnixMilliseconds = 0,
                LastFailureAtUnixMilliseconds = 0,
                LastError = null,
            });
            if (_store.CompareExchange(key, currentBytes, resetBytes))
                return ValueTask.CompletedTask;
        }

        throw new InvalidOperationException(
            "Settlement recovery changed repeatedly while resetting poison state");
    }

    /// <inheritdoc />
    public ValueTask ClearSettlementRecoveryAsync(
        uint chainId,
        ulong batchNumber,
        UInt256 artifactContentHash,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(artifactContentHash);
        ThrowIfDisposed();
        var key = SettlementRecoveryKey(artifactContentHash);
        var currentBytes = _store.Get(key);
        if (currentBytes is null) return ValueTask.CompletedTask;
        var current = SettlementRecoveryCheckpointSerializer.Decode(currentBytes);
        ValidateRecoveryBinding(current, chainId, batchNumber, artifactContentHash);
        _store.Delete(key);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyCollection<ulong>> GetTrackedForcedInclusionNoncesAsync(
        uint chainId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var reserved = new HashSet<ulong>();
        await foreach (var artifact in EnumerateCommittedAsync(chainId, cancellationToken))
        {
            if (artifact.ExecutionPayload.ForcedInclusions.Count == 0) continue;
            foreach (var proof in artifact.ExecutionPayload.ForcedInclusions)
                reserved.Add(proof.Nonce);
        }
        return reserved;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsStore) _store.Dispose();
    }

    private static void ValidateManifestBinding(
        ProofResultManifest manifest,
        ProofWitnessArtifactV1 artifact)
    {
        if (manifest.ChainId != artifact.ChainId || manifest.BatchNumber != artifact.BatchNumber)
            throw new ArgumentException(
                "Proof manifest identity does not match the committed artifact",
                nameof(manifest));
        if (!manifest.ArtifactContentHash.Equals(artifact.ContentHash))
            throw new ArgumentException(
                "Proof manifest content hash does not match the committed artifact",
                nameof(manifest));
        var publicInputHash = new UInt256(
            Crypto.Hash256(BatchSerializer.EncodePublicInputs(artifact.PublicInputs)));
        if (!manifest.PublicInputHash.Equals(publicInputHash))
            throw new ArgumentException(
                "Proof manifest public-input hash does not match the committed artifact",
                nameof(manifest));
        if (!manifest.VerificationKeyId.Equals(artifact.VerificationKeyId))
            throw new ArgumentException(
                "Proof manifest verification key does not match the committed artifact",
                nameof(manifest));
        if (manifest.ProofSystem != artifact.ProofSystem)
            throw new ArgumentException(
                "Proof manifest proof system does not match the committed artifact",
                nameof(manifest));
        if (manifest.ProofType != artifact.ProofType)
            throw new ArgumentException(
                "Proof manifest proof type does not match the committed artifact",
                nameof(manifest));
        if (!manifest.ExecutionSemanticId.Equals(artifact.ExecutionSemanticId))
            throw new ArgumentException(
                "Proof manifest execution semantic does not match the committed artifact",
                nameof(manifest));
    }

    private static byte[] ArtifactKey(uint chainId, ulong batchNumber)
    {
        var key = new byte[ArtifactPrefix.Length + 4 + 8];
        ArtifactPrefix.CopyTo(key);
        BinaryPrimitives.WriteUInt32LittleEndian(key.AsSpan(4, 4), chainId);
        BinaryPrimitives.WriteUInt64LittleEndian(key.AsSpan(8, 8), batchNumber);
        return key;
    }

    private static byte[] ArtifactChainPrefix(uint chainId)
    {
        var key = new byte[ArtifactPrefix.Length + 4];
        ArtifactPrefix.CopyTo(key);
        BinaryPrimitives.WriteUInt32LittleEndian(key.AsSpan(4, 4), chainId);
        return key;
    }

    private static byte[] ProofKey(UInt256 artifactContentHash)
    {
        var key = new byte[ProofPrefix.Length + UInt256.Length];
        ProofPrefix.CopyTo(key);
        artifactContentHash.GetSpan().CopyTo(key.AsSpan(ProofPrefix.Length));
        return key;
    }

    private static byte[] SettlementRecoveryKey(UInt256 artifactContentHash)
    {
        var key = new byte[SettlementRecoveryPrefix.Length + UInt256.Length];
        SettlementRecoveryPrefix.CopyTo(key);
        artifactContentHash.GetSpan().CopyTo(key.AsSpan(SettlementRecoveryPrefix.Length));
        return key;
    }

    private void ValidateRecoveryArtifact(
        uint chainId,
        ulong batchNumber,
        UInt256 artifactContentHash)
    {
        var artifactBytes = _store.Get(ArtifactKey(chainId, batchNumber))
            ?? throw new InvalidOperationException(
                $"Cannot record settlement recovery for unknown chain {chainId}, batch {batchNumber}");
        var artifact = ProofWitnessArtifactSerializer.Decode(artifactBytes);
        if (!artifact.ContentHash.Equals(artifactContentHash))
            throw new InvalidOperationException(
                "Settlement recovery content hash does not match the committed artifact");
    }

    private static void ValidateRecoveryBinding(
        SettlementRecoveryCheckpoint checkpoint,
        uint chainId,
        ulong batchNumber,
        UInt256 artifactContentHash)
    {
        if (checkpoint.ChainId != chainId
            || checkpoint.BatchNumber != batchNumber
            || !checkpoint.ArtifactContentHash.Equals(artifactContentHash))
        {
            throw new InvalidDataException(
                "Settlement recovery checkpoint does not match the canonical artifact");
        }
    }

    private static string HashHex(UInt256 hash)
        => Convert.ToHexString(hash.GetSpan()).ToLowerInvariant();

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(KeyValueProofWitnessStore));
    }
}

internal static class SettlementRecoveryCheckpointSerializer
{
    private static ReadOnlySpan<byte> Magic => "NEO4SRCV"u8;
    private const byte Version = 1;
    private const int FixedSize = 80;
    private const int MaxErrorBytes = 4096;

    public static byte[] Encode(SettlementRecoveryCheckpoint checkpoint)
    {
        Validate(checkpoint);
        var errorBytes = checkpoint.LastError is null
            ? Array.Empty<byte>()
            : Encoding.UTF8.GetBytes(checkpoint.LastError);
        if (errorBytes.Length > MaxErrorBytes)
            errorBytes = errorBytes.AsSpan(0, MaxErrorBytes).ToArray();
        var encodedSize = checked(FixedSize + errorBytes.Length);
        var writer = new ManifestWireWriter(encodedSize);
        writer.WriteBytes(Magic);
        writer.WriteByte(Version);
        writer.WriteByte((byte)checkpoint.State);
        writer.WriteBytes(stackalloc byte[2]);
        writer.WriteUInt32(checkpoint.ChainId);
        writer.WriteUInt64(checkpoint.BatchNumber);
        writer.WriteUInt256(checkpoint.ArtifactContentHash);
        writer.WriteUInt32(checked((uint)checkpoint.RetryCount));
        writer.WriteUInt64(checked((ulong)checkpoint.FirstFailureAtUnixMilliseconds));
        writer.WriteUInt64(checked((ulong)checkpoint.LastFailureAtUnixMilliseconds));
        writer.WriteLengthPrefixedBytes(errorBytes);
        if (writer.WrittenCount != encodedSize)
            throw new InvalidOperationException("Settlement recovery checkpoint length mismatch");
        return writer.ToArray();
    }

    public static SettlementRecoveryCheckpoint Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < FixedSize || data.Length > FixedSize + MaxErrorBytes)
            throw new InvalidDataException("Settlement recovery checkpoint has an invalid length");
        var reader = new ManifestWireReader(data);
        reader.RequireMagic(Magic);
        var version = reader.ReadByte("version");
        if (version != Version)
            throw new InvalidDataException(
                $"Unsupported settlement recovery checkpoint version {version}");
        var stateByte = reader.ReadByte("state");
        if (!Enum.IsDefined((SettlementRecoveryState)stateByte))
            throw new InvalidDataException(
                $"Unknown settlement recovery state byte {stateByte}");
        var reserved = reader.ReadBytes(2, "reserved bytes");
        if (reserved[0] != 0 || reserved[1] != 0)
            throw new InvalidDataException(
                "Settlement recovery reserved bytes must be zero");
        var checkpoint = new SettlementRecoveryCheckpoint
        {
            ChainId = reader.ReadUInt32("chain id"),
            BatchNumber = reader.ReadUInt64("batch number"),
            ArtifactContentHash = reader.ReadUInt256("artifact hash"),
            State = (SettlementRecoveryState)stateByte,
            RetryCount = checked((int)reader.ReadUInt32("retry count")),
            FirstFailureAtUnixMilliseconds = checked((long)reader.ReadUInt64("first failure")),
            LastFailureAtUnixMilliseconds = checked((long)reader.ReadUInt64("last failure")),
            LastError = DecodeError(reader.ReadLengthPrefixedBytes(MaxErrorBytes, "last error")),
        };
        reader.EnsureEnd();
        try
        {
            Validate(checkpoint);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "Settlement recovery checkpoint fields are inconsistent", exception);
        }
        return checkpoint;
    }

    private static string? DecodeError(ReadOnlyMemory<byte> bytes)
        => bytes.IsEmpty ? null : Encoding.UTF8.GetString(bytes.Span);

    private static void Validate(SettlementRecoveryCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(checkpoint.ArtifactContentHash);
        if (!Enum.IsDefined(checkpoint.State))
            throw new ArgumentException("Unknown settlement recovery state", nameof(checkpoint));
        if (checkpoint.RetryCount < 0)
            throw new ArgumentException("RetryCount must be non-negative", nameof(checkpoint));
        if (checkpoint.State == SettlementRecoveryState.Poisoned
            && checkpoint.RetryCount == 0)
            throw new ArgumentException("Poisoned recovery requires a retry count", nameof(checkpoint));
        if (checkpoint.RetryCount == 0)
        {
            if (checkpoint.FirstFailureAtUnixMilliseconds != 0
                || checkpoint.LastFailureAtUnixMilliseconds != 0
                || checkpoint.LastError is not null)
            {
                throw new ArgumentException(
                    "Reset recovery state must clear failure details", nameof(checkpoint));
            }
            return;
        }
        if (checkpoint.FirstFailureAtUnixMilliseconds <= 0
            || checkpoint.LastFailureAtUnixMilliseconds < checkpoint.FirstFailureAtUnixMilliseconds)
            throw new ArgumentException("Invalid settlement recovery timestamps", nameof(checkpoint));
        if (string.IsNullOrWhiteSpace(checkpoint.LastError))
            throw new ArgumentException("Failed recovery requires LastError", nameof(checkpoint));
    }
}

/// <summary>Canonical serializer for <see cref="ProofResultManifest"/>.</summary>
/// <remarks>See doc.md §7.5 and §8.</remarks>
public static class ProofResultManifestSerializer
{
    private static ReadOnlySpan<byte> Magic => "NEO4PRMF"u8;
    private static ReadOnlySpan<byte> ContentHashDomainV1 => "neo-n4/proof-result/v1\0"u8;
    private static ReadOnlySpan<byte> ContentHashDomainV2 => "neo-n4/proof-result/v2\0"u8;

    private const ushort SubmittedFlag = 1;
    private const ushort SettlementObservedFlag = 2;
    private const ushort TransactionHashFlag = 4;
    private const ushort ForcedInclusionFinalizedFlag = 8;
    private const ushort KnownFlags = SubmittedFlag
        | SettlementObservedFlag
        | TransactionHashFlag
        | ForcedInclusionFinalizedFlag;
    private const ushort V1SettlementObservedFlag = 1;
    private const ushort V1TransactionHashFlag = 2;
    private const ushort V1ForcedInclusionFinalizedFlag = 4;
    private const ushort V1KnownFlags = V1SettlementObservedFlag
        | V1TransactionHashFlag
        | V1ForcedInclusionFinalizedFlag;
    private const int FixedSize = 164;
    private const int ContentHashSize = 32;

    /// <summary>Maximum terminal proof size (16 MiB).</summary>
    public const int MaxProofBytes = 16 * 1024 * 1024;

    /// <summary>Maximum public-values size (16 MiB).</summary>
    public const int MaxPublicValuesBytes = 16 * 1024 * 1024;

    /// <summary>Encode a proof result manifest.</summary>
    public static byte[] Encode(ProofResultManifest manifest)
    {
        Validate(manifest);
        var hasTransactionHash = manifest.L1TransactionHash is not null;
        var flags = manifest.SubmissionState switch
        {
            ProofSubmissionState.ProofReady => (ushort)0,
            ProofSubmissionState.Submitted => SubmittedFlag,
            ProofSubmissionState.SettlementObserved => SettlementObservedFlag,
            _ => throw new ArgumentOutOfRangeException(
                nameof(manifest), manifest.SubmissionState, "unknown proof submission state"),
        };
        if (hasTransactionHash) flags |= TransactionHashFlag;
        if (manifest.ForcedInclusionFinalized) flags |= ForcedInclusionFinalizedFlag;
        var bodySize = checked(
            FixedSize
            + manifest.Proof.Length
            + manifest.PublicValues.Length
            + (hasTransactionHash ? UInt256.Length : 0));
        var writer = new ManifestWireWriter(bodySize);
        writer.WriteBytes(Magic);
        writer.WriteUInt16(ProofResultManifest.Version);
        writer.WriteUInt16(flags);
        writer.WriteByte((byte)manifest.ProofType);
        writer.WriteByte((byte)manifest.ProofSystem);
        writer.WriteBytes(stackalloc byte[2]);
        writer.WriteUInt32(manifest.ChainId);
        writer.WriteUInt64(manifest.BatchNumber);
        writer.WriteUInt256(manifest.ArtifactContentHash);
        writer.WriteUInt256(manifest.PublicInputHash);
        writer.WriteUInt256(manifest.VerificationKeyId);
        writer.WriteUInt256(manifest.ExecutionSemanticId);
        writer.WriteLengthPrefixedBytes(manifest.Proof.Span);
        writer.WriteLengthPrefixedBytes(manifest.PublicValues.Span);
        if (manifest.L1TransactionHash is not null)
            writer.WriteUInt256(manifest.L1TransactionHash);
        if (writer.WrittenCount != bodySize)
            throw new InvalidOperationException(
                $"Proof result manifest length mismatch: wrote {writer.WrittenCount}, expected {bodySize}");
        var body = writer.ToArray();
        var contentHash = ComputeContentHash(body, ProofResultManifest.Version);
        var encoded = new byte[checked(body.Length + ContentHashSize)];
        body.CopyTo(encoded, 0);
        contentHash.GetSpan().CopyTo(encoded.AsSpan(body.Length));
        return encoded;
    }

    /// <summary>Decode a proof result manifest and reject malformed bytes.</summary>
    public static ProofResultManifest Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < FixedSize + ContentHashSize)
            throw new InvalidDataException("Proof result manifest is truncated");
        var reader = new ManifestWireReader(data);
        reader.RequireMagic(Magic);
        var version = reader.ReadUInt16("version");
        if (version is not (1 or ProofResultManifest.Version))
            throw new InvalidDataException($"Unsupported proof result version {version}");
        var flags = reader.ReadUInt16("flags");
        var knownFlags = version == 1 ? V1KnownFlags : KnownFlags;
        if ((flags & ~knownFlags) != 0)
            throw new InvalidDataException($"Unknown proof result flags 0x{flags:x4}");
        if (version == ProofResultManifest.Version
            && (flags & SubmittedFlag) != 0
            && (flags & SettlementObservedFlag) != 0)
            throw new InvalidDataException(
                "Proof result cannot be both submitted and settlement-observed");
        var proofTypeByte = reader.ReadByte("proof type");
        if (!Enum.IsDefined((ProofType)proofTypeByte))
            throw new InvalidDataException($"Unknown proof type byte {proofTypeByte}");
        var proofSystemByte = reader.ReadByte("proof system");
        if (!Enum.IsDefined((WitnessProofSystem)proofSystemByte))
            throw new InvalidDataException($"Unknown proof system byte {proofSystemByte}");
        var reserved = reader.ReadBytes(2, "reserved bytes");
        if (reserved[0] != 0 || reserved[1] != 0)
            throw new InvalidDataException("Proof result reserved bytes must be zero");
        var chainId = reader.ReadUInt32("chain id");
        var batchNumber = reader.ReadUInt64("batch number");
        var artifactHash = reader.ReadUInt256("artifact hash");
        var publicInputHash = reader.ReadUInt256("public input hash");
        var verificationKeyId = reader.ReadUInt256("verification key id");
        var executionSemanticId = reader.ReadUInt256("execution semantic id");
        var proof = reader.ReadLengthPrefixedBytes(MaxProofBytes, "proof");
        var publicValues = reader.ReadLengthPrefixedBytes(MaxPublicValuesBytes, "public values");
        var transactionHashFlag = version == 1
            ? V1TransactionHashFlag
            : TransactionHashFlag;
        var l1TransactionHash = (flags & transactionHashFlag) != 0
            ? reader.ReadUInt256("L1 transaction hash")
            : null;
        var contentHash = reader.ReadUInt256("content hash");
        reader.EnsureEnd();
        var expectedContentHash = ComputeContentHash(data[..^ContentHashSize], version);
        if (!expectedContentHash.Equals(contentHash))
            throw new InvalidDataException("Proof result manifest content hash mismatch");
        var submissionState = version == 1
            ? DecodeV1SubmissionState(flags)
            : (flags & SettlementObservedFlag) != 0
                ? ProofSubmissionState.SettlementObserved
                : (flags & SubmittedFlag) != 0
                    ? ProofSubmissionState.Submitted
                    : ProofSubmissionState.ProofReady;
        var forcedFinalizedFlag = version == 1
            ? V1ForcedInclusionFinalizedFlag
            : ForcedInclusionFinalizedFlag;
        var manifest = new ProofResultManifest
        {
            ProofType = (ProofType)proofTypeByte,
            ChainId = chainId,
            BatchNumber = batchNumber,
            ArtifactContentHash = artifactHash,
            PublicInputHash = publicInputHash,
            VerificationKeyId = verificationKeyId,
            ProofSystem = (WitnessProofSystem)proofSystemByte,
            ExecutionSemanticId = executionSemanticId,
            Proof = proof,
            PublicValues = publicValues,
            SubmissionState = submissionState,
            ForcedInclusionFinalized = (flags & forcedFinalizedFlag) != 0,
            L1TransactionHash = l1TransactionHash,
        };
        try
        {
            Validate(manifest);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException("Proof result manifest fields are inconsistent", ex);
        }
        return manifest;
    }

    private static ProofSubmissionState DecodeV1SubmissionState(ushort flags)
    {
        if ((flags & V1ForcedInclusionFinalizedFlag) != 0)
            return ProofSubmissionState.SettlementObserved;
        if ((flags & V1TransactionHashFlag) != 0)
            return ProofSubmissionState.Submitted;
        return (flags & V1SettlementObservedFlag) != 0
            ? ProofSubmissionState.SettlementObserved
            : ProofSubmissionState.ProofReady;
    }

    private static UInt256 ComputeContentHash(ReadOnlySpan<byte> body, ushort version)
    {
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha256.AppendData(version == 1 ? ContentHashDomainV1 : ContentHashDomainV2);
        sha256.AppendData(body);
        var firstHash = sha256.GetHashAndReset();
        return new UInt256(SHA256.HashData(firstHash));
    }

    private static void Validate(ProofResultManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(manifest.ArtifactContentHash);
        ArgumentNullException.ThrowIfNull(manifest.PublicInputHash);
        ArgumentNullException.ThrowIfNull(manifest.VerificationKeyId);
        ArgumentNullException.ThrowIfNull(manifest.ExecutionSemanticId);
        if (!Enum.IsDefined(manifest.ProofType) || manifest.ProofType == ProofType.None)
            throw new ArgumentException("Unknown proof type", nameof(manifest));
        if (!Enum.IsDefined(manifest.ProofSystem))
            throw new ArgumentException("Unknown proof system", nameof(manifest));
        if (manifest.ExecutionSemanticId.Equals(UInt256.Zero))
            throw new ArgumentException("ExecutionSemanticId must be non-zero", nameof(manifest));
        if (manifest.ProofType == ProofType.Zk)
        {
            if (manifest.ProofSystem == WitnessProofSystem.None
                || manifest.VerificationKeyId.Equals(UInt256.Zero))
                throw new ArgumentException(
                    "ZK proof manifests require a proof system and verification key",
                    nameof(manifest));
        }
        else if (manifest.ProofType is ProofType.Multisig or ProofType.Optimistic)
        {
            if (manifest.ProofSystem != WitnessProofSystem.None
                || !manifest.VerificationKeyId.Equals(UInt256.Zero))
                throw new ArgumentException(
                    "Legacy proof manifests must use ProofSystem.None and zero VK",
                    nameof(manifest));
        }
        else
        {
            throw new ArgumentException(
                "Only explicit ZK, multisig, and optimistic manifests are supported",
                nameof(manifest));
        }
        if (manifest.Proof.Length == 0)
            throw new ArgumentException("Proof must be non-empty", nameof(manifest));
        if (manifest.Proof.Length > MaxProofBytes)
            throw new ArgumentException($"Proof exceeds {MaxProofBytes} bytes", nameof(manifest));
        if (manifest.PublicValues.Length > MaxPublicValuesBytes)
            throw new ArgumentException(
                $"PublicValues exceeds {MaxPublicValuesBytes} bytes", nameof(manifest));
        if (manifest.L1TransactionHash is not null
            && manifest.L1TransactionHash.Equals(UInt256.Zero))
            throw new ArgumentException("L1 transaction hash must be non-zero", nameof(manifest));
        if (!Enum.IsDefined(manifest.SubmissionState))
            throw new ArgumentException(
                "Unknown proof submission state", nameof(manifest));
        if (manifest.SubmissionState == ProofSubmissionState.ProofReady
            && manifest.L1TransactionHash is not null)
            throw new ArgumentException(
                "ProofReady cannot carry an L1 transaction hash", nameof(manifest));
        if (manifest.SubmissionState == ProofSubmissionState.Submitted
            && manifest.L1TransactionHash is null)
            throw new ArgumentException(
                "Submitted requires an L1 transaction hash", nameof(manifest));
        if (manifest.ForcedInclusionFinalized && !manifest.SettlementObserved)
            throw new ArgumentException(
                "forced-inclusion finalization requires SettlementObserved", nameof(manifest));
    }
}

internal sealed class ManifestWireWriter
{
    private readonly ArrayBufferWriter<byte> _writer;

    public ManifestWireWriter(int capacity) => _writer = new ArrayBufferWriter<byte>(capacity);

    public int WrittenCount => _writer.WrittenCount;

    public void WriteByte(byte value)
    {
        _writer.GetSpan(1)[0] = value;
        _writer.Advance(1);
    }

    public void WriteUInt16(ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(_writer.GetSpan(2), value);
        _writer.Advance(2);
    }

    public void WriteUInt32(uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(_writer.GetSpan(4), value);
        _writer.Advance(4);
    }

    public void WriteUInt64(ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(_writer.GetSpan(8), value);
        _writer.Advance(8);
    }

    public void WriteUInt256(UInt256 value) => WriteBytes(value.GetSpan());

    public void WriteLengthPrefixedBytes(ReadOnlySpan<byte> value)
    {
        WriteUInt32(checked((uint)value.Length));
        WriteBytes(value);
    }

    public void WriteBytes(ReadOnlySpan<byte> value)
    {
        value.CopyTo(_writer.GetSpan(value.Length));
        _writer.Advance(value.Length);
    }

    public byte[] ToArray() => _writer.WrittenSpan.ToArray();
}

internal ref struct ManifestWireReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _position;

    public ManifestWireReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _position = 0;
    }

    public byte ReadByte(string field)
    {
        EnsureAvailable(1, field);
        return _data[_position++];
    }

    public ushort ReadUInt16(string field)
        => BinaryPrimitives.ReadUInt16LittleEndian(ReadBytes(2, field));

    public uint ReadUInt32(string field)
        => BinaryPrimitives.ReadUInt32LittleEndian(ReadBytes(4, field));

    public ulong ReadUInt64(string field)
        => BinaryPrimitives.ReadUInt64LittleEndian(ReadBytes(8, field));

    public UInt256 ReadUInt256(string field) => new(ReadBytes(UInt256.Length, field));

    public byte[] ReadLengthPrefixedBytes(int maximum, string field)
    {
        var length = ReadUInt32($"{field} length");
        if (length > maximum)
            throw new InvalidDataException($"{field} length {length} exceeds maximum {maximum}");
        return ReadBytes(checked((int)length), field).ToArray();
    }

    public ReadOnlySpan<byte> ReadBytes(int length, string field)
    {
        EnsureAvailable(length, field);
        var result = _data.Slice(_position, length);
        _position += length;
        return result;
    }

    public void RequireMagic(ReadOnlySpan<byte> expected)
    {
        if (!ReadBytes(expected.Length, "magic").SequenceEqual(expected))
            throw new InvalidDataException("Invalid proof result manifest magic");
    }

    public void EnsureEnd()
    {
        if (_position != _data.Length)
            throw new InvalidDataException(
                $"Proof result manifest has {_data.Length - _position} trailing bytes");
    }

    private void EnsureAvailable(int length, string field)
    {
        if (length < 0 || length > _data.Length - _position)
            throw new InvalidDataException($"{field} is truncated");
    }
}
