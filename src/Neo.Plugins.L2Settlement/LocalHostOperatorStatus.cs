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
    /// <see cref="IsProductionWired"/> and <see cref="HasSealedBatchSink"/> — matches default
    /// LocalHost <c>/readyz</c>.
    /// </summary>
    public required bool IsOperatorReady { get; init; }

    /// <summary>True when WireProduction installed a SharedBridge deposit source.</summary>
    public required bool HasDepositSource { get; init; }

    /// <summary>True when WireProduction installed a MessageRouter.</summary>
    public required bool HasMessageRouter { get; init; }

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

    /// <summary>Durable pending / retry / poison recovery surface.</summary>
    public required SettlementRecoveryStatus Recovery { get; init; }

    /// <summary>Count of forced-inclusion nonces tracked in durable settlement state.</summary>
    public required int TrackedForcedInclusionNonceCount { get; init; }
}
