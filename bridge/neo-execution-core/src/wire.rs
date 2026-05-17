use alloc::vec::Vec;
use core::convert::TryInto;

use crate::types::{BatchRequest, ExecutionError, L1Message, BATCH_WIRE_VERSION};

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

    let l1_messages = read_l1_messages(&mut p, bytes)?;
    let transactions = read_transactions(&mut p, bytes)?;

    Ok(BatchRequest {
        chain_id,
        batch_number,
        pre_state_root,
        da_commitment,
        l1_messages,
        transactions,
    })
}

fn read_l1_messages(p: &mut usize, bytes: &[u8]) -> Result<Vec<L1Message>, ExecutionError> {
    let l1_count = read_u32(p, bytes)? as usize;
    if l1_count > 1024 {
        return Err(ExecutionError::OversizedField("l1_messages"));
    }

    let mut l1_messages = Vec::with_capacity(l1_count);
    for _ in 0..l1_count {
        let payload = read_var_bytes(p, bytes)?;
        l1_messages.push(L1Message { bytes: payload });
    }
    Ok(l1_messages)
}

fn read_transactions(p: &mut usize, bytes: &[u8]) -> Result<Vec<Vec<u8>>, ExecutionError> {
    let tx_count = read_u32(p, bytes)? as usize;
    if tx_count > 65_536 {
        return Err(ExecutionError::OversizedField("transactions"));
    }

    let mut transactions = Vec::with_capacity(tx_count);
    for _ in 0..tx_count {
        transactions.push(read_var_bytes(p, bytes)?);
    }
    Ok(transactions)
}

fn read_var_bytes(p: &mut usize, bytes: &[u8]) -> Result<Vec<u8>, ExecutionError> {
    let len = read_u32(p, bytes)? as usize;
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
