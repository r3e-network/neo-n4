use crate::{
    BATCH_GUEST_ELF, BATCH_VK_BYTES32, BATCH_VK_WORDS, GATEWAY_GUEST_ELF, GATEWAY_VK_BYTES32,
    TEST_ONLY_BUILD,
    protocol::{
        GROTH16_PROOF_BYTES, GatewayError, GatewayProofArtifacts, canonical_sidecar_filename,
    },
};
use bincode::Options;
use neo_zkvm_gateway_guest::{
    GatewayRequest, MAX_CHILD_SIDECAR_BYTES, PUBLIC_INPUT_SUPPLEMENT_BYTES,
    expected_child_public_values, gateway_public_values, parse_child_sidecar,
    parse_request_with_gateway_vk,
};
use sp1_sdk::{
    HashableKey, ProvingKey, SP1Proof, SP1ProofWithPublicValues, SP1Stdin,
    blocking::{Elf, ProveRequest, Prover, ProverClient},
};
use sp1_verifier::{GROTH16_VK_BYTES, Groth16Verifier};
use std::{fs::OpenOptions, io::Read, os::unix::fs::OpenOptionsExt, path::Path};

#[must_use]
pub const fn gateway_verification_key() -> [u8; 32] {
    GATEWAY_VK_BYTES32
}

pub fn prove_request(
    request: &GatewayRequest,
    request_bytes: &[u8],
    child_proof_directory: &Path,
) -> Result<GatewayProofArtifacts, GatewayError> {
    if TEST_ONLY_BUILD {
        return Err(GatewayError::Proving(
            "test-only-vk build cannot generate production proofs".into(),
        ));
    }
    let parsed = parse_request_with_gateway_vk(request_bytes, Some(&GATEWAY_VK_BYTES32))
        .map_err(|error| GatewayError::Request(error.to_string()))?;
    if &parsed != request {
        return Err(GatewayError::Request(
            "request bytes do not exactly match the parsed Gateway statement".into(),
        ));
    }
    let child_directory_metadata = std::fs::symlink_metadata(child_proof_directory)
        .map_err(|error| GatewayError::io(child_proof_directory, error))?;
    if child_directory_metadata.file_type().is_symlink()
        || !child_directory_metadata.file_type().is_dir()
    {
        return Err(GatewayError::ChildProof(
            "child-proof directory must be a non-symlink directory".into(),
        ));
    }

    let prover = ProverClient::builder().cpu().build();
    let batch_pk = prover
        .setup(Elf::Static(BATCH_GUEST_ELF))
        .map_err(|error| GatewayError::Proving(format!("batch setup: {error:?}")))?;
    if batch_pk.verifying_key().hash_u32() != BATCH_VK_WORDS
        || batch_pk.verifying_key().bytes32_raw() != BATCH_VK_BYTES32
    {
        return Err(GatewayError::Proving(
            "embedded batch ELF does not match the build-locked batch VK".into(),
        ));
    }
    let gateway_pk = prover
        .setup(Elf::Static(GATEWAY_GUEST_ELF))
        .map_err(|error| GatewayError::Proving(format!("Gateway setup: {error:?}")))?;
    if gateway_pk.verifying_key().bytes32_raw() != GATEWAY_VK_BYTES32 {
        return Err(GatewayError::Proving(
            "embedded Gateway ELF does not match the build-locked Gateway VK".into(),
        ));
    }

    let expected_public_values = expected_child_public_values(request);
    let mut child_proofs = Vec::with_capacity(request.constituents.len());
    let mut public_input_supplements = Vec::with_capacity(request.constituents.len());
    for (batch, expected) in request.constituents.iter().zip(&expected_public_values) {
        let filename = canonical_sidecar_filename(
            batch.chain_id,
            batch.batch_number,
            &batch.public_input_hash,
        );
        let path = child_proof_directory.join(filename);
        let loaded = load_child_proof(
            &path,
            batch.chain_id,
            batch.batch_number,
            &batch.public_input_hash,
        )?;
        let proof = loaded.proof;
        validate_child_proof(&proof, expected)?;
        prover
            .verify(&proof, batch_pk.verifying_key(), None)
            .map_err(|error| {
                GatewayError::ChildProof(format!(
                    "SP1 verification failed for ({}, {}): {error:?}",
                    batch.chain_id, batch.batch_number
                ))
            })?;
        child_proofs.push(proof);
        public_input_supplements.push(loaded.public_input_supplement.to_vec());
    }

    let mut stdin = SP1Stdin::new();
    stdin.write::<Vec<u8>>(&request_bytes.to_vec());
    stdin.write::<Vec<Vec<u8>>>(&public_input_supplements);
    for proof in child_proofs {
        let SP1Proof::Compressed(compressed) = proof.proof else {
            unreachable!("proof kind validated before deferred-proof insertion")
        };
        stdin.write_proof(*compressed, batch_pk.verifying_key().vk.clone());
    }

    let proof = prover
        .prove(&gateway_pk, stdin)
        .groth16()
        .run()
        .map_err(|error| GatewayError::Proving(format!("recursive Groth16 proving: {error:?}")))?;
    prover
        .verify(&proof, gateway_pk.verifying_key(), None)
        .map_err(|error| GatewayError::Proving(format!("host Gateway verification: {error:?}")))?;
    if !matches!(proof.proof, SP1Proof::Groth16(_)) {
        return Err(GatewayError::Proving(
            "Gateway terminal proof is not Groth16".into(),
        ));
    }
    let expected_gateway_public_values = gateway_public_values(&request.binding);
    let public_values: [u8; 33] = proof.public_values.as_slice().try_into().map_err(|_| {
        GatewayError::Proving(format!(
            "Gateway public values must be 33 bytes, got {}",
            proof.public_values.as_slice().len()
        ))
    })?;
    if public_values != expected_gateway_public_values {
        return Err(GatewayError::Proving(
            "Gateway public values are not 0x00 || Hash256(binding170)".into(),
        ));
    }
    let proof_bytes = proof.bytes();
    let artifacts = GatewayProofArtifacts {
        proof_bytes,
        verification_key: GATEWAY_VK_BYTES32,
        public_values,
    };
    verify_gateway_artifacts(&artifacts, &expected_gateway_public_values)?;
    Ok(artifacts)
}

pub(crate) fn verify_gateway_artifacts(
    artifacts: &GatewayProofArtifacts,
    expected_public_values: &[u8; 33],
) -> Result<(), GatewayError> {
    if artifacts.proof_bytes.len() != GROTH16_PROOF_BYTES {
        return Err(GatewayError::Proving(format!(
            "Gateway Groth16 proof must be {GROTH16_PROOF_BYTES} bytes, got {}",
            artifacts.proof_bytes.len()
        )));
    }
    if artifacts.verification_key != GATEWAY_VK_BYTES32 {
        return Err(GatewayError::Proving(
            "Gateway artifact verification key is not the build-locked key".into(),
        ));
    }
    if &artifacts.public_values != expected_public_values {
        return Err(GatewayError::Proving(
            "Gateway artifact public values do not match the request binding".into(),
        ));
    }
    let vkey = format!("0x{}", hex::encode(artifacts.verification_key));
    Groth16Verifier::verify(
        &artifacts.proof_bytes,
        &artifacts.public_values,
        &vkey,
        &GROTH16_VK_BYTES,
    )
    .map_err(|error| GatewayError::Proving(format!("terminal Groth16 verify: {error:?}")))
}

fn validate_child_proof(
    proof: &SP1ProofWithPublicValues,
    expected_public_values: &[u8; 33],
) -> Result<(), GatewayError> {
    if !matches!(proof.proof, SP1Proof::Compressed(_)) {
        return Err(GatewayError::ChildProof(
            "sidecar proof kind must be SP1 Compressed; Core, Plonk, and Groth16 children are forbidden"
                .into(),
        ));
    }
    if proof.tee_proof.is_some() {
        return Err(GatewayError::ChildProof(
            "TEE-wrapped child proofs are forbidden".into(),
        ));
    }
    validate_child_sp1_version(proof)?;
    if proof.public_values.as_slice() != expected_public_values {
        return Err(GatewayError::ChildProof(
            "child public values must be exactly 0x00 || batch.PublicInputHash".into(),
        ));
    }
    Ok(())
}

fn validate_child_sp1_version(proof: &SP1ProofWithPublicValues) -> Result<(), GatewayError> {
    if proof.sp1_version != sp1_sdk::SP1_CIRCUIT_VERSION {
        return Err(GatewayError::ChildProof(format!(
            "child proof SP1 circuit version must be {}, got {}",
            sp1_sdk::SP1_CIRCUIT_VERSION,
            proof.sp1_version
        )));
    }
    Ok(())
}

struct LoadedChildProof {
    proof: SP1ProofWithPublicValues,
    public_input_supplement: [u8; PUBLIC_INPUT_SUPPLEMENT_BYTES],
}

fn load_child_proof(
    path: &Path,
    expected_chain_id: u32,
    expected_batch_number: u64,
    expected_public_input_hash: &[u8; 32],
) -> Result<LoadedChildProof, GatewayError> {
    let mut file = OpenOptions::new()
        .read(true)
        .custom_flags(libc::O_NOFOLLOW)
        .open(path)
        .map_err(|error| GatewayError::io(path, error))?;
    let metadata = file
        .metadata()
        .map_err(|error| GatewayError::io(path, error))?;
    if !metadata.file_type().is_file() {
        return Err(GatewayError::ChildProof(format!(
            "sidecar must be a regular non-symlink file: {}",
            path.display()
        )));
    }
    if metadata.len() > MAX_CHILD_SIDECAR_BYTES as u64 {
        return Err(GatewayError::ChildProof(format!(
            "sidecar exceeds {} bytes: {}",
            MAX_CHILD_SIDECAR_BYTES,
            path.display()
        )));
    }
    let mut bytes = Vec::with_capacity(metadata.len() as usize);
    (&mut file)
        .take(MAX_CHILD_SIDECAR_BYTES as u64 + 1)
        .read_to_end(&mut bytes)
        .map_err(|error| GatewayError::io(path, error))?;
    if bytes.len() > MAX_CHILD_SIDECAR_BYTES || bytes.len() as u64 != metadata.len() {
        return Err(GatewayError::ChildProof(format!(
            "sidecar changed while reading: {}",
            path.display()
        )));
    }
    let sidecar = parse_child_sidecar(&bytes).map_err(|error| {
        GatewayError::ChildProof(format!(
            "decode canonical sidecar {}: {error}",
            path.display()
        ))
    })?;
    if sidecar.chain_id != expected_chain_id
        || sidecar.batch_number != expected_batch_number
        || &sidecar.public_input_hash != expected_public_input_hash
    {
        return Err(GatewayError::ChildProof(format!(
            "sidecar tuple does not match requested constituent: {}",
            path.display()
        )));
    }
    let proof: SP1ProofWithPublicValues = bincode::DefaultOptions::new()
        .with_fixint_encoding()
        .with_limit(sidecar.proof_bytes.len() as u64)
        .reject_trailing_bytes()
        .deserialize(&sidecar.proof_bytes)
        .map_err(|error| {
            GatewayError::ChildProof(format!(
                "decode compressed proof in canonical sidecar {}: {error}",
                path.display()
            ))
        })?;
    let canonical = bincode::DefaultOptions::new()
        .with_fixint_encoding()
        .with_limit(sidecar.proof_bytes.len() as u64)
        .reject_trailing_bytes()
        .serialize(&proof)
        .map_err(|error| {
            GatewayError::ChildProof(format!("re-encode canonical sidecar: {error}"))
        })?;
    if canonical != sidecar.proof_bytes {
        return Err(GatewayError::ChildProof(format!(
            "compressed proof is not the canonical SP1ProofWithPublicValues encoding: {}",
            path.display()
        )));
    }
    let mut supplement = [0u8; PUBLIC_INPUT_SUPPLEMENT_BYTES];
    supplement[..32].copy_from_slice(&sidecar.l1_message_hash);
    supplement[32..].copy_from_slice(&sidecar.block_context_hash);
    Ok(LoadedChildProof {
        proof,
        public_input_supplement: supplement,
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    use sp1_sdk::{SP1_CIRCUIT_VERSION, SP1Proof, SP1ProofWithPublicValues};

    #[test]
    fn rejects_groth16_child_proof_kind_before_crypto() {
        let proof = SP1ProofWithPublicValues::new(
            SP1Proof::Groth16(Default::default()),
            sp1_sdk::SP1PublicValues::from(&[0u8; 33]),
            SP1_CIRCUIT_VERSION.to_string(),
        );
        let error = validate_child_proof(&proof, &[0u8; 33]).unwrap_err();
        assert!(error.to_string().contains("must be SP1 Compressed"));
    }

    #[test]
    fn rejects_core_child_proof_kind_before_crypto() {
        let proof = SP1ProofWithPublicValues::new(
            SP1Proof::Core(Vec::new()),
            sp1_sdk::SP1PublicValues::from(&[0u8; 33]),
            SP1_CIRCUIT_VERSION.to_string(),
        );
        let error = validate_child_proof(&proof, &[0u8; 33]).unwrap_err();
        assert!(error.to_string().contains("must be SP1 Compressed"));
    }

    #[test]
    fn rejects_wrong_sp1_circuit_version_before_crypto() {
        let proof = SP1ProofWithPublicValues::new(
            SP1Proof::Core(Vec::new()),
            sp1_sdk::SP1PublicValues::from(&[0u8; 33]),
            "wrong-circuit-version".into(),
        );
        let error = validate_child_sp1_version(&proof).unwrap_err();
        assert!(error.to_string().contains("SP1 circuit version"));
    }

    #[test]
    fn rejects_noncanonical_sidecar_bytes() {
        let directory = tempfile::tempdir().unwrap();
        let path = directory.path().join("child");
        std::fs::write(&path, b"not-bincode").unwrap();
        assert!(load_child_proof(&path, 1, 1, &[0u8; 32]).is_err());
    }

    #[test]
    fn loads_canonical_bounded_sp1_sidecar() {
        let proof = SP1ProofWithPublicValues::new(
            SP1Proof::Core(Vec::new()),
            sp1_sdk::SP1PublicValues::from(&[0u8; 33]),
            SP1_CIRCUIT_VERSION.to_string(),
        );
        let proof_bytes = bincode::serialize(&proof).unwrap();
        let sidecar = neo_zkvm_gateway_guest::encode_child_sidecar(
            7,
            42,
            &[0x51; 32],
            &[0x61; 32],
            &[0x71; 32],
            &proof_bytes,
        )
        .unwrap();
        let directory = tempfile::tempdir().unwrap();
        let path = directory.path().join("child");
        std::fs::write(&path, sidecar).unwrap();
        let loaded = load_child_proof(&path, 7, 42, &[0x51; 32]).unwrap();
        assert_eq!(loaded.public_input_supplement[..32], [0x61; 32]);
        assert_eq!(loaded.public_input_supplement[32..], [0x71; 32]);
        assert!(matches!(loaded.proof.proof, SP1Proof::Core(_)));
    }

    #[cfg(unix)]
    #[test]
    fn rejects_sidecar_symlink_without_following_it() {
        use std::os::unix::fs::symlink;
        let directory = tempfile::tempdir().unwrap();
        let target = directory.path().join("target");
        let link = directory.path().join("link");
        std::fs::write(&target, b"not-bincode").unwrap();
        symlink(&target, &link).unwrap();
        assert!(load_child_proof(&link, 1, 1, &[0u8; 32]).is_err());
    }
}
