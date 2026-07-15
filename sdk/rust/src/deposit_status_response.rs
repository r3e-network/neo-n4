use serde::Deserialize;

#[derive(Debug, Clone, Deserialize)]
pub struct DepositStatusResponse {
    #[serde(rename = "sourceChainId")]
    pub source_chain_id: u32,
    #[serde(deserialize_with = "crate::wire::deserialize_u64")]
    pub nonce: u64,
    #[serde(rename = "consumedOnL2")]
    pub consumed_on_l2: bool,
    #[serde(
        rename = "includedInBatch",
        default,
        deserialize_with = "crate::wire::deserialize_optional_u64"
    )]
    pub included_in_batch: Option<u64>,
}
