# Gateway outbox crash-recovery consistency model — 2026-09-18

Consistency model of the Gateway outbox rehydration `Recover()` in
`src/Neo.Plugins.L2Gateway/GatewayOutbox.cs` (lines 179-242). It proves the
**protocol-layer** crash-recovery guarantees the implementation documents: how a persisted
snapshot (per-constituent item states plus an optional publication checkpoint) maps to the
recovered sealed-items set and active publication. A handwritten abstraction, **not** a
verification of RocksDB internals or of C#/NeoVM.

## Established properties

- A **Confirmed item is never re-sealed**: recovery never returns a confirmed constituent to
  the sealed set (no re-publication of an already-reconciled batch after a crash).
- An **orphaned Proving item is demoted to Sealed** — a crash before publication leaves the
  item restorable, not lost, and not stranded in a half-proven state.
- A **non-active item in a later state** (Proved/Submitted/Poisoned) is treated as corruption,
  not silently rehydrated: without an active checkpoint such a state cannot legitimately
  exist, so `Recover()` throws rather than inventing a sealed copy.
- A **declared checkpoint constituent must be present** in the store; a checkpoint referencing
  a missing item is corruption.
- **Recovery is a deterministic function of the persisted snapshot**: two constituents with
  identical (active-reference, persisted-state) inputs rehydrate identically.

`verify_outbox_recovery.py` has 7 obligations (5 UNSAT safety, 1 SAT corruption-feasibility,
1 SAT valid-snapshot feasibility). All pass. `gateway-outbox-recovery-result.json` records
solver, source/script/spec hashes and trusted assumptions.

## Boundary and limits

This models the **recovery protocol** — the mapping the implementation performs on crash and
the consistency invariants it enforces. It does **not** model RocksDB's internal
crash-consistency (WAL, atomicity of a single write), the durable write ordering argument
(SavePublication writes the checkpoint before its constituents; MarkConfirmed confirms before
deleting), or the L1 RPC confirmation semantics. Those are separate obligations covered by
the persistence tests and outbox safety/liveness models. The item-key-to-commitment binding
is trusted from the source (`BuildItemKey`/`DecodeItem`).

## Reproduce

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_outbox_recovery.py
```

On Windows use `.venv-formal/Scripts/python`. Five self-tests cover source drift, line
endings, UNKNOWN, counterexample rejection, and the normal model/negative controls.