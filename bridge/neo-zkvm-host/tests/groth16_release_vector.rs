#![cfg(unix)]

use serde::Deserialize;
use sha2::{Digest, Sha256};

const VECTOR_JSON: &str =
    include_str!("../../../tests/fixtures/sp1-groth16-positive-vector-v1.json");
const WITNESS_HEX: &str =
    include_str!("../../neo-zkvm-guest/tests/fixtures/native_transition_v1.hex");

#[derive(Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct Groth16ReleaseVector {
    schema_version: u32,
    sp1_version: String,
    guest_elf_sha256: String,
    witness_path: String,
    witness_sha256: String,
    proof_hex: String,
    proof_sha256: String,
    public_values_hex: String,
    public_values_sha256: String,
    program_vkey_hex: String,
    program_vkey_sha256: String,
    public_input_hash_hex: String,
    public_input_hash_sha256: String,
}

#[test]
fn committed_vector_verifies_against_current_guest_and_witness() {
    let vector: Groth16ReleaseVector = serde_json::from_str(VECTOR_JSON).expect("vector JSON");
    let witness = hex::decode(WITNESS_HEX.split_whitespace().collect::<String>())
        .expect("native witness hex");
    let proof = decode(&vector.proof_hex, "proof");
    let public_values = decode(&vector.public_values_hex, "public values");
    let program_vkey = decode(&vector.program_vkey_hex, "program VK");
    let public_input_hash = decode(&vector.public_input_hash_hex, "public-input hash");

    assert_eq!(1, vector.schema_version);
    assert_eq!(neo_zkvm_host::NEO_ZKVM_SP1_VERSION, vector.sp1_version);
    assert_eq!(
        hex::encode(neo_zkvm_host::NEO_ZKVM_GUEST_ELF_SHA256),
        vector.guest_elf_sha256
    );
    assert_eq!(
        "bridge/neo-zkvm-guest/tests/fixtures/native_transition_v1.hex",
        vector.witness_path
    );
    assert_eq!(sha256_hex(&witness), vector.witness_sha256);
    assert_eq!(sha256_hex(&proof), vector.proof_sha256);
    assert_eq!(sha256_hex(&public_values), vector.public_values_sha256);
    assert_eq!(sha256_hex(&program_vkey), vector.program_vkey_sha256);
    assert_eq!(
        sha256_hex(&public_input_hash),
        vector.public_input_hash_sha256
    );

    assert_eq!(356, proof.len());
    assert_eq!(33, public_values.len());
    assert_eq!(32, program_vkey.len());
    assert_eq!(32, public_input_hash.len());
    assert_eq!(0, public_values[0]);
    assert_eq!(public_input_hash, public_values[1..]);
    assert_eq!(
        neo_zkvm_host::NEO_ZKVM_GUEST_VK_BYTES32.as_slice(),
        program_vkey
    );

    let native_result = neo_zkvm_guest::execute_batch(&witness).expect("native guest execution");
    assert_eq!(
        native_result.public_input_hash.as_slice(),
        public_input_hash
    );

    let expected_hash: [u8; 32] = public_input_hash.try_into().expect("32-byte hash");
    neo_zkvm_host::verify(&proof, &program_vkey, &expected_hash)
        .expect("committed Groth16 vector must verify");
}

fn decode(value: &str, label: &str) -> Vec<u8> {
    hex::decode(value).unwrap_or_else(|error| panic!("invalid {label} hex: {error}"))
}

fn sha256_hex(bytes: &[u8]) -> String {
    hex::encode(Sha256::digest(bytes))
}
