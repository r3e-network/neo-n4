use serde::Serialize;
use sha2::{Digest, Sha256};
use std::fs;
use std::io;
use std::path::{Path, PathBuf};

const WITNESS_PATH: &str = "bridge/neo-zkvm-guest/tests/fixtures/native_transition_v1.hex";
const WITNESS_HEX: &str =
    include_str!("../../neo-zkvm-guest/tests/fixtures/native_transition_v1.hex");

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct Groth16ReleaseVector {
    schema_version: u32,
    sp1_version: &'static str,
    guest_elf_sha256: String,
    witness_path: &'static str,
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

fn main() -> Result<(), Box<dyn std::error::Error>> {
    let witness = hex::decode(WITNESS_HEX.split_whitespace().collect::<String>())?;
    let expected = neo_zkvm_guest::execute_batch(&witness).map_err(io::Error::other)?;
    let proof = neo_zkvm_host::prove(&witness).map_err(io::Error::other)?;

    if proof.public_input_hash != expected.public_input_hash {
        return Err(io::Error::other("proof public input does not match native execution").into());
    }
    if proof.vk_bytes != neo_zkvm_host::NEO_ZKVM_GUEST_VK_BYTES32 {
        return Err(io::Error::other("proof program VK does not match the pinned guest VK").into());
    }
    neo_zkvm_host::verify(
        &proof.proof_bytes,
        &proof.vk_bytes,
        &proof.public_input_hash,
    )
    .map_err(io::Error::other)?;

    let vector = Groth16ReleaseVector {
        schema_version: 1,
        sp1_version: neo_zkvm_host::NEO_ZKVM_SP1_VERSION,
        guest_elf_sha256: hex::encode(neo_zkvm_host::NEO_ZKVM_GUEST_ELF_SHA256),
        witness_path: WITNESS_PATH,
        witness_sha256: sha256_hex(&witness),
        proof_hex: hex::encode(&proof.proof_bytes),
        proof_sha256: sha256_hex(&proof.proof_bytes),
        public_values_hex: hex::encode(proof.public_values),
        public_values_sha256: sha256_hex(&proof.public_values),
        program_vkey_hex: hex::encode(proof.vk_bytes),
        program_vkey_sha256: sha256_hex(&proof.vk_bytes),
        public_input_hash_hex: hex::encode(proof.public_input_hash),
        public_input_hash_sha256: sha256_hex(&proof.public_input_hash),
    };

    let output = std::env::args_os()
        .nth(1)
        .map_or_else(default_output, PathBuf::from);
    write_atomically(&output, &serde_json::to_vec_pretty(&vector)?)?;
    println!("wrote {}", output.display());
    Ok(())
}

fn default_output() -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("../..")
        .join("tests/fixtures/sp1-groth16-positive-vector-v1.json")
}

fn write_atomically(output: &Path, bytes: &[u8]) -> Result<(), Box<dyn std::error::Error>> {
    let parent = output
        .parent()
        .ok_or_else(|| io::Error::other("release-vector output must have a parent directory"))?;
    fs::create_dir_all(parent)?;
    let temporary = output.with_extension("json.tmp");
    let mut payload = bytes.to_vec();
    payload.push(b'\n');
    fs::write(&temporary, payload)?;
    fs::rename(temporary, output)?;
    Ok(())
}

fn sha256_hex(bytes: &[u8]) -> String {
    hex::encode(Sha256::digest(bytes))
}
