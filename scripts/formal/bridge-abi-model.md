# Bridge method-surface correspondence model — 2026-09-18

Spec-to-implementation correspondence model for doc.md §10/§11's bridge method surface
versus the deployed manifests. doc declares one logical bridge method list; the
implementation splits it across the **L1-side SharedBridge contract** (parsed from the
regenerated TestingArtifacts manifest) and the **L2-side native bridge** (external/neo
`L2NativeContracts.cs`, source-anchored). A handwritten abstraction, **not** a C#/NeoVM
verifier.

## What the correspondence table pins

Every doc-declared method is recorded at its documented location with the documented arity:

- SharedBridge: `registerMapping(1)`, `getL2Asset(2)`, `deposit(4)`,
  `publishMessageRoots(4)`, `sendMessage(3)`, `isL2ToL1MessageConsumed(1)`,
  `finalizeWithdrawal(9)`.
- L2 native bridge (source-anchored): `GetL1Asset`, `GetL2Asset`.

Documented renames/merges/supersets are pinned so the drift history is not lost:

- `isMessageConsumed` → **renamed** `isL2ToL1MessageConsumed` (the stale doc-era name is
  flagged as drift if re-introduced).
- `routeMessage` + `enqueueL1ToL2Message` → **merged** into `sendMessage`.
- `finalizeWithdrawal`'s doc arity (4) was **deliberately superseded** (9; At/WithProof/
  Emergency variants 10/12/12) by the V5 leaf-hash binding; all four variants must exist.
- `deposit` arity (4) matches doc; the parameter ORDER differs (asset-first vs
  targetChainId-first) and is recorded in doc.md §10.

`verify_bridge_abi.py` has 16 obligations, all pass. `bridge-abi-result.json` records
spec/script hashes and trusted assumptions. Four self-tests: all-green, missing-method
fail-closed, rename-drift caught, and missing-manifest fail-closed.

## Boundary and limits

This models the **method-surface correspondence** — that the doc-declared bridge ABI is
implemented at its documented location with the documented arity. It does not model the
deposit/withdrawal state machines (bridge-conservation model), the withdrawal-leaf Merkle
binding (batch-spec/preimage models + VmTests), or the external-chain (EVM/Tron/Solana)
watcher architecture. doc.md §10's method list was updated to the implemented surface in
the same change set; the doc-era names are preserved here as the rename/merge history.

## Reproduce

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_bridge_abi.py
```

On Windows use `.venv-formal/Scripts/python`.