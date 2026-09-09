use alloc::vec::Vec;

use crate::types::{
    BatchBlockContext, ExecutionError, ExecutionPayload, ForcedInclusionProof, L1Message,
};

use super::constants::{
    MAX_EXECUTION_PAYLOAD_BYTES, MAX_FORCED_INCLUSION_SIBLINGS, MAX_MESSAGE_BYTES,
    MAX_PAYLOAD_ITEMS, MAX_PAYLOAD_TRANSACTION_BYTES, PAYLOAD_MAGIC,
};
use super::io::{ArtifactlessMagic, Reader, Writer, require_version_flags};

pub fn parse_execution_payload(bytes: &[u8]) -> Result<ExecutionPayload, ExecutionError> {
    if bytes.len() > MAX_EXECUTION_PAYLOAD_BYTES {
        return Err(ExecutionError::Oversized("execution payload"));
    }
    let mut reader = Reader::new(bytes);
    reader.require_magic(PAYLOAD_MAGIC, "execution payload magic")?;
    require_version_flags(&mut reader, "execution payload")?;
    let chain_id = reader.read_u32()?;
    let batch_number = reader.read_u64()?;
    let first_block = reader.read_u64()?;
    let last_block = reader.read_u64()?;
    if last_block < first_block {
        return Err(ExecutionError::Invalid("payload block range"));
    }
    let pre_state_root = reader.read_fixed::<32>()?;
    let block_context = BatchBlockContext {
        l1_finalized_height: reader.read_u32()?,
        first_block_timestamp: reader.read_u64()?,
        last_block_timestamp: reader.read_u64()?,
        sequencer_committee_hash: reader.read_fixed::<32>()?,
        network: reader.read_u32()?,
    };
    if block_context.last_block_timestamp < block_context.first_block_timestamp {
        return Err(ExecutionError::Invalid("payload timestamp range"));
    }

    let message_count = reader.read_count(MAX_PAYLOAD_ITEMS, "L1 messages")?;
    let mut l1_messages = Vec::with_capacity(message_count);
    for _ in 0..message_count {
        let message_bytes = reader.read_length_prefixed(61 + MAX_MESSAGE_BYTES, "L1 message")?;
        let mut message_reader = Reader::new(&message_bytes);
        let message = L1Message {
            source_chain_id: message_reader.read_u32()?,
            target_chain_id: message_reader.read_u32()?,
            nonce: message_reader.read_u64()?,
            sender: message_reader.read_fixed::<20>()?,
            receiver: message_reader.read_fixed::<20>()?,
            message_type: message_reader.read_u8()?,
            payload: message_reader.read_length_prefixed(MAX_MESSAGE_BYTES, "message payload")?,
        };
        message_reader.ensure_end("L1 message")?;
        if message.source_chain_id != 0
            || message.target_chain_id != chain_id
            || message.message_type > 4
        {
            return Err(ExecutionError::Invalid("L1 message routing"));
        }
        l1_messages.push(message);
    }

    let forced_inclusion_count = reader.read_count(MAX_PAYLOAD_ITEMS, "forced-inclusion proofs")?;
    let mut forced_inclusions = Vec::with_capacity(forced_inclusion_count);
    for _ in 0..forced_inclusion_count {
        let nonce = reader.read_u64()?;
        let leaf_index = reader.read_u32()?;
        let tx_hash = reader.read_fixed::<32>()?;
        let sibling_count =
            reader.read_count(MAX_FORCED_INCLUSION_SIBLINGS, "forced-inclusion siblings")?;
        let mut siblings = Vec::with_capacity(sibling_count);
        for _ in 0..sibling_count {
            siblings.push(reader.read_fixed::<32>()?);
        }
        forced_inclusions.push(ForcedInclusionProof {
            nonce,
            leaf_index,
            tx_hash,
            siblings,
        });
    }

    let transaction_count = reader.read_count(MAX_PAYLOAD_ITEMS, "payload transactions")?;
    if transaction_count == 0 {
        return Err(ExecutionError::Invalid("empty payload transactions"));
    }
    let mut transactions = Vec::with_capacity(transaction_count);
    for _ in 0..transaction_count {
        transactions.push(
            reader.read_length_prefixed(MAX_PAYLOAD_TRANSACTION_BYTES, "payload transaction")?,
        );
    }
    reader.ensure_end("execution payload")?;
    Ok(ExecutionPayload {
        chain_id,
        batch_number,
        first_block,
        last_block,
        pre_state_root,
        block_context,
        l1_messages,
        forced_inclusions,
        transactions,
    })
}

pub fn encode_execution_payload(payload: &ExecutionPayload) -> Result<Vec<u8>, ExecutionError> {
    if payload.last_block < payload.first_block
        || payload.block_context.last_block_timestamp < payload.block_context.first_block_timestamp
        || payload.transactions.is_empty()
    {
        return Err(ExecutionError::Invalid("execution payload fields"));
    }
    let mut writer = Writer::new();
    writer.push(ArtifactlessMagic::Payload);
    writer.write_version_flags();
    writer.write_u32(payload.chain_id);
    writer.write_u64(payload.batch_number);
    writer.write_u64(payload.first_block);
    writer.write_u64(payload.last_block);
    writer.write_bytes(&payload.pre_state_root);
    writer.write_u32(payload.block_context.l1_finalized_height);
    writer.write_u64(payload.block_context.first_block_timestamp);
    writer.write_u64(payload.block_context.last_block_timestamp);
    writer.write_bytes(&payload.block_context.sequencer_committee_hash);
    writer.write_u32(payload.block_context.network);
    writer.write_count(payload.l1_messages.len(), MAX_PAYLOAD_ITEMS, "L1 messages")?;
    for message in &payload.l1_messages {
        if message.source_chain_id != 0
            || message.target_chain_id != payload.chain_id
            || message.message_type > 4
        {
            return Err(ExecutionError::Invalid("L1 message routing"));
        }
        let mut message_writer = Writer::new();
        message_writer.write_u32(message.source_chain_id);
        message_writer.write_u32(message.target_chain_id);
        message_writer.write_u64(message.nonce);
        message_writer.write_bytes(&message.sender);
        message_writer.write_bytes(&message.receiver);
        message_writer.write_u8(message.message_type);
        message_writer.write_length_prefixed(
            &message.payload,
            MAX_MESSAGE_BYTES,
            "message payload",
        )?;
        writer.write_length_prefixed(
            &message_writer.finish(),
            61 + MAX_MESSAGE_BYTES,
            "L1 message",
        )?;
    }
    writer.write_count(
        payload.forced_inclusions.len(),
        MAX_PAYLOAD_ITEMS,
        "forced-inclusion proofs",
    )?;
    for proof in &payload.forced_inclusions {
        writer.write_u64(proof.nonce);
        writer.write_u32(proof.leaf_index);
        writer.write_bytes(&proof.tx_hash);
        writer.write_count(
            proof.siblings.len(),
            MAX_FORCED_INCLUSION_SIBLINGS,
            "forced-inclusion siblings",
        )?;
        for sibling in &proof.siblings {
            writer.write_bytes(sibling);
        }
    }
    writer.write_count(
        payload.transactions.len(),
        MAX_PAYLOAD_ITEMS,
        "payload transactions",
    )?;
    for transaction in &payload.transactions {
        writer.write_length_prefixed(
            transaction,
            MAX_PAYLOAD_TRANSACTION_BYTES,
            "payload transaction",
        )?;
    }
    let bytes = writer.finish();
    if bytes.len() > MAX_EXECUTION_PAYLOAD_BYTES {
        return Err(ExecutionError::Oversized("execution payload"));
    }
    Ok(bytes)
}
