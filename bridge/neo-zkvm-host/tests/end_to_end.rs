//! End-to-end test: runs a real `BatchExecutionRequest` through the SP1
//! zkVM by loading the compiled guest ELF, then cross-checks the
//! public-input hash against what `neo_zkvm_guest::execute_batch` produces
//! on the host. Pin: zkVM execution and host execution agree byte-for-byte.

#![cfg(unix)]

fn build_minimal_request() -> Vec<u8> {
    let mut buf = Vec::new();
    buf.push(1u8); // version
    buf.extend_from_slice(&1099u32.to_le_bytes()); // chainId
    buf.extend_from_slice(&7u64.to_le_bytes()); // batchNumber
    buf.extend_from_slice(&[0u8; 32]); // preStateRoot
    buf.extend_from_slice(&[0xCDu8; 32]); // daCommitment
    buf.extend_from_slice(&0u32.to_le_bytes()); // 0 L1 messages
    buf.extend_from_slice(&1u32.to_le_bytes()); // 1 tx
    buf.extend_from_slice(&3u32.to_le_bytes()); // tx0 len = 3
    buf.extend_from_slice(&[0xAA, 0xBB, 0xCC]); // tx0 bytes
    buf
}

#[test]
#[serial_test::serial]
fn execute_guest_in_zkvm_matches_host_run() {
    let bytes = build_minimal_request();
    // Run inside SP1 zkVM
    let zkvm_result = neo_zkvm_host::execute(&bytes).expect("zkVM execute failed");
    // Run on the host (same code, no zkVM)
    let host_result = neo_zkvm_guest::execute_batch(&bytes).expect("host execute failed");
    // Public-input hash must match byte-for-byte.
    assert_eq!(
        zkvm_result.public_input_hash, host_result.public_input_hash,
        "zkVM and host execution must agree on the public-input hash"
    );
    println!(
        "✅ guest in zkVM committed {:?} after {} cycles",
        zkvm_result.public_input_hash, zkvm_result.cycles
    );
}

/// Generate a real cryptographic proof + verify it. Gated behind `--ignored`
/// because CPU proving for even a minimal batch takes minutes; running it in
/// every CI loop would dominate the test budget. Run manually with:
///
/// ```text
/// CPATH=/home/neo/.local/include cargo test --release \
///     -p neo-zkvm-host --test end_to_end -- --ignored --nocapture
/// ```
#[test]
#[ignore]
#[serial_test::serial]
fn prove_and_verify_real_zk_proof() {
    let bytes = build_minimal_request();
    let host_result = neo_zkvm_guest::execute_batch(&bytes).expect("host execute failed");

    let t0 = std::time::Instant::now();
    let proof_result = neo_zkvm_host::prove(&bytes).expect("zkVM prove failed");
    let prove_elapsed = t0.elapsed();
    println!(
        "✅ proof generated in {:?}: {} bytes (vk: {} bytes)",
        prove_elapsed,
        proof_result.proof_bytes.len(),
        proof_result.vk_bytes.len()
    );

    assert_eq!(
        proof_result.public_input_hash, host_result.public_input_hash,
        "prove() public-input hash must match host execute_batch()"
    );

    let t1 = std::time::Instant::now();
    neo_zkvm_host::verify(
        &proof_result.proof_bytes,
        &proof_result.vk_bytes,
        &proof_result.public_input_hash,
    )
    .expect("proof verification failed");
    println!("✅ proof verified in {:?}", t1.elapsed());
}

/// Negative test: a proof with the wrong expected public-input hash must
/// be rejected. Also #[ignore]-gated for the same proving-cost reason.
#[test]
#[ignore]
#[serial_test::serial]
fn verify_rejects_mismatched_public_input_hash() {
    let bytes = build_minimal_request();
    let proof_result = neo_zkvm_host::prove(&bytes).expect("zkVM prove failed");

    let mut tampered = proof_result.public_input_hash;
    tampered[0] ^= 0xFF;
    let err = neo_zkvm_host::verify(&proof_result.proof_bytes, &proof_result.vk_bytes, &tampered)
        .expect_err("tampered hash must fail verification");
    assert!(
        err.contains("public-input hash mismatch"),
        "expected hash-mismatch error, got: {}",
        err
    );
}
