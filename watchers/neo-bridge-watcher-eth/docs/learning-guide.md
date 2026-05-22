# neo-bridge-watcher-eth Source-Level Learning Guide

This guide is generated from the crate's actual `Cargo.toml`, Rust source files, public symbols, and test functions. It is meant to help a reader understand what this crate owns before reading implementation details.

## What This Crate Is

| Topic | Detail |
| --- | --- |
| Layer | Cross-chain watcher |
| Purpose | Observes ETH bridge events and turns them into normalized Neo N4 relay messages. |
| Inputs | ETH RPC/log stream, bridge contract events, checkpoint cursor |
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
| `src/lib.rs` | crate root, public exports, and top-level documentation | 0 | 0 |
| `src/chains.rs` | implementation detail or helper module | 41 | 8 |
| `src/live/eth_rpc.rs` | implementation detail or helper module | 10 | 15 |
| `src/live/health.rs` | implementation detail or helper module | 11 | 11 |
| `src/proof.rs` | proof object, layout, and verification evidence | 10 | 5 |
| `src/live/neo_rpc.rs` | implementation detail or helper module | 8 | 8 |
| `src/messaging.rs` | implementation detail or helper module | 7 | 5 |
| `src/core.rs` | implementation detail or helper module | 6 | 7 |
| `src/signer.rs` | implementation detail or helper module | 7 | 3 |
| `src/event_source.rs` | implementation detail or helper module | 7 | 0 |
| `tests/preflight_smoke.rs` | external behavior or integration test | 3 | 10 |
| `src/submitter.rs` | implementation detail or helper module | 6 | 0 |
| `src/live/file_journal.rs` | implementation detail or helper module | 2 | 10 |
| `src/journal.rs` | implementation detail or helper module | 4 | 0 |
| `tests/daemon_run_loop_smoke.rs` | external behavior or integration test | 3 | 1 |
| `src/live/test_support.rs` | implementation detail or helper module | 2 | 0 |
| `tests/parity.rs` | external behavior or integration test | 0 | 4 |
| `src/bin/neo-bridge-watcher-eth.rs` | additional binary entrypoint | 0 | 0 |
| `src/live/mod.rs` | implementation detail or helper module | 0 | 0 |

## Public API Surface

| Symbol | File |
| --- | --- |
| `const ETH_MAINNET` | `src/chains.rs` |
| `const ETH_SEPOLIA` | `src/chains.rs` |
| `const ETH_HOLESKY` | `src/chains.rs` |
| `const TRON_MAINNET` | `src/chains.rs` |
| `const TRON_NILE_TESTNET` | `src/chains.rs` |
| `const TRON_SHASTA_TESTNET` | `src/chains.rs` |
| `const SOLANA_MAINNET` | `src/chains.rs` |
| `const SOLANA_DEVNET` | `src/chains.rs` |
| `const SOLANA_TESTNET` | `src/chains.rs` |
| `const BSC_MAINNET` | `src/chains.rs` |
| `const BSC_TESTNET` | `src/chains.rs` |
| `const POLYGON_MAINNET` | `src/chains.rs` |
| `const POLYGON_AMOY_TESTNET` | `src/chains.rs` |
| `const POLYGON_ZKEVM` | `src/chains.rs` |
| `const POLYGON_ZKEVM_CARDONA` | `src/chains.rs` |
| `const ARBITRUM_ONE` | `src/chains.rs` |
| `const ARBITRUM_SEPOLIA` | `src/chains.rs` |
| `const ARBITRUM_NOVA` | `src/chains.rs` |
| `const OPTIMISM_MAINNET` | `src/chains.rs` |
| `const OPTIMISM_SEPOLIA` | `src/chains.rs` |
| `const BASE_MAINNET` | `src/chains.rs` |
| `const BASE_SEPOLIA` | `src/chains.rs` |
| `const AVALANCHE_C_MAINNET` | `src/chains.rs` |
| `const AVALANCHE_FUJI` | `src/chains.rs` |
| `const LINEA_MAINNET` | `src/chains.rs` |
| `const LINEA_SEPOLIA` | `src/chains.rs` |
| `const ZKSYNC_ERA_MAINNET` | `src/chains.rs` |
| `const ZKSYNC_SEPOLIA` | `src/chains.rs` |
| `const SCROLL_MAINNET` | `src/chains.rs` |
| `const SCROLL_SEPOLIA` | `src/chains.rs` |
| `const MANTLE_MAINNET` | `src/chains.rs` |
| `const MANTLE_SEPOLIA` | `src/chains.rs` |
| `const FANTOM_OPERA` | `src/chains.rs` |
| `const SONIC_MAINNET` | `src/chains.rs` |
| `const CELO_MAINNET` | `src/chains.rs` |
| `const CELO_ALFAJORES` | `src/chains.rs` |
| `const fn` | `src/chains.rs` |
| `fn name_for_chain_id` | `src/chains.rs` |
| `const EVM_FAMILY_CHAINS` | `src/chains.rs` |
| `fn is_evm_family` | `src/chains.rs` |
| `fn recommended_confirmations` | `src/chains.rs` |
| `enum CoreError` | `src/core.rs` |
| `struct WatcherCore` | `src/core.rs` |
| `fn new` | `src/core.rs` |
| `fn tick` | `src/core.rs` |
| `fn drain` | `src/core.rs` |
| `fn process_event` | `src/core.rs` |
| `struct LockedEvent` | `src/event_source.rs` |
| `enum EventSourceError` | `src/event_source.rs` |
| `trait EventSource` | `src/event_source.rs` |
| `struct MockEventSource` | `src/event_source.rs` |
| `fn new` | `src/event_source.rs` |
| `fn push` | `src/event_source.rs` |
| `fn pending` | `src/event_source.rs` |
| `enum JournalError` | `src/journal.rs` |
| `trait Journal` | `src/journal.rs` |
| `struct InMemoryJournal` | `src/journal.rs` |
| `fn new` | `src/journal.rs` |
| `enum EthRpcError` | `src/live/eth_rpc.rs` |
| `struct EthRpcEventSourceBuilder` | `src/live/eth_rpc.rs` |
| `fn new` | `src/live/eth_rpc.rs` |
| `fn chunk_size` | `src/live/eth_rpc.rs` |
| `fn request_timeout` | `src/live/eth_rpc.rs` |
| `fn min_confirmations` | `src/live/eth_rpc.rs` |
| `fn build` | `src/live/eth_rpc.rs` |
| `struct EthRpcEventSource` | `src/live/eth_rpc.rs` |
| `fn builder` | `src/live/eth_rpc.rs` |
| `fn fetch_block_number` | `src/live/eth_rpc.rs` |
| `struct FileJournal` | `src/live/file_journal.rs` |
| `fn open` | `src/live/file_journal.rs` |
| `struct HealthState` | `src/live/health.rs` |
| `fn new` | `src/live/health.rs` |
| `fn with_chain_id` | `src/live/health.rs` |
| `fn record_tick` | `src/live/health.rs` |
| `fn record_submission` | `src/live/health.rs` |
| `fn record_cursor` | `src/live/health.rs` |
| `fn record_error` | `src/live/health.rs` |
| `fn snapshot` | `src/live/health.rs` |
| `fn metrics_text` | `src/live/health.rs` |
| `struct HealthServer` | `src/live/health.rs` |
| `fn spawn` | `src/live/health.rs` |
| `enum NeoRpcError` | `src/live/neo_rpc.rs` |
| `trait SignAndSend` | `src/live/neo_rpc.rs` |
| `struct NeoRpcSubmitterBuilder` | `src/live/neo_rpc.rs` |
| `fn new` | `src/live/neo_rpc.rs` |
| `fn request_timeout` | `src/live/neo_rpc.rs` |
| `fn build` | `src/live/neo_rpc.rs` |
| `struct NeoRpcSubmitter` | `src/live/neo_rpc.rs` |
| `fn builder` | `src/live/neo_rpc.rs` |
| `struct FakeRpcServer` | `src/live/test_support.rs` |
| `fn spawn` | `src/live/test_support.rs` |
| `enum ExternalBridgeDirection` | `src/messaging.rs` |
| `enum ExternalMessageType` | `src/messaging.rs` |
| `struct ExternalCrossChainMessage` | `src/messaging.rs` |
| `enum BuildError` | `src/messaging.rs` |
| `fn canonical_message_bytes` | `src/messaging.rs` |
| `fn message_hash` | `src/messaging.rs` |
| `fn encode_asset_transfer_payload` | `src/messaging.rs` |
| `enum Curve` | `src/proof.rs` |
| `fn pubkey_len` | `src/proof.rs` |
| `enum ProofBuildError` | `src/proof.rs` |
| `const MAX_SIGNERS` | `src/proof.rs` |
| `struct PubkeySignature` | `src/proof.rs` |
| `struct NeoProofBytes` | `src/proof.rs` |
| `fn encode` | `src/proof.rs` |
| `struct IndexedRsv` | `src/proof.rs` |
| `struct EthProofBytes` | `src/proof.rs` |
| `fn encode` | `src/proof.rs` |
| `enum SignerError` | `src/signer.rs` |
| `struct SignerOutput` | `src/signer.rs` |
| `trait Signer` | `src/signer.rs` |
| `struct FileSigner` | `src/signer.rs` |
| `fn from_file` | `src/signer.rs` |
| `fn from_bytes` | `src/signer.rs` |
| `fn verifying_key` | `src/signer.rs` |
| `enum SubmitterError` | `src/submitter.rs` |
| `struct InboundSubmission` | `src/submitter.rs` |
| `trait NeoSubmitter` | `src/submitter.rs` |
| `struct MockSubmitter` | `src/submitter.rs` |
| `fn new` | `src/submitter.rs` |
| `fn submissions` | `src/submitter.rs` |
| `struct TempDir` | `tests/daemon_run_loop_smoke.rs` |
| `fn new` | `tests/daemon_run_loop_smoke.rs` |
| `fn path` | `tests/daemon_run_loop_smoke.rs` |
| `struct TempDir` | `tests/preflight_smoke.rs` |
| `fn new` | `tests/preflight_smoke.rs` |
| `fn path` | `tests/preflight_smoke.rs` |

## Module and Re-Export Signals

| Signal |
| --- |
| `src/lib.rs: mod chains` |
| `src/lib.rs: mod core` |
| `src/lib.rs: mod event_source` |
| `src/lib.rs: mod journal` |
| `src/lib.rs: mod messaging` |
| `src/lib.rs: mod proof` |
| `src/lib.rs: mod signer` |
| `src/lib.rs: mod submitter` |
| `src/lib.rs: mod live` |
| `src/lib.rs: pub use core::{CoreError, WatcherCore}` |
| `src/lib.rs: pub use event_source::{EventSource, EventSourceError, LockedEvent, MockEventSource}` |
| `src/lib.rs: pub use journal::{InMemoryJournal, Journal, JournalError}` |
| `src/lib.rs: pub use messaging::{     canonical_message_bytes, message_hash, BuildError, ExternalBridgeDirection,     ExternalCrossChainMessage, ExternalMessageType, }` |
| `src/lib.rs: pub use proof::{Curve, EthProofBytes, NeoProofBytes, ProofBuildError}` |
| `src/lib.rs: pub use signer::{FileSigner, Signer, SignerError, SignerOutput}` |
| `src/live/mod.rs: mod eth_rpc` |
| `src/live/mod.rs: mod file_journal` |
| `src/live/mod.rs: mod health` |
| `src/live/mod.rs: mod neo_rpc` |
| `src/live/mod.rs: pub use eth_rpc::{EthRpcError, EthRpcEventSource, EthRpcEventSourceBuilder}` |
| `src/live/mod.rs: pub use file_journal::FileJournal` |
| `src/live/mod.rs: pub use health::{HealthServer, HealthState}` |
| `src/live/mod.rs: pub use neo_rpc::{NeoRpcError, NeoRpcSubmitter, NeoRpcSubmitterBuilder, SignAndSend}` |

## Test Evidence

| Test | File |
| --- | --- |
| `all_chain_ids_carry_foreign_namespace_prefix` | `src/chains.rs` |
| `chain_ids_are_distinct` | `src/chains.rs` |
| `every_constant_has_a_human_name` | `src/chains.rs` |
| `every_constant_has_a_recommended_confirmation` | `src/chains.rs` |
| `anchor_values_for_well_known_chains` | `src/chains.rs` |
| `unknown_chain_id_returns_none` | `src/chains.rs` |
| `evm_family_classification` | `src/chains.rs` |
| `family_banks_align_to_16_slots` | `src/chains.rs` |
| `amount_be_to_le_minimal_examples` | `src/core.rs` |
| `process_event_full_pipeline` | `src/core.rs` |
| `process_event_rejects_chain_id_mismatch` | `src/core.rs` |
| `process_event_rejects_replay` | `src/core.rs` |
| `process_event_does_not_advance_cursor_on_submit_failure` | `src/core.rs` |
| `drain_processes_all_events_in_order` | `src/core.rs` |
| `drain_resumes_after_partial_progress` | `src/core.rs` |
| `locked_event_topic_hash_is_stable` | `src/live/eth_rpc.rs` |
| `decode_topic_helpers` | `src/live/eth_rpc.rs` |
| `decode_locked_event_round_trip` | `src/live/eth_rpc.rs` |
| `decode_locked_event_rejects_wrong_topic_count` | `src/live/eth_rpc.rs` |
| `decode_locked_event_rejects_wrong_topic0` | `src/live/eth_rpc.rs` |
| `decode_hex_helpers_handle_prefix` | `src/live/eth_rpc.rs` |
| `live_decodes_locked_event_via_http` | `src/live/eth_rpc.rs` |
| `live_advances_cursor_through_empty_window` | `src/live/eth_rpc.rs` |
| `live_skips_get_logs_when_cursor_above_head` | `src/live/eth_rpc.rs` |
| `live_propagates_rpc_error_response` | `src/live/eth_rpc.rs` |
| `live_min_confirmations_caps_polling_window_below_head` | `src/live/eth_rpc.rs` |
| `live_min_confirmations_saturates_when_head_below_threshold` | `src/live/eth_rpc.rs` |
| `live_min_confirmations_emits_events_below_buffer` | `src/live/eth_rpc.rs` |
| `live_default_min_confirmations_is_zero` | `src/live/eth_rpc.rs` |
| `live_propagates_transport_failure` | `src/live/eth_rpc.rs` |
| `empty_journal_returns_zero_cursor` | `src/live/file_journal.rs` |
| `cursor_round_trip_across_reopen` | `src/live/file_journal.rs` |
| `cursor_does_not_regress` | `src/live/file_journal.rs` |
| `mark_submitted_persists_across_reopen` | `src/live/file_journal.rs` |
| `truncated_consumed_log_is_replayed_safely` | `src/live/file_journal.rs` |
| `large_consumed_log_replays_correctly` | `src/live/file_journal.rs` |
| `corrupt_short_cursor_returns_error` | `src/live/file_journal.rs` |
| `set_cursor_does_not_leak_tmp_file` | `src/live/file_journal.rs` |
| `second_open_on_same_dir_fails_with_lock_error` | `src/live/file_journal.rs` |
| `interleaved_cursor_and_marks_persist_independently` | `src/live/file_journal.rs` |
| `snapshot_carries_recorder_writes` | `src/live/health.rs` |
| `snapshot_pre_first_tick_uses_start_time` | `src/live/health.rs` |
| `record_error_appears_in_snapshot_truncated` | `src/live/health.rs` |
| `http_server_serves_healthz_and_info` | `src/live/health.rs` |
| `http_server_returns_503_when_stale` | `src/live/health.rs` |
| `http_server_404s_unknown_paths` | `src/live/health.rs` |
| `http_server_serves_prometheus_metrics` | `src/live/health.rs` |
| `metrics_carry_chain_id_label_when_set` | `src/live/health.rs` |
| `metrics_unlabelled_when_chain_id_not_set` | `src/live/health.rs` |
| `metrics_reports_healthy_zero_when_stale` | `src/live/health.rs` |
| `server_drop_releases_port` | `src/live/health.rs` |
| `invokefunction_request_shape` | `src/live/neo_rpc.rs` |
| `fault_translation` | `src/live/neo_rpc.rs` |
| `live_submit_halt_invokes_callback_and_returns_tx_hash` | `src/live/neo_rpc.rs` |
| `live_submit_fault_skips_callback_and_returns_verifier_rejected` | `src/live/neo_rpc.rs` |
| `live_submit_already_consumed_fault_translates` | `src/live/neo_rpc.rs` |
| `live_submit_propagates_rpc_error_response` | `src/live/neo_rpc.rs` |
| `live_submit_propagates_callback_failure` | `src/live/neo_rpc.rs` |
| `live_submit_propagates_transport_failure` | `src/live/neo_rpc.rs` |
| `canonical_bytes_match_csharp_vector` | `src/messaging.rs` |
| `message_hash_matches_csharp_vector` | `src/messaging.rs` |
| `rejects_non_namespaced_external_chain_id` | `src/messaging.rs` |
| `hash_changes_for_every_field` | `src/messaging.rs` |
| `fixed_prefix_is_102_bytes` | `src/messaging.rs` |
| `neo_proof_secp256k1_layout` | `src/proof.rs` |
| `neo_proof_ed25519_layout` | `src/proof.rs` |
| `neo_proof_rejects_wrong_pubkey_length` | `src/proof.rs` |
| `eth_proof_layout` | `src/proof.rs` |
| `eth_proof_rejects_bad_v` | `src/proof.rs` |
| `file_signer_signs_and_recovers` | `src/signer.rs` |
| `file_signer_rejects_wrong_key_length` | `src/signer.rs` |
| `pubkey_bytes_are_33_for_secp256k1` | `src/signer.rs` |
| `daemon_run_loop_starts_polls_and_shuts_down_on_sigterm` | `tests/daemon_run_loop_smoke.rs` |
| `canonical_bytes_pinned_against_csharp` | `tests/parity.rs` |
| `message_hash_pinned_against_csharp` | `tests/parity.rs` |
| `neo_proof_bytes_layout_matches_csharp` | `tests/parity.rs` |
| `eth_proof_bytes_layout_matches_solidity` | `tests/parity.rs` |
| `preflight_passes_with_responsive_rpc_endpoints` | `tests/preflight_smoke.rs` |
| `preflight_fails_when_router_address_has_no_bytecode` | `tests/preflight_smoke.rs` |
| `preflight_fails_when_eth_rpc_unreachable` | `tests/preflight_smoke.rs` |
| `preflight_fails_when_eth_router_is_zero_address` | `tests/preflight_smoke.rs` |
| `preflight_fails_when_neo_rpc_returns_jsonrpc_error` | `tests/preflight_smoke.rs` |
| `version_flag_prints_pkg_name_and_version` | `tests/preflight_smoke.rs` |
| `version_short_form_is_equivalent` | `tests/preflight_smoke.rs` |
| `config_template_emits_parseable_toml` | `tests/preflight_smoke.rs` |
| `journal_info_reads_hand_crafted_journal` | `tests/preflight_smoke.rs` |
| `unknown_flag_surfaces_error_with_valid_list` | `tests/preflight_smoke.rs` |

## Dependency Boundary

| Dependency | Kind |
| --- | --- |
| `hex` | runtime |
| `k256` | runtime |
| `libc` | runtime |
| `reqwest` | runtime |
| `serde` | runtime |
| `serde_json` | runtime |
| `sha2` | runtime |
| `thiserror` | runtime |
| `tiny-keccak` | runtime |
| `toml` | runtime |
| `zeroize` | runtime |
| `rand` | test |

## Suggested Reading Path

1. Read `src/lib.rs`: crate root, public exports, and top-level documentation.
2. Read `src/chains.rs`: implementation detail or helper module.
3. Read `src/live/eth_rpc.rs`: implementation detail or helper module.
4. Read `src/live/health.rs`: implementation detail or helper module.
5. Read `src/proof.rs`: proof object, layout, and verification evidence.
6. Read `src/live/neo_rpc.rs`: implementation detail or helper module.

## Change Safety Checklist

- Keep the stated responsibility boundary intact: Filter bridge events, Normalize payloads, Protect replay/cursor state.
- Update the workflow and dataflow diagrams when adding or removing major execution steps.
- Add or update tests in the files listed under Test Evidence when public API or state-transition behavior changes.
- Re-run `python tools/docs/generate_crate_visual_docs.py` from the Neo N4 repository root after source layout changes.
