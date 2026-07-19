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
