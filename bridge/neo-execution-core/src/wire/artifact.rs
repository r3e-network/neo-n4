use alloc::vec::Vec;

use crate::{
    hashing::{hash_block_context, hash_l1_messages, hash256},
    types::{
        BatchExecutionResult, ExecutionError, ExecutionPayload, ProofWitnessArtifact, PublicInputs,
        UInt256,
    },
};

use super::constants::{
    ARTIFACT_MAGIC, MAX_DA_EVIDENCE_BYTES, MAX_DA_POINTER_BYTES, MAX_EFFECTS_BYTES,
    MAX_EXECUTION_PAYLOAD_BYTES, MAX_PROOF_WITNESS_ARTIFACT_BYTES, MAX_STATE_WITNESS_BYTES,
};
use super::io::{ArtifactlessMagic, Reader, Writer};
use super::payload::{encode_execution_payload, parse_execution_payload};
use super::state::{
    encode_batch_effects, encode_state_witness, parse_batch_effects, parse_state_witness,
};

const CONTENT_HASH_DOMAIN: &[u8] = b"neo-n4/proof-witness/v1\0";

pub fn parse_proof_witness_artifact(bytes: &[u8]) -> Result<ProofWitnessArtifact, ExecutionError> {
    if bytes.len() > MAX_PROOF_WITNESS_ARTIFACT_BYTES {
        return Err(ExecutionError::Oversized("proof witness artifact"));
    }
    if bytes.len() < 32 {
        return Err(ExecutionError::Truncated);
    }
    let body_len = bytes.len() - 32;
    let mut content_bytes = Vec::with_capacity(CONTENT_HASH_DOMAIN.len() + body_len);
    content_bytes.extend_from_slice(CONTENT_HASH_DOMAIN);
    content_bytes.extend_from_slice(&bytes[..body_len]);
    if hash256(&content_bytes) != bytes[body_len..] {
        return Err(ExecutionError::Invalid("proof witness content hash"));
    }

    let mut reader = Reader::new(&bytes[..body_len]);
    reader.require_magic(ARTIFACT_MAGIC, "proof witness magic")?;
    if reader.read_u16()? != 1 {
        return Err(ExecutionError::Invalid("proof witness version"));
    }
    let flags = reader.read_u16()?;
    if flags & !1 != 0 {
        return Err(ExecutionError::Invalid("proof witness flags"));
    }
    let execution_witness_authenticated = flags & 1 != 0;
    let proof_type = reader.read_u8()?;
    if proof_type != 3 {
        return Err(ExecutionError::Invalid("proof type"));
    }
    let proof_system = reader.read_u8()?;
    if !(1..=4).contains(&proof_system) {
        return Err(ExecutionError::Invalid("proof system"));
    }
    if reader.read_fixed::<2>()? != [0u8; 2] {
        return Err(ExecutionError::Invalid("proof witness reserved bytes"));
    }
    if !execution_witness_authenticated {
        return Err(ExecutionError::Invalid("unauthenticated proof witness"));
    }
    let verification_key_id = reader.read_fixed::<32>()?;
    if verification_key_id == [0u8; 32] {
        return Err(ExecutionError::Invalid("zero verification key id"));
    }
    let execution_semantic_id = reader.read_fixed::<32>()?;
    if execution_semantic_id == [0u8; 32] {
        return Err(ExecutionError::Invalid("zero execution semantic id"));
    }
    let chain_id = reader.read_u32()?;
    let batch_number = reader.read_u64()?;
    let first_block = reader.read_u64()?;
    let last_block = reader.read_u64()?;
    if last_block < first_block {
        return Err(ExecutionError::Invalid("artifact block range"));
    }
    let payload_bytes =
        reader.read_length_prefixed(MAX_EXECUTION_PAYLOAD_BYTES, "execution payload")?;
    let execution_payload = parse_execution_payload(&payload_bytes)?;
    let state_witness_bytes =
        reader.read_length_prefixed(MAX_STATE_WITNESS_BYTES, "state witness")?;
    if state_witness_bytes.is_empty() {
        return Err(ExecutionError::Invalid("empty state witness"));
    }
    let state_witness = parse_state_witness(&state_witness_bytes)?;
    let execution_result = read_execution_result(&mut reader)?;
    let effects_bytes = reader.read_length_prefixed(MAX_EFFECTS_BYTES, "batch effects")?;
    if effects_bytes.is_empty() {
        return Err(ExecutionError::Invalid("empty batch effects"));
    }
    let effects = parse_batch_effects(&effects_bytes)?;
    let da_mode = reader.read_u8()?;
    if !matches!(da_mode, 0..=3 | u8::MAX) {
        return Err(ExecutionError::Invalid("DA mode"));
    }
    let da_receipt_kind = reader.read_u8()?;
    if !(1..=6).contains(&da_receipt_kind) {
        return Err(ExecutionError::Invalid("DA receipt kind"));
    }
    if reader.read_fixed::<2>()? != [0u8; 2] {
        return Err(ExecutionError::Invalid("DA receipt reserved bytes"));
    }
    let da_commitment = reader.read_fixed::<32>()?;
    let da_pointer = reader.read_length_prefixed(MAX_DA_POINTER_BYTES, "DA pointer")?;
    if da_pointer.is_empty() {
        return Err(ExecutionError::Invalid("empty DA pointer"));
    }
    let da_evidence = reader.read_length_prefixed(MAX_DA_EVIDENCE_BYTES, "DA evidence")?;
    if da_evidence.is_empty() {
        return Err(ExecutionError::Invalid("empty DA evidence"));
    }
    let public_inputs = read_public_inputs(&mut reader)?;
    reader.ensure_end("proof witness body")?;

    if chain_id != execution_payload.chain_id
        || batch_number != execution_payload.batch_number
        || first_block != execution_payload.first_block
        || last_block != execution_payload.last_block
    {
        return Err(ExecutionError::Invalid("artifact payload identity"));
    }
    if hash256(&payload_bytes) != da_commitment {
        return Err(ExecutionError::Invalid("DA commitment"));
    }
    validate_public_input_claims(
        &execution_payload,
        &execution_result,
        &da_commitment,
        &public_inputs,
    )?;
    if effects.transactions.len() != execution_payload.transactions.len() {
        return Err(ExecutionError::Invalid("batch effects transaction count"));
    }

    Ok(ProofWitnessArtifact {
        proof_type,
        proof_system,
        execution_witness_authenticated,
        verification_key_id,
        execution_semantic_id,
        chain_id,
        batch_number,
        first_block,
        last_block,
        payload_bytes,
        execution_payload,
        state_witness_bytes,
        state_witness,
        execution_result,
        effects_bytes,
        effects,
        da_mode,
        da_receipt_kind,
        da_commitment,
        da_pointer,
        da_evidence,
        public_inputs,
    })
}

pub fn encode_proof_witness_artifact(
    artifact: &ProofWitnessArtifact,
) -> Result<Vec<u8>, ExecutionError> {
    let payload_bytes = encode_execution_payload(&artifact.execution_payload)?;
    let state_witness_bytes = encode_state_witness(&artifact.state_witness)?;
    let effects_bytes = encode_batch_effects(&artifact.effects)?;
    if artifact.proof_type != 3
        || artifact.proof_system == 0
        || artifact.proof_system > 4
        || !artifact.execution_witness_authenticated
        || artifact.verification_key_id == [0u8; 32]
        || artifact.execution_semantic_id == [0u8; 32]
        || artifact.chain_id != artifact.execution_payload.chain_id
        || artifact.batch_number != artifact.execution_payload.batch_number
        || artifact.first_block != artifact.execution_payload.first_block
        || artifact.last_block != artifact.execution_payload.last_block
        || artifact.execution_result.gas_consumed < 0
    {
        return Err(ExecutionError::Invalid("proof witness artifact fields"));
    }
    let da_commitment = hash256(&payload_bytes);
    validate_public_input_claims(
        &artifact.execution_payload,
        &artifact.execution_result,
        &da_commitment,
        &artifact.public_inputs,
    )?;
    if effects_bytes.len() > MAX_EFFECTS_BYTES
        || artifact.da_pointer.len() > MAX_DA_POINTER_BYTES
        || artifact.da_pointer.is_empty()
        || artifact.da_evidence.len() > MAX_DA_EVIDENCE_BYTES
        || artifact.da_evidence.is_empty()
        || !(1..=6).contains(&artifact.da_receipt_kind)
        || !matches!(artifact.da_mode, 0..=3 | u8::MAX)
    {
        return Err(ExecutionError::Invalid("proof witness artifact bounds"));
    }

    let mut writer = Writer::new();
    writer.push(ArtifactlessMagic::Artifact);
    writer.write_u16(1);
    writer.write_u16(1);
    writer.write_u8(artifact.proof_type);
    writer.write_u8(artifact.proof_system);
    writer.write_bytes(&[0u8; 2]);
    writer.write_bytes(&artifact.verification_key_id);
    writer.write_bytes(&artifact.execution_semantic_id);
    writer.write_u32(artifact.chain_id);
    writer.write_u64(artifact.batch_number);
    writer.write_u64(artifact.first_block);
    writer.write_u64(artifact.last_block);
    writer.write_length_prefixed(
        &payload_bytes,
        MAX_EXECUTION_PAYLOAD_BYTES,
        "execution payload",
    )?;
    writer.write_length_prefixed(
        &state_witness_bytes,
        MAX_STATE_WITNESS_BYTES,
        "state witness",
    )?;
    write_execution_result(&mut writer, &artifact.execution_result);
    writer.write_length_prefixed(&effects_bytes, MAX_EFFECTS_BYTES, "batch effects")?;
    writer.write_u8(artifact.da_mode);
    writer.write_u8(artifact.da_receipt_kind);
    writer.write_bytes(&[0u8; 2]);
    writer.write_bytes(&da_commitment);
    writer.write_length_prefixed(&artifact.da_pointer, MAX_DA_POINTER_BYTES, "DA pointer")?;
    writer.write_length_prefixed(&artifact.da_evidence, MAX_DA_EVIDENCE_BYTES, "DA evidence")?;
    write_public_inputs(&mut writer, &artifact.public_inputs);
    let body = writer.finish();
    let mut content_bytes = Vec::with_capacity(CONTENT_HASH_DOMAIN.len() + body.len());
    content_bytes.extend_from_slice(CONTENT_HASH_DOMAIN);
    content_bytes.extend_from_slice(&body);
    let mut encoded = body;
    encoded.extend_from_slice(&hash256(&content_bytes));
    if encoded.len() > MAX_PROOF_WITNESS_ARTIFACT_BYTES {
        return Err(ExecutionError::Oversized("proof witness artifact"));
    }
    Ok(encoded)
}

pub(crate) fn read_execution_result(reader: &mut Reader<'_>) -> Result<BatchExecutionResult, ExecutionError> {
    let result = BatchExecutionResult {
        post_state_root: reader.read_fixed::<32>()?,
        tx_root: reader.read_fixed::<32>()?,
        receipt_root: reader.read_fixed::<32>()?,
        withdrawal_root: reader.read_fixed::<32>()?,
        l2_to_l1_message_root: reader.read_fixed::<32>()?,
        l2_to_l2_message_root: reader.read_fixed::<32>()?,
        gas_consumed: reader.read_i64()?,
    };
    if result.gas_consumed < 0 {
        return Err(ExecutionError::Invalid("negative execution gas"));
    }
    Ok(result)
}

pub(crate) fn write_execution_result(writer: &mut Writer, result: &BatchExecutionResult) {
    writer.write_bytes(&result.post_state_root);
    writer.write_bytes(&result.tx_root);
    writer.write_bytes(&result.receipt_root);
    writer.write_bytes(&result.withdrawal_root);
    writer.write_bytes(&result.l2_to_l1_message_root);
    writer.write_bytes(&result.l2_to_l2_message_root);
    writer.write_i64(result.gas_consumed);
}

fn read_public_inputs(reader: &mut Reader<'_>) -> Result<PublicInputs, ExecutionError> {
    Ok(PublicInputs {
        chain_id: reader.read_u32()?,
        batch_number: reader.read_u64()?,
        first_block: reader.read_u64()?,
        last_block: reader.read_u64()?,
        pre_state_root: reader.read_fixed::<32>()?,
        post_state_root: reader.read_fixed::<32>()?,
        tx_root: reader.read_fixed::<32>()?,
        receipt_root: reader.read_fixed::<32>()?,
        withdrawal_root: reader.read_fixed::<32>()?,
        l2_to_l1_message_root: reader.read_fixed::<32>()?,
        l2_to_l2_message_root: reader.read_fixed::<32>()?,
        l1_message_hash: reader.read_fixed::<32>()?,
        da_commitment: reader.read_fixed::<32>()?,
        block_context_hash: reader.read_fixed::<32>()?,
        forced_inclusion_count: reader.read_u32()?,
    })
}

fn write_public_inputs(writer: &mut Writer, inputs: &PublicInputs) {
    writer.write_u32(inputs.chain_id);
    writer.write_u64(inputs.batch_number);
    writer.write_u64(inputs.first_block);
    writer.write_u64(inputs.last_block);
    writer.write_bytes(&inputs.pre_state_root);
    writer.write_bytes(&inputs.post_state_root);
    writer.write_bytes(&inputs.tx_root);
    writer.write_bytes(&inputs.receipt_root);
    writer.write_bytes(&inputs.withdrawal_root);
    writer.write_bytes(&inputs.l2_to_l1_message_root);
    writer.write_bytes(&inputs.l2_to_l2_message_root);
    writer.write_bytes(&inputs.l1_message_hash);
    writer.write_bytes(&inputs.da_commitment);
    writer.write_bytes(&inputs.block_context_hash);
    writer.write_u32(inputs.forced_inclusion_count);
}

fn validate_public_input_claims(
    payload: &ExecutionPayload,
    result: &BatchExecutionResult,
    da_commitment: &UInt256,
    inputs: &PublicInputs,
) -> Result<(), ExecutionError> {
    if inputs.chain_id != payload.chain_id
        || inputs.batch_number != payload.batch_number
        || inputs.first_block != payload.first_block
        || inputs.last_block != payload.last_block
        || inputs.pre_state_root != payload.pre_state_root
        || inputs.post_state_root != result.post_state_root
        || inputs.tx_root != result.tx_root
        || inputs.receipt_root != result.receipt_root
        || inputs.withdrawal_root != result.withdrawal_root
        || inputs.l2_to_l1_message_root != result.l2_to_l1_message_root
        || inputs.l2_to_l2_message_root != result.l2_to_l2_message_root
        || inputs.l1_message_hash != hash_l1_messages(&payload.l1_messages)
        || inputs.da_commitment != *da_commitment
        || inputs.block_context_hash != hash_block_context(&payload.block_context)
        || u32::try_from(payload.forced_inclusions.len())
            .map(|count| count != inputs.forced_inclusion_count)
            .unwrap_or(true)
    {
        return Err(ExecutionError::Invalid("public input claims"));
    }
    Ok(())
}
