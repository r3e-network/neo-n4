use serde::Deserialize;

use crate::BatchStatus;

#[derive(Debug, Clone, Deserialize)]
pub struct BatchStatusResponse {
    #[serde(rename = "chainId")]
    pub chain_id: u32,
    #[serde(rename = "batchNumber")]
    pub batch_number: u64,
    pub status: u8,
    #[serde(rename = "statusName", default)]
    pub status_name: String,
}

impl BatchStatusResponse {
    pub fn status(&self) -> BatchStatus {
        BatchStatus::from_u8(self.status)
    }
}
