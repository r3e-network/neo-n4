# Sample L2 contracts

Original Neo smart contracts demonstrating common L2-aware patterns. Each is a
short, copy-friendly example showing how an app contract integrates with the
`L2Native.*` system contracts already in `contracts/`.

## What's here

| Sample | What it demonstrates |
|--------|----------------------|
| [`Sample.CrossChainGreeter`](./Sample.CrossChainGreeter/) | Emit an L2 → L1 (or L2 → L2) cross-chain message via `L2Native.L2MessageContract.EmitMessage`. App-defined messageType byte; receiver dispatches on it. |
| [`Sample.WithdrawalDemo`](./Sample.WithdrawalDemo/) | Initiate a bridged-asset withdrawal via `L2Native.L2BridgeContract.InitiateWithdrawal`. Burn-on-L2 + emit a withdrawal record into the next batch's Merkle tree. |

## Building

Each sample is a standalone `.csproj` that imports `contracts/Directory.Build.props`
(via `<Import>` in its csproj). Build identically to the production contracts:

```bash
# C# type-check only (no nccs needed)
dotnet build samples/contracts/Sample.CrossChainGreeter \
    /p:NuGetAudit=false /p:DisableNccs=true

# Full bytecode + manifest emission (needs nccs on PATH)
dotnet build samples/contracts/Sample.CrossChainGreeter /p:NuGetAudit=false
# → samples/contracts/Sample.CrossChainGreeter/bin/sc/Sample.CrossChainGreeter.{nef,manifest.json}
```

## Wiring after deploy

Each sample takes its `L2Native.*` partner hash as deploy data. The deploy flow:

1. Deploy the L2 native suite as part of an L2's genesis (the chain operator's
   responsibility — happens once per L2).
2. Discover the deployed L2Native hashes from your L2 node's chain-state cache
   (or from the deploy bundle if you ran `neo-stack init-l2`).
3. Deploy this sample with `(owner, <l2NativeHash>)` as the deploy data tuple
   — exactly what `_deploy(object data, bool update)` parses.
4. Call the sample's user-facing method (`SendGreeting` / `WithdrawTo`) from
   another contract or directly from a Neo wallet.

The samples don't ship their own deploy plan; an operator wiring them into a
real L2 typically extends `tools/Neo.Hub.Deploy/ScaffoldPlan.cs` to add the
sample as a step alongside the L2Native ones.

## Adding your own sample

The recipe mirrors `contracts/L2Native.*` exactly:

1. Create `samples/contracts/Sample.<Name>/Sample.<Name>.csproj` — a one-line
   wrapper that `<Import>`s `..\..\..\contracts\Directory.Build.props`.
2. Class under `Sample.<Name>` namespace, ending in the descriptive name (no
   `Contract` suffix needed for samples — these are illustrative).
3. Decorate with `[DisplayName]`, `[ContractAuthor]`, `[ContractDescription]`,
   `[ContractVersion]`, `[ContractSourceCode]`, `[ContractPermission(Permission.Any, Method.Any)]`.
4. Storage prefixes per logical map; `_deploy(object data, bool update)` for
   one-shot wiring; `[Safe]` on read-only methods; `Contract.Call` to invoke
   the L2Native partners.

## Reference

- L2Native contracts the samples integrate with: [`contracts/L2Native.*`](../../contracts/)
- Operator wiring guide: [`docs/launching-an-l2.md`](../../docs/launching-an-l2.md)
- Tech-stack coverage: [`docs/tech-stack-coverage.md`](../../docs/tech-stack-coverage.md)
