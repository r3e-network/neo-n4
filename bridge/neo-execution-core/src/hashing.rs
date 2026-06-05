use alloc::vec::Vec;

use sha2::{Digest, Sha256};

use crate::types::{L1Message, VmExecutionReceipt};

pub fn merkle_root(leaves: &[[u8; 32]]) -> [u8; 32] {
    if leaves.is_empty() {
        return [0u8; 32];
    }

    let mut current: Vec<[u8; 32]> = leaves.to_vec();
    let mut n = current.len();
    while n > 1 {
        // Compute parent hashes in-place: parent[i] overwrites children[2*i],
        // which is safe because child[2*i] is never read after parent[i] is
        // computed.
        let half = n / 2;
        for i in 0..half {
            let left = current[2 * i];
            let right = if 2 * i + 1 < n {
                current[2 * i + 1]
            } else {
                left
            };
            let mut buf = [0u8; 64];
            buf[..32].copy_from_slice(&left);
            buf[32..].copy_from_slice(&right);
            current[i] = hash256(&buf);
        }
        // If n is odd, the last leaf has no sibling. Neo L1 convention:
        // pair the orphan with itself and hash, matching the C# MerkleTree
        // and Neo's native MerkleTree (Cryptography/MerkleTree.cs:54-56).
        // Without this, tx_root in Rust would diverge from C# for any batch
        // with an odd number of transactions.
        if n % 2 != 0 {
            let orphan = current[n - 1];
            let mut buf = [0u8; 64];
            buf[..32].copy_from_slice(&orphan);
            buf[32..].copy_from_slice(&orphan);
            current[half] = hash256(&buf);
            n = half + 1;
        } else {
            n = half;
        }
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

/// Compute the zkVM-internal receipt leaf hash.
///
/// Layout (90 bytes): `"neo-vm-receipt:v1:"[17] || tx_hash[32] || state[1] ||
/// gas_consumed[8 LE] || output_hash[32]`, double-SHA256.
///
/// This is the **zkVM proof path** receipt hash. The C# native-executor receipt
/// hash at `Receipt.Hash()` uses a different layout (105 bytes, no prefix, separate
/// StorageDeltaHash + EventsHash). These two hashes intentionally differ because they
/// represent different execution backends. Only the zkVM's receipt hash flow reaches
/// L1 settlement; the C# native path is for local devnet/testing.
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
    // Wire parser enforces 1 MiB per-element cap; the u32 conversion is safe
    // for all parser-constructed messages. For programmatically constructed
    // L1Message, saturate at u32::MAX to prevent silent truncation.
    let msg_len = u32::try_from(msg.bytes.len()).unwrap_or(u32::MAX);
    let mut buf = Vec::with_capacity(32 + 5 + 4 + msg.bytes.len());
    buf.extend_from_slice(prev_root);
    buf.extend_from_slice(b"L1MSG");
    buf.extend_from_slice(&msg_len.to_le_bytes());
    buf.extend_from_slice(&msg.bytes);
    hash256(&buf)
}

/// Compute the canonical public-input hash matching the C# `StateRootCalculator.HashPublicInputs`.
///
/// Layout (332 bytes, all little-endian):
///   [4B chain_id][8B batch_number][10 × 32B roots]
///
/// The 10 roots in order: PreStateRoot, PostStateRoot, TxRoot, ReceiptRoot,
/// WithdrawalRoot, L2ToL1MessageRoot, L2ToL2MessageRoot, L1MessageHash,
/// DACommitment, BlockContextHash.
///
/// This is double-SHA256 (Hash256) over the concatenated 332-byte buffer,
/// matching Neo's canonical hash convention.
#[allow(clippy::too_many_arguments)]
pub fn hash_public_inputs(
    chain_id: u32,
    batch_number: u64,
    pre_state_root: &[u8; 32],
    post_state_root: &[u8; 32],
    tx_root: &[u8; 32],
    receipt_root: &[u8; 32],
    withdrawal_root: &[u8; 32],
    l2_to_l1_message_root: &[u8; 32],
    l2_to_l2_message_root: &[u8; 32],
    l1_message_hash: &[u8; 32],
    da_commitment: &[u8; 32],
    block_context_hash: &[u8; 32],
) -> [u8; 32] {
    let mut buf = [0u8; 4 + 8 + 10 * 32]; // 332 bytes
    buf[0..4].copy_from_slice(&chain_id.to_le_bytes());
    buf[4..12].copy_from_slice(&batch_number.to_le_bytes());
    let mut pos = 12;
    for root in [
        pre_state_root,
        post_state_root,
        tx_root,
        receipt_root,
        withdrawal_root,
        l2_to_l1_message_root,
        l2_to_l2_message_root,
        l1_message_hash,
        da_commitment,
        block_context_hash,
    ] {
        buf[pos..pos + 32].copy_from_slice(root);
        pos += 32;
    }
    hash256(&buf)
}
