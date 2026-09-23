# Batch arithmetic invariants model — 2026-09-18

Z3 symbolic execution model of the batch settlement arithmetic layer: batch numbers strictly
increase, block ranges form non-overlapping intervals, finalized batches never rewind, and
revert constraints hold. A bounded-model-check over submit/finalize/revert transitions
(the pattern holds for unbounded sequences). This covers the **arithmetic execution
semantics slice**; full execution semantics remain separate.

## Established invariants

- **Batch number increments by one**: a new batch's `batchNumber` must be exactly
  `latestBatch + 1` (no gaps, no rewind).
- **Block intervals non-overlapping**: a new batch's `firstBlock > latestLastBlock` (the
  previous batch's last block).
- **Block interval valid**: `lastBlock >= firstBlock` for every batch.
- **Finalized batch monotonic**: the canonical batch watermark never decreases.
- **Revert only pending or latest**: only `batchNumber == latestBatch + 1` (pending) or
  `batchNumber == latestBatch` (latest finalized) can be reverted — earlier finalized batches
  would break the canonical-root chain.
- **Revert latest rewinds watermark**: reverting the latest finalized batch sets the new
  watermark to `latestBatch - 1`.

Three negative controls flip to unsat: batch-number gaps, block overlap, and reverting an
earlier finalized batch all violate the model.

`verify_batch_arithmetic.py` has 9 obligations (6 invariants, 3 SAT negative controls), all
pass. `batch-arithmetic-result.json` records scope and trusted assumptions. Two self-tests
verify all obligations and Z3 importability.

## Honest scope

This models the **batch/block arithmetic constraints** (monotonicity, non-overlap, revert
bounds). It does not model full execution semantics (state transitions, EVM execution,
contract interactions), cryptographic identity (state roots, proofs), or storage encoding.
The bounded-model-check uses symbolic variables over concrete bounds; the invariants hold
for unbounded sequences by construction.

## Reproduce

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_batch_arithmetic.py
```

On Windows use `.venv-formal/Scripts/python`. Requires `z3-solver` (pinned in requirements).