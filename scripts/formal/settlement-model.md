# Settlement state machine model — 2026-09-17

`scripts/formal/verify_settlement.py` adds the repo's second SMT model: an
inductive safety proof of a registered chain's settlement state machine, the core
path of `contracts/NeoHub.RollupHub/RollupHubContract.cs` (doc.md §3.2, §8.3).

## What is established

Over states with finalized history (per-height pre/post/verified arrays), an
optional pending submission, and a canonical root, the model proves:

- **Base**: the registered but not yet settled state (height 0, canonical root = immutable nonzero
  genesis root) satisfies the invariant; the genesis domain is nonempty.
- **Inductive step** for submit, two-step finalize, atomic submit-and-finalize,
  and fault stutter: each transition's premise is satisfiable, and its successor
  preserves the invariant — batch N's pre-root equals batch N-1's post-root
  (genesis for batch 1), only proof-verified inputs finalize, canonical root
  equals the last finalized post-root, heights are contiguous and do not wrap,
  and the finalized history is immutable.
- **Three negative controls**: deleting the pre-root linkage guard, freezing the
  canonical-root update, or finalizing an unverified batch each yields a concrete
  counterexample. The checker itself is therefore not vacuous.

This is induction over a symbolic transition relation, not enumeration of a few
batches. The report records solver version, source and script SHA-256, per-query
results, exit code and explicit assumptions, with `wholeSystemVerified: false`.

## Correspondence and assumptions

`RollupHubContract.cs` SHA-256 (CRLF-normalized) is pinned; any change forces
re-review. The model was checked against `SubmitBatchCore`, `FinalizeBatch`,
`FinalizeBatchInternal`, `GetCanonicalStateRoot`, `GetLatestFinalizedBatchNumber`,
`RecordBatchDAInternal` and `ConsumeForcedTransactionsInternal`, and against the
existing VM regressions (15/15 passing). The model **trusts** rather than proves:
C#/NeoVM atomicity and rollback, guard reachability, no reentrancy or hidden
storage writers, the exact ABI (heights as mathematical integers, roots as
256-bit words), `proof_ok` modeling external verifier success, and the absence of
governance rollback/reconfiguration/concurrency transitions. The doc.md registry
requires atomic config+genesis registration while the contract exposes separate
`RegisterChain` / `RegisterGenesisStateRoot` entry points; the model scopes itself
to the post-registration state and does not bless that discrepancy.

## Implementation finding REG-ATOMIC-01

Status: closed (atomic ABI + genesis guard). `RegisterChain(uint chainId, byte[]
configBytes, UInt256 genesisStateRoot)` is now the single registration entry and
atomically persists config and the immutable non-zero genesis root in one call
(doc.md §3.2). The separate `registerGenesisStateRoot` ABI was removed, so no
two-transaction path exists. `IsActive(chainId)` returns
`config[OffsetActive] == 1 && Storage.Get(GenesisRootKey(chainId)) != null`, so a
partially initialized chain is never observable as active. Re-registration is
idempotent: it may refresh config only while the already-registered genesis root
is unchanged (a different root is rejected), and a zero root is rejected up front.

Pinned by VM tests: `RegisterChain_IsAtomic_ActiveAndIdempotent` (zero root rejected,
same-root idempotent re-registration, different root rejected, pause/resume/update
all gated on the anchored root) and `RegisterChain_Atomic_Success`. `GetCanonicalStateRoot`
(900–906) faults if neither canonical nor genesis storage exists, so an unanchored
first batch cannot be accepted. The settlement transition model assumes both
registrations have completed, which is now exactly what the atomic call guarantees.

## Reproduce

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_settlement.py
```

Self-tests (7) include: removed pre-root guard rejected via digest change,
UNKNOWN and counterexample injection cannot pass, CRLF-only source changes do not
trigger re-review, and all three negative controls produce witnesses. Results are
written to `settlement-result.json`; failing queries, digest drift or exceptions
exit 1.
