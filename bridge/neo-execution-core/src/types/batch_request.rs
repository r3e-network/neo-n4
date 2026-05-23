use alloc::vec::Vec;

use super::L1Message;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct BatchRequest {
    pub chain_id: u32,
    pub batch_number: u64,
    pub pre_state_root: [u8; 32],
    pub da_commitment: [u8; 32],
    pub l1_messages: Vec<L1Message>,
    pub transactions: Vec<Vec<u8>>,
}
