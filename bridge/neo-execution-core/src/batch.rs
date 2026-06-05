use alloc::vec::Vec;

use crate::hashing::{
    apply_l1_message, fold_state_root, hash_public_inputs, hash_receipt, hash256, merkle_root,
};
use crate::types::{BatchResult, DEFAULT_PER_TX_GAS_LIMIT, ExecutionError, VmExecutionReceipt};
use crate::wire::parse_batch_request;

/// Execute a canonical batch request with a caller-provided VM backend.
///
/// The closure receives each tx script and the canonical gas limit. It returns
/// a compact receipt digest that the shared core folds into receipt/state roots.
pub fn execute_batch_with<F>(input: &[u8], mut execute_tx: F) -> Result<BatchResult, ExecutionError>
where
    F: FnMut(&[u8], u64) -> VmExecutionReceipt,
{
    let request = parse_batch_request(input)?;
    let mut state_root = request.pre_state_root;
    let mut tx_hashes = Vec::with_capacity(request.transactions.len());
    let mut receipt_hashes = Vec::with_capacity(request.transactions.len());

    for msg in &request.l1_messages {
        state_root = apply_l1_message(&state_root, msg);
    }

    for tx in &request.transactions {
        let tx_hash = hash256(tx);
        let execution = execute_tx(tx, DEFAULT_PER_TX_GAS_LIMIT);
        if execution.gas_consumed > DEFAULT_PER_TX_GAS_LIMIT {
            return Err(ExecutionError::GasExceeded);
        }
        let receipt_hash = hash_receipt(&tx_hash, execution);
        state_root = fold_state_root(&state_root, &receipt_hash);
        tx_hashes.push(tx_hash);
        receipt_hashes.push(receipt_hash);
    }

    let tx_root = merkle_root(&tx_hashes);
    let receipt_root = merkle_root(&receipt_hashes);
    let public_input_hash = hash_public_inputs(
        request.chain_id,
        request.batch_number,
        &request.pre_state_root,
        &state_root,
        &tx_root,
        &receipt_root,
        &request.withdrawal_root,
        &request.l2_to_l1_message_root,
        &request.l2_to_l2_message_root,
        &request.l1_message_hash,
        &request.da_commitment,
        &request.block_context_hash,
    );

    Ok(BatchResult {
        post_state_root: state_root,
        tx_root,
        receipt_root,
        public_input_hash,
    })
}
