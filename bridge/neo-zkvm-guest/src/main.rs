//! SP1 ZK guest entrypoint: reads canonical `ProofWitnessArtifactV1` bytes
//! from the SP1 input stream, runs the deterministic `execute_batch`
//! function from the lib crate, and commits the public-input hash.
//!
//! The pure execution functions live in `src/lib.rs` so they can be
//! unit-tested on the host without the SP1 toolchain.

#![no_main]

sp1_zkvm::entrypoint!(main);

use neo_zkvm_guest::execute_batch;

pub fn main() {
    let bytes = sp1_zkvm::io::read::<Vec<u8>>();
    match execute_batch(&bytes) {
        Ok(result) => {
            sp1_zkvm::io::commit::<u8>(&0); // success tag
            sp1_zkvm::io::commit::<[u8; 32]>(&result.public_input_hash);
        }
        Err(_) => {
            // Propagate the error cleanly via the commit channel instead of
            // panicking. The host detects the error tag and surfaces the
            // failure without needing the zkVM to format the error message.
            sp1_zkvm::io::commit::<u8>(&1); // error tag
            sp1_zkvm::io::commit::<[u8; 32]>(&[0u8; 32]);
        }
    }
}
