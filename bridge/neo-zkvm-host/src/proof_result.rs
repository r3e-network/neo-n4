/// Result of a successful ZK proof generation.
pub struct ProofResult {
    /// 32-byte public-input hash committed by the guest.
    pub public_input_hash: [u8; 32],
    /// Serialized proof bytes — submitted to L1 via SettlementManager.
    /// Format: bincode-encoded `SP1ProofWithPublicValues`.
    pub proof_bytes: Vec<u8>,
    /// Serialized verifying key bytes — used by the on-chain verifier
    /// to check the proof. Stable for a given guest ELF.
    /// Format: bincode-encoded `SP1VerifyingKey`.
    pub vk_bytes: Vec<u8>,
}
