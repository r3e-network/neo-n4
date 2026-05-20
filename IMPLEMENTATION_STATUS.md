# Implementation Status

> Snapshot of what's built in `neo4` against `doc.md`. Updated alongside meaningful PRs.

## Phase coverage (doc.md §18)

| Phase | Goal                                      | Status                                      |
| ----- | ----------------------------------------- | ------------------------------------------- |
| 0     | Sidechain PoC                             | ✅ MVP integration test passes              |
| 1     | NeoHub v0 + Shared Bridge                 | ✅ All 24 NeoHub contracts compile + deploy planner emits 23 production steps (15 core + NativeZkVerifier + 2 fraud verifiers + 5 external-bridge) |
| 2     | Batch Settlement                          | ✅ Off-chain green; real `KeyedStateStore` continuity verified across batches |
| 3     | Optimistic Challenge Window               | ✅ `OptimisticChallenge` contract + `ChallengeOrchestrator` + `BisectionGame` (log-N narrowing) + `GovernanceFraudVerifier` reference (structural, governance-arbitration mode) all green |
| 4     | NeoVM2 / RISC-V ZK Validity Proof         | ✅ **Neo N4 L2 execution targets NeoVM2/RISC-V.** `src/Neo.L2.Executor.RiscV` wires `RiscVTransactionExecutor` to the PolkaVM-backed native host in `external/neo-riscv-vm`, and `neo-l2-devnet --executor riscv` is the canonical L2 VM path. The SP1 proof boundary remains in `bridge/neo-zkvm-host` / `bridge/neo-zkvm-guest`: current compatibility tests still prove the legacy Neo VM guest while the N4 path converges on RISC-V execution receipts. Operator install: build `external/neo-riscv-vm` for the target OS, and use SP1 (`sp1up`) for real proof generation. |
| 5     | Neo Gateway (proof aggregation)           | ✅ `BinaryTreeAggregator` ships **two production-grade `IRoundProver`s** (`MultisigRoundProver` — Secp256r1 threshold-attested rounds; `MerklePathRoundProver` — per-constituent inclusion proofs against the aggregate root) plus the `PassThroughRoundProver` reference. Real cryptography, no toolchain dependency. Recursive-ZK fold variants (SP1 Compress / Halo2 / Risc0) remain operator-supplied via the same `IRoundProver` seam |
| 6     | Neo Stack CLI / Templates                 | ✅ All 12 subcommands functional (create-chain / init-l2 / register-chain / deploy-bridge-adapter / start-sequencer / start-batcher / start-prover / submit-batch / validate / scaffold-executor / new-l2 / list-templates) |

Legend: ✅ done, 🟡 substantial scaffolding + tests, 🔴 stub.

## Production-readiness audit

The phase matrix above measures **architectural coverage** — does the
component exist with the right shape? It does NOT measure whether each
component is mainnet-ready. Below is an honest readiness audit.

### Production-ready

These are real production-shape implementations with full test coverage:

- **All 24 NeoHub L1 deployable contracts** type-check via
  `Neo.SmartContract.Framework`; CI compiles each with `nccs` and verifies
  the `.nef` + `.manifest.json` artifacts. The 23-contract production bundle
  includes `NativeZkVerifier`, a deployable ZK adapter that delegates heavy
  proof verification to an L1 native accelerator. NeoHub is intentionally shipped as
  deployed contracts plus plugin/service integration, not as L1 Neo core native
  contracts. **All 10 N4 L2 system contracts**
  are Neo core native contracts in `external/neo/src/Neo/SmartContract/Native/L2NativeContracts.cs`
  and are verified by `external/neo/tests/Neo.UnitTests/SmartContract/Native/UT_L2NativeContracts.cs`.
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
  `neo-hub-deploy` declarative L1 deploy planner (now scaffolds the
  external-bridge stack alongside NeoHub: 23 steps + 17 post-deploy
  hints); `neo-external-bridge` operator CLI for bridge committee
  setup + dual-side deploy planning.
- **Cross-foreign-chain bridge (Phase B + C — doc.md §11.3)** —
  pluggable M-of-N committee verifier (`NeoHub.MpcCommitteeVerifier`
  with secp256k1 + ed25519 dispatch, replay-protected per nonce; now
  also stores per-signer bond-holder member binding via
  `RegisterCommitteeWithMembers`) + `NeoHub.ExternalBridgeRegistry`
  for verifier dispatch + `NeoHub.ExternalBridgeEscrow` (locks
  NEP-17 outbound + verifies inbound via registry) +
  `NeoHub.ExternalBridgeBond` (committee bonding mirroring
  `SequencerBond`) + `NeoHub.MpcCommitteeFraudVerifier` (Phase C —
  proves equivocation cryptographically + slashes the full bond +
  pays the reporter; replay-protected per `(chainId, signerIdx)`) +
  `L2NativeExternalBridgeContract` (Neo core native L2-side burn/mint counterpart).
  Eth-side `NeoExternalBridgeRouter.sol` (393 lines, solc 0.8.24,
  **39 Foundry tests** = 32 single-chain coverage + 7 multi-chain
  pinning per-instance state isolation across 17 canonical mainnet
  slots) ships in `external/foreign-contracts/eth/`. The same Solidity
  bytecode deploys unchanged on **any EVM chain** — constructor
  parameterizes `externalChainId`, `EthRpcEventSource` polls
  `eth_getLogs` against any EVM RPC endpoint, and the secp256k1
  `Signer` is reusable. Canonical 16-slot family banks in
  `watchers/neo-bridge-watcher-eth/src/chains.rs` cover Ethereum,
  Tron, BSC, Polygon, Arbitrum, Optimism, Base, Avalanche, Linea,
  zkSync Era, Scroll, Mantle, Fantom/Sonic, Celo; adding a new EVM
  chain takes 5 steps and writes zero new code (operator runbook in
  `docs/external-bridge-evm-chains.md`). Off-chain signing core
  lives in `watchers/neo-bridge-watcher-eth/` (Rust crate,
  byte-for-byte parity tests against the C# encoder, end-to-end
  orchestration with mockable trait abstractions). 7 real-secp256k1 tests in
  `UT_MpcFraudProof_RealCrypto.cs` pin the equivocation proof shape
  end-to-end (happy path + identical-messages reject + different-nonces
  reject + wrong-pubkey reject + chainId-mismatch reject + nonce-zero
  edge + committee-blob layout invariant).

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
| Full L1 disputed-tx re-execution | The on-chain v3 verifier (`NeoHub.RestrictedExecutionFraudVerifier`) now ships and re-derives pre/post state roots from each storage proof's siblings + leafIndex against the v1 header roots — well-formed v3 fraud claims are accepted on-chain without governance arbitration. **Still missing**: actually re-executing the disputed transaction on L1 with restricted state (an `ApplicationEngine` instance seeded only with the storage proofs' pre-values) to confirm the challenger's `ReplayedPostStateRoot` is truly correct. Until that lands, "accepted by RestrictedExecutionFraudVerifier" means "the challenger has made a structurally credible claim a downstream re-execution service must arbitrate." | `contracts/NeoHub.RestrictedExecutionFraudVerifier/` (on-chain v3 ✅), no on-L1 NeoVM-with-restricted-state re-executor yet |

### Reference / scaffolding — operator must replace

These are the deliberate "framework provides seam, operator brings impl"
boundaries. They're functional for the in-process devnet and tests, but
production would inject a real implementation through the documented
interface:

| Reference / scaffolding default | Production needs | Plug-in point |
|---------------------------------|------------------|---------------|
| `ReferenceTransactionExecutor` (devnet/tests) | `RiscVTransactionExecutor` ships in `src/Neo.L2.Executor.RiscV/` and is the canonical Neo N4 L2 executor (`--executor riscv`, also accepted as `--executor neovm2-riscv`). `ApplicationEngineTransactionExecutor` remains available only for legacy NeoVM compatibility checks (`--executor neovm`) and N3-era state-continuity tests. | `ITransactionExecutor` |
| `ReferenceBatchExecutor` + `DerivedPostStateRootOracle` (XOR placeholder) | `MerkleStatePostStateRootOracle` ships in `src/Neo.L2.Executor/` — production state root via `KeyedStateMerkleTree` (binary Merkle over sorted (key, value) pairs, same primitive ZKsync / Polygon zkEVM / Optimism use). Per-key inclusion proofs via `Prove(byte[])`. Plugs into the existing `ReferenceBatchExecutor` (which is otherwise production-quality); replacing the oracle turns the whole batch executor into production code | `IL2BatchExecutor` / `IPostStateRootOracle` |
| `MockRiscVProver` / `MockRiscVVerifier` (in-process testing) | Real ZK prover lives out-of-process: `prove-batch daemon` (Rust, `bridge/neo-zkvm-host/`). The .NET `L2ProverPlugin` keeps the in-process Zk path mock-only by design — see `docs/launching-an-l2.md` § "Prover deployment" | `IL2Prover` / `IL2ProofVerifier` |
| `PassThroughRoundProver` is one of THREE production implementations alongside `MultisigRoundProver` and `MerklePathRoundProver` — pick the one that matches your trust model | SP1 Compress / Halo2 fold / Risc0 fold (only needed for *recursive ZK* aggregation; the three shipped implementations are real production cryptography for committee-attested + inclusion-proof models) | `IRoundProver` |
| `InMemorySequencerCommitteeProvider` (devnet/tests) | `RpcSequencerCommitteeProvider` ships in `src/Neo.L2.Sequencer/` — production L1-RPC poller with configurable cache TTL, parallel status fanout across known keys, operator-supplied known-keys bootstrap (genesis + RegisterKnownKey hook for event-driven additions). `IsRegisteredAsync` always hits L1 (source of truth) | `ISequencerCommitteeProvider` |
| `InMemoryForcedInclusionSource` (devnet/tests) | `RpcForcedInclusionSource` ships in `src/Neo.L2.ForcedInclusion/` — production L1-RPC poller. Operator wires `RegisterNonce` to `OnForcedTxEnqueued` event subscription; `DrainAsync` issues parallel `getEntry` + `isConsumed` reads per known nonce, drops L1-finalized entries automatically, returns deadline-ordered list. `MarkConsumedAsync` is local bookkeeping (the L1 contract's matching method is SettlementManager-driven) | `IForcedInclusionSource` |
| `InMemoryMessageRouter` (devnet/tests) | `RpcMessageRouter` ships in `src/Neo.L2.Messaging/` — production L1-RPC poller for the inbound (L1→L2) side via `getL1ToL2` + `isConsumed` parallel reads; local outbox staging for outbound (L2-internal); pluggable finalized-proof store for `GetMessageProofAsync` (RocksDb-backed in production). `DecodeMessage` parses the canonical contract encoding + recomputes the canonical hash via `MessageHasher` — never trusts an off-wire hash | `IMessageRouter` |
| `InMemorySettlementClient` | Real L1 JSON-RPC client + signer | `ISettlementClient` (RpcSettlementClient exists; signer = operator-supplied delegate) |
| `InMemoryDAWriter`, `NeoFsLikeDAWriter`, `JsonRpcL1DAWriter` (signer = delegate), `CommitteeAttestedDAWriter` (committee = delegate) | Real NeoFS SDK adapter / signed L1 transactions / real DAC committee | `IDAWriter` |

### Plan-printers — not actual executors

These CLI subcommands print structured operator plans but do not execute
the corresponding L1/L2 wallet operations:

- `neo-stack register-chain` — emits 91-byte configBytes hex; does not sign or submit.
- `neo-stack deploy-bridge-adapter` — prints deploy plan; does not deploy.
- `neo-stack submit-batch` — validates the batch payload; does not sign or submit.
- `neo-stack start-{sequencer,batcher,prover}` — preflight check + "compose with neo-cli" instruction; does not spawn anything.

Wallet integration patterns are documented in
[`docs/wallet-integration.md`](docs/wallet-integration.md): paste-into-wallet
hex (cold-key flows) + delegate signing (hot-wallet automation). Worked
examples for NeoLine, Neon, NEP-6, Ledger, KMS — every CLI emits canonical
hex; the framework never holds private keys.

### Out of repo by design

- `[RpcMethod]`-attributed `RpcServerExtensions` partial class wrapping
  `L2RpcMethods` (10 methods exist but no neo `RpcServer` integration yet)
  — pending neo's `RpcServer` source becoming integrable. Tracked in
  `docs/spec-gap-plan.md` upstream/operator-blocked items.
- `Neo.L2.Sequencer` → `DBFTPlugin` consensus-selector wiring — deployment-specific.
- **Block explorer / bridge UI / faucet UI**: web variants ship in
  `sdk/web-explorer/index.html` (single static-file app with inlined JS SDK).
- **Typed SDKs**: ship in three languages — `src/Neo.L2.Sdk/` (.NET),
  `sdk/typescript/` (TS), `sdk/rust/` (Rust). All same wire shape, same
  4-class error taxonomy.
- **Faucet CLI**: `tools/Neo.L2.Faucet.Cli/` (`neo-l2-faucet`) — production
  drip with rate limiting + RocksDB-persisted journal.

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
| `Neo.L2.Proving`          | Stage 0 multisig (real), Stage 1 optimistic, Stage 2 mock RISC-V (in-process testing seam); `VerifierRegistry`. Real Stage-2 ZK proving lives in `bridge/neo-zkvm-host/` (Rust) — a separate process operators run as the `prove-batch daemon`. |
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

### Native FFI bridge (`bridge/`) + watchers (`watchers/`)

| Crate                       | Role                                             |
| --------------------------- | ------------------------------------------------ |
| `neo-zkvm-host`             | Rust binary (sp1-sdk 6.2.1): `prove-batch daemon --watch <dir>` is the production prover. Also exposes lib API (`execute()` / `prove()` / `verify()`) for callers that want the proof inline. |
| `neo-bridge-watcher-eth`    | Rust crate: messaging + signing core for the **entire EVM family** ↔ Neo external bridge. Same daemon binary serves Ethereum, Tron, BSC, Polygon, Arbitrum, Optimism, Base, Avalanche, Linea, zkSync, Scroll, Mantle, Fantom, Celo — chain-id is a config field, not a code dimension. Canonical `ExternalCrossChainMessage` encoder (byte-for-byte parity with C# `Neo.L2.Messaging.ExternalMessageHasher`) + `NeoProofBytes` / `EthProofBytes` encoders (same signatures, two wire formats) + curve-agnostic `Signer` trait + `WatcherCore<S, ES, NS, J>` orchestration that pins the safety invariant *cursor MUST NOT advance on submit failure*. 32 base tests + 55 live-RPC integration tests = 87 with `--features live-rpc`. **Production daemon ships a complete operational story**: graceful SIGTERM/SIGINT shutdown (~100ms exit via `interruptible_sleep` + async-signal-safe handler), `flock(LOCK_EX | LOCK_NB)`-based concurrent-instance detection on the journal directory, per-chain `min_confirmations` reorg buffer with `chains::recommended_confirmations` operator-warning at startup, and an HTTP server exposing `/healthz` (200/503), `/info` (always 200), and `/metrics` (9 Prometheus-format gauges + counters). Reference k8s + systemd manifests in `watchers/neo-bridge-watcher-eth/deploy/` (Deployment + Service + ConfigMap + Secret + PVC; ClusterIP service so health/metrics don't leak chain id + journal cursor + last error to the public internet). |
| `neo-bridge-watcher-tron`   | Rust crate: thin re-export of `neo-bridge-watcher-eth` with Tron-specific chain-id constants (`TRON_MAINNET_CHAIN_ID = 0xE000_0010` + Nile/Shasta testnets). Tron uses the same secp256k1+SHA256 + Keccak256 address derivation as Ethereum, so no separate messaging or signing core is needed — confirmation that the Phase-B abstractions were chain-agnostic at the right level. 7 tests pin chain-id namespacing + cross-chain hash distinctness. |
| `neo-bridge-watcher-sol`    | Rust crate: `Ed25519FileSigner` implementing `Signer` with `curve_tag = 2` (vs Eth/Tron's 1) — validates the curve-agnostic refactor. Solana chain-ids (`0xE000_0020..2F`). On-chain dispatch flows to `CryptoLib.VerifyWithEd25519` per the registered curveTag. Per `doc.md` §11.3.4, Solana stays MPC-committee-only (Tower BFT light client is too expensive on-chain); the committee model handles Solana via the same trait surface. 9 tests including real `ed25519-dalek` sign+verify and a `Vec<Box<dyn Signer>>` polymorphism check. |

#### Foreign-side router artifacts (`external/foreign-contracts/`)

| Path                        | Role                                             |
| --------------------------- | ------------------------------------------------ |
| `eth/`                      | `NeoExternalBridgeRouter.sol` (393 lines, solc 0.8.24, via_ir + optimizer). Locks ETH/ERC-20 bound for Neo, finalizes Neo → Eth withdrawals via committee-attested `ecrecover` proofs. **39 Foundry tests** with real `vm.sign` round-trips: 32 single-chain (constructor + committee + lock + withdraw + every guard incl. messageType-offset regression + Ownable2Step accept/overwrite + 14 revert-path tests for access/payload/sig framing/reentrancy) + 7 multi-chain (17 canonical mainnet slots construct, out-of-namespace ids reject, per-router state isolation across nonces / committees / chain-id stamping, BSC router rejects Polygon-claiming messages). Deploys unchanged on Ethereum, BSC, Polygon, Arbitrum, Optimism, Base, Avalanche, Linea, zkSync Era, Scroll, Mantle, Fantom, Celo, Tron via `forge create` with the right `externalChainId` constructor arg. |
| `tron/`                     | README pointing at `eth/` since TVM is EVM-flavored — same Solidity, different `externalChainId` constructor arg (`0xE000_0010` mainnet / `0xE000_0011` Nile / `0xE000_0012` Shasta). Documents tronbox / tronweb deployment, Tron-specific energy/bandwidth budgeting, and TVM opcode caveats. The full slot allocation table for *every* supported EVM chain lives in `watchers/neo-bridge-watcher-eth/src/chains.rs` — Tron is one of 14 chain families. |
| `sol/`                      | Anchor program (~638 lines) implementing the same semantics on Solana: PDA-based state (`BridgeState` + `Vault` + per-`(chainId, nonce)` `ConsumedNonce` for replay protection), ed25519 verification via Solana's sigverify precompile (the canonical Wormhole/Neon pattern — saves ~30k CU/sig vs in-program ed25519), four instructions (`initialize` / `set_committee` / `lock_sol_and_send` / `finalize_withdrawal`). Source-only in this iteration; operators run `anchor build` + `anchor test` against `solana-test-validator`. v0 is SOL-only (SPL deferred), `MSG_TYPE_CALL` reverts, recipient zero-pads upper 12 bytes. Reviewed-needed flag in the README before mainnet. |

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

### Smart contracts - 24 deployable NeoHub + 10 Neo core native L2 contracts

**NeoHub L1 suite (24 projects / 23 production + 1 test-only stub):**
Phase 0-4: `ChainRegistry` · `SharedBridge` · `SettlementManager` · `VerifierRegistry` · **`NativeZkVerifier`** (ProofType.Zk adapter -> L1 native accelerator) · `MessageRouter` · `TokenRegistry` · `DARegistry` · **`DAValidator`** · **`L1TxFilter`** · `GovernanceController` · `EmergencyManager` · `ForcedInclusion` · `SequencerBond` · `SequencerRegistry` · `OptimisticChallenge` · `GovernanceFraudVerifier` (structural v1/v2) · **`RestrictedExecutionFraudVerifier`** (trustless v3 — on-chain Merkle re-derivation)

External-bridge stack (doc.md §11.3 — cross-foreign-chain to Eth/Tron/Sol):
**`MpcCommitteeVerifier`** (Phase B M-of-N secp256k1/ed25519 verifier; Phase C-extended with per-signer bond-holder binding via `RegisterCommitteeWithMembers`) · **`ExternalBridgeRegistry`** (pluggable verifier dispatch; same upgrade-via-governance shape as `VerifierRegistry`) · **`ExternalBridgeEscrow`** (locks NEP-17 outbound + dispatches inbound through registry; defense-in-depth replay tracking) · **`ExternalBridgeBond`** (committee bonding + slashing-on-equivocation; mirrors `SequencerBond` 1:1) · **`ExternalBridgeStubVerifier`** (Phase-A devnet acceptance verifier; bridgeKind=0 to refuse production deployments) · **`MpcCommitteeFraudVerifier`** (Phase C — proves equivocation cryptographically + slashes full bond + pays reporter; replay-protected per `(chainId, signerIdx)`)

**L2 native (10):**
`L2BridgeContract` · `L2MessageContract` · `L2BatchInfoContract` · `L2FeeContract` · `L2PaymasterContract` · `L2SystemConfigContract` · **`L2NativeExternalBridgeContract`** (L2-side counterpart to `ExternalBridgeEscrow` — burn-on-send / mint-on-receive, sequencer-injected inbound) · **`BridgedNep17Contract`** (canonical mint/burn NEP-17 representation for bridged L1 assets) · **`L2AccountAbstraction`** (validator/paymaster/nonce entry point) · **`L2InteropVerifier`** (L2-side global message-root mirror and inclusion verifier)

### Tools (`tools/`)

| Tool                  | Role                                                  |
| --------------------- | ----------------------------------------------------- |
| `Neo.External.Bridge.Cli` | `neo-external-bridge` CLI: operator key-gen + dual-side committee setup + ordered deploy plan for the cross-foreign-chain bridge. `genkey` (real secp256k1 keypair → pub33 + ethAddr20 for the same identity; private key written to a file, 0600 on POSIX); `committee-blob` (validates each pubkey is a real secp256k1 point, rejects duplicates / oversize, emits Neo blob + matching Eth address list); `deploy-bundle` (cross-checks committee size + threshold, prints the ordered 4-step Neo+Eth wire-up checklist). Same plan-printer pattern as `neo-bridge` / `neo-l2-faucet` — no live RPC, no built-in signer. |
| `Neo.Stack.Cli`       | `neo-stack` CLI: 12 subcommands all functional (create-chain, init-l2, register-chain, deploy-bridge-adapter, start-{sequencer,batcher,prover}, submit-batch, validate, scaffold-executor, new-l2, list-templates). The 3 commands that need L1/L2 wallet integration print structured operator plans (target contract + args + numbered next steps) instead of placeholder "would do X" text. `validate` is a pure JSON sanity-check (enum names, required fields, cross-field consistency warnings) that catches operator-edit typos before deploy/devnet. **`scaffold-executor`** emits a starter custom-`ITransactionExecutor` project (csproj + executor skeleton + state seam + tx builder + state-store adapter + README; with `--with-tests` also emits a sibling MSTest project with 3 starter tests) so an operator goes from "I want a custom L2" to a buildable + testable project in one command — output mirrors the working `samples/executors/Sample.CounterChainExecutor` reference. **`new-l2`** is the composite that strings `create-chain` + `init-l2` + `scaffold-executor --with-tests` together, so an operator goes from zero to "buildable + testable + devnet-previewable starter" in a single command. **`list-templates`** prints the four chain-config templates (rollup / zk-rollup / validium / sidechain) with their §16.2 dimensions + use-case descriptions so operators can evaluate which template fits their chain without reading source. |
| `Neo.L2.Devnet`       | `neo-l2-devnet <N> [--metrics-port <P>] [--data-dir <path>] [--config <path>] [--executor <kind>]` — runs N batches end-to-end with real `KeyedStateStore` continuity + sequencer committee + DA publish per batch + post-run `ChainAuditor` pass; with `--metrics-port` stands up a live HTTP server + self-scrapes `/metrics`, `/healthz`, `/readyz`; with `--data-dir` wires `RocksDbKeyValueStore` instances under that path so committee + state + RPC proofs + DA payloads all survive restart; with `--config <path>` reads §16.2 dimensions (security/da/sequencer/exit/gateway) from a `chain.config.json`; with `--executor counter` wires `Sample.CounterChainExecutor` end-to-end (state mutation via `KeyedStateStoreAdapter`, real receipts/withdrawals/messages from CounterTxBuilder-built transactions) so an operator can preview a real custom executor through the same pipeline. |
| `Neo.Hub.Deploy`      | `neo-hub-deploy` — declarative L1 deploy planner: scaffold / plan / verify |

### Tests

**1467 .NET tests across 34 projects, plus 202 cross-language tests
(15 TypeScript + 10 Rust SDK + 5 shared execution-core + 7 SP1 guest host-mode + 103 Rust bridge
watcher core across 3 crates [eth: 87 with `live-rpc`; tron: 7; sol: 9],
39 Foundry Solidity tests for `NeoExternalBridgeRouter` [32 single-chain
and 7 multi-chain validating the router deploys unchanged across the entire
EVM family], and 22 Solana router tests) — all green on the Windows audit
matrix.** Phase-C real-crypto fraud-proof tests (7 of the 1467 .NET) pin the
equivocation slash path's bytes-on-the-wire contract end-to-end with
real secp256k1 signatures.

| Project                              | Tests | Coverage                                    |
| ------------------------------------ | ----- | ------------------------------------------- |
| `Neo.L2.Abstractions.UnitTests`      | 52    | enum discriminants (ChainMode / SecurityLevel / DAMode / ProofType / MessageType / BatchStatus / AssetType / **SequencerModel / ExitModel** — closing doc.md §16.2 spec coverage), models, interface shape, **`ProofTypeExtensions.Resolve` boundary tests, `ChainIdValidator.ValidateL2` (zero-rejection / non-zero-acceptance / setting-name), record byte-content equality (DAPublishRequest / DAReceipt / ProofRequest / ProofResult / BatchExecutionRequest — overrides per AGENTS.md convention, including list-of-bytes element-wise comparison), `L2ChainConfigSerializer` 91-byte wire-format pin (layout + roundtrip + enum-extreme / wrong-length / out-of-range-byte / null rejection + chainId LE parity with the on-chain `ChainRegistry` parser), `L2ChainConfigJsonReader` (full population from create-chain JSON + 4 UInt160 hashes, named-error-message paths for unknown enum / missing field / malformed UInt160 / null inputs, validium-template shape pin, roundtrip through serializer)** |
| `Neo.L2.Batch.UnitTests`             | 35    | builder lifecycle, serializer round-trip, **proof-length bounds, unknown-ProofType rejection, all-valid-ProofType round-trip, trailing-byte rejection** |
| `Neo.L2.State.UnitTests`             | 113   | Merkle tree, proof verify, hashers, **canonical proof wire format (round-trip, layout, truncation, oversized depth, 7-leaf all-positions), `MessageHasher.HashMessage` + `HashWithdrawal` canonical-buffer layout pinned, HashMessage field-order sensitivity, HashWithdrawal at-max 64-byte amount accepted, on-chain Merkle verifier parity (4-leaf / 5-leaf odd-card / 7-leaf all-positions / tampered-sibling rejection / state-tree pin), `KeyedStateMerkleTree.ComputeRoot` ↔ `MerkleTree.ComputeRoot(HashEntry leaves)` cross-pin across 10 cardinalities incl. odd cases + `HashLeaf` ↔ `KeyedStateStore.HashEntry` byte-identity (NeoClassicParity suite), **wire-format fuzz suite** (`UT_WireFormat_Fuzz`, 19 ZKsync-style tests): random byte sequences through `MerkleProofSerializer.Decode` + `DepositPayload.Decode` (must reject with typed exception, never crash); differential round-trip across fuzzed tree shapes (1..16 leaves) + fuzzed (l1Asset, l2Recipient, amount) tuples; suffix-truncation rejection** |
| `Neo.L2.Messaging.UnitTests`         | 46    | inbox FIFO, replay protection, outbox split, **L2Outbox metric emission across destinations, persistence reopen pins, MessageBuilder rejects self-routed messages (incl. zero-to-zero)** |
| `Neo.L2.Bridge.UnitTests`            | 88    | registry, deposit replay, withdrawal staging, **metric emission on all paths, retryability after transient validation failure, registry orphan cleanup, `DepositPayload` byte-layout pinned at documented offsets, at-max 64-byte amount boundary, **property-based invariant suite** (`UT_BridgeInvariants_PropertyBased`, 17 ZKsync-style tests): seeded random walks (200 ops × 4-8 seeds) asserting AssetRegistry bidirectional consistency, WithdrawalProcessor nonce-uniqueness across SealBatch promotion, DepositProcessor accepted-sum ↔ DepositsProcessed counter equality** |
| `Neo.L2.Proving.UnitTests`           | 51    | Stage 0/1/2 prove+verify, registry dispatch, **optimistic sequencer account/signature binding, proof-payload boundary tests (length, version, ProofSystem range), AttestationVerifier dedup-before-verify, `MultisigProofPayload` byte-layout pinned at documented offsets ([1B version][2B signerCount LE]·N×([33B pubkey][64B sig])), ProofSystem enum discriminants pinned (Unknown=0..Axiom=4 — wire byte at RiscVProofPayload offset 1)** |
| `Neo.L2.Executor.UnitTests`          | 56    | empty/single/many, ordering, determinism, **KeyedStateStore + oracle, persistence reopen pins** |
| `Neo.L2.ForcedInclusion.UnitTests`   | 28    | nonce ordering, replay, overdue detection, **persistence reopen pins**   |
| `Neo.L2.Sequencer.UnitTests`         | 32    | register/exit/finalize lifecycle, **metric emission for all three lifecycle ops + committee-size gauge, `SetMaxCommitteeSize` shrink-below-count rejection, persistence reopen pins (incl. exit-window survives restart)** |
| `Neo.L2.Censorship.UnitTests`        | 15    | overdue detection, sequencer attribution, **metric emission per detection batch** |
| `Neo.L2.Challenge.UnitTests`         | 104   | fraud-proof payload, orchestrator, BisectionGame, **`InspectWithBisectionAsync` (no-fraud agreement, log-N narrowing, arg validation, empty/mismatched/single-element checkpoint shape rejection, **v2 witness auto-emission** + bounded fallback to v1 when disputed tx is oversized / index out of range)**, metric emission, **`GovernanceFraudVerifier` parity coverage (real-discrepancy → accept; same-root → reject NoDiscrepancy; bad-length → reject BadLength; bad-version → reject BadVersion; decision-tree order pins; layout-offsets parity vs FraudProofPayload encoder; DisputedTxIndex doesn't change structural verdict; v2 round-trip incl. truncated/oversized witness rejection; v2 acceptance through verifier; v2 OversizedWitness path), **v3 wire-format round-trip + caps + auto version dispatch (v1/v2/v3 IsX flags; storage-proof per-proof + per-payload caps; truncated key/value/sibling rejection; zero-proof v3 rejected — use v2 instead)**, **`V3StorageProofVerifier` (off-chain reference: NotV3 / NoDiscrepancy returns; happy-path 2-leaf-tree Verified; PreStateRootMismatch + ReplayedPostStateRootMismatch rejected; encode→decode round-trip preserves verifiability; `HashEntry` layout pinned in lockstep with `KeyedStateStore.HashEntry`)**, **`RestrictedExecutionFraudVerifier` parity (15 tests: happy-path 2-leaf-tree → ReasonAccepted; v1/v2 payloads → BadVersion; truncated below v2 header / past num-proofs prefix → BadLength; oversized witness → OversizedWitness; same-root → NoDiscrepancy short-circuits before per-proof verify; zero-proof / >MaxStorageProofsPerPayload → ProofCountInvalid; pre-derived root mismatch → PreStateRootMismatch; post-derived root mismatch → ReplayedPostStateRootMismatch; decision-tree order pins; layout-offset pins for PreStateRoot@1 + ReplayedPostStateRoot@65; encode→decode→encode survives on-chain accept)** |
| `Neo.L2.Audit.UnitTests`             | 57    | continuity + proof-validity + **public-input-hash consistency** checks, summary, `NoZeroProofCheck`, **`ChainAuditor` self-emits runs + failures (delta = failed-finding count), strict-ascending duplicate-rejection, `AuditReport.Passed` non-empty guard, `ProofValidityCheck` null-guard, `DAAvailabilityCheck` (all-available, one-missing, zero-commitment-skipped, mixed, null-arg guards, stable name), `BatchRangeCheck` (valid-range / inverted-range / zero-batch-number / empty-list / multi-failure / null-arg / stable name), all 6 `IAuditCheck.Name` strings pinned (continuity / proof / no_zero_proof / public_input_hash / da_availability / batch_range), `PublicInputHashConsistencyCheck` resolver path (default zero-fill vs override-resolver pin + null-resolver loud failure)** |
| `Neo.Plugins.L2Rpc.UnitTests`        | 42    | all 10 RPC methods (incl. `getsecuritylabel` for the doc.md §16.2 5-dimension label), foreign-chain rejection, **per-method metric emission (calls/latency/failures), too-few-params clear-error, oversized-chainId overflow, monotonic `_latestStateRoot` on out-of-order Finalize, persistence reopen pins, `getsecuritylabel` defaults + override propagation pins, IL2RpcStore default-interface defaults (External / false / DbftCommittee / Permissionless) + e2e through getsecuritylabel for third-party minimal-impl stores** |
| `Neo.Plugins.L2DA.UnitTests`         | 79    | InMemory + NeoFsLike DA writers + **MetricsEmittingDAWriter (success / throw / accumulate / passthrough), `ResolveDAMode` accepts 0..3 / rejects unknown, all DAWriter null-arg paths, `L2DAPlugin` default-writer / `WithWriter` injection / Name+Description non-empty / `WithMetrics` propagates to active writer (mid-flight sink swap), `CommitteeAttestedDAWriter` round-trip + tampered-sig + null-arg + buggy-callback contracts, `BuildDefaultWriter` (External/NeoFS/L1/DAC × dataDir-set/null/empty/whitespace boundary), `PersistentDAWriter` (RocksDB-backed: round-trip + configured-mode-flow + cross-instance reopen pin + unknown-commitment / null-store / null-request / null-receipt / null-commitment guards + defensive-copy + dispose-owning-vs-borrowed semantics + default-mode = External), `JsonRpcL1DAWriter` (mode=L1, ctor null-guards / empty-rpc-method, PublishAsync delegates with contract+request, Commitment=Hash256(payload) cross-tier convention, pointer=32B tx hash, null-tx-hash defense, IsAvailableAsync zero-commitment short-circuit / HALT-true / HALT-false / FAULT-state-false, dispose semantics)** |
| `Neo.Plugins.L2Gateway.UnitTests`    | 55    | flat + binary-tree aggregator, edge cases, **metric emission with rounds=log2(N) + per-batch accumulation, `PassThroughRoundProver` round-prover-level pinning (BackendId=0xFE constant, right-null odd-leaf rule, Hash256 message-root composition, [4B leftLen][bytes][4B rightLen][bytes] proof byte layout, both-empty-proof envelope, asymmetry, null-left rejection)** |
| `Neo.Plugins.L2Metrics.UnitTests`    | 19    | composition root: bound port, idempotent Start, real HTTP scrape, readiness predicate gating, default settings, **`ResolveBindAddress` boundary tests, concurrent-Start race-safety, `ValidatePort` boundary tests, plugin Name + Description non-empty** |
| `Neo.Plugins.L2Batch.UnitTests`      | 22    | `BatchSealer` block / tx / age triggers, batch-number monotonicity, gauge replace, NoOp default, **`ValidatePositive` boundary tests, plugin Name + Description non-empty** |
| `Neo.Plugins.L2Bridge.UnitTests`     | 10    | `L2BridgePlugin` lifecycle, asset registration, default behavior, **WithMetrics propagates to existing Deposit + Withdrawal processors (symmetric pins — without both, a refactor that drops one of the `?.WithMetrics()` calls would silently lose half the `l2.bridge.*` metric stream)** |
| `Neo.Plugins.L2Prover.UnitTests`     | 10    | `L2ProverPlugin` lifecycle, ProofType resolution, **Wire dispatch coverage for all branches: Zk-with-prover / Zk-without (helpful error) / Optimistic (points at L2SettlementPlugin) / None (points at Multisig+Optimistic+Zk)** |
| `Neo.Plugins.L2Settlement.UnitTests` | 24    | **metric emission on submit success / failure / no-op default, wire-before-dequeue, concurrent-call gate serialization, plugin Name + Description non-empty, `L2SettlementSettings.From` config parsing (defaults / explicit-zero ChainId rejected / non-zero accepted / invalid ProofType byte rejected / valid byte stored / explicit L1RpcEndpoint + Enabled overrides)** |
| `Neo.L2.Settlement.Rpc.UnitTests`    | 38    | JSON-RPC envelope, stack parsing, signer, **InMemorySettlementClient lifecycle, AdvanceStatus driver, retry semantics**    |
| `Neo.L2.Telemetry.UnitTests`         | 73    | counter/histogram/gauge accumulation, tag canonicalization, Prometheus exporter (counter/gauge/summary, labels, name sanitization, frozen-snapshot), request handler routing, TCP server round-trip + multi-request, catalog completeness vs MetricNames + Prometheus integration, **`/healthz` + `/readyz` (with predicate)** |
| `Neo.L2.Persistence.UnitTests`       | 35    | **`InMemoryKeyValueStore` + `RocksDbKeyValueStore` parity (Put / Get / Delete / Contains / EnumeratePrefix / Count, lexicographic ordering, dispose semantics, defensive-copy on read)** |
| `Neo.L2.Executor.RiscV.UnitTests`    | 6     | `RiscVHost` structural contracts (Neo VMState byte constants, default network = N3 mainnet, application trigger byte, IsAvailable doesn't throw on missing lib, empty-script rejection, RiscVExecutionResult.Halted property) |
| `Neo.Hub.Deploy.UnitTests`           | 56    | topo sort, cycle detection, scaffold, **plan-version check, duplicate / empty step names, full 23-step scaffold pin (15 core NeoHub + NativeZkVerifier + GovernanceFraudVerifier v1/v2 + RestrictedExecutionFraudVerifier v3 + 4 Phase-B external-bridge contracts + 1 Phase-C MpcCommitteeFraudVerifier), SequencerBond slashers[] array shape, OptimisticChallenge dependency edges, no-cycle pin (bond → challenge but not back), both fraud verifiers have empty deploy data + no deps + parallel shape pin (peers, not asymmetric), PostDeployActions surfaces RegisterSlasher + ChainRegistry/VerifierRegistry SetGovernanceController wiring hints + NativeZkVerifier native accelerator / verification-key / ProofType.Zk registry wiring + DARegistry/DAValidator + L1TxFilter wiring hints + per-verifier informational notes (v1/v2 GovernanceFraudVerifier + v3 RestrictedExecutionFraudVerifier — operators get one note per deployed verifier) / suppresses governance hints when GovernanceController absent / asymmetric (ChainRegistry-only-emits-one-hint) / asymmetric-only-v3 (v3 verifier without v1/v2 emits only the v3 note) / null-arg guard, **`VerifyCommand` exit codes — 0 (all artifacts present) / 1 (caller error: missing --rpc, missing plan file, malformed plan JSON) / 2 (at least one nef or manifest missing on disk); partial-missing also exits 2 (CI-script semantics: most-are-ok ≠ success); null-args rejected at boundary**.** |
| `Neo.L2.IntegrationTests`            | 25    | Phase 0 MVP + Phase 1 cross-component + Phase 2 full-stack + Phase 3 optimistic-challenge + all-phases stitch + e2e telemetry pipeline + **L2MetricsPlugin composition root (every instrumented component → one sink → HTTP scrape) + e2e RocksDB persistence (KeyedStateStore + InMemoryL2RpcStore + InMemorySequencerCommitteeProvider all rehydrate from one shared data dir on reopen) + e2e audit pipeline (all 6 checks pass on healthy chain + DA-dropped scenario specifically catches via `DAAvailabilityCheck` + broken-batch-range failure-detection metric counts) + **e2e custom-executor full-stack (Sample.CounterChainExecutor + KeyedStateStoreAdapter + ReferenceBatchExecutor + KeyedStateRootOracle + AttestationProver/Verifier all wire cleanly: 3-batch run with mixed Increment/Withdraw/Message txs → all 4 batch roots non-zero, state-root advances per batch + uniqueness pin across 4 distinct roots, multisig verifier accepts custom-executor commitments, BatchSerializer encode/decode round-trip is identity, final state has 6 expected counter entries; failed-tx batch → effects don't pollute withdrawal/message roots, gas accounting still correct, state from successful txs intact)** |
| `Neo.Stack.Cli.UnitTests`            | 133   | **`scaffold-executor` subcommand** — happy path emits all 6 expected files (csproj + executor + state seam + tx builder + adapter + README); csproj has correct RootNamespace + AssemblyName + 3-up project refs; executor has expected skeleton + Opcode.NoOp dispatch + customization marker; README links to reference sample + 5-step checklist. **Argument validation**: invalid identifier (digit-first / hyphen / empty) → exit 1 + no output dir created; chainId=0 throws (L1 sentinel reject); non-numeric chainId → exit 1; non-empty output dir refused (no overwrite); default chainId 1001 surfaced in README; case preservation (MyDeFi → MyDeFiExecutor.cs); --path alias for --output. **`--with-tests` flag**: emits a sibling `<output>.UnitTests` project (csproj with MSTest + ProjectReference to main + Usings.cs + 3 starter tests for NoOp success / empty-tx Failed / unknown-opcode Failed); README mentions companion test project; default behavior (no flag) does NOT create the tests dir + README does NOT mention tests project; non-empty tests dir rejected atomically (main project not created either). **`new-l2` composite (4-step: create-chain + validate + init-l2 + scaffold-executor --with-tests)**: happy path produces all artifacts in one go (chain.config.json + data/logs/Plugins + executor + tests project); validate step runs against EVERY template (rollup / zk-rollup / validium / sidechain) — defense-in-depth catches template/serializer/validator drift; missing --name → exit 1 atomically before create-chain runs; chainId=0 throws atomically; non-numeric chainId → exit 1; invalid name surfaces from scaffold-executor mid-flow (earlier-step artifacts on disk are preserved for inspection); --path alias for --output; default-style ./chain-<id> output works; --template propagates through to chain.config.json's securityLevel. **`list-templates` + `TemplateCatalog`**: catalog has exactly 4 templates in canonical order (rollup default first, then zk-rollup / validium / sidechain); Resolve returns the right struct for known names + falls back to default for unknown; IsKnown is case-sensitive; ValidNames is comma-separated in order; list-templates with no args prints all 4 + default note + exits 0; --template <name> prints full per-template details (chainMode + daMode + use-case + sample command); unknown --template exits 1 with valid-names error; per-template detail round-trips through every supported name. |
| `Sample.CounterChainExecutor.UnitTests` | 24 | **operator-facing reference for "how to plug in custom chain logic"**: 3-opcode custom executor (IncrementCounter / EmitWithdrawal / EmitMessage) demonstrates the `ITransactionExecutor` seam. Per-sender-counter happy-path + accumulation + ulong wraparound semantics; per-sender state isolation; truncated-tx → Failed-receipt path (not crash); withdrawal happy-path produces valid `WithdrawalRequest` with deterministic txHash-derived nonce; zero-amount withdrawal rejected; message happy-path produces routable `CrossChainMessage` via canonical `MessageBuilder.Build`; self-routed (source==target) rejected; oversized message body builder-rejected at MaxMessageBytes cap; unknown-opcode + empty-tx → Failed; SPEC.md determinism pin (two fresh executors + identical inputs → identical receipts + state); mixed-opcode batch smoke test; **executor ctor null-state / null-emittingContract guards; ExecuteAsync null-batchContext guard; cooperative-cancellation pin (cancelled token → OperationCanceledException, not a wasted receipt); `KeyedStateStoreAdapter` round-trip Put/Get + missing-key returns false + adapter writes flow through to `KeyedStateStore.ComputeRoot` parity vs direct writes; adapter ctor null-store + Put null-key/null-value + TryGet null-key all reject with ArgumentNullException at the call boundary so a misconfigured DI wiring fails at composition, not later with an unattributed NRE** |

## What's not yet wired (out of MVP scope)

- **Live L1 signer for `RpcSettlementClient.SubmitBatchAsync`** — interface in place; concrete wallet integration is operator-specific. For tests + devnets, `Neo.L2.Settlement.Rpc.InMemorySettlementClient` provides a fully-functional in-process `ISettlementClient` with deterministic tx hashes and an explicit `AdvanceStatus` lifecycle driver.
- **`nccs` artifact generation** - `Directory.Build.props` calls `nccs` with `ContinueOnError=true` so dev builds without nccs still type-check. CI installs `Neo.Compiler.CSharp` on the runner and verifies all 24 deployable NeoHub contracts plus the 2 sample contracts produce `.nef` + `.manifest.json` artifacts on every commit. L2 system contracts are not deployable artifacts; CI verifies them through the Neo core native-contract tests in `external/neo`.
- **RpcServer plugin integration partial** — `L2RpcMethods` callable as plain methods; the `[RpcMethod]`-attributed wrapper for neo's `RpcServer` plugin needs the RpcServer source.
- **Real SP1 prover** — `bridge/neo-zkvm-host/` (Rust, sp1-sdk 6.2.1). The framework's only production proving path: `prove()` returns proof + verifying-key bytes for on-chain submission, `verify()` confirms off-chain pre-settlement. The `prove-batch` CLI ships a `daemon --watch <dir> --archive <dir>` mode that polls a queue directory for `*.batch.bin` files and emits matching `*.proof.bin` + `*.proof.vk`, atomically renaming inputs so the loop is restart-safe. Verified end-to-end on a real proof: 87s prove time, 2.78MB proof artifact, 42s verify time, public-input hash matches host execute byte-for-byte. The prover lives in a separate process from the sequencer (matches Optimism / Arbitrum / ZKsync architecture) — see `docs/launching-an-l2.md` § "Prover deployment" for the operator runbook.
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

# Build the SP1 prover (real Stage-2 ZK validity prover daemon)
CPATH=~/.local/include cargo build --release -p neo-zkvm-host

# Run the prover daemon (consumes *.batch.bin from --watch dir,
# emits matching *.proof.bin + *.proof.vk)
target/release/prove-batch daemon \
    --watch /var/lib/neo-l2/batches \
    --archive /var/lib/neo-l2/proven \
    --poll-secs 5

# Use the launcher CLI
dotnet run --project tools/Neo.Stack.Cli -- help
```
