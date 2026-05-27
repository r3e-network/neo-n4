//! SP1 guest adapter for the shared Neo L2 execution core.
//!
//! The backend-neutral batch logic lives in `neo-execution-core`: canonical
//! request parsing, L1 message folding, tx/receipt Merkle roots, state-root
//! folding, and public-input commitments. This crate supplies the zkVM-specific
//! transaction executor by running every tx through `neo_vm_guest::execute`.
//! It deliberately does not depend on the PolkaVM-backed RISC-V host; PolkaVM
//! remains the fast native RISC-V execution backend.

extern crate alloc;

use alloc::vec::Vec;
use neo_execution_core::VmExecutionReceipt;

pub use neo_execution_core::{
    execute_batch_with, hash256, merkle_root, BatchResult, ExecutionError, DEFAULT_PER_TX_GAS_LIMIT,
};

/// Pure execution function invariant under the proving contract.
///
/// Returns the public-input bundle that the host commits to L1. The shared
/// core owns canonical batch folding; this guest owns the SP1/NeoVM adapter.
pub fn execute_batch(input: &[u8]) -> Result<BatchResult, ExecutionError> {
    execute_batch_with(input, execute_transaction)
}

fn execute_transaction(tx: &[u8], gas_limit: u64) -> VmExecutionReceipt {
    let input = neo_vm_guest::ProofInput {
        script: tx.to_vec(),
        arguments: Vec::new(),
        gas_limit,
    };
    let output = neo_vm_guest::execute(input);

    VmExecutionReceipt {
        state: output.state,
        gas_consumed: output.gas_consumed,
        output_hash: neo_vm_guest::try_hash_proof_output(&output)
            .expect("ProofOutput serialization must not fail for valid VM output"),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn build_minimal_request_v2() -> Vec<u8> {
        // Wire format v2: [1B version=2][4B chain_id][8B batch_number]
        // [10 × 32B roots][4B l1_count][l1_messages][4B tx_count][transactions]
        let mut buf = Vec::new();
        buf.push(2u8); // version 2
        buf.extend_from_slice(&1099u32.to_le_bytes());
        buf.extend_from_slice(&7u64.to_le_bytes());
        // pre_state_root (32B zero)
        buf.extend_from_slice(&[0u8; 32]);
        // da_commitment
        buf.extend_from_slice(&[0xCDu8; 32]);
        // withdrawal_root
        buf.extend_from_slice(&[0u8; 32]);
        // l2_to_l1_message_root
        buf.extend_from_slice(&[0u8; 32]);
        // l2_to_l2_message_root
        buf.extend_from_slice(&[0u8; 32]);
        // l1_message_hash
        buf.extend_from_slice(&[0u8; 32]);
        // block_context_hash
        buf.extend_from_slice(&[0u8; 32]);
        // l1_messages: 0
        buf.extend_from_slice(&0u32.to_le_bytes());
        // transactions: 1
        buf.extend_from_slice(&1u32.to_le_bytes());
        // tx: 3 bytes
        buf.extend_from_slice(&3u32.to_le_bytes());
        buf.extend_from_slice(&[0xAA, 0xBB, 0xCC]);
        buf
    }

    #[test]
    fn parse_then_execute_minimal() {
        let bytes = build_minimal_request_v2();
        let result = execute_batch(&bytes).expect("execute_batch failed");
        assert_ne!(result.post_state_root, [0u8; 32]);
        assert_ne!(result.public_input_hash, [0u8; 32]);
    }

    #[test]
    fn determinism_same_input_same_output() {
        let bytes = build_minimal_request_v2();
        let r1 = execute_batch(&bytes).unwrap();
        let r2 = execute_batch(&bytes).unwrap();
        assert_eq!(r1, r2);
    }

    #[test]
    fn v1_backward_compat() {
        // v1 wire format still works — roots default to zero
        let mut buf = Vec::new();
        buf.push(1u8);
        buf.extend_from_slice(&1099u32.to_le_bytes());
        buf.extend_from_slice(&7u64.to_le_bytes());
        buf.extend_from_slice(&[0u8; 32]);
        buf.extend_from_slice(&[0xCDu8; 32]);
        buf.extend_from_slice(&0u32.to_le_bytes()); // l1_count
        buf.extend_from_slice(&1u32.to_le_bytes()); // tx_count
        buf.extend_from_slice(&3u32.to_le_bytes()); // tx_len
        buf.extend_from_slice(&[0xAA, 0xBB, 0xCC]);
        let result = execute_batch(&buf).expect("v1 execution failed");
        assert_ne!(result.public_input_hash, [0u8; 32]);
    }

    #[test]
    fn truncated_input_rejected() {
        assert!(matches!(
            execute_batch(&[1u8]),
            Err(ExecutionError::Truncated)
        ));
    }

    #[test]
    fn unsupported_version_rejected() {
        let mut bytes = vec![99u8];
        bytes.resize(300, 0);
        match execute_batch(&bytes) {
            Err(ExecutionError::InvalidVersion(99)) => (),
            other => panic!("expected InvalidVersion(99), got {:?}", other),
        }
    }

    #[test]
    fn merkle_root_single_leaf_is_leaf() {
        let leaf = [0x42u8; 32];
        assert_eq!(merkle_root(&[leaf]), leaf);
    }

    #[test]
    fn merkle_root_empty_is_zero() {
        assert_eq!(merkle_root(&[]), [0u8; 32]);
    }

    #[test]
    fn merkle_root_changes_with_leaf_order() {
        let a = [0x01u8; 32];
        let b = [0x02u8; 32];
        assert_ne!(merkle_root(&[a, b]), merkle_root(&[b, a]));
    }
}
