//! Canonical wire encoding/decoding for neo-execution-core.

mod artifact;
mod commitment;
mod constants;
mod io;
mod native_output;
mod payload;
mod state;

pub use artifact::{encode_proof_witness_artifact, parse_proof_witness_artifact};
pub use commitment::{
    encode_chain_config, encode_commitment, encode_merkle_proof, parse_chain_config,
    parse_commitment, parse_merkle_proof, verify_merkle_proof,
};
pub use constants::{
    CHAIN_CONFIG_SIZE, COMMITMENT_FIXED_SIZE, MAX_EXECUTION_PAYLOAD_BYTES, MAX_MERKLE_DEPTH,
    MAX_NATIVE_EXECUTION_OUTPUT_BYTES, MAX_PROOF_BYTES, MAX_PROOF_WITNESS_ARTIFACT_BYTES,
    MAX_STATE_WITNESS_BYTES, MERKLE_PROOF_HEADER_SIZE,
};
pub use native_output::{encode_native_execution_output, parse_native_execution_output};
pub use payload::{encode_execution_payload, parse_execution_payload};
pub use state::{
    encode_batch_effects, encode_state_witness, parse_batch_effects, parse_state_witness,
};
