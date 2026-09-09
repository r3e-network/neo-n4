use alloc::{string::ToString, vec::Vec};

use crate::{
    hashing::{
        contract_binding_hash, contract_binding_key, encode_receipt, encode_stack_state,
    },
    manifest::parse_contract_manifest,
    types::{
        BatchEffects, CanonicalReceiptV1, CanonicalStackValue, ContractWitness,
        DEFAULT_ADDRESS_VERSION, DEFAULT_EXEC_FEE_FACTOR, DEFAULT_PER_TX_GAS_LIMIT,
        DEFAULT_STORAGE_PRICE, ExecutionError, ExecutionEvent, ProtocolConfig, StateEntry,
        StateWitness, StorageDelta, StorageOperation, TransactionEffects,
    },
};

use super::{
    constants::{
        EFFECTS_MAGIC, MAX_CONTRACTS, MAX_CONTRACT_MANIFEST_BYTES, MAX_CONTRACT_SCRIPT_BYTES,
        MAX_DELTAS_PER_TRANSACTION, MAX_EFFECTS_BYTES, MAX_EFFECT_TRANSACTIONS,
        MAX_EVENTS_PER_TRANSACTION, MAX_STATE_ENTRIES, MAX_STATE_KEY_BYTES, MAX_STATE_VALUE_BYTES,
        MAX_STATE_WITNESS_BYTES, STATE_WITNESS_MAGIC,
    },
    io::{require_version_flags, ArtifactlessMagic, Reader, Writer},
};

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
