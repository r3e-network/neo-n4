//! Shared wire-format size limits and magic constants.

pub const COMMITMENT_FIXED_SIZE: usize = 321;
pub const MAX_PROOF_BYTES: usize = 1024 * 1024;
pub const CHAIN_CONFIG_SIZE: usize = 91;
pub const MERKLE_PROOF_HEADER_SIZE: usize = 48;
pub const MAX_MERKLE_DEPTH: usize = 64;

pub(crate) const ARTIFACT_MAGIC: &[u8; 8] = b"NEO4PWIT";
pub(crate) const PAYLOAD_MAGIC: &[u8; 8] = b"NEO4EXEC";
pub(crate) const STATE_WITNESS_MAGIC: &[u8; 8] = b"NEO4STW1";
pub(crate) const EFFECTS_MAGIC: &[u8; 8] = b"NEO4EFX1";
pub(crate) const NATIVE_EXECUTION_OUTPUT_MAGIC: &[u8; 8] = b"NEO4EXR1";

pub const MAX_PROOF_WITNESS_ARTIFACT_BYTES: usize = 256 * 1024 * 1024;
pub const MAX_EXECUTION_PAYLOAD_BYTES: usize = 64 * 1024 * 1024;
pub const MAX_STATE_WITNESS_BYTES: usize = 128 * 1024 * 1024;
pub const MAX_NATIVE_EXECUTION_OUTPUT_BYTES: usize =
    MAX_STATE_WITNESS_BYTES + MAX_EFFECTS_BYTES + 512;
pub(crate) const MAX_EFFECTS_BYTES: usize = 64 * 1024 * 1024;
pub(crate) const MAX_DA_POINTER_BYTES: usize = 1024 * 1024;
pub(crate) const MAX_DA_EVIDENCE_BYTES: usize = 16 * 1024 * 1024;
pub(crate) const MAX_MESSAGE_BYTES: usize = 4 * 1024 * 1024;
pub(crate) const MAX_PAYLOAD_TRANSACTION_BYTES: usize = 16 * 1024 * 1024;
pub(crate) const MAX_PAYLOAD_ITEMS: usize = 1_000_000;
pub(crate) const MAX_FORCED_INCLUSION_SIBLINGS: usize = 64;
pub(crate) const MAX_STATE_ENTRIES: usize = 65_536;
pub(crate) const MAX_STATE_KEY_BYTES: usize = 4096;
pub(crate) const MAX_STATE_VALUE_BYTES: usize = 1024 * 1024;
pub(crate) const MAX_CONTRACTS: usize = 4096;
pub(crate) const MAX_CONTRACT_SCRIPT_BYTES: usize = 1024 * 1024;
pub(crate) const MAX_CONTRACT_MANIFEST_BYTES: usize = u16::MAX as usize;
pub(crate) const MAX_EFFECT_TRANSACTIONS: usize = 65_536;
pub(crate) const MAX_DELTAS_PER_TRANSACTION: usize = 65_536;
pub(crate) const MAX_EVENTS_PER_TRANSACTION: usize = 512;
