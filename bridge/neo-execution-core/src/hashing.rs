use alloc::vec::Vec;

use sha2::{Digest, Sha256};

use crate::types::{L1Message, VmExecutionReceipt};

pub fn merkle_root(leaves: &[[u8; 32]]) -> [u8; 32] {
    if leaves.is_empty() {
        return [0u8; 32];
    }

    let mut current: Vec<[u8; 32]> = leaves.to_vec();
    while current.len() > 1 {
        let mut next = Vec::with_capacity(current.len().div_ceil(2));
        let mut i = 0;
        while i < current.len() {
            let left = current[i];
            let right = if i + 1 < current.len() {
                current[i + 1]
            } else {
                left
            };
            let mut buf = [0u8; 64];
            buf[..32].copy_from_slice(&left);
            buf[32..].copy_from_slice(&right);
            next.push(hash256(&buf));
            i += 2;
        }
        current = next;
    }

    current[0]
}

pub fn hash256(input: &[u8]) -> [u8; 32] {
    let h1 = Sha256::digest(input);
    let h2 = Sha256::digest(h1);
    let mut out = [0u8; 32];
    out.copy_from_slice(&h2);
    out
}

pub fn hash_receipt(tx_hash: &[u8; 32], execution: VmExecutionReceipt) -> [u8; 32] {
    const RECEIPT_PREFIX: &[u8] = b"neo-vm-receipt:v1:";
    let mut receipt_buf = Vec::with_capacity(RECEIPT_PREFIX.len() + 32 + 1 + 8 + 32);
    receipt_buf.extend_from_slice(RECEIPT_PREFIX);
    receipt_buf.extend_from_slice(tx_hash);
    receipt_buf.push(execution.state);
    receipt_buf.extend_from_slice(&execution.gas_consumed.to_le_bytes());
    receipt_buf.extend_from_slice(&execution.output_hash);
    hash256(&receipt_buf)
}

pub fn fold_state_root(prev_root: &[u8; 32], receipt_hash: &[u8; 32]) -> [u8; 32] {
    let mut buf = [0u8; 64];
    buf[..32].copy_from_slice(prev_root);
    buf[32..].copy_from_slice(receipt_hash);
    hash256(&buf)
}

pub fn apply_l1_message(prev_root: &[u8; 32], msg: &L1Message) -> [u8; 32] {
    let mut h = Sha256::new();
    h.update(prev_root);
    h.update(b"L1MSG");
    h.update((msg.bytes.len() as u32).to_le_bytes());
    h.update(&msg.bytes);
    let first = h.finalize();
    let mut h2 = Sha256::new();
    h2.update(first);
    let final_hash = h2.finalize();
    let mut out = [0u8; 32];
    out.copy_from_slice(&final_hash);
    out
}

pub fn hash_public_inputs(
    chain_id: u32,
    batch_number: u64,
    pre_state_root: &[u8; 32],
    post_state_root: &[u8; 32],
    tx_root: &[u8; 32],
    receipt_root: &[u8; 32],
    da_commitment: &[u8; 32],
) -> [u8; 32] {
    let mut h = Sha256::new();
    h.update(chain_id.to_le_bytes());
    h.update(batch_number.to_le_bytes());
    h.update(pre_state_root);
    h.update(post_state_root);
    h.update(tx_root);
    h.update(receipt_root);
    h.update(da_commitment);
    let first = h.finalize();
    let mut h2 = Sha256::new();
    h2.update(first);
    let final_hash = h2.finalize();
    let mut out = [0u8; 32];
    out.copy_from_slice(&final_hash);
    out
}
