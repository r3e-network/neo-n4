# Sample.CounterChainExecutor

The working reference implementation of `Neo.L2.Executor.ITransactionExecutor`
for the Neo Elastic Network. Operators forking this sample to bootstrap
their own custom L2 chain logic see a complete, end-to-end-tested example
of how a custom executor plugs into the framework.

## What's in here

| File | Role |
|------|------|
| [`CounterChainExecutor.cs`](./CounterChainExecutor.cs) | The executor — implements `ITransactionExecutor.ExecuteAsync` for three opcodes (`IncrementCounter` / `EmitWithdrawal` / `EmitMessage`). |
| [`ICounterChainState.cs`](./ICounterChainState.cs) | State seam + `InMemoryCounterChainState` for tests. |
| [`CounterTxBuilder.cs`](./CounterTxBuilder.cs) | Canonical tx-byte builders (mirrors the executor's decoder). |
| [`KeyedStateStoreAdapter.cs`](./KeyedStateStoreAdapter.cs) | Production bridge to `Neo.L2.Executor.State.KeyedStateStore`. |
| [`Sample.CounterChainExecutor.csproj`](./Sample.CounterChainExecutor.csproj) | Builds against `Neo.L2.Abstractions` + `Neo.L2.Executor`. |

## The three opcodes

```
Opcode 0x01 (IncrementCounter):  [1B opcode][20B sender][8B u64 amount LE]
Opcode 0x02 (EmitWithdrawal):    [1B opcode][20B recipient][20B token][8B u64 amount LE]
Opcode 0x03 (EmitMessage):       [1B opcode][4B destChainId LE][2B msgLen LE][N bytes msg]
```

- `IncrementCounter` mutates per-sender state via the state seam. Demonstrates
  the read-modify-write pattern with documented `ulong` wraparound semantics
  matching Neo NEP-17.
- `EmitWithdrawal` produces a `WithdrawalRequest` with a deterministic
  `txHash`-derived nonce — no clock or RNG, so the SPEC.md determinism
  contract holds. Zero-amount withdrawals are rejected at execution time.
- `EmitMessage` builds a `CrossChainMessage` via canonical
  `MessageBuilder.Build`, inheriting the framework's hash composition +
  self-routed (source==target) rejection.

## How it plugs in

The framework's seam takes `ITransactionExecutor` via constructor injection
into `ReferenceBatchExecutor`. With `KeyedStateStoreAdapter` wired, the
executor's state writes flow into the same `KeyedStateStore` the post-state-
root oracle hashes — so `BatchExecutionResult.PostStateRoot` reflects the
executor's actual mutations:

```csharp
var stateStore = new KeyedStateStore();             // production: rocksdb-backed
var stateAdapter = new KeyedStateStoreAdapter(stateStore);
var executor = new CounterChainExecutor(
    chainId: 1100,
    state: stateAdapter,
    emittingContract: emittingContractHash);
var batchExecutor = new ReferenceBatchExecutor(
    txExecutor: executor,                            // ← injected
    postStateRootOracle: new KeyedStateRootOracle(stateStore),
    l1Processor: depositProcessor);
```

Sealing / proving / settlement / fraud-proof inherit from the standard
pipeline with no further wiring.

## Tests + run-through

| What to read | Where |
|--------------|-------|
| Per-opcode unit tests (16 total — happy path + edge cases + determinism + adapter parity) | [`tests/Sample.CounterChainExecutor.UnitTests/`](../../../tests/Sample.CounterChainExecutor.UnitTests) |
| End-to-end integration test (3-batch run through full pipeline + multisig prover/verifier + commitment round-trip + failed-tx defense) | [`tests/Neo.L2.IntegrationTests/UT_E2E_CustomExecutor_FullStack.cs`](../../../tests/Neo.L2.IntegrationTests/UT_E2E_CustomExecutor_FullStack.cs) |
| Run it through the in-process devnet (deposits + Counter txs + sealing + proving + audit) | `dotnet run --project tools/Neo.L2.Devnet -- 5 --executor counter` |

## Forking this for your own chain

For a one-command starter mirroring this sample's shape (with a placeholder
NoOp opcode and a sibling MSTest project), see the parent
[`samples/executors/README.md`](../README.md) for `neo-stack scaffold-executor`
+ `neo-stack new-l2`. The 5-step "make it your chain" checklist:

1. **Define your opcodes.** Edit the `Opcode` enum + add `Execute<Op>`
   private methods to dispatch.
2. **Match tx builders.** Add `<Name>TxBuilder.<Op>(...)` for each opcode —
   keeps encode + decode in one repo.
3. **Define your state model.** The `ICounterChainState` seam is intentionally
   byte-array-level so both `InMemoryCounterChainState` (tests) and
   `KeyedStateStoreAdapter` (production) implement it cleanly.
4. **Mirror new opcodes' tests.** Use the per-opcode happy path + edge case
   pattern from this sample's tests.
5. **Wire into a batch executor.** Same as above — pass to
   `ReferenceBatchExecutor` and the standard pipeline takes it from there.

## Reference

- Determinism contract: [`Neo.L2.Executor/SPEC.md`](../../../src/Neo.L2.Executor/SPEC.md)
- Full launching-an-l2 walkthrough: [`docs/launching-an-l2.md`](../../../docs/launching-an-l2.md)
- Mapping doc.md → code: [`AGENTS.md`](../../../AGENTS.md)
