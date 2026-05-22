# neo-external-bridge-router Source-Level Learning Guide

This guide is generated from the crate's actual `Cargo.toml`, Rust source files, public symbols, and test functions. It is meant to help a reader understand what this crate owns before reading implementation details.

## What This Crate Is

| Topic | Detail |
| --- | --- |
| Layer | Foreign-chain bridge program |
| Purpose | Solana-side bridge router that represents Neo N4 cross-chain lock, mint, burn, and unlock flows. |
| Inputs | Solana instruction, token account state, bridge authority |
| Responsibilities | Validate route, Move escrowed assets, Emit bridge event |
| Outputs | bridge event, escrow mutation, relay evidence |
| Consumers | watcher-sol, gateway, shared bridge |

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
| `src/lib.rs` | crate root, public exports, and top-level documentation | 15 | 21 |

## Public API Surface

| Symbol | File |
| --- | --- |
| `fn initialize` | `src/lib.rs` |
| `fn set_committee` | `src/lib.rs` |
| `fn lock_sol_and_send` | `src/lib.rs` |
| `fn finalize_withdrawal` | `src/lib.rs` |
| `struct Initialize` | `src/lib.rs` |
| `struct SetCommittee` | `src/lib.rs` |
| `struct LockSolAndSend` | `src/lib.rs` |
| `struct FinalizeWithdrawal` | `src/lib.rs` |
| `struct BridgeState` | `src/lib.rs` |
| `fn space` | `src/lib.rs` |
| `struct ConsumedNonce` | `src/lib.rs` |
| `const SPACE` | `src/lib.rs` |
| `struct LockedEvent` | `src/lib.rs` |
| `struct WithdrawalFinalizedEvent` | `src/lib.rs` |
| `enum BridgeError` | `src/lib.rs` |

## Module and Re-Export Signals

No `mod` or `pub use` declarations were scanned.

## Test Evidence

| Test | File |
| --- | --- |
| `sigverify_parser_accepts_same_instruction_offsets` | `src/lib.rs` |
| `sigverify_parser_rejects_cross_instruction_message_offset` | `src/lib.rs` |
| `sigverify_parser_rejects_cross_instruction_pubkey_offset` | `src/lib.rs` |
| `validate_committee_accepts_well_formed` | `src/lib.rs` |
| `validate_committee_rejects_empty` | `src/lib.rs` |
| `validate_committee_rejects_too_large` | `src/lib.rs` |
| `validate_committee_rejects_zero_threshold` | `src/lib.rs` |
| `validate_committee_rejects_threshold_above_size` | `src/lib.rs` |
| `validate_committee_rejects_duplicate_member` | `src/lib.rs` |
| `validate_committee_accepts_unanimity_threshold` | `src/lib.rs` |
| `validate_committee_accepts_max_size` | `src/lib.rs` |
| `read_u32_le_happy_path` | `src/lib.rs` |
| `read_u32_le_returns_zero_on_underflow` | `src/lib.rs` |
| `read_u64_le_happy_path` | `src/lib.rs` |
| `read_u64_le_returns_zero_on_underflow` | `src/lib.rs` |
| `read_uint_le_variable_length` | `src/lib.rs` |
| `read_for_seeds_under_length_returns_zero` | `src/lib.rs` |
| `canonical_message_offsets_are_pinned` | `src/lib.rs` |
| `canonical_message_layout_round_trips` | `src/lib.rs` |
| `direction_constants_disjoint` | `src/lib.rs` |
| `message_type_constants` | `src/lib.rs` |

## Dependency Boundary

| Dependency | Kind |
| --- | --- |
| `anchor-lang` | runtime |
| `solana-instructions-sysvar` | runtime |
| `solana-sdk-ids` | runtime |

## Suggested Reading Path

1. Read `src/lib.rs`: crate root, public exports, and top-level documentation.

## Change Safety Checklist

- Keep the stated responsibility boundary intact: Validate route, Move escrowed assets, Emit bridge event.
- Update the workflow and dataflow diagrams when adding or removing major execution steps.
- Add or update tests in the files listed under Test Evidence when public API or state-transition behavior changes.
- Re-run `python tools/docs/generate_crate_visual_docs.py` from the Neo N4 repository root after source layout changes.
