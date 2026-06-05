use alloc::vec::Vec;

use crate::types::{BATCH_WIRE_VERSION, BatchRequest, ExecutionError, L1Message};

/// Maximum byte size of a single transaction or L1 message payload. Reject at
/// parse-time before allocating to prevent OOM from a single element claiming
/// to be gigabytes long. Also used as a per-element guard alongside
/// MAX_TOTAL_TX_BYTES which limits the aggregate.
const MAX_PER_ELEMENT_BYTES: u32 = 1024 * 1024; // 1 MiB

/// Maximum total byte size across all transactions in a batch.
/// Reject at parse-time to prevent OOM from an adversarial request
/// specifying 65,536 txs each up to 4 GiB.
const MAX_TOTAL_TX_BYTES: u64 = 8 * 1024 * 1024; // 8 MiB

/// Maximum number of L1 messages per batch.
const MAX_L1_MESSAGES: u32 = 1024;

/// Maximum number of transactions per batch.
const MAX_TRANSACTIONS: u32 = 65_536;

/// Parse canonical batch request bytes.
///
/// Version 1 (deprecated, 75+ bytes): 4-root layout.
/// Version 2 (current, 235+ bytes): 10-root layout matching C# PublicInputs.
pub fn parse_batch_request(bytes: &[u8]) -> Result<BatchRequest, ExecutionError> {
    let mut p = 0;
    let version = read_byte(&mut p, bytes)?;
    if version == 1 {
        return parse_batch_request_v1(bytes, &mut p);
    }
    if version != BATCH_WIRE_VERSION {
        return Err(ExecutionError::InvalidVersion(version));
    }

    let chain_id = read_u32(&mut p, bytes)?;
    let batch_number = read_u64(&mut p, bytes)?;
    let pre_state_root = read_32b(&mut p, bytes)?;
    let da_commitment = read_32b(&mut p, bytes)?;
    let withdrawal_root = read_32b(&mut p, bytes)?;
    let l2_to_l1_message_root = read_32b(&mut p, bytes)?;
    let l2_to_l2_message_root = read_32b(&mut p, bytes)?;
    let l1_message_hash = read_32b(&mut p, bytes)?;
    let block_context_hash = read_32b(&mut p, bytes)?;

    let l1_messages = read_l1_messages(&mut p, bytes)?;
    let transactions = read_transactions(&mut p, bytes)?;

    Ok(BatchRequest {
        wire_version: BATCH_WIRE_VERSION,
        chain_id,
        batch_number,
        pre_state_root,
        da_commitment,
        withdrawal_root,
        l2_to_l1_message_root,
        l2_to_l2_message_root,
        l1_message_hash,
        block_context_hash,
        l1_messages,
        transactions,
    })
}

fn parse_batch_request_v1(bytes: &[u8], p: &mut usize) -> Result<BatchRequest, ExecutionError> {
    let chain_id = read_u32(p, bytes)?;
    let batch_number = read_u64(p, bytes)?;
    let pre_state_root = read_32b(p, bytes)?;
    let da_commitment = read_32b(p, bytes)?;

    let l1_messages = read_l1_messages(p, bytes)?;
    let transactions = read_transactions(p, bytes)?;

    Ok(BatchRequest {
        wire_version: 1,
        chain_id,
        batch_number,
        pre_state_root,
        da_commitment,
        withdrawal_root: [0u8; 32],
        l2_to_l1_message_root: [0u8; 32],
        l2_to_l2_message_root: [0u8; 32],
        l1_message_hash: [0u8; 32],
        block_context_hash: [0u8; 32],
        l1_messages,
        transactions,
    })
}

fn read_l1_messages(p: &mut usize, bytes: &[u8]) -> Result<Vec<L1Message>, ExecutionError> {
    let l1_count = read_u32(p, bytes)?;
    if l1_count > MAX_L1_MESSAGES {
        return Err(ExecutionError::OversizedField("l1_messages"));
    }
    let l1_count = l1_count as usize;

    let mut l1_messages = Vec::with_capacity(l1_count);
    let mut total_bytes: u64 = 0;
    for _ in 0..l1_count {
        let payload = read_var_bytes(p, bytes)?;
        total_bytes = total_bytes.saturating_add(payload.len() as u64);
        if total_bytes > MAX_TOTAL_TX_BYTES {
            return Err(ExecutionError::OversizedField("total L1 message bytes"));
        }
        l1_messages.push(L1Message { bytes: payload });
    }
    Ok(l1_messages)
}

fn read_transactions(p: &mut usize, bytes: &[u8]) -> Result<Vec<Vec<u8>>, ExecutionError> {
    let tx_count = read_u32(p, bytes)?;
    if tx_count > MAX_TRANSACTIONS {
        return Err(ExecutionError::OversizedField("transactions"));
    }
    let tx_count = tx_count as usize;

    let mut transactions = Vec::with_capacity(tx_count);
    let mut total_bytes: u64 = 0;
    for _ in 0..tx_count {
        let tx = read_var_bytes(p, bytes)?;
        total_bytes = total_bytes.saturating_add(tx.len() as u64);
        if total_bytes > MAX_TOTAL_TX_BYTES {
            return Err(ExecutionError::OversizedField("total transaction bytes"));
        }
        transactions.push(tx);
    }
    Ok(transactions)
}

fn read_var_bytes(p: &mut usize, bytes: &[u8]) -> Result<Vec<u8>, ExecutionError> {
    let len = read_u32(p, bytes)? as usize;
    if len > MAX_PER_ELEMENT_BYTES as usize {
        return Err(ExecutionError::OversizedField(
            "element exceeds per-element byte cap",
        ));
    }
    let end = p.checked_add(len).ok_or(ExecutionError::Truncated)?;
    if end > bytes.len() {
        return Err(ExecutionError::Truncated);
    }

    let out = bytes[*p..end].to_vec();
    *p = end;
    Ok(out)
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
    let v = u32::from_le_bytes(
        b[*p..end]
            .try_into()
            .map_err(|_| ExecutionError::Truncated)?,
    );
    *p = end;
    Ok(v)
}

fn read_u64(p: &mut usize, b: &[u8]) -> Result<u64, ExecutionError> {
    let end = p.checked_add(8).ok_or(ExecutionError::Truncated)?;
    if end > b.len() {
        return Err(ExecutionError::Truncated);
    }
    let v = u64::from_le_bytes(
        b[*p..end]
            .try_into()
            .map_err(|_| ExecutionError::Truncated)?,
    );
    *p = end;
    Ok(v)
}

fn read_32b(p: &mut usize, b: &[u8]) -> Result<[u8; 32], ExecutionError> {
    let end = p.checked_add(32).ok_or(ExecutionError::Truncated)?;
    if end > b.len() {
        return Err(ExecutionError::Truncated);
    }
    let v: [u8; 32] = b[*p..end]
        .try_into()
        .map_err(|_| ExecutionError::Truncated)?;
    *p = end;
    Ok(v)
}
