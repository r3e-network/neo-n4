# neo-n4-sdk Source-Level Learning Guide

This guide is generated from the crate's actual `Cargo.toml`, Rust source files, public symbols, and test functions. It is meant to help a reader understand what this crate owns before reading implementation details.

## What This Crate Is

| Topic | Detail |
| --- | --- |
| Layer | Developer SDK |
| Purpose | Rust client SDK for building tools and services that talk to Neo N4 APIs. |
| Inputs | developer app, gateway endpoint, wallet/config |
| Responsibilities | Encode API requests, Handle bridge/proof models, Return typed results |
| Outputs | typed client result, transaction request, query response |
| Consumers | apps, operators, integration tests |

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
| `src/lib.rs` | crate root, public exports, and top-level documentation | 40 | 0 |
| `tests/integration.rs` | external behavior or integration test | 0 | 10 |

## Public API Surface

| Symbol | File |
| --- | --- |
| `enum SecurityLevel` | `src/lib.rs` |
| `fn from_u8` | `src/lib.rs` |
| `enum DAMode` | `src/lib.rs` |
| `fn from_u8` | `src/lib.rs` |
| `enum SequencerModel` | `src/lib.rs` |
| `fn from_u8` | `src/lib.rs` |
| `enum ExitModel` | `src/lib.rs` |
| `fn from_u8` | `src/lib.rs` |
| `enum ProofType` | `src/lib.rs` |
| `fn from_u8` | `src/lib.rs` |
| `enum BatchStatus` | `src/lib.rs` |
| `fn from_u8` | `src/lib.rs` |
| `struct L2BatchView` | `src/lib.rs` |
| `fn proof_type` | `src/lib.rs` |
| `struct BatchStatusResponse` | `src/lib.rs` |
| `fn status` | `src/lib.rs` |
| `struct DepositStatusResponse` | `src/lib.rs` |
| `struct SecurityLevelResponse` | `src/lib.rs` |
| `fn level` | `src/lib.rs` |
| `struct SecurityLabelResponse` | `src/lib.rs` |
| `fn security_level` | `src/lib.rs` |
| `fn da_mode` | `src/lib.rs` |
| `fn sequencer` | `src/lib.rs` |
| `fn exit` | `src/lib.rs` |
| `enum L2RpcError` | `src/lib.rs` |
| `type Result` | `src/lib.rs` |
| `struct L2RpcClient` | `src/lib.rs` |
| `fn new` | `src/lib.rs` |
| `fn chain_id` | `src/lib.rs` |
| `fn get_batch` | `src/lib.rs` |
| `fn get_batch_status` | `src/lib.rs` |
| `fn get_latest_state_root` | `src/lib.rs` |
| `fn get_state_root_at` | `src/lib.rs` |
| `fn get_withdrawal_proof` | `src/lib.rs` |
| `fn get_message_proof` | `src/lib.rs` |
| `fn get_deposit_status` | `src/lib.rs` |
| `fn get_canonical_asset` | `src/lib.rs` |
| `fn get_bridged_asset` | `src/lib.rs` |
| `fn get_security_level` | `src/lib.rs` |
| `fn get_security_label` | `src/lib.rs` |

## Module and Re-Export Signals

No `mod` or `pub use` declarations were scanned.

## Test Evidence

| Test | File |
| --- | --- |
| `ctor_rejects_zero_chain_id` | `tests/integration.rs` |
| `ctor_rejects_non_http_scheme` | `tests/integration.rs` |
| `ctor_rejects_invalid_url` | `tests/integration.rs` |
| `get_latest_state_root_returns_string` | `tests/integration.rs` |
| `get_security_label_decodes_all_dimensions` | `tests/integration.rs` |
| `get_withdrawal_proof_decodes_hex` | `tests/integration.rs` |
| `get_withdrawal_proof_null_returns_none` | `tests/integration.rs` |
| `server_error_surfaces_with_code` | `tests/integration.rs` |
| `http_502_surfaces_as_transport_error` | `tests/integration.rs` |
| `mismatched_chain_id_surfaces_as_mismatch_error` | `tests/integration.rs` |

## Dependency Boundary

| Dependency | Kind |
| --- | --- |
| `reqwest` | runtime |
| `serde` | runtime |
| `serde_json` | runtime |
| `thiserror` | runtime |
| `tokio` | runtime |
| `mockito` | test |
| `tokio` | test |

## Suggested Reading Path

1. Read `src/lib.rs`: crate root, public exports, and top-level documentation.
2. Read `tests/integration.rs`: external behavior or integration test.

## Change Safety Checklist

- Keep the stated responsibility boundary intact: Encode API requests, Handle bridge/proof models, Return typed results.
- Update the workflow and dataflow diagrams when adding or removing major execution steps.
- Add or update tests in the files listed under Test Evidence when public API or state-transition behavior changes.
- Re-run `python tools/docs/generate_crate_visual_docs.py` from the Neo N4 repository root after source layout changes.
