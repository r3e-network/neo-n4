/// Result of a successful ZK proof generation.
#[derive(Debug, Clone)]
pub struct ProofResult {
    /// 32-byte public-input hash committed by the guest.
    pub public_input_hash: [u8; 32],
    /// Exact public values committed by the guest: success tag followed by
    /// the 32-byte N4 public-input hash.
    pub public_values: [u8; 33],
    /// SP1 Groth16 on-chain proof bytes submitted to L1 via SettlementManager.
    /// Format: selector, exit code, recursion VK root, nonce, A, B, C.
    pub proof_bytes: Vec<u8>,
    /// Raw big-endian `vk.bytes32_raw()` program verification-key digest.
    pub vk_bytes: [u8; 32],
}
