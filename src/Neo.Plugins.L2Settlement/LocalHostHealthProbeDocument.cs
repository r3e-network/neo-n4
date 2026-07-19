using System.Text.Json;

namespace Neo.Plugins.L2;

/// <summary>
/// Compact LocalHost health probe for ops scripts (passport / pipeline / metrics / settlement).
/// </summary>
/// <remarks>
/// See doc.md §7.5 / §14.2. Smaller than <see cref="LocalHostOperatorStatusDocument"/>;
/// does not claim L1 settle, prove-batch, or live scan (funded gates).
/// Produced by Multisig/Optimistic/Zk <c>GetHealthProbeAsync</c> /
/// <c>WriteHealthProbeAsync</c> / metrics HTTP <c>/healthprobe</c>.
/// </remarks>
public sealed record LocalHostHealthProbeDocument
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Serialize as indented camelCase JSON (trailing newline) for ops scripts and
    /// metrics HTTP <c>/healthprobe</c>.
    /// </summary>
    public static string FormatJson(LocalHostHealthProbeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(document, JsonOptions) + Environment.NewLine;
    }

    /// <summary>Offline wiring/config passport is complete.</summary>
    public required bool IsOfflinePassportComplete { get; init; }

    /// <summary>Failed offline passport checks (empty when complete).</summary>
    public required IReadOnlyList<string> OfflinePassportFailures { get; init; }

    /// <summary>
    /// Host L2 chain id (bridge). Helps interpret <see cref="IsChainIdConfigConsistent"/>
    /// without the full status dump.
    /// </summary>
    public required uint ChainId { get; init; }

    /// <summary>Batcher configured chain id (offline).</summary>
    public required uint BatcherConfiguredChainId { get; init; }

    /// <summary>Settlement configured chain id (offline).</summary>
    public required uint SettlementConfiguredChainId { get; init; }

    /// <summary>RPC store chain id (offline).</summary>
    public required uint RpcChainId { get; init; }

    /// <summary>Host proof type name (prover kind).</summary>
    public required string ProofType { get; init; }

    /// <summary>Settlement configured proof type name (offline).</summary>
    public required string SettlementConfiguredProofType { get; init; }

    /// <summary>Host DA mode name (offline).</summary>
    public required string DaMode { get; init; }

    /// <summary>RPC store DA mode name (offline).</summary>
    public required string RpcDaMode { get; init; }

    /// <summary>RPC store security level name (offline).</summary>
    public required string SecurityLevel { get; init; }

    /// <summary>Expected L1 network magic when configured; otherwise null.</summary>
    public required uint? ExpectedNetwork { get; init; }

    /// <summary>Metrics plugin enabled in settings (offline).</summary>
    public required bool IsMetricsEnabled { get; init; }

    /// <summary>Metrics wiring complete for HTTP health when metrics are enabled.</summary>
    public required bool IsMetricsWiringComplete { get; init; }

    /// <summary>Configured metrics HTTP port from settings (0 when unset).</summary>
    public required int MetricsConfiguredPort { get; init; }

    /// <summary>Configured metrics bind address (offline).</summary>
    public required string MetricsBindAddress { get; init; }

    /// <summary>Configured max concurrent metrics HTTP connections.</summary>
    public required int MetricsMaxConcurrentConnections { get; init; }

    /// <summary>
    /// Soft metrics snapshot entry count when exportable (local sink only).
    /// </summary>
    public required int MetricsEntryCount { get; init; }

    /// <summary>RPC-store gateway enabled flag (offline config).</summary>
    public required bool GatewayEnabled { get; init; }

    /// <summary>Local DA reader is supported for this host profile (offline).</summary>
    public required bool SupportsLocalDaReader { get; init; }

    /// <summary>Registered bridge assets count (local registry; soft).</summary>
    public required int BridgeAssetCount { get; init; }

    /// <summary>Production wired and sealed-batch sink present (local operator ready).</summary>
    public required bool IsOperatorReady { get; init; }

    /// <summary>WireProduction installed the production composition (local).</summary>
    public required bool IsProductionWired { get; init; }

    /// <summary>Sealed-batch sink is installed on the batcher (local).</summary>
    public required bool HasSealedBatchSink { get; init; }

    /// <summary>Batch prover is wired on the batcher (local; not a prove-batch claim).</summary>
    public required bool HasBatchProver { get; init; }

    /// <summary>Settlement plugin enabled in settings.</summary>
    public required bool IsSettlementEnabled { get; init; }

    /// <summary>Batcher plugin enabled in settings.</summary>
    public required bool IsBatcherEnabled { get; init; }

    /// <summary>L1 JSON-RPC endpoint configured (offline; not a live RPC claim).</summary>
    public required bool HasL1RpcEndpoint { get; init; }

    /// <summary>Expected network magic configured (offline).</summary>
    public required bool HasExpectedNetwork { get; init; }

    /// <summary>Scanner deploy heights present for FI/bridge/router (offline).</summary>
    public required bool HasScannerDeployHeights { get; init; }

    /// <summary>
    /// ForcedInclusion scanner deployment height (0 when unset). Helps interpret
    /// <see cref="HasScannerDeployHeights"/>.
    /// </summary>
    public required uint ForcedInclusionDeploymentHeight { get; init; }

    /// <summary>SharedBridge scanner deployment height (0 when unset).</summary>
    public required uint SharedBridgeDeploymentHeight { get; init; }

    /// <summary>MessageRouter scanner deployment height (0 when unset).</summary>
    public required uint MessageRouterDeploymentHeight { get; init; }

    /// <summary>
    /// SettlementManager hash present in settings (offline; not on-chain verified).
    /// Helps interpret <see cref="IsNeoHubHashWiringComplete"/>.
    /// </summary>
    public required bool HasSettlementManagerHash { get; init; }

    /// <summary>ForcedInclusion contract hash present in settings (offline).</summary>
    public required bool HasForcedInclusionHash { get; init; }

    /// <summary>SharedBridge contract hash present in settings (offline).</summary>
    public required bool HasSharedBridgeHash { get; init; }

    /// <summary>MessageRouter contract hash present in settings (offline).</summary>
    public required bool HasMessageRouterHash { get; init; }

    /// <summary>L2 bridge hash present in settings (offline; optional for some profiles).</summary>
    public required bool HasL2BridgeHash { get; init; }

    /// <summary>L1 finality depth from settlement settings (offline).</summary>
    public required uint L1FinalityDepth { get; init; }

    /// <summary>Production deposit source wired (local).</summary>
    public required bool HasDepositSource { get; init; }

    /// <summary>Production MessageRouter wired (local).</summary>
    public required bool HasMessageRouter { get; init; }

    /// <summary>Forced-inclusion finalizer wired (local).</summary>
    public required bool HasForcedInclusionFinalizer { get; init; }

    /// <summary>Settlement client wired (local; not L1 settle claim).</summary>
    public required bool HasSettlementClient { get; init; }

    /// <summary>L1 transaction sender wired (local; not broadcast claim).</summary>
    public required bool HasTransactionSender { get; init; }

    /// <summary>
    /// Batcher has a deposit source installed (helps interpret
    /// <see cref="IsBatcherInboxWiringComplete"/>).
    /// </summary>
    public required bool HasBatchDepositSource { get; init; }

    /// <summary>Batcher has MessageRouter installed (local).</summary>
    public required bool HasBatchMessageRouter { get; init; }

    /// <summary>Batcher has forced-inclusion source installed (local).</summary>
    public required bool HasBatchForcedInclusionSource { get; init; }

    /// <summary>Deposit pipeline wiring complete (local composition).</summary>
    public required bool IsDepositPipelineWiringComplete { get; init; }

    /// <summary>Message pipeline wiring complete (local composition).</summary>
    public required bool IsMessagePipelineWiringComplete { get; init; }

    /// <summary>Forced-inclusion pipeline wiring complete (local composition).</summary>
    public required bool IsForcedInclusionPipelineWiringComplete { get; init; }

    /// <summary>Settlement client pipeline wiring complete (local composition).</summary>
    public required bool IsSettlementClientWiringComplete { get; init; }

    /// <summary>Chain id config consistent across host/batcher/settlement/RPC (offline).</summary>
    public required bool IsChainIdConfigConsistent { get; init; }

    /// <summary>Proof type config consistent (offline).</summary>
    public required bool IsProofTypeConfigConsistent { get; init; }

    /// <summary>DA mode config consistent (offline).</summary>
    public required bool IsDaModeConfigConsistent { get; init; }

    /// <summary>
    /// Security level matches configured proof type (offline; helps interpret
    /// <see cref="OfflinePassportFailures"/>).
    /// </summary>
    public required bool IsSecurityLevelProofTypeConsistent { get; init; }

    /// <summary>
    /// Security level matches configured DA mode (offline; helps interpret
    /// <see cref="OfflinePassportFailures"/>).
    /// </summary>
    public required bool IsSecurityLevelDaModeConsistent { get; init; }

    /// <summary>NeoHub hash wiring complete (offline).</summary>
    public required bool IsNeoHubHashWiringComplete { get; init; }

    /// <summary>Batcher inbox wiring complete (offline).</summary>
    public required bool IsBatcherInboxWiringComplete { get; init; }

    /// <summary>Batcher + settlement settings both enabled.</summary>
    public required bool IsPipelineEnabled { get; init; }

    /// <summary>Sealed batch awaits settlement ack (local durable).</summary>
    public required bool HasPendingSealedBatch { get; init; }

    /// <summary>Pending sealed batch number when present; otherwise null.</summary>
    public required ulong? PendingSealedBatchNumber { get; init; }

    /// <summary>Last L2 block of the pending sealed batch when present; otherwise null.</summary>
    public required ulong? PendingSealedBatchLastBlock { get; init; }

    /// <summary>A batch is currently being accumulated (local batcher).</summary>
    public required bool HasOpenBatch { get; init; }

    /// <summary>
    /// Max blocks per sealed batch (batcher settings). Helps interpret open-batch
    /// block counts without the full status dump.
    /// </summary>
    public required int MaxBlocksPerBatch { get; init; }

    /// <summary>Max transactions per sealed batch (batcher settings).</summary>
    public required int MaxTransactionsPerBatch { get; init; }

    /// <summary>
    /// Max open-batch age in milliseconds (batcher settings). Helps interpret
    /// <see cref="IsOpenBatchPastMaxAge"/>.
    /// </summary>
    public required int MaxBatchAgeMillis { get; init; }

    /// <summary>Max forced-inclusion entries per sealed batch (batcher settings).</summary>
    public required int MaxForcedTransactionsPerBatch { get; init; }

    /// <summary>Max L1 inbox messages per sealed batch (batcher settings).</summary>
    public required int MaxL1MessagesPerBatch { get; init; }

    /// <summary>Wall-clock age of the open batch in milliseconds when open; otherwise null.</summary>
    public required long? OpenBatchAgeMillis { get; init; }

    /// <summary>Open batch is at or past MaxBatchAgeMillis (seal-by-age overdue).</summary>
    public required bool IsOpenBatchPastMaxAge { get; init; }

    /// <summary>Transaction count in the open batch (local batcher).</summary>
    public required int InProgressTxCount { get; init; }

    /// <summary>First L2 block in the open batch when open; otherwise null.</summary>
    public required ulong? OpenBatchFirstBlock { get; init; }

    /// <summary>Last L2 block in the open batch when open; otherwise null.</summary>
    public required ulong? OpenBatchLastBlock { get; init; }

    /// <summary>Block count in the open batch (local batcher).</summary>
    public required int OpenBatchBlockCount { get; init; }

    /// <summary>L1 messages consumed into the open batch (local batcher).</summary>
    public required int OpenBatchL1MessageCount { get; init; }

    /// <summary>L2→L1 messages staged in the open batch (local batcher).</summary>
    public required int OpenBatchL2ToL1MessageCount { get; init; }

    /// <summary>L2→L2 messages staged in the open batch (local batcher).</summary>
    public required int OpenBatchL2ToL2MessageCount { get; init; }

    /// <summary>Forced-inclusion entries staged in the open batch (local batcher).</summary>
    public required int OpenBatchForcedInclusionCount { get; init; }

    /// <summary>Withdrawals staged in the open batch (local batcher).</summary>
    public required int OpenBatchWithdrawalCount { get; init; }

    /// <summary>
    /// Batcher last-acked batch aligns with durable settlement checkpoint (local artifacts).
    /// </summary>
    public required bool IsBatcherCheckpointAligned { get; init; }

    /// <summary>Next L2 block the batcher expects (local batcher; null when unset).</summary>
    public required ulong? NextExpectedBlock { get; init; }

    /// <summary>Last batch number the batcher has acked from settlement (local).</summary>
    public required ulong LastAcknowledgedBatchNumber { get; init; }

    /// <summary>Last L2 block the batcher has acked from settlement (local).</summary>
    public required ulong LastAcknowledgedBlock { get; init; }

    /// <summary>Next batch number the batcher will seal (local).</summary>
    public required ulong NextBatchNumber { get; init; }

    /// <summary>
    /// Durable settlement checkpoint batch number when present; otherwise null (local store).
    /// </summary>
    public required ulong? LatestCheckpointBatchNumber { get; init; }

    /// <summary>
    /// Durable settlement checkpoint last L2 block when present; otherwise null (local store).
    /// </summary>
    public required ulong? LatestCheckpointLastBlock { get; init; }

    /// <summary>
    /// Soft forced-inclusion overdue from local cache only (no L1 scan).
    /// Live poll remains <c>HasOverdueForcedInclusionAsync</c>.
    /// </summary>
    public required bool HasOverdueForcedInclusion { get; init; }

    /// <summary>Pipeline health (passport + batcher/settlement runtime) is clean.</summary>
    public required bool IsPipelineHealthy { get; init; }

    /// <summary>Failed pipeline health checks (empty when healthy).</summary>
    public required IReadOnlyList<string> PipelineHealthFailures { get; init; }

    /// <summary>Metrics HTTP server is listening.</summary>
    public required bool IsMetricsHttpListening { get; init; }

    /// <summary>Bound metrics port when listening; otherwise 0.</summary>
    public required int MetricsBoundPort { get; init; }

    /// <summary>Metrics <c>/readyz</c> predicate is installed.</summary>
    public required bool HasMetricsReadinessCheck { get; init; }

    /// <summary>Metrics <c>/healthprobe</c> JSON body provider is installed.</summary>
    public required bool HasMetricsHealthProbe { get; init; }

    /// <summary>Metrics <c>/operatorstatus</c> JSON body provider is installed.</summary>
    public required bool HasMetricsOperatorStatus { get; init; }

    /// <summary>Metrics HTTP health is clean (or metrics disabled).</summary>
    public required bool IsMetricsHttpHealthy { get; init; }

    /// <summary>Failed metrics HTTP checks (empty when healthy or metrics disabled).</summary>
    public required IReadOnlyList<string> MetricsHttpHealthFailures { get; init; }

    /// <summary>Combined pipeline + metrics HTTP health is clean.</summary>
    public required bool IsLocalHostHealthy { get; init; }

    /// <summary>Failed combined host health checks.</summary>
    public required IReadOnlyList<string> LocalHostHealthFailures { get; init; }

    /// <summary>Local settlement queue is idle (no L1 settle claim).</summary>
    public required bool IsSettlementRuntimeIdle { get; init; }

    /// <summary>Durable recovery is poisoned (no L1 settle claim).</summary>
    public required bool IsSettlementPoisoned { get; init; }

    /// <summary>Durable recovery is retrying (no L1 settle claim).</summary>
    public required bool IsSettlementRetrying { get; init; }

    /// <summary>Local settlement recovery retry count (no L1 claim).</summary>
    public required int SettlementRetryCount { get; init; }

    /// <summary>Local settlement confirmation lag in batches (no L1 claim).</summary>
    public required int SettlementConfirmationLagBatches { get; init; }

    /// <summary>Local pending settlement artifact count (no L1 claim).</summary>
    public required int PendingSettlementCount { get; init; }

    /// <summary>Soft deposit-source ready count (in-memory; no L1 scan).</summary>
    public required int DepositSourceReadyCount { get; init; }

    /// <summary>Soft deposit-source reserved count (drained for seal; no L1 claim).</summary>
    public required int DepositSourceReservedCount { get; init; }

    /// <summary>Soft deposit-source consumed-nonce cache size (no L1 claim).</summary>
    public required int DepositSourceSoftConsumedCount { get; init; }

    /// <summary>
    /// Hard-consumed deposit nonces on the bridge processor (local; distinct from soft source cache).
    /// </summary>
    public required int ConsumedDepositCount { get; init; }

    /// <summary>Local L1 inbox pending count (soft cache; no L1 scan claim).</summary>
    public required int L1InboxPendingCount { get; init; }

    /// <summary>Local L1 inbox soft consumed count (no L1 scan claim).</summary>
    public required int L1InboxConsumedCount { get; init; }

    /// <summary>Message outbox is wired on the host (local composition).</summary>
    public required bool HasMessageOutbox { get; init; }

    /// <summary>L2→L1 message outbox depth when wired; otherwise 0 (local).</summary>
    public required int MessageOutboxL2ToL1Count { get; init; }

    /// <summary>L2→L2 message outbox depth when wired; otherwise 0 (local).</summary>
    public required int MessageOutboxL2ToL2Count { get; init; }

    /// <summary>Soft inbound message nonce cache size (no L1 scan claim).</summary>
    public required int KnownInboundNonceCount { get; init; }

    /// <summary>Soft forced-inclusion nonce cache size (local; no L1 drain claim).</summary>
    public required int KnownForcedInclusionNonceCount { get; init; }

    /// <summary>Staged L2 withdrawal count on the bridge processor (local; no L1 claim).</summary>
    public required int StagedWithdrawalCount { get; init; }
}
