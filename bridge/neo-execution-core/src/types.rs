use alloc::vec::Vec;

/// Current canonical batch request wire-format version.
pub const BATCH_WIRE_VERSION: u8 = 1;

/// Default per-tx gas limit when the wire format does not specify one.
///
/// 100,000,000 datoshi = 1 GAS, matching Neo N3's typical tx-level cap.
pub const DEFAULT_PER_TX_GAS_LIMIT: u64 = 100_000_000;

/// Result committed by the batch execution core.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct BatchResult {
    pub post_state_root: [u8; 32],
    pub tx_root: [u8; 32],
    pub receipt_root: [u8; 32],
    pub public_input_hash: [u8; 32],
}

/// Minimal VM execution summary required by the canonical batch fold.
///
/// The concrete backend owns execution semantics. For example:
/// - SP1 guest code can hash `neo_vm_guest::ProofOutput`.
/// - A PolkaVM-backed RISC-V runner can hash its ABI `ExecutionResult`.
///
/// The core only commits to the state byte, gas used, and backend output hash.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct VmExecutionReceipt {
    pub state: u8,
    pub gas_consumed: u64,
    pub output_hash: [u8; 32],
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum ExecutionError {
    Truncated,
    InvalidVersion(u8),
    OversizedField(&'static str),
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct BatchRequest {
    pub chain_id: u32,
    pub batch_number: u64,
    pub pre_state_root: [u8; 32],
    pub da_commitment: [u8; 32],
    pub l1_messages: Vec<L1Message>,
    pub transactions: Vec<Vec<u8>>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct L1Message {
    pub bytes: Vec<u8>,
}
