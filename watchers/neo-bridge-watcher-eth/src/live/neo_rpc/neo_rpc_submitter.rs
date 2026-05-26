use serde::Deserialize;
use std::sync::atomic::{AtomicU64, Ordering};

use crate::submitter::{InboundSubmission, NeoSubmitter, SubmitterError};

use super::invoke_function_result::InvokeFunctionResult;
use super::json_rpc_request::JsonRpcRequest;
use super::json_rpc_response::JsonRpcResponse;
use super::{decode_hex_bytes, NeoRpcError, NeoRpcSubmitterBuilder, SignAndSend};

/// Live `NeoSubmitter` impl backed by Neo JSON-RPC + an operator-supplied
/// signer.
pub struct NeoRpcSubmitter<S: SignAndSend> {
    pub(super) client: reqwest::blocking::Client,
    pub(super) rpc_url: String,
    pub(super) escrow_address: [u8; 20],
    pub(super) signer: [u8; 20],
    pub(super) sign_and_send: S,
    pub(super) next_request_id: AtomicU64,
}

impl<S: SignAndSend> NeoSubmitter for NeoRpcSubmitter<S> {
    fn submit_inbound(
        &mut self,
        submission: InboundSubmission,
    ) -> Result<[u8; 32], SubmitterError> {
        let req = self.build_invokefunction(&submission)?;
        let resp: InvokeFunctionResult = self.send_rpc("invokefunction", req)?;
        if resp.state != "HALT" {
            return Err(NeoRpcError::Fault(
                resp.exception
                    .unwrap_or_else(|| format!("VM state {} (no exception detail)", resp.state)),
            )
            .into());
        }
        let script_bytes = decode_hex_bytes(&resp.script)
            .map_err(|e| NeoRpcError::Decode(format!("script: {e}")))?;

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

    pub(super) fn build_invokefunction(
        &self,
        submission: &InboundSubmission,
    ) -> Result<serde_json::Value, NeoRpcError> {
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
        let req_id = self.next_request_id.fetch_add(1, Ordering::Relaxed);
        let req = JsonRpcRequest {
            jsonrpc: "2.0",
            id: req_id,
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
        if resp.id != Some(req_id) {
            return Err(NeoRpcError::Decode(format!(
                "response id {:?} != request id {}",
                resp.id, req_id
            )));
        }
        resp.result
            .ok_or_else(|| NeoRpcError::Decode("response has no result and no error".into()))
    }
}
