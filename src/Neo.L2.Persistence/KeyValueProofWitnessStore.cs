using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Neo.Cryptography;
using Neo.L2.Batch;

namespace Neo.L2.Persistence;

/// <summary><see cref="IL2KeyValueStore"/>-backed proof witness store.</summary>
/// <remarks>
/// See doc.md §7.5 and §8. Artifact keys are exactly
/// <c>"PWIT" | chainId:u32 LE | batchNumber:u64 LE</c>. Each value is one complete canonical
/// artifact. Production RocksDB commits use one guarded <c>CompareExchangeBatch</c>, so the
/// artifact, rollback guard, and related lifecycle preconditions share one crash-recovery point.
/// </remarks>
public sealed class KeyValueProofWitnessStore : IProofWitnessStore
{
    private static ReadOnlySpan<byte> ArtifactPrefix => "PWIT"u8;
    private static ReadOnlySpan<byte> ProofPrefix => "PWRF"u8;
    private static ReadOnlySpan<byte> SettlementRecoveryPrefix => "PWRC"u8;
    private static ReadOnlySpan<byte> QuarantinedArtifactPrefix => "PWQA"u8;
    private static ReadOnlySpan<byte> QuarantinedProofPrefix => "PWQP"u8;
    private static ReadOnlySpan<byte> SettlementRollbackPrefix => "PWRB"u8;

    private readonly IL2KeyValueStore _store;
    private readonly bool _ownsStore;
    private readonly Lock _mutationGate = new();
    private bool _disposed;

    /// <summary>Create a witness store over a shared key-value backend.</summary>
    public KeyValueProofWitnessStore(IL2KeyValueStore store, bool ownsStore = false)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _ownsStore = ownsStore;
    }

    /// <inheritdoc />
    public bool IsDurable => _store is IDurableL2KeyValueStore;

    /// <inheritdoc />
    public ValueTask CommitAsync(
        ProofWitnessArtifactV1 artifact,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(artifact);
        ThrowIfDisposed();
        var key = ArtifactKey(artifact.ChainId, artifact.BatchNumber);
        var rollbackKey = SettlementRollbackKey(artifact.ChainId);
        var encoded = ProofWitnessArtifactSerializer.Encode(artifact);

        for (var attempt = 0; attempt < 8; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryPutIfConditionsMatch(
                    key,
                    encoded,
                    [(rollbackKey, null)]))
            {
                return ValueTask.CompletedTask;
            }
            if (_store.Get(rollbackKey) is not null)
                throw new InvalidOperationException(
                    "cannot commit a batch while reverted execution state is being restored");
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
        var artifactKey = ArtifactKey(manifest.ChainId, manifest.BatchNumber);
        var rollbackKey = SettlementRollbackKey(manifest.ChainId);
        var key = ProofKey(manifest.ArtifactContentHash);
        var encoded = ProofResultManifestSerializer.Encode(manifest);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var committedBytes = _store.Get(artifactKey)
                ?? throw new InvalidOperationException(
                    $"Cannot persist proof for unknown chain {manifest.ChainId}, batch {manifest.BatchNumber}");
            var committedArtifact = ProofWitnessArtifactSerializer.Decode(committedBytes);
            ValidateManifestBinding(manifest, committedArtifact);
            if (TryPutIfConditionsMatch(
                    key,
                    encoded,
                    [(rollbackKey, null), (artifactKey, committedBytes)]))
            {
                return ValueTask.CompletedTask;
            }
            if (_store.Get(rollbackKey) is not null)
                throw new InvalidOperationException(
                    "cannot persist a proof while reverted execution state is being restored");
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
    public ValueTask MarkSettlementFinalizedAsync(
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
            if (current.SettlementFinalized) return ValueTask.CompletedTask;
            var updatedBytes = ProofResultManifestSerializer.Encode(current with
            {
                SubmissionState = ProofSubmissionState.SettlementObserved,
                SettlementFinalized = true,
            });
            if (_store.CompareExchange(key, currentBytes, updatedBytes))
                return ValueTask.CompletedTask;
        }

        throw new InvalidOperationException(
            "Proof result changed repeatedly while finalizing settlement");
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
            if (!current.SettlementFinalized)
                throw new InvalidOperationException(
                    "forced inclusion cannot finalize before settlement is finalized");
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
        var key = SettlementRecoveryKey(artifactContentHash);
        var artifactKey = ArtifactKey(chainId, batchNumber);
        var rollbackKey = SettlementRollbackKey(chainId);

        for (var attempt = 0; attempt < 32; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var artifactBytes = ValidateRecoveryArtifact(
                chainId, batchNumber, artifactContentHash);
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
                if (TryPutIfConditionsMatch(
                        key,
                        inserted,
                        [(rollbackKey, null), (artifactKey, artifactBytes)]))
                {
                    return ValueTask.FromResult(
                        SettlementRecoveryCheckpointSerializer.Decode(inserted));
                }
                if (_store.Get(rollbackKey) is not null)
                    throw new InvalidOperationException(
                        "cannot record settlement recovery while reverted execution state is being restored");
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
        _ = ValidateRecoveryArtifact(chainId, batchNumber, artifactContentHash);
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
    public ValueTask<SettlementRollbackCheckpoint> QuarantineRevertedTailAsync(
        uint chainId,
        ulong firstBatchNumber,
        UInt256 revertedArtifactContentHash,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(revertedArtifactContentHash);
        if (firstBatchNumber == 0)
            throw new ArgumentOutOfRangeException(nameof(firstBatchNumber));
        ThrowIfDisposed();
        if (_store is not IAtomicL2KeyValueStore atomicStore)
            throw new InvalidOperationException(
                "reverted-tail quarantine requires an atomic proof witness store");

        lock (_mutationGate)
        {
            var rollbackKey = SettlementRollbackKey(chainId);
            var existingRollbackBytes = _store.Get(rollbackKey);
            if (existingRollbackBytes is not null)
            {
                var existingRollback = SettlementRollbackCheckpointSerializer.Decode(
                    existingRollbackBytes);
                if (existingRollback.ChainId != chainId
                    || existingRollback.FirstBatchNumber != firstBatchNumber
                    || !existingRollback.RevertedArtifactContentHash.Equals(
                        revertedArtifactContentHash))
                {
                    throw new InvalidOperationException(
                        "a different reverted-tail rollback is already pending");
                }
                return ValueTask.FromResult(existingRollback);
            }

            for (var attempt = 0; attempt < 8; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = _store.EnumeratePrefix(ReadOnlySpan<byte>.Empty).ToList();
                var tail = current
                    .Where(pair => IsArtifactKeyForChainAtOrAfter(
                        pair.Key, chainId, firstBatchNumber))
                    .Select(pair => (Pair: pair, Artifact: ProofWitnessArtifactSerializer.Decode(pair.Value)))
                    .OrderBy(static item => item.Artifact.BatchNumber)
                    .ToArray();
                if (tail.Length == 0 || tail[0].Artifact.BatchNumber != firstBatchNumber)
                    throw new InvalidOperationException(
                        $"no canonical artifact exists for reverted batch {firstBatchNumber}");
                if (!tail[0].Artifact.ContentHash.Equals(revertedArtifactContentHash))
                    throw new InvalidOperationException(
                        "reverted artifact content hash differs from the canonical witness");
                for (var index = 1; index < tail.Length; index++)
                {
                    if (tail[index].Artifact.BatchNumber != tail[index - 1].Artifact.BatchNumber + 1)
                        throw new InvalidDataException(
                            "reverted settlement tail is not contiguous");
                }

                var checkpoint = new SettlementRollbackCheckpoint
                {
                    ChainId = chainId,
                    FirstBatchNumber = firstBatchNumber,
                    LastBatchNumber = tail[^1].Artifact.BatchNumber,
                    RevertedArtifactContentHash = revertedArtifactContentHash,
                    ExpectedCurrentStateRoot = new UInt256(
                        tail[^1].Artifact.ExecutionResult.PostStateRoot.GetSpan()),
                    TargetStateRoot = new UInt256(
                        tail[0].Artifact.ExecutionPayload.PreStateRoot.GetSpan()),
                };
                var replacement = new SortedDictionary<byte[], byte[]>(
                    LexicographicByteArrayComparer.Instance);
                foreach (var pair in current)
                    replacement.Add(pair.Key, pair.Value);
                foreach (var (pair, artifact) in tail)
                {
                    replacement.Remove(pair.Key);
                    PutExact(
                        replacement,
                        QuarantinedArtifactKey(artifact.ContentHash),
                        pair.Value,
                        "quarantined proof witness");

                    var proofKey = ProofKey(artifact.ContentHash);
                    if (replacement.Remove(proofKey, out var proofBytes))
                    {
                        PutExact(
                            replacement,
                            QuarantinedProofKey(artifact.ContentHash),
                            proofBytes,
                            "quarantined proof manifest");
                    }
                    replacement.Remove(SettlementRecoveryKey(artifact.ContentHash));
                }
                replacement.Add(
                    rollbackKey,
                    SettlementRollbackCheckpointSerializer.Encode(checkpoint));

                if (atomicStore.CompareExchangeAll(
                    current.Select(static pair =>
                        ((ReadOnlyMemory<byte>)pair.Key, (ReadOnlyMemory<byte>)pair.Value)),
                    replacement.Select(static pair =>
                        ((ReadOnlyMemory<byte>)pair.Key, (ReadOnlyMemory<byte>)pair.Value))))
                {
                    return ValueTask.FromResult(checkpoint);
                }
            }
        }

        throw new InvalidOperationException(
            "proof witness store changed repeatedly while quarantining reverted settlement");
    }

    /// <inheritdoc />
    public ValueTask<SettlementRollbackCheckpoint?> GetSettlementRollbackAsync(
        uint chainId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        var bytes = _store.Get(SettlementRollbackKey(chainId));
        if (bytes is null)
            return new ValueTask<SettlementRollbackCheckpoint?>((SettlementRollbackCheckpoint?)null);
        var checkpoint = SettlementRollbackCheckpointSerializer.Decode(bytes);
        if (checkpoint.ChainId != chainId)
            throw new InvalidDataException(
                "settlement rollback identity does not match its storage key");
        return new ValueTask<SettlementRollbackCheckpoint?>(checkpoint);
    }

    /// <inheritdoc />
    public ValueTask<ProofWitnessArtifactV1?> GetQuarantinedArtifactAsync(
        UInt256 artifactContentHash,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(artifactContentHash);
        ThrowIfDisposed();
        var bytes = _store.Get(QuarantinedArtifactKey(artifactContentHash));
        if (bytes is null)
            return new ValueTask<ProofWitnessArtifactV1?>((ProofWitnessArtifactV1?)null);
        var artifact = ProofWitnessArtifactSerializer.Decode(bytes);
        if (!artifact.ContentHash.Equals(artifactContentHash))
            throw new InvalidDataException(
                "quarantined proof witness identity does not match its storage key");
        return new ValueTask<ProofWitnessArtifactV1?>(artifact);
    }

    /// <inheritdoc />
    public ValueTask CompleteSettlementRollbackAsync(
        SettlementRollbackCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(checkpoint);
        ThrowIfDisposed();
        if (_store is not IAtomicL2KeyValueStore atomicStore)
            throw new InvalidOperationException(
                "settlement rollback completion requires an atomic proof witness store");
        var expectedRollbackBytes = SettlementRollbackCheckpointSerializer.Encode(checkpoint);
        var rollbackKey = SettlementRollbackKey(checkpoint.ChainId);

        for (var attempt = 0; attempt < 8; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var persisted = _store.Get(rollbackKey);
            if (persisted is null)
                return ValueTask.CompletedTask;
            if (!persisted.AsSpan().SequenceEqual(expectedRollbackBytes))
                throw new InvalidOperationException(
                    "persisted settlement rollback differs from the completion target");
            if (atomicStore.CompareExchangeBatch(
                [(rollbackKey, (ReadOnlyMemory<byte>?)persisted)],
                [(rollbackKey, (ReadOnlyMemory<byte>?)null)]))
            {
                return ValueTask.CompletedTask;
            }
        }

        throw new InvalidOperationException(
            "proof witness store changed repeatedly while completing settlement rollback");
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

    private static byte[] QuarantinedArtifactKey(UInt256 artifactContentHash)
    {
        var key = new byte[QuarantinedArtifactPrefix.Length + UInt256.Length];
        QuarantinedArtifactPrefix.CopyTo(key);
        artifactContentHash.GetSpan().CopyTo(key.AsSpan(QuarantinedArtifactPrefix.Length));
        return key;
    }

    private static byte[] QuarantinedProofKey(UInt256 artifactContentHash)
    {
        var key = new byte[QuarantinedProofPrefix.Length + UInt256.Length];
        QuarantinedProofPrefix.CopyTo(key);
        artifactContentHash.GetSpan().CopyTo(key.AsSpan(QuarantinedProofPrefix.Length));
        return key;
    }

    private static byte[] SettlementRollbackKey(uint chainId)
    {
        var key = new byte[SettlementRollbackPrefix.Length + sizeof(uint)];
        SettlementRollbackPrefix.CopyTo(key);
        BinaryPrimitives.WriteUInt32LittleEndian(
            key.AsSpan(SettlementRollbackPrefix.Length), chainId);
        return key;
    }

    private static bool IsArtifactKeyForChainAtOrAfter(
        byte[] key,
        uint chainId,
        ulong firstBatchNumber)
        => key.Length == ArtifactPrefix.Length + sizeof(uint) + sizeof(ulong)
            && key.AsSpan().StartsWith(ArtifactPrefix)
            && BinaryPrimitives.ReadUInt32LittleEndian(
                key.AsSpan(ArtifactPrefix.Length, sizeof(uint))) == chainId
            && BinaryPrimitives.ReadUInt64LittleEndian(
                key.AsSpan(ArtifactPrefix.Length + sizeof(uint), sizeof(ulong)))
                >= firstBatchNumber;

    private static void PutExact(
        IDictionary<byte[], byte[]> entries,
        byte[] key,
        byte[] value,
        string description)
    {
        if (entries.TryGetValue(key, out var existing))
        {
            if (!existing.AsSpan().SequenceEqual(value))
                throw new InvalidDataException($"conflicting {description} archive");
            return;
        }
        entries.Add(key, value);
    }

    private byte[] ValidateRecoveryArtifact(
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
        return artifactBytes;
    }

    private bool TryPutIfConditionsMatch(
        byte[] key,
        byte[] value,
        IReadOnlyList<(byte[] Key, byte[]? ExpectedValue)> conditions)
    {
        if (_store is IAtomicL2KeyValueStore atomicStore)
        {
            var expected = conditions
                .Select(static condition =>
                    (Key: (ReadOnlyMemory<byte>)condition.Key,
                     ExpectedValue: condition.ExpectedValue is null
                        ? (ReadOnlyMemory<byte>?)null
                        : condition.ExpectedValue))
                .Append((key, (ReadOnlyMemory<byte>?)null));
            return atomicStore.CompareExchangeBatch(
                expected,
                [(key, (ReadOnlyMemory<byte>?)value)]);
        }

        lock (_mutationGate)
        {
            foreach (var condition in conditions)
            {
                var current = _store.Get(condition.Key);
                if (condition.ExpectedValue is null)
                {
                    if (current is not null) return false;
                }
                else if (current is null
                    || !current.AsSpan().SequenceEqual(condition.ExpectedValue))
                {
                    return false;
                }
            }
            return _store.TryPut(key, value);
        }
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
