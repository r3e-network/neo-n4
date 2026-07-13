#![no_std]

extern crate alloc;

mod batch;
mod hashing;
mod manifest;
mod transaction;
mod types;
mod wire;

pub use batch::{compute_batch_with, verify_artifact_with, verify_decoded_artifact_with};
pub use hashing::{
    CONTRACT_BINDING_HASH_DOMAIN, CONTRACT_BINDING_KEY_PREFIX, EVENTS_HASH_DOMAIN,
    STACK_STATE_MAGIC, STORAGE_DELTA_HASH_DOMAIN, contract_binding_hash, contract_binding_key,
    encode_receipt, encode_stack_state, events_hash, hash_block_context, hash_l1_message,
    hash_l1_messages, hash_public_inputs, hash160, hash256, keyed_state_root,
    keyed_state_root_from_map, merkle_root, normalize_signed_le, receipt_hash, state_leaf_hash,
    storage_delta_hash,
};
pub use transaction::parse_transaction;
pub use types::*;
pub use wire::{
    encode_batch_effects, encode_execution_payload, encode_proof_witness_artifact,
    encode_state_witness, parse_batch_effects, parse_execution_payload,
    parse_proof_witness_artifact, parse_state_witness,
};
