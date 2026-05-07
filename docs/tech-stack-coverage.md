# Tech-stack coverage

A comprehensive L2 ecosystem typically spans five layers — protocol contracts,
node infrastructure, operator tooling, application development, and end-user
interfaces. This document catalogs each layer against what `neo4` currently
ships and what's intentionally out-of-repo (third-party, deployment-specific,
or external dependency).

The structure isn't ZKsync-specific — these are the categories any L2 stack
covers. The implementations here are Neo-native and built from scratch
against `doc.md`; the intent is functional parity with what a mature L2
ecosystem provides for operators and app developers, not a translation of
any other project's source.

---

## Layer 1 — Protocol contracts

| Component | Status | Code |
|-----------|:------:|------|
| Chain registry (admission policy + per-chain config) | ✅ | `contracts/NeoHub.ChainRegistry/` |
| Shared L1↔L2 bridge (escrow, deposits, withdrawals) | ✅ | `contracts/NeoHub.SharedBridge/` |
| Settlement manager (batch finalization, state-root anchoring) | ✅ | `contracts/NeoHub.SettlementManager/` |
| Verifier registry (pluggable proof dispatch) | ✅ | `contracts/NeoHub.VerifierRegistry/` |
| Token registry (canonical L1↔L2 asset mapping) | ✅ | `contracts/NeoHub.TokenRegistry/` |
| Message router (L1↔L2 + L2↔L2 cross-chain delivery) | ✅ | `contracts/NeoHub.MessageRouter/` |
| DA registry (per-batch DA commitment store) | ✅ | `contracts/NeoHub.DARegistry/` |
| Sequencer registry + bonding | ✅ | `contracts/NeoHub.SequencerRegistry/`, `SequencerBond/` |
| Forced-inclusion contract | ✅ | `contracts/NeoHub.ForcedInclusion/` |
| Optimistic challenge game | ✅ | `contracts/NeoHub.OptimisticChallenge/` |
| Governance + council + timelock | ✅ | `contracts/NeoHub.GovernanceController/` |
| Emergency pause + escape hatch | ✅ | `contracts/NeoHub.EmergencyManager/` |
| Fraud verifier (governance-arbitration mode reference) | ✅ | `contracts/NeoHub.GovernanceFraudVerifier/` |
| Fraud verifier (trustless v3 — on-chain Merkle re-derivation) | ✅ | `contracts/NeoHub.RestrictedExecutionFraudVerifier/` |

**15 NeoHub contracts.** All type-check via `Neo.SmartContract.Framework`; CI
builds each with `nccs` and verifies the `.nef` + `.manifest.json` artifacts.

| Component | Status | Code |
|-----------|:------:|------|
| L2 batch info (chainId, batch number, L1 height) | ✅ | `contracts/L2Native.L2BatchInfoContract/` |
| L2 bridge (mint on deposit, burn on withdrawal) | ✅ | `contracts/L2Native.L2BridgeContract/` |
| L2 message I/O (outbound emit + inbound apply) | ✅ | `contracts/L2Native.L2MessageContract/` |
| L2 fee splitter (sequencer / prover / DA shares) | ✅ | `contracts/L2Native.L2FeeContract/` |
| L2 paymaster (fee abstraction, sponsored assets) | ✅ | `contracts/L2Native.L2PaymasterContract/` |
| L2 system-config cache | ✅ | `contracts/L2Native.L2SystemConfigContract/` |

**6 L2-side native contracts.**

---

## Layer 2 — Node infrastructure

| Component | Status | Code |
|-----------|:------:|------|
| Batch builder (block ↦ batch sealing) | ✅ | `src/Neo.L2.Batch/`, `Neo.Plugins.L2Batch/` |
| State-root generator | ✅ | `src/Neo.L2.State/` |
| Deterministic batch executor (the proving target) | ✅ | `src/Neo.L2.Executor/` |
| RISC-V execution kernel (PolkaVM-backed) | ✅ | `src/Neo.L2.Executor.RiscV/` (P/Invoke binding) |
| Persistence backends (in-memory + RocksDB) | ✅ | `src/Neo.L2.Persistence/` |
| Sequencer committee provider | ✅ | `src/Neo.L2.Sequencer/` |
| Censorship detection | ✅ | `src/Neo.L2.Censorship/` |
| Forced-inclusion source | ✅ | `src/Neo.L2.ForcedInclusion/` |
| Multisig (Stage 0) prover/verifier | ✅ | `src/Neo.L2.Proving.Attestation/` |
| Optimistic (Stage 1) prover/verifier | ✅ | `src/Neo.L2.Proving.Optimistic/` |
| RISC-V ZK (Stage 2) prover/verifier scaffolding | 🟡 | `src/Neo.L2.Proving.RiscVZk/`, `src/Neo.L2.Proving.Sp1/` (FFI bridge buildable, real proof needs SP1 toolchain) |
| DA writers (in-memory / NeoFS / L1 / DAC / RocksDB) | ✅ | `src/Neo.Plugins.L2DA/` (5 implementations) |
| Settlement RPC client | ✅ | `src/Neo.L2.Settlement.Rpc/` |
| Telemetry (Prometheus-shaped) | ✅ | `src/Neo.L2.Telemetry/`, `Neo.Plugins.L2Metrics/` |
| Audit pipeline (6 invariant checks) | ✅ | `src/Neo.L2.Audit/` |
| Bisection / fraud-proof game | ✅ | `src/Neo.L2.Challenge/` |
| Cross-chain messaging | ✅ | `src/Neo.L2.Messaging/` |
| Asset registry + deposit/withdrawal processors | ✅ | `src/Neo.L2.Bridge/` |
| Per-L2 RPC method surface | ✅ | `src/Neo.Plugins.L2Rpc/` (10 methods) |
| Phase-5 proof aggregation | 🟡 | `src/Neo.Plugins.L2Gateway/` (BinaryTreeAggregator + IRoundProver pluggable; default = pass-through) |

**16 off-chain libraries + 8 plugins.** All have `tests/Neo.*.UnitTests/` mirrors;
1025 tests across 29 projects pass.

---

## Layer 3 — Operator tooling

| Component | Status | Code |
|-----------|:------:|------|
| Chain creation CLI (templates, scaffolding) | ✅ | `tools/Neo.Stack.Cli/` (`create-chain`) |
| Node-directory init | ✅ | `tools/Neo.Stack.Cli/` (`init-l2`) |
| Chain registration (configBytes hex emit) | ✅ | `tools/Neo.Stack.Cli/` (`register-chain`) |
| Bridge adapter deploy plan | ✅ | `tools/Neo.Stack.Cli/` (`deploy-bridge-adapter`) |
| Sequencer / batcher / prover preflight | ✅ | `tools/Neo.Stack.Cli/` (`start-{sequencer,batcher,prover}`) |
| Batch submission preflight | ✅ | `tools/Neo.Stack.Cli/` (`submit-batch`) |
| Config sanity-checker | ✅ | `tools/Neo.Stack.Cli/` (`validate`) |
| Declarative L1 deploy planner | ✅ | `tools/Neo.Hub.Deploy/` (`scaffold` / `plan` / `verify`) |
| Post-deploy wiring hints | ✅ | `tools/Neo.Hub.Deploy/` (`PostDeployActions`) |
| In-process devnet runner | ✅ | `tools/Neo.L2.Devnet/` (5 batches default; `--config`, `--data-dir`, `--metrics-port`) |
| Sample chain configs | ✅ | `samples/` (4 templates verified end-to-end) |

**3 CLI tools, 9 + 3 + 1 = 13 subcommands across them.**

---

## Layer 4 — Application development

| Component | Status | Code |
|-----------|:------:|------|
| L2 contract framework (compile to NeoVM bytecode) | ✅ | Uses `Neo.SmartContract.Framework` from `external/neo-devpack-dotnet/` (vendored) |
| L2-aware contract patterns documented | ✅ | `docs/launching-an-l2.md` (5 extension points + 3 worked examples) |
| Custom IDAWriter / ISequencerCommitteeProvider / IL2Prover examples | ✅ | `docs/launching-an-l2.md` (worked examples) |
| L2-side dApp examples | ✅ | `samples/contracts/` (cross-chain greeter + withdrawal demo) |
| Sample chain configs (rollup / gaming / validium / sidechain) | ✅ | `samples/*.config.json` (4 templates verified end-to-end) |
| App-developer SDK / client library (.NET, JS/TS, Rust) | 🔴 | **Out of repo.** Existing Neo SDKs (`Neo.RpcClient`, NeonJS, etc.) provide the base RPC client; the L2-specific RPC methods (`getl2batch` / `getsecuritylabel` / etc.) are documented in `Neo.Plugins.L2Rpc.L2RpcMethods` — wrap as needed for your language. |

---

## Layer 5 — End-user interfaces

| Component | Status | Notes |
|-----------|:------:|-------|
| Block explorer / indexer | 🔴 | **Out of repo.** Operators wire to existing tools (NEO N3 explorers, custom indexer pointing at `getl2batch` / `getl2stateroot` / `getl2withdrawalproof`). |
| Bridge UI / web portal | 🔴 | **Out of repo.** A web app calling `SharedBridge.Deposit` (L1) and `SharedBridge.FinalizeWithdrawalWithProof` is operator-built; the canonical 91-byte configBytes + RPC method shapes are documented for portal builders. |
| Documentation site (rendered) | 🔴 | **Out of repo.** This repo's markdown docs are the source. Operators using static-site generators (mdBook, Docusaurus, etc.) can render against `docs/`, `WHITEPAPER.md`, `ARCHITECTURE.md`. |
| Testnet faucet | 🔴 | **Out of repo.** Standard Neo N3 faucet patterns apply; the L2's bridge contract handles the L1→L2 deposit flow once L1 GAS is in hand. |
| Wallet integration (NEP-6 / Ledger / etc.) | 🔴 | **Out of repo.** `register-chain` emits configBytes hex paste-able into any Neo wallet's invokefunction call; `RpcSettlementClient.SignAndSendAsync` is a delegate operators wire to their preferred signer. |

---

## Coverage assessment

| Layer | Components | ✅ Done | 🟡 Scaffolded | 🔴 Out-of-repo |
|-------|-----------:|--------:|--------------:|---------------:|
| L1 protocol contracts | 13 | 13 | 0 | 0 |
| L2 native contracts | 6 | 6 | 0 | 0 |
| Node infrastructure | 19 | 17 | 2 | 0 |
| Operator tooling | 11 | 11 | 0 | 0 |
| App development | 6 | 5 | 0 | 1 |
| End-user UIs | 5 | 0 | 0 | 5 |
| **Total** | **60** | **52** | **2** | **6** |

The two 🟡 items are explicitly tracked in `IMPLEMENTATION_STATUS.md`'s Phase
matrix — both blocked on real ZK infrastructure (SP1 toolchain offline + matching
guest ELF for Phase 4; SP1 Compress / Halo2 / Risc0 fold for Phase 5). They're
scaffolded with the right plug-in points (`IL2Prover` / `IL2ProofVerifier` /
`IRoundProver`) so an operator with the toolchain can swap in production
backends without framework changes.

The six 🔴 items are explicitly out-of-scope. Layer 5 (end-user interfaces)
is operator territory in any L2 ecosystem — wallets, explorers, portals are
deployment-specific. Layer 4's missing piece (a typed SDK) is operator
territory too, since the canonical client depends on the operator's choice
of language and Neo SDK lineage.

## What's next

Plan items beyond the spec-gap-plan are tracked in `CHANGELOG.md`'s
`[Unreleased]` section. The L2 dev framework (this repo's primary scope) is
functionally complete per `doc.md` §0–§22; iteration ahead is on operator
ergonomics (more sample chains, more worked customization examples, more
dApp examples) rather than core architecture.
