namespace Neo.Plugins.L2;

/// <summary>
/// Operator-status and health-probe JSON document builders for LocalHost compositions.
/// </summary>
/// <remarks>
/// See doc.md §7.5 / §14.1 / §14.2. Split from <see cref="LocalHostCompositionBase"/> for
/// structure only; visibility and behavior are unchanged.
/// </remarks>
public abstract partial class LocalHostCompositionBase
{
    /// <summary>
    /// Serialize <see cref="GetHealthProbeAsync"/> as indented camelCase JSON for ops
    /// scripts and metrics HTTP <c>/healthprobe</c>. Soft FI clock via
    /// <paramref name="nowUnixSeconds"/>; no L1 settle claim.
    /// </summary>
    public string FormatHealthProbeJson(uint? nowUnixSeconds = null)
    {
        var document = GetHealthProbeAsync(CancellationToken.None, nowUnixSeconds)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        return LocalHostHealthProbeDocument.FormatJson(document);
    }

    /// <summary>
    /// Serialize <see cref="GetOperatorStatusAsync"/> as indented camelCase JSON for ops
    /// scripts without writing a file (primitive fields + recovery summary only).
    /// Soft FI clock via <paramref name="nowUnixSeconds"/>; no L1 settle claim.
    /// </summary>
    public async ValueTask<string> FormatOperatorStatusJsonAsync(
        int depositPeekLimit = 64,
        CancellationToken cancellationToken = default,
        uint? nowUnixSeconds = null)
    {
        var status = await GetOperatorStatusAsync(
                depositPeekLimit, cancellationToken, nowUnixSeconds)
            .ConfigureAwait(false);
        return LocalHostOperatorStatusDocument.FormatJson(
            LocalHostOperatorStatusDocument.From(status));
    }

    /// <summary>
    /// Write <see cref="GetOperatorStatusAsync"/> as indented JSON for host health files
    /// without Neo.CLI (primitive fields + recovery summary only).
    /// </summary>
    public async ValueTask WriteOperatorStatusAsync(
        string path,
        int depositPeekLimit = 64,
        CancellationToken cancellationToken = default,
        uint? nowUnixSeconds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var json = await FormatOperatorStatusJsonAsync(
                depositPeekLimit, cancellationToken, nowUnixSeconds)
            .ConfigureAwait(false);
        var fullPath = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(fullPath, json, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Build a compact in-memory health probe (passport / pipeline / metrics / settlement)
    /// without the full operator status document. Soft FI clock via
    /// <paramref name="nowUnixSeconds"/>; no L1 settle claim.
    /// </summary>
    public async ValueTask<LocalHostHealthProbeDocument> GetHealthProbeAsync(
        CancellationToken cancellationToken = default,
        uint? nowUnixSeconds = null)
    {
        var pipelineFailures = await GetPipelineHealthFailuresAsync(
                cancellationToken, nowUnixSeconds)
            .ConfigureAwait(false);
        var metricsFailures = MetricsHttpHealthFailures;
        var localHostFailures = LocalHostOperatorStatus.BuildLocalHostHealthFailures(
            pipelineFailures, metricsFailures);
        var pending = await GetPendingCountAsync(cancellationToken).ConfigureAwait(false);
        var checkpoint = await GetLatestDurableCheckpointAsync(cancellationToken)
            .ConfigureAwait(false);
        var recovery = await GetRecoveryStatusAsync(cancellationToken).ConfigureAwait(false);
        var initialStateRoot = await GetInitialStateRootAsync(cancellationToken)
            .ConfigureAwait(false);
        var trackedForced = await GetTrackedForcedInclusionNoncesAsync(ChainId, cancellationToken)
            .ConfigureAwait(false);
        var now = nowUnixSeconds
            ?? (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return new LocalHostHealthProbeDocument
        {
            IsOfflinePassportComplete = IsOfflinePassportComplete,
            OfflinePassportFailures = OfflinePassportFailures,
            ChainId = ChainId,
            BatcherConfiguredChainId = BatcherConfiguredChainId,
            SettlementConfiguredChainId = SettlementConfiguredChainId,
            RpcChainId = RpcChainId,
            ProofType = ProofType.ToString(),
            SettlementConfiguredProofType = SettlementConfiguredProofType.ToString(),
            DaMode = DaMode.ToString(),
            RpcDaMode = RpcDaMode.ToString(),
            SecurityLevel = RpcStore.SecurityLevel.ToString(),
            Sequencer = RpcStore.Sequencer.ToString(),
            Exit = RpcStore.Exit.ToString(),
            ExpectedNetwork = ExpectedNetwork,
            IsMetricsEnabled = IsMetricsEnabled,
            IsMetricsWiringComplete = IsMetricsWiringComplete,
            MetricsConfiguredPort = MetricsConfiguredPort,
            MetricsBindAddress = MetricsBindAddress,
            MetricsMaxConcurrentConnections = MetricsMaxConcurrentConnections,
            MetricsEntryCount = CaptureMetricsSnapshot().TotalEntries,
            GatewayEnabled = RpcStore.GatewayEnabled,
            SupportsLocalDaReader = SupportsLocalDaReader,
            BridgeAssetCount = BridgeAssetCount,
            IsOperatorReady = IsOperatorReady,
            IsProductionWired = IsProductionWired,
            HasSealedBatchSink = HasSealedBatchSink,
            HasBatchProver = HasBatchProver,
            IsSettlementEnabled = IsSettlementEnabled,
            IsBatcherEnabled = IsBatcherEnabled,
            HasL1RpcEndpoint = HasL1RpcEndpoint,
            HasExpectedNetwork = HasExpectedNetwork,
            HasScannerDeployHeights = HasScannerDeployHeights,
            ForcedInclusionDeploymentHeight = ForcedInclusionDeploymentHeight,
            SharedBridgeDeploymentHeight = SharedBridgeDeploymentHeight,
            MessageRouterDeploymentHeight = MessageRouterDeploymentHeight,
            HasSettlementManagerHash = HasSettlementManagerHash,
            HasForcedInclusionHash = HasForcedInclusionHash,
            HasSharedBridgeHash = HasSharedBridgeHash,
            HasMessageRouterHash = HasMessageRouterHash,
            HasL2BridgeHash = HasL2BridgeHash,
            L1FinalityDepth = L1FinalityDepth,
            HasDepositSource = HasDepositSource,
            HasMessageRouter = HasMessageRouter,
            HasForcedInclusionFinalizer = HasForcedInclusionFinalizer,
            HasSettlementClient = HasSettlementClient,
            HasTransactionSender = HasTransactionSender,
            HasBatchDepositSource = HasBatchDepositSource,
            HasBatchMessageRouter = HasBatchMessageRouter,
            HasBatchForcedInclusionSource = HasBatchForcedInclusionSource,
            IsDepositPipelineWiringComplete = IsDepositPipelineWiringComplete,
            IsMessagePipelineWiringComplete = IsMessagePipelineWiringComplete,
            IsForcedInclusionPipelineWiringComplete = IsForcedInclusionPipelineWiringComplete,
            IsSettlementClientWiringComplete = IsSettlementClientWiringComplete,
            IsChainIdConfigConsistent = IsChainIdConfigConsistent,
            IsProofTypeConfigConsistent = IsProofTypeConfigConsistent,
            IsDaModeConfigConsistent = IsDaModeConfigConsistent,
            IsSecurityLevelProofTypeConsistent = IsSecurityLevelProofTypeConsistent,
            IsSecurityLevelDaModeConsistent = IsSecurityLevelDaModeConsistent,
            IsNeoHubHashWiringComplete = IsNeoHubHashWiringComplete,
            IsBatcherInboxWiringComplete = IsBatcherInboxWiringComplete,
            IsPipelineEnabled = IsPipelineEnabled,
            HasPendingSealedBatch = HasPendingSealedBatch,
            PendingSealedBatchNumber = PendingSealedBatchNumber,
            PendingSealedBatchLastBlock = PendingSealedBatchLastBlock,
            HasOpenBatch = HasOpenBatch,
            MaxBlocksPerBatch = MaxBlocksPerBatch,
            MaxTransactionsPerBatch = MaxTransactionsPerBatch,
            MaxBatchAgeMillis = MaxBatchAgeMillis,
            MaxForcedTransactionsPerBatch = MaxForcedTransactionsPerBatch,
            MaxL1MessagesPerBatch = MaxL1MessagesPerBatch,
            OpenBatchAgeMillis = OpenBatchAgeMillis,
            IsOpenBatchPastMaxAge = IsOpenBatchPastMaxAge,
            InProgressTxCount = InProgressTxCount,
            OpenBatchFirstBlock = OpenBatchFirstBlock,
            OpenBatchLastBlock = OpenBatchLastBlock,
            OpenBatchBlockCount = OpenBatchBlockCount,
            OpenBatchL1MessageCount = OpenBatchL1MessageCount,
            OpenBatchL2ToL1MessageCount = OpenBatchL2ToL1MessageCount,
            OpenBatchL2ToL2MessageCount = OpenBatchL2ToL2MessageCount,
            OpenBatchForcedInclusionCount = OpenBatchForcedInclusionCount,
            OpenBatchWithdrawalCount = OpenBatchWithdrawalCount,
            IsBatcherCheckpointAligned = LocalHostOperatorStatus.AreBatcherAndCheckpointAligned(
                LastAcknowledgedBatchNumber, checkpoint?.BatchNumber),
            NextExpectedBlock = NextExpectedBlock,
            LastAcknowledgedBatchNumber = LastAcknowledgedBatchNumber,
            LastAcknowledgedBlock = LastAcknowledgedBlock,
            NextBatchNumber = NextBatchNumber,
            LatestCheckpointBatchNumber = checkpoint?.BatchNumber,
            LatestCheckpointLastBlock = checkpoint?.LastBlock,
            LatestCheckpointPostStateRoot = checkpoint?.PostStateRoot?.ToString(),
            InitialStateRoot = initialStateRoot.ToString(),
            LatestRpcStateRoot = GetLatestRpcStateRoot().ToString(),
            HasOverdueForcedInclusion = HasOverdueForcedInclusionCached(now),
            IsPipelineHealthy = pipelineFailures.Count == 0,
            PipelineHealthFailures = pipelineFailures,
            IsMetricsHttpListening = IsMetricsHttpListening,
            MetricsBoundPort = MetricsBoundPort,
            HasMetricsReadinessCheck = HasMetricsReadinessCheck,
            HasMetricsHealthProbe = HasMetricsHealthProbe,
            HasMetricsOperatorStatus = HasMetricsOperatorStatus,
            IsMetricsHttpHealthy = metricsFailures.Count == 0,
            MetricsHttpHealthFailures = metricsFailures,
            IsLocalHostHealthy = localHostFailures.Count == 0,
            LocalHostHealthFailures = localHostFailures,
            IsSettlementRuntimeIdle = LocalHostOperatorStatus.IsSettlementRuntimeIdle(pending, recovery),
            IsSettlementIdle = LocalHostOperatorStatus.IsSettlementRuntimeIdle(pending, recovery),
            IsSettlementPoisoned = LocalHostOperatorStatus.IsSettlementPoisonedState(recovery),
            IsSettlementRetrying = LocalHostOperatorStatus.IsSettlementRetryingState(recovery),
            SettlementRetryCount = recovery.RetryCount,
            SettlementConfirmationLagBatches = recovery.ConfirmationLagBatches,
            PendingSettlementCount = pending,
            Recovery = LocalHostRecoveryDocument.From(recovery),
            DepositSourceReadyCount = DepositSourceReadyCount,
            ReadyDepositCount = PeekSharedBridgeDeposits(64).Count,
            DepositSourceReservedCount = DepositSourceReservedCount,
            DepositSourceSoftConsumedCount = DepositSourceSoftConsumedCount,
            ConsumedDepositCount = ConsumedDepositCount,
            L1InboxPendingCount = L1InboxPendingCount,
            L1InboxConsumedCount = L1InboxConsumedCount,
            HasMessageOutbox = HasMessageOutbox,
            MessageOutboxL2ToL1Count = MessageOutbox?.L2ToL1Count ?? 0,
            MessageOutboxL2ToL2Count = MessageOutbox?.L2ToL2Count ?? 0,
            MessageOutboxL2ToL1Root = MessageOutboxL2ToL1Root.ToString(),
            MessageOutboxL2ToL2Root = MessageOutboxL2ToL2Root.ToString(),
            KnownInboundNonceCount = KnownInboundNonceCount,
            KnownForcedInclusionNonceCount = KnownForcedInclusionNonceCount,
            TrackedForcedInclusionNonceCount = trackedForced.Count,
            StagedWithdrawalCount = StagedWithdrawalCount,
        };
    }

    /// <summary>
    /// Write <see cref="GetHealthProbeAsync"/> as indented camelCase JSON for ops scripts
    /// without the full operator status document. Soft FI clock via
    /// <paramref name="nowUnixSeconds"/>; no L1 settle claim.
    /// </summary>
    public async ValueTask WriteHealthProbeAsync(
        string path,
        CancellationToken cancellationToken = default,
        uint? nowUnixSeconds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var document = await GetHealthProbeAsync(cancellationToken, nowUnixSeconds)
            .ConfigureAwait(false);
        var fullPath = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(
                fullPath,
                LocalHostHealthProbeDocument.FormatJson(document),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
