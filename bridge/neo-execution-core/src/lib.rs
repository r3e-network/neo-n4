#![no_std]
//! Backend-agnostic Neo L2 batch execution primitives.
//!
//! This crate is intentionally smaller than either execution backend:
//! it knows how to parse canonical batch bytes, fold L1 messages, compute
//! transaction/receipt Merkle roots, and build the public-input commitment.
//! It does not execute NeoVM bytecode itself and depends on neither SP1 nor
//! PolkaVM. Callers provide a small per-transaction execution receipt from
//! their backend of choice.

extern crate alloc;

use alloc::vec::Vec;
use core::convert::TryInto;
use sha2::{Digest, Sha256};

/// Current canonical batch request wire-format version.
pub const BATCH_WIRE_VERSION: u8 = 1;

/// Default per-tx gas limit when the wire format does not specify one.
///
/// 100,000,000 datoshi = 1 GAS, matching Neo N3's typical tx-level cap.
pub const DEFAULT_PER_TX_GAS_LIMIT: u64 = 100_000_000;

/// Result committed by the batch execution core.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct BatchResult {
    pub post_state_root: [u8; 32],
    pub tx_root: [u8; 32],
    pub receipt_root: [u8; 32],
    pub public_input_hash: [u8; 32],
}

/// Minimal VM execution summary required by the canonical batch fold.
///
/// The concrete backend owns execution semantics. For example:
/// - SP1 guest code can hash `neo_vm_guest::ProofOutput`.
/// - A PolkaVM-backed RISC-V runner can hash its ABI `ExecutionResult`.
///
/// The core only commits to the state byte, gas used, and backend output hash.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct VmExecutionReceipt {
    pub state: u8,
    pub gas_consumed: u64,
    pub output_hash: [u8; 32],
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum ExecutionError {
    Truncated,
    InvalidVersion(u8),
    OversizedField(&'static str),
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct BatchRequest {
    pub chain_id: u32,
    pub batch_number: u64,
    pub pre_state_root: [u8; 32],
    pub da_commitment: [u8; 32],
    pub l1_messages: Vec<L1Message>,
    pub transactions: Vec<Vec<u8>>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct L1Message {
    pub bytes: Vec<u8>,
}

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
        &request.da_commitment,
    );

    Ok(BatchResult {
        post_state_root: state_root,
        tx_root,
        receipt_root,
        public_input_hash,
    })
}

/// Parse canonical batch request bytes.
pub fn parse_batch_request(bytes: &[u8]) -> Result<BatchRequest, ExecutionError> {
    let mut p = 0;
    let version = read_byte(&mut p, bytes)?;
    if version != BATCH_WIRE_VERSION {
        return Err(ExecutionError::InvalidVersion(version));
    }

    let chain_id = read_u32(&mut p, bytes)?;
    let batch_number = read_u64(&mut p, bytes)?;
    let pre_state_root = read_32b(&mut p, bytes)?;
    let da_commitment = read_32b(&mut p, bytes)?;

    let l1_count = read_u32(&mut p, bytes)? as usize;
    if l1_count > 1024 {
        return Err(ExecutionError::OversizedField("l1_messages"));
    }
    let mut l1_messages = Vec::with_capacity(l1_count);
    for _ in 0..l1_count {
        let len = read_u32(&mut p, bytes)? as usize;
        let end = p.checked_add(len).ok_or(ExecutionError::Truncated)?;
        if end > bytes.len() {
            return Err(ExecutionError::Truncated);
        }
        l1_messages.push(L1Message {
            bytes: bytes[p..end].to_vec(),
        });
        p = end;
    }

    let tx_count = read_u32(&mut p, bytes)? as usize;
    if tx_count > 65_536 {
        return Err(ExecutionError::OversizedField("transactions"));
    }
    let mut transactions = Vec::with_capacity(tx_count);
    for _ in 0..tx_count {
        let len = read_u32(&mut p, bytes)? as usize;
        let end = p.checked_add(len).ok_or(ExecutionError::Truncated)?;
        if end > bytes.len() {
            return Err(ExecutionError::Truncated);
        }
        transactions.push(bytes[p..end].to_vec());
        p = end;
    }

    Ok(BatchRequest {
        chain_id,
        batch_number,
        pre_state_root,
        da_commitment,
        l1_messages,
        transactions,
    })
}

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

fn read_byte(p: &mut usize, b: &[u8]) -> Result<u8, ExecutionError> {
    if *p >= b.len() {
        return Err(ExecutionError::Truncated);
    }
    let v = b[*p];
    *p += 1;
    Ok(v)
}

fn read_u32(p: &mut usize, b: &[u8]) -> Result<u32, ExecutionError> {
    let end = p.checked_add(4).ok_or(ExecutionError::Truncated)?;
    if end > b.len() {
        return Err(ExecutionError::Truncated);
    }
    let v = u32::from_le_bytes(b[*p..end].try_into().unwrap());
    *p = end;
    Ok(v)
}

fn read_u64(p: &mut usize, b: &[u8]) -> Result<u64, ExecutionError> {
    let end = p.checked_add(8).ok_or(ExecutionError::Truncated)?;
    if end > b.len() {
        return Err(ExecutionError::Truncated);
    }
    let v = u64::from_le_bytes(b[*p..end].try_into().unwrap());
    *p = end;
    Ok(v)
}

fn read_32b(p: &mut usize, b: &[u8]) -> Result<[u8; 32], ExecutionError> {
    let end = p.checked_add(32).ok_or(ExecutionError::Truncated)?;
    if end > b.len() {
        return Err(ExecutionError::Truncated);
    }
    let v: [u8; 32] = b[*p..end].try_into().unwrap();
    *p = end;
    Ok(v)
}
