# Custom transaction executors

The Neo Elastic Network framework's `Neo.L2.Executor.ITransactionExecutor`
seam is the plug-in point for "what happens when a transaction lands on this
L2." This directory contains both the **working reference** that demonstrates
a real custom executor end-to-end, and the **default location** the
[`neo-stack scaffold-executor`](#scaffolding-your-own) helper writes to.

## Contents

| Path | Role |
|------|------|
| [`Sample.CounterChainExecutor/`](./Sample.CounterChainExecutor) | Runnable reference with three opcodes (`IncrementCounter` / `EmitWithdrawal` / `EmitMessage`). State mutation via `KeyedStateStoreAdapter`, withdrawal emission, L2→L2 messaging via canonical `MessageBuilder.Build`, full SPEC.md determinism. **Operators reading this for the first time start here.** |
| `<Name>Executor/` | Where `neo-stack scaffold-executor --name <Name>` writes a starter project (csproj + executor skeleton + state seam + tx builder + state-store adapter + README) when run from the monorepo root. With `--with-tests`, `<Name>Executor.UnitTests/` lands as a sibling. |

## Scaffolding your own

Three CLI paths. Use whichever matches how much existing structure you want:

```bash
# 1. Just the executor (csproj + 4 source files + README, builds as-is):
neo-stack scaffold-executor --name MyChain --chain-id 1099

# 2. Executor + companion test project (csproj + 3 starter tests):
neo-stack scaffold-executor --name MyChain --chain-id 1099 --with-tests

# 3. Composite: chain.config.json + node working dirs + executor + tests
#    (the recommended starting point — produces a buildable + testable +
#    devnet-previewable starter at ./chain-1099/):
neo-stack new-l2 --name MyChain --chain-id 1099
```

After any of these, the resulting project compiles + tests pass with no
edits — the placeholder `NoOp` opcode is enough to exercise the seam. As
you replace `NoOp` with real opcodes, mirror new tests against the working
sample's pattern at
[`tests/Sample.CounterChainExecutor.UnitTests/UT_CounterChainExecutor.cs`](../../tests/Sample.CounterChainExecutor.UnitTests/UT_CounterChainExecutor.cs).

## Seeing it run end-to-end

The in-process devnet has a `--executor` flag that swaps the no-op
`ReferenceTransactionExecutor` for `Sample.CounterChainExecutor`:

```bash
# Same pipeline as the default devnet (deposits + sealing + proving +
# settlement + audit), but each batch also runs three Counter txs:
dotnet run --project tools/Neo.L2.Devnet -- 5 --executor counter
```

Look for the `[exec] N Counter txs → gas=… txRoot=… l2L2Root=…` line per
batch — gas + roots all come from the Counter executor's actual outputs.

The full custom-executor + framework wiring is integration-tested at
[`tests/Neo.L2.IntegrationTests/UT_E2E_CustomExecutor_FullStack.cs`](../../tests/Neo.L2.IntegrationTests/UT_E2E_CustomExecutor_FullStack.cs).
That test pins: all 4 batch roots non-zero across a 3-batch run, state-root
continuity + uniqueness across batches, multisig verifier accepts
custom-executor commitments, BatchSerializer encode/decode round-trips,
final state has the expected counter entries, and a failed-tx batch
defends the withdrawal/message channels from polluted output.

## Reference

- Determinism contract: [`Neo.L2.Executor/SPEC.md`](../../src/Neo.L2.Executor/SPEC.md)
- Full launching-an-l2 walkthrough: [`docs/launching-an-l2.md`](../../docs/launching-an-l2.md)
- Mapping doc.md → code: [`AGENTS.md`](../../AGENTS.md)
- Per-component tests: [`IMPLEMENTATION_STATUS.md`](../../IMPLEMENTATION_STATUS.md)
