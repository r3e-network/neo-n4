# Preimage injectivity model — 2026-09-18

Structural injectivity model of the canonical message / withdrawal preimages in
`src/Neo.L2.State/MessageHasher.cs`. A handwritten abstraction, **not** a C#/NeoVM
verifier.

## Established properties

`EncodeMessage` / `EncodeWithdrawal` lay each field into fixed-width little-endian bit
positions and length-prefix the variable payload. Because the fields occupy disjoint
offsets and the payload boundary is length-prefixed, the encoding is **injective**:

- a different `chainId`, `targetChainId`, `nonce`, `sender`, `receiver`, or `messageType`
  necessarily produces different preimage bytes;
- a different payload length or payload bytes necessarily produces different preimage bytes;
- the chainId field is load-bearing as a domain separator: two otherwise-identical messages
  from different L2 chains encode to distinct preimages, so cross-L2 inclusion-proof replay
  is structurally prevented at the preimage level.

Under the trusted assumption that `Hash256` (double-SHA256) is collision-free, distinct
preimages imply distinct leaf hashes. This covers the nonce/chainId replay-binding half of
"nonce/replay" and the structural (preimage) half of "message hashing"; the cryptographic
collision resistance of SHA-256 itself is a separate trusted assumption, not re-derived.

`verify_preimage.py` has 13 obligations (9 UNSAT field-binding, 1 SAT distinct-message
feasibility, 3 SAT negative controls for dropping a length-prefix / nonce / chainId guard).
All pass. `preimage-injectivity-result.json` records solver, source/script/spec hashes and
trusted assumptions.

## Boundary and limits

This proves **structural injectivity of preimages**, not the absence of SHA-256 collisions,
not the runtime NEF behavior, and not Merkle/settlement use of the hashes. The withdrawal
and message field layouts are modeled at the widths the encoder writes; the actual `UInt160`
serialization and `BigInteger.ToByteArray` are trusted from the source. The length prefix is
modeled for a bounded payload slice to keep the check exact; the decoder's own length guards
are covered separately by tests.

## Reproduce

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_preimage.py
```

On Windows use `.venv-formal/Scripts/python`. Five self-tests cover source drift, line
endings, UNKNOWN, counterexample rejection, and the normal model/negative controls.