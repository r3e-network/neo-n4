use std::{
    fs,
    path::{Path, PathBuf},
    process::Command,
    time::{SystemTime, UNIX_EPOCH},
};

use neo_execution_core::{
    encode_execution_payload, encode_state_witness, hash256, parse_native_execution_output,
    parse_proof_witness_artifact,
};

const FIXTURE_HEX: &str = include_str!("fixtures/stateful_batch_v1.hex");

#[test]
fn native_executor_cli_executes_exact_guest_runtime_and_binds_requests() {
    let fixture = hex::decode(FIXTURE_HEX.split_whitespace().collect::<String>()).unwrap();
    let artifact = parse_proof_witness_artifact(&fixture).unwrap();
    let payload = encode_execution_payload(&artifact.execution_payload).unwrap();
    let witness = encode_state_witness(&artifact.state_witness).unwrap();
    let directory = TestDirectory::new();
    let payload_path = directory.path().join("payload.bin");
    let witness_path = directory.path().join("witness.bin");
    let output_path = directory.path().join("output.bin");
    fs::write(&payload_path, &payload).unwrap();
    fs::write(&witness_path, &witness).unwrap();

    let result = Command::new(env!("CARGO_BIN_EXE_neo-zkvm-executor"))
        .arg("--payload")
        .arg(&payload_path)
        .arg("--state-witness")
        .arg(&witness_path)
        .arg("--output")
        .arg(&output_path)
        .output()
        .unwrap();

    assert!(
        result.status.success(),
        "{}",
        String::from_utf8_lossy(&result.stderr)
    );
    assert!(result.stdout.is_empty());
    let output = parse_native_execution_output(&fs::read(output_path).unwrap()).unwrap();
    assert_eq!(output.request_payload_hash, hash256(&payload));
    assert_eq!(output.request_state_witness_hash, hash256(&witness));
    assert_eq!(output.execution_result, artifact.execution_result);
    assert_eq!(output.effects_bytes, artifact.effects_bytes);
    assert_eq!(output.public_input_hash, artifact.public_inputs_hash());
}

#[test]
fn native_executor_cli_rejects_malformed_state_without_output() {
    let fixture = hex::decode(FIXTURE_HEX.split_whitespace().collect::<String>()).unwrap();
    let artifact = parse_proof_witness_artifact(&fixture).unwrap();
    let directory = TestDirectory::new();
    let payload_path = directory.path().join("payload.bin");
    let witness_path = directory.path().join("witness.bin");
    let output_path = directory.path().join("output.bin");
    fs::write(
        &payload_path,
        encode_execution_payload(&artifact.execution_payload).unwrap(),
    )
    .unwrap();
    fs::write(&witness_path, [0x00]).unwrap();

    let result = Command::new(env!("CARGO_BIN_EXE_neo-zkvm-executor"))
        .arg("--payload")
        .arg(&payload_path)
        .arg("--state-witness")
        .arg(&witness_path)
        .arg("--output")
        .arg(&output_path)
        .output()
        .unwrap();

    assert!(!result.status.success());
    assert!(!output_path.exists());
    assert!(String::from_utf8_lossy(&result.stderr).contains("neo-zkvm-executor"));
}

trait PublicInputsHash {
    fn public_inputs_hash(&self) -> [u8; 32];
}

impl PublicInputsHash for neo_execution_core::ProofWitnessArtifact {
    fn public_inputs_hash(&self) -> [u8; 32] {
        neo_execution_core::hash_public_inputs(
            self.public_inputs.chain_id,
            self.public_inputs.batch_number,
            &self.public_inputs.pre_state_root,
            &self.public_inputs.post_state_root,
            &self.public_inputs.tx_root,
            &self.public_inputs.receipt_root,
            &self.public_inputs.withdrawal_root,
            &self.public_inputs.l2_to_l1_message_root,
            &self.public_inputs.l2_to_l2_message_root,
            &self.public_inputs.l1_message_hash,
            &self.public_inputs.da_commitment,
            &self.public_inputs.block_context_hash,
        )
    }
}

struct TestDirectory(PathBuf);

impl TestDirectory {
    fn new() -> Self {
        let nonce = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_nanos();
        let path = std::env::temp_dir().join(format!(
            "neo-n4-native-executor-{}-{nonce}",
            std::process::id()
        ));
        fs::create_dir(&path).unwrap();
        Self(path)
    }

    fn path(&self) -> &Path {
        &self.0
    }
}

impl Drop for TestDirectory {
    fn drop(&mut self) {
        let _ = fs::remove_dir_all(&self.0);
    }
}
