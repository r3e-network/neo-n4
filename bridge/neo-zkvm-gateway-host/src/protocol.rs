use neo_zkvm_gateway_guest::{
    GatewayRequest, canonical_child_sidecar_filename, gateway_public_values, hash256,
    parse_request_with_gateway_vk,
};
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::path::{Path, PathBuf};

pub const SCHEMA_VERSION: u32 = 1;
pub const SP1_PROOF_SYSTEM: u8 = 1;
pub const RECURSIVE_BACKEND_ID: u8 = 0xc2;
pub const GROTH16_PROOF_BYTES: usize = 356;
pub const PUBLIC_VALUES_BYTES: usize = 33;
pub const MAX_MANIFEST_BYTES: u64 = 16 * 1024;
pub const MAX_REQUEST_BYTES: u64 = 64 * 1024 * 1024;
pub const REQUEST_PAYLOAD_SUFFIX: &str = ".gateway-request.bin";
pub const REQUEST_MANIFEST_SUFFIX: &str = ".gateway-request.json";
pub const PROOF_SUFFIX: &str = ".gateway-proof.bin";
pub const VERIFICATION_KEY_SUFFIX: &str = ".gateway-verification-key.bin";
pub const PUBLIC_VALUES_SUFFIX: &str = ".gateway-public-values.bin";
pub const RESULT_MANIFEST_SUFFIX: &str = ".gateway-result.json";

#[derive(Debug, thiserror::Error)]
pub enum GatewayError {
    #[error("invalid Gateway protocol: {0}")]
    Protocol(String),
    #[error("invalid Gateway request: {0}")]
    Request(String),
    #[error("invalid child proof: {0}")]
    ChildProof(String),
    #[error("Gateway proof failed: {0}")]
    Proving(String),
    #[error("filesystem error for {path}: {source}")]
    Io {
        path: PathBuf,
        #[source]
        source: std::io::Error,
    },
    #[error("JSON error: {0}")]
    Json(#[from] serde_json::Error),
}

impl GatewayError {
    pub(crate) fn io(path: impl Into<PathBuf>, source: std::io::Error) -> Self {
        Self::Io {
            path: path.into(),
            source,
        }
    }
}

#[derive(Debug, Clone, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct GatewayProofRequestManifest {
    pub schema_version: u32,
    pub request_id: String,
    pub request_hash: String,
    pub binding_hash: String,
    pub proof_system: u8,
    pub aggregation_backend_id: u8,
    pub verification_key: String,
    pub request_file: String,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct GatewayProofResultManifest {
    pub schema_version: u32,
    pub status: String,
    pub request_id: String,
    pub request_hash: String,
    pub binding_hash: String,
    pub proof_system: u8,
    pub aggregation_backend_id: u8,
    pub verification_key: String,
    pub request_file: String,
    pub proof_file: String,
    pub verification_key_file: String,
    pub public_values_file: String,
    pub proof_sha256: String,
    pub verification_key_sha256: String,
    pub public_values_sha256: String,
}

#[derive(Debug, Clone)]
pub struct GatewayProofArtifacts {
    pub proof_bytes: Vec<u8>,
    pub verification_key: [u8; 32],
    pub public_values: [u8; PUBLIC_VALUES_BYTES],
}

pub fn canonical_sidecar_filename(
    chain_id: u32,
    batch_number: u64,
    public_input_hash: &[u8; 32],
) -> String {
    canonical_child_sidecar_filename(chain_id, batch_number, public_input_hash)
}

pub(crate) fn validate_request_manifest(
    manifest: &GatewayProofRequestManifest,
    request_bytes: &[u8],
    gateway_vk: &[u8; 32],
) -> Result<GatewayRequest, GatewayError> {
    validate_lower_hex(&manifest.request_id, 32, "requestId")?;
    validate_lower_hex(&manifest.request_hash, 32, "requestHash")?;
    validate_lower_hex(&manifest.binding_hash, 32, "bindingHash")?;
    validate_lower_hex(&manifest.verification_key, 32, "verificationKey")?;
    if manifest.schema_version != SCHEMA_VERSION {
        return Err(GatewayError::Protocol("unsupported schemaVersion".into()));
    }
    if manifest.proof_system != SP1_PROOF_SYSTEM {
        return Err(GatewayError::Protocol("proofSystem must be SP1 (1)".into()));
    }
    if manifest.aggregation_backend_id != RECURSIVE_BACKEND_ID {
        return Err(GatewayError::Protocol(
            "aggregationBackendId must be recursive Gateway backend 0xC2".into(),
        ));
    }
    let request_id = hex::encode(hash256(request_bytes));
    if manifest.request_id != request_id || manifest.request_hash != request_id {
        return Err(GatewayError::Protocol("request Hash256 mismatch".into()));
    }
    let request_file = format!("{request_id}{REQUEST_PAYLOAD_SUFFIX}");
    if manifest.request_file != request_file || !is_plain_filename(&manifest.request_file) {
        return Err(GatewayError::Protocol(
            "requestFile is not the canonical request-id-derived filename".into(),
        ));
    }
    if manifest.verification_key != hex::encode(gateway_vk) {
        return Err(GatewayError::Protocol(
            "manifest Gateway verification key is not the compiled key".into(),
        ));
    }
    let request = parse_request_with_gateway_vk(request_bytes, Some(gateway_vk))
        .map_err(|error| GatewayError::Request(error.to_string()))?;
    let binding_hash = hex::encode(&gateway_public_values(&request.binding)[1..]);
    if manifest.binding_hash != binding_hash {
        return Err(GatewayError::Protocol("binding Hash256 mismatch".into()));
    }
    Ok(request)
}

pub(crate) fn result_manifest(
    request: &GatewayProofRequestManifest,
    artifacts: &GatewayProofArtifacts,
) -> GatewayProofResultManifest {
    let request_id = &request.request_id;
    GatewayProofResultManifest {
        schema_version: SCHEMA_VERSION,
        status: "succeeded".into(),
        request_id: request_id.clone(),
        request_hash: request.request_hash.clone(),
        binding_hash: request.binding_hash.clone(),
        proof_system: SP1_PROOF_SYSTEM,
        aggregation_backend_id: RECURSIVE_BACKEND_ID,
        verification_key: hex::encode(artifacts.verification_key),
        request_file: request.request_file.clone(),
        proof_file: format!("{request_id}{PROOF_SUFFIX}"),
        verification_key_file: format!("{request_id}{VERIFICATION_KEY_SUFFIX}"),
        public_values_file: format!("{request_id}{PUBLIC_VALUES_SUFFIX}"),
        proof_sha256: sha256_hex(&artifacts.proof_bytes),
        verification_key_sha256: sha256_hex(&artifacts.verification_key),
        public_values_sha256: sha256_hex(&artifacts.public_values),
    }
}

pub(crate) fn request_id_from_manifest_path(path: &Path) -> Option<&str> {
    let name = path.file_name()?.to_str()?;
    let request_id = name.strip_suffix(REQUEST_MANIFEST_SUFFIX)?;
    if request_id.len() == 64
        && request_id
            .bytes()
            .all(|byte| byte.is_ascii_digit() || (b'a'..=b'f').contains(&byte))
    {
        Some(request_id)
    } else {
        None
    }
}

pub(crate) fn is_plain_filename(value: &str) -> bool {
    !value.is_empty()
        && Path::new(value).file_name().and_then(|name| name.to_str()) == Some(value)
        && !value.contains('/')
        && !value.contains('\\')
        && value != "."
        && value != ".."
}

pub(crate) fn sha256_hex(bytes: &[u8]) -> String {
    hex::encode(Sha256::digest(bytes))
}

fn validate_lower_hex(value: &str, bytes: usize, field: &str) -> Result<(), GatewayError> {
    if value.len() != bytes * 2
        || !value
            .bytes()
            .all(|byte| byte.is_ascii_digit() || (b'a'..=b'f').contains(&byte))
    {
        return Err(GatewayError::Protocol(format!(
            "{field} must be exactly {bytes} lowercase hexadecimal bytes"
        )));
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn sidecar_name_is_canonical_flat_tuple() {
        let name = canonical_sidecar_filename(7, 42, &[0xab; 32]);
        assert_eq!(
            name,
            format!(
                "00000007-000000000000002a-{}{}",
                "ab".repeat(32),
                neo_zkvm_gateway_guest::CHILD_SIDECAR_SUFFIX
            )
        );
        assert!(is_plain_filename(&name));
        assert!(!name.contains(".."));
    }

    #[test]
    fn manifest_path_accepts_only_lower_hex_request_ids() {
        let valid = format!("{}{}", "ab".repeat(32), REQUEST_MANIFEST_SUFFIX);
        assert_eq!(
            request_id_from_manifest_path(Path::new(&valid)),
            Some("abababababababababababababababababababababababababababababababab")
        );
        assert!(
            request_id_from_manifest_path(Path::new(&format!(
                "{}{}",
                "AB".repeat(32),
                REQUEST_MANIFEST_SUFFIX
            )))
            .is_none()
        );
    }

    #[test]
    fn plain_filename_rejects_user_paths() {
        for path in ["../proof", "a/b", "a\\b", ".", "..", ""] {
            assert!(!is_plain_filename(path), "accepted {path}");
        }
    }

    #[test]
    fn binding_size_stays_locked_to_dotnet() {
        assert_eq!(neo_zkvm_gateway_guest::BINDING_BYTES, 170);
    }
}
