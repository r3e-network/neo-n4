# Cross-component pipeline liveness model — 2026-09-18

CTL temporal-logic composition model of the sequencer batch pipeline: the anti-censorship
forced-inclusion FIFO queue **composed with** the batch settlement lifecycle, checked with
the pyModelChecking model checker over a product Kripke structure. This extends the
single-component outbox liveness model to the **cross-component composition** slice.

## Composition

States are `(queueDepth ∈ {0,1,2}, phase ∈ {idle, sealed, proving, proved, submitted,
confirmed})` — 18 states. Transitions: users enqueue forced txs while collecting; sealing a
batch consumes the entire queue (full-drain discipline); the batch then proves (with retry
self-loop), is submitted, confirmed, and the epoch completes back to collecting. The Kripke
structure is total (every state has a successor).

## Established cross-component properties

- **Pipeline fully drainable**: `AG EF(confirmed ∧ queue-empty)` — from every reachable state
  there is a path to a fully drained pipeline (batch confirmed, queue empty).
- **Queue drains via batch**: `AG(queue-nonempty → EF(sealed))` — queued forced transactions
  are consumed only by a batch taking them; they are never silently dropped.
- **No deadlock trap**: `AG EF(sealed ∨ confirmed)`.
- `AG(idle → EF(sealed))` and `AG(submitted → EF(confirmed))` — every phase advances.

Two negative controls flip: without the seal-consumption edge, a queue-full deadlock (queued
transactions that can never be consumed) is reachable; without the submit edge, Confirmed is
unreachable from the whole pipeline.

`verify_pipeline_liveness.py` has 7 obligations (5 cross-component liveness, 2 SAT negative
controls), all pass. `pipeline-liveness-result.json` records tool, scope and trusted
assumptions. Two self-tests verify all obligations and tool importability.

## Honest scope

This covers the **two-component sequencer pipeline composition** (queue × batch lifecycle).
It does not model full multi-component network composition (Gateway federation, multi-chain
routing), network-time liveness, SP1 proof liveness, or Byzantine-fault scenarios. The queue
is bounded to depth 2 (bounded-model-check; the unbounded FIFO invariant is proved separately
in forced-inclusion-model.md); sealing consumes all queued txs (full-drain discipline; the
FIFO model covers partial consumption). Retry failures are folded into a proving self-loop.

## Reproduce

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_pipeline_liveness.py
```

On Windows use `.venv-formal/Scripts/python`. Requires `pyModelChecking==1.3.4`.