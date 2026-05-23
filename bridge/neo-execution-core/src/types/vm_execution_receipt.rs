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
