use alloc::vec::Vec;

use crate::{
    hashing::{hash256, keyed_state_root},
    types::{ExecutionError, NativeExecutionOutputV1, SP1_STATEFUL_NEO_VM_V1_EXECUTION_SEMANTIC_ID},
};

use super::{
    artifact::{read_execution_result, write_execution_result},
    constants::{
        MAX_EFFECTS_BYTES, MAX_NATIVE_EXECUTION_OUTPUT_BYTES, MAX_STATE_WITNESS_BYTES,
        NATIVE_EXECUTION_OUTPUT_MAGIC,
    },
    io::{require_version_flags, Reader, Writer},
    state::{encode_batch_effects, encode_state_witness, parse_batch_effects, parse_state_witness},
};

const NATIVE_EXECUTION_OUTPUT_HASH_DOMAIN: &[u8] = b"neo-n4/native-execution-output/v1\0";

pub fn parse_native_execution_output(
    bytes: &[u8],
) -> Result<NativeExecutionOutputV1, ExecutionError> {
    if bytes.len() > MAX_NATIVE_EXECUTION_OUTPUT_BYTES {
        return Err(ExecutionError::Oversized("native execution output"));
    }
    if bytes.len() < 32 {
        return Err(ExecutionError::Truncated);
    }
    let body_len = bytes.len() - 32;
    let mut content_bytes =
        Vec::with_capacity(NATIVE_EXECUTION_OUTPUT_HASH_DOMAIN.len() + body_len);
    content_bytes.extend_from_slice(NATIVE_EXECUTION_OUTPUT_HASH_DOMAIN);
    content_bytes.extend_from_slice(&bytes[..body_len]);
    if hash256(&content_bytes) != bytes[body_len..] {
        return Err(ExecutionError::Invalid(
            "native execution output content hash",
        ));
    }

    let mut reader = Reader::new(&bytes[..body_len]);
    reader.require_magic(
        NATIVE_EXECUTION_OUTPUT_MAGIC,
        "native execution output magic",
    )?;
    require_version_flags(&mut reader, "native execution output")?;
    let request_payload_hash = reader.read_fixed::<32>()?;
    let request_state_witness_hash = reader.read_fixed::<32>()?;
    let execution_semantic_id = reader.read_fixed::<32>()?;
    if execution_semantic_id != SP1_STATEFUL_NEO_VM_V1_EXECUTION_SEMANTIC_ID {
        return Err(ExecutionError::Invalid(
            "native execution output semantic id",
        ));
    }
    let execution_result = read_execution_result(&mut reader)?;
    let effects_bytes = reader.read_length_prefixed(MAX_EFFECTS_BYTES, "batch effects")?;
    let post_state_witness_bytes =
        reader.read_length_prefixed(MAX_STATE_WITNESS_BYTES, "post-state witness")?;
    let public_input_hash = reader.read_fixed::<32>()?;
    reader.ensure_end("native execution output body")?;

    let effects = parse_batch_effects(&effects_bytes)?;
    if encode_batch_effects(&effects)? != effects_bytes {
        return Err(ExecutionError::Invalid(
            "non-canonical native execution effects",
        ));
    }
    let post_state_witness = parse_state_witness(&post_state_witness_bytes)?;
    if encode_state_witness(&post_state_witness)? != post_state_witness_bytes
        || keyed_state_root(&post_state_witness.entries) != execution_result.post_state_root
    {
        return Err(ExecutionError::Invalid(
            "native execution post-state witness",
        ));
    }

    Ok(NativeExecutionOutputV1 {
        request_payload_hash,
        request_state_witness_hash,
        execution_semantic_id,
        execution_result,
        effects_bytes,
        post_state_witness_bytes,
        public_input_hash,
    })
}

pub fn encode_native_execution_output(
    output: &NativeExecutionOutputV1,
) -> Result<Vec<u8>, ExecutionError> {
    if output.execution_semantic_id != SP1_STATEFUL_NEO_VM_V1_EXECUTION_SEMANTIC_ID
        || output.execution_result.gas_consumed < 0
    {
        return Err(ExecutionError::Invalid("native execution output fields"));
    }
    let effects = parse_batch_effects(&output.effects_bytes)?;
    if encode_batch_effects(&effects)? != output.effects_bytes {
        return Err(ExecutionError::Invalid(
            "non-canonical native execution effects",
        ));
    }
    let post_state_witness = parse_state_witness(&output.post_state_witness_bytes)?;
    if encode_state_witness(&post_state_witness)? != output.post_state_witness_bytes
        || keyed_state_root(&post_state_witness.entries) != output.execution_result.post_state_root
    {
        return Err(ExecutionError::Invalid(
            "native execution post-state witness",
        ));
    }

    let mut writer = Writer::new();
    writer.write_bytes(NATIVE_EXECUTION_OUTPUT_MAGIC);
    writer.write_version_flags();
    writer.write_bytes(&output.request_payload_hash);
    writer.write_bytes(&output.request_state_witness_hash);
    writer.write_bytes(&output.execution_semantic_id);
    write_execution_result(&mut writer, &output.execution_result);
    writer.write_length_prefixed(&output.effects_bytes, MAX_EFFECTS_BYTES, "batch effects")?;
    writer.write_length_prefixed(
        &output.post_state_witness_bytes,
        MAX_STATE_WITNESS_BYTES,
        "post-state witness",
    )?;
    writer.write_bytes(&output.public_input_hash);
    let body = writer.finish();
    let mut content_bytes =
        Vec::with_capacity(NATIVE_EXECUTION_OUTPUT_HASH_DOMAIN.len() + body.len());
    content_bytes.extend_from_slice(NATIVE_EXECUTION_OUTPUT_HASH_DOMAIN);
    content_bytes.extend_from_slice(&body);
    let mut encoded = body;
    encoded.extend_from_slice(&hash256(&content_bytes));
    if encoded.len() > MAX_NATIVE_EXECUTION_OUTPUT_BYTES {
        return Err(ExecutionError::Oversized("native execution output"));
    }
    Ok(encoded)
}
