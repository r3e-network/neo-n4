use serde::Deserialize;

use crate::ProofType;

#[derive(Debug, Clone, Deserialize)]
pub struct L2BatchView {
    #[serde(rename = "chainId")]
    pub chain_id: u32,
    #[serde(rename = "batchNumber")]
    pub batch_number: u64,
    #[serde(rename = "firstBlock")]
    pub first_block: u64,
    #[serde(rename = "lastBlock")]
    pub last_block: u64,
    #[serde(rename = "preStateRoot")]
    pub pre_state_root: String,
    #[serde(rename = "postStateRoot")]
    pub post_state_root: String,
    #[serde(rename = "txRoot")]
    pub tx_root: String,
    #[serde(rename = "receiptRoot")]
    pub receipt_root: String,
    #[serde(rename = "withdrawalRoot")]
    pub withdrawal_root: String,
    #[serde(rename = "l2ToL1MessageRoot")]
    pub l2_to_l1_message_root: String,
    #[serde(rename = "l2ToL2MessageRoot")]
    pub l2_to_l2_message_root: String,
    #[serde(rename = "daCommitment")]
    pub da_commitment: String,
    #[serde(rename = "publicInputHash")]
    pub public_input_hash: String,
    #[serde(rename = "proofType")]
    pub proof_type: u8,
    pub proof: String,
    pub encoded: String,
}

impl L2BatchView {
    pub fn proof_type(&self) -> ProofType {
        ProofType::from_u8(self.proof_type)
    }
}
