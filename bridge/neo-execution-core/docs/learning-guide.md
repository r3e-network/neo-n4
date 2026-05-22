# neo-execution-core Source-Level Learning Guide

This guide is generated from the crate's actual `Cargo.toml`, Rust source files, public symbols, and test functions. It is meant to help a reader understand what this crate owns before reading implementation details.

## What This Crate Is

| Topic | Detail |
| --- | --- |
| Layer | N4 batch execution core |
| Purpose | Backend-neutral L2 batch transition primitives shared by fast execution and proof generation. |
| Inputs | L2 batch, previous state root, execution parameters |
| Responsibilities | Validate batch shape, Apply deterministic transition, Commit new state root |
| Outputs | execution trace, new state root, public proof inputs |
| Consumers | neo-zkvm-guest, neo-zkvm-host, gateway services |

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

## Source File Map

| File | Role | Public symbols | Tests |
| --- | --- | ---: | ---: |
| `src/lib.rs` | crate root, public exports, and top-level documentation | 0 | 0 |
| `src/types.rs` | implementation detail or helper module | 7 | 0 |
| `src/hashing.rs` | implementation detail or helper module | 6 | 0 |
| `tests/batch_core.rs` | external behavior or integration test | 0 | 5 |
| `src/batch.rs` | implementation detail or helper module | 1 | 0 |
| `src/wire.rs` | implementation detail or helper module | 1 | 0 |

## Public API Surface

| Symbol | File |
| --- | --- |
| `fn execute_batch_with` | `src/batch.rs` |
| `fn merkle_root` | `src/hashing.rs` |
| `fn hash256` | `src/hashing.rs` |
| `fn hash_receipt` | `src/hashing.rs` |
| `fn fold_state_root` | `src/hashing.rs` |
| `fn apply_l1_message` | `src/hashing.rs` |
| `fn hash_public_inputs` | `src/hashing.rs` |
| `const BATCH_WIRE_VERSION` | `src/types.rs` |
| `const DEFAULT_PER_TX_GAS_LIMIT` | `src/types.rs` |
| `struct BatchResult` | `src/types.rs` |
| `struct VmExecutionReceipt` | `src/types.rs` |
| `enum ExecutionError` | `src/types.rs` |
| `struct BatchRequest` | `src/types.rs` |
| `struct L1Message` | `src/types.rs` |
| `fn parse_batch_request` | `src/wire.rs` |

## Module and Re-Export Signals

| Signal |
| --- |
| `src/lib.rs: mod batch` |
| `src/lib.rs: mod hashing` |
| `src/lib.rs: mod types` |
| `src/lib.rs: mod wire` |
| `src/lib.rs: pub use batch::execute_batch_with` |
| `src/lib.rs: pub use hashing::{     apply_l1_message, fold_state_root, hash256, hash_public_inputs, hash_receipt, merkle_root, }` |
| `src/lib.rs: pub use types::{     BatchRequest, BatchResult, ExecutionError, L1Message, VmExecutionReceipt, BATCH_WIRE_VERSION,     DEFAULT_PER_TX_GAS_LIMIT, }` |
| `src/lib.rs: pub use wire::parse_batch_request` |

## Test Evidence

| Test | File |
| --- | --- |
| `executes_batch_through_backend_agnostic_receipts` | `tests/batch_core.rs` |
| `deterministic_for_same_input_and_executor` | `tests/batch_core.rs` |
| `rejects_bad_wire_inputs` | `tests/batch_core.rs` |
| `merkle_root_is_stable_and_ordered` | `tests/batch_core.rs` |
| `manifest_stays_backend_agnostic` | `tests/batch_core.rs` |

## Dependency Boundary

| Dependency | Kind |
| --- | --- |
| `sha2` | runtime |

## Suggested Reading Path

1. Read `src/lib.rs`: crate root, public exports, and top-level documentation.
2. Read `src/types.rs`: implementation detail or helper module.
3. Read `src/hashing.rs`: implementation detail or helper module.
4. Read `tests/batch_core.rs`: external behavior or integration test.
5. Read `src/batch.rs`: implementation detail or helper module.
6. Read `src/wire.rs`: implementation detail or helper module.

## Change Safety Checklist

- Keep the stated responsibility boundary intact: Validate batch shape, Apply deterministic transition, Commit new state root.
- Update the workflow and dataflow diagrams when adding or removing major execution steps.
- Add or update tests in the files listed under Test Evidence when public API or state-transition behavior changes.
- Re-run `python tools/docs/generate_crate_visual_docs.py` from the Neo N4 repository root after source layout changes.
