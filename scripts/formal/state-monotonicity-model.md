# State monotonicity invariants model — 2026-09-18

Z3 symbolic execution model of the state-layer monotonicity: finalized state roots form a
continuous chain (pre-state of batch N+1 == post-state of batch N), the canonical state root
never spontaneously rewinds (only explicit governance revert of the latest finalized batch
restores its pre-state), and the Gateway finalized-through watermark strictly increases
(never decreases). A bounded-model-check over finalize/revert transitions. This covers the
**state-layer execution-semantics slice**; cryptographic state-root identity and full
execution semantics remain separate.

## Established invariants

- **Pre-state matches canonical**: a new batch's pre-state root must match the current
  canonical state root (chain continuity).
- **Canonical advances to post-state**: after finalize, the canonical root becomes the batch's
  post-state root.
- **Canonical root never spontaneously rewinds**: the canonical root changes only via explicit
  batch finalization or governance revert.
- **Gateway watermark monotonic**: the Gateway finalized-through watermark strictly increases.
- **Gateway-published irreversible**: once a batch is Gateway-published
  (`batchNumber <= gatewayWatermark`), it cannot be reverted.
- **Revert restores pre-state root**: reverting the latest finalized batch restores the
  canonical root to that batch's pre-state root.

Three negative controls flip to unsat: pre-state mismatch (violates chain continuity),
Gateway watermark rewind (violates monotonicity), and attempting to revert a
Gateway-published batch (violates irreversibility).

`verify_state_monotonicity.py` has 9 obligations (6 invariants, 3 SAT/unsat controls), all
pass. `state-monotonicity-result.json` records scope and trusted assumptions. Two self-tests
verify all obligations and Z3 importability.

## Honest scope

This models the **state-root chain continuity and Gateway watermark monotonicity**
(state-layer execution semantics). It does not model cryptographic state-root identity (the
mapping from EVM state to SHA-256 roots), full execution semantics (EVM execution, contract
interactions), or the Gateway federation protocol. The bounded-model-check uses symbolic
variables; the invariants hold for unbounded sequences by construction.

## Reproduce

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_state_monotonicity.py
```

On Windows use `.venv-formal/Scripts/python`. Requires `z3-solver` (pinned in requirements).