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

`Neo.Plugins.DBFTPlugin` selects the next block proposer through
`NativeContract.NEO.GetNextBlockValidators`. **Who is allowed to propose** is governed by
`NeoHub.SequencerRegistry` on L1: an `ISequencerCommitteeProvider` computes the intended active
set, `neo-stack` commits it through a governed L2 native transaction, and DBFT reads only the
finalized `L2SystemConfigContract` state.

### 3. Block commit hook

`Neo.Plugins.L2Batch.L2BatchPlugin` subscribes to `Neo.Ledger.Blockchain.Committed`. Each
committed block is appended to a `Neo.L2.Batch.BatchBuilder`; the plugin seals when any
threshold trips: `MaxBlocksPerBatch`, `MaxTransactionsPerBatch`, `MaxBatchAgeMillis`.

### 4. Batch executor + state evolution

`Neo.L2.Executor.ReferenceBatchExecutor.ApplyBatchAsync`:
- Applies L1 inbox messages first (deposits, etc.) via `IL1MessageProcessor`.
- Iterates ordered transactions through `ITransactionExecutor`.
- Commits each transaction through `ExecutionStateTransaction`: storage remains in a
  read-through overlay until `HALT`, canonical effect collection succeeds, and the
  backing store accepts the complete transition.
- Builds `CanonicalExecutionEffects` V1 once per successful transaction. Both the
  ApplicationEngine compatibility executor and the PolkaVM/RISC-V executor use this
  same object for receipt hashes and downstream notification effects.
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
canonical 321-byte fixed prefix + variable proof bytes that `NeoHub.SettlementManager`
will decode (the byte format is documented in `BatchSerializer`'s XML doc).

### 6. Proving (multisig today, ZK in Phase 4)

`Neo.Plugins.L2Settlement.L2SettlementPlugin.OnBatchSealed` picks up the sealed batch and
hands it to the configured `IL2Prover`:

- **Stage 0 (default):** `Neo.L2.Proving.Attestation.AttestationProver` collects validator
  signatures over `BatchSerializer.EncodePublicInputs(...)`. Production-usable today.
- **Stage 1 (challenge window):** `OptimisticProofPayload` carries the sequencer
  account, sequencer signature, and bond reference. The verifier checks the
  signature and key/account binding; `NeoHub.SettlementManager` marks the batch
  `Challengeable` and opens `NeoHub.OptimisticChallenge`.
- **Stage 2 (ZK):** `Sp1SettlementExecutionStack` binds one exact
  `Sp1StatefulNeoVmV1` semantic from execution through proof. The sequencer invokes the
  SHA-256-pinned host-native `neo-zkvm-executor`, validates canonical `NEO4EXR1`, and
  atomically commits its complete post-state. `bridge/neo-zkvm-host` then runs
  `prove-batch daemon --watch <queue-dir>` and re-executes the same
  `neo-execution-core` + vendored `neo-vm-rs` runtime inside the SP1 RISC-V guest.
  `external/neo-riscv-vm` and `Neo.L2.Executor.RiscV` remain the distinct PolkaVM
  `ChainMode.L2RiscV` execution profile; they are not silently treated as the same SP1
  semantic. A chain must use a matching proof profile before claiming validity for that
  path. `MockRiscVProver` remains in-process testing only.

The prover output goes into `L2BatchCommitment.Proof` with the matching `ProofType`.

### 7. Submission to L1

`Neo.L2.Settlement.Rpc.RpcSettlementClient.SubmitBatchAsync` encodes the commitment and
delegates to the operator-supplied `SignAndSendAsync` to build + sign + post a
`NeoHub.SettlementManager.SubmitBatch(commitmentBytes, l1MessageHash, blockContextHash)`
transaction. The `l1MessageHash` / `blockContextHash` arguments are required: they
feed the on-chain `publicInputHash` binding that the registered verifier checks.

### 8. L1 verification + finalization

`NeoHub.SettlementManager.SubmitBatch`:
- Decodes the commitment (matches `BatchSerializer` byte layout).
- Confirms the chain is registered + active via `ChainRegistry.IsActive`.
- Enforces sequential batch numbers.
- Dispatches verification to `VerifierRegistry.VerifyCommitment`, which inspects the
  `proofType` byte and routes to the right verifier (multisig / optimistic / zk).
- Captures `withdrawalRoot` so `SharedBridge.FinalizeWithdrawal` can later prove
  individual user withdrawals.
- Marks multisig/ZK batches `Pending`; marks optimistic batches `Challengeable`
  and opens the configured `OptimisticChallenge` window.

`NeoHub.SettlementManager.FinalizeBatch` later moves the batch to `Finalized`, sets the
canonical state root, and bumps `latestFinalizedBatch[chainId]`. Challengeable
optimistic batches can reach this path only through `OptimisticChallenge` after
the window expires.

During that window, legacy fraud payloads v1/v2/v3 remain advisory-only and fail
closed even with governance witness; state changes require an exact registered executable v4 profile.
Permissionless v4 is profile-scoped to one chain, verifier, executor semantic id,
and replay domain. The restricted verifier reads SettlementManager's stored
321-byte `Challengeable` header, executes one supported existing-key Counter
Increment transition, and only reverts/slashes when the derived root differs from
the committed post-state root. Multi-transaction and general NeoVM semantics fail
closed.

### 9. Withdrawal claim (much later)

A user calls `NeoHub.SharedBridge.FinalizeWithdrawal(chainId, withdrawalLeafHash,
emittingContract, l2Sender, l2Asset, withdrawalNonce, asset, recipient, amount)`.
SharedBridge recomputes the full withdrawal-leaf preimage from these fields and
verifies the L1↔L2 asset mapping, then calls back into
`SettlementManager.VerifyWithdrawalLeaf` to check the leaf is in the most recently
finalized batch's `withdrawalRoot`, then releases the canonical asset.

## Walk #2: anti-censorship via forced inclusion

<p align="center">
  <img src="figures/forced-inclusion.svg" alt="Forced-inclusion + censorship slashing sequence: user enqueues forced tx on L1, L2 polls and drains, sequencer censors past deadline, CensorshipDetector observes overdue, operator reports, SequencerBond slashes" width="900">
</p>

`doc.md` §15.4 + §17 spell out the censorship-resistance design. Here's how it works:

### 1. User posts forced tx on L1

<p align="center">
  <img src="figures/architecture/forced-inclusion-step1.svg" alt="Forced inclusion step 1: L1 user calls NeoHub.ForcedInclusion.EnqueueForcedTransaction(chainId, encodedTx, txHash). The contract returns a nonce, emits a ForcedTxEnqueued event, and records (sender, txHash, encodedTx, deadlineUnix=now+2h) in storage. The L2 batcher must include the forced tx within deadlineUnix or the operator can submit a censorship report and slash the sequencer's bond" width="900">
</p>

### 2. L2 batcher polls + drains

`Neo.L2.ForcedInclusion.IForcedInclusionSource.DrainAsync` (production-backed by L1 RPC,
test-backed by `InMemoryForcedInclusionSource`) returns nonce-ordered entries the batcher
must include in its next batch. After inclusion, `MarkConsumedAsync` removes them.

### 3. If sequencer censors past the deadline

`Neo.L2.Censorship.CensorshipDetector` polls the source; when `HasOverdueEntryAsync` returns
true, it emits `CensorshipReport[]`. Committee membership alone cannot identify the dBFT
proposer for a missed deadline. An optional `ICensorshipAttributionProvider` may attach an
identity only from finalized consensus evidence; otherwise the report carries the explicit
unknown sentinel rather than blaming the first committee member.

### 4. Operator submits the report

The operator calls `NeoHub.ForcedInclusion.ReportCensorship(chainId, nonce, sequencerAddr)`;
`UInt160.Zero` is valid when attribution is still unknown. The permissionless report records
the overdue event at most once and pauses the chain, but cannot slash. After reviewing
finalized dBFT evidence, governance separately calls `SlashReportedCensorship`; only that
owner-gated path invokes `SequencerBond.Slash` and debits the evidenced member's bond.

## Walk #3: multi-L2 proof aggregation (Phase 5)

<p align="center">
  <img src="figures/proof-aggregation.svg" alt="Multi-L2 proof aggregation: eight L2 batches reduced through three rounds of pairwise IRoundProver.Combine into a single AggregatedCommitment that NeoHub.SettlementManager verifies in one call" width="900">
</p>

`Neo.Plugins.L2Gateway.BinaryTreeAggregator` does log(N)-round pairwise reduction —
each round invokes the swappable `IRoundProver.Combine` on adjacent siblings.

`IRoundProver.Combine` is the swappable hot path:

- `PassThroughRoundProver`: reference Hash256 + length-prefixed proof concat
  (minimal cost; valid production choice for chains that don't need recursive ZK).
- `MultisigRoundProver`: Secp256r1 threshold-attested rounds (production, ships
  in `src/Neo.Plugins.L2Gateway/MultisigRoundProver.cs`).
- `MerklePathRoundProver`: per-constituent inclusion proofs against the aggregate
  root (production, ships in the same folder).
- `Sp1RecursiveRoundProver` fixes backend `0xC2`; the terminal recursive proof is bundled in
  `bridge/neo-zkvm-gateway-guest` and `bridge/neo-zkvm-gateway-host`. The guest verifies only
  compile-time-batch-VK SP1 compressed children whose public values are exactly
  `0x00 || batch.PublicInputHash`, then commits `0x00 || Hash256(binding170)`. Halo2/Risc0
  alternatives may still use the seam, but they are not the bundled production path.

The `AggregatedCommitment` carries:
- All constituent `L2BatchCommitment`s.
- A `GlobalMessageRoot` over the L2→L2 message roots (used for L2-to-L2 inclusion proofs).
- The aggregated proof bytes.
- The `BackendId` so on-L1 verification can route correctly.

Publication is intentionally not a direct Router write. The RPC publisher queries
`MessageRouter` only for idempotent reconciliation, then submits the aggregate plus a packed,
strictly ordered list of 12-byte `(chainId:uint32 LE,batchNumber:uint64 LE)` references to
`SettlementManager.PublishGatewayGlobalRoot`. SettlementManager requires every referenced batch
to remain finalized and Gateway-enabled, rebuilds the exact commitment and message roots from
its finalized records, and advances per-chain non-revertible watermarks before making the
same-transaction call to `MessageRouter.PublishGlobalRoot`. A Router fault rolls the watermarks
back; a successful publication prevents later batch reversion below the published watermark.

## Walk #4: telemetry — emit, snapshot, scrape

<p align="center">
  <img src="figures/telemetry-pipeline.svg" alt="Telemetry pipeline: plugins emit to IL2Metrics, snapshotted into a point-in-time copy, exported by PrometheusExporter, served by MetricsRequestHandler over MetricsHttpServer, scraped by Prometheus" width="900">
</p>

Cross-cutting observability layer. Every plugin that does meaningful work emits to a
shared `IL2Metrics` sink; one HTTP endpoint serves the result.

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

## Walk #5: durable state — IL2KeyValueStore + RocksDB by default

The Neo Elastic Network's "L2 component holds an in-memory dict" pattern is fine for
tests + devnets but unacceptable in production: a sequencer mid-exit losing its
ExitsAtUnixSeconds deadline on restart could re-admit a sequencer that should be in
cooldown, or fail to finalize an exit that already passed its window. Same
correctness risk for finalized message proofs, withdrawal proofs, consumed forced-
inclusion nonces, and DA payloads.

Solution: an explicit `IL2KeyValueStore` abstraction (`src/Neo.L2.Persistence/`) with two
implementations:

- `InMemoryKeyValueStore` — a `SortedDictionary<byte[], byte[]>` with
  `ByteArrayComparer.Lexicographic`. Devnet / test default.
- `RocksDbKeyValueStore` — backed by the `RocksDB` NuGet package (v10.10.1.649,
  namespace `RocksDbSharp`) with snappy compression. Production default.

Six L2 components take an `IL2KeyValueStore` ctor argument with a backwards-compatible
default ctor that wires `InMemoryKeyValueStore`:

| Component | What persists |
| --- | --- |
| `KeyedStateStore` | (asset, holder) → balance entries |
| `InMemoryL2RpcStore` | withdrawal + message proofs (other in-mem dicts rebuildable from L1) |
| `InMemoryMessageRouter` | finalized message proofs |
| `InMemoryForcedInclusionSource` | consumed nonce set |
| `InMemorySequencerCommitteeProvider` | committee membership + exit windows (write-through dict) |
| `PersistentDAWriter` | content-addressed batch payloads |

Devnet's `--data-dir <path>` flag wires four of these (state, RPC proofs, sequencer,
DA) under one root automatically — operators can see the persistence story end-to-end
in two commands:

```bash
dotnet run --project tools/Neo.L2.Devnet -- 5 --data-dir /tmp/devnet1
dotnet run --project tools/Neo.L2.Devnet -- 0 --data-dir /tmp/devnet1
# → committee + state + DA payloads all rehydrate
```

Tests pin both per-component reopen behavior and the combined-story integration test
(`UT_E2E_Persistence_FullStack`) so a refactor that accidentally collapses two stores
onto one directory or omits a component breaks the suite, not devnet-on-restart. See
[`docs/persistence.md`](./persistence.md) for the operator wiring recipes + per-component
"what breaks if X is lost" table.

## Walk #6: invariant audit — `ChainAuditor` + 6 checks

Settlement gives canonical batches; proofs cryptographically bind state transitions; DA
keeps payloads recoverable. But none of those individually answer the operator's
day-2 question: *"is the chain still well-formed?"* That's where `Neo.L2.Audit.ChainAuditor`
fits — it composes a sequence of `IAuditCheck` invariants and runs them over a batch
sequence on a periodic ops-side schedule. Failures bump `l2.audit.failures` for
dashboards; the report names every failed finding by check + batch.

Built-in checks (devnet wires all six):

| Check | What it catches |
| --- | --- |
| `ContinuityCheck` | inter-batch state-root continuity + monotonic batch numbers + non-overlapping block ranges |
| `NoZeroProofCheck` | "soft-sealed but never proved" batches — `ProofType.None` or empty proof bytes |
| `ProofValidityCheck` | the cryptographic verifier rejects the proof against its public inputs |
| `PublicInputHashConsistencyCheck` | the stored `PublicInputHash` doesn't match what the commitment fields would hash to (tampered submission) |
| `BatchRangeCheck` | intra-batch invariants: `firstBlock <= lastBlock`, `batchNumber >= 1` |
| `DAAvailabilityCheck` | the DA layer dropped the payload that the proof commitment binds to |

Each check returns one summary `AuditFinding` on success, one finding per failure
otherwise; the auditor catches buggy custom checks (`Exception` thrown from `RunAsync`)
and converts them to a failure finding so one bad check doesn't abort the whole pass.
Mixed-chainId batch lists are rejected with `ArgumentException` upstream of the
per-check pipeline.

The integration test `UT_E2E_AuditPipeline` exercises three scenarios end-to-end on
a real attestation-signed chain: healthy (all 6 pass + metric counts), `BatchRange`
violation (caught with right detail + counter increments), and DA-dropped (caught
specifically by `DAAvailabilityCheck` against a writer that never saw the payload).

## Where each `doc.md` section lives in code

- **§3.2 ChainRegistry** — L2 admission registry. `contracts/NeoHub.ChainRegistry/` + `Neo.L2.L2ChainConfig` model.
- **§3.2 SharedBridge** — Asset escrow. `contracts/NeoHub.SharedBridge/` + `Neo.L2.Bridge.*`.
- **§3.2 SettlementManager** — Batch ↦ canonical state. `contracts/NeoHub.SettlementManager/` + `Neo.L2.Settlement.Rpc`.
- **§3.2 VerifierRegistry** — Pluggable proof dispatch. `contracts/NeoHub.VerifierRegistry/` + `Neo.L2.Proving.VerifierRegistry`.
- **§3.2 MessageRouter** — L1↔L2 / L2↔L2 messaging. `contracts/NeoHub.MessageRouter/` + `Neo.L2.Messaging.*`.
- **§3.2 TokenRegistry** — L1↔L2 asset mapping. `contracts/NeoHub.TokenRegistry/` + `Neo.L2.AssetMapping`.
- **§3.2 DARegistry** — DA commitment store. `contracts/NeoHub.DARegistry/`.
- **§3.2 GovernanceController** — Council + timelocks. `contracts/NeoHub.GovernanceController/`.
- **§3.2 EmergencyManager** — Pause + escape hatch. `contracts/NeoHub.EmergencyManager/`.
- **§4 Neo Gateway** — Proof aggregation. `Neo.Plugins.L2Gateway` plus `bridge/neo-zkvm-gateway-{guest,host}` for the SP1 recursive terminal proof.
- **§5 L2 chain internals** — per-L2 plugin layout. `Neo.Plugins.L2Batch / L2Settlement / L2Bridge / L2DA / L2Prover / L2Rpc`.
- **§7.1 Sequencer / dBFT** — Committee selection. `contracts/NeoHub.SequencerRegistry/` + `Neo.L2.Sequencer`.
- **§7.2 Batcher** — Block ↦ batch. `Neo.L2.Batch.BatchBuilder` + `Neo.Plugins.L2Batch.L2BatchPlugin`.
- **§7.3 StateRootGenerator** — Per-batch roots. `Neo.L2.State.*` + `Neo.L2.Executor.State.KeyedStateStore`; production SP1 capture/atomic transition is `Sp1StateWitnessSource`.
- **§7.4 DAWriter** — DA layer abstraction. `Neo.L2.Abstractions.IDAWriter` + `Neo.Plugins.L2DA.*`.
- **§7.5 ProverAdapter** — 3-stage proving. `Neo.L2.Proving.Attestation / Optimistic / RiscVZk`, `Sp1SettlementExecutionStack`, host-native `neo-zkvm-executor`, and `bridge/neo-zkvm-host/` (out-of-process Stage-2).
- **§8 Proof system** — Proving spec. `src/Neo.L2.Executor/SPEC.md`.
- **§9 Token / GAS model** — Bridged asset accounting. `Neo.L2.Bridge.AssetRegistry` + Neo Core native `L2BridgeContract`.
- **§10 Neo Connect** — Cross-chain messaging. `Neo.L2.Messaging.*` + Neo Core native `L2MessageContract`.
- **§11 Bridge** — SharedBridge design. `contracts/NeoHub.SharedBridge/` + `Neo.L2.Bridge.*`.
- **§12 Data Availability** — DA tiers. `Neo.L2.DAMode` + `Neo.Plugins.L2DA.*` (incl. `NeoFsLikeDAWriter`).
- **§13 L2 native contracts** — On-L2 system contracts. `external/neo/src/Neo/SmartContract/Native/L2NativeContracts.cs` (10 native contracts).
- **§14.1 L2 RPC** — RPC method surface. `Neo.Plugins.L2Rpc.L2RpcMethods` (10 methods incl. `getsecuritylabel`).
- **§14.2 neo-stack CLI** — Launch framework. `tools/Neo.Stack.Cli/`.
- **§15.1 Tx flow** — Hot path. This doc, Walk #1.
- **§15.2 Deposit** — L1→L2. `Neo.L2.Bridge.DepositProcessor` + Neo Core native `L2BridgeContract.ApplyDeposit`.
- **§15.3 Withdrawal** — L2→L1. `Neo.L2.Bridge.WithdrawalProcessor` + `NeoHub.SharedBridge.FinalizeWithdrawal`.
- **§15.4 Forced inclusion** — Anti-censorship. `contracts/NeoHub.ForcedInclusion/` + `Neo.L2.ForcedInclusion` + `Neo.L2.Censorship`.
- **§15.5 Emergency exit** — Escape hatch. `contracts/NeoHub.EmergencyManager/`.
- **§16 Governance** — 3-layer governance. `contracts/NeoHub.GovernanceController/` (Council, timelock, admission policy).
- **§17 Threat model** — Mitigations. Distributed across the codebase; bond + slashing live in `SequencerBond`.
- **§18 Phased rollout** — Phase 0–6 plan. `IMPLEMENTATION_STATUS.md` (per-phase status matrix).
- **§19 Module layout** — Recommended structure. This repo's `src/`, `contracts/`, `tools/` layout.
- **§20 MVP** — Phase-0 success criteria. `tests/Neo.L2.IntegrationTests/UT_Mvp_Phase0_Sidechain`.
- **§22 Design tradeoffs** — Choices made. This doc + `ARCHITECTURE.md`.
- **Cross-cutting** — Telemetry / observability. `Neo.L2.Telemetry` + `Neo.Plugins.L2Metrics`; operator catalog in [`docs/telemetry.md`](./telemetry.md).
