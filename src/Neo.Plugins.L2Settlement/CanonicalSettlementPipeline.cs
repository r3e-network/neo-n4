using Neo.Cryptography;
using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.Executor.ProofWitness;
using Neo.L2.Persistence;
using Neo.L2.Proving.RiscVZk;
using Neo.L2.State;
using Neo.L2.Telemetry;

namespace Neo.Plugins.L2;

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

    /// <summary>Require a public production DA writer rather than local/simulated durability.</summary>
    public required bool RequireProductionDA { get; init; }

    /// <summary>Require non-zero finalized height, timestamps, committee hash, and network.</summary>
    public required bool RequireCanonicalBlockContext { get; init; }

    /// <summary>Create a production ZK profile.</summary>
    public static ProofWitnessPipelineProfile Zk(
        uint chainId,
        WitnessProofSystem proofSystem,
        UInt256 verificationKeyId) => new()
    {
        ChainId = chainId,
        ProofType = ProofType.Zk,
        ProofSystem = proofSystem,
        VerificationKeyId = verificationKeyId,
        RequireProductionDA = true,
        RequireCanonicalBlockContext = true,
    };

    /// <summary>Create an explicitly isolated multisig or optimistic compatibility profile.</summary>
    public static ProofWitnessPipelineProfile Legacy(
        uint chainId,
        ProofType proofType,
        bool requireProductionDA = false) => new()
    {
        ChainId = chainId,
        ProofType = proofType,
        ProofSystem = WitnessProofSystem.None,
        VerificationKeyId = UInt256.Zero,
        RequireProductionDA = requireProductionDA,
        RequireCanonicalBlockContext = false,
    };

    internal void Validate()
    {
        ChainIdValidator.ValidateL2(ChainId);
        ArgumentNullException.ThrowIfNull(VerificationKeyId);
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
    private readonly IProofWitnessBatchExecutor _executor;
    private readonly IDAWriter _daWriter;
    private readonly IProofWitnessStore _store;
    private readonly IL2Prover _prover;
    private readonly ISettlementClient _client;
    private readonly IForcedInclusionFinalizationClient? _forcedInclusionFinalizer;
    private readonly ProofWitnessPipelineProfile _profile;
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
        IForcedInclusionFinalizationClient? forcedInclusionFinalizer = null)
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

        _executor = executor;
        _daWriter = daWriter;
        _store = store;
        _prover = prover;
        _client = client;
        _forcedInclusionFinalizer = forcedInclusionFinalizer;
        _profile = profile;
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
        if (batch.ChainId != _profile.ChainId)
            throw new InvalidOperationException(
                $"sealed batch chain {batch.ChainId} does not match pipeline chain {_profile.ChainId}");
        ValidateBlockContext(batch.BlockContext);

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
            var processed = 0;
            await foreach (var artifact in _store.EnumerateCommittedAsync(
                _profile.ChainId, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
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
                var needsSettlement = !manifest.SettlementObserved;
                var needsForcedFinalization =
                    hasForcedNonces && !manifest.ForcedInclusionFinalized;
                if (!needsSettlement && !needsForcedFinalization) continue;

                BatchStatus? status = null;
                if (needsSettlement)
                {
                    status = await _client.GetBatchStatusAsync(
                        artifact.ChainId,
                        artifact.BatchNumber,
                        cancellationToken).ConfigureAwait(false);
                    if (status == BatchStatus.Reverted)
                        throw new InvalidOperationException(
                            "reverted batch remains pending for operator recovery");
                    if (status != BatchStatus.Unknown)
                    {
                        await _store.MarkSubmissionObservedAsync(
                            artifact.ContentHash, cancellationToken).ConfigureAwait(false);
                    }
                    else
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
                        await _store.MarkSubmittedAsync(
                            artifact.ContentHash,
                            transactionHash,
                            cancellationToken).ConfigureAwait(false);
                        submitStarted.Stop();
                        _metrics.SafeIncrementCounter(MetricNames.BatchesSubmitted);
                        _metrics.SafeRecordHistogram(
                            MetricNames.SubmitLatencyMs,
                            submitStarted.Elapsed.TotalMilliseconds);
                        status = null;
                    }
                    manifest = await _store.GetProofAsync(
                        artifact.ContentHash, cancellationToken).ConfigureAwait(false)
                        ?? throw new InvalidOperationException(
                            "proof manifest disappeared during settlement reconciliation");
                }

                if (hasForcedNonces && !manifest.ForcedInclusionFinalized)
                {
                    status ??= await _client.GetBatchStatusAsync(
                        artifact.ChainId,
                        artifact.BatchNumber,
                        cancellationToken).ConfigureAwait(false);
                    if (status == BatchStatus.Reverted)
                        throw new InvalidOperationException(
                            "reverted batch retains forced-inclusion reservations for operator recovery");
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

                processed++;
                if (processed >= maximumBatches) return;
            }
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
            if (manifest?.SettlementObserved != true
                || (artifact.ExecutionPayload.ForcedInclusions.Count != 0
                    && manifest.ForcedInclusionFinalized != true))
                count++;
        }
        return count;
    }

    /// <summary>Return forced-inclusion nonces tracked by durable artifacts.</summary>
    public ValueTask<IReadOnlyCollection<ulong>> GetTrackedForcedInclusionNoncesAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _store.GetTrackedForcedInclusionNoncesAsync(
            _profile.ChainId, cancellationToken);
    }

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
            SettlementObserved = false,
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
            || (manifest.ForcedInclusionFinalized
                && artifact.ExecutionPayload.ForcedInclusions.Count == 0)
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
