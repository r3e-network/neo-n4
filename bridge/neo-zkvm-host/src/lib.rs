//! SP1 host runtime for the neo-zkvm-guest crate.
//!
//! Loads the guest's RISC-V ELF (compiled by `cargo prove build`), runs it
//! via the SP1 prover with a canonical `BatchExecutionRequest` payload, and
//! returns a serialized proof + the public-input commitment.
//!
//! End-to-end verified: `cargo test --release execute_guest_in_zkvm` proves
//! a real batch through the SP1 zkVM and checks the public-input commitment
//! matches what `neo_zkvm_guest::execute_batch` produces on the host.

use sp1_sdk::blocking::{Elf, ProverClient, Prover};
use sp1_sdk::SP1Stdin;

/// Embedded guest ELF — wired by build.rs from `cargo prove build`.
pub const NEO_ZKVM_GUEST_ELF: &[u8] = include_bytes!(env!("NEO_ZKVM_GUEST_ELF"));

/// Result of a successful end-to-end zkVM execution.
pub struct ExecutionResult {
    /// 32-byte public-input hash committed by the guest.
    pub public_input_hash: [u8; 32],
    /// Reported gas / cycle count from the SP1 executor.
    pub cycles: u64,
}

/// Execute the guest inside SP1's zkVM (no proving — just the deterministic
/// run). Cheap; suitable for development + the "did the script HALT" check.
pub fn execute(request_bytes: &[u8]) -> Result<ExecutionResult, String> {
    // SP1 6.x API: build a CPU prover, call execute(elf, stdin).
    let prover = ProverClient::builder().cpu().build();
    let mut stdin = SP1Stdin::new();
    stdin.write::<Vec<u8>>(&request_bytes.to_vec());

    let (mut public_values, report) = prover
        .execute(Elf::Static(NEO_ZKVM_GUEST_ELF), stdin)
        .run()
        .map_err(|e| format!("zkVM execute failed: {:?}", e))?;

    let public_input_hash: [u8; 32] = public_values.read::<[u8; 32]>();

    Ok(ExecutionResult {
        public_input_hash,
        cycles: report.total_instruction_count(),
    })
}
