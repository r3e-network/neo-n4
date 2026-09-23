# Scoped formal verification

## Status

**Whole-system formal verification is NOT complete.** This directory contains
nineteen Z3 models and two CTL models — the `BatchSerializer.Decode` length arithmetic,
an inductive safety model of the atomic `RegisterChain` registration state machine,
an inductive safety model of the forced-inclusion FIFO queue, inductive safety
models of SharedBridge L1 escrow conservation and the L2 bridge token-supply
ledger, an inductive safety model of the GovernanceController authorization gate,
an inductive safety model of the DA write-side failover policy, a
structural-injectivity model of the canonical message/withdrawal preimages,
inductive safety, crash-recovery, crash-atomicity (write-order) and CTL
reachability-liveness models of the Gateway publication outbox, a CTL cross-component
pipeline composition model (forced-inclusion queue × batch lifecycle), a Z3 batch
arithmetic invariants model (monotonicity, non-overlap, revert constraints), a Z3 state
monotonicity model (finalized state roots and Gateway watermark never rewind), an inductive model
of the optimistic-challenge bisection game, spec-correspondence models of the
doc.md §3.2 chain-config and batch-commitment wire formats the §3.2 settlement
method surface, and the §10/§11 bridge method surface, and an inductive safety
model of a registered chain's settlement state machine — not a verifier for C#/NeoVM
programs. Property tests, mutation tests and SP1 execution proofs are different
evidence classes; none is a substitute for a system-wide correctness proof. See
also settlement-model.md, registration-model.md, forced-inclusion-model.md,
bridge-conservation-model.md, l2-bridge-model.md, governance-model.md,
da-failover-model.md, preimage-injectivity-model.md, gateway-outbox-model.md,
gateway-outbox-recovery-model.md, gateway-outbox-crashatomic-model.md,
gateway-outbox-liveness-model.md, bisection-model.md, config-spec-model.md, batch-spec-model.md, settlement-abi-model.md, bridge-abi-model.md,
pipeline-liveness-model.md, batch-arithmetic-model.md, state-monotonicity-model.md
and mutation-results.md.

## Reproduce

From the repository root, with Python 3.13:

```sh
python -m venv .venv-formal
# Windows: .venv-formal/Scripts/python; Unix: .venv-formal/bin/python
.venv-formal/bin/python -m pip install -r scripts/formal/requirements.txt
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_batch_length.py
.venv-formal/bin/python scripts/formal/verify_registration.py
.venv-formal/bin/python scripts/formal/verify_forced_inclusion.py
.venv-formal/bin/python scripts/formal/verify_bridge.py
.venv-formal/bin/python scripts/formal/verify_governance.py
.venv-formal/bin/python scripts/formal/verify_da_failover.py
.venv-formal/bin/python scripts/formal/verify_preimage.py
.venv-formal/bin/python scripts/formal/verify_outbox.py
.venv-formal/bin/python scripts/formal/verify_outbox_recovery.py
.venv-formal/bin/python scripts/formal/verify_outbox_crashatomic.py
.venv-formal/bin/python scripts/formal/verify_bisection.py
.venv-formal/bin/python scripts/formal/verify_config_spec.py
.venv-formal/bin/python scripts/formal/verify_batch_spec.py
.venv-formal/bin/python scripts/formal/verify_settlement_abi.py
.venv-formal/bin/python scripts/formal/verify_bridge_abi.py
.venv-formal/bin/python scripts/formal/verify_pipeline_liveness.py
.venv-formal/bin/python scripts/formal/verify_batch_arithmetic.py
.venv-formal/bin/python scripts/formal/verify_state_monotonicity.py
.venv-formal/bin/python scripts/formal/verify_l2_bridge.py
.venv-formal/bin/python scripts/formal/verify_outbox_liveness.py
.venv-formal/bin/python scripts/formal/verify_settlement.py
```

Each runner writes its JSON/text evidence beside itself, including source/script/spec
SHA-256, solver version, results and exit code. Regenerate reports for each revision;
repository snapshots are not current-run evidence. Missing dependencies, changed source
anchors, exceptions, unexpected SAT or UNKNOWN all produce exit code 1. The self-tests
deliberately invoke failed checks on temporary source copies; their printed `FAILED` lines
are expected and the unittest process must still exit 0.

## What is established

The model represents signed 32-bit `proofLen` and buffer length. Under the
assumptions `0 <= proofLen <= 1048576`, buffer length >= 321, and cursor `pos=321`:

- Adding the header to an allowed proof length does not overflow signed int32.
- The modeled equality guard accepts only exact mathematical `321 + proofLen`.
- The modeled accepted domain excludes negative/oversized proof lengths.
- The accepted domain is nonempty, including zero and maximum proof lengths.
- A weakened equality guard admits a trailing byte (negative control).

Three counterexample queries must be UNSAT. Satisfiability checks prevent an empty
premise from being mistaken for useful evidence. 64-bit sign extension represents
exact addition here: the sum of any signed int32 value and 321 fits in signed int64.

## Trusted assumptions and limits

The guard text and cap are checked against `src/Neo.L2.Batch/BatchSerializer.cs`.
This is **not a C# parser or symbolic execution engine**. Text-anchor presence does
not prove control flow, guard reachability, exception behavior, field order, cursor
value, or rejection in a compiled binary. Those remain trusted correspondence
assumptions supported separately by production tests. In particular, changing a
throw body while preserving guard text is outside the drift checker's coverage.
The invalid-length and exact-length obligations express the guard model itself;
they are not independent proof that production executes those guards.

`UT_BatchSerializer` independently exercises oversized proof rejection, exact-limit
acceptance, oversized declared length rejection and trailing-byte rejection. The
full Batch suite most recently passed 136 cases; this is regression evidence only.
The public-inputs domain is 352 bytes (ForcedInclusionCount at 348); it is not the
321-byte commitment header modeled here.

## Open system-level work

No whole-system completion percentage is assigned. Registration, forced-inclusion
FIFO/queue, bridge L1 escrow and L2 supply conservation, governance authorization,
DA write-side failover, canonical-preimage injectivity, Gateway publication-outbox
(safety, crash-recovery rehydration, crash-atomicity of the write ordering, and CTL
reachability-liveness), the optimistic-challenge bisection game (rollback narrowing),
the doc.md §3.2 chain-config and batch-commitment wire-format correspondence, and
settlement state machines are now covered by scoped models (see registration-model.md, forced-inclusion-model.md,
bridge-conservation-model.md, l2-bridge-model.md, governance-model.md,
da-failover-model.md, preimage-injectivity-model.md, gateway-outbox-model.md,
gateway-outbox-recovery-model.md, gateway-outbox-crashatomic-model.md,
gateway-outbox-liveness-model.md, bisection-model.md, config-spec-model.md, batch-spec-model.md, settlement-abi-model.md, bridge-abi-model.md and
settlement-model.md). Each model's trusted assumptions are recorded in its notes;
none claims more than its stated scope. The outbox recovery/crashatomic models cover
the protocol-layer recovery rehydration and the write-ordering crash-atomicity; they
do not model RocksDB's internal WAL or the atomicity of a single storage write. The
config-spec, batch-spec, settlement-abi and bridge-abi models cover the chain-config,
batch-commitment wire-format, settlement method-surface and bridge method-surface
correspondence slices; the spec-to-implementation correspondence obligation is now
covered at the method-surface level for the doc-declared contract surfaces. The
pipeline-liveness model covers the two-component sequencer pipeline composition
(queue × batch lifecycle); the batch-arithmetic model covers the execution-semantics
arithmetic layer (batch/block monotonicity, non-overlap, revert constraints); the
state-monotonicity model covers the state-layer execution-semantics slice (finalized state
roots chain continuity, Gateway watermark monotonic); full multi-component network
composition and complete execution semantics remain open. Remaining proof obligations include
spec-to-implementation correspondence beyond the doc-declared method surfaces (field-
and state-level semantics); execution semantics; full nonce-replay binding and SHA-256 collision resistance; RocksDB's
internal WAL/durability and the L1-core ChainMode-gated GAS hooks; and
composition/liveness under explicit network assumptions. These need individual
models, reviewed assumptions, counterexamples, implementation linkage and reproducible
CI evidence. The existing SP1 release gates remain separate: they attest execution of a
pinned program, not every intended property of that program and the surrounding system.

After earlier failed/cancelled attempts, two complete Stryker runs now provide
measured evidence: 74.81% before and 91.60% after four targeted regressions.
See [mutation-results.md](mutation-results.md) for full reports, remaining mutants
and exclusions from the score. This does not establish whole-system verification
or validate historical certification claims.
