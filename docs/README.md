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
> protocol roadmap is owned by the Neo project. The code is undergoing production
> hardening: real cryptographic and persistence components ship, while general NeoVM
> fraud proofs plus independent Gateway audit and executed real-proof deployment evidence remain explicit open gates. Audit before mainnet use and wire the
> production seams (operator key/HSM policy, real NeoFS adapter, reviewed dBFT node bundle)
> per your deployment.

`neo4` is the consolidation repo for the **Neo Elastic Network** — a system that uses
the [`r3e-network/neo`](https://github.com/r3e-network/neo) Neo core fork as the L2
execution kernel. The fork has two maintained r3e branches: `r3e/neo-n3-core` tracks
upstream `master-n3` for L1 core work, while `r3e/neo-n4-core` tracks upstream `master`
for L2 execution-kernel and native-contract changes. Every L2 chain anchors to a
unified deployable L1 contract suite (**NeoHub**) on Neo N3 / Neo 4 L1, and proofs and
inter-L2 messages aggregate through an optional **Neo Gateway** layer. NeoHub is deployed
as contracts and extended through plugins/services where needed; it is not registered as
an L1 native-contract set.

The architecture borrows the *shared-bridge / chain-registry / proof-aggregation* pattern
from ZKsync Elastic Chain, rebuilt on Neo's stack: dBFT 2.0 finality, NEP-17 assets,
NeoVM2/RISC-V execution, and NeoFS data availability.

Platform assets are normalized at the L2 boundary. L1 NEO remains indivisible
(`decimals = 0`) and L1 GAS remains 8-decimal, while every N4 L2 exposes the
same built-in NEO, GAS, USDT, USDC, and BTC catalog. USDT/USDC are fixed at 6
decimals, BTC is fixed at 8 decimals, and NEO is represented on L2 with 8
decimals. The native L2 bridge records both L1 and L2 decimals per
`TokenRegistry` mapping, scales deposits/withdrawals exactly, and rejects lossy
fractional withdrawals such as non-whole L1 NEO exits.

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

- **L1 (NeoHub on Neo N3 / Neo 4)** — canonical anchor. 26 contract projects
  (24 production, 1 advisory-only structural fraud verifier, and 1 test-only stub) grouped into six concerns: *Settlement*
  (SettlementManager · VerifierRegistry · ContractZkVerifier · Sp1Groth16Verifier), *Bridge*
  (SharedBridge · TokenRegistry · ChainRegistry), *Messaging* (MessageRouter · DARegistry),
  *Security* (SequencerRegistry · SequencerBond · ForcedInclusion · OptimisticChallenge),
  *Governance* (GovernanceController · EmergencyManager · GovernanceFraudVerifier · RestrictedExecutionFraudVerifier),
  and *External Bridge* (MpcCommitteeVerifier · ExternalBridgeRegistry · ExternalBridgeEscrow · ExternalBridgeBond · MpcCommitteeFraudVerifier · ExternalBridgeStubVerifier). Owns assets, settlement,
  message routing, and governance. `ContractZkVerifier` keeps ZK settlement in the deployable NeoHub path while routing proof-system work to governance-registered deployable verifier contracts.
- **Neo Gateway (Phase 5, optional)** — aggregates many L2s' proofs into one settlement
  post on L1. `BinaryTreeAggregator` reduces in log-N rounds; `IRoundProver` ships in
  two production-grade implementations (`MultisigRoundProver` for committee-attested
  rounds, `MerklePathRoundProver` for per-leaf inclusion proofs against the aggregate
  root) plus `PassThroughRoundProver` as the minimal-cost reference. The dedicated
  `neo-zkvm-gateway-{guest,host}` crates bundle the SP1 6.2.1 recursive terminal proof;
  Halo2 / Risc0 alternatives plug into the same seam when the operator
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
- **Smart contracts (26 NeoHub + 10 L2 native)** — 26 NeoHub L1 contract projects (24 production, advisory-only `GovernanceFraudVerifier`, and test-only `ExternalBridgeStubVerifier`, incl. `DAValidator`, `L1TxFilter`, `ContractZkVerifier`, immutable `Sp1Groth16Verifier`,
  `RestrictedExecutionFraudVerifier` executable restricted-v4 verifier, and the 6
  cross-foreign-chain bridge contracts: `MpcCommitteeVerifier` /
  `ExternalBridgeRegistry` / `ExternalBridgeEscrow` /
  `ExternalBridgeBond` / `ExternalBridgeStubVerifier` /
  `MpcCommitteeFraudVerifier`) type-check via `Neo.SmartContract.Framework`.
  The 10 L2 system contracts are Neo core native contracts in
  `external/neo/src/Neo/SmartContract/Native/L2NativeContracts.cs`.
- **CLI tools (7)** — `neo-stack`, `neo-l2-devnet`, `neo-hub-deploy`,
  `neo-l2-explore`, `neo-bridge`, `neo-l2-faucet`, `neo-external-bridge`.
- **App SDKs (4)** — `src/Neo.L2.Sdk/` (.NET) · `sdk/typescript/`
  (`@neo-n4/sdk`) · `sdk/rust/` (`neo-n4-sdk`) · `sdk/python/`
  (`neo-n4-sdk`) — all 10 RPC methods,
  same wire shape, same 4-class error taxonomy, and one shared offline/live
  conformance suite documented in [`sdk-conformance.md`](./sdk-conformance.md).
- **Static web experiences (4)** — `sdk/web-explorer/index.html` (operator
  explorer) · `experience-hub/index.html` (architecture tour) ·
  `interactive-runtime/index.html` (runtime theater) ·
  `interactive-math/index.html` (proof math lab).
- **Docs site config (1)** — `book.toml` + `docs/SUMMARY.md` (mdBook).
- **Rust prover/core (5)** — `bridge/neo-execution-core/` (shared canonical
  execution semantics) · `bridge/neo-zkvm-{host,guest}/` (batch SP1 proof
  producer/program) · `bridge/neo-zkvm-gateway-{host,guest}/` (recursive
  Gateway SP1 proof producer/program).
- **Submodules (5)** — `external/neo` (`r3e-network/neo` fork, L2 branch `r3e/neo-n4-core`; L1 core branch is `r3e/neo-n3-core` in the same fork) ·
  `external/neo-devpack-dotnet` (smart-contract devpack + nccs) ·
  `external/neo-riscv-vm` (PolkaVM-backed Neo RISC-V engine) ·
  `external/neo-zkvm` (SP1 prover crates and legacy Neo VM compatibility guest) ·
  `external/neo-vm-rs` (stateful Rust NeoVM runtime shared by native execution and SP1). None
  are released on NuGet/crates.io for the versions tracked here.
- **Tests (38 .NET test projects + cross-language gates)** — the current solution inventory is discovered dynamically, plus TypeScript, Rust SDK/core/watchers/zkVM, Python SDK, Node experience, vendored `neo-zkvm` / `neo-riscv-vm`, Solidity, Solana, and SP1 release-proof gates documented in [`testing-approach.md`](./testing-approach.md).

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
│   ├── NeoHub.* (26)                       # L1 suite: 24 production + 1 advisory + 1 test stub
├── external/neo/                            # r3e Neo fork with N4 L2 native contracts
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
│   ├── neo-zkvm-guest/                     # Rust → RISC-V ELF (SP1-proven execution guest)
│   └── neo-zkvm-host/                      # sp1-sdk 6.2.1 prover daemon (prove-batch)
└── tests/                                  # 38 solution test projects; inventory is discovered dynamically
```

---

## Phased status

Per [`doc.md` §18](../doc.md):

| Phase | Goal | Design | Code | Integrated | Crypto enforced | Exact-revision deployment | Production-ready |
| ----- | ---- | :----: | :--: | :--------: | :-------------: | :-----------------------: | :--------------: |
| 0 | Sidechain PoC | ✅ | ✅ | ✅ local | N/A | ❌ | ❌ |
| 1 | NeoHub v0 + Shared Bridge | ✅ | ✅ | ✅ local | 🟡 profile dependent | ❌ | ❌ |
| 2 | Batch Settlement | ✅ | ✅ | ✅ local | 🟡 proof profile dependent | ❌ | ❌ |
| 3 | Optimistic Challenge Window | ✅ | 🟡 restricted v4 | 🟡 restricted transition | 🟡 exact v4 only | ❌ | ❌ |
| 4 | NeoVM2 / RISC-V ZK validity | ✅ | ✅ | ✅ local + CI | ✅ pinned SP1 Groth16 path | ❌ | ❌ |
| 5 | Neo Gateway aggregation | ✅ | ✅ | ✅ local + CI | 🟡 validity profile dependent | ❌ | ❌ |
| 6 | Neo Stack CLI / templates | ✅ | ✅ | 🟡 wallet adapters required | N/A | ❌ | ❌ |

Legend: ✅ proved for this revision · 🟡 partial/profile-dependent · ❌ missing.
No row is a production-readiness claim; the complete evidence matrix is in
[`IMPLEMENTATION_STATUS.md`](../IMPLEMENTATION_STATUS.md).

Detailed coverage per project: [`IMPLEMENTATION_STATUS.md`](../IMPLEMENTATION_STATUS.md).

---

## Quick start

**Requires** .NET 10 SDK (`dotnet --version` must report `10.0.x`). The
[`r3e-network/neo`](https://github.com/r3e-network/neo) Neo core fork is vendored as a
git submodule at `external/neo` on branch `r3e/neo-n4-core` (it is never released on
NuGet; project references go directly at the source tree). L1 core work uses
`r3e/neo-n3-core`, based on upstream `master-n3`; L2 core work uses
`r3e/neo-n4-core`, based on upstream `master`. `neo-project/neo` is kept as the
read-only upstream source for controlled syncs, not as this repo's build dependency.

```bash
git clone --recurse-submodules https://github.com/r3e-network/neo-n4
cd neo-n4

# If you forgot --recurse-submodules:
# git submodule update --init --recursive

# Type-check everything + run the complete current .NET test inventory
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

# Generate a NeoHub deploy bundle (24 production contracts, declarative, dependency-resolved)
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
| [`docs/technical-roadmap.md`](./technical-roadmap.md)              | architects, reviewers | Contract-first roadmap for NeoHub, ZK verifier routing, NeoFS DA, VM profiles, assets, and interoperability. |
| [`docs/launching-an-l2.md`](./launching-an-l2.md)                  | L2 operators          | 5-command path to a registered L2 chain + every plug-in point for custom logic (executor / DA / prover / sequencer). Templates: rollup / zk-rollup / validium / sidechain. |
| [`samples/`](../samples/README.md)                                       | L2 operators          | 4 ready-to-run sample chain configs covering distinct use cases (general-rollup / gaming-rollup / exchange-validium / privacy-sidechain), each verified end-to-end via `neo-l2-devnet --config`. |
| [`samples/contracts/`](../samples/contracts/README.md)                   | dApp developers       | Sample L2-aware app contracts (`CrossChainGreeter`, `WithdrawalDemo`) showing standard patterns for integrating with N4 L2 native contracts. |
| [`docs/tech-stack-coverage.md`](./tech-stack-coverage.md)          | reviewers             | Source-tree coverage and remaining evidence: four typed SDKs and the explorer/bridge/faucet Experience Hub ship; live SDK/provider rehearsals, exact-revision deployment, and independent approval remain open. |
| [`docs/neohub-architecture-and-workflows.md`](./neohub-architecture-and-workflows.md) | engineers, operators, reviewers | Detailed NeoHub architecture, dataflow, workflow diagrams, and per-contract responsibility matrix for all NeoHub contracts. |
| [`docs/crate-visual-guide.md`](./crate-visual-guide.md) | developers, reviewers | Crate-by-crate visual learning index with architecture, workflow, and dataflow diagrams for every Rust crate. |
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
