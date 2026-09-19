# DA failover model — 2026-09-18

Inductive safety model of the write-side failover policy in
`src/Neo.Plugins.L2DA/FailoverDAWriter.cs`. A handwritten abstraction, **not** a
C#/NeoVM verifier. It models the ordered-tier publish loop and its profile invariants.

## Established properties

- Only a **transient** `IOException` advances to the next tier; a non-transient exception,
  cancellation, or a malformed / non-binding receipt aborts immediately and is **never
  masked by a fallback**.
- A successful publish returns only a receipt that binds the published payload
  (`Crypto.Hash256(payload)`) and carries this profile's required metadata.
- The constructor enforces that every tier shares the primary's `DAMode` and a common
  non-`Unspecified` `DAReceiptKind`, so failover never downgrades the DA profile; the
  publish loop therefore never runs without that shared profile.
- Success and fatal outcomes are disjoint; the set reports "all tiers failed" if and only
  if every tier was transient.

`verify_da_failover.py` has 14 obligations (7 UNSAT safety, 4 SAT feasibility, 3 SAT
negative controls) over a fixed three-tier model (the policy holds for any N; three tiers
keep the formulas ground). All pass. `da-failover-result.json` records solver, source/script/
spec hashes and trusted assumptions.

## Boundary and limits

This models the **write-side failover policy** — how a transiently-failed publish falls
through to the next same-profile endpoint, and how a non-transient failure or a bad receipt
is surfaced instead of silently downgraded. It does **not** model RocksDB crash-consistency,
the durability/replay of a persisted outbox, or the L1 confirm path; those are separate
obligations. Transience is modeled at the level the code expresses it
(`IOException` excluding `InvalidDataException`); the actual exception taxonomy is trusted
from the source.

## Reproduce

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_da_failover.py
```

On Windows use `.venv-formal/Scripts/python`. Five self-tests cover source drift, line
endings, UNKNOWN, counterexample rejection, and the normal model/negative controls.