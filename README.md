# Neo Elastic Network (`neo4`)

> **Multi-L2 network on Neo 4 core, with shared bridge, proof aggregation, and native interoperability.**

`neo4` is the consolidation repo for the Neo Elastic Network — a system that uses
[`neo-project/neo`](https://github.com/neo-project/neo) Neo 4 core as the L2 execution
kernel, anchors all L2 chains to a unified L1 contract suite (**NeoHub**) on Neo N3 / Neo 4
L1, and aggregates proofs and inter-L2 messages through **Neo Gateway**.

The full architecture spec is in [`doc.md`](./doc.md). For a fast tour:

- [**`docs/getting-started.md`**](./docs/getting-started.md) — clone → test → run devnet in 5 minutes.
- [**`docs/architecture-walkthrough.md`**](./docs/architecture-walkthrough.md) — narrative tour mapping every `doc.md` section to code.
- [**`ARCHITECTURE.md`**](./ARCHITECTURE.md) — English distillation of `doc.md`.
- [**`IMPLEMENTATION_STATUS.md`**](./IMPLEMENTATION_STATUS.md) — per-phase coverage matrix + what's deferred.
- [**`CONTRIBUTING.md`**](./CONTRIBUTING.md) — how to add a component, code style, PR checklist.
- [**`AGENTS.md`**](./AGENTS.md) — guide for AI-assisted contributors.

## Architecture at a glance

```
Neo N3 / Neo 4 L1
    │
    ▼
NeoHub (13 L1 contracts)
  ChainRegistry · SharedBridge · SettlementManager · VerifierRegistry · MessageRouter
  TokenRegistry · DARegistry · GovernanceController · EmergencyManager
  ForcedInclusion · SequencerBond · SequencerRegistry · OptimisticChallenge
    │
    ▼
Neo Gateway (Phase 5 — optional)
  BinaryTreeAggregator + IRoundProver (default = pass-through; production = SP1 Compress / Halo2 / Risc0 fold)
    │
    ▼
Multiple Neo 4 L2 chains
  Each runs Neo 4 core + plugin suite (Batch, Settlement, Bridge, DA, Prover, Rpc, Gateway)
  + 6 on-L2 native contracts (L2BridgeContract, L2MessageContract, L2BatchInfoContract, …)
```

## Phased status

Per [`doc.md` §18](./doc.md):

| Phase | Goal                                | Status                                              |
| ----- | ----------------------------------- | --------------------------------------------------- |
| 0     | Sidechain PoC                       | ✅ MVP integration test passes                      |
| 1     | NeoHub v0 + Shared Bridge           | ✅ All 13 NeoHub contracts compile + deploy planner |
| 2     | Batch Settlement                    | ✅ Real `KeyedStateStore` continuity verified       |
| 3     | Optimistic Challenge Window         | ✅ `OptimisticChallenge` + `BisectionGame` (log-N)  |
| 4     | NeoVM2 / RISC-V ZK Validity Proof   | 🟡 SP1 FFI bridge scaffolded                        |
| 5     | Neo Gateway proof aggregation       | 🟡 Recursive structure ready; round-prover stub    |
| 6     | Neo Stack CLI                       | 🟡 8 subcommands scaffolded                         |

## Repo layout

```
neo4/
├── doc.md / ARCHITECTURE.md / CHANGELOG.md / IMPLEMENTATION_STATUS.md
├── CONTRIBUTING.md / AGENTS.md / docs/
├── src/
│   ├── Neo.L2.Abstractions / Batch / State / Bridge / Messaging
│   ├── Neo.L2.Proving / Neo.L2.Proving.Sp1
│   ├── Neo.L2.Executor (incl. KeyedStateStore + KeyedStateRootOracle)
│   ├── Neo.L2.ForcedInclusion / Sequencer / Censorship / Challenge / Audit
│   ├── Neo.L2.Settlement.Rpc
│   └── Neo.Plugins.L2{Batch,Settlement,Bridge,DA,Prover,Rpc,Gateway}
├── contracts/
│   ├── NeoHub.*/                # 13 L1 contracts
│   └── L2Native.*/              # 6 on-L2 native contracts
├── tools/
│   ├── Neo.Stack.Cli/           # neo-stack CLI
│   ├── Neo.L2.Devnet/           # neo-l2-devnet runnable demo (state continuity + audit)
│   └── Neo.Hub.Deploy/          # neo-hub-deploy declarative L1 deploy planner
├── bridge/
│   └── neo-zkvm-bridge/         # Rust cdylib + C ABI for SP1 prover P/Invoke
└── tests/                       # 194 tests across 19 test projects
```

## Quick start

Requires **.NET 10 SDK** (`dotnet 10.0.x`) and a sibling clone of
[`neo-project/neo`](https://github.com/neo-project/neo) at `../neo`.

```bash
# Type-check + run all 194 tests
dotnet test Neo.L2.sln /p:NuGetAudit=false

# Run the in-process devnet (state-root continuity + audit pass)
dotnet run --project tools/Neo.L2.Devnet -- 5

# Generate a NeoHub deploy bundle
dotnet run --project tools/Neo.Hub.Deploy -- scaffold --output deploy-plan.json
dotnet run --project tools/Neo.Hub.Deploy -- plan --plan deploy-plan.json --output bundle.json

# Build a smart contract (type-check only without nccs)
dotnet build contracts/NeoHub.ChainRegistry /p:NuGetAudit=false /p:DisableNccs=true
```

See [`docs/getting-started.md`](./docs/getting-started.md) for the annotated walkthrough.

## What ships in this repo

- **19 smart contracts** (13 NeoHub L1 + 6 L2 native), all type-checked against
  [`Neo.SmartContract.Framework`](https://github.com/neo-project/neo-devpack-dotnet).
- **11 off-chain libraries** with deterministic encodings, real Merkle state-root
  computation, multisig + optimistic + ZK-mock provers, JSON-RPC client.
- **7 neo-node plugins** (`Neo.Plugins.L2*`) extending `Neo.Plugins.Plugin`.
- **3 CLI tools** (`neo-stack`, `neo-l2-devnet`, `neo-hub-deploy`).
- **1 Rust FFI bridge** crate (`neo-zkvm-bridge`) for SP1 prover P/Invoke.
- **194 tests / 19 test projects**, all green.
- **494 lines of contributor + getting-started + architecture docs**.

## License

MIT — see [`LICENSE`](./LICENSE).
