#![cfg(unix)]

use neo_zkvm_gateway_guest::{
    BINDING_BYTES, BINDING_MAGIC, RECURSIVE_BACKEND_ID, REQUEST_MAGIC, SP1_PROOF_SYSTEM,
    ZK_PROOF_TYPE, encode_child_sidecar, hash256, parse_request_with_gateway_vk,
};
use neo_zkvm_gateway_host::{
    TEST_ONLY_BUILD, canonical_sidecar_filename, gateway_verification_key, prove_request,
};

const BATCH_FIXTURE: &str =
    include_str!("../../neo-zkvm-guest/tests/fixtures/stateful_batch_v1.hex");

#[test]
#[ignore = "release gate: generates a real compressed batch proof and recursive Gateway Groth16 proof"]
#[serial_test::serial]
fn proves_and_host_verifies_real_recursive_gateway_groth16() {
    assert!(
        core::hint::black_box(!TEST_ONLY_BUILD),
        "release gate must run without the test-only-vk feature"
    );
    let batch_fixture = hex::decode(BATCH_FIXTURE.split_whitespace().collect::<String>()).unwrap();
    let artifact = neo_execution_core::parse_proof_witness_artifact(&batch_fixture).unwrap();
    let terminal = neo_zkvm_host::prove(&batch_fixture).expect("real terminal batch proof");
    let child = neo_zkvm_host::prove_compressed(&batch_fixture)
        .expect("real compressed recursive child proof");
    assert_eq!(terminal.public_input_hash, child.public_input_hash);

    let child_directory = tempfile::tempdir().unwrap();
    let sidecar = child_directory.path().join(canonical_sidecar_filename(
        child.chain_id,
        child.batch_number,
        &child.public_input_hash,
    ));
    let sidecar_bytes = encode_child_sidecar(
        child.chain_id,
        child.batch_number,
        &child.public_input_hash,
        &child.l1_message_hash,
        &child.block_context_hash,
        &child.proof_bytes,
    )
    .unwrap();
    std::fs::write(&sidecar, sidecar_bytes).expect("save canonical recursive child sidecar");

    let gateway_vk = gateway_verification_key();
    let request_bytes = request(&artifact, &terminal.proof_bytes, &gateway_vk);
    let parsed = parse_request_with_gateway_vk(&request_bytes, Some(&gateway_vk)).unwrap();
    let result = prove_request(&parsed, &request_bytes, child_directory.path())
        .expect("real recursive Gateway proof");

    assert_eq!(result.proof_bytes.len(), 356);
    assert_eq!(result.verification_key, gateway_vk);
    assert_eq!(result.public_values[0], 0);
    assert_eq!(result.public_values[1..], hash256(&parsed.binding.bytes));
}

fn request(
    artifact: &neo_execution_core::ProofWitnessArtifact,
    proof: &[u8],
    gateway_vk: &[u8; 32],
) -> Vec<u8> {
    let commitment = commitment(artifact, proof);
    let constituent_root = hash256(&commitment);
    let message_root = [0x77; 32];
    let mut binding = [0u8; BINDING_BYTES];
    binding[..8].copy_from_slice(BINDING_MAGIC);
    binding[8..28].fill(0x11);
    binding[28..60].fill(0x22);
    binding[60..68].copy_from_slice(&1u64.to_le_bytes());
    binding[68..100].copy_from_slice(&message_root);
    binding[100..132].copy_from_slice(&constituent_root);
    binding[132..136].copy_from_slice(&1u32.to_le_bytes());
    binding[136] = RECURSIVE_BACKEND_ID;
    binding[137] = SP1_PROOF_SYSTEM;
    binding[138..170].copy_from_slice(gateway_vk);

    let mut request = Vec::new();
    request.extend_from_slice(REQUEST_MAGIC);
    request.extend_from_slice(&binding);
    request.extend_from_slice(&1u32.to_le_bytes());
    request.extend_from_slice(&(commitment.len() as u32).to_le_bytes());
    request.extend_from_slice(&commitment);
    request
}

fn commitment(artifact: &neo_execution_core::ProofWitnessArtifact, proof: &[u8]) -> Vec<u8> {
    let inputs = &artifact.public_inputs;
    let public_input_hash = neo_execution_core::hash_public_inputs(
        inputs.chain_id,
        inputs.batch_number,
        &inputs.pre_state_root,
        &inputs.post_state_root,
        &inputs.tx_root,
        &inputs.receipt_root,
        &inputs.withdrawal_root,
        &inputs.l2_to_l1_message_root,
        &inputs.l2_to_l2_message_root,
        &inputs.l1_message_hash,
        &inputs.da_commitment,
        &inputs.block_context_hash,
    );
    let mut bytes = vec![0u8; 321 + proof.len()];
    bytes[..4].copy_from_slice(&artifact.chain_id.to_le_bytes());
    bytes[4..12].copy_from_slice(&artifact.batch_number.to_le_bytes());
    bytes[12..20].copy_from_slice(&artifact.first_block.to_le_bytes());
    bytes[20..28].copy_from_slice(&artifact.last_block.to_le_bytes());
    for (index, root) in [
        inputs.pre_state_root,
        inputs.post_state_root,
        inputs.tx_root,
        inputs.receipt_root,
        inputs.withdrawal_root,
        inputs.l2_to_l1_message_root,
        inputs.l2_to_l2_message_root,
        inputs.da_commitment,
        public_input_hash,
    ]
    .iter()
    .enumerate()
    {
        bytes[28 + index * 32..28 + (index + 1) * 32].copy_from_slice(root);
    }
    bytes[316] = ZK_PROOF_TYPE;
    bytes[317..321].copy_from_slice(&(proof.len() as i32).to_le_bytes());
    bytes[321..].copy_from_slice(proof);
    bytes
}
