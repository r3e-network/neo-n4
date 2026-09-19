# Batch commitment spec-correspondence model — 2026-09-18

Spec-to-implementation correspondence model for doc.md §3.2's `L2BatchCommitment` struct
versus the implemented wire layout (BatchSerializer.cs + RollupHubContract.cs). A
handwritten abstraction, **not** a C#/NeoVM verifier.

## Established properties

Walking doc.md §3.2's declared fields in order (chainId, batchNumber, firstBlock, lastBlock,
preStateRoot, postStateRoot, txRoot, receiptRoot, withdrawalRoot, l2ToL1MessageRoot,
l2ToL2MessageRoot, daCommitment, publicInputHash, proofType) with the standard C# wire widths
(uint32=4, ulong=8, UInt256=32, byte=1) derives **exactly** the implemented offsets
chainId@0 .. proofType@316 — the spec field list and the implementation are in lockstep.

- The proof region follows immediately: a 4-byte length prefix at [317,321) and the proof
  bytes at [321,321+proofLen); the fixed header is exactly 321 bytes (HeaderMinLength).
- Header fields are contiguous with no gaps and strictly increasing offsets.
- The **public-inputs domain is exactly 352 bytes**: header[0..252) + l1MessageHash(32) +
  daCommitment(32) + blockContextHash(32) + forcedInclusionCount(4), with the prefix ending
  where l2ToL2MessageRoot ends.
- **Injectivity**: the 15-field partition of [0,321) (14 fixed fields + the proof-length
  prefix, the encoding of doc.md's variable `byte[] proof`) means equal declared fields imply
  an identical header — the spec field list determines the whole header encoding; a differing
  chainId necessarily changes it.
- Three negative controls build witnesses when the partition is broken: dropping
  publicInputHash leaves [284,316) uncovered (settlement-digest ambiguity), omitting
  forcedInclusionCount leaves fic@348..352 free (forced-count ambiguity), and omitting the
  proofLen prefix leaves the proof payload length under-determined.

`verify_batch_spec.py` has 13 obligations (7 UNSAT static/layout facts, 2 UNSAT injectivity,
2 SAT feasibility, 3 SAT negative controls). All pass. `batch-spec-result.json` records
solver, spec/script hashes and trusted assumptions.

## Boundary and limits

This models the **wire-format correspondence** between the doc.md struct and the implemented
layout — that the declared field decomposition is the encoding. It does **not** model
execution semantics, the proof-verification logic, DA-mode gating, or the L1 settlement
state machine (separate obligations; the settlement model and VmTests cover behavior). The
Hash256 collision resistance is a separate trusted assumption. Fail-closed is via source
anchors (the serializer's 321/352 layout docs and the contract's OffsetProofType=316 /
HeaderMinLength=321 declarations), not digest pinning, so comment-independent layout drift
is caught while doc-formatting changes are not.

## Reproduce

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_batch_spec.py
```

On Windows use `.venv-formal/Scripts/python`. Four self-tests cover anchor drift, UNKNOWN,
counterexample rejection, and the model/negative controls.