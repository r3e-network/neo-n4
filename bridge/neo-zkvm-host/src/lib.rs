//! SP1 host runtime for the neo-zkvm-guest crate.
//!
//! Loads the guest's RISC-V ELF (compiled by `cargo prove build`), runs it
//! via the SP1 prover with a canonical `ProofWitnessArtifactV1` payload, and
//! returns a serialized proof + the public-input commitment.
//!
//! End-to-end verified: `cargo test --release execute_guest_in_zkvm` proves
//! a real batch through the SP1 zkVM and checks the public-input commitment
//! matches what `neo_zkvm_guest::execute_batch` produces on the host.

#[cfg(unix)]
use sp1_sdk::blocking::{Elf, ProveRequest, Prover, ProverClient};
#[cfg(unix)]
use sp1_sdk::{HashableKey, ProvingKey, SP1Proof, SP1Stdin};
#[cfg(unix)]
use sp1_verifier::{GROTH16_VK_BYTES, Groth16Verifier};

const COMMITTED_PUBLIC_VALUES_LEN: usize = 33;
const SP1_GROTH16_PROOF_LEN: usize = 356;

#[path = "../../sp1_runtime_support.rs"]
mod sp1_runtime_support;

mod pinned {
    include!("../../neo-zkvm-guest/vk_manifest.rs");
}

/// Audited raw 32-byte verification key of the reproducibly built batch guest.
pub const NEO_ZKVM_GUEST_VK_BYTES32: [u8; 32] = pinned::PINNED_BATCH_VK_BYTES32;

/// SHA-256 of the reproducibly built batch guest ELF.
pub const NEO_ZKVM_GUEST_ELF_SHA256: [u8; 32] = pinned::PINNED_BATCH_ELF_SHA256;

/// SP1 toolchain version bound by the audited guest manifest.
pub const NEO_ZKVM_SP1_VERSION: &str = pinned::SP1_VERSION;

mod proof_result;
mod recursive_child_result;
mod zk_execution_result;

pub use proof_result::ProofResult;
pub use recursive_child_result::RecursiveChildProofResult;
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
    let prover = ProverClient::builder().cpu().build();
    let mut stdin = SP1Stdin::new();
    let request_vec = request_bytes.to_vec();
    stdin.write::<Vec<u8>>(&request_vec);

    let (public_values, report) = prover
        .execute(Elf::Static(NEO_ZKVM_GUEST_ELF), stdin)
        .run()
        .map_err(|error| format!("zkVM execute failed: {error:?}"))?;

    let public_input_hash = decode_committed_public_values(public_values.as_slice())?;

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
    sp1_runtime_support::validate_gnark_backend()?;
    let prover = ProverClient::builder().cpu().build();
    let mut stdin = SP1Stdin::new();
    let request_vec = request_bytes.to_vec();
    stdin.write::<Vec<u8>>(&request_vec);

    let pk = prover
        .setup(Elf::Static(NEO_ZKVM_GUEST_ELF))
        .map_err(|error| format!("prover setup failed: {error:?}"))?;

    let proof = prover
        .prove(&pk, stdin)
        .groth16()
        .run()
        .map_err(|error| format!("zkVM prove failed: {error:?}"))?;

    let public_values: [u8; COMMITTED_PUBLIC_VALUES_LEN] =
        proof.public_values.as_slice().try_into().map_err(|_| {
            format!(
                "unexpected public-values length: expected {}, got {}",
                COMMITTED_PUBLIC_VALUES_LEN,
                proof.public_values.as_slice().len()
            )
        })?;
    let public_input_hash = decode_committed_public_values(&public_values)?;
    let proof_bytes = proof.bytes();
    if proof_bytes.len() != SP1_GROTH16_PROOF_LEN {
        return Err(format!(
            "unexpected SP1 Groth16 proof length: expected {}, got {}",
            SP1_GROTH16_PROOF_LEN,
            proof_bytes.len()
        ));
    }
    let vk_bytes = pk.verifying_key().bytes32_raw();
    verify_groth16_artifact(&proof_bytes, &vk_bytes, &public_values)?;

    Ok(ProofResult {
        public_input_hash,
        public_values,
        proof_bytes,
        vk_bytes,
    })
}

/// Generate the compressed proof sidecar consumed by the recursive Gateway prover.
///
/// The sidecar carries the two public-input fields that are not present in
/// `L2BatchCommitment` (`l1_message_hash` and `block_context_hash`). The Gateway guest
/// reconstructs all 332 canonical public-input bytes, hashes them, and requires that hash
/// to equal the public value of this recursively verified child proof.
#[cfg(unix)]
pub fn prove_compressed(request_bytes: &[u8]) -> Result<RecursiveChildProofResult, String> {
    let artifact = neo_execution_core::parse_proof_witness_artifact(request_bytes)
        .map_err(|error| format!("parse proof witness artifact: {error}"))?;
    let expected_public_input_hash = neo_execution_core::hash_public_inputs(
        artifact.public_inputs.chain_id,
        artifact.public_inputs.batch_number,
        &artifact.public_inputs.pre_state_root,
        &artifact.public_inputs.post_state_root,
        &artifact.public_inputs.tx_root,
        &artifact.public_inputs.receipt_root,
        &artifact.public_inputs.withdrawal_root,
        &artifact.public_inputs.l2_to_l1_message_root,
        &artifact.public_inputs.l2_to_l2_message_root,
        &artifact.public_inputs.l1_message_hash,
        &artifact.public_inputs.da_commitment,
        &artifact.public_inputs.block_context_hash,
    );

    let prover = ProverClient::builder().cpu().build();
    let pk = prover
        .setup(Elf::Static(NEO_ZKVM_GUEST_ELF))
        .map_err(|error| format!("prover setup failed: {error:?}"))?;
    let mut stdin = SP1Stdin::new();
    stdin.write::<Vec<u8>>(&request_bytes.to_vec());
    let proof = prover
        .prove(&pk, stdin)
        .compressed()
        .run()
        .map_err(|error| format!("compressed proof generation failed: {error:?}"))?;
    prover
        .verify(&proof, pk.verifying_key(), None)
        .map_err(|error| format!("compressed proof verification failed: {error:?}"))?;
    if !matches!(proof.proof, SP1Proof::Compressed(_)) {
        return Err("recursive child proof is not SP1 Compressed".into());
    }
    if proof.tee_proof.is_some() {
        return Err("TEE-wrapped recursive child proofs are forbidden".into());
    }
    let public_values: [u8; COMMITTED_PUBLIC_VALUES_LEN] =
        proof.public_values.as_slice().try_into().map_err(|_| {
            format!(
                "unexpected compressed public-values length: expected {}, got {}",
                COMMITTED_PUBLIC_VALUES_LEN,
                proof.public_values.as_slice().len()
            )
        })?;
    let public_input_hash = decode_committed_public_values(&public_values)?;
    if public_input_hash != expected_public_input_hash {
        return Err(
            "compressed proof public-input hash does not match canonical artifact claims".into(),
        );
    }
    let proof_bytes = bincode::serialize(&proof)
        .map_err(|error| format!("serialize canonical compressed proof: {error}"))?;

    Ok(RecursiveChildProofResult {
        chain_id: artifact.chain_id,
        batch_number: artifact.batch_number,
        public_input_hash,
        l1_message_hash: artifact.public_inputs.l1_message_hash,
        block_context_hash: artifact.public_inputs.block_context_hash,
        proof_bytes,
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
    let vk: [u8; 32] = vk_bytes
        .try_into()
        .map_err(|_| format!("program vkey must be 32 bytes, got {}", vk_bytes.len()))?;
    let public_values = committed_public_values(expected_public_input_hash);
    verify_groth16_artifact(proof_bytes, &vk, &public_values)
}

#[cfg(unix)]
fn verify_groth16_artifact(
    proof_bytes: &[u8],
    vk_bytes: &[u8; 32],
    public_values: &[u8; COMMITTED_PUBLIC_VALUES_LEN],
) -> Result<(), String> {
    if proof_bytes.len() != SP1_GROTH16_PROOF_LEN {
        return Err(format!(
            "SP1 Groth16 proof must be {} bytes, got {}",
            SP1_GROTH16_PROOF_LEN,
            proof_bytes.len()
        ));
    }
    let vkey = format!("0x{}", hex::encode(vk_bytes));
    Groth16Verifier::verify(proof_bytes, public_values, &vkey, &GROTH16_VK_BYTES)
        .map_err(|e| format!("SP1 Groth16 verification failed: {e:?}"))
}

fn committed_public_values(public_input_hash: &[u8; 32]) -> [u8; COMMITTED_PUBLIC_VALUES_LEN] {
    let mut public_values = [0u8; COMMITTED_PUBLIC_VALUES_LEN];
    public_values[1..].copy_from_slice(public_input_hash);
    public_values
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

/// Generate a recursive compressed proof sidecar.
#[cfg(not(unix))]
pub fn prove_compressed(_request_bytes: &[u8]) -> Result<RecursiveChildProofResult, String> {
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

fn decode_committed_public_values(public_values: &[u8]) -> Result<[u8; 32], String> {
    const STATUS_LEN: usize = 1;

    if public_values.len() != COMMITTED_PUBLIC_VALUES_LEN {
        return Err(format!(
            "public values must be exactly {} bytes, got {}",
            COMMITTED_PUBLIC_VALUES_LEN,
            public_values.len()
        ));
    }

    match public_values[0] {
        0 => public_values[STATUS_LEN..COMMITTED_PUBLIC_VALUES_LEN]
            .try_into()
            .map_err(|_| "public-input hash decode failed".to_string()),
        1 => Err("guest reported execution error".to_string()),
        status => Err(format!("unknown guest status tag: {status}")),
    }
}

#[cfg(test)]
mod tests {
    use super::decode_committed_public_values;
    #[cfg(unix)]
    use super::verify;

    #[test]
    fn decode_committed_public_values_skips_status_tag() {
        let expected_hash = [0x42u8; 32];
        let mut committed = Vec::with_capacity(33);
        committed.push(0);
        committed.extend_from_slice(&expected_hash);

        let decoded = decode_committed_public_values(&committed).unwrap();

        assert_eq!(decoded, expected_hash);
    }

    #[test]
    fn decode_committed_public_values_rejects_guest_error_tag() {
        let mut committed = Vec::with_capacity(33);
        committed.push(1);
        committed.extend_from_slice(&[0u8; 32]);

        let error = decode_committed_public_values(&committed).unwrap_err();

        assert!(error.contains("guest reported execution error"));
    }

    #[test]
    fn decode_committed_public_values_rejects_truncated_values() {
        let error = decode_committed_public_values(&[0, 1, 2]).unwrap_err();

        assert!(error.contains("public values must be exactly"));
    }

    #[test]
    fn decode_committed_public_values_rejects_uncommitted_suffix() {
        let error = decode_committed_public_values(&[0; 34]).unwrap_err();

        assert!(error.contains("public values must be exactly"));
    }

    #[cfg(unix)]
    #[test]
    fn verify_rejects_noncanonical_artifact_lengths_before_crypto() {
        let hash = [0u8; 32];
        let error = verify(&[0; 355], &[0; 32], &hash).unwrap_err();
        assert!(error.contains("must be 356 bytes"));

        let error = verify(&[0; 356], &[0; 31], &hash).unwrap_err();
        assert!(error.contains("program vkey must be 32 bytes"));
    }
}
