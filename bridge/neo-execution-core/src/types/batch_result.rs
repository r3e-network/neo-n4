/// Result committed by the batch execution core.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct BatchResult {
    pub post_state_root: [u8; 32],
    pub tx_root: [u8; 32],
    pub receipt_root: [u8; 32],
    pub public_input_hash: [u8; 32],
}
