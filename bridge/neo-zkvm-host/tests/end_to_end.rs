#![cfg(unix)]

const FIXTURE_HEX: &str = include_str!("../../neo-zkvm-guest/tests/fixtures/stateful_batch_v1.hex");

fn stateful_fixture() -> Vec<u8> {
    hex::decode(FIXTURE_HEX.split_whitespace().collect::<String>()).expect("fixture hex")
}

fn skip_when_cached_elf_is_allowed(test_name: &str) -> bool {
    if std::env::var("NEO_ZKVM_ALLOW_CACHED_ELF").as_deref() == Ok("1") {
        eprintln!("skipping {test_name}: cached-ELF mode cannot prove the embedded guest is fresh");
        true
    } else {
        false
    }
}

#[test]
#[serial_test::serial]
fn execute_fresh_stateful_guest_matches_host_run() {
    if skip_when_cached_elf_is_allowed("execute_fresh_stateful_guest_matches_host_run") {
        return;
    }

    let bytes = stateful_fixture();
    let zkvm_result = neo_zkvm_host::execute(&bytes).expect("zkVM execute failed");
    let host_result = neo_zkvm_guest::execute_batch(&bytes).expect("host execute failed");

    assert_eq!(zkvm_result.public_input_hash, host_result.public_input_hash);
    assert_ne!(host_result.post_state_root, [0u8; 32]);
    assert_ne!(host_result.receipt_root, [0u8; 32]);
    assert!(host_result.gas_consumed > 0);
}

#[test]
#[serial_test::serial]
fn execute_fresh_guest_rejects_tampered_witness() {
    if skip_when_cached_elf_is_allowed("execute_fresh_guest_rejects_tampered_witness") {
        return;
    }

    let mut bytes = stateful_fixture();
    bytes[200] ^= 1;
    let error = neo_zkvm_host::execute(&bytes).expect_err("tampered witness must fail");
    assert!(error.contains("guest reported execution error"));
}

#[test]
#[ignore]
#[serial_test::serial]
fn prove_and_verify_real_zk_proof() {
    let bytes = stateful_fixture();
    let host_result = neo_zkvm_guest::execute_batch(&bytes).expect("host execute failed");

    let proof_result = neo_zkvm_host::prove(&bytes).expect("zkVM prove failed");
    assert_eq!(356, proof_result.proof_bytes.len());
    assert_eq!(32, proof_result.vk_bytes.len());
    assert_eq!(33, proof_result.public_values.len());
    assert_eq!(0, proof_result.public_values[0]);
    assert_eq!(
        proof_result.public_values[1..],
        proof_result.public_input_hash
    );
    assert_eq!(
        proof_result.public_input_hash,
        host_result.public_input_hash
    );

    neo_zkvm_host::verify(
        &proof_result.proof_bytes,
        &proof_result.vk_bytes,
        &proof_result.public_input_hash,
    )
    .expect("proof verification failed");
}

#[test]
#[ignore]
#[serial_test::serial]
fn verify_rejects_mismatched_public_input_hash() {
    let proof_result = neo_zkvm_host::prove(&stateful_fixture()).expect("zkVM prove failed");
    let mut tampered = proof_result.public_input_hash;
    tampered[0] ^= 0xff;
    let error = neo_zkvm_host::verify(&proof_result.proof_bytes, &proof_result.vk_bytes, &tampered)
        .expect_err("tampered hash must fail verification");
    assert!(error.contains("SP1 Groth16 verification failed"));
}
