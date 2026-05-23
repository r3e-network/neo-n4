use serde::Deserialize;

use crate::{DAMode, ExitModel, SecurityLevel, SequencerModel};

#[derive(Debug, Clone, Deserialize)]
pub struct SecurityLabelResponse {
    #[serde(rename = "chainId")]
    pub chain_id: u32,
    #[serde(rename = "securityLevel")]
    pub security_level: u8,
    #[serde(rename = "daMode")]
    pub da_mode: u8,
    #[serde(rename = "gatewayEnabled")]
    pub gateway_enabled: bool,
    pub sequencer: u8,
    pub exit: u8,
}

impl SecurityLabelResponse {
    pub fn security_level(&self) -> SecurityLevel {
        SecurityLevel::from_u8(self.security_level)
    }
    pub fn da_mode(&self) -> DAMode {
        DAMode::from_u8(self.da_mode)
    }
    pub fn sequencer(&self) -> SequencerModel {
        SequencerModel::from_u8(self.sequencer)
    }
    pub fn exit(&self) -> ExitModel {
        ExitModel::from_u8(self.exit)
    }
}
