# Chain-config spec-correspondence model — 2026-09-18

Spec-to-implementation correspondence model for doc.md §3.2's `L2ChainConfig` struct versus
the 91-byte wire serializer (`src/Neo.L2.Abstractions/Models/L2ChainConfigSerializer.cs`).
A handwritten abstraction, **not** a C#/NeoVM verifier.

## Established properties

The 12 fields doc.md §3.2 declares (chainId, operatorManager, verifier, bridgeAdapter,
messageAdapter, securityLevel, daMode, gatewayEnabled, permissionlessExit, sequencerModel,
exitModel, active) **exactly partition** the 91-byte wire domain:

- Offsets are contiguous with no gaps and no overlaps, strictly increasing in doc.md field
  order, and the last field ends exactly at `ConfigSize == 91 == sum(field widths)`.
- The encoding is **injective**: two 91-byte encodings with equal declared fields are equal
  wires (the fields cover the whole buffer), so the spec field list alone determines the
  encoding; a differing `chainId` necessarily changes the wire.
- The `active` bit sits at offset 90, inside the buffer (the contract reads it there).
- Three negative controls build witnesses when the partition is broken: an uncovered trailing
  byte (a 90-byte layout would leave the final byte undetermined by the field list), an
  overlapping field pair (writing the second destroys the first), and a gap byte (two wires
  with identical fields but a different gap byte are indistinguishable by the field list).

`verify_config_spec.py` has 12 obligations (5 UNSAT static/field facts, 2 UNSAT injectivity,
2 SAT feasibility, 3 SAT negative controls). All pass. `config-spec-result.json` records
solver, source/spec/script hashes and trusted assumptions.

## Boundary and limits

This models the **wire-format correspondence** between the doc.md struct and the serializer —
that the declared field decomposition is the encoding, and nothing else rides on the wire. It
does **not** model the contract's storage semantics, the chainId-0-reserved guard, the enum
range guards (securityLevel 0..4, daMode 0..3), or decimal scaling; those live in the
contract and serializer logic and are covered by tests. Both `L2ChainConfigSerializer.cs` and
`doc.md` digests are recorded; serializer drift fails closed.

## Reproduce

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_config_spec.py
```

On Windows use `.venv-formal/Scripts/python`. Four self-tests cover source drift, line
endings, UNKNOWN, and the model/negative controls.