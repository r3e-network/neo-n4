# Architecture Walkthrough

> Narrative tour through the codebase, mapping `doc.md` sections to actual files.
> If you only read one architecture doc, this is the one.

## Layered diagram

<p align="center">
  <img src="figures/architecture.svg" alt="Neo Elastic Network architecture: L1 NeoHub anchor, optional Phase 5 Neo Gateway, and N elastic L2 chains" width="900">
</p>

## Walk #1: a transaction's life on an L2 chain

<p align="center">
  <img src="figures/tx-lifecycle.svg" alt="Transaction lifecycle: nine stages from user submission, through dBFT sequencing, batch execution, sealing, proving, submission, L1 verification, finalization, and eventual withdrawal claim" width="900">
</p>

This is the hot path. The user submits a transaction; we follow it through every
component until the L1 commit lands.

### 1. User submits transaction (off-chain)

The user signs a tx and pushes it to the L2 mempool over RPC. Standard Neo flow — neo's
RpcServer plugin handles it, no neo4 code in this step.

### 2. dBFT sequencer (committee mode)

`Neo.Plugins.DBFTPlugin` (upstream) selects the next block proposer. **Who is allowed to
propose** is governed by `NeoHub.SequencerRegistry` on L1; the L2 node pulls the active set
via `Neo.L2.Sequencer.ISequencerCommitteeProvider` (production wires the L1-RPC-backed
implementation; tests use `InMemorySequencerCommitteeProvider`).

### 3. Block commit hook

`Neo.Plugins.L2Batch.L2BatchPlugin` subscribes to `Neo.Ledger.Blockchain.Committed`. Each
committed block is appended to a `Neo.L2.Batch.BatchBuilder`; the plugin seals when any
threshold trips: `MaxBlocksPerBatch`, `MaxTransactionsPerBatch`, `MaxBatchAgeMillis`.

### 4. Batch executor + state evolution

`Neo.L2.Executor.ReferenceBatchExecutor.ApplyBatchAsync`:
- Applies L1 inbox messages first (deposits, etc.) via `IL1MessageProcessor`.
- Iterates ordered transactions through `ITransactionExecutor`.
- Computes `txRoot`, `receiptRoot`, `withdrawalRoot`, `l2ToL1MessageRoot`,
  `l2ToL2MessageRoot` via `Neo.L2.State.MerkleTree.ComputeRoot`.
- Resolves `postStateRoot` via `IPostStateRootOracle`. The shipping
  `KeyedStateRootOracle` returns a real Merkle root over the sorted
  `(asset, holder) → balance` entries the batch produced.

The proving boundary is documented in [`src/Neo.L2.Executor/SPEC.md`](../src/Neo.L2.Executor/SPEC.md).
Anything outside that contract (P2P, RPC, mempool, plugins, logging, wallet, on-disk DB)
is NOT proven.

### 5. Sealing into `L2BatchCommitment`

`Neo.L2.Batch.BatchBuilder.Seal` packs the `BatchExecutionResult` plus the proof bytes
into an `L2BatchCommitment` record. `Neo.L2.Batch.BatchSerializer.Encode` produces the
canonical 317-byte fixed prefix + variable proof bytes that `NeoHub.SettlementManager`
will decode (the byte format is documented in `BatchSerializer`'s XML doc).

### 6. Proving (multisig today, ZK in Phase 4)

`Neo.Plugins.L2Settlement.L2SettlementPlugin.OnBatchSealed` picks up the sealed batch and
hands it to the configured `IL2Prover`:

- **Stage 0 (default):** `Neo.L2.Proving.Attestation.AttestationProver` collects validator
  signatures over `BatchSerializer.EncodePublicInputs(...)`. Production-usable today.
- **Stage 1 (challenge window):** `OptimisticProofPayload` carries a sequencer signature
  + bond reference. Verifier accepts immediately; the actual challenge logic lives in
  `NeoHub.SettlementManager`.
- **Stage 2 (ZK):** `Neo.L2.Proving.Sp1.Sp1RiscVProver` calls `bridge/neo-zkvm-bridge`
  via P/Invoke. Falls back to `MockRiscVProver` when the native lib isn't present.

The prover output goes into `L2BatchCommitment.Proof` with the matching `ProofType`.

### 7. Submission to L1

`Neo.L2.Settlement.Rpc.RpcSettlementClient.SubmitBatchAsync` encodes the commitment and
delegates to the operator-supplied `SignAndSendAsync` to build + sign + post a
`NeoHub.SettlementManager.SubmitBatch(commitmentBytes)` transaction.

### 8. L1 verification + finalization

`NeoHub.SettlementManager.SubmitBatch`:
- Decodes the commitment (matches `BatchSerializer` byte layout).
- Confirms the chain is registered + active via `ChainRegistry.IsActive`.
- Enforces sequential batch numbers.
- Dispatches verification to `VerifierRegistry.VerifyCommitment`, which inspects the
  `proofType` byte and routes to the right verifier (multisig / optimistic / zk).
- Captures `withdrawalRoot` so `SharedBridge.FinalizeWithdrawal` can later prove
  individual user withdrawals.
- Marks the batch `Pending`.

`NeoHub.SettlementManager.FinalizeBatch` later moves the batch to `Finalized`, sets the
canonical state root, and bumps `latestFinalizedBatch[chainId]`.

### 9. Withdrawal claim (much later)

A user calls `NeoHub.SharedBridge.FinalizeWithdrawal(chainId, withdrawalLeafHash, asset,
recipient, amount)`. SharedBridge calls back into `SettlementManager.VerifyWithdrawalLeaf`
to check the leaf is in the most recently finalized batch's `withdrawalRoot`, then
releases the canonical asset.

## Walk #2: anti-censorship via forced inclusion

<p align="center">
  <img src="figures/forced-inclusion.svg" alt="Forced-inclusion + censorship slashing sequence: user enqueues forced tx on L1, L2 polls and drains, sequencer censors past deadline, CensorshipDetector observes overdue, operator reports, SequencerBond slashes" width="900">
</p>

`doc.md` §15.4 + §17 spell out the censorship-resistance design. Here's how it works:

### 1. User posts forced tx on L1

```text
NeoHub.ForcedInclusion.EnqueueForcedTransaction(chainId, encodedTx, txHash)
  → returns nonce; emits ForcedTxEnqueued event
  → records (sender, txHash, encodedTx, deadlineUnix=now+2h)
```

### 2. L2 batcher polls + drains

`Neo.L2.ForcedInclusion.IForcedInclusionSource.DrainAsync` (production-backed by L1 RPC,
test-backed by `InMemoryForcedInclusionSource`) returns nonce-ordered entries the batcher
must include in its next batch. After inclusion, `MarkConsumedAsync` removes them.

### 3. If sequencer censors past the deadline

`Neo.L2.Censorship.CensorshipDetector` polls the source; when `HasOverdueEntryAsync` returns
true, it identifies the responsible sequencer via `ISequencerCommitteeProvider` and emits
`CensorshipReport[]`.

### 4. Operator submits the report

The operator calls `NeoHub.ForcedInclusion.ReportCensorship(chainId, nonce, sequencerAddr)`.
Per the `_deploy` wiring, `ForcedInclusion` is registered as a slasher in
`SequencerBond`, so the contract calls `SequencerBond.Slash(chainId, sequencer, amount,
reporter)` directly. Bond is debited; reporter is paid (or the funds go to the treasury).

## Walk #3: multi-L2 proof aggregation (Phase 5)

<p align="center">
  <img src="figures/proof-aggregation.svg" alt="Multi-L2 proof aggregation: eight L2 batches reduced through three rounds of pairwise IRoundProver.Combine into a single AggregatedCommitment that NeoHub.SettlementManager verifies in one call" width="900">
</p>

`Neo.Plugins.L2Gateway.BinaryTreeAggregator` does log(N)-round pairwise reduction —
each round invokes the swappable `IRoundProver.Combine` on adjacent siblings.

`IRoundProver.Combine` is the swappable hot path:

- `PassThroughRoundProver` (default): Hash256 + length-prefixed proof concat.
- Production: `Sp1CompressRoundProver` / `Halo2AccumulatorProver` / `Risc0FoldRoundProver`
  — wraps the actual recursive ZK backend.

The `AggregatedCommitment` carries:
- All constituent `L2BatchCommitment`s.
- A `GlobalMessageRoot` over the L2→L2 message roots (used for L2-to-L2 inclusion proofs).
- The aggregated proof bytes.
- The `BackendId` so on-L1 verification can route correctly.

## Walk #4: telemetry — emit, snapshot, scrape

Cross-cutting observability layer. Every plugin that does meaningful work emits to a
shared `IL2Metrics` sink; one HTTP endpoint serves the result.

```text
plugins emit ──> IL2Metrics ──> InMemoryMetrics ──> Snapshot()
                                                      │
                                                      ▼
                                              PrometheusExporter
                                                      │
                                                      ▼
                                            MetricsRequestHandler
                                                      │  /metrics, /healthz, /readyz
                                                      ▼
                                              MetricsHttpServer
                                                      │
                                                      ▼
                                          GET http://node/metrics
```

The composition root is `Neo.Plugins.L2Metrics.L2MetricsPlugin`. Operators construct it
first, then wire each L2 plugin's `WithMetrics()` setter to `metricsPlugin.Metrics`. After
that, every counter / histogram / gauge is reachable via a single Prometheus scrape:

- `l2.batch.sealed/seal_latency_ms/tx_count` (`L2BatchPlugin` → `BatchSealer`)
- `l2.settlement.submitted/submit_latency_ms/submit_failures` + `l2.proving.generated/latency_ms` (`L2SettlementPlugin`)
- `l2.da.published/publish_latency_ms/publish_failures` (`MetricsEmittingDAWriter`, mode-tagged)
- `l2.bridge.deposits/withdrawals` + rejected variants (`Deposit/WithdrawalProcessor`)
- `l2.rpc.calls/latency_ms/failures` (`L2RpcMethods`, method-tagged)
- `l2.gateway.aggregations/aggregation_rounds/aggregation_latency_ms/batches_aggregated` (`BinaryTreeAggregator`)
- `l2.sequencer.{registered,exits_started,exits_finalized,committee_size}` (`InMemorySequencerCommitteeProvider`)
- `l2.forced_inclusion.observed`, `l2.censorship.reports`, `l2.challenge.{fraud_proofs,bisection_rounds}`
- `l2.audit.runs/failures` (`ChainAuditor`, intrinsic)
- `l2.messaging.emitted` (`L2Outbox`)

`MetricCatalog` holds an operator-facing description for each, which `PrometheusExporter`
embeds as `# HELP` lines so the exposition is self-documenting. A reflection-based
completeness test (`UT_MetricCatalog`) fails the build if a new `MetricNames` constant is
added without a catalog entry. A composition-root integration test
(`UT_E2E_L2MetricsPlugin_CompositionRoot`) drives every component above through one sink
and asserts each metric family appears in a real HTTP scrape.

For the full catalog and wiring example, see [`docs/telemetry.md`](./telemetry.md).

## Where each `doc.md` section lives in code

| `doc.md` § | What it specifies            | Code location                                                                  |
| ---------- | ---------------------------- | ------------------------------------------------------------------------------ |
| §3.2 ChainRegistry      | L2 admission registry         | `contracts/NeoHub.ChainRegistry/` + `Neo.L2.L2ChainConfig` model               |
| §3.2 SharedBridge       | Asset escrow                  | `contracts/NeoHub.SharedBridge/` + `Neo.L2.Bridge.*`                           |
| §3.2 SettlementManager  | Batch ↦ canonical state       | `contracts/NeoHub.SettlementManager/` + `Neo.L2.Settlement.Rpc`                |
| §3.2 VerifierRegistry   | Pluggable proof dispatch      | `contracts/NeoHub.VerifierRegistry/` + `Neo.L2.Proving.VerifierRegistry`       |
| §3.2 MessageRouter      | L1↔L2 / L2↔L2 messaging       | `contracts/NeoHub.MessageRouter/` + `Neo.L2.Messaging.*`                       |
| §3.2 TokenRegistry      | L1↔L2 asset mapping           | `contracts/NeoHub.TokenRegistry/` + `Neo.L2.AssetMapping`                      |
| §3.2 DARegistry         | DA commitment store           | `contracts/NeoHub.DARegistry/`                                                 |
| §3.2 GovernanceController | Council + timelocks         | `contracts/NeoHub.GovernanceController/`                                       |
| §3.2 EmergencyManager   | Pause + escape hatch          | `contracts/NeoHub.EmergencyManager/`                                           |
| §4 Neo Gateway          | Proof aggregation             | `Neo.Plugins.L2Gateway` (incl. `BinaryTreeAggregator` + `IRoundProver`)        |
| §5 L2 chain internals   | per-L2 plugin layout          | `Neo.Plugins.L2Batch / L2Settlement / L2Bridge / L2DA / L2Prover / L2Rpc`      |
| §7.1 Sequencer / dBFT   | Committee selection           | `contracts/NeoHub.SequencerRegistry/` + `Neo.L2.Sequencer`                     |
| §7.2 Batcher            | Block ↦ batch                 | `Neo.L2.Batch.BatchBuilder` + `Neo.Plugins.L2Batch.L2BatchPlugin`             |
| §7.3 StateRootGenerator | Per-batch roots               | `Neo.L2.State.*` + `Neo.L2.Executor.State.KeyedStateStore`                     |
| §7.4 DAWriter           | DA layer abstraction          | `Neo.L2.Abstractions.IDAWriter` + `Neo.Plugins.L2DA.*`                         |
| §7.5 ProverAdapter      | 3-stage proving               | `Neo.L2.Proving.Attestation / Optimistic / RiscVZk` + `Neo.L2.Proving.Sp1`     |
| §8 Proof system         | Proving spec                  | `src/Neo.L2.Executor/SPEC.md`                                                  |
| §9 Token / GAS model    | Bridged asset accounting      | `Neo.L2.Bridge.AssetRegistry` + `L2Native.L2BridgeContract`                    |
| §10 Neo Connect         | Cross-chain messaging         | `Neo.L2.Messaging.*` + `L2Native.L2MessageContract`                            |
| §11 Bridge              | SharedBridge design           | `contracts/NeoHub.SharedBridge/` + `Neo.L2.Bridge.*`                           |
| §12 Data Availability   | DA tiers                      | `Neo.L2.DAMode` + `Neo.Plugins.L2DA.*` (incl. `NeoFsLikeDAWriter`)             |
| §13 L2 native contracts | On-L2 system contracts        | `contracts/L2Native.*/` (6 contracts)                                          |
| §14.1 L2 RPC            | RPC method surface            | `Neo.Plugins.L2Rpc.L2RpcMethods` (9 methods)                                   |
| §14.2 neo-stack CLI     | Launch framework              | `tools/Neo.Stack.Cli/`                                                          |
| §15.1 Tx flow           | Hot path                       | This doc, Walk #1                                                              |
| §15.2 Deposit           | L1→L2                          | `Neo.L2.Bridge.DepositProcessor` + `L2Native.L2BridgeContract.ApplyDeposit`    |
| §15.3 Withdrawal        | L2→L1                          | `Neo.L2.Bridge.WithdrawalProcessor` + `NeoHub.SharedBridge.FinalizeWithdrawal` |
| §15.4 Forced inclusion  | Anti-censorship                | `contracts/NeoHub.ForcedInclusion/` + `Neo.L2.ForcedInclusion` + `Neo.L2.Censorship` |
| §15.5 Emergency exit    | Escape hatch                   | `contracts/NeoHub.EmergencyManager/`                                           |
| §16 Governance          | 3-layer governance             | `contracts/NeoHub.GovernanceController/` (Council, timelock, admission policy) |
| §17 Threat model        | Mitigations                    | Distributed across the codebase; bond + slashing live in `SequencerBond`       |
| §18 Phased rollout      | Phase 0–6 plan                 | `IMPLEMENTATION_STATUS.md` (per-phase status matrix)                           |
| §19 Module layout       | Recommended structure          | This repo's `src/`, `contracts/`, `tools/` layout                              |
| §20 MVP                 | Phase-0 success criteria       | `tests/Neo.L2.IntegrationTests/UT_Mvp_Phase0_Sidechain`                        |
| §22 Design tradeoffs    | Choices made                   | This doc + `ARCHITECTURE.md`                                                   |
| Cross-cutting           | Telemetry / observability      | `Neo.L2.Telemetry` + `Neo.Plugins.L2Metrics`; operator catalog in [`docs/telemetry.md`](./telemetry.md) |
