using Neo.Cryptography;
using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.Executor.ProofWitness;
using Neo.L2.Persistence;
using Neo.L2.Proving.RiscVZk;
using Neo.L2.State;
using Neo.L2.Telemetry;

namespace Neo.Plugins.L2;

/// <summary>Operator-facing durable settlement queue and poison status.</summary>
/// <remarks>See doc.md §7.5, §15.1, and §17.</remarks>
public sealed record SettlementRecoveryStatus
{
    /// <summary>Number of canonical artifacts not fully reconciled on L1.</summary>
    public required int PendingCount { get; init; }

    /// <summary>Number of canonical batches not yet finalized through L1 settlement state.</summary>
    public required int ConfirmationLagBatches { get; init; }

    /// <summary>Retry or poison state of the earliest failed artifact.</summary>
    public SettlementRecoveryState? State { get; init; }

    /// <summary>Batch number of the earliest failed artifact.</summary>
    public ulong? BlockedBatchNumber { get; init; }

    /// <summary>Content hash of the earliest failed artifact.</summary>
    public UInt256? ArtifactContentHash { get; init; }

    /// <summary>Consecutive failed reconciliation attempts.</summary>
    public required int RetryCount { get; init; }

    /// <summary>Most recent durable failure.</summary>
    public string? LastError { get; init; }

    /// <summary>UTC Unix milliseconds of the first consecutive failure.</summary>
    public long? FirstFailureAtUnixMilliseconds { get; init; }

    /// <summary>UTC Unix milliseconds of the most recent failure.</summary>
    public long? LastFailureAtUnixMilliseconds { get; init; }
}

/// <summary>Raised when durable settlement is quarantined pending operator remediation.</summary>
/// <remarks>See doc.md §7.5, §15.1, and §17.</remarks>
public sealed class SettlementPoisonedException : InvalidOperationException
{
    /// <summary>Create an exception bound to the exact durable poison checkpoint.</summary>
    public SettlementPoisonedException(SettlementRecoveryCheckpoint recovery)
        : base(BuildMessage(recovery))
    {
        Recovery = recovery;
    }

    /// <summary>Exact durable recovery checkpoint that blocked reconciliation.</summary>
    public SettlementRecoveryCheckpoint Recovery { get; }

    private static string BuildMessage(SettlementRecoveryCheckpoint recovery)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        return $"settlement batch {recovery.BatchNumber} is poisoned after " +
            $"{recovery.RetryCount} failures; inspect settlement status and call " +
            "RecoverPoisonedBatchAsync after remediation";
    }
}

/// <summary>Explicit proof and DA profile for the canonical settlement pipeline.</summary>
/// <remarks>See doc.md §7.4, §7.5, §8, and §15.1.</remarks>
public sealed record ProofWitnessPipelineProfile
{
    /// <summary>L2 chain handled by this pipeline instance.</summary>
    public required uint ChainId { get; init; }

    /// <summary>Settlement proof type.</summary>
    public required ProofType ProofType { get; init; }

    /// <summary>ZK backend, or <see cref="WitnessProofSystem.None"/> for legacy profiles.</summary>
    public required WitnessProofSystem ProofSystem { get; init; }

    /// <summary>Verification-key identifier, or zero for legacy profiles.</summary>
    public required UInt256 VerificationKeyId { get; init; }

    /// <summary>Immutable authenticated state root that batch 1 must build on.</summary>
    public required UInt256 GenesisStateRoot { get; init; }

    /// <summary>Require a public production DA writer rather than local/simulated durability.</summary>
    public required bool RequireProductionDA { get; init; }

    /// <summary>Require non-zero finalized height, timestamps, committee hash, and network.</summary>
    public required bool RequireCanonicalBlockContext { get; init; }

    /// <summary>Create a production ZK profile.</summary>
    public static ProofWitnessPipelineProfile Zk(
        uint chainId,
        WitnessProofSystem proofSystem,
        UInt256 verificationKeyId,
        UInt256 genesisStateRoot) => new()
        {
            ChainId = chainId,
            ProofType = ProofType.Zk,
            ProofSystem = proofSystem,
            VerificationKeyId = verificationKeyId,
            GenesisStateRoot = genesisStateRoot,
            RequireProductionDA = true,
            RequireCanonicalBlockContext = true,
        };

    /// <summary>Create an explicitly isolated multisig or optimistic compatibility profile.</summary>
    public static ProofWitnessPipelineProfile Legacy(
        uint chainId,
        ProofType proofType,
        UInt256 genesisStateRoot,
        bool requireProductionDA = false) => new()
        {
            ChainId = chainId,
            ProofType = proofType,
            ProofSystem = WitnessProofSystem.None,
            VerificationKeyId = UInt256.Zero,
            GenesisStateRoot = genesisStateRoot,
            RequireProductionDA = requireProductionDA,
            RequireCanonicalBlockContext = false,
        };

    internal void Validate()
    {
        ChainIdValidator.ValidateL2(ChainId);
        ArgumentNullException.ThrowIfNull(VerificationKeyId);
        ArgumentNullException.ThrowIfNull(GenesisStateRoot);
        if (GenesisStateRoot.Equals(UInt256.Zero))
            throw new ArgumentException("settlement profile requires a non-zero genesis state root");
        if (ProofType == ProofType.Zk)
        {
            if (ProofSystem == WitnessProofSystem.None || !Enum.IsDefined(ProofSystem))
                throw new ArgumentException("ZK profile requires a concrete proof system");
            if (VerificationKeyId.Equals(UInt256.Zero))
                throw new ArgumentException("ZK profile requires a non-zero verification key");
            if (!RequireProductionDA || !RequireCanonicalBlockContext)
                throw new ArgumentException(
                    "ZK profile requires production DA and canonical block context");
            return;
        }

        if (ProofType is not (ProofType.Multisig or ProofType.Optimistic)
            || ProofSystem != WitnessProofSystem.None
            || !VerificationKeyId.Equals(UInt256.Zero))
            throw new ArgumentException(
                "Legacy profile must be explicit multisig/optimistic with no ZK backend or VK");
    }
}

/// <summary>
/// The single durable execution, DA, witness, proving, and settlement coordinator.
/// </summary>
/// <remarks>
/// See doc.md §7.2, §7.4, §7.5, §8, §12, and §15.1. The committed
/// <see cref="IProofWitnessStore"/> is the queue of record; no in-memory commitment queue is
/// authoritative.
/// </remarks>
public sealed class CanonicalSettlementPipeline : IDisposable
{
    private const int DefaultMaxAutomaticRetries = 3;
    private readonly IProofWitnessBatchExecutor _executor;
    private readonly IDAWriter _daWriter;
    private readonly IProofWitnessStore _store;
    private readonly IL2Prover _prover;
    private readonly ISettlementClient _client;
    private readonly IForcedInclusionFinalizationClient? _forcedInclusionFinalizer;
    private readonly ProofWitnessPipelineProfile _profile;
    private readonly int _maxAutomaticRetries;
    private readonly SemaphoreSlim _reconcileGate = new(1, 1);
    private IL2Metrics _metrics;
    private bool _disposed;

    /// <summary>Create a canonical pipeline from explicit production seams.</summary>
    public CanonicalSettlementPipeline(
        IProofWitnessBatchExecutor executor,
        IDAWriter daWriter,
        IProofWitnessStore store,
        IL2Prover prover,
        ISettlementClient client,
        ProofWitnessPipelineProfile profile,
        IL2Metrics? metrics = null,
        IForcedInclusionFinalizationClient? forcedInclusionFinalizer = null,
        int maxAutomaticRetries = DefaultMaxAutomaticRetries)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(daWriter);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(prover);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        if (prover.Kind != profile.ProofType)
            throw new ArgumentException(
                $"prover kind {prover.Kind} does not match profile {profile.ProofType}",
                nameof(prover));
        if (daWriter.ReceiptKind == DAReceiptKind.Unspecified)
            throw new ArgumentException(
                "DA writer must declare a receipt evidence kind", nameof(daWriter));
        if (profile.RequireProductionDA && daWriter is not IProductionDAWriter)
            throw new ArgumentException(
                "profile requires an IProductionDAWriter", nameof(daWriter));
        if (maxAutomaticRetries is < 1 or > 100)
            throw new ArgumentOutOfRangeException(
                nameof(maxAutomaticRetries), "maxAutomaticRetries must be in [1, 100]");

        _executor = executor;
        _daWriter = daWriter;
        _store = store;
        _prover = prover;
        _client = client;
        _forcedInclusionFinalizer = forcedInclusionFinalizer;
        _profile = profile;
        _maxAutomaticRetries = maxAutomaticRetries;
        _metrics = metrics ?? NoOpMetrics.Instance;
        ValidateProverProfile();
    }

    /// <summary>Replace the telemetry sink without changing durable pipeline state.</summary>
    public void WithMetrics(IL2Metrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        _metrics = metrics;
    }

    /// <summary>
    /// Execute a sealed batch once, publish its payload, create the canonical artifact, and
    /// atomically persist it before returning the post-state root.
    /// </summary>
    public async ValueTask<UInt256> PersistAsync(
        SealedBatch batch,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(batch);
        await _reconcileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsurePendingRollbackCompletedAsync(cancellationToken).ConfigureAwait(false);
            return await PersistUnderGateAsync(batch, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ReleaseReconcileGate();
        }
    }

    private async ValueTask<UInt256> PersistUnderGateAsync(
        SealedBatch batch,
        CancellationToken cancellationToken)
    {
        if (batch.ChainId != _profile.ChainId)
            throw new InvalidOperationException(
                $"sealed batch chain {batch.ChainId} does not match pipeline chain {_profile.ChainId}");
        ValidateBlockContext(batch.BlockContext);
        await ValidateBatchContinuityAsync(batch, cancellationToken).ConfigureAwait(false);

        var existing = await _store.GetAsync(
            batch.ChainId, batch.BatchNumber, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            ValidateCommittedArtifact(existing);
            await ValidateDaReceiptAsync(
                existing.DAReceipt,
                ExecutionPayloadSerializer.ComputeCommitment(existing.ExecutionPayload),
                cancellationToken).ConfigureAwait(false);
            if (!existing.ExecutionPayload.Equals(batch.ToExecutionPayload()))
                throw new InvalidOperationException(
                    "persisted artifact payload conflicts with the sealed batch");
            await EnsureExecutionStateCommittedAsync(existing, cancellationToken)
                .ConfigureAwait(false);
            return existing.ExecutionResult.PostStateRoot;
        }

        var execution = await _executor
            .ApplyBatchWithWitnessAsync(batch, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "IProofWitnessBatchExecutor returned null");
        ValidateExecution(batch, execution);

        var payload = batch.ToExecutionPayload();
        var payloadBytes = ExecutionPayloadSerializer.Encode(payload);
        var expectedDaCommitment = ExecutionPayloadSerializer.ComputeCommitment(payloadBytes);
        var receipt = await _daWriter.PublishAsync(new DAPublishRequest
        {
            ChainId = batch.ChainId,
            BatchNumber = batch.BatchNumber,
            Payload = payloadBytes,
        }, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("IDAWriter.PublishAsync returned null");
        await ValidateDaReceiptAsync(
            receipt, expectedDaCommitment, cancellationToken).ConfigureAwait(false);

        var publicInputs = BuildPublicInputs(batch, execution.ExecutionResult, receipt.Commitment);
        var artifact = ProofWitnessArtifactV1.Create(
            _profile.ProofType,
            _profile.ProofSystem,
            _profile.VerificationKeyId,
            execution.ExecutionSemanticId,
            execution.WitnessAuthenticated,
            batch.ChainId,
            batch.BatchNumber,
            batch.FirstBlock,
            batch.LastBlock,
            payload,
            execution.StateWitness,
            execution.ExecutionResult,
            execution.Effects,
            receipt,
            publicInputs);
        ValidateCommittedArtifact(artifact);
        var artifactBytes = ProofWitnessArtifactSerializer.Encode(artifact);
        if (artifactBytes.Length == 0)
            throw new InvalidOperationException("canonical artifact bytes are empty");

        await _store.CommitAsync(artifact, cancellationToken).ConfigureAwait(false);
        var persisted = await _store.GetAsync(
            artifact.ChainId, artifact.BatchNumber, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "proof witness store did not return the committed artifact");
        ValidateCommittedArtifact(persisted);
        if (!persisted.ContentHash.Equals(artifact.ContentHash)
            || !ProofWitnessArtifactSerializer.Encode(persisted).AsSpan().SequenceEqual(artifactBytes))
            throw new InvalidOperationException(
                "persisted artifact differs from the canonical committed bytes");
        await EnsureExecutionStateCommittedAsync(persisted, cancellationToken)
            .ConfigureAwait(false);
        await EmitRecoveryMetricsAsync(cancellationToken).ConfigureAwait(false);
        return persisted.ExecutionResult.PostStateRoot;
    }

    /// <summary>
    /// Recover committed artifacts in order, prove missing manifests, reconcile L1 state, and
    /// submit batches that are still unknown to settlement.
    /// </summary>
    public async Task ReconcileAsync(
        int maximumBatches = int.MaxValue,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (maximumBatches <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumBatches));
        await _reconcileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsurePendingRollbackCompletedAsync(cancellationToken).ConfigureAwait(false);
            var processed = 0;
            var expectedBatchNumber = 1UL;
            await foreach (var artifact in _store.EnumerateCommittedAsync(
                _profile.ChainId, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var recovery = await _store.GetSettlementRecoveryAsync(
                    artifact.ContentHash, cancellationToken).ConfigureAwait(false);
                if (recovery?.State == SettlementRecoveryState.Poisoned)
                {
                    await EmitRecoveryMetricsAsync(cancellationToken).ConfigureAwait(false);
                    throw new SettlementPoisonedException(recovery);
                }

                try
                {
                    if (artifact.BatchNumber != expectedBatchNumber)
                        throw new InvalidDataException(
                            $"durable settlement is non-contiguous: expected batch " +
                            $"{expectedBatchNumber}, got {artifact.BatchNumber}");
                    if (expectedBatchNumber == ulong.MaxValue)
                        throw new InvalidDataException(
                            "durable settlement cannot advance beyond the maximum batch number");
                    expectedBatchNumber++;
                    ValidateCommittedArtifact(artifact);
                    await ValidateDaReceiptAsync(
                        artifact.DAReceipt,
                        ExecutionPayloadSerializer.ComputeCommitment(artifact.ExecutionPayload),
                        cancellationToken).ConfigureAwait(false);
                    var manifest = await _store.GetProofAsync(
                        artifact.ContentHash, cancellationToken).ConfigureAwait(false);
                    if (manifest is null)
                        manifest = await ProveCommittedArtifactAsync(
                            artifact, cancellationToken).ConfigureAwait(false);
                    ValidateManifest(artifact, manifest);
                    var hasForcedNonces =
                        artifact.ExecutionPayload.ForcedInclusions.Count != 0;
                    var needsSettlement = !manifest.SettlementFinalized;
                    var needsForcedFinalization =
                        hasForcedNonces && !manifest.ForcedInclusionFinalized;
                    if (!needsSettlement && !needsForcedFinalization)
                    {
                        await AcknowledgeProofArtifactsAsync(
                            artifact, cancellationToken).ConfigureAwait(false);
                        await ClearRecoveryAsync(artifact, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    BatchStatus? status = null;
                    if (needsSettlement)
                    {
                        status = await _client.GetBatchStatusAsync(
                            artifact.ChainId,
                            artifact.BatchNumber,
                            cancellationToken).ConfigureAwait(false);
                        if (status == BatchStatus.Reverted)
                        {
                            await RollBackRevertedTailAsync(artifact, cancellationToken)
                                .ConfigureAwait(false);
                            processed++;
                            break;
                        }
                        if (status == BatchStatus.Unknown)
                        {
                            if (manifest.SettlementObserved)
                                throw new InvalidOperationException(
                                    "previously observed settlement batch is now unknown; automatic resubmission is disabled");
                            manifest = await ReconcileUnknownSubmissionAsync(
                                artifact, manifest, cancellationToken).ConfigureAwait(false);
                        }
                        else if (status == BatchStatus.Finalized)
                        {
                            await _store.MarkSettlementFinalizedAsync(
                                artifact.ContentHash, cancellationToken).ConfigureAwait(false);
                            manifest = await ReadManifestAsync(
                                artifact.ContentHash, cancellationToken).ConfigureAwait(false);
                        }
                        else if (status is BatchStatus.Pending or BatchStatus.Challengeable)
                        {
                            await _store.MarkSubmissionObservedAsync(
                                artifact.ContentHash, cancellationToken).ConfigureAwait(false);
                            manifest = await ReadManifestAsync(
                                artifact.ContentHash, cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            throw new InvalidDataException(
                                $"settlement client returned unknown batch status {status}");
                        }

                        if (!manifest.SettlementFinalized)
                        {
                            await ClearRecoveryAsync(artifact, cancellationToken).ConfigureAwait(false);
                            processed++;
                            break;
                        }
                    }

                    if (hasForcedNonces && !manifest.ForcedInclusionFinalized)
                    {
                        status ??= await _client.GetBatchStatusAsync(
                            artifact.ChainId,
                            artifact.BatchNumber,
                            cancellationToken).ConfigureAwait(false);
                        if (status == BatchStatus.Reverted)
                        {
                            await RollBackRevertedTailAsync(artifact, cancellationToken)
                                .ConfigureAwait(false);
                            processed++;
                            break;
                        }
                        if (status == BatchStatus.Finalized)
                        {
                            var finalizer = _forcedInclusionFinalizer
                                ?? throw new InvalidOperationException(
                                    "finalized forced-inclusion batch requires an L1 finalization client");
                            await finalizer.ConsumeAndConfirmAsync(
                                artifact.ChainId,
                                artifact.BatchNumber,
                                artifact.ExecutionPayload.ForcedInclusions,
                                cancellationToken).ConfigureAwait(false);
                            await _store.MarkForcedInclusionFinalizedAsync(
                                artifact.ContentHash, cancellationToken).ConfigureAwait(false);
                        }
                    }

                    if (manifest.SettlementFinalized)
                        await AcknowledgeProofArtifactsAsync(
                            artifact, cancellationToken).ConfigureAwait(false);
                    await ClearRecoveryAsync(artifact, cancellationToken).ConfigureAwait(false);
                    processed++;
                    if (processed >= maximumBatches) break;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException
                    and not StackOverflowException)
                {
                    if (await _store.GetSettlementRollbackAsync(
                        _profile.ChainId, cancellationToken).ConfigureAwait(false) is null)
                    {
                        await RecordFailureAsync(artifact, exception, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    throw;
                }
            }
            await EmitRecoveryMetricsAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                _reconcileGate.Release();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    /// <summary>Count committed artifacts that have not been observed on L1.</summary>
    public async ValueTask<int> GetPendingCountAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var count = 0;
        await foreach (var artifact in _store.EnumerateCommittedAsync(
            _profile.ChainId, cancellationToken))
        {
            var manifest = await _store.GetProofAsync(
                artifact.ContentHash, cancellationToken).ConfigureAwait(false);
            if (manifest?.SettlementFinalized != true
                || (artifact.ExecutionPayload.ForcedInclusions.Count != 0
                    && manifest.ForcedInclusionFinalized != true))
                count++;
        }
        return count;
    }

    /// <summary>Read durable pending, retry, and poison state for operators and health checks.</summary>
    public async ValueTask<SettlementRecoveryStatus> GetRecoveryStatusAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await ReadRecoveryStatusAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<SettlementRecoveryStatus> ReadRecoveryStatusAsync(
        CancellationToken cancellationToken)
    {
        var pendingCount = 0;
        var confirmationLagBatches = 0;
        SettlementRecoveryCheckpoint? earliestRecovery = null;
        await foreach (var artifact in _store.EnumerateCommittedAsync(
            _profile.ChainId, cancellationToken))
        {
            var manifest = await _store.GetProofAsync(
                artifact.ContentHash, cancellationToken).ConfigureAwait(false);
            if (manifest?.SettlementFinalized != true)
                confirmationLagBatches++;
            var pending = manifest?.SettlementFinalized != true
                || (artifact.ExecutionPayload.ForcedInclusions.Count != 0
                    && manifest.ForcedInclusionFinalized != true);
            if (!pending) continue;
            pendingCount++;
            earliestRecovery ??= await _store.GetSettlementRecoveryAsync(
                artifact.ContentHash, cancellationToken).ConfigureAwait(false);
        }

        return new SettlementRecoveryStatus
        {
            PendingCount = pendingCount,
            ConfirmationLagBatches = confirmationLagBatches,
            State = earliestRecovery?.State,
            BlockedBatchNumber = earliestRecovery?.BatchNumber,
            ArtifactContentHash = earliestRecovery?.ArtifactContentHash,
            RetryCount = earliestRecovery?.RetryCount ?? 0,
            LastError = earliestRecovery?.LastError,
            FirstFailureAtUnixMilliseconds = earliestRecovery?.FirstFailureAtUnixMilliseconds,
            LastFailureAtUnixMilliseconds = earliestRecovery?.LastFailureAtUnixMilliseconds,
        };
    }

    /// <summary>
    /// Reset the exact poisoned artifact after the operator corrects its prover, DA, RPC, or L1
    /// state. The canonical artifact and proof/submission reconciliation state are retained.
    /// </summary>
    public async Task RecoverPoisonedBatchAsync(
        ulong batchNumber,
        UInt256 artifactContentHash,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(artifactContentHash);
        await _reconcileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var artifact = await _store.GetAsync(
                _profile.ChainId, batchNumber, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"No canonical settlement artifact exists for batch {batchNumber}");
            if (!artifact.ContentHash.Equals(artifactContentHash))
                throw new InvalidOperationException(
                    "Operator recovery content hash does not match the canonical artifact");
            await _store.ResetSettlementRecoveryAsync(
                artifact.ChainId,
                artifact.BatchNumber,
                artifact.ContentHash,
                cancellationToken).ConfigureAwait(false);
            await EmitRecoveryMetricsAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ReleaseReconcileGate();
        }
    }

    /// <summary>Return forced-inclusion nonces tracked by durable artifacts.</summary>
    public ValueTask<IReadOnlyCollection<ulong>> GetTrackedForcedInclusionNoncesAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _store.GetTrackedForcedInclusionNoncesAsync(
            _profile.ChainId, cancellationToken);
    }

    /// <summary>Return the authenticated state root preceding batch 1.</summary>
    public async ValueTask<UInt256> GetInitialStateRootAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (_executor is IInitialStateRootProvider provider)
        {
            var executorRoot = await provider.GetInitialStateRootAsync(cancellationToken)
                .ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(executorRoot);
            if (!executorRoot.Equals(_profile.GenesisStateRoot))
                throw new InvalidDataException(
                    "executor initial state root differs from the settlement profile genesis root");
        }
        else if (_profile.ProofType == ProofType.Zk)
        {
            throw new InvalidOperationException(
                "ZK execution requires an IInitialStateRootProvider");
        }
        return new UInt256(_profile.GenesisStateRoot.GetSpan());
    }

    /// <summary>
    /// Recover the latest continuously committed batch checkpoint from canonical artifacts.
    /// </summary>
    public async ValueTask<SealedBatchCheckpoint?> GetLatestCheckpointAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _reconcileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsurePendingRollbackCompletedAsync(cancellationToken).ConfigureAwait(false);
            await RefreshSettlementLifecycleForCheckpointAsync(cancellationToken)
                .ConfigureAwait(false);
            return await GetLatestCheckpointUnderGateAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            ReleaseReconcileGate();
        }
    }

    private async ValueTask<SealedBatchCheckpoint?> GetLatestCheckpointUnderGateAsync(
        CancellationToken cancellationToken)
    {
        var initialStateRoot = await GetInitialStateRootAsync(cancellationToken)
            .ConfigureAwait(false);
        SealedBatchCheckpoint? latest = null;
        ProofWitnessArtifactV1? latestArtifact = null;
        var expectedBatchNumber = 1UL;
        await foreach (var artifact in _store.EnumerateCommittedAsync(
            _profile.ChainId, cancellationToken))
        {
            ValidateCommittedArtifact(artifact);
            if (artifact.BatchNumber != expectedBatchNumber)
                throw new InvalidDataException(
                    $"durable batch checkpoint is non-contiguous: expected batch {expectedBatchNumber}, got {artifact.BatchNumber}");
            if (latest is null
                && !artifact.ExecutionPayload.PreStateRoot.Equals(initialStateRoot))
                throw new InvalidDataException(
                    "durable first batch does not link the authenticated genesis state root");
            if (latest is not null)
            {
                var expectedFirstBlock = checked(latest.LastBlock + 1);
                if (artifact.FirstBlock != expectedFirstBlock)
                    throw new InvalidDataException(
                        $"durable block checkpoint is non-contiguous: expected block {expectedFirstBlock}, got {artifact.FirstBlock}");
                if (!artifact.ExecutionPayload.PreStateRoot.Equals(latest.PostStateRoot))
                    throw new InvalidDataException(
                        "durable state checkpoint does not link the prior post-state root to the next pre-state root");
            }
            if (artifact.BatchNumber == ulong.MaxValue)
                throw new InvalidDataException(
                    "durable batch checkpoint cannot advance beyond the maximum batch number");
            latest = new SealedBatchCheckpoint(
                artifact.BatchNumber,
                artifact.LastBlock,
                new UInt256(artifact.ExecutionResult.PostStateRoot.GetSpan()));
            latestArtifact = artifact;
            expectedBatchNumber++;
        }
        if (latestArtifact is not null)
        {
            await EnsureExecutionStateCommittedAsync(latestArtifact, cancellationToken)
                .ConfigureAwait(false);
        }
        else if (_executor is ICurrentStateRootProvider currentStateRootProvider)
        {
            var currentStateRoot = await currentStateRootProvider
                .GetCurrentStateRootAsync(cancellationToken).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(currentStateRoot);
            if (!currentStateRoot.Equals(initialStateRoot))
                throw new InvalidDataException(
                    "execution state advanced without a durable proof artifact");
        }
        return latest;
    }

    private async ValueTask RefreshSettlementLifecycleForCheckpointAsync(
        CancellationToken cancellationToken)
    {
        var artifacts = new List<ProofWitnessArtifactV1>();
        await foreach (var artifact in _store.EnumerateCommittedAsync(
            _profile.ChainId, cancellationToken))
            artifacts.Add(artifact);

        var encounteredUnfinalizedBatch = false;
        UInt256 expectedCanonicalRoot = _profile.GenesisStateRoot;
        foreach (var artifact in artifacts)
        {
            var manifest = await _store.GetProofAsync(
                artifact.ContentHash, cancellationToken).ConfigureAwait(false);
            var status = await _client.GetBatchStatusAsync(
                artifact.ChainId, artifact.BatchNumber, cancellationToken).ConfigureAwait(false);
            if (status == BatchStatus.Reverted)
            {
                await RollBackRevertedTailAsync(artifact, cancellationToken)
                    .ConfigureAwait(false);
                await RefreshSettlementLifecycleForCheckpointAsync(cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            if (manifest is null)
            {
                if (status != BatchStatus.Unknown)
                    throw new InvalidDataException(
                        "L1 settlement contains a batch without its durable local proof manifest");
                encounteredUnfinalizedBatch = true;
                continue;
            }
            ValidateManifest(artifact, manifest);
            switch (status)
            {
                case BatchStatus.Finalized:
                    if (encounteredUnfinalizedBatch)
                        throw new InvalidDataException(
                            "L1 settlement finality is not a contiguous batch prefix");
                    await _store.MarkSettlementFinalizedAsync(
                        artifact.ContentHash, cancellationToken).ConfigureAwait(false);
                    expectedCanonicalRoot = artifact.ExecutionResult.PostStateRoot;
                    break;
                case BatchStatus.Pending:
                case BatchStatus.Challengeable:
                    if (manifest.SettlementFinalized)
                        throw new InvalidDataException(
                            "L1 settlement status regressed after local finalization");
                    await _store.MarkSubmissionObservedAsync(
                        artifact.ContentHash, cancellationToken).ConfigureAwait(false);
                    encounteredUnfinalizedBatch = true;
                    break;
                case BatchStatus.Unknown:
                    if (manifest.SettlementObserved || manifest.SettlementFinalized)
                        throw new InvalidDataException(
                            "locally observed settlement is missing from L1 during checkpoint recovery");
                    encounteredUnfinalizedBatch = true;
                    break;
                default:
                    throw new InvalidDataException(
                        $"settlement client returned unknown batch status {status}");
            }
        }

        var l1CanonicalRoot = await _client.GetCanonicalStateRootAsync(
            _profile.ChainId, cancellationToken).ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(l1CanonicalRoot);
        if (!l1CanonicalRoot.Equals(expectedCanonicalRoot))
            throw new InvalidDataException(
                "L1 canonical state root differs from the latest finalized local artifact");
    }

    private ValueTask EnsureExecutionStateCommittedAsync(
        ProofWitnessArtifactV1 artifact,
        CancellationToken cancellationToken)
        => _executor is ICommittedProofWitnessStateSink stateSink
            ? stateSink.EnsureStateCommittedAsync(_store, artifact, cancellationToken)
            : ValueTask.CompletedTask;

    private async ValueTask RollBackRevertedTailAsync(
        ProofWitnessArtifactV1 revertedArtifact,
        CancellationToken cancellationToken)
    {
        ProofWitnessArtifactV1? latestArtifact = null;
        await foreach (var artifact in _store.EnumerateCommittedAsync(
            revertedArtifact.ChainId, cancellationToken))
        {
            if (artifact.BatchNumber >= revertedArtifact.BatchNumber)
                latestArtifact = artifact;
        }
        if (latestArtifact is null)
            throw new InvalidOperationException(
                "reverted settlement tail has no canonical witness artifact");
        await EnsureExecutionStateCommittedAsync(latestArtifact, cancellationToken)
            .ConfigureAwait(false);
        await _store.QuarantineRevertedTailAsync(
            revertedArtifact.ChainId,
            revertedArtifact.BatchNumber,
            revertedArtifact.ContentHash,
            cancellationToken).ConfigureAwait(false);
        await EnsurePendingRollbackCompletedAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask EnsurePendingRollbackCompletedAsync(
        CancellationToken cancellationToken)
    {
        var checkpoint = await _store.GetSettlementRollbackAsync(
            _profile.ChainId, cancellationToken).ConfigureAwait(false);
        if (checkpoint is null) return;
        if (_executor is not IRevertibleCommittedProofWitnessStateSink stateSink)
            throw new InvalidOperationException(
                "reverted settlement requires an execution-state rollback sink");
        await stateSink.EnsureStateRolledBackAsync(
            _store, checkpoint, cancellationToken).ConfigureAwait(false);
        await _store.CompleteSettlementRollbackAsync(checkpoint, cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask ValidateBatchContinuityAsync(
        SealedBatch batch,
        CancellationToken cancellationToken)
    {
        if (batch.BatchNumber == 0)
            throw new InvalidDataException(
                "batch number 0 is reserved for genesis; settlement batches start at 1");
        if (batch.BatchNumber == 1)
        {
            var initialStateRoot = await GetInitialStateRootAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!batch.PreStateRoot.Equals(initialStateRoot))
                throw new InvalidDataException(
                    "batch 1 pre-state root differs from the authenticated genesis state root");
            var l1CanonicalRoot = await _client.GetCanonicalStateRootAsync(
                batch.ChainId, cancellationToken).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(l1CanonicalRoot);
            if (!l1CanonicalRoot.Equals(initialStateRoot))
                throw new InvalidDataException(
                    "L1 canonical state root differs from the settlement profile genesis root");
            return;
        }

        var predecessor = await _store.GetAsync(
            batch.ChainId, batch.BatchNumber - 1, cancellationToken).ConfigureAwait(false);
        if (predecessor is null)
            throw new InvalidDataException(
                $"batch {batch.BatchNumber} cannot persist before predecessor " +
                $"batch {batch.BatchNumber - 1}");
        ValidateCommittedArtifact(predecessor);
        if (predecessor.LastBlock == ulong.MaxValue)
            throw new InvalidDataException(
                "predecessor block range cannot advance beyond the maximum block number");
        var expectedFirstBlock = predecessor.LastBlock + 1;
        if (batch.FirstBlock != expectedFirstBlock)
            throw new InvalidDataException(
                $"batch block chain is non-contiguous: expected block " +
                $"{expectedFirstBlock}, got {batch.FirstBlock}");
        if (!batch.PreStateRoot.Equals(predecessor.ExecutionResult.PostStateRoot))
            throw new InvalidDataException(
                "batch pre-state root does not match the predecessor post-state root");
    }

    private ValueTask AcknowledgeProofArtifactsAsync(
        ProofWitnessArtifactV1 artifact,
        CancellationToken cancellationToken)
        => _prover is IProofArtifactRetention retention
            ? retention.AcknowledgeSettlementAsync(
                artifact.ContentHash, cancellationToken)
            : ValueTask.CompletedTask;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _reconcileGate.Dispose();
    }

    private async ValueTask<ProofResultManifest> ProveCommittedArtifactAsync(
        ProofWitnessArtifactV1 artifact,
        CancellationToken cancellationToken)
    {
        var artifactBytes = ProofWitnessArtifactSerializer.Encode(artifact);
        if (artifactBytes.Length == 0)
            throw new InvalidOperationException("committed artifact bytes are empty");
        var persisted = await _store.GetAsync(
            artifact.ChainId, artifact.BatchNumber, cancellationToken).ConfigureAwait(false);
        if (persisted is null || !persisted.ContentHash.Equals(artifact.ContentHash))
            throw new InvalidOperationException(
                "artifact must be committed before proof generation");

        var proveStarted = System.Diagnostics.Stopwatch.StartNew();
        var proof = await _prover.ProveAsync(new ProofRequest
        {
            PublicInputs = artifact.PublicInputs,
            Witness = artifactBytes,
            Kind = artifact.ProofType,
        }, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("IL2Prover.ProveAsync returned null");
        proveStarted.Stop();
        ArgumentNullException.ThrowIfNull(proof.PublicInputHash);
        if (proof.Kind != artifact.ProofType)
            throw new InvalidOperationException(
                $"prover returned {proof.Kind}, expected {artifact.ProofType}");
        var publicInputHash = StateRootCalculator.HashPublicInputs(artifact.PublicInputs);
        if (!proof.PublicInputHash.Equals(publicInputHash))
            throw new InvalidOperationException(
                "prover returned a different public-input hash");
        if (proof.Proof.IsEmpty)
            throw new InvalidOperationException("prover returned empty proof bytes");

        var manifest = new ProofResultManifest
        {
            ProofType = artifact.ProofType,
            ChainId = artifact.ChainId,
            BatchNumber = artifact.BatchNumber,
            ArtifactContentHash = artifact.ContentHash,
            PublicInputHash = publicInputHash,
            VerificationKeyId = artifact.VerificationKeyId,
            ProofSystem = artifact.ProofSystem,
            ExecutionSemanticId = artifact.ExecutionSemanticId,
            Proof = proof.Proof.ToArray(),
            PublicValues = ReadOnlyMemory<byte>.Empty,
            SubmissionState = ProofSubmissionState.ProofReady,
            SettlementFinalized = false,
            ForcedInclusionFinalized = false,
        };
        await _store.PutProofAsync(manifest, cancellationToken).ConfigureAwait(false);
        var persistedManifest = await _store.GetProofAsync(
            artifact.ContentHash, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "proof witness store did not return the committed proof manifest");
        ValidateManifest(artifact, persistedManifest);
        _metrics.SafeIncrementCounter(
            MetricNames.ProofsGenerated, 1, ("kind", proof.Kind.ToString()));
        _metrics.SafeRecordHistogram(
            MetricNames.ProveLatencyMs,
            proveStarted.Elapsed.TotalMilliseconds,
            ("kind", proof.Kind.ToString()));
        return persistedManifest;
    }

    private async ValueTask RecordFailureAsync(
        ProofWitnessArtifactV1 artifact,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var message = string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : $"{exception.GetType().Name}: {exception.Message}";
        await _store.RecordSettlementFailureAsync(
            artifact.ChainId,
            artifact.BatchNumber,
            artifact.ContentHash,
            message,
            _maxAutomaticRetries,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            cancellationToken).ConfigureAwait(false);
        _metrics.SafeIncrementCounter(MetricNames.SettlementRetries);
        await EmitRecoveryMetricsAsync(cancellationToken).ConfigureAwait(false);
    }

    private ValueTask ClearRecoveryAsync(
        ProofWitnessArtifactV1 artifact,
        CancellationToken cancellationToken)
        => _store.ClearSettlementRecoveryAsync(
            artifact.ChainId,
            artifact.BatchNumber,
            artifact.ContentHash,
            cancellationToken);

    private async ValueTask EmitRecoveryMetricsAsync(CancellationToken cancellationToken)
    {
        var status = await ReadRecoveryStatusAsync(cancellationToken).ConfigureAwait(false);
        _metrics.SafeSetGauge(MetricNames.SettlementPending, status.PendingCount);
        _metrics.SafeSetGauge(
            MetricNames.SettlementConfirmationLagBatches,
            status.ConfirmationLagBatches);
        _metrics.SafeSetGauge(
            MetricNames.SettlementPoisoned,
            status.State == SettlementRecoveryState.Poisoned ? 1 : 0);
    }

    private void ReleaseReconcileGate()
    {
        try
        {
            _reconcileGate.Release();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async ValueTask<ProofResultManifest> ReconcileUnknownSubmissionAsync(
        ProofWitnessArtifactV1 artifact,
        ProofResultManifest manifest,
        CancellationToken cancellationToken)
    {
        if (manifest.SubmissionState == ProofSubmissionState.ProofReady)
        {
            await BroadcastAndPersistAsync(
                artifact, manifest, null, cancellationToken).ConfigureAwait(false);
            return await ReadManifestAsync(
                artifact.ContentHash, cancellationToken).ConfigureAwait(false);
        }

        if (manifest.SubmissionState != ProofSubmissionState.Submitted
            || manifest.L1TransactionHash is null)
            throw new InvalidDataException(
                "unobserved proof manifest has an invalid durable submission state");
        if (_client is not ISettlementTransactionStatusClient transactionClient)
            throw new InvalidOperationException(
                "batch is unknown and a submission transaction is persisted, but the settlement " +
                "client cannot reconcile transaction status; automatic duplicate submission is disabled");

        var transactionStatus = await transactionClient.GetTransactionStatusAsync(
            manifest.L1TransactionHash, cancellationToken).ConfigureAwait(false);
        switch (transactionStatus)
        {
            case SettlementTransactionStatus.Pending:
                return manifest;
            case SettlementTransactionStatus.Dropped:
            case SettlementTransactionStatus.Reverted:
                await BroadcastAndPersistAsync(
                    artifact,
                    manifest,
                    manifest.L1TransactionHash,
                    cancellationToken).ConfigureAwait(false);
                return await ReadManifestAsync(
                    artifact.ContentHash, cancellationToken).ConfigureAwait(false);
            case SettlementTransactionStatus.Confirmed:
                throw new InvalidOperationException(
                    "submission transaction is confirmed but the settlement batch is unknown; " +
                    "automatic resubmission is disabled until L1 state is reconciled");
            case SettlementTransactionStatus.Unknown:
                throw new InvalidOperationException(
                    "submission transaction status is unknown; automatic resubmission is disabled");
            default:
                throw new InvalidDataException(
                    $"settlement client returned unknown transaction status {transactionStatus}");
        }
    }

    private async ValueTask BroadcastAndPersistAsync(
        ProofWitnessArtifactV1 artifact,
        ProofResultManifest manifest,
        UInt256? replacedTransactionHash,
        CancellationToken cancellationToken)
    {
        var commitment = BuildCommitment(artifact, manifest);
        var submitStarted = System.Diagnostics.Stopwatch.StartNew();
        var transactionHash = await _client.SubmitBatchAsync(
            commitment,
            artifact.PublicInputs,
            cancellationToken).ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(transactionHash);
        if (transactionHash.Equals(UInt256.Zero))
            throw new InvalidOperationException(
                "ISettlementClient returned a zero transaction hash");

        if (replacedTransactionHash is null)
        {
            await _store.MarkSubmittedAsync(
                artifact.ContentHash,
                transactionHash,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _store.ReplaceSubmittedTransactionAsync(
                artifact.ContentHash,
                replacedTransactionHash,
                transactionHash,
                cancellationToken).ConfigureAwait(false);
        }

        submitStarted.Stop();
        _metrics.SafeIncrementCounter(MetricNames.BatchesSubmitted);
        _metrics.SafeRecordHistogram(
            MetricNames.SubmitLatencyMs,
            submitStarted.Elapsed.TotalMilliseconds);
    }

    private async ValueTask<ProofResultManifest> ReadManifestAsync(
        UInt256 artifactContentHash,
        CancellationToken cancellationToken)
        => await _store.GetProofAsync(artifactContentHash, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "proof manifest disappeared during settlement reconciliation");

    private void ValidateExecution(
        SealedBatch batch,
        ProofWitnessExecutionResult execution)
    {
        ArgumentNullException.ThrowIfNull(execution.ExecutionResult);
        ArgumentNullException.ThrowIfNull(execution.ExecutionSemanticId);
        ValidateCanonicalOutputs(execution.StateWitness, execution.Effects);
        ValidateExecutionResult(execution.ExecutionResult);
        if (_profile.ProofType == ProofType.Zk)
            ValidateZkExecution(
                execution.ExecutionSemanticId,
                execution.WitnessAuthenticated,
                execution.StateWitness);
    }

    private async ValueTask ValidateDaReceiptAsync(
        DAReceipt receipt,
        UInt256 expectedCommitment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(receipt.Commitment);
        if (!receipt.HasRequiredMetadata(_daWriter.Mode, _daWriter.ReceiptKind))
            throw new InvalidDataException(
                "DA receipt layer, kind, locator, or evidence is invalid");
        if (!receipt.Commitment.Equals(expectedCommitment))
            throw new InvalidDataException(
                "DA receipt commitment does not bind the encoded execution payload");
        if (!await _daWriter.IsAvailableAsync(receipt, cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException(
                "DA writer could not independently confirm published payload availability");
    }

    private void ValidateCommittedArtifact(ProofWitnessArtifactV1 artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (artifact.ChainId != _profile.ChainId
            || artifact.ProofType != _profile.ProofType
            || artifact.ProofSystem != _profile.ProofSystem
            || !artifact.VerificationKeyId.Equals(_profile.VerificationKeyId))
            throw new InvalidDataException(
                "artifact profile differs from the configured pipeline profile");
        if (artifact.DAReceipt.Layer != _daWriter.Mode
            || artifact.DAReceipt.Kind != _daWriter.ReceiptKind)
            throw new InvalidDataException(
                "artifact DA receipt differs from the configured writer profile");
        ValidateBlockContext(artifact.ExecutionPayload.BlockContext);
        _ = ProofWitnessArtifactSerializer.Encode(artifact);
        _ = new SealedBatch(
            artifact.ChainId,
            artifact.BatchNumber,
            artifact.FirstBlock,
            artifact.LastBlock,
            artifact.ExecutionPayload.PreStateRoot,
            artifact.ExecutionPayload.Transactions,
            artifact.ExecutionPayload.L1Messages,
            artifact.ExecutionPayload.BlockContext,
            artifact.ExecutionPayload.ForcedInclusions);
        ValidateCanonicalOutputs(artifact.StateWitness, artifact.Effects);
        ValidateExecutionResult(artifact.ExecutionResult);
        if (_profile.ProofType == ProofType.Zk)
            ValidateZkExecution(
                artifact.ExecutionSemanticId,
                artifact.ExecutionWitnessAuthenticated,
                artifact.StateWitness);
    }

    private void ValidateProverProfile()
    {
        if (_profile.ProofType != ProofType.Zk)
        {
            if (_prover is IZkExecutionProver)
                throw new ArgumentException(
                    "legacy profile cannot be wired to a ZK prover", nameof(_prover));
            return;
        }

        if (_prover is not IZkExecutionProver zkProver)
            throw new ArgumentException(
                "ZK profile requires IZkExecutionProver metadata", nameof(_prover));
        ArgumentNullException.ThrowIfNull(zkProver.ExecutionSemanticId);
        ArgumentNullException.ThrowIfNull(zkProver.VerificationKeyId);
        if (!zkProver.ProducesCryptographicProof)
            throw new ArgumentException(
                "ZK profile rejects mock/non-cryptographic provers", nameof(_prover));
        if (zkProver.WitnessProofSystem != _profile.ProofSystem
            || !zkProver.VerificationKeyId.Equals(_profile.VerificationKeyId))
            throw new ArgumentException(
                "ZK prover proof-system or verification-key metadata differs from the profile",
                nameof(_prover));
    }

    private void ValidateZkExecution(
        UInt256 executionSemanticId,
        bool authenticated,
        ReadOnlyMemory<byte> stateWitness)
    {
        if (_prover is not IZkExecutionProver zkProver)
            throw new InvalidOperationException("ZK prover metadata is unavailable");
        if (!executionSemanticId.Equals(zkProver.ExecutionSemanticId))
            throw new InvalidDataException(
                "executor and prover execution semantic identifiers differ");
        if (IsUnsafeZkSemantic(executionSemanticId))
            throw new InvalidDataException(
                "ZK profile rejects unspecified, mock, preview, and legacy execution semantics");
        if (!authenticated || stateWitness.IsEmpty)
            throw new InvalidDataException(
                "ZK profile requires a non-empty authenticated execution witness");
    }

    private static void ValidateCanonicalOutputs(
        ReadOnlyMemory<byte> stateWitness,
        ReadOnlyMemory<byte> effects)
    {
        if (stateWitness.Length > ProofWitnessArtifactSerializer.MaxStateWitnessBytes)
            throw new InvalidDataException("state witness exceeds the canonical artifact limit");
        if (effects.IsEmpty)
            throw new InvalidDataException("canonical execution effects are empty");
        if (effects.Length > ProofWitnessArtifactSerializer.MaxEffectsBytes)
            throw new InvalidDataException("execution effects exceed the canonical artifact limit");
    }

    private static void ValidateExecutionResult(BatchExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result.PostStateRoot);
        ArgumentNullException.ThrowIfNull(result.ReceiptRoot);
        ArgumentNullException.ThrowIfNull(result.WithdrawalRoot);
        ArgumentNullException.ThrowIfNull(result.L2ToL1MessageRoot);
        ArgumentNullException.ThrowIfNull(result.L2ToL2MessageRoot);
        ArgumentNullException.ThrowIfNull(result.TxRoot);
        if (result.GasConsumed < 0)
            throw new InvalidDataException("execution result gas cannot be negative");
    }

    private static bool IsUnsafeZkSemantic(UInt256 semanticId)
        => semanticId.Equals(ExecutionSemanticIds.UnspecifiedV1)
            || semanticId.Equals(ExecutionSemanticIds.ReferenceNoOpV1)
            || semanticId.Equals(ExecutionSemanticIds.NeoN3ApplicationEngineV1)
            || semanticId.Equals(ExecutionSemanticIds.NeoVm2PolkaVmStatelessPreviewV1)
            || semanticId.Equals(ExecutionSemanticIds.Sp1LegacyNeoN3GuestV1);

    private void ValidateBlockContext(BatchBlockContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.SequencerCommitteeHash);
        if (context.LastBlockTimestamp < context.FirstBlockTimestamp)
            throw new InvalidDataException(
                "block context last timestamp precedes first timestamp");
        if (_profile.RequireCanonicalBlockContext
            && (context.L1FinalizedHeight == 0
                || context.FirstBlockTimestamp == 0
                || context.LastBlockTimestamp == 0
                || context.Network == 0
                || context.SequencerCommitteeHash.Equals(UInt256.Zero)))
            throw new InvalidDataException(
                "ZK profile requires real non-zero block-context fields");
    }

    private static PublicInputs BuildPublicInputs(
        SealedBatch batch,
        BatchExecutionResult executionResult,
        UInt256 daCommitment) => new()
        {
            ChainId = batch.ChainId,
            BatchNumber = batch.BatchNumber,
            PreStateRoot = batch.PreStateRoot,
            PostStateRoot = executionResult.PostStateRoot,
            TxRoot = executionResult.TxRoot,
            ReceiptRoot = executionResult.ReceiptRoot,
            WithdrawalRoot = executionResult.WithdrawalRoot,
            L2ToL1MessageRoot = executionResult.L2ToL1MessageRoot,
            L2ToL2MessageRoot = executionResult.L2ToL2MessageRoot,
            L1MessageHash = StateRootCalculator.HashL1Messages(batch.L1Messages),
            DACommitment = daCommitment,
            BlockContextHash = StateRootCalculator.HashBlockContext(batch.BlockContext),
        };

    private static L2BatchCommitment BuildCommitment(
        ProofWitnessArtifactV1 artifact,
        ProofResultManifest manifest) => new()
        {
            ChainId = artifact.ChainId,
            BatchNumber = artifact.BatchNumber,
            FirstBlock = artifact.FirstBlock,
            LastBlock = artifact.LastBlock,
            PreStateRoot = artifact.ExecutionPayload.PreStateRoot,
            PostStateRoot = artifact.ExecutionResult.PostStateRoot,
            TxRoot = artifact.ExecutionResult.TxRoot,
            ReceiptRoot = artifact.ExecutionResult.ReceiptRoot,
            WithdrawalRoot = artifact.ExecutionResult.WithdrawalRoot,
            L2ToL1MessageRoot = artifact.ExecutionResult.L2ToL1MessageRoot,
            L2ToL2MessageRoot = artifact.ExecutionResult.L2ToL2MessageRoot,
            DACommitment = artifact.DAReceipt.Commitment,
            PublicInputHash = manifest.PublicInputHash,
            ProofType = manifest.ProofType,
            Proof = manifest.Proof.ToArray(),
        };

    private static void ValidateManifest(
        ProofWitnessArtifactV1 artifact,
        ProofResultManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.ChainId != artifact.ChainId
            || manifest.BatchNumber != artifact.BatchNumber
            || manifest.ProofType != artifact.ProofType
            || manifest.ProofSystem != artifact.ProofSystem
            || !manifest.ArtifactContentHash.Equals(artifact.ContentHash)
            || !manifest.VerificationKeyId.Equals(artifact.VerificationKeyId)
            || !manifest.ExecutionSemanticId.Equals(artifact.ExecutionSemanticId)
            || !manifest.PublicInputHash.Equals(
                StateRootCalculator.HashPublicInputs(artifact.PublicInputs))
            || !Enum.IsDefined(manifest.SubmissionState)
            || (manifest.SubmissionState == ProofSubmissionState.ProofReady
                && manifest.L1TransactionHash is not null)
            || (manifest.SubmissionState == ProofSubmissionState.Submitted
                && manifest.L1TransactionHash is null)
            || (manifest.SettlementFinalized && !manifest.SettlementObserved)
            || (manifest.ForcedInclusionFinalized
                && (artifact.ExecutionPayload.ForcedInclusions.Count == 0
                    || !manifest.SettlementFinalized))
            || manifest.Proof.IsEmpty)
            throw new InvalidDataException(
                "proof manifest is not bound to the committed artifact");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(CanonicalSettlementPipeline));
    }
}
