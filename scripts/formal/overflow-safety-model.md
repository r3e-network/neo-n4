# Arithmetic overflow safety model — 2026-09-18

Z3 bitvector model of the arithmetic overflow-safety layer: RollupHub uses `ulong` (uint64)
for batch numbers, block numbers, and forced-inclusion nonces. These must never overflow in
practice (a 2^64 batch sequence at 1 batch/second would take ~584 billion years). This model
checks the overflow-safety invariants with 64-bit bitvectors: batch/block/nonce increments
are safe (far below 2^63), and key-construction helpers produce collision-free keys. This
covers the **overflow-safety execution-semantics slice**; full execution semantics remain
separate.

## Established invariants

- **Batch increment safe**: incrementing a batch number that is far below 2^64 never overflows.
- **Block increment safe**: incrementing a block number that is far below 2^64 never overflows.
- **Nonce increment safe**: incrementing a forced-inclusion nonce that is far below 2^64 never
  overflows.
- **Batch key chainId collision-free**: different `(chainId, batchNumber)` pairs produce
  different `BatchCommitmentKey` prefixes (key structure: prefix + chainId(4B) + batch(8B)).
- **Batch key batch collision-free**: same chainId, different batch => different keys.
- **Forced key nonce collision-free**: different `(chainId, nonce)` pairs for forced-inclusion
  produce different `ForcedTxKey` storage keys.

Two negative controls: incrementing a value at the 2^64 boundary overflows (wraps to 0, flips
to unsat); two keys with identical `(chainId, batch)` parameters are identical (collision, sat).

`verify_overflow_safety.py` has 8 obligations (6 safety invariants, 2 controls), all pass.
`overflow-safety-result.json` records scope and trusted assumptions. Two self-tests verify all
obligations and Z3 importability.

## Honest scope

This models the **arithmetic overflow-safety slice** (ulong increments and key-construction
non-collision). It does not model full execution semantics (EVM execution, contract
interactions), cryptographic identity, or storage encoding beyond key construction. Practical
batch/block/nonce sequences stay far below 2^63; the model uses a conservative bound.

## Reproduce

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_overflow_safety.py
```

On Windows use `.venv-formal/Scripts/python`. Requires `z3-solver` (pinned in requirements).