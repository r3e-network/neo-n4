use serde::Deserialize;
use std::sync::atomic::{AtomicU64, Ordering};

use crate::event_source::{EventSource, EventSourceError, LockedEvent};
use crate::live::json_rpc_types::{JsonRpcRequest, JsonRpcResponse};

use super::raw_log::RawLog;
use super::{
    EthRpcError, EthRpcEventSourceBuilder, decode_hex_u64, decode_locked_event,
    locked_event_topic_hash,
};

/// Live `EventSource` impl backed by Eth JSON-RPC.
pub struct EthRpcEventSource {
    pub(super) client: reqwest::blocking::Client,
    pub(super) rpc_url: String,
    pub(super) router_address: [u8; 20],
    pub(super) chunk_size: u64,
    pub(super) min_confirmations: u64,
    pub(super) queue: std::collections::VecDeque<LockedEvent>,
    pub(super) next_block_to_poll: u64,
    /// Monotonically incrementing JSON-RPC request id.
    pub(super) next_request_id: AtomicU64,
}

impl EventSource for EthRpcEventSource {
    fn next_event(&mut self, start_block: u64) -> Result<Option<LockedEvent>, EventSourceError> {
        // If we have a queued event, return it immediately. The watcher
        // drains one per tick; keeping a queue lets us batch-fetch blocks
        // and still return one at a time.
        if let Some(ev) = self.queue.pop_front() {
            return Ok(Some(ev));
        }

        // The journal cursor is the source of truth for where to resume.
        if start_block > self.next_block_to_poll {
            self.next_block_to_poll = start_block;
        }

        let head = self.fetch_block_number()?;
        let effective_head = head.saturating_sub(self.min_confirmations);
        if self.next_block_to_poll > effective_head {
            return Ok(None);
        }

        let from = self.next_block_to_poll;
        let to = (from.saturating_add(self.chunk_size - 1)).min(effective_head);
        let logs = self.fetch_locked_logs(from, to)?;

        for log in logs {
            self.queue.push_back(decode_locked_event(&log)?);
        }
        self.next_block_to_poll = to + 1;

        Ok(self.queue.pop_front())
    }
}

impl EthRpcEventSource {
    fn next_id(&self) -> u64 {
        self.next_request_id.fetch_add(1, Ordering::Relaxed)
    }

    pub fn builder(
        rpc_url: impl Into<String>,
        router_address: [u8; 20],
    ) -> EthRpcEventSourceBuilder {
        EthRpcEventSourceBuilder::new(rpc_url, router_address)
    }

    /// `eth_blockNumber` - returns the current head height.
    ///
    /// Public so operator tooling (e.g. `--preflight`) can use it to
    /// validate RPC reachability without reaching for a separate
    /// JSON-RPC client.
    pub fn fetch_block_number(&self) -> Result<u64, EthRpcError> {
        let req = JsonRpcRequest {
            jsonrpc: "2.0",
            id: self.next_id(),
            method: "eth_blockNumber",
            params: serde_json::json!([]),
        };
        let resp = self.send::<String>(&req)?;
        decode_hex_u64(&resp).map_err(|e| EthRpcError::Decode(format!("blockNumber: {e}")))
    }

    /// `eth_getLogs` for the `Locked` topic on the router address.
    fn fetch_locked_logs(&self, from: u64, to: u64) -> Result<Vec<RawLog>, EthRpcError> {
        let topic_locked = locked_event_topic_hash();
        let address_hex = format!("0x{}", hex::encode(self.router_address));
        let req = JsonRpcRequest {
            jsonrpc: "2.0",
            id: self.next_id(),
            method: "eth_getLogs",
            params: serde_json::json!([{
                "address": address_hex,
                "topics": [format!("0x{}", hex::encode(topic_locked))],
                "fromBlock": format!("0x{:x}", from),
                "toBlock": format!("0x{:x}", to),
            }]),
        };
        let logs = self.send::<Vec<RawLog>>(&req)?;
        for log in &logs {
            self.validate_log_envelope(log, from, to)?;
        }
        Ok(logs)
    }

    fn validate_log_envelope(&self, log: &RawLog, from: u64, to: u64) -> Result<(), EthRpcError> {
        let address =
            decode_hex20(&log.address).map_err(|e| EthRpcError::BadLog(format!("address: {e}")))?;
        if address != self.router_address {
            return Err(EthRpcError::BadLog(format!(
                "log address 0x{} != configured router 0x{}",
                hex::encode(address),
                hex::encode(self.router_address)
            )));
        }

        let block_number = decode_hex_u64(&log.block_number)
            .map_err(|e| EthRpcError::BadLog(format!("blockNumber: {e}")))?;
        if block_number < from || block_number > to {
            return Err(EthRpcError::BadLog(format!(
                "log block {block_number} outside requested range [{from}..{to}]"
            )));
        }
        Ok(())
    }

    fn send<T: for<'de> Deserialize<'de>>(&self, req: &JsonRpcRequest) -> Result<T, EthRpcError> {
        let resp: JsonRpcResponse<T> = self
            .client
            .post(&self.rpc_url)
            .json(req)
            .send()
            .map_err(|e| EthRpcError::Http(format!("send: {e}")))?
            .json()
            .map_err(|e| EthRpcError::Decode(format!("response json: {e}")))?;
        if let Some(err) = resp.error {
            return Err(EthRpcError::Rpc {
                code: err.code,
                message: err.message,
            });
        }
        // Validate response id matches request to prevent cross-talk in
        // pipelined/concurrent scenarios.
        if resp.id != Some(req.id) {
            return Err(EthRpcError::Decode(format!(
                "response id {:?} != request id {}",
                resp.id, req.id
            )));
        }
        resp.result
            .ok_or_else(|| EthRpcError::Decode("response has no result and no error".into()))
    }
}

fn decode_hex20(s: &str) -> Result<[u8; 20], String> {
    let bytes = crate::live::json_rpc_types::decode_hex_bytes(s)?;
    if bytes.len() != 20 {
        return Err(format!("expected 20 bytes, got {}", bytes.len()));
    }
    let mut out = [0u8; 20];
    out.copy_from_slice(&bytes);
    Ok(out)
}
