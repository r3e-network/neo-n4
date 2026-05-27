use serde::Deserialize;

#[derive(Deserialize, Debug, Clone)]
pub(super) struct RawLog {
    pub(super) topics: Vec<String>,
    pub(super) data: String,
    #[serde(rename = "blockNumber")]
    pub(super) block_number: String,
    #[serde(rename = "transactionHash")]
    pub(super) transaction_hash: String,
}
