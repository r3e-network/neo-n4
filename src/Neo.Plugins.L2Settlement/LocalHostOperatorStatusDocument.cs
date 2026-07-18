using Neo.L2;

namespace Neo.Plugins.L2;

/// <summary>
/// JSON-serializable projection of <see cref="LocalHostOperatorStatus"/> for host health files.
/// </summary>
/// <remarks>
/// See doc.md §7.5 / §14.2. Roots and enums are stringified so operators can dump status
/// without custom UInt256 converters. Produced by <c>WriteOperatorStatusAsync</c>.
/// </remarks>
public sealed record LocalHostOperatorStatusDocument
{
    /// <summary>L2 chain id.</summary>
    public required uint ChainId { get; init; }

    /// <summary>Batcher plugin configured chain id (0 when unset).</summary>
    public required uint BatcherConfiguredChainId { get; init; }

    /// <summary>Settlement plugin configured chain id.</summary>
    public required uint SettlementConfiguredChainId { get; init; }

    /// <summary>RPC store chain id.</summary>
    public required uint RpcChainId { get; init; }

    /// <summary>Proof type name.</summary>
    public required string ProofType { get; init; }

    /// <summary>Settlement plugin configured proof type name.</summary>
    public required string SettlementConfiguredProofType { get; init; }

    /// <summary>Host/batcher/settlement/RPC chain ids agree.</summary>
    public required bool IsChainIdConfigConsistent { get; init; }

    /// <summary>Host and settlement proof types agree.</summary>
    public required bool IsProofTypeConfigConsistent { get; init; }

    /// <summary>DA mode name of the wired DA writer.</summary>
    public required string DaMode { get; init; }

    /// <summary>DA mode name from the durable L2 RPC store.</summary>
    public required string RpcDaMode { get; init; }

    /// <summary>Wired DA writer and RPC store DA modes agree.</summary>
    public required bool IsDaModeConfigConsistent { get; init; }

    /// <summary>Security level name.</summary>
    public required string SecurityLevel { get; init; }

    /// <summary>Whether Gateway is enabled in the RPC store.</summary>
    public required bool GatewayEnabled { get; init; }

    /// <summary>Sequencer model name.</summary>
    public required string Sequencer { get; init; }

    /// <summary>Exit model name.</summary>
    public required string Exit { get; init; }

    /// <summary>Production WireProduction installed.</summary>
    public required bool IsProductionWired { get; init; }

    /// <summary>Sealed-batch sink installed.</summary>
    public required bool HasSealedBatchSink { get; init; }

    /// <summary>Next expected L2 block index, if sealer is restored.</summary>
    public required ulong? NextExpectedBlock { get; init; }

    /// <summary>Sealed batch awaiting durable persistence.</summary>
    public required bool HasPendingSealedBatch { get; init; }

    /// <summary>Pending sealed batch number, if any.</summary>
    public required ulong? PendingSealedBatchNumber { get; init; }

    /// <summary>Last L2 block of the pending sealed batch, if any.</summary>
    public required ulong? PendingSealedBatchLastBlock { get; init; }

    /// <summary>Batcher plugin enabled flag.</summary>
    public required bool IsBatcherEnabled { get; init; }

    /// <summary>Configured max L2 blocks per sealed batch.</summary>
    public required int MaxBlocksPerBatch { get; init; }

    /// <summary>Configured max transactions per sealed batch.</summary>
    public required int MaxTransactionsPerBatch { get; init; }

    /// <summary>Configured max open-batch age in milliseconds.</summary>
    public required int MaxBatchAgeMillis { get; init; }

    /// <summary>Open batch currently accumulating.</summary>
    public required bool HasOpenBatch { get; init; }

    /// <summary>Open batch wall-clock age in milliseconds, if any.</summary>
    public required long? OpenBatchAgeMillis { get; init; }

    /// <summary>Open batch is at or past MaxBatchAgeMillis (seal-by-age overdue).</summary>
    public required bool IsOpenBatchPastMaxAge { get; init; }

    /// <summary>Transaction count in the open batch.</summary>
    public required int InProgressTxCount { get; init; }

    /// <summary>First L2 block in the open batch, if any.</summary>
    public required ulong? OpenBatchFirstBlock { get; init; }

    /// <summary>Last L2 block in the open batch, if any.</summary>
    public required ulong? OpenBatchLastBlock { get; init; }

    /// <summary>Block count in the open batch.</summary>
    public required int OpenBatchBlockCount { get; init; }

    /// <summary>L1 messages in the open batch.</summary>
    public required int OpenBatchL1MessageCount { get; init; }

    /// <summary>L2→L1 messages in the open batch.</summary>
    public required int OpenBatchL2ToL1MessageCount { get; init; }

    /// <summary>L2→L2 messages in the open batch.</summary>
    public required int OpenBatchL2ToL2MessageCount { get; init; }

    /// <summary>Forced-inclusion entries in the open batch.</summary>
    public required int OpenBatchForcedInclusionCount { get; init; }

    /// <summary>Withdrawals in the open batch.</summary>
    public required int OpenBatchWithdrawalCount { get; init; }

    /// <summary>Local DA reader available on this host profile.</summary>
    public required bool SupportsLocalDaReader { get; init; }

    /// <summary>Settlement settings include a non-empty L1 RPC endpoint.</summary>
    public required bool HasL1RpcEndpoint { get; init; }

    /// <summary>Configured expected L1 network magic, if any.</summary>
    public required uint? ExpectedNetwork { get; init; }

    /// <summary>SettlementManager hash present in settings.</summary>
    public required bool HasSettlementManagerHash { get; init; }

    /// <summary>ForcedInclusion hash present in settings.</summary>
    public required bool HasForcedInclusionHash { get; init; }

    /// <summary>SharedBridge hash present in settings.</summary>
    public required bool HasSharedBridgeHash { get; init; }

    /// <summary>MessageRouter hash present in settings.</summary>
    public required bool HasMessageRouterHash { get; init; }

    /// <summary>Explicit L2 bridge hash present in settings.</summary>
    public required bool HasL2BridgeHash { get; init; }

    /// <summary>MessageRouter L2 outbox is installed.</summary>
    public required bool HasMessageOutbox { get; init; }

    /// <summary>Soft consumed-deposit cache size.</summary>
    public required int ConsumedDepositCount { get; init; }

    /// <summary>Last acknowledged batch number.</summary>
    public required ulong LastAcknowledgedBatchNumber { get; init; }

    /// <summary>Last acknowledged L2 block.</summary>
    public required ulong LastAcknowledgedBlock { get; init; }

    /// <summary>Next batch number to seal.</summary>
    public required ulong NextBatchNumber { get; init; }

    /// <summary>Operator readiness flag.</summary>
    public required bool IsOperatorReady { get; init; }

    /// <summary>Deposit source installed.</summary>
    public required bool HasDepositSource { get; init; }

    /// <summary>MessageRouter installed.</summary>
    public required bool HasMessageRouter { get; init; }

    /// <summary>Forced-inclusion finalizer installed.</summary>
    public required bool HasForcedInclusionFinalizer { get; init; }

    /// <summary>Settlement client installed.</summary>
    public required bool HasSettlementClient { get; init; }

    /// <summary>L1 transaction sender installed.</summary>
    public required bool HasTransactionSender { get; init; }

    /// <summary>Bridge L1 inbox pending message count.</summary>
    public required int L1InboxPendingCount { get; init; }

    /// <summary>Bridge L1 inbox consumed message count.</summary>
    public required int L1InboxConsumedCount { get; init; }

    /// <summary>MessageRouter known inbound nonce count.</summary>
    public required int KnownInboundNonceCount { get; init; }

    /// <summary>Metrics HTTP listening.</summary>
    public required bool IsMetricsHttpListening { get; init; }

    /// <summary>Metrics HTTP port.</summary>
    public required int MetricsBoundPort { get; init; }

    /// <summary>Pending settlement artifacts.</summary>
    public required int PendingSettlementCount { get; init; }

    /// <summary>Ready deposit peek count.</summary>
    public required int ReadyDepositCount { get; init; }

    /// <summary>Latest RPC state root as 0x-hex.</summary>
    public required string LatestRpcStateRoot { get; init; }

    /// <summary>Bridge asset mapping count.</summary>
    public required int BridgeAssetCount { get; init; }

    /// <summary>Metrics entry count.</summary>
    public required int MetricsEntryCount { get; init; }

    /// <summary>L2→L1 outbox depth.</summary>
    public required int MessageOutboxL2ToL1Count { get; init; }

    /// <summary>L2→L2 outbox depth.</summary>
    public required int MessageOutboxL2ToL2Count { get; init; }

    /// <summary>L2→L1 outbox root as 0x-hex.</summary>
    public required string MessageOutboxL2ToL1Root { get; init; }

    /// <summary>L2→L2 outbox root as 0x-hex.</summary>
    public required string MessageOutboxL2ToL2Root { get; init; }

    /// <summary>Staged withdrawal count.</summary>
    public required int StagedWithdrawalCount { get; init; }

    /// <summary>Tracked forced-inclusion nonce count.</summary>
    public required int TrackedForcedInclusionNonceCount { get; init; }

    /// <summary>In-process FI source known-nonce count.</summary>
    public required int KnownForcedInclusionNonceCount { get; init; }

    /// <summary>Unconsumed forced-inclusion entry past status clock (local FI source).</summary>
    public required bool HasOverdueForcedInclusion { get; init; }

    /// <summary>Batcher forced-inclusion source wired.</summary>
    public required bool HasBatchForcedInclusionSource { get; init; }

    /// <summary>Batcher deposit source wired.</summary>
    public required bool HasBatchDepositSource { get; init; }

    /// <summary>Batcher MessageRouter wired.</summary>
    public required bool HasBatchMessageRouter { get; init; }

    /// <summary>Max forced-inclusion entries per sealed batch.</summary>
    public required int MaxForcedTransactionsPerBatch { get; init; }

    /// <summary>Max L1 inbox messages per sealed batch.</summary>
    public required int MaxL1MessagesPerBatch { get; init; }

    /// <summary>Metrics max concurrent HTTP connections.</summary>
    public required int MetricsMaxConcurrentConnections { get; init; }

    /// <summary>Settlement plugin enabled in settings.</summary>
    public required bool IsSettlementEnabled { get; init; }

    /// <summary>Batcher and settlement plugins both enabled.</summary>
    public required bool IsPipelineEnabled { get; init; }

    /// <summary>Durable settlement recovery is poisoned.</summary>
    public required bool IsSettlementPoisoned { get; init; }

    /// <summary>Durable settlement recovery is retrying automatic attempts.</summary>
    public required bool IsSettlementRetrying { get; init; }

    /// <summary>No pending settlement work and recovery is idle.</summary>
    public required bool IsSettlementIdle { get; init; }

    /// <summary>Offline passport + pipeline enabled + no pending seal + settlement not poisoned + idle.</summary>
    public required bool IsPipelineHealthy { get; init; }

    /// <summary>Failed pipeline health check names (empty when healthy).</summary>
    public required IReadOnlyList<string> PipelineHealthFailures { get; init; }

    /// <summary>Metrics HTTP runtime healthy when metrics enabled (or N/A when disabled).</summary>
    public required bool IsMetricsHttpHealthy { get; init; }

    /// <summary>Failed metrics HTTP health check names (empty when healthy or metrics disabled).</summary>
    public required IReadOnlyList<string> MetricsHttpHealthFailures { get; init; }

    /// <summary>Combined pipeline + metrics HTTP local host health.</summary>
    public required bool IsLocalHostHealthy { get; init; }

    /// <summary>Union of pipeline and metrics HTTP health failure names.</summary>
    public required IReadOnlyList<string> LocalHostHealthFailures { get; init; }

    /// <summary>Configured L1 finality depth.</summary>
    public required uint L1FinalityDepth { get; init; }

    /// <summary>Deposit source soft ready-queue depth.</summary>
    public required int DepositSourceReadyCount { get; init; }

    /// <summary>Deposit source soft reserved-queue depth.</summary>
    public required int DepositSourceReservedCount { get; init; }

    /// <summary>Deposit source soft consumed-nonce cache size.</summary>
    public required int DepositSourceSoftConsumedCount { get; init; }

    /// <summary>Metrics HTTP enabled in settings.</summary>
    public required bool IsMetricsEnabled { get; init; }

    /// <summary>Configured metrics HTTP port from settings.</summary>
    public required int MetricsConfiguredPort { get; init; }

    /// <summary>Configured metrics bind address from settings.</summary>
    public required string MetricsBindAddress { get; init; }

    /// <summary>Metrics settings operator-ready (enabled + port + bind address).</summary>
    public required bool IsMetricsWiringComplete { get; init; }

    /// <summary>Metrics /readyz predicate installed.</summary>
    public required bool HasMetricsReadinessCheck { get; init; }

    /// <summary>Production + batcher deposit sources both wired.</summary>
    public required bool IsDepositPipelineWiringComplete { get; init; }

    /// <summary>Production MessageRouter + batcher MessageRouter + MessageOutbox wired.</summary>
    public required bool IsMessagePipelineWiringComplete { get; init; }

    /// <summary>FI finalizer + batcher FI source both wired.</summary>
    public required bool IsForcedInclusionPipelineWiringComplete { get; init; }

    /// <summary>Settlement client + transaction sender + settlement enabled.</summary>
    public required bool IsSettlementClientWiringComplete { get; init; }

    /// <summary>ForcedInclusion scanner deployment height.</summary>
    public required uint ForcedInclusionDeploymentHeight { get; init; }

    /// <summary>SharedBridge scanner deployment height.</summary>
    public required uint SharedBridgeDeploymentHeight { get; init; }

    /// <summary>MessageRouter scanner deployment height.</summary>
    public required uint MessageRouterDeploymentHeight { get; init; }

    /// <summary>SettlementManager + ForcedInclusion + SharedBridge + MessageRouter hashes present.</summary>
    public required bool IsNeoHubHashWiringComplete { get; init; }

    /// <summary>Batcher deposit + message-router + forced-inclusion sources present.</summary>
    public required bool IsBatcherInboxWiringComplete { get; init; }

    /// <summary>RPC security level pairs with host proof type (offline heuristic).</summary>
    public required bool IsSecurityLevelProofTypeConsistent { get; init; }

    /// <summary>RPC security level pairs with host DA mode (offline heuristic).</summary>
    public required bool IsSecurityLevelDaModeConsistent { get; init; }

    /// <summary>Expected L1 network magic is configured.</summary>
    public required bool HasExpectedNetwork { get; init; }

    /// <summary>All three L1 scanner deploy heights are non-zero.</summary>
    public required bool HasScannerDeployHeights { get; init; }

    /// <summary>Offline operator passport (no L1 settle/prove claim).</summary>
    public required bool IsOfflinePassportComplete { get; init; }

    /// <summary>Failed offline passport check names (empty when complete).</summary>
    public required IReadOnlyList<string> OfflinePassportFailures { get; init; }

    /// <summary>Batch prover installed on the host.</summary>
    public required bool HasBatchProver { get; init; }

    /// <summary>Durable checkpoint batch number, if any.</summary>
    public required ulong? LatestCheckpointBatchNumber { get; init; }

    /// <summary>Durable checkpoint last L2 block, if any.</summary>
    public required ulong? LatestCheckpointLastBlock { get; init; }

    /// <summary>Durable checkpoint post-state root as 0x-hex.</summary>
    public required string LatestCheckpointPostStateRoot { get; init; }

    /// <summary>Batcher last-acked batch matches durable checkpoint (or both empty).</summary>
    public required bool IsBatcherCheckpointAligned { get; init; }

    /// <summary>Settlement initial/genesis state root as 0x-hex.</summary>
    public required string InitialStateRoot { get; init; }

    /// <summary>Recovery summary.</summary>
    public required LocalHostRecoveryDocument Recovery { get; init; }

    /// <summary>Map a live status snapshot into a JSON document.</summary>
    public static LocalHostOperatorStatusDocument From(LocalHostOperatorStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(status.Recovery);
        return new LocalHostOperatorStatusDocument
        {
            ChainId = status.ChainId,
            BatcherConfiguredChainId = status.BatcherConfiguredChainId,
            SettlementConfiguredChainId = status.SettlementConfiguredChainId,
            RpcChainId = status.RpcChainId,
            ProofType = status.ProofType.ToString(),
            SettlementConfiguredProofType = status.SettlementConfiguredProofType.ToString(),
            IsChainIdConfigConsistent = status.IsChainIdConfigConsistent,
            IsProofTypeConfigConsistent = status.IsProofTypeConfigConsistent,
            DaMode = status.DaMode.ToString(),
            RpcDaMode = status.RpcDaMode.ToString(),
            IsDaModeConfigConsistent = status.IsDaModeConfigConsistent,
            SecurityLevel = status.SecurityLevel.ToString(),
            GatewayEnabled = status.GatewayEnabled,
            Sequencer = status.Sequencer.ToString(),
            Exit = status.Exit.ToString(),
            IsProductionWired = status.IsProductionWired,
            HasSealedBatchSink = status.HasSealedBatchSink,
            NextExpectedBlock = status.NextExpectedBlock,
            HasPendingSealedBatch = status.HasPendingSealedBatch,
            PendingSealedBatchNumber = status.PendingSealedBatchNumber,
            PendingSealedBatchLastBlock = status.PendingSealedBatchLastBlock,
            IsBatcherEnabled = status.IsBatcherEnabled,
            MaxBlocksPerBatch = status.MaxBlocksPerBatch,
            MaxTransactionsPerBatch = status.MaxTransactionsPerBatch,
            MaxBatchAgeMillis = status.MaxBatchAgeMillis,
            HasOpenBatch = status.HasOpenBatch,
            OpenBatchAgeMillis = status.OpenBatchAgeMillis,
            IsOpenBatchPastMaxAge = status.IsOpenBatchPastMaxAge,
            InProgressTxCount = status.InProgressTxCount,
            OpenBatchFirstBlock = status.OpenBatchFirstBlock,
            OpenBatchLastBlock = status.OpenBatchLastBlock,
            OpenBatchBlockCount = status.OpenBatchBlockCount,
            OpenBatchL1MessageCount = status.OpenBatchL1MessageCount,
            OpenBatchL2ToL1MessageCount = status.OpenBatchL2ToL1MessageCount,
            OpenBatchL2ToL2MessageCount = status.OpenBatchL2ToL2MessageCount,
            OpenBatchForcedInclusionCount = status.OpenBatchForcedInclusionCount,
            OpenBatchWithdrawalCount = status.OpenBatchWithdrawalCount,
            SupportsLocalDaReader = status.SupportsLocalDaReader,
            HasL1RpcEndpoint = status.HasL1RpcEndpoint,
            ExpectedNetwork = status.ExpectedNetwork,
            HasSettlementManagerHash = status.HasSettlementManagerHash,
            HasForcedInclusionHash = status.HasForcedInclusionHash,
            HasSharedBridgeHash = status.HasSharedBridgeHash,
            HasMessageRouterHash = status.HasMessageRouterHash,
            HasL2BridgeHash = status.HasL2BridgeHash,
            HasMessageOutbox = status.HasMessageOutbox,
            ConsumedDepositCount = status.ConsumedDepositCount,
            LastAcknowledgedBatchNumber = status.LastAcknowledgedBatchNumber,
            LastAcknowledgedBlock = status.LastAcknowledgedBlock,
            NextBatchNumber = status.NextBatchNumber,
            IsOperatorReady = status.IsOperatorReady,
            HasDepositSource = status.HasDepositSource,
            HasMessageRouter = status.HasMessageRouter,
            HasForcedInclusionFinalizer = status.HasForcedInclusionFinalizer,
            HasSettlementClient = status.HasSettlementClient,
            HasTransactionSender = status.HasTransactionSender,
            L1InboxPendingCount = status.L1InboxPendingCount,
            L1InboxConsumedCount = status.L1InboxConsumedCount,
            KnownInboundNonceCount = status.KnownInboundNonceCount,
            IsMetricsHttpListening = status.IsMetricsHttpListening,
            MetricsBoundPort = status.MetricsBoundPort,
            PendingSettlementCount = status.PendingSettlementCount,
            ReadyDepositCount = status.ReadyDepositCount,
            LatestRpcStateRoot = status.LatestRpcStateRoot.ToString(),
            BridgeAssetCount = status.BridgeAssetCount,
            MetricsEntryCount = status.MetricsEntryCount,
            MessageOutboxL2ToL1Count = status.MessageOutboxL2ToL1Count,
            MessageOutboxL2ToL2Count = status.MessageOutboxL2ToL2Count,
            MessageOutboxL2ToL1Root = status.MessageOutboxL2ToL1Root.ToString(),
            MessageOutboxL2ToL2Root = status.MessageOutboxL2ToL2Root.ToString(),
            StagedWithdrawalCount = status.StagedWithdrawalCount,
            TrackedForcedInclusionNonceCount = status.TrackedForcedInclusionNonceCount,
            KnownForcedInclusionNonceCount = status.KnownForcedInclusionNonceCount,
            HasOverdueForcedInclusion = status.HasOverdueForcedInclusion,
            HasBatchForcedInclusionSource = status.HasBatchForcedInclusionSource,
            HasBatchDepositSource = status.HasBatchDepositSource,
            HasBatchMessageRouter = status.HasBatchMessageRouter,
            MaxForcedTransactionsPerBatch = status.MaxForcedTransactionsPerBatch,
            MaxL1MessagesPerBatch = status.MaxL1MessagesPerBatch,
            MetricsMaxConcurrentConnections = status.MetricsMaxConcurrentConnections,
            IsSettlementEnabled = status.IsSettlementEnabled,
            IsPipelineEnabled = status.IsPipelineEnabled,
            IsSettlementPoisoned = status.IsSettlementPoisoned,
            IsSettlementRetrying = status.IsSettlementRetrying,
            IsSettlementIdle = status.IsSettlementIdle,
            IsPipelineHealthy = status.IsPipelineHealthy,
            PipelineHealthFailures = status.PipelineHealthFailures,
            IsMetricsHttpHealthy = status.IsMetricsHttpHealthy,
            MetricsHttpHealthFailures = status.MetricsHttpHealthFailures,
            IsLocalHostHealthy = status.IsLocalHostHealthy,
            LocalHostHealthFailures = status.LocalHostHealthFailures,
            L1FinalityDepth = status.L1FinalityDepth,
            DepositSourceReadyCount = status.DepositSourceReadyCount,
            DepositSourceReservedCount = status.DepositSourceReservedCount,
            DepositSourceSoftConsumedCount = status.DepositSourceSoftConsumedCount,
            IsMetricsEnabled = status.IsMetricsEnabled,
            MetricsConfiguredPort = status.MetricsConfiguredPort,
            MetricsBindAddress = status.MetricsBindAddress,
            IsMetricsWiringComplete = status.IsMetricsWiringComplete,
            HasMetricsReadinessCheck = status.HasMetricsReadinessCheck,
            IsDepositPipelineWiringComplete = status.IsDepositPipelineWiringComplete,
            IsMessagePipelineWiringComplete = status.IsMessagePipelineWiringComplete,
            IsForcedInclusionPipelineWiringComplete = status.IsForcedInclusionPipelineWiringComplete,
            IsSettlementClientWiringComplete = status.IsSettlementClientWiringComplete,
            ForcedInclusionDeploymentHeight = status.ForcedInclusionDeploymentHeight,
            SharedBridgeDeploymentHeight = status.SharedBridgeDeploymentHeight,
            MessageRouterDeploymentHeight = status.MessageRouterDeploymentHeight,
            IsNeoHubHashWiringComplete = status.IsNeoHubHashWiringComplete,
            IsBatcherInboxWiringComplete = status.IsBatcherInboxWiringComplete,
            IsSecurityLevelProofTypeConsistent = status.IsSecurityLevelProofTypeConsistent,
            IsSecurityLevelDaModeConsistent = status.IsSecurityLevelDaModeConsistent,
            HasExpectedNetwork = status.HasExpectedNetwork,
            HasScannerDeployHeights = status.HasScannerDeployHeights,
            IsOfflinePassportComplete = status.IsOfflinePassportComplete,
            OfflinePassportFailures = status.OfflinePassportFailures,
            HasBatchProver = status.HasBatchProver,
            LatestCheckpointBatchNumber = status.LatestCheckpointBatchNumber,
            LatestCheckpointLastBlock = status.LatestCheckpointLastBlock,
            LatestCheckpointPostStateRoot = status.LatestCheckpointPostStateRoot.ToString(),
            IsBatcherCheckpointAligned = status.IsBatcherCheckpointAligned,
            InitialStateRoot = status.InitialStateRoot.ToString(),
            Recovery = LocalHostRecoveryDocument.From(status.Recovery),
        };
    }
}

/// <summary>JSON-serializable recovery subset of <see cref="SettlementRecoveryStatus"/>.</summary>
public sealed record LocalHostRecoveryDocument
{
    /// <summary>Pending artifact count.</summary>
    public required int PendingCount { get; init; }

    /// <summary>Confirmation lag in batches.</summary>
    public required int ConfirmationLagBatches { get; init; }

    /// <summary>Recovery state name, if any.</summary>
    public string? State { get; init; }

    /// <summary>Blocked batch number, if any.</summary>
    public ulong? BlockedBatchNumber { get; init; }

    /// <summary>Artifact content hash as 0x-hex, if any.</summary>
    public string? ArtifactContentHash { get; init; }

    /// <summary>Retry count.</summary>
    public required int RetryCount { get; init; }

    /// <summary>Last error text, if any.</summary>
    public string? LastError { get; init; }

    /// <summary>UTC Unix milliseconds of the first consecutive failure, if any.</summary>
    public long? FirstFailureAtUnixMilliseconds { get; init; }

    /// <summary>UTC Unix milliseconds of the most recent failure, if any.</summary>
    public long? LastFailureAtUnixMilliseconds { get; init; }

    /// <summary>Map a recovery snapshot into a JSON document.</summary>
    public static LocalHostRecoveryDocument From(SettlementRecoveryStatus recovery)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        return new LocalHostRecoveryDocument
        {
            PendingCount = recovery.PendingCount,
            ConfirmationLagBatches = recovery.ConfirmationLagBatches,
            State = recovery.State?.ToString(),
            BlockedBatchNumber = recovery.BlockedBatchNumber,
            ArtifactContentHash = recovery.ArtifactContentHash?.ToString(),
            RetryCount = recovery.RetryCount,
            LastError = recovery.LastError,
            FirstFailureAtUnixMilliseconds = recovery.FirstFailureAtUnixMilliseconds,
            LastFailureAtUnixMilliseconds = recovery.LastFailureAtUnixMilliseconds,
        };
    }
}
