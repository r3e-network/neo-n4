#![no_main]

use neo_zkvm_gateway_guest::{
    BATCH_VK_WORDS, expected_child_public_values, gateway_public_values, parse_request,
    validate_public_input_supplements,
};
use sha2::{Digest, Sha256};

sp1_zkvm::entrypoint!(main);

pub fn main() {
    let request_bytes = sp1_zkvm::io::read::<Vec<u8>>();
    let public_input_supplements = sp1_zkvm::io::read::<Vec<Vec<u8>>>();
    let request = parse_request(&request_bytes).expect("invalid canonical NEO4GWP1 request");
    validate_public_input_supplements(&request, &public_input_supplements)
        .expect("batch commitment fields do not match recursively proven public inputs");

    for public_values in expected_child_public_values(&request) {
        let digest: [u8; 32] = Sha256::digest(public_values).into();
        sp1_zkvm::lib::verify::verify_sp1_proof(&BATCH_VK_WORDS, &digest);
    }

    sp1_zkvm::io::commit_slice(&gateway_public_values(&request.binding));
}
