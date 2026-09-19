# Gateway outbox crash-atomicity (write-order) model — 2026-09-18

Inductive model of the durable write ordering in
`src/Neo.Plugins.L2Gateway/GatewayOutbox.cs` (`SavePublication`/`MarkConfirmed`). A
handwritten abstraction, **not** a verification of RocksDB internals or of C#/NeoVM.

## What is established

SavePublication writes the checkpoint first, then transitions each constituent to the
publication state; MarkConfirmed transitions each constituent to Confirmed, then deletes the
checkpoint. A crash can occur between any two writes. Modeled as four single-write operations,
an induction proves the crash-atomicity invariant is preserved at every step and holds at the
base state:

- **A constituent item is in a publication state only while the checkpoint is present.**
  With the checkpoint absent, every item is Sealed or Confirmed — never an orphaned
  publication state. This is exactly the write-order guarantee the code comment records
  ("a crash between operations is recoverable from the checkpoint plus sealed items").
- The base state (checkpoint absent, all Sealed) satisfies the invariant.
- Each write operation — write-checkpoint, transition-to-pubstate, confirm-item,
  delete-checkpoint — preserves it, so *every* reachable crash-interleaving snapshot is
  recoverable.

`verify_outbox_crashatomic.py` has 9 obligations (5 UNSAT induction steps + 2 SAT feasibility
+ 2 SAT negative controls for a delete-with-orphaned-pub-state and a transition-without-
checkpoint). All pass. `gateway-outbox-crashatomic-result.json` records solver, source/script/
spec hashes and trusted assumptions.

## Boundary and limits

This covers the **write-ordering crash-atomicity** at the protocol layer: given the
documented order, every crash snapshot is recoverable. It does **not** model RocksDB's
internal WAL or the atomicity of a single storage write — those are external to this code and
would need a RocksDB-formalization facility. The per-write durability (`Sync`/`Put` semantics)
is trusted; the model reasons about the order across writes, not the store's crash behavior
within a single write. The item-key-to-commitment binding is trusted from the source.

## Reproduce

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_outbox_crashatomic.py
```

On Windows use `.venv-formal/Scripts/python`. Four self-tests cover source drift, line
endings, UNKNOWN, and the induction obligations.