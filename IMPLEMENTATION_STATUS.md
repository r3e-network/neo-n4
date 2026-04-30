# Implementation Status

> Snapshot of what's built in `neo4` against `doc.md`. Updated alongside meaningful PRs.

## Phase coverage (doc.md §18)

| Phase | Goal                                      | Status                                      |
| ----- | ----------------------------------------- | ------------------------------------------- |
| 0     | Sidechain PoC                             | ✅ MVP integration test passes              |
| 1     | NeoHub v0 + Shared Bridge                 | 🟡 Contracts + deploy planner ready; needs wallet-equipped runner |
| 2     | Batch Settlement                          | 🟡 Off-chain green; on-chain wiring depends on (1) |
| 3     | Optimistic Challenge Window               | 🟡 Verifier shipped; challenge logic deferred |
| 4     | NeoVM2 / RISC-V ZK Validity Proof         | 🟡 SP1 FFI bridge scaffolded; flip `real-prover` cargo feature to enable |
| 5     | Neo Gateway (proof aggregation)           | 🔴 Pass-through reference aggregator only   |
| 6     | Neo Stack CLI / Templates                 | 🟡 8 subcommands scaffolded                 |

Legend: ✅ done, 🟡 substantial scaffolding + tests, 🔴 stub.

## Completed work — by code

### Off-chain libraries (`src/Neo.L2.*`)

| Project                   | Role                                                              |
| ------------------------- | ----------------------------------------------------------------- |
| `Neo.L2.Abstractions`     | 7 interfaces + 14 model records (doc.md §19)                      |
| `Neo.L2.Batch`            | `L2Batch`, `BatchBuilder`, deterministic `BatchSerializer`        |
| `Neo.L2.State`            | `MerkleTree` (matches Neo `Hash256`), `MessageHasher`, `WithdrawalTree`, `MessageTree`, `StateRootCalculator` |
| `Neo.L2.Bridge`           | `AssetRegistry`, `DepositPayload`, `DepositProcessor`, `WithdrawalProcessor` |
| `Neo.L2.Messaging`        | `MessageBuilder`, `L1MessageInbox`, `L2Outbox`, `InMemoryMessageRouter` |
| `Neo.L2.Proving`          | Stage 0 multisig (real), Stage 1 optimistic, Stage 2 mock RISC-V; `VerifierRegistry` |
| `Neo.L2.Proving.Sp1`      | **Phase 4 SP1 P/Invoke wrapper** with graceful fallback to mock when native bridge missing |
| `Neo.L2.Executor`         | `SPEC.md` + `Receipt`, pluggable `ITransactionExecutor` / `IPostStateRootOracle` / `IL1MessageProcessor`, `ReferenceBatchExecutor` |
| `Neo.L2.ForcedInclusion`  | **Anti-censorship `IForcedInclusionSource` + in-memory backend** |
| `Neo.L2.Settlement.Rpc`   | **JSON-RPC client + `RpcSettlementClient` for L1 read methods + signer-delegated submit** |

### Native FFI bridge (`bridge/`)

| Crate                | Role                                             |
| -------------------- | ------------------------------------------------ |
| `neo-zkvm-bridge`    | Rust cdylib with stable C ABI (`neo_zkvm_prove` / `_verify` / `_free_buffer` / `_abi_version`); optional `real-prover` feature links against `neo-zkvm-prover` |

### neo-node plugins (`src/Neo.Plugins.L2*`)

| Plugin                       | Role                                                  |
| ---------------------------- | ----------------------------------------------------- |
| `Neo.Plugins.L2Batch`        | Hooks `Blockchain.Committed`; seals batches by size/age threshold |
| `Neo.Plugins.L2Settlement`   | Wires prover + settlement client; signs sealed batches |
| `Neo.Plugins.L2Bridge`       | Hosts `AssetRegistry` + processors                    |
| `Neo.Plugins.L2DA`           | Picks DA writer by `DAMode` config                    |
| `Neo.Plugins.L2Prover`       | Hosts `IL2Prover` for the configured `ProofType`      |
| `Neo.Plugins.L2Rpc`          | 9 RPC handlers (doc.md §14.1) + `IL2RpcStore`         |
| `Neo.Plugins.L2Gateway`      | **Phase 5 pass-through aggregator scaffold**          |

### Smart contracts (`contracts/`) — 16 total, all type-check via devpack

**NeoHub L1 suite (10):**
`ChainRegistry` · `SharedBridge` · `SettlementManager` · `VerifierRegistry` · `MessageRouter` · `TokenRegistry` · `DARegistry` · `GovernanceController` · `EmergencyManager` · **`ForcedInclusion`** (new)

**L2 native (6):**
`L2BridgeContract` · `L2MessageContract` · `L2BatchInfoContract` · `L2FeeContract` · `L2PaymasterContract` · `L2SystemConfigContract`

### Tools (`tools/`)

| Tool                  | Role                                                  |
| --------------------- | ----------------------------------------------------- |
| `Neo.Stack.Cli`       | `neo-stack` CLI: 8 subcommands (create-chain, init-l2, register-chain, deploy-bridge-adapter, start-{sequencer,batcher,prover}, submit-batch) |
| `Neo.L2.Devnet`       | `neo-l2-devnet <N>` — runs N batches end-to-end in-process |
| `Neo.Hub.Deploy`      | **`neo-hub-deploy` — declarative L1 deploy planner: scaffold / plan / verify** |

### Tests

**121 unit + integration tests across 16 projects:**

| Project                              | Tests | Coverage                                    |
| ------------------------------------ | ----- | ------------------------------------------- |
| `Neo.L2.Abstractions.UnitTests`      | 17    | enum discriminants, models, interface shape |
| `Neo.L2.Batch.UnitTests`             | 13    | builder lifecycle, serializer round-trip    |
| `Neo.L2.State.UnitTests`             | 12    | Merkle tree (matches neo's), proof verify, hashers |
| `Neo.L2.Messaging.UnitTests`         | 7     | inbox FIFO, replay protection, outbox split |
| `Neo.L2.Bridge.UnitTests`            | 7     | registry, deposit replay, withdrawal staging |
| `Neo.L2.Proving.UnitTests`           | 11    | Stage 0/1/2 prove+verify, registry dispatch |
| `Neo.L2.Proving.Sp1.UnitTests`       | 6     | bridge unavailable, mock fallback, VK mismatch |
| `Neo.L2.Executor.UnitTests`          | 6     | empty batch, ordering, determinism, effects |
| `Neo.L2.ForcedInclusion.UnitTests`   | 8     | nonce ordering, replay, overdue detection   |
| `Neo.Plugins.L2Rpc.UnitTests`        | 9     | all 9 RPC methods, foreign-chain rejection  |
| `Neo.Plugins.L2Gateway.UnitTests`    | 5     | aggregation, root, proof concatenation      |
| `Neo.L2.Settlement.Rpc.UnitTests`    | 6     | JSON-RPC envelope, ByteString/Integer parse, FAULT, signer delegate |
| `Neo.Hub.Deploy.UnitTests`           | 8     | topo sort, cycle detection, placeholder resolution, scaffold |
| `Neo.L2.IntegrationTests`            | 6     | **MVP Phase 0 + Phase 1 cross-component (forced inclusion + SP1 fallback + Gateway aggregation)** |

## What's not yet wired (out of MVP scope)

- **Live L1 RPC client `SignAndSendAsync` implementation**: `RpcSettlementClient` requires the operator to inject a wallet-equipped signer; no built-in implementation.
- **One-shot deploy runner**: `Neo.Hub.Deploy` emits the bundle JSON; the consumer (signer + chain bookkeeper) is not in this repo.
- **`nccs` artifact generation**: `Directory.Build.props` calls `nccs` with `ContinueOnError=true`; users install nccs separately.
- **RpcServer plugin integration partial**: `L2RpcMethods` is callable as plain methods; the `[RpcMethod]`-attributed wrapper that registers them with neo's `RpcServer` plugin needs the RpcServer source.
- **Real SP1 prover linkage**: `bridge/neo-zkvm-bridge` defaults to NOT_IMPLEMENTED; flip `--features real-prover` to link against `../../../neo-zkvm` and re-build the cdylib.
- **Phase 5 recursive proof aggregation**: scaffold (`PassThroughAggregator`) only.
- **Forced-inclusion bond/slashing logic**: contract has the report event; actual slashing wiring depends on `SettlementManager` integration.
- **NeoFS DA writer**: `Neo.Plugins.L2DA` has the stub class with `NotImplementedException`; production wires NeoFS client.
- **dBFT sequencer-committee selection** (doc.md §7.1): defaults to neo's existing `DBFTPlugin` consensus; Neo Elastic's per-chain validator-set governance is not yet plumbed.

## How to run

```bash
# Type-check + run all unit + integration tests
dotnet test Neo.L2.sln /p:NuGetAudit=false

# Build smart contracts (type-check only without nccs)
dotnet build contracts/NeoHub.ChainRegistry /p:NuGetAudit=false /p:DisableNccs=true

# Run the in-process devnet demo
dotnet run --project tools/Neo.L2.Devnet -- 5

# Generate a NeoHub deploy bundle
dotnet run --project tools/Neo.Hub.Deploy -- scaffold --output deploy-plan.json
dotnet run --project tools/Neo.Hub.Deploy -- plan --plan deploy-plan.json --output bundle.json

# Build the SP1 FFI bridge (default features = mock fallback)
cd bridge/neo-zkvm-bridge && cargo build --release

# Build the SP1 FFI bridge with real prover linkage
cd bridge/neo-zkvm-bridge && cargo build --release --features real-prover

# Use the launcher CLI
dotnet run --project tools/Neo.Stack.Cli -- help
```
