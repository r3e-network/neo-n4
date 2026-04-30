# Implementation Status

> Snapshot of what's built in `neo4` against `doc.md`. Updated alongside meaningful PRs.

## Phase coverage (doc.md §18)

| Phase | Goal                                      | Status                                      |
| ----- | ----------------------------------------- | ------------------------------------------- |
| 0     | Sidechain PoC                             | ✅ MVP integration test passes              |
| 1     | NeoHub v0 + Shared Bridge                 | ✅ All 12 NeoHub contracts compile + deploy planner emits 10-step bundle |
| 2     | Batch Settlement                          | ✅ Off-chain green; real `KeyedStateStore` continuity verified across batches |
| 3     | Optimistic Challenge Window               | ✅ `OptimisticChallenge` contract + `ChallengeOrchestrator` + `BisectionGame` (log-N narrowing) all green |
| 4     | NeoVM2 / RISC-V ZK Validity Proof         | 🟡 SP1 FFI bridge scaffolded; flip `--features real-prover` to enable |
| 5     | Neo Gateway (proof aggregation)           | 🟡 `BinaryTreeAggregator` with pluggable `IRoundProver` (default = pass-through hash) |
| 6     | Neo Stack CLI / Templates                 | 🟡 8 subcommands scaffolded                 |

Legend: ✅ done, 🟡 substantial scaffolding + tests, 🔴 stub.

## Completed work — by code

### Off-chain libraries (`src/Neo.L2.*`)

| Project                   | Role                                                              |
| ------------------------- | ----------------------------------------------------------------- |
| `Neo.L2.Abstractions`     | 7 interfaces + 14 model records (doc.md §19)                      |
| `Neo.L2.Batch`            | `L2Batch`, `BatchBuilder`, deterministic `BatchSerializer`        |
| `Neo.L2.State`            | `MerkleTree` (matches Neo `Hash256`), `MessageHasher`, `WithdrawalTree`, `MessageTree`, `StateRootCalculator` |
| `Neo.L2.Bridge`           | `AssetRegistry`, `DepositPayload`, `DepositProcessor`, `WithdrawalProcessor` (both processors emit `l2.bridge.deposits/deposits_rejected/withdrawals/withdrawals_rejected` to a per-instance `IL2Metrics`) |
| `Neo.L2.Messaging`        | `MessageBuilder`, `L1MessageInbox`, `L2Outbox`, `InMemoryMessageRouter` |
| `Neo.L2.Proving`          | Stage 0 multisig (real), Stage 1 optimistic, Stage 2 mock RISC-V; `VerifierRegistry` |
| `Neo.L2.Proving.Sp1`      | Phase 4 SP1 P/Invoke wrapper with graceful fallback to mock when native bridge missing |
| `Neo.L2.Executor`         | `SPEC.md` + `Receipt`, pluggable `ITransactionExecutor` / `IPostStateRootOracle` / `IL1MessageProcessor`, `ReferenceBatchExecutor`, **`KeyedStateStore` + `KeyedStateRootOracle`** |
| `Neo.L2.ForcedInclusion`  | Anti-censorship `IForcedInclusionSource` + in-memory backend     |
| `Neo.L2.Sequencer`        | `ISequencerCommitteeProvider` + in-memory backend (Register / BeginExit / Finalize) |
| `Neo.L2.Censorship`       | `CensorshipDetector` — turns overdue forced-tx entries into `CensorshipReport[]` |
| `Neo.L2.Challenge`        | `FraudProofPayload` + `ChallengeOrchestrator` + `BisectionGame` for Phase-3 |
| `Neo.L2.Settlement.Rpc`   | JSON-RPC client + `RpcSettlementClient` for L1 read methods + signer-delegated submit |
| `Neo.L2.Audit`            | **End-to-end chain auditor: `ContinuityCheck` + `ProofValidityCheck` + `ChainAuditor`** |
| `Neo.L2.Telemetry`        | `IL2Metrics` (counter/histogram/gauge) + `NoOpMetrics` + `InMemoryMetrics` + `MetricsSnapshot` + `PrometheusExporter` + `MetricsRequestHandler` (framework-agnostic) + `MetricsHttpServer` (TcpListener-based, no third-party deps) + canonical `MetricNames` + **`MetricCatalog`** (operator-facing HELP descriptions) |

### Native FFI bridge (`bridge/`)

| Crate                | Role                                             |
| -------------------- | ------------------------------------------------ |
| `neo-zkvm-bridge`    | Rust cdylib with stable C ABI (`neo_zkvm_prove` / `_verify` / `_free_buffer` / `_abi_version`); optional `real-prover` feature links against `neo-zkvm-prover` |

### neo-node plugins (`src/Neo.Plugins.L2*`)

| Plugin                       | Role                                                  |
| ---------------------------- | ----------------------------------------------------- |
| `Neo.Plugins.L2Batch`        | Hooks `Blockchain.Committed`; seal logic lives on testable `BatchSealer`; emits `l2.batch.sealed/seal_latency_ms/tx_count` via `WithMetrics()` |
| `Neo.Plugins.L2Settlement`   | Wires prover + settlement client; signs sealed batches; **emits `l2.settlement.submitted/submit_failures/submit_latency_ms` + `l2.proving.generated/latency_ms` via `WithMetrics()`** |
| `Neo.Plugins.L2Bridge`       | Hosts `AssetRegistry` + processors                    |
| `Neo.Plugins.L2DA`           | Picks DA writer by `DAMode` config — `InMemoryDAWriter`, **`NeoFsLikeDAWriter`** (content-addressed), L1/External/DAC stubs; `WithMetrics()` wraps the chosen writer in `MetricsEmittingDAWriter` (mode-tagged `l2.da.published/publish_latency_ms/publish_failures`) |
| `Neo.Plugins.L2Prover`       | Hosts `IL2Prover` for the configured `ProofType`      |
| `Neo.Plugins.L2Rpc`          | 9 RPC handlers (doc.md §14.1) + `IL2RpcStore`         |
| `Neo.Plugins.L2Gateway`      | **`BinaryTreeAggregator`** with pluggable `IRoundProver` (default `PassThroughRoundProver`); `PassThroughAggregator` for flat aggregation |

### Smart contracts (`contracts/`) — 19 total, all type-check via devpack

**NeoHub L1 suite (13):**
`ChainRegistry` · `SharedBridge` · `SettlementManager` · `VerifierRegistry` · `MessageRouter` · `TokenRegistry` · `DARegistry` · `GovernanceController` · `EmergencyManager` · `ForcedInclusion` · `SequencerBond` · `SequencerRegistry` · **`OptimisticChallenge`**

**L2 native (6):**
`L2BridgeContract` · `L2MessageContract` · `L2BatchInfoContract` · `L2FeeContract` · `L2PaymasterContract` · `L2SystemConfigContract`

### Tools (`tools/`)

| Tool                  | Role                                                  |
| --------------------- | ----------------------------------------------------- |
| `Neo.Stack.Cli`       | `neo-stack` CLI: 8 subcommands                        |
| `Neo.L2.Devnet`       | `neo-l2-devnet <N>` — runs N batches end-to-end with real `KeyedStateStore` continuity + sequencer committee + **post-run `ChainAuditor` pass** |
| `Neo.Hub.Deploy`      | `neo-hub-deploy` — declarative L1 deploy planner: scaffold / plan / verify |

### Tests

**257 unit + integration tests across 26 projects:**

| Project                              | Tests | Coverage                                    |
| ------------------------------------ | ----- | ------------------------------------------- |
| `Neo.L2.Abstractions.UnitTests`      | 17    | enum discriminants, models, interface shape |
| `Neo.L2.Batch.UnitTests`             | 13    | builder lifecycle, serializer round-trip    |
| `Neo.L2.State.UnitTests`             | 12    | Merkle tree, proof verify, hashers          |
| `Neo.L2.Messaging.UnitTests`         | 7     | inbox FIFO, replay protection, outbox split |
| `Neo.L2.Bridge.UnitTests`            | 14    | registry, deposit replay, withdrawal staging, **metric emission on success/replay/unknown-asset/duplicate-nonce/negative-amount paths** |
| `Neo.L2.Proving.UnitTests`           | 11    | Stage 0/1/2 prove+verify, registry dispatch |
| `Neo.L2.Proving.Sp1.UnitTests`       | 6     | bridge unavailable, mock fallback, VK mismatch |
| `Neo.L2.Executor.UnitTests`          | 14    | empty/single/many, ordering, determinism, **KeyedStateStore + oracle** |
| `Neo.L2.ForcedInclusion.UnitTests`   | 8     | nonce ordering, replay, overdue detection   |
| `Neo.L2.Sequencer.UnitTests`         | 9     | register/exit/finalize lifecycle            |
| `Neo.L2.Censorship.UnitTests`        | 7     | overdue detection, sequencer attribution    |
| `Neo.L2.Challenge.UnitTests`         | 19    | fraud-proof payload, orchestrator, BisectionGame |
| `Neo.L2.Audit.UnitTests`             | 9     | **continuity + proof-validity checks, summary** |
| `Neo.Plugins.L2Rpc.UnitTests`        | 9     | all 9 RPC methods, foreign-chain rejection  |
| `Neo.Plugins.L2DA.UnitTests`         | 13    | InMemory + NeoFsLike DA writers + **MetricsEmittingDAWriter (success / throw / accumulate / passthrough)** |
| `Neo.Plugins.L2Gateway.UnitTests`    | 13    | flat + binary-tree aggregator, edge cases   |
| `Neo.Plugins.L2Batch.UnitTests`      | 7     | `BatchSealer` block / tx / age triggers, batch-number monotonicity, gauge replace, NoOp default |
| `Neo.Plugins.L2Settlement.UnitTests` | 4     | **metric emission on submit success / failure / no-op default** |
| `Neo.L2.Settlement.Rpc.UnitTests`    | 6     | JSON-RPC envelope, stack parsing, signer    |
| `Neo.L2.Telemetry.UnitTests`         | 37    | counter/histogram/gauge accumulation, tag canonicalization, Prometheus exporter (counter/gauge/summary, labels, name sanitization, frozen-snapshot), request handler routing, TCP server round-trip + multi-request, **catalog completeness vs MetricNames + Prometheus integration** |
| `Neo.Hub.Deploy.UnitTests`           | 8     | topo sort, cycle detection, scaffold        |
| `Neo.L2.IntegrationTests`            | 14    | Phase 0 MVP + Phase 1 cross-component + Phase 2 full-stack + Phase 3 optimistic-challenge + all-phases stitch + **e2e telemetry pipeline (BatchSealer + DA decorator + HTTP scrape)** |

## What's not yet wired (out of MVP scope)

- **Live L1 signer for `RpcSettlementClient.SubmitBatchAsync`** — interface in place; concrete wallet integration is operator-specific.
- **`nccs` artifact generation** — `Directory.Build.props` calls `nccs` with `ContinueOnError=true`; users install nccs separately.
- **RpcServer plugin integration partial** — `L2RpcMethods` callable as plain methods; the `[RpcMethod]`-attributed wrapper for neo's `RpcServer` plugin needs the RpcServer source.
- **Real SP1 prover linkage** — flip `--features real-prover` on the bridge crate to enable.
- **Real recursive ZK round prover** — `BinaryTreeAggregator` has the right shape; production swaps `PassThroughRoundProver` for SP1 Compress / Halo2 accumulator / Risc0 fold.
- **Real NeoFS client** — `NeoFsLikeDAWriter` models the semantics with content-addressed in-process storage; production wires a NeoFS SDK.
- **dBFT consensus integration** — `SequencerRegistry` and `Neo.L2.Sequencer` provide the per-chain validator set; wiring it into neo's `DBFTPlugin` consensus selector is operator-specific.

## How to run

```bash
# Type-check + run all unit + integration tests
dotnet test Neo.L2.sln /p:NuGetAudit=false

# Build smart contracts (type-check only without nccs)
dotnet build contracts/NeoHub.ChainRegistry /p:NuGetAudit=false /p:DisableNccs=true

# Run the in-process devnet demo with real state-root continuity
dotnet run --project tools/Neo.L2.Devnet -- 5

# Generate a NeoHub deploy bundle
dotnet run --project tools/Neo.Hub.Deploy -- scaffold --output deploy-plan.json
dotnet run --project tools/Neo.Hub.Deploy -- plan --plan deploy-plan.json --output bundle.json

# Build the SP1 FFI bridge (default = mock fallback)
cd bridge/neo-zkvm-bridge && cargo build --release

# Build the SP1 FFI bridge with real prover linkage
cd bridge/neo-zkvm-bridge && cargo build --release --features real-prover

# Use the launcher CLI
dotnet run --project tools/Neo.Stack.Cli -- help
```
