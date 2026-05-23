use serde::Deserialize;

#[derive(Debug, Clone, Deserialize)]
pub struct DepositStatusResponse {
    #[serde(rename = "sourceChainId")]
    pub source_chain_id: u32,
    pub nonce: u64,
    #[serde(rename = "consumedOnL2")]
    pub consumed_on_l2: bool,
    #[serde(rename = "includedInBatch")]
    pub included_in_batch: Option<u64>,
}
