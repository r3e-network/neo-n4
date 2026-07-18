using Neo.L2;

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

    /// <summary>Configured proof type of the wired prover.</summary>
    public required ProofType ProofType { get; init; }

    /// <summary>DA mode of the wired DA writer.</summary>
    public required DAMode DaMode { get; init; }

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

    /// <summary>ForcedInclusion scanner deployment height (0 when unset).</summary>
    public required uint ForcedInclusionDeploymentHeight { get; init; }

    /// <summary>SharedBridge scanner deployment height (0 when unset).</summary>
    public required uint SharedBridgeDeploymentHeight { get; init; }

    /// <summary>MessageRouter scanner deployment height (0 when unset).</summary>
    public required uint MessageRouterDeploymentHeight { get; init; }

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
    /// Authenticated genesis / initial state root from the settlement sink (zero for legacy).
    /// </summary>
    public required UInt256 InitialStateRoot { get; init; }
}
