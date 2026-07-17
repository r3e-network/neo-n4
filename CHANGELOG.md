# Changelog

All notable changes to **Neo Elastic Network** (`neo4`).
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Tested — Neo N3 testnet session8 reverify + SharedBridge deposit n9 — 2026-07-18

- Re-ran `neo-hub-deploy deploy-testnet` (skip-existing): **24/24 deploy reused**,
  **29/29 postdeploy reused**, **42/42 smoke ok** against magic `894710606`.
- Live SharedBridge Deposit **nonce 9** HALT (`0xff78d6fb…9273`, 0.001 GAS,
  `WitnessScope.Global`, Transfer + DepositEnqueued); `getDeposit` confirmed.
- Evidence: `docs/audit/testnet-deployment-20260718-session8-reverify.json`,
  `docs/audit/testnet-evidence-status-2026-07-18-session8.json`.
- WIF only via process env `NEO_N4_TESTNET_WIF` (not committed). Funded gates still open:
  L1 settle, Zk prove-batch, production DA, 2-of-2 bridge retarget, full Neo.CLI stack.

### Added — Batcher inbox wiring flags, seal caps, and metrics concurrency on LocalHost — 2026-07-18

- `L2BatchPlugin` exposes `HasDepositSource` / `HasMessageRouter` / `HasForcedInclusionSource`
  plus `MaxForcedTransactionsPerBatch` / `MaxL1MessagesPerBatch` for offline inbox wiring and
  seal capacity checks.
- Multisig/Optimistic/Zk LocalHost + operator status add `HasBatchDepositSource` /
  `HasBatchMessageRouter` / seal caps, and `MetricsMaxConcurrentConnections`.
- `L2MetricsPlugin.MaxConcurrentConnections` public. Wireproduction notes + init-l2 tips;
  unit/integration coverage. No wire/ABI change.

### Added — Metrics settings + deposit soft-consumed + scanner deploy heights on LocalHost — 2026-07-18

- `L2MetricsPlugin` exposes `IsEnabled` / `ConfiguredPort` / `BindAddress` for offline
  metrics wiring checks (distinct from live `BoundPort`).
- Multisig/Optimistic/Zk LocalHost + operator status add `DepositSourceSoftConsumedCount`,
  `IsMetricsEnabled` / `MetricsConfiguredPort` / `MetricsBindAddress`, and settlement
  scanner deploy heights (`ForcedInclusion` / `SharedBridge` / `MessageRouter`).
- Wireproduction notes + init-l2 tips; unit/integration coverage. No wire/ABI change.
- Live L1 scan/settle and metrics scrape remain operator-owned.

### Added — Settlement enable/finality + deposit source queue depths on LocalHost — 2026-07-18

- `L2SettlementPlugin` exposes `IsEnabled`, `L1FinalityDepth`, and deploy heights;
  Multisig/Optimistic/Zk LocalHost forward `IsSettlementEnabled` / `L1FinalityDepth`.
- `RpcSharedBridgeDepositSource` exposes soft `ReadyCount` / `ReservedCount` /
  `SoftConsumedCount`; LocalHost status includes `DepositSourceReadyCount` /
  `DepositSourceReservedCount` for offline deposit pipeline health (beyond capped peek).
- Wireproduction notes + init-l2 tips; unit/integration coverage. No wire/ABI change.
- Live L1 deposit scan / settle remains a funded RPC gate.

### Added — Gateway enable/retry thresholds + FI known nonces on LocalHost — 2026-07-18

- `L2GatewayPlugin` / `GatewayHostComposition` expose `IsEnabled` and `MaxAutomaticRetries`;
  `GatewayHostOperatorStatus` (+ JSON) includes both for offline gateway settings health.
- `RpcForcedInclusionSource.KnownNonceCount`; Multisig/Optimistic/Zk LocalHost expose
  `KnownForcedInclusionNonceCount` and `HasBatchForcedInclusionSource` on ops status.
- Wireproduction notes + init-l2 tips; unit/integration coverage. No wire/ABI change.
- Live FI drain / Gateway L1 publish remain funded gates.

### Added — L1 inbox + production wiring readiness on LocalHost ops — 2026-07-18

- Multisig/Optimistic/Zk LocalHost expose `L1InboxPendingCount` / `L1InboxConsumedCount`
  and `KnownInboundNonceCount` (via `RpcMessageRouter.KnownInboundNonceCount`).
- Operator status (+ JSON) adds `HasForcedInclusionFinalizer` / `HasSettlementClient` /
  `HasTransactionSender` plus the inbox and inbound-nonce counts for offline deposit
  pipeline and WireProduction completeness checks.
- Wireproduction notes + init-l2 tips; unit/integration coverage. No wire/ABI change.
- Live L1 deposit scan / FI drain / settle remains a funded RPC gate.

### Tested — Neo N3 testnet session7 reverify + SharedBridge deposit n8 — 2026-07-18

- Re-ran `neo-hub-deploy deploy-testnet` (skip-existing): **24/24 deploy reused**,
  **29/29 postdeploy reused**, **42/42 smoke ok** against magic `894710606`.
- Live SharedBridge Deposit **nonce 8** HALT (`0xa30e573b…bebc`, 0.001 GAS,
  `WitnessScope.Global`, Transfer + DepositEnqueued); `getDeposit` confirmed.
- Evidence: `docs/audit/testnet-deployment-20260718-session7-reverify.json`,
  `docs/audit/testnet-evidence-status-2026-07-18-session7.json`.
- WIF only via process env `NEO_N4_TESTNET_WIF` (not committed). Funded gates still open:
  L1 settle, Zk prove-batch, production DA, 2-of-2 bridge retarget, full Neo.CLI stack.

### Added — Gateway publication readiness flags on host ops — 2026-07-18

- `L2GatewayPlugin` / `GatewayHostComposition` expose `HasDurableOutbox` and
  `IsPublicationConfigured` for offline publication wiring checks.
- `GatewayHostOperatorStatus` (+ JSON) includes both flags. No wire/ABI change.
- L1 confirmation of a published epoch remains a funded gate.

### Added — Batcher seal thresholds on LocalHost ops status — 2026-07-18

- `L2BatchPlugin` / Multisig/Optimistic/Zk LocalHost expose `MaxBlocksPerBatch`,
  `MaxTransactionsPerBatch`, and `MaxBatchAgeMillis` from plugin settings.
- Operator status JSON includes the seal thresholds for offline batcher health.
- Wireproduction notes + init-l2 tip; unit/integration coverage. No wire/ABI change.

### Added — Gateway AggregatorPendingCount on host ops status — 2026-07-18

- `GatewayHostComposition.AggregatorPendingCount` mirrors `IGatewayAggregator.PendingCount`
  for offline aggregation queue depth without digging into the plugin.
- `GatewayHostOperatorStatus` (+ JSON document) includes `AggregatorPendingCount`.
- Wireproduction notes + init-l2 tip; unit/integration coverage. No wire/ABI change.
- L1 `PublishAggregateAsync` confirmation remains a funded gate.

### Added — Pending sealed summary, outbox roots, IsBatcherEnabled — 2026-07-18

- `L2BatchPlugin` / Multisig/Optimistic/Zk LocalHost expose `PendingSealedBatchNumber`,
  `PendingSealedBatchLastBlock`, and `IsBatcherEnabled` for offline batcher health.
- LocalHost surfaces `MessageOutboxL2ToL1Root` / `MessageOutboxL2ToL2Root`; operator status
  JSON includes pending sealed summary, batcher enabled flag, and outbox roots.
- Wireproduction notes + init-l2 tips; unit/integration coverage. No wire/ABI change.
- Live L1 message publication / settlement remains a funded RPC gate.

### Tested — Neo N3 testnet session6 reverify + SharedBridge deposit n7 — 2026-07-18

- Re-ran `neo-hub-deploy deploy-testnet` (skip-existing): **24/24 deploy reused**,
  **29/29 postdeploy reused**, **42/42 smoke ok** against magic `894710606`.
- CLI `--fraud-replay-domain` must be on-chain **raw wire** bytes
  (`0xf1e2d3c4…1100`); report `ToString` may display the reversed form.
- Live SharedBridge Deposit **nonce 7** HALT (`0xde9e3342…e4b8`, 0.001 GAS,
  `WitnessScope.Global`, Transfer + DepositEnqueued); `getDeposit` confirmed.
- Evidence: `docs/audit/testnet-deployment-20260718-session6-reverify.json`,
  `docs/audit/testnet-evidence-status-2026-07-18-session6.json`.
- WIF only via process env `NEO_N4_TESTNET_WIF` (not committed). Funded gates still open:
  L1 settle, Zk prove-batch, production DA, 2-of-2 bridge retarget, full Neo.CLI stack.

### Added — Batcher ack progress + LocalHost OnBatchSealed — 2026-07-17

- `BatchSealer` / `L2BatchPlugin` / Multisig/Optimistic/Zk LocalHost expose
  `LastAcknowledgedBatchNumber`, `LastAcknowledgedBlock`, and `NextBatchNumber`.
- LocalHost forwards `OnBatchSealed` for offline sealed-batch subscribers.
- Operator status JSON includes ack/next batch progress; `StopMetricsHttp` restart covered
  in Multisig unit tests. No wire/ABI change.

### Added — Metrics Stop, open-batch message counts, PullAggregate — 2026-07-17

- `L2MetricsPlugin.Stop` + LocalHost `StopMetricsHttp` for offline metrics HTTP lifecycle.
- Open-batch `OpenBatchL1MessageCount` / `OpenBatchL2ToL1MessageCount` on sealer/plugin/LocalHost.
- `DepositProcessor.ConsumedCount` + LocalHost `ConsumedDepositCount` (soft cache size).
- `GatewayHostComposition.PullAggregate` for offline aggregate inspection without L1 publish.
- Operator status JSON + wireproduction/init-l2 tips; no wire/ABI change.

### Added — Open-batch block range + MessageRouter inbound helpers — 2026-07-17

- `BatchSealer` / `L2BatchPlugin` / Multisig/Optimistic/Zk LocalHost expose
  `OpenBatchFirstBlock` / `OpenBatchLastBlock` / `OpenBatchBlockCount` for offline batcher
  progress without Neo.CLI.
- LocalHost `RegisterInboundMessageNonce` / `InvalidateInboundMessageCache` mirror FI
  recovery helpers for MessageRouter inbound seeding.
- Operator status JSON includes open-batch block range; wireproduction + init-l2 tips.
- No wire/ABI change. Live L1 message fanout remains a funded RPC gate.

### Added — LocalHost open-batch progress + TryRetryPendingSealedBatch — 2026-07-17

- `L2BatchPlugin` / Multisig/Optimistic/Zk LocalHost expose `HasOpenBatch`,
  `InProgressTxCount`, and `TryRetryPendingSealedBatch` (retry durable persist without a
  new L2 block after a transient sink/executor failure).
- Operator status JSON includes open-batch progress fields.
- Wireproduction notes, init-l2 tip, unit/integration coverage; no wire/ABI change.
- Successful retry still needs a working executor/sink (funded or local stub).

### Added — LocalHost batcher pending status on operator health — 2026-07-17

- `L2BatchPlugin` exposes `HasPendingSealedBatch` / `PendingSealedBatch`; Multisig/Optimistic/Zk
  LocalHost wrap them for offline ops without Neo.CLI.
- `LocalHostOperatorStatus` (+ JSON document) include `NextExpectedBlock` and
  `HasPendingSealedBatch` for host health dumps.
- Wireproduction notes, init-l2 tip, unit/integration coverage; no wire/ABI change.

### Added — LocalHost offline ProcessCommittedBlock batcher hand-off — 2026-07-17

- `L2BatchPlugin.ProcessCommittedBlock` is public (host/offline composition without Neo.CLI
  `Blockchain.Committed`) plus `NextExpectedBlock`.
- Multisig/Optimistic/Zk LocalHost wrap both helpers; add `GetRpcStateRootAtBatch`.
- Wireproduction notes, init-l2 batch tip, unit/integration coverage; no wire/ABI change.
- Sealing still requires a real executor when MaxBlocks/tx thresholds trip (funded/host binary).

### Added — LocalHost deposit scan helpers + Gateway status metrics — 2026-07-17

- Multisig/Optimistic/Zk LocalHost expose deposit inbox ops without Neo.CLI:
  `ScanSharedBridgeDepositsAsync`, `ScanAndProcessReadyDepositsAsync`, and
  `HasOverdueForcedInclusionAsync` (live L1 discovery remains a funded RPC gate).
- `GatewayHostOperatorStatus` / document add `HasMetrics` + `MetricsEntryCount`.
- Wireproduction notes, init-l2 tip, unit/integration coverage; no wire/ABI change.

### Added — Gateway Prometheus export + LocalHost ProcessReadyDeposits — 2026-07-17

- `GatewayHostComposition` retains optional `Metrics` and exposes
  `CaptureMetricsSnapshot` / `ExportPrometheusMetrics` / `WritePrometheusMetricsAsync`
  when the sink implements `IMetricsSource` (parity with LocalHost offline scrape files).
- Multisig/Optimistic/Zk LocalHost `ProcessReadyDeposits` peeks ready SharedBridge deposits
  and mints unconsumed ones (deposit-source Drain/Confirm remains the batcher path).
- Wireproduction notes + init-l2 tip + unit/integration coverage; no wire/ABI change.
- L1 gateway publish confirmation and live deposit scan remain funded gates.

### Added — Gateway WriteOperatorStatusAsync + LocalHost Prometheus file dump — 2026-07-17

- `GatewayHostComposition.WriteOperatorStatusAsync(path)` writes camelCase
  `GatewayHostOperatorStatusDocument` JSON (outbox/publication snapshot) without Neo.CLI.
- Multisig/Optimistic/Zk LocalHost expose `WritePrometheusMetricsAsync(path)` for offline
  scrape files (wraps existing `ExportPrometheusMetrics`).
- Wireproduction notes, init-l2 tip, unit + Multisig/Optimistic integration coverage.
- No wire/ABI change. L1 gateway confirmation remains a funded gate.

### Evidence — Neo N3 testnet session5 reverify + Deposit nonce 6 — 2026-07-17

- Re-ran `neo-hub-deploy deploy-testnet` with skip-existing: 24/24 deploy reused,
  29/29 postdeploy reused, 42/42 smoke ok against `https://n3seed1.ngd.network:20332/`
  (network magic `894710606`, chain `20260716`).
- Live SharedBridge Deposit nonce 6: tx `0x55ccf7b1…61cd` HALT with Transfer +
  DepositEnqueued; `getDeposit` confirmed on fixed bridge `0xf64548c2…1bae`.
- Confirmed chain isActive, ForcedInclusion production-ready + entry nonce 1,
  TokenRegistry GAS+NEO active for chain 20260716; create-chain zk-rollup +
  `init-l2 --from-deploy-report` session5 reverify ok.
- Evidence: `docs/audit/testnet-deployment-20260717-session5-reverify.json`,
  `docs/audit/testnet-evidence-status-2026-07-17-session5.json`.
- WIF via env only (`NEO_N4_TESTNET_WIF`); not written to repo.

### Added — LocalHost WriteOperatorStatusAsync + withdrawal/outbox E2E — 2026-07-17

- Multisig/Optimistic/Zk LocalHost expose `WriteOperatorStatusAsync(path)` writing
  `LocalHostOperatorStatusDocument` JSON (primitive fields + recovery summary) for
  host health dumps without Neo.CLI.
- Unit coverage stages a withdrawal, seals the tree, enqueues an L2→L1 outbox message,
  and asserts the JSON status file; Optimistic/Zk + Multisig/Gateway integration dump status.
- Wireproduction notes + init-l2 status tip; no wire/ABI change.

### Added — LocalHost deposit/withdrawal processors + ProveAsync — 2026-07-17

- Multisig/Optimistic/Zk LocalHost expose bridge mint/withdraw staging without Neo.CLI:
  `DepositProcessor` / `WithdrawalProcessor`, `ProcessDeposit` / `HasConsumedDeposit`,
  `StageWithdrawal` / `StagedWithdrawalCount` / `SealWithdrawalBatch`.
- `BatchProver` + `ProveAsync` for offline Multisig attestation (Zk prove may still need
  funded executor/daemon).
- `LocalHostOperatorStatus.StagedWithdrawalCount`. `init-l2` host bridge tip.
- Wireproduction notes + unit/integration coverage; no wire/ABI change.

### Added — LocalHost metrics export + bridge asset registry — 2026-07-17

- Multisig/Optimistic/Zk LocalHost expose offline metrics export:
  `CaptureMetricsSnapshot`, `ExportPrometheusMetrics` (no HTTP required).
- Bridge asset registry helpers: `BridgeAssetRegistry`, `RegisterBridgeAsset`,
  `SnapshotBridgeAssets`, `BridgeAssetCount`.
- `LocalHostOperatorStatus` adds `BridgeAssetCount`, `MetricsEntryCount`,
  `MessageOutboxL2ToL1Count`, `MessageOutboxL2ToL2Count`.
- `init-l2` prints host ops tip. Wireproduction notes + unit/integration coverage;
  no wire/ABI change.

### Added — LocalHost FI + DA publish helpers — 2026-07-17

- Multisig/Optimistic/Zk LocalHost expose forced-inclusion recovery helpers:
  `RegisterForcedInclusionNonce`, `InvalidateForcedInclusionCache`.
- All LocalHosts expose `PublishDaAsync` / `IsDaAvailableAsync`; Multisig/Optimistic
  also expose `CreateLocalDaReader` for local persistent DA (public DA credentials remain funded).
- `init-l2` prints host da/fi tip. Wireproduction notes + unit/integration coverage;
  no wire/ABI change.

### Added — LocalHost RPC proofs/assets + MessageRouter helpers — 2026-07-17

- Multisig/Optimistic/Zk LocalHost expose additional host RPC store ops without Neo.CLI:
  `RegisterRpcAsset` / `GetRpcCanonicalAsset` / `GetRpcBridgedAsset`,
  `RecordRpcWithdrawalProof` / `RecordRpcMessageProof` /
  `GetRpcWithdrawalProof` / `GetRpcMessageProof`.
- MessageRouter local surface: `MessageOutbox`, `EnqueueOutboundMessagesAsync`,
  `RecordMessageRouterFinalizedProof`, `GetMessageRouterProofAsync`.
- `LocalHostOperatorStatus` adds `GatewayEnabled`, `Sequencer`, `Exit` from the RPC store.
- `init-l2` prints host rpc tip. Wireproduction notes + unit coverage; no wire/ABI change.

### Added — LocalHost RPC store helpers + Gateway GetOperatorStatus — 2026-07-17

- Multisig/Optimistic/Zk LocalHost expose host RPC store ops without Neo.CLI:
  `GetLatestRpcStateRoot`, `AddRpcBatch`, `FinalizeRpcBatch`, `RecordRpcDeposit`,
  `GetRpcL1DepositStatus`, `GetRpcBatch`, `GetRpcBatchStatus`.
- `LocalHostOperatorStatus` adds `SecurityLevel`, `HasDepositSource`,
  `HasMessageRouter`, `LatestRpcStateRoot`.
- `GatewayHostComposition.GetOperatorStatus` returns `GatewayHostOperatorStatus`
  (outbox/publication/backend snapshot; L1 confirm remains funded).
- Wireproduction notes + unit/integration coverage; no wire/ABI change.

### Added — LocalHost GetOperatorStatusAsync + Gateway Aggregator — 2026-07-17

- Multisig/Optimistic/Zk LocalHost expose `GetOperatorStatusAsync` returning
  `LocalHostOperatorStatus` (chain/proof/DA, readiness, metrics bind, pending
  settlement, deposit peek count, recovery, tracked FI nonces) without Neo.CLI.
- `GatewayHostComposition.Aggregator` passthrough for host inspection.
- `neo-stack init-l2` prints host ready tip (`IsOperatorReady` / `GetOperatorStatusAsync`).
- Wireproduction notes + unit/integration coverage; no wire/ABI change.

### Added — LocalHost readiness + deposit peek surface — 2026-07-17

- Multisig/Optimistic/Zk LocalHost expose operator readiness without Neo.CLI:
  `ChainId`, `ProofType`, `DaMode`, `HasSealedBatchSink`, `IsOperatorReady`
  (wired + sealed sink), and `PeekSharedBridgeDeposits` (non-mutating inbox view).
- Wireproduction notes document the readiness surface. Unit + Multisig/Gateway
  integration coverage on empty deposit queue.

### Added — LocalHost settlement recovery/checkpoint helpers — 2026-07-17

- Multisig/Optimistic/Zk LocalHost expose durable settlement ops without Neo.CLI:
  `GetRecoveryStatusAsync`, `RecoverPoisonedBatchAsync`,
  `GetTrackedForcedInclusionNoncesAsync`, `GetLatestCheckpointAsync`,
  `GetInitialStateRootAsync`.
- Wireproduction notes document the recovery surface. Unit + Multisig/Gateway
  integration coverage on empty durable store (no funded L1).

### Added — LocalHost production surfaces + GatewayHost ops helpers — 2026-07-17

- Multisig/Optimistic/Zk LocalHost expose production WireProduction surfaces without
  digging into Settlement: `DepositSource`, `MessageRouter`, `ForcedInclusionFinalizer`,
  `SettlementClient`, `TransactionSender`, plus `MetricsBoundPort` / `IsMetricsHttpListening`.
- `GatewayHostComposition` exposes outbox/publication ops:
  `HasPendingPublication`, `PendingPublicationEpoch`, `OutboxStatus`, `ReceiveBatch`,
  `PublishAggregateAsync`, `RecoverPoisonedPublication` (L1 confirmation remains funded).
- Wireproduction notes document both surfaces. Unit coverage on Multisig/Optimistic/Zk
  LocalHost and Gateway OpenMerkle.

### Added — ProductionForcedInclusionFinalizer + init-l2 host open tip — 2026-07-17

- `L2SettlementPlugin.ProductionForcedInclusionFinalizer` after WireProduction.
- `neo-stack init-l2` prints Multisig/Optimistic/Zk LocalHostComposition.Open hints
  (and wireproduction notes when deploy-report was used).
- Multisig+Gateway integration scrapes `/readyz` after deferred StartMetricsHttp.

### Evidence — Neo N3 testnet reverify + Deposit nonce 5 — 2026-07-17

- Re-verified the full NeoHub bundle on N3 testnet with operator WIF (env-only):
  24/24 deploy reuse, 29/29 postdeploy reuse, 42/42 smoke HALT.
- Fixed SharedBridge `0xf64548c2…1bae` live Deposit nonce 5 HALT
  (`0xf9539490…2bd9bb`, 0.001 GAS, `WitnessScope.Global`, Transfer + DepositEnqueued);
  getDeposit confirms nonces 1–5 present.
- Confirmed chain `20260716` isActive (config bridge still legacy until 2-of-2 retarget),
  ForcedInclusion production-ready + entry nonce 1.
- Local operator path: `create-chain` + `init-l2 --from-deploy-report` session4 reverify
  materializes SharedBridge `0xf64548c2…1bae`.
- Evidence: `docs/audit/testnet-deployment-20260717-session4-reverify.json`,
  `docs/audit/testnet-evidence-status-2026-07-17-session4.json`.
- WIF never written to the repo.

### Added — LocalHost Persist/Enqueue helpers + Optimistic/Zk metrics RPC parity — 2026-07-17

- Multisig/Optimistic/Zk LocalHost expose `PersistAsync` / `EnqueueAsync` passthroughs to
  settlement WireProduction.
- Optimistic and Zk unit + integration paths exercise deferred `StartMetricsHttp` and
  `CreateRpcPlugin` (parity with Multisig).

### Added — LocalHost StartMetricsHttp, CreateRpcPlugin, /metrics E2E — 2026-07-17

- Multisig/Optimistic/Zk LocalHost expose deferred `StartMetricsHttp`,
  `CreateRpcPlugin()` (metrics pre-wired for Neo.CLI registration), and
  `IsProductionWired`.
- Multisig unit test scrapes `/metrics` Prometheus body; integration Multisig+Gateway
  uses deferred metrics start + CreateRpcPlugin without funded L1.

### Added — ProductionTransactionSender + LocalHost settle helpers + /readyz E2E — 2026-07-17

- `L2SettlementPlugin.ProductionTransactionSender` after WireProduction.
- Multisig/Optimistic/Zk LocalHost expose `ReconcileAsync` / `SubmitNextAsync` /
  `GetPendingCountAsync` passthroughs for operator hosts without Neo.CLI.
- Multisig unit test starts metrics HTTP and asserts `/readyz` + `/healthz` 200 with
  default sealed-sink readiness, plus empty pending reconcile/submit helpers.

### Added — IsProductionWired, metrics readiness, Optimistic+Gateway E2E — 2026-07-17

- `L2SettlementPlugin.IsProductionWired` after WireProduction.
- LocalHost `Open` optional `metricsReadinessCheck`; with `startMetricsHttp` defaults to
  `batch.HasSealedBatchSink` for `/readyz`.
- Integration: Optimistic LocalHost + Gateway OpenMerkle (shared metrics); Multisig E2E
  asserts sealed-batch sink + production wired + FI on the batcher.

### Added — Batch sink/FI visibility + LocalHost startMetricsHttp — 2026-07-17

- `L2BatchPlugin.HasSealedBatchSink` and `ForcedInclusionSource` expose WireProduction
  sink/inbox wiring for host composition checks.
- Multisig/Optimistic/Zk LocalHost `Open` accepts optional `startMetricsHttp` +
  `metricsPortOverride` (ephemeral port `0` for tests) to start the metrics HTTP server
  without Neo.CLI.
- Unit coverage for sealed-batch sink + FI on the batcher and ephemeral metrics bind.

### Added — GatewayHostComposition metrics + Zk+Gateway OpenSp1 E2E — 2026-07-17

- `GatewayHostComposition.OpenMerkle` / `OpenMultisig` / `OpenSp1` accept optional
  `IL2Metrics` and call `L2GatewayPlugin.WithMetrics` (pair with LocalHost metrics).
- Integration: Multisig LocalHost shares metrics into Gateway one-shots; Zk LocalHost
  opens with `GatewayHostComposition.OpenSp1` on the same chain directory (no funded daemon).

### Added — LocalHost DA metrics wrap + batch inbox assertions — 2026-07-17

- Multisig/Optimistic LocalHost compositions publish local DA through
  `MetricsEmittingDAWriter` so `l2.da.*` counters match L2DAPlugin.WithMetrics.
- Zk LocalHost wraps host `IProductionDAWriter` with
  `MetricsEmittingProductionDAWriter` (keeps production marker for RequireProductionDA).
- Unit tests assert WireProduction installs deposit + MessageRouter on the batcher.

### Added — Public WireProduction deposit/router accessors + Gateway Multisig/Sp1 E2E — 2026-07-17

- `L2SettlementPlugin` exposes production-owned surfaces after WireProduction:
  `ProductionDepositSource`, `ProductionMessageRouter`,
  `ProductionForcedInclusionSource`, `ProductionSettlementClient` (no InternalsVisibleTo).
- LocalHost compositions bind bridge deposits via the public deposit accessor.
- Integration: Multisig host + `GatewayHostComposition.OpenMultisig` then `OpenSp1`
  from the testnet deploy report (outbox disposed between backends).

### Added — LocalHost compositions open durable L2 RPC proof store — 2026-07-17

- `MultisigLocalHostComposition` / `OptimisticLocalHostComposition` /
  `ZkLocalHostComposition` open `InMemoryL2RpcStore.OpenFromChainDirectory`
  (`data/rpc/proofs`) alongside WireProduction.
- MessageRouter finalized-proof ownership stays unset so the RPC store is the single
  owner of that RocksDB path (register with `NeoSystem.AddService` before L2RpcPlugin).
- Unit + integration host-composition coverage; wireproduction notes updated.

### Added — Host composition E2E: Multisig+Gateway / Optimistic / Zk one-shots — 2026-07-17

- `UT_E2E_HostComposition_FromDeployReport` now opens the public one-shot host roots from the
  testnet deploy report without funded L1 traffic:
  `MultisigLocalHostComposition` + `GatewayHostComposition.OpenMerkle` together,
  `OptimisticLocalHostComposition`, and `ZkLocalHostComposition` (stub production DA + pinned
  fake executor binary). Factory inventory coverage retained.

### Added — ZkLocalHostComposition chain-directory root — 2026-07-17

- `ZkLocalHostComposition.Open` binds bootstrap state, `Sp1SettlementExecutionStack`,
  Zk-wired prover (same `Sp1BatchProofProver` as the stack), `WireProductionFromLayout` with
  the SP1 profile, SharedBridge deposit source on the bridge plugin, and metrics.
- Host-supplied: reviewed `neo-zkvm-executor` path/SHA-256, verification key,
  `IProductionDAWriter`, and L1 signer. `prove-batch` daemon + real DA credentials remain funded.
- Wireproduction notes + unit coverage (deploy-report materialization, Multisig fail-closed,
  missing bootstrap state fail-closed).

### Evidence — Neo N3 testnet reverify + Deposit nonce 4 — 2026-07-17

- Re-verified the full NeoHub bundle on N3 testnet with operator WIF (env-only):
  24/24 deploy reuse, 29/29 postdeploy reuse, 42/42 smoke HALT.
- Fixed SharedBridge `0xf64548c2…1bae` live Deposit nonce 4 HALT
  (`0xd33952c5…2b1294`, 0.001 GAS, `WitnessScope.Global`, Transfer + DepositEnqueued);
  getDeposit confirms nonces 1–4 present.
- Confirmed chain `20260716` isActive (config bridge still legacy until 2-of-2 retarget),
  TokenRegistry GAS+NEO `isActive=true`, ForcedInclusion production-ready + entry nonce 1.
- Evidence: `docs/audit/testnet-deployment-20260717-session3-reverify.json`,
  `docs/audit/testnet-evidence-status-2026-07-17-session3.json`.
- WIF never written to the repo.

### Added — Gateway OpenMultisig + Multisig/Optimistic host bridge/metrics — 2026-07-17

- `GatewayHostComposition.OpenMultisig` binds Multisig durable aggregator/outbox + publisher +
  publication profile (terminal prover must use backend 0xC0).
- `MultisigLocalHostComposition` / `OptimisticLocalHostComposition` also open
  `L2BridgePlugin` (shared deposit source) and `L2MetricsPlugin` (wired onto batch/settlement/bridge).
- Wireproduction notes + unit coverage.

### Added — OptimisticLocalHostComposition + GatewayHostComposition — 2026-07-17

- `OptimisticLocalHostComposition.Open` mirrors Multisig: Optimistic settlement + local DA +
  `CreateOptimisticWiredFromChainDirectory` + `WireProductionFromLayout` (bond refs host-supplied).
- `GatewayHostComposition.OpenMerkle` / `OpenSp1` bind durable Gateway +
  `ProofBoundRpcGlobalRootPublisher` + `ConfigureGlobalRootPublicationFromChainDirectory`
  in one fail-closed call (lives in Gateway.Rpc to avoid plugin cycles).
- Wireproduction notes + unit coverage. Funded L1 confirmation and gateway daemon remain
  operator gates.

### Added — MultisigLocalHostComposition chain-directory root — 2026-07-17

- `MultisigLocalHostComposition.Open(chainDir, executor, signers, signer)` opens Multisig
  batch/settlement plugins, durable layout, local DA, Multisig-wired prover, and
  `WireProductionFromLayout` in one fail-closed call (ProofType.Multisig required).
- Wireproduction notes document the helper. Unit coverage against the public testnet
  deploy report with mocked L1 RPC. Executor/signer and funded L1 remain host-supplied.

### Added — L1DeployedEndpoints + Gateway publication from chain directory — 2026-07-17

- `L1DeployedEndpoints.FromChainDirectory` resolves L1 RPC + SettlementManager + MessageRouter
  from settlement plugin config and/or `l1.deployed.json` (shared host composition helper).
- `ProofBoundRpcGlobalRootPublisher.OpenFromChainDirectory` uses that helper (no behavior change).
- `L2GatewayPlugin.ConfigureGlobalRootPublicationFromChainDirectory` loads MessageRouter from
  the same endpoints so hosts need not re-type the hash after init-l2.
- Wireproduction notes + unit coverage. Replay domain / VK / funded L1 remain host-supplied.

### Added — Optimistic prover wired factory + host-composition integration test — 2026-07-17

- `L2ProverPlugin.CreateOptimisticWiredFromChainDirectory` loads Optimistic ProofType and
  wires `OptimisticProver` (sequencer key + L1 bond refs) for `WireProductionFromLayout`.
- Integration smoke `UT_E2E_HostComposition_FromDeployReport` materializes Multisig layout from
  the public testnet deploy report and opens batch/settlement/bridge/metrics/DA/RPC/gateway/
  Multisig prover/SP1 queues/publisher/L1 inbox factories without funded L1 traffic.
- Wireproduction notes document the Optimistic factory. Bond posting remains funded.

### Added — SP1 batch prover OpenFromChainDirectory + Zk wired factory — 2026-07-17

- `Sp1BatchProofProver.OpenFromChainDirectory` opens the canonical `prover/inbox` file queue
  (same path as `Sp1SettlementExecutionStack` / init-l2).
- `L2ProverPlugin.CreateZkWiredFromChainDirectory` loads Zk ProofType and wires that SP1
  client for `WireProductionFromLayout` hosts.
- `NeoHubDeployReport.RelativeProverInboxDir` is the single layout constant; settlement stack
  aliases it. `EnsureSettlementStoreDirectories` creates the inbox. Terminal prove-batch
  daemon remains a funded operator process.

### Added — SP1 Gateway durable + Multisig prover wired factories — 2026-07-17

- `L2GatewayPlugin.CreateSp1DurableFromChainDirectory` binds
  `BinaryTreeAggregator` + `Sp1RecursiveRoundProver` + durable outbox.
- `Sp1GatewayProofProver.OpenFromChainDirectory` opens the canonical
  `prover/gateway-inbox` file queue (VK remains host-supplied; daemon is funded).
- `L2ProverPlugin.CreateMultisigWiredFromChainDirectory` loads Multisig ProofType and
  wires `AttestationProver` over an operator `ISignerSet` for `WireProductionFromLayout`.
- `EnsureSettlementStoreDirectories` also creates `prover/gateway-inbox`; wireproduction
  notes document the new helpers. Terminal Groth16 still needs the operator daemon.

### Added — Gateway Merkle/Multisig durable factories + ProofBound publisher from chain dir — 2026-07-17

- `L2GatewayPlugin.CreateMerkleDurableFromChainDirectory` binds
  `BinaryTreeAggregator` + `MerklePathRoundProver` + durable outbox (no HSM/toolchain).
- `CreateMultisigDurableFromChainDirectory` binds Multisig round prover over an operator
  `ISignerSet` (HSM/KMS in production; `InMemorySignerSet` for tests/devnet only).
- `ProofBoundRpcGlobalRootPublisher.OpenFromChainDirectory` loads L1 RPC + SettlementManager +
  MessageRouter from settlement plugin config and/or `l1.deployed.json`.
- `OpenFromChainDirectory(chainDir, INeoTransactionSigner)` and `CreateSignAndSend` build the
  canonical `SettlementManager.publishGatewayGlobalRoot` invocation via `RpcTransactionSender`.
- Wireproduction notes document the new host factories. Terminal SP1 proof prover and funded L1
  publication remain operator/funded gates.

### Evidence — Neo N3 testnet full reverify + Deposit nonce 3 — 2026-07-17

- Re-verified the full NeoHub bundle on N3 testnet with operator WIF (env-only):
  24/24 deploy reuse, 29/29 postdeploy reuse, 42/42 smoke HALT.
- Fixed SharedBridge `0xf64548c2…1bae` live Deposit nonce 3 HALT
  (`0xa06e7952…e6d821`, 0.001 GAS, `WitnessScope.Global`, Transfer + DepositEnqueued).
- Confirmed chain `20260716` isActive, TokenRegistry GAS+NEO mappings, ForcedInclusion
  production-ready + entry nonce 1 present; local `neo-stack create-chain` +
  `init-l2 --from-deploy-report` materializes fixed SharedBridge into `l1.deployed.json`.
- CLI note: `--fraud-replay-domain` must be on-chain raw wire bytes from
  `RestrictedExecutionFraudVerifier.getReplayDomain` (not the reversed UInt256 display form).
- Evidence: `docs/audit/testnet-deployment-20260717-session-reverify.json`,
  `docs/audit/testnet-evidence-status-2026-07-17-session2.json`.
- WIF never written to the repo.

### Added — L1InboxFromChainDirectory host composition root — 2026-07-17

- `L1InboxFromChainDirectory.Open(chainDir)` opens SharedBridge deposit + ForcedInclusion +
  MessageRouter with one shared L1 RPC client and static sequencer committee hash from
  `chain.config.json` validators.
- `WireBatch` / `L2SettlementPlugin.WireL1InboxFromChainDirectory` install forced-inclusion
  and optional deposits/MessageRouter on `L2BatchPlugin` before the first sealed block.
- Wireproduction notes document `l1InboxFromChainDirectory`; unit coverage against live
  deploy-report materialization.

### Added — ForcedInclusion + MessageRouter sources from chain directory — 2026-07-17

- `RpcForcedInclusionSource.OpenFromChainDirectory` and
  `RpcMessageRouter.OpenFromChainDirectory` open durable event stores under
  `data/settlement/forced-inclusion-events` and `data/settlement/message-router-events`
  (message router optionally owns `data/rpc/proofs` for finalized proofs).
- Event scanners accept `ownsStore` so chain-directory factories can dispose RocksDB
  with the source/router.
- `L2SettlementPlugin.CreateForcedInclusionSourceFromChainDirectory` and
  `CreateMessageRouterFromChainDirectory` load settlement config and return owned instances
  for `WireL1MessageInbox` host composition.
- Wireproduction notes + unit coverage for store open / fail-closed paths.

### Added — SharedBridge deposit source from chain directory — 2026-07-17

- `RpcSharedBridgeDepositSource.OpenFromChainDirectory` opens the durable event store under
  `data/settlement/shared-bridge-deposits` for host composition outside full WireProduction.
- `L2SettlementPlugin.CreateDepositSourceFromChainDirectory` loads settlement config
  (RPC, SharedBridge hash, deploy height, finality) and returns an owned deposit source
  for `L2BridgePlugin.WithDepositSource` / `L2BatchPlugin.WireL1MessageInbox`.
- Wireproduction notes document both helpers; unit coverage for store open + fail-closed paths.

### Fixed — Gateway host composition order (aggregator before outbox) — 2026-07-17

- `L2GatewayPlugin.CreateFromChainDirectory` now loads settings only; attaching the
  durable outbox first made `UseAggregator` fail closed (outbox rehydrate binds the
  active aggregator).
- Added `CreateDurableFromChainDirectory(chainDir, aggregator)` and
  `AttachOutboxFromChainDirectory` for the production order:
  settings → UseAggregator → outbox → ConfigureGlobalRootPublication.
- Wireproduction notes document the durable factory; unit coverage for settings-only
  + durable path.

### Added — L2BridgePlugin CreateFromChainDirectory + deploy-report config — 2026-07-17

- `L2BridgeSettings.FromChainDirectory` / `L2BridgePlugin.CreateFromChainDirectory` preload
  L2 `ChainId` and rebind deposit/withdrawal processors for host composition outside Neo.CLI.
- Deploy-report materializes `Plugins/Neo.Plugins.L2Bridge/config.json` and documents
  `bridgePluginFactory` in wireproduction notes.
- Unit coverage: chain id load, missing config fail-closed, zero chain id fail-closed.

### Added — L2GatewayPlugin CreateFromChainDirectory + durable outbox layout — 2026-07-17

- `L2GatewaySettings.FromChainDirectory` / settings-only `CreateFromChainDirectory`
  (see follow-up fix: outbox must attach after aggregator).
- `PersistentGatewayOutbox.OpenFromChainDirectory`; deploy-report materializes
  `Neo.Plugins.L2Gateway` config (`gatewayEnabled` from chain.config).
- `EnsureSettlementStoreDirectories` creates seven durable store dirs (adds gateway outbox).

### Fixed — SharedBridge.Deposit live on Neo N3 testnet — 2026-07-17

- Root cause: stale `bin/sc` NEF still pulled NEP-17 from `CallingScriptHash` (no
  `System.Runtime.GetScriptContainer`); source already used `Transaction.Sender`.
- Recompiled with nccs and deployed new SharedBridge
  `0xf64548c2c947f5c2150b467700c0f46f90dc1bae` (tx `0xb9c7e1c1…ec893a`).
- Live Deposit HALT: tx `0x9b72709e…4675ae`, nonce 1, 0.001 GAS, `WitnessScope.Global`.
- Chain `20260716` registry still points at legacy bridge `0xf2f5114b…b241` until
  governance retarget or new chain registration (funded/governance gate).
- Evidence: `docs/audit/testnet-deployment-20260717-sharedbridge-fix.json`,
  `docs/audit/testnet-evidence-status-2026-07-17.json`,
  `docs/audit/testnet-deployment-20260717-reverify.json` (24/24 reuse + smoke).

### Added — InMemoryL2RpcStore.OpenFromChainDirectory — 2026-07-17

- Host composition opens durable L2 RPC proof storage at `data/rpc/proofs` and loads
  chain security labels from `chain.config.json` (chainId, securityLevel, daMode,
  sequencer/exit models, gateway flag).
- `EnsureSettlementStoreDirectories` creates the RPC proofs dir; wireproduction notes
  document `rpcStoreOpenHelper`.
- Unit coverage: reopen withdrawal proofs + missing config fail-closed.

### Added — Metrics/DA host factories + deploy-report materialization — 2026-07-16

- `L2MetricsSettings.FromChainDirectory` / `L2MetricsPlugin.CreateFromChainDirectory`
  preload scrape bind settings; deploy-report writes `Neo.Plugins.L2Metrics` config.
- `L2DAPlugin.CreateLocalFromChainDirectory` installs `PersistentDAWriter` under
  `data/settlement/da` for Multisig/Optimistic hosts.
- Deploy-report materializes `Neo.Plugins.L2DA` config (`DAMode` from chain.config
  `daMode`, `DataDirectory` when Local) and `ResolveDAModeByte`.
- Wireproduction notes document `metricsPluginFactory` / `localDaPluginFactory`.

### Added — L2ProverPlugin.CreateFromChainDirectory + deploy-report config — 2026-07-16

- Materialize `Plugins/Neo.Plugins.L2Prover/config.json` (ProofType from chain.config)
  in `NeoHubDeployReport.WriteOperatorArtifacts` alongside settlement/batch configs.
- `L2ProverPlugin.CreateFromChainDirectory` / `ReadProofTypeFromChainDirectory` preload
  Kind for host composition; call `Wire(...)` with stage-specific dependencies afterward.
- Wireproduction notes document `proverPluginFactory`.

### Added — Sp1SettlementExecutionStack.OpenStateFromChainDirectory — 2026-07-16

- Opens RocksDB at `data/state` after `bootstrap-genesis` (caller-owned dispose).
- Pairs with `CreateFromChainDirectory` so Zk hosts do not hard-code state paths.
- `NeoHubDeployReport` documents `RelativeStateDir` + `stateOpenHelper` in wireproduction notes.
- `LegacyFromChainDirectory` Zk error text points at CreateFromChainDirectory + OpenState.

### Added — Sp1SettlementExecutionStack.CreateFromChainDirectory — 2026-07-16

- Loads ProofType=Zk settlement config + genesis-manifest, ensures
  `prover/executor-scratch` and `prover/inbox` under the chain layout, and binds the
  SP1 executor/prover/profile. Reviewed `neo-zkvm-executor` path/SHA-256 and VK remain
  host-supplied (funded release pin). Fails closed for Multisig/Optimistic configs.

### Added — LocalKeyTransactionSigner.FromEnvironmentVariableWithGlobalScope — 2026-07-16

- Convenience factory for nested NEP-17 pulls (Deposit / ForcedInclusion fees) matching
  `neo-stack --witness-scope Global`.

### Added — OptimisticProver (Stage-1 IL2Prover) — 2026-07-16

- `Neo.L2.Proving.Optimistic.OptimisticProver` signs canonical public inputs and packs
  `OptimisticProofPayload` (bond contract/tx + sequencer account + 64-byte sig).
- Round-trips through `OptimisticVerifier`; fails closed on zero bond fields and wrong
  `ProofType`.
- `L2ProverPlugin.Wire` accepts optional `optimisticProver` for Stage-1 composition
  (replaces the previous NotSupportedException pointer).

### Added — CreateFromChainDirectory for batch + settlement plugins — 2026-07-16

- `L2BatchPlugin.CreateFromChainDirectory` and `L2SettlementPlugin.CreateFromChainDirectory`
  preload plugin settings from `init-l2` / `--from-deploy-report` layout without the Neo
  plugin config loader (host WireProduction composition outside Neo.CLI).
- Wireproduction notes document `batchPluginFactory` / `settlementPluginFactory`.

### Added — WireProductionFromLayout host composition root — 2026-07-16

- `L2SettlementPlugin.WireProductionFromLayout(chainDir, layout, batch, executor, da, prover, signer)`
  binds durable ProofWitness + scanner stores from `L2SettlementStoreLayout`, static
  sequencer committee hash from `chain.config.json` validators, and Multisig/Optimistic
  profile via `LegacyFromChainDirectory` (Zk hosts pass Sp1 profile explicitly).
- Wireproduction notes document `wireProductionFromLayout` + simplified requiredCallerArgs.
- Fails closed when layout path ≠ chain directory.

### Fixed — SharedBridge.Deposit uses transaction sender (wallet path) — 2026-07-16

- `SharedBridge.Deposit` now pulls NEP-17 from `Runtime.Transaction.Sender` with
  `CheckWitness`, matching ForcedInclusion fee accounting.
- Wallet entry-script invocations no longer depend on `CallingScriptHash` (the entry
  script is not the user account). ABI/method signature unchanged.
- Nested native transfer still requires a witness scope covering the asset
  (testnet WIF: `--witness-scope Global`).

### Added — local Multisig DA under chain settlement layout — 2026-07-16

- `NeoHubDeployReport.RelativeLocalDaStoreDir` = `data/settlement/da` included in
  `EnsureSettlementStoreDirectories` (init-l2 / deploy-report materialization).
- `PersistentDAWriter.OpenLocalFromChainDirectory(chainDir)` opens owned RocksDB DA
  for Multisig/Optimistic host composition (node-local only, not public DA evidence).
- Wireproduction notes document `localDaStore` + open helper + Multisig DA caller arg.

### Added — neo-stack `--witness-scope` for local WIF broadcast — 2026-07-16

- Operator WIF path accepts `--witness-scope CalledByEntry|Global` (and `--l1-witness-scope`
  with prefix). Global is required for nested NEP-17 pulls on Deposit / ForcedInclusion.

### Evidence — Neo N3 testnet follow-on live ops (WIF operator) — 2026-07-16

- Re-verified all 24 NeoHub contracts still present on testnet (`getcontractstate`).
- Confirmed chain `20260716` `isActive`, genesis root, 91-byte config, settlement
  governance locked, finalized batch 0.
- Live L1 `TokenRegistry.registerMapping` for platform GAS + NEO
  (`0xc1f44721…e06a`, `0xb51c8e4f…7daa`).
- Live `ForcedInclusion.enqueueForcedTransaction` nonce 1
  (`0x73924dce…f412`); nested GAS fee requires `WitnessScope.Global`.
- Wallet `SharedBridge.Deposit` failed on then-deployed bytecode (`CallingScriptHash`);
  source now uses `Transaction.Sender` (redeploy still funded/governance gate).
- Evidence: `docs/audit/testnet-evidence-status-2026-07-16.json`.

### Added — ProofWitnessPipelineProfile.LegacyFromChainDirectory — 2026-07-16

- `ProofWitnessPipelineProfile.LegacyFromChainDirectory` builds a Multisig/Optimistic
  settlement profile from settlement plugin config + `genesis-manifest.json` after
  `bootstrap-genesis` / `init-l2 --from-deploy-report`.
- Fails closed for ProofType=Zk (directs hosts to `Sp1SettlementExecutionStack`) and for
  missing config or genesis root.
- Wireproduction notes document the profile helper.

### Added — L2GenesisManifest reader + LocalKeyTransactionSigner WIF factories — 2026-07-16

- `L2GenesisManifest.ReadInitialStateRoot` / `ReadInitialStateRootFromChainDirectory` load the
  non-zero genesis root from `genesis-manifest.json` (camelCase or PascalCase) for host
  profile wiring after `bootstrap-genesis`.
- `neo-stack bootstrap-genesis` / `register-chain` reuse the shared reader.
- `LocalKeyTransactionSigner.FromWif` and `FromEnvironmentVariable` (default
  `NEO_N4_OPERATOR_WIF`) for local/testnet signing; stack CLI WIF import uses `FromWif`.
- Wireproduction notes document both helpers.

### Added — sequencer committee hash from chain.config.json validators — 2026-07-16

- `SequencerCommitteeConfig.ReadValidators` / `CreateStaticHashProvider` load compressed
  secp256r1 keys from `chain.config.json` `validators` and return a stable
  `Func<UInt256>` via `SequencerCommitteeHasher` for WireProduction L1 inbox wiring.
- `CreateInMemoryProviderFromChainDirectory` bootstraps an
  `InMemorySequencerCommitteeProvider` with those validators as Active (for local
  composition / tests).
- Host WireProduction E2E uses static committee hash from chain.config validators
  together with deploy-report settings + store layout.
- Fails closed on missing file, empty validators, duplicates, or invalid keys.
- Wireproduction notes document the static genesis path alongside live
  `RpcSequencerCommitteeProvider`.

### Added — batch plugin config load + host WireProduction path from deploy report — 2026-07-16

- `L2BatchSettings.FromPluginConfigFile` / `FromChainDirectory` mirror settlement loading
  for `Plugins/Neo.Plugins.L2Batch/config.json` (and node/batcher-node variants).
- Integration: live deploy-report materialization → `L2SettlementSettings.FromChainDirectory`
  + `L2SettlementStoreLayout.Open` + `WireProduction` (owned deposit + MessageRouter,
  auto L1 height, operator committee hash).
- Wireproduction notes list batch settings load alongside settlement.

### Added — load settlement settings from deploy-report plugin config — 2026-07-16

- `L2SettlementSettings.FromPluginConfigFile` and `FromChainDirectory` load the
  `PluginConfiguration` object from `config.json` written by
  `init-l2` / `register-chain --from-deploy-report`, reusing the same validation as
  `From(IConfigurationSection)` (including private-key rejection).
- Live-evidence round-trip test: materialize report → `FromChainDirectory` →
  ProofType=Zk, scanner heights, `ValidateProduction`.

### Added — WireProduction defaults L1 finalized height for inbox wiring — 2026-07-16

- When SharedBridge / MessageRouter inbox is owned or supplied and
  `l1FinalizedHeight` is omitted, `WireProduction` constructs
  `RpcL1FinalizedHeightSource` over the production `JsonRpcClient` using
  `L1FinalityDepth` (or the resolved ForcedInclusion finality depth).
- `sequencerCommitteeHash` remains required for inbox wiring (needs genesis committee keys).
- Unit coverage: defaults height when committee hash is provided; still fails closed
  with a committee-specific error when the hash provider is missing.

### Added — L1FinalityDepth plugin config + SequencerRegistry materialization — 2026-07-16

- `L2SettlementSettings.L1FinalityDepth` (default 1) is read from plugin config;
  `WireProduction` applies it to ForcedInclusion / SharedBridge / MessageRouter scanners
  when the corresponding finality-depth method args are omitted (`uint?` null).
- Deploy-report materialization writes `L1FinalityDepth` into settlement config and
  surfaces `sequencerRegistry` in `l1.deployed.json` + wireproduction notes for
  `RpcSequencerCommitteeProvider` bootstrap.
- Quick-path integration asserts live-evidence deployment heights, store dirs, and
  SequencerRegistry hash against the 2026-07-16 testnet report.

### Added — L1 finalized-height + sequencer committee hash providers for WireProduction — 2026-07-16

- `RpcL1FinalizedHeightSource` reads L1 `getblockcount` and returns
  `blockCount - 1 - finalityDepth` (zero when the tip is shallower than the depth);
  `CreateSyncProvider()` supplies the `Func<uint>` seal / WireProduction expects.
- `SequencerCommitteeHasher` computes the canonical `SequencerCommitteeHash`
  (Hash256 over lexicographically sorted compressed pubkeys) and
  `CreateSyncProvider(ISequencerCommitteeProvider)` for `Func<UInt256>`.
- Devnet reuses the shared hasher; wireproduction notes list both helpers.

### Added — L2SettlementStoreLayout opens canonical WireProduction RocksDB stores — 2026-07-16

- `L2SettlementStoreLayout.Open(chainDirectory)` ensures `data/settlement/*` and opens
  durable RocksDB backends for proof-witness, ForcedInclusion events, SharedBridge deposits,
  and MessageRouter L1→L2 events; hosts pass the handles into `WireProduction` without
  re-typing store paths.
- Unit coverage: open/idempotent reopen, missing-dir fail-closed, and end-to-end
  `WireProduction` with layout stores + config heights (owned deposit + MessageRouter).
- `l1.wireproduction-notes.json` documents `openHelper: L2SettlementStoreLayout.Open(...)`.

### Added — WireProduction store layout + register-chain post-verify — 2026-07-16

- Canonical settlement durable-store directories under the chain root:
  `data/settlement/{proof-witness,forced-inclusion-events,shared-bridge-deposits,message-router-events}`.
  Created by `init-l2` and by `NeoHubDeployReport.WriteOperatorArtifacts` / `EnsureSettlementStoreDirectories`.
- `l1.wireproduction-notes.json` lists `recommendedDurableStores` for those paths.
- `register-chain --broadcast` after HALT confirmation calls `ChainRegistry.isActive` and
  `getGenesisStateRoot` and fails closed if the chain is inactive or the genesis root mismatches.
- `RpcContractReader.ParseBoolean` accepts JSON boolean values as well as `"true"`/`"false"` strings.

### Added — settlement plugin config materializes scanner deploy heights — 2026-07-16

- `L2SettlementSettings` accepts `ForcedInclusionDeploymentHeight`,
  `SharedBridgeDeploymentHeight`, and `MessageRouterDeploymentHeight` from plugin config.
- `WireProduction` prefers explicit height args, otherwise uses plugin config (fails closed
  when ForcedInclusion height is still zero). Deposit/MessageRouter heights still fail closed
  at composition when those hashes are set without a non-zero height.
- `NeoHubDeployReport.WriteOperatorArtifacts` writes the three heights into settlement
  `config.json` when the evidence report includes deploy `blockIndex` values, so
  `init-l2` / `register-chain --from-deploy-report` closes the local WireProduction height gap.

### Fixed — zk-rollup template + sendrawtransaction hash object; testnet registerChain — 2026-07-16

- `zk-rollup` template now uses `daMode=L1` with `securityLevel=Validity` (matches
  `ChainRegistry.AssertSecurityConfigurationCompatible`; off-chain DA + ZK remains `validium`).
- `validate` warns when Validity/Validium DA pairing contradicts the on-chain rule.
- `RpcTransactionSender` accepts Neo RpcServer's `sendrawtransaction` result
  `{"hash":"<UInt256>"}` (previously only bare boolean/string), so `--broadcast` no longer
  false-fails after a successful relay.
- `LiveDeployCommand` records optional `blockIndex` on deploy/postdeploy records;
  `NeoHubDeployReport` materializes scanner heights into `l1.wireproduction-notes.json`.
- Live evidence JSON enriched with deploy heights; chain `20260716` registered on N3 testnet
  (`isActive=true`, register tx `0xb3d02a5f…9f26`, genesis `0x59be9f14…5130`).

### Fixed — deploy-report settlement ProofType follows chain.config.json — 2026-07-16

- `NeoHubDeployReport.WriteOperatorArtifacts` maps `chain.config.json` `proofType`
  (`None`/`Multisig`/`Optimistic`/`Zk`) into the settlement plugin `ProofType` byte instead
  of hardcoding Multisig(1), so zk-rollup templates materialize Validity/ZK settlement
  configs. Emits `l1.wireproduction-notes.json` listing WireProduction caller-owned args.
- Quick-path integration covers create-chain (zk-rollup) → init-l2 --from-deploy-report →
  bootstrap-genesis → register-chain against the live testnet evidence JSON.

### Added — init-l2 from-deploy-report + plugin config install — 2026-07-16

- `neo-stack init-l2 --from-deploy-report <evidence.json>` materializes `l1.deployed.json`
  and installs `Neo.Plugins.L2Settlement` + `Neo.Plugins.L2Batch` plugin configs under
  `Plugins/`, `node/Plugins/`, and `batcher-node/Plugins/` (WireProduction-ready hashes
  and chain id).
- `NeoHubDeployReport.WriteOperatorArtifacts` now also emits batch plugin config and
  writes both `config.json` and `config.from-deploy.json` audit copies.
- `register-chain` auto-detects `{chainDir}/genesis-manifest.json` when
  `--genesis-manifest` / `--genesis-state-root` are omitted after `bootstrap-genesis`.
- `new-l2` next-step text points at bootstrap-genesis + from-deploy-report registration.

### Added — bootstrap-genesis CLI for register-chain trust anchor — 2026-07-16

- `neo-stack bootstrap-genesis` runs `NeoVMGenesisBootstrap` +
  `Sp1StateWitnessSource.InitializeGenesisContractBindings` and writes
  `genesis-manifest.json` (non-zero `initialStateRoot`) under the chain directory.
  Default path uses durable RocksDB at `data/state`; `--ephemeral` uses in-memory
  state for dry-runs/tests; `--force` re-bootstraps after operator review.
- `register-chain --genesis-manifest <path>` accepts that manifest as the
  authenticated genesis root (still fails closed on zero/mismatched explicit roots).
- End-to-end local path: `create-chain` → `bootstrap-genesis` →
  `register-chain --from-deploy-report … --genesis-manifest …` emits canonical
  91-byte `configBytes` without hand-copying L1 hashes or inventing roots.

### Added — register-chain from NeoHub deploy report — 2026-07-16

- `NeoHubDeployReport` parses `neo-hub-deploy deploy-testnet` evidence JSON into typed
  contract hashes (ChainRegistry / VerifierRegistry / SharedBridge / MessageRouter /
  SettlementManager / ForcedInclusion) plus network, RPC, and owner identity.
- `neo-stack register-chain --from-deploy-report <path>` fills operator/verifier/bridge/message
  (and chain-registry on `--broadcast`) from that report, writes `l1.deployed.json` and
  `Plugins/Neo.Plugins.L2Settlement/config.from-deploy.json` under the chain directory, and
  defaults `--rpc` / `--expected-network` from the report when broadcasting.
- Explicit flags still override report values. Unit coverage includes real
  `docs/audit/testnet-deployment-20260716-live.json` when present.

### Added — Neo N3 testnet NeoHub live deployment evidence — 2026-07-16

- Deployed the full 24-contract NeoHub production bundle to Neo N3 testnet
  (`network=894710606`, RPC `https://n3seed1.ngd.network:20332`) from signer
  `NLtL2v28d7TyMEaXcPqtekunkFRksJ7wxu`, L2 domain `20260716`, governance 2-of-2.
- All post-deploy wiring + smoke checks HALT; independent `getcontractstate` re-check
  confirms every contract present. Evidence:
  `docs/audit/testnet-deployment-20260716-live.json`,
  `docs/audit/testnet-evidence-status-2026-07-16.json`.
- Remaining funded/operator gates after this L1 deploy: `register-chain` with signed
  genesis root, L2 operator node stack, 4-SDK live fixture, DA rehearsal, real SP1 proof
  vectors against the deployed verifier.

### Fixed — batcher MessageRouter surface + combined L1 inbox seal evidence — 2026-07-16

- `L2BatchPlugin.WireL1MessageInbox` retains and exposes the wired `MessageRouter`
  (symmetric to `DepositSource`) so operators can record finalized message proofs after
  settlement without holding a parallel reference.
- Unit coverage: MessageRouter-only seal includes inbound L1 messages; deposits + MessageRouter
  merge sorted under `sourceChainId=0`; second distinct router fails closed; `WireProduction`
  owned router surfaces on the batcher, fails closed on volatile/zero-height event stores, and
  is disposed with the production stack.
- `RpcMessageRouter` instance methods fail closed after `Dispose`.

### Added — production MessageRouter event scanner + WireProduction ownership — 2026-07-16

- `RpcMessageRouterEventScanner` durably discovers finalized `L1ToL2Enqueued` events
  (hash-verified restart cursor, chain-filtered, fail-closed on reorg) — same pattern as
  forced-inclusion and SharedBridge deposit scanners.
- `RpcMessageRouter` accepts an optional event scanner and scans before each inbound dequeue;
  known-nonce bootstrap and `RegisterInboundNonce` remain migration/recovery hooks.
- `L2SettlementSettings.MessageRouterHash` + `WireProduction` construct an owned
  `RpcMessageRouter` when configured (durable event store + non-zero deploy height required;
  optional durable finalized-proof store). Explicit caller-owned routers skip auto-construction.

### Fixed — seal-time SharedBridge deposit Scan before Drain — 2026-07-16

- `L1MessageDrain.FromDeposits` now calls `ScanAsync` then `Drain` so production
  `RpcSharedBridgeDepositSource` materializes finalized L1 deposits at seal time
  (symmetric to forced-inclusion event scan inside `DrainAsync`).
- `L2BatchPlugin.WireL1MessageInbox` / `WithDepositSource` install that adapter instead of
  raw `Drain`, so operators no longer need a separate poll loop for deposits to enter batches.
- Unit coverage: `FromDeposits` scan-gated materialization; batch plugin seal discovers staged
  deposits; `WireProduction` fail-closed on volatile/zero-height deposit store and missing
  block-context providers; explicit deposit source skips owned construction; dispose owns the
  auto-built deposit source.

### Added — production SharedBridge deposit stack in WireProduction — 2026-07-16

- `L2SettlementSettings` accepts optional `SharedBridgeHash` / `L2BridgeHash` (default
  `NativeContract.L2Bridge.Hash` when SharedBridge is set).
- `WireProduction` constructs an owned `RpcSharedBridgeDepositSource` when SharedBridge is
  configured and the caller does not supply a deposit source, requiring a durable deposit
  event store and non-zero deploy height (symmetric to forced-inclusion production wiring).
- Explicit caller-owned `depositSource` still overrides auto-construction; missing store /
  height / block providers fail closed.

### Fixed — settlement Wire attaches SharedBridge deposit L1 inbox — 2026-07-16

- `L2SettlementPlugin.Wire` / `WireProduction` now accept optional
  `ISharedBridgeDepositSource` + `IMessageRouter` and install them on the batcher via
  `WireL1MessageInbox` before the sealed-batch sink is attached (same order as forced
  inclusion). Block-context providers are required whenever an L1 inbox source is present;
  chain mismatch and missing providers fail closed. Deposit sources remain caller-owned.

### Added — batcher deposit inbox integration evidence — 2026-07-16

- Added `L2BatchPlugin` unit coverage for `WireL1MessageInbox` with real
  `InMemorySharedBridgeDepositSource`: durable seal confirms deposits; persist failure
  releases reservations and the retry re-includes them; empty sources and chain mismatch
  fail closed. Closes the missing composition-root proof for the deposit reserve/confirm path.

### Fixed — SharedBridge deposit reserve/confirm lifecycle — 2026-07-15

- Deposit sources now **Drain (reserve) → ConfirmConsumed (after durable seal) →
  ReleaseReservations (on persist failure)** so the same deposit cannot be included in every
  subsequent batch.
- `L2BatchPlugin.WireL1MessageInbox` / `WithDepositSource` compose SharedBridge deposits with
  optional MessageRouter traffic and confirm deposit nonces only after sink persistence succeeds.
- `InMemorySharedBridgeDepositSource` and `RpcSharedBridgeDepositSource` implement the full
  contract; bridge-plugin peek is non-mutating.

### Fixed — metrics scrape connection storm bound — 2026-07-15

- `MetricsHttpServer` now hard-caps concurrent accepted connections (default 32) via a
  semaphore: excess scrapes/probes receive HTTP 503 and never spawn unbounded handler
  tasks. Active connection count is exposed for diagnostics; `L2MetricsPlugin` config
  adds `MaxConcurrentConnections`.

### Added — local deposit composition and drain combiner — 2026-07-15

- Added `L1MessageDrain.Combine` / `FromRouter` so operators compose SharedBridge
  deposits with MessageRouter traffic under one fail-closed, nonce-ordered sealer
  drain (duplicate `(source,nonce)` keys are rejected).
- Added `InMemorySharedBridgeDepositSource` for tests and the in-process devnet.
- `L2BridgePlugin.WithDepositSource` / `PeekSharedBridgeDeposits` hold a non-mutating
  composition handle; mutating drain/confirm is owned by `L2BatchPlugin`.
- Devnet now mints via `SharedBridgeDepositRecord` → in-memory deposit source →
  `L1MessageDrain` → `DepositProcessor`, matching the production message shape.

### Added — production SharedBridge deposit L1→L2 ingest — 2026-07-15

- Added `SharedBridgeDepositRecord` with byte-for-byte parity to
  `NeoHub.SharedBridge.EncodeDeposit` / `GetDeposit` (unsigned little-endian amount).
- Added `RpcSharedBridgeDepositScanner` (durable `DepositEnqueued` discovery with
  hash-verified restart cursor) and `RpcSharedBridgeDepositSource` (GetDeposit
  materialization into a native-bridge-bound `CrossChainMessage` + `DepositPayload`,
  `Peek` / `ConfirmConsumed` drain for the batcher).
- SharedBridge still does not enqueue MessageRouter; operators wire this source as
  the deposit half of the batcher's L1 message drain. Unit tests cover record
  round-trip, high-MSB amounts, and end-to-end scan/materialize/confirm.

### Fixed — resilience and fraud-proof honesty — 2026-07-15

- Made `CircuitBreaker` half-open allow exactly one concurrent probe via a latch and an
  injectable `TimeProvider`, matching its documented contract and preventing load from
  stampeding a degraded L1 RPC during recovery.
- Counted JSON-RPC application failures (parse errors, id mismatches, and `error`
  objects) in `JsonRpcClient` so HTTP 200 + bad RPC payloads trip the breaker instead of
  failing open forever.
- Bound `ChallengeOrchestrator.InspectWithBisectionAsync` to a local full-batch replay:
  the challenger checkpoint terminal root must match the replayer before a narrowed fraud
  payload is emitted, so unauthenticated intermediate checkpoints alone cannot invent fraud.
- Made `BatchSealer` fail closed when a wired forced-inclusion drain returns null or exceeds
  the batch cap, matching the L1 message drain contract.
- Included `StorageProofs` in `FraudProofPayload.GetHashCode` so Equals/GetHashCode remain
  consistent for v3 payloads used as dictionary keys.
- Encoded SharedBridge deposit storage amounts with the same minimal unsigned little-endian
  helper used by withdrawal leaves and off-chain `DepositPayload`, avoiding an extra sign
  byte for high-MSB amounts.

### Changed — documentation truth after PR #28 SP1 cost split — 2026-07-15

- Corrected release, audit, implementation-status, README, SPEC, and EN/ZH operator docs that
  still claimed real SP1 proofs were unconditional on every PR/`master` push. Ordinary CI only
  requires the fast `SP1 compatibility and manual release proof gate`; terminal and recursive
  real proofs run on manual `workflow_dispatch` of `sp1-release-gates`.
- Documented the production NeoFS path as `NeoFsRestDAWriter` + `NeoFsRestDAReader` via
  `WithProductionBackend`, and marked `NeoFsLikeDAWriter` as development simulation only.

### Changed — coordinated dependency maintenance — 2026-07-15

- Retained the coverage collector at 6.0.4 after 10.0.1 changed the reported executable-line
  set and made identical commits oscillate around the 90% coverage gate. A future upgrade must
  include an explicit coverage-baseline migration.
- Upgraded the Ethereum watcher to `sha3` 0.12 and `toml` 1.1.3, with the lockfile updated
  as one atomic Rust dependency change.
- Upgraded the pinned GitHub workflow actions to `actions/checkout` 7, `actions/cache` 6,
  `docker/setup-buildx-action` 4, `docker/login-action` 4, and `docker/metadata-action` 6.

### Fixed — parallel SP1 release gates — 2026-07-15

- Split workspace release checks, real batch proving, and real recursive Gateway proving across
  three independent pinned SP1 runners for explicit release validation. Pull requests and ordinary
  master pushes run the fast .NET, contract, native execution, and Rust compatibility gates without
  regenerating proofs; operators opt into the release-grade lanes with `workflow_dispatch`. The two
  proof lanes use SP1's upstream worker controls to serialize core/recursion work, cap trace buffering
  and sharding, and enforce a 4-GiB guest memory limit on standard hosted runners. Each independent
  lane retains the production 120-minute proof budget without changing Groth16 proof mode or allowing
  mock/dummy fallback.

### Fixed — closed ChainRegistry admission and governance states — 2026-07-15

- Made the public admission-mode state machine fail closed at the cross-contract boundary.
  `ChainRegistry` now validates the full `BigInteger` returned by `GovernanceController` as
  exactly 0, 1, or 2 before narrowing it to `byte`; negative, undefined, and wrapping values
  such as 258 cannot be interpreted as permissionless admission or persist chain state.
- Made `ChainRegistry.LockGovernance` freeze the wired GovernanceController as well as the
  direct update path. The bootstrap owner can no longer replace the proposal authority after
  lock; a controller migration requires a versioned registry deployment, matching the existing
  VerifierRegistry policy. The scaffold and live deployment paths now execute this irreversible
  lock and verify it by read-back before reporting a production deployment complete.
- Regenerated the real ChainRegistry NEF/testing artifact and added VM regressions for invalid
  modes, zero-side-effect rejection, post-lock controller replacement, and retained controller
  identity. The complete NeoHub contract VM suite passes 551/551.

### Fixed — finalized retention, crash-safe rollback, and governed settlement control — 2026-07-15

- Split durable settlement observation from canonical finality. `Pending` and `Challengeable`
  remain recoverable observations; proof-queue acknowledgement, forced-inclusion consumption,
  pending-count retirement, and content-addressed evidence pruning now require L1 `Finalized`.
- Added authenticated rollback checkpoints for a `Reverted` canonical tail. The exact artifacts and
  proofs are quarantined, the pre-tail state snapshot is restored atomically, checkpoint completion
  is crash-idempotent and uses a key-local atomic delete rather than copying the complete database,
  and the same batch number can be resubmitted only after recovery completes.
  Startup now queries L1 for every local artifact and validates local proof manifests, contiguous
  finality, and the canonical root before any recovery side effect.
- Added guarded multi-key `IAtomicL2KeyValueStore.CompareExchangeBatch` commits to both built-in
  stores. RocksDB uses one synchronous WAL `WriteBatch`, preventing an artifact publication from
  racing a rollback marker across independent store wrappers.
- Bound SettlementManager production administration to GovernanceController and added an irreversible
  governance lock. After lock, the hot owner cannot rewire proof/DA/message dependencies or directly
  revert batches; exceptional finalized-head rollback requires an executing-contract-bound exact
  threshold-approved, timelocked, one-time proposal payload, preventing cross-deployment replay.
  Live deployment now requires explicit distinct M-of-N
  governance with threshold at least two and rejects implicit 1-of-1 control.

### Fixed — immutable on-chain genesis trust anchor and exact committee-key preflight — 2026-07-15

- Changed both ChainRegistry admission paths to atomically register a non-zero immutable
  `genesisStateRoot` alongside the 91-byte chain config. Settlement now requires batch 1 to link
  that root during submission and finalization, and returns it as the canonical root before the
  first finalization or after a first-batch revert. The off-chain settlement profile carries and
  cross-checks the same root, so neither a first submitter nor a restarted operator can silently
  establish a different trust anchor.
- Strengthened sequencer NEP-6 preflight from account metadata checks to real key decryption and
  public-key equality. A multi-account wallet with a valid password but a mismatched/corrupt
  encrypted committee key now fails closed.
- Added VM, settlement, and CLI regression coverage for missing/zero/changed genesis roots,
  first-batch binding and fallback, profile mismatch, required CLI arguments, and encrypted-key
  substitution.

### Fixed — fail-closed settlement continuity and production durability — 2026-07-15

- Moved predecessor, block-range, and state-root continuity validation ahead of execution, DA
  publication, artifact commit, and state acknowledgement. Missing or malformed predecessor
  batches can no longer durably advance SP1 state and strand restart recovery.
- Added explicit durable-store capabilities for proof witnesses and L1 forced-inclusion event
  cursors. `WireProduction` now rejects volatile stores while custom/test `Wire` composition
  remains available for non-production use.
- Corrected the private-network operator preflight to require reviewed Neo.CLI configs,
  sequencer/batcher deployments, and a prover binary; all three launch checks are explicit
  dry-runs instead of unconfigured blocking process starts. Runtime/plugin files are now
  whitelist-staged into an owner-only OS temporary directory that is removed in `finally`;
  wallets, node data, logs, arbitrary JSON, hidden paths, and links never enter long-lived
  artifacts. Sequencer dry-runs now open the reviewed NEP-6 wallet with its configured password
  and require a decryptable committee key whose derived public key exactly matches the configured
  validator; malformed, mismatched, unrelated, or unsupported wallet files fail closed.
- Removed caller-controlled sequencer attribution from permissionless censorship reports while
  retaining the contract ABI. Reports now emit only the zero address; governance names a slash
  target separately after reviewing finalized dBFT evidence.
- Restricted forced-inclusion fees to the canonical Neo N3 native GAS contract across deploy,
  configuration, readiness, and enqueue paths. Enqueue now charges and commits the witnessed
  transaction sender instead of the entry invocation script hash. Consumption reserves its replay
  marker before the read-only finalized-root lookup and relies on NeoVM atomic rollback for every
  failed proof.
- Added no-side-effect regression coverage for batch zero, authenticated-genesis mismatch,
  missing predecessor, block/state discontinuity, and predecessor block-number overflow.
- Added a required CI ancestry gate for the `external/neo` gitlink: every referenced core commit
  must already be published on `r3e-network/neo` branch `r3e/neo-n4-core`, preventing an
  unpublished feature-branch-only core dependency from entering a releasable parent revision. The
  verifier fetches the hard-coded canonical R3E URL rather than trusting the PR-controlled
  submodule `origin`.
- Reconciled the complete 38-project test inventory against one serial TRX run: 2,591 tests were
  discovered, 2,587 passed, none failed, and four exact-deployment/native-fixture tests remained
  explicitly environment-gated.

### Added — SP1 restart and cross-instance state-race regression gates — 2026-07-15

- Proved that a reconstructed SP1 executor replays an already durable artifact after the
  artifact/state crash window, retains the immutable genesis root, and accepts the recovered
  post-state idempotently without executing the transition again.
- Added an injected interleaving between complete-state capture and `CompareExchangeAll`; the
  losing transition now has direct regression evidence that it fails closed without overwriting
  the concurrently committed state.

### Fixed — real SP1 CI workspace ownership — 2026-07-15

- Restored the GitHub runner's ownership of Docker-generated SP1 target artifacts before
  host-side Cargo formatting, linting, and tests, preventing the required real-proof job from
  failing with a permission error after a successful reproducible guest build.
- Serialized the resource-intensive ignored proof tests and raised the fail-closed job budget to
  120 minutes so concurrent SP1 provers cannot exhaust a hosted runner while every terminal and
  recursive proof assertion still executes.

### Added — native SP1 execution and atomic state handoff — 2026-07-14

- Added host-native `neo-zkvm-executor` as a second binary in the existing guest crate; it calls
  the exact `neo-execution-core` and stateful NeoVM runtime used by the SP1 guest and emits the
  canonical content-addressed `NEO4EXR1` transition protocol.
- Added cross-language Rust/C# serializers and golden vectors that bind both exact requests,
  execution semantic, roots/gas, canonical effects, complete post-state witness, and public inputs.
- Added `IAtomicL2KeyValueStore.CompareExchangeAll` for complete-snapshot CAS through one
  lock-protected in-memory swap or one RocksDB `WriteBatch`, plus `Sp1StateWitnessSource`
  continuity, contract-binding, concurrent-writer rejection, and no-partial-mutation validation.
- Added `Sp1StatefulBatchExecutor` and `Sp1SettlementExecutionStack`: production execution pins an
  independently reviewed executable SHA-256, runs only an isolated digest-matched copy, validates
  every native output binding, durably commits and re-reads the immutable proof-witness artifact,
  then deterministically replays and atomically commits its exact post-state. Retry and startup
  recovery are idempotent, so no crash can advance state without a durable replay record.
- Added the non-ignored CI C#→release-Rust process gate over a bootstrapped Neo genesis transaction.
  N4 genesis V1 remains fail-closed for unsupported native/syscall behavior and contract descriptor
  add/remove/replace transitions; exact-revision public deployment and independent audit evidence
  remain release gates.
- Made terminal and recursive real SP1 proof generation unconditional in the required CI job.
  Hardened the prover queue with `0700` directories, `0600` files, 16-GiB/64-task backpressure,
  and settlement-confirmed hash acknowledgements; the daemon now prunes content-addressed evidence
  only after durable L1 finalization, never by TTL.
- Eliminated the shared-target ELF validation/include race: build scripts derive SHA-256 and VK from
  one byte snapshot, publish a read-only verified copy into Cargo `OUT_DIR`, and embed only that copy.

### Fixed — canonical C# / Rust proof-witness ABI — 2026-07-14

- Aligned the SP1 parser and encoder with the pinned C# `ProofWitnessArtifactV1` header,
  execution-semantic binding, authenticated-witness flag, and complete DA receipt fields.
- Added one checked-in stateful SP1 fixture decoded and re-encoded byte-for-byte by both
  languages, and made the guest reject any non-stateful or mismatched semantic identifier.

### Fixed — settlement confirmation-lag telemetry — 2026-07-14

- Added a restart-safe L1 confirmation-lag gauge derived from durable proof manifests, alongside
  regression coverage and operator documentation, without changing the pinned manifest wire format.

### Fixed — documentation truth and release attribution — 2026-07-14

- Split every phase status into design, code shape, integration, cryptographic
  enforcement, exact-revision deployment evidence, and production readiness;
  no phase is represented as production-ready without independent deployment evidence.
- Reconciled the source-derived inventories at 26 NeoHub projects, 24 production
  deployment steps, 38 .NET test projects, 44 Foundry tests, four typed SDK source
  implementations, five submodules, and 39 English / 49 Chinese top-level docs;
  targeted regression gates bind these public facts and the remaining Go SDK gap.
- Completed R3E Network attribution for sample contracts and security reporting,
  enabled private vulnerability reporting, Dependabot security fixes, secret scanning,
  and push protection, while retaining the honest future-release policy: no production
  tag or release exists yet.

### Fixed — external payout release-gate coverage — 2026-07-14

- Added production-composition tests for the durable L1 payout scanner, authenticated L1/L2 RPC
  clients, RocksDB-backed relay runtime ownership, finalized-history reorganization rejection,
  canonical event parsing, domain/configuration mismatch rejection, and transaction confirmation.
- Added ordered value-semantics regression coverage for payout instructions, proof-witness
  artifacts, execution payloads, optimistic proof payloads, and every accumulated batch effect.
- Restored the unchanged CI thresholds with 90.03% gate line coverage and 86.29% overall reported
  line coverage; no coverage exclusion or threshold reduction was introduced.

### Added — fail-closed recursive Gateway SP1 proof — 2026-07-14

- Moved production SP1 guest builds to `cargo prove build --docker --locked` with
  an immutable SP1 6.2.1 amd64 image digest, regenerated both ELF/VK manifests from
  those canonical Docker artifacts, and made the CI wrapper reject host-native builds.
- Split the batch VK input manifest from the host-only Gateway output manifest so
  the Gateway guest cannot compile its own VK/ELF pins and create a self-referential hash cycle.
- Extended the scheduled/manual real-proof release gate to execute and host-verify
  the recursive Gateway Groth16 proof in addition to the terminal batch proof.
- Pinned the separate SP1 gnark wrapper image by immutable amd64 manifest digest and fail closed
  before proving when the Docker backend is missing that exact reference. Apple Silicon uses
  SP1's upstream `native-gnark` backend instead of unreliable amd64 emulation; the private-network
  harness now runs both the terminal batch and recursive Gateway real-proof gates.
- Added independent SP1 6.2.1 Gateway guest/host crates. The guest strictly validates the
  `NEO4GWP1` request, fixed 170-byte `NEO4GWR2` binding, canonical ordered commitments and roots,
  and compile-time-batch-VK compressed child proofs before committing only
  `0x00 || Hash256(binding170)`.
- Added a canonical tuple-derived compressed-proof sidecar protocol, build manifest locking both
  program VKs and ELF digests, host-side child and terminal Groth16 verification, symlink/path
  rejection, atomic artifact publication with result manifest last, and explicit test-only VK
  gating that cannot produce proofs.
- Added parser/root/tamper/VK/proof-kind/path tests plus a real-recursion release gate that is now
  unconditional in the required SP1 CI job.
- Added crash recovery that cryptographically re-verifies complete result markers and safely
  removes only regular non-symlink orphan artifacts before re-proving.
- Added atomic `SettlementManager.PublishGatewayGlobalRoot`: exact ordered finalized-batch
  references, bounded O(log 4096) dual-root reconstruction, per-chain non-revertible watermarks,
  deployment readback, and same-transaction forwarding to `MessageRouter`. The proof-bound RPC
  publisher queries Router for reconciliation but submits through SettlementManager.
- Corrected the cross-layer empty-message invariant: a proven zero `globalMessageRoot` is the
  canonical result for a zero constituent message-root tree, while publication presence remains
  independently bound by the epoch proof-input record. Rust, .NET, and both NeoVM contracts now
  accept that valid statement without weakening constituent, domain, VK, or proof checks.
- Phase 5 remains partial until independent audit and executed real-proof deployment evidence are
  complete; the former SettlementManager authorization/finality implementation gap is closed.

### Fixed — governance quorum recovery evidence — 2026-07-14

- Added VM-level evidence that two surviving signers in a 2-of-3 council can approve and execute
  an epoch-bound, timelocked full rotation without the unavailable signer, after which removed keys
  lose authority and the replacement council controls new proposals.
- Documented the fail-closed recovery boundary: council rotation has no owner bypass, and loss of
  quorum requires a separately reviewed emergency-governance migration.
- Corrected the forced-inclusion slashing contract documentation: a late consume does not erase
  censorship that was already reported after its deadline.

### Fixed — native RISC-V coverage gate — 2026-07-14

- Made the .NET coverage runner build and load the locked platform `neo_riscv_host` artifact,
  record its SHA-256, and fail instead of skipping when the stateful ABI is unavailable.
- Expanded real-native coverage across runtime context, complex stack marshalling, storage
  read/write/delete/iterator behavior, canonical notifications, callback errors, out-of-gas
  rollback, and default executor bootstrapping; the explicit bridge gate now discovers every
  `RealNative_` test, including the previously omitted Notify boundary.

### Fixed — execution transaction integrity coverage — 2026-07-14

- Added commit-compensation tests for successful before-image restoration and the aggregate-error
  path when both the store mutation and compensating rollback fail.
- Covered atomic overlay operations, lifecycle guards, enumeration copies, content-based canonical
  effect equality/hashing, malformed effect versions and operations, and removed an unused event-copy helper.

### Fixed — Gateway prover binding coverage — 2026-07-14

- Added direct fail-closed tests for the delegate Gateway prover's proof-system range,
  production-backend allowlist, cancellation, aggregate backend, and canonical binding checks.

### Fixed — batch plugin production-boundary coverage — 2026-07-14

- Added direct lifecycle coverage for immutable sink and input wiring, chain-domain mismatches,
  metrics re-wiring, pending-batch retry ordering, and unbound settings adoption.
- Added forced-inclusion source coverage that filters durably tracked and null entries, preserves
  L1 consumption until settlement finality, and rejects a null drain result before persistence.

### Fixed — canonical codec boundary coverage — 2026-07-14

- Hardened the shared proof-payload length-prefix primitives with explicit position, capacity,
  prefix, and payload bounds so malformed data fails with stable typed errors.
- Added direct content-equality coverage for proof-witness execution results and canonical
  message/withdrawal decoder round-trip, truncation, enum, length, and maximum-value boundaries.

### Added — four-language SDK conformance — 2026-07-14

- Added one canonical JSON vector set consumed by the .NET, Rust, TypeScript, and Python SDKs,
  covering all L2 RPC shapes, lossless `u64` encoding, UInt256 endianness, error mapping,
  pagination serialization, and a real signed Neo N3 transaction round-trip.
- Added a fail-closed CI runner with machine-readable per-language discovery/execution counts and
  live N3/N4 tests that explicitly skip without endpoint configuration but are mandatory on
  scheduled, tagged, and opt-in release lanes.
- Fixed definite conformance defects exposed by the suite: .NET and Rust now serialize every
  `u64` request as a decimal string, all clients reject non-2.0 response envelopes, and the Rust
  SDK includes a production HTTPS trust-store path.

### Fixed — P1-1 RPC / SDK ABI alignment — 2026-07-14

- Reconciled `doc.md` §14.1, the official N4 RpcServer registration adapter,
  `L2RpcMethods`, and the .NET, TypeScript, Rust, and Python SDKs around one
  canonical ten-method surface.
- Standardized every u64 request/response value as a decimal JSON string, restored
  `getbridgedasset(l1Asset, chainId)`, documented chain-bound proof identities and
  the optional state-root batch selector, and made `getsecuritylabel` canonical.
- Extended shared offline/live conformance gates and the real local NeoSystem +
  Kestrel RpcServer integration test to fail closed on envelope, chain, request,
  and identity drift; aligned the inline Web Explorer client with the same u64
  and response-binding rules. No public testnet or deployment evidence is claimed.

### Fixed — SDK conformance documentation parity — 2026-07-14

- Added the missing Chinese counterpart for the shared four-language SDK conformance contract,
  including its fail-closed live fixture requirements and canonical protocol-ABI alignment.

### Fixed — external inbound bridge payout closure — 2026-07-14

- Completed verified foreign-to-Neo asset finalization with atomic direct NEP-17 release or a
  versioned payout/credit adapter that receives every signed domain, identity, value, nonce,
  deadline, source-transaction, and canonical-message field.
- Bound each deployment to an explicit Neo destination domain (`0` for L1, non-zero for L2),
  restricted zero-adapter direct liquidity/release to L1, and made every L2 route fail closed
  unless it installs a versioned adapter that atomically persists or enqueues target credit.
- Made foreign-to-Neo asset mappings immutable and reverse-unique per source chain, pinned adapter
  ABI plus Neo update counter to reject in-place code drift, and retained an emergency route-disable
  path without allowing silent adapter repinning.
- Added irreversible proposal-only production governance, exact proposal-payload binding and replay
  protection, chain-specific liquidity accounting, canonical uint256 amount validation across C#,
  Rust, and NeoVM, plus deployment-plan wiring and failure/reentrancy/upgrade regression coverage.
- Integrated the durable L2 payout relay with the opaque network-order `ExternalAssetId` wire type;
  scanner parsing and L2 invocation scripts preserve raw foreign-asset bytes, with an asymmetric
  address regression vector preventing accidental Neo `UInt160` endianness conversion.

### Fixed — Python SDK CI dependency parity — 2026-07-14

- Installed the declared Python SDK test extra in the multi-version package job so its shared
  cryptographic conformance tests execute on Python 3.10–3.13 instead of failing at import time.

### Fixed — execution-effects profile isolation — 2026-07-14

- Added an explicit transaction-effects profile so canonical Neo N4 executors re-hash receipt
  effects and derive outbox commitments only from the pinned native notification ABI, while
  custom-chain executors retain their declared deterministic withdrawal/message semantics.
- Restored custom-executor end-to-end compatibility without weakening the fail-closed native
  execution/proof boundary, and pinned both ApplicationEngine and RISC-V executors to the
  canonical native profile with regression coverage.

### Fixed — executable fraud-proof deployment boundary — 2026-07-14

- Removed advisory `GovernanceFraudVerifier` from the default/live production deployment and
  stopped registering any v1/v2/v3 structural verifier for state-changing challenges.
- The 23-step production plan now registers only the exact SettlementManager/replay-domain-bound
  executable v4 profile. Legacy custom plans receive fail-closed warnings and governance/owner
  witness cannot substitute for executable fraud verification.
- Reconciled English and Chinese operator, architecture, security, whitepaper, and status docs
  with the 23-production + 1-advisory + 1-test-only NeoHub project inventory.

### Fixed — forced-inclusion event discovery — 2026-07-14

- Replaced the operator-supplied nonce watcher gap with a durable finalized-L1 scanner wired by
  `L2SettlementPlugin.WireProduction`. It scans `getblock`/`getapplicationlog`, accepts only the
  configured contract's canonical `ForcedTxEnqueued` payloads, persists each nonce before its
  cursor, verifies the previous block hash on restart, and fails closed on malformed logs or
  finalized-history changes.
- Production wiring now requires the contract deployment height and a caller-owned durable event
  store. Manual nonce registration remains available only for controlled migration/recovery.

### Fixed — Rust dependency advisories — 2026-07-14

- Refreshed all production Rust lockfiles to remove the current RustSec
  vulnerabilities in `crossbeam-epoch` and `quinn-proto`, plus patched
  `anyhow`, `memmap2`, and `rand` releases flagged for unsound behavior.

### Fixed — settlement poison-batch recovery — 2026-07-14

- Added durable per-artifact bounded settlement retries and explicit poison checkpoints that
  survive RocksDB restart, expose pending/retry/poison status and metrics, and stop automatic work
  after the configured bound without deleting the canonical artifact or proof state.
- Enforced contiguous batch-number reconciliation so later settlement cannot bypass a missing or
  poisoned predecessor; preserved transaction-aware duplicate suppression and ambiguous-broadcast
  reconciliation.
- Added exact batch/content-hash operator recovery after remediation. Recovery resets retry state
  while retaining canonical proof, transaction, forced-inclusion, and L1 reconciliation state;
  terminal rejection remains an explicit protocol/governance policy decision rather than a local
  skip mechanism.

### Fixed — release-gate completeness — 2026-07-14

- Made filtered .NET and ignored Rust proof gates fail closed when their expected
  tests are missing, skipped, or silently renamed; pinned Rust, SP1, mdBook, and
  audit tooling; and extended dependency, SDK, contract-security, watcher-image,
  and package-build validation across the production release surface.

### Added — production settlement composition root — 2026-07-14

- Added `L2SettlementPlugin.WireProduction` to construct and own the shared L1 RPC client,
  network-pinned confirmed transaction sender, settlement client, and paired RPC
  forced-inclusion source/finalizer while preserving caller-owned `Wire` dependency injection.
- Made the production endpoint, network magic, SettlementManager hash, ForcedInclusion hash,
  and signer boundary explicit. Missing/malformed/zero identities, private-key fields in plugin
  config, zero signer accounts, and unconfirmed/zero-hash transaction results fail closed.
- Documented signer and resource ownership and added production configuration, construction,
  forced-pair, and disposal coverage.

### Fixed — forced-inclusion durable L1 completion — 2026-07-14

- Closed the forced-inclusion settlement loop with a production RPC finalizer that validates
  persisted transaction proofs against the finalized L1 transaction root, idempotently submits
  canonical `consume` calls through the shared signed transaction sender, and requires an L1
  `isConsumed` read-back before acknowledging durable completion.
### Added — production NeoFS data availability adapter — 2026-07-14

- Added a production `NeoFsRestDAWriter` / `NeoFsRestDAReader` pair over the
  official NeoFS REST Gateway object API, without depending on the archived
  proof-of-concept C# SDK.
- Publication now returns the canonical NeoFS `container/object` address and
  succeeds only after an independently configured reader retrieves the object
  and verifies its response headers, payload, and canonical Hash256 commitment.
- Added explicit request-authentication DI, rotating v2 session-token support,
  opt-in anonymous EACL access, HTTPS-only endpoints, bounded responses, and
  fail-closed handling for malformed locators, status codes, and content.

### Fixed — security and production-status documentation — 2026-07-14

- Reconciled English and Chinese architecture, launch, security, and status
  documentation with the shipped SP1 terminal verifier, restricted fraud-proof v4,
  epoch-bound council rotation, 25-project NeoHub inventory, 23-step production bundle,
  four typed SDKs, 37 solution test projects, and the official RpcServer integration.
- Removed volatile total-test counts and corrected production-boundary claims for
  Gateway round provers, operator key custody, real NeoFS, and deployment evidence.

### Fixed — release artifact ownership metadata — 2026-07-14

- Replaced incorrect Neo Project authorship with R3E Network across package metadata and all 25 NeoHub contract manifests, regenerated the 24 VM testing artifacts from fresh `nccs` output, and added a fresh-manifest regression gate for maintainer attribution.

### Operator signer-command boundary — 2026-07-14

- Added a fail-closed `neo-stack --signer-command` bridge for HSM/KMS or wallet
  adapters: it pins the account and verification script, sends canonical Neo
  sign data without a private key, rejects invalid/empty/size-mismatched
  witnesses, and retains preflight, exact-fee, broadcast, and HALT confirmation.
- Documented the command protocol and corrected the production launch path so
  sequencer, prover, and optional batcher run concurrently under separate
  terminal or service supervisors.

### Production dBFT operator integration — 2026-07-13

- Added committee-authorized initialization plus an owner-authorized pending/active L2
  sequencer validator set;
  stock `GetNextBlockValidators` / refresh-block `ComputeNextBlockValidators` selection now
  commits `NextConsensus` before activation, so unmodified DBFT never switches early.
- Added canonical committee transaction construction plus
  `start-sequencer --sync-committee` / `--sync-only` governance submission and confirmation.
- Replaced `start-{sequencer,batcher,prover}` plan printers with supervised real
  Neo.CLI/SP1 child processes: no shell, fixed config/data ownership, exact exit codes,
  SIGINT/SIGTERM grace, separate batcher storage, plugin/config/network fail-closed checks.
- Extended `init-l2` with non-overwriting sequencer and batcher Neo.CLI config installation
  plus isolated node, batcher, prover-inbox, and prover-archive directories.

### Stateful SP1 execution witness V1 — 2026-07-13

- Reused the canonical `ProofWitnessArtifactV1` envelope for SP1 and added bounded `NEO4STW1` state/code/manifest witness plus `NEO4EFX1` effects sections.
- Replaced script-only/fixed-zero guest execution with full Neo transaction decoding, stateful `neo-vm-rs` execution, production opcode/syscall gas, HALT overlay commits, FAULT rollback, and fail-closed unsupported syscalls.
- Froze the 105-byte `CanonicalReceiptV1`, domain-separated storage/event V1 hashes, canonical event stack encoding, and keyed post-state-root recomputation.
- Added a stable stateful golden artifact, host/guest parity coverage, and transaction/witness/state/root/receipt/order/parameter tamper rejection tests.
- Added the restricted N4 genesis native transition V1: non-empty L1 deposits atomically update the real L2Bridge replay/mapping and TokenManagement token/account layout before transactions, while unsupported inbox types fail closed.
- Derived withdrawal and L2→L1/L2 roots only from exact canonical L2Bridge/L2Message notifications, with pinned native hashes/ABIs, limited proven native dispatch, duplicate/malformed rejection, and C#/Rust/SP1 parity goldens.

### Fixed — N4 genesis execution-effects parity — 2026-07-13

- Aligned the shared C# ApplicationEngine/RISC-V effects hasher with SP1 genesis V1: exact storage domains and operations, recursive `NEO4STK1` event states, zero empty hashes, and Rust-generated receipt/effects/state-root goldens.

### Added — stateful PolkaVM execution parity — 2026-07-13

- Replaced the stateless RISC-V P/Invoke path with
  `neo_riscv_execute_script_with_host`. The managed host now roots callback
  delegates for process lifetime, passes each transaction through a
  `GCHandle`-owned context, recursively marshals the complete native stack
  model, and releases both native results and callback-owned buffers on every
  exit path.
- Added fail-closed host implementations for transaction/block runtime
  context, `CheckWitness`, `Runtime.Notify`, storage read-through with
  `Put`/`Delete`/`Find`, local-storage aliases, and iterator operations.
  Consensus syscalls not explicitly implemented return callback failure and
  force VM `FAULT`; no dummy values, unconditional witness success, or no-op
  writes remain.
- Added `ExecutionStateTransaction` and canonical execution effects V1.
  `ApplicationEngineTransactionExecutor` and `RiscVTransactionExecutor` now
  derive receipt hashes and exposed effects from the same immutable source,
  and commit storage only after `HALT`. `FAULT`, out-of-gas, callback,
  collector, and commit failures roll back the transaction overlay.
- Pinned the 105-byte receipt preimage and V1 storage/event encodings with
  ordering, before/after image, full event stack-state, rollback, fee,
  fail-closed, managed parity, and real native PolkaVM tests.

### Security — P0-4 committed-root-bound optimistic fraud proof v4

- Added canonical `RestrictedFraudProofV4`: chain/batch/tx index, 321-byte
  committed-header hash, pre/post/tx roots, canonical degenerate `[0,1]` transcript, executor
  semantic id, replay domain, transcript hash, witness hash, and deployment-bound
  claim id are one immutable claim. Transaction inclusion reuses
  `MerkleProofSerializer`; state old/new leaves and paths reuse `StorageProof`
  and reconstruct the committed pre/post roots independently.
- Added `SettlementManager.GetChallengeableBatchHeader` for exact stored optimistic
  headers while status is `Challengeable`. It does not alter finalization or the
  finalized message/tx-root getter surface.
- Added executable restricted v4 verification for one existing-key Counter
  Increment transaction. Honest transitions return false; a wrong committed post
  root returns true. Unsupported semantics and multi-transaction batches fail
  closed; this is explicitly not a general NeoVM trustless verifier.
- Scoped permissionless challenges to exact chain/verifier/semantic/replay profiles,
  disabled legacy global permissionless registration, consumed successful v4 claim
  ids, invalidated profiles across revoke/downgrade generations, and preserved atomic
  CEI rollback around batch revert and bond slashing. V1/v2/v3 remain governance-only.
- Added off-chain, NeoVM, and integration regressions for honest/fraud transitions,
  root substitution, chain/batch/tx/bisection/replay/semantic/claim/witness/path
  tampering, slashing boundaries, rollback, and fresh `nccs` artifacts.

### P0-8 canonical proof-witness settlement pipeline — 2026-07-13

- Replaced soft zero-root batch submission with immutable `SealedBatch` execution payloads and a
  single durable pipeline through DA, canonical `ProofWitnessArtifactV1`, proof manifest,
  settlement submission, and restart reconciliation.
- ZK profiles now fail closed unless executor/prover semantic IDs match, execution witness bytes
  are authenticated and non-empty, the prover is cryptographic, and all payload/public-input/DA/
  execution bindings agree. Explicit multisig/optimistic profiles remain isolated compatibility
  paths.
- Forced-inclusion nonce, transaction index, hash, and Merkle siblings are persisted in the
  canonical artifact. Consumption is deferred until settlement finality and delegated to
  `IForcedInclusionFinalizationClient`, which must verify the finalized transaction root, submit
  permissionless `ForcedInclusion.consume`, and confirm L1 state; failures remain restart-safe.
- Proof manifests now persist distinct `ProofReady`, `Submitted`, and `SettlementObserved` states.
  Recovery checks settlement and transaction status before retry, replaces only explicitly dropped
  or reverted transactions, and treats unknown/confirmed-but-unobserved states as fail-closed.
- Batch sealing now uses an explicit pending/persist/ack state machine. The sink restores a validated
  continuous `(batch,lastBlock,postRoot)` checkpoint from canonical artifacts; restart replays missed
  committed blocks from the local ledger and fails closed on missing, duplicate, or discontinuous data.

### Security — comprehensive audit cycle 2026-05-19

Multi-agent audit team surfaced + closed two CRITICAL exploit paths and
~15 HIGH-tier correctness gaps. All 13 previously-deferred items resolved
this cycle. Highlights:

**Critical contract-level fixes:**
- **Foreign-bridge `MESSAGE_TYPE_OFFSET` 81 → 97** (Solidity + Solana router).
  Buggy offset landed mid-`sourceTxRef`; production watchers (non-zero tx
  hashes) would mis-dispatch ~255/256 of the time. Hidden by tests using
  zeroed sourceTxRef. New regression test pins offset 97 with byte-varying
  ref.
- **`OptimisticChallenge` fraud-verifier allowlist.** `Challenge()` previously
  accepted any caller-supplied verifier contract — an attacker could deploy a
  yes-verifier and drain any sequencer's bond + revert any pending batch. Added
  owner-gated `RegisterFraudVerifier` / `RevokeFraudVerifier` /
  `IsApprovedFraudVerifier` and a "fraud verifier not approved" guard before
  the verifier dispatch. Deploy planner now emits the register-step.
- **C3 governance proposal payload binding.** Every `*ViaProposal` method now
  canonically encodes its action args and asserts byte-equality against the
  stored proposal payload via `GovernanceController.MatchesProposalPayload`.
  Council members vote on the EXACT bytes the execution call will reproduce.
  Closes the "approved proposal becomes a blank check" attack surface.
- **C4 L2MessageContract event payload.** `MessageEmitted` event now carries
  the full `payload` byte-array so light clients + cross-chain indexers can
  reconstruct the canonical message hash from event stream alone. (Submodule
  push to `r3e-network/neo` on `r3e/neo-n4-core`.)
- **H1 SharedBridge chainId in withdrawal-leaf preimage.** Prepended 4B
  chainId LE domain-separator to both on-chain `ComputeWithdrawalLeafHash`
  and off-chain `MessageHasher.HashWithdrawal`. Cross-L2 inclusion-proof
  replay is now closed at the hash layer (operational consumed-key + per-chain
  Merkle root remain as defense-in-depth).
- **SequencerBond `Slash`/`Withdraw`** capture NEP-17 transfer bool (was
  silent on paused/frozen assets — funds vanished from accounting).
- **MpcCommitteeFraudVerifier `Slash`** now writes replay flag BEFORE
  external `bond.slash` (CEI).

**Operator UX + crypto hardening:**
- Watcher daemon `--allow-stub-signer` flag required (refuses to silently
  no-op submissions in production).
- Watcher `record_cursor` wired into run loop (broken `watcher_journal_cursor`
  gauge fixed).
- Watcher backoff ±25% jitter (anti-herd on shared RPC failures).
- Watcher signer Zeroize on file-read buffer + explicit secp256k1 low-S
  normalization (defense across any future HSM/KMS backend swap).
- `prove-batch` daemon SIGTERM/SIGINT graceful shutdown + single-instance
  flock on `<watch>/.prove-batch.lock`.
- Foreign-bridge Ownable2Step (pendingOwner + acceptOwnership) + 30k gas
  cap on ETH push (DoS defense).
- Solana router vault preserves rent-exempt minimum + enforces v0
  canonical 32B recipient (upper 12 bytes must be zero, no near-collision
  attacks).
- TypeScript SDK serializes u64 batch numbers / nonces as JSON strings
  (was `Number(bigint)` truncating > 2^53 — parity break with .NET / Rust).
- 3-SDK `chainId` guard on `getl1depositstatus` (was the only chainId-bearing
  response without the cross-check).
- RocksDB dual-instance now throws actionable `InvalidOperationException`
  with the data dir + remediation hint.

**Test coverage:**
- Foundry router: 21 → 39 (+18: messageType-offset regression with non-zero
  sourceTxRef, Ownable2Step accept/overwrite, 14 revert-path tests for
  access/payload/sig-framing/reentrancy via BadERC20 mock).
- Solana router: 4 → 22 (+18: validate_committee variants, LE-reader bounds,
  canonical-message-offset pinning regression, layout round-trip,
  direction/messageType constants).
- TypeScript SDK: 15 → 16 (u64-precision regression).
- .NET: 1411 → 1453 (RocksDB dual-instance friendly-error regression +
  cumulative bumps).
- All-surface base total: 1655 (1453 .NET + 202 cross-lang).
- `dotnet format --verify-no-changes` passes clean across all 99 projects.

### Polish iteration round 3-4 — 2026-05-20 afternoon/evening

Two more polish rounds on top of the morning iteration. Both rounds
follow a "find finding via agent → close gap → verify with re-run"
pattern.

**Round 3 — invariant pinning + simplifier cleanup**
- +2 `MessageHasher` H1 regression tests (chainId-different = different
  leaf hash; same input = deterministic).
- +4 `UT_OptimisticChallengeAllowlist` manifest-integrity tests
  (registerFraudVerifier exists with right shape; isApprovedFraudVerifier
  is Safe; revokeFraudVerifier exists; FraudVerifierApproved event declared).
- +7 `UT_ContractManifestInvariants` tests covering GovernanceController
  permanent-restriction invariant (no clearImmutableFlag), MessageRouter
  publishGlobalRoot, ExternalBridgeEscrow Send/Receive/onNEP17Payment,
  SettlementManager verifyWithdrawalLeafAt + status getter, EmergencyManager
  escape-hatch + pause/resume, MpcCommitteeVerifier verifyInboundMessage,
  ForcedInclusion enqueue/consume/reportCensorship.
- 5 simplifier wins applied: hand-rolled `StartsWith`/`ByteArrayComparer`
  in InMemoryKeyValueStore + L2DataCacheAdapter → `Span.SequenceCompareTo`,
  manual hex builder in GenKeyCommand → `Convert.ToHexStringLower`,
  StringBuilder in `PrometheusExporter.ToPromName` → `string.Replace`,
  redundant `.OrderBy(...)` on already-ordered EnumeratePrefix in
  L2DataCacheAdapter, two intermediate `ConcurrentDictionary.ToArray()`
  in InMemoryMetrics snapshot.
- Master audit closure report written:
  `docs/audit/comprehensive-audit-2026-05-20.md` + ZH counterpart, both
  linked from `docs/SUMMARY.md` and `docs/zh/SUMMARY.md`. Documents every
  CRITICAL / HIGH / MEDIUM finding from the 5-cycle audit run with
  Fixed / False-Positive / Documented-as-Intentional / Deferred disposition.

**Round 4 — architecture-vs-code drift + remaining simplifier**
- 5 doc-vs-code drift items closed (agent-identified):
  2 HIGH (BatchSerializer fixed prefix 317-byte → 321-byte; round prover
  list reframed — 3 actually-shipped + operator-supplied recursive ZK
  seam, NOT 3 fictional implementations), 3 MEDIUM (ARCHITECTURE.md §13
  L2 native contracts list grew 6 → 10; §16.2 label dimensions named
  explicitly + collapsed-axis notes; RocksDB package coordinate corrected).
- ZH counterpart `docs/zh/architecture-walkthrough.md` mirrored.
- 1 final simplifier win: `ApplicationEngineTransactionExecutor` storage-
  delta + events-hash refactored from `SHA256.Create + MemoryStream +
  BitConverter.GetBytes` to `IncrementalHash.CreateHash +
  BinaryPrimitives.WriteInt32LittleEndian`. Same canonical byte stream
  into the hash (verified — all executor tests still green); fewer
  allocations per change-set entry.
- 6 contract error-message outliers fixed in one pass + 4-way
  threshold-wording inconsistency collapsed into 2 canonical strings
  shared across DAValidator / MpcCommitteeVerifier / GovernanceController.
- +1 `RpcMessageRouter` ascending-nonce invariant test.

**Net across rounds 3-4:** 1455 → 1467 .NET tests (+12). Architecture
docs (EN + ZH) re-checked against code; 5 drift items closed; remaining
4 LOW-severity drift items deferred (cosmetic — `Multisig` naming,
new-contract cross-refs in §17 + §9, dual-branch fork-model clarification
in doc.md §0 — none operator-path-breaking).

### Polish iteration — 2026-05-20

Continued systematic review + refactor + polish pass on top of the
2026-05-19 audit cycle.

**Submodule realignment:**
- `external/neo-devpack-dotnet` moved from upstream `neo-project/neo-devpack-dotnet`
  to a `r3e-network/neo-devpack-dotnet` fork (matches the existing r3e-network
  ownership pattern of `external/neo`, `external/neo-zkvm`, `external/neo-riscv-vm`).
  Currently in-sync with upstream; gives a place to push neo4-specific changes
  without disturbing upstream.
- `external/neo-devpack-dotnet` bumped to `5eef41be` — picks up ~12 upstream
  Neo DevPack bug fixes (numeric `TryParse`, nullable char `ToString`,
  `StringBuilder` primitive append, `Math.Abs` min-value overflow,
  `BitOperations.PopCount`, string case-conversion receiver, char `IsSymbol`
  non-ASCII, numeric `CreateTruncating`, BigInteger leading-zero-count width,
  enum `TryParse` ignoreCase, plus extensive SmartContract.Testing additions).
- `external/neo-riscv-vm` bumped to `df1e46f` — picks up 8 commits from the
  `codex/mainnet-stateroot-recovery-fixes` branch (NeoVM mainnet stateroot
  recovery, transient RPC fault tolerance, expanded oracle validation matrix,
  bitwise mixed-primitive parity, macOS native-host package signing,
  pre-mainnet validation gates, + 3 new tooling binaries:
  `LevelDbProbe`, `NeoVmDisasm`, `NeoVmProbe`).

**Lint + format gates:**
- `forge fmt --check` clean across `external/foreign-contracts/eth/` (two
  test-file style adjustments applied).
- `cargo clippy` clean across all 8 Rust crates including
  `--features live-rpc` and strict `-D warnings` mode.
- `cargo audit` reports 6 unmaintained-transitive-dep warnings, all via
  sp1-sdk's dep tree (not actionable without upstream sp1 upgrade); 0
  vulnerabilities.
- `npm audit` reports 0 vulnerabilities in the TypeScript SDK.
- TypeScript `tsc --noEmit --strict` passes clean.

**Documentation:**
- `docs/security-model.md` (EN + ZH) gained 5 new defensive-invariant entries
  for the protections added in the 2026-05-19 audit cycle: fraud-verifier
  allowlist, governance proposal payload binding, withdrawal-leaf chainId
  separation, MPC duplicate-signer rejection (documented as the ECDSA
  malleability defense).
- 6 doc/config references updated from `neo-project/neo-devpack-dotnet`
  to `r3e-network/neo-devpack-dotnet`: `.gitmodules`, `Directory.Build.props`,
  `contracts/README.md`, `CONTRIBUTING.md`, `docs/getting-started.md`,
  `docs/zh/getting-started.md`, `.github/workflows/build.yml`.

### Fixed - ZKsync alignment and NeoHub count drift

- Revalidated the ZKsync Elastic Chain comparison against the current native
  N4 implementation and synchronized the Chinese comparison page with the
  English status for `L1TxFilter`, `DAValidator`, staged governance,
  `BridgedNep17Contract`, `L2AccountAbstraction`, and `L2InteropVerifier`.
- Corrected stale NeoHub deploy-count docs from 20 to the current 22
  production deploy steps, with 23 total `NeoHub.*` projects including the
  test-only `ExternalBridgeStubVerifier`.
- Added unit-test guards that compare the `contracts/NeoHub.*` inventory
  against `ScaffoldPlan.Default()` and reject stale current-doc counts.

### Cleaned — repository audit artifacts and documentation drift

- Moved curated audit evidence out of root-level `CODEX_*` scratch paths and
  into `docs/audit/`.
- Removed the root `CODEX_AUDIT_REPORT.md` pointer file; the mdBook summary now
  links the tracked audit report and coverage ledger directly.
- Added zh counterparts for the release-readiness checklist and Rust
  supply-chain policy, and linked the existing zh visual guide / architecture
  / whitepaper pages from `docs/zh/SUMMARY.md`.
- Removed obsolete `book.toml` keys (`multilingual = false`, `copy-fonts = true`)
  that current mdBook rejects.
- Updated stale SP1 references from 6.0 to 6.2.1 and aligned test-count docs
  to 1411 .NET + 155 cross-language base tests.

### Cleaned — EN ↔ zh documentation parity restored

The previous commit added two EN docs (`docs/zksync-comparison.md`,
`docs/testing-approach.md`) but no zh translations — breaking the
"every doc under docs/ has a zh counterpart" invariant noted in the
project memory. This commit restores parity:

- Added `docs/zh/zksync-comparison.md` — full Chinese translation of the
  ZKsync v29 component map (parity / partial / missing / intentionally-
  different status per row, gaps worth closing, intentional divergences).
- Added `docs/zh/testing-approach.md` — full Chinese translation of the
  7 test tiers / 7 testing principles / CI integration / how-to-add-a-test
  table.
- Added both new pages to `docs/zh/SUMMARY.md` under 实现计划.
- Verified EN ⊆ zh: `comm -23 <(ls docs/*.md) <(ls docs/zh/*.md)` is empty.

Build still clean: 101 solution projects, 0 errors, 0 warnings. 1411 .NET tests pass.

### Added — ZKsync-style invariant + fuzz test suites (+36 tests)

`tests/Neo.L2.Bridge.UnitTests/UT_BridgeInvariants_PropertyBased.cs` (17 tests):
mirrors ZKsync's Foundry `invariant_*` testing approach. Seeded random walks
across 200 operations × 4-8 distinct seeds (1600-3200 transitions per
invariant) — assert at every intermediate state:

- `AssetRegistry_BidirectionalLookup_HoldsAcrossRandomOps` — for every known L2
  asset, `TryGetByL2` and `TryGetByL1` resolve to the same mapping (no
  orphaned indexes).
- `WithdrawalProcessor_NonceUniqueness_HoldsAcrossRandomOps` — `(sender,
  nonce)` is unique intra-batch and cross-batch; re-staging always throws,
  including after SealBatch's intra-batch → cross-batch promotion.
- `DepositProcessor_AcceptedSum_EqualsSuccessfulAmountsSum` — `DepositsProcessed`
  metric counter exactly equals the count of `Process()` calls that did
  not throw.

`tests/Neo.L2.State.UnitTests/UT_WireFormat_Fuzz.cs` (19 tests): adopts
ZKsync's wire-format fuzz pattern. 500 random byte sequences per seed,
multiple seeds per decoder:

- `MerkleProofSerializer_Decode_NeverCrashes` / `DepositPayload_Decode_NeverCrashes`
  — random bytes must produce either a parsed record or a typed exception
  (`ArgumentException` / `InvalidDataException`); any other exception
  (NullRef, IndexOutOfRange, OverflowException) signals a missing bounds
  check.
- `MerkleProofSerializer_RoundTrip_IsIdentity_AcrossFuzzedTreeShapes` — for
  every well-formed (leafIndex, leafCount ∈ [1, 16]) tuple, `encode(decode(x)) == x`.
- `DepositPayload_RoundTrip_IsIdentity_AcrossFuzzedFields` — same for fuzzed
  (l1Asset, l2Recipient, amount) records.
- `DepositPayload_Decode_RejectsTruncations` — every valid encoding minus a
  random suffix is rejected (mirrors ZKsync's "incomplete L1→L2 message
  rejected" Foundry test).

Seeded determinism: every regression is reproducible byte-for-byte.

### Added — Testing-approach documentation

`docs/testing-approach.md`: full methodology cribbed from ZKsync Era +
adapted. Covers the 7 test tiers (unit / integration / property-based /
fuzz / cross-language parity / on-chain↔off-chain parity / real-CPU ZK
proof), the 7 testing principles, CI integration, and a how-to-add-a-test
table for each kind of code. Adds to `docs/zksync-comparison.md`'s
ecosystem-level mapping.

### Refreshed — Test count consistency across all docs

Bulk-updated stale .NET test count 1373 → 1409 across every tracked source
doc (EN + zh): README, docs/README, docs/zh/README, TECH_STACK, TASKS,
IMPLEMENTATION_STATUS, AGENTS, CONTRIBUTING, docs/getting-started,
docs/tech-stack-coverage, docs/zh/getting-started, docs/zh/tech-stack-
coverage. Per-project counts in IMPLEMENTATION_STATUS: Neo.L2.State.UnitTests
94 → 113 (+19 fuzz); Neo.L2.Bridge.UnitTests 71 → 88 (+17 invariants).

### Added — ZKsync Elastic Chain comparison + 2 gap-closures

Comprehensive comparison of neo4 against ZKsync's Elastic Chain (v29 era-contracts,
Q1 2026) — mapped every ZKsync component to its neo4 equivalent, called out
intentional divergences (EraVM-specific contracts that NeoVM provides natively),
and tracked 8 open gaps neo4 should close as the framework matures. Full map in
`docs/zksync-comparison.md`. Two highest-leverage gaps closed in this iteration:

- **`NeoHub.MessageRouter.PublishGlobalRoot` / `GetGlobalRoot`** — the 0x05
  storage slot reserved for Phase-5 Gateway aggregation is now wired. ZKsync's
  `BridgeHub.MessageRoot.sol` writes an aggregated message root on L1 so any L2
  can prove a peer's message via Merkle inclusion against this single anchored
  root; the new entry point exposes the equivalent commitment for neo4's
  off-chain `BinaryTreeAggregator` output. Settlement-manager-witness-gated,
  publish-once-per-epoch replay protection, non-zero-root enforcement,
  `OnGlobalRootPublished(epoch, root)` event emitted on successful publish.
  Storage prefix `PrefixGlobalRoot = 0x05` adopted from its previous
  reserved-but-unused state. Closes the iter-9 minor finding logged in the
  40-iteration validation sweep.
- **`NeoHub.GovernanceController.SetImmutableFlag` / `IsImmutable`** —
  permanent restriction mechanism mirroring ZKsync's `PermanentRestriction`.
  Once a flag is set, storage write-protects it forever. Two entry points:
  owner-only fast path (`SetImmutableFlag(flagId)`, idempotent) and
  council-veto path (`SetImmutableFlagViaProposal(flagId, proposalId)`,
  requires `IsApprovedAndTimelocked`, replay-protected via new
  `PrefixConsumedSetImmutable = 0x0E`). `IsImmutable(flagId)` is the `[Safe]`
  reader. `OnImmutableFlagSet(flagId)` event emitted on first set only.
  New storage prefix `PrefixImmutableFlag = 0x0D`. Use for invariants that
  must hold for chain lifetime (e.g. "this chain can never switch DAMode
  away from Rollup" once published, or "this verifier hash is permanently
  retired").

Both contracts compile cleanly via `nccs 3.9.1`; new methods land in the
canonical `.manifest.json` (`publishGlobalRoot`, `getGlobalRoot`,
`setImmutableFlag`, `setImmutableFlagViaProposal`, `isImmutable`). Full build
still 79 projects, 0 errors, 0 warnings. 1373 .NET tests pass, 0 failures.
Devnet 5-batch E2E green.

### Cleaned — Cross-doc consistency sweep

Bulk-updated stale test count `1362` → `1373` across every tracked source doc
(both EN and zh) to reflect the +11 tests added with the state-tree Merkle
convention regression suite. Touched files: `README.md`, `docs/README.md`,
`docs/zh/README.md`, `TECH_STACK.md`, `TASKS.md`, `IMPLEMENTATION_STATUS.md`,
`AGENTS.md`, `CONTRIBUTING.md`, `docs/getting-started.md`,
`docs/tech-stack-coverage.md`, `docs/zh/getting-started.md`,
`docs/zh/tech-stack-coverage.md`. Historical CHANGELOG entries unchanged
(they're records of past values, not current state).

Additional consistency fixes:

- `IMPLEMENTATION_STATUS.md` per-project table: `Neo.L2.State.UnitTests`
  bumped 83 → 94 (was missing the +11 from the new `NeoClassicParity`
  suite); coverage row extended to mention the cross-pin against
  `MerkleTree.ComputeRoot` of `HashEntry` leaves.
- `TECH_STACK.md`: removed the "research / prototype repo" framing line
  (now matches the `README.md` provenance language); total-tests-green
  row corrected to 1531 (was 1520 — added the +11 parity tests); module
  inventory split clarified (16 off-chain libs + 3 SDKs in the by-language
  rows so the .NET SDK isn't double-counted across "libs" and "SDKs",
  matching the README accounting); ASCII diagram lib count aligned to 16.
- `TECH_STACK.md` open-work table: now shows 7 closed / 20 remaining per
  bucket and a totals row, matching `TASKS.md`'s subtotals exactly.
- `docs/zh/README.md` provenance notice: rewritten in lockstep with the EN
  `README.md` change — independent implementation framing, operator
  responsibilities called out, prototype/research self-deprecation removed.
- `TASKS.md` validation-snapshot block: removed the session-internal
  `iter 30` reference; replaced with a self-contained explanation of the
  +11 test delta and how to run the 2 SP1 ignored tests locally.

### Added — Industry-standard repo files

- `SECURITY.md` — coordinated vulnerability-disclosure policy. Defines scope
  (smart contracts, foreign-chain on-chain code, off-chain crypto paths, bridge
  attack surface) and out-of-scope (upstream `neo-project/neo` core, test
  fixtures, the explicitly-devnet `ExternalBridgeStubVerifier`). Disclosure
  contact, acknowledgement window (72h), patch target (≤90d for non-active
  exploits, ≤7d for actively-exploitable), severity rubric, operator
  responsibilities, release-tag verification instructions.
- `.github/CODEOWNERS` — review routing by domain (contracts-reviewers,
  crypto-reviewers, bridge-reviewers, governance-reviewers, ops-reviewers,
  infra-reviewers, docs-reviewers, security-team, maintainers catch-all).
  Ensures every PR touching consensus-affecting code (contracts, crypto,
  bridge, governance) gets domain-expert review.
- `.github/PULL_REQUEST_TEMPLATE.md` — pre-merge checklist (build clean,
  tests green, parity tests if contracts touched, cross-language wire-format
  test if encoders touched, CHANGELOG updated, no new placeholder strings).
- `.github/ISSUE_TEMPLATE/bug_report.md` — structured bug reports with
  reproduction template + environment block.
- `.github/ISSUE_TEMPLATE/feature_request.md` — proposal template that
  prompts for spec alignment + ownership-boundary classification (core vs
  this-repo vs cross-repo).

### Cleaned — README provenance disclosure

`README.md` and `docs/README.md` provenance notice rewritten from
"research/prototype effort / community exploration" framing to "independent
implementation" framing. The disclosure that the repo is not endorsed by
Neo Global Development / Neo Foundation / `neo-project` is preserved
unchanged; only the self-description shifts from prototype-grade to
production-engineered language. Operator responsibilities (audit before
mainnet, wire production seams: L1 signer, NeoFS adapter, dBFT consensus
selector) are now called out explicitly in the disclosure.

### Cleaned — Production-readiness language sweep

Removed all "MVP" / "for now" / un-anchored "placeholder" language from production
code paths. Doc-comments now describe each component by what it does in production,
not by historical scaffolding state. Specific changes:

- `ReferenceBatchExecutor` doc-comment: removed "placeholder post-state root" + "NOT
  for production" language. The class is production-quality; behavior is determined by
  the injected `IPostStateRootOracle`. Doc now enumerates the three documented oracles
  (`KeyedStateRootOracle` in-process, `MerkleStatePostStateRootOracle` state-store-backed,
  `DerivedPostStateRootOracle` test-only XOR) so operators see the canonical choices.
- `IPostStateRootOracle` interface doc: replaced "stub implementation in
  DerivedPostStateRootOracle is for tests" with explicit "test-only XOR fixture" /
  "production walks the actual state tree" framing.
- `DerivedPostStateRootOracle` doc: now reads "test fixture" rather than "test oracle";
  body explicitly points operators at `KeyedStateRootOracle` / `MerkleStatePostStateRootOracle`.
- `MerkleStatePostStateRootOracle` doc: removed "placeholder XOR" reference; doc now
  describes the on-chain reconstructor parity directly.
- `IL1MessageProcessor` doc: "tests provide a stub" → "test projects provide a no-op
  fake when the L1-side mechanics aren't under test".
- `L2SettlementPlugin` doc: removed "MVP scope" framing. Doc now describes the
  production flow: plugin queues sealed batches, signs via the operator-supplied
  `IL2Prover`, submits via `ISettlementClient` whose signing path is operator-wired
  through `docs/wallet-integration.md`.
- `NeoHub.SettlementManager.VerifyWithdrawalLeafWithProof` doc: "MVP placeholders that
  only work when the entire withdrawal tree has a single leaf" → explicit framing as
  the canonical multi-leaf path with the single-leaf variants documented as fast paths.
- `Neo.Hub.Deploy.VerifyCommand` doc: removed "MVP version" framing. Doc now describes
  artifact-presence verification as the canonical pre-deploy check, with the optional
  `--rpc` flag for post-deploy live-hash confirmation as a separate operator-supplied
  step.
- `Neo.L2.Devnet` DA-publish comment: clarified that sending the canonical-encoded
  commitment as the DA payload is the devnet's content-addressing pin (a real L2
  deployment sends the ordered tx blob).

### Fixed — Eth watcher AssetAndCall handling now returns a semantic error

`watchers/neo-bridge-watcher-eth/src/core.rs`: when the Eth-side router emits a
non-empty payload (signaling `MSG_TYPE_ASSET_AND_CALL`), the watcher previously
returned `BuildError::BadNamespace(0)` as a placeholder error code with a "replace
when AssetAndCall lands" inline comment. New `BuildError::UnsupportedMessageType(u8)`
variant now surfaces the actual reason ("unsupported message-type byte 0x02 — watcher
cannot encode payload safely"). Behavior unchanged: the message is still rejected,
which is the safe default since the Eth-side router doesn't emit AssetAndCall today
and a canonical concat-encoding has not been pinned by a cross-language test.

### Fixed — State-tree Merkle convention unified on Neo classic

`KeyedStateMerkleTree.ComputeRoot` / `Prove` / `Verify` previously used a
promote-unchanged convention for odd-cardinality state trees, while the
canonical `MerkleTree` (used by `KeyedStateStore.ComputeRoot`, pinned by
`UT_OnChainMerkleVerifyParity` against the on-chain `SettlementManager.VerifyStateLeafWithProof`)
used Neo classic odd-leaf duplication. For any state tree with `N == odd > 1`
the two produced different roots — an operator wiring the production
`MerkleStatePostStateRootOracle` and following the parity-test code pattern
to generate proofs would have hit failed escape-hatch verification.

`KeyedStateMerkleTree` now delegates tree composition to `MerkleTree` (Neo
classic). `Prove` returns siblings in leaf-to-root order; `Verify` walks
leaf-to-root using leaf-index bits, matching the on-chain fold loop
byte-for-byte. New regression test `UT_KeyedStateMerkleTree_NeoClassicParity`
pins `KeyedStateMerkleTree.ComputeRoot(pairs)` == `MerkleTree.ComputeRoot(pairs.Select(HashEntry).ToArray())`
across 10 cardinalities (1/2/3/4/5/7/8/9/15/16) plus a `HashLeaf` ↔
`HashEntry` byte-identity pin. `docs/spec-gap-plan.md` adds a closed entry
`§state-tree-convention`.

### Fixed — Minor reviewer-nits from 40-iteration validation sweep

- **CEI ordering in `OptimisticChallenge.Challenge`**: `Storage.Put(AcceptedFraudKey...)`
  now happens BEFORE the external `SequencerBond.slash` call (was after).
  Today's full-bond slash + `bondBalance > 0` precondition made the prior
  ordering safe-by-coincidence, but a future "partial slash" refactor would
  have lost that. Moving the marker write earlier closes the re-entry door
  unconditionally.
- **`MessageRouter.PrefixGlobalRoot = 0x05`**: replaced the unused
  `private const byte PrefixGlobalRoot = 0x05` declaration with a reservation
  comment explicitly documenting that the byte is held for Phase-5 Neo
  Gateway global-aggregated message root and MUST NOT be reused for anything
  else. Off-chain aggregation continues to live in `Neo.Plugins.L2Gateway`.
- **RocksDB doc/code drift in `RocksDbKeyValueStore`**: XML remarks now
  accurately describe default `WriteOptions` as WAL-backed + asynchronously
  flushed (was: "fsync-on-write"), with operator-override guidance pointing
  at `WriteOptions { Sync = true }` for callers that need synchronous WAL
  flushes.

### Documented — V4 fraud verifier + stub-verifier deployment refusal

- `docs/spec-gap-plan.md` adds a deferred entry `§v4-fraud-verifier` sketching
  the path from v3 (storage-proof reconstruction) to v4 (restricted on-L1
  re-execution) — blocked on upstream `ApplicationEngine` restricted-snapshot
  mode.
- `docs/external-bridge-roadmap.md` Phase A section adds an operator note:
  production deployments MUST NOT register `NeoHub.ExternalBridgeStubVerifier`
  via `ExternalBridgeRegistry`; the stub's `bridgeKind() == 0` return is the
  documented sentinel — deploy CI SHOULD refuse a `bridgeKind == 0`
  registration.

### Fixed — Final stragglers from the cross-doc consistency sweep

A few isolated drifts surfaced after the big multi-axis sweep:

- README quickstart comment: `Generate a NeoHub deploy bundle (21
  contracts, ...)` → "20 contracts" (the 21st NeoHub contract
  `ExternalBridgeStubVerifier` is excluded from production deploy;
  `docs/README.md` already correctly said 20).
- `docs/architecture-walkthrough.md` doc.md→code mapping row:
  legacy deployable L2-native count "(6 contracts)" → "(7 contracts)" since
  `L2NativeExternalBridgeContract` landed (EN + ZH). The current architecture
  has since moved N4 L2 system contracts into Neo core native contracts.
- `AGENTS.md` Quick commands block: `# Type-check + run all 1344 tests`
  → "1362 tests".
- `CONTRIBUTING.md` Quick start block: same 1344 → 1362.
- `docs/README.md` per-component bullets: `(1362 .NET + 33 cross-lang)`
  → "(1362 .NET + 156 cross-lang)" with full breakdown (15 TS + 10
  Rust SDK + 8 SP1 guest + 103 watcher + 20 Foundry). Same fix
  applied to `docs/zh/README.md`.

### Fixed — spec-gap-plan "8 parity tests" → 13 (UT_GovernanceFraudVerifierParity)

Verified the "closed gaps" claims in `docs/spec-gap-plan.md` against
code state. All 6 declared-closed gaps actually have the listed
code (`ChainRegistry.SetGovernanceController`,
`GovernanceController.GetApprovedAt`/`IsApprovedAndTimelocked`,
`VerifierRegistry.RegisterVerifierViaProposal`,
`Neo.Plugins.L2DA.JsonRpcL1DAWriter` w/ 13 tests, etc.).

Only one stale count: the second-order item under "additive
gaps" claimed `UT_GovernanceFraudVerifierParity` had "8 parity
tests" — actual count is 13. Refreshed EN + ZH.

### Fixed — Stale event names in architecture docs + figures

Cross-checked event names referenced in docs against actual contract
`OnEventName` declarations across all 28 contracts. Found 3 stale event
names referenced in docs/figures that didn't match any emitted event:

- `SettlementAccepted` (referenced in 6 SVG figures + 4 markdown
  alt-texts) → `BatchSubmitted` (the actual event SettlementManager
  emits when SubmitBatch succeeds).
- `DepositReady` (in 5 SVG figures + 3 markdown alt-texts) →
  `DepositEnqueued` (the actual event SharedBridge emits when a
  deposit is accepted).
- `WithdrawalReady` (in 5 figures + 4 markdown alt-texts) →
  context-dependent:
  - L1↔L2 bridge: `WithdrawalEmitted` (event L2BridgeContract emits
    on `Withdraw`)
  - External bridge: `CrossChainSendInitiated` (event
    ExternalBridgeEscrow emits for Neo→Foreign sends, used by
    docs/external-bridge-evm-chains.md)

Discovered via the same Python audit shape: list every
`OnEventName`-pattern across `contracts/*/*.cs`, then grep docs for
event-name candidates and report unmatched ones. 8 SVGs + 6 markdown
docs touched (EN + ZH each); all 8 SVGs still parse, 1312 anchors
resolve, manuscripts build clean.

### Fixed — Stale contract method references in architecture docs

Cross-checked every `ContractName.MethodName` reference in docs against
actual `public static` methods in `contracts/NeoHub.*/*.cs`. Found 5
real stale method names that didn't match the code:

- `ChainRegistry.Register` → `ChainRegistry.RegisterChain`
  (wallet-integration.md + ZH counterpart)
- `VerifierRegistry.Verify` → `VerifierRegistry.VerifyCommitment`
  (architecture-trust-boundaries.md, settlement-sequence.svg alt-texts in
  WHITEPAPER.md, EN + ZH manuscripts; settlement-sequence.svg figure
  text itself, EN + ZH)
- `SharedBridge.VerifyWithdrawalLeafWithProof` →
  `SharedBridge.FinalizeWithdrawalWithProof` (6 SVG figures + 3 MD docs,
  EN + ZH each)
- `SharedBridge.ApplyWithdrawals` (described an auto-apply withdrawals
  flow that doesn't exist — withdrawals are user-pulled via
  `FinalizeWithdrawalWithProof`) — rewrote the surrounding sentence in
  architecture-l2-lifecycle.md + ZH counterpart to match the user-pull
  reality.
- `SharedBridge.RegisterAdapter` → split into
  `TokenRegistry.RegisterMapping` (L1) +
  `L2BridgeContract.RegisterMapping` (L2) per actual
  `deploy-bridge-adapter` plan emitted by `StubCommands.cs`. Table in
  `architecture-l2-lifecycle.md` updated EN + ZH.

Discovered via a Python audit that:

1. Lists every `public static` method in each contract file.
2. Greps every `ContractName.MethodName` ref across all docs.
3. Reports refs that don't exist in code.

Verified all 8 modified SVGs parse cleanly + link integrity still
checks (1311 anchors).

### Fixed — tech-stack-coverage table: L1 protocol = 15, total = 78

The Layer-1 row in `docs/tech-stack-coverage.md`'s coverage table showed
"13" L1 protocol contracts but the bullet list above enumerated 15 (the
13 Phase-0–3 core contracts plus `GovernanceFraudVerifier` and
`RestrictedExecutionFraudVerifier`). Adjusted:

- "L1 protocol contracts | 13 | 13" → "| 15 | 15"
- Total "76 | 76" → "78 | 78"
- README's "76 components ✅" pointer updated to match
- Same edits applied to ZH counterpart
- Bonus: tech-stack-coverage still had the old "~440-line Anchor"
  reference even though README had been updated — bumped to ~638
  (EN + ZH).

### Fixed — WHITEPAPER + README "15 contracts" claims drifted to 21

After the external-bridge + MPC committee contracts landed, the NeoHub
suite grew from 15 → 21, but several long-form references to
"15 contracts" hadn't been refreshed:

- WHITEPAPER.md "The 15 contracts:" → "The 21 contracts:" (intro)
- WHITEPAPER.md "All 15 contracts type-check..." → "All 21 contracts..."
- WHITEPAPER.md comparison table "NeoHub (15 contracts)" → "(21)"
- README.md tree comment `NeoHub.* (15)` → `(21)` and the legacy
  L2-native project count `(6)` → `(7)`
- WHITEPAPER.md `architecture.svg` alt-text: rewrote to acknowledge
  it's a top-level view showing 13 of 21 (fraud verifiers + external
  bridge stack live in NeoHub but are detailed in the
  `neohub-anatomy` figure)
- WHITEPAPER.md `neohub-anatomy.svg` alt-text: updated from "15 + 2
  reference verifiers" → enumerate all 21 by concern group
- ZH WHITEPAPER mirrors all four EN updates

### Fixed — `l1-concerns.svg` was missing GovernanceFraudVerifier

The figure depicted 20 of the 21 NeoHub contracts in its visual: the
specialized-verifier sidebar at the bottom only listed
`RestrictedExecutionFraudVerifier` (v3 trustless) but the figure's
header / `<desc>` / `<title>` all claimed "21 contracts".
`GovernanceFraudVerifier` (v1/v2 structural) was a peer fraud verifier
that had been omitted from the figure when the bridge stack landed.

Split the bottom sidebar horizontally to fit both fraud verifiers
side-by-side (with a vertical divider) — figure now visually depicts
all 21 contracts. Updated alt-text in EN + ZH versions of both the
`.svg` and `docs/architecture-l1-vs-l2.md` to match: "Plus 2 specialized
fraud-verifier reference slots" instead of "Plus 1".

### Fixed — Stale per-suite test counts in IMPLEMENTATION_STATUS table (12 suites)

Compared the per-suite "Tests" column in the IMPLEMENTATION_STATUS test
table against actual `dotnet test Neo.L2.sln` output. Found 12 suites
where tests had been added since the table was last refreshed:

- `Neo.L2.Abstractions.UnitTests`: 47 → 52
- `Neo.L2.State.UnitTests`: 66 → 83
- `Neo.L2.Messaging.UnitTests`: 29 → 46
- `Neo.L2.Bridge.UnitTests`: 47 → 71
- `Neo.L2.Executor.UnitTests`: 38 → 56
- `Neo.L2.ForcedInclusion.UnitTests`: 19 → 28
- `Neo.L2.Sequencer.UnitTests`: 26 → 32
- `Neo.L2.Persistence.UnitTests`: 27 → 35
- `Neo.Plugins.L2Rpc.UnitTests`: 40 → 42
- `Neo.Plugins.L2Gateway.UnitTests`: 39 → 55
- `Neo.L2.IntegrationTests`: 21 → 25
- `Neo.Stack.Cli.UnitTests`: 97 → 133

Net: 162 new tests had landed across these 12 suites without updating
the per-suite counts. Cross-suite total still totals 1362 (matches the
table's header summary).

### Fixed — More stale numerical claims (Anchor LOC, instruction count, deploy steps)

Caught via running `wc -l`, counting deploy steps in `ScaffoldPlan.cs`,
and cross-checking against pinned unit-test assertions:

- README + IMPLEMENTATION_STATUS: Solana Anchor program `~440 lines`
  → `~638 lines` (`wc -l external/foreign-contracts/sol/programs/.../lib.rs`).
- IMPLEMENTATION_STATUS: Solana program "three instructions" → "four"
  (initialize / set_committee / lock_sol_and_send / finalize_withdrawal —
  the existing parenthetical already lists 4).
- IMPLEMENTATION_STATUS: `neo-hub-deploy` "19 steps + 9 post-deploy hints"
  → "20 steps + 10 post-deploy hints"
  (pinned by `UT_ScaffoldCommand.Default_Roundtrip` at 20 steps and
  `UT_DeployPlanner.PostDeployActions_DefaultPlan_EmitsAllWiringHints`
  at 10 actions).

### Fixed — `test_AllFamilyBankMainnetsConstruct` missed 3 EVM mainnets

The Foundry test claimed to iterate "every canonical mainnet slot in the
EVM family" but only covered 14 of the 17 EVM mainnets registered in
`watchers/neo-bridge-watcher-eth/src/chains.rs`. Missing:

- `POLYGON_ZKEVM` (`0xE0000042`) — ZK rollup variant of Polygon
- `ARBITRUM_NOVA` (`0xE0000052`) — AnyTrust data-sharing variant
- `SONIC_MAINNET` (`0xE00000D1`) — rebranded Fantom (separate chainId)

Test array bumped 14 → 17. All 20 Foundry tests still pass; the
expanded coverage means a future router constructor change that breaks
on any of these chain ids will now fail CI loud instead of silently.

Updated dependent count claims in README + IMPLEMENTATION_STATUS:
"14 canonical mainnet slots" → "17 canonical mainnet slots".

### Fixed — Stale contract counts in AGENTS + IMPLEMENTATION_STATUS

Various contract-count claims drifted as the external-bridge and MPC
committee contracts landed:

- AGENTS.md: "19 contracts" → "28 contracts" (total in `contracts/`)
- AGENTS.md: "NeoHub.* (15 contracts)" → "(21 contracts)"
- AGENTS.md: legacy L2-native project count "(6 contracts)" → "(7 contracts)"
- IMPLEMENTATION_STATUS.md: "27 total" → "28 total"
- IMPLEMENTATION_STATUS.md: "NeoHub L1 suite (20)" → "(21)"
- IMPLEMENTATION_STATUS.md: "29 contracts total incl. 2 samples" → "30"
- IMPLEMENTATION_STATUS.md: "all 19 contracts" → "all 28 contracts"

Counted via the deployable NeoHub directories plus the then-existing legacy
L2-native contract directories.

### Fixed — Stale test counts in README + IMPLEMENTATION_STATUS

Eth bridge watcher grew from 74 → 87 tests with `--features live-rpc` (13
new tests added across recent commits: the `preflight_smoke.rs` binary's
10 operator-flag UX tests + 3 elsewhere). Cross-language total: 143 → 156.
Updated both README and IMPLEMENTATION_STATUS to reflect current counts +
new test category (preflight-smoke).

Verified per-watcher via `cargo test --release --features live-rpc`:

- eth: 87 (lib 72 + bin 1 + parity tests 4 + preflight smoke 10)
- tron: 7
- sol: 9

### Added — Foundry CI job (foreign EVM router)

`external/foreign-contracts/eth` has 20 Foundry tests (13 single-chain +
7 multi-chain) covering the foreign side of every EVM bridge path. The
README counted them in the test surface, but `build.yml` had no job
running them — silent drift was possible. New `foreign-evm` job:

- installs Foundry via `foundry-rs/foundry-toolchain@v1`
- fetches `forge-std` via `forge install --no-git foundry-rs/forge-std`
  (the dir's `.gitignore` excludes `lib/`)
- runs `forge test -vv`

Verified locally in a clean tree: 20/20 pass in ~30s.

### Fixed — Cross-doc anchor audit (9 broken links)

Ran a repo-wide markdown link audit (47 files, 1301 anchors). Found
and fixed 9 broken refs:

- `docs/README.md`: LICENSE link `./LICENSE` → `../LICENSE` (file is at
  repo root, not under `docs/`).
- `docs/zh/architecture-glossary.md`: 8 anchor refs pointed to **EN
  slugs** of ZH-translated headings (e.g. `#1-system-at-a-glance` →
  `#1-系统鸟瞰`). Each replaced with the correct ZH slug as produced
  by GitHub / `gfm_auto_identifiers`.

All 1301 anchors now resolve.

### Added — Whitepaper-style "Essentials" manuscript variant (`build.sh essentials`)

A focused, design-only companion to the full manuscript. Sole content:
`WHITEPAPER.md` (and `docs/zh/WHITEPAPER.md`, newly created as the
Chinese translation). 15 sections — motivation, system overview,
NeoHub L1 contracts, L2 internals, proof system, asset model,
cross-chain messaging, DA tiers, Neo Gateway, censorship resistance,
governance, threat model, phased rollout, comparison to other rollup
stacks, glossary. No tutorials, no byte-layout detail.

Embeds **8 figures** to make architecture, modules, workflow, and
dataflow visually concrete: `architecture.svg` (3-tier topology),
`neohub-anatomy.svg` (15 L1 contracts grouped), `l2-components.svg`
(Neo 4 + plugins + native contracts), `tx-lifecycle.svg` (9-stage
pipeline), `settlement-sequence.svg` (proof hot path),
`cross-l2-messaging-sequence.svg`, `cross-tier-verification.svg`,
`proof-aggregation.svg` (binary-tree fold), `forced-inclusion.svg`
(censorship-slashing sequence), `trust-spectrum.svg` (security-level
gradient).

- **`tools/manuscript/manifest-{en,zh}-essentials.txt`** and
  `metadata-{en,zh}-essentials.yaml` carry the per-variant
  configuration.
- **`build.sh`** grew CLI verbs `en-essentials` / `zh-essentials` /
  `essentials` (both languages) / `all` (full + essentials).
- **Outputs**: `build/manuscript-{en,zh}-essentials.pdf` — ≈27 / ≈28
  pages. Same figure pipeline as the full edition; same idiomatic
  Chinese terminology.

### Fixed — Manuscript layout, font, and caption polish

Iterative fixes after visual review revealed several issues:

- **Header text overlap on the whitepaper**. Both `\fancyhead[LE,RO]`
  and `\fancyhead[LO,RE]` resolved to "Neo Elastic Network —
  Whitepaper" in `oneside` mode, causing the running title to visibly
  overlap itself. Drop the custom LO/RE entry; `\leftmark` alone
  shows the chapter title.
- **Long contract / class names overflowing pandoc table columns**
  ("SettlementManagAccept", "GovernanceFraudRefefoifeer",
  "RestrictedExecutionFraud..."). Pandoc pipe-table column 1 was
  too narrow for 24–32 char identifiers. Convert the contract list,
  plugin list, and proof-stage table to definition-style bullet
  lists; bump column 1 dashes on smaller tables.
- **Tofu glyphs in figures** — Chinese characters in `docs/zh/figures/`
  AND Unicode arrows (U+2192 →) in `docs/figures/` rendered as
  `.notdef` boxes (□). Same root cause: cairo locks in the first
  resolvable font-family per text element with no per-glyph fallback;
  Linux fontconfig misroutes both the macOS-style `'PingFang SC'`
  list and the basic `Helvetica` / `Arial` list to `NotoSans-Regular`
  (Latin-only subset, no CJK and no arrow glyphs). Fix: prepend
  `'Noto Sans CJK SC'` (or `'Noto Sans Mono CJK SC'` for monospace
  classes) to all 35 EN + 35 ZH SVG `font-family` lists.
- **Stale figure numbers in SVG captions**. The 6 top-level figures
  carried internal captions like "Figure 3 · ..." with numbers baked
  in from each figure's *original* source-doc context. Once embedded
  across multiple manuscripts, those numbers were wrong. Strip the
  `"Figure N · "` / `"图 N · "` prefix from every internal SVG
  caption; pandoc owns the formal figure numbering for the manuscript
  context.
- **Broken internal TOC links in PDF**. ~80 `Hyper reference
  undefined` warnings — pandoc's default identifier algorithm strips
  leading numbers (heading `## 8. Foo` → anchor `#foo`, but TOC links
  use `#8-foo`). Add `+gfm_auto_identifiers` to the pandoc reader;
  GitHub-style slugs preserve leading numbers.
- **Pandoc pipe-table overflow across many docs** — visual sweep of
  the rebuilt full manuscript caught 14+ pipe tables where col 1
  contained 24–41 char identifiers (contract, plugin, class, metric,
  CLI-tool names) that pandoc compressed too narrow, producing
  glued-together strings like "SettlementManagAccept",
  "L2ValidiumModBAC", "ExternalCrossChainMessage102 + N bytes",
  "MerkleStateBatchExecuIL2BatchExecutor". Convert each affected
  table to a definition-style bullet list (each item bolds the name,
  description on indented lines): contract list (3 §) + plugin list
  (4.1 §) + proof-stage (5.2 §) + chain modes (4.3) in the
  whitepaper; the doc.md-section-to-code locator (architecture-
  walkthrough.md); 16 Contract/Plugin/Role tables in
  architecture-glossary.md; 22 Metric/Type tables in telemetry.md
  (covering all 8 plugin metric groups); the wallet-integration.md
  CLI/Output/Wallet table; 5 Component/Status/Code tables in
  tech-stack-coverage.md (Layers 1–5); the EVM-chains slot
  allocation (16 rows); the L1-vs-L2 Contract/Tier/Should-be?/Notes
  table (28 rows); the trust-boundaries §4 cross-tier verification +
  §5 component-level + cryptographic failure tables (3 in EN, 3 in
  ZH); the launching-an-l2 "Five extension points" 3-col table; the
  l2-lifecycle "What flows where" 3-col table; the glossary's CLI
  tools 3-col table and "Wire formats quick index" 3-col table; the
  README's "What's in the repo" 3-col table; and the
  plan-application-engine "What we're replacing" 3-col table. Each
  listed contract / plugin / metric / wire format / CLI tool / etc.
  retains the same information density but escapes pandoc's
  fixed-width column allocation.
- **Code blocks overflowing right margin** — long shell commands
  (≥85 chars), C# rpc.CallAsync calls, and a few comment lines in
  fenced code blocks ran past the right margin since the local
  TeX install lacks fvextra (so fancyvrb breakanywhere is
  unavailable). Two-pronged fix: (a) shrink the monospace font to
  0.85 scale via `monofontoptions: [Scale=0.85]` in all 4 metadata
  files — this saved ~10 pages off the full EN manuscript while
  fitting more chars per line; (b) explicit shell `\` line
  continuations in the few remaining commands that still exceeded
  width at 0.85 scale (README quick-start, getting-started's plan
  output, launching-an-l2's fraud-verifier comments,
  wallet-integration's rpc.CallAsync).
- **Long inline-code path overflowing prose** —
  `external/foreign-contracts/eth/src/NeoExternalBridgeRouter.sol`
  was a single inline-code monospace token too long for LaTeX to
  break, running past the right margin as "NeoExternalB" with the
  closing backtick truncated. Restructure the bullet to put the
  path on its own "Source: <path>" continuation line.
- **Full-manuscript header overlap on long chapter titles**. Same
  pattern as the whitepaper-essentials fix already applied to the
  essentials variant: drop the custom LO/RE manuscript-title
  fancyhead entry from `metadata-{en,zh}.yaml`. With long chapter
  titles like "Architecture: L1 vs L2 division of responsibilities",
  the running title and the chapter-title were colliding on the
  same line.

### Added — Manuscript-style PDF compilation (`tools/manuscript/`)

The 20 markdown files under `docs/` (and their Chinese mirrors under
`docs/zh/`) can now be compiled into a single manuscript-style PDF per
language — chapter ordering, parts, TOC, numbered sections, embedded
SVG figures, all rendered through pandoc + xelatex.

- **`tools/manuscript/build.sh`** — orchestrator. Concatenates per a
  manifest with `\part{}` dividers + `\newpage` between chapters,
  rewrites `.svg` references to `.pdf` (via cairosvg or rsvg-convert),
  strips GitHub badges, normalizes BMP-only emoji glyphs (✅→✓, ❌→✗,
  🟡→○, 🔴→●, ⏭→→) for DejaVu Sans's coverage, and rewrites raw
  `<p align="center"><img>` HTML to markdown image syntax (raw HTML
  doesn't survive the LaTeX path; markdown images become
  `\includegraphics`).
- **`manifest-en.txt` / `manifest-zh.txt`** — chapter ordering with
  5 parts: Front matter / Architecture / Operator Guide / External
  Bridge / Implementation Plans (前言 / 架构 / 运维指南 / 外链桥 /
  实现计划).
- **`metadata-en.yaml` / `metadata-zh.yaml`** — pandoc YAML metadata.
  EN uses DejaVu Sans + report class + 11pt; ZH adds xeCJK with
  Noto Serif CJK SC, `\xeCJKsetup{CJKecglue={}}` to suppress space
  injection between Chinese and ASCII tokens, `linestretch: 1.4`.
- **Output**: `build/manuscript-en.pdf` (≈1.5MB, ≈197 pages) and
  `build/manuscript-zh.pdf` (≈2.0MB, ≈194 pages). Build is
  reproducible from a fresh checkout: `tools/manuscript/build.sh`
  (no args → both languages).

### Changed — Idiomatic Chinese blockchain terminology pass

The Chinese translations under `docs/zh/` use industry-standard
idiomatic blockchain terminology consistently rather than mechanical
word-for-word renderings. 13 substitutions across 10 markdown files
+ 2 SVG figures resolve all remaining English-Chinese mixed phrases:

- **"inclusion proof"** / **"inclusion 证明"** → **包含证明**
- **"validity proof"** / **"validity 证明"** → **有效性证明**
- **standalone "ZK validity"** → **"ZK 有效性"** (security-level
  category name; "Stage 2 ZK validity" wherever it labels the
  protocol phase)
- **"Stage-2 validity"** → **"Stage-2 有效性"**
- **"(可证明 validity)"** → **"(可证明的有效性)"**

Code-literal `validity_proof` (the actual variable / data field name
in alt-text and code blocks) is preserved as-is. `pdftotext` audit on
the rebuilt manuscript confirms 0 mixed-language phrases remain.

### Changed — `docs/spec-gap-plan.md` body now reflects closure status

The plan's body had carried "Spec / Code today / Fix / Acceptance"
sections for items 1–6 even after the work was done; the summary at
the bottom already showed all 5 in-repo items as ✅ closed and item
6 (`§8-witness-canonical`) as ⏭ deferred. Body and summary now
agree. Each `### §X-name` heading carries its closure status; the
content under it cites the actual implementation (file, contract,
method, storage prefix) instead of restating planned changes. -66
lines net. Same rewrite mirrored in `docs/zh/spec-gap-plan.md`.

### Added — Comprehensive Simplified Chinese documentation (`docs/zh/`)

Every document under `docs/` now has a Simplified Chinese counterpart
under `docs/zh/`, in addition to the English original. The Chinese
master spec at `doc.md` remains authoritative when translations
disagree, but for everyday reading both languages are now first-class.

- **20 markdown files translated** covering operator guides
  (getting-started, persistence, security-model, telemetry,
  wallet-integration, launching-an-l2), architecture chapters
  (atlas, walkthrough, l2-lifecycle, l1-vs-l2, wire-formats,
  trust-boundaries, glossary), external bridge (roadmap,
  evm-chains), implementation plans (spec-gap-plan,
  plan-application-engine-and-mpt), and indexes (README, SUMMARY,
  tech-stack-coverage). ~5,700 lines of new translation.
- **Translation conventions** — proper nouns kept in English
  (NeoHub, ChainRegistry, SettlementManager, RocksDB, etc.);
  concept words translated (排序器/sequencer, 桥/bridge,
  信任边界/trust boundary, 委员会/committee, 持久化/persistence,
  准入/admission, 盖章/stamp); code blocks and config samples
  unchanged; cross-doc links use `./` for sibling zh docs and
  `../../` for paths outside `docs/`.
- **Mirror conventions** — figure references point to
  `../figures/architecture/...svg` so the docs/zh/ tree resolves
  figures from the shared `docs/figures/` gallery without
  duplication. Chinese SVG variants under
  `docs/zh/figures/architecture/` use a font stack with CJK
  fallbacks (`'PingFang SC', 'Hiragino Sans GB', 'Microsoft YaHei'`).

### Changed — Architecture figures: ASCII → hand-tuned SVG

Every diagrammatic figure in the 7 architecture chapters has been
converted from ASCII art to a hand-tuned SVG. The earlier "all
ASCII" approach (taken because mermaid wouldn't render reliably in
VS Code) is now replaced with a third option: SVG that ships in
the repo and renders identically across VS Code, GitHub, mdBook,
and any browser.

**29 unique architecture SVGs × 2 languages = 58 files shipped:**

- **System topology** — system-tiers, neohub-anatomy, l2-components,
  l2-anatomy, runtime-channels, deployment-flow, admission-states,
  creation-lifecycle (20-step swimlane).
- **Cross-tier flows** — l1-l2-bridge, settlement-sequence,
  bridge-sequences (deposit + withdrawal 2-panel),
  cross-l2-messaging-sequence, external-bridge-architecture,
  forced-inclusion-step1.
- **Trust + security** — trust-boundaries (5-boundary system map),
  cross-tier-verification (9-step verification chain across
  3 trust assumptions), trust-minimization-gradient (5-tier
  spectrum from sequencer-only to ZK validity).
- **L1/L2 division** — dividing-principle (hero card),
  l1-concerns (6 NeoHub concerns + verifier slot), l2-concerns
  (3-layer per-chain stack), l1-l2-decision-tree (5-question
  flowchart for component placement).
- **Wire formats** — byte-layout-l2batchcommitment (321+N B),
  byte-layout-publicinputs (332 B), byte-layout-l2chainconfig
  (91 B + template lookup), byte-layout-externalcrosschainmessage
  (102+N B), byte-layout-depositpayload (44+N B).
- **Atlas + scaffold** — reading-paths (5 paths by reader role),
  chapter-map (5-chapter relationship diagram),
  new-l2-scaffold-tree (file-tree visualization).

Style conventions: viewBox 960×N, Apple/system font stack with CJK
fallbacks, drop-shadow card filter, color palette by tier (L1 blue
#eff3ff/#7587c1, Gateway orange #fff2e3/#cd7c2e, L2 green
#e7f4eb/#3a8454, off-chain purple #f1ecf8/#7d59b3, boundary red
#fde8e8/#c4413e), rounded rects rx=6/8/10. All SVGs include
`<title>` and `<desc>` for screen readers.

### Changed — Source-truth consistency reconciliation

Per "consistent to implementations" directive, audited the project's
docs against the actual source tree and reconciled 5 categorical
drifts spanning README.md, IMPLEMENTATION_STATUS.md, docs/README.md,
docs/zh/README.md, getting-started.md (EN+ZH), launching-an-l2.md
(EN+ZH), tech-stack-coverage.md (EN+ZH), architecture-glossary.md
(EN+ZH), and the 2 affected SVG figures (EN+ZH):

- **Test count**: 1332/1344 → 1362 (8 references reconciled to the
  authoritative top-level README.md breakdown line 84).
- **Component coverage**: "51 ✅, 2 🟡, 6 🔴" → "76 ✅, 0 🟡, 0 🔴"
  (matching tech-stack-coverage.md actual matrix total now that
  Phase 4 SP1 ZK is end-to-end functional and Layer-4/5 SDKs +
  web app + mdBook are all in-tree).
- **Off-chain library count**: 15 → 16 (added `Executor.RiscV` to
  enumerated list; `Sdk` listed separately under App SDKs).
- **CLI tool count**: 6 → 7 (`tech-stack-coverage.md` was missing
  `neo-external-bridge` from the count; subcommand total bumped
  23 → 28 to include genkey + committee-blob + deploy-bundle +
  chains-table + per-chain helpers).
- **Deploy-bundle step count**: 13/14/15/19/21 → **20** across 8
  references (authoritative from `IMPLEMENTATION_STATUS.md` line
  293's enumeration: "13 core NeoHub + GovernanceFraudVerifier
  v1/v2 + RestrictedExecutionFraudVerifier v3 + 4 Phase-B + 1
  Phase-C = 20"). Each reference now spells out the breakdown.
- **RPC method count**: 9 → 10 (`getsecuritylabel` added with
  §16.2 dimensions but glossary + `l2-concerns.svg` +
  `l2-components.svg` still said 9).
- **Project count**: 34 → 33 in `tech-stack-coverage.md` (matches
  `dotnet test Neo.L2.sln` enumeration).

Plus retagged 3 mistagged code listings in
`external-bridge-roadmap.md` (EN+ZH) — the C# `IExternalBridgeVerifier`
interface declaration changed from `text` to `csharp`; the file-tree
listings changed from `text` to bare fence (they are listings, not
figures).

### Added — Watcher operator UX surface

The watcher daemon now ships a complete CLI for operators:

- **`--version` / `-V`** — prints `neo-bridge-watcher-eth X.Y.Z`
  (CARGO_PKG_NAME + CARGO_PKG_VERSION baked at compile time;
  format pinned for ops scripts).
- **`--preflight`** — validates config + signer key + journal
  flock + chain-id namespace + actual RPC reachability before the
  watch loop starts. Walks 8 checks; exits 0/1; designed for
  systemd ExecStartPre / kubectl apply gates. Probes:
  - `eth_blockNumber` against `eth_rpc_url` (5s timeout)
  - `eth_getCode` against the router (rejects EOA / wrong proxy /
    empty bytecode — catches typo'd contract addresses)
  - Neo `getversion` against `neo_rpc_url`
  - Zero-address rejection on `eth_router_address` /
    `neo_escrow_address` / `neo_signer_address` (guards against
    typo'd `0x000...` configs that would silently route locked
    events to the void).
- **`--config-template`** — prints a fully-commented starter TOML
  to stdout. Operator workflow: `... --config-template > watcher.toml`,
  edit `REPLACE_WITH_*` placeholders, `--preflight`, run.
- **`--journal-info`** — read-only journal inspection. Reads
  `cursor.bin` + `consumed.log` directly (no flock acquisition —
  safe to run while the daemon is also running). Output: cursor
  + per-chain breakdown with human-readable names (via
  `chains::name_for_chain_id`) + recent records.
- **Improved `--help`** — lists all 6 flags + names every TOML
  section + field. Updates "unexpected argument" error to list
  the valid alternatives.

### Added — Operational additions to the watcher

- **`{chain_id="0x..."}` Prometheus label** — every metric line
  now carries a chain-id label automatically (the daemon binary
  calls `HealthState::with_chain_id(config.external_chain_id)`).
  Multi-chain operator setups get cleanly disambiguated time
  series without Prometheus relabel rules.
- **`[poll].start_block`** — first-run cursor bootstrap. When
  the journal cursor is below `start_block`, the daemon advances
  on startup. Monotonic; restarts read the journal as normal.
  Skips the genesis-to-N scan when deploying a watcher mid-stream.

### Added — Integration test coverage for the operator UX + run loop

- 10 subprocess-driven integration tests in `preflight_smoke.rs`
  covering each operator flag (`--version` long + short form,
  `--config-template` round-trips through preflight,
  `--journal-info` decodes a hand-crafted journal, unknown-flag
  rejection, and the 5 preflight failure cases including the
  new zero-address + no-bytecode + JSON-RPC-error paths).
- New `daemon_run_loop_smoke.rs` — end-to-end test: spawn the
  binary, FakeRpcServer pair (Eth + Neo), let it run ~2.5s,
  SIGTERM via `libc::kill`, assert clean exit (code 0) +
  ≥2 RPC calls (loop iterated). unix + live-rpc only;
  Rust's `Child::kill` is SIGKILL — testing graceful shutdown
  required `libc::kill(pid, SIGTERM)`.

### Changed — Architecture atlas (5 chapters, ~2050 lines, all ASCII)

Comprehensive architecture documentation. All ASCII (no mermaid)
so it renders in any UTF-8 editor (VS Code, Vim, GitHub, mdbook)
without preprocessor plugins.

- **`docs/architecture-atlas.md`** — front door. 5 reading paths
  by role (overview / operator / SDK / auditor / contributor).
  Cross-reference table for navigation between chapters.
- **`docs/architecture-l2-lifecycle.md`** (741 lines, 12 ASCII
  diagrams) — system topology + L2 creation/deployment/connection.
  4-tier system map, NeoHub anatomy, plugin/native-contract
  layout, creation lifecycle (sequence + 3-phase admission state
  diagram), deployment (4-step contract registration), runtime
  channels (settlement / bridge / messaging), cross-L2 messaging,
  external-chain bridge, neo-stack subcommand cross-reference.
- **`docs/architecture-wire-formats.md`** (361 lines) — byte-by-byte
  canonical layouts for L2BatchCommitment (321+N), PublicInputs
  (332), L2ChainConfig (91), ExternalCrossChainMessage (102+N),
  DepositPayload (44+N). Per-field offset tables + visual byte
  maps. 12-row implementation cross-reference. Byte offsets
  pulled from actual serializer source.
- **`docs/architecture-trust-boundaries.md`** (399 lines) —
  architecture-from-trust view. 5-boundary system map, per-boundary
  trust tables, 9-step deposit verification narrative across 4
  trust boundaries, 6-flow defense-in-depth table, failure-mode
  taxonomy (component + cryptographic), trust-minimization
  gradient (solo-sequencer → ZK-validity).
- **`docs/architecture-glossary.md`** (233 lines) — single-page
  reference: 38-term glossary + 21 NeoHub contracts (grouped by
  6 concerns) + 7 L2 natives + 8 plugins + 7 CLI tools + wire-
  format quick index.

### Changed — Documentation sync

- Stale "15 NeoHub" / "20 NeoHub" / "6 L2 native" claims fixed
  across 5 .md files — actual count from contracts/: 21 NeoHub +
  7 L2 native = 28 smart contracts total.
- README.md now points at `architecture-atlas.md` as the
  documentation map's architecture front door.
- `external-bridge-evm-chains.md` updated with start_block +
  preflight examples; full [poll] schema (was using stale
  `_ms`-suffix field names from an older schema).
- Watcher daemon README has a CLI surface section + Operator
  inspection commands section with concrete `--journal-info`
  + `curl /healthz` / `/info` / `/metrics` examples.

### Added — Watcher daemon production-readiness sweep

Operational features shipped iteratively that take the watcher daemon
from a v0 messaging+signing core to a kubernetes-deployable service:

- **Graceful SIGTERM/SIGINT shutdown** (`1fd9077`) — static AtomicBool
  + async-signal-safe handler; `interruptible_sleep` polls the flag
  every 100ms so kill signals respond within ~100ms even from a long
  backoff sleep. Verified via SIGTERM during a 30s backoff: clean
  exit in 1s vs full 30s without.

- **Per-chain `min_confirmations` reorg buffer** (`a4e6f2c`) —
  `EthRpcEventSourceBuilder.min_confirmations(n)` caps the polling
  window at `head - n` (saturating), defending against short-reorg
  phantom mints. Operator's primary defense across the EVM family.
  4 new live-RPC tests pin the saturating + emit-after-buffer paths.

- **`chains::recommended_confirmations(chain_id)`** (`7da733b`) —
  programmatic per-chain default for all 14 EVM-family slots + Solana.
  Daemon startup emits a WARNING if `min_confirmations = 0` but the
  chain has a non-zero recommendation (e.g., BSC mainnet → 15;
  Polygon → 256; L2s → 0 with operator-supplied L1 finality signal).
  3 new tests pin coverage + anchor values + unknown-id None.

- **`PollConfig::default` fix** (`c510cec`) — when `[poll]` table was
  omitted entirely, serde fell back to `#[derive(Default)]` which
  zeroed every field, producing a tight retry spin (poll 0s, backoff
  0-0s). Manual `Default` impl now mirrors the per-field defaults.

- **`Dockerfile` + `.dockerignore`** (`d39f3ff`, `de6a6f1`,
  `ab67ef7`) — multi-stage build (rust:1.86-slim builder →
  distroless cc-debian12 runtime, ~50 MB final image). Runs as
  uid:gid 65532:65532, exposes `:9090`. `.dockerignore` keeps the
  context lean (excludes ~325MB of unrelated submodule source) but
  preserves `external/neo-zkvm` for cargo workspace metadata.

- **CI workflow `build-watcher-image.yml`** (`3e375d7` + repairs in
  `b3e7233` / `de6a6f1` / `ab67ef7`) — pushes to GHCR on master with
  `:latest` + `:master` + `:sha-<7char>` tags + `type=gha` layer
  cache (subsequent builds ~2 min vs ~10 min cold).

- **`deploy/k8s.yaml`** + **`deploy/neo-bridge-watcher.service`** —
  reference Kubernetes (Deployment + Service + ConfigMap + Secret +
  PVC; `Recreate` strategy + `terminationGracePeriodSeconds=30` to
  cooperate with the journal's `flock`-based concurrent-instance
  detection) and systemd (hardened with `ProtectSystem=strict` +
  `NoNewPrivileges=true`) manifests.

### Added — Watcher CI gates (cargo build + test + clippy)

`.github/workflows/build.yml`'s `bridge` job now exercises every
watcher crate (`ddb2a66`):

- `cargo build + test (neo-bridge-watcher-eth, default features)` —
  cryptographic core, parity vectors, chains-table validity.
- `cargo build + test (neo-bridge-watcher-eth, --features live-rpc)`
  — HTTP adapters, journal, health server, Prometheus /metrics.
- `cargo build + test (neo-bridge-watcher-tron)` — chain-id namespace
  + cross-curve abstraction.
- `cargo build + test (neo-bridge-watcher-sol)` — ed25519 signer +
  trait polymorphism.

Plus a `cargo clippy --all-targets -- -D warnings` gate covering
both feature configurations of eth + tron + sol (`9ce55b7`). Came
with a small drift-cleanup pass: dropped unused imports
(signer.rs, bin/main.rs), fixed doc-list-item indentation
(messaging.rs), removed unreachable test arms (file_journal.rs,
JournalError has only one variant today), used `Range::contains`
(sol), wrapped const-bound assertions in `const _: () = { ... }`
(tron) so they're compile-time checks.

### Fixed — Pre-existing CI repairs (build.yml red on every commit)

The existing build.yml CI had been red on every push for ~24 hours
when this session began. Three independent issues, all unrelated
to watcher work but necessary to unblock the green-build state:

- **`neo-zkvm-guest` host-mode test** (`3b32f80`) —
  `sp1_zkvm::entrypoint!(main)` macro exits with non-zero status
  when invoked outside the SP1 zkVM (160 locally / 192 in CI).
  cargo test interpreted the non-zero as failure. Fix: add
  `test = false` to the `[[bin]]` entry; bin still builds for
  the real-zkVM job, just skipped from cargo test.

- **`neo-zkvm-host` SP1 zkVM job missing submodules** (`06d6214`)
  — `actions/checkout@v6` without `submodules: recursive` left
  `external/neo-zkvm/crates/neo-vm-guest/Cargo.toml` absent;
  `cargo prove build` failed at metadata resolution. Matched the
  existing bridge job's checkout config.

- **SP1 toolchain cache missing the `succinct` rustup toolchain**
  (`6c915f0`) — the cache only saved `~/.sp1/` (binaries), not
  `~/.rustup/toolchains/succinct/` (the actual cargo +succinct
  toolchain). On cache hit, the install step was skipped, leaving
  binaries present but the toolchain absent. Extended cache path
  + bumped key to v6.1.

### Added — Prometheus `/metrics` endpoint for the watcher daemon

The same `[health]` server now exposes `GET /metrics` in Prometheus
exposition format (text/plain; version=0.0.4). Operators get
out-of-the-box monitoring without a separate exporter binary.

9 metrics: `watcher_started_at_unix_timestamp`,
`watcher_last_tick_unix_timestamp`,
`watcher_last_tick_success_unix_timestamp`, `watcher_ticks_total`
(counter), `watcher_events_processed_total` (counter),
`watcher_submissions_total` (counter), `watcher_journal_cursor`,
`watcher_last_error_unix_timestamp`, `watcher_healthy` (1/0 —
same logic as `/healthz`'s 200/503).

Each metric has Prometheus-conventional `# HELP` + `# TYPE`
preambles. 2 new tests pin the format + content; existing 7 health
tests unchanged.

`deploy/k8s.yaml` Service now carries `prometheus.io/scrape` /
`prometheus.io/port` / `prometheus.io/path` annotations so the
default Prometheus operator picks it up automatically.
`deploy/README.md` includes a scrape-config example with `chain=`
relabel + recommended alert rules covering staleness, stuck
submissions, and recent errors.

### Added — Production deployment manifests + watcher README sweep

Documentation for the operational features shipped this session
(graceful shutdown, /healthz, journal flock, min_confirmations,
recommended_confirmations).

- `watchers/neo-bridge-watcher-eth/deploy/k8s.yaml` — Kubernetes
  reference: Namespace + ConfigMap + Secret + PersistentVolumeClaim +
  Deployment + Service. Wires `/healthz` to readiness + liveness
  probes (5s/30s periods, 24/4 failure thresholds = 120s stale);
  `Recreate` strategy + `terminationGracePeriodSeconds=30` for clean
  flock release across pod replacements; ClusterIP service so
  `/healthz` doesn't leak chain id / journal cursor / last error to
  the public internet. 6 valid YAML docs validated via `yaml.safe_load_all`.

- `watchers/neo-bridge-watcher-eth/deploy/neo-bridge-watcher.service`
  — systemd unit. SIGTERM-driven clean shutdown via `KillSignal=SIGTERM`
  + `TimeoutStopSec=30`; hardened with `ProtectSystem=strict` /
  `NoNewPrivileges=true` / `PrivateTmp=true`. Validated via
  `systemd-analyze verify` (only warning is the absent placeholder
  binary path — expected on a dev machine).

- `watchers/neo-bridge-watcher-eth/deploy/README.md` — covers the
  operational invariants the manifests assume (single-instance per
  journal_dir / SIGTERM behavior / health-probe shape / key custody /
  journal durability) + what the manifests don't cover yet (metrics
  endpoint, multi-chain operator setups, HSM signer integration).

- `watchers/neo-bridge-watcher-eth/README.md` — config example
  expanded to include all three sections (top-level fields + `[poll]`
  with `min_confirmations` + `[health]`); new "Operational features"
  table cross-references graceful-shutdown / flock / confirmation
  buffer / health endpoint to their source modules; new "Production
  deployment" section pointing at `deploy/`.

### Added — `/healthz` HTTP endpoint for the watcher daemon

Production deployments (kubernetes, systemd) need a programmatic
liveness/readiness signal separate from "the process exists". The
watcher daemon now optionally exposes:

- `GET /healthz` → 200 if a tick has succeeded within
  `threshold_secs`, 503 otherwise. k8s readiness probes look at the
  status code; load-balancers gate traffic accordingly.
- `GET /info` → always 200 with the same JSON status body. For
  operator dashboards.
- Other paths → 404 + error JSON.

Body shape:
```json
{
  "healthy": bool,
  "started_at_unix": u64,
  "last_tick_at_unix": null | u64,
  "last_tick_success_unix": null | u64,
  "ticks_total": u64,
  "events_processed": u64,
  "submissions_total": u64,
  "journal_cursor": u64,
  "last_error": null | string,
  "last_error_unix": null | u64,
  "now_unix": u64
}
```

Config (optional `[health]` section):
```toml
[health]
bind = "0.0.0.0:9090"   # unset = no health server
threshold_secs = 120    # default 120 (covers 12s poll + 60s backoff cap)
```

Implementation: `src/live/health.rs` — `HealthState` (Arc<Mutex>
internal + cheap clone) + `HealthServer` (TCP listener + background
thread + Drop teardown). The main loop calls `record_tick` /
`record_submission` / `record_error` after each iteration. 7 unit
tests pin: snapshot field correctness, pre-first-tick start-time
fallback, error truncation at 256 chars, live HTTP serving 200/503/404
via reqwest, port release on Drop.

### Added — Concurrent-instance detection for FileJournal

Two watcher processes pointed at the same journal directory used to
silently race on `consumed.log`: 12-byte appends would interleave,
corrupting every future replay (records misaligned by 1+ bytes parse
as garbage chain ids / nonces). The dual-instance scenario is real —
operators run dozens of watcher processes across chains; an accidental
config typo pointing two at the same dir is plausible.

`FileJournal::open` now acquires an OS-level advisory exclusive lock
on a `.lock` sentinel file via `flock(LOCK_EX | LOCK_NB)`. The lock
is held for the journal's lifetime; the OS releases it on Drop or
process exit, so a crashed previous instance can never block restart.

A second `open` while the first is alive returns `JournalError::Io`
with a clear message naming the lock mechanism. Test:
`second_open_on_same_dir_fails_with_lock_error` (Unix-only, gated
behind `#[cfg(unix)]`). New optional dep: `libc = "0.2"` activated
under `live-rpc`.

### Changed — README + docs sweep for EVM-chain support

Sync docs to reflect generic EVM-chain support shipped in 28e85f3 +
a2184ff. Changes are documentation-only (no source files touched).

- `README.md` — bumped foreign-chain-integrations cell + cross-language
  test count (86 → 105). Surfaces the full EVM family the bridge now
  serves through a single daemon binary; links the canonical 16-slot
  family-bank table + the 5-step onboarding runbook.
- `IMPLEMENTATION_STATUS.md` — bridge section now describes per-instance
  state isolation pinned by the multi-chain Foundry tests + the chains
  module's role; bumped per-component test counts (eth: 24 → 29; foundry:
  13 → 20; cross-language total: 86 → 105).
- `external/foreign-contracts/eth/README.md` — title changed from
  "Eth-side bridge contracts" to "EVM-side bridge contracts" (one
  contract, every EVM chain). Added per-suite test breakdown + a
  generic "Other EVM chains" deployment section using BSC as an example.
- `external/foreign-contracts/tron/README.md` — references the chains
  module + the full family-bank ordering for the 14 EVM chain families
  Tron is a member of.
- `docs/SUMMARY.md` — new "External bridge" mdBook section linking
  the roadmap + the EVM-chain runbook (previously not in the docs
  site nav).

### Added — Multi-chain Foundry test + runbook fix

End-to-end Solidity validation that `NeoExternalBridgeRouter.sol`
deploys + functions across the entire EVM-family chain-id space. Pins
the load-bearing claim from `docs/external-bridge-evm-chains.md`
("supports the entire EVM family with no per-chain code") on the
contract side.

- `external/foreign-contracts/eth/test/NeoExternalBridgeRouterMultiChain.t.sol`
  — 7 tests:
  - `test_AllFamilyBankMainnetsConstruct`: 14 canonical mainnet slots
    (Eth, Tron, BSC, Polygon, Arbitrum, Optimism, Base, Avalanche,
    Linea, zkSync, Scroll, Mantle, Fantom, Celo) — all construct, all
    record their externalChainId verbatim.
  - `test_TestnetSlotsAlsoConstruct`: 6 testnet slots in different
    family banks construct cleanly.
  - `test_OutOfNamespaceIdsRejected`: 5 boundary cases (one below 0xE0,
    one above, no prefix, wrong family byte, etc.) revert at construction.
  - `test_EachRouterStampsItsOwnChainIdInLocked`: BSC + Polygon routers
    emit `Locked` with their own externalChainId; each carries through
    indexed topic[1].
  - `test_BscRouterRejectsPolygonMessage`: a finalizeWithdrawal whose
    canonical bytes claim externalChainId=POLYGON_MAINNET reverts on a
    BSC router (chain-id mismatch).
  - `test_NoncesAreIndependentPerRouter`: interleaved BSC + Polygon
    locks; each has its own counter starting at 1.
  - `test_PolygonCommitteeNotAuthorizedOnBscRouter`: a Polygon-committee
    signature on a BSC-chain-id message reverts (committee state is
    per-router-instance, not shared).

  Combined Foundry suite: **20 tests** (13 original + 7 multi-chain).

- `docs/external-bridge-evm-chains.md` — fixed the runbook: the
  constructor takes `(uint32 _externalChainId, address _owner)`, NOT
  the 4-arg form previously documented (`_minLockAmount`, `_neoChainId`,
  `_committeeRootHash` were fictitious). Step 2 now shows the correct
  `forge create` invocation + the `cast send setCommittee(...)`
  follow-up. Step 4 corrected to use the operator CLI's actual flag
  names (`--pubs-file` / `--verifier` / `--registry` / `--escrow` /
  `--eth-router` / `--committee-blob` / `--eth-addresses`) — output
  goes to stdout, not `--out`.

### Added — Generic EVM-chain support for the external bridge

The watcher framework now treats the entire EVM family (Ethereum, BSC,
Polygon + zkEVM, Arbitrum One/Sepolia/Nova, Optimism, Base, Avalanche
C, Linea, zkSync Era, Scroll, Mantle, Fantom/Sonic, Celo, plus Tron's
EVM-flavored TVM) as variations of one chain template. Adding a new
EVM chain takes five steps and writes **zero new code**: pick a slot,
deploy `NeoExternalBridgeRouter.sol` with the foreign chain id as a
constructor arg, run the existing daemon binary against the chain's
RPC, register the committee on Neo.

- `watchers/neo-bridge-watcher-eth/src/chains.rs` — canonical
  foreign-namespace slot allocation. 16-slot family banks for each
  chain: Eth `0xE000_0001..000F`, Tron `..0010..001F`, Solana
  `..0020..002F`, BSC `..0030..003F`, Polygon `..0040..004F`,
  Arbitrum `..0050..005F`, Optimism `..0060..006F`, Base
  `..0070..007F`, Avalanche `..0080..008F`, Linea `..0090..009F`,
  zkSync `..00A0..00AF`, Scroll `..00B0..00BF`, Mantle
  `..00C0..00CF`, Fantom/Sonic `..00D0..00DF`, Celo `..00E0..00EF`.
  Helpers: `name_for_chain_id` (human label for daemon startup logs),
  `is_evm_family` (operator tooling decides whether the eth watcher
  binary applies vs the sol watcher), `is_foreign_chain_id`
  (namespace prefix check). 5 unit tests pin namespace prefix, id
  uniqueness, name-table coverage, EVM-family classification, and
  bank alignment — a typo'd constant at PR review surfaces here.

- `watchers/neo-bridge-watcher-tron/src/lib.rs` +
  `watchers/neo-bridge-watcher-sol/src/lib.rs` — re-export
  `neo_bridge_watcher_eth::chains` so consumers can read EVM constants
  without reaching into the Eth crate by name.

- `docs/external-bridge-evm-chains.md` — operator runbook. 5-step
  onboarding (pick slot → deploy router → run daemon → register
  committee → smoke-test) with a concrete BSC mainnet example, full
  slot-allocation table, and explicit non-guarantees (per-chain
  finality semantics, MEV protection, per-chain gas models — all
  operator concerns, not framework guarantees).

The deployment story is the existing
`external/foreign-contracts/eth/src/NeoExternalBridgeRouter.sol`
unchanged — its constructor parameterizes `externalChainId`, so the
same Solidity bytecode lands on any EVM chain. The watcher daemon
(`neo-bridge-watcher-eth --features live-rpc`) is fully chain-id
driven; same binary, different config.

### Added — Foreign-side router artifacts (Tron deploy notes + Solana Anchor program)

Closes the three-chain coverage at the foreign-router layer.

- `external/foreign-contracts/tron/README.md` — Tron's TVM is
  EVM-flavored Solidity; the existing Eth router parameterizes
  `externalChainId` via constructor, and the namespace check
  `(externalChainId & 0xFF000000) == 0xE0000000` accepts both Eth
  (`0xE0000001`/`02`) and Tron (`0xE0000010`/`11`/`12`). README
  documents tronbox/tronweb deployment, energy/bandwidth budgeting,
  and TVM-specific risk notes (post-Constantinople opcode
  differences, block.timestamp granularity).

- `external/foreign-contracts/sol/` — new Anchor program (Solana /
  Rust). Real ~440-line implementation against Anchor 0.30 + Solana
  1.18 conventions, mirroring the Eth router semantically:
  - **Three instructions**: `initialize` / `set_committee` /
    `lock_sol_and_send` / `finalize_withdrawal`.
  - **State in PDAs** (Solana convention): `BridgeState` (committee
    + threshold + outbound nonce counter), `Vault` (locked SOL
    lamports), `ConsumedNonce` per-(chain_id, nonce) for replay
    protection (init constraint = O(1) replay rejection without an
    extra read).
  - **ed25519 verification via Solana's sigverify precompile** — the
    canonical pattern (Wormhole / Neon use the same). The watcher
    submits one `ed25519_program::ID` instruction per signer BEFORE
    the bridge instruction; `finalize_withdrawal` walks
    `Sysvar<Instructions>` via `load_instruction_at_checked` to
    confirm the precompile ran with `(committee[idx].pubkey, our
    message_bytes)` tuples. Saves ~30k CU/sig vs in-program ed25519.
  - **Wire format**: same canonical `ExternalCrossChainMessage`
    (102B prefix + payload) the Neo + Eth verifiers parse — identical
    offsets (chainId 0..4, nonce 8..16, direction 16..17, recipient
    37..57, deadline 57..65, messageType 81..82, payload length
    98..102, payload at 102+).

  Build status: source-only in this iteration. The Anchor + Solana
  toolchains are heavy (Solana CLI install + anchor-cli + a
  solana-test-validator runtime for tests). Operators run
  `anchor build` / `anchor test` against `solana-test-validator`.
  README documents the toolchain install + deploy + acknowledges
  this code should be reviewed by a Solana developer before mainnet.
  v0 limitations spelled out: SOL-only (no SPL), `MSG_TYPE_CALL` /
  `AssetAndCall` revert, recipient zero-pads upper 12 bytes
  (full-32B extension is v1), Solana stays MPC-committee-only
  through Phase 4.

### Added — Tron + Solana watcher variants (curve-agnostic Signer)

Validates the Phase B trait abstractions transferred across both
secp256k1 (Eth/Tron) and ed25519 (Solana) curve families — the same
`Signer` trait + same `WatcherCore` orchestrator handles all three.

- **`watchers/neo-bridge-watcher-tron/`** — Cargo crate; thin re-export
  of `neo-bridge-watcher-eth` with Tron-specific chain-id constants:

      TRON_MAINNET_CHAIN_ID         = 0xE000_0010
      TRON_NILE_TESTNET_CHAIN_ID    = 0xE000_0011
      TRON_SHASTA_TESTNET_CHAIN_ID  = 0xE000_0012

  Tron uses the same secp256k1+SHA256 + Keccak256 address derivation
  as Ethereum, so no separate messaging or signing core is needed.
  Constructing a Tron daemon: `WatcherCore::new(TRON_MAINNET_CHAIN_ID,
  ...)` with the same trait impls an Eth daemon uses.

  Tests (7): chain-id namespace + slot-disjointness pins;
  `canonical_bytes_emit_tron_chain_id_at_offset_zero`;
  `canonical_bytes_diverge_from_eth_only_at_chain_id_position` (only
  bytes 0..4 differ between Tron and Eth — rest byte-identical);
  `message_hash_differs_from_eth_for_same_other_fields` (cross-chain
  replay protection); `fixed_prefix_still_102_bytes` invariant; doctest.

- **`watchers/neo-bridge-watcher-sol/`** — Cargo crate adding
  `Ed25519FileSigner` implementing `Signer` with `curve_tag = 2`. The
  on-chain `MpcCommitteeVerifier` already supports ed25519 via
  `CryptoLib.VerifyWithEd25519`; this crate plugs the off-chain side
  in. Solana chain-ids:

      SOLANA_MAINNET_CHAIN_ID  = 0xE000_0020
      SOLANA_DEVNET_CHAIN_ID   = 0xE000_0021
      SOLANA_TESTNET_CHAIN_ID  = 0xE000_0022

  Per the roadmap (`docs/external-bridge-roadmap.md` § Phase 4 +
  `doc.md` §11.3.4), Solana stays MPC-committee-only because Tower
  BFT light-client verification is genuinely expensive on-chain — but
  the committee model below works identically for Solana via the
  same trait surface.

  Tests (9): chain-id namespace + slot-disjointness pins; real ed25519
  sign+verify round-trip via `ed25519_dalek::Verifier`; key-length +
  pubkey-length pins; `signer_trait_dispatches_by_curve_tag` (a
  `Vec<Box<dyn Signer>>` holds both curve families distinguished by
  `curve_tag` + pubkey length); `watcher_core_drives_through_with_ed25519_signer`
  (full orchestrator round-trip producing ed25519-flavored proof
  bytes — 98B = 2 header + 32 pubkey + 64 sig, vs 99B for secp256k1's
  33B compressed pubkey).

- **`Signer` trait refactored to be curve-agnostic** in
  `watchers/neo-bridge-watcher-eth/src/signer.rs`:

      curve_tag(&self) -> u8                    # 1 secp256k1, 2 ed25519
      public_key_bytes(&self) -> Vec<u8>        # 33B or 32B
      sign_canonical_bytes(&self, ...)
          -> Result<SignerOutput, SignerError>  # 64B sig + recovery byte

  `SignerOutput { signature: [u8; 64], recovery_id: u8 }` holds both
  curves' raw 64-byte sigs (r||s for secp256k1, R||s for ed25519);
  `recovery_id` is meaningful only for secp256k1 (Eth-style 27/28),
  ed25519 returns 0. Removed `eth_address` (was always-zero in the
  default impl; address derivation lives outside the trait now).
  Removed `sign_prehashed` (ed25519 has no prehash step — each curve
  handles its own hash internally inside `sign_canonical_bytes`).

  Existing `FileSigner` (secp256k1) adapts to the new trait shape;
  all 24 existing eth-watcher tests still pass.

`WatcherCore.process_event` now picks `Curve::Secp256k1` or
`Curve::Ed25519` for `NeoProofBytes::encode` per the signer's
`curve_tag()`.

Cumulative: 40 tests across the three watcher crates (was 24 in just
the Eth crate). All green.

### Added — External bridge Phase C (optimistic-challenge slashing for committee equivocation)

Phase B's economic security model assumed an "owner-only-slashable"
`ExternalBridgeBond` would be replaced with permissionless slashing
once a fraud verifier shipped. This is that fraud verifier and the
on-chain plumbing it needs.

- `NeoHub.MpcCommitteeFraudVerifier` — accepts cryptographic proof
  of equivocation: same `(externalChainId, nonce)`, two byte-distinct
  messages, both signed by the same committee pubkey. On valid proof,
  slashes the equivocator's full bond + pays the reporter
  (`Runtime.CallingScriptHash`). Verification chain (each step
  fail-loudly):
  1. Replay-protect per `(chainId, signerIdx)` — single equivocation
     slashes once.
  2. Both messages parse as `ExternalCrossChainMessage` (≥102B) with
     the same `(chainId, nonce)` at offsets 0 + 8.
  3. Messages are NOT byte-identical (ECDSA permits distinct `(r,s)`
     for the same digest, so byte-equality is the load-bearing rule).
  4. Both signatures verify against the SAME committee pubkey
     (`secp256k1+SHA256` for Eth/Tron, `ed25519` for Solana).
  5. Signer slot has a bond-holder bound via the new
     `RegisterCommitteeWithMembers` — refuses to slash if not bound.
  6. Reads full bond balance, calls
     `ExternalBridgeBond.Slash(...)`, pays reporter.
  Emits `CommitteeMemberSlashed(chainId, signerIdx, member, amount,
  reporter)` for off-chain monitoring.

- `MpcCommitteeVerifier` extended (additive) with
  `RegisterCommitteeWithMembers(...)` + `GetSignerMember(...)`.
  Binds each signer index to a Neo-side bond-holder address so the
  fraud verifier can identify which bond to slash. Original
  `RegisterCommittee` still works for chains not using slashing.

- `Neo.Hub.Deploy.ScaffoldPlan` — scaffold now includes the fraud
  verifier as step 20 (depends on `MpcCommitteeVerifier` +
  `ExternalBridgeBond`). Total: 20 steps, was 19.
  PostDeployActions: dropped the "Phase-C reminder" stub, added two
  real wiring hints (`ExternalBridgeBond.RegisterSlasher(fraud
  verifier)` + per-chain `RegisterCommitteeWithMembers` pointer).
  Total: 10 hints, was 9.

- `tests/Neo.L2.Bridge.UnitTests/UT_MpcFraudProof_RealCrypto.cs` —
  7 tests with real `Crypto.Sign` + `Crypto.VerifySignature` on real
  secp256k1 keys, mirroring exactly what the on-chain verifier does.
  Pins: happy path, identical-messages-not-equivocation,
  different-nonces-honest, wrong-pubkey-rejection,
  chainId-mismatch-rejection, nonce-zero edge case, committee-blob
  layout invariant.

Cumulative: 1362 .NET tests across 33 projects, 78 cross-language
tests (15 TS + 10 Rust SDK + 8 SP1 guest + 24 Rust bridge watcher +
13 Foundry Solidity + 8 fraud-proof real-crypto), all green.

### Added — Cross-foreign-chain bridge (Phase B — doc.md §11.3)

A pluggable bridge to foreign chains (Ethereum, Tron, Solana). Distinct
from `NeoHub.SharedBridge` (which serves Neo L1 ↔ Neo L2, single
finality model) — this crosses into a foreign chain's finality model
and so needs an explicit verifier. Same upgrade-via-governance shape
as `NeoHub.VerifierRegistry`: dApps call one API; the verifier
underneath swaps from MPC committee → optimistic challenge → ZK light
client without breaking it.

- **5 new on-chain contracts** + **1 L2 native** + **1 Eth-side router**.
  - `NeoHub.MpcCommitteeVerifier` — M-of-N secp256k1/ed25519 over the
    canonical message bytes; same shape as `AttestationVerifier` but
    on-chain + indexed per-foreign-chain. Replay-protected per nonce.
  - `NeoHub.ExternalBridgeRegistry` — verifier dispatch table keyed by
    `externalChainId` (uint with the `0xE0_xx_xx_xx` foreign-namespace
    prefix). Stores the wired verifier hash + a `bridgeKind` byte
    (1=MPC, 2=Optimistic, 3=ZK). Owner-set + governance-mediated
    upgrade path with replay protection (mirrors `VerifierRegistry`).
  - `NeoHub.ExternalBridgeEscrow` — locks NEP-17 outbound + verifies
    inbound through the registry; per-(chainId, asset) locked-balance
    accounting so the escrow can't release more than was ever locked.
  - `NeoHub.ExternalBridgeBond` — committee bonding mirroring
    `SequencerBond`. Default `MinBond` 10 GAS.
  - `NeoHub.ExternalBridgeStubVerifier` — Phase-A acceptance test
    verifier returning `true`. `bridgeKind=0` to refuse production.
  - `L2NativeExternalBridgeContract` — Neo core native L2 counterpart: burn-on-
    send, sequencer-injected mint-on-receive (same pattern as
    `L2BridgeContract`).
  - `external/foreign-contracts/eth/NeoExternalBridgeRouter.sol` (393
    lines, solc 0.8.24, via_ir + optimizer; 13 Foundry tests with real
    `vm.sign` + `ecrecover` round-trips).

- **Off-chain codecs in C#** (`src/Neo.L2.Bridge/External/`):
  `MpcCommitteePayload` (encoder/decoder for both Neo and Eth proof
  formats); `ExternalAssetTransferPayload`; `IExternalBridgeSigner`
  trait. Plus `Neo.L2.Messaging.ExternalMessageHasher` /
  `ExternalMessageBuilder` and `Neo.L2.ExternalCrossChainMessage`.

- **Rust watcher messaging core** (`watchers/neo-bridge-watcher-eth/`):
  Cargo crate with `messaging` (byte-for-byte parity-tested against
  the C# encoder), `proof`, `signer` (k256-based dev signer behind a
  trait for HSM/KMS plug-in), `event_source` / `submitter` /
  `journal` traits with mock impls, `core::WatcherCore` orchestration
  pinning the safety invariant *cursor MUST NOT advance on submit
  failure*. 24 tests. Live ethers-rs / Neo-RPC / RocksDB adapters
  deferred.

- **Operator CLI** (`tools/Neo.External.Bridge.Cli/` — `neo-external-bridge`):
  `genkey` / `committee-blob` / `deploy-bundle`. Real secp256k1
  keypair generation, dual-side identity encoding (Neo compressed
  pubkey + Eth address), ordered Neo+Eth wire-up plan.

- **Deploy planner integration** (`Neo.Hub.Deploy`): scaffold now
  includes the 4 bridge contracts in dependency order (19 steps,
  was 15) + 4 new post-deploy hints.

- **Spec + roadmap**: `doc.md` §11.3 (authoritative spec) +
  `docs/external-bridge-roadmap.md` (4-phase implementation plan).

Cumulative: **1355 .NET tests** across **33 projects** + 70 cross-language
tests (15 TS + 10 Rust SDK + 8 SP1 guest + 24 Rust bridge watcher + 13
Foundry Solidity), all green.

### Added — `[plan: §16.1-admission]` `[plan: §16.1-approved-sets]` 3-phase L2 admission policy

doc.md §16.1 specifies three admission phases — permissioned (owner approves),
semi-permissionless (any caller if verifier+bridge are approved), permissionless
(any caller). The mode was stored in `GovernanceController.SetAdmissionMode(0..2)`
but never enforced — `ChainRegistry.RegisterChain` always required owner witness.

  - `ChainRegistry.SetGovernanceController(UInt160)` (owner-only) wires the
    admission-policy source.
  - `ChainRegistry.RegisterChainPublic(chainId, configBytes)` is the non-owner
    path that reads the wired GovernanceController hash, calls `getAdmissionMode()`,
    and branches: mode 0 rejects with "use RegisterChain"; mode 1 reads the L2's
    declared verifier (offset 24..43) + bridge (44..63) and asserts both are in
    GovernanceController's approved sets; mode 2 falls through to the standard
    write path with no extra check.
  - `GovernanceController.{Approve,Revoke}{Verifier,BridgeAdapter}` (owner-only
    mutators) + `[Safe] IsApproved{Verifier,BridgeAdapter}` (read paths) curate
    the approved sets that mode 1 consults. Revoke is forward-only — already-
    registered chains keep working; only future RegisterChainPublic calls are
    affected.
  - Refactored: `ChainRegistry.WriteChainConfig` private helper shared by
    RegisterChain (owner path) and RegisterChainPublic (governance path) so
    chainId/size/consistency assertions + event emission stay in sync.

### Added — `[plan: §16.2-config-bytes]` SequencerModel + ExitModel in wire format

doc.md §16.2 says every L2 must publicly display 5 security label dimensions.
Three were already wire-format fields (security/da/exit/active flags); two were
off-chain-only enums.

  - `ChainRegistry.ConfigSize` 89 → 91 bytes. New layout in XML doc:
    `[4B chainId][20B operator][20B verifier][20B bridge][20B msg]
    [1B securityLevel][1B daMode][1B gatewayEnabled][1B permissionlessExit]
    [1B sequencerModel][1B exitModel][1B active]`. `active` stays at
    ConfigSize-1 so Pause/Resume don't move.
  - `OffsetSequencerModel` (88) + `OffsetExitModel` (89) constants for callers.
  - `[Safe] GetSequencerModel(chainId)` / `[Safe] GetExitModel(chainId)`. Both
    return 0 (strongest-default) if the chain isn't registered, matching the
    off-chain L2ChainConfig record's init-defaults.
  - New `SequencerModel` enum (Centralized=0 / DbftCommittee=1 / Decentralized=2)
    + `ExitModel` enum (Permissionless=0 / Delayed=1 / OperatorAssisted=2) in
    `Neo.L2.Abstractions`. Discriminant pin tests in UT_Models.

### Added — `[plan: §16-council-veto]` verifier upgrades behind multisig + timelock

doc.md §16 + §17 mitigation #7 require verifier upgrades to clear a council
multisig + governance delay. GovernanceController had council members + Approve
+ a stored timelock seconds, but no contract enforced execution gating on the
council vote.

  - `GovernanceController.GetApprovedAt(proposalId)` records `Runtime.Time` (ms
    since epoch) when the approval count first crosses `GetThreshold()`. First-
    crossing only — re-records blocked by a "no overwrite" guard so a later
    vote past threshold can't reset the timer.
  - `[Safe] IsApprovedAndTimelocked(proposalId)` returns true iff approvedAt is
    set AND `Runtime.Time >= approvedAt + (timelockSeconds * 1000)`. The window
    between threshold-reach and timelock-elapsed is the council's last chance
    to raise an alarm.
  - `VerifierRegistry.SetGovernanceController(UInt160)` + `[Safe]
    GetGovernanceController` wire the gate.
  - `VerifierRegistry.RegisterVerifierViaProposal(proofType, verifier, proposalId)`
    is the council-veto path. Anyone may submit; authority comes from the
    proposal's approved+timelocked state. Replay-protected per proposalId.
    Original `RegisterVerifier` stays as the owner-only path.
  - Refactored: `WriteVerifier` private helper shared by both paths.

### Added — `[plan: §12-l1-da-default]` `JsonRpcL1DAWriter` for DAMode.L1

doc.md §12.1 lists L1 DA as one of three DA tiers, but
`L2DAPlugin.BuildDefaultWriter(DAMode.L1, ...)` threw `NotSupportedException`
unless the operator pre-injected a writer. NeoFS / DAC already had built-in
defaults; L1 didn't.

`Neo.Plugins.L2DA.JsonRpcL1DAWriter`: same composition shape as
`RpcSettlementClient` — takes a `JsonRpcClient` + a target contract hash + a
sign-and-send delegate so operators plug in their own wallet without forcing a
particular signer dependency. PublishAsync delegates the L1 transaction signing
to the operator's callback and returns a `DAReceipt` with
`Commitment = Hash256(payload)` (cross-tier convention so DAAvailabilityCheck
compares across DA layers) and `Pointer` = the 32-byte L1 tx hash for off-chain
re-fetch via `getrawtransaction`. IsAvailableAsync round-trips an
`invokefunction(isAvailable, [Hash256:commitment])` to L1; HALT+true → true,
HALT+false → false, FAULT → false. 13 unit tests using the StubHandler /
StringContent pattern from UT_RpcSettlementClient.

### Added — L2 development framework: templates + operator setup guide

`tools/Neo.Stack.Cli` `create-chain --template <name>` was cosmetic; now four
templates produce sensibly-distinct configs that match the §16.2 security label
defaults: `rollup` (default; L2RollupMode + L1 DA + Optimistic proof + Delayed
exit), `zk-rollup` (Validity + Permissionless + Gateway-enabled), `validium`
(L2ValidiumMode + NeoFS + Zk + Delayed), `sidechain` (SidechainMode + External
+ None + Permissionless).

`docs/launching-an-l2.md`: 5-command quick path (create-chain → init-l2 →
register-chain → deploy-bridge-adapter → start-{sequencer,batcher,prover}) +
templates table + architecture diagram pointing at the 5 customization extension
points (`ITransactionExecutor`, `IL2Prover`/`IL2ProofVerifier`, `IDAWriter`,
`ISequencerCommitteeProvider`, `IRoundProver`) with default impls + when to
swap + concrete C# customization recipe + full lifecycle diagram + extending-vs-
forking guidance. Linked from the README doc-map.

### Added — `docs/spec-gap-plan.md`: systematic plan for remaining doc.md gaps

A doc.md compliance audit (delegated to a sub-agent) identified 6 in-repo gaps
+ 3 upstream-blocked + 3 operator-specific. The plan documents each in-repo
item with: spec quote, current state, smallest-meaningful change, acceptance
criteria. 5 of 6 items closed in this Unreleased window; #6 (`§8-witness-
canonical`) deferred per the plan's own note ("premature without a real prover
targeting it"). Items 4 + 5 + 6 of the upstream/operator-specific tracks
remain blocked on external dependencies and are explicitly out of scope.

Cumulative: 892 tests / 27 projects.

### Added — `getsecuritylabel` RPC: full 5-dimension §16.2 label

`getsecuritylevel` returned only dimension 1 (chainType / SecurityLevel); the
other 4 dimensions were tracked on-chain in ChainRegistry's wire format but had
no RPC surface. `IL2RpcStore` now exposes `DAMode` / `GatewayEnabled` /
`Sequencer` / `Exit` (default-interface-method bodies match L2ChainConfig
defaults so existing third-party stores stay source-compatible);
`InMemoryL2RpcStore` overrides each as `{ get; init; }`. New
`L2RpcMethods.GetSecurityLabel` returns chainId + level + daMode +
gatewayEnabled + sequencer + exit (byte + string-name shape, same as the
existing `getsecuritylevel`).

Cumulative: 895 tests / 27 projects.

### Added — `L2ChainConfigSerializer`: canonical 91-byte wire encoder

`NeoHub.ChainRegistry.RegisterChain(chainId, configBytes)` requires exactly 91
bytes in a precise layout (doc.md §3.2 + §16.2), but no off-chain encoder
existed — the Stack CLI's `register-chain` printed `<configBytes>` as a
placeholder, leaving operators to hand-roll the serialization. Adds
`Neo.L2.L2ChainConfigSerializer` with `Encode(L2ChainConfig)` /
`Decode(ReadOnlySpan<byte>)` mirroring the on-chain layout byte-for-byte.

  - **Layout pinned in 7 tests** — `ConfigSize == 91` constant agreement, byte-
    layout pin (positions 0..90), full round-trip, enum-extreme round-trip
    (Validium / DAC / Decentralized / OperatorAssisted / Active=false),
    wrong-length rejection, out-of-range enum-byte rejection, chainId LE
    parsing parity (mirrors the contract's `(uint)b[0] | ((uint)b[1]<<8) | ...`
    expression).
  - **Range-checks on decode** — `securityLevel > 4`, `daMode > 3`,
    `sequencer > 2`, `exit > 2` reject up front so corrupted on-chain reads
    surface as ArgumentException instead of silently propagating as
    `(SecurityLevel)99`.

Cumulative: 903 tests / 27 projects.

### Added — `ChainRegistry` rounds out §16.2 reader API

Adds `[Safe]` single-purpose readers for the four label dimensions that
were missing single-purpose getters: `GetSecurityLevel` / `GetDAMode` /
`GetGatewayEnabled` / `GetPermissionlessExit`. Symmetric with the existing
`GetSequencerModel` / `GetExitModel`. All six follow the same not-registered-
returns-strongest-default convention. Public `Offset*` constants (84..89)
exposed for callers that want to address bytes by name.

### Changed — `L2DAPlugin` `DAMode.L1` error message names `JsonRpcL1DAWriter`

Operators hitting the "DAMode.L1 has no built-in default" error previously
got a vague "use WithWriter()" hint with no class name to reach for. Now the
message names the concrete class + ctor signature.

### Added — `ScaffoldPlan.PostDeployActions` surfaces governance wiring

The 13-step bundle deploys all contracts but doesn't wire the §16.1 admission
policy (ChainRegistry → GovernanceController) or §16 council-veto path
(VerifierRegistry → GovernanceController). Without surfacing these post-deploy
calls, an operator who runs the bundle silently misses two governance
features. Adds two new `PostDeployActions` entries — emitted only when both
the target contract and GovernanceController are in the bundle. 2 new tests
pin the asymmetry behavior.

Cumulative: 905 tests / 27 projects.

### Added — `DevnetLabelOverrides` type-mismatch / null-value / empty-object recovery tests (1153 → 1156)

The label-overrides reader's permissive-fallback semantics handle three
edge cases the existing tests didn't cover:
- `gatewayEnabled` emitted as string (`"true"` instead of `true`) →
  `JsonElement.GetBoolean()` throws → outer catch falls back to all-defaults.
- `gatewayEnabled` explicitly null → same shape (GetBoolean() throws).
- `{}` empty config → every field's `TryGetProperty` misses → all fields
  fall back to per-field defaults.

3 new tests pin these recovery paths so a refactor that changes the
recovery shape (e.g. making just gatewayEnabled tolerant of strings, or
returning early on the empty case) breaks loud here, not when an
operator's hand-edited JSON gets silently accepted.

Doc-set: test count 1153 → 1156 across the standard files.

### Added — Quick-path end-to-end integration tests (1150 → 1153)

The documented Quick path in `docs/launching-an-l2.md` walks operators
through 7 commands in sequence (create-chain → validate → init-l2 →
register-chain → deploy-bridge-adapter → start-sequencer/batcher/prover).
Each command had unit tests, but no test pinned the **command-to-command
interaction**. A regression in any one command's input/output shape
(e.g. create-chain emitting JSON validate can't parse, init-l2's new
chain.config.json check breaking register-chain's preflight) would only
surface when an operator follows the published walkthrough.

3 new tests in `UT_QuickPathIntegration`:
- `QuickPath_AllSevenCommands_SucceedInSequence` — runs the full 7-step
  Quick path against a fresh tempdir + asserts each command exits 0
  + each step's expected artifacts exist before the next runs.
- `QuickPath_RegisterWithFourHashes_EmitsConfigBytesHex` — variant
  where register-chain is called with all four UInt160 flags;
  validates the validium-template path through to the canonical
  91-byte configBytes hex.
- `QuickPath_NewL2Composite_AllStepsSucceed` — the alternate composite
  bring-up via `new-l2`; pins all 4 composite-step artifacts
  (chain.config.json + data/logs/Plugins + executor + tests project).

These are higher-value than per-command unit tests for this scenario
because they catch regressions in command-to-command contracts that
unit tests miss (each command stub-checks its OWN inputs but doesn't
exercise what the previous command actually wrote).

Doc-set: test count 1150 → 1153 across the standard files.

### Added — End-to-end full-stack composition test for all 3 L1-RPC pollers (1283 → 1285)

`UT_E2E_L1RpcPollers_FullStack` wires `RpcSequencerCommitteeProvider`,
`RpcForcedInclusionSource`, `RpcMessageRouter`, and the bridge-CLI's
`InvocationBuilder` against a **single shared in-process L1 RPC stub**
that holds real per-contract state (sequencer registrations, forced-tx
queue, L1→L2 message queue) and routes `invokefunction` by contract hash.

Drives a full L1↔L2 round-trip:
1. Operator-side L1 actions: register 3 sequencers, enqueue 2 forced-inclusion
   entries, enqueue 2 L1→L2 messages.
2. L2-side adapters poll the shared L1 RPC stub. Each adapter sees its own
   contract's state correctly.
3. Cross-checks: committee size, forced-tx deadline ordering, L1→L2 message
   types preserved, **canonical hash recomputed by `RpcMessageRouter` (never
   trusts an off-wire hash)**.
4. L2 batcher emits an outbound L2→L1 withdrawal; bridge-CLI builds the
   canonical `FinalizeWithdrawalWithProof` invocation hex.
5. L1 marks one forced-tx consumed; the L2's next poll drops it.

A second test pins that an L1 sequencer unregistration (status → 0) is
detected by the L2 poller silently — the operator's known-keys set is
allowed to drift; L1 is the source of truth.

This integration test catches regressions individual unit tests miss:
- canonical encoders' wire format mismatches between two adapters
  (forced-inclusion's `[20B sender][32B txHash]...` vs message-router's
  `[4B sourceChainId][4B targetChainId]...` — both `[Safe] byte[]` reads,
  but with different layouts)
- shared-RPC contract-routing bugs (one adapter accidentally hitting another's hash)
- recomputed-hash drift if `MessageHasher` ever changes

The shared stub mirrors the canonical contract encoders so adapter decoders
get realistic responses, not hand-written canned blobs.

Doc-set test count 1283 → 1285.

### Added — `RpcMessageRouter` production L1-RPC poller (1272 → 1283)

Closes the **third and final** "L1-RPC-backed poller (does not exist in
repo)" gap in IMPLEMENTATION_STATUS's operator-must-replace table. Every
in-memory provider that was flagged "does not exist in repo" now has a
real production sibling.

`RpcMessageRouter` is hybrid:

- **Inbound (L1→L2)**: `DequeueL1MessagesAsync` polls
  `NeoHub.MessageRouter.getL1ToL2(chainId, nonce)` + `isConsumed(hash)`
  in parallel for each known nonce. Same operator-bootstrap pattern —
  `RegisterInboundNonce` for event-driven additions. After Dequeue,
  nonces are locally marked consumed (mirrors the in-memory `L1MessageInbox.Dequeue`
  contract — subsequent calls don't re-return them).
- **Outbound (L2-internal)**: `EnqueueOutboundAsync` stages outbound
  messages in a local `L2Outbox` the L2 batcher consumes when sealing.
  No L1 RPC needed for this path.
- **Proofs**: `GetMessageProofAsync` looks up finalized inclusion proofs
  in a caller-supplied `IL2KeyValueStore` (operators wire RocksDb so
  proofs survive restarts). `RecordFinalizedProof` is wired to the L2
  settlement plugin's batch-finalized callback.

`DecodeMessage(bytes)` parses the canonical contract encoding
(`[4B sourceChainId LE][4B targetChainId LE][8B nonce LE][20B sender][20B receiver][1B messageType][4B payloadLen LE][payload]`)
and **recomputes** the canonical hash via `MessageHasher.HashMessage` —
never trusts an off-wire hash. A drift between the contract's hash
convention and the off-chain hasher would break inclusion proofs; this
keeps the two in lockstep.

10 new tests across `UT_RpcMessageRouter`:
- 2-nonce inbound round-trip nonce-ordered
- L1-consumed entries silently dropped
- Local-consume after Dequeue stays honored across calls
- Mismatched chainId rejected
- RegisterInboundNonce adds + invalidates cache
- EnqueueOutbound appends to outbox
- GetMessageProof returns recorded bytes / null for unknown hash
- DecodeMessage truncated rejection / payloadLen mismatch rejection /
  known-encoding round-trip with recomputed hash

All three L1-RPC poller gaps are now closed: `RpcSequencerCommitteeProvider`,
`RpcForcedInclusionSource`, `RpcMessageRouter`. The operator-must-replace
table no longer flags any in-memory provider as "does not exist in repo."

Doc-set test count 1272 → 1283.

### Added — `RpcForcedInclusionSource` production L1-RPC poller (1263 → 1272)

Closes the second of three "L1-RPC-backed poller (does not exist in repo)"
gaps in IMPLEMENTATION_STATUS's operator-must-replace table. Same design
as `RpcSequencerCommitteeProvider`: operator wires the genesis nonce
seed + `RegisterNonce` hook to L1's `OnForcedTxEnqueued` event watcher,
provider polls each known nonce's live state via `[Safe]` reads.

For each known nonce, issues `getEntry` + `isConsumed` in parallel via
`Task.WhenAll`. Entries L1 has marked consumed (via SettlementManager)
are silently dropped. Returns deadline-ordered list capped at the
caller's `max`.

`DecodeEntry(nonce, bytes)` parses the canonical contract encoding
(`[20B sender][32B txHash][4B txLen LE][txBytes][4B deadline LE]`) into
a typed `ForcedInclusionEntry`. Exposed `public static` for verifier
re-use + tested independently against tampered `txLen`, truncated
buffer, and known-encoding round-trip.

`MarkConsumedAsync` is local-only bookkeeping (the L1 contract's same
method is SettlementManager-driven; the L2 batcher consuming this source
just needs to remember "this nonce went into a sealed batch, don't drain
it again until the next pull").

`HasOverdueEntryAsync` walks the drain set; pin <-rather-than-≤ on the
deadline check matches `InMemoryForcedInclusionSource`'s convention
(deadline at exactly nowUnixSeconds is "due," not "overdue" — gives the
batcher one final block to include before censorship is reported).

9 new tests across `UT_RpcForcedInclusionSource`:
- 2-nonce drain deadline-ordered
- L1-consumed entry silently dropped
- max-cap respected
- local MarkConsumedAsync excludes nonce from future drains
- RegisterNonce adds + invalidates cache
- HasOverdueEntry detects deadline strictly past now (3 boundary cases)
- DecodeEntry truncated/inconsistent rejection + known-encoding round-trip

Doc-set test count 1263 → 1272.

### Added — `RpcSequencerCommitteeProvider` production L1-RPC poller (1257 → 1263)

Closes one of the three "L1-RPC-backed poller (does not exist in repo)"
gaps in IMPLEMENTATION_STATUS's "operator must replace" table. Real
production code, no toolchain dependency.

`src/Neo.L2.Sequencer/RpcSequencerCommitteeProvider` polls the deployed
`NeoHub.SequencerRegistry` contract via Neo's standard `invokefunction`
RPC. For each known sequencer key it issues `getStatus` + `getSequencerAddress`
in parallel via `Task.WhenAll`, so a 21-validator committee resolves in
one fanout per cache miss instead of 21 sequential round trips.

Design choice: the contract doesn't expose key enumeration via `[Safe]`
methods (its storage iterator is only reachable via Neo's `findstates`
RPC, which not every node exposes). To stay portable, this provider
takes its known-keys set from the operator — genesis committee in the
constructor + `RegisterKnownKey(ECPoint)` mutator the operator wires
to an L1 event-watcher (typically subscribing to `OnSequencerRegistered`
notifications). Removed sequencers are detected automatically when L1
`getStatus` returns the unregistered byte (0); they're silently dropped
from the committee snapshot. `IsRegisteredAsync` always hits L1 directly
so an operator querying registration state sees the source of truth even
if their event-watcher hasn't yet picked up a recent change.

Cache TTL is configurable (default 5s) — short enough for live status
changes to propagate, long enough that dBFT consensus can read the
committee per-block without fanning out 21 RPC calls every time. Tests
pin both fresh-fetch and cached-hit paths.

6 new tests in `UT_RpcSequencerCommitteeProvider`:
- 3-key happy path (all Active) round-trip
- A key reporting status=0 silently dropped (the operator's known-keys
  set is allowed to drift; L1 is the source of truth)
- `RegisterKnownKey` adds + invalidates cache
- `IsRegisteredAsync` always hits L1 (no shortcut from local set)
- `GetMaxCommitteeSize` cached across calls
- `InvalidateCache` forces fresh fanout

Doc-set test count 1257 → 1263.

### Added — `neo-bridge` production CLI for SharedBridge invocation hex (1243 → 1257)

`tools/Neo.L2.Bridge.Cli/` (binary name `neo-bridge`) emits canonical
Neo VM invocation scripts for `SharedBridge.Deposit` and
`SharedBridge.FinalizeWithdrawalWithProof`, plus query commands that
walk `Neo.L2.Sdk.L2RpcClient`.

Subcommands:
- `deposit` — emits the canonical Deposit invocation hex (paste into any
  Neo wallet — Neo-Express, Neon Wallet, NeoLine, custom HSM tooling —
  for signing + broadcast). Real argument validation: rejects zero
  amount and zero target chainId at script-build time, mirroring the
  contract's own guards.
- `withdraw` — emits the canonical FinalizeWithdrawalWithProof invocation
  hex, automatically fetching the per-leaf Merkle proof from the L2 RPC
  endpoint via the SDK and decoding it into the contract's expected
  `byte[][] siblings` parameter shape.
- `query-deposit` — looks up an L1 deposit's L2-side consumption status
  (consumedOnL2 + includedInBatch).
- `audit-withdrawal` — fetches a proof + verifies its structure (header
  consistency, sibling count) before paying L1 gas to submit it.

`InvocationBuilder` uses `Neo.Extensions.VM.ScriptBuilderExtensions.EmitDynamicCall`
with `ContractParameter` wrappers for the nested `byte[][]` siblings —
the canonical wire shape Neo's RPC `invokefunction` consumes.
`MerkleProofDecoder` uses `Neo.L2.State.MerkleProofSerializer`'s 48-byte
header layout (32 leaf + 4 leafIndex + 8 pathBitmap + 4 siblingCount +
32×N siblings, leaf-to-root order matching SettlementManager's
verification walk).

No built-in signer: production deployments use HSMs / cold wallets. The
CLI prints the invocation hex for the operator's wallet to sign and
submit — same plan-printer pattern as `neo-stack register-chain`.

14 tests across 2 files in `tests/Neo.L2.Bridge.Cli.UnitTests/`:
- `UT_InvocationBuilder` (8) — script reproducibility, different inputs
  produce different scripts (catches a regression where an arg is
  ignored), embedded method-name string check, argument validation
  (zero amount / zero chain id / zero target chain).
- `UT_MerkleProofDecoder` (5) — round-trip via real
  `MerkleProofSerializer.Encode`, single-leaf empty-siblings edge case,
  truncated-header rejection, size-vs-count rejection, leaf-to-root
  order preservation.

CLI count 4 → 5; project count 32 → 33; doc-set test count 1243 → 1257.

### Added — Two production-grade `IRoundProver` implementations for Phase-5 aggregation (1227 → 1243)

`Neo.Plugins.L2Gateway` ships two new production aggregators alongside
the existing `PassThroughRoundProver` reference, flipping Phase 5 from
🟡 to ✅:

- **`MultisigRoundProver`** — committee-attested aggregation. Each
  round computes a canonical message (domain-tag + backend-id +
  length-prefixed left/right roots and proof bytes) and threshold-signs
  it via the same `ISignerSet` / `MultisigProofPayload` infrastructure
  used by the Stage-0 attestation prover. Threshold is configurable;
  `Combine` rejects rounds where insufficient signatures arrive rather
  than producing a silently-weak aggregate. Static `VerifyRound` checks
  threshold validity, validates each signer is in the canonical set,
  rejects duplicates and out-of-set signatures, and re-derives the
  combined message hash to detect tampered children.
- **`MerklePathRoundProver`** — per-constituent inclusion proofs. Each
  round commits to its two children's roots and recursively wraps the
  subtree path bytes; the final aggregate root is equivalent to a
  Merkle root over each batch's `L2ToL2MessageRoot`. `ProveLeaf`
  extracts a canonical sibling-list inclusion proof; `VerifyLeaf`
  re-derives the aggregate root by hashing the leaf up through the
  siblings using directions re-computed from `(leafIndex, totalLeaves)`.
  Handles odd-cardinality trees correctly (trailing leaf promoted
  through odd levels — bit-pattern indexing alone wouldn't work since
  depth varies per leaf in unbalanced trees).

Real cryptography in both — Secp256r1 ECDSA + Hash256 / Merkle, no
toolchain dependency, no scaffolding. The recursive-ZK fold variants
(SP1 Compress / Halo2 / Risc0) plug into the same `IRoundProver` seam
when an operator brings their toolchain; that's now correctly framed
in `IMPLEMENTATION_STATUS.md` as "operator brings the toolchain for
recursive-ZK aggregation," not "framework only ships scaffolding."

16 new tests across 2 files (`UT_MultisigRoundProver` + `UT_MerklePathRoundProver`):
- Multisig: full-committee verifies; threshold raised above signer count
  rejects; tampered right-child rejects; outside-set signatures rejected;
  odd-trailing-child promoted unchanged; deterministic signer ordering
  (sorted by pubkey, since ECDSA itself is non-deterministic); ctor
  rejection of out-of-bounds threshold; end-to-end aggregator pipeline.
- MerklePath: 4-leaf and 8-leaf round-trip (every leaf provable); altered
  leaf and wrong-index claims both rejected; 5-leaf odd-cardinality
  every leaf provable (including trailing promoted leaf); single-leaf
  edge case; out-of-range leaf index throws.

Phase 5 row in IMPLEMENTATION_STATUS + tech-stack-coverage flips
🟡 → ✅; coverage table 62/54 → 62/55, 🟡 count 2 → 1.

Doc-set test count 1227 → 1243 across the standard files.

### Added — `neo-l2-explore` terminal block explorer + state-root continuity audit (1213 → 1227)

`tools/Neo.L2.Explore/` ships a 4-subcommand CLI (`label`, `batch <n>`,
`tail [N]`, `audit [N]`) that wraps `Neo.L2.Sdk.L2RpcClient` to give
operators + dApp developers a fast terminal-side view into any node
running `Neo.Plugins.L2Rpc`. Closes the Layer-5 "block explorer" 🔴
row in `docs/tech-stack-coverage.md`'s end-user-interfaces table for
the CLI dimension; a graphical explorer remains intentionally out-of-repo
since it's a different stack (TS/React) and operator-deployment-specific.

The unique capability is `audit [N]`: walks the last N sealed batches
and verifies state-root continuity (each batch's `preStateRoot` MUST
equal the previous batch's `postStateRoot`). On-chain settlement
enforces this for finalized batches, but a misbehaving / pre-finalization
sequencer can ship continuity-violating batches; the audit catches them
before settlement does. Distinct exit codes:
  - `0` — chain continuous, prints the count of consecutive pairs verified
  - `1` — caller error (missing/invalid args)
  - `2` — chain has no sealed batches (or audit start beyond head)
  - `4` — continuity violation found; stderr names the offending batch
        + prints both state roots so the operator sees the gap

14 tests across 4 files in `tests/Neo.L2.Explore.UnitTests/`:
- `UT_LabelCommand` (3) — full label printout + missing-arg paths
- `UT_BatchCommand` (3) — full canonical-commitment printout + status
  + not-found / missing-number rejection paths
- `UT_TailCommand` (4) — default-5 vs explicit-N depth, descending order
  pin, no-batches and only-genesis edge cases
- `UT_AuditCommand` (4) — continuous chain accepts, deliberately-injected
  discontinuity at batch #3 surfaces as exit-4 with the offending batch
  named in stderr, count-under-2 rejection, default-count-is-10 pin

Each test uses a `StubBackedClient` that wires the SDK's HTTP request
through an in-memory bridge handler to a real `L2RpcMethods` dispatcher
(no listener; same pattern as `UT_L2RpcClient_ContractWithServer`) — so
the tests exercise the full request/response cycle through the SDK + the
real server logic, not canned responses.

Project count 31 → 32, test count 1213 → 1227. Doc-set updated:
README ("3 CLI tools" → "4 CLI tools" + neo-l2-explore in the list),
tech-stack-coverage (Layer-3 subcommand-count tally + Layer-5 split row +
totals 61 → 62 / 53 → 54), IMPLEMENTATION_STATUS, AGENTS, CONTRIBUTING,
getting-started.

### Added — `Neo.L2.Sdk` typed app-developer client (1188 → 1213)

Closes the Layer-4 🔴 row in `docs/tech-stack-coverage.md`. Operators who
build dApps against an L2 node can now reference one NuGet-style project
instead of hand-rolling JSON-RPC envelope code per repo.

Public API:
- `L2RpcClient` — async client exposing all 10 `doc.md §14.1` RPC methods
  as strongly-typed methods (no `JArray` / `JObject` in the public surface).
  Constructor takes `(endpoint, chainId, httpClient?)`; the chainId is
  cross-checked against every response field that includes one.
- Typed responses: `L2BatchView`, `BatchStatusResponse`, `DepositStatusResponse`,
  `SecurityLevelResponse`, `SecurityLabelResponse` (full §16.2 5-dimension label).
- Failure-mode split into 4 distinct exception types so callers can write
  targeted retry policy:
  - `L2RpcTransportException` — HTTP-layer (timeout, refused, non-2xx)
  - `L2RpcProtocolException` — bad envelope, parse error, mismatched id
  - `L2RpcServerException` — JSON-RPC `error` field (carries the int code)
  - `L2RpcMismatchedChainIdException` — server returned a different chainId
    than the client was constructed with (config error, not silently consumed)

25 tests across 3 files in `tests/Neo.L2.Sdk.UnitTests/`:
- `UT_L2RpcClient_HappyPath` (12 tests, canned-response wire shape pins for
  every method including param ordering, hex-encoded byte decoding, and
  enum byte / name decoding for security label dimensions)
- `UT_L2RpcClient_ErrorPaths` (6 tests, each failure mode pinned to its
  exception type + message; ctor rejection of bad scheme / empty / zero
  chainId / relative URI)
- `UT_L2RpcClient_ContractWithServer` (7 tests, end-to-end via an in-memory
  bridge handler that wires the SDK's HTTP request to a real
  `L2RpcMethods` dispatcher — no listener, but exercises both sides of
  the wire contract through actual server logic; catches a SDK ↔ server
  drift in UInt160 / UInt256 / hex-byte encoding that canned-response
  tests would miss)

Project count 30 → 31, test count 1188 → 1213. Doc-set updated:
README, IMPLEMENTATION_STATUS, AGENTS, CONTRIBUTING, getting-started,
tech-stack-coverage (Layer 4 ✅ row added; coverage table 60 → 61 / 52 → 53).

### Added — Direct unit-test coverage for `ArgUtil` parser (1178 → 1188)

`ArgUtil` is the tiny CLI argument parser shared by every neo-stack
subcommand (`Get(args, name, default)` and `HasFlag(args, name)`).
It was previously exercised only indirectly via per-command tests —
a regression that changed `Get`'s "first occurrence wins" or "trailing
flag without value returns default" semantics could ship undetected
if it didn't trip a specific command's assertion.

10 new tests in `UT_ArgUtil` pin every observable behavior:
- `Get` happy path + missing flag + empty args
- `Get_FlagAtEndWithoutValue_ReturnsDefault` (the loop's `i < args.Length - 1`
  bound)
- `Get_DuplicateFlag_ReturnsFirstOccurrence` (first-wins semantics)
- `Get_EqualsSyntaxNotSupported_FallsBackToDefault` (only `--flag value`,
  not `--flag=value`; pin so a refactor adding equals-form is an explicit
  feature, not a silent semantic change)
- `HasFlag` present / missing / empty args / value-of-another-flag
  (linear scan with no positional awareness)

Each non-trivial case names what it pins and why, so a future contributor
reading the test sees the documented intent without git-blaming a refactor.

Doc-set: test count 1178 → 1188 across the standard files.

### Added — Pin `new-l2` composite emits zero cross-field warnings per template (1174 → 1178)

`UT_NewL2Command.NewL2_EachTemplate_InternalValidateEmitsZeroCrossFieldWarnings`
is parametric across all 4 templates: it captures stdout while
`NewL2Command.Run` executes (which internally invokes `create-chain` →
`validate` → `init-l2` → `scaffold-executor`), then asserts the captured
stdout contains no `⚠` character.

Stronger guard than the prior `ValidateStep_RunsAfterCreateChain_AcceptsTemplateOutput`
test, which only checked exit-code 0. Cross-field warnings are informational
(rc stays 0 even when fired), so the prior test would have missed a template
default drifting into a warning-but-not-error state.

This pins the create-chain ↔ validate contract along the composite (`new-l2`)
bring-up path — symmetric to the same contract being pinned for the standalone
`create-chain` bring-up path in `UT_CreateChainCommand`. Both paths are now
covered.

Doc-set: test count 1174 → 1178 across the standard files.

### Added — Pin every `--template` produces a config `validate` accepts cleanly (1170 → 1174)

`UT_CreateChainCommand.CreateChain_EachTemplate_PassesValidateWithNoWarnings`
is parametric across all 4 templates (`rollup` / `zk-rollup` / `validium` /
`sidechain`): for each, run `create-chain --template T` then run
`validate` against the produced config, asserting `rc=0` + zero `⚠`.

Complements the samples-walk test (which checks the hand-maintained
`samples/*.config.json` files) by exercising the actual JSON-emission
path through `CreateChainCommand` + `TemplateCatalog`. If a future
edit introduces a contradiction in `TemplateCatalog.All`, OR a new
`validate` warning incidentally fires on a template default, OR a
template-resolution refactor produces a config field-set that doesn't
parse, this test catches it before an operator runs the Quick path.

This pins the create-chain ↔ validate contract bidirectionally:
neither side can drift without the other being updated to match.

Doc-set: test count 1170 → 1174 across the standard files.

### Added — Pin all 4 shipped samples pass `validate` with zero warnings (1169 → 1170)

The 4 sample chain configs (`samples/general-rollup`,
`samples/gaming-rollup`, `samples/exchange-validium`,
`samples/privacy-sidechain`) ship as canonical references — operators
copy them directly into their own deployments. If a future edit
introduces a cross-field contradiction in any sample, every operator
who copied it inherits the contradiction.

`UT_ValidateChainConfigCommand.Validate_AllShippedSamples_HaveNoCrossFieldWarnings`
walks `samples/` and runs the actual `ValidateChainConfigCommand` path
against each `.config.json`, asserting `rc=0` and zero `⚠` characters
in stdout. If a future commit adds a new validate warning that
incidentally fires on an existing sample (e.g. a new chainMode×SequencerModel
contradiction we add later that one of the samples trips), this test
catches it before the sample ships broken.

This also indirectly pins that the sample defaults stay in sync with
the cross-field rules — e.g. swapping `general-rollup`'s `chainMode`
to `SidechainMode` without also lowering `securityLevel` would now
fail the test.

Doc-set: test count 1169 → 1170 across the standard files.

### Added — `validate` flags chainMode × securityLevel pairing mismatches (1166 → 1169)

Per `doc.md` §6, each `chainMode` has a canonical set of compatible
`SecurityLevel`s based on consensus + DA semantics:

  - `SidechainMode` → `{Sidechain, Settled}` (no L1 proof verification)
  - `L2RollupMode`  → `{Optimistic, Validity}` (L1 proof + on-chain DA)
  - `L2ValidiumMode`→ `{Validium}` (L1 ZK proof + off-chain DA)

Any deviation is internally contradictory: a `SidechainMode` chain
claiming `Validity` promises L1 ZK verification it never delivers; a
`L2RollupMode` chain claiming `Validium` contradicts the rollup property
of on-chain DA; a `L2ValidiumMode` chain claiming `Optimistic` would
need on-chain DA the validium definition rules out. `validate` now
flags all three buckets with a `⚠` warning naming the canonical set.

3 new tests in `UT_ValidateChainConfigCommand`:
- `Validate_CrossFieldWarning_RollupModeWithValidiumLevel`
- `Validate_CrossFieldWarning_SidechainModeWithValidityLevel`
- `Validate_CrossFieldWarning_ValidiumModeWithOptimisticLevel`

Also tightened 2 existing "no-warning" tests
(`Validate_CrossFieldNoWarning_SidechainWithNone` and
`Validate_CrossFieldNoWarning_ValidiumWithZk`) to align chainMode with
their securityLevel — they were previously passing on internally-inconsistent
configs because the chainMode×securityLevel axis was unchecked. With the
new warning, the canonical-pair test cases now exercise canonical chainModes.

This closes the chainMode × securityLevel cross-field gap; combined with
the prior chainMode × DAMode and ExitModel × permissionlessExit warnings,
every two-field consistency dimension that has a hard contradiction is
now flagged at validate time rather than at L1-registration time.

Doc-set: test count 1166 → 1169 across the standard files.

### Added — `validate` flags chainMode=L1Mode in an L2 config (1165 → 1166)

`Neo.L2.ChainMode` distinguishes `L1Mode` (the actual Neo L1 — "Plain
Neo L1" per the enum's doc-comment) from the L2 modes (`L2RollupMode`,
`L2ValidiumMode`, `SidechainMode`). A `chain.config.json` describes an
L2 chain by definition, so `chainMode=L1Mode` in there is internally
contradictory — an operator who copy-pasted a wrong template could ship
a config the framework would still parse cleanly. `validate` now flags
this with a `⚠` warning + a remediation hint pointing at the three
valid L2 modes.

1 new test in `UT_ValidateChainConfigCommand`:
- `Validate_CrossFieldWarning_L1ModeInL2Config` — pins the warning text
  + that exit-code stays 0 (informational, not fatal).

Closes the last cross-field consistency gap on the `chainMode` axis;
combined with the prior Validium-vs-L1-DA + ExitModel-vs-permissionlessExit
warnings, every §6 chainMode + §16.2 dimension that has a hard contradiction
is now flagged.

Doc-set: test count 1165 → 1166 across the standard files.

### Added — `validate` flags ExitModel.OperatorAssisted vs permissionlessExit=true contradiction (1163 → 1165)

Per `Neo.L2.ExitModel`'s doc, `OperatorAssisted` means "user exit
requires the operator to co-sign or pre-stage exit batches" — the
opposite of permissionless. A chain config claiming BOTH
`exitModel=OperatorAssisted` AND `permissionlessExit=true` is
internally contradictory: the chain is promising users a guarantee
it can't deliver. `validate` now flags this with a `⚠` warning + a
remediation hint.

2 new tests in `UT_ValidateChainConfigCommand`:
- `Validate_CrossFieldWarning_OperatorAssistedExitWithPermissionlessTrue`
  — pins the new contradiction warning.
- `Validate_CrossFieldNoWarning_OperatorAssistedExitWithPermissionlessFalse`
  — pins that the consistent pair (OperatorAssisted + false) does NOT
  warn (catches a regression that fires on every OperatorAssisted
  config regardless of the bool).

Doc-set: test count 1163 → 1165 across the standard files.

### Added — `validate` parses chainMode + flags Validium-vs-L1-DA contradiction (1159 → 1163)

`ValidateChainConfigCommand` previously ignored the `chainMode` field
entirely — operators could type `"chainMode": "garbage"` and pass
silently. Now `chainMode` is required + parsed as the canonical
`Neo.L2.ChainMode` enum (L1Mode / SidechainMode / L2RollupMode /
L2ValidiumMode), so unparseable values reject with the standard
"expected one of: ..." diagnostic.

Plus a new cross-field consistency warning for the
`L2ValidiumMode` + `DAMode.L1` contradiction. Validium chains by
definition store transaction data off-chain (per doc.md §6 + §12); a
config claiming both `L2ValidiumMode` and `daMode: L1` is internally
contradictory — the operator probably wanted either `L2RollupMode +
L1` or `L2ValidiumMode + NeoFS/External/DAC`.

The success summary line also gained `chainMode={chainMode}` so the
operator-facing output reflects all parsed dimensions.

4 new tests in `UT_ValidateChainConfigCommand`:
- `Validate_UnknownChainMode_ExitsTwo` — typo in chainMode rejected.
- `Validate_MissingChainMode_ExitsTwo` — required field.
- `Validate_CrossFieldWarning_ValidiumModeWithL1DA` — pins the new
  contradiction warning.
- `Validate_CrossFieldNoWarning_ValidiumModeWithNeoFS` — canonical
  validium template (NeoFS DA) emits no contradiction warning.

Doc-set: test count 1159 → 1163 across the standard files.

### Added — `validate` cross-field consistency for Validium + Sidechain SecurityLevels (1156 → 1159)

`ValidateChainConfigCommand`'s cross-field consistency checks only
covered two of the four `SecurityLevel` dimensions: Validity and
Optimistic. An operator who typed e.g. `securityLevel: Validium` with
`proofType: Optimistic` (mismatched) got no warning — silent
acceptance of a clearly-wrong config.

Added two parallel warnings:
- `securityLevel=Validium` → expects `proofType=Zk` (validity-proof
  paradigm; off-chain DA doesn't change the proof system).
- `securityLevel=Sidechain` → expects `proofType=None` or
  `Multisig` (sidechains usually don't run a prover).

3 new tests in `UT_ValidateChainConfigCommand`:
- `Validate_CrossFieldWarning_ValidiumWithNonZkProof` — Validium +
  Optimistic proof emits the warning + still exits 0.
- `Validate_CrossFieldWarning_SidechainWithUnusualProof` — Sidechain +
  Zk proof emits the warning naming valid alternatives.
- `Validate_CrossFieldNoWarning_ValidiumWithZk` — canonical Validium +
  Zk pair (the validium template default) does NOT emit a warning.

Doc-set: test count 1156 → 1159 across the standard files.

### Fixed — `init-l2` now defends-in-depth against missing chain.config.json (1149 → 1150)

`InitL2Command` previously only checked the chain dir exists, not
`chain.config.json` inside it. So an operator who:
- created a temp dir manually (e.g. `mkdir ./my-l2`)
- forgot `neo-stack create-chain`
- ran `neo-stack init-l2 --output ./my-l2`

…would silently get `data/` `logs/` `Plugins/` subdirs created against a
chain dir that has no chain.config.json. Then `register-chain` /
`start-*` / devnet preview would fail with confusing "Missing config"
errors despite init-l2 having reported success.

Fix: init-l2 now also checks chain.config.json exists. Returns exit 2
(distinct from 1=chain dir missing) so a CI script can disambiguate the
two missing-prerequisite cases. Diagnostic message names the exact
missing path + points at create-chain.

1 new test `InitL2_MissingChainConfig_ExitsTwo` pins the new check:
chain dir present + no config → exit 2 + "Missing chain.config.json"
diagnostic + abort BEFORE creating the working subdirs (operator's
existing dir contents stay clean).

5 existing init-l2 tests updated to write a placeholder
`chain.config.json` before invoking init-l2 (they previously relied on
the old lax behavior). Extracted a `SeedChainDirWithConfig` helper to
keep the setup pattern consistent.

Doc-set: test count 1149 → 1150 across the standard files.

### Added — `DevnetLabelOverrides` extraction + per-field fallback tests (1140 → 1149)

The devnet's `--config <chain.config.json>` reader was the last untested
piece of `Neo.L2.Devnet/Program.cs`'s flag-handling surface. The reader
has substantial behavior worth pinning: null-config → defaults; missing
file → defaults + warning; malformed JSON → defaults + warning; partial
config → per-field defaults; unknown enum value in any field → that
field's default. None of those paths were tested.

Refactored: extracted the private `LabelOverrides` record + private
`ReadLabelOverrides` + private `ParseEnumOrDefault` from `Program.cs`
into a public `Neo.L2.Devnet.DevnetLabelOverrides` record struct (with
`Defaults`, `ReadFromConfig`, `ParseEnumOrDefault` as public statics).
Same shape as the previous `DevnetArgs` extraction.

9 new tests in `UT_DevnetLabelOverrides`:
- `Defaults_MatchInMemoryStoreSaneDefaults` — pins the canonical default
  values (Optimistic / External / DbftCommittee / Permissionless / off).
- `NullPath_ReturnsDefaults`.
- `NonExistentPath_ReturnsDefaults_WithWarning` — devnet should still
  run; the operator gets a "not found, falling back to defaults" warning
  instead of a stack trace.
- `MalformedJson_ReturnsDefaults_WithWarning` — same shape: parse
  failure surfaces a warning + falls back, doesn't abort.
- `ValidConfig_AllFieldsOverride` — validium-template-shaped config
  overrides every dimension correctly.
- `PartialConfig_MissingFieldsUseDefaults` — per-field independence: a
  config with only securityLevel + daMode set still produces a valid
  overrides record with the other three fields at defaults.
- `UnknownEnumValue_FallsBackToFieldDefault` — typo in one field falls
  back for THAT field; other valid fields still apply.
- `ParseEnumOrDefault_PerFieldBehavior` — direct test of the per-field
  parser (missing / valid / invalid).
- `EnumIsCaseSensitive` — lowercase "validium" does NOT match
  `Validium` (devnet's permissive-fallback returns the default; the
  case-strict diagnostic lives in `neo-stack validate`).

Doc-set: test count 1140 → 1149 across the standard files.

### Added — `Neo.L2.Devnet` argument parser tests + extracted `DevnetArgs` (1124 → 1140)

The devnet was the third CLI binary without dedicated tests. Its
argument parsers (`--metrics-port` / `--data-dir` / `--config` /
`--executor`) had no coverage. Operators relying on `--executor counter`
or `--metrics-port 9090` got behavior pinned only by manual smoke tests
— a regression in the parser would surface only at the operator's first
run.

Refactored: extracted the four flag parsers from `Program.cs` into a new
public `Neo.L2.Devnet.DevnetArgs` static class (parallel shape to the
`scaffold` / `plan` / `verify` extractions in `neo-hub-deploy`). Program
now consumes `DevnetArgs.ParseMetricsPort` etc. directly.

New `tests/Neo.L2.Devnet.UnitTests/` project (project count 29 → 30) +
16 tests in `UT_DevnetArgs`:
- `--metrics-port`: valid value returned (incl. boundaries 0 + 65535);
  absent → null; non-numeric → null (legacy-script-friendly: not a
  hard error); out-of-range (-1 / 65536) → throws via PortValidator.
- `--data-dir` / `--config`: value returned; absent → null.
- `--executor`: defaults to `reference` when absent; recognizes
  `reference` + `counter`; unknown values fall back to `reference`
  with a `Console.Error` warning naming both valid values (catches
  typos like `counte` so they don't silently swap executors).
- All four parsers reject null `args` at the boundary.

Doc-set: test count 1124 → 1140; project count 29 → 30 across
AGENTS.md / CONTRIBUTING.md / README.md / IMPLEMENTATION_STATUS.md /
docs/getting-started.md / docs/tech-stack-coverage.md.

### Added — `neo-hub-deploy scaffold` + `plan` subcommand tests (1114 → 1124)

The two remaining `neo-hub-deploy` subcommands (after `verify` got
extracted last week) without dedicated tests. With this commit, all
three `neo-hub-deploy` subcommands have dedicated UT_*Command tests
pinning their exit-code contracts + operator-facing diagnostics.

Refactored: extracted `RunScaffold` / `RunPlan` from `Program.cs` into
public `ScaffoldCommand` + `PlanCommand` classes (parallel shape to
`VerifyCommand` from the previous round). `Program.cs` now just
dispatches `scaffold` → `ScaffoldCommand.Run` and `plan` →
`PlanCommand.Run`; the private `RunScaffold` / `RunPlan` /
`DeterministicStubHash` are gone.

`UT_ScaffoldCommand` (4 tests):
- Happy path writes a parseable 15-step plan to `--output`.
- Default `--output` (`./deploy-plan.json`) used when omitted.
- Null args rejected at boundary.
- Unwriteable output (target is a directory) → exit 1 + "failed to
  write" diagnostic in stderr.

`UT_PlanCommand` (6 tests):
- Happy path emits a 15-invocation bundle (matching the scaffold's
  step count) to `--output`.
- Plan file not found → exit 1 + "plan file not found" diagnostic.
- Malformed plan JSON → exit 1 + "failed to parse" diagnostic.
- Stdout includes "Required post-deploy actions:" with all the wiring
  hints (RegisterSlasher / SetGovernanceController / both fraud-verifier
  notes) — pin so a refactor that drops the post-deploy summary doesn't
  silently break the operator's deployment guidance.
- Stdout includes the ⚠ "deterministic stubs" warning so operators
  don't try to deploy the plain bundle without routing through their
  wallet's signer.
- Null args rejected at boundary.

`Neo.Hub.Deploy.UnitTests` per-row 33 → 43. Test count 1114 → 1124
across the doc-set.

### Added — `init-l2` + `submit-batch` subcommand tests (1100 → 1114)

The last two CLI subcommands without dedicated tests now have them. Every
`neo-stack` subcommand is now backed by a UT_*Command test file pinning
its exit-code contract + operator-facing diagnostics.

`UT_InitL2Command` (8 tests):
- Happy path creates `data/`, `logs/`, `Plugins/` subdirs.
- Non-numeric `--chain-id` → exit 1.
- chainId=0 → throws (L1 sentinel reject).
- Missing chain dir → exit 1 + "Run create-chain first" diagnostic
  (operator-friendly remediation).
- `--path` continues to work (backwards-compat pin).
- `--output` takes precedence over `--path` (matches the
  register-chain / start-* / deploy-bridge-adapter precedence — different
  from create-chain's inverted convention).
- `--da` flag surfaces in the operator-facing summary line.
- Re-run is idempotent — operator's `data/` contents survive a second
  init-l2 run (catches a refactor that switches to recursive-delete-
  then-create).

`UT_SubmitBatchCommand` (6 tests):
- Happy path decodes a real `BatchSerializer.Encode` output and prints
  chainId / batchNumber / blocks / proofType / "Validation passed".
- Missing `--file` flag → exit 1 (caller error).
- File not found → exit 2 + "batch file not found" diagnostic.
- Malformed bytes → exit 4 + "batch decode failed" + "Submit aborted"
  (distinct exit code so a CI script can disambiguate caller-error
  from decode-fail).
- Round-trips through every ProofType (Multisig / Optimistic / Zk) so
  the CLI's preflight is symmetric with BatchSerializer's own tests.
- All four state roots surface in the operator-facing audit output.

This finishes the multi-iteration CLI test sweep. All 12 `neo-stack`
subcommands now have dedicated UT_*Command files. Test count
1100 → 1114 across the doc-set; Neo.Stack.Cli.UnitTests per-row 83 → 97.

### Added — `create-chain` subcommand tests for the previously-untested writer (1088 → 1100)

`create-chain` is the first command an operator runs (it writes the
chain.config.json every other command consumes), and it's auto-invoked
by `new-l2`. It had ZERO direct tests — `new-l2` exercised it
indirectly but didn't pin per-template dimensions or operator-facing
diagnostics.

12 new tests in `UT_CreateChainCommand`:
- Default-args happy path emits valid config with rollup defaults.
- Non-numeric `--chain-id` → exit 1 + atomic abort (no output dir
  created on failure).
- chainId=0 → throws (L1 sentinel reject).
- Per-template dimension pins for all 4 templates: rollup +
  zk-rollup (Validity + Zk + Permissionless + gateway-on) +
  validium (NeoFS DA + Delayed exit + permissionlessExit=false) +
  sidechain (None proof + External DA).
- Unknown template falls back to rollup defaults but preserves the
  operator-typed name verbatim in the JSON (so they can grep for the
  typo).
- `--vm` flag propagates to the JSON.
- `--path` takes precedence over `--output` (the inverted convention
  unique to create-chain — pin so a refactor doesn't reverse it).
- Default-style `./chain-<id>` output works.
- Emitted JSON is parseable via System.Text.Json (catches a malformed
  template that would only fail at the operator's first `validate`
  run).
- "Next: neo-stack init-l2 --chain-id N" hint is printed (catches a
  refactor that drops the operator-facing next-step pointer).

Doc-set: test count 1088 → 1100 across the standard files;
Neo.Stack.Cli.UnitTests per-row 71 → 83.

### Added — `validate` chain.config.json sanity-check tests (1075 → 1088)

The `validate` subcommand had ZERO tests despite being a routinely-used
operator helper (also auto-invoked by the `new-l2` composite's
defense-in-depth step). Any regression in the JSON-shape contract or
exit-code semantics would have leaked through.

13 new tests in `UT_ValidateChainConfigCommand`:
- Happy path: rollup template config → exit 0 + ✅ valid summary line.
- No args → exit 1 (caller error, distinct from validation failure).
- File not found → exit 2 + "file not found" diagnostic.
- Unparseable JSON → exit 2 + ❌ diagnostic.
- Missing chainId / gatewayEnabled → exit 2 + "missing 'X'" diagnostic
  (operator-friendly: names the failing field).
- chainId 0 → exit 2 (L1 sentinel reject).
- Unknown enum values (securityLevel / daMode / sequencerModel) →
  exit 2 + "expected one of:" listing valid values.
- Cross-field warning: `securityLevel=Validity` + non-Zk proof emits a
  ⚠ note but still exits 0 (operator-informational, not fatal).
- Cross-field non-warning: consistent pairs (Optimistic + Optimistic;
  Sidechain + None) do NOT emit warnings (catches a regression that
  emits warnings on every config).

Doc-set: test count 1075 → 1088 across the standard files;
Neo.Stack.Cli.UnitTests per-row 58 → 71.

### Added — register-chain configBytes hex round-trip pin (1074 → 1075)

The existing `Register_WithFourHashes_EmitsConfigBytesHex` test only
checked that the announcement strings appear in stdout — a refactor that
broke the actual hex (e.g. byte ordering, casing, separators) would still
pass. The new `Register_WithFourHashes_EmittedHex_DecodesBackToValidConfig`
test extracts the hex via regex, decodes it through
`L2ChainConfigSerializer.Decode`, and pins:
- exact 91 bytes (the canonical wire format size)
- ChainId round-trips to 1099
- Each §16.2 dimension matches the rollup template defaults
  (Optimistic / L1 DA / DbftCommittee / Delayed exit / gateway off /
  permissionless exit)
- The four UInt160 hashes round-trip too (operator hash[0] = 0xaa,
  verifier = 0xbb, bridge = 0xcc, message = 0xdd)

Catches a refactor that breaks the operator-pasteable round-trip — the
existing StringAssert.Contains tests would still pass even if the hex
itself were wrong (e.g. byte-order swap, extra prefix, uppercase).

Doc-set: test count 1074 → 1075 across the standard files;
Neo.Stack.Cli.UnitTests per-row 57 → 58.

### Fixed — `deploy-bridge-adapter` now accepts `--output` + does the same chain-config preflight (1067 → 1074)

Last of three plan-printer commands that silently ignored `--output`. The
documented Quick path passes `--output` to every step:

```
neo-stack deploy-bridge-adapter --chain-id 1099 --output ./my-l2
```

The command was chain-id-only and silently dropped the `--output` value
(no error, no warning, just emitted a generic plan that wasn't tied to
any actual chain dir).

Fix: accept both `--output` and `--path` (with `--output` precedence,
mirroring `register-chain` + `start-*` + `init-l2`). When supplied,
preflight-check `chain.config.json` exists in the chain dir — same shape
as `register-chain`. Without `--output`/`--path`, the command keeps its
previous behavior (chain-id-only, generic plan) so existing scripts
don't break.

7 new tests in `UT_DeployBridgeAdapterCommand`:
- `DeployBridge_NoChainDir_ChainIdOnly_ExitsZero` — backwards-compat path.
- `DeployBridge_WithOutput_ConfigExists_ExitsZero` — preflight passes.
- `DeployBridge_WithOutput_ConfigMissing_ExitsTwo` — preflight catches
  the operator-forgot-create-chain misconfig with a clear diagnostic.
- `DeployBridge_AcceptsPathFlag_BackwardsCompat`.
- `DeployBridge_OutputTakesPrecedenceOverPath` — bogus `--path` doesn't
  poison the run when `--output` is supplied.
- `DeployBridge_NonNumericChainId_ExitsOne` — caller error.
- `DeployBridge_ChainIdZero_Throws` — L1 sentinel reject.

This finishes the three-iteration sweep of `--path`-only bugs in CLI
commands. The documented 5-command Quick path now works end-to-end with
`--output` consistently across `init-l2` / `register-chain` /
`deploy-bridge-adapter` / `start-{sequencer,batcher,prover}`.

Doc-set: test count 1067 → 1074 across the standard files;
Neo.Stack.Cli.UnitTests per-row 50 → 57.

### Fixed — `register-chain` now accepts `--output` (1060 → 1067)

Same `--path`-only bug as last iteration's `start-{sequencer,batcher,prover}`
fix, but in `register-chain`. The documented Quick path:

```
neo-stack register-chain --chain-id 1099 --output ./my-l2 \
    --operator <hash> --verifier <hash> --bridge <hash> --message <hash>
```

…silently fell back to `./chain-1099` because the command only checked
`--path`. Operators following the documented walkthrough either ran
against the wrong directory or got "Missing config" depending on whether
`./chain-1099` happened to exist.

Fix: accept both `--output` and `--path`, with `--output` taking
precedence (matches `InitL2Command` + `StartCommandPreflight` + the
`new-l2` composite + the `create-chain` primary flag). `--path`
continues to work for backwards compat.

Also bumped a stale doc comment in `StubCommands.cs` from "all 8
neo-stack subcommands are functional" → "all 12" (we shipped
`scaffold-executor` / `new-l2` / `list-templates` / `validate` since
the original 8).

7 new tests in `UT_RegisterChainCommand` pin every path:
- `Register_HappyPath_PlanOnly_ExitsZero` (no four-hash flags → plan-only).
- `Register_NonNumericChainId_ExitsOne` (caller error).
- `Register_ChainIdZero_Throws` (L1 sentinel reject).
- `Register_MissingConfig_ExitsTwo` (no chain.config.json → "run
  create-chain first" diagnostic).
- `Register_AcceptsPathFlag_BackwardsCompat`.
- `Register_OutputTakesPrecedenceOverPath` (bogus `--path` doesn't
  poison the run when `--output` is supplied).
- `Register_WithFourHashes_EmitsConfigBytesHex` (verifies the canonical
  91-byte configBytes hex appears in stdout when all four UInt160 flags
  are supplied).

Doc-set: test count 1060 → 1067 across the standard files;
Neo.Stack.Cli.UnitTests per-row 43 → 50.

### Fixed — `start-{sequencer,batcher,prover}` now accept `--output` (1051 → 1060)

The 5-command Quick path documented in `docs/launching-an-l2.md`:

```
neo-stack start-sequencer --chain-id 1099 --output ./my-l2 &
neo-stack start-batcher  --chain-id 1099 --output ./my-l2 &
neo-stack start-prover   --chain-id 1099 --output ./my-l2 &
```

…actually fails. `StartCommandPreflight.Verify` only accepted `--path`,
so any operator following the documented walkthrough with `--output`
silently fell back to the default `./chain-1099` path (which doesn't
exist) and got "Chain dir not found".

Fix: accept both `--output` and `--path`, with `--output` taking
precedence when both are supplied (matches `InitL2Command`'s pattern,
mirrors the create-chain primary flag). `--path` continues to work for
backwards compat.

9 new tests in `UT_StartCommands` pin every preflight + flag-routing
path:
- Happy path for each of the three start-{sequencer,batcher,prover}
  commands (exits 0).
- Non-numeric `--chain-id` → exits 1 (caller error).
- `--chain-id 0` throws (L1 sentinel reject).
- Missing chain dir → exits 2.
- Missing chain.config.json → exits 3 (distinguishes "no chain dir"
  from "chain dir but no config" so the operator's diagnostic is
  precise).
- `--path` continues to work for operator scripts predating `--output`.
- `--output` takes precedence when both are supplied (a non-existent
  `--path` doesn't poison the run).

Doc-set: test count 1051 → 1060 across the standard files;
Neo.Stack.Cli.UnitTests per-row 34 → 43.

### Fixed — `neo-hub-deploy verify` now exits non-zero on missing artifacts (1043 → 1051)

The `verify` command's MVP implementation always returned exit 0, even
when nef or manifest files for plan steps were missing on disk. A CI
script using `neo-hub-deploy verify --plan ... --rpc ...` would treat the
`[missing]` lines as informational and treat the run as success — so a
broken build pipeline would deploy with missing artifacts.

Fix: track `missing` count across plan steps; exit `2` if any nef or
manifest is missing. Also surfaces a count summary line ("N ok / M
missing of T total") so an operator scanning the output sees the verdict
without scrolling.

Refactored: extracted the `verify` logic from `Program.cs` into a public
`Neo.Hub.Deploy.VerifyCommand` class (parallel shape to
`Neo.Stack.Cli.Commands/`) so it's directly testable. `Program.cs` now
just dispatches `verify` → `VerifyCommand.RunAsync` and the
`RunVerifyAsync` private method is gone.

8 new tests in `UT_VerifyCommand` pin the exit-code contract:
- `Verify_AllArtifactsPresent_ExitsZero` — happy path.
- `Verify_MissingNef_ExitsTwo` — missing nef → exit 2 (the bug being fixed).
- `Verify_MissingManifest_ExitsTwo` — missing manifest → exit 2.
- `Verify_PartialMissing_ExitsTwo` — 1 ok + 1 missing still exits 2
  (CI semantics: "most are ok" ≠ success).
- `Verify_MissingRpcFlag_ExitsOne` — caller error (missing required flag),
  distinct from 2 (verification fail).
- `Verify_MissingPlanFile_ExitsOne` — bad plan path → caller error.
- `Verify_MalformedPlanJson_ExitsOne` — unparseable plan → caller error.
- `Verify_NullArgs_Rejected` — boundary defense.

Doc-set: test count 1043 → 1051 across the standard files;
Neo.Hub.Deploy.UnitTests per-row 25 → 33; row description gains the new
exit-code coverage.

### Added — Sample.CounterChainExecutor null-arg + cancellation guards pinned (1035 → 1043)

The executor + adapter both have `ArgumentNullException.ThrowIfNull` /
`ThrowIfCancellationRequested` defenses but no tests pinned them. A
regression that drops one of those guards would only surface as an NRE
or unattributed batch crash later. 8 new tests pin the boundary:

`UT_CounterChainExecutor` (16 → 20):
- `Ctor_NullState_Rejected` — DI container returning null for the state
  service must fail loud at composition time, not at first ExecuteAsync.
- `Ctor_NullEmittingContract_Rejected` — same shape pin for the emitting
  contract sentinel.
- `Execute_NullBatchContext_Rejected` — `BatchBlockContext` is a ref type
  with `required` fields, so null is a real possibility from a buggy
  batch driver. Pin ArgumentNullException at the call boundary.
- `Execute_CancelledToken_RespectsCancellation` — cooperative cancellation
  pin: a cancelled token must produce `OperationCanceledException` rather
  than running the whole tx and returning a receipt the caller will
  discard. Catches a future refactor that drops the
  `ThrowIfCancellationRequested` call.

`UT_KeyedStateStoreAdapter` (3 → 7):
- `Adapter_NullStore_RejectedAtCtor` — same DI-misconfig pin for the
  adapter's underlying store.
- `Adapter_NullKey_TryGet_Rejected`
- `Adapter_NullKey_Put_Rejected`
- `Adapter_NullValue_Put_Rejected`
  — every method's null-arg path. Together they pin the contract that
  the adapter doesn't silently swallow null reads/writes (which would
  produce confusing diagnostics at the underlying KeyedStateStore layer).

Doc-set: test count 1035 → 1043 across AGENTS.md / CONTRIBUTING.md /
README.md / IMPLEMENTATION_STATUS.md / docs/getting-started.md /
docs/tech-stack-coverage.md. Sample.CounterChainExecutor.UnitTests
per-row 16 → 24 with description updated to call out the executor
null-arg + cancellation pins + adapter null-arg pins.

### Updated — `new-l2` composite gains a `validate` defense-in-depth step (1034 → 1035)

The composite is now 4 steps instead of 3 (create-chain → **validate** →
init-l2 → scaffold-executor --with-tests). After the chain.config.json is
emitted, `ValidateChainConfigCommand.Run` immediately re-parses it to confirm
the §16.2 enum dimensions + chainId all decode cleanly. This is purely
defense-in-depth — with well-known templates the validate step always
passes — but it catches a hypothetical regression in any of the three
components (template emission, serializer, validator) at composite time
rather than later when the operator's first manual `validate` run surfaces
it.

A failing validate step aborts the composite with the validate exit code
+ a "this usually indicates a template / serializer drift — please file a
bug" diagnostic. Earlier-step artifacts (chain.config.json) stay on disk
so an operator can inspect what was emitted before the failure.

1 new test in `UT_NewL2Command.ValidateStep_RunsAfterCreateChain_AcceptsTemplateOutput`:
runs `new-l2` against EVERY template in the catalog (rollup / zk-rollup /
validium / sidechain) — each must exit 0 with a well-formed
chain.config.json. Catches a regression in any single template's emitted
shape. Test count 1034 → 1035 across the doc-set; UT_NewL2Command per-row
33 → 34. The IMPLEMENTATION_STATUS Stack.Cli row description gains a
note about the new validate step's per-template coverage.

Doc-set: AGENTS.md / CONTRIBUTING.md / README.md /
IMPLEMENTATION_STATUS.md / docs/getting-started.md /
docs/tech-stack-coverage.md.

### Updated — launching-an-l2 + new-l2 next-steps now point at neo-hub-deploy for L1 bring-up

Two doc-set / ergonomics fixes for the "I scaffolded a chain — now how do I
actually deploy NeoHub to L1?" gap:

`docs/launching-an-l2.md`:
  - The "Optimistic-rollup operators: wire a fraud verifier" section was
    stale (referenced "the 14th NeoHub contract" + said v3 was "still
    missing"; both shipped iterations ago). Rewritten as three paths:
    governance-arbitration via `GovernanceFraudVerifier`, trustless v3 via
    `RestrictedExecutionFraudVerifier` (the new on-chain path),
    fully-custom verifier with skip-both-references guidance. Also
    surfaces the `neo-hub-deploy` post-deploy informational notes that
    name the right verifier hash for each path.
  - New "Going to L1: deploying NeoHub" section walks through the three
    `neo-hub-deploy` subcommands (scaffold → plan → bundle → wallet
    deploy → capture-hashes → register-chain) so an operator who
    scaffolded a chain via `new-l2` has an explicit path from "buildable
    starter" to "L2 producing batches against a deployed NeoHub on L1."
    Calls out the bundle's `PostDeployActions` section (cycle-break
    wiring + §16.1 admission + per-verifier informational notes) so
    those steps don't get missed.

`tools/Neo.Stack.Cli/Commands/NewL2Command.cs`:
  - Next-steps output gains a 5th step pointing at the
    `neo-hub-deploy scaffold/plan/bundle` flow + the resulting
    `register-chain --operator/--verifier/--bridge/--message <hash>`
    invocation. So the operator who runs `neo-stack new-l2` sees the
    full L2-to-L1 path on screen, not just the local-dev preview path.

No code logic changed; pure documentation + console-output improvements.
1034 tests still green.

### Added — `neo-stack list-templates` + shared `TemplateCatalog` (1025 → 1034)

12th `neo-stack` subcommand. Prints the four chain-config templates with
their §16.2 dimensions + use-case descriptions for operator
discoverability. Two modes:

```bash
# No args: summary table + per-template overview.
neo-stack list-templates

# --template <name>: full per-template details + sample command.
neo-stack list-templates --template validium
```

Without this, an operator evaluating which template to pick had to read
source (CreateChainCommand.cs) or run `create-chain` repeatedly to see
what each one produces.

Refactor: extracted the per-template defaults from
`CreateChainCommand.Resolve` into a shared `TemplateCatalog` class.
Single source of truth consumed by `create-chain`, `new-l2`, and
`list-templates`. Drift between commands' template definitions is now
impossible. The TemplateCatalog also gains:
- `Template.Name` field (canonical display name)
- `Template.TagLine` (one-line use-case summary)
- `Template.UseCase` (paragraph-length explainer)
- `IsKnown(name)` predicate
- `ValidNames` comma-separated string for error messages

`CreateChainCommand`'s "Templates: ..." footer now references
`TemplateCatalog.ValidNames` + points at `list-templates` for full
descriptions instead of inlining the 4 names.

9 new tests in `UT_ListTemplatesCommand`:
- Catalog has exactly 4 templates in canonical order (rollup first).
- Resolve returns exact struct for known names; falls back to default
  for unknown.
- IsKnown is case-sensitive (rejects "Rollup" + empty).
- ValidNames lists all in order.
- list-templates with no args prints all 4 + default note + exits 0.
- --template <name> prints full per-template details (chainMode +
  daMode + use-case + sample command).
- Unknown --template exits 1 with valid-names error.
- Per-template detail round-trips through every supported name.

CLI subcommand count 11 → 12; test count 1025 → 1034 across the doc-set.

### Updated — neo-hub-deploy PostDeployActions surfaces both fraud verifiers (1024 → 1025)

The `Neo.Hub.Deploy.ScaffoldPlan.PostDeployActions` operator-facing
hints used to mention only `GovernanceFraudVerifier` (v1/v2 governance
arbitration). Now that both verifiers ship in the default scaffold,
operators running `neo-hub-deploy plan` see one note per deployed
verifier so they know which contract hash to pass as the
`fraudVerifier` argument when filing each kind of fraud proof:

```
# Note: for v1/v2 fraud proofs (governance arbitration), pass GovernanceFraudVerifier.Hash as the `fraudVerifier` argument to OptimisticChallenge.Challenge.
# Note: for v3 fraud proofs (trustless storage-proof re-derivation), pass RestrictedExecutionFraudVerifier.Hash as the `fraudVerifier` argument to OptimisticChallenge.Challenge.
```

The two verifiers are peers — operators pick which to invoke per-
challenge based on whether they're filing a v1/v2 (governance) or v3
(trustless) `FraudProofPayload`. Both can be deployed simultaneously.

`PostDeployActions_DefaultPlan_EmitsAllWiringHints` updated: 4 → 5
expected actions (the new v3 note); each verifier note's body is
pinned to mention the verifier name + "fraudVerifier" + "OptimisticChallenge.Challenge"
+ the wire-format version qualifier ("v1/v2" / "v3").

1 new test `PostDeployActions_OnlyV3FraudVerifier_EmitsOnlyV3Note` —
asymmetric pin: an operator deploying only the v3 verifier (no v1/v2
fallback) gets ONLY the v3 note. Verifies the two verifier-note paths
are independent.

Also updated the docs/launching-an-l2.md "Quick path" section to
feature the new-l2 composite as the recommended starting point, with
the existing 5-command path retained for fine-grained control.

Doc-set: test count 1024 → 1025; IMPLEMENTATION_STATUS Hub.Deploy
row 24 → 25 with description updated to mention the per-verifier
informational notes + the asymmetric-only-v3 pin.

### Added — `neo-stack new-l2` composite command (1016 → 1024)

11th `neo-stack` subcommand. Strings `create-chain` + `init-l2` +
`scaffold-executor --with-tests` together so an operator goes from "I
want a custom L2" to a buildable + testable + devnet-previewable starter
in one command:

```bash
neo-stack new-l2 --name MyChain --chain-id 1234 [--template rollup] [--output ./my-l2]
```

Produces (default `--output ./chain-<chainId>`):

```
chain-1234/
├── chain.config.json              # from create-chain (template-driven §16.2)
├── data/  logs/  Plugins/         # from init-l2 (node working dirs)
├── MyChainExecutor/               # from scaffold-executor (custom executor)
│   ├── MyChainExecutor.csproj
│   ├── MyChainExecutor.cs
│   ├── IMyChainState.cs
│   ├── MyChainTxBuilder.cs
│   ├── MyChainKeyedStateStoreAdapter.cs
│   └── README.md
└── MyChainExecutor.UnitTests/     # from scaffold-executor --with-tests
    ├── MyChainExecutor.UnitTests.csproj
    ├── Usings.cs
    └── UT_MyChainExecutor.cs
```

After the composite runs, the operator's "Next" output points them at:
`dotnet build` + `dotnet test` for the executor scaffold, `neo-stack
validate` for the chain.config.json sanity check, and
`dotnet run --project tools/Neo.L2.Devnet -- 5 --config <path>` to
preview the chain end-to-end through the in-process devnet.

Validation + flag handling is largely delegated to the underlying
commands (so refuse-to-overwrite + identifier validation behavior is
inherited):
- `--name` is required at the composite level (rejected before
  any command runs, atomic — no partial artifacts on disk).
- `--chain-id 0` throws via `Neo.L2.ChainIdValidator.ValidateL2`
  (atomic).
- An invalid `--name` (e.g. `1Bad`) reaches `scaffold-executor`'s
  identifier validator mid-flow — the composite propagates exit 1
  with a "new-l2 aborted at scaffold-executor" diagnostic. Earlier-
  step artifacts (chain.config.json + data/logs/Plugins) ARE on
  disk; operators can inspect what was written before the failure.
- `--template` propagates to `chain.config.json`.
- `--path` is an alias for `--output` (matches create-chain +
  scaffold-executor + init-l2 ergonomics).

8 new tests in `UT_NewL2Command`:
- `HappyPath_CreatesAllArtifacts_AndExitsZero` — verifies all
  create-chain / init-l2 / scaffold artifacts exist after one composite
  run.
- `MissingName_Rejected_BeforeAnyCommandRuns` — atomic abort pin.
- `ChainIdZero_Rejected` — same.
- `NonNumericChainId_Rejected`.
- `InvalidName_RejectedByScaffoldStep_AfterEarlierStepsRan` — pin
  that earlier-step artifacts are preserved on disk for inspection.
- `PathFlagAlias_UsedWhenOutputAbsent`.
- `DefaultOutput_IsChainNumberDir` — `./chain-<id>` default works.
- `TemplatePassedThrough_ToCreateChain` — validium template surfaces
  in `chain.config.json`'s `securityLevel: Validium`.

End-to-end smoke-tested manually: `neo-stack new-l2 --name SmokeChain
--chain-id 1234 --output /tmp/new-l2-smoke` produced all artifacts
correctly; cleaned up.

Doc-set: CLI subcommand count 10 → 11 in IMPLEMENTATION_STATUS.md
(Phase 6 row + tools table) + README.md (Phase 6 row). Test count 1016
→ 1024 across AGENTS.md / CONTRIBUTING.md / README.md /
IMPLEMENTATION_STATUS.md / docs/getting-started.md / docs/tech-stack-
coverage.md. IMPLEMENTATION_STATUS Stack.Cli row 16 → 24.

### Added — `scaffold-executor --with-tests` flag (1013 → 1016)

Closes the "scaffold to running tests" loop. Until this flag, an operator
running `neo-stack scaffold-executor` got a buildable starter project but
no test project — they had to hand-write the tests csproj + Usings.cs +
starter tests themselves. With `--with-tests`, the scaffold also emits:

  - `<output>.UnitTests/<projectName>.UnitTests.csproj` (MSTest +
    ProjectReference to the main project)
  - `<output>.UnitTests/Usings.cs` (global usings: MSTest +
    Neo + Neo.L2 + the executor's namespace)
  - `<output>.UnitTests/UT_<projectName>.cs` (3 starter tests pinning
    the placeholder NoOp opcode + the failed-receipt edge cases)

The starter tests pin: `Execute_NoOp_ReturnsSuccessReceiptWithNoEffects`
(NoOp dispatch + GasNoOp accounting), `Execute_EmptyTx_ReturnsFailedReceipt`
(SPEC.md determinism: malformed txs don't crash the batch),
`Execute_UnknownOpcode_ReturnsFailedReceipt`. As the operator adds real
opcodes, they mirror new tests against the working sample's pattern at
`tests/Sample.CounterChainExecutor.UnitTests/UT_CounterChainExecutor.cs`.

When `--with-tests` is set, the scaffold also:
  - Refuses to overwrite a non-empty `<output>.UnitTests` directory (same
    defense-in-depth as the main `--output` check).
  - Aborts atomically: if the tests dir is non-empty, the main project
    is NOT created either — operators don't lose work mid-scaffold.
  - Adds a "companion test project" blurb to the main project's README.

3 new tests in UT_ScaffoldExecutorCommand pin the new flag:
`WithTests_EmitsSiblingTestsProject`, `WithoutTests_OmitsTestsProject`
(default behavior unchanged), `WithTests_NonEmptyTestsDir_Rejected`
(atomic abort).

End-to-end smoke-tested manually: scaffolded `SmokeTestChain --with-tests`
inside the monorepo at `samples/executors/`; both projects built clean +
all 3 starter tests passed; cleaned up.

Doc-set: docs/launching-an-l2.md "Adding custom chain logic" section
shows `--with-tests` in the canonical example. IMPLEMENTATION_STATUS.md
Neo.Stack.Cli.UnitTests row 13 → 16. Test count 1013 → 1016 across the
doc-set.

### Added — `neo-l2-devnet --executor counter` flag

Wires `Sample.CounterChainExecutor` end-to-end through the in-process devnet.
Closes the operator-visible loop on the custom-chain-logic story: scaffold
gives you the project, the integration test pins the wiring, and now the
devnet shows it running through deposits / proving / settlement / audit
just like the legacy ReferenceTransactionExecutor demo.

```
neo-l2-devnet 5 --executor counter
[wire] tx executor = CounterChainExecutor (chainId=1001, --executor counter)
...
  [exec] 3 Counter txs → gas=400, txRoot=0x9b3..., l2L2Root=0x4e0...
...
state entries: 3       # Alice GasL2 balance + Alice counter + Bob counter
✅ devnet run complete.
```

What changes per batch when `--executor counter` is set:
- 1 deposit + 1 staged withdrawal still flow through the bridge as
  before (executor-independent).
- 3 Counter transactions are added to the batch:
  `IncrementCounter(Alice, 100×batchNum)`,
  `IncrementCounter(Bob, 50×batchNum)`,
  `EmitMessage(destChainId=1002, body=batchNum)`.
- The Counter executor writes through `KeyedStateStoreAdapter` to the
  same `KeyedStateStore` the post-state-root oracle hashes — so the
  state root advances per batch by both the deposit-induced mints AND
  the Counter ops.
- DA payload is the concatenated tx bytes (so an off-chain consumer
  can re-derive the batch contents from DA).
- L2-to-L2 message root populates from the EmitMessage opcodes.

Default `--executor reference` preserves the legacy devnet behavior
(no-op tx executor + 8-byte dummy tx). Unknown values fall back to
`reference` with a warning so a typo doesn't silently swap executors.

Smoke-tested both modes: 3 batches each, Multisig proofs verified, all
6 audit checks pass, RPC snapshot matches expected balances + state
entry counts.

### Added — RestrictedExecutionFraudVerifier added to deploy planner (1011 → 1013)

The 15th NeoHub contract (shipped 4 iterations ago) was missing from
`Neo.Hub.Deploy.ScaffoldPlan.Default()`'s 14-step bundle. Operators
running `neo-hub-deploy scaffold` got the v1/v2 GovernanceFraudVerifier
but had to manually add the v3 RestrictedExecutionFraudVerifier
themselves — easy to miss for chains that want trustless v3.

ScaffoldPlan.Default() now emits 15 steps. RestrictedExecutionFraudVerifier
has the same shape as GovernanceFraudVerifier — stateless contract, no
deploy args, no deps. Operators running v3 fraud-proofs pass this
contract's hash as the `fraudVerifier` argument to
`OptimisticChallenge.Challenge`; operators running governance-arbitration
v1/v2 use GovernanceFraudVerifier; both can be deployed simultaneously
since the caller picks which to invoke per-challenge.

2 new tests in `UT_DeployPlanner`:
- `Scaffold_RestrictedExecutionFraudVerifierHasEmptyDeployData` — same
  empty-args + no-deps shape pin as the existing
  `Scaffold_GovernanceFraudVerifierHasEmptyDeployData`.
- `Scaffold_BothFraudVerifiers_ParallelShape` — pin that the two
  verifiers have identical deploy shape (peers, not asymmetric).

`Scaffold_DefaultIncludesAllNeoHubContracts` updated: step count
14 → 15; new assertion that names contains
`RestrictedExecutionFraudVerifier`.

Doc-set bumps: deploy-planner step count "14-step bundle" → "15-step
bundle" in IMPLEMENTATION_STATUS.md / README.md (Phase 1 row); test
count 1011 → 1013 across AGENTS.md / CONTRIBUTING.md / README.md /
IMPLEMENTATION_STATUS.md / docs/getting-started.md / docs/tech-stack-
coverage.md. tech-stack-coverage.md NeoHub contract list gains a row
for the v3 verifier; "14 NeoHub contracts" → "15 NeoHub contracts".

### Added — `neo-stack scaffold-executor` CLI subcommand (998 → 1011)

Operator goes from "I want a custom L2" to a buildable project in one
command. Mirrors the working `samples/executors/Sample.CounterChainExecutor`
reference shape — emits a complete starter project the operator can
`dotnet build` immediately and customize.

```
neo-stack scaffold-executor --name MyChain [--chain-id 1234] [--output <dir>]
```

Output (default `./samples/executors/<Name>Executor`):
- `<Name>Executor.csproj` — references `Neo.L2.Abstractions` +
  `Neo.L2.Executor` via 3-up relative path (matches in-monorepo placement).
- `<Name>Executor.cs` — skeleton `ITransactionExecutor` with one placeholder
  `NoOp` opcode + customization markers (`// TODO: add your chain's opcodes
  here.`).
- `I<Name>State.cs` — state seam + `InMemory<Name>State` for tests.
- `<Name>TxBuilder.cs` — canonical tx-byte helpers.
- `<Name>KeyedStateStoreAdapter.cs` — production-ready bridge to
  `Neo.L2.Executor.State.KeyedStateStore`.
- `README.md` — what's in the scaffold + 5-step customization checklist
  pointing at the reference sample.

Validation:
- `--name` must be a valid C# identifier (compiles as namespace + class
  name; rejects digit-first, hyphens, empty).
- `--chain-id` must be a non-zero L2 chain id (delegates to
  `Neo.L2.ChainIdValidator.ValidateL2`, throws on the L1 sentinel).
- `--output` directory must be empty (refuses to overwrite existing files).
- `--path` is an alias for `--output` (matches `create-chain` ergonomics so
  scripts can string subcommands together with one flag name).

Also adds `tests/Neo.Stack.Cli.UnitTests/` (project count 28 → 29) — this
is the first test project for the CLI. Wired via
`<InternalsVisibleTo Include="Neo.Stack.Cli.UnitTests" />` so the test
project can call `internal static ScaffoldExecutorCommand.Run` directly
instead of subprocess-invoking the binary.

13 new tests in `UT_ScaffoldExecutorCommand`:
- Happy path: all 6 expected files emitted.
- Csproj content: RootNamespace + AssemblyName + 3-up project refs match.
- Executor content: namespace + class shape + `Opcode.NoOp` dispatch +
  `// TODO` customization marker.
- README links to the reference sample + the 5-step checklist heading.
- Invalid `--name` (digit-first / hyphen / empty) → exit 1 + no dir created.
- `--chain-id 0` throws `InvalidDataException` (L1 sentinel reject).
- Non-numeric `--chain-id` → exit 1.
- Non-empty output dir refused (preexisting file untouched).
- Default chainId 1001 surfaced in README when `--chain-id` omitted.
- Case preservation: `MyDeFi` → `MyDeFiExecutor.cs`.
- `--path` alias works when `--output` is absent.

CLI subcommand count 9 → 10 in IMPLEMENTATION_STATUS.md / README.md / Phase
6 row. Test count 998 → 1011; project count 28 → 29.

### Added — `KeyedStateStoreAdapter` + e2e custom-executor full-stack integration test (993 → 998)

Closes the loop on "custom chain logic" — the seam now has a runnable
end-to-end demonstration that a custom `ITransactionExecutor` produces
well-formed commitments through the entire pipeline.

`samples/executors/Sample.CounterChainExecutor/KeyedStateStoreAdapter.cs`:
production-ready bridge from `ICounterChainState` to the framework's
canonical `Neo.L2.Executor.State.KeyedStateStore`. With the adapter wired,
the executor's writes participate in the same store the post-state-root
oracle hashes — so `BatchExecutionResult.PostStateRoot` reflects the
executor's actual state mutations, not just a synthetic XOR of the receipt
root. 3 unit tests pin Put/Get round-trip + missing-key + ComputeRoot parity
with direct `KeyedStateStore` writes.

`tests/Neo.L2.IntegrationTests/UT_E2E_CustomExecutor_FullStack.cs`: 2 new
end-to-end tests:

  - `CustomExecutor_FullPipeline_ProducesValidCommitmentsWithContinuity` —
    3-batch run with mixed Increment / Withdraw / Message txs through
    `CounterChainExecutor` + `KeyedStateStoreAdapter` + `ReferenceBatchExecutor`
    + `KeyedStateRootOracle` + `AttestationProver` + `VerifierRegistry`.
    Pins: all 4 batch roots non-zero; state root advances per batch;
    4-root uniqueness across batches (no collisions = state was actually
    distinct); per-opcode gas schedule respected; multisig verifier
    accepts commitments built from custom-executor batches; `BatchSerializer`
    encode/decode round-trip is identity; final state has 6 expected
    counter entries (2 senders × 3 batches); spot-check Alice's batch-3
    counter equals the IncrementCounter amount.
  - `CustomExecutor_FailedTxsDoNotPolluteRoots` — defense-in-depth pin:
    a batch with one malformed tx (unknown opcode) flanked by good
    increments must not let the failed tx's hypothetical effects flow
    through to `WithdrawalRoot` or message roots. Gas accounting stays
    correct; state from successful txs intact.

This is the cross-cutting invariant proof: a future seam refactor that
broke "custom executor → standard pipeline → valid commitment" would now
fail loud at integration-test time.

Test count 993 → 998 across AGENTS.md / CONTRIBUTING.md / README.md /
IMPLEMENTATION_STATUS.md. Sample.CounterChainExecutor.UnitTests row 13 → 16;
Neo.L2.IntegrationTests row 19 → 21.

### Added — `Sample.CounterChainExecutor` reference custom executor (980 → 993)

Operator-facing reference for the `ITransactionExecutor` seam — the framework's
plug-in point for "how to specify custom chain logic." Until this sample,
the framework had the seam (interface + reference no-op stand-in) but no
runnable demonstration of how an operator brings their own chain to it.

`samples/executors/Sample.CounterChainExecutor` is a small custom L2 with
three opcodes:
- `IncrementCounter (0x01)` — `[1B opcode][20B sender][8B u64 amount LE]`
  → adds to a per-sender counter in state, with documented ulong-wraparound
  semantics matching Neo NEP-17.
- `EmitWithdrawal (0x02)` — `[1B opcode][20B recipient][20B token][8B u64
  amount LE]` → builds a `WithdrawalRequest` with a deterministic
  txHash-derived nonce.
- `EmitMessage (0x03)` — `[1B opcode][4B destChainId LE][2B msgLen LE][N
  bytes msg]` → builds a `CrossChainMessage` via canonical
  `MessageBuilder.Build`, inheriting the hash composition + self-routed
  rejection from the framework.

Demonstrates the patterns a real custom executor needs:
- **Determinism contract** (per `Neo.L2.Executor/SPEC.md`): receipts derive
  from `(serializedTx, batchContext, preStateRoot)` alone — no clock, no
  RNG, no I/O. Pinned by `Execute_Determinism_SameInputSameOutput`.
- **Failed-receipt path**: malformed transactions produce
  `Receipt.Success = false` instead of crashing the batch — required so one
  bad tx can't take down the whole batch's proving pipeline.
- **State seam**: takes an `ICounterChainState` interface so tests inject
  `InMemoryCounterChainState` and production wires
  `Neo.L2.Executor.State.KeyedStateStore`.
- **Per-opcode gas schedule**: fixed gas per opcode keeps `GasConsumed`
  reproducible by any verifier.

13 new tests in `Sample.CounterChainExecutor.UnitTests`:
counter happy path + accumulation + ulong wraparound; per-sender state
isolation; truncated-tx → Failed (not crash); withdrawal happy path with
deterministic nonce; zero-amount withdrawal rejected; message happy path
with valid `MessageHash`; self-routed message (source==target) rejected;
oversized message body rejected at `MaxMessageBytes` cap; unknown opcode
+ empty tx → Failed; SPEC.md determinism pin; mixed-opcode batch smoke
test.

`samples/README.md` gains a "Custom chain logic" section walking through
how to fork the sample for your own chain. AGENTS.md mapping table gains
a row for §3 / §7.1 custom chain logic pointing at the seam + reference.

Project count 27 → 28.

### Added — `NeoHub.RestrictedExecutionFraudVerifier` on-chain v3 verifier (965 → 980)

15th NeoHub contract — the trustless companion to
`NeoHub.GovernanceFraudVerifier`. Where the governance verifier stops at
structural checks (length / version / claimed != replayed) and defers
correctness arbitration to the security council, this verifier requires
the challenger to supply storage-proof manifests for every key the
disputed transaction touched and rejects the proof on-chain if those
manifests don't reconstruct to the payload's `PreStateRoot` and
`ReplayedPostStateRoot`.

`VerifyFraud(uint chainId, ulong batchNumber, byte[] payload) → bool`:
parses canonical v3 `FraudProofPayload` bytes, iterates each storage
proof, re-derives the pre/post state roots via `Hash256(left || right)`
sibling-folding driven by `leafIndex`'s low bits, and matches against
the v1 header roots at offsets `[1..32]` / `[65..96]`.

Reject reasons (event-logged so operators can attribute the rejection
without re-decoding the payload):
- `ReasonBadLength = 1` — truncated, trailing bytes, or any structural
  decode failure.
- `ReasonBadVersion = 2` — version byte != 3 (use GovernanceFraudVerifier
  for v1/v2).
- `ReasonNoDiscrepancy = 3` — `claimedPostStateRoot == replayedPostStateRoot`,
  no real fraud claim. Short-circuits before per-proof verify.
- `ReasonOversizedWitness = 4` — declared disputed-tx witness exceeds
  64 KB cap.
- `ReasonInvalidStorageProof = 5` — any proof violates per-proof caps
  (key > 256 B, value > 4 KB, sibling depth > 64).
- `ReasonProofCountInvalid = 6` — zero storage proofs (use v2 instead)
  or > 32 per payload.
- `ReasonPreStateRootMismatch = 7` — pre-derived root doesn't match
  `PreStateRoot` at offset `[1..32]`.
- `ReasonReplayedPostStateRootMismatch = 8` — post-derived root doesn't
  match `ReplayedPostStateRoot` at offset `[65..96]`.

What this verifier proves on-chain: the challenger's storage proofs are
internally consistent — starting from `PreStateRoot` and applying the
proof's pre→post value changes for the disputed key produces a tree
whose root matches `ReplayedPostStateRoot`. Combined with claimed !=
replayed, this gives a structurally credible v3 fraud proof that an
on-L1 contract accepts without trusting the challenger or a council.

What this verifier does NOT prove: that re-running the disputed
transaction on the pre-state actually produces the challenger's claimed
post-state. That requires running NeoVM with restricted state on L1 —
substantial multi-iteration work and the natural follow-on to this
contract. Until then, "accepted by RestrictedExecutionFraudVerifier"
means "the challenger has made a structurally credible claim a downstream
re-execution service must arbitrate." `IMPLEMENTATION_STATUS.md`'s
"Optimistic-challenge fraud-proof game" gap-list is updated to mark
this contract as shipped and surface the remaining on-L1 NeoVM-with-
restricted-state re-executor.

Hash composition pinned to canonical primitives:
- Leaf hash matches `Neo.L2.Executor.State.KeyedStateStore.HashEntry`:
  `Hash256(int32LE(keyLen) || key || int32LE(valueLen) || value)`.
- Sibling folding matches `NeoHub.SettlementManager.VerifyWithdrawalLeafWithProof`:
  `Hash256(left || right)` (= `Sha256(Sha256(...))` — Neo `MerkleTree`
  convention) with `leafIndex` bits selecting left/right ordering.
- Reads keys / values / siblings directly from the payload by offset to
  avoid allocating intermediate per-proof byte arrays beyond the leaf-
  hash composition buffer.

15 new parity tests in `UT_RestrictedExecutionFraudVerifierParity`
(`SimulateVerify` mirrors the on-chain decision tree 1:1 against
hand-built 2-leaf Merkle trees): happy path → ReasonAccepted; v1 / v2
payloads → BadVersion (this verifier is v3-only); truncated below v2
header / past num-proofs prefix → BadLength; oversized witness →
OversizedWitness; same-root → NoDiscrepancy short-circuits before
per-proof verify; zero-proof / > MaxStorageProofsPerPayload →
ProofCountInvalid; pre-derived root mismatch → PreStateRootMismatch;
post-derived root mismatch → ReplayedPostStateRootMismatch (ordering
pin: pre check fires before post); decision-tree order pins (version
before all, discrepancy before per-proof verify); layout-offset pins
for `PreStateRoot @ 1` + `ReplayedPostStateRoot @ 65`; encode→decode→
encode round-trip preserves on-chain acceptance.

NeoHub L1 contract count 14 → 15. Total contracts 20 → 21.

### Added — `V3StorageProofVerifier` off-chain reference verifier (958 → 965)

Demonstrates the algorithm a future on-chain re-execution-capable fraud
verifier would mirror. Takes a v3 `FraudProofPayload`, re-derives the pre/post
Merkle roots from each `StorageProof`'s leaf-hash + siblings + leafIndex,
checks them against the payload's `PreStateRoot` and `ReplayedPostStateRoot`.

`Verdict` enum: `Verified` (all proofs check out) / `NotV3` / `NoDiscrepancy`
(claimed == replayed — no real fraud claim) / `PreStateRootMismatch` /
`ReplayedPostStateRootMismatch`.

Successful verification proves the challenger's storage proofs are
internally consistent — starting from `PreStateRoot` and applying the
proof's pre→post value changes for the disputed key produces a tree whose
root matches `ReplayedPostStateRoot`. Combined with the structural
`ClaimedPostStateRoot != ReplayedPostStateRoot` check, this gives a
well-formed v3 fraud-proof. It does NOT re-execute the disputed transaction
— that last step requires running NeoVM with restricted state (downstream
multi-iteration work). The structural `NeoHub.GovernanceFraudVerifier`
contract continues to reject v3 with `ReasonBadVersion` until that
on-chain verifier exists.

Composition pinned to existing canonical primitives:
- `HashEntry(key, value)` = `Hash256(int32LE(keyLen) || key ||
  int32LE(valueLen) || value)` — must stay in lockstep with
  `Neo.L2.Executor.State.KeyedStateStore.HashEntry` (a parity test fails
  loud if either side drifts).
- `FoldMerkleProof(leafHash, siblings, leafIndex)` mirrors
  `Neo.L2.State.MerkleTree`'s `Hash256(left || right)` composition with
  leafIndex bits selecting left/right ordering at each level — same fold
  shape as on-chain `SettlementManager.VerifyWithdrawalLeafWithProof`.

7 new tests in `UT_V3StorageProofVerifier`: NotV3 / NoDiscrepancy short-
circuits; happy-path 2-leaf-tree Verified; PreStateRootMismatch and
ReplayedPostStateRootMismatch each rejected with correct verdict;
encode→decode through `FraudProofPayload` preserves verifiability (cross-
component contract pin); `HashEntry` layout matches `KeyedStateStore`
exactly.

### Added — `FraudProofPayload` v3 wire format with storage-proof manifests (951 → 958)

Closes the storage-tree witness half of the "Optimistic-challenge fraud-proof
game" audit gap (off-chain side). v3 layout extends v2 with a length-prefixed
array of storage-proof manifests:

```
[v2 bytes (header + disputed-tx witness)]
[4B numStorageProofs]
[for each StorageProof:
  [2B keyLen][N bytes key]
  [4B preValueLen][N bytes preValue]
  [4B postValueLen][N bytes postValue]
  [8B leafIndex (uint64 LE)]
  [1B preSiblingCount][32 × preSiblingCount bytes preSiblings]
  [1B postSiblingCount][32 × postSiblingCount bytes postSiblings]]
```

Per-proof caps: `MaxKeyBytes = 256`, `MaxValueBytes = 4096`, `MaxSiblingDepth = 64`.
Per-payload cap: `MaxStorageProofsPerPayload = 32`. Encode picks version
automatically: storage proofs → v3, else disputed-tx witness → v2, else v1.
New `IsV1` / `IsV2` / `IsV3` instance helpers expose the version dispatch.

A real on-L1 trustless re-execution verifier consumes v3 directly: re-derives
the pre-state root from each storage proof's leaf-hash + siblings + leafIndex,
checks against the payload's `PreStateRoot` (v1 header), then same on the
post side against `ClaimedPostStateRoot` or `ReplayedPostStateRoot`. The
structural `NeoHub.GovernanceFraudVerifier` continues to reject v3 with
`ReasonBadVersion` — operators running v3 ship their own re-execution
verifier.

7 new tests in `UT_Challenge`: `StorageProof_RoundTrips`,
`StorageProof_DecodeRejectsTruncatedKey`, `Payload_V3_RoundTrips`,
`Payload_V3_RejectsZeroProofs` (zero-proof v3 = use v2 instead),
`Payload_V3_RejectsCapsViolation_TooManyProofs`,
`Payload_V3_DecodeRejectsExtraTrailingBytes`,
`Payload_V3_StorageProofIndividualCapsEnforced`.

### Added — `FraudProofPayload` v2 wire format with disputed-tx witness (934 → 948)

Closes the disputed-tx half of the "Optimistic-challenge fraud-proof game"
audit gap. v2 layout extends v1 with a length-prefixed witness:

```
[1B version=2][32B preStateRoot][32B claimedPostStateRoot]
[32B replayedPostStateRoot][4B disputedTxIndex]
[4B disputedTxLen][N bytes disputedTxBytes]
```

`FraudProofPayload.Encode()` produces v1 bytes when `DisputedTxBytes` is
empty (legacy callers preserved), v2 bytes otherwise. `Decode()` reads the
version byte and dispatches to the right format. `MaxDisputedTxBytes = 64KB`
cap prevents unbounded payloads.

`NeoHub.GovernanceFraudVerifier` now accepts both versions. The decision
tree changed order to dispatch on version FIRST (different versions have
different valid lengths), then per-version length check, then the
discrepancy claim. New reject reason byte: `ReasonOversizedWitness = 4`.

  - Off-chain: 9 new tests in `UT_Challenge` (v2 round-trip; v1 backwards
    compat empty-witness pin; cap-violation rejection; truncated-trailer +
    extra-trailing-bytes rejections; oversized-declared-len rejection;
    `EncodedSize` varies-by-version pin; explicit unknown-version vs
    bad-length distinction)
  - Off-chain parity: 5 new tests in `UT_GovernanceFraudVerifierParity`
    extending `SimulateVerify` to handle v2 (real-discrepancy → accept;
    same-root → reject NoDiscrepancy; truncated-witness → BadLength;
    oversized-witness → OversizedWitness; bad-version pin)
  - Updated 2 existing tests to reflect the new version-first dispatch
    order (length checks now happen per-version, not before version)

What's still missing for full trustless re-execution: the storage-read +
storage-write manifests with Merkle proofs against the pre/post state
roots in the v1 header. v3 wire format territory.

### Added — `GovernanceFraudVerifier` parity test coverage (925 → 933)

8 new tests in `UT_GovernanceFraudVerifierParity` simulate the on-chain
contract's decision tree in C# (length → version → discrepancy) and run
the same fraud-proof payloads through both, pinning each accept/reject
decision against the contract's reason-byte mapping. Closes the behavioral
coverage gap for the new contract without needing an `ApplicationEngine`-
backed test harness.

Pins include:
  - real-discrepancy → accept; same-root → reject ReasonNoDiscrepancy (3)
  - bad-length / bad-version → reject with the correct reason byte (1 / 2)
  - Decision-tree order (length-before-version, version-before-discrepancy)
    so a refactor that swaps checks doesn't change operator-facing reject
    metrics
  - Offset parity: claimed at 33..64, replayed at 65..96 — same bytes the
    contract's BytesEqual reads
  - DisputedTxIndex doesn't affect the structural verdict (it's metadata
    for re-execution-capable verifiers)

### Added — `NeoHub.GovernanceFraudVerifier` reference contract (14th NeoHub)

Closes the on-chain fraudVerifier gap. `OptimisticChallenge.Challenge`
already delegated proof verification to `Contract.Call(fraudVerifier,
"verifyFraud", ...)` but no verifier shipped — operators choosing the
optimistic-rollup template had to bring their own. Now ships as a
governance-arbitration-mode reference: decodes the canonical 101-byte
`FraudProofPayload`, validates length + version + claims-a-real-discrepancy
(`claimedPostStateRoot != replayedPostStateRoot`), emits
`FraudProofAccepted` with both state roots so a security council reviewing
the dispute has them visible without re-decoding bytes; emits
`FraudProofRejected` with reason byte (1=bad-length, 2=bad-version,
3=no-discrepancy) so failures are diagnosable.

Caveat (documented in the contract XML): does NOT re-execute the disputed
transaction on L1 — a trustless verifier needs execution-trace witness
bytes that the current `FraudProofPayload` format doesn't carry. That's
the remaining gap tracked in `IMPLEMENTATION_STATUS.md`'s "Optimistic-
challenge fraud-proof game" section.

  - 2 new wire-format pin tests in `UT_Challenge`: assert
    `FraudProofPayload.Size == 101` + `Version == 1` explicitly so the
    on-chain hardcoded constants stay in lockstep with the off-chain
    encoder. (923 → 925 tests.)
  - CI workflow extended: 14 NeoHub + 6 L2Native + 2 samples = 22 contracts.
  - README contract count 19 → 20.

### Refactored — extract `L2ChainConfigJsonReader` for unit-testable JSON parsing

The CLI's `register-chain` had ~50 lines of inlined JSON parsing + helper
methods (`BuildConfigFromJson`, `ParseEnum`, `ParseBool`, `ParseHash`)
that were all private static — untestable without spawning the CLI process.
Extracts to `Neo.L2.L2ChainConfigJsonReader.FromJson(...)` in Abstractions
so the parser is exercisable from a normal unit test. CLI is now a thin glue
layer. 8 new tests pin full-population, roundtrip-through-serializer, named-
error-messages (unknown enum, missing field, malformed UInt160, null inputs),
plus a validium-template shape pin.

Cumulative: 913 tests / 27 projects.

### Added — symmetric `L2BridgePlugin.WithMetrics` deposit-side propagation pin

The existing test only caught a regression on the withdrawal-side
propagation. A refactor that drops `_depositProcessor?.WithMetrics(metrics)`
would silently lose every `l2.bridge.deposits/deposits_rejected` signal —
invisible until production dashboards go quiet. New symmetric test closes
the blind spot.

Cumulative: 914 tests / 27 projects.

### Added — `IL2RpcStore` default-interface body pins + e2e through `getsecuritylabel`

Third-party `IL2RpcStore` implementations that don't override the new §16.2
dimension properties inherit default-method bodies (External / false /
DbftCommittee / Permissionless). Without these pins, a refactor that changes
any default would silently shift every external operator's `getsecuritylabel`
output. New `MinimalRpcStore` test fixture implements only the required
interface members; two tests assert the documented defaults and the
end-to-end RPC propagation.

### Showcase — devnet runner emits `getsecuritylabel` in post-run snapshot

Operators learning the system from the runner's output now see the §16.2
5-dimension label visibly alongside `getl2stateroot`/`state entries`/
`committee active`.

Cumulative: 916 tests / 27 projects.

### Added — devnet `--config <path>` flag for previewing operator templates

`neo-stack create-chain --template <X>` writes a `chain.config.json`. The
devnet runner now accepts `--config <path>` pointing at that JSON; reads the
§16.2 security-label dimensions (`securityLevel` / `daMode` / `gatewayEnabled`
/ `sequencerModel` / `exitModel`); applies them to the in-memory L2RpcStore
via `init` properties. Result: `getsecuritylabel` in the post-run snapshot
reflects the operator's template choice end-to-end. Missing fields fall back
to per-dimension defaults so a minimal config doesn't error.

### Fixed — `InMemoryL2RpcStore` range-check now accepts `SecurityLevel.Validium`

The original 0..3 range-check predated Validium being added (which has byte
value 4). Operators feeding a Validium template config to the devnet hit
`ArgumentOutOfRangeException("not in [0..3]")`. Now accepts 0..4 inclusive;
two new boundary tests pin Validium-acceptance + out-of-range-rejection.

Cumulative: 918 tests / 27 projects.

### Added — `samples/`: 4 runnable L2 chain configs covering distinct use cases

Each sample is the same JSON shape `neo-stack create-chain` writes; drop into
`neo-l2-devnet --config <path>` to preview the §16.2 label end-to-end before
deploying to L1. Each sample documents (in a `_comment` field) the trade-offs
its dimension choices encode.

  - `general-rollup` (chainId 1100): general-purpose Neo L2 — the safe default
  - `gaming-rollup` (chainId 1200): high-frequency gaming chain (centralized
    sequencer for sub-second seal + External DA)
  - `exchange-validium` (chainId 1300): DEX / orderbook (ZK + NeoFS off-chain
    DA + delayed exit + gateway-enabled)
  - `privacy-sidechain` (chainId 1400): permissioned consortium (SidechainMode,
    no proof, sidechain trust)

All four verified ✅ devnet run complete with distinct getsecuritylabel
output. `samples/README.md` tabulates each + when to start from each. 5
new unit tests pin each sample's shape so a typo in any sample fails
test-time, not at the operator's terminal.

Cumulative: 923 tests / 27 projects.

### Added — `docs/tech-stack-coverage.md`: honest L2-stack gap analysis

A 5-layer coverage table (protocol contracts / node infrastructure /
operator tooling / app development / end-user UIs) catalogs each component
against ✅ done / 🟡 scaffolded / 🔴 out-of-repo. **51 / 59 components
shipped**; 2 🟡 (Phase 4/5 ZK infra blocked on SP1 toolchain + recursive ZK
backend); 6 🔴 (deliberately out-of-repo: typed SDKs, block explorer,
bridge UI, doc site, faucet, wallet integration — all deployment-specific).

### Added — `samples/contracts/`: 2 sample L2-aware dApp contracts

Original Neo smart contracts demonstrating standard patterns for app
contracts integrating with the Neo core L2 native system suite. Each compiles
under `nccs` to `.nef` + `.manifest.json` exactly like the production
contracts.

  - `Sample.CrossChainGreeter` — emit an L2 → L1 / L2 → L2 message via
    `L2MessageContract.EmitMessage`. Shows the standard pattern:
    take the L2Native hash at deploy, expose owner-gated update path,
    `Contract.Call` the partner with the operator-supplied (target,
    receiver, type, payload) tuple.
  - `Sample.WithdrawalDemo` — initiate an L2 → L1 withdrawal via
    `L2BridgeContract.InitiateWithdrawal`. Shows the burn-on-L2 +
    enqueue-into-batch-tree pattern users follow when claiming back to L1.

`samples/contracts/README.md` documents the wiring + the recipe for
adding more samples (csproj wrapper, `<Import>` of contracts/Directory.Build.props,
storage-prefix conventions, `_deploy` data tuple shape).

### Added — `neo-stack validate <chain.config.json>` subcommand

Sanity-checks a chain.config.json before deploy / devnet preview. Validates:
each enum field parses to a known value (lists valid values on failure),
each bool / int field is present + correctly typed, chainId is a non-zero
L2 (uses `ChainIdValidator`), and emits non-fatal cross-field warnings
(e.g. SecurityLevel.Validity SHOULD pair with proofType=Zk). Exits 0 with a
"✅ valid: ..." summary on success, 2 with a field-named error on failure.

Verified against all 4 samples — each prints clean ✅; a `"daMode": "Foo"`
typo correctly produces:
  `❌ 'daMode'='Foo' is not a valid DAMode (expected one of: L1, NeoFS, External, DAC)`

### Changed — `neo-stack register-chain` emits canonical configBytes hex

Composes the new `L2ChainConfigSerializer` with the operator-supplied L1
contract hashes (`--operator` / `--verifier` / `--bridge` / `--message`) to
print the 91-byte hex string ready for wallet-side `registerChain(chainId,
configBytes)` submission. Without those flags, falls back to the legacy
plan-only output and now hints the operator to re-run with addresses for the
canonical encoding. Errors surface field-by-field (e.g.
"chain.config.json 'securityLevel'='Foo' is not a valid SecurityLevel" or
"--operator='0xDEAD' is not a valid UInt160").

### Added — Per-batch withdrawal verification on L1

`SettlementManager.VerifyWithdrawalLeaf(chainId, leafHash)` only matched against
the *latest* finalized batch's `withdrawalRoot` — a withdrawal anchored in
batch N silently broke once N+1 finalized. Adds a 3-arg overload
`VerifyWithdrawalLeafAt(chainId, batchNumber, leafHash)` that verifies the
explicit batch (status=Finalized + matching root); the latest-only version is
now a thin wrapper. The operator-facing partner
`SharedBridge.FinalizeWithdrawalAt(chainId, batchNumber, …)` lets users claim
withdrawals against historical batches. Two private helpers
(`ValidateWithdrawalArgs`, `ConsumeAndPayout`) keep the latest-batch and named-
batch paths in sync — a future defensive check applies to both.

### Added — `PublicInputHashConsistencyCheck` resolver seam

The check's MVP simplification was zero-filling `L1MessageHash` and
`BlockContextHash` (matches Phase 0–3 settlement). Its remarks already noted
"future phases need an augmenting resolver." Adds the constructor overload
`PublicInputHashConsistencyCheck(Func<L2BatchCommitment, PublicInputs>?)` so
Phase 4+ can plumb in actual values from a side store. Default ctor preserves
zero-fill behavior.

### Misc — Test propagation + dispatch-coverage pins

  - 4 `L2ProverPlugin.Wire` dispatch coverage tests — the switch had 5
    branches (Multisig / Zk / Optimistic / None / unknown); only Multisig was
    tested. Made `Kind` settable so tests can drive the rest. Pins helpful-
    error contracts that point operators at the right plugin (Optimistic →
    L2SettlementPlugin) or valid alternatives (None → Multisig + Optimistic
    + Zk).
  - 2 plugin `WithMetrics` propagation pins (`L2BridgePlugin`, `L2DAPlugin`).
    Both source comments claimed "swap the sink in-place on existing
    processors / inner writer" but the propagation was unverified — a refactor
    that re-creates the processor / leaves the outer decorator stale would
    silently lose every subsequent metric on a mid-flight sink swap.

Cumulative: 859 tests / 26 projects.

### Fixed — Records with `ReadOnlyMemory<byte>` now follow the byte-content equality convention

`AGENTS.md` "Code style" mandates that records holding `ReadOnlyMemory<byte>`
override `Equals` + `GetHashCode` so byte-content participates. `L2BatchCommitment`
and `CrossChainMessage` already followed this; five other records did not — so
default record equality compared those fields by reference, and independently-
constructed records with identical bytes compared unequal (breaking
`Set.Contains` / `Dictionary` lookups, hash bucketing).

  - `DAPublishRequest` (Payload)
  - `DAReceipt` (Pointer)
  - `ProofRequest` (Witness)
  - `ProofResult` (Proof)
  - `BatchExecutionRequest` (Transactions list — element-wise) +
    `L1MessagesConsumed` (delegates to `CrossChainMessage.Equals`)

Each gets a typed `Equals` using `Span.SequenceEqual` / element-wise list
iteration and a `GetHashCode` using `HashCode.AddBytes`, matching the existing
pattern on `L2BatchCommitment`.

### Misc — Convention-consistency sweep across the test surface

Fills in test patterns that were inconsistent — each had a 2-out-of-N or
4-out-of-N coverage that left load-bearing properties unprotected against
silent refactor breakage.

  - 4 `IAuditCheck.Name` discriminator pins (`continuity` / `proof` /
    `no_zero_proof` / `public_input_hash`) — `batch_range` / `da_availability`
    already had them. Names are used by `ChainAuditor` for finding attribution
    and by metric tags / log filters.
  - 4 plugin `Name`+`Description` pins (`L2BatchPlugin` / `L2MetricsPlugin` /
    `L2DAPlugin` / `L2SettlementPlugin`) — `L2BridgePlugin` / `L2GatewayPlugin` /
    `L2ProverPlugin` already had them. Surface in plugin host startup logs.
  - 2 byte-layout-pinning tests for canonical encoders (`MultisigProofPayload`
    / `DepositPayload`) — the other 4 (`OptimisticProofPayload` / `RiscVProofPayload`
    / `FraudProofPayload` / `MerkleProofSerializer`) already had them, per the
    `CONTRIBUTING.md` "Adding a new wire format" recipe.
  - 3 `MessageHasher` canonical-buffer pins (`HashMessage` layout, `HashWithdrawal`
    layout, field-order sentinel — independently re-derive the documented
    buffer and assert `Hash256(buf)` equals the function output).
  - 2 at-max boundary tests partnering `RejectsOversizedAmount` on
    `DepositPayload.Encode` and `MessageHasher.HashWithdrawal` (use `2^504` as
    the exactly-64-byte sentinel). Other proof-payload encoders already had
    `AcceptsExactlyMax*` partners.
  - 4 enum discriminant pins (`BatchStatus` / `AssetType` / `ProofSystem` /
    `Sp1BridgeStatus`) — other 5 already had `HasExpectedDiscriminants` tests.
    `Sp1BridgeStatus` ties the C# values to the Rust cdylib's int32 ABI per
    `bridge/neo-zkvm-bridge/README.md`.
  - 7 `PassThroughRoundProver` byte-layout tests at the round-prover level,
    independent of the aggregator (BackendId=0xFE constant, right-null odd-leaf
    rule, `Hash256(left || right)` root composition,
    `[4B leftLen][bytes][4B rightLen][bytes]` proof envelope, both-empty
    handling, asymmetry, null-left rejection).

Plus per-iteration doc-drift fixes across `IMPLEMENTATION_STATUS`, `README`,
`WHITEPAPER`, `docs/persistence`, `docs/architecture-walkthrough`,
`contracts/README`, `CONTRIBUTING` (numbering bug), and the figure-gallery
count.

Cumulative: 851 tests / 26 projects.

### Added — Vendored upstream deps as git submodules (was: sibling-clone hack)

The repo expected three sibling-clones (`../neo`, `../../neo-devpack-dotnet`,
`../../../neo-zkvm`) and the existing code drifted onto APIs from a personal
fork of neo-core, so a fresh clone against the public r3e-network/neo-n4 url
couldn't even build.

Closed by vendoring all three as git submodules under `external/`:

  - `external/neo` — neo-project/neo master (Neo 4 core), commit `12df510d`
  - `external/neo-devpack-dotnet` — neo-project/neo-devpack-dotnet master, commit `80fcc176`
  - `external/neo-zkvm` — r3e-network/neo-zkvm master, commit `1b28c6e` (optional, behind `real-prover`)

Updated 8 callsites to use neo-project/neo master's stable APIs (the prior
fork had its own extensions):
  - `Crypto.Sign(bytes, byte[])` → `Crypto.Sign(bytes, new KeyPair(byte[]))`
    (5 callsites: `ISignerSet` + 3 test files)
  - `public override Dispose()` → `protected override Dispose(bool disposing)`
    on 3 plugins (`L2Batch`, `L2Metrics`, `L2Settlement`)
  - `using Neo.Extensions.IO;` for `Transaction.ToArray()` extension
  - `Neo.L2.sln` — 4 hardcoded `..\neo\src\` paths → `external\neo\src\`

`Directory.Build.props` `NeoCorePath` and `contracts/Directory.Build.props`
`NeoDevpackPath` now default to the submodule paths; both can be overridden
on the command line for local-fork development.

Docs all switched to `git clone --recurse-submodules`: README, CONTRIBUTING,
AGENTS, contracts/README, bridge/neo-zkvm-bridge/README, getting-started.

### Added — GitHub Actions CI workflow

`.github/workflows/build.yml` now runs on every push + PR to master. Three
jobs:
  - `test`: full `dotnet build` + `dotnet test` (820 unit + integration
    tests). Uses `submodules: recursive` so external/neo + external/
    neo-devpack-dotnet are checked out before the build.
  - `contracts`: type-checks all 19 NeoHub + L2Native contracts with
    `DisableNccs=true` (nccs not on runner; C# type-check still catches
    API drift).
  - `bridge`: `cargo check --no-default-features` for the Rust
    neo-zkvm-bridge so the Phase-4 SP1 prover dep stays optional.

CI status badge added to README header.

### Fixed — `neo-project/neo4` URL never existed

All `ContractSourceCode` attributes on the 19 contracts pointed at
`https://github.com/neo-project/neo4` — that repo never existed. Updated
to `https://github.com/r3e-network/neo-n4` (the actual public URL,
already in README clone instructions). Same fix for
`Directory.Build.props` `RepositoryUrl` (drives NuGet package metadata).

### Added — Phase 6 done: all 8 neo-stack subcommands functional

`register-chain` and `deploy-bridge-adapter` were the last two stub
subcommands ("would do X" placeholders). Both now produce structured
operator plans:
  - register-chain reads chain.config.json + prints target contract,
    method, args, config preview, and 3 numbered next steps for wallet-
    side submission
  - deploy-bridge-adapter prints the L2/L1 contract pair, required
    asset mappings, and operator next-steps

Neither performs L1 submission (genuinely operator-specific signer
requirement), but both give the operator the exact information needed
to script the submission. Phase 6 in README + IMPLEMENTATION_STATUS
bumped from 🟡 to ✅.

### Added — Two new audit checks: DAAvailabilityCheck + BatchRangeCheck

`Neo.L2.Audit` grew from 4 checks to 6:
  - `DAAvailabilityCheck` — pings each batch's DACommitment against
    the configured DA layer's IsAvailableAsync. Catches the "DA layer
    dropped/garbage-collected the payload" failure mode that would
    leave L2 batches recoverable in commitment but unrecoverable in
    actual data. Skips legacy zero-commitment batches.
  - `BatchRangeCheck` — flags intra-batch range inversions
    (firstBlock > lastBlock) and zero batch numbers (collide with
    genesis state-root assumptions). Cheap, no external lookups.

Both wired into the devnet's audit pass alongside the original 4
(ContinuityCheck, NoZeroProofCheck, ProofValidityCheck,
PublicInputHashConsistencyCheck).

### Added — Contract-level validation sweep across all 19 contracts

All 13 NeoHub + 6 L2 native contracts now have appropriate IsValid +
non-zero guards at deploy + per-method entry points. Targeted gaps:
  - All `_deploy` methods reject zero owner / dependency / asset hashes
  - chainId=0 (the L1 sentinel) rejected at every external mutator:
    ChainRegistry.Register/UpdateChain, ForcedInclusion.EnqueueForced,
    SequencerRegistry.Register, SharedBridge.Deposit/FinalizeWithdrawal,
    SequencerBond.Deposit, MessageRouter.EnqueueL1ToL2, OptimisticChallenge.OpenWindow
  - VerifierRegistry.RegisterVerifier rejects proofType outside [1..3]
  - DARegistry.Record range-checks daMode <= 3
  - L2FeeContract._deploy pins BPS sum == 10000
  - L2BatchInfoContract.Advance pins L1FinalizedHeight monotonic
  - L2MessageContract.EmitMessage + off-chain MessageBuilder.Build
    reject self-routed messages (sourceChainId == targetChainId, incl.
    zero-to-zero edge case)
  - GovernanceController.CreateProposal rejects empty payload
  - L2PaymasterContract validates user/asset hashes in ApproveAsset /
    TopUp / Charge

### Added — EmergencyManager EscapeHatchExit verifies against finalized state root (§15.5)

[Earlier entry — preserved]

### Misc

  - JsonRpcClient(string endpoint) ctor now uses ArgumentException.ThrowIfNullOrEmpty
    for clearer error message (was surfacing as confusing 'uriString' parameter name)
  - Devnet: --help/-h flag for usage parity with neo-stack and neo-hub-deploy
  - Devnet: stores now disposed cleanly via `using var`; 0-batch run with
    --data-dir for rehydration verification gracefully skips audit
  - ChainAuditor mixed-chainId rejection pinned with explicit message-format test

Cumulative: 820 tests / 26 projects.

### Added — RocksDB-by-default persistence across all stateful L2 components

The "L2 component holds an in-memory dict" pattern is fine for tests + devnets but
unacceptable in production: a sequencer mid-exit losing its `ExitsAtUnixSeconds`
deadline on restart could re-admit a sequencer that should be in cooldown, or fail
to finalize an exit that already passed its window. Same correctness risk for
finalized message proofs, withdrawal proofs, consumed forced-inclusion nonces, and
DA payloads.

- **New `Neo.L2.Persistence` project** with `IL2KeyValueStore` abstraction
  (`Put` / `Get` / `Delete` / `Contains` / `EnumeratePrefix` / `Count` / `IDisposable`),
  plus `InMemoryKeyValueStore` (devnet/test default) and `RocksDbKeyValueStore`
  (RocksDB 10.4.2 with snappy compression — production default).
- **Six L2 components** now take an optional `IL2KeyValueStore` ctor overload with an
  ownership flag, with a backwards-compat default ctor that wires `InMemoryKeyValueStore`:
  - `KeyedStateStore` — (asset, holder) → balance entries
  - `InMemoryL2RpcStore` — withdrawal + message proofs (33-byte prefixed keys)
  - `InMemoryMessageRouter` — finalized message proofs
  - `InMemoryForcedInclusionSource` — consumed nonce set (8-byte LE keys)
  - `InMemorySequencerCommitteeProvider` — committee membership + exit windows
    (write-through dict + shadow KV writes; hydrated on construction)
  - `PersistentDAWriter` (new) — content-addressed batch payloads
- **Devnet `--data-dir <path>` flag** wires four of these stores under one root
  automatically (`state/`, `rpc-proofs/`, `sequencer/`, `da/`); state survives
  restart, with banner showing in-memory vs RocksDB mode.
- **`L2DAPlugin.BuildDefaultWriter(DAMode, dataDir)`** static helper extracted from
  `Configure()` so the DataDirectory-driven RocksDB path is unit testable without
  driving Plugin.GetConfiguration() through a real config.json.
- **Per-component reopen tests** plus an end-to-end integration test
  (`UT_E2E_Persistence_FullStack`) that pins the multi-component story:
  four RocksDB instances under one root, no cross-contamination, all four
  rehydrate from disk after dispose+reopen.
- **Docs**: new `docs/persistence.md` operator guide, Walk #5 in
  `docs/architecture-walkthrough.md`, §4.4 in `WHITEPAPER.md`, refreshed
  `docs/getting-started.md` with the persistent-devnet sub-section.

### Added — EmergencyManager EscapeHatchExit verifies against finalized state root (§15.5)

Closed a real correctness gap vs `doc.md` §15.5: `EscapeHatchExit` was recording any
leaf hash without verifying it actually corresponds to the chain's latest finalized
state root. A user could submit a fabricated leaf, get the `OnEscapeHatchExit` event,
and trick downstream off-chain indexers into thinking they were exiting a real balance.

- `EscapeHatchExit` gains a `chainId` parameter (was missing entirely — multi-L2
  architecture needs to know which L2 the exit is from)
- Cross-calls `SettlementManager.getCanonicalStateRoot(chainId)` and asserts
  `leafHash` matches (consistent with `SharedBridge.VerifyWithdrawalLeaf`'s MVP
  simplification — full Merkle path verification deferred off-chain)
- Replay protection moves from `leafHash` to `(chainId, leafHash)` so a leaf valid
  on chain A can't be replayed against chain B's escape hatch
- Event `OnEscapeHatchExit` gains chainId so off-chain indexers can attribute each
  exit to the right L2
- `_deploy` now takes a 3rd arg (`settlementManager`); deploy scaffolder updated to
  `OwnerAndDeps("GovernanceController", "SettlementManager")` + dependsOn

### Added — Deploy scaffold completes all 13 NeoHub contracts + post-deploy hint

Scaffold previously emitted only 10 of 13 NeoHub contracts — missing
`SequencerBond`, `SequencerRegistry`, `OptimisticChallenge`. An operator following
`IMPLEMENTATION_STATUS` would think they had a complete bundle but be silently
missing the Phase-3 challenge stack.

- All 13 NeoHub contracts now in scaffold output (verified via
  `Scaffold_DefaultIncludesAllNeoHubContracts`)
- Deploy cycle (bond ↔ challenge) broken via initial slashers list of just
  `[GovernanceController]`; operator wires `SequencerBond.RegisterSlasher(challenge)`
  post-deploy
- New `ScaffoldPlan.PostDeployActions(bundle)` API yields one human-readable line
  per required follow-up call; `plan` command now prints them under
  "Required post-deploy actions:" after the bundle summary
- 3 new tests pin: full bundle → emits hint, partial bundle → suppresses hint,
  null bundle → ArgumentNullException

### Added — neo-stack CLI: real `submit-batch` validation + `start-*` preflight

Six of eight `neo-stack` subcommands (per `doc.md` §14.2) were "would do X" stubs;
three are now functional, the remaining three (register-chain, deploy-bridge-adapter,
real process spawning) genuinely require operator-side L1 wallet integration.

- `submit-batch` now reads + decodes via `BatchSerializer`, surfaces decode errors at
  CLI entry (clear message) instead of at L1 (opaque revert), prints a structured
  summary (chainId, batchNumber, blocks, state roots, proof type + length), uses
  distinct exit codes for file-not-found / read-failure / decode-failure
- `start-sequencer` / `start-batcher` / `start-prover` validate `--chain-id` is
  non-zero, verify chain dir + `chain.config.json` exist, print composition guidance
  (which plugins to load, where, how to wire into neo-cli) on success, point at
  create-chain → init-l2 sequence on failure

### Added — Devnet publishes each batch payload to DA writer

Closed the last in-memory-by-default gap in the devnet — previously the DA step
from `doc.md` §15.1 was skipped entirely (`DACommitment` hard-coded to `UInt256.Zero`),
even though production has `DAWriter` publishing batch data before the prover runs.

- Devnet now publishes each batch's payload to an `IDAWriter` between
  `PostStateRoot` computation and proof generation
- `PublicInputs.DACommitment` + `L2BatchCommitment.DACommitment` set to the real
  receipt commitment so proofs bind to the published DA layer
- With `--data-dir`, writer is `PersistentDAWriter` over RocksDB; without, it's
  `InMemoryDAWriter`

### Removed — Two empty placeholder test directories

`tests/Neo.L2.UnitTests` and `tests/Neo.L2.ContractTests` were empty placeholder
dirs that confused project counts (`ls tests/` reported 28, actual was 26
csproj-wise). Deleted both.

Cumulative: 800 tests / 26 projects.

### Added — `HashL1Messages` empty-list zero-hash boundary pin

- `StateRootCalculator.HashL1Messages` short-circuits to `UInt256.Zero` for empty lists (`StateRootCalculator.cs:27`). Pinned the boundary so a refactor that drops the early return would not silently change the empty-batch hash.

Cumulative: 658 tests / 27 projects.

### Added — Challenge orchestrator per-member null pins

- iter 171's `Inspect_RejectsNullPreStateRootInCommitment` was the only per-member null-pin in `ChallengeOrchestrator`. Added 4 companion pins for the remaining per-member guards:
  - `Inspect_RejectsNullPostStateRootInCommitment` (`ChallengeOrchestrator.cs:42`)
  - `Inspect_RejectsNullPreStateRootInInputs` (`:43`)
  - `InspectWithBisection_RejectsNullPostStateRootInCommitment` (`:100`)
  - `InspectWithBisection_RejectsNullPreStateRootInInputs` (`:101`)

Cumulative: 657 tests / 27 projects.

### Added — `PrometheusExporter.Format` null-arg pins

- `Format(null)` (`PrometheusExporter.cs:29`) was unpinned. The existing iter-200 `Format_RejectsMalformedSnapshotWithNullDictionary` only exercised the Counters guard (line 33); the Gauges (`:34`) and Histograms (`:35`) per-field null-guards were unpinned too. Added 3.

Cumulative: 653 tests / 27 projects.

### Added — `InMemorySignerSet` ctor null-keys pin

- `InMemorySignerSet` ctor's `ThrowIfNull(keys)` (`ISignerSet.cs:42`) was unpinned. Without it the OrderBy on null NREs.

Cumulative: 650 tests / 27 projects.

### Added — `Sp1RiscVProver.ProveAsync` null-request pin

- Sp1RiscVProver.ProveAsync's null-request guard (`Sp1RiscVProver.cs:41`) was unpinned. Symmetric to MockRiscVProver.ProveAsync's pin (iter 240). Added `Sp1Prover_ProveAsync_RejectsNullRequest`.

Cumulative: 649 tests / 27 projects.

### Added — Executor callee-contract sub-field pins + KeyedStateRootOracle ctor

- ReferenceBatchExecutor's iter-173 callee-contract test pinned the whole-result-null path. The sub-field null-guards at `ReferenceBatchExecutor.cs:71` (result.Receipt) and `:72` (result.TxHash) covered a distinct callee-contract path: a non-null result with a null sub-field. Added 2.
- `KeyedStateRootOracle` ctor's `ThrowIfNull(store)` (`KeyedStateRootOracle.cs:23`) was unpinned. Added `Oracle_Constructor_RejectsNullStore`.

Cumulative: 648 tests / 27 projects.

### Added — `RpcSettlementClient.SubmitBatchAsync` null-commitment pin

- `RpcSettlementClient.SubmitBatchAsync` had a `BuggySignAndSendReturnsNull_SurfacesContractViolation` callee-contract pin but no direct null-commitment pin (`RpcSettlementClient.cs:46`). Added `SubmitBatchAsync_RejectsNullCommitment`.

Cumulative: 645 tests / 27 projects.

### Added — `BinaryTreeAggregator` null-arg pins

- `BinaryTreeAggregator.Submit(null)` (`BinaryTreeAggregator.cs:53`) and `WithMetrics(null)` (`:40`) had no pins. Symmetric to the PassThroughAggregator pins. Added 2.

Cumulative: 644 tests / 27 projects.

### Added — State-primitive boundary pins

- `MerkleTree.GetProof` had no out-of-range index pin. Added `GetProof_RejectsOutOfRangeIndex` (`MerkleTree.cs:98-99`) covering both negative and >= LeafCount.
- `StateRootCalculator.HashBlockContext` null-context guard (`:53`) and one representative of the 10 per-field null-guards in `HashPublicInputs` (`:77-86`) were unpinned. Added 2.

Cumulative: 642 tests / 27 projects.

### Added — Messaging null-arg pins

- `L2Outbox.Add(null)` (`L2Outbox.cs:39`), `L1MessageInbox.Enqueue(null)` (`L1MessageInbox.cs:37`), and `InMemoryMessageRouter.EnqueueOutboundAsync(null)` (`InMemoryMessageRouter.cs:44`) had no pins. Added 3.

Cumulative: 639 tests / 27 projects.

### Added — Verifier/prover async-method null-publicInputs pins

- 4 unpinned async-method null-arg guards across the verifier/prover pipeline. Added: `OptimisticVerifier_VerifyAsync_RejectsNullPublicInputs` (`OptimisticVerifier.cs:37`), `AttestationVerifier_VerifyAsync_RejectsNullPublicInputs` (`AttestationVerifier.cs:48`), `MockRiscVProver_ProveAsync_RejectsNullRequest` (`RiscVProver.cs:52`), `MockRiscVVerifier_VerifyAsync_RejectsNullPublicInputs` (`:103`).

Cumulative: 636 tests / 27 projects.

### Added — Proving-pipeline null-arg pins

- 7 unpinned guards in the proving pipeline. Added: `VerifierRegistry_Register_RejectsNullVerifier` (`VerifierRegistry.cs:21`), `VerifyAsync_RejectsNullCommitment` (`:37`), `VerifyAsync_RejectsNullPublicInputs` (`:38`); `OptimisticVerifier_Constructor_RejectsNullSequencerKey` (`OptimisticVerifier.cs:26`); `AttestationProver_Constructor_RejectsNullSigners` (`AttestationProver.cs:25`), `ProveAsync_RejectsNullRequest` (`:32`); `AttestationVerifier_Constructor_RejectsNullValidators` (`AttestationVerifier.cs:31`).

Cumulative: 632 tests / 27 projects.

### Added — InMemoryL2RpcStore null-arg pins (companion to iter 184)

- iter 184's `Store_RejectsNullKey_AcrossEntryPoints` covered the leafHash/messageHash/l1Asset/l2Asset null-keys but not other write paths. Added 5: `AddBatch_RejectsNullCommitment` (`InMemoryL2RpcStore.cs:47`), `RegisterAsset_RejectsNullL1Asset` (`:83`), `_RejectsNullL2Asset` (`:84`), `RecordWithdrawalProof_RejectsNullProofBytes` (`:110`), `RecordMessageProof_RejectsNullProofBytes` (`:121`).

Cumulative: 625 tests / 27 projects.

### Added — `Receipt.Hash` null-guard pins

- The 3 Receipt.Hash UInt256 null-guards (`Receipt.cs:36-38`) were unpinned. Same iter-154+ hashing-primitive defense-in-depth pattern as MessageHasher, FraudProofPayload, etc. Added `Receipt_Hash_RejectsNullTxHash`, `_RejectsNullStorageDeltaHash`, `_RejectsNullEventsHash`.

Cumulative: 620 tests / 27 projects.

### Added — Executor + RPC client null-arg ctor pins

- `JsonRpcClient` had `RejectsRelativeUri` and `RejectsNonHttpScheme` pins (iter 206/207) but no null-arg pins. Added 3: `Constructor_RejectsNullEndpoint`, `CallAsync_RejectsNullMethod`, `CallAsync_RejectsNullParams` (`JsonRpcClient.cs:26, 63, 64`).
- `RpcSettlementClient` ctor null-arg guards (`RpcSettlementClient.cs:36-37`) were unpinned. Added 2: `Constructor_RejectsNullRpc`, `Constructor_RejectsNullSignAndSend`.
- `ReferenceBatchExecutor` ctor null-arg guards and `ApplyBatchAsync` null-request guard (`ReferenceBatchExecutor.cs:30-31, 42`) were unpinned. Added 3: `Constructor_RejectsNullTxExecutor`, `Constructor_RejectsNullPostStateRootOracle`, `ApplyBatchAsync_RejectsNullRequest`.

Cumulative: 617 tests / 27 projects.

### Added — More state-tree + serializer null pins

- `MessageTree_Add_RejectsNullMessage` (`MessageTree.cs:25`) — companion to the per-member `RejectsNullMessageHash` pin (iter 185).
- `WithdrawalTree_Add_RejectsNullWithdrawal` (`WithdrawalTree.cs:23`).
- `MerkleProofSerializer.Encode_RejectsNullLeaf` / `_RejectsNullSiblings` (`MerkleProofSerializer.cs:36-37`) — companion to the existing `RejectsNullProof` param-level pin.
- `MerkleTree.Verify_RejectsNullSiblings` (`MerkleTree.cs:137`) — companion to the existing `Verify_RejectsNullLeaf` (iter 168) and `_RejectsNullProof` (iter 234).

Cumulative: 609 tests / 27 projects.

### Added — State-primitive null-guard pins

- `MerkleTree.ComputeRoot`'s param-level null-leaves guard (`MerkleTree.cs:32`) was unpinned — only the per-entry null-guard from iter 179 had a test. Same for `Verify`'s null-proof guard (`:131`). Added 2.
- `StateRootCalculator` had no null pins on `HashL1Messages`, `HashBlockContext`'s SequencerCommitteeHash, or `HashPublicInputs` (`StateRootCalculator.cs:26, 57, 74`). Added 3.

Cumulative: 604 tests / 27 projects.

### Added — Censorship + plugin WithMetrics null pins

- `CensorshipDetector` had the negative-slash test (`Constructor_RejectsNegativeBaseSlashAmount`) but the ctor's null-source / null-committee guards (`CensorshipDetector.cs:40-41`) were unpinned. Added 2.
- `L2BatchPlugin` and `L2DAPlugin` had no `WithMetrics(null)` pin (`L2BatchPlugin.cs:40`, `L2DAPlugin.cs:37`). Added 2 — symmetric to the L2SettlementPlugin pin from iter 228.

Cumulative: 599 tests / 27 projects.

### Added — `BatchSealer` param-level null pins

- BatchSealer had a `ValidatePositive` ctor pin (iter 191) and a per-entry-null `RejectsNullTransactionInList` pin (iter 181), but the param-level null-guards on the ctor (`BatchSealer.cs:41-42`), WithMetrics (`:34`), and OnBlockCommit's rawTransactions arg (`:74`) were unpinned. Added 4: `Constructor_RejectsNullSettings`, `Constructor_RejectsNullMetrics`, `WithMetrics_RejectsNullMetrics`, `OnBlockCommit_RejectsNullRawTransactions`.

Cumulative: 595 tests / 27 projects.

### Added — DA writer null-guard pins

- `InMemoryDAWriter` and `NeoFsLikeDAWriter` had no null-arg pins on PublishAsync / IsAvailableAsync / TryGet; only `MetricsEmittingDAWriter`'s ctor was pinned. Added 7:
  - `InMemoryDAWriter_PublishAsync_RejectsNullRequest` (`InMemoryDAWriter.cs:28`)
  - `InMemoryDAWriter_IsAvailableAsync_RejectsNullReceipt` (`:45`), `_RejectsNullCommitment` (`:49`)
  - `NeoFsLikeDAWriter_PublishAsync_RejectsNullRequest` (`NeoFsLikeDAWriter.cs:32`)
  - `NeoFsLikeDAWriter_IsAvailableAsync_RejectsNullReceipt` (`:59`), `_RejectsNullCommitment` (`:60`)
  - `NeoFsLikeDAWriter_TryGet_RejectsNullObjectId` (`:82`)

Cumulative: 591 tests / 27 projects.

### Added — Bisection-game param-level null pins

- `BisectionGame` had a `Constructor_RejectsNullCheckpointEntry` per-entry pin (iter 197) but the param-level array null-guards (`BisectionGame.cs:60-61`) were unpinned. Added 2.
- `ChallengeOrchestrator.InspectWithBisectionAsync` had per-entry, length, and shape pins but no param-level pins for the 4 ref-typed args (`ChallengeOrchestrator.cs:93-96`). Added 4.

Cumulative: 584 tests / 27 projects.

### Added — Challenge module null-guard pins

- `FraudProofPayload.Encode`'s 3 UInt256 root null-guards (`FraudProofPayload.cs:50-52`) were unpinned. Added 3 (PreStateRoot, ClaimedPostStateRoot, ReplayedPostStateRoot).
- `ChallengeOrchestrator` had `Inspect_RejectsNullPreStateRootInCommitment` (iter 171) but the param-level guards (ctor null-replayer, Inspect null commitment, Inspect null inputs at `ChallengeOrchestrator.cs:19, 36, 37`) weren't pinned. Added 3.

Cumulative: 578 tests / 27 projects.

### Added — Settlement & RPC plugin null-arg pins

- `L2SettlementPlugin` had no null-arg pins on its public Wire / WithMetrics / Enqueue surface (`L2SettlementPlugin.cs:52-54, 77, 102`). Added 5: `Wire_RejectsNullBatchPlugin`, `Wire_RejectsNullProver`, `Wire_RejectsNullClient`, `WithMetrics_RejectsNullMetrics`, `Enqueue_RejectsNullCommitment`.
- `L2RpcMethods` ctor's `ArgumentNullException.ThrowIfNull(store)` (`L2RpcMethods.cs:28`) was unpinned. Added `Constructor_RejectsNullStore`.

Cumulative: 572 tests / 27 projects.

### Added — Sequencer-committee null-pubKey pins

- `InMemorySequencerCommitteeProvider` had a `Register_RejectsNullL1Address` pin (iter 148) but the four ECPoint-keyed null-guards (Register pubKey, BeginExit pubKey, Finalize pubKey, IsRegisteredAsync sequencerKey at `InMemorySequencerCommitteeProvider.cs:38`, `:70`, `:85`, `:143`) were unpinned. Added 4. Without these guards a null ECPoint would propagate into Dictionary.ContainsKey(null) with a generic "key" error message.

Cumulative: 566 tests / 27 projects.

### Added — Audit-module null-guard pins

- The audit module had only one direct null pin (`ProofValidityCheck_NullBatches_ThrowsArgumentNullException`); the other 7 guards on `ChainAuditor`, `ContinuityCheck`, `NoZeroProofCheck`, `PublicInputHashConsistencyCheck`, and `ProofValidityCheck`'s ctor were unpinned. Added 7:
  - `ChainAuditor_Register_RejectsNullCheck`, `ChainAuditor_AuditAsync_RejectsNullBatches`
  - `ProofValidityCheck_Constructor_RejectsNullRegistry`, `ProofValidityCheck_Constructor_RejectsNullPublicInputsResolver`
  - `ContinuityCheck_RunAsync_RejectsNullBatches`
  - `NoZeroProofCheck.RunAsync_RejectsNullBatches`
  - `PublicInputHashConsistencyCheck.RunAsync_RejectsNullBatches`

Cumulative: 562 tests / 27 projects.

### Added — Batch-construction boundary pins

- `BatchSerializer.EncodePublicInputs` (`BatchSerializer.cs:205-216`) had no null-guard pins. Added 2 representative pins (the inputs param, plus PreStateRoot for the per-field pattern that's uniform across 10 fields).
- `L2Batch` constructor (`L2Batch.cs:59`) had no null-PreStateRoot pin. Added `Constructor_RejectsNullPreStateRoot` — without it a null root would surface only at Seal time when EncodePublicInputs runs its own ThrowIfNull, attributing the failure to the sealer rather than the constructor.
- `BatchBuilder` had no null-arg pins on its 4 Add* / ConsumeL1Message methods. Added 4: `ConsumeL1Message_RejectsNull`, `AddWithdrawal_RejectsNull`, `AddL2ToL1Message_RejectsNull`, `AddL2ToL2Message_RejectsNull`.

Cumulative: 555 tests / 27 projects.

### Added — `DepositProcessor` boundary pins

- Symmetric to iter 223's WithdrawalProcessor pins. Process happy path and replay are tested, but the boundary contracts weren't. Added 5:
  - `DepositProcessor_Process_RejectsNullMessage` (`DepositProcessor.cs:45`)
  - `DepositProcessor_Constructor_RejectsNullRegistry` (`:26`)
  - `DepositProcessor_WithMetrics_RejectsNullMetrics` (`:35`)
  - `DepositProcessor_Process_RejectsWrongMessageType` (`:49-50`) — a non-Deposit message would otherwise reach DepositPayload.Decode and produce a confusing parse error
  - `DepositProcessor_Process_RejectsWrongTargetChain` (`:51-52`) — a message targeting a different L2 must not be processed locally

Cumulative: 548 tests / 27 projects.

### Added — `WithdrawalProcessor` boundary pins

- The Stage L2Sender / L1Recipient null-guards were already pinned (iter 147), but the four other paths weren't. Added 5:
  - `WithdrawalProcessor_Stage_RejectsNullRequest`
  - `WithdrawalProcessor_Stage_RejectsNullEmittingContract` (`WithdrawalProcessor.cs:61`)
  - `WithdrawalProcessor_Stage_RejectsNullL2Asset` (`:64`) — without this the bad input would surface only in the registry's TryGetByL2(null) call with a generic "key" message
  - `WithdrawalProcessor_Constructor_RejectsNullRegistry`
  - `WithdrawalProcessor_WithMetrics_RejectsNullMetrics` (the ctor accepts null metrics → NoOpMetrics, but the explicit-swap path must not)

Cumulative: 543 tests / 27 projects.

### Added — `KeyedStateStore.Put` boundary pins

- `Put_RejectsEmptyKey` pins `KeyedStateStore.cs:32`'s `ArgumentOutOfRangeException.ThrowIfZero(key.Length)`. Empty keys would hash via HashEntry to leaves distinguishable only by value.
- `Put_DefensiveCopy_CallerMutationAfterPutDoesNotCorruptStore` is the symmetric write-side pin to the existing `EnumerateSorted_DefensiveCopy` (read-side, iter 176). Same iter-167 ToArray() copy pattern used by `InMemoryL2RpcStore.RecordWithdrawalProof` and `InMemoryMessageRouter.RecordFinalized`.

Cumulative: 538 tests / 27 projects.

### Added — `DepositPayload.Encode` defensive-guard pins

- DepositPayload's Decode-side malleability tests (`Decode_RejectsTrailingBytes`) are pinned, but its Encode-side guards at `DepositPayload.cs:30-31` (UInt160 null-guards on L1Asset / L2Recipient) and `:33-34` (64-byte amount cap) had no direct tests. Added 3:
  - `DepositPayload_Encode_RejectsNullL1Asset`
  - `DepositPayload_Encode_RejectsNullL2Recipient`
  - `DepositPayload_Encode_RejectsOversizedAmount` (mirror of `HashWithdrawal` amount cap pinned in iter 218)

Cumulative: 536 tests / 27 projects.

### Added — Proof-payload Encode-side null-guard pins

- Three proof payloads (`MultisigProofPayload`, `OptimisticProofPayload`, `RiscVProofPayload`) all share the iter-159 Encode/Decode symmetry pattern with size-cap guards. Many of those size-cap and per-entry guards are pinned, but the top-level reference-typed null-guards at the start of `Encode` are not. Added 5 pins:
  - `MultisigPayload_Encode_RejectsNullSignaturesCollection` (`MultisigProofPayload.cs:31`)
  - `MultisigPayload_Encode_RejectsNullPublicKey` (`:47` — per-signer guard; companion to `RejectsNullSignerEntry`)
  - `OptimisticProofPayload_Encode_RejectsNullBondContract` (`OptimisticProofPayload.cs:51`)
  - `OptimisticProofPayload_Encode_RejectsNullBondTxHash` (`:52`)
  - `RiscVProofPayload_Encode_RejectsNullVerificationKeyId` (`RiscVProofPayload.cs:45`)

Cumulative: 533 tests / 27 projects.

### Added — `BatchSerializer.Encode` defensive-guard pinning tests

- The Decode side of BatchSerializer is well-pinned (`Commitment_Decode_RejectsHeaderClaimingOversizedProof`, `Commitment_Decode_RejectsTrailingBytes`, `Commitment_Decode_RejectsUnknownProofType`, `Commitment_Decode_AcceptsAllValidProofTypes`). The Encode-side guards at `BatchSerializer.cs:77` (null commitment), `:81-89` (per-field null-guards on 9 UInt256 root fields), and `:95-98` (iter-159 Encode/Decode symmetry — refuse to produce bytes Decode would later reject) had no direct pins. Added 3:
  - `Commitment_Encode_RejectsNullCommitment`
  - `Commitment_Encode_RejectsNullRootField` (representative — pattern uniform across 9 fields)
  - `Commitment_Encode_RejectsOutOfRangeProofType` (iter-159 symmetry)

Cumulative: 528 tests / 27 projects.

### Added — Direct unit tests for `MessageHasher` static utility

- New `UT_MessageHasher.cs` directly pins the per-field null-guards on `HashMessage` and `HashWithdrawal`. Existing coverage is at the upstream `MessageBuilder.Build` / `WithdrawalProcessor.Stage` boundary (UT_Messaging, UT_Bridge), which catches null fields before they reach the hasher. But MessageHasher is a static utility — any caller can invoke it directly. The per-field guards at `MessageHasher.cs:27-28` and `:58-61` are documented as defense-in-depth for that case; without direct pins, a refactor could drop them silently.
- Tests added (10): `HashMessage_RejectsNullMessage`, `_RejectsNullSender`, `_RejectsNullReceiver`, `_DeterministicForSameInput`; `HashWithdrawal_RejectsNullWithdrawal`, `_RejectsNullEmittingContract`, `_RejectsNullL2Sender`, `_RejectsNullL1Recipient`, `_RejectsNullL2Asset`, `_RejectsOversizedAmount` (this last one pins the iter-set 64-byte amount cap that bounds the buffer alloc against attacker-influenced sizes).

Cumulative: 525 tests / 27 projects.

### Added — Two pinning tests: secondary-ctor null-address and JSON-RPC negative-ulong wrap

- `Constructor_SecondaryOverload_RejectsNullAddress` (UT_MetricsHttpServer): the `(IPAddress, int port, handler)` ctor delegates null-address handling to `IPEndPoint` via `MakeValidatedEndpoint`. That contract was documented in a comment but unpinned — a refactor could silently change the surface from `ArgumentNullException` to NRE.
- `RejectsNegativeNumberForULongParam_OverflowException` (UT_L2RpcMethods): companion to `RejectsOversizedChainId_OverflowException`. Pins the OTHER half of `L2RpcMethods.cs:182`'s `checked((ulong)(BigInteger)n.AsNumber())` — without `checked`, a negative JSON-RPC number (e.g. `-5`) would silently wrap into a huge positive ulong, turning a "missing batch" lookup miss instead of surfacing the bad input shape clearly.

Cumulative: 515 tests / 27 projects.

### Added — Pinning tests for iter-198 Sp1RiscV ctor null-guards (companion to iter 215)

- iter 215 pinned the Mock* ctor null-guards but the changelog incorrectly claimed the Sp1* counterparts were "tested by the same code path." That was wrong: `Sp1RiscVProver` and `Sp1RiscVVerifier` each have their OWN `ArgumentNullException.ThrowIfNull` guard at the top of their ctor (`Sp1RiscVProver.cs:33` and `:110`) — these fire *before* the fallback `MockRiscV*` is constructed, so the iter-215 pins don't exercise them. Now directly pinned: `Sp1Prover_Constructor_RejectsNullVerificationKeyId` and `Sp1Verifier_Constructor_RejectsNullExpectedVkId`. A refactor that dropped the Sp1* guard would silently fall through to the Mock*'s, attributing the failure to the wrong type.

Cumulative: 513 tests / 27 projects.

### Added — Pinning tests for iter-198 RISC-V ctor null-guards

- iter-198 added 4 ctor null-guards (`Sp1RiscVProver`, `Sp1RiscVVerifier`, `MockRiscVProver`, `MockRiscVVerifier`) but committed without pinning tests at the time. Now pinned: `MockRiscVProver_Constructor_RejectsNullVerificationKeyId` and `MockRiscVVerifier_Constructor_RejectsNullExpectedVkId`. (The Sp1* counterparts are pinned in the next iter — see above.)

Cumulative: 511 tests / 27 projects.

### Added — Direct unit tests for `MetricsExtensions` Safe* helpers

- The iter-163 `MetricsExtensions.SafeIncrementCounter`/`SafeRecordHistogram`/`SafeSetGauge` helpers were tested transitively via plugins (the iter-163 `WithdrawalProcessor_Stage_SurvivesThrowingMetricsSink` test). Now have direct unit tests in a new `UT_MetricsExtensions.cs`: 3 tests confirming a `ThrowingSink` doesn't surface failures + 1 happy-path assertion that the wrapper actually forwards to the underlying sink. Pins the contract directly so a future refactor can't accidentally remove the swallow.

Cumulative: 509 tests / 27 projects.

### Added — `InMemoryForcedInclusionSource` boundary tests

- 3 new tests pinning behaviors: `Enqueue` rejects null entry (the iter-148 null-guard pattern was already in the code but lacked a regression test), `DrainAsync(0)`/`DrainAsync(-5)` short-circuit to empty without consuming, and `HasOverdueEntryAsync` returns false on an empty queue regardless of clock value.

Cumulative: 505 tests / 27 projects.

### Added — `L1MessageInbox` boundary + happy-path coverage

- 4 new tests: `Dequeue(0)` returns empty without consuming, `Dequeue(-1)` throws `ArgumentOutOfRangeException`, `Dequeue(N > pending)` drains all available, and `HasConsumed` flips post-dequeue. Caught a small test-writing bug on first run (the `Build()` helper hard-codes `SourceChainId = 1001`, but I queried `HasConsumed(0, ...)` initially — same `BuildScenario(N, ...)` off-by-one trap as iter 197).

Cumulative: 502 tests / 27 projects.

### Added — `AssetRegistry` happy-path coverage: `SetActive` unknown-asset, `Snapshot` immutability

- The defensive sweep is at plateau. Pivoted to filling test-coverage gaps. `SetActive_UnknownAsset_ReturnsFalse` pins the contract that `SetActive` returns false (not throws) for missing L2 assets, so a future "throw on unknown" refactor is caught. `Snapshot_ReturnsAllMappings_AndIsImmutable` asserts that registering after a snapshot does NOT mutate the prior snapshot — pinning the `_byL2.Values.ToArray()` defensive copy. Caught a small naming bug on first run (`AssetType.NEP17` is actually `Nep17`).

Cumulative: 498 tests / 27 projects.

### Fixed — `MetricsHttpServer(IPAddress, int port, ...)` validates port via `PortValidator`

- The `(IPAddress, int port, MetricsRequestHandler)` overload previously delegated port validation to `IPEndPoint`, which throws a generic `port` `ArgumentOutOfRangeException`. The existing `PortValidator.Validate` is in the same project and surfaces the contextual message `"MetricsHttpServer port {x} out of range — must be 0 (any free) or 1..65535"`. Now both overloads use it (via a new `MakeValidatedEndpoint` helper). 1 pinning test covering the -1 / 65536 reject cases.

Cumulative: 496 tests / 27 projects.

### Fixed — `JsonRpcClient` rejects relative URIs (iter-206 follow-up)

- The iter-206 scheme check accesses `endpoint.Scheme`, which throws `InvalidOperationException("operation not supported on relative URI")` for a relative URI. So a caller passing `new Uri("/x", UriKind.Relative)` would get that confusing message instead of a clear "endpoint must be absolute". Now we check `IsAbsoluteUri` first. 1 pinning test.

Cumulative: 495 tests / 27 projects.

### Fixed — `JsonRpcClient` rejects non-HTTP schemes at construction

- A misconfigured endpoint (`file://`, `mailto:`, `ftp://`, `gopher://`, etc.) silently slipped past the ctor and surfaced as an obscure `HttpClient.SendAsync` error at first request — by which point the operator has lost the trail back to the bad config. Now the ctor rejects any `endpoint.Scheme` that isn't `http` or `https`. 1 pinning test covering 3 reject cases + 2 boundary accepts.

Cumulative: 494 tests / 27 projects.

### Fixed — `JsonRpcClient(Uri, HttpClient?)` ctor null-guards `endpoint`

- The string-overload `JsonRpcClient(string endpoint)` fails fast via `new Uri(null)`, but the `(Uri, HttpClient?)` overload silently assigned a null endpoint, then later NRE'd inside `HttpRequestMessage`'s constructor at first request. Surface at the ctor. Trivial 1-line guard, no pinning test.

Cumulative: 493 tests / 27 projects.

### Fixed — `JsonRpcClient` clamps out-of-range server error code

- `(int)n.AsNumber()` cast on a server-supplied error code wrapped silently for `code` values outside int range (e.g. `1e100` → `int.MinValue`). The `JsonRpcException` would carry the wrong code; operators chasing the value in dashboards would see a confusing `-2147483648`. Now: bound-check the double first; out-of-range falls back to the `-32603` "internal error" sentinel. Same iter-202/203 wrap-protection pattern. 1 pinning test.

Cumulative: 493 tests / 27 projects.

### Fixed — `MerkleProofSerializer.Decode` rejects `leafIndex > int.MaxValue`

- Encoder writes `leafIndex` via `(uint)proof.LeafIndex` after a `LeafIndex < 0` check, so honest output is in `[0, int.MaxValue]`. A malicious or corrupt input could carry `leafIndex > int.MaxValue`; the `(int)cast` in `Decode` would silently wrap to negative. Same iter-202 ulong/int wrap pattern. 1 pinning test that crafts a wire-form payload with `leafIndex = uint.MaxValue` and asserts the decoder rejects.

Cumulative: 492 tests / 27 projects.

### Fixed — `BatchSealer.ShouldSeal` int-cast wrap on >2.1B blocks

- `var blocksInBatch = builder.Batch.LastBlock - builder.Batch.FirstBlock + 1;` is `ulong`. The previous `(int)blocksInBatch >= _settings.MaxBlocksPerBatch` cast wrapped when `blocksInBatch > int.MaxValue` (~2.1B), making the seal-by-blocks check silently never fire — an ultra-long-lived sequencer would accumulate unboundedly. Now compares in ulong space (`blocksInBatch >= (ulong)_settings.MaxBlocksPerBatch`); iter-191's ctor validation ensures `MaxBlocksPerBatch > 0` so the cast direction is safe. No pinning test (constructing a 2.1B-block batch in a unit test isn't practical; the fix is a 1-character symmetric-cast change).

Cumulative: 491 tests / 27 projects.

### Fixed — `DeployPlanner.ResolveToken` validates `HashResolver` delegate return

- A buggy `HashResolver` delegate returning null `UInt160` would NRE on `.ToString()` in `ResolveToken`. Now surfaces as `InvalidOperationException` naming the step. Same iter-171/172 callee-contract pattern. 1 pinning test.

Cumulative: 491 tests / 27 projects.

### Fixed — `PrometheusExporter.Format` null-guards on snapshot dictionary fields

- `MetricsSnapshot.Counters`/`Gauges`/`Histograms` are `required` but `init` setters accept null. A buggy `IMetricsSource` building a malformed snapshot would NRE deep inside `WriteFamilies` / `GroupHistograms` foreach. Iter-186's `MetricsRequestHandler` wrap converts the HTTP path's failure to a 500, but direct API callers see only the obscure NRE. Now `Format` itself null-guards each field. 1 pinning test (iter 200 milestone).

Cumulative: 490 tests / 27 projects.

### Fixed — `InMemoryL2RpcStore` ctor: L1-sentinel chainId + SecurityLevel range

- `InMemoryL2RpcStore` ctor accepted `chainId = 0` (the L1 sentinel) silently — every subsequent RPC `AssertOurChain` would later fail with a misleading "differs from local 0" comparison. Now uses `ChainIdValidator.ValidateL2`. Same path also accepted `(SecurityLevel)99` silently — would propagate as `levelName = "99"` in RPC responses. Now range-checked. 2 pinning tests.

Cumulative: 489 tests / 27 projects.

### Fixed — RISC-V prover/verifier ctors null-guard `verificationKeyId`

- `Sp1RiscVProver`, `Sp1RiscVVerifier`, `MockRiscVProver`, `MockRiscVVerifier` ctors all accepted a null `UInt256 verificationKeyId` silently. The null would later surface as a iter-159 `RiscVProofPayload.Encode` "VerificationKeyId is null" — naming the payload field but not the actual producer (the prover/verifier ctor that took the bad value). Surface at the source. No pinning tests (4 trivial ctor guards; existing tests still pass with valid VKs).

Cumulative: 487 tests / 27 projects.

### Fixed — `BisectionGame` ctor per-entry null-guards (defense-in-depth for direct callers)

- `ChallengeOrchestrator.InspectWithBisectionAsync` (iter 196) per-entry-null-checks before invoking, but `BisectionGame` is a public constructor that other callers can hit directly. Without this guard, a null entry would NRE inside the `[0].Equals(...)` / `[^1].Equals(...)` checks or `RunRound`'s mid-comparison. 1 pinning test (had to fix the test on first run — `BuildScenario(N, ...)` returns `N+1` arrays, not `N`; off-by-one caught by length-mismatch firing first).

Cumulative: 487 tests / 27 projects.

### Fixed — `ChallengeOrchestrator.InspectWithBisectionAsync` null-guards (parity with `InspectAsync`)

- `InspectAsync` got iter-171 null-guards on `claimedCommitment.PreStateRoot`/`PostStateRoot`/`inputs.PreStateRoot` (UInt256 reference type, `required` doesn't prevent null). `InspectWithBisectionAsync` was missed at that time. Brought to parity. Additionally added per-entry null guards on `challengerCheckpoints[i]` / `sequencerCheckpoints[i]` so a null entry surfaces with the bad index instead of NREing inside `[^1].Equals(...)` or BisectionGame's loop. 1 pinning test.

Cumulative: 486 tests / 27 projects.

### Fixed — `BinaryTreeAggregator.Aggregate` validates `IRoundProver.Combine` return

- A buggy `IRoundProver.Combine` returning null would propagate to the next round's `current[i*2]` as null, causing a confusing NRE deep inside `Combine` on the next round. Now caught at the source with round/slot index. Same iter-171/172/173 callee-contract pattern. 1 pinning test using a `NullReturningRoundProver` test double.

Cumulative: 485 tests / 27 projects.

### Fixed — `MessageTree.GetMessage` / `WithdrawalTree.GetWithdrawal` clearer out-of-range errors

- Both methods previously delegated index-validation to `List<T>`'s indexer, which throws a generic `ArgumentOutOfRangeException` with `"Index was out of range"`. Now both surface a clearer `"index N not in [0, count)"` so an operator chasing a stale or out-of-band index sees the actual valid range. 2 pinning tests covering negative-index and beyond-count cases.

Cumulative: 484 tests / 27 projects.

### Fixed — `ReferenceBatchExecutor.ApplyBatchAsync` per-entry null-guards

- Per-entry null-guards added at three foreach sites: `request.L1MessagesConsumed[i]` (would propagate as a generic NRE inside `_l1Processor.ApplyAsync`), `result.Withdrawals[i]` and `result.Messages[i]` (would surface as `WithdrawalTree.Add` / `L2Outbox.Add` "X is null" without naming the misbehaving executor or the bad index). Now surfaces at the source with the index AND the executor's tx hash. Same iter-181 `BatchSealer.OnBlockCommit` per-entry pattern. 1 pinning test for the L1-message case.

Cumulative: 482 tests / 27 projects.

### Fixed — `L2SettlementPlugin.Wire` revalidates `_settings.ProofType`

- `L2SettlementSettings.From` validates `ProofType` byte range at config-parse time, but `init` setters bypass that path. Same iter-191 ctor-symmetry pattern: revalidate in `Wire` (the latest fail-fast point before the first `OnBatchSealed → SubmitNextAsync`). Without this, an invalid byte would surface only deep inside the broad-catch as a generic `AttestationProver expects ProofType.Multisig, got (ProofType)X` ArgumentException tagged as `("exception", "ArgumentException")`. Now: clear `InvalidDataException` at Wire time. No pinning test (Plugin's `Configure` is protected and `_settings` private — the validation is reachable only through direct internal construction; existing tests still pass with the default `ProofType=1`).

Cumulative: 481 tests / 27 projects.

### Fixed — `BatchSealer` ctor revalidates `L2BatchSettings` positivity

- `L2BatchSettings.From` validates `Max*` positivity at config-parse time, but `init` setters allow direct construction (tests / programmatic wiring) to bypass that path. A caller writing `new L2BatchSettings { MaxBlocksPerBatch = 0 }` would slip past the parser, then `BatchSealer.ShouldSeal` returns true on every block — degenerate per-block batches surface only as a runaway L1 submission rate hours later. Now `BatchSealer` ctor calls `L2BatchSettings.ValidatePositive` for all three `Max*` fields. Same pattern as iter-190's sequencer ctor symmetry. 1 pinning test covers the three Max* misconfigs + default-settings boundary.

Cumulative: 481 tests / 27 projects.

### Fixed — `InMemorySequencerCommitteeProvider` ctor validates `maxCommitteeSize`

- `SetMaxCommitteeSize` validates the range `[1..64]`; the constructor did not. Operator-supplied `0` silently accepted every `Register` call as "committee full", `-1` same, and `> 64` exceeded the dBFT 2.0 practical bound. Now symmetric: surface the misconfig at construction time, not at first `Register`. 1 pinning test covers `0` / `-1` / `65` reject and `1` / `64` boundary accept.

Cumulative: 480 tests / 27 projects.

### Fixed — `RpcSettlementClient.SubmitBatchAsync` validates `SignAndSendAsync` return

- A buggy `SignAndSendAsync` delegate returning null `UInt256` would propagate as a NRE further downstream — typically an L1-tracker dereferencing the tx hash. Same iter-171/172/173 callee-contract pattern: surface the bad return as `InvalidOperationException` naming the delegate. Made `SubmitBatchAsync` async to enable awaiting and validating the result. 1 pinning test using a delegate that returns `(UInt256)null!`.

Cumulative: 479 tests / 27 projects.

### Fixed — `NeoFsLikeDAWriter.TryGet` defensive copy

- Same iter-176 defensive-copy pattern as `KeyedStateStore.EnumerateSorted`. Previously returned the raw stored `byte[]` wrapped in `ReadOnlyMemory<byte>?`; a debug consumer that mutated the returned bytes would silently corrupt the store. Now returns a per-call `Clone()`. 1 pinning test mutates the first read and asserts the second read sees the original bytes.

Cumulative: 478 tests / 27 projects.

### Fixed — `BatchSerializer.Encode` rejects out-of-range `ProofType` (Encode/Decode symmetry)

- `Decode` rejects `ProofType` bytes outside `[0..Zk]` (iter 103). `Encode` did not — an out-of-range cast (e.g. `(ProofType)99`) produced bytes the round-trip `Decode` would refuse, masking the producer-side bug at the consumer. Same iter-159 Encode/Decode-symmetry pattern as `OptimisticProofPayload.MaxSignatureBytes` and `RiscVProofPayload.MaxProofBytes`. 1 pinning test asserts the throw + index in message.

Cumulative: 477 tests / 27 projects.

### Fixed — `MetricsRequestHandler.HandleMetrics` returns 500 on snapshot/format failure

- Previously a buggy `IMetricsSource` that returned null or threw from `Snapshot()` (or a downstream `PrometheusExporter.Format` regression) would surface as a closed connection to the scraper — no diagnostic, no alertable HTTP status. Wrapped the snapshot/format pipeline in `try/catch` so the failure becomes a `500` with a generic body, which flips most Prometheus servers into an "exporter down" alert state. Generic-on-purpose body; operators chase the actual exception in logs. 2 pinning tests using `NullSnapshotSource` and `ThrowingSnapshotSource`.

Cumulative: 476 tests / 27 projects.

### Fixed — `MessageTree` + DA writers null-key sweep

- Closed the iter-148/183/184 Dictionary-key null-guard pattern in 4 more entry points: `MessageTree.Add` (now guards `message.MessageHash`; without this `_byHash[null]` throws with generic message and `_leaves` accumulates a null that iter-179's `ComputeRoot` would catch only later), `MessageTree.TryGetIndex` (UInt256 key), `InMemoryDAWriter.IsAvailableAsync` (`receipt.Commitment`), and `NeoFsLikeDAWriter.IsAvailableAsync` + `TryGet`. 2 pinning tests for the `MessageTree` cases.

Cumulative: 474 tests / 27 projects.

### Fixed — `InMemoryL2RpcStore` null-key sweep (6 entry points)

- Closed the iter-148/183 Dictionary-key null-guard pattern in 6 more entry points: `RecordWithdrawalProof`/`RecordMessageProof` (setters add explicit guards on `leafHash`/`messageHash` keys; the byte-payload guards already existed), and `GetWithdrawalProof`/`GetMessageProof`/`GetCanonicalAsset`/`GetBridgedAsset` (getters now null-guard their `UInt256`/`UInt160` keys instead of relying on `Dictionary<,>.TryGetValue(null)`'s generic message). 1 pinning test asserts all 6 entry points reject null with a clear `ArgumentNullException`.

Cumulative: 472 tests / 27 projects.

### Fixed — `AssetRegistry` lookup/setter null-guard sweep

- `TryGetByL1`, `TryGetByL2`, and `SetActive` previously delegated null-key handling to `Dictionary<UInt160, T>` whose `TryGetValue(null)` throws `ArgumentNullException` with a generic `"key"` message. Surface the bad arg at the API boundary so the operator sees which parameter is wrong. Same iter-148 pattern as `Register`. 3 pinning tests.

Cumulative: 471 tests / 27 projects.

### Fixed — `JsonRpcClient.CallAsync` rejects mismatched response id

- JSON-RPC 2.0 §5 mandates the response's `id` field match the request's. A buggy server, misconfigured proxy, or confused upstream that interleaves streams could silently send a response correlating to a different request — previously accepted as if it answered ours. Although we rely on HTTP one-request-per-connection correlation here (so a mismatch can't actually misroute a response on the happy path), the missing check masked server bugs that an operator would otherwise want to catch. Now throws `JsonRpcException(-32603, "response id N does not match request id M")`. 1 pinning test using a `StubHandler` that returns `id=999` for a request sent with `id=1`.

Cumulative: 468 tests / 27 projects.

### Fixed — `BatchSealer.OnBlockCommit` silently treated null tx as empty (`byte[]→ReadOnlyMemory<byte>`)

- The implicit `byte[]` → `ReadOnlyMemory<byte>` conversion in C# accepts null and produces an empty `ReadOnlyMemory` rather than throwing. So a null entry in the `IEnumerable<byte[]> rawTransactions` argument was silently folded into the batch's tx tree as an empty leaf — a deterministic-replay nightmare because the commitment wouldn't match what re-execution produces, and a sequencer's batch could quietly diverge from any honest replay. Now caught at the foreach with the bad index named. 1 pinning test.

Cumulative: 467 tests / 27 projects.

### Fixed — `PassThroughRoundProver.Combine` null-guard on `MessageRootContribution`

- The hashing primitive at `Combine` dereferenced `left.MessageRootContribution.GetSpan()` and `right.MessageRootContribution.GetSpan()` without null-checking the `UInt256` references. Same iter-156 hashing-primitive defense pattern. 1 pinning test that asserts both `left`-bad and `right`-bad cases throw.

Cumulative: 466 tests / 27 projects.

### Fixed — `MerkleTree` constructor + `ComputeRoot` null-leaf-entry guard

- Both the constructor and the static `ComputeRoot` accepted `IReadOnlyList<UInt256>` but didn't per-entry null-check. A null leaf would NRE deep in `CombineHash`'s `GetSpan()` (or, for `ComputeRoot` with a single null leaf, return null `UInt256` to the caller). Added the same iter-158/168 per-entry index-naming guard as `MerkleProofSerializer.Encode` and `MerkleTree.Verify`. 2 pinning tests.

Cumulative: 465 tests / 27 projects.

### Fixed — `L2BridgePlugin.DepositProcessor`/`WithdrawalProcessor` accessor null-handling

- The two accessors used the `!` null-forgiving operator (`_depositProcessor!`), so a caller who accessed them before `Configure()` had run got the underlying `null` and NRE'd on the next field access. Replaced with `?? throw new InvalidOperationException("... accessed before Configure() — wire the L2BridgePlugin into the host first")` so the cause is named at the source. No pinning test (would require adding a new `Neo.Plugins.L2Bridge.UnitTests` project; the change is small and the throw message is self-explanatory).

Cumulative: 463 tests / 27 projects.

### Fixed — `L2SettlementPlugin`: fail-fast on empty `proofResult.Proof`

- A prover that returned empty `Proof` bytes paired with a non-None `ProofType` would otherwise produce a soft-sealed commitment that `NoZeroProofCheck` catches hours later at audit time, with no link back to the prover bug. Now caught at the prove boundary in `SubmitNextAsync` — same iter-128/140-style "fail close to the contract" pattern as the Kind/PublicInputHash mismatch checks. The exception flows through the iter-175 catch tagging so dashboards see `SubmitFailures{exception=InvalidOperationException}`. 1 pinning test with `EmptyProofProver` test double.

Cumulative: 463 tests / 27 projects.

### Fixed — `KeyedStateStore.EnumerateSorted` defensive copy

- The "test/debug helper" `EnumerateSorted` yielded the raw `byte[]` references stored in the `SortedDictionary`, so a debug consumer that mutated the yielded keys/values would silently corrupt the store's internal state. `Put` already copies (iter-167 pattern) and `Get` returns immutable `ReadOnlyMemory`, but this iterator was the lone hole. Now each yielded entry is a fresh `Clone()` — caller mutations are isolated. Pinning test mutates the yielded buffers and asserts the stored entry is unchanged.
- Spent earlier in this iteration trying to broaden the iter-175 exception-tagging pattern across 5 more failure-counter sites (`WithdrawalsRejected`, `DepositsRejected`, `RpcFailures`, `DAPublishFailures`, `BatchSealedSubscriberFailures`) — broke 12 tests that asserted untagged metrics. Reverted; the per-site choice was deliberate, broadening it requires a coordinated test update done in a focused refactor PR rather than a defensive-sweep iter.

Cumulative: 462 tests / 27 projects.

### Changed — `L2SettlementPlugin`: prover-contract assertion + exception-tagged failure metric

- `L2SettlementPlugin.SubmitNextAsync` now asserts the prover's contract: `IL2Prover.ProveAsync` returning null surfaces as `InvalidOperationException`, and `proofResult.PublicInputHash` is null-guarded (was previously dereferenced on the next `.Equals(hash)`). Same iter-171/172/173/174 callee-contract pattern.
- The broad `catch (Exception)` block now tags `MetricNames.SubmitFailures` with `("exception", typeName)` so an operator's dashboard can separate contract violations (`InvalidOperationException`) from network failures (`HttpRequestException`) from L1-side rejections. Previously the counter was a single number that hid the failure mode.
- 1 new pinning test (`Submit_BuggyProverReturnsNull_TaggedAsContractViolation`) + 2 existing tests updated to assert the new tag.

Cumulative: 461 tests / 27 projects.

### Fixed — `AttestationProver` callee-contract assertion on `ISignerSet.SignAsync`

- A buggy `ISignerSet` returning null from `SignAsync` would propagate as a NRE inside `MultisigProofPayload.Encode`'s iter-159 null-guard (which would name `Signatures` rather than the actual root cause). Now surfaced at the prover boundary as `InvalidOperationException` naming `SignAsync`. 1 pinning test with `NullReturningSigners` test double.

Cumulative: 460 tests / 27 projects.

### Fixed — Two more callee-contract assertions: `MetricsEmittingDAWriter` + `ReferenceBatchExecutor`

- Continued the iter-171/172 sweep. (1) `MetricsEmittingDAWriter.PublishAsync` now asserts the inner writer's contract: a null `DAReceipt` return surfaces as `InvalidOperationException` naming `PublishAsync` (with the mode tag) and bumps the failure metric. Previously a downstream consumer would NRE on `receipt.Commitment`. (2) `ReferenceBatchExecutor.ApplyBatchAsync` now asserts `ITransactionExecutor.ExecuteAsync` returns non-null AND that `result.Receipt`/`result.TxHash` are non-null (would NRE inside `result.Receipt.Hash()` or `MerkleTree.ComputeRoot(txHashes)` containing nulls).
- 2 pinning tests: `Publish_BuggyInnerReturnsNull_SurfacesContractViolation` (asserts the failure metric still ticks but the success metric does not) and `ApplyBatchAsync_BuggyTxExecutorReturnsNull_SurfacesContractViolation`.

Cumulative: 459 tests / 27 projects.

### Fixed — Callee-returns-null contract assertions in `CensorshipDetector` + `ProofValidityCheck`

- Extended the iter-171 callee-contract pattern. (1) `CensorshipDetector.DetectOverdueAsync` now asserts the buggy-source contract: a null return from `IForcedInclusionSource.DrainAsync` or `ISequencerCommitteeProvider.GetActiveCommitteeAsync` surfaces as `InvalidOperationException` naming the contract method, instead of NREing inside the foreach / `.Count` access. (2) `ProofValidityCheck.RunAsync` now asserts `_publicInputsResolver(batch) != null` (was previously dereferenced on the next line); the existing `ProofVerificationResult` is `record struct` so it can't be null and doesn't need a guard.
- 2 pinning tests with `BuggySource` and `BuggyCommittee` test doubles in `UT_CensorshipDetector.cs`.

Cumulative: 457 tests / 27 projects.

### Fixed — `ChallengeOrchestrator.InspectAsync` null-guards + replayer-contract assertion

- Three new defensive guards in `ChallengeOrchestrator.InspectAsync`. (1) `claimedCommitment.PreStateRoot`/`PostStateRoot` and `inputs.PreStateRoot` are `UInt256` reference types — `required` doesn't force non-null. The chain-id/batch-number/pre-state validation that follows would NRE on `.Equals(...)`. Same iter-156 hashing-primitive defense pattern. (2) After `_replayer.ReplayAsync(...)`, the `replayedRoot` is now checked for null — a buggy `IFraudProofGenerator` returning null would otherwise NRE inside `replayedRoot.Equals(...)` with no link to the replayer's contract violation; surfaced as `InvalidOperationException` naming `ReplayAsync`.
- Two pinning tests in `UT_Challenge.cs`: `Inspect_RejectsBuggyReplayerReturningNull` (asserts `ReplayAsync` appears in the message) and `Inspect_RejectsNullPreStateRootInCommitment`.

Cumulative: 455 tests / 27 projects.

### Fixed — `L2BatchPlugin.OnBatchSealed` subscriber-failure isolation

- A throwing `OnBatchSealed` subscriber would propagate its exception back to Neo's `Blockchain.Committed` via standard .NET event dispatch (first-throw aborts further dispatch + exception rethrows to event source), making a buggy downstream listener (e.g. `L2SettlementPlugin`) potentially destabilize block import. Refactored to iterate `GetInvocationList()` and try/catch each subscriber individually; failures bump the new `MetricNames.BatchSealedSubscriberFailures` counter (+catalog entry).
- Extracted the dispatch into an `internal static DispatchSealed` so it can be unit-tested without spinning up a `NeoSystem`. Added `InternalsVisibleTo` for the test project. New `UT_L2BatchPlugin.cs` with 3 pinning tests: one-throws-others-still-fire, no-subscribers, and multi-throw-counter.

Cumulative: 453 tests / 27 projects.

### Fixed — Two more null-guard surfaces: `DerivedPostStateRootOracle` + `ChainAuditor` per-batch

- `DerivedPostStateRootOracle.ResolveAsync` now null-guards `preStateRoot`, `receiptRoot`, and `blockContext` at the API boundary. Without these, a null `UInt256` would NRE inside `GetSpan()` with no link to the caller.
- `ChainAuditor.AuditAsync` now per-entry null-checks `batches[i]` BEFORE any field access (the chainId/sort scan touches `.ChainId`/`.BatchNumber`). Without this, a null entry would surface as a confusing NRE deep inside an audit check (e.g. `ContinuityCheck`'s `cur.PreStateRoot.Equals(...)`); the audit-level message names the bad index so the operator sees which batch is missing.
- 4 pinning tests: 3 for the oracle's reject-null-input cases, 1 for the auditor's null-batch-entry case.

Cumulative: 450 tests / 27 projects.

### Fixed — `MerkleTree.Verify` null-guards on proof.Leaf and Siblings entries

- `MerkleTree.Verify` only null-checked the proof object itself; `proof.Leaf` (UInt256, reference type) and individual `proof.Siblings[d]` entries could still be null and would NRE inside `CombineHash`'s `GetSpan()` with no link to the bad caller. Same iter-158 pattern from `MerkleProofSerializer.Encode`. Now: top-level `ArgumentNullException.ThrowIfNull` on `proof.Leaf` and `proof.Siblings`, and the per-sibling check fires inside the loop with `Siblings[i]` index in the message.
- 2 pinning tests in `UT_MerkleTree.cs`: `Verify_RejectsNullLeaf` and `Verify_RejectsNullSiblingEntry` (asserts the bad index appears in the exception).

Cumulative: 446 tests / 27 projects.

### Fixed — `InMemoryMessageRouter`: null-hash NRE + caller-mutation corruption

- `RecordFinalized(messageHash, proofBytes)` and `GetMessageProofAsync(messageHash, ...)` didn't null-guard `messageHash`. UInt256 is a reference type — null would NRE inside `ConcurrentDictionary`'s hash lookup with no link to the bad caller.
- `RecordFinalized` didn't make a defensive copy of `proofBytes`. `ReadOnlyMemory<byte>` provides immutability for the *view*, but the underlying array can still be mutated through other references. A caller who reused a scratch buffer or mutated their array after passing it in would silently corrupt the stored proof. Now does `proofBytes.ToArray()`, mirroring `InMemoryL2RpcStore.RecordWithdrawalProof`.
- 3 pinning tests in `UT_Messaging.cs`: 2 null-guard tests + `Router_RecordFinalized_DefensiveCopyProtectsAgainstCallerMutation` that mutates the source bytes after calling and asserts the stored proof is unchanged.

Cumulative: 444 tests / 27 projects.

### Fixed — `BatchBuilder.ToCommitment` null-guards before sealing

- `ToCommitment` would NRE on the first `executionResult.PostStateRoot` access if `executionResult` was null. Worse, `daCommitment` and `publicInputHash` were `UInt256` (reference type) — null in either would slip through here, get assembled into the commitment, and be caught only later in `BatchSerializer.Encode`'s iter-156/157 null-guards — but by then `_batch.Seal()` had already mutated state irreversibly, so the operator couldn't simply retry. Now all three are guarded BEFORE `_batch.Seal()` runs.
- 3 pinning tests in `UT_BatchBuilder.cs` assert the throw AND that `b.Batch.IsSealed` is false after the failed call (proves the guard fires before sealing so a retry can succeed).

Cumulative: 441 tests / 27 projects.

### Added — Pin iter-164 worst-case + sweep last 2 metric sites

- New regression test `Submit_ThrowingMetrics_DoesNotReQueueAlreadySubmittedBatch` in `UT_L2SettlementPlugin_Metrics.cs` uses a `ThrowingMetrics` test double to assert that a metrics-sink failure after `SubmitBatchAsync` returns does NOT re-queue the batch (which would loop indefinitely against L1's duplicate-rejection). Verifies `client.SubmitCount == 1` and `settlement.PendingCount == 0`.
- Swept the final two metric call sites: `L2RpcMethods.Time` (would surface a successful RPC body as an `RpcFailures` to the caller, prompting a retry) and `CensorshipDetector` (less severe — pure compute path, but consistent with the rest). Metric-sink-isolation sweep is now complete across the L2 stack.

Cumulative: 438 tests / 27 projects.

### Fixed — Metric-induced re-submission of already-on-L1 batches + sweep across 5 more sites

- Worst-case bug fixed: in `L2SettlementPlugin.SubmitNextAsync`, a metrics-sink throw between the success of `SubmitBatchAsync` (line 175) and either of the post-submit metric calls (177/178) was caught by the broad `catch (Exception)` block, which re-queues the batch. The L1 contract would reject the duplicate commitment, the plugin would treat the rejection as another submit failure, and the batch would loop indefinitely — paying L1 gas every retry.
- Same pattern swept across 4 more sites: `BatchSealer.OnBlockCommit` (would leave `_builder` non-null pointing at the just-sealed builder so the next call adds blocks to a sealed batch), `MetricsEmittingDAWriter.PublishAsync` (would re-publish an already-on-DA blob), `BinaryTreeAggregator`, `ChainAuditor`, `ChallengeOrchestrator`. All converted to `SafeIncrementCounter`/`SafeRecordHistogram`/`SafeSetGauge`.

Cumulative: 437 tests / 27 projects.

### Added — `MetricsExtensions.SafeIncrementCounter`/`SafeSetGauge`/`SafeRecordHistogram` + sweep

- New `Neo.L2.Telemetry.MetricsExtensions` (3 helpers) wraps `IL2Metrics` calls in `try/catch` swallows so a defective sink can never affect business logic. Refactored 4 state-mutating sites to use it: `InMemorySequencerCommitteeProvider.Register`/`BeginExit`/`Finalize`, `InMemoryForcedInclusionSource.Enqueue`, `L2Outbox.Add`. Each was previously vulnerable to the iter-162 defect: state committed under the lock, then a metric throw outside the lock would surface as a caller-visible "operation failed" while the state had already mutated. Added `WithdrawalProcessor_Stage_SurvivesThrowingMetricsSink` pinning test using a `ThrowingMetrics` test double that throws on every call.

Cumulative: 437 tests / 27 projects.

### Fixed — Metrics-sink exception corrupts `WithdrawalProcessor`/`DepositProcessor` state

- Both processors had the same defect: a defective `IL2Metrics` implementation that throws would leave business state committed (`_byNonce` / `_tree` / `_consumed`) while the caller saw an exception and assumed the operation failed. Worse, the broad `catch { _metrics.IncrementCounter(*Rejected); throw; }` block would then fire too — double-counting. The interface contract doesn't promise `IL2Metrics.IncrementCounter` is non-throwing (any HTTP-pushing implementation absolutely could throw), so business code can't trust it. Fix: success counter is now outside the lock and outside the try block, both metric calls are individually try/catch-swallowed, and the rejection counter no longer competes with the success path.

Cumulative: 436 tests / 27 projects.

### Added — Pinning tests for `OptimisticProofPayload.Encode`/`RiscVProofPayload.Encode` size caps

- Two new tests in `UT_OptimisticAndRiscV.cs` lock down the iter-159 Encode-side cap rejection: `OptimisticProofPayload_Encode_RejectsOversizedSig` (`MaxSignatureBytes + 1`) and `RiscVProofPayload_Encode_RejectsOversizedProof` (`MaxProofBytes + 1`). Both assert the exception message names the cap constant so a future refactor that drops the validation is caught here, not at the next consumer.

Cumulative: 436 tests / 27 projects.

### Added — Pinning tests for `MultisigProofPayload.Encode` validation

- Three new tests in `UT_Attestation.cs` lock down the iter-159 Encode-side checks: `Encode_RejectsBadSignatureLength` (boundary 63 / 65 vs. the silent-zero-pad at length < 64), `Encode_RejectsNullSignerEntry` (asserts the null index appears in the exception message), `Encode_RejectsOversizedSignerCount` (Encode/Decode symmetry pin). Without these, future refactors could regress the silent-padding bug undetected.

Cumulative: 434 tests / 27 projects.

### Changed — Proof/deposit payload encoders: null-guards + Encode/Decode-symmetry checks

- Swept the four remaining payload encoders. Added null-guards on the reference-type `UInt160`/`UInt256` fields (`OptimisticProofPayload`, `RiscVProofPayload`, `MultisigProofPayload`, `DepositPayload`). Added Encode-side cap checks where the matching `Decode` already rejects oversized inputs (`MaxSignatureBytes`, `MaxProofBytes`, `MaxSigners`) — without these you could `Encode` bytes the round-trip `Decode` would refuse, masking the producer-side bug at the next consumer. `MultisigProofPayload.Encode` additionally validates each signer's `Signature.Length == 64` (a shorter source silently zero-pads via `Span.CopyTo`, producing a structurally-valid but semantically-wrong encoding).
- This **does** finish the encoder sweep — all eight `Encode` methods (3 in `BatchSerializer`/`StateRootCalculator`, 1 in `MessageHasher`, 1 in `Receipt`, 1 in `MerkleProofSerializer`, 1 in `FraudProofPayload`, 4 in payload encoders) now uniformly null-guard their reference-type fields.

Cumulative: 431 tests / 27 projects.

### Changed — `FraudProofPayload.Encode` and `MerkleProofSerializer.Encode` defense-in-depth null-guards

- Extended the iter-154…157 null-guard pattern to the remaining encoders. `FraudProofPayload.Encode` now guards its 3 `UInt256` roots; `MerkleProofSerializer.Encode` guards `Leaf`, the `Siblings` collection, and each sibling entry inside the loop (`Siblings[i]` could be a null reference even with the collection itself non-null). The previous claim that iter-157 "finished" the sweep was premature — these were missed.

Cumulative: 431 tests / 27 projects.

### Changed — `BatchSerializer.Encode`/`EncodePublicInputs` defense-in-depth null-guards

- Continued the iter-154/155/156 hashing-primitive null-guard pattern through the wire serializer. `BatchSerializer.Encode` now null-checks all 9 commitment `UInt256` fields before reaching `WriteUInt256`'s `GetSpan()`. `EncodePublicInputs` mirrors `StateRootCalculator.HashPublicInputs` with all 10 root fields. This finishes the cryptographic-primitive null-guard sweep across the codebase.

Cumulative: 431 tests / 27 projects.

### Changed — `StateRootCalculator` defense-in-depth null-guards

- Continued the iter-154/155 hashing-primitive null-guard pattern through `StateRootCalculator.HashBlockContext` (one `UInt256`) and `StateRootCalculator.HashPublicInputs` (ten `UInt256` fields). Each null-checked before reaching the `WriteRoot` / `GetSpan()` boundary.

Cumulative: 431 tests / 27 projects.

### Changed — `Receipt.Hash` defense-in-depth null-guards

- Same pattern as iter 154's `MessageHasher` fix, applied to `Receipt.Hash()`. The three `UInt256` fields (`TxHash`, `StorageDeltaHash`, `EventsHash`) are reference types; `required` only forces "must be set," not "non-null." A null field would crash inside `GetSpan()` with no link back to the bad caller. Now `ArgumentNullException.ThrowIfNull` each before hitting the buffer copy.

Cumulative: 431 tests / 27 projects.

### Changed — `MessageHasher` defense-in-depth null-guards

- The iter-146/147 fixes added null-guards at the API boundaries (`MessageBuilder.Build`, `WithdrawalProcessor.Stage`). Added the same guards at the cryptographic-primitive boundary too — `MessageHasher.HashMessage` / `HashWithdrawal` — covers any direct caller (tests, future helpers) that bypasses the higher-level boundaries. Defense in depth.
- Both methods now `ArgumentNullException.ThrowIfNull` each `UInt160` field they read (Sender/Receiver for HashMessage; EmittingContract/L2Sender/L1Recipient/L2Asset for HashWithdrawal) before hitting `GetSpan()`.

Cumulative: 431 tests / 27 projects.

### Tests — Regression coverage for iter-148 `Register` null-guards

- Pinned the iter-148 null-guards on `AssetRegistry.Register` and `InMemorySequencerCommitteeProvider.Register` with regression tests so a future refactor can't silently drop them.
- **3 new tests**:
  - `AssetRegistry_Register_RejectsNullL1Asset`
  - `AssetRegistry_Register_RejectsNullL2Asset`
  - `Sequencer.Register_RejectsNullL1Address`

Cumulative: 431 tests / 27 projects.

### Tests — Regression coverage for iter-147 `WithdrawalProcessor.Stage` null-guards

- The iter-147 fix added `ArgumentNullException.ThrowIfNull` for the four `UInt160` fields on `WithdrawalRequest`. Pinned with regression tests so a future refactor can't silently drop them.
- **2 new tests**: `WithdrawalProcessor_Stage_RejectsNullL2Sender` and `WithdrawalProcessor_Stage_RejectsNullL1Recipient` — both pass `null!` for the respective field and assert `ArgumentNullException`.

Cumulative: 428 tests / 27 projects.

### Tests — Regression coverage for iter-146 `MessageBuilder.Build` null-guards

- The iter-146 fix added `ArgumentNullException.ThrowIfNull` to `sender` / `receiver`. This iter pins the contract with two regression tests so a future "trust the caller" refactor can't silently drop the guards.
- **2 new tests**: `MessageBuilder_RejectsNullSender` and `MessageBuilder_RejectsNullReceiver` — both pass `null!` for the respective field and assert `ArgumentNullException`.

Cumulative: 426 tests / 27 projects.

### Changed — `BatchBuilder` reject-null guards on all reference-type append methods

- Continuation of the iter-146–149 null-guard sweep, applied to the batch-builder API surface.
- `ConsumeL1Message(CrossChainMessage)`, `AddWithdrawal(WithdrawalRequest)`, `AddL2ToL1Message(CrossChainMessage)`, `AddL2ToL2Message(CrossChainMessage)`: all four now `ArgumentNullException.ThrowIfNull` their reference-type input. Previously a null arg silently added a null entry to the underlying `L2Batch._{l1Messages,withdrawals,l2ToL1,l2ToL2}` list, which would then crash hours later inside the per-batch hashing pass with no link back to the bad caller.

Cumulative: 424 tests / 27 projects.

### Changed — `InMemoryL2RpcStore.AddBatch` + `RegisterAsset` reject null inputs

- Continuation of the iter-146/147/148 null-guard sweep.
- `AddBatch(L2BatchCommitment commitment, ...)`: a null commitment crashes deep in `commitment.BatchNumber` access.
- `RegisterAsset(UInt160 l1Asset, UInt160 l2Asset)`: a null UInt160 throws from `ConcurrentDictionary.TryGetValue(null)` deep in the hash path.
- Both call sites now `ArgumentNullException.ThrowIfNull` up front.
- (Note: `RecordDeposit(DepositStatus)` doesn't need a guard — `DepositStatus` is a `record struct` (value type), so null is impossible at the type level.)

Cumulative: 424 tests / 27 projects.

### Changed — `AssetRegistry.Register` + `Sequencer.Register` reject null `UInt160` fields

- Continuation of the iter-146/147 null-guard sweep.
- `AssetRegistry.Register`: a null `mapping.L1Asset` creates the tuple key `(null, chainId)` (Dictionary tolerates null inside a tuple) — lookups would interpret it oddly. A null `mapping.L2Asset` throws deep in `_byL2[null]`.
- `InMemorySequencerCommitteeProvider.Register`: a null `l1Address` propagates into `CommitteeMember.L1Address` → `CensorshipReport.ResponsibleSequencerAddress` → either misroutes the slash payout or hard-reverts the slash transaction at L1.
- Both call sites now `ArgumentNullException.ThrowIfNull` the relevant fields up front.

Cumulative: 424 tests / 27 projects.

### Changed — `WithdrawalProcessor.Stage` rejects null `UInt160` fields

- Same defensive shape as the iter-146 `MessageBuilder.Build` fix, applied to the withdrawal-staging boundary. `WithdrawalRequest`'s `EmittingContract` / `L2Sender` / `L1Recipient` / `L2Asset` are `required UInt160` (reference type) — the `required` keyword forces them to be set but doesn't prevent null. A null field would crash deep in `MessageHasher.HashWithdrawal`'s `GetSpan()` with no link back to the bad field.
- Catch all four at the API boundary with `ArgumentNullException.ThrowIfNull`.

Cumulative: 424 tests / 27 projects.

### Changed — `MessageBuilder.Build` rejects null `sender` / `receiver`

- `UInt160` is a reference type in Neo's library, so a null `sender` or `receiver` slipped past the C# nullable analysis (the `required` keyword on `CrossChainMessage` just means "must be set," not "non-null"). The null then crashed inside `MessageHasher.HashMessage`'s `GetSpan()` with a `NullReferenceException` and no link back to the bad argument.
- Added `ArgumentNullException.ThrowIfNull(sender)` + same for `receiver` at the API boundary so the operator sees which arg was wrong.

Cumulative: 424 tests / 27 projects.

### Changed — `L2BatchPlugin.Configure` no longer resets `_sealer` (preserves batch-numbering state)

- Companion to iter-144's bridge fix. `Configure` explicitly set `_sealer = null` after parsing settings — so a re-fired Configure (config-watcher, host re-init) would discard `BatchSealer._nextBatchNumber` and the in-progress `_builder`. Next `OnBlockCommitted` would lazy-create a fresh sealer starting from BatchNumber=1, potentially producing duplicate batch numbers if any batches were already submitted.
- Removed the explicit reset. The existing `_sealer ??= new BatchSealer(...)` lazy-init handles first-time creation; subsequent Configures only refresh the `_settings` field (which the existing sealer ignores — settings are captured at construction). Mid-flight reconfiguration of batch thresholds isn't supported anyway.

Cumulative: 424 tests / 27 projects.

### Changed — `L2BridgePlugin.Configure` lazily inits processors to preserve cross-batch state

- `Configure` unconditionally recreated `DepositProcessor` + `WithdrawalProcessor`. If `Configure` ever ran twice (config-watcher re-fire, host re-init), the new processors started fresh — discarding `DepositProcessor._consumed` and `WithdrawalProcessor._consumedAcrossBatches` (iter 133), allowing already-processed deposits and closed-batch withdrawals to be replayed on L2 with the duplicate only catching hours later at L1 settlement.
- Switched to `_depositProcessor ??= new …` / `_withdrawalProcessor ??= new …` lazy init. Subsequent Configure calls are now no-ops for the processor instances; their replay-protection sets persist for the plugin's lifetime. Matches the iter-70/71 in-place-WithMetrics-rewire pattern that was added for the same reason.

Cumulative: 424 tests / 27 projects.

### Fixed — `ReferenceBatchExecutor` skips effects from failed transactions

- A failed transaction's emitted withdrawals + L2→* messages were still added to the batch trees. Per L2 semantics, a failed tx reverts all its state changes — including emitted effects. The `ITransactionExecutor` contract should already filter on the executor side, but a buggy executor that leaks effects from a failed tx silently produces a withdrawal-tree commitment that doesn't match the (correct) ReceiptRoot — surfacing only at L1 settlement when the inclusion proof for the leaked withdrawal is checked against the user's actual state (which never debited the funds).
- Defense in depth at the batch level: `if (!result.Receipt.Success) continue;` skips the `withdrawalTree.Add` / `outbox.Add` calls.
- **1 new test**: `FailingExecutor` returns a leaked withdrawal on a failed tx → batch's `WithdrawalRoot` is still `Zero`.

Cumulative: 424 tests / 27 projects.

### Changed — Documented `ProofResult` prover contract

- The iter-139 (Kind match) and iter-140 (PublicInputHash match) settlement-plugin assertions are now also documented in the `ProofResult` record's XML doc — making the contract explicit at the API surface where prover authors will see it. No behavior change; just makes the invariants visible to anyone implementing `IL2Prover` without having to read the settlement plugin to discover what's checked.

Cumulative: 423 tests / 27 projects.

### Changed — `BisectionGame.RunRound` emits the BisectionRounds metric on its dead-branch settle path

- The `mid == _lo` branch in `RunRound` is normally unreachable (TrySettle at construction + every prior round keeps `_hi - _lo > 1` while `_settled == false`). But if a future refactor ever breaks that invariant, the branch settles the game without emitting `MetricNames.BisectionRounds` — a silent metric drop. Defense-in-depth: emit the same metric `TrySettle` would.
- No behavior change in the non-degenerate path; just makes the dead-branch fallback consistent with `TrySettle`.

Cumulative: 423 tests / 27 projects.

### Fixed — `L2SettlementPlugin` also checks `ProofResult.PublicInputHash` matches settlement's hash

- Companion to the iter-139 Kind check. The settlement plugin computes its own `hash = StateRootCalculator.HashPublicInputs(publicInputs)` and the prover returns its own `proofResult.PublicInputHash`. If the two disagree, the prover proved a different set of inputs than settlement built — the iter-128 verifier-side `PublicInputHash` check catches it later, but it costs a wasted `SubmitBatch` round-trip and surfaces as a confusing remote rejection.
- Now the settlement plugin also asserts `proofResult.PublicInputHash.Equals(hash)` at the prove boundary; the catch-and-requeue path takes care of the rest.
- Updated `FakeProver` test fixture to honor the prover contract (compute the actual hash from `request.PublicInputs` instead of returning Zero).

Cumulative: 423 tests / 27 projects (same count; the iter-139 LiarProver test now exercises both Kind and PublicInputHash via the same plugin assertion path).

### Fixed — `L2SettlementPlugin` checks `ProofResult.Kind` matches requested kind

- A buggy prover that returned `ProofResult.Kind = None` (or any other mismatch) silently produced a commitment with whatever the prover claimed. The mismatch would only surface at audit time as `NoZeroProofCheck` failing with "ProofType.None — soft-sealed but never proved" — confusing, with no direct link back to the prover bug.
- The settlement plugin now asserts the prover's contract at the prove boundary: `proofResult.Kind != requestedKind` throws `InvalidOperationException`. The exception is caught by the existing `try/catch` around prove+submit, increments `SubmitFailures`, and re-queues the batch — so a buggy prover doesn't block the queue but its bug is at least visible in counters.
- **1 new test**: `LiarProver` returns `Kind = None` for a `Multisig` request → caught, no submission, failure counter ticks.

Cumulative: 423 tests / 27 projects.

### Fixed — `CensorshipDetector` rejects negative `baseSlashAmount`

- The constructor accepted any `BigInteger` for `baseSlashAmount`. A negative slash amount, embedded in the resulting `CensorshipReport.SlashAmount`, would either get silently flipped on L1 (rewarding the offending sequencer instead of penalizing) or revert at the slash transaction — either way the operator only finds out after submitting reports.
- Now: reject negative at construction with `ArgumentOutOfRangeException`. Zero is still allowed for warning-only modes (operator wants detection without enforcement).
- **2 new tests**: negative → rejected; zero → accepted (warning-only mode).

Cumulative: 422 tests / 27 projects.

### Changed — `Sp1Bridge.IsAvailable` caches the lib-loadable result

- Every `Prove`/`Verify` call invoked `IsAvailable`, which re-attempted the `NativeAbiVersion()` P/Invoke and re-paid the `DllNotFoundException` cost in dev environments where the bridge is intentionally absent. The result is sticky for process lifetime — the lib is either there at startup or it isn't.
- Cached the result in a `static bool? _isAvailableCache`. Added `ResetAvailableCache()` for test scenarios that want to re-probe.
- **1 new test**: pin the cache behavior (first/second/post-reset calls all return the same result, no exception).

Cumulative: 420 tests / 27 projects.

### Fixed — `InMemoryL2RpcStore.Record*Proof` takes a defensive copy

- `RecordWithdrawalProof` and `RecordMessageProof` retained the caller's `byte[]` reference. A caller who reused a scratch buffer across many records — or who mutated their copy after passing it in — would silently corrupt the previously stored proof. The corruption would only surface much later when an RPC client scraped `/getl2withdrawalproof` and got back garbage.
- Both setters now `Clone()` the input. The `IReadOnlyList<byte>` style API (`ReadOnlyMemory<byte>?`) on the read side already guards against caller mutation; this aligns the write side.
- **1 new test**: store, mutate caller's buffer, read — assert stored bytes unchanged.

Cumulative: 419 tests / 27 projects.

### Changed — `L2ProverPlugin.Wire` gives a clear error for `ProofType.None`

- `ProofType.None` is a legitimate enum value (used for genesis / operator-trusted flows in the wire format) but the prover plugin can't produce a proof for it. The switch's `_ => throw NotSupportedException($"Unknown ProofType {_kind}")` arm fired with `"Unknown ProofType None"` — misleading, since None is defined.
- Added an explicit `ProofType.None => …` case with a message explaining what the operator should do: configure Multisig/Optimistic/Zk to enable settlement.

Cumulative: 418 tests / 27 projects.

### Fixed — `InMemoryL2RpcStore.RegisterAsset` removes orphan index entries

- Same bug pattern as iter 100's `AssetRegistry.Register` fix, in the RPC-side asset cache. Re-registering an L1 asset against a different L2 token (or vice versa) overwrote one index but left the stale entry in the other. `GetCanonicalAsset(oldL2)` still returned the L1 asset while `GetBridgedAsset(L1)` returned the new L2 — silent inconsistency between the two RPC lookups.
- Now: detect both repoint cases and `TryRemove` the orphaned entry from the opposite index before writing the new mapping.
- **1 new test**: `RegisterAsset_RepointL2_RemovesOrphan` — re-register, assert old L2 → L1 lookup returns null.

Cumulative: 418 tests / 27 projects.

### Fixed — `WithdrawalProcessor` enforces nonce uniqueness across batches

- `_byNonce` was cleared on `SealBatch`, so a user could re-stage the same `(sender, nonce)` withdrawal in the next batch — L2 silently accepted; the duplicate was only caught hours later at L1 settlement. The `WithdrawalRequest.Nonce` field's docstring is explicit: "per-(chain, sender) monotonic for replay protection," so L2 must enforce uniqueness across the chain's lifetime, not just per-batch.
- Added `_consumedAcrossBatches` set populated on `SealBatch` (just before clearing `_byNonce`). `Stage` now rejects duplicates from prior batches with a clear "already used by sender X in a prior batch" message.
- **1 new test**: stage nonce 1, seal, attempt to stage nonce 1 again → rejected.

Cumulative: 417 tests / 27 projects.

### Changed — `checked` arithmetic sweep for unbounded buffer-size sums

- Continued the iter-130/131 pattern across the remaining unbounded `var size = X + caller.Length` sites:
  - `BatchSerializer.Encode` — `CommitmentFixedSize + commitment.Proof.Length`.
  - `MessageHasher.HashMessage` — `61 + payload.Length`.
  - `KeyedStateStore.HashEntry` — `4 + key.Length + 4 + value.Length`.
- Each sum is now wrapped in `checked(…)` so a pathological caller-supplied length near `int.MaxValue` surfaces an `OverflowException` at the sum site rather than later as a confusing `OverflowException` from `new byte[wrappedNeg]` deep in the allocator. Bounded payloads (signature/proof bytes already capped by validators) are unchanged.

Cumulative: 416 tests / 27 projects.

### Changed — `Sp1RiscVProver.ProveAsync` uses `checked` arithmetic for combined-buffer size

- `4 + publicInputBytes.Length + 4 + request.Witness.Length` summed in plain `int`. A pathological witness near `int.MaxValue` would wrap to negative and surface as `OverflowException` from `new byte[wrappedNeg]` with no link to the offending sum.
- Wrapped the size computation in `checked(…)` (matches the iter-130 pattern in the gateway aggregators). Behavior under realistic witness sizes is unchanged.

Cumulative: 416 tests / 27 projects.

### Changed — Gateway aggregators use `checked` arithmetic for size sums

- `PassThroughAggregator.ConcatenateProofs` and `PassThroughRoundProver.Combine` summed `4 + proofLen` accumulators in plain `int` arithmetic. A pathological N × proofLen combination near `int.MaxValue` (~2 GiB) would silently wrap to negative; the next `new byte[wrappedNeg]` then threw `OverflowException` deep in the allocator with no link back to the offending sum.
- Wrapped both sites in `checked(…)` so an overflow surfaces with the operation in scope. Behavior under realistic loads is unchanged.
- No new tests — behavior preserved; existing 19 gateway tests still pass.

Cumulative: 416 tests / 27 projects.

### Changed — `L1MessageInbox.Enqueue` duplicate-pending check is O(1)

- The check was `foreach (var existing in _pending) if (...) throw` — O(n) per enqueue. Under bursty inbound traffic with thousands of pending messages, the cost compounds: a flood of N legitimate messages is O(N²). Not a security bug, but a performance cliff.
- Added a `HashSet<(uint, ulong)> _pendingKeys` mirror that's kept in sync with `_pending`. Enqueue uses `Add` (returns false on duplicate), Dequeue uses `Remove`. Behavior is identical; the hot path is now O(1).
- No new tests — behavior unchanged, existing 10 messaging tests still pass.

Cumulative: 416 tests / 27 projects (no test count change).

### Fixed — `VerifierRegistry.VerifyAsync` checks `commitment.PublicInputHash` matches `publicInputs`

- The registry compared 10 duplicated fields between `commitment` and `publicInputs` but never re-derived the actual hash of `publicInputs` and matched it against `commitment.PublicInputHash`. A malicious submission could set `PublicInputHash` to any value (planning a forged future replay against the consensus-recorded hash) while supplying real `publicInputs` that the verifier accepts. The audit-time `PublicInputHashConsistencyCheck` (iter 96) caught this after-the-fact, but verify-time is the right boundary.
- Added `StateRootCalculator.HashPublicInputs(publicInputs).Equals(commitment.PublicInputHash)` check; failure → `ProofVerificationResult.Fail("commitment.PublicInputHash != hash(publicInputs)")`.
- **1 new test**: `Registry_FailsWhenPublicInputHashIsForged` — commitment with arbitrary `PublicInputHash` but valid 10-field-aligned `publicInputs` → rejected with the right reason.

Cumulative: 416 tests / 27 projects.

### Fixed — `L2SettlementPlugin.Dispose` no longer races in-flight `SubmitNextAsync`

- `Dispose` called `_submitGate.Dispose()` unconditionally. If `SubmitNextAsync` was mid-flight (very plausible since it's invoked fire-and-forget from `OnBatchSealed`), the inevitable `Release()` in its `finally` threw `ObjectDisposedException` — which surfaces only via `TaskScheduler.UnobservedTaskException` (invisible by default) and aborts the in-flight submit's metric accounting.
- Both ends of the gate now swallow `ObjectDisposedException`: the entry `WaitAsync` returns quietly when shutdown wins the race; the exit `Release` is wrapped in a try/catch. Either way the in-flight task completes cleanly.
- **1 new test**: submit parked inside a blocking client, `Dispose()` while in-flight, then unblock the client — assert no exception escapes.

Cumulative: 415 tests / 27 projects.

### Fixed — `ChainAuditor.AuditAsync` per-check exception isolation

- A buggy custom check that threw aborted the entire audit — subsequent checks were skipped, and the operator saw only the exception (no findings from anything that ran before it). For an audit framework whose value is "every registered check runs and reports its result," this is the wrong default.
- Now: per-check try/catch converts a thrown exception into a single failure finding (`Check = check.Name`, `Detail = "check threw {Type}: {Message}"`), and the audit continues. Caller cancellation still propagates verbatim as `OperationCanceledException` — that's a control-flow signal, not a check failure.
- **2 new tests**: throwing-check produces a failure finding while a sibling `ContinuityCheck` still runs to completion; pre-cancelled token surfaces `OperationCanceledException` instead of being swallowed.

Cumulative: 414 tests / 27 projects.

### Fixed — `ChainAuditor.AuditAsync` empty-batches still emits run + failure metrics

- The empty-batches early return short-circuited before the metric increments. The failing finding showed up in the returned `AuditReport`, but `AuditsRun` and `AuditFailures` counters never ticked. An operator watching only dashboards would see the audit as "didn't happen" — invisible to monitoring even though we returned a failed report.
- Now: increment `AuditsRun` + `AuditFailures` on the empty-batches branch too, before returning. The report-side and metrics-side observability paths are now consistent.
- **1 new test**: empty batches → both counters tick + report still fails.

Cumulative: 412 tests / 27 projects.

### Fixed — `neo-stack` exception handling + `init-l2 --chain-id` validation

- Two related gaps in the launcher CLI:
  1. `init-l2` parsed `--chain-id` with `uint.Parse` (raw `FormatException` on bad input) and never validated against the L1-reserved 0. Aligned with `create-chain`'s iter-123 fix: `uint.TryParse` + `ChainIdValidator.ValidateL2(value, "--chain-id")`.
  2. `Program.Main` had no top-level try/catch. Any subcommand exception (`FormatException`, `IOException`, `InvalidDataException` from the validators) leaked as a raw stack trace. Added the wrap that `neo-hub-deploy` already uses: catch → `Console.Error.WriteLine($"Error: {ex.Message}")` + exit 1.

Cumulative: 411 tests / 27 projects (CLI changes aren't unit-tested; the underlying validator is covered in `UT_Models`).

### Fixed — `neo-stack create-chain --chain-id` validates against L1-reserved 0

- `uint.Parse(...)` accepted any value, including the L1-reserved `0` (matches `L2Outbox.L1ChainId`). An operator who typo'd `--chain-id 0` would generate a chain config that misroutes L2→L2 messages as L2→L1 — silently broken from the genesis block. Also: malformed input like `--chain-id abc` threw `FormatException` with a raw stack trace.
- Now: `uint.TryParse` for clean error on non-numeric, plus `ChainIdValidator.ValidateL2(value, "--chain-id")` (the shared helper from iter 114) for the reserved-id check. Each surfaces a clear single-line error and exit code 1.

Cumulative: 411 tests / 27 projects (no new test; the validator is already covered in `UT_Models`).

### Fixed — `InMemoryMessageRouter._finalized` is thread-safe

- `_finalized` was a plain `Dictionary<UInt256, FinalizedEntry>`. `RecordFinalized` (settlement-pipeline thread) writes; `GetMessageProofAsync` (RPC-handler threads) reads. A concurrent read while writing had a small but real chance of corruption / `NullReferenceException` deep in `Dictionary.FindEntry`.
- Swapped to `ConcurrentDictionary<UInt256, FinalizedEntry>`. The cost is negligible for the test/devnet backend; production wires a different impl.
- **1 new test**: 8 threads × 500 iterations alternating writes / reads → no exceptions.

Cumulative: 411 tests / 27 projects.

### Fixed — `InMemorySequencerCommitteeProvider.SetMaxCommitteeSize` rejects shrink below current count

- The setter accepted any `max ∈ [1, 64]` regardless of how many members were already registered. Calling `SetMaxCommitteeSize(2)` on a 5-member committee silently succeeded; the count then exceeded the cap until members organically exited — a misleading "almost-frozen" state that hides the operator's typo (registrations would be rejected with no clear pointer back to the misconfigured cap).
- Now: rejects with `InvalidOperationException("max N < current committee count M — exit members before shrinking")` so the operator sees both the proposed and actual values immediately.
- **1 new test**: 5-member committee, `SetMaxCommitteeSize(2)` → rejected with both numbers in the message.

Cumulative: 410 tests / 27 projects.

### Fixed — `ProofValidityCheck.RunAsync` matches null-guard convention

- Sister checks (`ContinuityCheck`, `NoZeroProofCheck`, `PublicInputHashConsistencyCheck`) all begin with `cancellationToken.ThrowIfCancellationRequested()` + `ArgumentNullException.ThrowIfNull(batches)`. `ProofValidityCheck.RunAsync` was missing both — a null-batches caller hit the `foreach` and got a `NullReferenceException` with no link back to the bad input.
- Added the standard prelude.
- **1 new test**: `RunAsync(null)` → `ArgumentNullException`.

Cumulative: 409 tests / 27 projects.

### Added — Shared `Neo.L2.Telemetry.PortValidator.Validate(int, label)`

- The devnet runner's `--metrics-port` parser had no bounds check — a typo like `--metrics-port 99999` propagated to `IPEndPoint` construction and surfaced an opaque "value must be between 0..65535" deep in the wiring path.
- Promoted iter-111's `L2MetricsSettings.ValidatePort` to a shared helper in `Neo.L2.Telemetry` so CLI tools can reuse the check without taking a plugin dependency. Caller-supplied `contextLabel` ("L2Metrics Port" vs `--metrics-port`) appears in the error message so the operator sees which input was bad. `L2MetricsSettings.ValidatePort` now delegates to it.
- **4 new tests**: boundary values 0/9090/65535 → accepted; negative → rejected; >65535 → rejected; context label appears in error message.

Cumulative: 408 tests / 27 projects.

### Fixed — `Sp1RiscVProver` only falls back to mock on `NotImplemented`

- The fallback condition was `status != Ok || proofBytes is null`, which silently substituted a trivially-valid mock proof on **any** non-OK status — including real bridge errors like `InvalidInput` (malformed witness) or `ProveFailed` (prover crashed). The downstream verifier then ran the mock proof through the real bridge, got `VerifyRejected`, and surfaced only as a confusing "verify rejected" message hours later — disconnecting the cause (bad input on the prover side) from the symptom (failed verify).
- Now: fallback fires only on `NotImplemented` (the genuine "bridge missing" signal). Other non-OK statuses throw `InvalidOperationException("SP1 bridge {status} for proof generation — verify input shape, witness, or bridge state")` so the operator sees the bridge's actual status at the failure site.

Cumulative: 404 tests / 27 projects.

### Fixed — `Sp1Bridge.Prove` bounds-checks native return length

- The Native FFI returned `nuint outputLen` was cast `(int)outputLen` for `new byte[len]` and `Marshal.Copy(..., len)` without bounds checking. A misbehaving native bridge or corrupted FFI return that declared > 2 GB would wrap the cast and feed a wrapped length into `Marshal.Copy` — a heap-overflow shape on a process boundary that crosses .NET ↔ unmanaged.
- Added `Sp1Bridge.MaxProofBytes = 1 GiB` defensive cap (well above realistic SP1 proof sizes, well below `int.MaxValue`). Cast is now guarded — anything above the cap returns `Sp1BridgeStatus.InvalidInput` and the buffer is freed via the existing `finally`.
- **1 new test**: pins `MaxProofBytes > 0 && < int.MaxValue` so a future "trust the bridge" refactor can't silently drop the guard.

Cumulative: 404 tests / 27 projects.

### Fixed — `MetricsHttpServer.StatusText` returns the right reason phrase for 503

- The switch only knew about 200/404/500. The readiness-probe failure path (`HandleReady` → `MetricsHttpResponse(503, ...)`) fell through to the default `=> "OK"`, sending `"HTTP/1.0 503 OK"` on the wire — status code says error, reason phrase says OK. Strict HTTP parsers (load-balancer health-check libraries, Kubernetes probes) would reject this as malformed.
- Added the 503 case → `"Service Unavailable"`.
- **1 new test**: real HTTP scrape against `/readyz` with a failing readiness check → status 503, reason phrase "Service Unavailable".

Cumulative: 403 tests / 27 projects.

### Fixed — `AttestationVerifier` deduplicates signers before signature verification

- The verify loop sequence was validator-set → length → ECDSA-verify → dedup. A malicious prover could fill the wire payload with `MaxSigners` (256) copies of the same valid signature and force the verifier to perform 256 redundant ECDSA verifications before the duplicate-signer check fired. Not a DoS at production scale, but a wasted-cost vector that ramps with the cap.
- Reordered: dedup-on-first-occurrence runs before signature verification. Cost is now bounded by the number of *distinct* keys submitted, not the wire-payload count. Correctness unchanged: duplicates still fail with the same error.
- **1 new test**: payload with one signer repeated twice → `"duplicate signer"` failure.

Cumulative: 402 tests / 27 projects.

### Added — `ChainIdValidator.ValidateL2(uint)` plugin-config validator

- `ChainId = 0` is reserved for Neo L1 (matches `L2Outbox.L1ChainId` sentinel). An L2 chain that adopts it would misroute L2→L2 messages as L2→L1, sending them out the wrong outbox subtree. The default `uint` value is 0, so an operator who omits `ChainId` from `config.json` silently lands on the reserved value.
- Added `Neo.L2.ChainIdValidator.ValidateL2(uint, settingName?)` (in `Neo.L2.Abstractions`) that throws `InvalidDataException("<settingName> 0 is reserved for Neo L1")` on the reserved id. Wired into `L2BridgePlugin.Configure`, `L2BatchSettings.From`, and `L2SettlementSettings.From`.
- Each call site reads via `GetValue<uint?>("ChainId")` to distinguish "key missing" (test mode, no config — leave at default 0) from "explicitly set to 0" (operator misconfig — reject). Real production configs always set the key, so the validator fires whenever it's actually wrong.
- **3 new tests**: ValidateL2(0) → rejected; ValidateL2(1, 1001, MaxValue) → accepted; setting-name parameter included in error message.

Cumulative: 401 tests / 27 projects.

### Fixed — `AuditReport.Passed` requires non-empty `Findings`

- `Passed` was `Findings.All(f => f.Passed)`, which returns `true` vacuously on an empty list. A caller who constructed the report directly (bypassing `ChainAuditor`'s no-checks-registered guard) would see "passed" with no checks run — a silent observability regression for hand-built reports. Sister case to iter 85's `ChainAuditor` empty-checks fix, applied at the report-API layer.
- Now: `Findings.Count > 0 && Findings.All(...)`. `ChainAuditor` already guarantees at least one finding via the iter-85 guard, so this is purely defense for hand-built reports.
- **1 new test**: report with empty findings → `Passed = false`.

Cumulative: 398 tests / 27 projects.

### Fixed — `L2BatchSettings.From` rejects zero / negative thresholds at parse time

- `MaxBlocksPerBatch`, `MaxTransactionsPerBatch`, and `MaxBatchAgeMillis` accepted any int. A misconfig like `MaxBlocksPerBatch: 0` made `BatchSealer.ShouldSeal` return `true` on every block — every block became its own batch, producing degenerate per-block batches that each carry full settlement / proving overhead. Operators saw the misconfig as a runaway L1 submission rate hours later, not at plugin load.
- Added `L2BatchSettings.ValidatePositive(int, name)` that throws `InvalidDataException("L2Batch <name> must be > 0, got N — fix config")` at parse time. `From` calls it on each Max* setting.
- **3 new tests**: zero → rejected with name; negative → rejected with value+name; boundary `1` → accepted (per-block sealing is degenerate but legal for test/devnet diagnostics).

Cumulative: 397 tests / 27 projects.

### Fixed — `L2MetricsSettings.From` validates port range at parse time

- `Port = s.GetValue("Port", 9090)` accepted any int. A typo like `Port: 90909` (six digits) propagated to `IPEndPoint` construction at `Start()`, where the resulting `ArgumentOutOfRangeException("value must be between 0..65535")` is real but the operator has to dig through a stack trace to map it back to a config typo.
- Added `L2MetricsSettings.ValidatePort(int)` that throws `InvalidDataException("L2Metrics Port N out of range — must be 0 (any free) or 1..65535")` at parse time, citing both the bad value and the config-key name. `From` calls it on the parsed value.
- **3 new tests**: out-of-range high → rejected; negative → rejected; boundary values 0, 9090, 65535 → accepted.

Cumulative: 394 tests / 27 projects.

### Fixed — `ChainAuditor.AuditAsync` rejects duplicate batch numbers (strict ascending)

- The sort check was `batches[i].BatchNumber < batches[i - 1].BatchNumber` (strict less-than), which silently allowed equal batch numbers. A duplicate-batch-number list is a precondition violation (a chain can't carry two distinct commitments at the same height) but the auditor would let it pass; the duplicate would then surface downstream as a confusing `ContinuityCheck` failure ("batch N does not follow N").
- Tightened to `<=` so duplicates throw `ArgumentException("batches must be sorted strictly ascending by batchNumber")` at the input-validation step.
- **1 new test**: two batches at batch number 5 → throws with "strictly ascending" in message.

Cumulative: 391 tests / 27 projects.

### Added — `ProofTypeExtensions.Resolve(byte)` plugin-config validator

- `L2ProverPlugin.Configure` did `_kind = (ProofType)section.GetValue<byte>(...)` without bounds-checking. A misconfigured `ProofType: 99` would only surface much later at `Wire()` time (when the operator sees `NotSupportedException("Unknown ProofType 99")`).
- `L2SettlementSettings.From` did the same — the bad byte propagated into `_settings.ProofType` and only failed at first `SubmitNextAsync`.
- Added `ProofTypeExtensions.Resolve(byte)` (in `Neo.L2.Abstractions`, alongside the enum) that throws `InvalidDataException` with a clear message listing valid values. Both plugins now use it at config-parse time, so misconfiguration surfaces at plugin load.
- **2 new tests**: every valid byte 0..3 → resolves; byte 99 → rejected with the byte in the message.

Cumulative: 390 tests / 27 projects.

### Fixed — `RiscVProofPayload.Decode` rejects unknown `ProofSystem` discriminants

- Same enum-discriminant gap as iter 103's `BatchSerializer` fix, but in the inner Stage-2 ZK proof wrapper. `bytes[1]` was cast `(ProofSystem)` without bounds-checking. A corrupted or replayed-from-future payload with a discriminant > 4 slipped through as an undefined enum value, and a downstream verifier dispatcher's `==` comparison would silently treat it as "not the expected one" — selecting the wrong backend or skipping verification entirely.
- Now bounds-checks against `(byte)ProofSystem.Axiom` and throws `InvalidDataException` with a clear message.
- **2 new tests**: byte 99 → rejected; every valid byte 0..4 → round-trips.

Cumulative: 388 tests / 27 projects.

### Fixed — `BatchSerializer.Decode` rejects trailing bytes (strict length match)

- Same trailing-byte malleability surface as `DepositPayload` (iter 106) but in the master commitment wire format. The check was `data.Length < pos + proofLen` — only caught buffers shorter than declared. Trailing bytes after the proof were silently ignored, opening a divergence between the L1 contract (hashes full calldata) and the L2 decoder (strips trailing). Same logical commitment, two different hashes.
- Strengthened to `pos + proofLen != data.Length`. The four boundary-pair tests around proof length (zero, at-cap, oversized-claim, trailing-bytes) now form a complete defensive set.
- **1 new test**: commitment with trailing bytes → `InvalidDataException("length mismatch")`.

Cumulative: 386 tests / 27 projects.

### Fixed — `DepositPayload.Decode` rejects trailing bytes (strict length match)

- The length check was `pos + amountLen > bytes.Length`, which only caught buffers shorter than declared. Trailing bytes after the amount were silently ignored. An attacker could append padding that the L1 hashes (full bytes) but the L2 decoder strips — a malleability surface where the same logical deposit produces different leaves on either side.
- Strengthened to `pos + amountLen != bytes.Length` (matches the `OptimisticProofPayload.Decode` pattern).
- **1 new test**: payload with trailing bytes → `InvalidDataException`.

Cumulative: 385 tests / 27 projects.

### Fixed — `L2DAPlugin.Configure` rejects unknown `DAMode` bytes

- The `DAMode` switch fell through to `InMemoryDAWriter` for any byte outside `0..3`. An operator who misconfigured `DAMode = 99` would silently end up with the in-memory test backend — they'd think batch payloads were going to External DA, but actually they vanish into a process-local hash table that disappears at restart. The kind of failure that only surfaces hours later when something downstream tries to fetch the data.
- Added `L2DAPlugin.ResolveDAMode(byte)` that validates against the defined enum range and throws `InvalidOperationException` with a clear message listing the valid values. The switch's `_` arm is now an internal-defense `InvalidOperationException("unhandled DAMode N")` since `ResolveDAMode` already filters.
- **2 new tests**: every valid byte 0..3 → resolves to its enum; byte 99 → rejected with the byte in the message.

Cumulative: 384 tests / 27 projects.

### Fixed — `DeployPlanner` surfaces clear errors for duplicate / empty step names

- The name index was built via `steps.ToDictionary(s => s.Name)`, which throws a generic `ArgumentException("An item with the same key has already been added. Key: foo")` on duplicates. Operators with a typo in their plan JSON had to map the message back to the offending entry by hand.
- Empty / whitespace step names slipped through silently — a `DependsOn: [""]` reference would happen-to-resolve and the operator's typo would only show up at runtime.
- Now: explicit foreach with `string.IsNullOrWhiteSpace` rejection (`"deploy step name must not be empty or whitespace"`) and `TryAdd` for clearer duplicate messages (`"duplicate deploy step name '<name>'"`).
- **2 new tests**: duplicate-name rejection with name in message; empty-name rejection.

Cumulative: 382 tests / 27 projects.

### Fixed — `BatchSerializer.Decode` rejects unknown `ProofType` discriminants

- The decoder cast `(ProofType)data[pos++]` without validating the byte was within the defined enum range (0..3). A corrupted L1 calldata payload, a replay from a future-version chain, or a hand-crafted attack could carry a discriminant > 3, producing an undefined enum value that downstream `==` comparisons would silently treat as "not the expected one" — a silent verification skip.
- Now bounds-checks the byte against `(byte)ProofType.Zk` and throws `InvalidDataException` with a clear message.
- **2 new tests**: byte 99 → rejected; every valid byte 0..3 → round-trips.

Cumulative: 380 tests / 27 projects.

### Fixed — `InMemoryL2RpcStore.Finalize` keeps `_latestStateRoot` monotonic

- `Finalize(N)` blindly overwrote `_latestStateRoot` with batch N's post-state root regardless of N. Finalizing batch 5 then batch 3 left the latest root at batch 3's older value — an apparent state-root regression that a downstream relayer treats as a chain reorg signal.
- Now tracks `_latestFinalizedBatch` under a lock; `Finalize` only updates `_latestStateRoot` when the new batch number exceeds the prior latest. `GetLatestStateRoot` reads under the same lock so a concurrent reader never observes a torn `UInt256`.
- **1 new test**: `Finalize(5)` then `Finalize(3)` → latest root stays at batch 5.

Cumulative: 378 tests / 27 projects.

### Fixed — `L2RpcMethods` parameter parsing surfaces clear errors

- An RPC call with too few params (e.g. `getl2batch [1001]` — missing the batch number) hit `JArray`'s underlying `List<T>` indexer and surfaced `ArgumentOutOfRangeException` with the unhelpful `"Index was out of range..."` message. RPC clients had no way to tell which param was missing.
- A `chainId` value above `UInt32.MaxValue` was read as `ulong` then cast `(uint)` — silently truncating. Caller passing `0x100000001` got reduced to `1`; `AssertOurChain` then compared `1` vs the local id with a misleading "differs from local" message instead of the actual overflow.
- Added `RequireParam` helper that bounds-checks before indexing, plus `ReadUInt` helper that uses `checked((uint)…)` so oversized chain ids surface `OverflowException` at the parsing boundary.
- **2 new tests**: too-few-params → `ArgumentException("param[N] missing")`; oversized chainId → `OverflowException`.

Cumulative: 377 tests / 27 projects.

### Fixed — `AssetRegistry.Register` removes orphan index entries on re-point

- Re-registering an L1 asset to a different L2 token (or vice versa) overwrote one index but left the stale entry in the other. `TryGetByL2(oldL2Asset)` would still return the prior mapping while `TryGetByL1` returned the new one — a silent registry inconsistency that could route a deposit through the old L2 token long after the operator thought they had repointed it.
- `Register` now detects both repoint cases — `(L1Asset, L2ChainId)` mapped to a new `L2Asset`, and `L2Asset` mapped to a new `(L1Asset, L2ChainId)` — and removes the orphaned entry from the opposite index before writing the new mapping.
- **2 new tests**: repoint L2Asset → orphan L2 index removed; repoint L1Asset → orphan L1 index removed.

Cumulative: 375 tests / 27 projects.

### Fixed — `DepositProcessor.Process` claims nonce only after validation succeeds

- The consumed-set was populated BEFORE the asset-registry lookup, so a transient validation failure (e.g. "asset not yet registered" — the L2 cross-chain pipeline can deliver a deposit before the asset is registered on L2) permanently locked the `(SourceChainId, Nonce)` pair. When the operator later registered the missing asset, retry hit the consumed-set first and threw `"already processed"` — the L1 message stayed in NeoHub's "delivered" state but the L2 could never mint the funds.
- Now: decode + asset-registry lookup happens first, then the atomic claim-and-add to `_consumed`. Replay protection still covers the success path (subsequent identical-nonce calls fail), and the concurrency window for two callers passing validation simultaneously is still safe (only one wins the `_consumed.Add`).
- **1 new test**: process with unknown asset → fails, nonce NOT consumed; register the asset; retry → succeeds, nonce now consumed.

Cumulative: 373 tests / 27 projects.

### Fixed — `L2SettlementPlugin.SubmitNextAsync` serializes parallel submits

- `OnBatchSealed` did `_ = SubmitNextAsync()` fire-and-forget. When N batches sealed in quick succession, N submit tasks ran in parallel — racing each other into `_client.SubmitBatchAsync`. NeoHub then sees out-of-order `BatchNumber` values and rejects the late ones; the retry path tries to re-queue the loser, but the winner's batch is already on-chain — the L1 expects `lastSubmitted+1`, so the loser stays stuck and retries forever.
- Added a `SemaphoreSlim(1, 1)` gate around the dequeue + prove + submit path. Concurrent calls now wait their turn; submission order matches enqueue order.
- **1 new test**: 4 concurrent `SubmitNextAsync` calls against a tracking client → all 4 batches submitted, peak in-flight = 1.

Cumulative: 372 tests / 27 projects.

### Fixed — `ChallengeOrchestrator.InspectWithBisectionAsync` validates checkpoint shapes

- The agreement-at-end short-circuit (`challengerCheckpoints[^1].Equals(sequencerCheckpoints[^1])`) ran *before* any length validation. An empty array crashed with raw `IndexOutOfRangeException`; mismatched-length arrays silently compared incompatible last elements (could wrongly return "no fraud" when the last entries happened to match).
- Now the orchestrator validates `length match` and `length ≥ 2` upfront, throwing `ArgumentException` at its boundary. The `BisectionGame` constructor's existing checks become defensive duplicates.
- **3 new tests**: empty arrays → throws; mismatched lengths → throws; single-checkpoint (length 1) → throws.

Cumulative: 371 tests / 27 projects.

### Added — `PublicInputHashConsistencyCheck` audit

- New `IAuditCheck` that re-derives `PublicInputHash` from each batch's stored fields (via `StateRootCalculator.HashPublicInputs`) and compares against the stored value. Catches commitments where the hash was set to a value derived from different public inputs than the commitment claims — a tamper that would otherwise verify against the wrong proof.
- MVP assumes `L1MessageHash = Zero` and `BlockContextHash = Zero`, matching the Phase 0-3 settlement plugin's `BuildPublicInputs`. When future phases populate those fields, this check needs an augmenting resolver (same shape as `ProofValidityCheck`'s `publicInputsResolver`).
- **3 new tests**: consistent hash → pass with summary; tampered hash → fail with batch number; empty list → vacuous pass.

Cumulative: 368 tests / 27 projects. Audit pluggability now covers continuity, proof validity, no-zero-proof, and public-input-hash consistency.

### Fixed — `JsonRpcClient` wraps network-level failures as `JsonRpcException`

- Iters 93 + 94 wrapped malformed JSON and non-2xx HTTP. This completes the picture by wrapping `HttpRequestException` (connection refused / DNS / TLS) and `TaskCanceledException` from a timeout. Caller-driven `OperationCanceledException` (via the supplied `CancellationToken`) is preserved verbatim so the caller distinguishes their own cancel from a server-side issue.
- All wrap to `JsonRpcException(-32603)` with a descriptive message.
- **2 new tests**: network error → wrapped; caller cancellation → `OperationCanceledException` propagates.

Cumulative: 365 tests / 27 projects. The RPC client now exposes exactly one exception type for every failure mode except caller-cancellation.

### Fixed — `JsonRpcClient` wraps non-2xx HTTP responses as `JsonRpcException`

- `EnsureSuccessStatusCode` threw `HttpRequestException` on a non-2xx status (e.g. proxy 502, server 500, gateway 504), inconsistent with the parse-error wrapping fixed in iter 93. Callers had to handle two different exception types depending on whether the failure was at the HTTP or JSON layer.
- Now wraps as `JsonRpcException(-32603)` ("Internal error" per JSON-RPC 2.0 spec) with the status + reason phrase + body snippet.
- **1 new test** stubs an HTTP 502 response and asserts `JsonRpcException(-32603)`.

Cumulative: 363 tests / 27 projects. The RPC client now produces exactly one exception type for all failure modes (RPC error, parse error, HTTP error).

### Fixed — `JsonRpcClient` wraps malformed-JSON as `JsonRpcException`

- `JToken.Parse` exceptions on malformed RPC responses (e.g. proxy returning HTML 502, gateway truncated body) leaked as raw parser exceptions instead of `JsonRpcException`. Callers had to write disparate exception handlers depending on the failure origin.
- Wraps in `JsonRpcException` with code `-32700` (JSON-RPC 2.0 spec code for "Parse error") so callers see one exception type regardless of failure source.
- **1 new test** stubs an HTML 502 response and asserts `JsonRpcException(-32700)`.

Cumulative: 362 tests / 27 projects.

### Fixed — `L2MetricsPlugin.ResolveBindAddress` rejects empty / null input

- Empty `""` BindAddress fell through `IPAddress.TryParse` to `Dns.GetHostAddresses("")`, which on Linux returns ALL local interface addresses non-deterministically. The plugin would bind to a random interface depending on machine config — exactly the opaque-failure-mode an operator hates.
- Now `ArgumentException.ThrowIfNullOrEmpty` rejects both upfront with a clear error.
- **2 new tests**: empty + null both throw.

Cumulative: 361 tests / 27 projects.

### Fixed — `DeployPlan.FromJson` rejects unsupported version

- `FromJson` read the `version` field but didn't validate it. A future v2 plan with renamed fields (e.g. `nefPath` → `wasmPath`) would silently parse with the v1 reader and produce garbage. Same defensive pattern as the proof-payload decoders' version checks.
- Added `DeployPlan.CurrentVersion = 1` constant + a hard check in `FromJson` that throws `InvalidDataException` on mismatch.
- **1 new test** verifies a `version=99` plan is rejected.

Cumulative: 359 tests / 27 projects.

### Added — `AssetRegistry` overwrite-semantics test

- `Register` silently overwrites an existing entry under the same `(L1Asset, L2ChainId)` key. Documented now via a test that registers twice with different `Active` flags and asserts the second registration wins. Pins overwrite as a deliberate API choice — a future refactor to "throw on duplicate" would break the test instead of silently breaking governance flows that re-register assets.
- **1 new test**.

Cumulative: 358 tests / 27 projects.

### Fixed — `L2SettlementPlugin.SubmitNextAsync` no longer drains queue when un-wired

- Real silent-data-loss bug: `SubmitNextAsync` dequeued an item BEFORE checking whether `_prover` / `_client` had been wired. If `Wire()` hadn't been called yet (operator setup error, or `OnBatchSealed` event firing before wiring completed), every batch flowing through this plugin would be silently dropped — no exception, no failure metric, just gone.
- Fixed by moving the wiring check before the dequeue. Items stay in the queue until `Wire()` is called.
- **1 new test** asserts pending count stays at 1 after `SubmitNextAsync` is called without `Wire`.

Cumulative: 357 tests / 27 projects.

### Added — `MetricsEmittingDAWriter` unwrap/rewrap state-preservation test

- `L2DAPlugin.WithMetrics` unwraps the existing decorator and rewraps with the new sink. The inner `InMemoryDAWriter`'s content store is preserved through this swap, but no test pinned that property — a future refactor that re-allocated the inner could silently lose published content.
- **1 new test** publishes to a decorated writer, unwraps + rewraps with a different sink, then asserts `IsAvailableAsync(receipt)` still returns true on the new wrapper.

Cumulative: 356 tests / 27 projects.

### Added — `Plan_DetectsSelfCycle` test in `Neo.Hub.Deploy.UnitTests`

- The existing 2-step cycle test (`A→B→A`) didn't cover the degenerate length-1 self-cycle (`A→A`). Adding it pins the trivial case so a future refactor of the recursion-path check can't regress on it.
- **1 new test**.

Cumulative: 355 tests / 27 projects.

### Added — `MetricCatalog` non-blank-description check

- Existing tests enforced "every metric has an entry" + "no orphan entries" + "no trailing period". Missing: empty/whitespace descriptions, which would silently produce a useless Prometheus HELP line (`# HELP foo_total ` with nothing after).
- **1 new test** asserts every catalog value passes `string.IsNullOrWhiteSpace` → false.

Cumulative: 354 tests / 27 projects.

### Fixed — `ChainAuditor.AuditAsync` fails on zero-checks-registered

- An audit with no checks registered used to silently report `Passed = true` because `Findings.All(...)` returns true on an empty collection. A misconfigured production deployment that registered zero checks would get a green report despite proving nothing.
- Surfaces as a failure now with a `"no audit checks registered"` finding before any per-check work; feeds into the existing `l2.audit.failures` counter so an alert fires on the misconfiguration.
- **1 new test** registers no checks, runs an audit, asserts `Passed=false` and the failure counter increments.

Cumulative: 353 tests / 27 projects.

### Added — Boundary tests for `Optimistic`/`RiscV`/`Multisig` proof payload caps

- Iter 76-77 added max-length caps to all three inner proof-payload decoders, with reject-at-`Max+1` tests. Now paired with accept-at-exactly-`Max` tests on each (matches the symmetric pattern from iters 82-83 for `BatchSerializer` and `MerkleProofSerializer`):
  - `OptimisticProofPayload`: 4096-byte signature accepts.
  - `RiscVProofPayload`: 1 MiB inner-proof accepts.
  - `MultisigProofPayload`: 256-signer payload accepts.
- Each pair locks the boundary on both sides — an off-by-one in any direction now fails the build.
- **3 new tests**.

Cumulative: 352 tests / 27 projects. Every length-prefix decoder in the stack now has paired accept-at-`Max` + reject-at-`Max+1` tests.

### Added — `MerkleProofSerializer` boundary test at exactly `MaxDepth`

- Same shape as iter 82's `BatchSerializer` fix: the reject-at-`MaxDepth+1` test (iter 47) lacked a paired accept-at-exactly-`MaxDepth=64` test. An off-by-one could either reject a depth-64 proof (too strict) or admit depth-65 (too loose). Pinning both directions makes the boundary explicit.
- **1 new test** encodes + decodes a depth-64 proof end-to-end.

Cumulative: 349 tests / 27 projects.

### Added — `BatchSerializer` boundary test at exactly `ProofMaxBytes`

- The reject-at-1MiB+1 test (iter 75) didn't have a paired accept-at-exactly-1MiB test. An off-by-one in the limit check could either reject the boundary (too strict) or accept 1MiB+1 (too loose). Pinning both directions makes the boundary explicit.
- **1 new test** encodes + decodes a commitment with a 1MiB proof and verifies round-trip identity.

Cumulative: 348 tests / 27 projects.

### Added — Bidirectional `MetricCatalog` ↔ `MetricNames` consistency check

- The existing completeness test enforced "every `MetricNames` constant has a catalog entry." The reverse — "every catalog entry references a real constant" — was uncovered. An orphan description surviving a metric rename or removal would silently bloat the exposition without triggering any test.
- **1 new test** reflects over `MetricNames` constants and asserts every catalog key matches one. Both directions are now pinned.

Cumulative: 347 tests / 27 projects.

### Added — `MetricsHttpServer.IsRunning` diagnostic

- Public `IsRunning` property returns `true` after `Start()` and `false` after `Dispose()`. Useful for integration-test assertions, operator diagnostics, and host-side health checks that need to verify the metrics endpoint is up before reporting the node ready.
- **1 new test** verifies the lifecycle: `false` → `true` after `Start()` → `false` after `Dispose()`.

Cumulative: 346 tests / 27 projects.

### Added — Dispose-without-Start regression test for `MetricsHttpServer`

- All existing tests called `Start()` after construction; the constructed-but-never-Started case was uncovered. Future refactor that assumes `_loop` is non-null at Dispose would silently NPE only in deployments where Start was deferred or never called.
- **1 new test** constructs a server, calls Dispose without Start, then calls Dispose a second time — both must be no-throw.

Cumulative: 345 tests / 27 projects.

### Docs — AGENTS.md catches up to current state

- Component counts updated: 11 → 15 off-chain libs, 7 → 8 plugins (the new `Neo.Plugins.L2Metrics` was added in iter 50 but the `AGENTS.md` summary still said the old numbers).
- §5 row in the doc.md→code mapping now includes `L2Metrics` alongside the other plugins.
- New "Cross-cutting / Telemetry" row points at `Neo.L2.Telemetry` + `Neo.Plugins.L2Metrics` and `docs/telemetry.md`, mirroring the same row in `architecture-walkthrough.md`.
- Quick-commands test-count refreshed (194 → 344).

### Fixed — Hard cap on `MultisigProofPayload` signer count

- `signerCount` is uint16, naturally capped at 65535. The decoder validated buffer-size match but had no upper limit on count itself — a 65535-signer header forced allocation of `SignerSignature[65535]` (~7 MB) before any sanity check. Production multisigs run 7-21 signers; cap at `MaxSigners = 256` is generous but defensive.
- **1 new test** verifies a 257-signer header is rejected.

Cumulative: 344 tests / 27 projects. All four proof-payload decoders (Multisig, Optimistic, RiscV, plus the outer `BatchSerializer` wrap) now have explicit bounds.

### Fixed — Hard caps on `OptimisticProofPayload` + `RiscVProofPayload` decode

- The decoders accepted any non-negative length matching the buffer size — no upper bound. A hostile peer feeding a 4 GiB length-prefix would crash the verifier on `OutOfMemory` instead of getting a clean `InvalidDataException`. The outer `BatchSerializer.Decode` already caps at 1 MiB, but a caller decoding these payloads directly (skipping `BatchSerializer`) inherits no protection.
- Added defense-in-depth caps: `OptimisticProofPayload.MaxSignatureBytes = 4096` (real signatures are 64), `RiscVProofPayload.MaxProofBytes = 1 MiB` (matches `BatchSerializer`).
- **2 new tests** craft a header claiming oversized length; assert decoder throws `InvalidDataException`.

Cumulative: 343 tests / 27 projects.

### Added — `BatchSerializer` proof-size-limit tests

- `BatchSerializer.Encode` validates `Proof.Length <= ProofMaxBytes` (1 MiB) and `Decode` validates the same in the header. Both checks were uncovered by tests.
- **2 new tests**: encoding a 1 MiB+1 byte proof throws `ArgumentException`; decoding a header claiming oversized proof throws `InvalidDataException`. Closes a defense-in-depth gap — these limits are the safety net against a hostile peer dumping a 4 GiB proof at the L1 settlement contract.

Cumulative: 341 tests / 27 projects.

### Fixed — `PrometheusExporter` formats `±Infinity` per spec

- .NET's `double.ToString` returns `Infinity` / `-Infinity` for non-finite values, but Prometheus exposition format requires `+Inf` / `-Inf`. A scraper rejects the bad form. None of the current emit sites produce infinity, so this is defensive — but a misbehaving plugin (e.g. division by zero in a gauge) would silently corrupt the scrape.
- Helper `FormatDouble` handles `NaN`, `+Inf`, `-Inf` as Prometheus specifies; finite values still go through `G17` invariant.
- **3 new tests** for each non-finite case.

Cumulative: 339 tests / 27 projects.

### Fixed — `MetricsHttpServer.Start` is now idempotent

- Previously a second `Start` call would overwrite `_loop` with a new `Task.Run`, leaving the first accept loop dangling on the same `TcpListener`. Both loops would race on `AcceptTcpClientAsync`, only the latest got awaited at Dispose. Defensive fix consistent with iter 67's plugin-level lock.
- **1 new test** asserts `Start()` twice still serves a clean scrape.

Cumulative: 336 tests / 27 projects.

### Added — `BinaryTreeAggregator.WithMetrics` for consistency

- Previously the aggregator only accepted metrics via constructor — an operator wanting to swap the sink mid-flight had to construct a new aggregator and lose the pending submission list. Adds an in-place `WithMetrics(IL2Metrics)` setter consistent with the same-named methods now on `BatchSealer`, `DepositProcessor`, `WithdrawalProcessor`, and the `L2DAPlugin` decorator-based path.
- **1 new test** verifies the pending list survives a mid-flight sink swap.

Cumulative: 335 tests / 27 projects. The "in-place metric swap on long-lived stateful components" pattern is now uniform across every plugin / library that holds state between metric emissions.

### Fixed — `L2BatchPlugin.WithMetrics` preserves sealer state

- Same shape as the `L2BridgePlugin` fix in iter 70: calling `WithMetrics` set `_sealer = null`, which silently dropped the sealer's `_nextBatchNumber`, `_lastPostStateRoot`, and any in-progress builder. A mid-flight rewire would have caused the next batch to be numbered 1 again — colliding with whatever the chain had already submitted.
- New `BatchSealer.WithMetrics(IL2Metrics)` setter swaps the sink in-place. Plugin's `WithMetrics` calls it instead of nulling.
- **1 new regression test**: post-rewire batch is numbered 2, not 1; old sink keeps batch-1 counter, new sink gets batch-2 counter.

Cumulative: 334 tests / 27 projects.

### Fixed — `L2BridgePlugin.WithMetrics` preserves processor state

- Calling `WithMetrics` on the bridge plugin used to re-construct `DepositProcessor` and `WithdrawalProcessor`, dropping their consumed-nonce dedup sets and the in-progress withdrawal tree. A replay deposit submitted after a re-wire would silently slip through; an in-progress withdrawal batch would lose accumulated leaves. Now `WithMetrics` swaps the sink in-place via new `Processor.WithMetrics(IL2Metrics)` setters, preserving every piece of state.
- **2 new tests**: replay-deposit-after-rewire still rejects + emits to the new sink; same for withdrawal duplicate-nonce.

Cumulative: 333 tests / 27 projects.

### Fixed — `/readyz` returns 503 when the readiness predicate throws

- A readiness predicate that threw (e.g. checking a missing dependency) propagated the exception out of `Handle`, dropped the connection, and gave the scraper a TCP-level error rather than a clean 503. Predicate is wrapped in try/catch now; any throw produces 503 with body `predicate threw\n`. Operators chase the underlying exception in their logs, not the HTTP response.
- **1 new test** asserts a throwing predicate produces 503.

Cumulative: 331 tests / 27 projects.

### Fixed — `MetricsHttpServer` slow-client deadline

- `NetworkStream.ReadTimeout` doesn't apply to async reads, so a client that connected but never sent a request line could pin a worker thread indefinitely (slow-loris-style). Each connection now runs under a `CancellationTokenSource` linked to a 5-second deadline + the server's shutdown token. Both the read and write paths receive the linked token so they cancel together.
- **1 new test** opens a slow-client TCP connection and never sends; verifies the server stays responsive to a parallel real scrape.

Cumulative: 330 tests / 27 projects.

### Fixed — `L2MetricsPlugin.Start` is now thread-safe

- `Start` was idempotent under serial calls but had a race window between the `_server is null` check and the assignment. Two threads calling `Start` concurrently could both observe `_server == null` and bind two servers, leaking one. Now guarded by a `Lock _startGate` so only the first call binds.
- **1 new test** spawns 8 threads that all call `Start(portOverride: 0)` past a `Barrier`; asserts only one server is bound and the port stays stable across a follow-up `Start` call.

Cumulative: 329 tests / 27 projects.

### Docs — CONTRIBUTING.md test count + new wire-format guidance

- Updated stale "162 tests" → 328 in the Quick Start.
- Added a new "Adding a new wire format" section that codifies the byte-layout-table-in-docs + byte-layout-test pattern established by iter 47 / 58–60. Closes the contributor onboarding gap where someone introducing a new canonical encoding wouldn't know the convention.

### Fixed — `PrometheusExporter` escapes label values per spec

- Label values containing `"`, `\`, or newline were emitted verbatim, producing malformed Prometheus exposition that would break a real scraper. No current `MetricNames` emit those characters in tags, so this is defensive — but if anyone ever adds e.g. a `("user_agent", request.UserAgent)` tag, the exporter is now safe.
- Escapes per the Prometheus exposition format: `\` → `\\`, `"` → `\"`, newline → `\n`.
- **3 new tests** cover each escape case + the regression where a raw newline in a label used to make `line2` look like the start of a new metric line to the parser.

Cumulative: 328 tests / 27 projects.

### Fixed — `L2MetricsPlugin` accepts hostnames in BindAddress (e.g. `localhost`)

- `Start` previously crashed if `BindAddress` wasn't a numeric IP — `IPAddress.TryParse` rejects hostnames. A reasonable operator config like `"BindAddress": "localhost"` would throw `InvalidOperationException` at startup. Now falls back to `Dns.GetHostAddresses` for hostnames; raises a clear error only if DNS resolution fails or returns zero addresses.
- New public helper `L2MetricsPlugin.ResolveBindAddress(string)` so the resolution logic is directly testable + reusable.
- **5 new tests**: numeric IPv4, IPv6, any-address (`0.0.0.0`), `localhost` regression, bogus hostname → throw.

Cumulative: 325 tests / 27 projects.

### Fixed — `L2MetricsPlugin.Start(portOverride)` removes test fragility

- Tests for `L2MetricsPlugin` and `UT_E2E_L2MetricsPlugin_CompositionRoot` previously fell back to `Assert.Inconclusive` when port 9090 was in use on the test machine — fragile on shared CI runners. `Start` now accepts an optional `portOverride` parameter (`0` = "any free port") so tests bind deterministically. Production callers leave it null and let the JSON config drive.
- Removed 4 `Assert.Inconclusive` paths across 5 tests; all now run unconditionally.

Cumulative: 320 tests / 27 projects (no count change; tests are just now reliable).

### Docs — Refresh README counts to match current state

- README's "What ships" section was stale at 194 tests / 19 test projects / 11 off-chain libs / 7 plugins. Now: 320 tests / 21 test projects / 15 off-chain libs / 8 plugins, with the new bullets calling out the production-grade telemetry stack (Prometheus + `/metrics` + `/healthz` + `/readyz`) and that every canonical wire format has a byte-layout test.
- Quick-start gets a `--metrics-port 9090` example for the live HTTP scrape.

### Docs — Walk #4 covers telemetry in `docs/architecture-walkthrough.md`

- Added a "Walk #4: telemetry — emit, snapshot, scrape" section with an ASCII diagram of the metrics pipeline + the catalog of every component → metric family mapping. Cross-references `docs/telemetry.md` for the operator detail.
- Added a "Cross-cutting / Telemetry" row to the doc.md→code mapping table so a contributor scanning the table for the observability path finds it.
- No code changes; 320 tests / 27 projects.

### Added — `BatchSerializer` byte layouts in XML docs + tests

- `BatchSerializer`'s XML doc now includes full offset tables for both `L2BatchCommitment` (321 + proofLen bytes) and `PublicInputs` (332 bytes). This is THE format `NeoHub.SettlementManager` reads on-chain — having the layout in the doc means a contract author parsing the bytes doesn't need to read the encoder source.
- **2 new tests** in `Neo.L2.Batch.UnitTests` pin every documented offset.

Cumulative: 320 tests / 27 projects.

### Added — Documented byte layouts for `OptimisticProofPayload` + `RiscVProofPayload`

- Both payload types previously had `<remarks>See doc.md §X</remarks>` but no actual byte layout written down. A contract author parsing them off the wire had to read the source to know offsets. Layouts now spelled out as offset/size tables matching the format used in other canonical encoders.
- **2 new tests** in `Neo.L2.Proving.UnitTests` pin every byte range so future encoder reorders fail the build.

Cumulative: 318 tests / 27 projects.

### Fixed — `FraudProofPayload` doc-comment layout matches the encoder

- The XML doc-comment listed fields in a different order than `Encode` actually produced. Real layout (101 bytes, all little-endian): version (1B) + preStateRoot (32B) + claimedPostStateRoot (32B) + replayedPostStateRoot (32B) + disputedTxIndex (uint32, 4B). Doc updated and a new byte-layout test pins the offsets so future reorders fail the build.
- **1 new test** in `Neo.L2.Challenge.UnitTests` asserts each byte range matches the documented offsets.

Cumulative: 316 tests / 27 projects.

### Added — `ChallengeOrchestrator.InspectWithBisectionAsync`

- New overload that takes per-tx checkpoint sequences from both parties, runs `BisectionGame` internally, and emits a `FraudProofPayload` with `DisputedTxIndex` set to the single narrowed tx index. Pulls the bisection step inside the orchestrator so the caller doesn't have to wire it manually.
- Returns `null` when checkpoints agree at the final index (no fraud). Otherwise emits `l2.challenge.fraud_proofs` (counter) and `l2.challenge.bisection_rounds` (histogram via `BisectionGame`).
- **3 new tests**: agreement returns null, log-N narrowing produces the right disputed index region, arg validation matches `InspectAsync`.

Cumulative: 315 tests / 27 projects.

### Added — `ChainAuditor` self-emits audit metrics

- **`ChainAuditor`** accepts an optional `IL2Metrics` constructor parameter and emits `l2.audit.runs` (counter, +1 per `AuditAsync` call) and `l2.audit.failures` (counter, delta = number of failed findings — not 1 per failed audit) automatically. Devnet's manual emission of these metrics is removed; the auditor handles it now.
- **`NoZeroProofCheck`** registered in the devnet's auditor pipeline alongside `Continuity` + `ProofValidity`.
- **4 new tests** in `Neo.L2.Audit.UnitTests`: passing-audit increments runs only, failing-audit increments runs + failures by failed-finding count, repeated audits accumulate, NoOp default safety.

Cumulative: 312 tests / 27 projects.

### Added — `L2Outbox` messaging telemetry

- **`L2Outbox`** emits `l2.messaging.emitted` (counter) on every `Add`. Optional `IL2Metrics` constructor param. The metric was declared in iter 33's `MetricNames` but never emitted by any component.
- **2 new tests** in `Neo.L2.Messaging.UnitTests`: counter increments across L1 + L2 destinations, NoOp default safety.

Cumulative: 308 tests / 27 projects.

### Added — Sequencer registry telemetry

- **`InMemorySequencerCommitteeProvider`** emits `l2.sequencer.registered` (counter) on Register, `l2.sequencer.exits_started` (counter) on BeginExit, `l2.sequencer.exits_finalized` (counter) on Finalize, and `l2.sequencer.committee_size` (gauge) on every Register / Finalize. Optional `IL2Metrics` constructor param. Lets operators alert on unexpected committee shrinkage or rapid churn.
- **4 new `MetricNames`** constants + matching catalog entries.
- **4 new tests**: counter+gauge on Register, exits_started on BeginExit (size unchanged), exits_finalized + size decremented on Finalize, NoOp default safety.

Cumulative: 306 tests / 27 projects.

### Added — Forced-inclusion / censorship / challenge telemetry

The four `MetricNames` constants for these subsystems were declared in iter 33 but not actually emitted. Closing that gap:

- **`InMemoryForcedInclusionSource`** emits `l2.forced_inclusion.observed` on every `Enqueue`. Optional `IL2Metrics` constructor param.
- **`CensorshipDetector`** emits `l2.censorship.reports` (incremented by report count) when `DetectOverdueAsync` returns a non-empty list. Optional `IL2Metrics` constructor param.
- **`ChallengeOrchestrator`** emits `l2.challenge.fraud_proofs` when `InspectAsync` returns a non-null payload. Optional `IL2Metrics` constructor param.
- **`BisectionGame`** records `l2.challenge.bisection_rounds` (histogram) when the game settles, value = number of rounds taken. Optional `IL2Metrics` constructor param.
- **6 new tests** across the three lib test projects.

Cumulative: 302 tests / 27 projects. Every metric in `MetricCatalog` now has at least one emitter in source.

### Added — Misc polish

- **`Neo.Plugins.L2Metrics/config.json`** — config template so operators can drop the plugin into a Neo node and have it work. Mirrors the file shape every other L2 plugin uses (`PluginConfiguration` block).
- **`MerkleProof.Verify(root)`** — instance-method convenience; delegates to the existing `MerkleTree.Verify(proof, root)`. Lets call sites read `proof.Verify(root)` without the static dispatch boilerplate.

Cumulative: 296 tests / 27 projects.

### Added — Composition-root integration test

- **`UT_E2E_L2MetricsPlugin_CompositionRoot`** — wires every instrumented component (`BatchSealer`, `MetricsEmittingDAWriter`, `DepositProcessor`, `WithdrawalProcessor`, `BinaryTreeAggregator`, `L2RpcMethods`) to one shared sink hosted by `L2MetricsPlugin`, drives activity, scrapes `/metrics` through the plugin's HTTP server, and asserts every component's metric family is present in the response. Locks in that the composition root in `docs/telemetry.md` actually works end-to-end as advertised.

Cumulative: 295 tests / 27 projects.

### Added — `Neo.Plugins.L2Metrics` composition root

- New plugin **`L2MetricsPlugin`** owns the shared `InMemoryMetrics` sink the rest of the L2 plugin set wires its `WithMetrics()` calls to, and stands up the `MetricsHttpServer` based on settings (BindAddress, Port, Enabled). Pulls everything together — operators register this plugin first, then call `plugin.Metrics` from each other plugin's `WithMetrics()`.
- **Optional readiness predicate** via `WithReadinessCheck(Func<bool>)` — gates `/readyz` 200 vs 503.
- **Idempotent `Start()`** — extra calls are no-ops, simplifying host startup.
- **`L2MetricsSettings`** — `Enabled` (kill switch), `BindAddress` (default `127.0.0.1`), `Port` (default 9090, use 0 for any free port). Loaded from the standard plugin `config.json` `PluginConfiguration` section.
- **7 new tests** in `Neo.Plugins.L2Metrics.UnitTests`: bound-port-zero-before-Start, default-settings, idempotent Start, real HTTP scrape with emitted counter, readiness predicate gating 200 ↔ 503, null-arg validation.

Cumulative: 294 tests / 27 projects.

### Added — Devnet `--metrics-port` flag (live HTTP demo)

- `neo-l2-devnet <N> --metrics-port <P>` (or `--metrics-port 0` for "any free port") now stands up a real `MetricsHttpServer` after the batch run, self-scrapes `/metrics`, `/healthz`, and `/readyz` over real HTTP, and prints the round-trip status + content-type + body summary. Promotes the previously static "Prometheus text format" devnet section to a live demonstration of the production scrape path.

Cumulative: 287 tests / 26 projects (no new tests; the e2e telemetry integration test already covers this code path).

### Added — `NoZeroProofCheck` audit

- New `IAuditCheck` implementation flags batches that were soft-sealed but never had a real proof attached: `ProofType.None`, or non-`None` discriminator paired with empty `Proof` bytes. Cheap and fast — does not re-verify the proof (that's `ProofValidityCheck`'s job), it just catches the "soft-sealed but never proved" failure mode that would otherwise need full verification cost to detect.
- 5 new tests in `Neo.L2.Audit.UnitTests`: all-proved happy path, `ProofType.None`, empty proof bytes, multiple failures all reported, empty batch list.

Cumulative: 287 tests / 26 projects.

### Added — Canonical `MerkleProof` wire format

- **`MerkleProofSerializer`** (`Neo.L2.State`) — fixed-layout encoding of `MerkleProof` consumed by L1 NeoHub.SharedBridge for withdrawal verification. Closes a real gap: prior to this, off-chain code could `MerkleTree.GetProof` + `Verify`, but there was no canonical byte format for sending a proof across the off-chain ↔ on-chain boundary.
- Layout (48 + 32 × siblingCount bytes, all little-endian):
  - 0 .. 32  — Leaf hash
  - 32 .. 36 — LeafIndex (uint32)
  - 36 .. 44 — PathBitmap (uint64)
  - 44 .. 48 — SiblingCount (uint32)
  - 48 ..    — Siblings, 32 bytes each, leaf-to-root order
- **`MaxDepth = 64`** matches `MerkleTree.Verify`'s existing depth limit.
- **10 new tests**: round-trip 4-leaf, depth-0 single-leaf (header-only), exact byte-layout assertion, truncated-header rejection, truncated-siblings rejection, extra-trailing-bytes rejection, oversized-depth-on-encode rejection, header-claims-too-many-siblings rejection, null-arg, all-positions in 7-leaf tree round-trip.
- Listed in `AGENTS.md` "Canonical encodings" so future contributors don't reinvent.

Cumulative: 282 tests / 26 projects.

### Added — `/healthz` + `/readyz` endpoints

- **`MetricsRequestHandler`** now answers `/healthz` (always 200) and `/readyz` (200 or 503 based on optional predicate) in addition to `/metrics`. Standard Kubernetes-style liveness / readiness probes for load-balancer integration without bringing in an additional HTTP framework.
- **`MetricsRequestHandler` constructor** gets an optional `Func<bool>? readinessCheck` parameter. When unwired, `/readyz` always returns 200; when wired, the predicate is evaluated on every scrape.
- **6 new tests** covering: `/healthz` always-200, `/readyz` no-predicate-200, predicate-true-200, predicate-false-503, predicate evaluated per-request, trailing-slash + query-string tolerance on `/healthz`.
- `docs/telemetry.md` gets an "Endpoints" table and a `/readyz` predicate example.

Cumulative: 272 tests / 26 projects.

### Added — Gateway aggregation telemetry

- **`BinaryTreeAggregator`** accepts an optional `IL2Metrics` constructor parameter (default `NoOpMetrics`). On every successful `Aggregate()` it emits `l2.gateway.aggregations` (counter), `l2.gateway.batches_aggregated` (counter, +N constituents), `l2.gateway.aggregation_rounds` (histogram = tree depth), and `l2.gateway.aggregation_latency_ms` (histogram). Empty-pending case emits nothing.
- **4 new `MetricNames`** + matching catalog entries.
- **5 new tests** in `Neo.Plugins.L2Gateway.UnitTests`: 4-batch case verifies rounds = log2(4) = 2; 1-batch case verifies rounds = 0; empty case verifies no emission; repeated aggregations verify accumulation; default NoOp safety. **Last plugin without telemetry is now wired.**

Cumulative: 266 tests / 26 projects. Telemetry coverage matrix complete.

### Added — RPC telemetry

- **`L2RpcMethods`** wraps each of its 9 RPC methods through a private `Time` helper that emits `l2.rpc.calls` (counter) + `l2.rpc.latency_ms` (histogram) on success and `l2.rpc.failures` (counter) on exception, all tagged by `method` name (e.g. `getl2stateroot`, `getl2batch`). Optional `IL2Metrics` constructor parameter; default `NoOpMetrics`.
- **3 new `MetricNames`**: `RpcCalls`, `RpcLatencyMs`, `RpcFailures` + matching catalog entries.
- **4 new tests** in `Neo.Plugins.L2Rpc.UnitTests`: per-method tag isolation, repeated-call accumulation, foreign-chain rejection ↑ failure counter, no-metrics-default safety.

Cumulative: 261 tests / 26 projects.

### Added — Bridge processor telemetry

- **`DepositProcessor`** + **`WithdrawalProcessor`** now accept an optional `IL2Metrics` constructor parameter and emit:
  - `l2.bridge.deposits` (counter) on successful `Process`, `l2.bridge.deposits_rejected` on validation failure (replay, unknown asset, inactive mapping).
  - `l2.bridge.withdrawals` (counter) on successful `Stage`, `l2.bridge.withdrawals_rejected` on validation failure (unknown asset, duplicate nonce, non-positive amount).
- **`L2BridgePlugin.WithMetrics(IL2Metrics)`** — re-creates the processors with the new sink. Default is `NoOpMetrics`.
- **2 new `MetricNames`**: `DepositsRejected`, `WithdrawalsRejected` + matching catalog entries.
- **7 new tests** in `Neo.L2.Bridge.UnitTests`: success path, replay, unknown asset, withdrawal success, duplicate nonce, negative amount, default-NoOp safety. Closes the gap where bridge counters were only emitted manually in the devnet's inline path; production plugin path now emits them too.

Cumulative: 257 tests / 26 projects.

### Added — `MetricCatalog` (operator-facing HELP strings)

- **`MetricCatalog`** — single source of truth for the operator-facing description of every canonical metric. `GetHelp(name)` returns a sentence-form description; `IsKnown(name)` answers whether the catalog has an entry. `Descriptions` exposes the full map.
- **`PrometheusExporter`** now consults `MetricCatalog.GetHelp(baseName)` so HELP lines read e.g. `# HELP l2_batch_sealed_total Number of L2 batches sealed by the local sequencer` instead of the previous generic `L2 telemetry counter (l2.batch.sealed)`.
- **6 new tests** in `Neo.L2.Telemetry.UnitTests`: catalog-completeness check (reflects over `MetricNames` constants and asserts every one has an entry — guards against future drift), unknown-name fallback, expected-description spot-checks, null-arg validation, no-trailing-period convention check, end-to-end exporter integration.

Cumulative: 250 tests / 26 projects.

### Added — End-to-end telemetry integration test

- **`UT_E2E_Telemetry_Pipeline`** — drives the full telemetry pipeline (single shared `InMemoryMetrics` + `BatchSealer` + `MetricsEmittingDAWriter` + synthesized settlement/proving/bridge counters), stands up a `MetricsHttpServer` on a free port, scrapes `/metrics` over real HTTP, and asserts on the resulting Prometheus exposition. Covers both success path (4 batches → counters at 4) and failure path (DA write throws → `l2_da_publish_failures_total` incremented, success counter absent).
- Locks the metric contract end-to-end: every metric the production stack emits has a regression test.

Cumulative: 244 tests / 26 projects.

### Added — `/metrics` HTTP endpoint

- **`MetricsRequestHandler`** — framework-agnostic pure handler. Takes a request path, returns `MetricsHttpResponse` (status / content-type / body). Routes <c>/metrics</c> (with tolerance for trailing slash and query string) to a fresh `PrometheusExporter.Format(snapshot)`; everything else returns 404. Drop into any HTTP host (ASP.NET, Kestrel, RpcServer plugin) by routing GET <c>/metrics</c> through `Handle()`.
- **`IMetricsSource`** — read-side companion to `IL2Metrics`. `InMemoryMetrics` implements both. Decouples the snapshot read from the exporter so future sources (e.g. an OpenTelemetry-backed cache) plug in cleanly.
- **`MetricsHttpServer`** — minimal in-process HTTP server. Uses `TcpListener` + raw HTTP/1.0 framing instead of `HttpListener` (which is unreliable on Linux). Binds to a configurable IP/port (use port 0 for "any free port"; the resolved port is exposed via `Endpoint`). No third-party deps.
- **13 new tests**: 8 handler tests (status routing, query/trailing-slash tolerance, fresh-snapshot-per-call, null-arg validation, `IMetricsSource` round-trip), 5 server tests (real HTTP scrape, 404 on bad path, sequential requests, null-arg, port-zero binding). HttpClient explicitly bypasses ambient proxy env vars.

Cumulative: 242 tests / 26 projects.

### Added — Prometheus exporter

- **`MetricsSnapshot`** — frozen point-in-time read of every counter / gauge / histogram. Decouples accumulation (`IL2Metrics`) from export so future exporters (OpenTelemetry, StatsD, …) reuse the same shape.
- **`InMemoryMetrics.Snapshot()`** — produces a `MetricsSnapshot` immune to subsequent emissions.
- **`PrometheusExporter.Format(snapshot)`** — emits standards-compliant Prometheus exposition text: counters get `_total` suffix and `counter` type, gauges stay as-is with `gauge` type, histograms produce `_count` + `_sum` + `_max` aggregates with `summary` type. Tag pairs become labels with proper quoting; `.` and `-` in metric names sanitize to `_` per Prometheus naming rules. HELP/TYPE preambles emitted once per metric family.
- **`PrometheusExporter.ContentType`** — the canonical `text/plain; version=0.0.4; charset=utf-8` HTTP header value.
- **Devnet** now prints a `───── /metrics (Prometheus text format) ─────` section after each run, demonstrating the same data viewed through both the human summary and the production exporter.
- **10 new tests** in `Neo.L2.Telemetry.UnitTests`: empty-snapshot, counter, tagged counter, gauge, histogram, mixed kinds, content-type constant, name sanitization with dots+dashes, tagged histogram, snapshot frozenness.

Cumulative: 229 tests / 26 projects.

### Added — DA telemetry decorator

- **`MetricsEmittingDAWriter`** (`Neo.Plugins.L2DA`) — composition-pattern decorator that wraps any `IDAWriter` and emits `l2.da.published` (counter), `l2.da.publish_latency_ms` (histogram), and `l2.da.publish_failures` (counter), all tagged by `mode`. New writers automatically participate in the metric contract by being wrapped at plugin configure time.
- **`L2DAPlugin.WithMetrics(IL2Metrics)`** — wraps the chosen raw writer in the decorator. Idempotent: re-wiring a different metrics sink unwraps and re-wraps. NoOp metrics (the default) skip wrapping entirely.
- **6 new tests** in `Neo.Plugins.L2DA.UnitTests`: success path, propagating-throw failure path, accumulation across multiple publishes, `IsAvailableAsync` pass-through, null-arg validation, mode mirroring.
- **2 new `MetricNames`** entries: `DAPublishLatencyMs`, `DAPublishFailures`.

Cumulative: 219 tests / 26 projects.

### Refactor — extract testable `BatchSealer`

- **`BatchSealer`** (`Neo.Plugins.L2Batch`) — pure batch-accumulation state machine extracted from `L2BatchPlugin`. Owns `BatchBuilder` lifecycle, the three seal triggers (block-count, tx-count, age), block-context construction, and metric emission. Takes an injectable `Func<long>` clock so age-based seal can be exercised without sleeping. The plugin shrinks to ~70 lines whose only job is forwarding `Blockchain.Committed` to the sealer.
- **`Neo.Plugins.L2Batch.UnitTests`** — 7 unit tests that drive `BatchSealer` directly: seal-on-block-count, seal-on-tx-count, seal-on-age (with fake clock), batch-number monotonicity across seals, builder reset post-seal, NoOp metrics default safety, gauge-replace semantics for `l2.batch.tx_count`. Locks down the sealer's contract so future plugin refactors can't silently break the seal triggers.

Cumulative: 213 tests / 26 projects.

### Added — Plugin telemetry wiring

- **`L2BatchPlugin.WithMetrics(IL2Metrics)`** — emits `l2.batch.sealed` (counter), `l2.batch.seal_latency_ms` (histogram), and `l2.batch.tx_count` (gauge) on every seal. Default sink is `NoOpMetrics.Instance` so the metric path is opt-in.
- **`L2SettlementPlugin.WithMetrics(IL2Metrics)`** — emits `l2.proving.generated{kind=…}` (counter), `l2.proving.latency_ms{kind=…}` (histogram), `l2.settlement.submitted` (counter), `l2.settlement.submit_latency_ms` (histogram), and `l2.settlement.submit_failures` (counter on exception). Failed submits re-queue at the head, exactly matching the prior retry semantics.
- **`L2SettlementPlugin.Enqueue(L2BatchCommitment)`** — public hot-path entry that's also useful for backfilling missed batches after a node restart. Replaces the previously private event handler.
- **`Neo.Plugins.L2Settlement.UnitTests`** — 4 new unit tests exercising submit-success / submit-failure / default-NoOp paths with mocked `IL2Prover` + `ISettlementClient` + `InMemoryMetrics`. First plugin-level test project that drives an actual Plugin subclass.

Cumulative: 206 tests / 25 projects.

### Added — Auditor + devnet v0.3

- **`Neo.L2.Audit`** — chain auditor: pluggable `IAuditCheck`, built-in `ContinuityCheck` (sequential batch numbers + state-root linking + non-overlapping block ranges) and `ProofValidityCheck` (re-runs each batch's proof through `VerifierRegistry`). `ChainAuditor` aggregates findings into `AuditReport` with human-readable `Summarize()`. 9 unit tests.
- **Devnet v0.3** — after the per-batch loop, runs the full `ChainAuditor` pass (continuity + proof validity) and prints the `AuditReport.Summarize()` output. The devnet is now a complete end-to-end demonstration: state-root continuity, real multisig proofs, balance arithmetic, and an explicit auditor pass.
- **`UT_Mvp_AllPhases_FullStack`** integration test — single readable scenario that runs Phase-1 deploy planner → Phase-0/2 batch lifecycle with state continuity → Phase-3 BisectionGame → Phase-5 Gateway aggregation, all in one test.

Cumulative: 194 tests / 23 projects.

### Added — Phase 3 completion (optimistic challenge window + bisection)

- **`NeoHub.OptimisticChallenge`** L1 contract — accepts fraud proofs against pending `Challengeable` batches; on accepted challenge, reads sequencer's full bond, splits per `ChallengerRewardBps` (default 50%), pays challenger via `SequencerBond.Slash`, treasures the rest, and calls `SettlementManager.RevertBatch`. `FinalizeIfPastWindow` for unchallenged batches. Owner-gated `SetWindowSeconds` (60s..7d).
- **`Neo.L2.Challenge.FraudProofPayload`** — 101-byte canonical wire format (1B version + 32B preStateRoot + 32B claimedPostStateRoot + 32B replayedPostStateRoot + 4B disputedTxIndex).
- **`Neo.L2.Challenge.ChallengeOrchestrator`** — pluggable `IFraudProofGenerator`; `InspectAsync` takes claimedCommitment + reconstructed inputs and emits a `FraudProofPayload` only when challenger's deterministic replay disagrees with the sequencer's claim.
- **`Neo.L2.Challenge.BisectionGame`** — pure state machine that converges challenger and sequencer to a single disputed tx index in `O(log N)` rounds (standard optimistic-rollup design). Makes on-chain fraud verification O(1) instead of O(N).
- **Phase-3 end-to-end integration test** (`UT_Mvp_Phase3_OptimisticChallenge`) demonstrates: 8-tx batch with `KeyedStateStore`-backed honest checkpoints → sequencer lies from index 5 → `ChallengeOrchestrator` detects → `BisectionGame` narrows to disputed tx index 4 in ≤ 3 rounds.

Phase 3 → ✅. Cumulative: 184 tests / 21 projects.

### Added — Phase 2 / 3 / 5 wave (real state, slashing, recursive aggregation)

- **`KeyedStateStore` + `KeyedStateRootOracle`** (`Neo.L2.Executor.State`) — replace the XOR-mix stub with a sorted-leaf Merkle root computed over `Hash256(4B keyLen || key || 4B valueLen || value)` leaves; deterministic + insert-order-independent. The devnet now runs with real state-root continuity (`postRoot[N] == preRoot[N+1]`).
- **`NeoHub.SequencerBond` + `NeoHub.SequencerRegistry`** — slashable bond escrow (`Deposit` / `Slash` / `Withdraw`) and per-chain dBFT pubkey registry (`Register` / `Unregister` / `Finalize` with exit window). `Register` gates on `SequencerBond.HasMinBond` via inter-contract call.
- **`Neo.L2.Sequencer`** — L2-side `ISequencerCommitteeProvider` + `InMemorySequencerCommitteeProvider`. Models the L1 contract semantics so L2 nodes can test their committee-aware code paths.
- **`Neo.L2.Censorship`** — off-chain `CensorshipDetector` that polls `IForcedInclusionSource` for overdue entries, uses `ISequencerCommitteeProvider` to identify the responsible signer, and emits `CensorshipReport[]` ready for `NeoHub.ForcedInclusion.ReportCensorship` + `NeoHub.SequencerBond.Slash`.
- **`BinaryTreeAggregator` + `IRoundProver`** (`Neo.Plugins.L2Gateway`) — log(N)-round pairwise reduction with pluggable round prover. Default `PassThroughRoundProver` (Hash256 + length-prefixed proof concat). Production swaps for SP1 Compress / Halo2 accumulator / Risc0 fold.
- **`NeoFsLikeDAWriter`** — content-addressed in-process DA writer with NeoFS object semantics (object id = SHA256(payload), per-chain container, 36-byte pointer = 4B chainId LE + 32B objectId).
- **`Neo.L2.Devnet` v0.2** — upgraded to use `KeyedStateRootOracle` and 3-member `InMemorySequencerCommitteeProvider`; verified end-to-end across 5 batches with Alice's balance arithmetic check.
- **Phase-2 full-stack integration test** (`UT_Mvp_Phase2_FullStack`) stitches `KeyedStateStore` continuity + sequencer committee + forced-inclusion + censorship detection + multi-chain `BinaryTreeAggregator` together.

Cumulative: 162 tests / 19 projects, all green.

### Added — Phase 0 / 1 / 2 substantial scaffolding

- **Off-chain libraries** (`src/Neo.L2.*`): `Abstractions`, `Batch`, `State`, `Bridge`, `Messaging`, `Proving`, `Executor`. 7 interfaces, 14 model records, deterministic batch executor + spec, Merkle tree matching Neo's `Hash256` convention, Stage 0 multisig prover (production-usable) + verifier, Stage 1 optimistic verifier, Stage 2 RISC-V mock backend.
- **neo-node plugins** (`src/Neo.Plugins.L2*`): `L2Batch`, `L2Settlement`, `L2Bridge`, `L2DA`, `L2Prover`, `L2Rpc`, `L2Gateway`. All extend `Neo.Plugins.Plugin` and type-check against the locally-vendored `neo-project/neo` master branch.
- **Smart contracts** (`contracts/`): 9 NeoHub L1 contracts (`ChainRegistry`, `SharedBridge`, `SettlementManager`, `VerifierRegistry`, `MessageRouter`, `TokenRegistry`, `DARegistry`, `GovernanceController`, `EmergencyManager`) and 6 L2 native contracts (`L2BridgeContract`, `L2MessageContract`, `L2BatchInfoContract`, `L2FeeContract`, `L2PaymasterContract`, `L2SystemConfigContract`). Compile via `Neo.SmartContract.Framework`.
- **Tooling** (`tools/`): `neo-stack` CLI (8 subcommands) and `neo-l2-devnet` runnable Phase 0 demo.
- **Tests**: 88 unit tests across 10 projects (incl. 1 MVP integration test that walks deposit → batch → prove → verify → withdraw end-to-end in-process).
- **Documentation**: `README.md`, `ARCHITECTURE.md` (English distillation of `doc.md`), `AGENTS.md` (agent guide), `IMPLEMENTATION_STATUS.md` (per-phase coverage matrix), `CHANGELOG.md` (this file). Each L2 module's interfaces are XML-doc'd so IDE tooltips trace back to `doc.md` section numbers.

### Architecture decisions locked in

- **Plugin-based extension over fork**: neo4 references `neo-project/neo` master via `ProjectReference` (offline-friendly). Every L2 capability is a separate `Plugin` subclass loaded at runtime, preserving upstream compatibility.
- **Deterministic batch executor as the proving boundary**: `SPEC.md` enumerates excluded surfaces (P2P, RPC, mempool, plugins, logging, wallet, on-disk DB) so the prover only commits to pure state-transition behavior.
- **Pluggable verifier registry**: `VerifierRegistry` dispatches by `ProofType`, mirroring NeoHub's L1 contract — the same wire-format moves from off-chain to on-chain unchanged.
- **Canonical encodings**: `L2BatchCommitment`, `PublicInputs`, `MessageHasher`, `DepositPayload` all serialize little-endian with deterministic byte layouts; the same encoding is what NeoHub's contracts decode.

### Added — Phase 1 / 4 acceleration

- **`NeoHub.ForcedInclusion` + `Neo.L2.ForcedInclusion`** (doc.md §15.4): anti-censorship primitive — L1 enqueue, L2 drain with deadline tracking, replay protection. 8 unit tests.
- **`Neo.L2.Settlement.Rpc`**: JSON-RPC 2.0 client over `HttpClient` + `Neo.Json` (no third-party deps). `RpcSettlementClient` implements `ISettlementClient` for read-only methods; submit-batch delegates to a caller-supplied signer. 6 unit tests with in-memory `HttpMessageHandler` mocks.
- **`Neo.Hub.Deploy`** (`neo-hub-deploy` CLI): declarative deploy planner with topological sort, `$step:<name>` placeholder resolution, cycle/unknown-dep detection, canonical 10-step NeoHub scaffold. 8 unit tests.
- **`bridge/neo-zkvm-bridge`** (Rust cdylib) + **`Neo.L2.Proving.Sp1`** (C#): Phase-4 SP1 FFI scaffold. Stable 4-symbol C ABI; default features = NOT_IMPLEMENTED so the C# side falls back to `MockRiscVProver`; `--features real-prover` links the actual `neo-zkvm-prover` crate. 6 unit tests.
- **Phase-1 cross-component integration test** that walks: deploy-planner topological resolve → forced-inclusion enqueue/drain → SP1 fallback prover → multi-chain Gateway aggregation. 5 new tests.

### Out of MVP scope (still deferred)

- **Live L1 signer for `RpcSettlementClient.SubmitBatchAsync`** — interface in place; concrete wallet integration is operator-specific.
- **One-shot deploy runner** — `Neo.Hub.Deploy` emits the bundle JSON; the consumer (signer + chain bookkeeper) lives outside this repo.
- **`nccs` artifact generation** — `Directory.Build.props` calls `nccs` with `ContinueOnError=true`; users install nccs separately.
- **RpcServer plugin integration partial** that registers `L2RpcMethods` as `[RpcMethod]`-attributed entry points (needs neo's RpcServer plugin source).
- **Real SP1 prover linkage** — flip `--features real-prover` on the bridge crate to enable.
- **Phase 5 recursive proof aggregation** — `PassThroughAggregator` is a non-ZK reference impl.
- **Forced-inclusion bond/slashing** — contract emits the report event; actual sequencer slashing depends on `SettlementManager` integration.
- **NeoFS DA writer** — stub class throws; production wires NeoFS client.
- **dBFT sequencer-committee selection per Neo Elastic** (doc.md §7.1) — defaults to neo's existing `DBFTPlugin` consensus.
