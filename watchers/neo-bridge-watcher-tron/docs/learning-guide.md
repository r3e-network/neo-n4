# neo-bridge-watcher-tron Source-Level Learning Guide

This guide is generated from the crate's actual `Cargo.toml`, Rust source files, public symbols, and test functions. It is meant to help a reader understand what this crate owns before reading implementation details.

## What This Crate Is

| Topic | Detail |
| --- | --- |
| Layer | Cross-chain watcher |
| Purpose | Observes TRON bridge events and turns them into normalized Neo N4 relay messages. |
| Inputs | TRON RPC/log stream, bridge contract events, checkpoint cursor |
| Responsibilities | Filter bridge events, Normalize payloads, Protect replay/cursor state |
| Outputs | relay job, audit log, health metric |
| Consumers | gateway, shared bridge, operator dashboard |

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
| `src/lib.rs` | crate root, public exports, and top-level documentation | 3 | 2 |
| `tests/parity.rs` | external behavior or integration test | 0 | 4 |

## Public API Surface

| Symbol | File |
| --- | --- |
| `const TRON_MAINNET_CHAIN_ID` | `src/lib.rs` |
| `const TRON_NILE_TESTNET_CHAIN_ID` | `src/lib.rs` |
| `const TRON_SHASTA_TESTNET_CHAIN_ID` | `src/lib.rs` |

## Module and Re-Export Signals

| Signal |
| --- |
| `src/lib.rs: pub use neo_bridge_watcher_eth::chains` |
| `src/lib.rs: pub use neo_bridge_watcher_eth::*` |

## Test Evidence

| Test | File |
| --- | --- |
| `tron_chain_ids_have_foreign_namespace_prefix` | `src/lib.rs` |
| `tron_chain_ids_disjoint_from_eth_and_solana` | `src/lib.rs` |
| `canonical_bytes_emit_tron_chain_id_at_offset_zero` | `tests/parity.rs` |
| `canonical_bytes_diverge_from_eth_only_at_chain_id_position` | `tests/parity.rs` |
| `message_hash_differs_from_eth_for_same_other_fields` | `tests/parity.rs` |
| `fixed_prefix_still_102_bytes` | `tests/parity.rs` |

## Dependency Boundary

| Dependency | Kind |
| --- | --- |
| `hex` | runtime |
| `neo-bridge-watcher-eth` | runtime |
| `sha2` | test |

## Suggested Reading Path

1. Read `src/lib.rs`: crate root, public exports, and top-level documentation.
2. Read `tests/parity.rs`: external behavior or integration test.

## Change Safety Checklist

- Keep the stated responsibility boundary intact: Filter bridge events, Normalize payloads, Protect replay/cursor state.
- Update the workflow and dataflow diagrams when adding or removing major execution steps.
- Add or update tests in the files listed under Test Evidence when public API or state-transition behavior changes.
- Re-run `python tools/docs/generate_crate_visual_docs.py` from the Neo N4 repository root after source layout changes.
