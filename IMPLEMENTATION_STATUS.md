# Implementation Status

> Snapshot of what's built in `neo4` against `doc.md`. Updated alongside meaningful PRs.

## Phase coverage (doc.md §18)

| Phase | Goal                                      | Status                                      |
| ----- | ----------------------------------------- | ------------------------------------------- |
| 0     | Sidechain PoC                             | ✅ MVP integration test passes              |
| 1     | NeoHub v0 + Shared Bridge                 | ✅ All 13 NeoHub contracts compile + deploy planner emits 13-step bundle |
| 2     | Batch Settlement                          | ✅ Off-chain green; real `KeyedStateStore` continuity verified across batches |
| 3     | Optimistic Challenge Window               | ✅ `OptimisticChallenge` contract + `ChallengeOrchestrator` + `BisectionGame` (log-N narrowing) all green |
| 4     | NeoVM2 / RISC-V ZK Validity Proof         | 🟡 Two tracks: **RISC-V execution** — `Neo.L2.Executor.RiscV` P/Invokes into `external/neo-riscv-vm/crates/neo-riscv-host` (PolkaVM-backed). **SP1 ZK proving** — bridge buildable (`cargo build --release --features real-prover` produces `libneo_zkvm_bridge.so`); CI exercises the link path with `SP1_FORCE_DUMMY=true`. Real proof requires the SP1 toolchain offline + matching guest ELF |
| 5     | Neo Gateway (proof aggregation)           | 🟡 `BinaryTreeAggregator` with pluggable `IRoundProver` (default = pass-through hash) |
| 6     | Neo Stack CLI / Templates                 | ✅ All 8 subcommands functional (create-chain / init-l2 / register-chain / deploy-bridge-adapter / start-sequencer / start-batcher / start-prover / submit-batch) |

Legend: ✅ done, 🟡 substantial scaffolding + tests, 🔴 stub.

## Production-readiness audit

The phase matrix above measures **architectural coverage** — does the
component exist with the right shape? It does NOT measure whether each
component is mainnet-ready. Below is an honest readiness audit.

### Production-ready

These are real production-shape implementations with full test coverage:

- **All 13 NeoHub L1 contracts** + **6 L2Native contracts** type-check via
  `Neo.SmartContract.Framework`; CI compiles each with `nccs` and verifies
  the `.nef` + `.manifest.json` artifacts (21 contracts total incl.
  `samples/contracts/Sample.*`).
- **Off-chain canonical encoders**, byte-layout-pinned + tested:
  `BatchSerializer`, `MessageHasher`, `MerkleProofSerializer`,
  `L2ChainConfigSerializer`, `DepositPayload`, `MultisigProofPayload`,
  `RiscVProofPayload`, `OptimisticProofPayload`, `FraudProofPayload`.
- **Persistence layer** — `IL2KeyValueStore` with `InMemoryKeyValueStore`
  (tests) + `RocksDbKeyValueStore` (production); per-component reopen tests
  pin the durability story across 6 components.
- **Stage 0 multisig prover** — real Secp256r1 signature aggregation
  (`AttestationProver`, `AttestationVerifier`).
- **Optimistic challenge bisection game** — real log-N narrowing algorithm.
- **Bridge accounting** — `AssetRegistry`, `DepositProcessor`,
  `WithdrawalProcessor` with replay protection + nonce dedup, plus per-batch
  withdrawal verification on L1 (`SettlementManager.VerifyWithdrawalLeafWithProof`).
- **Forced-inclusion spam control** — `NeoHub.ForcedInclusion` charges
  configurable GAS fee per enqueue (`SetFee` / `SetFeeRecipient` /
  `SetGasToken`); atomic fail-on-transfer-failure; default 0 preserves the
  fee-free legacy path.
- **Audit pipeline** — 6 invariant checks (continuity / proof-validity /
  public-input hash / no-zero-proof / DA availability / batch range).
- **CLI tooling** — `neo-stack` plan-printers + `validate` subcommand;
  `neo-hub-deploy` declarative L1 deploy planner.

### Optimistic-challenge fraud-proof game — partial

The optimistic challenge mechanism's settlement path delegates to an
operator-supplied fraud verifier. A reference verifier
(`NeoHub.GovernanceFraudVerifier`) now ships for chains running in
**governance-arbitration mode** — it decodes the canonical 101-byte
`FraudProofPayload`, verifies the wire format (length + version +
claims-a-real-discrepancy), and emits structured events for the
human security council to review. Wire by passing this contract's
deployed hash as the `fraudVerifier` argument to
`OptimisticChallenge.Challenge`.

Production trustlessness still requires extending the
`FraudProofPayload` wire format with execution-trace witness bytes a
verifier could replay step-by-step on L1 — that's tracked here:

| Item | What's still missing | Where |
|------|---------------------|-------|
| `Neo.L2.Challenge.FraudProofPayload` execution-trace witness | The current 101-byte layout proves "there is a discrepancy" but not the specific opcode-step that produced it. A trustless fraud verifier needs witness bytes (the disputed tx + its pre-state reads + expected post-state writes) to re-execute on L1 without trusting the challenger's replay. | `src/Neo.L2.Challenge/FraudProofPayload.cs` |

### Reference / scaffolding — operator must replace

These are the deliberate "framework provides seam, operator brings impl"
boundaries. They're functional for the in-process devnet and tests, but
production would inject a real implementation through the documented
interface:

| Reference / scaffolding default | Production needs | Plug-in point |
|---------------------------------|------------------|---------------|
| `ReferenceTransactionExecutor` | NeoVM `ApplicationEngine`-backed executor | `ITransactionExecutor` |
| `ReferenceBatchExecutor` (placeholder post-state root) | Real MPT-backed batch executor | `IL2BatchExecutor` |
| `MockRiscVProver` / `MockRiscVVerifier` | Real ZK prover / verifier | `IL2Prover` / `IL2ProofVerifier` |
| `Sp1RiscVProver` falls back to mock without bridge | SP1 toolchain offline + matching guest ELF (operator-built); real `--features real-prover` libneo_zkvm_bridge | `IL2Prover` |
| `PassThroughRoundProver` (Phase 5 default) | SP1 Compress / Halo2 fold / Risc0 fold | `IRoundProver` |
| `InMemorySequencerCommitteeProvider` | L1-RPC-backed `SequencerRegistry` poller (does not exist in repo) | `ISequencerCommitteeProvider` |
| `InMemoryForcedInclusionSource` | L1-RPC-backed `ForcedInclusion` poller (does not exist in repo) | `IForcedInclusionSource` |
| `InMemoryMessageRouter` | L1-RPC-backed `MessageRouter` poller (does not exist in repo) | `IMessageRouter` |
| `InMemorySettlementClient` | Real L1 JSON-RPC client + signer | `ISettlementClient` (RpcSettlementClient exists; signer = operator-supplied delegate) |
| `InMemoryDAWriter`, `NeoFsLikeDAWriter`, `JsonRpcL1DAWriter` (signer = delegate), `CommitteeAttestedDAWriter` (committee = delegate) | Real NeoFS SDK adapter / signed L1 transactions / real DAC committee | `IDAWriter` |

### Plan-printers — not actual executors

These CLI subcommands print structured operator plans but do not execute
the corresponding L1/L2 wallet operations:

- `neo-stack register-chain` — emits 91-byte configBytes hex; does not sign or submit.
- `neo-stack deploy-bridge-adapter` — prints deploy plan; does not deploy.
- `neo-stack submit-batch` — validates the batch payload; does not sign or submit.
- `neo-stack start-{sequencer,batcher,prover}` — preflight check + "compose with neo-cli" instruction; does not spawn anything.

Wallet integration is operator-specific (NEP-6 keystore / Ledger / etc.)
and is deliberately out-of-repo per the spec-gap-plan's operator-track.

### Out of repo by design

- `[RpcMethod]`-attributed `RpcServerExtensions` partial class wrapping
  `L2RpcMethods` (10 methods exist but no neo `RpcServer` integration yet)
  — pending neo's `RpcServer` source becoming integrable. Tracked in
  `docs/spec-gap-plan.md` upstream/operator-blocked items.
- `Neo.L2.Sequencer` → `DBFTPlugin` consensus-selector wiring — deployment-specific.
- Block explorer / bridge UI / wallet integration / typed SDKs / faucet —
  Layer 5 of [`docs/tech-stack-coverage.md`](docs/tech-stack-coverage.md);
  operator territory in any L2 ecosystem.

### Bottom line

The framework is **architecturally complete + sufficient for a devnet** with
real cryptographic primitives, real persistence, real test coverage. It is
**not** a turnkey mainnet deployment — operators targeting production must
(a) replace the reference / in-memory scaffolding through the documented
plug-in seams, (b) wire production fee config on `ForcedInclusion`
(`SetFee` / `SetFeeRecipient` / `SetGasToken`) to enable spam control,
(c) use the `*WithProof` variants of `SettlementManager.VerifyWithdrawalLeaf*`
and `EmergencyManager.EscapeHatchExit` (the no-proof variants are
intentional single-leaf fast paths, only valid when the relevant tree has
exactly one entry), and (d) integrate wallet signing for the 4 plan-printing
subcommands.

## Completed work — by code

### Off-chain libraries (`src/Neo.L2.*`)

| Project                   | Role                                                              |
| ------------------------- | ----------------------------------------------------------------- |
| `Neo.L2.Abstractions`     | 7 interfaces + 14 model records (doc.md §19)                      |
| `Neo.L2.Batch`            | `L2Batch`, `BatchBuilder`, deterministic `BatchSerializer`        |
| `Neo.L2.State`            | `MerkleTree` (matches Neo `Hash256`), **`MerkleProofSerializer`** (canonical 48 + 32×N byte wire format consumed by L1 SharedBridge), `MessageHasher`, `WithdrawalTree`, `MessageTree`, `StateRootCalculator` |
| `Neo.L2.Bridge`           | `AssetRegistry`, `DepositPayload`, `DepositProcessor`, `WithdrawalProcessor` (both processors emit `l2.bridge.deposits/deposits_rejected/withdrawals/withdrawals_rejected` to a per-instance `IL2Metrics`) |
| `Neo.L2.Messaging`        | `MessageBuilder`, `L1MessageInbox`, `L2Outbox` (emits `l2.messaging.emitted`), `InMemoryMessageRouter` |
| `Neo.L2.Proving`          | Stage 0 multisig (real), Stage 1 optimistic, Stage 2 mock RISC-V; `VerifierRegistry` |
| `Neo.L2.Proving.Sp1`      | Phase 4 SP1 P/Invoke wrapper with graceful fallback to mock when native bridge missing |
| `Neo.L2.Executor`         | `SPEC.md` + `Receipt`, pluggable `ITransactionExecutor` / `IPostStateRootOracle` / `IL1MessageProcessor`, `ReferenceBatchExecutor`, **`KeyedStateStore` + `KeyedStateRootOracle`** |
| `Neo.L2.ForcedInclusion`  | Anti-censorship `IForcedInclusionSource` + in-memory backend with optional `IL2KeyValueStore` (RocksDB) for consumed-nonce durability across restart (emits `l2.forced_inclusion.observed` on Enqueue) |
| `Neo.L2.Sequencer`        | `ISequencerCommitteeProvider` + in-memory backend with optional `IL2KeyValueStore` (RocksDB) for committee + exit-window durability across restart (Register / BeginExit / Finalize); emits `l2.sequencer.registered/exits_started/exits_finalized` + `l2.sequencer.committee_size` gauge |
| `Neo.L2.Censorship`       | `CensorshipDetector` — turns overdue forced-tx entries into `CensorshipReport[]` (emits `l2.censorship.reports` per detection batch) |
| `Neo.L2.Challenge`        | `FraudProofPayload` + `ChallengeOrchestrator` (`InspectAsync` for replay-based fraud detection, `InspectWithBisectionAsync` for log-N narrowing of disputed tx index) + `BisectionGame` for Phase-3 |
| `Neo.L2.Settlement.Rpc`   | JSON-RPC client + `RpcSettlementClient` for L1 read methods + signer-delegated submit |
| `Neo.L2.Audit`            | End-to-end chain auditor: `ContinuityCheck` + `ProofValidityCheck` + `NoZeroProofCheck` + `PublicInputHashConsistencyCheck` + `DAAvailabilityCheck` + **`BatchRangeCheck`** + `ChainAuditor` (auto-emits `l2.audit.runs` + `l2.audit.failures`) |
| `Neo.L2.Telemetry`        | `IL2Metrics` (counter/histogram/gauge) + `NoOpMetrics` + `InMemoryMetrics` + `MetricsSnapshot` + `PrometheusExporter` + `MetricsRequestHandler` (`/metrics` + **`/healthz` + `/readyz`**) + `MetricsHttpServer` (TcpListener-based, no third-party deps) + canonical `MetricNames` + `MetricCatalog` (operator-facing HELP descriptions) |
| `Neo.L2.Persistence`      | **`IL2KeyValueStore` abstraction + `InMemoryKeyValueStore` + `RocksDbKeyValueStore` (RocksDbSharp 10.10.1, snappy compression default).** Wired into `KeyedStateStore`, `InMemoryL2RpcStore`, `InMemoryMessageRouter`, `InMemoryForcedInclusionSource`, `InMemorySequencerCommitteeProvider`, `PersistentDAWriter` so production data survives restart. Per-component reopen tests pin the durability story. |
| `Neo.L2.Executor.RiscV`   | **Phase 4 RISC-V execution engine binding.** P/Invoke wrapper around `libneo_riscv_host` (PolkaVM-backed, vendored at `external/neo-riscv-vm`). `RiscVHost.IsAvailable` is a sticky-cached probe; `RiscVHost.Execute(script, trigger, network, timestamp, gasLeft)` thin-wraps the FFI and returns `RiscVExecutionResult` (state / fee / error). Native lib operator-deployed via `LD_LIBRARY_PATH` or alongside the C# binaries. |

### Native FFI bridge (`bridge/`)

| Crate                | Role                                             |
| -------------------- | ------------------------------------------------ |
| `neo-zkvm-bridge`    | Rust cdylib with stable C ABI (`neo_zkvm_prove` / `_verify` / `_free_buffer` / `_abi_version`); optional `real-prover` feature links against `neo-zkvm-prover` |

### neo-node plugins (`src/Neo.Plugins.L2*`)

| Plugin                       | Role                                                  |
| ---------------------------- | ----------------------------------------------------- |
| `Neo.Plugins.L2Batch`        | Hooks `Blockchain.Committed`; seal logic lives on testable `BatchSealer`; emits `l2.batch.sealed/seal_latency_ms/tx_count` via `WithMetrics()` |
| `Neo.Plugins.L2Settlement`   | Wires prover + settlement client; signs sealed batches; **emits `l2.settlement.submitted/submit_failures/submit_latency_ms` + `l2.proving.generated/latency_ms` via `WithMetrics()`** |
| `Neo.Plugins.L2Bridge`       | Hosts `AssetRegistry` + `DepositProcessor` + `WithdrawalProcessor`; emits `l2.bridge.{deposits,withdrawals,*_rejected}` via `WithMetrics()` |
| `Neo.Plugins.L2DA`           | Picks DA writer by `DAMode` config — `InMemoryDAWriter` (External default), **`NeoFsLikeDAWriter`** (content-addressed), `CommitteeAttestedDAWriter` (DAC mode, operator-injected), `PersistentDAWriter` over RocksDB when `DataDirectory` is set, L1 mode requires operator-supplied L1-RPC writer; `WithMetrics()` wraps the chosen writer in `MetricsEmittingDAWriter` (mode-tagged `l2.da.published/publish_latency_ms/publish_failures`) |
| `Neo.Plugins.L2Prover`       | Hosts `IL2Prover` for the configured `ProofType`      |
| `Neo.Plugins.L2Rpc`          | 9 RPC handlers (doc.md §14.1) + `IL2RpcStore` (`InMemoryL2RpcStore` with optional `IL2KeyValueStore` for withdrawal/message proofs); per-method `l2.rpc.calls/latency_ms/failures` tagged by `method` |
| `Neo.Plugins.L2Gateway`      | `BinaryTreeAggregator` with pluggable `IRoundProver` (default `PassThroughRoundProver`); `PassThroughAggregator` for flat aggregation; emits `l2.gateway.aggregations/batches_aggregated/aggregation_rounds/aggregation_latency_ms` |
| `Neo.Plugins.L2Metrics`      | **Composition root**: hosts the shared `IL2Metrics` sink + `MetricsHttpServer`; other plugins call `metricsPlugin.Metrics` and pass to their `WithMetrics()` setters; configurable bind address + port + readiness predicate |

### Smart contracts (`contracts/`) — 19 total, all type-check via devpack

**NeoHub L1 suite (13):**
`ChainRegistry` · `SharedBridge` · `SettlementManager` · `VerifierRegistry` · `MessageRouter` · `TokenRegistry` · `DARegistry` · `GovernanceController` · `EmergencyManager` · `ForcedInclusion` · `SequencerBond` · `SequencerRegistry` · **`OptimisticChallenge`**

**L2 native (6):**
`L2BridgeContract` · `L2MessageContract` · `L2BatchInfoContract` · `L2FeeContract` · `L2PaymasterContract` · `L2SystemConfigContract`

### Tools (`tools/`)

| Tool                  | Role                                                  |
| --------------------- | ----------------------------------------------------- |
| `Neo.Stack.Cli`       | `neo-stack` CLI: 9 subcommands all functional (create-chain, init-l2, register-chain, deploy-bridge-adapter, start-{sequencer,batcher,prover}, submit-batch, validate). The 3 commands that need L1/L2 wallet integration print structured operator plans (target contract + args + numbered next steps) instead of placeholder "would do X" text. `validate` is a pure JSON sanity-check (enum names, required fields, cross-field consistency warnings) that catches operator-edit typos before deploy/devnet. |
| `Neo.L2.Devnet`       | `neo-l2-devnet <N> [--metrics-port <P>] [--data-dir <path>]` — runs N batches end-to-end with real `KeyedStateStore` continuity + sequencer committee + DA publish per batch + post-run `ChainAuditor` pass; with `--metrics-port` stands up a live HTTP server + self-scrapes `/metrics`, `/healthz`, `/readyz`; with `--data-dir` wires `RocksDbKeyValueStore` instances under that path so committee + state + RPC proofs + DA payloads all survive restart |
| `Neo.Hub.Deploy`      | `neo-hub-deploy` — declarative L1 deploy planner: scaffold / plan / verify |

### Tests

**925 unit + integration tests across 27 projects:**

| Project                              | Tests | Coverage                                    |
| ------------------------------------ | ----- | ------------------------------------------- |
| `Neo.L2.Abstractions.UnitTests`      | 47    | enum discriminants (ChainMode / SecurityLevel / DAMode / ProofType / MessageType / BatchStatus / AssetType / **SequencerModel / ExitModel** — closing doc.md §16.2 spec coverage), models, interface shape, **`ProofTypeExtensions.Resolve` boundary tests, `ChainIdValidator.ValidateL2` (zero-rejection / non-zero-acceptance / setting-name), record byte-content equality (DAPublishRequest / DAReceipt / ProofRequest / ProofResult / BatchExecutionRequest — overrides per AGENTS.md convention, including list-of-bytes element-wise comparison), `L2ChainConfigSerializer` 91-byte wire-format pin (layout + roundtrip + enum-extreme / wrong-length / out-of-range-byte / null rejection + chainId LE parity with the on-chain `ChainRegistry` parser), `L2ChainConfigJsonReader` (full population from create-chain JSON + 4 UInt160 hashes, named-error-message paths for unknown enum / missing field / malformed UInt160 / null inputs, validium-template shape pin, roundtrip through serializer)** |
| `Neo.L2.Batch.UnitTests`             | 35    | builder lifecycle, serializer round-trip, **proof-length bounds, unknown-ProofType rejection, all-valid-ProofType round-trip, trailing-byte rejection** |
| `Neo.L2.State.UnitTests`             | 66    | Merkle tree, proof verify, hashers, **canonical proof wire format (round-trip, layout, truncation, oversized depth, 7-leaf all-positions), `MessageHasher.HashMessage` + `HashWithdrawal` canonical-buffer layout pinned (independent assembly + Hash256 re-derivation), HashMessage field-order sensitivity, HashWithdrawal at-max 64-byte amount accepted (boundary partner of RejectsOversizedAmount), on-chain Merkle verifier parity (4-leaf / 5-leaf odd-card / 7-leaf all-positions / tampered-sibling rejection / state-tree pin via KeyedStateStore.HashEntry — guards against algorithmic divergence between off-chain proof generator and on-chain `SettlementManager.Verify*WithProof`)** |
| `Neo.L2.Messaging.UnitTests`         | 29    | inbox FIFO, replay protection, outbox split, **L2Outbox metric emission across destinations, persistence reopen pins, MessageBuilder rejects self-routed messages (incl. zero-to-zero)** |
| `Neo.L2.Bridge.UnitTests`            | 47    | registry, deposit replay, withdrawal staging, **metric emission on success/replay/unknown-asset/duplicate-nonce/negative-amount paths, retryability after transient validation failure, registry orphan cleanup on L1/L2 repoint, DepositPayload trailing-byte rejection, `DepositPayload` byte-layout pinned at documented offsets ([20B l1Asset][20B l2Recipient][4B amountLen LE][amountBytes]), DepositPayload at-max 64-byte amount accepted (boundary partner of RejectsOversizedAmount)** |
| `Neo.L2.Proving.UnitTests`           | 50    | Stage 0/1/2 prove+verify, registry dispatch, **proof-payload boundary tests (length, version, ProofSystem range), AttestationVerifier dedup-before-verify, `MultisigProofPayload` byte-layout pinned at documented offsets ([1B version][2B signerCount LE]·N×([33B pubkey][64B sig])), ProofSystem enum discriminants pinned (Unknown=0..Axiom=4 — wire byte at RiscVProofPayload offset 1)** |
| `Neo.L2.Proving.Sp1.UnitTests`       | 12    | bridge unavailable, mock fallback, VK mismatch, **`MaxProofBytes` bound pinned, Sp1BridgeStatus enum discriminants pinned (Ok=0, InvalidInput=-1, ProveFailed=-2, VerifyRejected=-3, NotImplemented=-9 — must match Rust ABI)** |
| `Neo.L2.Executor.UnitTests`          | 38    | empty/single/many, ordering, determinism, **KeyedStateStore + oracle, persistence reopen pins** |
| `Neo.L2.ForcedInclusion.UnitTests`   | 19    | nonce ordering, replay, overdue detection, **persistence reopen pins**   |
| `Neo.L2.Sequencer.UnitTests`         | 26    | register/exit/finalize lifecycle, **metric emission for all three lifecycle ops + committee-size gauge, `SetMaxCommitteeSize` shrink-below-count rejection, persistence reopen pins (incl. exit-window survives restart)** |
| `Neo.L2.Censorship.UnitTests`        | 15    | overdue detection, sequencer attribution, **metric emission per detection batch** |
| `Neo.L2.Challenge.UnitTests`         | 48    | fraud-proof payload, orchestrator, BisectionGame, **`InspectWithBisectionAsync` (no-fraud agreement, log-N narrowing, arg validation, empty/mismatched/single-element checkpoint shape rejection)**, metric emission |
| `Neo.L2.Audit.UnitTests`             | 57    | continuity + proof-validity + **public-input-hash consistency** checks, summary, `NoZeroProofCheck`, **`ChainAuditor` self-emits runs + failures (delta = failed-finding count), strict-ascending duplicate-rejection, `AuditReport.Passed` non-empty guard, `ProofValidityCheck` null-guard, `DAAvailabilityCheck` (all-available, one-missing, zero-commitment-skipped, mixed, null-arg guards, stable name), `BatchRangeCheck` (valid-range / inverted-range / zero-batch-number / empty-list / multi-failure / null-arg / stable name), all 6 `IAuditCheck.Name` strings pinned (continuity / proof / no_zero_proof / public_input_hash / da_availability / batch_range), `PublicInputHashConsistencyCheck` resolver path (default zero-fill vs override-resolver pin + null-resolver loud failure)** |
| `Neo.Plugins.L2Rpc.UnitTests`        | 40    | all 10 RPC methods (incl. `getsecuritylabel` for the doc.md §16.2 5-dimension label), foreign-chain rejection, **per-method metric emission (calls/latency/failures), too-few-params clear-error, oversized-chainId overflow, monotonic `_latestStateRoot` on out-of-order Finalize, persistence reopen pins, `getsecuritylabel` defaults + override propagation pins, IL2RpcStore default-interface defaults (External / false / DbftCommittee / Permissionless) + e2e through getsecuritylabel for third-party minimal-impl stores** |
| `Neo.Plugins.L2DA.UnitTests`         | 79    | InMemory + NeoFsLike DA writers + **MetricsEmittingDAWriter (success / throw / accumulate / passthrough), `ResolveDAMode` accepts 0..3 / rejects unknown, all DAWriter null-arg paths, `L2DAPlugin` default-writer / `WithWriter` injection / Name+Description non-empty / `WithMetrics` propagates to active writer (mid-flight sink swap), `CommitteeAttestedDAWriter` round-trip + tampered-sig + null-arg + buggy-callback contracts, `BuildDefaultWriter` (External/NeoFS/L1/DAC × dataDir-set/null/empty/whitespace boundary), `PersistentDAWriter` (RocksDB-backed: round-trip + configured-mode-flow + cross-instance reopen pin + unknown-commitment / null-store / null-request / null-receipt / null-commitment guards + defensive-copy + dispose-owning-vs-borrowed semantics + default-mode = External), `JsonRpcL1DAWriter` (mode=L1, ctor null-guards / empty-rpc-method, PublishAsync delegates with contract+request, Commitment=Hash256(payload) cross-tier convention, pointer=32B tx hash, null-tx-hash defense, IsAvailableAsync zero-commitment short-circuit / HALT-true / HALT-false / FAULT-state-false, dispose semantics)** |
| `Neo.Plugins.L2Gateway.UnitTests`    | 39    | flat + binary-tree aggregator, edge cases, **metric emission with rounds=log2(N) + per-batch accumulation, `PassThroughRoundProver` round-prover-level pinning (BackendId=0xFE constant, right-null odd-leaf rule, Hash256 message-root composition, [4B leftLen][bytes][4B rightLen][bytes] proof byte layout, both-empty-proof envelope, asymmetry, null-left rejection)** |
| `Neo.Plugins.L2Metrics.UnitTests`    | 19    | composition root: bound port, idempotent Start, real HTTP scrape, readiness predicate gating, default settings, **`ResolveBindAddress` boundary tests, concurrent-Start race-safety, `ValidatePort` boundary tests, plugin Name + Description non-empty** |
| `Neo.Plugins.L2Batch.UnitTests`      | 22    | `BatchSealer` block / tx / age triggers, batch-number monotonicity, gauge replace, NoOp default, **`ValidatePositive` boundary tests, plugin Name + Description non-empty** |
| `Neo.Plugins.L2Bridge.UnitTests`     | 10    | `L2BridgePlugin` lifecycle, asset registration, default behavior, **WithMetrics propagates to existing Deposit + Withdrawal processors (symmetric pins — without both, a refactor that drops one of the `?.WithMetrics()` calls would silently lose half the `l2.bridge.*` metric stream)** |
| `Neo.Plugins.L2Prover.UnitTests`     | 10    | `L2ProverPlugin` lifecycle, ProofType resolution, **Wire dispatch coverage for all branches: Zk-with-prover / Zk-without (helpful error) / Optimistic (points at L2SettlementPlugin) / None (points at Multisig+Optimistic+Zk)** |
| `Neo.Plugins.L2Settlement.UnitTests` | 24    | **metric emission on submit success / failure / no-op default, wire-before-dequeue, concurrent-call gate serialization, plugin Name + Description non-empty, `L2SettlementSettings.From` config parsing (defaults / explicit-zero ChainId rejected / non-zero accepted / invalid ProofType byte rejected / valid byte stored / explicit L1RpcEndpoint + Enabled overrides)** |
| `Neo.L2.Settlement.Rpc.UnitTests`    | 38    | JSON-RPC envelope, stack parsing, signer, **InMemorySettlementClient lifecycle, AdvanceStatus driver, retry semantics**    |
| `Neo.L2.Telemetry.UnitTests`         | 73    | counter/histogram/gauge accumulation, tag canonicalization, Prometheus exporter (counter/gauge/summary, labels, name sanitization, frozen-snapshot), request handler routing, TCP server round-trip + multi-request, catalog completeness vs MetricNames + Prometheus integration, **`/healthz` + `/readyz` (with predicate)** |
| `Neo.L2.Persistence.UnitTests`       | 27    | **`InMemoryKeyValueStore` + `RocksDbKeyValueStore` parity (Put / Get / Delete / Contains / EnumeratePrefix / Count, lexicographic ordering, dispose semantics, defensive-copy on read)** |
| `Neo.L2.Executor.RiscV.UnitTests`    | 6     | `RiscVHost` structural contracts (Neo VMState byte constants, default network = N3 mainnet, application trigger byte, IsAvailable doesn't throw on missing lib, empty-script rejection, RiscVExecutionResult.Halted property) |
| `Neo.Hub.Deploy.UnitTests`           | 21    | topo sort, cycle detection, scaffold, **plan-version check, duplicate / empty step names, full 13-NeoHub-contract scaffold pin, SequencerBond slashers[] array shape, OptimisticChallenge dependency edges, no-cycle pin (bond → challenge but not back), PostDeployActions surfaces RegisterSlasher + ChainRegistry/VerifierRegistry SetGovernanceController wiring hints / suppresses governance hints when GovernanceController absent / asymmetric (ChainRegistry-only-emits-one-hint) / null-arg guard** |
| `Neo.L2.IntegrationTests`            | 19    | Phase 0 MVP + Phase 1 cross-component + Phase 2 full-stack + Phase 3 optimistic-challenge + all-phases stitch + e2e telemetry pipeline + **L2MetricsPlugin composition root (every instrumented component → one sink → HTTP scrape) + e2e RocksDB persistence (KeyedStateStore + InMemoryL2RpcStore + InMemorySequencerCommitteeProvider all rehydrate from one shared data dir on reopen) + e2e audit pipeline (all 6 checks pass on healthy chain + DA-dropped scenario specifically catches via `DAAvailabilityCheck` + broken-batch-range failure-detection metric counts)** |

## What's not yet wired (out of MVP scope)

- **Live L1 signer for `RpcSettlementClient.SubmitBatchAsync`** — interface in place; concrete wallet integration is operator-specific. For tests + devnets, `Neo.L2.Settlement.Rpc.InMemorySettlementClient` provides a fully-functional in-process `ISettlementClient` with deterministic tx hashes and an explicit `AdvanceStatus` lifecycle driver.
- **`nccs` artifact generation** — `Directory.Build.props` calls `nccs` with `ContinueOnError=true` so dev builds without nccs still type-check. CI installs `Neo.Compiler.CSharp` on the runner and verifies all 19 contracts produce `.nef` + `.manifest.json` artifacts on every commit (catches NeoVM-specific compile errors that the C# type-check doesn't).
- **RpcServer plugin integration partial** — `L2RpcMethods` callable as plain methods; the `[RpcMethod]`-attributed wrapper for neo's `RpcServer` plugin needs the RpcServer source.
- **Real SP1 prover linkage** — flip `--features real-prover` on the bridge crate to link `neo-zkvm-prover` into the cdylib. CI now exercises this path with `SP1_FORCE_DUMMY=true` so the host-side dependency graph stays buildable across crate bumps; a real proof requires the SP1 toolchain (`sp1up`) installed and the matching `neo-zkvm-program` guest ELF compiled (without `SP1_FORCE_DUMMY`).
- **Real recursive ZK round prover** — `BinaryTreeAggregator` has the right shape; production swaps `PassThroughRoundProver` for SP1 Compress / Halo2 accumulator / Risc0 fold.
- **Real NeoFS client** — `NeoFsLikeDAWriter` models the semantics with content-addressed in-process storage and is now the default `DAMode.NeoFS` writer wired by `L2DAPlugin`. Production swaps it via `L2DAPlugin.WithWriter(yourNeoFsSdkAdapter)` before `Configure` runs.
- **L1-DA writer** — `DAMode.L1` (publish to Neo N3) has no built-in default targeting Neo N3 specifically. Operator paths: (a) wire an L1-RPC-backed `IDAWriter` via `L2DAPlugin.WithWriter()` before `Configure`, or (b) set `DataDirectory` in plugin config to use `PersistentDAWriter` over RocksDB for content-addressed local DA. The plain `DAMode.L1` path with no override + no DataDirectory throws a clear `NotSupportedException` at `Configure`-time.
- **DAC mode wiring** — `Neo.Plugins.L2DA.CommitteeAttestedDAWriter` provides a real DAMode.DAC implementation (N committee signers, secp256r1 sigs verified before answering IsAvailable=true), but the plugin can't auto-wire it without operator-supplied committee keys + signer callback — operators inject via `L2DAPlugin.WithWriter(new CommitteeAttestedDAWriter(committee, sign))` before `Configure`.
- **dBFT consensus integration** — `SequencerRegistry` and `Neo.L2.Sequencer` provide the per-chain validator set; wiring it into neo's `DBFTPlugin` consensus selector is operator-specific.

## How to run

```bash
# Type-check + run all unit + integration tests
dotnet test Neo.L2.sln /p:NuGetAudit=false

# Build smart contracts (type-check only without nccs)
dotnet build contracts/NeoHub.ChainRegistry /p:NuGetAudit=false /p:DisableNccs=true

# Run the in-process devnet demo with real state-root continuity
dotnet run --project tools/Neo.L2.Devnet -- 5

# Same demo plus live HTTP /metrics scrape on port 9090
dotnet run --project tools/Neo.L2.Devnet -- 5 --metrics-port 9090

# Persist devnet state to disk via RocksDB (state survives restart)
dotnet run --project tools/Neo.L2.Devnet -- 5 --data-dir /tmp/neo-l2-devnet

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
