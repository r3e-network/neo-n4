//! Typed Rust client for the Neo Elastic Network L2 RPC surface
//! (`doc.md` §14.1). Mirrors the .NET reference SDK in `src/Neo.L2.Sdk/` —
//! same method names, same 4-way error taxonomy, same chainId cross-check.

use serde::Deserialize;
use std::sync::atomic::{AtomicI64, Ordering};
use std::time::Duration;
use thiserror::Error;

// ─── enum mirrors of doc.md §16.2 ───────────────────────────────────
//
// Stored on the wire as u8 (Neo's native enum encoding). Response structs
// keep them as raw u8; helper conversion methods on the structs decode to
// these typed variants when the operator wants the named form.

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SecurityLevel {
    Sidechain,
    Settled,
    Optimistic,
    Validity,
    Validium,
    /// Unrecognized byte (forward-compat for future protocol additions).
    Unknown(u8),
}

impl SecurityLevel {
    pub fn from_u8(b: u8) -> Self {
        match b {
            0 => Self::Sidechain,
            1 => Self::Settled,
            2 => Self::Optimistic,
            3 => Self::Validity,
            4 => Self::Validium,
            other => Self::Unknown(other),
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum DAMode {
    L1,
    NeoFS,
    External,
    DAC,
    Unknown(u8),
}

impl DAMode {
    pub fn from_u8(b: u8) -> Self {
        match b {
            0 => Self::L1,
            1 => Self::NeoFS,
            2 => Self::External,
            3 => Self::DAC,
            other => Self::Unknown(other),
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SequencerModel {
    Centralized,
    DbftCommittee,
    PoSRotation,
    Unknown(u8),
}

impl SequencerModel {
    pub fn from_u8(b: u8) -> Self {
        match b {
            0 => Self::Centralized,
            1 => Self::DbftCommittee,
            2 => Self::PoSRotation,
            other => Self::Unknown(other),
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ExitModel {
    Permissionless,
    Delayed,
    OperatorAssisted,
    Unknown(u8),
}

impl ExitModel {
    pub fn from_u8(b: u8) -> Self {
        match b {
            0 => Self::Permissionless,
            1 => Self::Delayed,
            2 => Self::OperatorAssisted,
            other => Self::Unknown(other),
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ProofType {
    None,
    Multisig,
    Optimistic,
    Zk,
    Unknown(u8),
}

impl ProofType {
    pub fn from_u8(b: u8) -> Self {
        match b {
            0 => Self::None,
            1 => Self::Multisig,
            2 => Self::Optimistic,
            3 => Self::Zk,
            other => Self::Unknown(other),
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum BatchStatus {
    Pending,
    Challengeable,
    Finalized,
    Challenged,
    Slashed,
    Unknown(u8),
}

impl BatchStatus {
    pub fn from_u8(b: u8) -> Self {
        match b {
            0 => Self::Pending,
            1 => Self::Challengeable,
            2 => Self::Finalized,
            3 => Self::Challenged,
            4 => Self::Slashed,
            other => Self::Unknown(other),
        }
    }
}

// ─── typed RPC responses ────────────────────────────────────────────

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

// ─── error taxonomy ────────────────────────────────────────────────

#[derive(Debug, Error)]
pub enum L2RpcError {
    /// HTTP-layer failure (timeout, connection refused, non-2xx). Retry-safe.
    #[error("transport: {method}: {message}")]
    Transport { method: String, message: String },

    /// JSON-RPC envelope or parse failure (bad shape, mismatched id). NOT retry-safe.
    #[error("protocol: {method}: {message}")]
    Protocol { method: String, message: String },

    /// Server returned a JSON-RPC error response.
    #[error("server: {method}: code {code}: {message}")]
    Server {
        method: String,
        code: i32,
        message: String,
    },

    /// Server's chainId differs from the client's. Config error — don't retry.
    #[error("mismatched chainId: {method}: expected {expected}, got {got}")]
    MismatchedChainId {
        method: String,
        expected: u32,
        got: u32,
    },
}

pub type Result<T> = std::result::Result<T, L2RpcError>;

// ─── client ────────────────────────────────────────────────────────

#[derive(Debug)]
pub struct L2RpcClient {
    endpoint: String,
    chain_id: u32,
    http: reqwest::Client,
    next_id: AtomicI64,
}

impl L2RpcClient {
    pub fn new(endpoint: impl Into<String>, chain_id: u32) -> Result<Self> {
        let endpoint = endpoint.into();
        if chain_id == 0 {
            return Err(L2RpcError::Protocol {
                method: "<ctor>".to_string(),
                message: "chainId 0 is reserved for L1".to_string(),
            });
        }
        // Validate URL via reqwest's parsing — keeps the dep tree minimal vs
        // adding a top-level `url` crate.
        let parsed = reqwest::Url::parse(&endpoint).map_err(|e| L2RpcError::Protocol {
            method: "<ctor>".to_string(),
            message: format!("invalid endpoint URL: {}", e),
        })?;
        if parsed.scheme() != "http" && parsed.scheme() != "https" {
            return Err(L2RpcError::Protocol {
                method: "<ctor>".to_string(),
                message: format!("endpoint scheme '{}' must be http(s)", parsed.scheme()),
            });
        }
        let http = reqwest::Client::builder()
            .timeout(Duration::from_secs(30))
            .build()
            .map_err(|e| L2RpcError::Transport {
                method: "<ctor>".to_string(),
                message: format!("http client init: {}", e),
            })?;
        Ok(Self {
            endpoint,
            chain_id,
            http,
            next_id: AtomicI64::new(0),
        })
    }

    pub fn chain_id(&self) -> u32 {
        self.chain_id
    }

    pub async fn get_batch(&self, batch_number: u64) -> Result<Option<L2BatchView>> {
        let value = self
            .call(
                "getl2batch",
                serde_json::json!([self.chain_id, batch_number]),
            )
            .await?;
        if value.is_null() {
            return Ok(None);
        }
        let batch: L2BatchView =
            serde_json::from_value(value).map_err(|e| L2RpcError::Protocol {
                method: "getl2batch".to_string(),
                message: format!("decode failed: {}", e),
            })?;
        if batch.chain_id != self.chain_id {
            return Err(L2RpcError::MismatchedChainId {
                method: "getl2batch".to_string(),
                expected: self.chain_id,
                got: batch.chain_id,
            });
        }
        Ok(Some(batch))
    }

    pub async fn get_batch_status(&self, batch_number: u64) -> Result<BatchStatusResponse> {
        let value = self
            .call(
                "getl2batchstatus",
                serde_json::json!([self.chain_id, batch_number]),
            )
            .await?;
        let resp: BatchStatusResponse =
            serde_json::from_value(value).map_err(|e| L2RpcError::Protocol {
                method: "getl2batchstatus".to_string(),
                message: format!("decode failed: {}", e),
            })?;
        if resp.chain_id != self.chain_id {
            return Err(L2RpcError::MismatchedChainId {
                method: "getl2batchstatus".to_string(),
                expected: self.chain_id,
                got: resp.chain_id,
            });
        }
        Ok(resp)
    }

    pub async fn get_latest_state_root(&self) -> Result<String> {
        let value = self
            .call("getl2stateroot", serde_json::json!([self.chain_id]))
            .await?;
        value
            .as_str()
            .map(String::from)
            .ok_or_else(|| L2RpcError::Protocol {
                method: "getl2stateroot".to_string(),
                message: "expected string".to_string(),
            })
    }

    pub async fn get_state_root_at(&self, batch_number: u64) -> Result<String> {
        let value = self
            .call(
                "getl2stateroot",
                serde_json::json!([self.chain_id, batch_number]),
            )
            .await?;
        value
            .as_str()
            .map(String::from)
            .ok_or_else(|| L2RpcError::Protocol {
                method: "getl2stateroot".to_string(),
                message: "expected string".to_string(),
            })
    }

    pub async fn get_withdrawal_proof(&self, leaf: &str) -> Result<Option<Vec<u8>>> {
        let value = self
            .call(
                "getl2withdrawalproof",
                serde_json::json!([self.chain_id, leaf]),
            )
            .await?;
        if value.is_null() {
            return Ok(None);
        }
        let hex = value.as_str().ok_or_else(|| L2RpcError::Protocol {
            method: "getl2withdrawalproof".to_string(),
            message: "expected hex string".to_string(),
        })?;
        Ok(Some(hex_decode(hex).map_err(|e| L2RpcError::Protocol {
            method: "getl2withdrawalproof".to_string(),
            message: format!("invalid hex: {}", e),
        })?))
    }

    pub async fn get_message_proof(&self, message_hash: &str) -> Result<Option<Vec<u8>>> {
        let value = self
            .call(
                "getl2messageproof",
                serde_json::json!([self.chain_id, message_hash]),
            )
            .await?;
        if value.is_null() {
            return Ok(None);
        }
        let hex = value.as_str().ok_or_else(|| L2RpcError::Protocol {
            method: "getl2messageproof".to_string(),
            message: "expected hex string".to_string(),
        })?;
        Ok(Some(hex_decode(hex).map_err(|e| L2RpcError::Protocol {
            method: "getl2messageproof".to_string(),
            message: format!("invalid hex: {}", e),
        })?))
    }

    pub async fn get_deposit_status(
        &self,
        source_chain_id: u32,
        nonce: u64,
    ) -> Result<Option<DepositStatusResponse>> {
        let value = self
            .call(
                "getl1depositstatus",
                serde_json::json!([source_chain_id, nonce]),
            )
            .await?;
        if value.is_null() {
            return Ok(None);
        }
        let resp: DepositStatusResponse =
            serde_json::from_value(value).map_err(|e| L2RpcError::Protocol {
                method: "getl1depositstatus".to_string(),
                message: format!("decode failed: {}", e),
            })?;
        // Cross-check the requested source-chain matches what came back. A misbehaving
        // server returning another L1's deposit would otherwise sail through and the
        // caller would consume the wrong consumed/included status.
        if resp.source_chain_id != source_chain_id {
            return Err(L2RpcError::MismatchedChainId {
                method: "getl1depositstatus".to_string(),
                expected: source_chain_id,
                got: resp.source_chain_id,
            });
        }
        Ok(Some(resp))
    }

    pub async fn get_canonical_asset(&self, l2_asset: &str) -> Result<Option<String>> {
        let value = self
            .call("getcanonicalasset", serde_json::json!([l2_asset]))
            .await?;
        if value.is_null() {
            return Ok(None);
        }
        Ok(Some(value.as_str().map(String::from).ok_or_else(|| {
            L2RpcError::Protocol {
                method: "getcanonicalasset".to_string(),
                message: "expected string".to_string(),
            }
        })?))
    }

    pub async fn get_bridged_asset(&self, l1_asset: &str) -> Result<Option<String>> {
        let value = self
            .call("getbridgedasset", serde_json::json!([l1_asset]))
            .await?;
        if value.is_null() {
            return Ok(None);
        }
        Ok(Some(value.as_str().map(String::from).ok_or_else(|| {
            L2RpcError::Protocol {
                method: "getbridgedasset".to_string(),
                message: "expected string".to_string(),
            }
        })?))
    }

    pub async fn get_security_level(&self) -> Result<SecurityLevelResponse> {
        let value = self
            .call("getsecuritylevel", serde_json::json!([self.chain_id]))
            .await?;
        let resp: SecurityLevelResponse =
            serde_json::from_value(value).map_err(|e| L2RpcError::Protocol {
                method: "getsecuritylevel".to_string(),
                message: format!("decode failed: {}", e),
            })?;
        if resp.chain_id != self.chain_id {
            return Err(L2RpcError::MismatchedChainId {
                method: "getsecuritylevel".to_string(),
                expected: self.chain_id,
                got: resp.chain_id,
            });
        }
        Ok(resp)
    }

    pub async fn get_security_label(&self) -> Result<SecurityLabelResponse> {
        let value = self
            .call("getsecuritylabel", serde_json::json!([self.chain_id]))
            .await?;
        let resp: SecurityLabelResponse =
            serde_json::from_value(value).map_err(|e| L2RpcError::Protocol {
                method: "getsecuritylabel".to_string(),
                message: format!("decode failed: {}", e),
            })?;
        if resp.chain_id != self.chain_id {
            return Err(L2RpcError::MismatchedChainId {
                method: "getsecuritylabel".to_string(),
                expected: self.chain_id,
                got: resp.chain_id,
            });
        }
        Ok(resp)
    }

    async fn call(&self, method: &str, params: serde_json::Value) -> Result<serde_json::Value> {
        let id = self.next_id.fetch_add(1, Ordering::SeqCst) + 1;
        let body = serde_json::json!({
            "jsonrpc": "2.0",
            "method": method,
            "params": params,
            "id": id,
        });

        let response = self
            .http
            .post(&self.endpoint)
            .json(&body)
            .send()
            .await
            .map_err(|e| L2RpcError::Transport {
                method: method.to_string(),
                message: format!("send failed: {}", e),
            })?;

        let status = response.status();
        if !status.is_success() {
            let text = response
                .text()
                .await
                .unwrap_or_else(|_| "<no body>".to_string());
            let snippet = if text.len() > 200 {
                &text[..200]
            } else {
                &text
            };
            return Err(L2RpcError::Transport {
                method: method.to_string(),
                message: format!("http {}: {}", status, snippet),
            });
        }

        let envelope: serde_json::Value =
            response.json().await.map_err(|e| L2RpcError::Protocol {
                method: method.to_string(),
                message: format!("parse error: {}", e),
            })?;
        let response_id = envelope.get("id").and_then(|v| v.as_i64()).unwrap_or(-1);
        if response_id != id {
            return Err(L2RpcError::Protocol {
                method: method.to_string(),
                message: format!(
                    "response id {} does not match request id {}",
                    response_id, id
                ),
            });
        }
        if let Some(error) = envelope.get("error").filter(|v| !v.is_null()) {
            let code = error.get("code").and_then(|v| v.as_i64()).unwrap_or(-32603) as i32;
            let msg = error
                .get("message")
                .and_then(|v| v.as_str())
                .unwrap_or("rpc error")
                .to_string();
            return Err(L2RpcError::Server {
                method: method.to_string(),
                code,
                message: msg,
            });
        }
        Ok(envelope
            .get("result")
            .cloned()
            .unwrap_or(serde_json::Value::Null))
    }
}

fn hex_decode(s: &str) -> std::result::Result<Vec<u8>, String> {
    let s = s.strip_prefix("0x").unwrap_or(s);
    if !s.len().is_multiple_of(2) {
        return Err(format!("odd-length hex string: {}", s.len()));
    }
    let mut out = Vec::with_capacity(s.len() / 2);
    for i in (0..s.len()).step_by(2) {
        let byte = u8::from_str_radix(&s[i..i + 2], 16).map_err(|e| e.to_string())?;
        out.push(byte);
    }
    Ok(out)
}
