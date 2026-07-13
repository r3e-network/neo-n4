use neo_execution_core::{
    CanonicalStackValue, ExecutionError, L1Message, ProofWitnessArtifact, StateEntry, StorageDelta,
    StorageOperation, bridged_nep17_hash, contract_binding_hash, contract_binding_key,
    contract_management_key, encode_execution_payload, encode_proof_witness_artifact,
    encode_receipt, events_hash, hash_l1_messages, hash160, hash256, keyed_state_root,
    l2_bridge_hash, l2_message_hash, parse_proof_witness_artifact, storage_delta_hash,
    token_management_hash,
};

const FIXTURE_HEX: &str = include_str!("fixtures/stateful_batch_v1.hex");
const NATIVE_FIXTURE_HEX: &str = include_str!("fixtures/native_transition_v1.hex");

fn fixture_bytes() -> Vec<u8> {
    hex::decode(FIXTURE_HEX.split_whitespace().collect::<String>()).expect("fixture hex")
}

fn fixture_artifact() -> ProofWitnessArtifact {
    parse_proof_witness_artifact(&fixture_bytes()).expect("fixture artifact")
}

fn native_fixture_bytes() -> Vec<u8> {
    hex::decode(NATIVE_FIXTURE_HEX.split_whitespace().collect::<String>())
        .expect("native fixture hex")
}

fn encode(artifact: &ProofWitnessArtifact) -> Vec<u8> {
    encode_proof_witness_artifact(artifact).expect("artifact encoding")
}

fn refresh_contract_binding(artifact: &mut ProofWitnessArtifact) {
    let contract = &artifact.state_witness.contracts[0];
    let key = contract_binding_key(&contract.hash);
    let value = contract_binding_hash(
        contract.id,
        &contract.hash,
        &contract.script,
        &contract.manifest_bytes,
    );
    let entry = artifact
        .state_witness
        .entries
        .iter_mut()
        .find(|entry| entry.key == key)
        .expect("contract binding entry");
    entry.value = value.to_vec();
}

fn recompute_claims(artifact: &mut ProofWitnessArtifact) {
    artifact.execution_payload.pre_state_root = keyed_state_root(&artifact.state_witness.entries);
    let mut computed =
        neo_zkvm_guest::compute_batch(&artifact.execution_payload, &artifact.state_witness)
            .expect("batch recomputation");
    artifact.payload_bytes =
        encode_execution_payload(&artifact.execution_payload).expect("payload encoding");
    artifact.da_commitment = hash256(&artifact.payload_bytes);
    computed.public_inputs.da_commitment = artifact.da_commitment;
    artifact.execution_result = computed.execution_result;
    artifact.effects = computed.effects;
    artifact.effects_bytes = computed.effects_bytes;
    artifact.public_inputs = computed.public_inputs;
}

fn refresh_da_claim(artifact: &mut ProofWitnessArtifact) {
    artifact.payload_bytes =
        encode_execution_payload(&artifact.execution_payload).expect("payload encoding");
    artifact.da_commitment = hash256(&artifact.payload_bytes);
    artifact.public_inputs.da_commitment = artifact.da_commitment;
}

fn refresh_pre_state_claim(artifact: &mut ProofWitnessArtifact) {
    artifact.execution_payload.pre_state_root = keyed_state_root(&artifact.state_witness.entries);
    artifact.public_inputs.pre_state_root = artifact.execution_payload.pre_state_root;
    refresh_da_claim(artifact);
}

#[test]
fn golden_fixture_round_trips_and_executes_statefully() {
    let bytes = fixture_bytes();
    let artifact = fixture_artifact();
    let result = neo_zkvm_guest::execute_batch(&bytes).expect("stateful fixture execution");

    assert_eq!(encode(&artifact), bytes);
    assert_eq!(
        result.post_state_root,
        artifact.execution_result.post_state_root
    );
    assert_ne!(
        result.post_state_root,
        artifact.execution_payload.pre_state_root
    );
    assert_eq!(artifact.effects.transactions.len(), 1);
    let transaction = &artifact.effects.transactions[0];
    assert!(transaction.receipt.success);
    assert_eq!(encode_receipt(&transaction.receipt).len(), 105);
    assert_eq!(transaction.storage_deltas.len(), 2);
    assert_eq!(transaction.events.len(), 2);
    assert_eq!(
        transaction.storage_deltas[0].new_value.as_deref(),
        Some(b"one".as_slice())
    );
    assert_eq!(
        transaction.storage_deltas[1].old_value.as_deref(),
        Some(b"old".as_slice())
    );
    assert_eq!(
        transaction.storage_deltas[1].new_value.as_deref(),
        Some(b"new".as_slice())
    );
    assert_eq!(transaction.events[0].name, "Updated");
    assert_eq!(transaction.events[1].name, "Updated");
}

#[test]
fn storage_and_event_v1_bind_order_and_full_parameters() {
    let artifact = fixture_artifact();
    let transaction = &artifact.effects.transactions[0];
    let mut reversed_deltas = transaction.storage_deltas.clone();
    reversed_deltas.reverse();
    assert_ne!(
        storage_delta_hash(&transaction.storage_deltas),
        storage_delta_hash(&reversed_deltas)
    );

    let mut reversed_events = transaction.events.clone();
    reversed_events.reverse();
    assert_ne!(
        events_hash(&transaction.events).unwrap(),
        events_hash(&reversed_events).unwrap()
    );
    let mut parameter_tamper = transaction.events.clone();
    parameter_tamper[0].state =
        CanonicalStackValue::Array(vec![CanonicalStackValue::ByteString(b"tampered".to_vec())]);
    assert_ne!(
        events_hash(&transaction.events).unwrap(),
        events_hash(&parameter_tamper).unwrap()
    );
}

#[test]
fn noncanonical_storage_order_is_rejected() {
    let mut artifact = fixture_artifact();
    artifact.effects.transactions[0].storage_deltas.reverse();
    let error = encode_proof_witness_artifact(&artifact).unwrap_err();
    assert!(matches!(error, ExecutionError::Invalid("storage delta")));
}

#[test]
fn event_order_and_parameter_tampering_are_rejected_by_execution() {
    let mut event_order = fixture_artifact();
    event_order.effects.transactions[0].events.reverse();
    let error = neo_zkvm_guest::execute_batch(&encode(&event_order)).unwrap_err();
    assert!(matches!(
        error,
        ExecutionError::ClaimMismatch("canonical effects")
    ));

    let mut event_parameter = fixture_artifact();
    event_parameter.effects.transactions[0].events[0].state =
        CanonicalStackValue::Array(vec![CanonicalStackValue::ByteString(b"tampered".to_vec())]);
    let error = neo_zkvm_guest::execute_batch(&encode(&event_parameter)).unwrap_err();
    assert!(matches!(
        error,
        ExecutionError::ClaimMismatch("canonical effects")
    ));
}

#[test]
fn pre_state_and_contract_witness_tampering_are_rejected() {
    let mut pre_state = fixture_artifact();
    let storage_entry = pre_state
        .state_witness
        .entries
        .iter_mut()
        .find(|entry| entry.key.ends_with(b"counter"))
        .expect("counter entry");
    storage_entry.value = b"forged".to_vec();
    let error = neo_zkvm_guest::execute_batch(&encode(&pre_state)).unwrap_err();
    assert!(matches!(error, ExecutionError::Invalid("pre-state root")));

    let mut code_witness = fixture_artifact();
    code_witness.state_witness.contracts[0].script[0] ^= 1;
    refresh_contract_binding(&mut code_witness);
    let error = neo_zkvm_guest::execute_batch(&encode(&code_witness)).unwrap_err();
    assert!(matches!(error, ExecutionError::Invalid("pre-state root")));
}

#[test]
fn root_receipt_and_transaction_tampering_are_rejected() {
    let mut post_root = fixture_artifact();
    post_root.execution_result.post_state_root[0] ^= 1;
    post_root.public_inputs.post_state_root = post_root.execution_result.post_state_root;
    let error = neo_zkvm_guest::execute_batch(&encode(&post_root)).unwrap_err();
    assert!(matches!(
        error,
        ExecutionError::ClaimMismatch("execution result")
    ));

    let mut receipt = fixture_artifact();
    receipt.effects.transactions[0].receipt.gas_consumed += 1;
    let error = neo_zkvm_guest::execute_batch(&encode(&receipt)).unwrap_err();
    assert!(matches!(
        error,
        ExecutionError::ClaimMismatch("canonical effects")
    ));

    let mut transaction = fixture_artifact();
    let bytes = &mut transaction.execution_payload.transactions[0];
    let position = bytes
        .windows(5)
        .position(|window| window == b"store")
        .expect("method name");
    bytes[position] = b'S';
    refresh_da_claim(&mut transaction);
    let error = neo_zkvm_guest::execute_batch(&encode(&transaction)).unwrap_err();
    assert!(matches!(
        error,
        ExecutionError::ClaimMismatch("execution result")
    ));
}

#[test]
fn malformed_full_transaction_terminates_the_batch() {
    let mut artifact = fixture_artifact();
    artifact.execution_payload.transactions[0].pop();
    refresh_da_claim(&mut artifact);
    let error = neo_zkvm_guest::execute_batch(&encode(&artifact)).unwrap_err();
    assert!(matches!(error, ExecutionError::Truncated));
}

#[test]
fn fault_rolls_back_storage_and_events() {
    let mut artifact = fixture_artifact();
    artifact.state_witness.contracts[0].script = fault_after_write_script();
    refresh_contract_binding(&mut artifact);
    recompute_claims(&mut artifact);
    let encoded = encode(&artifact);
    let result = neo_zkvm_guest::execute_batch(&encoded).expect("FAULT batch is valid");
    let effects = &artifact.effects.transactions[0];

    assert!(!effects.receipt.success);
    assert!(effects.storage_deltas.is_empty());
    assert!(effects.events.is_empty());
    assert_eq!(effects.receipt.storage_delta_hash, [0u8; 32]);
    assert_eq!(effects.receipt.events_hash, [0u8; 32]);
    assert_eq!(
        result.post_state_root,
        artifact.execution_payload.pre_state_root
    );
}

#[test]
fn unknown_consensus_syscall_fails_closed() {
    let mut artifact = fixture_artifact();
    artifact.state_witness.contracts[0].script = vec![0x41, 0xde, 0xad, 0xbe, 0xef, 0x40];
    refresh_contract_binding(&mut artifact);
    recompute_claims(&mut artifact);
    let encoded = encode(&artifact);
    neo_zkvm_guest::execute_batch(&encoded).expect("fail-closed receipt is provable");
    assert!(!artifact.effects.transactions[0].receipt.success);
    assert_eq!(
        artifact.execution_result.post_state_root,
        artifact.execution_payload.pre_state_root
    );
}

#[test]
fn non_empty_l1_inbox_executes_native_deposit_and_rejects_tampering() {
    let mut artifact = fixture_artifact();
    let binding = artifact
        .state_witness
        .entries
        .iter()
        .find(|entry| {
            entry
                .key
                .starts_with(neo_execution_core::CONTRACT_BINDING_KEY_PREFIX)
        })
        .expect("fixture contract binding")
        .clone();
    artifact.state_witness.entries = deposit_state_entries();
    artifact.state_witness.entries.push(binding);
    artifact
        .state_witness
        .entries
        .sort_by(|left, right| left.key.cmp(&right.key));
    artifact.execution_payload.l1_messages = vec![deposit_message()];
    artifact.execution_payload.transactions = vec![transaction(vec![0x40])];
    recompute_claims(&mut artifact);

    assert_eq!(
        artifact.execution_payload.pre_state_root,
        hex32("97a91f19828e53b4b04e2a012fa5879f04fe2216efa7b1c857b0e2ccb9817a7a")
    );
    assert_eq!(
        artifact.execution_result.post_state_root,
        hex32("b4e6a5f1d64e621a0ebea61187c65de599416d59a8db3147989d0d62f22670eb")
    );
    assert_ne!(artifact.public_inputs.l1_message_hash, [0u8; 32]);
    let result = neo_zkvm_guest::execute_batch(&encode(&artifact)).expect("deposit executes");
    assert_eq!(
        result.post_state_root,
        artifact.execution_result.post_state_root
    );

    let mut replay = artifact.clone();
    replay.state_witness.entries.push(StateEntry {
        key: native_key(-104, 0x02, &[&0u32.to_le_bytes(), &7u64.to_le_bytes()]),
        value: vec![1],
    });
    replay
        .state_witness
        .entries
        .sort_by(|left, right| left.key.cmp(&right.key));
    refresh_pre_state_claim(&mut replay);
    assert!(matches!(
        neo_zkvm_guest::execute_batch(&encode(&replay)),
        Err(ExecutionError::Invalid("deposit replay"))
    ));

    let mut wrong_source = artifact.clone();
    wrong_source.execution_payload.l1_messages[0].source_chain_id = 1;
    assert!(matches!(
        encode_execution_payload(&wrong_source.execution_payload),
        Err(ExecutionError::Invalid("L1 message routing"))
    ));

    let mut mapping = artifact.clone();
    let mapping_entry = mapping
        .state_witness
        .entries
        .iter_mut()
        .find(|entry| entry.key.starts_with(&(-104i32).to_le_bytes()) && entry.key[4] == 0x01)
        .expect("mapping entry");
    mapping_entry.value[21] = 19;
    refresh_pre_state_claim(&mut mapping);
    assert!(matches!(
        neo_zkvm_guest::execute_batch(&encode(&mapping)),
        Err(ExecutionError::Invalid("L2 bridge asset mapping"))
    ));

    let mut message_hash = artifact.clone();
    message_hash.public_inputs.l1_message_hash[0] ^= 1;
    assert!(matches!(
        encode_proof_witness_artifact(&message_hash),
        Err(ExecutionError::Invalid("public input claims"))
    ));

    let mut unsupported = artifact.clone();
    unsupported.execution_payload.l1_messages[0].message_type = 2;
    unsupported.public_inputs.l1_message_hash =
        hash_l1_messages(&unsupported.execution_payload.l1_messages);
    refresh_da_claim(&mut unsupported);
    assert!(matches!(
        neo_zkvm_guest::execute_batch(&encode(&unsupported)),
        Err(ExecutionError::Unsupported("L1 inbox message type V1"))
    ));
}

#[test]
fn native_message_execution_produces_nonzero_bound_outbox_root() {
    let mut artifact = fixture_artifact();
    artifact.state_witness.entries.extend([
        StateEntry {
            key: contract_management_key(&l2_message_hash()),
            value: b"native".to_vec(),
        },
        StateEntry {
            key: native_key(-103, 0x03, &[]),
            value: vec![0x4b, 0x04],
        },
    ]);
    artifact
        .state_witness
        .entries
        .sort_by(|left, right| left.key.cmp(&right.key));
    artifact.execution_payload.transactions = vec![transaction(native_message_script())];
    recompute_claims(&mut artifact);

    let effects = &artifact.effects.transactions[0];
    assert!(effects.receipt.success);
    assert_eq!(effects.events.len(), 1);
    assert_eq!(effects.events[0].script_hash, l2_message_hash());
    assert_eq!(effects.events[0].name, "MessageEmitted");
    assert_ne!(artifact.execution_result.l2_to_l1_message_root, [0u8; 32]);
    assert_eq!(artifact.execution_result.l2_to_l2_message_root, [0u8; 32]);
    neo_zkvm_guest::execute_batch(&encode(&artifact)).expect("native message executes");

    let mut tampered = artifact.clone();
    tampered.execution_result.l2_to_l1_message_root[0] ^= 1;
    tampered.public_inputs.l2_to_l1_message_root = tampered.execution_result.l2_to_l1_message_root;
    assert!(matches!(
        neo_zkvm_guest::execute_batch(&encode(&tampered)),
        Err(ExecutionError::ClaimMismatch("execution result"))
    ));
}

#[test]
fn native_withdrawal_execution_burns_native_balance_and_binds_root() {
    let script = native_withdrawal_script();
    let sender = hash160(&script);
    let l2_asset = bridged_asset();
    let l1_asset = [0x22; 20];
    let mut entries = native_contract_entries();
    entries.extend([
        StateEntry {
            key: native_key(-109, 0xfe, &[]),
            value: l2_bridge_hash().to_vec(),
        },
        StateEntry {
            key: native_key(-104, 0x04, &[&l2_asset]),
            value: [l1_asset.as_slice(), &[6, 8]].concat(),
        },
        StateEntry {
            key: native_key(-12, 0x0a, &[&l2_asset]),
            value: token_state_bytes(20_000),
        },
        StateEntry {
            key: native_key(-12, 0x0c, &[&sender, &l2_asset]),
            value: account_state_bytes(12_300),
        },
    ]);
    entries.sort_by(|left, right| left.key.cmp(&right.key));

    let mut artifact = fixture_artifact();
    let binding = artifact
        .state_witness
        .entries
        .iter()
        .find(|entry| {
            entry
                .key
                .starts_with(neo_execution_core::CONTRACT_BINDING_KEY_PREFIX)
        })
        .expect("fixture contract binding")
        .clone();
    entries.push(binding);
    entries.sort_by(|left, right| left.key.cmp(&right.key));
    artifact.state_witness.entries = entries;
    artifact.execution_payload.l1_messages.clear();
    artifact.execution_payload.transactions = vec![transaction(script)];
    recompute_claims(&mut artifact);

    let effects = &artifact.effects.transactions[0];
    assert!(effects.receipt.success);
    assert_eq!(effects.events.len(), 2);
    assert_eq!(effects.events[1].script_hash, l2_bridge_hash());
    assert_eq!(effects.events[1].name, "WithdrawalEmitted");
    assert_ne!(artifact.execution_result.withdrawal_root, [0u8; 32]);
    assert!(effects.storage_deltas.iter().any(|delta| {
        delta.key == native_key(-12, 0x0c, &[&sender, &l2_asset]) && delta.new_value.is_none()
    }));
    neo_zkvm_guest::execute_batch(&encode(&artifact)).expect("native withdrawal executes");

    let mut tampered = artifact.clone();
    tampered.execution_result.withdrawal_root[0] ^= 1;
    tampered.public_inputs.withdrawal_root = tampered.execution_result.withdrawal_root;
    assert!(matches!(
        neo_zkvm_guest::execute_batch(&encode(&tampered)),
        Err(ExecutionError::ClaimMismatch("execution result"))
    ));
}

#[test]
fn native_batch_fixture_executes_non_empty_inbox_and_outboxes() {
    let artifact = native_batch_artifact();
    let encoded = encode(&artifact);
    assert_eq!(encoded, native_fixture_bytes());
    neo_zkvm_guest::execute_batch(&encoded).expect("native fixture executes");
    assert_ne!(artifact.execution_result.withdrawal_root, [0u8; 32]);
    assert_ne!(artifact.execution_result.l2_to_l1_message_root, [0u8; 32]);
    assert_ne!(artifact.public_inputs.l1_message_hash, [0u8; 32]);
}

fn fault_after_write_script() -> Vec<u8> {
    let mut script = vec![0x0c, 3];
    script.extend_from_slice(b"bad");
    script.extend_from_slice(&[0x0c, 7]);
    script.extend_from_slice(b"counter");
    script.extend_from_slice(&[0x41, 0x39, 0x0c, 0xe3, 0x0a]);
    script.push(0x38);
    script
}

#[test]
fn storage_transition_hash_binds_presence_and_operation() {
    let original = StorageDelta {
        key: b"key".to_vec(),
        operation: StorageOperation::Update,
        old_value: Some(b"old".to_vec()),
        new_value: Some(b"new".to_vec()),
    };
    let mut changed = original.clone();
    changed.operation = StorageOperation::Add;
    changed.old_value = None;
    assert_ne!(
        storage_delta_hash(&[original]),
        storage_delta_hash(&[changed])
    );
}

fn deposit_state_entries() -> Vec<StateEntry> {
    let l1_asset = [0x22; 20];
    let l2_asset = bridged_asset();
    let mut entries = native_contract_entries();
    entries.extend([
        StateEntry {
            key: native_key(-109, 0xfe, &[]),
            value: l2_bridge_hash().to_vec(),
        },
        StateEntry {
            key: native_key(-104, 0x01, &[&l1_asset]),
            value: [l2_asset.as_slice(), &[6, 8]].concat(),
        },
        StateEntry {
            key: native_key(-12, 0x0a, &[&l2_asset]),
            value: token_state_bytes(5),
        },
    ]);
    entries.sort_by(|left, right| left.key.cmp(&right.key));
    entries
}

fn native_batch_artifact() -> ProofWitnessArtifact {
    let message_script = native_message_script();
    let withdrawal_script = native_withdrawal_script();
    let withdrawal_sender = hash160(&withdrawal_script);
    let l2_asset = bridged_asset();
    let l1_asset = [0x22; 20];
    let mut artifact = fixture_artifact();
    let binding = artifact
        .state_witness
        .entries
        .iter()
        .find(|entry| {
            entry
                .key
                .starts_with(neo_execution_core::CONTRACT_BINDING_KEY_PREFIX)
        })
        .expect("fixture contract binding")
        .clone();
    let mut entries = deposit_state_entries();
    entries
        .iter_mut()
        .find(|entry| entry.key.starts_with(&(-12i32).to_le_bytes()) && entry.key[4] == 0x0a)
        .expect("token state")
        .value = token_state_bytes(20_000);
    entries.extend([
        binding,
        StateEntry {
            key: contract_management_key(&l2_message_hash()),
            value: b"native".to_vec(),
        },
        StateEntry {
            key: native_key(-103, 0x03, &[]),
            value: vec![0x4b, 0x04],
        },
        StateEntry {
            key: native_key(-104, 0x04, &[&l2_asset]),
            value: [l1_asset.as_slice(), &[6, 8]].concat(),
        },
        StateEntry {
            key: native_key(-12, 0x0c, &[&withdrawal_sender, &l2_asset]),
            value: account_state_bytes(12_300),
        },
    ]);
    entries.sort_by(|left, right| left.key.cmp(&right.key));
    artifact.state_witness.entries = entries;
    artifact.execution_payload.l1_messages = vec![deposit_message()];
    artifact.execution_payload.transactions = vec![
        transaction(message_script),
        transaction_with_nonce(withdrawal_script, 8),
    ];
    recompute_claims(&mut artifact);
    artifact
}

fn native_contract_entries() -> Vec<StateEntry> {
    [
        l2_bridge_hash(),
        bridged_nep17_hash(),
        token_management_hash(),
    ]
    .into_iter()
    .map(|hash| StateEntry {
        key: contract_management_key(&hash),
        value: b"native".to_vec(),
    })
    .collect()
}

fn deposit_message() -> L1Message {
    let amount = [0x40, 0xe2, 0x01];
    let mut payload = [0x22; 20].to_vec();
    payload.extend_from_slice(&[0x33; 20]);
    payload.extend_from_slice(&(amount.len() as i32).to_le_bytes());
    payload.extend_from_slice(&amount);
    L1Message {
        source_chain_id: 0,
        target_chain_id: 1099,
        nonce: 7,
        sender: [0x11; 20],
        receiver: l2_bridge_hash(),
        message_type: 0,
        payload,
    }
}

fn native_message_script() -> Vec<u8> {
    let mut script = Vec::new();
    push_bytes(&mut script, b"guest-message");
    push_integer(&mut script, 2);
    push_bytes(&mut script, &[0x55; 20]);
    push_integer(&mut script, 0);
    push_integer(&mut script, 4);
    script.push(0xc0);
    push_integer(&mut script, 15);
    push_bytes(&mut script, b"emitMessage");
    push_bytes(&mut script, &l2_message_hash());
    script.extend_from_slice(&[0x41, 0x62, 0x7d, 0x5b, 0x52, 0x40]);
    script
}

fn native_withdrawal_script() -> Vec<u8> {
    let mut script = Vec::new();
    push_bytes(&mut script, &[0x77; 20]);
    push_integer(&mut script, 12_300);
    push_bytes(&mut script, &bridged_asset());
    push_integer(&mut script, 3);
    script.push(0xc0);
    push_integer(&mut script, 15);
    push_bytes(&mut script, b"initiateWithdrawal");
    push_bytes(&mut script, &l2_bridge_hash());
    script.extend_from_slice(&[0x41, 0x62, 0x7d, 0x5b, 0x52, 0x40]);
    script
}

fn transaction(script: Vec<u8>) -> Vec<u8> {
    transaction_with_nonce(script, 7)
}

fn transaction_with_nonce(script: Vec<u8>, nonce: u32) -> Vec<u8> {
    let mut transaction = Vec::new();
    transaction.push(0);
    transaction.extend_from_slice(&nonce.to_le_bytes());
    transaction.extend_from_slice(&0i64.to_le_bytes());
    transaction.extend_from_slice(&0i64.to_le_bytes());
    transaction.extend_from_slice(&5000u32.to_le_bytes());
    transaction.push(1);
    transaction.extend_from_slice(&[0x11; 20]);
    transaction.push(0x80);
    transaction.push(0);
    transaction.push(u8::try_from(script.len()).expect("test script length"));
    transaction.extend_from_slice(&script);
    transaction.extend_from_slice(&[1, 0, 0]);
    transaction
}

fn push_bytes(script: &mut Vec<u8>, value: &[u8]) {
    script.extend_from_slice(&[0x0c, u8::try_from(value.len()).expect("test byte string")]);
    script.extend_from_slice(value);
}

fn push_integer(script: &mut Vec<u8>, value: i16) {
    if (0..=16).contains(&value) {
        script.push(0x10 + value as u8);
    } else if (-128..=127).contains(&value) {
        script.extend_from_slice(&[0x00, value as i8 as u8]);
    } else {
        script.push(0x01);
        script.extend_from_slice(&value.to_le_bytes());
    }
}

fn bridged_asset() -> [u8; 20] {
    let mut preimage = bridged_nep17_hash().to_vec();
    preimage.extend_from_slice(b"Wrapped GAS");
    hash160(&preimage)
}

fn token_state_bytes(total_supply: u64) -> Vec<u8> {
    let mut bytes = vec![0x41, 7];
    append_integer(&mut bytes, 1);
    append_bytes(&mut bytes, &bridged_nep17_hash());
    append_bytes(&mut bytes, b"Wrapped GAS");
    append_bytes(&mut bytes, b"WGAS");
    append_integer(&mut bytes, 8);
    append_integer(&mut bytes, total_supply);
    append_integer(&mut bytes, 1_000_000_000);
    bytes
}

fn account_state_bytes(balance: u64) -> Vec<u8> {
    let mut bytes = vec![0x41, 1];
    append_integer(&mut bytes, balance);
    bytes
}

fn append_integer(bytes: &mut Vec<u8>, value: u64) {
    let mut encoded = value.to_le_bytes().to_vec();
    while encoded.len() > 1 && encoded.last() == Some(&0) && encoded[encoded.len() - 2] & 0x80 == 0
    {
        encoded.pop();
    }
    bytes.push(0x21);
    bytes.push(encoded.len() as u8);
    bytes.extend_from_slice(&encoded);
}

fn append_bytes(bytes: &mut Vec<u8>, value: &[u8]) {
    bytes.extend_from_slice(&[0x28, value.len() as u8]);
    bytes.extend_from_slice(value);
}

fn native_key(contract_id: i32, prefix: u8, parts: &[&[u8]]) -> Vec<u8> {
    let mut key = contract_id.to_le_bytes().to_vec();
    key.push(prefix);
    for part in parts {
        key.extend_from_slice(part);
    }
    key
}

fn hex32(value: &str) -> [u8; 32] {
    hex::decode(value)
        .expect("test hash")
        .try_into()
        .expect("32-byte hash")
}
