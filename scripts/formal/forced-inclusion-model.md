# Forced-inclusion queue model — 2026-09-18

Inductive safety model of the RollupHub anti-censorship FIFO queue:
`EnqueueForcedTransaction`, `ConsumeForcedTransactionsInternal`,
`GetNextForcedNonce`, and `GetPendingForcedCount` (contract lines 284–320).
This is a handwritten abstraction, **not** a C#/NeoVM verifier.

## Established properties

- `head` and `tail` start at zero and remain in the ordered `uint64` domain.
- Enqueue returns the old `tail` nonce and advances `tail` strictly, so consecutive
  forced transactions receive unique nonces; `head` is not changed by enqueue.
- Consume advances only `head`; the contract guard `head + count <= tail` prevents
  underflow and preserves FIFO order.
- `head` and `tail` are monotone, `tail - head` is never negative, and pending count
  is therefore a sound queue-depth measure.
- Three negative controls construct witnesses when the underflow guard, the uint64
  no-wrap boundary guard, or the FIFO pointer discipline is removed.

`verify_forced_inclusion.py` has 18 obligations: initial-state checks, 2 transition
families with invariant/monotonicity/pending checks, a strict nonce-increase proof,
and 3 SAT negative controls. All pass. `forced-inclusion-result.json` records solver,
source/script/spec hashes and trusted assumptions.

## Boundary and limits

The model bounds pointers to `0..2^64-1`, matching the contract's `ulong` read path.
Storage writes use `BigInteger`; an enqueue at `ulong.MaxValue` would require a
separate protocol-level decision because the subsequent `ulong` read can wrap.
The model therefore treats the no-wrap guard as a load-bearing boundary and reports
that witness explicitly rather than claiming protection beyond the current contract.
It does not prove transaction uniqueness, signature validity, DA inclusion, batch
public-input binding, or concurrent producer/consumer behavior; those are separate
proof obligations. The batch's `forcedInclusionCount` binding is trusted here and
covered by the public-input tests/settlement model.

## Reproduce

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_forced_inclusion.py
```

On Windows use `.venv-formal/Scripts/python`. Five self-tests cover source drift,
line endings, UNKNOWN, counterexample rejection, and the normal model/negative controls.