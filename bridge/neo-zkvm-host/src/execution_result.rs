/// Result of a successful end-to-end zkVM execution.
pub struct ExecutionResult {
    /// 32-byte public-input hash committed by the guest.
    pub public_input_hash: [u8; 32],
    /// Reported gas / cycle count from the SP1 executor.
    pub cycles: u64,
}
