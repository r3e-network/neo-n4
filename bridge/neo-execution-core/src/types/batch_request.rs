use alloc::vec::Vec;

use super::L1Message;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct BatchRequest {
    /// Wire format version that was parsed (1 = deprecated 4-root, 2 = current 10-root).
    /// v1-parsed requests zero-fill the 6 v2-only roots; downstream code that compares
    /// public-input hashes across versions must account for this.
    pub wire_version: u8,
    pub chain_id: u32,
    pub batch_number: u64,
    pub pre_state_root: [u8; 32],
    pub da_commitment: [u8; 32],
    pub withdrawal_root: [u8; 32],
    pub l2_to_l1_message_root: [u8; 32],
    pub l2_to_l2_message_root: [u8; 32],
    pub l1_message_hash: [u8; 32],
    pub block_context_hash: [u8; 32],
    pub l1_messages: Vec<L1Message>,
    pub transactions: Vec<Vec<u8>>,
}
