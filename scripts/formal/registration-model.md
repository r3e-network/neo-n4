# Registration state machine model — 2026-09-18

Inductive safety model of the atomic `RegisterChain`. A handwritten abstraction, **not**
a C#/NeoVM program verifier. Roots are 256-bit vectors; booleans model config storage,
genesis-root storage, the OffsetActive bit, and observable active.

## What is proved

On a governed, single-entry registered chain, the state machine satisfies:

- **No partial initialization**: config and the genesis root are always stored together
  or absent together (`registered == genesis_set`). The old two-entry path that allowed
  "configured-but-unanchored" or "anchored-but-unconfigured" is gone, so `IsActive` can
  never be true while the genesis root is absent.
- **Genesis root non-zero and immutable**: every registered chain has a non-zero genesis
  root; re-registration may refresh config only while the root is unchanged, and no
  transition can rewrite a set root.
- **Sound active definition**: `IsActive(chainId) = config[OffsetActive]==1 && genesis-root present`,
  structurally excluding "active with no root".

Four transitions (`register`/`update`/`resume`/`pause`) each check three properties
(preserves invariant, active implies anchored, root immutable), plus the initial state and
the active definition: 13 UNSAT safety queries, 4 reachability SAT checks (prevent an empty
premise from being mistaken for evidence) and 3 negative controls (a dropped guard admits a
bad state).

Obligations: `verify_registration.py` 21 items, all pass (13 unsat / 8 sat).
Result is written to `registration-result.json`; solver version, source/script/spec hashes
and trusted assumptions land with the report.

## Trusted assumptions and limits

The model trusts: handwritten C#/NeoVM correspondence (not an extracted transition
relation), the single atomic registration entry, idempotent re-registration that only
refreshes config, atomic fault rollback without reentrancy, `IsActive` semantics, and no
governance rollback or concurrent transitions. It does **not** replace the VM tests
(`RegisterChain_IsAtomic_ActiveAndIdempotent` etc. pin the compiled bytecode behaviour) —
the model proves state-machine properties, the tests prove compiled product behaviour, and
the two are complementary.

The text anchor binds to the `RegisterChain` zero-root assertion; a source digest change
fails closed and demands a re-review of the correspondence. This model supplies the
"chain registered and genesis root present" premise the settlement model assumes; the
settlement transitions themselves are covered in settlement-model.md.

## Reproduce

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_registration.py
```

On Windows use `.venv-formal/Scripts/python`. Five self-tests cover source-guard changes,
line-ending normalization, UNKNOWN rejection, counterexample rejection, and the model +
negative controls.