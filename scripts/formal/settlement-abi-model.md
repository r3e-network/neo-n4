# Settlement method-surface correspondence model — 2026-09-18

Spec-to-implementation correspondence model for doc.md §3.2's settlement method surface
versus the deployed RollupHub contract manifest (parsed from the regenerated
TestingArtifacts — the same NEF/manifest the VmTests execute against). A handwritten
abstraction, **not** a C#/NeoVM verifier.

## What this model caught and now pins

Running this check surfaced a real doc↔code discrepancy: doc.md §3.2 declared
`revertBatch(chainId, batchNumber)` and `lockGovernance()`, but the contract implemented
neither. Both are now implemented (see CHANGELOG), and the model pins the closed surface:

- **Every doc-declared settlement method exists with the doc-declared parameter count**:
  `submitBatch(4)`, `submitAndFinalizeBatch(4)`, `finalizeBatch(2)`, `revertBatch(2)`,
  `publishGatewayGlobalRoot` (long tail, existence-checked), `setGovernanceController(1)`,
  `lockGovernance(0)`, `isGovernanceLocked(0)`, `getCanonicalStateRoot(1)`,
  `isProofTypeCompatible(2)`. doc.md §3.2's signatures were updated to record the 4th
  `forcedInclusionCount` parameter that the 352-byte public-inputs domain seals in
  (batch-spec model).
- Revert/lock invariants: `revertBatch` takes (chainId, batchNumber); `lockGovernance` is
  parameterless; `isGovernanceLocked` is queryable.
- Registry surface presence (`registerChain`/`updateChain`/`pauseChain`/`resumeChain`): the
  post-lock authorization guards in this contract are the only authority path, so the owner
  cannot route around them through a missing entry point.

`verify_settlement_abi.py` has 17 obligations, all pass. `settlement-abi-result.json` records
spec/script hashes and trusted assumptions. Three self-tests: all-green, missing-method
fail-closed, and missing-manifest fail-closed.

## Boundary and limits

This models the **method-surface correspondence** — that the doc-declared settlement ABI is
implemented with the declared arity. It does not model the bodies' execution semantics, the
GovernanceController relay-proposal machinery, signature/witness verification, or the bridge
ABI (separate obligations). Post-lock authorization relies on `Runtime.CheckWitness` of the
GovernanceController contract; the relay path's execution semantics live in
GovernanceController.

## Reproduce

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_settlement_abi.py
```

On Windows use `.venv-formal/Scripts/python`.