use alloc::{string::ToString, vec::Vec};

use crate::{
    hashing::{
        contract_binding_hash, contract_binding_key, encode_receipt, encode_stack_state,
        hash_block_context, hash_l1_messages, hash256,
    },
    manifest::parse_contract_manifest,
    types::{
        BatchBlockContext, BatchEffects, BatchExecutionResult, CanonicalReceiptV1,
        CanonicalStackValue, ContractWitness, DEFAULT_ADDRESS_VERSION, DEFAULT_EXEC_FEE_FACTOR,
        DEFAULT_PER_TX_GAS_LIMIT, DEFAULT_STORAGE_PRICE, ExecutionError, ExecutionEvent,
        ExecutionPayload, L1Message, ProofWitnessArtifact, ProtocolConfig, PublicInputs,
        StateEntry, StateWitness, StorageDelta, StorageOperation, TransactionEffects, UInt256,
    },
};

const ARTIFACT_MAGIC: &[u8; 8] = b"NEO4PWIT";
const PAYLOAD_MAGIC: &[u8; 8] = b"NEO4EXEC";
const STATE_WITNESS_MAGIC: &[u8; 8] = b"NEO4STW1";
const EFFECTS_MAGIC: &[u8; 8] = b"NEO4EFX1";
const CONTENT_HASH_DOMAIN: &[u8] = b"neo-n4/proof-witness/v1\0";

const MAX_ARTIFACT_BYTES: usize = 256 * 1024 * 1024;
const MAX_PAYLOAD_BYTES: usize = 64 * 1024 * 1024;
const MAX_STATE_WITNESS_BYTES: usize = 128 * 1024 * 1024;
const MAX_EFFECTS_BYTES: usize = 64 * 1024 * 1024;
const MAX_DA_POINTER_BYTES: usize = 1024 * 1024;
const MAX_MESSAGE_BYTES: usize = 4 * 1024 * 1024;
const MAX_PAYLOAD_TRANSACTION_BYTES: usize = 16 * 1024 * 1024;
const MAX_PAYLOAD_ITEMS: usize = 1_000_000;
const MAX_STATE_ENTRIES: usize = 65_536;
const MAX_STATE_KEY_BYTES: usize = 4096;
const MAX_STATE_VALUE_BYTES: usize = 1024 * 1024;
const MAX_CONTRACTS: usize = 4096;
const MAX_CONTRACT_SCRIPT_BYTES: usize = 1024 * 1024;
const MAX_CONTRACT_MANIFEST_BYTES: usize = u16::MAX as usize;
const MAX_EFFECT_TRANSACTIONS: usize = 65_536;
const MAX_DELTAS_PER_TRANSACTION: usize = 65_536;
const MAX_EVENTS_PER_TRANSACTION: usize = 512;

pub fn parse_proof_witness_artifact(bytes: &[u8]) -> Result<ProofWitnessArtifact, ExecutionError> {
    if bytes.len() > MAX_ARTIFACT_BYTES {
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
    require_version_flags(&mut reader, "proof witness")?;
    let proof_system = reader.read_u8()?;
    if !(1..=4).contains(&proof_system) {
        return Err(ExecutionError::Invalid("proof system"));
    }
    if reader.read_fixed::<3>()? != [0u8; 3] {
        return Err(ExecutionError::Invalid("proof witness reserved bytes"));
    }
    let verification_key_id = reader.read_fixed::<32>()?;
    if verification_key_id == [0u8; 32] {
        return Err(ExecutionError::Invalid("zero verification key id"));
    }
    let chain_id = reader.read_u32()?;
    let batch_number = reader.read_u64()?;
    let first_block = reader.read_u64()?;
    let last_block = reader.read_u64()?;
    if last_block < first_block {
        return Err(ExecutionError::Invalid("artifact block range"));
    }
    let payload_bytes = reader.read_length_prefixed(MAX_PAYLOAD_BYTES, "execution payload")?;
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
    let da_commitment = reader.read_fixed::<32>()?;
    let da_pointer = reader.read_length_prefixed(MAX_DA_POINTER_BYTES, "DA pointer")?;
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
        proof_system,
        verification_key_id,
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
        da_commitment,
        da_pointer,
        public_inputs,
    })
}

pub fn parse_execution_payload(bytes: &[u8]) -> Result<ExecutionPayload, ExecutionError> {
    if bytes.len() > MAX_PAYLOAD_BYTES {
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
        transactions,
    })
}

pub fn parse_state_witness(bytes: &[u8]) -> Result<StateWitness, ExecutionError> {
    if bytes.is_empty() || bytes.len() > MAX_STATE_WITNESS_BYTES {
        return Err(ExecutionError::Invalid("state witness size"));
    }
    let mut reader = Reader::new(bytes);
    reader.require_magic(STATE_WITNESS_MAGIC, "state witness magic")?;
    require_version_flags(&mut reader, "state witness")?;
    let config = ProtocolConfig {
        exec_fee_factor: reader.read_u32()?,
        storage_price: reader.read_u32()?,
        address_version: reader.read_u8()?,
        per_tx_gas_limit: {
            if reader.read_fixed::<3>()? != [0u8; 3] {
                return Err(ExecutionError::Invalid("state witness reserved bytes"));
            }
            reader.read_i64()?
        },
    };
    if config.exec_fee_factor != DEFAULT_EXEC_FEE_FACTOR
        || config.storage_price != DEFAULT_STORAGE_PRICE
        || config.address_version != DEFAULT_ADDRESS_VERSION
        || config.per_tx_gas_limit != DEFAULT_PER_TX_GAS_LIMIT
    {
        return Err(ExecutionError::Invalid(
            "N4 genesis V1 protocol configuration",
        ));
    }

    let entry_count = reader.read_count(MAX_STATE_ENTRIES, "state entries")?;
    if entry_count == 0 {
        return Err(ExecutionError::Invalid("empty pre-state witness"));
    }
    let mut entries = Vec::with_capacity(entry_count);
    for _ in 0..entry_count {
        let key = reader.read_length_prefixed(MAX_STATE_KEY_BYTES, "state key")?;
        if key.is_empty() {
            return Err(ExecutionError::Invalid("empty state key"));
        }
        let value = reader.read_length_prefixed(MAX_STATE_VALUE_BYTES, "state value")?;
        if entries
            .last()
            .is_some_and(|entry: &StateEntry| entry.key >= key)
        {
            return Err(ExecutionError::Invalid("state entry ordering"));
        }
        entries.push(StateEntry { key, value });
    }

    let contract_count = reader.read_count(MAX_CONTRACTS, "contract witnesses")?;
    if contract_count == 0 {
        return Err(ExecutionError::Invalid("empty contract witness"));
    }
    let mut contracts = Vec::with_capacity(contract_count);
    for _ in 0..contract_count {
        let id = reader.read_i32()?;
        let hash = reader.read_fixed::<20>()?;
        let script = reader.read_length_prefixed(MAX_CONTRACT_SCRIPT_BYTES, "contract script")?;
        if script.is_empty() {
            return Err(ExecutionError::Invalid("empty contract script"));
        }
        let manifest_bytes =
            reader.read_length_prefixed(MAX_CONTRACT_MANIFEST_BYTES, "contract manifest")?;
        let manifest = parse_contract_manifest(&manifest_bytes, script.len())?;
        if contracts
            .iter()
            .any(|existing: &ContractWitness| existing.id == id || existing.hash == hash)
        {
            return Err(ExecutionError::Invalid("duplicate contract witness"));
        }
        contracts.push(ContractWitness {
            id,
            hash,
            script,
            manifest_bytes,
            manifest,
        });
    }
    reader.ensure_end("state witness")?;
    contracts.sort_by_key(|contract| contract.hash);

    for contract in &contracts {
        let key = contract_binding_key(&contract.hash);
        let expected = contract_binding_hash(
            contract.id,
            &contract.hash,
            &contract.script,
            &contract.manifest_bytes,
        );
        let actual = entries
            .binary_search_by(|entry| entry.key.as_slice().cmp(&key))
            .ok()
            .map(|index| entries[index].value.as_slice());
        if actual != Some(expected.as_slice()) {
            return Err(ExecutionError::Invalid("contract binding state entry"));
        }
    }
    Ok(StateWitness {
        config,
        entries,
        contracts,
    })
}

pub fn parse_batch_effects(bytes: &[u8]) -> Result<BatchEffects, ExecutionError> {
    if bytes.is_empty() || bytes.len() > MAX_EFFECTS_BYTES {
        return Err(ExecutionError::Invalid("batch effects size"));
    }
    let mut reader = Reader::new(bytes);
    reader.require_magic(EFFECTS_MAGIC, "batch effects magic")?;
    require_version_flags(&mut reader, "batch effects")?;
    let transaction_count = reader.read_count(MAX_EFFECT_TRANSACTIONS, "effect transactions")?;
    if transaction_count == 0 {
        return Err(ExecutionError::Invalid("empty batch effects"));
    }
    let mut transactions = Vec::with_capacity(transaction_count);
    for _ in 0..transaction_count {
        let receipt = read_receipt(&mut reader)?;
        let delta_count = reader.read_count(MAX_DELTAS_PER_TRANSACTION, "storage deltas")?;
        let mut storage_deltas = Vec::with_capacity(delta_count);
        for _ in 0..delta_count {
            let key = reader.read_length_prefixed(MAX_STATE_KEY_BYTES, "storage delta key")?;
            if key.is_empty()
                || storage_deltas
                    .last()
                    .is_some_and(|delta: &StorageDelta| delta.key >= key)
            {
                return Err(ExecutionError::Invalid("storage delta ordering"));
            }
            let operation = match reader.read_u8()? {
                1 => StorageOperation::Add,
                2 => StorageOperation::Update,
                3 => StorageOperation::Delete,
                _ => return Err(ExecutionError::Invalid("storage operation")),
            };
            let old_value = reader.read_optional_bytes(MAX_STATE_VALUE_BYTES, "old value")?;
            let new_value = reader.read_optional_bytes(MAX_STATE_VALUE_BYTES, "new value")?;
            if !valid_storage_transition(operation, &old_value, &new_value) {
                return Err(ExecutionError::Invalid("storage delta transition"));
            }
            storage_deltas.push(StorageDelta {
                key,
                operation,
                old_value,
                new_value,
            });
        }
        let event_count = reader.read_count(MAX_EVENTS_PER_TRANSACTION, "execution events")?;
        let mut events = Vec::with_capacity(event_count);
        for _ in 0..event_count {
            let script_hash = reader.read_fixed::<20>()?;
            let name_bytes = reader.read_length_prefixed(32, "event name")?;
            let name = core::str::from_utf8(&name_bytes)
                .map_err(|_| ExecutionError::Invalid("event name UTF-8"))?
                .to_string();
            if name.is_empty() {
                return Err(ExecutionError::Invalid("empty event name"));
            }
            let state_bytes = reader.read_length_prefixed(1024, "canonical event state")?;
            let state = decode_stack_state(&state_bytes)?;
            events.push(ExecutionEvent {
                script_hash,
                name,
                state,
            });
        }
        if !receipt.success && (!storage_deltas.is_empty() || !events.is_empty()) {
            return Err(ExecutionError::Invalid("FAULT transaction effects"));
        }
        transactions.push(TransactionEffects {
            receipt,
            storage_deltas,
            events,
        });
    }
    reader.ensure_end("batch effects")?;
    Ok(BatchEffects { transactions })
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
    if bytes.len() > MAX_PAYLOAD_BYTES {
        return Err(ExecutionError::Oversized("execution payload"));
    }
    Ok(bytes)
}

pub fn encode_state_witness(witness: &StateWitness) -> Result<Vec<u8>, ExecutionError> {
    if witness.config.exec_fee_factor != DEFAULT_EXEC_FEE_FACTOR
        || witness.config.storage_price != DEFAULT_STORAGE_PRICE
        || witness.config.address_version != DEFAULT_ADDRESS_VERSION
        || witness.config.per_tx_gas_limit != DEFAULT_PER_TX_GAS_LIMIT
        || witness.entries.is_empty()
        || witness.contracts.is_empty()
    {
        return Err(ExecutionError::Invalid("state witness fields"));
    }
    let mut writer = Writer::new();
    writer.push(ArtifactlessMagic::StateWitness);
    writer.write_version_flags();
    writer.write_u32(witness.config.exec_fee_factor);
    writer.write_u32(witness.config.storage_price);
    writer.write_u8(witness.config.address_version);
    writer.write_bytes(&[0u8; 3]);
    writer.write_i64(witness.config.per_tx_gas_limit);
    writer.write_count(witness.entries.len(), MAX_STATE_ENTRIES, "state entries")?;
    let mut previous_key: Option<&[u8]> = None;
    for entry in &witness.entries {
        if entry.key.is_empty()
            || previous_key.is_some_and(|previous| previous >= entry.key.as_slice())
        {
            return Err(ExecutionError::Invalid("state entry ordering"));
        }
        writer.write_length_prefixed(&entry.key, MAX_STATE_KEY_BYTES, "state key")?;
        writer.write_length_prefixed(&entry.value, MAX_STATE_VALUE_BYTES, "state value")?;
        previous_key = Some(&entry.key);
    }
    writer.write_count(witness.contracts.len(), MAX_CONTRACTS, "contract witnesses")?;
    for contract in &witness.contracts {
        writer.write_i32(contract.id);
        writer.write_bytes(&contract.hash);
        writer.write_length_prefixed(
            &contract.script,
            MAX_CONTRACT_SCRIPT_BYTES,
            "contract script",
        )?;
        writer.write_length_prefixed(
            &contract.manifest_bytes,
            MAX_CONTRACT_MANIFEST_BYTES,
            "contract manifest",
        )?;
    }
    let bytes = writer.finish();
    let parsed = parse_state_witness(&bytes)?;
    if parsed != *witness {
        return Err(ExecutionError::Invalid("non-canonical state witness model"));
    }
    Ok(bytes)
}

pub fn encode_batch_effects(effects: &BatchEffects) -> Result<Vec<u8>, ExecutionError> {
    if effects.transactions.is_empty() {
        return Err(ExecutionError::Invalid("empty batch effects"));
    }
    let mut writer = Writer::new();
    writer.push(ArtifactlessMagic::Effects);
    writer.write_version_flags();
    writer.write_count(
        effects.transactions.len(),
        MAX_EFFECT_TRANSACTIONS,
        "effect transactions",
    )?;
    for transaction in &effects.transactions {
        writer.write_bytes(&encode_receipt(&transaction.receipt));
        writer.write_count(
            transaction.storage_deltas.len(),
            MAX_DELTAS_PER_TRANSACTION,
            "storage deltas",
        )?;
        let mut previous_key: Option<&[u8]> = None;
        for delta in &transaction.storage_deltas {
            if delta.key.is_empty()
                || previous_key.is_some_and(|previous| previous >= delta.key.as_slice())
                || !valid_storage_transition(delta.operation, &delta.old_value, &delta.new_value)
            {
                return Err(ExecutionError::Invalid("storage delta"));
            }
            writer.write_length_prefixed(&delta.key, MAX_STATE_KEY_BYTES, "storage delta key")?;
            writer.write_u8(delta.operation as u8);
            writer.write_optional_bytes(&delta.old_value, MAX_STATE_VALUE_BYTES, "old value")?;
            writer.write_optional_bytes(&delta.new_value, MAX_STATE_VALUE_BYTES, "new value")?;
            previous_key = Some(&delta.key);
        }
        writer.write_count(
            transaction.events.len(),
            MAX_EVENTS_PER_TRANSACTION,
            "execution events",
        )?;
        for event in &transaction.events {
            if event.name.is_empty() || event.name.len() > 32 {
                return Err(ExecutionError::Invalid("event name"));
            }
            writer.write_bytes(&event.script_hash);
            writer.write_length_prefixed(event.name.as_bytes(), 32, "event name")?;
            let state = encode_stack_state(&event.state)?;
            writer.write_length_prefixed(&state, 1024, "canonical event state")?;
        }
        if !transaction.receipt.success
            && (!transaction.storage_deltas.is_empty() || !transaction.events.is_empty())
        {
            return Err(ExecutionError::Invalid("FAULT transaction effects"));
        }
    }
    let bytes = writer.finish();
    if bytes.len() > MAX_EFFECTS_BYTES {
        return Err(ExecutionError::Oversized("batch effects"));
    }
    Ok(bytes)
}

pub fn encode_proof_witness_artifact(
    artifact: &ProofWitnessArtifact,
) -> Result<Vec<u8>, ExecutionError> {
    let payload_bytes = encode_execution_payload(&artifact.execution_payload)?;
    let state_witness_bytes = encode_state_witness(&artifact.state_witness)?;
    let effects_bytes = encode_batch_effects(&artifact.effects)?;
    if artifact.proof_system == 0
        || artifact.proof_system > 4
        || artifact.verification_key_id == [0u8; 32]
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
        || !matches!(artifact.da_mode, 0..=3 | u8::MAX)
    {
        return Err(ExecutionError::Invalid("proof witness artifact bounds"));
    }

    let mut writer = Writer::new();
    writer.push(ArtifactlessMagic::Artifact);
    writer.write_version_flags();
    writer.write_u8(artifact.proof_system);
    writer.write_bytes(&[0u8; 3]);
    writer.write_bytes(&artifact.verification_key_id);
    writer.write_u32(artifact.chain_id);
    writer.write_u64(artifact.batch_number);
    writer.write_u64(artifact.first_block);
    writer.write_u64(artifact.last_block);
    writer.write_length_prefixed(&payload_bytes, MAX_PAYLOAD_BYTES, "execution payload")?;
    writer.write_length_prefixed(
        &state_witness_bytes,
        MAX_STATE_WITNESS_BYTES,
        "state witness",
    )?;
    write_execution_result(&mut writer, &artifact.execution_result);
    writer.write_length_prefixed(&effects_bytes, MAX_EFFECTS_BYTES, "batch effects")?;
    writer.write_u8(artifact.da_mode);
    writer.write_bytes(&da_commitment);
    writer.write_length_prefixed(&artifact.da_pointer, MAX_DA_POINTER_BYTES, "DA pointer")?;
    write_public_inputs(&mut writer, &artifact.public_inputs);
    let body = writer.finish();
    let mut content_bytes = Vec::with_capacity(CONTENT_HASH_DOMAIN.len() + body.len());
    content_bytes.extend_from_slice(CONTENT_HASH_DOMAIN);
    content_bytes.extend_from_slice(&body);
    let mut encoded = body;
    encoded.extend_from_slice(&hash256(&content_bytes));
    if encoded.len() > MAX_ARTIFACT_BYTES {
        return Err(ExecutionError::Oversized("proof witness artifact"));
    }
    Ok(encoded)
}

fn read_execution_result(reader: &mut Reader<'_>) -> Result<BatchExecutionResult, ExecutionError> {
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

fn write_execution_result(writer: &mut Writer, result: &BatchExecutionResult) {
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
    })
}

fn write_public_inputs(writer: &mut Writer, inputs: &PublicInputs) {
    writer.write_u32(inputs.chain_id);
    writer.write_u64(inputs.batch_number);
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
}

fn validate_public_input_claims(
    payload: &ExecutionPayload,
    result: &BatchExecutionResult,
    da_commitment: &UInt256,
    inputs: &PublicInputs,
) -> Result<(), ExecutionError> {
    if inputs.chain_id != payload.chain_id
        || inputs.batch_number != payload.batch_number
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
    {
        return Err(ExecutionError::Invalid("public input claims"));
    }
    Ok(())
}

fn read_receipt(reader: &mut Reader<'_>) -> Result<CanonicalReceiptV1, ExecutionError> {
    let tx_hash = reader.read_fixed::<32>()?;
    let success = match reader.read_u8()? {
        0 => false,
        1 => true,
        _ => return Err(ExecutionError::Invalid("receipt success byte")),
    };
    let gas_consumed = reader.read_i64()?;
    if gas_consumed < 0 {
        return Err(ExecutionError::Invalid("receipt gas"));
    }
    Ok(CanonicalReceiptV1 {
        tx_hash,
        success,
        gas_consumed,
        storage_delta_hash: reader.read_fixed::<32>()?,
        events_hash: reader.read_fixed::<32>()?,
    })
}

fn valid_storage_transition(
    operation: StorageOperation,
    old_value: &Option<Vec<u8>>,
    new_value: &Option<Vec<u8>>,
) -> bool {
    matches!(
        (operation, old_value.is_some(), new_value.is_some()),
        (StorageOperation::Add, false, true)
            | (StorageOperation::Update, true, true)
            | (StorageOperation::Delete, true, false)
    )
}

fn decode_stack_state(bytes: &[u8]) -> Result<CanonicalStackValue, ExecutionError> {
    let mut reader = Reader::new(bytes);
    reader.require_magic(b"NEO4STK1", "canonical stack state magic")?;
    require_version_flags(&mut reader, "canonical stack state")?;
    let mut nodes = 0usize;
    let value = decode_stack_value(&mut reader, 0, &mut nodes)?;
    reader.ensure_end("canonical stack state")?;
    if encode_stack_state(&value)? != bytes {
        return Err(ExecutionError::Invalid("non-canonical stack state"));
    }
    Ok(value)
}

fn decode_stack_value(
    reader: &mut Reader<'_>,
    depth: usize,
    nodes: &mut usize,
) -> Result<CanonicalStackValue, ExecutionError> {
    if depth > 16 {
        return Err(ExecutionError::Oversized("canonical stack depth"));
    }
    *nodes = nodes
        .checked_add(1)
        .ok_or(ExecutionError::Oversized("canonical stack nodes"))?;
    if *nodes > 512 {
        return Err(ExecutionError::Oversized("canonical stack nodes"));
    }
    match reader.read_u8()? {
        0x00 => Ok(CanonicalStackValue::Null),
        0x20 => match reader.read_u8()? {
            0 => Ok(CanonicalStackValue::Boolean(false)),
            1 => Ok(CanonicalStackValue::Boolean(true)),
            _ => Err(ExecutionError::Invalid("canonical stack boolean")),
        },
        0x21 => Ok(CanonicalStackValue::Integer(
            reader.read_length_prefixed(32, "canonical stack integer")?,
        )),
        0x28 => Ok(CanonicalStackValue::ByteString(
            reader.read_length_prefixed(1024, "canonical byte string")?,
        )),
        0x30 => Ok(CanonicalStackValue::Buffer(
            reader.read_length_prefixed(1024, "canonical buffer")?,
        )),
        tag @ (0x40 | 0x41) => {
            let count = reader.read_count(512, "canonical stack sequence")?;
            let mut items = Vec::with_capacity(count);
            for _ in 0..count {
                items.push(decode_stack_value(reader, depth + 1, nodes)?);
            }
            if tag == 0x40 {
                Ok(CanonicalStackValue::Array(items))
            } else {
                Ok(CanonicalStackValue::Struct(items))
            }
        }
        0x48 => {
            let count = reader.read_count(512, "canonical stack map")?;
            let mut entries = Vec::with_capacity(count);
            for _ in 0..count {
                let key = decode_stack_value(reader, depth + 1, nodes)?;
                let value = decode_stack_value(reader, depth + 1, nodes)?;
                entries.push((key, value));
            }
            Ok(CanonicalStackValue::Map(entries))
        }
        _ => Err(ExecutionError::Invalid("canonical stack type")),
    }
}

fn require_version_flags(
    reader: &mut Reader<'_>,
    field: &'static str,
) -> Result<(), ExecutionError> {
    if reader.read_u16()? != 1 || reader.read_u16()? != 0 {
        return Err(ExecutionError::Invalid(field));
    }
    Ok(())
}

struct Reader<'a> {
    bytes: &'a [u8],
    position: usize,
}

impl<'a> Reader<'a> {
    fn new(bytes: &'a [u8]) -> Self {
        Self { bytes, position: 0 }
    }

    fn read_u8(&mut self) -> Result<u8, ExecutionError> {
        let value = *self
            .bytes
            .get(self.position)
            .ok_or(ExecutionError::Truncated)?;
        self.position += 1;
        Ok(value)
    }

    fn read_u16(&mut self) -> Result<u16, ExecutionError> {
        Ok(u16::from_le_bytes(self.read_fixed::<2>()?))
    }

    fn read_u32(&mut self) -> Result<u32, ExecutionError> {
        Ok(u32::from_le_bytes(self.read_fixed::<4>()?))
    }

    fn read_i32(&mut self) -> Result<i32, ExecutionError> {
        Ok(i32::from_le_bytes(self.read_fixed::<4>()?))
    }

    fn read_u64(&mut self) -> Result<u64, ExecutionError> {
        Ok(u64::from_le_bytes(self.read_fixed::<8>()?))
    }

    fn read_i64(&mut self) -> Result<i64, ExecutionError> {
        Ok(i64::from_le_bytes(self.read_fixed::<8>()?))
    }

    fn read_fixed<const N: usize>(&mut self) -> Result<[u8; N], ExecutionError> {
        let end = self
            .position
            .checked_add(N)
            .ok_or(ExecutionError::Truncated)?;
        let value = self
            .bytes
            .get(self.position..end)
            .ok_or(ExecutionError::Truncated)?
            .try_into()
            .map_err(|_| ExecutionError::Truncated)?;
        self.position = end;
        Ok(value)
    }

    fn require_magic(
        &mut self,
        magic: &[u8; 8],
        field: &'static str,
    ) -> Result<(), ExecutionError> {
        if self.read_fixed::<8>()? != *magic {
            return Err(ExecutionError::Invalid(field));
        }
        Ok(())
    }

    fn read_count(&mut self, maximum: usize, field: &'static str) -> Result<usize, ExecutionError> {
        let value =
            usize::try_from(self.read_u32()?).map_err(|_| ExecutionError::Oversized(field))?;
        if value > maximum {
            return Err(ExecutionError::Oversized(field));
        }
        Ok(value)
    }

    fn read_length_prefixed(
        &mut self,
        maximum: usize,
        field: &'static str,
    ) -> Result<Vec<u8>, ExecutionError> {
        let length = self.read_count(maximum, field)?;
        let end = self
            .position
            .checked_add(length)
            .ok_or(ExecutionError::Truncated)?;
        let value = self
            .bytes
            .get(self.position..end)
            .ok_or(ExecutionError::Truncated)?
            .to_vec();
        self.position = end;
        Ok(value)
    }

    fn read_optional_bytes(
        &mut self,
        maximum: usize,
        field: &'static str,
    ) -> Result<Option<Vec<u8>>, ExecutionError> {
        match self.read_u8()? {
            0 => Ok(None),
            1 => Ok(Some(self.read_length_prefixed(maximum, field)?)),
            _ => Err(ExecutionError::Invalid("optional byte presence")),
        }
    }

    fn ensure_end(&self, field: &'static str) -> Result<(), ExecutionError> {
        if self.position == self.bytes.len() {
            Ok(())
        } else {
            Err(ExecutionError::Invalid(field))
        }
    }
}

enum ArtifactlessMagic {
    Artifact,
    Payload,
    StateWitness,
    Effects,
}

struct Writer {
    bytes: Vec<u8>,
}

impl Writer {
    fn new() -> Self {
        Self { bytes: Vec::new() }
    }

    fn push(&mut self, magic: ArtifactlessMagic) {
        self.write_bytes(match magic {
            ArtifactlessMagic::Artifact => ARTIFACT_MAGIC,
            ArtifactlessMagic::Payload => PAYLOAD_MAGIC,
            ArtifactlessMagic::StateWitness => STATE_WITNESS_MAGIC,
            ArtifactlessMagic::Effects => EFFECTS_MAGIC,
        });
    }

    fn write_version_flags(&mut self) {
        self.write_u16(1);
        self.write_u16(0);
    }

    fn write_u8(&mut self, value: u8) {
        self.bytes.push(value);
    }

    fn write_u16(&mut self, value: u16) {
        self.bytes.extend_from_slice(&value.to_le_bytes());
    }

    fn write_u32(&mut self, value: u32) {
        self.bytes.extend_from_slice(&value.to_le_bytes());
    }

    fn write_i32(&mut self, value: i32) {
        self.bytes.extend_from_slice(&value.to_le_bytes());
    }

    fn write_u64(&mut self, value: u64) {
        self.bytes.extend_from_slice(&value.to_le_bytes());
    }

    fn write_i64(&mut self, value: i64) {
        self.bytes.extend_from_slice(&value.to_le_bytes());
    }

    fn write_bytes(&mut self, value: &[u8]) {
        self.bytes.extend_from_slice(value);
    }

    fn write_count(
        &mut self,
        value: usize,
        maximum: usize,
        field: &'static str,
    ) -> Result<(), ExecutionError> {
        if value > maximum {
            return Err(ExecutionError::Oversized(field));
        }
        let value = u32::try_from(value).map_err(|_| ExecutionError::Oversized(field))?;
        self.write_u32(value);
        Ok(())
    }

    fn write_length_prefixed(
        &mut self,
        value: &[u8],
        maximum: usize,
        field: &'static str,
    ) -> Result<(), ExecutionError> {
        self.write_count(value.len(), maximum, field)?;
        self.write_bytes(value);
        Ok(())
    }

    fn write_optional_bytes(
        &mut self,
        value: &Option<Vec<u8>>,
        maximum: usize,
        field: &'static str,
    ) -> Result<(), ExecutionError> {
        match value {
            Some(value) => {
                self.write_u8(1);
                self.write_length_prefixed(value, maximum, field)?;
            }
            None => self.write_u8(0),
        }
        Ok(())
    }

    fn finish(self) -> Vec<u8> {
        self.bytes
    }
}
