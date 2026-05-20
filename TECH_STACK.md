# Neo N4 — Tech Stack at a Glance

One-page developer onboarding for the Neo Elastic Network repo. Counts verified against the codebase on 2026-05-19.

---

## 1. What is Neo N4?

**Neo N4 (a.k.a. Neo Elastic Network)** is a multi-L2 network built on top of the [`r3e-network/neo`](https://github.com/r3e-network/neo) Neo core fork. The fork reserves `r3e/neo-n3-core` for minimal L1 core hooks based on upstream `master-n3`, and `r3e/neo-n4-core` for L2 core work based on upstream `master`. Many L2 chains anchor to one shared deployable L1 contract suite (**NeoHub**) on Neo N3 / Neo 4 L1, with NeoVM2/RISC-V as the standard L2 execution target, legacy NeoVM compatibility only for N3-era checks, pluggable proofs (multisig / optimistic / ZK), pluggable data availability (L1 / NeoFS / DAC), and optional cross-L2 proof aggregation (**Neo Gateway**).

Independent implementation — not the official Neo 4 release, not endorsed by Neo Global Development, Neo Foundation, or the `neo-project` organization. Architecture borrows the *shared-bridge + chain-registry + proof-aggregation* pattern from ZKsync Elastic Chain, rebuilt on Neo's stack (dBFT 2.0, NEP-17, NeoVM2/RISC-V, NeoFS). See [`README.md`](README.md) for the full provenance disclosure and operator responsibilities; [`SECURITY.md`](SECURITY.md) for the vulnerability-disclosure process.

---

## 2. Architecture — the 5 layers

```
+--------------------------------------------------------------------------+
| Layer 5 - End-user interfaces                                            |
|   Web explorer | TS/Rust/.NET SDKs | Faucet CLI | neo-l2-explore         |
+--------------------------------------------------------------------------+
| Layer 4 - Application development                                        |
|   Sample dApps | Sample executor | Scaffold-executor templates | mdBook  |
+--------------------------------------------------------------------------+
| Layer 3 - Operator tooling                                               |
|   neo-stack CLI | neo-hub-deploy | neo-external-bridge | Devnet | Audit  |
+--------------------------------------------------------------------------+
| Layer 2 - Node infrastructure                                            |
|   8 plugins (Batch / Bridge / DA / Gateway / Metrics / Prover /          |
|              Rpc / Settlement)                                           |
|   16 off-chain libs (Batch / State / Bridge / Messaging / Proving /      |
|                       Executor / Audit / Persistence / Telemetry / ...)  |
|   Bridge crates (zkvm-host / guest) | Watchers (eth / tron / sol)        |
+--------------------------------------------------------------------------+
| Layer 1 - Protocol contracts (on-chain)                                  |
|   NeoHub L1 deployable suite (24: 23 prod + 1 stub) | L2 native contracts (10) |
|   Foreign-side routers (EVM family Solidity | Solana Anchor program)     |
+--------------------------------------------------------------------------+
                                    |
                                    v
+--------------------------------------------------------------------------+
| Maintained dependency - Neo core fork (r3e-network/neo)                  |
|   NeoVM2/RISC-V | Native contracts | dBFT | RpcServer | ApplicationEngine        |
+--------------------------------------------------------------------------+
```

Per-component detail lives in [`docs/tech-stack-coverage.md`](docs/tech-stack-coverage.md).

---

## 3. Module inventory

| Category | Count | Where |
|----------|------:|-------|
| Smart contracts — NeoHub L1 suite | 23 | `contracts/NeoHub.*/` |
| Smart contracts — L2 native | 10 | `external/neo/src/Neo/SmartContract/Native/L2NativeContracts.cs` |
| Smart contracts — sample dApps | 2 | `samples/contracts/Sample.*/` |
| Sample executor (reference) | 1 | `samples/executors/Sample.CounterChainExecutor/` |
| Foreign-side on-chain programs | 2 | `external/foreign-contracts/eth/` (Solidity, deploys to 14 EVM chains) + `external/foreign-contracts/sol/` (Solana Anchor) |
| Off-chain .NET libraries | 16 | `src/Neo.L2.*/` (the .NET SDK in `src/Neo.L2.Sdk/` is counted separately under "App SDKs" below to match the README accounting) |
| neo-node plugins | 8 | `src/Neo.Plugins.L2*/` |
| CLI tools | 7 | `tools/Neo.*.Cli/`, `tools/Neo.L2.Devnet/`, `tools/Neo.Hub.Deploy/`, `tools/Neo.L2.Explore/` (`tools/manuscript/` is a PDF/EPUB build-script directory, not a CLI) |
| Rust crates — bridge | 2 | `bridge/neo-zkvm-{host,guest}/` |
| Rust crates — watchers | 3 | `watchers/neo-bridge-watcher-{eth,tron,sol}/` |
| App SDKs (TS / Rust / .NET) | 3 | `sdk/typescript/`, `sdk/rust/`, `src/Neo.L2.Sdk/` — 11 RPC methods × 3 langs, parity-pinned |
| Web explorer (static-file dApp) | 1 | `sdk/web-explorer/index.html` |
| Test projects | 34 | `tests/Neo.L2.*.UnitTests/` + `tests/Neo.L2.IntegrationTests/` + sample test projects |
| Documentation pages | 20 EN + 22 zh | `docs/*.md`, `docs/zh/*.md` |

**Total runnable code modules: 80** (24 deployable NeoHub contracts — 23 production + 1 test-only stub — + 10 Neo core native L2 contracts + 2 samples + 1 executor + 2 foreign + 16 libs + 8 plugins + 7 tools + 3 bridge crates + 3 watchers + 3 SDKs + 1 web).

---

## 4. Verification state

| Check | Result |
|-------|--------|
| .NET tests | **1455 passing across 34 projects, 0 failures** |
| Cross-language tests | **202 passing** (16 TS + 10 Rust SDK + 5 shared execution-core + 7 SP1 guest + 103 watcher with `--features live-rpc` (87 eth + 7 tron + 9 sol) + 39 Foundry (32 single + 7 multi) + 22 Solana router) |
| Real-CPU SP1 proof generation | **2 ignored release-gate tests** (~40s prove, ~20s verify, 2.78 MB proof artifact) |
| **Base tests green** | **1617** |
| Smart contract artifacts | 24/24 deployable `.nef` + `.manifest.json` compile cleanly via `nccs 3.9.1`; 10 L2 native contracts pass Neo core native tests |
| Devnet 5-batch end-to-end | green (state-root continuity, multisig proofs, audit pass) |
| `dotnet build Neo.L2.sln` | 99 solution projects, 0 errors, 0 warnings |

---

## 5. Open development work

**37 actionable tasks total, 15 closed, 22 remaining** — see [`TASKS.md`](TASKS.md) for the full checklist.

| Repo | Total | Closed | Remaining | Remaining breakdown |
|------|------:|-------:|----------:|---------------------|
| Neo L2 Core (`r3e-network/neo`, branch `r3e/neo-n4-core`) | 10 | 0 | 10 | 4 critical (ChainMode + GAS / NEO / Policy gating) · 3 high (RpcServer source, OnPersist hook, optional Oracle) · 3 medium (dBFT hook, restricted-state mode, RISC-V mode) |
| This repo (`neo4`) | 24 | 15 | 9 | 4 production-readiness examples · 2 future features · 1 spec-gap deferred (§8-witness-canonical) · 2 ecosystem items (more samples, Python/Go SDKs) |
| Cross-repo coordination | 3 | 0 | 3 | L2 bootstrap handoff · RpcServer migration · RISC-V mode promotion |
| **Totals** | **37** | **15** | **22** | |

ZKsync parity tracking comes from [`docs/zksync-comparison.md`](docs/zksync-comparison.md),
which maps every ZKsync v29 component to its neo4 equivalent and identifies the
remaining gaps worth closing as the framework matures.

---

## 6. Where to implement what

| If you want to change... | Goes in... | Why |
|--------------------------|-----------|-----|
| NeoVM2/RISC-V execution / native contracts | **Core fork** (`r3e-network/neo`) | L1 core deltas tracked on `r3e/neo-n3-core`; L2 execution-kernel and N4-native deltas tracked on `r3e/neo-n4-core` |
| dBFT consensus / RpcServer plugin | **Core** | Same |
| L1 contracts (NeoHub) | This repo → `contracts/NeoHub.*/` | All 24 L1 contracts live here (23 production + 1 test-only stub) |
| L2 native contracts | **Core fork** (`external/neo/src/Neo/SmartContract/Native/L2NativeContracts.cs`) | Native contracts registered by Neo core at genesis |
| Batch / state / messaging logic | This repo → `src/Neo.L2.{Batch,State,Messaging}/` | Pure off-chain libraries |
| Sequencer / batcher / prover runtime | This repo → `src/Neo.L2.{Sequencer,Batch,Proving}/` + `src/Neo.Plugins.L2*/` | Plugin-based runtime |
| Off-chain fraud-proof generation | This repo → `src/Neo.L2.Challenge/` | Bisection game + payload encoding |
| On-chain fraud verifiers | This repo → `contracts/NeoHub.{Governance,RestrictedExecution,MpcCommittee}FraudVerifier/` | 3 verifier variants |
| Cross-foreign-chain bridge (Eth / Tron / Sol) | This repo → `contracts/NeoHub.External*/` + `watchers/neo-bridge-watcher-*/` + `external/foreign-contracts/` | Multi-chain integration |
| Operator CLI / devnet | This repo → `tools/Neo.*.Cli/` | Operator tooling |
| Application dApp examples | This repo → `samples/contracts/Sample.*/` | Developer-facing references |
| SDK API surface | This repo → `sdk/typescript/`, `sdk/rust/`, `src/Neo.L2.Sdk/` | 11 RPC methods × 3 SDKs, parity-pinned |

### Ownership rule (one sentence)

> Anything touching **NeoVM2/RISC-V execution semantics, native contracts, dBFT consensus, or `RpcServer` plugin internals** belongs in **core**. Everything else belongs in **this repo**.

---

## 7. Quick start

```bash
# Build everything (~10s)
dotnet build Neo.L2.sln /p:NuGetAudit=false

# Run all .NET tests (1455 tests, ~30s)
dotnet test Neo.L2.sln /p:NuGetAudit=false

# Run the in-process devnet (5 batches, full pipeline)
dotnet run --project tools/Neo.L2.Devnet -- 5

# Smart contracts (requires nccs on PATH)
PATH="$HOME/.dotnet/tools:$PATH" dotnet build contracts/NeoHub.ChainRegistry

# Rust crates (watcher tests)
cd watchers/neo-bridge-watcher-eth && cargo test --release

# Real SP1 proof generation (~100s for both ignored tests)
cd bridge/neo-zkvm-host && cargo test --release --tests -- --ignored

# Scaffold a starter L2
dotnet run --project tools/Neo.Stack.Cli -- new-l2 --name my-l2

# Inspect any L2 chain
dotnet run --project tools/Neo.L2.Explore -- audit --endpoint <url> --chain-id <id>
```

---

## 8. Deeper docs

| Topic | Doc |
|-------|-----|
| Per-component layer matrix | [`docs/tech-stack-coverage.md`](docs/tech-stack-coverage.md) |
| Architecture overview | [`ARCHITECTURE.md`](ARCHITECTURE.md) |
| L1 vs L2 split | [`docs/architecture-l1-vs-l2.md`](docs/architecture-l1-vs-l2.md) |
| L2 batch lifecycle | [`docs/architecture-l2-lifecycle.md`](docs/architecture-l2-lifecycle.md) |
| Trust boundaries | [`docs/architecture-trust-boundaries.md`](docs/architecture-trust-boundaries.md) |
| Wire formats (byte-level) | [`docs/architecture-wire-formats.md`](docs/architecture-wire-formats.md) |
| Glossary | [`docs/architecture-glossary.md`](docs/architecture-glossary.md) |
| Launching an L2 | [`docs/launching-an-l2.md`](docs/launching-an-l2.md) |
| Spec gap plan | [`docs/spec-gap-plan.md`](docs/spec-gap-plan.md) |
| Implementation status (full) | [`IMPLEMENTATION_STATUS.md`](IMPLEMENTATION_STATUS.md) |
| Open tasks (checklist) | [`TASKS.md`](TASKS.md) |
