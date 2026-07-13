use alloc::{collections::BTreeMap, vec::Vec};

use ripemd::{Digest as RipemdDigest, Ripemd160};
use sha2::{Digest as ShaDigest, Sha256};

use crate::types::{
    BatchBlockContext, CANONICAL_RECEIPT_V1_BYTES, CanonicalReceiptV1, CanonicalStackValue,
    ExecutionError, ExecutionEvent, L1Message, StateEntry, StorageDelta, UInt160, UInt256,
};

pub const CONTRACT_BINDING_KEY_PREFIX: &[u8] = b"\xffneo-n4/contract-binding/v1/";
pub const CONTRACT_BINDING_HASH_DOMAIN: &[u8] = b"neo-n4/contract-binding/v1\0";
pub const STORAGE_DELTA_HASH_DOMAIN: &[u8] = b"neo-n4/storage-delta/v1\0";
pub const EVENTS_HASH_DOMAIN: &[u8] = b"neo-n4/events/v1\0";
pub const STACK_STATE_MAGIC: &[u8; 8] = b"NEO4STK1";

#[must_use]
pub fn hash256(input: &[u8]) -> UInt256 {
    let first = Sha256::digest(input);
    let second = Sha256::digest(first);
    let mut output = [0u8; 32];
    output.copy_from_slice(&second);
    output
}

#[must_use]
pub fn hash160(input: &[u8]) -> UInt160 {
    let sha = Sha256::digest(input);
    let digest = <Ripemd160 as RipemdDigest>::digest(sha);
    let mut output = [0u8; 20];
    output.copy_from_slice(&digest);
    output
}

#[must_use]
pub fn merkle_root(leaves: &[UInt256]) -> UInt256 {
    if leaves.is_empty() {
        return [0u8; 32];
    }
    let mut level = leaves.to_vec();
    while level.len() > 1 {
        let mut parents = Vec::with_capacity(level.len().div_ceil(2));
        for pair in level.chunks(2) {
            let left = pair[0];
            let right = pair.get(1).copied().unwrap_or(left);
            let mut bytes = [0u8; 64];
            bytes[..32].copy_from_slice(&left);
            bytes[32..].copy_from_slice(&right);
            parents.push(hash256(&bytes));
        }
        level = parents;
    }
    level[0]
}

#[must_use]
pub fn keyed_state_root(entries: &[StateEntry]) -> UInt256 {
    let leaves = entries
        .iter()
        .map(|entry| state_leaf_hash(&entry.key, &entry.value))
        .collect::<Vec<_>>();
    merkle_root(&leaves)
}

#[must_use]
pub fn keyed_state_root_from_map(entries: &BTreeMap<Vec<u8>, Vec<u8>>) -> UInt256 {
    let leaves = entries
        .iter()
        .map(|(key, value)| state_leaf_hash(key, value))
        .collect::<Vec<_>>();
    merkle_root(&leaves)
}

#[must_use]
pub fn state_leaf_hash(key: &[u8], value: &[u8]) -> UInt256 {
    let mut bytes = Vec::with_capacity(8 + key.len() + value.len());
    push_u32(&mut bytes, key.len());
    bytes.extend_from_slice(key);
    push_u32(&mut bytes, value.len());
    bytes.extend_from_slice(value);
    hash256(&bytes)
}

#[must_use]
pub fn contract_binding_key(hash: &UInt160) -> Vec<u8> {
    let mut key = Vec::with_capacity(CONTRACT_BINDING_KEY_PREFIX.len() + hash.len());
    key.extend_from_slice(CONTRACT_BINDING_KEY_PREFIX);
    key.extend_from_slice(hash);
    key
}

#[must_use]
pub fn contract_binding_hash(id: i32, hash: &UInt160, script: &[u8], manifest: &[u8]) -> UInt256 {
    let mut bytes = Vec::with_capacity(
        CONTRACT_BINDING_HASH_DOMAIN.len() + 4 + 20 + 4 + script.len() + 4 + manifest.len(),
    );
    bytes.extend_from_slice(CONTRACT_BINDING_HASH_DOMAIN);
    bytes.extend_from_slice(&id.to_le_bytes());
    bytes.extend_from_slice(hash);
    push_u32(&mut bytes, script.len());
    bytes.extend_from_slice(script);
    push_u32(&mut bytes, manifest.len());
    bytes.extend_from_slice(manifest);
    hash256(&bytes)
}

pub fn encode_receipt(receipt: &CanonicalReceiptV1) -> [u8; CANONICAL_RECEIPT_V1_BYTES] {
    let mut bytes = [0u8; CANONICAL_RECEIPT_V1_BYTES];
    bytes[..32].copy_from_slice(&receipt.tx_hash);
    bytes[32] = u8::from(receipt.success);
    bytes[33..41].copy_from_slice(&receipt.gas_consumed.to_le_bytes());
    bytes[41..73].copy_from_slice(&receipt.storage_delta_hash);
    bytes[73..105].copy_from_slice(&receipt.events_hash);
    bytes
}

#[must_use]
pub fn receipt_hash(receipt: &CanonicalReceiptV1) -> UInt256 {
    hash256(&encode_receipt(receipt))
}

#[must_use]
pub fn storage_delta_hash(deltas: &[StorageDelta]) -> UInt256 {
    if deltas.is_empty() {
        return [0u8; 32];
    }
    let mut bytes = Vec::new();
    bytes.extend_from_slice(STORAGE_DELTA_HASH_DOMAIN);
    push_u32(&mut bytes, deltas.len());
    for delta in deltas {
        push_u32(&mut bytes, delta.key.len());
        bytes.extend_from_slice(&delta.key);
        bytes.push(delta.operation as u8);
        push_optional_bytes(&mut bytes, delta.old_value.as_deref());
        push_optional_bytes(&mut bytes, delta.new_value.as_deref());
    }
    hash256(&bytes)
}

pub fn events_hash(events: &[ExecutionEvent]) -> Result<UInt256, ExecutionError> {
    if events.is_empty() {
        return Ok([0u8; 32]);
    }
    let mut bytes = Vec::new();
    bytes.extend_from_slice(EVENTS_HASH_DOMAIN);
    push_u32(&mut bytes, events.len());
    for event in events {
        bytes.extend_from_slice(&event.script_hash);
        push_u32(&mut bytes, event.name.len());
        bytes.extend_from_slice(event.name.as_bytes());
        let state = encode_stack_state(&event.state)?;
        push_u32(&mut bytes, state.len());
        bytes.extend_from_slice(&state);
    }
    Ok(hash256(&bytes))
}

pub fn encode_stack_state(value: &CanonicalStackValue) -> Result<Vec<u8>, ExecutionError> {
    let mut bytes = Vec::new();
    bytes.extend_from_slice(STACK_STATE_MAGIC);
    bytes.extend_from_slice(&1u16.to_le_bytes());
    bytes.extend_from_slice(&0u16.to_le_bytes());
    let mut nodes = 0usize;
    encode_stack_value(value, 0, &mut nodes, &mut bytes)?;
    if bytes.len() > 1024 {
        return Err(ExecutionError::Oversized("canonical event state"));
    }
    Ok(bytes)
}

fn encode_stack_value(
    value: &CanonicalStackValue,
    depth: usize,
    nodes: &mut usize,
    bytes: &mut Vec<u8>,
) -> Result<(), ExecutionError> {
    if depth > 16 {
        return Err(ExecutionError::Oversized("canonical stack depth"));
    }
    *nodes = nodes
        .checked_add(1)
        .ok_or(ExecutionError::Oversized("canonical stack nodes"))?;
    if *nodes > 512 {
        return Err(ExecutionError::Oversized("canonical stack nodes"));
    }
    match value {
        CanonicalStackValue::Null => bytes.push(0x00),
        CanonicalStackValue::Boolean(value) => {
            bytes.push(0x20);
            bytes.push(u8::from(*value));
        }
        CanonicalStackValue::Integer(value) => {
            if normalize_signed_le(value).as_slice() != value.as_slice() {
                return Err(ExecutionError::Invalid("non-canonical stack integer"));
            }
            bytes.push(0x21);
            push_u32(bytes, value.len());
            bytes.extend_from_slice(value);
        }
        CanonicalStackValue::ByteString(value) => {
            bytes.push(0x28);
            push_u32(bytes, value.len());
            bytes.extend_from_slice(value);
        }
        CanonicalStackValue::Buffer(value) => {
            bytes.push(0x30);
            push_u32(bytes, value.len());
            bytes.extend_from_slice(value);
        }
        CanonicalStackValue::Array(items) | CanonicalStackValue::Struct(items) => {
            bytes.push(if matches!(value, CanonicalStackValue::Array(_)) {
                0x40
            } else {
                0x41
            });
            push_u32(bytes, items.len());
            for item in items {
                encode_stack_value(item, depth + 1, nodes, bytes)?;
            }
        }
        CanonicalStackValue::Map(entries) => {
            bytes.push(0x48);
            push_u32(bytes, entries.len());
            for (key, value) in entries {
                encode_stack_value(key, depth + 1, nodes, bytes)?;
                encode_stack_value(value, depth + 1, nodes, bytes)?;
            }
        }
    }
    Ok(())
}

#[must_use]
pub fn normalize_signed_le(input: &[u8]) -> Vec<u8> {
    let mut bytes = input.to_vec();
    while bytes.len() > 1 {
        let last = bytes[bytes.len() - 1];
        let next = bytes[bytes.len() - 2];
        if (last == 0 && next & 0x80 == 0) || (last == 0xff && next & 0x80 != 0) {
            bytes.pop();
        } else {
            break;
        }
    }
    if bytes == [0] {
        bytes.clear();
    }
    bytes
}

#[must_use]
pub fn hash_l1_message(message: &L1Message) -> UInt256 {
    let mut bytes = Vec::with_capacity(61 + message.payload.len());
    bytes.extend_from_slice(&message.source_chain_id.to_le_bytes());
    bytes.extend_from_slice(&message.target_chain_id.to_le_bytes());
    bytes.extend_from_slice(&message.nonce.to_le_bytes());
    bytes.extend_from_slice(&message.sender);
    bytes.extend_from_slice(&message.receiver);
    bytes.push(message.message_type);
    push_u32(&mut bytes, message.payload.len());
    bytes.extend_from_slice(&message.payload);
    hash256(&bytes)
}

#[must_use]
pub fn hash_l1_messages(messages: &[L1Message]) -> UInt256 {
    merkle_root(&messages.iter().map(hash_l1_message).collect::<Vec<_>>())
}

#[must_use]
pub fn hash_block_context(context: &BatchBlockContext) -> UInt256 {
    let mut bytes = Vec::with_capacity(56);
    bytes.extend_from_slice(&context.l1_finalized_height.to_le_bytes());
    bytes.extend_from_slice(&context.first_block_timestamp.to_le_bytes());
    bytes.extend_from_slice(&context.last_block_timestamp.to_le_bytes());
    bytes.extend_from_slice(&context.sequencer_committee_hash);
    bytes.extend_from_slice(&context.network.to_le_bytes());
    hash256(&bytes)
}

#[must_use]
#[allow(clippy::too_many_arguments)]
pub fn hash_public_inputs(
    chain_id: u32,
    batch_number: u64,
    pre_state_root: &UInt256,
    post_state_root: &UInt256,
    tx_root: &UInt256,
    receipt_root: &UInt256,
    withdrawal_root: &UInt256,
    l2_to_l1_message_root: &UInt256,
    l2_to_l2_message_root: &UInt256,
    l1_message_hash: &UInt256,
    da_commitment: &UInt256,
    block_context_hash: &UInt256,
) -> UInt256 {
    let mut bytes = Vec::with_capacity(332);
    bytes.extend_from_slice(&chain_id.to_le_bytes());
    bytes.extend_from_slice(&batch_number.to_le_bytes());
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
        bytes.extend_from_slice(root);
    }
    hash256(&bytes)
}

fn push_optional_bytes(bytes: &mut Vec<u8>, value: Option<&[u8]>) {
    match value {
        Some(value) => {
            bytes.push(1);
            push_u32(bytes, value.len());
            bytes.extend_from_slice(value);
        }
        None => bytes.push(0),
    }
}

pub(crate) fn push_u32(bytes: &mut Vec<u8>, value: usize) {
    let value = u32::try_from(value).unwrap_or(u32::MAX);
    bytes.extend_from_slice(&value.to_le_bytes());
}
