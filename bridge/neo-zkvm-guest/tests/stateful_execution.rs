use neo_execution_core::{
    CanonicalStackValue, ExecutionError, ProofWitnessArtifact, StorageDelta, StorageOperation,
    contract_binding_hash, contract_binding_key, encode_execution_payload,
    encode_proof_witness_artifact, encode_receipt, events_hash, hash256, keyed_state_root,
    parse_proof_witness_artifact, storage_delta_hash,
};

const FIXTURE_HEX: &str = include_str!("fixtures/stateful_batch_v1.hex");

fn fixture_bytes() -> Vec<u8> {
    hex::decode(FIXTURE_HEX.split_whitespace().collect::<String>()).expect("fixture hex")
}

fn fixture_artifact() -> ProofWitnessArtifact {
    parse_proof_witness_artifact(&fixture_bytes()).expect("fixture artifact")
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
