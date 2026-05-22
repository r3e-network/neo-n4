# neo-zkvm-host Source-Level Learning Guide

This guide is generated from the crate's actual `Cargo.toml`, Rust source files, public symbols, and test functions. It is meant to help a reader understand what this crate owns before reading implementation details.

## What This Crate Is

| Topic | Detail |
| --- | --- |
| Layer | N4 zk host |
| Purpose | Host-side SP1 prover orchestration for creating and checking L2 batch proofs. |
| Inputs | L2 batch, guest ELF, prover configuration |
| Responsibilities | Prepare SP1 stdin, Run prover, Verify proof envelope |
| Outputs | proof bytes, verification report, state commitment |
| Consumers | bridge relayer, L1 verifier adapter, devnet scripts |

## Visual Reading Order

| Step | Diagram | Use it to learn |
| ---: | --- | --- |
| 1 | [Position](figures/position.svg) | Why this crate exists and where it sits in Neo N4. |
| 2 | [Principles](figures/principles.svg) | The invariants and boundaries this crate must protect. |
| 3 | [Module map](figures/module-map.svg) | Which files are the best entry points. |
| 4 | [Public API surface](figures/api-surface.svg) | Which exported symbols form the crate contract. |
| 5 | [Architecture](figures/architecture.svg) | How inputs, internal components, dependencies, and outputs connect. |
| 6 | [Workflow](figures/workflow.svg) | The normal execution path. |
| 7 | [Dataflow](figures/dataflow.svg) | How data is transformed across the crate boundary. |
| 8 | [Test evidence](figures/test-map.svg) | Which tests protect the behavior. |
| 9 | [Dependency map](figures/dependency-map.svg) | Which dependencies are runtime, test, or build-only. |
| 10 | [Implementation atlas](figures/implementation-atlas.svg) | A dense one-page map of purpose, source entrypoints, API, workflow, dataflow, dependencies, tests, and change checks. |

## Source File Map

| File | Role | Public symbols | Tests |
| --- | --- | ---: | ---: |
| `src/lib.rs` | crate root, public exports, and top-level documentation | 10 | 0 |
| `tests/end_to_end.rs` | external behavior or integration test | 0 | 3 |
| `build.rs` | implementation detail or helper module | 0 | 0 |
| `src/bin/prove_batch.rs` | additional binary entrypoint | 0 | 0 |

## Public API Surface

| Symbol | File |
| --- | --- |
| `const NEO_ZKVM_GUEST_ELF` | `src/lib.rs` |
| `const NEO_ZKVM_GUEST_ELF` | `src/lib.rs` |
| `struct ExecutionResult` | `src/lib.rs` |
| `struct ProofResult` | `src/lib.rs` |
| `fn execute` | `src/lib.rs` |
| `fn prove` | `src/lib.rs` |
| `fn verify` | `src/lib.rs` |
| `fn execute` | `src/lib.rs` |
| `fn prove` | `src/lib.rs` |
| `fn verify` | `src/lib.rs` |

## Module and Re-Export Signals

No `mod` or `pub use` declarations were scanned.

## Test Evidence

| Test | File |
| --- | --- |
| `execute_guest_in_zkvm_matches_host_run` | `tests/end_to_end.rs` |
| `prove_and_verify_real_zk_proof` | `tests/end_to_end.rs` |
| `verify_rejects_mismatched_public_input_hash` | `tests/end_to_end.rs` |

## Dependency Boundary

| Dependency | Kind |
| --- | --- |
| `neo-zkvm-guest` | test |
| `serial_test` | test |

## Suggested Reading Path

1. Read `src/lib.rs`: crate root, public exports, and top-level documentation.
2. Read `tests/end_to_end.rs`: external behavior or integration test.
3. Read `build.rs`: implementation detail or helper module.
4. Read `src/bin/prove_batch.rs`: additional binary entrypoint.

## Change Safety Checklist

- Keep the stated responsibility boundary intact: Prepare SP1 stdin, Run prover, Verify proof envelope.
- Update the workflow and dataflow diagrams when adding or removing major execution steps.
- Add or update tests in the files listed under Test Evidence when public API or state-transition behavior changes.
- Re-run `python tools/docs/generate_crate_visual_docs.py` from the Neo N4 repository root after source layout changes.
