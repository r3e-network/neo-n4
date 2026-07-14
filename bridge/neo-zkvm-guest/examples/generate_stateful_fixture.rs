use neo_execution_core::{
    BatchBlockContext, ContractManifest, ContractParameterType, ContractWitness, ExecutionPayload,
    ManifestEvent, ManifestMethod, ManifestPermission, NativeExecutionOutputV1, PermissionContract,
    PermissionMethods, ProofWitnessArtifact, ProtocolConfig,
    SP1_STATEFUL_NEO_VM_V1_EXECUTION_SEMANTIC_ID, StateEntry, StateWitness, contract_binding_hash,
    contract_binding_key, encode_execution_payload, encode_native_execution_output,
    encode_proof_witness_artifact, encode_state_witness, hash256, keyed_state_root,
};

#[path = "../vk_manifest.rs"]
#[allow(dead_code)]
mod pinned;

fn main() {
    let artifact = fixture_artifact();
    if std::env::args().nth(1).as_deref() == Some("--native-output") {
        let payload_bytes = encode_execution_payload(&artifact.execution_payload)
            .expect("payload encoding must succeed");
        let state_witness_bytes =
            encode_state_witness(&artifact.state_witness).expect("state witness must encode");
        let transition = neo_zkvm_guest::compute_batch_transition(
            &artifact.execution_payload,
            &artifact.state_witness,
        )
        .expect("fixture transition must execute");
        let output = NativeExecutionOutputV1 {
            request_payload_hash: hash256(&payload_bytes),
            request_state_witness_hash: hash256(&state_witness_bytes),
            execution_semantic_id: SP1_STATEFUL_NEO_VM_V1_EXECUTION_SEMANTIC_ID,
            execution_result: transition.batch.execution_result,
            effects_bytes: transition.batch.effects_bytes,
            post_state_witness_bytes: transition.post_state_witness_bytes,
            public_input_hash: transition.batch.public_input_hash,
        };
        println!(
            "{}",
            hex::encode(encode_native_execution_output(&output).expect("output encoding"))
        );
        return;
    }
    let encoded = encode_proof_witness_artifact(&artifact).expect("fixture encoding must succeed");
    let result = neo_zkvm_guest::execute_batch(&encoded).expect("fixture must execute");
    assert_eq!(
        result.post_state_root,
        artifact.execution_result.post_state_root
    );
    println!("{}", hex::encode(encoded));
}

fn fixture_artifact() -> ProofWitnessArtifact {
    let contract_hash = [0x22; 20];
    let contract_script = contract_script();
    let manifest_bytes = br#"{"name":"StatefulFixture","groups":[],"features":{},"supportedstandards":[],"abi":{"methods":[{"name":"store","parameters":[],"returntype":"Void","offset":0,"safe":false}],"events":[{"name":"Updated","parameters":[{"name":"value","type":"ByteArray"}]}]},"permissions":[{"contract":"*","methods":"*"}],"trusts":[],"extra":null}"#.to_vec();
    let manifest = ContractManifest {
        name: "StatefulFixture".to_string(),
        groups: Vec::new(),
        methods: vec![ManifestMethod {
            name: "store".to_string(),
            parameter_types: Vec::new(),
            return_type: ContractParameterType::Void,
            offset: 0,
            safe: false,
        }],
        events: vec![ManifestEvent {
            name: "Updated".to_string(),
            parameter_types: vec![ContractParameterType::ByteArray],
        }],
        permissions: vec![ManifestPermission {
            contract: PermissionContract::Wildcard,
            methods: PermissionMethods::Wildcard,
        }],
    };
    let contract = ContractWitness {
        id: 42,
        hash: contract_hash,
        script: contract_script.clone(),
        manifest_bytes: manifest_bytes.clone(),
        manifest,
    };
    let mut storage_key = 42i32.to_le_bytes().to_vec();
    storage_key.extend_from_slice(b"counter");
    let mut entries = vec![
        StateEntry {
            key: storage_key,
            value: b"old".to_vec(),
        },
        StateEntry {
            key: contract_binding_key(&contract_hash),
            value: contract_binding_hash(
                contract.id,
                &contract_hash,
                &contract_script,
                &manifest_bytes,
            )
            .to_vec(),
        },
    ];
    entries.sort_by(|left, right| left.key.cmp(&right.key));
    let witness = StateWitness {
        config: ProtocolConfig {
            exec_fee_factor: neo_execution_core::DEFAULT_EXEC_FEE_FACTOR,
            storage_price: neo_execution_core::DEFAULT_STORAGE_PRICE,
            address_version: neo_execution_core::DEFAULT_ADDRESS_VERSION,
            per_tx_gas_limit: neo_execution_core::DEFAULT_PER_TX_GAS_LIMIT,
        },
        entries,
        contracts: vec![contract],
    };
    let pre_state_root = keyed_state_root(&witness.entries);
    let payload = ExecutionPayload {
        chain_id: 1099,
        batch_number: 7,
        first_block: 100,
        last_block: 100,
        pre_state_root,
        block_context: BatchBlockContext {
            l1_finalized_height: 1234,
            first_block_timestamp: 1_750_000_000_000,
            last_block_timestamp: 1_750_000_000_000,
            sequencer_committee_hash: [0x33; 32],
            network: 0x334f_454e,
        },
        l1_messages: Vec::new(),
        forced_inclusions: Vec::new(),
        transactions: vec![transaction(contract_hash)],
    };
    let mut computed =
        neo_zkvm_guest::compute_batch(&payload, &witness).expect("fixture execution must succeed");
    let payload_bytes = encode_execution_payload(&payload).expect("payload must encode");
    let da_commitment = hash256(&payload_bytes);
    computed.public_inputs.da_commitment = da_commitment;

    ProofWitnessArtifact {
        proof_type: 3,
        proof_system: 1,
        execution_witness_authenticated: true,
        verification_key_id: pinned::PINNED_BATCH_VK_BYTES32,
        execution_semantic_id: neo_execution_core::SP1_STATEFUL_NEO_VM_V1_EXECUTION_SEMANTIC_ID,
        chain_id: payload.chain_id,
        batch_number: payload.batch_number,
        first_block: payload.first_block,
        last_block: payload.last_block,
        payload_bytes,
        execution_payload: payload,
        state_witness_bytes: Vec::new(),
        state_witness: witness,
        execution_result: computed.execution_result,
        effects_bytes: computed.effects_bytes,
        effects: computed.effects,
        da_mode: u8::MAX,
        da_receipt_kind: 1,
        da_commitment,
        da_pointer: b"fixture://stateful-v1".to_vec(),
        da_evidence: b"fixture-evidence-v1".to_vec(),
        public_inputs: computed.public_inputs,
    }
}

fn transaction(contract_hash: [u8; 20]) -> Vec<u8> {
    let mut script = vec![0xc2, 0x1f, 0x0c, 5];
    script.extend_from_slice(b"store");
    script.extend_from_slice(&[0x0c, 20]);
    script.extend_from_slice(&contract_hash);
    script.extend_from_slice(&[0x41, 0x62, 0x7d, 0x5b, 0x52, 0x40]);

    let mut transaction = Vec::new();
    transaction.push(0);
    transaction.extend_from_slice(&7u32.to_le_bytes());
    transaction.extend_from_slice(&0i64.to_le_bytes());
    transaction.extend_from_slice(&0i64.to_le_bytes());
    transaction.extend_from_slice(&5000u32.to_le_bytes());
    transaction.push(1);
    transaction.extend_from_slice(&[0x11; 20]);
    transaction.push(0x80);
    transaction.push(0);
    transaction.push(u8::try_from(script.len()).expect("fixture script length"));
    transaction.extend_from_slice(&script);
    transaction.push(1);
    transaction.push(0);
    transaction.push(0);
    transaction
}

fn contract_script() -> Vec<u8> {
    let mut script = vec![0x0c, 3];
    script.extend_from_slice(b"new");
    script.extend_from_slice(&[0x0c, 7]);
    script.extend_from_slice(b"counter");
    script.extend_from_slice(&[0x41, 0x39, 0x0c, 0xe3, 0x0a]);
    script.extend_from_slice(&[0x0c, 3]);
    script.extend_from_slice(b"one");
    script.extend_from_slice(&[0x0c, 5]);
    script.extend_from_slice(b"alpha");
    script.extend_from_slice(&[0x41, 0x39, 0x0c, 0xe3, 0x0a]);
    script.extend_from_slice(&[0x0c, 3]);
    script.extend_from_slice(b"new");
    script.extend_from_slice(&[0x11, 0xc0, 0x0c, 7]);
    script.extend_from_slice(b"Updated");
    script.extend_from_slice(&[0x41, 0x95, 0x01, 0x6f, 0x61]);
    script.extend_from_slice(&[0x0c, 3]);
    script.extend_from_slice(b"one");
    script.extend_from_slice(&[0x11, 0xc0, 0x0c, 7]);
    script.extend_from_slice(b"Updated");
    script.extend_from_slice(&[0x41, 0x95, 0x01, 0x6f, 0x61, 0x40]);
    script
}
