# Plan: ApplicationEngine integration + real state-root batch executor

> Replace the two remaining "Reference / scaffolding" rows in
> `IMPLEMENTATION_STATUS.md` with real production implementations.

## What we're replacing

- **`ITransactionExecutor`**
  - *Today:* `ReferenceTransactionExecutor` — canned receipts.
  - *Target at the time:* `ApplicationEngineTransactionExecutor` — runs
    legacy NeoVM through Neo's real `ApplicationEngine`. The current Neo N4
    L2 target is `RiscVTransactionExecutor` / NeoVM2-RISC-V; this plan is
    retained as the legacy NeoVM compatibility work log.
- **`IL2BatchExecutor`**
  - *Today:* `ReferenceBatchExecutor` — placeholder post-state root.
  - *Target:* `MerkleStateBatchExecutor` — real cryptographic state
    root.

Both interfaces already exist; both have in-process devnet-quality
`Reference*` impls. This plan turns each into a real production one.

## Constraint check

`external/neo/src/Neo/SmartContract/ApplicationEngine.cs` is fully
present in the vendored submodule — we have everything needed for VM
integration.

`external/neo/src/Neo/Persistence/DataCache.cs` is abstract with 7
`protected abstract` methods — we can implement it over the existing
`IL2KeyValueStore`.

The vendored Neo core does **not** ship a Merkle Patricia Trie. That's
fine: real production L2s (ZKsync, Polygon zkEVM, Optimism) use a
**sorted-key binary Merkle tree over (key, value) pairs**, not MPT.
MPT is an Ethereum-mainnet optimization. We'll build the binary
Merkle variant in `Neo.L2.State` — same cryptographic guarantees,
simpler to verify.

## Phases (each one is a clean, mergeable PR)

### Phase A — ApplicationEngine-backed legacy transaction executor

**A1. `L2DataCacheAdapter`** (new file in `Neo.L2.Persistence`)
- Implements Neo's abstract `DataCache` over `IL2KeyValueStore`.
- The 7 protected methods (`AddInternal`, `DeleteInternal`,
  `ContainsInternal`, `GetInternal`, `SeekInternal`, `TryGetInternal`,
  `UpdateInternal`) translate to KV operations.
- `SeekInternal` requires a sorted prefix scan — every existing
  `IL2KeyValueStore` impl already has `EnumeratePrefix(prefix)`,
  which we wrap.
- Tests: happy-path put/get/delete/seek; round-trip via the abstract
  base class's commit-track machinery.

**A2. `L2TransactionContainer`** (new file in `Neo.L2.Executor`)
- Implements Neo's `IVerifiable` interface, exposing the L2
  transaction's hash + signature for witness verification inside
  ApplicationEngine.
- Maps from the existing L2 tx model to Neo's `Transaction`.

**A3. `ApplicationEngineTransactionExecutor`** (new file in `Neo.L2.Executor`)
- Implements `ITransactionExecutor.Execute(L2Transaction, IL2KeyValueStore)`.
- Wraps the KV store in `L2DataCacheAdapter`; constructs a dummy
  `Block` (timestamp = batch first-block time, index = batch first-block
  height); calls `ApplicationEngine.Run(script, cache, container)`.
- Captures the engine's `State` (HALT/FAULT), `GasConsumed`,
  `ResultStack`, and `Notifications`.
- Produces a real `TransactionReceipt` with status, gas, and event log.

**A4. Tests** (new `tests/Neo.L2.Executor.UnitTests/UT_ApplicationEngineTransactionExecutor.cs`)
- Happy path: a trivial script (e.g. `PUSH1 RET`) runs to HALT, returns 1.
- FAULT path: a script that throws → receipt status = Failed, no state changes.
- Gas-out-of-bounds: script with infinite loop hits gas limit, FAULT.
- Notifications: script that emits a notification, captured in receipt.

### Phase B — Real state-root batch executor

**B1. `KeyedStateMerkleTree`** (new file in `Neo.L2.State`)
- Builds a binary Merkle tree over sorted `(key, value)` pairs.
- Root = `Hash256(left ‖ right)` recursively, with single-leaf-tree edge case.
- Inclusion proofs: per-leaf sibling list (re-uses the
  `MerklePathRoundProver` verification convention shipped in this session).
- Tests: deterministic root for same set; root changes with single field
  edit; inclusion-proof round-trip for every leaf at all sizes 1..16.

**B2. `MerkleStateBatchExecutor`** (new file in `Neo.L2.Executor`)
- Implements `IL2BatchExecutor`.
- For each transaction:
  1. Snapshot the `IL2KeyValueStore` pre-state.
  2. Run via `ApplicationEngineTransactionExecutor`.
  3. Commit the data-cache changes to the underlying KV store.
- After all transactions:
  1. Enumerate the KV store's full key set.
  2. Build a `KeyedStateMerkleTree` over `(key, value)` pairs.
  3. Return its root as `postStateRoot`.
- Reproducibility: same starting KV state + same transactions →
  byte-identical root. (This is the proving-target invariant.)

**B3. Tests** (new `tests/Neo.L2.Executor.UnitTests/UT_MerkleStateBatchExecutor.cs`)
- Empty batch: `postStateRoot == preStateRoot` (no state changed).
- Single tx: root differs after applying.
- Replay: applying same txs twice gives same final root.
- Determinism: two independent runs of the same batch sequence produce
  byte-identical roots.

### Phase C — Integration

**C0. NeoVM genesis bootstrap helper** ✅ — `src/Neo.L2.Executor/NeoVMGenesisBootstrap.cs`
- Replicates the relevant slice of `NeoSystem.Blockchain.Initialize` without
  Akka actors. Runs `OnPersist` + `PostPersist` scripts against an
  `L2DataCacheAdapter`. Compiles + executes cleanly + propagates writes
  through to the underlying KV store.
- The earlier "cache propagation gap" diagnosis was wrong: writes DO
  propagate via Neo's standard child-cache `Commit()` chain. The actual
  bug was an `IsInitialized` false-positive (gas=0 ApplicationEngine.Create
  short-circuits before reading PolicyContract → returned true on empty
  stores). Fix: probe storage directly for PolicyContract's ExecFeeFactor key.
- **End-to-end verified**: `BootstrappedStore_RunsRealNeoVMScript_HALT`
  pins that after `Run()`, `ApplicationEngineTransactionExecutor` runs a
  real PUSH1 script through Neo VM and gets a Success receipt.

**C1. Devnet wiring** — `tools/Neo.L2.Devnet`
- Add `--executor neovm` flag to swap from `ReferenceTransactionExecutor`
  to `ApplicationEngineTransactionExecutor` for legacy compatibility.
- Add `--executor riscv` / `--executor neovm2-riscv` as the Neo N4 L2 path
  backed by `RiscVTransactionExecutor`.
- Add `--state-root merkle` flag to swap from `ReferenceBatchExecutor`
  to `MerkleStateBatchExecutor`.
- The two flags are independent — operators can mix and match.

**C2. End-to-end integration test**
- New `tests/Neo.L2.IntegrationTests/UT_E2E_RealVM_FullStack.cs`.
- Wires `ApplicationEngineTransactionExecutor` + `MerkleStateBatchExecutor`
  + existing `BatchBuilder` pipeline + the existing audit pipeline.
- Drives 5 batches with real Neo-VM script execution.
- Asserts: state-root continuity (each batch's preStateRoot ==
  previous batch's postStateRoot), audit pipeline accepts every batch,
  no FAULT-status transactions in happy-path.

## What's verifiable in this sandbox

✅ Compiles cleanly.
✅ Unit tests pass (happy-path, FAULT-path, gas-out-of-bounds).
✅ End-to-end integration test passes (state-root continuity via real VM).
🔴 Real-world gas accuracy / mainnet contract compatibility — needs an
   actual deployed contract suite to test against, beyond this sandbox's
   scope.

## Out of scope

- **Witness/signature verification** — ApplicationEngine handles this
  but the L2 transaction's witness format is a separate piece of the
  spec (currently ad-hoc). Phase A3 wires the engine; comprehensive
  witness verification across all signature schemes is a follow-up.
- **Cross-shard state reads** — interop with NeoHub L1 contracts during
  L2 execution. The framework already has L1 RPC pollers; wiring them
  into ApplicationEngine's interop service is a separate phase.
- **Custom executor support via Sample.CounterChainExecutor** — already
  shipped; `ApplicationEngineTransactionExecutor` is now a legacy NeoVM
  compatibility path, while Neo N4 L2 defaults to NeoVM2/RISC-V.

## LOC estimate

| Phase | Source LOC | Test LOC |
|-------|-----------:|---------:|
| A1 | ~150 | ~100 |
| A2 | ~80 | ~40 |
| A3 | ~250 | ~200 |
| A4 (just tests) | 0 | (counted in A3) |
| B1 | ~180 | ~150 |
| B2 | ~200 | ~150 |
| B3 (just tests) | 0 | (counted in B2) |
| C1 | ~50 | (counted in C2) |
| C2 | 0 | ~250 |
| **Total** | **~910** | **~890** |

## Execution order

Strict A1 → A2 → A3 → A4 → B1 → B2 → B3 → C1 → C2.

Each phase is a separate commit / PR. A1–A4 can ship independently
of B (the new transaction executor works against the existing
`ReferenceBatchExecutor`). B can ship independently of C.

## Risks

1. **`DataCache` snapshot semantics**: the abstract base class has
   subtle "uncommitted changes vs committed" behavior. Mistake here
   = state-root mismatches that only show up on reproducibility tests.
   Mitigation: A1's tests include explicit commit-then-reread cases.

2. **`Block` construction**: ApplicationEngine needs a "persisting
   block" with sane index/timestamp/witness. We construct a dummy
   in A3; if Neo VM expects specific fields populated, runs will
   FAULT in non-obvious ways. Mitigation: start with the simplest
   script (PUSH1) and grow.

3. **Merkle tree key ordering**: `IL2KeyValueStore.EnumeratePrefix`
   ordering must match what `KeyedStateMerkleTree` expects (sorted
   by key bytes, lexicographic). Mitigation: B1's first test asserts
   ordering explicitly against a known input.

4. **Gas accounting**: Neo's default `TestModeGas` is huge; production
   chains will want tx-specific gas budgets. A3 takes a per-tx
   gas-limit param. Test with both bounded and unbounded modes.
