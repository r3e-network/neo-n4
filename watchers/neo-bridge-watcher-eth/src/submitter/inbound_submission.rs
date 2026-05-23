/// One submission batch: the canonical message bytes + the wire-format
/// proof bytes (Neo flavor, see [`crate::proof::NeoProofBytes`]).
#[derive(Debug, Clone)]
pub struct InboundSubmission {
    pub external_chain_id: u32,
    pub message_bytes: Vec<u8>,
    pub proof_bytes: Vec<u8>,
}
