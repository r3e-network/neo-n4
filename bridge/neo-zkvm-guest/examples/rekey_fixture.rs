use neo_execution_core::{encode_proof_witness_artifact, parse_proof_witness_artifact};

#[path = "../vk_manifest.rs"]
#[allow(dead_code)]
mod pinned;

fn main() {
    let path = std::env::args()
        .nth(1)
        .expect("usage: cargo run -p neo-zkvm-guest --example rekey_fixture -- <fixture.hex>");
    let source = std::fs::read_to_string(&path).expect("read fixture");
    let bytes = hex::decode(source.split_whitespace().collect::<String>()).expect("fixture hex");
    let mut artifact = parse_proof_witness_artifact(&bytes).expect("proof witness artifact");
    artifact.verification_key_id = pinned::PINNED_BATCH_VK_BYTES32;
    let encoded = encode_proof_witness_artifact(&artifact).expect("canonical fixture encoding");
    println!("{}", hex::encode(encoded));
}
