#![no_std]

extern crate alloc;

mod batch;
mod hashing;
mod manifest;
mod native;
mod transaction;
mod types;
mod wire;

pub use batch::{
    compute_batch_transition_with, compute_batch_with, verify_artifact_with,
    verify_decoded_artifact_with,
};
pub use hashing::{
    CONTRACT_BINDING_HASH_DOMAIN, CONTRACT_BINDING_KEY_PREFIX, EVENTS_HASH_DOMAIN,
    STACK_STATE_MAGIC, STORAGE_DELTA_HASH_DOMAIN, contract_binding_hash, contract_binding_key,
    encode_receipt, encode_stack_state, events_hash, hash_block_context, hash_l1_message,
    hash_l1_messages, hash_public_inputs, hash160, hash256, keyed_state_root,
    keyed_state_root_from_map, merkle_root, normalize_signed_le, receipt_hash, state_leaf_hash,
    storage_delta_hash,
};
pub use native::{
    NativeCallContextV1, NativeTransitionV1, apply_l1_inbox_v1, bridged_nep17_hash,
    contract_management_key, derive_outbound_roots_v1, governance_hash, l2_bridge_hash,
    l2_message_hash, native_contract_hash, native_emit_message_v1, native_initiate_withdrawal_v1,
    token_management_hash,
};
pub use transaction::parse_transaction;
pub use types::*;
pub use wire::{
    MAX_EXECUTION_PAYLOAD_BYTES, MAX_NATIVE_EXECUTION_OUTPUT_BYTES,
    MAX_PROOF_WITNESS_ARTIFACT_BYTES, MAX_STATE_WITNESS_BYTES, encode_batch_effects,
    encode_execution_payload, encode_native_execution_output, encode_proof_witness_artifact,
    encode_state_witness, parse_batch_effects, parse_execution_payload,
    parse_native_execution_output, parse_proof_witness_artifact, parse_state_witness,
};
