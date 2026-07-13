/// Canonical inputs for one recursively verifiable Gateway child sidecar.
#[derive(Debug, Clone)]
pub struct RecursiveChildProofResult {
    /// L2 chain domain proven by the child program.
    pub chain_id: u32,
    /// Batch number proven by the child program.
    pub batch_number: u64,
    /// Hash256 of the canonical 332-byte public inputs.
    pub public_input_hash: [u8; 32],
    /// Canonical L1 inbox hash absent from `L2BatchCommitment`.
    pub l1_message_hash: [u8; 32],
    /// Canonical block-context hash absent from `L2BatchCommitment`.
    pub block_context_hash: [u8; 32],
    /// Canonical bincode encoding of `SP1ProofWithPublicValues::Compressed`.
    pub proof_bytes: Vec<u8>,
}
