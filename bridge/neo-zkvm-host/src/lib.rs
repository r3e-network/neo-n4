//! SP1 host runtime for the neo-zkvm-guest crate.
//!
//! Loads the guest's RISC-V ELF (compiled by `cargo prove build`), runs it
//! via the SP1 prover with a canonical `BatchExecutionRequest` payload, and
//! returns a serialized proof + the public-input commitment.
//!
//! End-to-end verified: `cargo test --release execute_guest_in_zkvm` proves
//! a real batch through the SP1 zkVM and checks the public-input commitment
//! matches what `neo_zkvm_guest::execute_batch` produces on the host.

#[cfg(unix)]
use sp1_sdk::blocking::{Elf, ProveRequest, Prover, ProverClient};
#[cfg(unix)]
use sp1_sdk::{ProvingKey, SP1Stdin};

mod proof_result;
mod zk_execution_result;

pub use proof_result::ProofResult;
pub use zk_execution_result::ZkExecutionResult;

/// Embedded guest ELF — wired by build.rs from `cargo prove build`.
#[cfg(unix)]
pub(crate) const NEO_ZKVM_GUEST_ELF: &[u8] = include_bytes!(env!("NEO_ZKVM_GUEST_ELF"));

/// Empty placeholder on platforms where SP1's JIT/prover stack is unavailable.
#[cfg(not(unix))]
pub(crate) const NEO_ZKVM_GUEST_ELF: &[u8] = &[];

/// Execute the guest inside SP1's zkVM (no proving — just the deterministic
/// run). Cheap; suitable for development + the "did the script HALT" check.
#[cfg(unix)]
pub fn execute(request_bytes: &[u8]) -> Result<ZkExecutionResult, String> {
    // SP1 6.x API: build a CPU prover, call execute(elf, stdin).
    let prover = ProverClient::builder().cpu().build();
    let mut stdin = SP1Stdin::new();
    stdin.write::<Vec<u8>>(&request_bytes.to_vec());

    let (mut public_values, report) = prover
        .execute(Elf::Static(NEO_ZKVM_GUEST_ELF), stdin)
        .run()
        .map_err(|e| format!("zkVM execute failed: {:?}", e))?;

    let public_input_hash: [u8; 32] = public_values.read::<[u8; 32]>();

    Ok(ZkExecutionResult {
        public_input_hash,
        cycles: report.total_instruction_count(),
    })
}

/// Generate a real ZK proof for the batch — the on-chain settlement artifact.
///
/// This is what an L2's prover infrastructure runs after each batch is sealed:
/// the proof bytes get submitted via `NeoHub.SettlementManager.SubmitBatch`,
/// `VerifierRegistry` dispatches to the registered verifier, and the chain
/// finalizes if the proof verifies against the canonical public-input hash.
///
/// Substantially slower than `execute` (proving ≫ executing — typically
/// 30-300 seconds per batch on a beefy CPU; minutes on a small one). For
/// development / debug, prefer `execute`.
#[cfg(unix)]
pub fn prove(request_bytes: &[u8]) -> Result<ProofResult, String> {
    let prover = ProverClient::builder().cpu().build();
    let mut stdin = SP1Stdin::new();
    stdin.write::<Vec<u8>>(&request_bytes.to_vec());

    let pk = prover
        .setup(Elf::Static(NEO_ZKVM_GUEST_ELF))
        .map_err(|e| format!("prover setup failed: {:?}", e))?;

    let vk_bytes = bincode::serialize(pk.verifying_key())
        .map_err(|e| format!("vk serialization failed: {}", e))?;

    let proof = prover
        .prove(&pk, stdin)
        .run()
        .map_err(|e| format!("zkVM prove failed: {:?}", e))?;

    let mut public_values = proof.public_values.clone();
    let public_input_hash: [u8; 32] = public_values.read::<[u8; 32]>();

    let proof_bytes =
        bincode::serialize(&proof).map_err(|e| format!("proof serialization failed: {}", e))?;

    Ok(ProofResult {
        public_input_hash,
        proof_bytes,
        vk_bytes,
    })
}

/// Verify a previously-generated proof against the canonical guest verifying key.
///
/// Mirrors what the on-chain `VerifierRegistry` dispatch path does: deserialize
/// the proof + vk, run SP1's verifier, and confirm the public-input hash matches
/// the expected commitment for this batch. Off-chain prover infrastructure can
/// call this before submission to catch bad proofs without paying L1 gas.
#[cfg(unix)]
pub fn verify(
    proof_bytes: &[u8],
    vk_bytes: &[u8],
    expected_public_input_hash: &[u8; 32],
) -> Result<(), String> {
    let proof: sp1_sdk::SP1ProofWithPublicValues =
        bincode::deserialize(proof_bytes).map_err(|e| format!("proof decode: {}", e))?;
    let vk: sp1_sdk::SP1VerifyingKey =
        bincode::deserialize(vk_bytes).map_err(|e| format!("vk decode: {}", e))?;

    // Public-input commitment check first — cheap, catches replay / mismatch
    // before we burn cycles on the cryptographic verifier.
    let mut public_values = proof.public_values.clone();
    let actual: [u8; 32] = public_values.read::<[u8; 32]>();
    if &actual != expected_public_input_hash {
        return Err(format!(
            "public-input hash mismatch: expected {:?}, got {:?}",
            expected_public_input_hash, actual
        ));
    }

    let prover = ProverClient::builder().cpu().build();
    prover
        .verify(&proof, &vk, None)
        .map_err(|e| format!("proof verification failed: {:?}", e))?;
    Ok(())
}

/// Execute the guest inside SP1's zkVM.
#[cfg(not(unix))]
pub fn execute(_request_bytes: &[u8]) -> Result<ZkExecutionResult, String> {
    Err(unsupported_platform())
}

/// Generate a real ZK proof for the batch.
#[cfg(not(unix))]
pub fn prove(_request_bytes: &[u8]) -> Result<ProofResult, String> {
    Err(unsupported_platform())
}

/// Verify a previously-generated proof against the canonical guest verifying key.
#[cfg(not(unix))]
pub fn verify(
    _proof_bytes: &[u8],
    _vk_bytes: &[u8],
    _expected_public_input_hash: &[u8; 32],
) -> Result<(), String> {
    Err(unsupported_platform())
}

#[cfg(not(unix))]
fn unsupported_platform() -> String {
    "neo-zkvm-host requires a Unix/WSL2/Linux target because SP1's JIT/prover stack uses POSIX file descriptors and shared memory".to_string()
}
