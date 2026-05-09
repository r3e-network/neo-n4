//! HTTP JSON-RPC `NeoSubmitter` for posting verified bridge inbounds to
//! Neo's `NeoHub.ExternalBridgeEscrow.Receive(...)`.
//!
//! Two-stage flow keeps Neo tx-signing fully external:
//!
//! 1. **Pre-check** via `invokefunction`. Neo's RPC node builds the call
//!    script, runs it in a read-only ApplicationEngine, and returns the
//!    final VM `state` (`HALT`/`FAULT`) + the constructed `script` bytes.
//!    A `FAULT` here means the verifier rejected the proof or the
//!    escrow rejected for some other reason — the watcher gets a typed
//!    error before paying gas.
//!
//! 2. **Sign + send** via an operator-supplied callback. The callback
//!    receives the script bytes from step 1, wraps them in a signed Neo
//!    transaction (using the operator's HSM / wallet of choice), and
//!    POSTs `sendrawtransaction`. We don't construct or sign the tx in
//!    this crate — Neo tx signing is operator infrastructure (HSM
//!    integrations vary), and a watcher daemon shouldn't ship its own
//!    Neo wallet.
//!
//! The callback returns the tx hash on success; the submitter returns
//! that as the `submit_inbound` result.

use crate::submitter::{InboundSubmission, NeoSubmitter, SubmitterError};
use serde::{Deserialize, Serialize};
use std::time::Duration;
use thiserror::Error;

const DEFAULT_REQUEST_TIMEOUT: Duration = Duration::from_secs(30);

#[derive(Debug, Error)]
pub enum NeoRpcError {
    #[error("HTTP error: {0}")]
    Http(String),
    #[error("RPC error: {code} {message}")]
    Rpc { code: i64, message: String },
    #[error("decoding response failed: {0}")]
    Decode(String),
    #[error("invokefunction returned FAULT: {0}")]
    Fault(String),
    #[error("sign_and_send callback failed: {0}")]
    SignAndSend(String),
}

impl From<NeoRpcError> for SubmitterError {
    fn from(err: NeoRpcError) -> Self {
        match err {
            NeoRpcError::Fault(msg) if msg.contains("already consumed") => {
                SubmitterError::AlreadyConsumed
            }
            NeoRpcError::Fault(msg) => SubmitterError::VerifierRejected(msg),
            NeoRpcError::Http(m) | NeoRpcError::Decode(m) | NeoRpcError::SignAndSend(m) => {
                SubmitterError::Rpc(m)
            }
            NeoRpcError::Rpc { code, message } => {
                SubmitterError::Rpc(format!("rpc {code}: {message}"))
            }
        }
    }
}

/// Operator-supplied signer/sender. Receives the script bytes from
/// `invokefunction` (which already validated the call would HALT),
/// wraps them in a Neo `Transaction`, signs with the operator's
/// witnessing identity, POSTs `sendrawtransaction`, and returns the
/// 32-byte tx hash. Production deployments wire this to an HSM / KMS.
///
/// Returning `Err` short-circuits the submitter — the journal cursor
/// MUST NOT advance, so the caller can retry on the next tick.
pub trait SignAndSend: Send {
    fn sign_and_send(&mut self, script: &[u8]) -> Result<[u8; 32], NeoRpcError>;
}

impl<F> SignAndSend for F
where
    F: FnMut(&[u8]) -> Result<[u8; 32], NeoRpcError> + Send,
{
    fn sign_and_send(&mut self, script: &[u8]) -> Result<[u8; 32], NeoRpcError> {
        self(script)
    }
}

/// Configuration for a [`NeoRpcSubmitter`].
pub struct NeoRpcSubmitterBuilder<S: SignAndSend> {
    rpc_url: String,
    escrow_address: [u8; 20],
    /// Account hash that will be the tx signer. Passed to
    /// `invokefunction` so the pre-check sees `Runtime.CheckWitness`
    /// against this address as TRUE — same as a real signed tx would.
    signer: [u8; 20],
    sign_and_send: S,
    request_timeout: Duration,
}

impl<S: SignAndSend> NeoRpcSubmitterBuilder<S> {
    pub fn new(
        rpc_url: impl Into<String>,
        escrow_address: [u8; 20],
        signer: [u8; 20],
        sign_and_send: S,
    ) -> Self {
        Self {
            rpc_url: rpc_url.into(),
            escrow_address,
            signer,
            sign_and_send,
            request_timeout: DEFAULT_REQUEST_TIMEOUT,
        }
    }

    pub fn request_timeout(mut self, t: Duration) -> Self {
        self.request_timeout = t;
        self
    }

    pub fn build(self) -> Result<NeoRpcSubmitter<S>, NeoRpcError> {
        let client = reqwest::blocking::Client::builder()
            .timeout(self.request_timeout)
            .build()
            .map_err(|e| NeoRpcError::Http(format!("client build: {e}")))?;
        Ok(NeoRpcSubmitter {
            client,
            rpc_url: self.rpc_url,
            escrow_address: self.escrow_address,
            signer: self.signer,
            sign_and_send: self.sign_and_send,
        })
    }
}

/// Live `NeoSubmitter` impl backed by Neo JSON-RPC + an operator-supplied
/// signer.
pub struct NeoRpcSubmitter<S: SignAndSend> {
    client: reqwest::blocking::Client,
    rpc_url: String,
    escrow_address: [u8; 20],
    signer: [u8; 20],
    sign_and_send: S,
}

impl<S: SignAndSend> NeoSubmitter for NeoRpcSubmitter<S> {
    fn submit_inbound(&mut self, submission: InboundSubmission) -> Result<[u8; 32], SubmitterError> {
        // 1. Pre-check: build invokefunction request, verify HALT,
        //    extract the constructed script.
        let req = self.build_invokefunction(&submission)?;
        let resp: InvokeFunctionResult = self.send_rpc("invokefunction", req)?;
        if resp.state != "HALT" {
            return Err(NeoRpcError::Fault(resp.exception.unwrap_or_else(|| {
                format!("VM state {} (no exception detail)", resp.state)
            }))
            .into());
        }
        let script_bytes = decode_hex_bytes(&resp.script)
            .map_err(|e| NeoRpcError::Decode(format!("script: {e}")))?;

        // 2. Hand the script to the operator's signer/sender.
        let tx_hash = self.sign_and_send.sign_and_send(&script_bytes)?;
        Ok(tx_hash)
    }
}

impl<S: SignAndSend> NeoRpcSubmitter<S> {
    pub fn builder(
        rpc_url: impl Into<String>,
        escrow_address: [u8; 20],
        signer: [u8; 20],
        sign_and_send: S,
    ) -> NeoRpcSubmitterBuilder<S> {
        NeoRpcSubmitterBuilder::new(rpc_url, escrow_address, signer, sign_and_send)
    }

    fn build_invokefunction(&self, submission: &InboundSubmission) -> Result<serde_json::Value, NeoRpcError> {
        // Neo addresses in JSON-RPC are little-endian hex, prefixed with
        // 0x. The escrow ContractParameter is a UInt160 (20 bytes BE in
        // protocol but Neo's RPC convention prints LE).
        let escrow_hex = format!("0x{}", hex::encode(self.escrow_address));
        let signer_hex = format!("0x{}", hex::encode(self.signer));

        Ok(serde_json::json!([
            escrow_hex,
            "receive",
            [
                { "type": "Integer", "value": submission.external_chain_id.to_string() },
                { "type": "ByteArray", "value": hex::encode(&submission.message_bytes) },
                { "type": "ByteArray", "value": hex::encode(&submission.proof_bytes) },
            ],
            // Signers list — required so Runtime.CheckWitness inside the
            // contract sees us as a witnessed caller. CalledByEntry is
            // the standard scope for end-user txs.
            [
                {
                    "account": signer_hex,
                    "scopes": "CalledByEntry",
                }
            ]
        ]))
    }

    fn send_rpc<T: for<'de> Deserialize<'de>>(
        &self,
        method: &str,
        params: serde_json::Value,
    ) -> Result<T, NeoRpcError> {
        let req = JsonRpcRequest {
            jsonrpc: "2.0",
            id: 1,
            method,
            params,
        };
        let resp: JsonRpcResponse<T> = self
            .client
            .post(&self.rpc_url)
            .json(&req)
            .send()
            .map_err(|e| NeoRpcError::Http(format!("send: {e}")))?
            .json()
            .map_err(|e| NeoRpcError::Decode(format!("response json: {e}")))?;
        if let Some(err) = resp.error {
            return Err(NeoRpcError::Rpc {
                code: err.code,
                message: err.message,
            });
        }
        resp.result
            .ok_or_else(|| NeoRpcError::Decode("response has no result and no error".into()))
    }
}

// ─── JSON-RPC types ──────────────────────────────────────────────────

#[derive(Serialize)]
struct JsonRpcRequest<'a> {
    jsonrpc: &'static str,
    id: u64,
    method: &'a str,
    params: serde_json::Value,
}

#[derive(Deserialize)]
struct JsonRpcResponse<T> {
    #[allow(dead_code)]
    jsonrpc: Option<String>,
    #[allow(dead_code)]
    id: Option<u64>,
    result: Option<T>,
    error: Option<JsonRpcError>,
}

#[derive(Deserialize)]
struct JsonRpcError {
    code: i64,
    message: String,
}

#[derive(Deserialize, Debug)]
struct InvokeFunctionResult {
    /// VM exit state — `"HALT"` on success, `"FAULT"` on revert.
    state: String,
    /// Pre-built NeoVM script as hex. Operators can submit this exact
    /// blob via `sendrawtransaction` once wrapped in a signed tx.
    script: String,
    /// Optional exception message on FAULT.
    exception: Option<String>,
}

fn decode_hex_bytes(s: &str) -> Result<Vec<u8>, String> {
    let s = s.strip_prefix("0x").unwrap_or(s);
    hex::decode(s).map_err(|e| format!("hex: {e}"))
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Pin the `invokefunction` JSON shape. This is what the operator's
    /// neo-cli RPC node sees; a refactor that drops a field or shifts a
    /// type breaks every Neo node that processed our calls before.
    #[test]
    fn invokefunction_request_shape() {
        let escrow = [0xAB; 20];
        let signer = [0xCD; 20];
        let dummy_callback = |_script: &[u8]| -> Result<[u8; 32], NeoRpcError> { Ok([0u8; 32]) };
        let s = NeoRpcSubmitterBuilder::new("http://x", escrow, signer, dummy_callback)
            .build()
            .unwrap();
        let req = s
            .build_invokefunction(&InboundSubmission {
                external_chain_id: 0xE000_0001,
                message_bytes: vec![0xAA, 0xBB, 0xCC],
                proof_bytes: vec![0xDE, 0xAD, 0xBE, 0xEF],
            })
            .unwrap();
        let arr = req.as_array().unwrap();
        assert_eq!(arr.len(), 4, "params: [contract, method, callargs, signers]");
        // Contract hash is LE-hex with 0x prefix (20 bytes = 40 hex chars).
        assert_eq!(arr[0].as_str().unwrap(), "0xabababababababababababababababababababab");
        assert_eq!(arr[1].as_str().unwrap(), "receive");
        // Three call args: Integer chainId, ByteArray message, ByteArray proof.
        let call_args = arr[2].as_array().unwrap();
        assert_eq!(call_args.len(), 3);
        assert_eq!(call_args[0]["type"].as_str().unwrap(), "Integer");
        assert_eq!(call_args[0]["value"].as_str().unwrap(), "3758096385"); // 0xE0000001
        assert_eq!(call_args[1]["type"].as_str().unwrap(), "ByteArray");
        assert_eq!(call_args[1]["value"].as_str().unwrap(), "aabbcc");
        assert_eq!(call_args[2]["type"].as_str().unwrap(), "ByteArray");
        assert_eq!(call_args[2]["value"].as_str().unwrap(), "deadbeef");
        // Signers list: one entry, CalledByEntry scope.
        let signers = arr[3].as_array().unwrap();
        assert_eq!(signers.len(), 1);
        assert_eq!(signers[0]["scopes"].as_str().unwrap(), "CalledByEntry");
        assert_eq!(
            signers[0]["account"].as_str().unwrap(),
            "0xcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcd"   // 20 bytes = 40 hex chars
        );
    }

    /// Pin the FAULT → SubmitterError translation: an "already consumed"
    /// fault produces SubmitterError::AlreadyConsumed (so the watcher's
    /// retry loop doesn't try forever on a duplicate).
    #[test]
    fn fault_translation() {
        let translated: SubmitterError =
            NeoRpcError::Fault("nonce already consumed (replay)".into()).into();
        assert!(matches!(translated, SubmitterError::AlreadyConsumed));

        let translated: SubmitterError =
            NeoRpcError::Fault("verifier rejected proof".into()).into();
        match translated {
            SubmitterError::VerifierRejected(m) => assert!(m.contains("verifier")),
            other => panic!("expected VerifierRejected, got {other:?}"),
        }

        let translated: SubmitterError =
            NeoRpcError::Http("connection refused".into()).into();
        match translated {
            SubmitterError::Rpc(m) => assert!(m.contains("connection")),
            other => panic!("expected Rpc, got {other:?}"),
        }
    }
}
