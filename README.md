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
> protocol roadmap is owned by the Neo project. The code in this repo is engineered
> for production deployment of L2 chains — full cryptographic primitives, real
> persistence, comprehensive test coverage, and documented operator seams. Provenance
> aside, treat it as you would any third-party implementation of a public protocol:
> review the [security model](docs/security-model.md), audit before mainnet use, and
> wire the documented production seams (live L1 signer, real NeoFS adapter, dBFT
> consensus selector) per your deployment.

`neo4` is the consolidation repo for the **Neo Elastic Network** — a system that uses
[`neo-project/neo`](https://github.com/neo-project/neo) Neo 4 core as the L2 execution
kernel, anchors every L2 chain to a unified L1 contract suite (**NeoHub**) on Neo N3 / Neo 4
L1, and aggregates proofs and inter-L2 messages through an optional **Neo Gateway** layer.

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
  <img src="docs/figures/architecture.svg" alt="Neo Elastic Network — three-tier architecture: L1 (NeoHub) anchor, optional Phase 5 Neo Gateway, and N elastic L2 execution chains" width="900">
</p>

The architecture is three tiers:

- **L1 (NeoHub on Neo N3 / Neo 4)** — canonical anchor. 21 contracts grouped into
  five concerns: *Settlement* (SettlementManager · VerifierRegistry), *Bridge*
  (SharedBridge · TokenRegistry · ChainRegistry), *Messaging* (MessageRouter · DARegistry),
  *Security* (SequencerRegistry · SequencerBond · ForcedInclusion · OptimisticChallenge),
  and *Governance* (GovernanceController · EmergencyManager). Owns assets, settlement,
  message routing, and governance.
- **Neo Gateway (Phase 5, optional)** — aggregates many L2s' proofs into one settlement
  post on L1. `BinaryTreeAggregator` reduces in log-N rounds; `IRoundProver` ships in
  three production-grade implementations (`MultisigRoundProver` for committee-attested
  rounds, `MerklePathRoundProver` for per-leaf inclusion proofs against the aggregate
  root, `PassThroughRoundProver` as the minimal-cost reference). Recursive-ZK fold
  variants (SP1 Compress / Halo2 / Risc0) plug into the same seam when the operator
  brings the toolchain.
- **L2 chains (elastic, N of them)** — Neo 4 core as execution kernel, 8 L2 plugins,
  7 native L2 contracts per chain. Independent state, shared L1 anchor.

For a full English distillation of the architecture, see [`ARCHITECTURE.md`](./ARCHITECTURE.md).
For the formal technical document, see [`WHITEPAPER.md`](./WHITEPAPER.md).
For the master Chinese spec, see [`doc.md`](./doc.md).

---

## What's in the repo

| Area              | Count     | Description                                                              |
| ----------------- | --------- | ------------------------------------------------------------------------ |
| Off-chain libraries | **16**  | `Neo.L2.{Abstractions,Audit,Batch,Bridge,Censorship,Challenge,Executor,Executor.RiscV,ForcedInclusion,Messaging,Persistence,Proving,Sequencer,Settlement.Rpc,State,Telemetry}` (App SDK in `Neo.L2.Sdk` is counted separately under App SDKs) |
| Persistence backends | **2**  | `InMemoryKeyValueStore` (tests) · `RocksDbKeyValueStore` (production default) — see [`docs/persistence.md`](./docs/persistence.md) |
| Node plugins      | **8**     | `Neo.Plugins.L2{Batch,Bridge,DA,Gateway,Metrics,Prover,Rpc,Settlement}`  |
| Smart contracts   | **28**    | 21 NeoHub L1 (Phase 0–3 + 6 cross-foreign-chain bridge contracts: `MpcCommitteeVerifier`, `ExternalBridgeRegistry`, `ExternalBridgeEscrow`, `ExternalBridgeBond`, `ExternalBridgeStubVerifier`, `MpcCommitteeFraudVerifier`) + 7 L2 native (incl. `L2NativeExternalBridgeContract`); all type-check via `Neo.SmartContract.Framework` |
| CLI tools         | **7**     | `neo-stack`, `neo-l2-devnet`, `neo-hub-deploy`, `neo-l2-explore`, `neo-bridge`, `neo-l2-faucet`, `neo-external-bridge` |
| App SDKs          | **3**     | `src/Neo.L2.Sdk/` (.NET) · `sdk/typescript/` (`@neo-n4/sdk`) · `sdk/rust/` (`neo-n4-sdk`) — all 10 RPC methods, same wire shape, same 4-class error taxonomy |
| Web app           | **1**     | `sdk/web-explorer/index.html` — single static-file UI: Explore + Bridge + Faucet + state-root continuity Audit |
| Docs site config  | **1**     | `book.toml` + `docs/SUMMARY.md` (mdBook) |
| Rust prover       | **2**     | `bridge/neo-zkvm-host/` (sp1-sdk 6.2.1 prover + `prove-batch daemon`) · `bridge/neo-zkvm-guest/` (the function being proved — compiles to RISC-V ELF, executes real Neo N3 VM via `neo_vm_guest::execute`) |
| Foreign-chain integrations | **6** | Watchers (3): `watchers/neo-bridge-watcher-eth/` (secp256k1+SHA256, **serves the entire EVM family** — Ethereum, Tron, BSC, Polygon, Arbitrum, Optimism, Base, Avalanche, Linea, zkSync Era, Scroll, Mantle, Fantom, Celo — via one chain-id-driven daemon binary; 32 base tests + 55 live-RPC integration tests = 87 with `--features live-rpc`. Production daemon ships **graceful SIGTERM shutdown**, **`/healthz`+`/info` HTTP endpoints**, **`/metrics` Prometheus exposition**, **per-chain `min_confirmations` reorg buffer**, and **`flock`-based concurrent-instance detection** on the journal directory; reference k8s + systemd manifests in [`watchers/neo-bridge-watcher-eth/deploy/`](./watchers/neo-bridge-watcher-eth/deploy/)) · `.../-tron/` (thin re-export with Tron chain-ids `0xE0000010..12`, 7 tests) · `.../-sol/` (ed25519-dalek + Solana chain-ids `0xE0000020..22`, 9 tests; curve-agnostic `Signer` trait dispatches to `CryptoLib.VerifyWithEd25519` on-chain). Foreign-side routers (3): `external/foreign-contracts/eth/` (393-line Solidity that deploys unchanged on any EVM chain — constructor parameterizes `externalChainId`; **20 Foundry tests** = 13 single-chain + 7 multi-chain pinning per-instance state isolation across 17 canonical mainnet slots (14 family banks + Polygon zkEVM, Arbitrum Nova, Sonic variants)) · `.../tron/` (README — TVM is EVM-flavored Solidity, points at the Eth contract) · `.../sol/` (~638-line Anchor program using Solana's ed25519 sigverify precompile, source-only — operator runs `anchor build`). Canonical 16-slot family banks for the namespace + 5-step EVM-onboarding runbook in [`docs/external-bridge-evm-chains.md`](./docs/external-bridge-evm-chains.md). |
| Submodules        | **4**     | `external/neo` (Neo 4 core) · `external/neo-devpack-dotnet` (smart-contract devpack + nccs) · `external/neo-riscv-vm` (PolkaVM-backed Neo RISC-V engine) · `external/neo-zkvm` (Neo VM in pure Rust + SP1 prover crates). None are released on NuGet/crates.io for the versions tracked here. |
| Tests             | **1423 .NET + 155 cross-lang** | 1423 across 34 .NET projects (incl. 7 Phase-C real-secp256k1 fraud-proof tests pinning the equivocation slash path end-to-end, plus optimistic sequencer account/signature binding); 15 TypeScript (vitest) + 10 Rust SDK (mockito) + 8 SP1 guest (host) + 101 Rust bridge watchers with `live-rpc` (eth: 85, tron: 7, sol: 9 — both secp256k1 and ed25519 paths exercised) + 20 Foundry (Solidity — 13 single-chain + 7 multi-chain) + 1 Solana Anchor program test — all green on the Windows audit matrix; `neo-zkvm-host` SP1 E2E requires Linux/macOS |

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
│   ├── NeoHub.* (21)                       # L1 contract suite
│   └── L2Native.* (7)                      # on-L2 native contracts
├── tools/
│   ├── Neo.Stack.Cli/                      # neo-stack CLI (12 subcommands)
│   ├── Neo.L2.Devnet/                      # in-process end-to-end demo runner
│   └── Neo.Hub.Deploy/                     # declarative L1 deploy planner
├── samples/
│   ├── *.config.json (4)                   # ready-to-run chain configs
│   ├── contracts/                          # Sample.CrossChainGreeter, Sample.WithdrawalDemo
│   └── executors/                          # Sample.CounterChainExecutor + scaffold target
├── bridge/
│   ├── neo-zkvm-guest/                     # Rust → RISC-V ELF (real Neo VM, SP1-proven)
│   └── neo-zkvm-host/                      # sp1-sdk 6.2.1 prover daemon (prove-batch)
└── tests/                                  # 1423 tests / 34 projects
```

---

## Phased status

Per [`doc.md` §18](./doc.md):

| Phase | Goal                                | Status | Evidence                                                  |
| ----- | ----------------------------------- | :----: | --------------------------------------------------------- |
| 0     | Sidechain PoC                       | ✅     | MVP integration test passes end-to-end                    |
| 1     | NeoHub v0 + Shared Bridge           | ✅     | All 21 NeoHub contracts compile; deploy planner emits 20-step bundle (13 core + 2 fraud verifiers + 5 external-bridge) |
| 2     | Batch Settlement                    | ✅     | Real `KeyedStateStore` continuity verified across batches |
| 3     | Optimistic Challenge Window         | ✅     | `OptimisticChallenge` contract + `BisectionGame` (log-N narrowing) |
| 4     | NeoVM 2 / RISC-V ZK Validity Proof  | 🟡     | SP1 FFI bridge scaffolded; `--features real-prover` flips to native |
| 5     | Neo Gateway proof aggregation       | ✅     | `BinaryTreeAggregator` ships 3 production `IRoundProver`s: `MultisigRoundProver` (Secp256r1 threshold-attested) · `MerklePathRoundProver` (per-leaf inclusion proofs) · `PassThroughRoundProver` (reference) |
| 6     | Neo Stack CLI / templates           | ✅     | 12 subcommands functional (3 print operator-plan output for the L1/L2-wallet-gated steps; `validate` is a pure JSON sanity-check; `scaffold-executor` emits a custom-executor starter project; `new-l2` is the composite; `list-templates` prints discoverable template + use-case descriptions) |

Legend: ✅ done · 🟡 substantial scaffolding + tests · 🔴 stub.

Detailed coverage per project: [`IMPLEMENTATION_STATUS.md`](./IMPLEMENTATION_STATUS.md).

---

## Quick start

**Requires** .NET 10 SDK (`dotnet --version` must report `10.0.x`). The
[`neo-project/neo`](https://github.com/neo-project/neo) Neo 4 core is vendored as a
git submodule at `external/neo` (it is never released on NuGet; project references
go directly at the source tree).

```bash
git clone --recurse-submodules https://github.com/r3e-network/neo-n4
cd neo-n4

# If you forgot --recurse-submodules:
# git submodule update --init --recursive

# Type-check everything + run all 1423 tests (~10 seconds)
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
dotnet run --project tools/Neo.Hub.Deploy -- scaffold --output deploy-plan.json
dotnet run --project tools/Neo.Hub.Deploy -- plan     --plan deploy-plan.json --output bundle.json

# Type-check a smart contract (no nccs required)
dotnet build contracts/NeoHub.ChainRegistry /p:NuGetAudit=false /p:DisableNccs=true
```

A 5-minute walkthrough is in [`docs/getting-started.md`](./docs/getting-started.md).

---

## Documentation map

| Document                                                                | Audience              | Purpose                                                              |
| ----------------------------------------------------------------------- | --------------------- | -------------------------------------------------------------------- |
| [`README.md`](./README.md)                                              | everyone              | This file. What is `neo4`, how to run it.                            |
| [`WHITEPAPER.md`](./WHITEPAPER.md)                                      | architects, reviewers | Formal technical document — design, security model, comparison.      |
| [`ARCHITECTURE.md`](./ARCHITECTURE.md)                                  | engineers             | English distillation of `doc.md` §-by-§ for quick cross-reference.   |
| [`doc.md`](./doc.md)                                                    | spec authors          | Master architecture spec (Chinese, authoritative).                   |
| [`IMPLEMENTATION_STATUS.md`](./IMPLEMENTATION_STATUS.md)                | reviewers             | What's built vs deferred, per project.                               |
| [`CHANGELOG.md`](./CHANGELOG.md)                                        | reviewers             | Per-iteration change log.                                            |
| [`docs/getting-started.md`](./docs/getting-started.md)                  | new contributors      | Clone → test → run devnet in 5 minutes.                              |
| [`docs/launching-an-l2.md`](./docs/launching-an-l2.md)                  | L2 operators          | 5-command path to a registered L2 chain + every plug-in point for custom logic (executor / DA / prover / sequencer). Templates: rollup / zk-rollup / validium / sidechain. |
| [`samples/`](./samples/README.md)                                       | L2 operators          | 4 ready-to-run sample chain configs covering distinct use cases (general-rollup / gaming-rollup / exchange-validium / privacy-sidechain), each verified end-to-end via `neo-l2-devnet --config`. |
| [`samples/contracts/`](./samples/contracts/README.md)                   | dApp developers       | Sample L2-aware app contracts (`CrossChainGreeter`, `WithdrawalDemo`) showing standard patterns for integrating with `L2Native.*`. |
| [`docs/tech-stack-coverage.md`](./docs/tech-stack-coverage.md)          | reviewers             | Honest gap analysis of L2-stack coverage — 78 components ✅, 0 🟡, 0 🔴 (Phase 4 SP1 ZK end-to-end functional; cross-foreign-chain bridge Phase B/C complete; Layer-4/5 SDKs + web app + mdBook all in-tree). |
| [`docs/architecture-atlas.md`](./docs/architecture-atlas.md) | everyone              | **Front door for the architecture docs.** Reading order by role + cross-reference between the 5 chapters: walkthrough (per-tx tour) · l2-lifecycle (system flow) · wire-formats (canonical bytes) · trust-boundaries (security view) · glossary (term + component catalog). ~2100 lines total, with 29 hand-tuned SVG figures (mirrored under `docs/zh/figures/architecture/`). |
| [`docs/architecture-walkthrough.md`](./docs/architecture-walkthrough.md) | engineers             | Narrative tour mapping every `doc.md` section to code.               |
| [`docs/telemetry.md`](./docs/telemetry.md)                              | operators             | Metric catalog, wiring example, Prometheus exposition format.        |
| [`docs/security-model.md`](./docs/security-model.md)                    | operators, reviewers  | What L1 guarantees, threat → mitigation table, operator checklist.   |
| [`docs/persistence.md`](./docs/persistence.md)                          | operators             | RocksDB-backed durable state — IL2KeyValueStore, per-component wiring, operator checklist. |
| [`docs/figures/`](./docs/figures/)                                      | everyone              | Figure gallery — 6 hand-tuned SVGs reused across README + walkthrough + whitepaper + security-model. |
| [`CONTRIBUTING.md`](./CONTRIBUTING.md)                                  | contributors          | Layout, conventions, PR checklist.                                   |
| [`AGENTS.md`](./AGENTS.md)                                              | AI tooling            | Guide for AI-assisted contributors.                                  |

---

## License

MIT — see [`LICENSE`](./LICENSE).
