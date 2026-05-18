# Neo Elastic Network (`neo4`)

[![build](https://github.com/r3e-network/neo-n4/actions/workflows/build.yml/badge.svg)](https://github.com/r3e-network/neo-n4/actions/workflows/build.yml)

> **A multi-L2 network on Neo 4 core, with a shared bridge, proof aggregation, and native cross-chain messaging.**

> [!IMPORTANT]
> **Independent implementation, not the official Neo 4 release.** This repository is
> an independent implementation of a multi-L2 elastic-network architecture on top of
> Neo's stack — **not endorsed by, affiliated with, or maintained by Neo Global
> Development (NGD), the Neo Foundation, or the
> [`neo-project`](https://github.com/neo-project) organization**. The "Neo 4" name
> refers to the *target core* used as the L2 execution kernel; the canonical Neo 4
> protocol roadmap is owned by the Neo project. The code is engineered for production
> L2 deployment — full cryptographic primitives, real persistence, comprehensive test
> coverage, and documented operator seams. Audit before mainnet use and wire the
> production seams (live L1 signer, real NeoFS adapter, dBFT consensus selector)
> per your deployment.

`neo4` is the consolidation repo for the **Neo Elastic Network** — a system that uses
the [`r3e-network/neo`](https://github.com/r3e-network/neo) Neo core fork as the L2
execution kernel. The fork tracks upstream `neo-project/neo` but gives N4 a dedicated
branch for native contracts and execution-kernel changes. Every L2 chain anchors to a
unified L1 contract suite (**NeoHub**) on Neo N3 / Neo 4 L1, and proofs and inter-L2
messages aggregate through an optional **Neo Gateway** layer.

The architecture borrows the *shared-bridge / chain-registry / proof-aggregation* pattern
from ZKsync Elastic Chain, rebuilt on Neo's stack: dBFT 2.0 finality, NEP-17 assets, NeoVM,
NeoFS data availability.

---

## Table of contents

1. [Architecture at a glance](#architecture-at-a-glance)
2. [What's in the repo](#whats-in-the-repo)
3. [Phased status](#phased-status)
4. [Quick start](#quick-start)
5. [Documentation map](#documentation-map)
6. [License](#license)

---

## Architecture at a glance

<p align="center">
  <img src="figures/architecture.svg" alt="Neo Elastic Network — three-tier architecture: L1 (NeoHub) anchor, optional Phase 5 Neo Gateway, and N elastic L2 execution chains" width="900">
</p>

The architecture is three tiers:

- **L1 (NeoHub on Neo N3 / Neo 4)** — canonical anchor. 23 contracts grouped into
  six concerns: *Settlement* (SettlementManager · VerifierRegistry), *Bridge*
  (SharedBridge · TokenRegistry · ChainRegistry), *Messaging* (MessageRouter · DARegistry),
  *Security* (SequencerRegistry · SequencerBond · ForcedInclusion · OptimisticChallenge),
  *Governance* (GovernanceController · EmergencyManager · GovernanceFraudVerifier · RestrictedExecutionFraudVerifier),
  and *External Bridge* (MpcCommitteeVerifier · ExternalBridgeRegistry · ExternalBridgeEscrow · ExternalBridgeBond · MpcCommitteeFraudVerifier · ExternalBridgeStubVerifier). Owns assets, settlement,
  message routing, and governance.
- **Neo Gateway (Phase 5, optional)** — aggregates many L2s' proofs into one settlement
  post on L1. `BinaryTreeAggregator` reduces in log-N rounds; `IRoundProver` ships in
  three production-grade implementations (`MultisigRoundProver` for committee-attested
  rounds, `MerklePathRoundProver` for per-leaf inclusion proofs against the aggregate
  root, `PassThroughRoundProver` as the minimal-cost reference). Recursive-ZK fold
  variants (SP1 Compress / Halo2 / Risc0) plug into the same seam when the operator
  brings the toolchain.
- **L2 chains (elastic, N of them)** — Neo 4 core as execution kernel, 8 L2 plugins,
  10 native L2 contracts per chain. Independent state, shared L1 anchor.

For a full English distillation of the architecture, see [`ARCHITECTURE.md`](../ARCHITECTURE.md).
For the formal technical document, see [`WHITEPAPER.md`](../WHITEPAPER.md).
For the master Chinese spec, see [`doc.md`](../doc.md).

---

## What's in the repo

- **Off-chain libraries (16)** — `Neo.L2.{Abstractions, Audit, Batch,
  Bridge, Censorship, Challenge, Executor, Executor.RiscV,
  ForcedInclusion, Messaging, Persistence, Proving, Sequencer,
  Settlement.Rpc, State, Telemetry}`. App SDK in `Neo.L2.Sdk` counted
  separately under App SDKs.
- **Persistence backends (2)** — `InMemoryKeyValueStore` (tests) ·
  `RocksDbKeyValueStore` (production default) — see
  [`docs/persistence.md`](./persistence.md).
- **Node plugins (8)** — `Neo.Plugins.L2{Batch, Bridge, DA, Gateway,
  Metrics, Prover, Rpc, Settlement}`.
- **Smart contracts (33)** — 23 NeoHub L1 (incl. `DAValidator`, `L1TxFilter`, `GovernanceFraudVerifier`,
  `RestrictedExecutionFraudVerifier` v3 trustless verifier, and the 6
  cross-foreign-chain bridge contracts: `MpcCommitteeVerifier` /
  `ExternalBridgeRegistry` / `ExternalBridgeEscrow` /
  `ExternalBridgeBond` / `ExternalBridgeStubVerifier` /
  `MpcCommitteeFraudVerifier`) + 10 L2 native (incl. bridged NEP-17, AA,
  interop verifier, and `L2NativeExternalBridgeContract`); all type-check via
  `Neo.SmartContract.Framework`.
- **CLI tools (7)** — `neo-stack`, `neo-l2-devnet`, `neo-hub-deploy`,
  `neo-l2-explore`, `neo-bridge`, `neo-l2-faucet`, `neo-external-bridge`.
- **App SDKs (3)** — `src/Neo.L2.Sdk/` (.NET) · `sdk/typescript/`
  (`@neo-n4/sdk`) · `sdk/rust/` (`neo-n4-sdk`) — all 10 RPC methods,
  same wire shape, same 4-class error taxonomy.
- **Web app (1)** — `sdk/web-explorer/index.html` — single static-file
  UI: Explore + Bridge + Faucet + state-root continuity Audit.
- **Docs site config (1)** — `book.toml` + `docs/SUMMARY.md` (mdBook).
- **Rust prover/core (3)** — `bridge/neo-execution-core/` (backend-neutral
  batch parsing, receipt/state folding, Merkle roots, public-input hash; no
  SP1/PolkaVM dependency) · `bridge/neo-zkvm-host/` (sp1-sdk 6.2.1 prover +
  `prove-batch daemon`) · `bridge/neo-zkvm-guest/` (the function being
  proved — RISC-V ELF, real Neo N3 VM via `neo_vm_guest::execute`).
- **Submodules (4)** — `external/neo` (`r3e-network/neo` fork, branch `r3e/neo-n4-core`) ·
  `external/neo-devpack-dotnet` (smart-contract devpack + nccs) ·
  `external/neo-riscv-vm` (PolkaVM-backed Neo RISC-V engine) ·
  `external/neo-zkvm` (Neo VM in pure Rust + SP1 prover crates). None
  are released on NuGet/crates.io for the versions tracked here.
- **Tests (1426 .NET + 159 cross-lang)** — 1426 across 34 .NET projects;
  15 TypeScript (vitest) + 10 Rust SDK (mockito) + 5 shared execution-core
  + 7 SP1 guest (host)
  + 101 Rust bridge watchers (eth 85 / tron 7 / sol 9) + 20 Foundry + 1 Solana Anchor —
  all green.

```
neo4/
├── README.md  ARCHITECTURE.md  WHITEPAPER.md  CHANGELOG.md
├── IMPLEMENTATION_STATUS.md  CONTRIBUTING.md  AGENTS.md  LICENSE
├── doc.md                                  # master spec (Chinese, authoritative)
├── docs/                                   # operator + integrator guides
├── src/
│   ├── Neo.L2.Abstractions/                # interfaces + records (doc.md §19)
│   ├── Neo.L2.{Batch,State,Bridge,Messaging,Executor}/
│   ├── Neo.L2.{Sequencer,ForcedInclusion,Censorship,Challenge,Audit}/
│   ├── Neo.L2.Persistence/                   # IL2KeyValueStore + RocksDB
│   ├── Neo.L2.Proving/                       # Stage 0/1 + RiscVZk testing seam
│   ├── Neo.L2.Settlement.Rpc/
│   ├── Neo.L2.Telemetry/                   # IL2Metrics + PrometheusExporter
│   └── Neo.Plugins.L2{Batch,Bridge,DA,Gateway,Metrics,Prover,Rpc,Settlement}/
├── contracts/
│   ├── NeoHub.* (23)                       # L1 contract suite
│   └── L2Native.* (10)                     # on-L2 native contracts
├── tools/
│   ├── Neo.Stack.Cli/                      # neo-stack CLI (12 subcommands)
│   ├── Neo.L2.Devnet/                      # in-process end-to-end demo runner
│   └── Neo.Hub.Deploy/                     # declarative L1 deploy planner
├── samples/
│   ├── *.config.json (4)                   # ready-to-run chain configs
│   ├── contracts/                          # Sample.CrossChainGreeter, Sample.WithdrawalDemo
│   └── executors/                          # Sample.CounterChainExecutor + scaffold target
├── bridge/
│   ├── neo-execution-core/                 # backend-neutral batch fold + public input hash
│   ├── neo-zkvm-guest/                     # Rust → RISC-V ELF (real Neo VM, SP1-proven)
│   └── neo-zkvm-host/                      # sp1-sdk 6.2.1 prover daemon (prove-batch)
└── tests/                                  # 1426 tests / 34 projects
```

---

## Phased status

Per [`doc.md` §18](../doc.md):

| Phase | Goal                                | Status | Evidence                                                  |
| ----- | ----------------------------------- | :----: | --------------------------------------------------------- |
| 0     | Sidechain PoC                       | ✅     | MVP integration test passes end-to-end                    |
| 1     | NeoHub v0 + Shared Bridge           | ✅     | All 23 NeoHub contracts compile; deploy planner emits 22-step bundle (15 core + 2 fraud verifiers + 5 external-bridge) |
| 2     | Batch Settlement                    | ✅     | Real `KeyedStateStore` continuity verified across batches |
| 3     | Optimistic Challenge Window         | ✅     | `OptimisticChallenge` contract + `BisectionGame` (log-N narrowing) |
| 4     | NeoVM 2 / RISC-V ZK Validity Proof  | 🟡     | SP1 FFI bridge scaffolded; `--features real-prover` flips to native |
| 5     | Neo Gateway proof aggregation       | ✅     | `BinaryTreeAggregator` ships 3 production `IRoundProver`s: `MultisigRoundProver` (Secp256r1 threshold-attested) · `MerklePathRoundProver` (per-leaf inclusion proofs) · `PassThroughRoundProver` (reference) |
| 6     | Neo Stack CLI / templates           | ✅     | 12 subcommands functional (3 print operator-plan output for the L1/L2-wallet-gated steps; `validate` is a pure JSON sanity-check; `scaffold-executor` emits a custom-executor starter project; `new-l2` is the composite; `list-templates` prints discoverable template + use-case descriptions) |

Legend: ✅ done · 🟡 substantial scaffolding + tests · 🔴 stub.

Detailed coverage per project: [`IMPLEMENTATION_STATUS.md`](../IMPLEMENTATION_STATUS.md).

---

## Quick start

**Requires** .NET 10 SDK (`dotnet --version` must report `10.0.x`). The
[`r3e-network/neo`](https://github.com/r3e-network/neo) Neo core fork is vendored as a
git submodule at `external/neo` on branch `r3e/neo-n4-core` (it is never released on
NuGet; project references go directly at the source tree). `neo-project/neo` is kept as
the read-only upstream source for controlled syncs, not as this repo's build dependency.

```bash
git clone --recurse-submodules https://github.com/r3e-network/neo-n4
cd neo-n4

# If you forgot --recurse-submodules:
# git submodule update --init --recursive

# Type-check everything + run all 1426 tests (~10 seconds)
dotnet test Neo.L2.sln /p:NuGetAudit=false

# --- Bootstrapping a new L2 chain (recommended path) ---

# See available templates + their use-case descriptions
dotnet run --project tools/Neo.Stack.Cli -- list-templates

# Composite: chain.config.json + node working dirs + custom-executor scaffold
# + sibling MSTest project, all in one command. Default output: ./chain-1099/
dotnet run --project tools/Neo.Stack.Cli -- new-l2 \
    --name MyChain --chain-id 1099 --template rollup

# Build + test the scaffolded executor
dotnet build chain-1099/MyChainExecutor /p:NuGetAudit=false
dotnet test  chain-1099/MyChainExecutor.UnitTests /p:NuGetAudit=false

# --- Devnet preview (in-process end-to-end) ---

# Run the default devnet (5 batches, real state-root continuity, post-run audit)
dotnet run --project tools/Neo.L2.Devnet -- 5

# Or preview the working Sample.CounterChainExecutor through the same pipeline
dotnet run --project tools/Neo.L2.Devnet -- 5 --executor counter

# Or preview your own chain config (the post-run getsecuritylabel reflects §16.2)
dotnet run --project tools/Neo.L2.Devnet -- 5 --config chain-1099/chain.config.json

# Same plus a live HTTP /metrics + /healthz + /readyz scrape at :9090
dotnet run --project tools/Neo.L2.Devnet -- 5 --metrics-port 9090

# Persist devnet state to disk via RocksDB (state survives restart)
dotnet run --project tools/Neo.L2.Devnet -- 5 --data-dir /tmp/neo-l2-devnet

# --- L1 deploy (when ready) ---

# Generate a NeoHub deploy bundle (20 contracts, declarative, dependency-resolved)
dotnet run --project tools/Neo.Hub.Deploy -- scaffold \
    --output deploy-plan.json
dotnet run --project tools/Neo.Hub.Deploy -- plan \
    --plan deploy-plan.json --output bundle.json

# Type-check a smart contract (no nccs required)
dotnet build contracts/NeoHub.ChainRegistry /p:NuGetAudit=false /p:DisableNccs=true
```

A 5-minute walkthrough is in [`docs/getting-started.md`](./getting-started.md).

---

## Documentation map

| Document                                                                | Audience              | Purpose                                                              |
| ----------------------------------------------------------------------- | --------------------- | -------------------------------------------------------------------- |
| [`README.md`](./README.md)                                              | everyone              | This file. What is `neo4`, how to run it.                            |
| [`WHITEPAPER.md`](../WHITEPAPER.md)                                      | architects, reviewers | Formal technical document — design, security model, comparison.      |
| [`ARCHITECTURE.md`](../ARCHITECTURE.md)                                  | engineers             | English distillation of `doc.md` §-by-§ for quick cross-reference.   |
| [`doc.md`](../doc.md)                                                    | spec authors          | Master architecture spec (Chinese, authoritative).                   |
| [`IMPLEMENTATION_STATUS.md`](../IMPLEMENTATION_STATUS.md)                | reviewers             | What's built vs deferred, per project.                               |
| [`CHANGELOG.md`](../CHANGELOG.md)                                        | reviewers             | Per-iteration change log.                                            |
| [`docs/getting-started.md`](./getting-started.md)                  | new contributors      | Clone → test → run devnet in 5 minutes.                              |
| [`docs/launching-an-l2.md`](./launching-an-l2.md)                  | L2 operators          | 5-command path to a registered L2 chain + every plug-in point for custom logic (executor / DA / prover / sequencer). Templates: rollup / zk-rollup / validium / sidechain. |
| [`samples/`](../samples/README.md)                                       | L2 operators          | 4 ready-to-run sample chain configs covering distinct use cases (general-rollup / gaming-rollup / exchange-validium / privacy-sidechain), each verified end-to-end via `neo-l2-devnet --config`. |
| [`samples/contracts/`](../samples/contracts/README.md)                   | dApp developers       | Sample L2-aware app contracts (`CrossChainGreeter`, `WithdrawalDemo`) showing standard patterns for integrating with `L2Native.*`. |
| [`docs/tech-stack-coverage.md`](./tech-stack-coverage.md)          | reviewers             | Honest gap analysis of L2-stack coverage — 51 components ✅, 2 🟡 (Phase 4/5 ZK infra), 6 🔴 (out-of-repo: SDKs, explorer, portal, faucet, wallet integration). |
| [`docs/architecture-walkthrough.md`](./architecture-walkthrough.md) | engineers             | Narrative tour mapping every `doc.md` section to code.               |
| [`docs/core-fork-policy.md`](./core-fork-policy.md)                | maintainers           | How `external/neo` tracks the `r3e-network/neo` fork and where N4 core/native-contract changes land. |
| [`docs/telemetry.md`](./telemetry.md)                              | operators             | Metric catalog, wiring example, Prometheus exposition format.        |
| [`docs/security-model.md`](./security-model.md)                    | operators, reviewers  | What L1 guarantees, threat → mitigation table, operator checklist.   |
| [`docs/persistence.md`](./persistence.md)                          | operators             | RocksDB-backed durable state — IL2KeyValueStore, per-component wiring, operator checklist. |
| [`docs/figures/`](./figures/)                                      | everyone              | Figure gallery — 6 hand-tuned SVGs reused across README + walkthrough + whitepaper + security-model. |
| [`CONTRIBUTING.md`](../CONTRIBUTING.md)                                  | contributors          | Layout, conventions, PR checklist.                                   |
| [`AGENTS.md`](../AGENTS.md)                                              | AI tooling            | Guide for AI-assisted contributors.                                  |

---

## License

MIT — see [`LICENSE`](../LICENSE).
