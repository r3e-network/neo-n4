//! Pure deterministic execution functions for the SP1 ZK guest. This crate
//! is host-testable; the SP1 entrypoint lives in `src/main.rs` and calls
//! into these functions.
//!
//! Each tx in the batch wire format is loaded as a Neo N3 VM script and
//! executed by `neo_vm_guest::execute` (vendored via the
//! `external/neo-zkvm` submodule). The proof attests to real VM
//! execution — opcodes, gas accounting, halt/fault outcome — not just a
//! hash of the bytes.

extern crate alloc;

use core::convert::TryInto;
use sha2::{Digest, Sha256};

/// Pure execution function — invariant under the proving contract.
/// Returns the public-input bundle that the host commits to L1.
pub fn execute_batch(input: &[u8]) -> Result<BatchResult, ExecutionError> {
    let request = parse_batch_request(input)?;
    let mut state_root = request.pre_state_root;
    let mut tx_hashes = Vec::with_capacity(request.transactions.len());
    let mut receipt_hashes = Vec::with_capacity(request.transactions.len());

    for msg in &request.l1_messages {
        state_root = apply_l1_message(&state_root, msg);
    }

    for tx in &request.transactions {
        let (new_root, tx_hash, receipt_hash, _success) = apply_transaction(&state_root, tx);
        state_root = new_root;
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
        &request.da_commitment,
    );

    Ok(BatchResult {
        post_state_root: state_root,
        tx_root,
        receipt_root,
        public_input_hash,
    })
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct BatchResult {
    pub post_state_root: [u8; 32],
    pub tx_root: [u8; 32],
    pub receipt_root: [u8; 32],
    pub public_input_hash: [u8; 32],
}

#[derive(Debug)]
pub enum ExecutionError {
    Truncated,
    InvalidVersion(u8),
    OversizedField(&'static str),
}

#[derive(Debug, Clone)]
struct BatchRequest {
    chain_id: u32,
    batch_number: u64,
    pre_state_root: [u8; 32],
    da_commitment: [u8; 32],
    l1_messages: Vec<L1Message>,
    transactions: Vec<Vec<u8>>,
}

#[derive(Debug, Clone)]
struct L1Message {
    bytes: Vec<u8>,
}

fn parse_batch_request(bytes: &[u8]) -> Result<BatchRequest, ExecutionError> {
    let mut p = 0;
    let read_byte = |p: &mut usize, b: &[u8]| -> Result<u8, ExecutionError> {
        if *p >= b.len() { return Err(ExecutionError::Truncated); }
        let v = b[*p]; *p += 1; Ok(v)
    };
    let read_u32 = |p: &mut usize, b: &[u8]| -> Result<u32, ExecutionError> {
        if *p + 4 > b.len() { return Err(ExecutionError::Truncated); }
        let v = u32::from_le_bytes(b[*p..*p+4].try_into().unwrap()); *p += 4; Ok(v)
    };
    let read_u64 = |p: &mut usize, b: &[u8]| -> Result<u64, ExecutionError> {
        if *p + 8 > b.len() { return Err(ExecutionError::Truncated); }
        let v = u64::from_le_bytes(b[*p..*p+8].try_into().unwrap()); *p += 8; Ok(v)
    };
    let read_32b = |p: &mut usize, b: &[u8]| -> Result<[u8; 32], ExecutionError> {
        if *p + 32 > b.len() { return Err(ExecutionError::Truncated); }
        let v: [u8; 32] = b[*p..*p+32].try_into().unwrap(); *p += 32; Ok(v)
    };

    let version = read_byte(&mut p, bytes)?;
    if version != 1 { return Err(ExecutionError::InvalidVersion(version)); }
    let chain_id = read_u32(&mut p, bytes)?;
    let batch_number = read_u64(&mut p, bytes)?;
    let pre_state_root = read_32b(&mut p, bytes)?;
    let da_commitment = read_32b(&mut p, bytes)?;

    let l1_count = read_u32(&mut p, bytes)? as usize;
    if l1_count > 1024 { return Err(ExecutionError::OversizedField("l1_messages")); }
    let mut l1_messages = Vec::with_capacity(l1_count);
    for _ in 0..l1_count {
        let len = read_u32(&mut p, bytes)? as usize;
        if p + len > bytes.len() { return Err(ExecutionError::Truncated); }
        l1_messages.push(L1Message { bytes: bytes[p..p+len].to_vec() });
        p += len;
    }

    let tx_count = read_u32(&mut p, bytes)? as usize;
    if tx_count > 65536 { return Err(ExecutionError::OversizedField("transactions")); }
    let mut transactions = Vec::with_capacity(tx_count);
    for _ in 0..tx_count {
        let len = read_u32(&mut p, bytes)? as usize;
        if p + len > bytes.len() { return Err(ExecutionError::Truncated); }
        transactions.push(bytes[p..p+len].to_vec());
        p += len;
    }

    Ok(BatchRequest { chain_id, batch_number, pre_state_root, da_commitment, l1_messages, transactions })
}

fn apply_l1_message(prev_root: &[u8; 32], msg: &L1Message) -> [u8; 32] {
    let mut h = Sha256::new();
    h.update(prev_root);
    h.update(b"L1MSG");
    h.update(&(msg.bytes.len() as u32).to_le_bytes());
    h.update(&msg.bytes);
    let first = h.finalize();
    let mut h2 = Sha256::new();
    h2.update(&first);
    let final_hash = h2.finalize();
    let mut out = [0u8; 32];
    out.copy_from_slice(&final_hash);
    out
}

/// Default per-tx gas limit when the wire format doesn't specify one.
/// 100,000,000 datoshi = 1 GAS — matches Neo N3's typical tx-level cap.
const DEFAULT_PER_TX_GAS_LIMIT: u64 = 100_000_000;

/// Execute a single tx through the real Neo N3 VM (`neo_vm_guest::execute`)
/// and fold its outcome into the running state root + receipt digest.
///
/// The tx bytes are loaded as the script; arguments are empty (entrypoint
/// scripts must push their own args via Neo's PUSH opcodes); gas is bounded.
/// The returned `ProofOutput` carries the VM exit state (`0=halt`, non-zero
/// fault), gas consumed, and the top-of-stack result — all of which feed
/// into the receipt hash so a tampered execution can't pass the proof.
fn apply_transaction(prev_root: &[u8; 32], tx: &[u8]) -> ([u8; 32], [u8; 32], [u8; 32], bool) {
    let tx_hash = hash256(tx);

    let input = neo_vm_guest::ProofInput {
        script: tx.to_vec(),
        arguments: alloc::vec::Vec::new(),
        gas_limit: DEFAULT_PER_TX_GAS_LIMIT,
    };
    let output = neo_vm_guest::execute(input);
    let success = output.state == 0;

    // Receipt digest commits to: tx hash + VM state + gas consumed + serialized
    // ProofOutput. Any tampering with the execution (different state, different
    // gas, different result) changes the receipt root and the proof fails.
    let mut receipt_buf = alloc::vec::Vec::with_capacity(64);
    receipt_buf.extend_from_slice(b"neo-vm-receipt:v1:");
    receipt_buf.extend_from_slice(&tx_hash);
    receipt_buf.push(output.state);
    receipt_buf.extend_from_slice(&output.gas_consumed.to_le_bytes());
    receipt_buf.extend_from_slice(&hash_proof_output(&output));
    let receipt_hash = hash256(&receipt_buf);

    // State root advances on every tx (halt OR fault) — both outcomes are
    // legitimate VM observations the proof attests to. The ordering of the
    // mix-in matches the canonical receipt content.
    let mut buf = [0u8; 64];
    buf[..32].copy_from_slice(prev_root);
    buf[32..].copy_from_slice(&receipt_hash);
    let new_root = hash256(&buf);

    (new_root, tx_hash, receipt_hash, success)
}

/// Bincode-encode a ProofOutput then hash. Defers to neo-vm-guest's
/// helper so wire-format changes upstream don't silently desync this
/// guest's hash from the verifier's expectation.
fn hash_proof_output(output: &neo_vm_guest::ProofOutput) -> [u8; 32] {
    neo_vm_guest::hash_proof_output(output)
}

pub fn merkle_root(leaves: &[[u8; 32]]) -> [u8; 32] {
    if leaves.is_empty() {
        return [0u8; 32];
    }
    let mut current: Vec<[u8; 32]> = leaves.to_vec();
    while current.len() > 1 {
        let mut next = Vec::with_capacity((current.len() + 1) / 2);
        let mut i = 0;
        while i < current.len() {
            let left = current[i];
            let right = if i + 1 < current.len() { current[i + 1] } else { left };
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
    let h2 = Sha256::digest(&h1);
    let mut out = [0u8; 32];
    out.copy_from_slice(&h2);
    out
}

fn hash_public_inputs(
    chain_id: u32,
    batch_number: u64,
    pre_state_root: &[u8; 32],
    post_state_root: &[u8; 32],
    tx_root: &[u8; 32],
    receipt_root: &[u8; 32],
    da_commitment: &[u8; 32],
) -> [u8; 32] {
    let mut h = Sha256::new();
    h.update(&chain_id.to_le_bytes());
    h.update(&batch_number.to_le_bytes());
    h.update(pre_state_root);
    h.update(post_state_root);
    h.update(tx_root);
    h.update(receipt_root);
    h.update(da_commitment);
    let first = h.finalize();
    let mut h2 = Sha256::new();
    h2.update(&first);
    let final_hash = h2.finalize();
    let mut out = [0u8; 32];
    out.copy_from_slice(&final_hash);
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    fn build_minimal_request() -> Vec<u8> {
        let mut buf = Vec::new();
        buf.push(1u8);
        buf.extend_from_slice(&1099u32.to_le_bytes());
        buf.extend_from_slice(&7u64.to_le_bytes());
        buf.extend_from_slice(&[0u8; 32]);
        buf.extend_from_slice(&[0xCDu8; 32]);
        buf.extend_from_slice(&0u32.to_le_bytes());
        buf.extend_from_slice(&1u32.to_le_bytes());
        buf.extend_from_slice(&3u32.to_le_bytes());
        buf.extend_from_slice(&[0xAA, 0xBB, 0xCC]);
        buf
    }

    #[test]
    fn parse_then_execute_minimal() {
        let bytes = build_minimal_request();
        let result = execute_batch(&bytes).expect("execute_batch failed");
        assert_ne!(result.post_state_root, [0u8; 32]);
        assert_ne!(result.public_input_hash, [0u8; 32]);
    }

    #[test]
    fn determinism_same_input_same_output() {
        let bytes = build_minimal_request();
        let r1 = execute_batch(&bytes).unwrap();
        let r2 = execute_batch(&bytes).unwrap();
        assert_eq!(r1, r2);
    }

    #[test]
    fn truncated_input_rejected() {
        assert!(matches!(execute_batch(&[1u8]), Err(ExecutionError::Truncated)));
    }

    #[test]
    fn unsupported_version_rejected() {
        let mut bytes = vec![99u8];
        bytes.resize(100, 0);
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

    #[test]
    fn hash256_equals_double_sha256() {
        let input = b"neo";
        let manual = {
            let h1 = sha2::Sha256::digest(input);
            let h2 = sha2::Sha256::digest(&h1);
            let mut o = [0u8; 32];
            o.copy_from_slice(&h2);
            o
        };
        assert_eq!(hash256(input), manual);
    }
}
