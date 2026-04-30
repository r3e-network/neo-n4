# Neo Elastic Network (`neo4`)

> **Multi-L2 network on Neo 4 core, with shared bridge, proof aggregation, and native interoperability.**

`neo4` is the consolidation repo for the Neo Elastic Network — a system that uses [`neo-project/neo`](https://github.com/neo-project/neo) Neo 4 core as the L2 execution kernel, anchors all L2s to a unified L1 settlement contract suite (NeoHub) on Neo N3 / Neo 4 L1, and aggregates proofs and inter-L2 messages through Neo Gateway.

The full architecture spec is in [`doc.md`](./doc.md). An English summary is in [`ARCHITECTURE.md`](./ARCHITECTURE.md).

## Architecture at a glance

```
Neo N3 / Neo 4 L1
    │
    ▼
NeoHub (L1 contract suite)
  ChainRegistry · SharedBridge · SettlementManager · VerifierRegistry
  MessageRouter · TokenRegistry · DARegistry · GovernanceController · EmergencyManager
    │
    ▼
Neo Gateway   (optional proof aggregation + global message root)
    │
    ▼
Multiple Neo 4 L2 chains
  Each runs Neo 4 core in L2RollupMode or L2ValidiumMode, plus L2 extensions:
  Sequencer (dBFT) · Batcher · StateRootGenerator · DAWriter
  ProverAdapter · SettlementSubmitter · BridgeAdapter · MessageAdapter
```

## Naming

| Component        | Role                                                              |
| ---------------- | ----------------------------------------------------------------- |
| **NeoHub**       | L1 contract suite — settlement, shared bridge, registry, messaging |
| **Neo Gateway**  | Proof aggregation + global L2-to-L2 message root                  |
| **Neo Stack**    | Launch framework / CLI for booting new L2 chains                  |
| **Neo Connect**  | Cross-chain message / call / asset transfer protocol              |
| **Neo 4 L2 Core** | Neo 4 core in `L2RollupMode` / `L2ValidiumMode`                   |

## Repo layout

```
neo4/
├── doc.md                       # master architecture spec (Chinese)
├── ARCHITECTURE.md              # English distilled summary
├── Neo.L2.sln                   # solution file
├── src/
│   ├── Neo.L2.Abstractions/     # IL2BatchExecutor, IL2ProofVerifier, IDAWriter, …
│   ├── Neo.L2.Batch/            # L2Batch, L2BatchCommitment, BatchBuilder, …
│   ├── Neo.L2.State/            # StateRootCalculator, WithdrawalTree, MessageTree
│   ├── Neo.L2.Bridge/           # AssetMapping, Deposit/Withdrawal processors
│   ├── Neo.L2.Messaging/        # CrossChainMessage, L1MessageQueue, L2Outbox
│   ├── Neo.L2.Proving/          # AttestationProofAdapter, OptimisticProofAdapter, RiscVProverAdapter
│   └── Neo.Plugins.L2*/         # neo-node plugins for batch/settlement/bridge/DA/prover/gateway
├── contracts/
│   ├── NeoHub.*/                # ChainRegistry, SharedBridge, SettlementManager, …
│   └── L2Native.*/              # L2BridgeContract, L2MessageContract, …
├── tools/
│   └── Neo.Stack.Cli/           # neo-stack CLI
├── tests/
└── docs/
```

## Build

Requires **.NET 10 SDK** (`dotnet 10.0.100+`) and a sibling clone of [`neo-project/neo`](https://github.com/neo-project/neo) at `../neo` (set via `NeoCorePath` in `Directory.Build.props`).

```bash
dotnet restore Neo.L2.sln
dotnet build Neo.L2.sln
dotnet test  Neo.L2.sln
```

Smart contract projects in `contracts/` use [`neo-devpack-dotnet`](https://github.com/neo-project/neo-devpack-dotnet) for compilation to NeoVM.

## Phased roadmap

Per [`doc.md` §18](./doc.md):

| Phase | Name                       | Security label              |
| ----- | -------------------------- | --------------------------- |
| 0     | Sidechain PoC              | sidechain                   |
| 1     | NeoHub v0 + Shared Bridge  | connected sidechain         |
| 2     | Batch Settlement           | settled L2                  |
| 3     | Optimistic L2              | optimistic rollup           |
| 4     | NeoVM2 / RISC-V ZK L2      | zk validity rollup          |
| 5     | Neo Gateway                | Neo Elastic Network         |
| 6     | Neo Stack                  | (ecosystem launch)          |

## License

MIT — see [`LICENSE`](./LICENSE).
