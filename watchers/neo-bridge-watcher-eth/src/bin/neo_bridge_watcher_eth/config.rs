use serde::Deserialize;
use std::path::PathBuf;

use crate::health_config::HealthConfig;
use crate::poll_config::PollConfig;

#[derive(Deserialize, Debug)]
pub(crate) struct Config {
    pub(crate) external_chain_id: u32,
    pub(crate) eth_rpc_url: String,
    #[serde(deserialize_with = "deserialize_addr20")]
    pub(crate) eth_router_address: [u8; 20],
    pub(crate) neo_rpc_url: String,
    #[serde(deserialize_with = "deserialize_addr20")]
    pub(crate) neo_escrow_address: [u8; 20],
    #[serde(deserialize_with = "deserialize_addr20")]
    pub(crate) neo_signer_address: [u8; 20],
    pub(crate) signer_key_path: PathBuf,
    pub(crate) journal_dir: PathBuf,
    #[serde(default)]
    pub(crate) poll: PollConfig,
    #[serde(default)]
    pub(crate) health: HealthConfig,
}

fn deserialize_addr20<'de, D: serde::Deserializer<'de>>(d: D) -> Result<[u8; 20], D::Error> {
    use serde::de::Error;
    let s: String = Deserialize::deserialize(d)?;
    let s = s.strip_prefix("0x").unwrap_or(&s);
    let bytes = hex::decode(s).map_err(D::Error::custom)?;
    if bytes.len() != 20 {
        return Err(D::Error::custom(format!(
            "address must be 20 bytes (got {})",
            bytes.len()
        )));
    }
    let mut out = [0u8; 20];
    out.copy_from_slice(&bytes);
    Ok(out)
}
