# neo-zkvm-guest Source-Level Learning Guide

This guide is generated from the crate's actual `Cargo.toml`, Rust source files, public symbols, and test functions. It is meant to help a reader understand what this crate owns before reading implementation details.

## What This Crate Is

| Topic | Detail |
| --- | --- |
| Layer | N4 zk guest |
| Purpose | SP1 guest program that runs deterministic Neo L2 batch execution inside the proof circuit. |
| Inputs | public batch input, private witness, shared execution core |
| Responsibilities | Run verifiable transition, Emit public values, Reject nondeterminism |
| Outputs | SP1 public output, state root, execution digest |
| Consumers | neo-zkvm-host, NativeZkVerifier adapter, audit tooling |

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
| `src/lib.rs` | crate root, public exports, and top-level documentation | 1 | 7 |
| `src/main.rs` | binary or CLI entrypoint | 1 | 0 |

## Public API Surface

| Symbol | File |
| --- | --- |
| `fn execute_batch` | `src/lib.rs` |
| `fn main` | `src/main.rs` |

## Module and Re-Export Signals

| Signal |
| --- |
| `src/lib.rs: pub use neo_execution_core::{     execute_batch_with, hash256, merkle_root, BatchResult, ExecutionError, DEFAULT_PER_TX_GAS_LIMIT, }` |

## Test Evidence

| Test | File |
| --- | --- |
| `parse_then_execute_minimal` | `src/lib.rs` |
| `determinism_same_input_same_output` | `src/lib.rs` |
| `truncated_input_rejected` | `src/lib.rs` |
| `unsupported_version_rejected` | `src/lib.rs` |
| `merkle_root_single_leaf_is_leaf` | `src/lib.rs` |
| `merkle_root_empty_is_zero` | `src/lib.rs` |
| `merkle_root_changes_with_leaf_order` | `src/lib.rs` |

## Dependency Boundary

| Dependency | Kind |
| --- | --- |
| `neo-execution-core` | runtime |
| `neo-vm-guest` | runtime |
| `sp1-zkvm` | runtime |

## Suggested Reading Path

1. Read `src/lib.rs`: crate root, public exports, and top-level documentation.
2. Read `src/main.rs`: binary or CLI entrypoint.

## Change Safety Checklist

- Keep the stated responsibility boundary intact: Run verifiable transition, Emit public values, Reject nondeterminism.
- Update the workflow and dataflow diagrams when adding or removing major execution steps.
- Add or update tests in the files listed under Test Evidence when public API or state-transition behavior changes.
- Re-run `python tools/docs/generate_crate_visual_docs.py` from the Neo N4 repository root after source layout changes.
