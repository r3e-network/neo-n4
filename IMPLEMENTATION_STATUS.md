# Implementation Status

> Snapshot of what's built in `neo4` against `doc.md`. Updated alongside meaningful PRs.

## Phase coverage (doc.md §18)

| Phase | Goal                                      | Status                  |
| ----- | ----------------------------------------- | ----------------------- |
| 0     | Sidechain PoC                             | ✅ MVP test passes      |
| 1     | NeoHub v0 + Shared Bridge                 | 🟡 Contracts shipped, deploy script TBD |
| 2     | Batch Settlement                          | 🟡 Off-chain green; on-chain wiring needs deployed contracts |
| 3     | Optimistic Challenge Window               | 🟡 Verifier shipped; challenge logic TBD |
| 4     | NeoVM2 / RISC-V ZK Validity Proof         | 🔴 Mock prover only; SP1 FFI bridge TBD |
| 5     | Neo Gateway (proof aggregation)           | 🔴 Plugin scaffold only |
| 6     | Neo Stack CLI / Templates                 | 🟡 CLI subcommands stubbed |

Legend: ✅ done, 🟡 substantial scaffolding + tests, 🔴 stub.

## Completed work — by code

### Off-chain libraries (`src/Neo.L2.*`)

| Project                | Role                                                          |
| ---------------------- | ------------------------------------------------------------- |
| `Neo.L2.Abstractions`  | 7 interfaces + 14 model records (doc.md §19)                  |
| `Neo.L2.Batch`         | `L2Batch`, `BatchBuilder`, deterministic `BatchSerializer`    |
| `Neo.L2.State`         | `MerkleTree` (matches Neo `Hash256`), `MessageHasher`, `WithdrawalTree`, `MessageTree`, `StateRootCalculator` |
| `Neo.L2.Bridge`        | `AssetRegistry`, `DepositPayload`, `DepositProcessor`, `WithdrawalProcessor` |
| `Neo.L2.Messaging`     | `MessageBuilder`, `L1MessageInbox`, `L2Outbox`, `InMemoryMessageRouter` |
| `Neo.L2.Proving`       | Stage 0 multisig (real), Stage 1 optimistic (real verifier), Stage 2 RISC-V (mock); `VerifierRegistry` |
| `Neo.L2.Executor`      | `SPEC.md` + `Receipt`, `ITransactionExecutor`, `IPostStateRootOracle`, `ReferenceBatchExecutor` |

### neo-node plugins (`src/Neo.Plugins.L2*`)

| Plugin                       | Role                                                  |
| ---------------------------- | ----------------------------------------------------- |
| `Neo.Plugins.L2Batch`        | Hooks `Blockchain.Committed`; seals batches by size/age threshold |
| `Neo.Plugins.L2Settlement`   | Wires prover + settlement client; signs sealed batches |
| `Neo.Plugins.L2Bridge`       | Hosts `AssetRegistry` + processors                    |
| `Neo.Plugins.L2DA`           | Picks DA writer by `DAMode` config                    |
| `Neo.Plugins.L2Prover`       | Hosts `IL2Prover` for the configured `ProofType`      |
| `Neo.Plugins.L2Rpc`          | 9 RPC handlers (doc.md §14.1) + `IL2RpcStore` + in-mem store |

### Smart contracts (`contracts/`) — 15 total, all type-check via devpack

**NeoHub L1 suite (9):**
`ChainRegistry` · `SharedBridge` · `SettlementManager` · `VerifierRegistry` · `MessageRouter` · `TokenRegistry` · `DARegistry` · `GovernanceController` · `EmergencyManager`

**L2 native (6):**
`L2BridgeContract` · `L2MessageContract` · `L2BatchInfoContract` · `L2FeeContract` · `L2PaymasterContract` · `L2SystemConfigContract`

### Tools (`tools/`)

| Tool                  | Role                                                  |
| --------------------- | ----------------------------------------------------- |
| `Neo.Stack.Cli`       | `neo-stack` CLI: 8 subcommands (create-chain, init-l2, register-chain, deploy-bridge-adapter, start-{sequencer,batcher,prover}, submit-batch) |
| `Neo.L2.Devnet`       | `neo-l2-devnet <N>` — runs N batches end-to-end in-process and prints state |

### Tests

83 unit tests across 9 test projects:

| Project                              | Tests | Coverage                                    |
| ------------------------------------ | ----- | ------------------------------------------- |
| `Neo.L2.Abstractions.UnitTests`      | 17    | enum discriminants, models, interface shape |
| `Neo.L2.Batch.UnitTests`             | 13    | builder lifecycle, serializer round-trip    |
| `Neo.L2.State.UnitTests`             | 12    | Merkle tree (matches neo's), proof verify, hashers |
| `Neo.L2.Messaging.UnitTests`         | 7     | inbox FIFO, replay protection, outbox split |
| `Neo.L2.Bridge.UnitTests`            | 7     | registry, deposit replay, withdrawal staging |
| `Neo.L2.Proving.UnitTests`           | 11    | Stage 0/1/2 prove+verify, registry dispatch |
| `Neo.L2.Executor.UnitTests`          | 6     | empty batch, ordering, determinism, effects |
| `Neo.Plugins.L2Rpc.UnitTests`        | 9     | all 9 RPC methods, foreign-chain rejection  |
| `Neo.L2.IntegrationTests`            | 1     | **MVP Phase 0 end-to-end full lifecycle**   |

## What's not yet wired (out of MVP scope)

- **Live L1 RPC submission**: `ISettlementClient` has only a scaffold; needs a real `Neo.Network.RPC` client to push commitments to a running NeoHub deploy.
- **NeoHub deploy script**: contracts compile but a one-shot deploy + register-chain scenario for the devnet has not been written.
- **`nccs` artifact generation**: `Directory.Build.props` calls `nccs` with `ContinueOnError=true`; running `nccs` requires having the devpack tool installed in `PATH`.
- **Real RpcServer plugin integration**: `L2RpcMethods` is callable as plain methods; the `[RpcMethod]`-attributed wrapper that registers them with neo's `RpcServer` plugin needs the RpcServer source (currently empty in the local `neo` checkout).
- **Phase 4 prover bridge**: `RiscVProverBase` has the API surface; the SP1 FFI bridge to `neo-zkvm` is not yet implemented.
- **Phase 5 Gateway aggregation**: scaffold only; recursive proof aggregation is a separate, substantial design.
- **Forced inclusion handler** (doc.md §15.4): not started.

## How to run

```bash
# Type-check + run all unit tests
dotnet test Neo.L2.sln /p:NuGetAudit=false

# Build smart contracts (type-check only without nccs)
dotnet build contracts/NeoHub.ChainRegistry /p:NuGetAudit=false /p:DisableNccs=true

# Run the devnet demo
dotnet run --project tools/Neo.L2.Devnet -- 5

# Use the launcher CLI
dotnet run --project tools/Neo.Stack.Cli -- help
```
