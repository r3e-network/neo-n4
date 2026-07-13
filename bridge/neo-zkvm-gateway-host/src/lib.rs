mod daemon;
mod protocol;

#[cfg(unix)]
mod prover;

pub use daemon::{DaemonConfig, process_ready_requests, run_daemon};
pub use protocol::{
    GatewayError, GatewayProofArtifacts, GatewayProofRequestManifest, GatewayProofResultManifest,
    canonical_sidecar_filename,
};

#[cfg(unix)]
pub use prover::{gateway_verification_key, prove_request};

include!(concat!(env!("OUT_DIR"), "/gateway_build.rs"));

pub const BATCH_GUEST_ELF: &[u8] = include_bytes!(env!("NEO_ZKVM_BATCH_GUEST_ELF"));
pub const GATEWAY_GUEST_ELF: &[u8] = include_bytes!(env!("NEO_ZKVM_GATEWAY_GUEST_ELF"));

#[must_use]
pub const fn build_manifest_json() -> &'static str {
    BUILD_MANIFEST_JSON
}
