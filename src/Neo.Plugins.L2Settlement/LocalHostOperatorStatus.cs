using Neo.L2;
using Neo.L2.Persistence;

namespace Neo.Plugins.L2;

/// <summary>
/// Aggregated LocalHost operator readiness snapshot for health/ops without Neo.CLI.
/// </summary>
/// <remarks>
/// See doc.md §7.5 / §14.2. Built by Multisig/Optimistic/Zk
/// <c>GetOperatorStatusAsync</c> from durable local state; L1 publication remains a funded
/// gate and is not reflected here beyond recovery lag counts already stored durably.
/// </remarks>
public sealed record LocalHostOperatorStatus
{
    /// <summary>L2 chain id from the wired bridge inbox.</summary>
    public required uint ChainId { get; init; }

    /// <summary>
    /// Batcher plugin settings chain id (0 when unset). Offline consistency check vs
    /// <see cref="ChainId"/>.
    /// </summary>
    public required uint BatcherConfiguredChainId { get; init; }

    /// <summary>
    /// Settlement plugin settings chain id. Offline consistency check vs
    /// <see cref="ChainId"/> / <see cref="BatcherConfiguredChainId"/>.
    /// </summary>
    public required uint SettlementConfiguredChainId { get; init; }

    /// <summary>L2 chain id advertised by the durable L2 RPC store.</summary>
    public required uint RpcChainId { get; init; }

    /// <summary>Configured proof type of the wired prover.</summary>
    public required ProofType ProofType { get; init; }

    /// <summary>
    /// Settlement plugin settings proof type. Offline consistency check vs
    /// <see cref="ProofType"/>.
    /// </summary>
    public required ProofType SettlementConfiguredProofType { get; init; }

    /// <summary>
    /// True when <see cref="ChainId"/> equals batcher, settlement, and RPC store chain ids.
    /// </summary>
    public required bool IsChainIdConfigConsistent { get; init; }

    /// <summary>
    /// True when host <see cref="ProofType"/> equals settlement configured proof type.
    /// </summary>
    public required bool IsProofTypeConfigConsistent { get; init; }

    /// <summary>DA mode of the wired DA writer.</summary>
    public required DAMode DaMode { get; init; }

    /// <summary>DA mode advertised by the durable L2 RPC store.</summary>
    public required DAMode RpcDaMode { get; init; }

    /// <summary>
    /// True when wired DA writer <see cref="DaMode"/> equals <see cref="RpcDaMode"/>.
    /// </summary>
    public required bool IsDaModeConfigConsistent { get; init; }

    /// <summary>Security level advertised by the durable L2 RPC store.</summary>
    public required SecurityLevel SecurityLevel { get; init; }

    /// <summary>Whether the L2 RPC store advertises Gateway support.</summary>
    public required bool GatewayEnabled { get; init; }

    /// <summary>Sequencer model advertised by the durable L2 RPC store.</summary>
    public required SequencerModel Sequencer { get; init; }

    /// <summary>Exit model advertised by the durable L2 RPC store.</summary>
    public required ExitModel Exit { get; init; }

    /// <summary>True after WireProduction installed the production composition.</summary>
    public required bool IsProductionWired { get; init; }

    /// <summary>True when the sealed-batch sink is installed on the batcher.</summary>
    public required bool HasSealedBatchSink { get; init; }

    /// <summary>
    /// Next L2 block index the batcher expects, or null when the sealer is not restored.
    /// </summary>
    public required ulong? NextExpectedBlock { get; init; }

    /// <summary>True when a sealed batch awaits durable persistence / acknowledgement.</summary>
    public required bool HasPendingSealedBatch { get; init; }

    /// <summary>Pending sealed batch number, or null when none is pending.</summary>
    public required ulong? PendingSealedBatchNumber { get; init; }

    /// <summary>Last L2 block of the pending sealed batch, or null when none is pending.</summary>
    public required ulong? PendingSealedBatchLastBlock { get; init; }

    /// <summary>Whether the batcher plugin is enabled in settings.</summary>
    public required bool IsBatcherEnabled { get; init; }

    /// <summary>Configured max L2 blocks per sealed batch.</summary>
    public required int MaxBlocksPerBatch { get; init; }

    /// <summary>Configured max transactions per sealed batch.</summary>
    public required int MaxTransactionsPerBatch { get; init; }

    /// <summary>Configured max open-batch age in milliseconds.</summary>
    public required int MaxBatchAgeMillis { get; init; }

    /// <summary>True when a batch is currently being accumulated by the batcher.</summary>
    public required bool HasOpenBatch { get; init; }

    /// <summary>
    /// Wall-clock age of the open batch in milliseconds, or null when none is open.
    /// </summary>
    public required long? OpenBatchAgeMillis { get; init; }

    /// <summary>
    /// True when the open batch is at or past <see cref="MaxBatchAgeMillis"/> (seal-by-age overdue).
    /// Included in <see cref="PipelineHealthFailures"/> when true.
    /// </summary>
    public required bool IsOpenBatchPastMaxAge { get; init; }

    /// <summary>Transaction count in the open batch (0 when none is open).</summary>
    public required int InProgressTxCount { get; init; }

    /// <summary>First L2 block in the open batch, or null when none is open.</summary>
    public required ulong? OpenBatchFirstBlock { get; init; }

    /// <summary>Last L2 block in the open batch, or null when none is open.</summary>
    public required ulong? OpenBatchLastBlock { get; init; }

    /// <summary>Block count in the open batch (0 when none is open).</summary>
    public required int OpenBatchBlockCount { get; init; }

    /// <summary>L1 messages consumed into the open batch (0 when none is open).</summary>
    public required int OpenBatchL1MessageCount { get; init; }

    /// <summary>L2→L1 messages staged in the open batch (0 when none is open).</summary>
    public required int OpenBatchL2ToL1MessageCount { get; init; }

    /// <summary>L2→L2 messages staged in the open batch (0 when none is open).</summary>
    public required int OpenBatchL2ToL2MessageCount { get; init; }

    /// <summary>Forced-inclusion entries staged in the open batch (0 when none is open).</summary>
    public required int OpenBatchForcedInclusionCount { get; init; }

    /// <summary>Withdrawals staged in the open batch (0 when none is open).</summary>
    public required int OpenBatchWithdrawalCount { get; init; }

    /// <summary>
    /// True when this host can open a local DA reader
    /// (Multisig/Optimistic local DA; Zk production DA does not expose a local reader).
    /// </summary>
    public required bool SupportsLocalDaReader { get; init; }

    /// <summary>
    /// True when settlement settings include a non-empty L1 RPC endpoint
    /// (connectivity not probed).
    /// </summary>
    public required bool HasL1RpcEndpoint { get; init; }

    /// <summary>Configured expected L1 network magic, or null when unset.</summary>
    public required uint? ExpectedNetwork { get; init; }

    /// <summary>
    /// Settlement settings include a SettlementManager hash (not on-chain verified).
    /// </summary>
    public required bool HasSettlementManagerHash { get; init; }

    /// <summary>Settlement settings include a ForcedInclusion hash.</summary>
    public required bool HasForcedInclusionHash { get; init; }

    /// <summary>Settlement settings include a SharedBridge hash.</summary>
    public required bool HasSharedBridgeHash { get; init; }

    /// <summary>Settlement settings include a MessageRouter hash.</summary>
    public required bool HasMessageRouterHash { get; init; }

    /// <summary>
    /// Settlement settings include an explicit L2 bridge hash
    /// (empty means native L2Bridge default).
    /// </summary>
    public required bool HasL2BridgeHash { get; init; }

    /// <summary>
    /// True when WireProduction installed a MessageRouter that exposes an L2 outbox.
    /// Distinct from outbox depth/root zeros when unwired.
    /// </summary>
    public required bool HasMessageOutbox { get; init; }

    /// <summary>Soft in-memory consumed-deposit cache size.</summary>
    public required int ConsumedDepositCount { get; init; }

    /// <summary>Last batch number that completed durable persist + acknowledgement.</summary>
    public required ulong LastAcknowledgedBatchNumber { get; init; }

    /// <summary>Last L2 block covered by the last acknowledged batch.</summary>
    public required ulong LastAcknowledgedBlock { get; init; }

    /// <summary>Batch number that will be assigned to the next sealed batch.</summary>
    public required ulong NextBatchNumber { get; init; }

    /// <summary>
    /// <see cref="IsProductionWired"/> and <see cref="HasSealedBatchSink"/> — matches default
    /// LocalHost <c>/readyz</c>.
    /// </summary>
    public required bool IsOperatorReady { get; init; }

    /// <summary>True when WireProduction installed a SharedBridge deposit source.</summary>
    public required bool HasDepositSource { get; init; }

    /// <summary>True when WireProduction installed a MessageRouter.</summary>
    public required bool HasMessageRouter { get; init; }

    /// <summary>True when WireProduction installed a forced-inclusion finalizer.</summary>
    public required bool HasForcedInclusionFinalizer { get; init; }

    /// <summary>True when WireProduction installed a settlement client.</summary>
    public required bool HasSettlementClient { get; init; }

    /// <summary>True when WireProduction installed an L1 transaction sender.</summary>
    public required bool HasTransactionSender { get; init; }

    /// <summary>
    /// Unconsumed L1→L2 messages buffered in the bridge inbox
    /// (<see cref="Neo.L2.Messaging.L1MessageInbox.PendingCount"/>).
    /// </summary>
    public required int L1InboxPendingCount { get; init; }

    /// <summary>
    /// Messages ever consumed from the bridge inbox
    /// (<see cref="Neo.L2.Messaging.L1MessageInbox.ConsumedCount"/>).
    /// </summary>
    public required int L1InboxConsumedCount { get; init; }

    /// <summary>
    /// Known MessageRouter L1→L2 inbound nonces (0 when MessageRouter is unwired).
    /// </summary>
    public required int KnownInboundNonceCount { get; init; }

    /// <summary>True when the metrics HTTP server is listening.</summary>
    public required bool IsMetricsHttpListening { get; init; }

    /// <summary>Bound metrics HTTP port (0 when not listening).</summary>
    public required int MetricsBoundPort { get; init; }

    /// <summary>Durable settlement artifacts not yet finalized on L1.</summary>
    public required int PendingSettlementCount { get; init; }

    /// <summary>
    /// Count of ready SharedBridge deposits visible via non-mutating peek
    /// (capped by the peek limit passed to the status builder).
    /// </summary>
    public required int ReadyDepositCount { get; init; }

    /// <summary>
    /// Latest finalized L2 state root from the host RPC store (zero until a batch is finalized).
    /// </summary>
    public required UInt256 LatestRpcStateRoot { get; init; }

    /// <summary>Count of L1↔L2 mappings in the bridge plugin asset registry.</summary>
    public required int BridgeAssetCount { get; init; }

    /// <summary>Total counter+gauge+histogram entries in the metrics snapshot.</summary>
    public required int MetricsEntryCount { get; init; }

    /// <summary>L2→L1 messages staged in the MessageRouter outbox (0 when unwired).</summary>
    public required int MessageOutboxL2ToL1Count { get; init; }

    /// <summary>L2→L2 messages staged in the MessageRouter outbox (0 when unwired).</summary>
    public required int MessageOutboxL2ToL2Count { get; init; }

    /// <summary>Current L2→L1 outbox root (zero when unwired/empty).</summary>
    public required UInt256 MessageOutboxL2ToL1Root { get; init; }

    /// <summary>Current L2→L2 outbox root (zero when unwired/empty).</summary>
    public required UInt256 MessageOutboxL2ToL2Root { get; init; }

    /// <summary>Withdrawals staged in the bridge withdrawal tree for the open batch.</summary>
    public required int StagedWithdrawalCount { get; init; }

    /// <summary>Durable pending / retry / poison recovery surface.</summary>
    public required SettlementRecoveryStatus Recovery { get; init; }

    /// <summary>Count of forced-inclusion nonces tracked in durable settlement state.</summary>
    public required int TrackedForcedInclusionNonceCount { get; init; }

    /// <summary>
    /// Soft known-nonce count on the in-process FI source
    /// (<see cref="Neo.L2.ForcedInclusion.RpcForcedInclusionSource.KnownNonceCount"/>).
    /// Distinct from durable <see cref="TrackedForcedInclusionNonceCount"/>.
    /// </summary>
    public required int KnownForcedInclusionNonceCount { get; init; }

    /// <summary>
    /// True when the last local FI drain cache has an entry past the status snapshot clock
    /// (soft, no L1 scan). Live L1 overdue remains <c>HasOverdueForcedInclusionAsync</c>.
    /// Included in <see cref="PipelineHealthFailures"/>.
    /// </summary>
    public required bool HasOverdueForcedInclusion { get; init; }

    /// <summary>
    /// True when the batcher has a forced-inclusion source wired
    /// (<see cref="L2BatchPlugin.HasForcedInclusionSource"/>).
    /// </summary>
    public required bool HasBatchForcedInclusionSource { get; init; }

    /// <summary>
    /// True when the batcher has a SharedBridge deposit source wired
    /// (<see cref="L2BatchPlugin.HasDepositSource"/>).
    /// </summary>
    public required bool HasBatchDepositSource { get; init; }

    /// <summary>
    /// True when the batcher has a MessageRouter wired
    /// (<see cref="L2BatchPlugin.HasMessageRouter"/>).
    /// </summary>
    public required bool HasBatchMessageRouter { get; init; }

    /// <summary>
    /// Max forced-inclusion entries per sealed batch
    /// (<see cref="L2BatchPlugin.MaxForcedTransactionsPerBatch"/>).
    /// </summary>
    public required int MaxForcedTransactionsPerBatch { get; init; }

    /// <summary>
    /// Max L1 inbox messages per sealed batch
    /// (<see cref="L2BatchPlugin.MaxL1MessagesPerBatch"/>).
    /// </summary>
    public required int MaxL1MessagesPerBatch { get; init; }

    /// <summary>
    /// Configured metrics max concurrent HTTP connections
    /// (<see cref="L2MetricsPlugin.MaxConcurrentConnections"/>).
    /// </summary>
    public required int MetricsMaxConcurrentConnections { get; init; }

    /// <summary>Whether settlement plugin submit/reconcile is enabled in settings.</summary>
    public required bool IsSettlementEnabled { get; init; }

    /// <summary>
    /// True when both batcher and settlement plugins are enabled in settings
    /// (<see cref="IsBatcherEnabled"/> and <see cref="IsSettlementEnabled"/>).
    /// </summary>
    public required bool IsPipelineEnabled { get; init; }

    /// <summary>
    /// True when durable settlement recovery state is poisoned (operator recovery required).
    /// Runtime state from local artifacts; not an L1 claim.
    /// </summary>
    public required bool IsSettlementPoisoned { get; init; }

    /// <summary>
    /// True when durable settlement recovery state is retrying (bounded automatic retries).
    /// Distinct from <see cref="IsSettlementPoisoned"/>; not an L1 claim.
    /// </summary>
    public required bool IsSettlementRetrying { get; init; }

    /// <summary>
    /// True when no pending settlement artifacts and recovery is not retrying/poisoned.
    /// Runtime idle health from local stores (not L1 confirmation).
    /// </summary>
    public required bool IsSettlementIdle { get; init; }

    /// <summary>
    /// Offline passport complete, pipeline enabled, no pending sealed batch, no open-batch age
    /// overdue, batcher/checkpoint aligned, no overdue forced inclusion, settlement not
    /// poisoned/retrying, and settlement idle. Distinct from L1 settle confirmation (funded gate).
    /// True when <see cref="PipelineHealthFailures"/> is empty.
    /// </summary>
    public required bool IsPipelineHealthy { get; init; }

    /// <summary>
    /// Names of pipeline health checks that failed (empty when <see cref="IsPipelineHealthy"/>).
    /// Combines offline passport rollup with runtime sealed-batch pending, open-batch age overdue,
    /// checkpoint alignment, overdue forced inclusion, settlement idle/retry/poison, and enablement.
    /// </summary>
    public required IReadOnlyList<string> PipelineHealthFailures { get; init; }

    /// <summary>
    /// Metrics HTTP runtime health when metrics are enabled in settings: wiring complete,
    /// HTTP listening, and <c>/readyz</c> predicate installed. When metrics are disabled,
    /// treated as healthy (not required). True when <see cref="MetricsHttpHealthFailures"/>
    /// is empty. Does not claim scrape clients or L1.
    /// </summary>
    public required bool IsMetricsHttpHealthy { get; init; }

    /// <summary>
    /// Names of metrics HTTP health checks that failed (empty when
    /// <see cref="IsMetricsHttpHealthy"/>). Empty when metrics are disabled.
    /// </summary>
    public required IReadOnlyList<string> MetricsHttpHealthFailures { get; init; }

    /// <summary>
    /// Combined local operator health: <see cref="IsPipelineHealthy"/> and
    /// <see cref="IsMetricsHttpHealthy"/>. True when <see cref="LocalHostHealthFailures"/>
    /// is empty. Does not claim L1 settle or external scrapers (funded / out-of-band).
    /// </summary>
    public required bool IsLocalHostHealthy { get; init; }

    /// <summary>
    /// Union of <see cref="PipelineHealthFailures"/> and
    /// <see cref="MetricsHttpHealthFailures"/> (empty when <see cref="IsLocalHostHealthy"/>).
    /// </summary>
    public required IReadOnlyList<string> LocalHostHealthFailures { get; init; }

    /// <summary>Configured L1 finality depth for production scanners.</summary>
    public required uint L1FinalityDepth { get; init; }

    /// <summary>
    /// Soft ready depth on the production deposit source (0 when unwired). Exact cache size,
    /// not the capped <see cref="ReadyDepositCount"/> peek.
    /// </summary>
    public required int DepositSourceReadyCount { get; init; }

    /// <summary>
    /// Soft reserved depth on the production deposit source (0 when unwired).
    /// </summary>
    public required int DepositSourceReservedCount { get; init; }

    /// <summary>
    /// Soft consumed-nonce cache size on the production deposit source (0 when unwired).
    /// Distinct from bridge <see cref="ConsumedDepositCount"/>.
    /// </summary>
    public required int DepositSourceSoftConsumedCount { get; init; }

    /// <summary>Whether metrics HTTP is enabled in settings.</summary>
    public required bool IsMetricsEnabled { get; init; }

    /// <summary>Configured metrics HTTP port from settings (0-bound until listening).</summary>
    public required int MetricsConfiguredPort { get; init; }

    /// <summary>Configured metrics bind address from settings.</summary>
    public required string MetricsBindAddress { get; init; }

    /// <summary>
    /// True when metrics settings are operator-ready: enabled, port &gt; 0, non-empty bind address.
    /// Does not claim the HTTP server is listening (<see cref="IsMetricsHttpListening"/>).
    /// </summary>
    public required bool IsMetricsWiringComplete { get; init; }

    /// <summary>True when a <c>/readyz</c> readiness predicate is installed on the metrics plugin.</summary>
    public required bool HasMetricsReadinessCheck { get; init; }

    /// <summary>
    /// True when production deposit source and batcher deposit source are both wired.
    /// </summary>
    public required bool IsDepositPipelineWiringComplete { get; init; }

    /// <summary>
    /// True when production MessageRouter, batcher MessageRouter, and MessageOutbox are wired.
    /// </summary>
    public required bool IsMessagePipelineWiringComplete { get; init; }

    /// <summary>
    /// True when forced-inclusion finalizer and batcher forced-inclusion source are both wired.
    /// </summary>
    public required bool IsForcedInclusionPipelineWiringComplete { get; init; }

    /// <summary>
    /// True when settlement client, transaction sender, and settlement enablement are all ready.
    /// </summary>
    public required bool IsSettlementClientWiringComplete { get; init; }

    /// <summary>ForcedInclusion scanner deployment height (0 when unset).</summary>
    public required uint ForcedInclusionDeploymentHeight { get; init; }

    /// <summary>SharedBridge scanner deployment height (0 when unset).</summary>
    public required uint SharedBridgeDeploymentHeight { get; init; }

    /// <summary>MessageRouter scanner deployment height (0 when unset).</summary>
    public required uint MessageRouterDeploymentHeight { get; init; }

    /// <summary>
    /// True when settlement settings include SettlementManager, ForcedInclusion, SharedBridge,
    /// and MessageRouter hashes (presence only; not on-chain verified).
    /// </summary>
    public required bool IsNeoHubHashWiringComplete { get; init; }

    /// <summary>
    /// True when the batcher has deposit, message-router, and forced-inclusion inbox sources.
    /// </summary>
    public required bool IsBatcherInboxWiringComplete { get; init; }

    /// <summary>
    /// True when RPC-store <see cref="SecurityLevel"/> is a recommended pairing with host
    /// <see cref="ProofType"/> (offline heuristic matching chain-config validation tips).
    /// </summary>
    public required bool IsSecurityLevelProofTypeConsistent { get; init; }

    /// <summary>
    /// True when RPC-store <see cref="SecurityLevel"/> is a recommended pairing with host
    /// <see cref="DaMode"/> (Validity→L1, Validium→off-chain DA).
    /// </summary>
    public required bool IsSecurityLevelDaModeConsistent { get; init; }

    /// <summary>True when settlement settings include a non-null expected L1 network magic.</summary>
    public required bool HasExpectedNetwork { get; init; }

    /// <summary>
    /// True when ForcedInclusion, SharedBridge, and MessageRouter scanner deploy heights are
    /// all non-zero (presence of start heights; not live L1 scan).
    /// </summary>
    public required bool HasScannerDeployHeights { get; init; }

    /// <summary>
    /// Offline operator passport: ready + config consistency + NeoHub/inbox wiring +
    /// production plugin enablement + L1 endpoint/network/heights + deposit/message/FI/client
    /// surfaces. Does not claim L1 settle, prove-batch, or live scan (funded gates).
    /// True when <see cref="OfflinePassportFailures"/> is empty.
    /// </summary>
    public required bool IsOfflinePassportComplete { get; init; }

    /// <summary>
    /// Names of offline passport checks that failed (empty when
    /// <see cref="IsOfflinePassportComplete"/>). Diagnostic only; not an L1 claim.
    /// </summary>
    public required IReadOnlyList<string> OfflinePassportFailures { get; init; }

    /// <summary>
    /// True when the batch prover plugin has installed an <see cref="IL2Prover"/>.
    /// Multisig prove is offline; Zk may still need a funded executor/daemon.
    /// </summary>
    public required bool HasBatchProver { get; init; }

    /// <summary>
    /// Durable sealed-batch checkpoint batch number from local artifacts only
    /// (no L1 refresh), or null when none is stored.
    /// </summary>
    public required ulong? LatestCheckpointBatchNumber { get; init; }

    /// <summary>
    /// Last L2 block of the durable sealed-batch checkpoint, or null when none is stored.
    /// </summary>
    public required ulong? LatestCheckpointLastBlock { get; init; }

    /// <summary>
    /// Post-state root of the durable sealed-batch checkpoint (zero when none is stored).
    /// </summary>
    public required UInt256 LatestCheckpointPostStateRoot { get; init; }

    /// <summary>
    /// True when batcher <see cref="LastAcknowledgedBatchNumber"/> matches durable
    /// <see cref="LatestCheckpointBatchNumber"/> (or both empty/zero). Local integrity only.
    /// </summary>
    public required bool IsBatcherCheckpointAligned { get; init; }

    /// <summary>
    /// Authenticated genesis / initial state root from the settlement sink (zero for legacy).
    /// </summary>
    public required UInt256 InitialStateRoot { get; init; }

    /// <summary>
    /// Offline heuristic: whether <paramref name="securityLevel"/> is a recommended pairing
    /// with <paramref name="proofType"/> (mirrors chain-config validation tips).
    /// </summary>
    public static bool IsSecurityLevelPairedWithProofType(SecurityLevel securityLevel, ProofType proofType)
        => securityLevel switch
        {
            SecurityLevel.Validity or SecurityLevel.Validium => proofType == ProofType.Zk,
            SecurityLevel.Optimistic =>
                proofType is ProofType.Optimistic or ProofType.Multisig,
            SecurityLevel.Sidechain or SecurityLevel.Settled =>
                proofType is ProofType.None or ProofType.Multisig,
            _ => false,
        };

    /// <summary>
    /// Offline heuristic: whether <paramref name="securityLevel"/> is a recommended pairing
    /// with <paramref name="daMode"/> (Validity requires L1 DA; Validium requires off-chain DA).
    /// </summary>
    public static bool IsSecurityLevelPairedWithDaMode(SecurityLevel securityLevel, DAMode daMode)
        => securityLevel switch
        {
            SecurityLevel.Validity => daMode == DAMode.L1,
            SecurityLevel.Validium =>
                daMode is DAMode.NeoFS or DAMode.External or DAMode.DAC,
            // Sidechain/Settled/Optimistic may use Local (dev) or any public DA tier.
            _ => true,
        };

    /// <summary>
    /// Offline alignment of batcher last-acked batch vs durable settlement checkpoint.
    /// Fresh (no ack, no checkpoint) is aligned; otherwise batch numbers must match.
    /// </summary>
    public static bool AreBatcherAndCheckpointAligned(
        ulong lastAcknowledgedBatchNumber,
        ulong? latestCheckpointBatchNumber)
    {
        if (latestCheckpointBatchNumber is null)
            return lastAcknowledgedBatchNumber == 0;
        return latestCheckpointBatchNumber.Value == lastAcknowledgedBatchNumber;
    }

    /// <summary>
    /// Build pipeline health failure names from offline passport + enablement + local batcher
    /// pending seal + checkpoint alignment + overdue forced inclusion + settlement runtime state.
    /// Empty list means <see cref="IsPipelineHealthy"/>. Not an L1 settle claim.
    /// </summary>
    public static IReadOnlyList<string> BuildPipelineHealthFailures(
        bool offlinePassportComplete,
        bool pipelineEnabled,
        bool hasPendingSealedBatch,
        bool isOpenBatchPastMaxAge,
        bool isBatcherCheckpointAligned,
        bool hasOverdueForcedInclusion,
        int pendingSettlementCount,
        SettlementRecoveryStatus recovery)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        var failures = new List<string>();
        if (!offlinePassportComplete)
            failures.Add(nameof(IsOfflinePassportComplete));
        if (!pipelineEnabled)
            failures.Add(nameof(IsPipelineEnabled));
        if (hasPendingSealedBatch)
            failures.Add(nameof(HasPendingSealedBatch));
        if (isOpenBatchPastMaxAge)
            failures.Add(nameof(IsOpenBatchPastMaxAge));
        if (!isBatcherCheckpointAligned)
            failures.Add(nameof(IsBatcherCheckpointAligned));
        if (hasOverdueForcedInclusion)
            failures.Add(nameof(HasOverdueForcedInclusion));
        var isPoisoned = recovery.State == SettlementRecoveryState.Poisoned;
        var isRetrying = recovery.State == SettlementRecoveryState.Retrying;
        // Prefer specific recovery labels over a generic idle miss when state is set.
        if (isPoisoned)
            failures.Add(nameof(IsSettlementPoisoned));
        else if (isRetrying)
            failures.Add(nameof(IsSettlementRetrying));
        var settlementIdle = pendingSettlementCount == 0
            && recovery.PendingCount == 0
            && !isPoisoned
            && !isRetrying
            && string.IsNullOrEmpty(recovery.LastError);
        if (!settlementIdle && !isPoisoned && !isRetrying)
            failures.Add(nameof(IsSettlementIdle));
        return failures;
    }

    /// <summary>
    /// Build metrics HTTP health failure names. When metrics are disabled, returns empty
    /// (not required). When enabled, requires wiring + listening + readiness check.
    /// </summary>
    public static IReadOnlyList<string> BuildMetricsHttpHealthFailures(
        bool metricsEnabled,
        bool metricsWiringComplete,
        bool metricsHttpListening,
        bool hasMetricsReadinessCheck)
    {
        if (!metricsEnabled)
            return Array.Empty<string>();

        var failures = new List<string>();
        if (!metricsWiringComplete)
            failures.Add(nameof(IsMetricsWiringComplete));
        if (!metricsHttpListening)
            failures.Add(nameof(IsMetricsHttpListening));
        if (!hasMetricsReadinessCheck)
            failures.Add(nameof(HasMetricsReadinessCheck));
        return failures;
    }

    /// <summary>
    /// Union of pipeline and metrics HTTP health failure names for a single local-host
    /// rollup. Empty means <see cref="IsLocalHostHealthy"/>.
    /// </summary>
    public static IReadOnlyList<string> BuildLocalHostHealthFailures(
        IReadOnlyList<string> pipelineHealthFailures,
        IReadOnlyList<string> metricsHttpHealthFailures)
    {
        ArgumentNullException.ThrowIfNull(pipelineHealthFailures);
        ArgumentNullException.ThrowIfNull(metricsHttpHealthFailures);
        if (pipelineHealthFailures.Count == 0 && metricsHttpHealthFailures.Count == 0)
            return Array.Empty<string>();
        var failures = new List<string>(
            pipelineHealthFailures.Count + metricsHttpHealthFailures.Count);
        failures.AddRange(pipelineHealthFailures);
        failures.AddRange(metricsHttpHealthFailures);
        return failures;
    }
}
