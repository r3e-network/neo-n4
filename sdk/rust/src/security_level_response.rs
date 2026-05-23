use serde::Deserialize;

use crate::SecurityLevel;

#[derive(Debug, Clone, Deserialize)]
pub struct SecurityLevelResponse {
    #[serde(rename = "chainId")]
    pub chain_id: u32,
    pub level: u8,
}

impl SecurityLevelResponse {
    pub fn level(&self) -> SecurityLevel {
        SecurityLevel::from_u8(self.level)
    }
}
